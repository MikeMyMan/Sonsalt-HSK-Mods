using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Each weapon at its own distance (playtest, 2026-10-25: "bowmen should stay in range and retreat when others get too
    /// close while melee units should close in on targets ... the longer the range the more it wants to stay away, pistols
    /// and SMGs are for shorter ranges than rifles and especially snipers").
    ///
    /// From the weapon's range R (CE range):
    /// - preferred distance = R x 0.45 (short guns) .. 0.70 (long guns, R 75+);
    /// - too close = (R - 18) x 0.35, at most 24: pistols / SMGs ~0 (they only step back from melee), rifles ~13,
    ///   snipers ~22; bows, crossbows and other pre-industrial ranged weapons at least 8-10 - they cannot fight close;
    /// - too far (assault only) = preferred x 1.3: short guns close in before they shoot, long guns stay back.
    /// Melee raiders close in on enemies in the open nearby, and do not turn their backs to run when one is close.
    /// A doctrine (KeepRange): a dull raid fights like vanilla, from wherever it stands.
    /// </summary>
    public static class RangeBand
    {
        public static int BackSteps, CloseIns, MeleeCharges;
        public static readonly Dictionary<string, string> Seen = new Dictionary<string, string>();   // weapon -> "R/pref/min" (Lab readout)

        public struct Band { public bool valid; public float range, pref, min, far; }

        public static Band Of(Pawn p)
        {
            var eq = p.equipment?.Primary;
            Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
            if (eq == null || v == null || v.IsMeleeAttack || v.verbProps.range < 3f) return default;
            float r = v.verbProps.range;
            float k = Mathf.Lerp(0.45f, 0.70f, Mathf.InverseLerp(20f, 75f, r));
            var b = new Band { valid = true, range = r, pref = r * k };
            b.min = Mathf.Clamp((r - 18f) * 0.35f, 0f, 24f);
            if (eq.def.techLevel <= TechLevel.Medieval) b.min = Mathf.Max(b.min, Mathf.Min(10f, b.pref * 0.6f));   // bows: never let them get close
            b.far = Mathf.Min(r * 0.95f, b.pref * 1.3f);
            if (AILab.Active && !Seen.ContainsKey(eq.def.defName))
                Seen[eq.def.defName] = Mathf.RoundToInt(r) + "/" + Mathf.RoundToInt(b.pref) + "/" + Mathf.RoundToInt(b.min);
            return b;
        }

        public static bool Knows(AssaultPlan plan) => TacticalAIMod.Settings.keepRange && Competence.Knows(plan, Doctrine.KeepRange);

        /// <summary>Close quarters: inside the breach, or an assault-role raider (SMG / shotgun) - back off from melee only.</summary>
        public static bool CloseQuarters(AssaultPlan plan, Pawn p) =>
            plan.entered.Contains(p) || (plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Assault || r == RaidRole.Shield));
    }

    /// <summary>Keep the weapon's distance: back off from enemies too close, close in on enemies too far (assault).</summary>
    public class JobGiver_HSKTAI_KeepRange : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || (plan.phase != PlanPhase.Approach && plan.phase != PlanPhase.Assault) || !RangeBand.Knows(plan)) return null;
            if (!pawn.RaceProps.Humanlike || pawn.carryTracker?.CarriedThing != null || ShieldWall.IsBearer(plan, pawn) || ShieldWall.BearerOf(plan, pawn) != null) return null;
            if (pawn.CurJobDef == TAI_DefOf.HSKTAI_PlantBreachCharge || plan.breachTeam.Contains(pawn) || CornerFire.Holding(pawn, plan)) return null;
            Map map = pawn.Map;
            if (Micro.IsMelee(pawn)) return Charge(pawn, plan, map);
            var b = RangeBand.Of(pawn);
            if (!b.valid) return null;
            Pawn enemy = ShieldWall.NearestEnemy(pawn, Mathf.Max(b.range + 15f, 30f));
            if (enemy == null) return null;
            float d = enemy.Position.DistanceTo(pawn.Position);
            if (d <= 1.5f) return null;   // in his reach already: fight
            bool cqb = RangeBand.CloseQuarters(plan, pawn);
            bool meleeClose = Micro.IsMelee(enemy) && d <= 5f;
            if (meleeClose || (!cqb && b.min >= 4f && d < b.min)) return BackOff(pawn, enemy, d, b, map, s);
            if (plan.phase == PlanPhase.Assault && d > b.far && d <= b.range + 15f && !plan.overwatchOf.ContainsKey(pawn)) return CloseIn(pawn, enemy, d, b, map, s);
            return null;
        }

        private static Job BackOff(Pawn pawn, Pawn enemy, float d, RangeBand.Band b, Map map, TacticalAISettings s)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            float want = Mathf.Min(b.pref, d + 8f);
            IntVec3 cell = PositionFinder.Find(pawn, 9f,
                c =>
                {
                    float de = c.c.DistanceTo(enemy.Position);
                    if (c.path < 2f || de < d + 2f) return float.NaN;
                    return c.path * 0.3f + Mathf.Abs(de - want) * 0.6f + c.exposure * 3f + (tm != null && tm.FireAt(c.c) > s.safeFire ? 5f : 0f);
                },
                c => GenSight.LineOfSight(c, enemy.Position, map, true) ? -3f : 0f, 12);   // keep a shot from there
            if (!cell.IsValid) return null;
            RangeBand.BackSteps++;
            Job go = JobMaker.MakeJob(JobDefOf.Goto, cell);
            go.locomotionUrgency = LocomotionUrgency.Sprint;
            go.expiryInterval = 90;
            go.checkOverrideOnExpire = true;
            return go;
        }

        private static Job CloseIn(Pawn pawn, Pawn enemy, float d, RangeBand.Band b, Map map, TacticalAISettings s)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            float floor = Mathf.Max(b.min + 1f, 4f);
            // cover to cover (cp9: pistols and SMGs closing in over open ground against rifles lost the raid): the next
            // spot must be covered from him or no hotter than here; the way there must not cross much fire
            float fireHere = tm != null && tm.CompletedCycles > 0 ? tm.FireAt(pawn.Position) : 0f;
            IntVec3 cell = PositionFinder.Find(pawn, 12f,
                c =>
                {
                    float de = c.c.DistanceTo(enemy.Position);
                    if (de >= d - 2f || de < floor || c.exposure > 1.5f) return float.NaN;
                    return Mathf.Abs(de - b.pref) * 0.5f + c.path * 0.2f + c.exposure * 3f + (tm != null && tm.FireAt(c.c) > s.safeFire ? 6f : 0f);
                },
                c =>
                {
                    bool covered = JobGiver_HSKTAI_Support.HasCover(c, enemy.Position, map);
                    if (!covered && tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > Mathf.Max(fireHere, s.safeFire)) return float.NaN;
                    return (covered ? -3f : 0f) + (GenSight.LineOfSight(c, enemy.Position, map, true) ? -4f : 2f);
                }, 12);
            if (!cell.IsValid) return null;
            RangeBand.CloseIns++;
            Job go = JobMaker.MakeJob(JobDefOf.Goto, cell);
            go.locomotionUrgency = tm != null && tm.FireAt(pawn.Position) > s.safeFire ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            go.expiryInterval = 150;
            go.checkOverrideOnExpire = true;
            return go;
        }

        /// <summary>Melee: an enemy in the open within 12 cells (no wall between) - go for him, also while holding position.</summary>
        private static Job Charge(Pawn pawn, AssaultPlan plan, Map map)
        {
            if (pawn.equipment?.Primary == null || pawn.WorkTagIsDisabled(WorkTags.Violent)) return null;   // fists: the unarmed node keeps him back
            Pawn enemy = ShieldWall.NearestEnemy(pawn, 12f);
            if (enemy == null || pawn.CurJobDef == JobDefOf.AttackMelee && pawn.CurJob.targetA.Thing == enemy) return null;
            if (!GenSight.LineOfSight(pawn.Position, enemy.Position, map, true) || !pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly)) return null;
            RangeBand.MeleeCharges++;
            Job hit = JobMaker.MakeJob(JobDefOf.AttackMelee, enemy);
            hit.expiryInterval = 240;
            hit.checkOverrideOnExpire = true;
            hit.killIncappedTarget = false;
            return hit;
        }
    }
}
