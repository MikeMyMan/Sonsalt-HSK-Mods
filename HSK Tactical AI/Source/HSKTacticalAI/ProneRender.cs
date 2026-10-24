using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// Prone pose (playtest, 2026-10-24: "the message prone pops up but the unit is not going to prone position"): a prone
    /// raider is drawn in vanilla's crawl pose - body flat, still facing where he moves or aims - with his weapon shown.
    /// Render only: in PawnRenderer.ParallelGetPreRenderResults the three reads that pick the pose (Crawling, GetPosture,
    /// BodyAngle) answer "crawling, lying, crawl angle" for prone pawns. Everything else - the game's own posture checks,
    /// GetDrawParms (which reads the posture again, so the weapon is still drawn and aimed), the AI - still sees a
    /// standing pawn. The prone state is read from the hediff (the renderer may run off the main thread; vanilla reads
    /// hediffs there too).
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    public static class Patch_ProneRender
    {
        private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> pawnOf = AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");
        public static int Replaced;

        public static bool ShowProne(Pawn p)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.proneVisual || p == null || !p.Spawned || p.Downed || p.Dead) return false;
            HediffDef def = Posture.Prone;
            return def != null && p.health?.hediffSet?.HasHediff(def) == true;
        }

        public static bool Crawling(Pawn p) => p.Crawling || ShowProne(p);

        public static PawnPosture GetPosture(Pawn p)
        {
            PawnPosture posture = p.GetPosture();
            return posture == PawnPosture.Standing && ShowProne(p) ? PawnPosture.LayingOnGroundNormal : posture;
        }

        public static float BodyAngle(PawnRenderer r, PawnRenderFlags flags)
        {
            Pawn p = pawnOf(r);
            return ShowProne(p) ? PawnRenderUtility.CrawlingBodyAngle(p.Rotation) : r.BodyAngle(flags);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo crawling = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Crawling));
            MethodInfo bodyAngle = AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle));
            foreach (var ci in code)
            {
                if (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call)
                {
                    var m = ci.operand as MethodInfo;
                    if (m != null && m == crawling) { ci.opcode = OpCodes.Call; ci.operand = AccessTools.Method(typeof(Patch_ProneRender), nameof(Crawling)); Replaced++; }
                    else if (m != null && m.Name == "GetPosture" && m.IsStatic && m.ReturnType == typeof(PawnPosture)) { ci.operand = AccessTools.Method(typeof(Patch_ProneRender), nameof(GetPosture)); Replaced++; }
                    else if (m != null && m == bodyAngle) { ci.opcode = OpCodes.Call; ci.operand = AccessTools.Method(typeof(Patch_ProneRender), nameof(BodyAngle)); Replaced++; }
                }
                yield return ci;
            }
            if (Replaced < 3) Log.Warning("[HSK Tactical AI] prone pose: renderer changed, " + Replaced + "/3 calls replaced - prone raiders may still be drawn standing");
        }
    }
}
