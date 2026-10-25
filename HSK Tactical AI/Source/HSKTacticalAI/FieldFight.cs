using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// Defenders out of their base (playtest, 2026-10-25: "when the defenders leave the base the attackers do not adjust
    /// their attack accordingly" - they kept waiting at their breach and assault spots, firing back only when hit).
    /// Defenders more than 15 cells from the objective and within 40 cells of the raid are a field fight:
    /// - everyone not busy at a wall turns on them: shooters from cover at their weapon's distance (a covered spot with a
    ///   line of fire, or holding and firing where they stand), melee raiders charge;
    /// - a sharp command (Flanking) sends every third rifleman round their side;
    /// - stepping back from those who come too close stays with the range rule (KeepRange);
    /// - once none has been out for 5 s, the raid goes back to its plan.
    /// A doctrine (FieldFight): a dull command only fires back when hit.
    /// </summary>
    public static class FieldFight
    {
        public static int Starts, Jobs, Flanks, Charges, ToCover;
        public const float BaseRadius = 15f;

        /// <summary>Every 60 ticks from the planner (approach and assault).</summary>
        public static void Tick(Map map, AssaultPlan plan, List<Pawn> alive, int now)
        {
            if (!TacticalAIMod.Settings.fieldFight || !Competence.Knows(plan, Doctrine.FieldFight)) { plan.fieldEnemies.Clear(); plan.fieldFight = false; return; }
            plan.fieldEnemies.Clear();
            foreach (var d in Suppression.Defenders(map, plan.objective, 999f))
                if (d.Position.DistanceTo(plan.objective) > BaseRadius && alive.Any(r => r.Position.DistanceTo(d.Position) <= 40f)) plan.fieldEnemies.Add(d);
            if (plan.fieldEnemies.Count > 0)
            {
                if (!plan.fieldFight)
                {
                    plan.fieldFight = true;
                    Starts++;
                    if (Prefs.DevMode) Log.Message("[HSK Tactical AI] field fight: " + plan.fieldEnemies.Count + " defenders out of their base");
                }
                plan.fieldLastSeen = now;
            }
            else if (plan.fieldFight && now - plan.fieldLastSeen > 300)
            {
                plan.fieldFight = false;
                plan.flankOf.Clear();
            }
        }

        public static Pawn Target(Pawn p, AssaultPlan plan, float maxDist = 55f)
        {
            Pawn best = null;
            float bs = float.MaxValue;
            foreach (var d in plan.fieldEnemies)
            {
                if (!d.Spawned || d.Downed || d.Dead) continue;
                float dist = d.Position.DistanceTo(p.Position);
                if (dist > maxDist) continue;
                float score = Targeting.Score(p, d, plan);
                if (score < bs) { bs = score; best = d; }
            }
            return best;
        }
    }

    public class JobGiver_HSKTAI_FieldFight : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || !plan.fieldFight || (plan.phase != PlanPhase.Approach && plan.phase != PlanPhase.Assault)) return null;
            if (!pawn.RaceProps.Humanlike || pawn.carryTracker?.CarriedThing != null || CornerFire.Holding(pawn, plan) || ShieldWall.BearerOf(plan, pawn) != null) return null;
            if (Arson.IsActiveArsonist(plan, pawn)) return null;
            Map map = pawn.Map;
            Pawn t = FieldFight.Target(pawn, plan, plan.mode == PlanMode.Sabotage ? 90f : 55f);
            if (t == null) return null;
            float d = t.Position.DistanceTo(pawn.Position);
            // breachers keep at their wall unless the sally reaches them
            if ((plan.breachTeam.Contains(pawn) || pawn.CurJobDef == TAI_DefOf.HSKTAI_PlantBreachCharge) && d > 15f) return null;
            if (Micro.IsMelee(pawn))
            {
                if (pawn.equipment?.Primary == null || pawn.WorkTagIsDisabled(WorkTags.Violent) || d > 30f || !pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly)) return null;
                if (pawn.CurJobDef == JobDefOf.AttackMelee && pawn.CurJob.targetA.Thing == t) return null;
                FieldFight.Charges++;
                FieldFight.Jobs++;
                Job hit = JobMaker.MakeJob(JobDefOf.AttackMelee, t);
                hit.expiryInterval = 240;
                hit.checkOverrideOnExpire = true;
                return hit;
            }
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null || v.IsMeleeAttack) return null;
            var b = RangeBand.Of(pawn);
            float range = b.valid ? b.range : v.verbProps.range;
            float pref = b.valid ? b.pref : range * 0.6f;
            float floor = Mathf.Max(b.valid ? b.min + 1f : 4f, 4f);

            // a sharp command: every third rifleman goes round their side
            if (Competence.Knows(plan, Doctrine.Flanking) && plan.roleOf.TryGetValue(pawn, out var role) && role == RaidRole.Rifleman && pawn.thingIDNumber % 3 == 0)
            {
                if (!plan.flankOf.TryGetValue(pawn, out IntVec3 fc) || !fc.IsValid)
                {
                    fc = FlankCell(pawn, plan, t, pref, map);
                    plan.flankOf[pawn] = fc;
                    if (fc.IsValid) FieldFight.Flanks++;
                }
                if (fc.IsValid && pawn.Position.DistanceTo(fc) > 3f && pawn.CanReach(fc, PathEndMode.OnCell, Danger.Deadly))
                {
                    FieldFight.Jobs++;
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, fc);
                    go.locomotionUrgency = LocomotionUrgency.Jog;
                    go.expiryInterval = 240;
                    go.checkOverrideOnExpire = true;
                    return go;
                }
            }
            // a shot from here at a sensible distance: hold and fire
            if (d <= range && d >= floor - 1f && v.CanHitTarget(t))
            {
                // in the open with cover a few steps away that keeps the shot: there first (playtest: "units should always seek cover")
                if (!JobGiver_HSKTAI_Support.HasCover(pawn.Position, t.Position, map))
                {
                    IntVec3 cov = PositionFinder.Find(pawn, 5f,
                        c => c.path < 1f || !JobGiver_HSKTAI_Support.HasCover(c.c, t.Position, map) || c.c.DistanceTo(t.Position) > range ? float.NaN : c.path + c.exposure * 3f,
                        c => GenSight.LineOfSight(c, t.Position, map, true) ? 0f : float.NaN, 8);
                    if (cov.IsValid && cov != pawn.Position)
                    {
                        FieldFight.ToCover++;
                        FieldFight.Jobs++;
                        Job toCover = JobMaker.MakeJob(JobDefOf.Goto, cov);
                        toCover.locomotionUrgency = LocomotionUrgency.Sprint;
                        toCover.expiryInterval = 120;
                        return toCover;
                    }
                }
                FieldFight.Jobs++;
                Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, t);
                shoot.maxNumStaticAttacks = 3;
                shoot.endIfCantShootTargetFromCurPos = true;
                shoot.expiryInterval = 180;
                shoot.checkOverrideOnExpire = true;
                return shoot;
            }
            // else a covered spot at the weapon's distance with a line of fire
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            IntVec3 cell = PositionFinder.Find(pawn, 14f,
                c =>
                {
                    if (c.path < 1.5f) return float.NaN;   // not where he stands: a goto that ends at once loops (ld1: 10 jobs in a tick)
                    float de = c.c.DistanceTo(t.Position);
                    if (de < floor || de > range * 0.9f || c.exposure > 2.5f) return float.NaN;
                    return Mathf.Abs(de - pref) * 0.4f + c.path * 0.2f + c.exposure * 3f;
                },
                c =>
                {
                    if (!GenSight.LineOfSight(c, t.Position, map, true)) return float.NaN;
                    return JobGiver_HSKTAI_Support.HasCover(c, t.Position, map) ? -4f : 0f;
                }, 15);
            if (!cell.IsValid && d > range * 0.9f)
            {
                // out of range and no firing spot near: close in, the least fire on the way
                cell = PositionFinder.Find(pawn, 16f,
                    c => c.path < 1.5f || c.c.DistanceTo(t.Position) > d - 4f || c.exposure > 3f ? float.NaN : c.exposure * 3f + c.c.DistanceTo(t.Position) * 0.3f + c.path * 0.1f,
                    null, 1);
            }
            if (!cell.IsValid || cell == pawn.Position) return null;
            FieldFight.Jobs++;
            Job move = JobMaker.MakeJob(JobDefOf.Goto, cell);
            move.locomotionUrgency = tm != null && tm.FireAt(pawn.Position) > TacticalAIMod.Settings.safeFire ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            move.expiryInterval = 180;
            move.checkOverrideOnExpire = true;
            return move;
        }

        /// <summary>To the side of the defenders (90 degrees off the line from the raid), at the preferred distance, reachable.</summary>
        private static IntVec3 FlankCell(Pawn p, AssaultPlan plan, Pawn t, float pref, Map map)
        {
            Vector3 line = (t.Position - p.Position).ToVector3();
            if (line.sqrMagnitude < 1f) return IntVec3.Invalid;
            Vector3 side = new Vector3(-line.z, 0f, line.x).normalized * ((p.thingIDNumber & 4) == 0 ? 1f : -1f);
            IntVec3 want = (t.Position.ToVector3() + side * Mathf.Clamp(pref, 10f, 25f)).ToIntVec3();
            if (!want.InBounds(map)) return IntVec3.Invalid;
            return CellFinder.StandableCellNear(want, map, 4f, c => p.CanReach(c, PathEndMode.OnCell, Danger.Deadly) && GenSight.LineOfSight(c, t.Position, map, true));
        }
    }
}
