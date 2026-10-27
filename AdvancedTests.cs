using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace NightreignLocalTrainer
{
    internal static partial class Program
    {
        static void DiagnoseAdvanced() {
            var report=new StringBuilder("READ ONLY: v0.3 advanced diagnosis\r\n");
            using(var e=new Engine(false)) {
                var s=e.ReadSnapshot();report.AppendLine(s.Text());report.AppendLine(e.ReadSkill(s).Text());
                foreach(NativeFeature f in Enum.GetValues(typeof(NativeFeature)))report.AppendLine(f+" @ RVA 0x"+Engine.FeatureRva(f).ToString("X")+" = "+e.ReadFeature(f));
                report.AppendLine("Local actor flags = 0x"+e.Mem.Byte(s.Module+Engine.GodFlagOffset).ToString("X2"));
                try{report.AppendLine(e.ReadClock().Text());}catch(InvalidOperationException ex){report.AppendLine("Clock unavailable: "+ex.Message);}
            }
            File.WriteAllText(OutputPath("advanced-diagnostics.txt"),report.ToString(),Encoding.UTF8);
        }
        static void VerifyAdvanced() {
            var report=new StringBuilder("v0.3 live reversible verification. Does not simulate attacks, purchases or skill input.\r\n");
            using(var e=new Engine(true)) {
                var start=e.ReadSnapshot();report.AppendLine("Before: "+start.Text());
                try {
                    foreach(NativeFeature f in Enum.GetValues(typeof(NativeFeature))) {
                        byte original=e.ReadFeature(f);
                        e.ConfigureNative(f,true);Assert(e.ReadFeature(f)==1,f+" enabled");
                        e.ConfigureNative(f,false);Assert(e.ReadFeature(f)==original,f+" restored");
                        report.AppendLine("PASS "+f+": "+original+" -> 1 -> "+original);
                    }
                    var s=e.ReadSnapshot();byte before=e.Mem.Byte(s.Module+Engine.GodFlagOffset);
                    e.ConfigureGodMode(true);e.TickAdvanced(s);
                    Assert((e.Mem.Byte(s.Module+Engine.GodFlagOffset)&Engine.GodMask)==Engine.GodMask,"actor god mode bits enabled");
                    e.ConfigureGodMode(false);
                    Assert((e.Mem.Byte(s.Module+Engine.GodFlagOffset)&Engine.GodMask)==(before&Engine.GodMask),"actor god mode bits restored");
                    report.AppendLine("PASS actor invulnerability/no-death mask: original -> 0x0C -> original mask");
                    bool rejected=false;try{Engine.FeatureRva((NativeFeature)999);}catch(InvalidOperationException){rejected=true;}
                    Assert(rejected,"unknown feature rejected");report.AppendLine("PASS unknown native feature rejected");
                    report.AppendLine("Skill telemetry: "+e.ReadSkill(e.ReadSnapshot()).Text());
                } finally { e.RestoreAdvanced(); }
                report.AppendLine("After: "+e.ReadSnapshot().Text());
                report.AppendLine("All modified control flags restored. Full gameplay effects remain separate from this roundtrip test.");
            }
            File.WriteAllText(OutputPath("advanced-verification.txt"),report.ToString(),Encoding.UTF8);
        }
        static void VerifyClock() {
            var report=new StringBuilder("v0.3 live game-clock freeze/resume verification\r\n");
            using(var e=new Engine(true)) {
                ClockSnapshot before=e.ReadClock();report.AppendLine("Before: "+before.Text());
                ClockSnapshot frozen1=null,frozen2=null;
                try {
                    e.ConfigureClockFreeze(true);
                    Thread.Sleep(100);frozen1=e.ReadClock();
                    Assert(frozen1.Scale==0,"clock scale set to zero");
                    Thread.Sleep(350);frozen2=e.ReadClock();
                    Assert(frozen2.Manager==frozen1.Manager,"same clock manager");
                    Assert(frozen2.Scale==0 && frozen1.Packed==frozen2.Packed,"packed game time remained unchanged");
                    report.AppendLine("PASS frozen for 350 ms: "+frozen1.Text()+"; packed=0x"+frozen1.Packed.ToString("X"));
                } finally { e.ConfigureClockFreeze(false); }
                ClockSnapshot restored=e.ReadClock();
                Assert(restored.Manager==before.Manager && restored.Scale==before.Scale,"original clock scale restored");
                Thread.Sleep(250);ClockSnapshot resumed=e.ReadClock();
                Assert(resumed.Manager==restored.Manager && resumed.Packed!=restored.Packed,"clock advanced after restore");
                report.AppendLine("PASS original scale restored: "+restored.Scale);
                report.AppendLine("PASS clock resumed: "+resumed.Text());
                report.AppendLine("This verifies the day/night clock only. Weather, enemy spawns, damage outside the circle and mission completion were not tested.");
            }
            File.WriteAllText(OutputPath("clock-verification.txt"),report.ToString(),Encoding.UTF8);
        }
    }
}
