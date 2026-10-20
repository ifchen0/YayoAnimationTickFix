using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using YayoAnimation;
using YayoAnimation.Data;

namespace YayoAnimationTickFix
{
    [StaticConstructorOnStartup]
    public static class Init
    {
        static Init() => new Harmony("ifchen0.yayoanimationtickfix").PatchAll();
    }

    /// <summary>
    /// Yayo computes animation frames as (TicksGame + thingIDNumber * 20) % length. In long-running saves the sum
    /// overflows int, the remainder turns negative and Core.Ani extrapolates its first segment instead of playing
    /// the animation (e.g. the burning roll pops far to the bottom-right and slides back with a fixed facing).
    /// Every remainder in the standing/laying animation code is replaced with a non-negative one.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_AnimationCore_Remainder
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AnimationCore), nameof(AnimationCore.AniStanding));
            yield return AccessTools.Method(typeof(AnimationCore), nameof(AnimationCore.AniLaying));
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var posMod = AccessTools.Method(typeof(Patch_AnimationCore_Remainder), nameof(PosMod));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Rem)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = posMod;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced == 0)
                Log.Warning($"[Yayo Animation Tick Fix] No remainder found in {original.Name}; Yayo's Animation may have changed.");
        }

        public static int PosMod(int value, int divisor)
        {
            int r = value % divisor;
            return r < 0 ? r + divisor : r;
        }
    }

    /// <summary>
    /// For standing jobs Yayo has no animation for (e.g. custom jobs from other mods), AniStanding caches the facing
    /// in fixedRot with nextUpdateTick = int.MaxValue, so it is never refreshed until the job changes. Rotation
    /// changes made by the job driver mid-job are then drawn with the stale facing. Only that cached case is
    /// invalidated when the facing differs; animations that set their own facing refresh on their own schedule.
    /// </summary>
    [HarmonyPatch(typeof(AnimationCore), nameof(AnimationCore.CheckAni))]
    public static class Patch_AnimationCore_CheckAni
    {
        public static void Prefix(Pawn pawn, Rot4 rot, PawnDrawData pdd)
        {
            if (pdd.nextUpdateTick == int.MaxValue && pdd.fixedRot.HasValue && pdd.fixedRot.Value != rot
                && pawn.GetPosture() == PawnPosture.Standing)
                pdd.nextUpdateTick = int.MinValue;
        }
    }
}
