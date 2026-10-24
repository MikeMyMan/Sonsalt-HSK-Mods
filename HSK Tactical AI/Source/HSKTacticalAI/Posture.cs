using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Posture (playtest, 2026-10-23: "make units go prone during firefights when they are suppressed, to lower their
    /// profile a bit and chance to get hit ... panic, shoot randomly into the general direction etc. and flee ... prone
    /// and crawling when injured").
    ///
    /// Combat Extended already: crouches behind low cover (a crouching pawn hides up to the cover's height), runs for
    /// cover and crouch-walks when suppressed, hunkers (stops firing) under heavy suppression; downed pawns crawl
    /// (vanilla). Panic fire: the fire doctrine has suppressed raiders fire snap bursts back. Flight: Enemy Self
    /// Preservation. What was missing is going flat in the open: crouching only helps next to cover.
    ///
    /// Prone: a suppressed raider with no cover towards the nearest enemy drops flat (hediff HSKTAI_Prone: CE height
    /// x proneHeightFactor, crawl speed). It keeps shooting. Not for shield bearers (they have the shield), breachers
    /// planting, raiders in close quarters (enemy within 6) or in a withdrawal. Up again proneRecoverTicks after the
    /// suppression ends.
    /// </summary>
    public static class Posture
    {
        public static int ProneEvents;
        private static HediffDef proneDef;
        public static HediffDef Prone => proneDef ?? (proneDef = DefDatabase<HediffDef>.GetNamedSilentFail("HSKTAI_Prone"));
        private static readonly Dictionary<Pawn, int> lastSuppressed = new Dictionary<Pawn, int>();

        // CE asks for the hit profile on every collision check: a set, not a hediff search
        private static readonly HashSet<Pawn> prone = new HashSet<Pawn>();
        public static bool IsProne(Pawn p) => p != null && prone.Contains(p);

        /// <summary>Every 30 ticks for the raiders under a plan.</summary>
        public static void Tick(Map map)
        {
            var s = TacticalAIMod.Settings;
            if (Prone == null) return;
            int now = Find.TickManager.TicksGame;
            // prone without a plan (plans are not saved: after a reload the raid plans anew): stand up
            if (map.IsHashIntervalTick(250))
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
                    if ((IsProne(p) || p.health?.hediffSet?.HasHediff(Prone) == true) && !AssaultPlanner.Plans.Values.Any(pl => pl.members.Contains(p))) Stand(p);
            foreach (var plan in AssaultPlanner.Plans.Values)
            {
                foreach (Pawn p in plan.members)
                {
                    if (p.Map != map || !p.Spawned || p.Dead) continue;
                    if (!prone.Contains(p) && p.health?.hediffSet?.HasHediff(Prone) == true) prone.Add(p);   // after a reload
                    bool isProne = IsProne(p);
                    if (p.Downed || !s.posture) { if (isProne) Stand(p); continue; }
                    int state = Suppression.State(p);
                    bool suppressed = state >= 1;
                    if (suppressed) lastSuppressed[p] = now;
                    Pawn enemy = ShieldWall.NearestEnemy(p, 60f);
                    bool exempt = plan.phase == PlanPhase.Withdraw || ShieldWall.IsBearer(plan, p)
                                  || p.CurJobDef == TAI_DefOf.HSKTAI_PlantBreachCharge
                                  || (enemy != null && enemy.Position.DistanceTo(p.Position) <= 6f);
                    if (isProne)
                    {
                        lastSuppressed.TryGetValue(p, out int last);
                        if (exempt || now - last > s.proneRecoverTicks) Stand(p);
                        continue;
                    }
                    if (state < 2 || exempt || enemy == null) continue;   // full suppression only (playtest: "only with full suppression")
                    if (JobGiver_HSKTAI_Support.HasCover(p.Position, enemy.Position, map)) continue;   // CE crouch does it
                    p.health.AddHediff(Prone);
                    prone.Add(p);
                    ProneEvents++;
                }
            }
        }

        /// <summary>Duck: drop flat now (no cover within reach while being aimed at); up again after proneRecoverTicks.</summary>
        public static void DropProne(Pawn p)
        {
            if (Prone == null || p == null || p.Downed || IsProne(p)) return;
            p.health.AddHediff(Prone);
            prone.Add(p);
            lastSuppressed[p] = Find.TickManager.TicksGame;
            ProneEvents++;
        }

        private static void Stand(Pawn p)
        {
            prone.Remove(p);
            var h = p.health?.hediffSet?.GetFirstHediffOfDef(Prone);
            if (h != null) p.health.RemoveHediff(h);
        }
    }

    /// <summary>
    /// Crawl speed (playtest: "when they do their movement should slow down"): ticks per cell / proneMoveFactor while prone.
    /// In TicksPerMove itself - a MoveSpeed factor on the hediff went through stat caches (Performance Optimizer) and
    /// arrived after the raider was up again.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "TicksPerMove")]
    public static class Patch_ProneMove
    {
        public static void Postfix(Pawn __instance, ref float __result)
        {
            if (!Posture.IsProne(__instance) || __instance.Downed) return;
            float f = TacticalAIMod.Settings.proneMoveFactor;
            if (f > 0f && f < 1f) __result = Mathf.Min(450f, __result / f);
        }
    }

    /// <summary>
    /// "prone" above a prone raider for as long as he is prone (playtest: "so players know they are prone and not down") -
    /// drawn with the pawn labels every frame; replaces the short pop-up text.
    /// </summary>
    [HarmonyPatch(typeof(PawnUIOverlay), nameof(PawnUIOverlay.DrawPawnGUIOverlay))]
    public static class Patch_ProneLabel
    {
        private static readonly AccessTools.FieldRef<PawnUIOverlay, Pawn> pawnOf = AccessTools.FieldRefAccess<PawnUIOverlay, Pawn>("pawn");
        private static readonly Color colour = new Color(0.85f, 0.85f, 0.55f);

        public static void Postfix(PawnUIOverlay __instance)
        {
            if (!TacticalAIMod.Settings.proneLabel) return;
            Pawn p = pawnOf(__instance);
            if (p == null || !p.Spawned || p.Downed || !Posture.IsProne(p) || p.Map.fogGrid.IsFogged(p.Position)) return;
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(p, 0.75f), "prone", colour);
        }
    }

    /// <summary>CE hit profile: a prone pawn is a fraction of its standing height.</summary>
    [HarmonyPatch]
    public static class Patch_ProneCollision
    {
        public static bool Prepare() => AccessTools.TypeByName("CombatExtended.CE_Utility") != null;

        public static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("CombatExtended.CE_Utility"), "GetCollisionBodyFactors", new[] { typeof(Pawn) });

        public static void Postfix(Pawn pawn, ref Vector2 __result)
        {
            long t0 = Perf.Start();
            bool prone = pawn != null && !pawn.Downed && Posture.IsProne(pawn);
            Perf.Stop("ce.proneProfile", t0);
            if (!prone) return;
            __result.y *= TacticalAIMod.Settings.proneHeightFactor;
        }
    }
}
