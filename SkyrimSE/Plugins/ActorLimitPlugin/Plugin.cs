using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetScriptFramework;
using NetScriptFramework.SkyrimSE;

namespace ActorLimitPlugin
{
    public class ActorLimitPlugin : Plugin
    {
        public override string Key
        {
            get
            {
                return "actor_limit";
            }
        }

        public override string Name
        {
            get
            {
                return "Actor Limit Plugin";
            }
        }

        public override int Version
        {
            get
            {
                return 2;
            }
        }

        private const ulong ContainsNearbyActorVid = 0x100000003;

        internal Settings Settings;

        protected override bool Initialize(bool loadedAny)
        {
            this.Settings = new Settings();
            this.Settings.Load();

            bool modern = NetScriptFramework.Main.Game.GameVersion[1] >= 7;
            var gameInfo = NetScriptFramework.Main.GameInfo;

            // Maximum actor movement updates.
            if(this.Settings.MoverLimit != 128)
            {
                var funcAssign = gameInfo.GetAddressOf(40296, modern ? 0x92 : 0x90, 0, "BE");
                var funcComp = gameInfo.GetAddressOf(40296, modern ? 0x9E : 0x9C, 0, "3D");

                int defaultCap = 128;
                int prev = NetScriptFramework.Memory.ReadInt32(funcAssign + 1);
                int prev2 = NetScriptFramework.Memory.ReadInt32(funcComp + 1);
                if (prev != 0x80 || prev2 != 0x80)
                {
                    if (prev == prev2)
                        NetScriptFramework.Main.Log.AppendLine("ActorLimitPlugin: Warning! Another mod may have already changed the mover limit. Expected to find " + defaultCap + " but it's " + prev + " instead. Skipping.");
                    else
                        NetScriptFramework.Main.Log.AppendLine("ActorLimitPlugin: Warning! Another mod may have already incorrectly changed the mover limit. Expected to find " + defaultCap + " but it's " + prev + " / " + prev2 + " instead. Skipping.");
                    return true;
                }

                Memory.WriteInt32(funcAssign + 1, this.Settings.MoverLimit, true);
                Memory.WriteInt32(funcComp + 1, this.Settings.MoverLimit, true);
            }

            // Replace static buffer.
            if(this.Settings.ReplaceStaticBuffer)
            {
                var contains = gameInfo.GetAddressOf(40435, 0x21, 0, modern ? "48 8D 0D 58 4D AC 02" : "48 8D 0D");
                var containsStride = gameInfo.GetAddressOf(40435, 0x31, 0, "48 C1 E3 09");
                var copy = gameInfo.GetAddressOf(40277, 0x26, 0, modern ? "48 8D 0D E3 44 AD 02" : "48 8D 0D");
                var copyStride = gameInfo.GetAddressOf(40277, 0x22, 0, "48 C1 E3 09");
                var morph = gameInfo.GetAddressOf(40427, modern ? 0x1D0 : 0x1C1, 0, modern ? "48 8D 0D 69 57 AC 02" : "48 8D 0D");
                var morphStride = gameInfo.GetAddressOf(40427, modern ? 0x1CC : 0x1BD, 0, "48 C1 E7 09");
                var append = gameInfo.GetAddressOf(40434, modern ? 0xBA : 0x61, 0, modern ? "48 8D 05 EF 4E AC 02" : "48 8D 05");
                var appendStride = gameInfo.GetAddressOf(40434, modern ? 0xB6 : 0x70, 0, "48 C1 E3 09");
                var appendLimit = gameInfo.GetAddressOf(40434, modern ? 0x94 : 0x41, 0, modern ? "83 FF 40 0F 83 DF 00 00 00" : "83 FF 40");
                var appendExit = gameInfo.GetAddressOf(40434, modern ? 0x17C : 0x137, 0, modern ? "49 8B CE 48 8B 9C 24 80 00 00 00" : "48 8B 6C 24 60");

                // 1.7 also reads the current bank through ProcessLists::ContainsNearbyActor.
                var search = modern ? gameInfo.GetAddressOf(ContainsNearbyActorVid, 0x60, 0, "48 8D 0D 49 02 AD 02") : IntPtr.Zero;
                var searchStride = modern ? gameInfo.GetAddressOf(ContainsNearbyActorVid, 0x6E, 0, "48 C1 E7 09") : IntPtr.Zero;

                var alloc = Memory.Allocate(0x2000);
                alloc.Pin();
                Memory.WriteZero(alloc.Address, alloc.Size);

                ReplaceBufferAddress(contains, ctx => ctx.CX = alloc.Address);
                Memory.WriteUInt8(containsStride + 3, 12, true);
                ReplaceBufferAddress(copy, ctx => ctx.CX = alloc.Address);
                Memory.WriteUInt8(copyStride + 3, 12, true);
                ReplaceBufferAddress(morph, ctx => ctx.CX = alloc.Address);
                Memory.WriteUInt8(morphStride + 3, 12, true);
                ReplaceBufferAddress(append, ctx => ctx.AX = alloc.Address);
                Memory.WriteUInt8(appendStride + 3, 12, true);
                if (modern)
                {
                    ReplaceBufferAddress(search, ctx => ctx.CX = alloc.Address);
                    Memory.WriteUInt8(searchStride + 3, 12, true);
                }

                Memory.WriteHook(new HookParameters()
                {
                    Address = appendLimit,
                    IncludeLength = 0,
                    ReplaceLength = 9,
                    Before = ctx =>
                    {
                        if(ctx.DI.ToUInt32() >= 512)
                            ctx.IP = appendExit;
                    }
                });

                /*Memory.WriteHook(new HookParameters()
                {
                    Address = new IntPtr(0x1406D2E30).FromBase(),
                    IncludeLength = 5,
                    ReplaceLength = 5,
                    Before = ctx =>
                    {
                        NetScriptFramework.Main.WriteDebugMessage("DEBUG FUNC CALLED!");
                    }
                });*/
            }

            // Maximum morph updates limit.
            if(this.Settings.MorphLimit != 0xA)
            {
                int want = !this.Settings.ReplaceStaticBuffer ? Math.Min(62, this.Settings.MorphLimit) : Math.Min(510, this.Settings.MorphLimit);
                int defaultCap = 0xA;
                var cap1 = gameInfo.GetAddressOf(40427, modern ? 0x1E8 : 0x1D9, 0, "BE");
                int read1 = Memory.ReadInt32(cap1 + 1);
                var cap2 = gameInfo.GetAddressOf(507579, 8);
                int read2 = Memory.ReadInt32(cap2);
                if (read1 != defaultCap || read2 != defaultCap)
                {
                    NetScriptFramework.Main.Log.AppendLine("ActorLimitPlugin: Warning! Another mod may have already changed the morph limit. Expected to find " + defaultCap + " but it's " + read1 + " / " + read2 + " instead. Skipping.");
                }
                else
                {
                    Memory.WriteInt32(cap1 + 1, want, true);
                    Memory.WriteInt32(cap2, want);
                }
            }

            return true;
        }

        private static void ReplaceBufferAddress(IntPtr address, Action<CPURegisters> setRegister)
        {
            Memory.WriteHook(new HookParameters()
            {
                Address = address,
                IncludeLength = 0,
                ReplaceLength = 7,
                Before = setRegister
            });
        }
    }
}
