using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugFixesSSE.Fixes
{
    class FixSpeedMultUpdate : Fix
    {
        internal override string Description
        {
            get
            {
                return "There is a bug where if an actor's speedmult actor value changes it does not immediately update the movement speed until something happens that would recalculate the movement speed (sprint, walk, carry weight changes). This is a problem because it stops things like slow poison or temporary movement speed buffs from working correctly.";
            }
        }

        private IntPtr CustomFunc;
        private IntPtr UnkFunc;
        private IntPtr UnkGlobal;
        private IntPtr UpdateSpeed;

        private void InstallCallback(IntPtr slot)
        {
            if (NetScriptFramework.Memory.ReadPointer(slot) != IntPtr.Zero)
                throw new InvalidOperationException("The fix " + this.Key + " can't be applied because there was already something else there!");

            NetScriptFramework.Memory.WritePointer(slot, this.CustomFunc);
        }

        internal override void Apply()
        {
            bool modern = NetScriptFramework.Main.Game.GameVersion[1] >= 7;
            this.UnkFunc = NetScriptFramework.Main.GameInfo.GetAddressOf(36585);
            this.UnkGlobal = NetScriptFramework.Main.GameInfo.GetAddressOf(516851);
            this.UpdateSpeed = NetScriptFramework.Main.GameInfo.GetAddressOf(36916);
            var replace1 = NetScriptFramework.Main.GameInfo.GetAddressOf(517383, -8);
            var hook1 = NetScriptFramework.Main.GameInfo.GetAddressOf(5998, modern ? 0x39 : 4, 0,
                modern ? "48 89 10 48 89 50 08" : "E8");
            int hookLength = modern ? 7 : 5;
            int flagsOffset = modern ? 0x340 : 208;

            var alloc = NetScriptFramework.Memory.Allocate(0x20, 0, true);
            alloc.Pin();
            this.CustomFunc = alloc.Address;
            NetScriptFramework.Memory.WriteNop(this.CustomFunc, 13);
            NetScriptFramework.Memory.WriteUInt8(this.CustomFunc + 13, 0xC3, true);

            NetScriptFramework.Memory.WriteHook(new NetScriptFramework.HookParameters()
            {
                Address = this.CustomFunc,
                IncludeLength = 0,
                ReplaceLength = 13,
                ForceLongJump = true,
                Before = ctx =>
                {
                    var actor = ctx.CX;
                    NetScriptFramework.Memory.InvokeCdecl(this.UnkFunc, actor);

                    var gb = NetScriptFramework.Memory.ReadPointer(this.UnkGlobal);
                    if (gb != IntPtr.Zero && ((NetScriptFramework.Memory.ReadUInt32(gb + flagsOffset) >> 1) & 1) == 0)
                        NetScriptFramework.Memory.InvokeCdecl(this.UpdateSpeed, actor);
                },
            });

            // The InventoryWeight entry follows SpeedMult and becomes non-null during initialization.
            if (modern && NetScriptFramework.Memory.ReadPointer(replace1 + 8) != IntPtr.Zero)
            {
                this.InstallCallback(replace1);
                return;
            }

            // The 1.7 site runs after the native loop has cleared the SpeedMult entry.
            NetScriptFramework.Memory.WriteHook(new NetScriptFramework.HookParameters()
            {
                Address = hook1,
                IncludeLength = hookLength,
                ReplaceLength = hookLength,
                After = ctx => this.InstallCallback(replace1),
            });
        }
    }
}
