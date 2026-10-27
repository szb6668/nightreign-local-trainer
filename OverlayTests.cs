using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
namespace NightreignLocalTrainer
{
    internal static partial class Program
    {
        static void VerifyOverlay()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var report=new StringBuilder("v0.4 overlay lifecycle tests. No game memory connection or simulated game input.\r\n");
            Rectangle monitor=new Rectangle(-1920,0,1920,1080);
            Rectangle fit=TrainerForm.FitOverlay(monitor,new Size(780,630));
            Assert(monitor.Contains(fit) && fit.Size==new Size(780,630),"negative monitor coordinates");
            Rectangle small=new Rectangle(100,100,400,300);
            Assert(small.Contains(TrainerForm.FitOverlay(small,new Size(780,630))),"small window bounds");
            bool bad=false;try{TrainerForm.FitOverlay(Rectangle.Empty,new Size(780,630));}catch(ArgumentException){bad=true;}
            Assert(bad,"invalid display area rejected");report.AppendLine("PASS positioning: multiple monitors, small windows, invalid bounds.");
            using(var form=new TrainerForm())
            {
                form.StartOverlayTest(new Rectangle(-10000,-10000,1600,900));
                Assert(!form.PanelIsVisible && !form.OverlayTaskbarEntry && form.OverlayIsBorderless,"hidden borderless startup");
                form.TestCheckState=true;
                form.TestMenuMessage();Application.DoEvents();Assert(form.PanelIsVisible,"F1 message shows panel");
                form.TestMenuMessage();Application.DoEvents();Assert(!form.PanelIsVisible && form.TestCheckState,"F1 hides without clearing state");
                form.TestMenuMessage();form.TestEscape();Assert(!form.PanelIsVisible && form.TestCheckState,"Escape hides without clearing state");
                form.ToggleOverlay(true);form.Close();Assert(!form.IsDisposed && !form.PanelIsVisible,"ordinary close hides to tray");
                form.ExitOverlay();Assert(form.IsDisposed,"explicit exit disposes form and tray");
            }
            report.AppendLine("PASS background startup: hidden, no taskbar entry, borderless panel.");
            report.AppendLine("PASS F1 message show/hide, Escape hide, enabled state retained while hidden.");
            report.AppendLine("PASS close-to-tray and explicit shutdown lifecycle.");
            report.AppendLine("Physical hotkey, foreground handling and fullscreen composition require separate live UI verification.");
            File.WriteAllText(OutputPath("overlay-verification.txt"),report.ToString(),Encoding.UTF8);
        }
    }
}
