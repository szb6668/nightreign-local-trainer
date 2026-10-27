using System;
using System.Collections.Generic;
using System.Linq;

namespace NightreignLocalTrainer
{
    internal enum NativeFeature { NoFpConsume, NoStaminaConsume, HighDamage, InfiniteItems, NoSkillCooldown, InfiniteUltimate }
    internal sealed class NativeLease { internal byte Original; }
    internal sealed class GodLease { internal long Player, Module; internal byte OriginalBits; }
    internal sealed class ClockLease { internal long Manager; internal float OriginalScale; }
    internal sealed class SkillSnapshot {
        internal long Module;
        internal float Ultimate, MaxUltimate, Elapsed, Cooldown;
        internal byte Charges, MaxCharges;
        internal string Text() { return String.Format("技能 {0}/{1}  ·  冷却进度 {2:F1}/{3:F1}s  ·  绝招 {4:F1}/{5:F1}",Charges,MaxCharges,Elapsed,Cooldown,Ultimate,MaxUltimate); }
    }
    internal sealed class ClockSnapshot {
        internal long Manager;
        internal ulong Packed;
        internal float Scale, BaseScale;
        internal byte Phase, Active, Ready;
        internal int Hour, Minute, Second;
        internal bool IsExpedition { get { return Active==1 && Ready==1 && Phase<=2; } }
        internal string Text() { return String.Format("局内时间 {0:D2}:{1:D2}:{2:D2}  ·  时间倍率 {3:F2}  ·  {4}",Hour,Minute,Second,Scale,IsExpedition?"出击中":"未在出击中"); }
    }

    internal sealed partial class Engine
    {
        internal const long DebugSlot=0x3C0F338, DebugVtable=0x2C1BB30, ClockSlot=0x3C13158;
        internal const long SkillVtable=0x2C142C0, GodFlagOffset=0x189;
        internal const byte GodMask=0x0C;
        readonly Dictionary<NativeFeature,NativeLease> nativeLeases=new Dictionary<NativeFeature,NativeLease>();
        GodLease godLease;
        ClockLease clockLease;
        bool godWanted;
        internal static long FeatureRva(NativeFeature feature) {
            switch(feature) {
                case NativeFeature.NoFpConsume:return 0x3C0F2F8;
                case NativeFeature.NoStaminaConsume:return 0x3C0F2F7;
                case NativeFeature.HighDamage:return 0x3C0F2F5;
                case NativeFeature.InfiniteItems:return 0x3C0F2F6;
                case NativeFeature.NoSkillCooldown:return 0x3C0F304;
                case NativeFeature.InfiniteUltimate:return 0x3C0F305;
                default:throw new InvalidOperationException("未知调试功能");
            }
        }
        void CheckDebug() { CheckType(Mem.Pointer(Base+DebugSlot),DebugVtable,"调试管理器"); }
        internal byte ReadFeature(NativeFeature feature) {
            CheckDebug();byte value=Mem.Byte(Base+FeatureRva(feature));
            if(value>1) throw new InvalidOperationException("调试标志不是布尔值，停止修改");
            return value;
        }
        internal void ConfigureNative(NativeFeature feature,bool enabled) {
            FeatureRva(feature);
            if(!enabled) { ReleaseNative(feature);return; }
            if(nativeLeases.ContainsKey(feature)) return;
            Snapshot player=ReadSnapshot();
            if(player.Hp<=0) throw new InvalidOperationException("角色已倒下，不能启用新功能");
            byte before=ReadFeature(feature);
            // Journal before writing so a failed readback can still be rolled back.
            nativeLeases.Add(feature,new NativeLease{Original=before});
            if(before!=1) Mem.WriteByte(Base+FeatureRva(feature),1);
            Log("Native enabled: "+feature+"; original="+before);
        }
        void ReleaseNative(NativeFeature feature) {
            NativeLease lease;if(!nativeLeases.TryGetValue(feature,out lease)) return;
            if(Mem!=null && !process.HasExited) {
                byte current=Mem.Byte(Base+FeatureRva(feature));
                if(current==1 && current!=lease.Original) Mem.WriteByte(Base+FeatureRva(feature),lease.Original);
                else if(current!=lease.Original) Log("Native restore skipped after external change: "+feature);
            }
            nativeLeases.Remove(feature);Log("Native released: "+feature);
        }
        internal void ConfigureGodMode(bool enabled) {
            godWanted=enabled;
            if(!enabled) RestoreGodMode();
        }
        void ApplyGodMode(Snapshot s) {
            if(!godWanted)return;
            if(s.Hp<=0)throw new InvalidOperationException("角色已倒下，无敌模式暂停");
            if(godLease!=null && (godLease.Module!=s.Module || godLease.Player!=s.Player)) RestoreGodMode();
            if(godLease==null) {
                byte current=Mem.Byte(s.Module+GodFlagOffset);
                godLease=new GodLease{Player=s.Player,Module=s.Module,OriginalBits=(byte)(current&GodMask)};
            }
            CheckType(s.Module,DataVtable,"角色属性");
            if(Mem.Pointer(s.Module+8)!=s.Player || Mem.Pointer(s.World+MainPlayer)!=s.Player)
                throw new InvalidOperationException("本机角色已改变，无敌写入取消");
            byte flags=Mem.Byte(s.Module+GodFlagOffset);
            if((flags&GodMask)!=GodMask) Mem.WriteByte(s.Module+GodFlagOffset,(byte)(flags|GodMask));
        }
        void RestoreGodMode() {
            GodLease lease=godLease;if(lease==null)return;
            if(Mem!=null && !process.HasExited) {
                try {
                    CheckType(lease.Module,DataVtable,"原角色属性");
                    if(Mem.Pointer(lease.Module+8)==lease.Player) {
                        byte current=Mem.Byte(lease.Module+GodFlagOffset);
                        if((current&GodMask)==GodMask) Mem.WriteByte(lease.Module+GodFlagOffset,(byte)((current&~GodMask)|lease.OriginalBits));
                    }
                } catch(InvalidOperationException ex) { Log("Actor restore unavailable: "+ex.Message); }
            }
            godLease=null;
        }
        internal SkillSnapshot ReadSkill(Snapshot s) {
            long modules=Mem.Pointer(s.Player+0x1B8),skill=Mem.Pointer(modules+0x148);
            CheckType(skill,SkillVtable,"角色技能");
            if(Mem.Pointer(skill+8)!=s.Player) throw new InvalidOperationException("技能归属校验失败");
            byte[] data=Mem.Read(skill+0x14,0x1C);
            var k=new SkillSnapshot{Module=skill,Ultimate=BitConverter.ToSingle(data,0),MaxUltimate=BitConverter.ToSingle(data,4),Charges=data[12],MaxCharges=data[13],Elapsed=BitConverter.ToSingle(data,16),Cooldown=BitConverter.ToSingle(data,20)};
            if(!FiniteRange(k.Ultimate,0,100000) || !FiniteRange(k.MaxUltimate,0,100000) ||
                !FiniteRange(k.Elapsed,0,100000) || !FiniteRange(k.Cooldown,0,100000) || k.MaxCharges>32 || k.Charges>k.MaxCharges)
                throw new InvalidOperationException("技能数据范围异常");
            return k;
        }
        internal static bool FiniteRange(float value,float min,float max) { return !float.IsNaN(value) && !float.IsInfinity(value) && value>=min && value<=max; }
        internal ClockSnapshot ReadClock() {
            long manager=Mem.Pointer(Base+ClockSlot);byte[] data=Mem.Read(manager+0x100,0x60);
            ulong packed=BitConverter.ToUInt64(data,0);
            var c=new ClockSnapshot{Manager=manager,Packed=packed,Scale=BitConverter.ToSingle(data,0x18),BaseScale=BitConverter.ToSingle(data,0x1C),Phase=data[0x54],Active=data[0x59],Ready=data[0x5E],Hour=(int)((packed>>34)&31),Minute=(int)((packed>>39)&63),Second=(int)((packed>>45)&63)};
            if(c.Hour>=24 || c.Minute>=60 || c.Second>=60 || !FiniteRange(c.Scale,0,5000) || !FiniteRange(c.BaseScale,0.001F,5000) || c.Active>1 || c.Ready>1)
                throw new InvalidOperationException("昼夜时间管理器未就绪");
            if(Mem.Pointer(Base+ClockSlot)!=manager) throw new InvalidOperationException("昼夜管理器正在切换");
            return c;
        }
        internal void ConfigureClockFreeze(bool enabled) {
            if(!enabled) { RestoreClock();return; }
            if(clockLease!=null)return;
            Snapshot s=ReadSnapshot();ClockSnapshot c=ReadClock();
            if(s.Hp<=0 || !c.IsExpedition || c.Scale<=0) throw new InvalidOperationException("冻结时间需在存活的出击场景中开启");
            clockLease=new ClockLease{Manager=c.Manager,OriginalScale=c.Scale};
            Mem.WriteFloat(c.Manager+0x118,0);
            Log("Clock frozen; original scale="+c.Scale);
        }
        void RestoreClock() {
            ClockLease lease=clockLease;if(lease==null)return;
            if(Mem!=null && !process.HasExited) {
                if(Mem.Pointer(Base+ClockSlot)==lease.Manager) {
                    // Do not overwrite a value another owner has subsequently changed.
                    if(Mem.Float(lease.Manager+0x118)==0) Mem.WriteFloat(lease.Manager+0x118,lease.OriginalScale);
                    else Log("Clock restore skipped after external change");
                } else Log("Clock manager changed; old heap object was not touched");
            }
            clockLease=null;Log("Clock released");
        }
        internal void TickAdvanced(Snapshot snapshot) {
            if(nativeLeases.Count>0) {
                if(snapshot.Hp<=0)throw new InvalidOperationException("角色已倒下，原生调试功能暂停");
                CheckDebug();
                foreach(var feature in nativeLeases.Keys.ToArray()) {
                    byte current=ReadFeature(feature);
                    if(current!=1)Mem.WriteByte(Base+FeatureRva(feature),1);
                }
            }
            ApplyGodMode(snapshot);
            if(clockLease!=null) {
                ClockSnapshot c=ReadClock();
                if(c.Manager!=clockLease.Manager || !c.IsExpedition || snapshot.Hp<=0) {
                    RestoreClock();throw new InvalidOperationException("出击场景或昼夜管理器已切换，冻结时间已停止");
                }
                if(c.Scale!=0) throw new InvalidOperationException("游戏重新设置了时间倍率，冻结已停止");
            }
        }
        internal void RestoreAdvanced() {
            // Attempt every independent rollback even if another one fails.
            godWanted=false;
            try{RestoreClock();}catch(Exception ex){Log("Clock rollback: "+ex.Message);}
            try{RestoreGodMode();}catch(Exception ex){Log("Actor rollback: "+ex.Message);}
            foreach(var feature in nativeLeases.Keys.ToArray())try{ReleaseNative(feature);}catch(Exception ex){Log("Native rollback "+feature+": "+ex.Message);}
        }
    }
}
