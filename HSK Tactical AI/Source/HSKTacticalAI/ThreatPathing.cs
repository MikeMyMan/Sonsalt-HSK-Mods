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
            // tactical competence (Competence.Perceive): the raid's command reads the fire only so far ahead and only the
            // heavier fire, weighs it less, prices funnels only if it knows them - and walks around the traps it knows of
            AssaultPlan plan = Competence.Enabled ? Competence.Ctx : null;
            bool perceive = plan != null && Competence.CtxOrigin.IsValid;
            float minFire = 0.02f, look2 = float.MaxValue;
            int ox = 0, oz = 0, sizeX = map.Size.x;
            if (perceive)
            {
                float scale = Competence.FireScale(plan);
                costPerFire *= scale;
                baseCostInFire = (int)(baseCostInFire * scale);
                if (!Competence.Knows(plan, Doctrine.AvoidFunnels)) funnelPenalty *= 0.15f;
                minFire = Math.Max(minFire, Competence.MinFireNoticed(plan));
                float look = Competence.LookAhead(plan);
                look2 = look * look;
                ox = Competence.CtxOrigin.x; oz = Competence.CtxOrigin.z;
            }
            for (int i = 0; i < n; i++)
            {
                float f = tm.FireAt(i);
                if (f <= minFire) continue;
                if (perceive)
                {
                    int dx = i % sizeX - ox, dz = i / sizeX - oz;
                    if (dx * dx + dz * dz > look2) continue;
                }
                int v = baseCostInFire + (int)(f * costPerFire);
                if (funnelPenalty > 0f && f > safeFire && OpenNeighbours(map, map.cellIndices.IndexToCell(i)) <= 4) v += (int)funnelPenalty;
                offsets[i] = (ushort)Math.Min(v, 30000);
            }
            if (plan != null)
            {
                traps.Clear();
                Competence.KnownTrapCells(map, plan, traps);
                foreach (int i in traps) offsets[i] = (ushort)Math.Min(offsets[i] + Competence.KnownTrapRouteCost, 30000);
            }
        }

        private static readonly List<int> traps = new List<int>();

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
