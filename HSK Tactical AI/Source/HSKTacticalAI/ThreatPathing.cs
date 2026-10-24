using System;
using System.Collections.Generic;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Extra path cost per cell from the threat map, handed to the vanilla pathfinder: cells the defenders can shoot
    /// cost a lot, so planned routes swing wide around their weapons' reach (dead ground) instead of crossing it.
    /// </summary>
    public sealed class ThreatCustomizer : PathRequest.IPathGridCustomizer, IDisposable
    {
        private NativeArray<ushort> offsets;

        /// <param name="funnelPenalty">extra cost for narrow cells under fire (corridors, doorways, gaps): 0 = off</param>
        public ThreatCustomizer(Map map, ThreatMap tm, float costPerFire, int baseCostInFire, float funnelPenalty = 0f, float safeFire = 0.05f)
        {
            int n = map.cellIndices.NumGridCells;
            offsets = new NativeArray<ushort>(n, Allocator.TempJob);
            if (tm == null || tm.CompletedCycles == 0 || tm.CellCount != n) return;
            for (int i = 0; i < n; i++)
            {
                float f = tm.FireAt(i);
                if (f <= 0.02f) continue;
                int v = baseCostInFire + (int)(f * costPerFire);
                if (funnelPenalty > 0f && f > safeFire && OpenNeighbours(map, map.cellIndices.IndexToCell(i)) <= 4) v += (int)funnelPenalty;
                offsets[i] = (ushort)Math.Min(v, 30000);
            }
        }

        /// <summary>Walkable neighbours of a cell (8 max): ≤ 4 means corridor / doorway / gap.</summary>
        public static int OpenNeighbours(Map map, IntVec3 c)
        {
            int n = 0;
            foreach (var d in GenAdj.AdjacentCells)
            {
                var a = c + d;
                if (a.InBounds(map) && a.Walkable(map)) n++;
            }
            return n;
        }

        public NativeArray<ushort> GetOffsetGrid() => offsets;

        public void Dispose()
        {
            if (offsets.IsCreated) offsets.Dispose();
        }
    }

    public static class ThreatPathing
    {
        /// <summary>Route from start to end that avoids the defenders' fire; nodes in walking order (start -> end).</summary>
        public static bool TryFindRoute(Map map, IntVec3 start, IntVec3 end, ThreatCustomizer customizer, out List<IntVec3> nodes, out float cost)
        {
            nodes = new List<IntVec3>();
            cost = 0f;
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            using (PawnPath p = map.pathFinder.FindPathNow(start, end, walk, null, PathEndMode.OnCell, customizer))
            {
                if (p == null || !p.Found) return false;
                nodes.AddRange(p.NodesReversed);
                nodes.Reverse();
                cost = p.TotalCost;
                return true;
            }
        }

        /// <summary>Every n-th node plus the last one: the route as waypoints.</summary>
        public static List<IntVec3> Waypoints(List<IntVec3> nodes, int every)
        {
            var w = new List<IntVec3>();
            for (int i = every; i < nodes.Count - 1; i += every) w.Add(nodes[i]);
            if (nodes.Count > 0) w.Add(nodes[nodes.Count - 1]);
            return w;
        }
    }
}
