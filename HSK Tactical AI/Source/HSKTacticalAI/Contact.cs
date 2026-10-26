using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Contact reaction (playtest 2026-10-26: "the attackers have a plan and a defender moves or gets in sight and they keep
    /// moving, like picking up an injured pawn while the defender then starts shooting. When defenders move in sight they
    /// need to react accordingly"). Every second: a raider with a gun who gets a defender newly in sight within his range,
    /// while walking, waiting or going for a downed comrade, breaks it off - after a reaction time from the command's
    /// competence (sharp at once, dull up to 2 s) - takes cover close by and returns fire. A rescue into a defender's sight
    /// goes ahead only when it is covered (CoverCarry). Carriers keep carrying (the cover deals with the threat); breachers
    /// at their charges, gun crews, shield bearers and raiders already fighting are left alone.
    /// </summary>
    public static class Contact
    {
        public static int Contacts, Reactions, RescuesAborted;
        private class Seen { public Pawn foe; public int since, reactAt = -1; public bool reacted; }
        private static readonly Dictionary<Pawn, Seen> seen = new Dictionary<Pawn, Seen>();

        private static readonly HashSet<string> interruptible = new HashSet<string>
            { "Goto", "GotoWander", "Wait", "Wait_Wander", "Wait_MaintainPosture", "Ignite", "TakeInventory", "HaulToCell", "Steal",
              "Kidnap", "Rescue", "CarryDownedPawn", "CarryDownedPawnDrafted", "Strip", "GotoSafeTemperature" };

        public static bool Ranged(Pawn p, out Verb v)
        {
            v = p.equipment?.PrimaryEq?.PrimaryVerb;
            return v != null && !v.verbProps.IsMeleeAttack && v.verbProps.range >= 8f;
        }

        /// <summary>The defender this raider is reacting to, if the contact is fresh and he still sees him.</summary>
        public static Pawn FoeOf(Pawn p)
        {
            if (!seen.TryGetValue(p, out var s) || !s.reacted || s.foe == null || !s.foe.Spawned || s.foe.Downed) return null;
            if (Find.TickManager.TicksGame - s.since > 600) return null;
            return GenSight.LineOfSight(p.Position, s.foe.Position, p.Map, true) ? s.foe : null;
        }

        private static bool Protected(AssaultPlan plan, Pawn p) =>
            plan.breachTeam.Contains(p) || CrewWeapons.TeamOf(plan, p) != null || ShieldWall.IsBearer(plan, p)
            || p.carryTracker?.CarriedThing != null || p.InMentalState;

        public static void Tick(Map map)
        {
            var st = TacticalAIMod.Settings;
            if (!st.contactReaction) return;
            int now = Find.TickManager.TicksGame;
            foreach (var plan in AssaultPlanner.Plans.Values)
            {
                if (plan.map != map || (plan.phase != PlanPhase.Approach && plan.phase != PlanPhase.Assault)) continue;
                var defenders = Suppression.Defenders(map, plan.objective, 120f);
                if (defenders.Count == 0) continue;
                int delay = Mathf.RoundToInt((1f - Mathf.Clamp01(plan.competence)) * 120f);
                foreach (var r in plan.members)
                {
                    if (r == null || !r.Spawned || r.Downed || r.Dead || r.Map != map || !Ranged(r, out Verb v)) { seen.Remove(r); continue; }
                    float range = v.verbProps.range;
                    Pawn foe = null;
                    float bd = float.MaxValue;
                    foreach (var d in defenders)
                    {
                        float dd = d.Position.DistanceToSquared(r.Position);
                        if (dd > range * range || dd >= bd) continue;
                        if (!GenSight.LineOfSight(r.Position, d.Position, map, true)) continue;
                        bd = dd;
                        foe = d;
                    }
                    if (foe == null) { seen.Remove(r); continue; }
                    if (!seen.TryGetValue(r, out var s)) { s = new Seen { foe = foe, since = now, reactAt = now + delay }; seen[r] = s; Contacts++; }
                    else if (s.foe != foe && !s.reacted) s.foe = foe;
                    // still in sight 10 s later and walking again (approach only - the assault fires on the move): react again
                    if (s.reacted && plan.phase == PlanPhase.Approach && now - s.since > 600) { s.reacted = false; s.foe = foe; s.reactAt = now; }
                    if (s.reacted || now < s.reactAt || Protected(plan, r)) continue;
                    // break off what he is doing if it isn't fighting
                    string job = r.CurJobDef?.defName;
                    if (job == null || !interruptible.Contains(job)) { s.reacted = true; continue; }
                    // going for a downed comrade into the defender's sight: only when it is covered
                    if ((job == "Kidnap" || job == "Rescue" || job.StartsWith("CarryDowned")) && r.CurJob.targetA.Thing is Pawn downed
                        && CoverCarry.CoveredRescue(r, downed)) { s.reacted = true; continue; }
                    if (job == "Kidnap" || job == "Rescue" || job.StartsWith("CarryDowned")) RescuesAborted++;
                    s.reacted = true;
                    s.since = now;
                    Reactions++;
                    r.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
            foreach (var k in seen.Keys.Where(k => k == null || k.Destroyed || k.Dead).ToList()) seen.Remove(k);
        }
    }

    /// <summary>After a contact break-off: into cover close by, then fire at the defender.</summary>
    public class JobGiver_HSKTAI_Contact : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!TacticalAIMod.Settings.contactReaction || pawn.Map == null) return null;
            Pawn foe = Contact.FoeOf(pawn);
            if (foe == null || !Contact.Ranged(pawn, out Verb v)) return null;
            Map map = pawn.Map;
            float range = v.verbProps.range;
            if (!JobGiver_HSKTAI_Support.HasCover(pawn.Position, foe.Position, map))
            {
                IntVec3 here = pawn.Position;
                IntVec3 cell = AssaultPlanner.CoverSpot(map, here, 6f, foe.Position,
                    x => x != here && x.Standable(map) && x.DistanceTo(foe.Position) <= range && pawn.CanReach(x, PathEndMode.OnCell, Danger.Deadly)
                         && JobGiver_HSKTAI_Support.HasCover(x, foe.Position, map), pawn.thingIDNumber);
                if (cell.IsValid)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, cell);
                    go.locomotionUrgency = LocomotionUrgency.Sprint;
                    go.expiryInterval = 90;
                    go.checkOverrideOnExpire = true;
                    return go;
                }
            }
            if (!v.CanHitTarget(foe)) return null;
            Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, foe);
            shoot.maxNumStaticAttacks = 3;
            shoot.endIfCantShootTargetFromCurPos = true;
            shoot.expiryInterval = 180;
            shoot.checkOverrideOnExpire = true;
            return shoot;
        }
    }
}
