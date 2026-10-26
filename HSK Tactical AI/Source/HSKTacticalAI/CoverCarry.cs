using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Covering fire for the wounded (playtest 2026-10-26: "when injured are carried away we need some of the attackers that
    /// have guns and are not injured to cover the retreat"). A raider carrying a downed comrade out - the medic, the planned
    /// withdrawal or No One Left Behind - in the defenders' fire gets up to two healthy raiders with guns from within 25
    /// cells: they take cover a few cells off him on the side toward the defence and fire at whoever threatens him, until he
    /// is out of fire. Part of the overwatch doctrine: a dull raid lets its carriers run alone.
    /// </summary>
    public static class CoverCarry
    {
        public static int Covers, Shots, Moves;
        private static readonly Dictionary<Pawn, Pawn> carrierOf = new Dictionary<Pawn, Pawn>();   // coverer -> carrier

        public static bool Carrying(Pawn p) => p != null && p.Spawned && !p.Downed && p.carryTracker?.CarriedThing is Pawn c && c.Downed;

        /// <summary>Unhurt enough to fight: 80 %+ health, hardly bleeding.</summary>
        public static bool Healthy(Pawn p) => p.health.summaryHealth.SummaryHealthPercent >= 0.8f && p.health.hediffSet.BleedRateTotal < 0.1f;

        /// <summary>In the defenders' fire (threat map) or in sight of a defender within 40 cells.</summary>
        public static bool InFire(Map map, IntVec3 c)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > TacticalAIMod.Settings.safeFire) return true;
            return Suppression.Defenders(map, c, 40f).Any(d => GenSight.LineOfSight(d.Position, c, map, true));
        }

        /// <summary>Could this rifleman cover a carrier: healthy, a gun of 12+ cells, no breach / gun-crew / shield duty.</summary>
        public static bool CanCover(AssaultPlan plan, Pawn p)
        {
            if (p == null || !p.Spawned || p.Downed || p.Dead || p.InMentalState || p.carryTracker?.CarriedThing != null || !Healthy(p)) return false;
            if (plan.breachTeam.Contains(p) || CrewWeapons.TeamOf(plan, p) != null || ShieldWall.IsBearer(plan, p)) return false;
            Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
            return v != null && !v.verbProps.IsMeleeAttack && v.verbProps.range >= 12f;
        }

        /// <summary>
        /// A rescue into fire is worth it when two raiders can cover it (sharp command, the fire there not extreme): then the
        /// No One Left Behind block lets it through and the cover takes over.
        /// </summary>
        public static bool CoveredRescue(Pawn rescuer, Pawn downed)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(rescuer);
            if (!s.coverCarry || plan == null || !Competence.Knows(plan, Doctrine.Overwatch)) return false;
            var tm = MapComponent_TacticalAI.For(rescuer.Map)?.Threat;
            if (tm != null && tm.CompletedCycles > 0 && tm.FireAt(downed.Position) > s.safeFire * 3f) return false;
            return plan.members.Count(m => m != rescuer && m.Map == rescuer.Map && m.Position.DistanceTo(downed.Position) <= 25f && CanCover(plan, m)) >= 2;
        }

        public static int CoverersOf(Pawn carrier) => carrierOf.Count(kv => kv.Value == carrier && kv.Key.Spawned && !kv.Key.Downed);

        /// <summary>
        /// Every 60 ticks: a carrier in fire with fewer than two coverers gets the nearest suitable riflemen at once (the
        /// job giver alone waits until they finish what they are doing - rw17 Lab: one cover in three carries).
        /// </summary>
        public static void Tick(Map map)
        {
            if (!TacticalAIMod.Settings.coverCarry) return;
            foreach (var plan in AssaultPlanner.Plans.Values)
            {
                if (plan.map != map || plan.phase == PlanPhase.Converted || !Competence.Knows(plan, Doctrine.Overwatch)) continue;
                foreach (var carrier in plan.members.Where(m => Carrying(m) && m.Map == map && InFire(map, m.Position)).ToList())
                {
                    int need = 2 - CoverersOf(carrier);
                    if (need <= 0) continue;
                    foreach (var p in plan.members.Where(m => m != carrier && m.Map == map && !carrierOf.ContainsKey(m) && m.Position.DistanceTo(carrier.Position) <= 25f && CanCover(plan, m))
                                                  .OrderBy(m => m.Position.DistanceToSquared(carrier.Position)).Take(need).ToList())
                    {
                        carrierOf[p] = carrier;
                        Covers++;
                        Job j = JobFor(p, plan, carrier);
                        if (j != null) p.jobs.StartJob(j, JobCondition.InterruptForced, null, false, true);
                    }
                }
            }
            // forget coverers whose carrier is done
            foreach (var k in carrierOf.Where(kv => !Carrying(kv.Value) || kv.Key.Destroyed || kv.Key.Downed).Select(kv => kv.Key).ToList()) carrierOf.Remove(k);
        }

        /// <summary>Into cover beside the carrier on the defence's side, then fire at whoever threatens him.</summary>
        public static Job JobFor(Pawn pawn, AssaultPlan plan, Pawn carrier)
        {
            Map map = pawn.Map;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null) return null;
            Pawn foe = Suppression.Defenders(map, carrier.Position, 45f).OrderBy(d => d.Position.DistanceToSquared(carrier.Position)).FirstOrDefault();
            IntVec3 threat = foe?.Position ?? plan.objective;
            Vector3 toward = (threat - carrier.Position).ToVector3();
            toward = toward.sqrMagnitude < 1f ? Vector3.forward : toward.normalized;
            Vector3 side = new Vector3(-toward.z, 0f, toward.x) * (pawn.thingIDNumber % 2 == 0 ? 1f : -1f);
            IntVec3 want = (carrier.Position.ToVector3() + toward * 3f + side * 4f).ToIntVec3();
            IntVec3 anchor = carrier.Position;
            IntVec3 spot = AssaultPlanner.CoverSpot(map, want, 4f, threat,
                x => x.Standable(map) && x.DistanceTo(anchor) >= 3f && x.DistanceTo(anchor) <= 8f && pawn.CanReach(x, PathEndMode.OnCell, Danger.Deadly),
                pawn.thingIDNumber);
            if (foe != null && pawn.Position.DistanceTo(foe.Position) <= v.verbProps.range && v.CanHitTarget(foe)
                && (!spot.IsValid || pawn.Position.DistanceTo(spot) <= 2f))
            {
                Shots++;
                Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, foe);
                shoot.maxNumStaticAttacks = 3;
                shoot.endIfCantShootTargetFromCurPos = true;
                shoot.expiryInterval = 180;
                shoot.checkOverrideOnExpire = true;
                return shoot;
            }
            if (spot.IsValid && pawn.Position.DistanceTo(spot) > 1.5f)
            {
                Moves++;
                Job go = JobMaker.MakeJob(JobDefOf.Goto, spot);
                go.locomotionUrgency = LocomotionUrgency.Sprint;
                go.expiryInterval = 120;
                go.checkOverrideOnExpire = true;
                return go;
            }
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 90, true);
        }

        public static Pawn CarrierFor(Pawn coverer, AssaultPlan plan)
        {
            Map map = coverer.Map;
            if (carrierOf.TryGetValue(coverer, out var c))
            {
                if (Carrying(c) && c.Map == map && c.Position.DistanceTo(coverer.Position) <= 30f && InFire(map, c.Position)) return c;
                carrierOf.Remove(coverer);
            }
            Pawn best = plan.members.Where(m => m != coverer && Carrying(m) && m.Map == map && m.Position.DistanceTo(coverer.Position) <= 25f
                                                && InFire(map, m.Position) && CoverersOf(m) < 2)
                                    .OrderBy(m => m.Position.DistanceToSquared(coverer.Position)).FirstOrDefault();
            if (best != null) { carrierOf[coverer] = best; Covers++; }
            return best;
        }
    }

    public class JobGiver_HSKTAI_CoverCarry : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!TacticalAIMod.Settings.coverCarry || pawn.Map == null) return null;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || !Competence.Knows(plan, Doctrine.Overwatch)) return null;
            if (!CoverCarry.CanCover(plan, pawn)) return null;
            Pawn carrier = CoverCarry.CarrierFor(pawn, plan);
            return carrier == null ? null : CoverCarry.JobFor(pawn, plan, carrier);
        }
    }
}
