using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// Planned withdrawal (design 3.9) instead of vanilla's panic run (nearest edge, no return fire, wounded left
    /// behind): back to the arrival edge along the covered route, in two elements by bounding overwatch - one moves
    /// while the other holds and fires (the base of fire suppresses pursuers) - carrying downed comrades out.
    /// </summary>
    public static class Withdrawal
    {
        public static int Started, DownedCarried;

        /// <summary>
        /// Withdraw when the attack is failing, not merely when it hurts: our own casualties (members downed or dead -
        /// the vanilla lord counter also counts raiders who flee in pain) at the threshold AND a poor exchange (the
        /// defenders' share down below exchangeRatio x ours); or at the hard limit regardless. A downed leader lowers
        /// the threshold.
        /// </summary>
        public static int AmmoWithdrawals;

        /// <summary>In the defenders' fire (threat map) and not covered by smoke: no place to go back for a comrade.</summary>
        public static bool InOpenFire(Map map, IntVec3 c)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0 || tm.FireAt(c) <= TacticalAIMod.Settings.safeFire) return false;
            try { if (map.gasGrid.DensityAt(c, GasType.BlindSmoke) > 0) return false; } catch { }
            return true;
        }

        public static bool ShouldWithdraw(Lord lord, AssaultPlan plan, out string why)
        {
            var s = TacticalAIMod.Settings;
            why = null;
            // a dull command doesn't plan a withdrawal: vanilla's panic run (Patch_NoPanicWhilePlanned lets it through)
            if (!Competence.PlannedWithdrawal(plan) || plan.members.Count == 0) return false;
            if (plan.intent == RaidIntent.Punitive) return false;   // revenge doesn't break off (RaidIntel)
            float lost = plan.members.Count(p => p.Dead || p.Downed || p.Destroyed) / (float)plan.members.Count;
            int defNow = Suppression.Defenders(lord.Map, plan.objective, 40f).Count;
            float defLost = plan.defendersAtStart > 0 ? 1f - defNow / (float)plan.defendersAtStart : 0f;
            bool leaderDown = plan.leader != null && (plan.leader.Dead || plan.leader.Downed);
            bool failing = defLost < lost * s.withdrawExchangeRatio;
            // competence: sharp commanders cut their losses in time, middling ones keep going longer
            float threshold = leaderDown ? Competence.WithdrawLoss(plan, s.withdrawLossFractionLeaderDown, 0.65f) : Competence.WithdrawLoss(plan, s.withdrawLossFraction, 0.75f);
            float hardLoss = Competence.WithdrawLoss(plan, s.withdrawHardLoss, 0.9f);
            // running dry (playtest: "Also when they run low on ammunition"): most gunners down to under a magazine
            if (s.ammoWithdraw)
            {
                int gunners = 0, dry = 0;
                foreach (var p in plan.members)
                {
                    if (p.Dead || p.Downed || !p.Spawned || p.equipment?.Primary == null || !p.equipment.Primary.def.IsRangedWeapon) continue;
                    if (!FireDoctrine.AmmoLeft(p, out int rounds, out int mag)) continue;
                    gunners++;
                    if (rounds < mag) dry++;
                }
                if (gunners >= 2 && dry >= gunners * s.ammoWithdrawShare)
                {
                    why = "low on ammunition (" + dry + "/" + gunners + " gunners under a magazine)";
                    AmmoWithdrawals++;
                    return true;
                }
            }
            if ((lost >= threshold && failing) || lost >= hardLoss)
            {
                why = Mathf.RoundToInt(lost * 100f) + "% lost, defenders " + Mathf.RoundToInt(defLost * 100f) + "% down" + (leaderDown ? ", leader down" : "");
                return true;
            }
            return false;
        }

        public static void Begin(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, string why)
        {
            var s = TacticalAIMod.Settings;
            plan.phase = PlanPhase.Withdraw;
            plan.withdrawTick = Find.TickManager.TicksGame;
            plan.hHourReason = "withdraw: " + why;
            int x = 0, z = 0;
            foreach (var p in alive) { x += p.Position.x; z += p.Position.z; }
            IntVec3 centre = new IntVec3(x / alive.Count, 0, z / alive.Count);
            if (!centre.Standable(map)) centre = alive[0].Position;
            plan.exitCell = ExitCellNear(map, plan.gather, centre);
            plan.withdrawRoute.Clear();
            plan.waypointIndex.Clear();
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
            {
                if (plan.exitCell.IsValid && ThreatPathing.TryFindRoute(map, centre, plan.exitCell, customizer, out var nodes, out _))
                    plan.withdrawRoute.AddRange(ThreatPathing.Waypoints(nodes, 10));
            }
            // two elements; the base of fire covers first (element 1), the rest moves first (element 0)
            plan.elementOf.Clear();
            int i = 0;
            foreach (var p in alive.OrderBy(p => RaidRoles.IsBaseOfFire(plan, p) ? 1 : 0))
                plan.elementOf[p] = RaidRoles.IsBaseOfFire(plan, p) ? 1 : (i++ % 2);
            plan.movingElement = 0;
            plan.boundTick = plan.withdrawTick;
            Grenades.OrderScreen(map, plan, alive);
            if (plan.csw.Count > 0) CrewWeapons.OnWithdraw(map, plan);   // pack the guns up or wreck them
            Started++;
            if (Prefs.DevMode) Verse.Log.Message("[HSK Tactical AI] withdrawal: " + why + " exit=" + plan.exitCell + " route=" + plan.withdrawRoute.Count);
        }

        /// <summary>Edge cell nearest where the raid came in, reachable from the group.</summary>
        private static IntVec3 ExitCellNear(Map map, IntVec3 arrival, IntVec3 from)
        {
            IntVec3 best = IntVec3.Invalid;
            float bd = float.MaxValue;
            var walk = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
            foreach (IntVec3 c in CellRect.WholeMap(map).EdgeCells)
            {
                if (!c.Standable(map)) continue;
                float d = c.DistanceToSquared(arrival);
                if (d >= bd) continue;
                if (!map.reachability.CanReach(from, c, PathEndMode.OnCell, walk)) continue;
                bd = d;
                best = c;
            }
            return best;
        }

        /// <summary>Every 60 ticks during a withdrawal: swap the moving element after boundTicks.</summary>
        public static void Tick(AssaultPlan plan, int now)
        {
            var s = TacticalAIMod.Settings;
            if (now - plan.boundTick >= s.boundTicks)
            {
                plan.movingElement = 1 - plan.movingElement;
                plan.boundTick = now;
            }
        }
    }

    /// <summary>
    /// Withdrawal duty: carry a downed comrade nearby out (one carrier each); else, if my element is moving, go back
    /// along the covered route (the last leg leaves the map); else hold and fire (base of fire: suppress pursuers).
    /// </summary>
    public class JobGiver_HSKTAI_Withdraw : ThinkNode_JobGiver
    {
        private static readonly JobGiver_AIFightEnemies fight = new JobGiver_AIFightEnemies();
        private static readonly JobGiver_HSKTAI_Support support = new JobGiver_HSKTAI_Support();

        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || !plan.exitCell.IsValid) return null;
            Job throwW = Grenades.TryThrowJob(pawn, plan);
            if (throwW != null) return throwW;
            Map map = pawn.Map;

            // shield bearers are the rearguard: between the pursuers and the group, last to leave
            if (TacticalAIMod.Settings.shieldTactics && TacticalAIMod.Settings.shieldRearguard && ShieldWall.IsBearer(plan, pawn))
            {
                Job rg = Rearguard(pawn, plan);
                if (rg != null) return rg;
            }

            // no one left behind: a downed comrade within 12 cells that nobody is carrying yet
            if (!RaidRoles.IsBaseOfFire(plan, pawn) && pawn.carryTracker?.CarriedThing == null)
            {
                Pawn downed = map.mapPawns.SpawnedPawnsInFaction(pawn.Faction)
                    .Where(d => d.Downed && !d.Dead && d.Position.DistanceTo(pawn.Position) <= 12f
                                && !Withdrawal.InOpenFire(map, d.Position)   // ff2: carriers walked back into the breach and went down
                                && !map.reservationManager.IsReservedByAnyoneOf(d, pawn.Faction)
                                && pawn.CanReserveAndReach(d, PathEndMode.OnCell, Danger.Deadly))
                    .OrderBy(d => d.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
                if (downed != null)
                {
                    Job carry = JobMaker.MakeJob(JobDefOf.Kidnap, downed, plan.exitCell);
                    carry.count = 1;
                    Withdrawal.DownedCarried++;
                    return carry;
                }
            }

            bool moving = !plan.elementOf.TryGetValue(pawn, out int el) || el == plan.movingElement;
            // close to the edge, or the route is done: leave
            if (pawn.Position.DistanceTo(plan.exitCell) <= 12f || !moving && pawn.Position.DistanceTo(plan.exitCell) <= 25f)
            {
                Job leave = JobMaker.MakeJob(JobDefOf.Goto, plan.exitCell);
                leave.exitMapOnArrival = true;
                leave.locomotionUrgency = LocomotionUrgency.Sprint;
                return leave;
            }
            if (moving)
            {
                IntVec3 next = NextWaypoint(pawn, plan);
                Job go = JobMaker.MakeJob(JobDefOf.Goto, next);
                float fire = MapComponent_TacticalAI.For(map)?.Threat?.FireAt(pawn.Position) ?? 0f;
                go.locomotionUrgency = fire > 0.3f ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                go.expiryInterval = 240;
                go.checkOverrideOnExpire = true;
                return go;
            }
            // covering element: hold and fire
            if (RaidRoles.IsBaseOfFire(plan, pawn))
            {
                Job sj = support.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                if (sj != null && sj.def != JobDefOf.Goto) return sj;
            }
            Job f = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
            if (f != null && f.def != JobDefOf.Goto) return f;
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 90, true);
        }

        public static int RearguardJobs;

        /// <summary>
        /// Rearguard (shield bearer): about 3 cells behind the rearmost raider, on the pursuers' side; crouched, shooting or
        /// throwing at whoever follows; falls back as the group moves, and leaves when nobody else is left.
        /// </summary>
        private static Job Rearguard(Pawn pawn, AssaultPlan plan)
        {
            Map map = pawn.Map;
            var others = plan.members.Where(m => m != pawn && m.Spawned && !m.Dead && !m.Downed && m.Map == map && !ShieldWall.IsBearer(plan, m)).ToList();
            if (others.Count == 0) return null;   // the normal withdrawal takes it out
            Pawn rear = others.OrderByDescending(m => m.Position.DistanceToSquared(plan.exitCell)).First();
            Pawn enemy = ShieldWall.NearestEnemy(pawn, 60f);
            IntVec3 threat = enemy?.Position ?? plan.objective;
            Vector3 dir = (threat - rear.Position).ToVector3();
            IntVec3 want = dir.sqrMagnitude < 1f ? rear.Position : rear.Position + (dir.normalized * 3f).ToIntVec3();
            if (!want.InBounds(map) || !want.Standable(map) || !pawn.CanReach(want, PathEndMode.OnCell, Danger.Deadly))
                want = CellFinder.StandableCellNear(want.InBounds(map) ? want : rear.Position, map, 3f, c => pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly));
            if (!want.IsValid) return null;
            RearguardJobs++;
            if (pawn.Position.DistanceTo(want) > 2.5f)
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, want);
                go.locomotionUrgency = LocomotionUrgency.Jog;
                go.expiryInterval = 120;
                go.checkOverrideOnExpire = true;
                return go;
            }
            Job frag = Grenades.TryShieldFrag(pawn, plan);
            if (frag != null) return frag;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (enemy != null && v != null && !v.IsMeleeAttack && enemy.Position.DistanceTo(pawn.Position) <= v.verbProps.range && v.CanHitTarget(enemy))
            {
                Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, enemy);
                shoot.maxNumStaticAttacks = 2;
                shoot.endIfCantShootTargetFromCurPos = true;
                shoot.expiryInterval = 90;
                shoot.checkOverrideOnExpire = true;
                return shoot;
            }
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 60, true);   // crouched behind the shield
        }

        private static IntVec3 NextWaypoint(Pawn pawn, AssaultPlan plan)
        {
            var wps = plan.withdrawRoute;
            if (wps.Count == 0) return plan.exitCell;
            if (!plan.waypointIndex.TryGetValue(pawn, out int i))
            {
                i = 0;
                float best = float.MaxValue;
                for (int k = 0; k < wps.Count; k++)
                {
                    float d = wps[k].DistanceToSquared(pawn.Position);
                    if (d < best) { best = d; i = k; }
                }
            }
            if (i >= wps.Count) i = wps.Count - 1;
            while (i < wps.Count - 1 && pawn.Position.DistanceTo(wps[i]) <= 4f) i++;
            plan.waypointIndex[pawn] = i;
            IntVec3 w = wps[i];
            return pawn.CanReach(w, PathEndMode.OnCell, Danger.Deadly) ? w : plan.exitCell;
        }
    }

    /// <summary>Vanilla's auto-flee (panic run) stays off while a planned withdrawal can take over.</summary>
    [HarmonyPatch(typeof(Trigger_FractionPawnsLost), nameof(Trigger_FractionPawnsLost.ActivateOn))]
    public static class Patch_NoPanicWhilePlanned
    {
        public static bool Prefix(Lord lord, ref bool __result)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.plannedWithdrawal || lord == null) return true;
            if (!AssaultPlanner.Plans.TryGetValue(lord.loadID, out var plan) || plan.phase == PlanPhase.Converted) return true;
            if (plan.phase != PlanPhase.Withdraw && !Competence.PlannedWithdrawal(plan)) return true;   // dull command: the vanilla panic run
            __result = false;
            return false;
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>
    /// No One Left Behind (CM_No_Pawn_Left_Behind) makes raiders carry downed comrades off, through fire or not (playtest
    /// 2026-10-25: "they retrieve their injured even if they are in the line of fire"). On the colony map, while a defender
    /// still stands, a raider does not go for a comrade lying in the defenders' open fire (threat map, no smoke) - the same
    /// rule as our own medics and withdrawal carriers.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_NOLBNoRescueIntoFire
    {
        public static int Blocked, Covered;

        public static bool Prepare() => AccessTools.TypeByName("CM_No_Pawn_Left_Behind.JobGiver_Rescue") != null;

        public static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("CM_No_Pawn_Left_Behind.JobGiver_Rescue"), "TryGiveJob");

        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (!TacticalAIMod.Settings.nolbRescueFilter) return;
            if (__result == null || pawn?.Map == null || !pawn.Map.IsPlayerHome || pawn.Faction == null || !pawn.Faction.HostileTo(Faction.OfPlayer)) return;
            // in the threat map's fire, or (contact reaction) in a defender's sight - the threat map lags behind a defender who
            // just stepped out
            if (!(__result.targetA.Thing is Pawn ally)) return;
            if (!Withdrawal.InOpenFire(pawn.Map, ally.Position) && !(TacticalAIMod.Settings.contactReaction && CoverCarry.InFire(pawn.Map, ally.Position))) return;
            if (Suppression.Defenders(pawn.Map, ally.Position, 60f).Count == 0) return;
            if (CoverCarry.CoveredRescue(pawn, ally)) { Covered++; return; }   // two riflemen cover him (CoverCarry)
            __result = null;
            Blocked++;
        }
    }
}
