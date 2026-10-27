using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NightreignLocalTrainer
{
    internal sealed partial class TrainerForm
    {
        internal const int OverlayHotkeyId=100;
        const int WmHotkey=0x0312;
        const uint MenuKey=0x70;
        static readonly uint ToggleMessage=RegisterWindowMessage("NightreignTrainerOverlay.Toggle.v04");
        NotifyIcon tray;
        System.Windows.Forms.Timer overlayTimer;
        Process trackedGame;
        IntPtr gameWindow;
        Rectangle gameClient;
        DateTime nextGameSearch=DateTime.MinValue,nextAutoConnect=DateTime.MinValue;
        bool allowPanelVisibility,exitRequested,overlayInitialized;
        readonly HashSet<int> registeredGameHotkeys=new HashSet<int>(),reportedHotkeyFailures=new HashSet<int>();
        internal bool autoConnectPaused;
        internal bool OverlayTestMode;
        string overlayHint="F1 呼出／收起 · Esc 返回游戏 · F4 全部关闭";
        internal bool PanelIsVisible {get{return Visible;}}

        [StructLayout(LayoutKind.Sequential)]struct NativeRect{internal int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]struct NativePoint{internal int X,Y;}
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref NativePoint point);
        [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint msg,IntPtr wParam,IntPtr lParam);

        void InitializeOverlay()
        {
            overlayInitialized=true;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            AutoScroll=true;AutoScrollMinSize=new Size(770,620);DoubleBuffered=true;
            BackColor=Color.FromArgb(22,25,31);ForeColor=Color.FromArgb(230,233,238);
            Opacity=0.97;
            foreach(Control c in Controls)
            {
                c.ForeColor=ForeColor;
                var button=c as Button;
                if(button!=null)StyleOverlayButton(button);
                var input=c as NumericUpDown;
                if(input!=null){input.BackColor=Color.FromArgb(37,42,50);input.ForeColor=ForeColor;input.BorderStyle=BorderStyle.FixedSingle;}
            }
            var title=Controls.OfType<Label>().OrderBy(c=>c.Top).First();
            title.Text="黑夜君临  功能栏";title.Width=420;title.Font=new Font(Font.FontFamily,18F,FontStyle.Bold);title.ForeColor=Color.FromArgb(228,193,120);
            var hint=Controls.OfType<Label>().OrderBy(c=>c.Top).Skip(1).First();hint.Text=overlayHint;hint.Width=720;
            var hide=new Button{Text="收起  F1",Location=new Point(510,20),Size=new Size(132,34),TabStop=false};
            StyleOverlayButton(hide);hide.Click+=(s,e)=>HideOverlay(true);Controls.Add(hide);
            var quit=new Button{Text="退出",Location=new Point(658,20),Size=new Size(92,34),TabStop=false};
            StyleOverlayButton(quit);quit.Click+=(s,e)=>ExitOverlay();Controls.Add(quit);
            var footer=Controls.OfType<Label>().FirstOrDefault(c=>c.Top==532);
            if(footer!=null)footer.Text="收起面板后已开启功能继续运行；退出时恢复本工具记录的状态。";
            if(previewMode)return;
            var menu=new ContextMenuStrip();
            menu.Items.Add("显示／隐藏功能栏（F1）",null,(s,e)=>ToggleOverlay(true));
            menu.Items.Add("全部关闭（F4）",null,(s,e)=>DisableAllFromTray());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出并恢复游戏状态",null,(s,e)=>ExitOverlay());
            tray=new NotifyIcon{Icon=SystemIcons.Application,Text="黑夜君临功能栏 · F1 呼出",ContextMenuStrip=menu,Visible=true};
            tray.DoubleClick+=(s,e)=>ToggleOverlay(true);
            overlayTimer=new System.Windows.Forms.Timer{Interval=250};overlayTimer.Tick+=(s,e)=>OverlayHeartbeat();overlayTimer.Start();
            FormClosed+=(s,e)=>DisposeOverlay();
            status.Text="后台已启动；进入游戏后按 F1 呼出";
        }
        void StyleOverlayButton(Button button)
        {
            button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(76,82,94);
            button.FlatAppearance.MouseOverBackColor=Color.FromArgb(57,64,76);
            button.BackColor=Color.FromArgb(38,43,52);button.ForeColor=Color.FromArgb(234,236,242);button.UseVisualStyleBackColor=false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using(var border=new Pen(Color.FromArgb(139,115,73)))e.Graphics.DrawRectangle(border,0,0,ClientSize.Width-1,ClientSize.Height-1);
        }
        protected override void SetVisibleCore(bool value)
        {
            if(!previewMode && value && !allowPanelVisibility)
            {
                if(!IsHandleCreated)CreateHandle();base.SetVisibleCore(false);return;
            }
            base.SetVisibleCore(value);
        }
        void SyncGameHotkeys()
        {
            if(previewMode || OverlayTestMode || !overlayInitialized)return;
            if(!FeatureHotkeysAllowed()){ReleaseGameHotkeys();return;}
            for(int i=1;i<=10;i++)TryRegisterGameHotkey(i,i==1?0x7Au:(uint)(0x6F+i));
            TryRegisterGameHotkey(OverlayHotkeyId,MenuKey);
            hotkeyConflict=reportedHotkeyFailures.Count>0;
        }
        void TryRegisterGameHotkey(int id,uint key)
        {
            if(registeredGameHotkeys.Contains(id))return;
            if(RegisterHotKey(Handle,id,0x4000,key))
            {
                registeredGameHotkeys.Add(id);reportedHotkeyFailures.Remove(id);return;
            }
            if(reportedHotkeyFailures.Add(id))
            {
                Engine.Log("Overlay hotkey unavailable: virtual key "+key);
                if(id==OverlayHotkeyId && tray!=null)tray.ShowBalloonTip(5000,"F1 被占用","请关闭旧版修改器；也可双击托盘图标打开功能栏。",ToolTipIcon.Info);
            }
        }
        void ReleaseGameHotkeys()
        {
            if(!IsHandleCreated)return;
            foreach(int id in registeredGameHotkeys.ToArray())UnregisterHotKey(Handle,id);
            registeredGameHotkeys.Clear();
        }
        internal static void RequestExistingOverlay(){PostMessage(new IntPtr(0xFFFF),ToggleMessage,IntPtr.Zero,IntPtr.Zero);}
        bool HandleOverlayMessage(ref Message message)
        {
            if(!previewMode && message.Msg==(int)ToggleMessage){ToggleOverlay(true);return true;}
            if(!previewMode && message.Msg==WmHotkey && (int)message.WParam==OverlayHotkeyId)
            {
                if(FeatureHotkeysAllowed())ToggleOverlay(false);return true;
            }
            return false;
        }
        bool FeatureHotkeysAllowed()
        {
            if(previewMode||OverlayTestMode)return true;
            IntPtr foreground=GetForegroundWindow();uint pid;GetWindowThreadProcessId(foreground,out pid);
            return pid==(uint)Process.GetCurrentProcess().Id || (trackedGame!=null && pid==(uint)trackedGame.Id);
        }
        void FindGameWindow()
        {
            if(OverlayTestMode)return;
            if(trackedGame!=null)
            {
                try{if(trackedGame.HasExited){trackedGame.Dispose();trackedGame=null;gameWindow=IntPtr.Zero;}else{trackedGame.Refresh();gameWindow=trackedGame.MainWindowHandle;}}
                catch(InvalidOperationException){trackedGame=null;gameWindow=IntPtr.Zero;}
            }
            if(trackedGame==null && DateTime.UtcNow>=nextGameSearch)
            {
                nextGameSearch=DateTime.UtcNow.AddSeconds(1);
                string expected=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","Game","nightreign.exe"));
                foreach(var p in Process.GetProcessesByName("nightreign"))
                {
                    bool match=false;try{match=String.Equals(p.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase);}catch(Exception){ }
                    if(match && trackedGame==null){trackedGame=p;gameWindow=p.MainWindowHandle;}else p.Dispose();
                }
            }
            NativeRect rect;var origin=new NativePoint();
            if(gameWindow!=IntPtr.Zero && IsWindow(gameWindow) && GetClientRect(gameWindow,out rect) && ClientToScreen(gameWindow,ref origin))
                gameClient=new Rectangle(origin.X,origin.Y,rect.Right-rect.Left,rect.Bottom-rect.Top);
            else gameClient=Rectangle.Empty;
        }
        void OverlayHeartbeat()
        {
            if(closing || previewMode)return;
            FindGameWindow();
            SyncGameHotkeys();
            if(!OverlayTestMode && engine==null && trackedGame!=null && !autoConnectPaused && DateTime.UtcNow>=nextAutoConnect)
            {
                nextAutoConnect=DateTime.UtcNow.AddSeconds(3);Connect();
            }
            if(!Visible)return;
            if(!OverlayTestMode && (!FeatureHotkeysAllowed() || (gameWindow!=IntPtr.Zero && (!IsWindowVisible(gameWindow)||IsIconic(gameWindow)))))
            {HideOverlay(false);return;}
            PositionOverlay();
        }
        internal static Rectangle FitOverlay(Rectangle area,Size preferred)
        {
            if(area.Width<64 || area.Height<64)throw new ArgumentException("显示区域太小");
            int width=Math.Min(preferred.Width,Math.Max(64,area.Width-32)),height=Math.Min(preferred.Height,Math.Max(64,area.Height-32));
            return new Rectangle(area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height);
        }
        void PositionOverlay()
        {
            Rectangle area=gameClient;
            if(area.Width<64 || area.Height<64)area=Screen.FromPoint(Cursor.Position).WorkingArea;
            Rectangle fit=FitOverlay(area,new Size(780,630));
            if(Bounds!=fit)Bounds=fit;
        }
        internal void ToggleOverlay(bool explicitTrayRequest)
        {
            if(Visible){HideOverlay(true);return;}
            FindGameWindow();
            if(!explicitTrayRequest && !FeatureHotkeysAllowed())return;
            PositionOverlay();allowPanelVisibility=true;Show();BringToFront();Activate();
            if(!OverlayTestMode && engine==null && trackedGame!=null && !autoConnectPaused){nextAutoConnect=DateTime.UtcNow.AddSeconds(3);Connect();}
            Engine.Log("Overlay shown");
        }
        internal void HideOverlay(bool returnToGame)
        {
            bool wasVisible=Visible;allowPanelVisibility=false;Hide();
            if(wasVisible && returnToGame && gameWindow!=IntPtr.Zero && IsWindow(gameWindow) && !IsIconic(gameWindow))SetForegroundWindow(gameWindow);
            if(wasVisible)Engine.Log("Overlay hidden; enabled features remain active");
        }
        void OverlayFirstShown(){ }
        bool ShouldHideInsteadOfClose(FormClosingEventArgs e){return !previewMode && !exitRequested && e.CloseReason==CloseReason.UserClosing;}
        internal void ExitOverlay(){exitRequested=true;Close();}
        void DisableAllFromTray()
        {
            hp.Checked=fp.Checked=stamina.Checked=false;foreach(var box in AdvancedBoxes())box.Checked=false;
            if(engine!=null)engine.RestoreAdvanced();Engine.Log("All features disabled from tray");
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            if(!previewMode && Visible && keyData==Keys.Escape){HideOverlay(true);return true;}
            return base.ProcessCmdKey(ref message,keyData);
        }
        void DisposeOverlay()
        {
            if(overlayTimer!=null){overlayTimer.Stop();overlayTimer.Dispose();overlayTimer=null;}
            ReleaseGameHotkeys();
            if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}
            if(trackedGame!=null){trackedGame.Dispose();trackedGame=null;}
        }
        internal void StartOverlayTest(Rectangle area)
        {
            OverlayTestMode=true;autoConnectPaused=true;gameClient=area;
            var unused=Handle;SetVisibleCore(true);
        }
        internal bool OverlayTaskbarEntry {get{return ShowInTaskbar;}}
        internal bool OverlayIsBorderless {get{return FormBorderStyle==FormBorderStyle.None;}}
        internal bool TestCheckState {get{return hp.Checked;}set{hp.Checked=value;}}
        internal void TestMenuMessage(){var m=Message.Create(Handle,WmHotkey,new IntPtr(OverlayHotkeyId),IntPtr.Zero);WndProc(ref m);}
        internal void TestEscape(){var m=new Message();ProcessCmdKey(ref m,Keys.Escape);}
    }
}
