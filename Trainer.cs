using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

// Layout independently resolved from RTTI and live objects in the local 1.3.2.0 build.
// No executable code patches, injected DLLs, drivers, or third-party trainer runtime.
namespace NightreignLocalTrainer
{
    internal sealed class Memory : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint rights, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr done);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr done);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        IntPtr handle;
        readonly bool writable;
        internal Memory(int pid, bool write) {
            writable = write;
            handle = OpenProcess(write ? 0x438u : 0x410u, false, pid);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("无法打开进程，Windows 错误 " + Marshal.GetLastWin32Error());
        }
        internal byte[] Read(long at, int count) {
            if (at < 0x10000 || count < 1 || count > 0x100000) throw new InvalidOperationException("地址或读取长度无效");
            byte[] data = new byte[count]; UIntPtr done;
            if (!ReadProcessMemory(handle, new IntPtr(at), data, (UIntPtr)count, out done) || done.ToUInt64() != (ulong)count)
                throw new InvalidOperationException("内存读取失败，可能正在切换场景");
            return data;
        }
        internal long Pointer(long at) { return BitConverter.ToInt64(Read(at, 8), 0); }
        internal int Int32(long at) { return BitConverter.ToInt32(Read(at, 4), 0); }
        internal byte Byte(long at) { return Read(at, 1)[0]; }
        internal float Float(long at) { return BitConverter.ToSingle(Read(at, 4), 0); }
        internal ulong UInt64(long at) { return BitConverter.ToUInt64(Read(at, 8), 0); }
        internal void WriteByte(long at, byte value) { WriteExact(at, new byte[]{value}); }
        internal void WriteFloat(long at, float value) {
            if(float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("浮点值无效");
            WriteExact(at,BitConverter.GetBytes(value));
        }
        void WriteExact(long at, byte[] data) {
            if(!writable || at<0x10000) throw new InvalidOperationException("连接只读或写入地址无效");
            UIntPtr done;
            if(!WriteProcessMemory(handle,new IntPtr(at),data,(UIntPtr)data.Length,out done) || done.ToUInt64()!=(ulong)data.Length)
                throw new InvalidOperationException("内存写入失败");
            if(!Read(at,data.Length).SequenceEqual(data)) throw new InvalidOperationException("写入后校验失败");
        }
        internal void WriteInt32(long at, int value) {
            if (!writable) throw new InvalidOperationException("当前连接只读");
            if (at < 0x10000) throw new InvalidOperationException("写入地址无效");
            byte[] data = BitConverter.GetBytes(value); UIntPtr done;
            if (!WriteProcessMemory(handle, new IntPtr(at), data, (UIntPtr)4, out done) || done.ToUInt64() != 4)
                throw new InvalidOperationException("内存写入失败");
            if (Int32(at) != value) throw new InvalidOperationException("写入后校验失败");
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    internal sealed class Snapshot
    {
        internal long World, Player, Module, GameData;
        internal int Hp, MaxHp, Fp, MaxFp, Stamina, MaxStamina, Runes;
        internal string Text() {
            return String.Format("HP {0}/{1}   FP {2}/{3}   精力 {4}/{5}   卢恩 {6}", Hp, MaxHp, Fp, MaxFp, Stamina, MaxStamina, Runes);
        }
    }

    internal sealed partial class Engine : IDisposable
    {
        internal const string ExpectedHash = "e3a60b675d01917003b56ce5a6990c61a17cab22d64b4ef1f140cfcb3d7e593c";
        internal const long WorldSlot = 0x3C0F0A8, MainPlayer = 0x174E8;
        internal const long WorldVtable = 0x2C1B930, PlayerVtable = 0x2C30808, DataVtable = 0x2C10B88;
        internal const long HpOffset = 0x140, FpOffset = 0x150, StaminaOffset = 0x15C;
        internal long Base;
        internal int Pid;
        internal Memory Mem;
        Process process;
        string gamePath;
        bool savesBackedUp;
        internal Engine(bool write) {
            gamePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Game", "nightreign.exe"));
            process = Process.GetProcessesByName("nightreign").FirstOrDefault(p => {
                try { return String.Equals(p.MainModule.FileName, gamePath, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
            if (process == null) throw new InvalidOperationException("请先从本目录启动游戏并进入可操作场景");
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(gamePath)) {
                string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (hash != ExpectedHash) throw new InvalidOperationException("游戏文件版本不匹配；已停止连接，需要重新适配地址");
            }
            Base = process.MainModule.BaseAddress.ToInt64(); Pid = process.Id;
            Mem = new Memory(Pid, write);
            // Constructor callers may retain a connection while no character is loaded.
            // Every actual read/write goes through the complete object chain below.
        }
        internal Snapshot ReadSnapshot() {
            if (process.HasExited) throw new InvalidOperationException("游戏进程已退出");
            long world = Mem.Pointer(Base + WorldSlot);
            CheckType(world, WorldVtable, "角色管理器");
            long player = Mem.Pointer(world + MainPlayer);
            CheckType(player, PlayerVtable, "本机角色");
            long module = Mem.Pointer(Mem.Pointer(player + 0x1B8));
            CheckType(module, DataVtable, "角色属性");
            if (Mem.Pointer(module + 8) != player) throw new InvalidOperationException("角色归属校验失败");
            var s = new Snapshot { World = world, Player = player, Module = module };
            byte[] stats = Mem.Read(module + 0x140, 0x28);
            s.Hp = BitConverter.ToInt32(stats, 0); s.MaxHp = BitConverter.ToInt32(stats, 4);
            s.Fp = BitConverter.ToInt32(stats, 0x10); s.MaxFp = BitConverter.ToInt32(stats, 0x14);
            s.Stamina = BitConverter.ToInt32(stats, 0x1C); s.MaxStamina = BitConverter.ToInt32(stats, 0x20);
            ValidateStats(s);
            long gd = Mem.Pointer(player + 0x5A0);
            if (TypeName(gd) != ".?AVPlayerGameData@CS@@") throw new InvalidOperationException("角色资源结构校验失败");
            s.GameData = gd; s.Runes = Mem.Int32(gd + 0x6C);
            if (s.Runes < 0 || s.Runes > 999999999) throw new InvalidOperationException("卢恩数据范围异常");
            if (Mem.Pointer(world + MainPlayer) != player || Mem.Pointer(module + 8) != player)
                throw new InvalidOperationException("角色正在切换，暂停修改");
            return s;
        }
        void CheckType(long at, long rva, string label) {
            if (Mem.Pointer(at) != Base + rva) throw new InvalidOperationException(label + "尚未就绪或地址校验失败");
        }
        string TypeName(long at) {
            long vt = Mem.Pointer(at);
            if (vt < Base || vt >= Base + 0x8000000) return "";
            long col = Mem.Pointer(vt - 8);
            if (col < Base || col >= Base + 0x8000000) return "";
            byte[] header = Mem.Read(col, 24);
            if (BitConverter.ToInt32(header, 0) != 1) return "";
            uint rva = BitConverter.ToUInt32(header, 12);
            if (rva > 0x8000000) return "";
            return Encoding.ASCII.GetString(Mem.Read(Base + rva + 16, 96)).Split('\0')[0];
        }
        internal static void ValidateStats(Snapshot s) {
            if (s.MaxHp <= 0 || s.MaxHp > 1000000 || s.Hp < 0 || s.Hp > s.MaxHp ||
                s.MaxFp < 0 || s.MaxFp > 1000000 || s.Fp < 0 || s.Fp > s.MaxFp ||
                s.MaxStamina <= 0 || s.MaxStamina > 1000000 || s.Stamina < -100 || s.Stamina > s.MaxStamina)
                throw new InvalidOperationException("属性范围异常，已停止写入");
        }
        internal Snapshot Tick(bool hp, bool fp, bool stamina) {
            Snapshot s = ReadSnapshot();
            if (s.Hp == 0 && (hp || fp || stamina)) throw new InvalidOperationException("角色已倒下，修改已暂停");
            // The object chain is re-resolved on every tick. Maxima are never altered.
            if (hp && s.Hp < s.MaxHp) WriteStat(s, HpOffset, s.MaxHp);
            if (fp && s.Fp < s.MaxFp) WriteStat(s, FpOffset, s.MaxFp);
            if (stamina && s.Stamina < s.MaxStamina) WriteStat(s, StaminaOffset, s.MaxStamina);
            return s;
        }
        internal void WriteStat(Snapshot s, long offset, int value) {
            Snapshot current = ReadSnapshot();
            if (current.Player != s.Player || current.Module != s.Module || current.Hp <= 0)
                throw new InvalidOperationException("角色已改变，写入取消");
            if (offset != HpOffset && offset != FpOffset && offset != StaminaOffset) throw new InvalidOperationException("未知属性");
            int limit = offset == HpOffset ? current.MaxHp : offset == FpOffset ? current.MaxFp : current.MaxStamina;
            if (value < (offset == HpOffset ? 1 : 0) || value > limit) throw new InvalidOperationException("属性值超出当前上限");
            Mem.WriteInt32(s.Module + offset, value);
        }
        internal void SetRunes(int value) {
            if (value < 0 || value > 999999999) throw new InvalidOperationException("卢恩超出范围");
            Snapshot s = ReadSnapshot();
            if (s.Hp <= 0) throw new InvalidOperationException("角色未存活");
            if (s.Runes != value && !savesBackedUp) {
                BackupSaves();
                savesBackedUp = true;
                Snapshot afterBackup = ReadSnapshot();
                if (afterBackup.Player != s.Player || afterBackup.GameData != s.GameData)
                    throw new InvalidOperationException("备份期间角色已切换，写入已取消");
            }
            Mem.WriteInt32(s.GameData + 0x6C, value);
            Log("Set runes: " + s.Runes + " -> " + value);
        }
        internal void BackupSaves() {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nightreign");
            if (!Directory.Exists(root)) throw new InvalidOperationException("未找到存档目录，卢恩写入已取消");
            string[] files = Directory.GetFiles(root, "NR0000.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".co2",StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sl2",StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".co2.bak",StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sl2.bak",StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length == 0) throw new InvalidOperationException("未找到游戏存档，卢恩写入已取消");
            string dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            foreach (string file in files) {
                string account = Path.GetFileName(Path.GetDirectoryName(file));
                string folder = Path.Combine(dest, account); Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, Path.GetFileName(file));
                bool stable=false;
                for (int attempt=0;attempt<3 && !stable;attempt++) {
                    // Compare full source bytes before/after copying to detect concurrent saves.
                    byte[] before = File.ReadAllBytes(file);
                    File.WriteAllBytes(target, before);
                    byte[] after = File.ReadAllBytes(file);
                    stable = before.SequenceEqual(after);
                }
                if (!stable) throw new InvalidOperationException("游戏正在写存档，请稍后重试；未修改卢恩");
            }
            File.WriteAllText(Path.Combine(dest,"restore-instructions.txt"),
                "Close the game before restoring. Account subfolders correspond to %APPDATA%\\Nightreign. Copy the needed save back manually. Snapshots are file backups taken while the game is running; an internally inconsistent existing save cannot be repaired by this backup.\r\n",Encoding.UTF8);
            Log("Save backup created: "+dest);
        }
        internal static void Log(string line) {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trainer.log"),
                DateTimeOffset.Now.ToString("o") + " " + line + Environment.NewLine, Encoding.UTF8); }
            catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
        public void Dispose() {
            try { RestoreAdvanced(); }
            finally { if (Mem != null) { Mem.Dispose(); Mem = null; } if (process != null) process.Dispose(); }
        }
    }

    internal sealed partial class TrainerForm : Form
    {
        Engine engine;
        readonly CheckBox hp = new CheckBox(), fp = new CheckBox(), stamina = new CheckBox();
        readonly CheckBox godMode=new CheckBox(),highDamage=new CheckBox(),noCooldown=new CheckBox(),infiniteUltimate=new CheckBox(),infiniteItems=new CheckBox(),freezeClock=new CheckBox();
        readonly Label status = new Label(), stats = new Label();
        readonly Label skillStatus=new Label(),clockStatus=new Label();
        readonly NumericUpDown runes = new NumericUpDown();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Button connect = new Button();
        readonly Button setRunes = new Button(), restoreRunes = new Button();
        int? previousRunes;
        long previousGameData;
        int previousPid;
        bool closing;
        readonly bool previewMode;
        bool hotkeyConflict;
        internal TrainerForm(bool preview = false) {
            previewMode=preview;
            Text = "黑夜君临 · 游戏功能栏 v0.4"; ClientSize = new Size(780, 630);
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(242, 244, 247);
            AddLabel("黑夜君临  1.3.2.0", 22, 18, 600, 34, 18F);
            AddLabel("角色属性修改 · 按需开启 · F4 关闭全部持续修改", 24, 57, 600, 28, 10F);
            connect.Text = "连接游戏"; connect.SetBounds(24, 99, 120, 36);
            connect.Click += (s,e) => {autoConnectPaused=false;Connect();}; Controls.Add(connect);
            var detach = new Button { Text = "断开 / 全部关闭" }; detach.SetBounds(155, 99, 160, 36);
            detach.Click += (s,e) => {autoConnectPaused=true;Disconnect("已断开，所有持续修改已关闭");}; Controls.Add(detach);
            status.SetBounds(24, 145, 730, 28); status.Text = "尚未连接"; Controls.Add(status);
            stats.SetBounds(24, 176, 730, 28); stats.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold); Controls.Add(stats);
            SetupCheck(hp, "F11  锁血（恢复至生命上限）", 216);
            SetupCheck(fp, "F2  无限蓝量（免消耗＋恢复）", 250);
            SetupCheck(stamina, "F3  无限精力（免消耗＋恢复）", 284);
            SetupCheck(godMode,"F5  角色无敌：免伤＋防死（实验）",318);
            SetupCheck(highDamage,"F6  极高伤害／秒杀（实验）",352);
            SetupCheck(noCooldown,"F7  角色技能无冷却",216,410);
            SetupCheck(infiniteUltimate,"F8  绝招能量不消耗",250,410);
            SetupCheck(infiniteItems,"F9  道具不消耗（实验）",284,410);
            SetupCheck(freezeClock,"F10  冻结黑夜时间（实验）",318,410);
            AddLabel("局内卢恩", 24, 408, 100, 28, 10F);
            runes.SetBounds(120, 406, 160, 30); runes.Maximum = 999999999; runes.Value = 999999; runes.ThousandsSeparator = true; Controls.Add(runes);
            setRunes.Text = "写入"; setRunes.SetBounds(290, 404, 90, 34); setRunes.Enabled = false;
            setRunes.Click += (s,e) => {
                try {
                    Snapshot before = RequireEngine().ReadSnapshot();
                    engine.SetRunes((int)runes.Value);
                    if (!previousRunes.HasValue) { previousRunes = before.Runes; previousGameData = before.GameData; previousPid = engine.Pid; }
                    restoreRunes.Enabled = true; status.Text = "卢恩已写入；可在同一角色会话中恢复原数值";
                } catch (Exception ex) { HandleError(ex); }
            }; Controls.Add(setRunes);
            restoreRunes.Text = "恢复原数值"; restoreRunes.SetBounds(390, 404, 135, 34); restoreRunes.Enabled = false;
            restoreRunes.Click += (s,e) => {
                try {
                    Snapshot now = RequireEngine().ReadSnapshot();
                    if (!previousRunes.HasValue || now.GameData != previousGameData || engine.Pid != previousPid)
                        throw new InvalidOperationException("角色会话已改变，不能套用旧数值");
                    engine.SetRunes(previousRunes.Value); previousRunes = null; restoreRunes.Enabled = false;
                    status.Text = "已恢复首次写入前的卢恩数值";
                } catch (Exception ex) { HandleError(ex); }
            }; Controls.Add(restoreRunes);
            skillStatus.SetBounds(24,458,730,28);Controls.Add(skillStatus);
            clockStatus.SetBounds(24,490,730,28);Controls.Add(clockStatus);
            AddLabel("F4 关闭所有持续功能；正常关闭窗口会恢复本工具记录的标志和时间倍率。",24,532,730,26,9F);
            AddLabel("冻结时间可能阻止缩圈和流程推进；继续任务前请关闭。无敌仍需实战验证。",24,560,730,26,9F);
            AddLabel("遗物、透视、移速／跳跃及结算绕过仍未实现，详情见 说明.md。",24,588,730,26,9F);
            timer.Interval = 20; timer.Tick += (s,e) => UpdateState(); if(!previewMode) timer.Start();
            Shown += (s,e) => { if(!previewMode) OverlayFirstShown(); };
            FormClosing += (s,e) => {
                if(ShouldHideInsteadOfClose(e)){e.Cancel=true;HideOverlay(true);return;}
                closing = true; timer.Stop(); Disconnect("已关闭");
            };
            InitializeOverlay();
        }
        void AddLabel(string text, int x, int y, int width, int height, float size) {
            Controls.Add(new Label { Text = text, Location = new Point(x,y), Size = new Size(width,height), Font = new Font("Microsoft YaHei UI",size) });
        }
        void SetupCheck(CheckBox box, string text, int y,int x=24) {
            box.Text = text; box.SetBounds(x, y, 350, 30); box.Enabled = false;
            box.CheckedChanged += (s,e) => { if (engine != null) Engine.Log(text + ": " + box.Checked); };
            Controls.Add(box);
        }
        Engine RequireEngine() { if (engine == null) throw new InvalidOperationException("请先连接游戏"); return engine; }
        internal void RenderPreview(string path) {
            try{using(var e=new Engine(false)) {
                    stats.Text=e.ReadSnapshot().Text(); status.Text="已连接 PID "+e.Pid;
                    hp.Enabled=fp.Enabled=stamina.Enabled=setRunes.Enabled=true;
                    foreach(var box in AdvancedBoxes())box.Enabled=true;
                    UpdateTelemetry(e,e.ReadSnapshot());
                }}catch(InvalidOperationException){status.Text="等待游戏启动并进入角色场景 · F1 显示／隐藏";}
            StartPosition=FormStartPosition.Manual; Location=new Point(-10000,-10000); ShowInTaskbar=false;
            Show(); Application.DoEvents();
            using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(path); }
            Close();
        }
        void Connect() {
            Disconnect("正在连接并校验版本…");
            try { engine = new Engine(true); var s = engine.ReadSnapshot(); status.Text = "已连接 PID " + engine.Pid+(hotkeyConflict?"；部分快捷键被占用，请关闭旧版窗口":""); stats.Text = s.Text();
                hp.Enabled = fp.Enabled = stamina.Enabled = setRunes.Enabled = true;
                foreach(var box in AdvancedBoxes())box.Enabled=true;
                UpdateTelemetry(engine,s);Engine.Log("Attached " + engine.Pid + "; " + s.Text()); }
            catch (Exception ex) { Disconnect(ex.Message); }
        }
        void Disconnect(string message) {
            hp.Checked = fp.Checked = stamina.Checked = false;
            hp.Enabled = fp.Enabled = stamina.Enabled = setRunes.Enabled = restoreRunes.Enabled = false;
            foreach(var box in AdvancedBoxes()){box.Checked=false;box.Enabled=false;}
            previousRunes = null;
            if (engine != null) { engine.Dispose(); engine = null; }
            status.Text = message; stats.Text = "";skillStatus.Text="";clockStatus.Text="";
        }
        CheckBox[] AdvancedBoxes(){return new[]{godMode,highDamage,noCooldown,infiniteUltimate,infiniteItems,freezeClock};}
        void UpdateTelemetry(Engine e,Snapshot s){
            try{skillStatus.Text=e.ReadSkill(s).Text();}catch(InvalidOperationException){skillStatus.Text="技能信息暂不可用";}
            try{var c=e.ReadClock();clockStatus.Text=c.Text();freezeClock.Enabled=c.IsExpedition;}catch(InvalidOperationException){clockStatus.Text="昼夜信息暂不可用";freezeClock.Enabled=false;}
        }
        int frames;
        void UpdateState() {
            if (engine == null || closing) return;
            try {
                var s = engine.Tick(hp.Checked,fp.Checked,stamina.Checked);
                engine.ConfigureNative(NativeFeature.NoFpConsume,fp.Checked);
                engine.ConfigureNative(NativeFeature.NoStaminaConsume,stamina.Checked);
                engine.ConfigureNative(NativeFeature.HighDamage,highDamage.Checked);
                engine.ConfigureNative(NativeFeature.NoSkillCooldown,noCooldown.Checked);
                engine.ConfigureNative(NativeFeature.InfiniteUltimate,infiniteUltimate.Checked);
                engine.ConfigureNative(NativeFeature.InfiniteItems,infiniteItems.Checked);
                engine.ConfigureGodMode(godMode.Checked);
                engine.ConfigureClockFreeze(freezeClock.Checked);
                engine.TickAdvanced(s);
                if (++frames % 10 == 0) {stats.Text = s.Text();UpdateTelemetry(engine,s);}
            }
            catch (Exception ex) { HandleError(ex); }
        }
        void HandleError(Exception ex) { Engine.Log("Paused: " + ex.Message); Disconnect(ex.Message + "；重新进入场景后点击连接"); }
        protected override void WndProc(ref Message m) {
            if(HandleOverlayMessage(ref m))return;
            if(m.Msg==0x0312 && !FeatureHotkeysAllowed()){base.WndProc(ref m);return;}
            if (m.Msg == 0x0312 && engine != null) {
                if ((int)m.WParam == 1 && hp.Enabled) hp.Checked = !hp.Checked;
                if ((int)m.WParam == 2 && fp.Enabled) fp.Checked = !fp.Checked;
                if ((int)m.WParam == 3 && stamina.Enabled) stamina.Checked = !stamina.Checked;
                if ((int)m.WParam == 4) { hp.Checked = fp.Checked = stamina.Checked = false;foreach(var box in AdvancedBoxes())box.Checked=false; }
                CheckBox selected=null;
                switch((int)m.WParam){case 5:selected=godMode;break;case 6:selected=highDamage;break;case 7:selected=noCooldown;break;case 8:selected=infiniteUltimate;break;case 9:selected=infiniteItems;break;case 10:selected=freezeClock;break;}
                if(selected!=null && selected.Enabled)selected.Checked=!selected.Checked;
            }
            base.WndProc(ref m);
        }
        [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            if(previewMode) return;
            SyncGameHotkeys();
        }
        protected override void OnHandleDestroyed(EventArgs e) {
            if(!previewMode)ReleaseGameHotkeys();
            base.OnHandleDestroyed(e);
        }
    }

    internal static partial class Program
    {
        [STAThread] static int Main(string[] args) {
            try {
                if (args.Contains("--diagnose")) {
                    using (var e = new Engine(false)) {
                        Snapshot s = e.ReadSnapshot();
                        File.WriteAllText(OutputPath("diagnostics.txt"), "READ ONLY\r\nSHA256 " + Engine.ExpectedHash + "\r\nPID " + e.Pid + "\r\n" + s.Text() +
                            "\r\nWorld 0x" + s.World.ToString("X") + "\r\nPlayer 0x" + s.Player.ToString("X") + "\r\nData 0x" + s.Module.ToString("X") + "\r\n", Encoding.UTF8);
                    } return 0;
                }
                if (args.Contains("--self-test")) { SelfTest(); return 0; }
                if (args.Contains("--verify-live")) { VerifyLive(); return 0; }
                if (args.Contains("--verify-advanced")) { VerifyAdvanced(); return 0; }
                if (args.Contains("--verify-clock")) { VerifyClock(); return 0; }
                if (args.Contains("--diagnose-advanced")) { DiagnoseAdvanced(); return 0; }
                if (args.Contains("--backup-saves")) { using(var e=new Engine(false)) { e.ReadSnapshot(); e.BackupSaves(); } return 0; }
                if (args.Contains("--render-preview")) {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using(var form=new TrainerForm(true)) form.RenderPreview(OutputPath("preview-v0.4.png")); return 0;
                }
                if(args.Contains("--verify-overlay")){VerifyOverlay();return 0;}
                using(var singleton=new System.Threading.Mutex(false,"Local\\NightreignTrainerOverlay_v04")){
                    if(!singleton.WaitOne(0,false)){TrainerForm.RequestExistingOverlay();return 0;}
                    try{
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new TrainerForm());return 0;
                    }finally{singleton.ReleaseMutex();}
                }
            } catch (Exception ex) {
                File.WriteAllText(OutputPath("last-error.txt"), ex.ToString(),Encoding.UTF8);
                if (args.Length == 0) MessageBox.Show(ex.Message,"修改器"); return 1;
            }
        }
        static string OutputPath(string file) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,file); }
        static void Assert(bool condition,string message) { if (!condition) throw new Exception("TEST FAILED: " + message); }
        static void SelfTest() {
            IntPtr block = Marshal.AllocHGlobal(64);
            try {
                Marshal.WriteInt32(block,42);
                using (var m = new Memory(Process.GetCurrentProcess().Id,true)) {
                    Assert(m.Int32(block.ToInt64())==42,"native read"); m.WriteInt32(block.ToInt64(),73);
                    Assert(Marshal.ReadInt32(block)==73,"native write/readback");
                }
                using(var m = new Memory(Process.GetCurrentProcess().Id,false)) {
                    bool rejected=false;try {m.WriteInt32(block.ToInt64(),1);}catch(InvalidOperationException){rejected=true;}
                    Assert(rejected && Marshal.ReadInt32(block)==73,"read-only connection rejects writes");
                    rejected=false;try {m.Read(0,4);}catch(InvalidOperationException){rejected=true;}
                    Assert(rejected,"null pointer rejected");
                }
                var s=new Snapshot{Hp=800,MaxHp=800,Fp=110,MaxFp=110,Stamina=102,MaxStamina=102}; Engine.ValidateStats(s);
                bool bad=false;s.MaxHp=-1;try{Engine.ValidateStats(s);}catch(InvalidOperationException){bad=true;}Assert(bad,"invalid layout rejected");
                File.WriteAllText(OutputPath("self-test.txt"),"PASS: native read, write/readback, read-only guard, null pointer guard, stats validation, invalid layout rejection.\r\n",Encoding.UTF8);
            } finally { Marshal.FreeHGlobal(block); }
        }
        static void VerifyLive() {
            var result = new StringBuilder("Live verification: reversible attribute refill only.\r\n");
            using(var e = new Engine(true)) {
                Snapshot start=e.ReadSnapshot();result.AppendLine("Before: "+start.Text());
                long[] offsets={Engine.HpOffset,Engine.FpOffset,Engine.StaminaOffset};
                string[] names={"HP","FP","Stamina"};
                for(int i=0;i<offsets.Length;i++) {
                    Snapshot s=e.ReadSnapshot(); int value=e.Mem.Int32(s.Module+offsets[i]);
                    int max=i==0?s.MaxHp:i==1?s.MaxFp:s.MaxStamina;
                    if(value!=max || value<2) throw new InvalidOperationException("Live test requires full, nonzero attributes");
                    try {
                        e.WriteStat(s,offsets[i],value-1);
                        e.Tick(i==0,i==1,i==2);
                        Assert(e.Mem.Int32(s.Module+offsets[i])==max,names[i]+" refill");
                        result.AppendLine("PASS "+names[i]+": "+value+" -> "+(value-1)+" -> "+max);
                    } finally {
                        Snapshot now=e.ReadSnapshot();
                        if(now.Module==s.Module && now.Player==s.Player && now.Hp>0) e.WriteStat(now,offsets[i],value);
                    }
                }
                // A no-op resource write verifies access without awarding currency.
                Snapshot r=e.ReadSnapshot();e.SetRunes(r.Runes);
                result.AppendLine("PASS runes no-op write/readback: "+r.Runes+" (amount unchanged)");
                result.AppendLine("After: "+e.ReadSnapshot().Text());
                result.AppendLine("Combat, one-shot survival, actual spending and new expedition transitions were not tested.");
            }
            File.WriteAllText(OutputPath("live-verification.txt"),result.ToString(),Encoding.UTF8);
        }
    }
}
