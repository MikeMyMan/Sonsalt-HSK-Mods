using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Medic (combined arms; one per raid of 6+, highest Medicine): a downed comrade within medicReach -
    /// - lying in the defenders' fire: smoke on him first (if the medic carries smoke), then pick him up and carry him to
    ///   the nearest safe cell (out of fire, cover preferred) and put him down;
    /// - out of fire: tend him (no medicine needed).
    /// The medic does nothing else while a comrade needs him; in a withdrawal everyone carries the downed out anyway.
    /// </summary>
    public class JobGiver_HSKTAI_Medic : ThinkNode_JobGiver
    {
        public static int Carries, Tends, Smokes;
        private static JobDef carryDef;
        private static readonly HashSet<Pawn> smoked = new HashSet<Pawn>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (!s.medics || plan == null || plan.phase == PlanPhase.Withdraw) return null;
            if (!plan.roleOf.TryGetValue(pawn, out var role) || role != RaidRole.Medic) return null;
            Map map = pawn.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool InFire(IntVec3 c) => tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > s.safeFire;

            // carrying a patient: to the safe cell, then down
            if (pawn.carryTracker?.CarriedThing is Pawn carried)
            {
                IntVec3 safe = SafeCell(pawn, carried.Position.IsValid ? pawn.Position : pawn.Position, s);
                if (safe.IsValid && pawn.Position.DistanceTo(safe) > 1.5f && InFire(pawn.Position))
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, safe);
                    go.locomotionUrgency = LocomotionUrgency.Sprint;
                    go.expiryInterval = 300;
                    return go;
                }
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                return null;
            }

            Pawn patient = plan.members
                .Where(m => m != pawn && m.Spawned && m.Downed && !m.Dead && m.Map == map && m.Position.DistanceTo(pawn.Position) <= s.medicReach
                            // in open fire only with smoke to cover him (ff2: rescuers went down in the breach)
                            && (!Withdrawal.InOpenFire(map, m.Position) || Grenades.CarriedSmoke(pawn) != null)
                            && !map.physicalInteractionReservationManager.IsReserved(m) && pawn.CanReserveAndReach(m, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(m => (InFire(m.Position) ? 0 : 1000) + m.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            if (patient == null) return null;

            if (InFire(patient.Position))
            {
                // smoke on him first
                if (!smoked.Contains(patient) && Grenades.CarriedSmoke(pawn) is Thing sm && pawn.Position.DistanceTo(patient.Position) <= 12f
                    && GenSight.LineOfSight(pawn.Position, patient.Position, map, true))
                {
                    smoked.Add(patient);
                    plan.throwOrders[pawn] = new ThrowOrder { item = sm, cell = patient.Position, from = pawn.Position, smoke = true, issuedTick = Find.TickManager.TicksGame };
                    Smokes++;
                    Job t = Grenades.TryThrowJob(pawn, plan);
                    if (t != null) return t;
                }
                if (carryDef == null) carryDef = DefDatabase<JobDef>.GetNamedSilentFail("CarryDownedPawnDrafted");
                if (carryDef != null)
                {
                    Job carry = JobMaker.MakeJob(carryDef, patient);
                    carry.count = 1;
                    Carries++;
                    return carry;
                }
            }
            if (patient.health.HasHediffsNeedingTend())
            {
                Job tend = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                tend.count = 1;
                Tends++;
                return tend;
            }
            return null;
        }

        /// <summary>The nearest standable cell out of the defenders' fire within 15 cells, cover towards them preferred.</summary>
        private static IntVec3 SafeCell(Pawn pawn, IntVec3 from, TacticalAISettings s)
        {
            Map map = pawn.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            // position finder: reachable cells with the fire on the way; out of fire, cover preferred, least exposed route
            return PositionFinder.Find(pawn, 15f,
                cell => tm != null && tm.CompletedCycles > 0 && tm.FireAt(cell.c) > s.safeFire ? float.NaN
                        : cell.path - (plan != null && JobGiver_HSKTAI_Support.HasCover(cell.c, plan.objective, map) ? 3f : 0f) + cell.exposure * 3f,
                null, 1);
        }
    }

    /// <summary>
    /// Assault fire (flanking for the riflemen): inside the assault, a gunner shoots the best defender it can hit from
    /// where it stands - exposed (not covered from here) first, unpinned next, then nearest. No shot: the vanilla fight
    /// node moves it to a firing position.
    /// </summary>
    public class JobGiver_HSKTAI_AssaultFire : ThinkNode_JobGiver
    {
        public static int Shots, ExposedShots;

        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (!s.flanking || plan == null || plan.phase != PlanPhase.Assault || !Competence.Knows(plan, Doctrine.Flanking)) return null;
            if (ShieldWall.IsBearer(plan, pawn) || ShieldWall.BearerOf(plan, pawn) != null) return null;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null || v.IsMeleeAttack) return null;
            Pawn target = Targeting.Best(pawn, plan, v);   // exposed, wounded, dangerous, focus fire
            if (target == null) return null;
            bool exposedTarget = !Flanking.Covered(target, pawn.Position);
            Job j = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            j.maxNumStaticAttacks = 2;
            j.endIfCantShootTargetFromCurPos = true;
            j.expiryInterval = 120;
            j.checkOverrideOnExpire = true;
            Shots++;
            if (exposedTarget) ExposedShots++;
            return j;
        }
    }
}
