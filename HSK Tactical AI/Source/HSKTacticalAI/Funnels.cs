using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Don't chase into a funnel (playtest, 2026-10-23: "the defenders retreat into a narrow funnel here the attackers have
    /// to be careful to follow into it"). Inside the fight, when the way to the nearest defender runs through a funnel
    /// (3+ narrow cells - 4 or fewer open neighbours - the first under fire or in the defender's view), a raider does not
    /// follow in:
    /// - grenade into it if it can reach a defender with one;
    /// - shield bearers still lead in (their followers with them) - the shield is made for this;
    /// - everyone else holds a covered cell beside the funnel mouth, outside the line of fire coming out of it, and shoots
    ///   whoever comes out (the fight node ahead of the chase does the shooting);
    /// - after `funnelHoldTicks` (per funnel mouth, group-wide) they go in - smoke first if anyone carries it.
    /// </summary>
    public static class Funnels
    {
        public static int Seen, HoldJobs, Smokes;

        public class Read
        {
            public int tick;
            public Pawn enemy;
            public IntVec3 mouth = IntVec3.Invalid, inner = IntVec3.Invalid;
            public int cells;
        }

        // performance: shared by raiders standing in the same 8x8 block heading for the same defender, kept 600 ticks
        private static readonly Dictionary<(int enemy, int gx, int gz), Read> cache = new Dictionary<(int, int, int), Read>();

        /// <summary>The funnel on the way to the enemy (path analysis, cached 120 ticks per pawn). Null if none.</summary>
        public static Read FunnelTo(Pawn p, Pawn enemy)
        {
            var s = TacticalAIMod.Settings;
            int now = Find.TickManager.TicksGame;
            var key = (enemy.thingIDNumber, p.Position.x >> 3, p.Position.z >> 3);   // 8x8 blocks: funnels do not move (pf5: 10 of 15 calls over 2 ms)
            if (cache.TryGetValue(key, out var r) && now - r.tick < 600 * Adaptive.Load) return r.cells >= s.funnelMinCells ? r : null;
            if (cache.Count > 400) cache.Clear();
            r = new Read { tick = now, enemy = enemy };
            cache[key] = r;
            Map map = p.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            using (PawnPath path = map.pathFinder.FindPathNow(p.Position, enemy, TraverseParms.For(p, Danger.Deadly), null, PathEndMode.Touch))
            {
                if (path == null || !path.Found) return null;
                var nodes = new List<IntVec3>(path.NodesReversed);
                nodes.Reverse();
                int start = -1, run = 0;
                for (int i = 1; i < nodes.Count; i++)
                {
                    IntVec3 c = nodes[i];
                    bool narrow = ThreatCustomizer.OpenNeighbours(map, c) <= 4;
                    if (start < 0)
                    {
                        if (!narrow) continue;
                        bool covered = (tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > s.safeFire) || GenSight.LineOfSight(enemy.Position, c, map, true);
                        if (!covered) continue;
                        start = i;
                        run = 1;
                    }
                    else if (narrow) run++;
                    else break;
                }
                if (start < 1 || run < s.funnelMinCells) return null;
                r.cells = run;
                r.mouth = nodes[start - 1];
                r.inner = nodes[Mathf.Min(nodes.Count - 1, start + run - 1)];
            }
            return r;
        }

        /// <summary>
        /// A holding cell beside the mouth: ≤ 5 cells from it, not itself narrow, outside the line of fire out of the funnel
        /// (its cone), out of fire now, line of sight to the mouth, cover towards it preferred, 2+ cells from other holders.
        /// </summary>
        public static IntVec3 HoldCell(AssaultPlan plan, Pawn p, Read f)
        {
            var s = TacticalAIMod.Settings;
            Map map = p.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            IntVec3 best = IntVec3.Invalid;
            float bs = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(f.mouth, 5f, true))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c == f.mouth) continue;
                if (ThreatCustomizer.OpenNeighbours(map, c) <= 4) continue;
                if (AssaultPlanner.InBreachCone(f.mouth, f.inner, c)) continue;
                if (tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > s.safeFire) continue;
                if (plan.funnelHoldOf.Any(kv => kv.Key != p && kv.Value.DistanceTo(c) < 2f)) continue;
                float score = c.DistanceTo(p.Position) * 0.3f + (JobGiver_HSKTAI_Support.HasCover(c, f.mouth, map) ? 0f : 4f);
                if (score >= bs || !GenSight.LineOfSight(c, f.mouth, map, true) || !p.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                bs = score;
                best = c;
            }
            return best;
        }
    }

    public class JobGiver_HSKTAI_FunnelCaution : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (!s.funnelCaution || plan == null || plan.phase != PlanPhase.Assault || !Competence.Knows(plan, Doctrine.FunnelCaution)) return null;
            if (ShieldWall.IsBearer(plan, pawn) || ShieldWall.BearerOf(plan, pawn) != null) return null;   // the shield goes in
            Pawn enemy = ShieldWall.NearestEnemy(pawn, 40f);
            if (enemy == null) { plan.funnelHoldOf.Remove(pawn); return null; }
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (verb != null && !verb.IsMeleeAttack && enemy.Position.DistanceTo(pawn.Position) <= verb.verbProps.range && verb.CanHitTarget(enemy))
                return null;   // a shot from here: the fight node takes it
            var f = Funnels.FunnelTo(pawn, enemy);
            if (f == null) { plan.funnelHoldOf.Remove(pawn); return null; }
            int now = Find.TickManager.TicksGame;
            if (!plan.funnelSeenTick.TryGetValue(f.mouth, out int first)) { plan.funnelSeenTick[f.mouth] = first = now; Funnels.Seen++; }

            // 1) a frag in if a defender is in reach
            Job frag = Grenades.TryShieldFrag(pawn, plan);
            if (frag != null) return frag;
            // 1b) a launcher (M79 and the like): blow the door in the funnel - the blast reaches whoever waits behind it
            Job doorShot = Launchers.FunnelDoorShot(pawn, f);
            if (doorShot != null) return doorShot;

            // 2) hold beside the mouth while the funnel is fresh
            if (now - first < s.funnelHoldTicks)
            {
                if (s.plunder && !plan.funnelHoldOf.ContainsKey(pawn)
                    && plan.funnelHoldOf.Count(kv => kv.Value.DistanceTo(f.mouth) <= 6f && kv.Key.Spawned && !kv.Key.Downed) >= s.funnelHolders)
                    return null;   // enough eyes on the mouth: the rest plunder
                if (!plan.funnelHoldOf.TryGetValue(pawn, out IntVec3 hold) || hold.DistanceTo(f.mouth) > 6f)
                {
                    hold = Funnels.HoldCell(plan, pawn, f);
                    if (!hold.IsValid) return null;
                    plan.funnelHoldOf[pawn] = hold;
                }
                if (pawn.Position.DistanceTo(hold) > 1.5f)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, hold);
                    go.locomotionUrgency = LocomotionUrgency.Jog;
                    go.expiryInterval = 180;
                    go.checkOverrideOnExpire = true;
                    Funnels.HoldJobs++;
                    return go;
                }
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 60, true);   // crouched, watching the mouth
            }

            // 3) with plunder on, the holders keep watching (the defenders must come out); else go in, smoke first
            if (s.plunder && plan.funnelHoldOf.ContainsKey(pawn)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 60, true);
            // 3) going in: smoke the funnel first (once per mouth)
            plan.funnelHoldOf.Remove(pawn);
            if (!plan.funnelSmoked.Contains(f.mouth) && Grenades.CarriedSmoke(pawn) is Thing sm)
            {
                IntVec3 aim = f.inner;
                if (aim.DistanceTo(pawn.Position) <= 12f && GenSight.LineOfSight(pawn.Position, aim, pawn.Map, true))
                {
                    plan.funnelSmoked.Add(f.mouth);
                    plan.throwOrders[pawn] = new ThrowOrder { item = sm, cell = aim, from = pawn.Position, smoke = true, issuedTick = now };
                    Funnels.Smokes++;
                    return Grenades.TryThrowJob(pawn, plan);
                }
            }
            return null;   // the normal chase takes it in
        }
    }
}
