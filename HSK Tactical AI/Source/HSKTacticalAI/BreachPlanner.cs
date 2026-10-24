using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// "Breach only when it pays." Vanilla's breaching raid (which Smarter Raid AI turns on for every assault) plans its
    /// path with walls almost free (cost 20 + 0.015 per HP ≈ two cells of walking) and rock only +20, so raiders cut
    /// through walls and even mountains although a way around exists.
    /// Before the breach path is planned we compare:
    ///   open route   = walking cost + exposure to player fire (threat map)
    ///   breach route = walking cost + time to destroy its walls + exposure
    /// If walking around is cheaper, walls get a prohibitive cost for this planning so the breach path follows the
    /// open route; a real killbox makes the open route expensive, so then raiders breach elsewhere. Rock is always
    /// expensive. The original costs are restored right after.
    /// </summary>
    public static class BreachPlanner
    {
        public struct Decision
        {
            public bool useOpenRoute;
            public bool openRouteExists;
            public float openCost, breachCost, openExposure, breachExposure;
            public int wallsOnBreachPath;
            public override string ToString() =>
                (useOpenRoute ? "walk" : "breach") + " (open=" + (openRouteExists ? openCost.ToString("0") : "none") + " exp " + openExposure.ToString("0.0") +
                ", breach=" + breachCost.ToString("0") + " exp " + breachExposure.ToString("0.0") + " walls " + wallsOnBreachPath + ")";
        }

        public static readonly AccessTools.FieldRef<int> WallCost = AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(BreachingGrid), "tweakWallCost"));
        public static readonly AccessTools.FieldRef<int> NaturalWallExtra = AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(BreachingGrid), "tweakNaturalWallExtraCost"));

        public static Decision LastDecision;
        public static int DecisionsWalk, DecisionsBreach;

        public static Decision Decide(Map map, IntVec3 start, IntVec3 end)
        {
            var s = TacticalAIMod.Settings;
            var d = new Decision();
            ThreatMap tm = MapComponent_TacticalAI.For(map)?.Threat;

            using (PawnPath open = map.pathFinder.FindPathNow(start, end, TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly)))
            {
                d.openRouteExists = open != null && open.Found;
                if (d.openRouteExists)
                {
                    d.openExposure = Exposure(tm, open.NodesReversed);
                    d.openCost = open.TotalCost + s.exposureCost * d.openExposure;
                }
            }

            var tuning = new PathFinderCostTuning
            {
                costBlockedWallBase = WallCost(),
                costBlockedWallExtraPerHitPoint = 0.015f,
                costBlockedDoor = WallCost(),
                costBlockedDoorPerHitPoint = 0.015f,
                costBlockedWallExtraForNaturalWalls = s.naturalWallExtraCost
            };
            using (PawnPath breach = map.pathFinder.FindPathNow(start, end, TraverseParms.For(TraverseMode.PassAllDestroyableThings, Danger.Deadly), tuning))
            {
                if (breach != null && breach.Found)
                {
                    float wallTime = 0f;
                    foreach (IntVec3 c in breach.NodesReversed)
                    {
                        Building ed = c.GetEdifice(map);
                        if (ed == null || ed.def.passability != Traversability.Impassable && !ed.def.IsDoor) continue;
                        d.wallsOnBreachPath++;
                        wallTime += s.wallPenalty + s.wallHpPenalty * ed.HitPoints;
                    }
                    d.breachExposure = Exposure(tm, breach.NodesReversed);
                    d.breachCost = breach.TotalCost + wallTime + s.exposureCost * d.breachExposure;
                }
                else d.breachCost = float.MaxValue;
            }

            d.useOpenRoute = d.openRouteExists && d.openCost <= d.breachCost;
            if (d.useOpenRoute) DecisionsWalk++; else DecisionsBreach++;
            LastDecision = d;
            if (Prefs.DevMode) Log.Message("[HSK Tactical AI] breach plan " + start + " -> " + end + ": " + d);
            return d;
        }

        private static float Exposure(ThreatMap tm, List<IntVec3> nodes)
        {
            if (tm == null || tm.CompletedCycles == 0 || nodes == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < nodes.Count; i++) sum += tm.FireAt(nodes[i]);
            return sum;
        }
    }

    [HarmonyPatch(typeof(BreachingGrid), nameof(BreachingGrid.CreateBreachPath))]
    public static class Patch_BreachingGrid_CreateBreachPath
    {
        public static void Prefix(BreachingGrid __instance, IntVec3 start, IntVec3 end, out int[] __state)
        {
            __state = new[] { BreachPlanner.WallCost(), BreachPlanner.NaturalWallExtra() };
            var s = TacticalAIMod.Settings;
            if (s == null || !s.smartBreaching || __instance?.Map == null) return;
            try
            {
                BreachPlanner.NaturalWallExtra() = s.naturalWallExtraCost;
                if (BreachPlanner.Decide(__instance.Map, start, end).useOpenRoute) BreachPlanner.WallCost() = s.blockedWallCostWhenWalking;
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[HSK Tactical AI] breach planning failed, using vanilla: " + e, 0x48534B31);
            }
        }

        public static void Postfix(int[] __state)
        {
            if (__state == null) return;
            BreachPlanner.WallCost() = __state[0];
            BreachPlanner.NaturalWallExtra() = __state[1];
        }
    }
}
