using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>What a raid came for. Assault (the planner's walk-in / breach / siege) needs a way in; the others don't.</summary>
    public enum RaidGoal { Assault, Scout, Sabotage }

    /// <summary>
    /// Raid goals (playtest, 2026-10-25: "there are other attacks where it is about sieging or scouting or sabotage etc
    /// where breaching is not needed. For sabotage they just lay fire on the map or kill some defenceless animals they can
    /// find and then leave again or see if a defender is outside alone").
    ///
    /// - Assault: as before; a raid that must breach brings the means (BreachKit.EnsureMeans).
    /// - Scout: a small party watches the base from cover out of your fire, avoids a fight and leaves. Survivors bring
    ///   home more intel than a beaten raid (raid memory).
    /// - Sabotage: set fires, kill livestock out of your fire, jump a colonist who is alone outside, then leave before
    ///   your defence turns out.
    /// Rolled per raid: weak raids (well under the odds for an assault) harass instead of attacking, factions that know
    /// little of you scout first, strong raids attack. Humanlike raids only.
    /// </summary>
    public static class RaidGoals
    {
        public static int Scouts, Sabotages, Ambushes, GoalJobs, Leaves;
        public static string LastLeaveWhy = "";

        public static RaidGoal Roll(Lord lord, AssaultPlan plan, List<Pawn> pawns, out string why)
        {
            var s = TacticalAIMod.Settings;
            why = "assault";
            if (!string.IsNullOrEmpty(s.raidGoalOverride))   // AI Lab only
            {
                why = "override";
                switch (s.raidGoalOverride.ToLowerInvariant())
                {
                    case "scout": return RaidGoal.Scout;
                    case "sabotage": return RaidGoal.Sabotage;
                    default: return RaidGoal.Assault;
                }
            }
            if (!s.raidGoals || pawns.Count == 0) return RaidGoal.Assault;
            int people = pawns.Count(p => p.RaceProps.Humanlike);
            if (people < pawns.Count * 0.5f) { why = "not people"; return RaidGoal.Assault; }   // mechs, insects, beasts just attack
            float ratio = AssaultPlanner.ForceRatio(lord.Map, pawns, 1, out float required);
            float wScout = s.scoutShare, wSab = s.sabotageShare;
            var notes = new List<string>();
            if (ratio < 1f) { wSab *= 3f; wScout *= 2f; notes.Add("too weak to take it (" + ratio.ToString("0.0") + ":1)"); }
            else if (ratio >= required) { wSab *= 0.3f; wScout *= 0.3f; notes.Add("strong enough (" + ratio.ToString("0.0") + ":1)"); }
            var mem = FactionMemory.Read(lord.faction);
            if (mem == null || mem.intel < 0.3f) { wScout *= 2f; notes.Add("knows little of you"); }
            if (pawns.Count > 12) { wScout *= 0.3f; notes.Add("too many to scout"); }
            float total = 1f + wScout + wSab;   // assault weight 1
            float r = Competence.Hash01(plan.lordId, 0x60A1) * total;
            string tail = notes.Count > 0 ? " (" + string.Join(", ", notes) + ")" : "";
            if (r < wScout) { why = "scout" + tail; return RaidGoal.Scout; }
            if (r < wScout + wSab) { why = "sabotage" + tail; return RaidGoal.Sabotage; }
            why = "assault" + tail;
            return RaidGoal.Assault;
        }

        // ---------------- planning ----------------

        public static void Plan(Lord lord, AssaultPlan plan, List<Pawn> pawns, Map map)
        {
            var s = TacticalAIMod.Settings;
            int now = Find.TickManager.TicksGame;
            if (plan.goal == RaidGoal.Scout)
            {
                plan.mode = PlanMode.Scout;
                Scouts++;
                ChoosePosts(map, plan, pawns);
                plan.goalEndTick = now + s.scoutObserveTicks * 3;   // never sees enough: leave anyway
                plan.capability = "scout: " + plan.scoutPosts.Count + " posts (" + plan.scoutPostsWithSight + " with sight)";
            }
            else
            {
                plan.mode = PlanMode.Sabotage;
                Sabotages++;
                Arson.Plan(map, plan, pawns, Mathf.Max(1, pawns.Count / 3), true);   // a fire per three raiders, any raid size
                plan.goalEndTick = now + s.sabotageTicks;
                plan.lastGoalJobTick = now;
                plan.capability = "sabotage: " + plan.arsonSites.Count + " fires planned";
            }
            foreach (var p in pawns) plan.members.Add(p);
        }

        /// <summary>Observation posts 32-48 cells from the base: out of fire, reachable, in sight of the base, in cover.</summary>
        private static void ChoosePosts(Map map, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            var defs = Suppression.Defenders(map, plan.objective, 60f);
            // the base itself: its buildings (walls seen from outside tell as much as the people behind them)
            var buildings = map.listerBuildings.allBuildingsColonist.Where(b => b.Spawned && b.Position.DistanceTo(plan.objective) <= 40f)
                               .OrderBy(b => b.thingIDNumber).Take(40).ToList();
            var cands = new List<(IntVec3 c, float score, bool sees)>();
            foreach (int r in new[] { 24, 32, 40, 48, 56 })
                for (int a = 0; a < 360; a += 20)
                {
                    float rad = a * Mathf.Deg2Rad;
                    var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                    if (!c.InBounds(map)) continue;
                    c = CellFinder.StandableCellNear(c, map, 3f);
                    if (!c.IsValid || (haveThreat && tm.FireAt(c) > s.safeFire)) continue;
                    if (!map.reachability.CanReach(plan.gather, c, PathEndMode.OnCell, walk)) continue;
                    bool sees = GenSight.LineOfSight(c, plan.objective, map, true) || defs.Any(d => d.Position.DistanceTo(c) <= 55f && GenSight.LineOfSight(c, d.Position, map, true))
                                || buildings.Any(b => b.Position.DistanceTo(c) <= 55f && GenSight.LineOfSight(c, b.Position, map, true));
                    float score = c.DistanceTo(plan.gather) * 0.3f + (sees ? 0f : 40f) + (JobGiver_HSKTAI_Support.HasCover(c, plan.objective, map) ? 0f : 8f);
                    cands.Add((c, score, sees));
                }
            int want = Mathf.Clamp(Mathf.CeilToInt(pawns.Count / 2f), 1, 3);
            foreach (var cand in cands.OrderBy(x => x.score))
            {
                if (plan.scoutPosts.Count >= want) break;
                if (plan.scoutPosts.Any(o => o.DistanceTo(cand.c) < 12f)) continue;
                plan.scoutPosts.Add(cand.c);
                if (cand.sees) { plan.scoutPostsWithSight++; plan.seeingPosts.Add(cand.c); }
            }
            if (plan.scoutPosts.Count == 0) plan.scoutPosts.Add(plan.gather);
            int i = 0;
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
                foreach (var p in pawns)
                {
                    IntVec3 post = plan.scoutPosts[i++ % plan.scoutPosts.Count];
                    plan.spotOf[p] = CellFinder.StandableCellNear(post, map, 2f, x => !plan.spotOf.Values.Contains(x)) is IntVec3 c && c.IsValid ? c : post;
                    if (ThreatPathing.TryFindRoute(map, p.Position, plan.spotOf[p], customizer, out var nodes, out _))
                        plan.goalRouteOf[p] = ThreatPathing.Waypoints(nodes, 8);
                }
        }

        // ---------------- duties ----------------

        public static void Duty(AssaultPlan plan, Pawn p)
        {
            if (Arson.IsActiveArsonist(plan, p)) { p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Arson, plan.arsonistOf[p].center); return; }
            if (plan.mode == PlanMode.Scout) p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Scout, plan.spotOf.TryGetValue(p, out var c) ? c : plan.gather);
            else p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Sabotage, plan.gather);
        }

        // ---------------- every 60 ticks ----------------

        public static void Tick(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, int now)
        {
            var s = TacticalAIMod.Settings;
            if (plan.phase != PlanPhase.Approach) return;
            bool harmed = alive.Any(p => now - p.mindState.lastHarmTick < 120);
            if (plan.mode == PlanMode.Scout)
            {
                // watching from a post that sees the base counts in full; from a blind one, a third (the outskirts, the approaches)
                bool seeing = alive.Any(p => plan.spotOf.TryGetValue(p, out var c) && p.Position.DistanceTo(c) <= 3f && plan.seeingPosts.Any(sp => sp.DistanceTo(c) <= 3f));
                bool atPost = alive.Any(p => plan.spotOf.TryGetValue(p, out var c) && p.Position.DistanceTo(c) <= 3f);
                if (seeing) plan.observedTicks += 60;
                else if (atPost && now % 180 < 60) plan.observedTicks += 60;
                if (alive.Count < plan.startCount) { Leave(map, lord, plan, alive, "lost a man"); return; }
                if (harmed) { Leave(map, lord, plan, alive, "seen - breaking contact"); return; }
                if (plan.observedTicks >= s.scoutObserveTicks) { Leave(map, lord, plan, alive, "seen enough (" + plan.observedTicks + " ticks)"); return; }
                if (now > plan.goalEndTick) Leave(map, lord, plan, alive, "out of time");
                return;
            }
            // sabotage
            plan.ambushTarget = LoneDefender(map, plan, alive);
            int cx = 0, cz = 0;
            foreach (var p in alive) { cx += p.Position.x; cz += p.Position.z; }
            var centre = new IntVec3(cx / alive.Count, 0, cz / alive.Count);
            int near = Suppression.Defenders(map, centre, 25f).Count;
            if (alive.Count < plan.startCount * 0.75f) { Leave(map, lord, plan, alive, "losses"); return; }
            if (plan.ambushTarget == null && near >= Mathf.Max(2, alive.Count / 2)) { Leave(map, lord, plan, alive, "your defence turned out (" + near + ")"); return; }
            if (now > plan.goalEndTick) { Leave(map, lord, plan, alive, "out of time"); return; }
            bool arsonBusy = plan.arsonSites.Any(a => !a.done);
            if (!arsonBusy && plan.ambushTarget == null && now - plan.lastGoalJobTick > s.sabotageIdleTicks) Leave(map, lord, plan, alive, "nothing left to wreck");
        }

        /// <summary>A colonist alone outside: no other colonist within 15 cells, not under a roof, at most 50 cells from the raid.</summary>
        private static Pawn LoneDefender(Map map, AssaultPlan plan, List<Pawn> alive)
        {
            var defs = Suppression.Defenders(map, plan.objective, 999f);
            Pawn best = null;
            float bd = float.MaxValue;
            foreach (var d in defs)
            {
                if (d.Position.Roofed(map)) continue;
                if (defs.Any(o => o != d && o.Position.DistanceTo(d.Position) <= 15f)) continue;
                float dist = alive.Min(p => p.Position.DistanceTo(d.Position));
                if (dist > 50f || dist >= bd) continue;
                int fighters = alive.Count(p => p.Position.DistanceTo(d.Position) <= 50f);
                if (fighters < 2) continue;
                bd = dist;
                best = d;
            }
            if (best != null && best != plan.ambushTarget) Ambushes++;
            return best;
        }

        private static void Leave(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, string why)
        {
            plan.goalLeaveWhy = why;
            LastLeaveWhy = plan.mode + ": " + why;
            Leaves++;
            if (Competence.PlannedWithdrawal(plan))
            {
                Withdrawal.Begin(map, lord, plan, alive, plan.mode.ToString().ToLowerInvariant() + " done: " + why);
                lord.CurLordToil?.UpdateAllDuties();
            }
            else AssaultPlanner.ExitMap(lord, plan);
            if (Prefs.DevMode) Log.Message("[HSK Tactical AI] " + plan.mode + " raid leaves: " + why);
        }
    }

    /// <summary>Scout: covered route to the post, watch without firing, fight back only when hit.</summary>
    public class JobGiver_HSKTAI_Scout : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || plan.mode != PlanMode.Scout || plan.phase != PlanPhase.Approach) return null;
            if (Find.TickManager.TicksGame - pawn.mindState.lastHarmTick < 300) return null;   // shot at: the fight node
            if (!plan.spotOf.TryGetValue(pawn, out IntVec3 post)) return null;
            if (pawn.Position.DistanceTo(post) > 2.5f)
            {
                IntVec3 next = post;
                if (plan.goalRouteOf.TryGetValue(pawn, out var route))
                {
                    while (route.Count > 0 && pawn.Position.DistanceTo(route[0]) <= 4f) route.RemoveAt(0);
                    if (route.Count > 0) next = route[0];
                }
                if (!pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly)) next = post;
                Job go = JobMaker.MakeJob(JobDefOf.Goto, next);
                go.locomotionUrgency = LocomotionUrgency.Jog;
                go.expiryInterval = 400;
                go.checkOverrideOnExpire = true;
                RaidGoals.GoalJobs++;
                return go;
            }
            Job wait = JobMaker.MakeJob(JobDefOf.Wait, 240);   // not Wait_Combat: a scout doesn't give himself away
            wait.checkOverrideOnExpire = true;
            return wait;
        }
    }

    /// <summary>Sabotage: jump a lone colonist, kill livestock out of your fire, set things alight.</summary>
    public class JobGiver_HSKTAI_Sabotage : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || plan.mode != PlanMode.Sabotage || plan.phase != PlanPhase.Approach) return null;
            Job j = null;
            Pawn t = plan.ambushTarget;
            if (t != null && t.Spawned && !t.Downed && !t.Dead && pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                if (v == null || v.IsMeleeAttack) j = JobMaker.MakeJob(JobDefOf.AttackMelee, t);
                else if (t.Position.DistanceTo(pawn.Position) <= v.verbProps.range * 0.8f && v.CanHitTarget(t))
                {
                    j = JobMaker.MakeJob(JobDefOf.AttackStatic, t);
                    j.endIfCantShootTargetFromCurPos = true;
                }
                else
                {
                    j = JobMaker.MakeJob(JobDefOf.Goto, t.Position);
                    j.locomotionUrgency = LocomotionUrgency.Jog;
                }
                j.expiryInterval = 180;
                j.checkOverrideOnExpire = true;
            }
            if (j == null) j = Plunder.KillJob(pawn, plan.objective, 100f);
            if (j == null && s.arson) j = Plunder.BurnJob(pawn, plan.objective, 100f);
            if (j == null) return null;
            plan.lastGoalJobTick = Find.TickManager.TicksGame;
            RaidGoals.GoalJobs++;
            return j;
        }
    }
}
