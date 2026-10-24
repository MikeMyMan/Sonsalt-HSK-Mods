using System;
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
    /// Ideas from CAI 5000 (research/ai/CAI5000.md section 9), reimplemented for CE:
    /// - evade area threats: out of tox gas (unless masked), away from fire next to the pawn, and away from the landing
    ///   cell of an explosive the defenders threw or launched (CE projectiles: recorded at launch - CAI's own version
    ///   misses them because ProjectileCE is not a vanilla Projectile);
    /// - duck when aimed at: a raider in the open that a defender is aiming at right now (warm-up on him) steps into
    ///   cover within 4 cells, or goes prone if there is none.
    /// First node of the approach / assault / support duties. Not for shield bearers (they draw fire on purpose),
    /// breachers at work or close quarters.
    /// </summary>
    public static class Evasion
    {
        public static int Evades, Ducks;
        /// <summary>Landing areas of the player's explosives in flight: cell, radius, until tick.</summary>
        public static readonly List<(Map map, IntVec3 cell, float radius, int until)> incoming = new List<(Map, IntVec3, float, int)>();

        public static bool InIncoming(Map map, IntVec3 c)
        {
            int now = Find.TickManager.TicksGame;
            incoming.RemoveAll(i => i.until < now);
            foreach (var i in incoming) if (i.map == map && i.cell.DistanceTo(c) <= i.radius) return true;
            return false;
        }

        // the stat once per pawn per tick, not per candidate cell (pf5: Evade up to 10 ms in gas)
        private static Pawn resistPawn;
        private static int resistTick = -1;
        private static bool resistValue;

        private static bool ToxResistant(Pawn p)
        {
            int now = Find.TickManager.TicksGame;
            if (p != resistPawn || now != resistTick)
            {
                resistPawn = p;
                resistTick = now;
                resistValue = p.GetStatValue(StatDefOf.ToxicEnvironmentResistance) >= 0.5f;
            }
            return resistValue;
        }

        /// <summary>A cell this pawn should not stand in: incoming explosive, tox gas (no protection), burning next to it.</summary>
        public static bool Hazard(Pawn p, IntVec3 c)
        {
            Map map = p.Map;
            if (InIncoming(map, c)) return true;
            try
            {
                if (map.gasGrid.DensityAt(c, GasType.ToxGas) > 20 && !ToxResistant(p)) return true;
            }
            catch { }
            foreach (IntVec3 a in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)))
                if (a.InBounds(map) && FireUtility.ContainsStaticFire(a, map)) return true;
            return FireUtility.ContainsStaticFire(c, map);
        }

        /// <summary>The nearest standable, reachable cell within 6 that is not a hazard (cover towards the objective preferred).</summary>
        public static IntVec3 SafeNearby(Pawn p, IntVec3 objective)
        {
            Map map = p.Map;
            return PositionFinder.Find(p, 8f,
                cell => cell.c == p.Position || Hazard(p, cell.c) ? float.NaN
                        : cell.path - (JobGiver_HSKTAI_Support.HasCover(cell.c, objective, map) ? 1.5f : 0f) + cell.exposure * 3f,
                null, 1);
        }

        /// <summary>The defender aiming at this pawn right now (warm-up on him), if any.</summary>
        public static Pawn Aimer(Pawn p, AssaultPlan plan)
        {
            foreach (var d in Suppression.Defenders(p.Map, plan.objective, 60f))
                if (d.stances?.curStance is Stance_Warmup w && w.focusTarg.Thing == p) return d;
            return null;
        }
    }

    public class JobGiver_HSKTAI_Evade : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            if (!s.evasion) return null;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || plan.phase == PlanPhase.Withdraw) return null;
            if (pawn.CurJobDef == TAI_DefOf.HSKTAI_PlantBreachCharge || pawn.carryTracker?.CarriedThing != null) return null;
            bool bearer = ShieldWall.IsBearer(plan, pawn);
            // 1) area threats: out of it
            if (Evasion.Hazard(pawn, pawn.Position))
            {
                IntVec3 safe = Evasion.SafeNearby(pawn, plan.objective);
                if (safe.IsValid)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, safe);
                    go.locomotionUrgency = LocomotionUrgency.Sprint;
                    go.expiryInterval = 90;
                    Evasion.Evades++;
                    return go;
                }
            }
            // 2) duck when aimed at (in the open, not in close quarters, not a shield bearer)
            if (bearer || !s.duckWhenAimed || pawn.CurJobDef == JobDefOf.AttackStatic) return null;
            Pawn aimer = Evasion.Aimer(pawn, plan);
            if (aimer == null || aimer.Position.DistanceTo(pawn.Position) <= 8f) return null;          // close quarters: fight
            if (JobGiver_HSKTAI_Support.HasCover(pawn.Position, aimer.Position, pawn.Map)) return null;   // already covered
            IntVec3 best = IntVec3.Invalid;
            float bd = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, 4f, false))
            {
                if (!c.InBounds(pawn.Map) || !c.Standable(pawn.Map) || !JobGiver_HSKTAI_Support.HasCover(c, aimer.Position, pawn.Map) || Evasion.Hazard(pawn, c)) continue;
                float d = c.DistanceTo(pawn.Position);
                if (d < bd && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) { bd = d; best = c; }
            }
            // nothing to duck behind: no prone here (pr2: 18 of 30 prone events were ducks) - prone is for full suppression
            if (!best.IsValid) return null;
            Evasion.Ducks++;
            Job duck = JobMaker.MakeJob(JobDefOf.Goto, best);
            duck.locomotionUrgency = LocomotionUrgency.Sprint;
            duck.expiryInterval = 60;
            return duck;
        }
    }

    /// <summary>Records where the player's explosives (thrown or launched) will land (CE ProjectileCE.Launch).</summary>
    [HarmonyPatch]
    public static class Patch_IncomingExplosives
    {
        private static Type tProj, tExplosive, tExplosiveProps;
        private static FieldInfo fDest, fTicks, fRadius;

        public static bool Prepare()
        {
            tProj = AccessTools.TypeByName("CombatExtended.ProjectileCE");
            tExplosive = AccessTools.TypeByName("CombatExtended.CompExplosiveCE");
            tExplosiveProps = AccessTools.TypeByName("CombatExtended.CompProperties_ExplosiveCE");
            if (tProj == null) return false;
            fDest = AccessTools.Field(tProj, "Destination");
            fTicks = AccessTools.Field(tProj, "startingTicksToImpact");
            if (tExplosiveProps != null) fRadius = AccessTools.Field(tExplosiveProps, "explosiveRadius");
            return fDest != null;
        }

        public static MethodBase TargetMethod() => AccessTools.Method(tProj, "Launch", new[] { typeof(Thing), typeof(Vector2), typeof(Thing) });

        public static void Postfix(Thing __instance, Thing launcher)
        {
            try
            {
                if (launcher?.Faction != Faction.OfPlayer || __instance.Map == null || !TacticalAIMod.Settings.evasion) return;
                float r = __instance.def.projectile?.explosionRadius ?? 0f;
                if (r <= 0f && tExplosive != null)
                {
                    ThingComp ex = (__instance as ThingWithComps)?.AllComps.FirstOrDefault(c => tExplosive.IsInstanceOfType(c));
                    if (ex != null && fRadius != null) r = (float)fRadius.GetValue(ex.props);
                }
                if (r <= 0f) return;
                Vector2 dest = (Vector2)fDest.GetValue(__instance);
                float ticks = fTicks != null ? Convert.ToSingle(fTicks.GetValue(__instance)) : 120f;
                var cell = new IntVec3(Mathf.RoundToInt(dest.x), 0, Mathf.RoundToInt(dest.y));
                Evasion.incoming.Add((__instance.Map, cell, r + 2f, Find.TickManager.TicksGame + Mathf.Clamp((int)ticks + 30, 30, 600)));
            }
            catch { }
        }
    }
}
