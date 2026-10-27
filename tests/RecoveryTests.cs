// Exercises rollback against memory owned by this test process, never the game.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
namespace NightreignLocalTrainer {
    internal static class RecoveryTests {
        static void Set(Engine e,string field,object value){typeof(Engine).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(e,value);}
        static void Check(bool value,string label){if(!value)throw new Exception("FAIL "+label);}
        static int Main(){
            IntPtr globals=Marshal.AllocHGlobal(0x5000),actor=Marshal.AllocHGlobal(0x300),clock=Marshal.AllocHGlobal(0x200);
            var report=new StringBuilder("Rollback integration tests: this process's allocated memory only.\r\n");
            Engine e=null;
            try{
                for(int i=0;i<0x5000;i++)Marshal.WriteByte(globals,i,0);
                for(int i=0;i<0x300;i++)Marshal.WriteByte(actor,i,0);
                for(int i=0;i<0x200;i++)Marshal.WriteByte(clock,i,0);
                e=(Engine)FormatterServices.GetUninitializedObject(typeof(Engine));
                e.Base=globals.ToInt64()-0x3C0F000;e.Mem=new Memory(Process.GetCurrentProcess().Id,true);
                Set(e,"process",Process.GetCurrentProcess());
                var leases=new Dictionary<NativeFeature,NativeLease>();Set(e,"nativeLeases",leases);
                foreach(NativeFeature f in Enum.GetValues(typeof(NativeFeature))){
                    leases.Add(f,new NativeLease{Original=0});e.Mem.WriteByte(e.Base+Engine.FeatureRva(f),1);
                }
                Marshal.WriteInt64(actor,e.Base+Engine.DataVtable);Marshal.WriteInt64(actor,8,0x12340000);
                e.Mem.WriteByte(actor.ToInt64()+Engine.GodFlagOffset,0x8C);
                Set(e,"godLease",new GodLease{Module=actor.ToInt64(),Player=0x12340000,OriginalBits=0});
                Marshal.WriteInt64(new IntPtr(e.Base+Engine.ClockSlot),clock.ToInt64());
                Set(e,"clockLease",new ClockLease{Manager=clock.ToInt64(),OriginalScale=72.9F});
                e.Mem.WriteFloat(clock.ToInt64()+0x118,0);e.RestoreAdvanced();
                foreach(NativeFeature f in Enum.GetValues(typeof(NativeFeature)))Check(e.Mem.Byte(e.Base+Engine.FeatureRva(f))==0,"native rollback "+f);
                Check(e.Mem.Byte(actor.ToInt64()+Engine.GodFlagOffset)==0x80,"unrelated actor bits preserved");
                Check(e.Mem.Float(clock.ToInt64()+0x118)==72.9F,"original clock scale restored");
                report.AppendLine("PASS all six native flags restored; unrelated actor bit 0x80 preserved; clock scale restored.");
                leases.Add(NativeFeature.HighDamage,new NativeLease{Original=1});e.Mem.WriteByte(e.Base+Engine.FeatureRva(NativeFeature.HighDamage),0);
                Set(e,"clockLease",new ClockLease{Manager=clock.ToInt64(),OriginalScale=72.9F});e.Mem.WriteFloat(clock.ToInt64()+0x118,4);
                e.RestoreAdvanced();
                Check(e.Mem.Byte(e.Base+Engine.FeatureRva(NativeFeature.HighDamage))==0,"external native flag change preserved");
                Check(e.Mem.Float(clock.ToInt64()+0x118)==4,"external clock scale preserved");
                report.AppendLine("PASS external native flag and clock-scale changes were preserved.");
                e.Mem.WriteByte(actor.ToInt64()+Engine.GodFlagOffset,0x0C);Marshal.WriteInt64(actor,8,0x99990000);
                Set(e,"godLease",new GodLease{Module=actor.ToInt64(),Player=0x12340000,OriginalBits=0});
                e.Mem.WriteFloat(clock.ToInt64()+0x118,0);
                Set(e,"clockLease",new ClockLease{Manager=clock.ToInt64(),OriginalScale=72.9F});
                Marshal.WriteInt64(new IntPtr(e.Base+Engine.ClockSlot),clock.ToInt64()+0x20);e.RestoreAdvanced();
                Check(e.Mem.Byte(actor.ToInt64()+Engine.GodFlagOffset)==0x0C,"reused actor object not touched");
                Check(e.Mem.Float(clock.ToInt64()+0x118)==0,"old clock object not touched after manager change");
                report.AppendLine("PASS changed actor ownership and changed clock-manager pointer prevent stale-object writes.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"rollback-verification.txt"),report.ToString(),Encoding.UTF8);return 0;
            }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"rollback-error.txt"),ex.ToString());return 1;}
            finally{if(e!=null)e.Dispose();Marshal.FreeHGlobal(globals);Marshal.FreeHGlobal(actor);Marshal.FreeHGlobal(clock);}
        }
    }
}
