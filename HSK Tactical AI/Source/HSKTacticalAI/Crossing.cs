using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    public enum CrossingMode { None, Covered, Mass, Smoke, Attrition, Flank, Bound }

    /// <summary>
    /// Don't walk into the fire (playtest, 2026-10-23: "feels a bit strange that the attackers would advance over open ground
    /// into the fire even with a shield they would stay out of range and let snipers take shots or advance in cover if
    /// there is no good way to approach use smoke or overwhelming force but like this it feels suicidal").
    ///
    /// Walk-mode H-hour: each axis' assault route is checked for open ground - cells in the defenders' fire without cover
    /// (or smoke). Below openGroundCells: go (covered approach). Else:
    /// - overwhelming force (raw odds, attackers vs defenders, >= overwhelmOdds 3:1): everyone at once, shields first;
    /// - smoke carriers: smoke screens on the open stretch of each route, the rush goes once they are up;
    /// - otherwise attrition: the raid stays out of the defenders' fire while the base of fire (snipers, MGs) works from
    ///   stand-off positions; a defender down or the odds turning overwhelming -> go; nothing after attritionTicks ->
    ///   withdraw (no safe approach).
    /// </summary>
    public static class Crossing
    {
        public static int Checks, Covered, Mass, Smoke, Attrition, AttritionGo, NoApproach, Reangled, Flanks, FlankGo, BreachFallback, Bounds, IdleAttrition;

        /// <summary>
        /// Flank group (playtest: "making the out-of-range phase useful (flank group ...)"): the best dead-ground position on
        /// another side whose approach is covered (fewer than openGroundCells open cells), favouring sides the defenders'
        /// cover does not face and at least 40 degrees from the current axes. The maneuver element (everyone not pinning
        /// the defenders) goes there on a route out of fire; the base of fire keeps pinning. Needs 2+ maneuverers.
        /// </summary>
        public static bool TryFlankManeuver(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0) return false;
            var maneuver = alive.Where(p => !RaidRoles.IsBaseOfFire(plan, p) && !Arson.IsActiveArsonist(plan, p)).ToList();
            if (maneuver.Count < 2) return false;
            int cx = 0, cz = 0;
            foreach (var p in maneuver) { cx += p.Position.x; cz += p.Position.z; }
            IntVec3 centre = new IntVec3(cx / maneuver.Count, 0, cz / maneuver.Count);
            var defs = Suppression.Defenders(map, plan.objective, 40f);
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            var flankCands = new List<(IntVec3 pos, float est)>();
            for (int a = 0; a < 360; a += 20)
            {
                if (plan.axes.Any(ax => AngleDiff(AssaultPlanner.Bearing(plan.objective, ax), a) < 40f)) continue;
                float rad = a * Mathf.Deg2Rad;
                IntVec3 pos = IntVec3.Invalid;
                for (int r = 10; r <= 80; r += 3)
                {
                    var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                    if (!c.InBounds(map)) break;
                    c = CellFinder.StandableCellNear(c, map, 2f);
                    if (!c.IsValid || c.GetTerrain(map).IsWater) continue;
                    if (tm.FireAt(c) <= s.safeFire && r >= 12) { pos = c; break; }
                }
                if (!pos.IsValid || !map.reachability.CanReach(centre, pos, PathEndMode.OnCell, walk)) continue;
                flankCands.Add((pos, LineOpen(map, pos, plan.objective) * 10f + pos.DistanceTo(centre) * 0.5f - (s.flanking ? 60f * Flanking.ExposedShare(pos, defs) : 0f)));
            }
            // performance: the full (threat-priced) route only for the 4 best by the straight-line estimate
            foreach (var cand in flankCands.OrderBy(fc => fc.est).Take(4))
            {
                int open = OpenStretch(map, cand.pos, plan.objective).Count;
                if (open >= s.openGroundCells) continue;
                float score = open * 10f + cand.pos.DistanceTo(centre) * 0.5f - (s.flanking ? 60f * Flanking.ExposedShare(cand.pos, defs) : 0f);
                if (score < bestScore) { bestScore = score; best = cand.pos; }
            }
            if (!best.IsValid) return false;
            int idx = plan.axes.Count;
            plan.axes.Add(best);
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
                plan.waypoints.Add(ThreatPathing.TryFindRoute(map, centre, best, customizer, out var nodes, out _) ? ThreatPathing.Waypoints(nodes, s.waypointSpacing) : new List<IntVec3>());
            foreach (var p in maneuver) { plan.waypointIndex.Remove(p); AssaultPlanner.AssignOne(plan, p, idx); }
            plan.flankAxis = idx;
            plan.flankTick = Find.TickManager.TicksGame;
            plan.capability += "; flank group (" + maneuver.Count + ") to " + Mathf.RoundToInt(AssaultPlanner.Bearing(plan.objective, best)) + "deg";
            lord.CurLordToil?.UpdateAllDuties();
            return true;
        }

        private static float AngleDiff(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>
        /// Another angle: every 20 degrees around the defenders, the first cell in dead ground outward (an assault position),
        /// reachable, whose approach has fewer than openGroundCells open cells. Found: the whole raid moves there (one axis)
        /// and the crossing check starts over. Once per raid.
        /// </summary>
        public static bool TryOtherAngle(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive)
        {
            var s = TacticalAIMod.Settings;
            plan.reangled = true;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0 || alive.Count == 0) return false;
            int cx = 0, cz = 0;
            foreach (var p in alive) { cx += p.Position.x; cz += p.Position.z; }
            IntVec3 centre = new IntVec3(cx / alive.Count, 0, cz / alive.Count);
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            IntVec3 best = IntVec3.Invalid;
            int bestOpen = int.MaxValue;
            float bestDist = float.MaxValue;
            var angleCands = new List<(IntVec3 pos, float est)>();
            for (int a = 0; a < 360; a += 20)
            {
                float rad = a * Mathf.Deg2Rad;
                IntVec3 pos = IntVec3.Invalid;
                for (int r = 10; r <= 80; r += 3)
                {
                    var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                    if (!c.InBounds(map)) break;
                    c = CellFinder.StandableCellNear(c, map, 2f);
                    if (!c.IsValid || c.GetTerrain(map).IsWater) continue;
                    if (tm.FireAt(c) <= s.safeFire && r >= 12) { pos = c; break; }
                }
                if (!pos.IsValid || !map.reachability.CanReach(centre, pos, PathEndMode.OnCell, walk)) continue;
                angleCands.Add((pos, LineOpen(map, pos, plan.objective) * 10f + pos.DistanceTo(centre) * 0.1f));
            }
            foreach (var cand in angleCands.OrderBy(ac => ac.est).Take(4))
            {
                int open = OpenStretch(map, cand.pos, plan.objective).Count;
                if (open >= s.openGroundCells) continue;
                float d = cand.pos.DistanceTo(centre);
                if (open < bestOpen || (open == bestOpen && d < bestDist)) { bestOpen = open; bestDist = d; best = cand.pos; }
            }
            if (!best.IsValid) return false;
            plan.axes.Clear(); plan.axisOf.Clear(); plan.spotOf.Clear(); plan.waypoints.Clear(); plan.waypointIndex.Clear();
            plan.axes.Add(best);
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
                plan.waypoints.Add(ThreatPathing.TryFindRoute(map, centre, best, customizer, out var nodes, out _) ? ThreatPathing.Waypoints(nodes, s.waypointSpacing) : new List<IntVec3>());
            AssaultPlanner.Assign(plan, alive);
            plan.crossing = CrossingMode.None;
            plan.capability += "; other angle " + Mathf.RoundToInt(AssaultPlanner.Bearing(plan.objective, best)) + "deg (" + bestOpen + " open cells)";
            plan.hHourReason = "moving to another angle";
            lord.CurLordToil?.UpdateAllDuties();
            return true;
        }

        /// <summary>Raw odds for crossing open ground: attackers vs defenders (force scores), like the assault check.</summary>
        public static float RawOdds(Map map, IEnumerable<Pawn> alive)
        {
            float d = ForceEstimate.Defenders(map);
            return d <= 0.01f ? 99f : ForceEstimate.Attackers(alive) / d;
        }

        /// <summary>Route cells (assault-route pricing) in the defenders' fire without cover or smoke, in walking order.</summary>
        public static List<IntVec3> OpenStretch(Map map, IntVec3 from, IntVec3 to)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            var open = new List<IntVec3>();
            if (tm == null || tm.CompletedCycles == 0) return open;
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire * 0.5f, s.routeBaseCostInFire, s.funnelCellPenalty, s.safeFire))
            {
                if (!ThreatPathing.TryFindRoute(map, from, to, customizer, out var nodes, out _)) return open;
                foreach (var c in nodes)
                {
                    if (tm.FireAt(c) <= s.safeFire) continue;
                    if (Smoked(map, c)) continue;
                    bool cover = false;
                    foreach (var d in GenAdj.AdjacentCells)
                    {
                        var a = c + d;
                        if (!a.InBounds(map)) continue;
                        Building b = a.GetEdifice(map);
                        if ((b != null && b.def.fillPercent >= 0.4f) || a.GetPlant(map)?.def.plant?.IsTree == true) { cover = true; break; }
                    }
                    if (!cover) open.Add(c);
                }
            }
            return open;
        }

        /// <summary>Cheap estimate for ranking angles: cells in fire without cover on the straight line (no path search).</summary>
        public static int LineOpen(Map map, IntVec3 from, IntVec3 to)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0) return 0;
            int n = 0;
            foreach (IntVec3 c in GenSight.PointsOnLineOfSight(from, to))
            {
                if (!c.InBounds(map) || tm.FireAt(c) <= s.safeFire) continue;
                bool cover = false;
                foreach (var d in GenAdj.AdjacentCells)
                {
                    var a = c + d;
                    if (!a.InBounds(map)) continue;
                    Building b = a.GetEdifice(map);
                    if ((b != null && b.def.fillPercent >= 0.4f) || a.GetPlant(map)?.def.plant?.IsTree == true) { cover = true; break; }
                }
                if (!cover) n++;
            }
            return n;
        }

        private static bool Smoked(Map map, IntVec3 c)
        {
            try { return map.gasGrid.DensityAt(c, GasType.BlindSmoke) > 0; } catch { return false; }
        }

        /// <summary>
        /// Walk-mode H-hour check. True: hold this tick (smoke going up, attrition, or withdrawing). False: go; reason says how.
        /// </summary>
        public static bool Hold(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, int now, ref string reason)
        {
            var s = TacticalAIMod.Settings;
            if (plan.crossing == CrossingMode.None)
            {
                Checks++;
                var stretches = plan.axes.Select(a => OpenStretch(map, a, plan.objective)).ToList();
                plan.openCells = stretches.Count == 0 ? 0 : stretches.Max(o => o.Count);
                if (plan.openCells < s.openGroundCells)
                {
                    plan.crossing = CrossingMode.Covered;
                    Covered++;
                    return false;
                }
                float ratio = RawOdds(map, alive);
                if (ratio >= s.overwhelmOdds)
                {
                    plan.crossing = CrossingMode.Mass;
                    Mass++;
                    reason += ", massed rush over " + plan.openCells + " open cells (odds " + ratio.ToString("0.0") + ":1)";
                    return false;
                }
                if (s.grenadeKit && alive.Any(p => Grenades.CarriedSmoke(p) != null) && Grenades.OrderSmokeScreen(map, plan, alive, stretches) > 0)
                {
                    plan.crossing = CrossingMode.Smoke;
                    plan.crossingTick = now;
                    Smoke++;
                    plan.hHourReason = "smoke screen over " + plan.openCells + " open cells";
                    return true;
                }
                // a covered approach on another side: the maneuver element flanks while the base of fire pins
                if (s.flankManeuver && TryFlankManeuver(map, lord, plan, alive))
                {
                    plan.crossing = CrossingMode.Flank;
                    plan.crossingTick = now;
                    Flanks++;
                    plan.hHourReason = "flank group moving (" + plan.openCells + " open cells in front)";
                    if (plan.suppressStartTick < 0) plan.suppressStartTick = now;   // the base of fire pins meanwhile
                    return true;
                }
                plan.crossing = CrossingMode.Attrition;
                plan.crossingTick = now;
                plan.defendersAtCrossing = Suppression.Defenders(map, plan.objective, 40f).Count;
                Attrition++;
                plan.hHourReason = "attrition: " + plan.openCells + " open cells, odds " + ratio.ToString("0.0") + ":1 < " + s.overwhelmOdds.ToString("0.0") + ":1";
                if (plan.suppressStartTick < 0) plan.suppressStartTick = now;   // the base of fire goes to work
                lord.CurLordToil?.UpdateAllDuties();
                return true;
            }
            if (plan.crossing == CrossingMode.Smoke)
            {
                if (now - plan.crossingTick < s.smokeSettleTicks) return true;
                reason += ", behind a smoke screen";
                return false;
            }
            if (plan.crossing == CrossingMode.Flank)
            {
                // go once the flank group is in position (75 %) or after flankTicks; the base of fire joins by the flank route
                var group = alive.Where(p => plan.axisOf.TryGetValue(p, out int ax) && ax == plan.flankAxis).ToList();
                int arrived = group.Count(p => plan.spotOf.TryGetValue(p, out var sp) && p.Position.DistanceTo(sp) <= 5f);
                if (group.Count == 0 || arrived >= group.Count * 0.75f || now - plan.crossingTick > s.flankTicks)
                {
                    foreach (var p in alive) plan.axisOf[p] = plan.flankAxis;   // everyone goes in from the flank
                    plan.crossing = CrossingMode.Covered;
                    FlankGo++;
                    reason += ", flank group in position (" + arrived + "/" + group.Count + ")";
                    return false;
                }
                return true;
            }
            if (plan.crossing == CrossingMode.Attrition)
            {
                int defs = Suppression.Defenders(map, plan.objective, 40f).Count;
                float ratio = RawOdds(map, alive);
                if (defs < plan.defendersAtCrossing || ratio >= s.overwhelmOdds)
                {
                    plan.crossing = CrossingMode.Mass;
                    AttritionGo++;
                    reason += ", after attrition (" + (plan.defendersAtCrossing - defs) + " defenders down, odds " + ratio.ToString("0.0") + ":1)";
                    return false;
                }
                // out of range and nobody shooting (bows against bows, pistols against rifles): waiting wears nobody down
                bool idle = now - plan.crossingTick > s.attritionTicks / 3 && plan.lastShotTick < plan.crossingTick;
                if (now - plan.crossingTick > s.attritionTicks || idle)
                {
                    if (idle) IdleAttrition++;
                    // before giving up: another angle to sneak in from (a covered approach on another side)?
                    if (!plan.reangled && TryOtherAngle(map, lord, plan, alive))
                    {
                        Reangled++;
                        return true;
                    }
                    // ... or something to breach?
                    if (s.breachFallback && !plan.breachTried && AssaultPlanner.TryBreachInstead(lord, plan, alive))
                    {
                        BreachFallback++;
                        return true;
                    }
                    // fair odds (not overwhelming, not hopeless): fire and movement over the open ground - the base of fire
                    // pins, the rest bound in halves (playtest: leave only when the attack is bound to fail)
                    if (ratio >= s.boundOdds)
                    {
                        plan.crossing = CrossingMode.Bound;
                        Bounds++;
                        if (plan.suppressStartTick < 0) plan.suppressStartTick = now;
                        reason += ", bounding assault over " + plan.openCells + " open cells (odds " + ratio.ToString("0.0") + ":1" + (idle ? ", no shots out of range" : "") + ")";
                        return false;
                    }
                    NoApproach++;
                    plan.capability += "; no safe approach over " + plan.openCells + " open cells (odds " + ratio.ToString("0.0") + ":1 < " + s.boundOdds.ToString("0.0") + ")";
                    if (s.plannedWithdrawal) Withdrawal.Begin(map, lord, plan, alive, "no safe approach");
                    lord.CurLordToil?.UpdateAllDuties();
                    return true;
                }
                return true;
            }
            return false;
        }
    }
}
