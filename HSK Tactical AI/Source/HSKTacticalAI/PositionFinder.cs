using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// One position finder (an improvement on CAI's cover flood, which scores destinations but not the way there): a
    /// Dijkstra flood out from the pawn over walkable cells. Each reached cell knows its path length and the fire it costs
    /// to get there (threat map, summed along the way). Callers score candidates cheaply on that, then run the expensive
    /// checks (line of sight) only on the best few. Reachability comes with the flood - no CanReach per candidate - and a
    /// spot behind a firing lane loses to an equally good one that can be reached in cover.
    /// </summary>
    public static class PositionFinder
    {
        public struct Cell
        {
            public IntVec3 c;
            public float path;       // walking cost
            public float exposure;   // fire summed along the way
        }

        public static int Floods;
        // performance (pf5: Support up to 14 ms): flat per-cell arrays with a generation stamp instead of dictionaries -
        // nothing to clear between floods, no hashing per neighbour
        private static float[] best = new float[0], expo = new float[0];
        private static int[] stamp = new int[0];
        private static int gen;
        private static readonly PriorityQueue<IntVec3, float> queue = new PriorityQueue<IntVec3, float>();

        private static bool Seen(int i, out float d)
        {
            if (stamp[i] == gen) { d = best[i]; return true; }
            d = 0f;
            return false;
        }

        /// <summary>All cells reachable within maxPath (diagonals 1.41), with path length and exposure; at most maxCells.</summary>
        public static List<Cell> Flood(Pawn p, float maxPath, int maxCells = 4000)
        {
            Floods++;
            Map map = p.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool threat = tm != null && tm.CompletedCycles > 0;
            var result = new List<Cell>();
            int gridCells = map.cellIndices.NumGridCells;
            if (stamp.Length != gridCells) { best = new float[gridCells]; expo = new float[gridCells]; stamp = new int[gridCells]; gen = 0; }
            if (++gen == int.MaxValue) { System.Array.Clear(stamp, 0, gridCells); gen = 1; }
            queue.Clear();
            IntVec3 start = p.Position;
            int si = map.cellIndices.CellToIndex(start);
            best[si] = 0f; expo[si] = 0f; stamp[si] = gen;
            queue.Enqueue(start, 0f);
            while (queue.TryDequeue(out IntVec3 c, out float d))
            {
                int ci = map.cellIndices.CellToIndex(c);
                if (d > best[ci] + 0.001f) continue;
                float e = expo[ci];
                result.Add(new Cell { c = c, path = d, exposure = e });
                if (result.Count >= maxCells) break;
                for (int k = 0; k < 8; k++)
                {
                    IntVec3 n = c + GenAdj.AdjacentCells[k];
                    if (!n.InBounds(map) || !n.Walkable(map)) continue;
                    bool diag = k >= 4;
                    if (diag && (!(c + new IntVec3(n.x - c.x, 0, 0)).Walkable(map) || !(c + new IntVec3(0, 0, n.z - c.z)).Walkable(map))) continue;   // no corner cutting
                    if (n.GetEdifice(map) is Building_Door door && !door.PawnCanOpen(p)) continue;
                    float step = diag ? 1.41f : 1f;
                    float nd = d + step;
                    if (nd > maxPath) continue;
                    int ni = map.cellIndices.CellToIndex(n);
                    if (Seen(ni, out float old) && old <= nd) continue;
                    best[ni] = nd;
                    stamp[ni] = gen;
                    expo[ni] = e + (threat ? tm.FireAt(n) * step : 0f);
                    queue.Enqueue(n, nd);
                }
            }
            return result;
        }

        /// <summary>
        /// Flood, keep the cells passing <paramref name="cheap"/> (returns a score, NaN = reject), and run
        /// <paramref name="expensive"/> (returns a score adjustment, NaN = reject) on the best <paramref name="topN"/> only.
        /// Lowest total wins.
        /// </summary>
        public static IntVec3 Find(Pawn p, float maxPath, Func<Cell, float> cheap, Func<IntVec3, float> expensive, int topN = 30)
        {
            var cands = new List<(IntVec3 c, float score)>();
            foreach (var cell in Flood(p, maxPath))
            {
                if (!cell.c.Standable(p.Map)) continue;
                float s = cheap(cell);
                if (float.IsNaN(s)) continue;
                cands.Add((cell.c, s));
            }
            cands.Sort((a, b) => a.score.CompareTo(b.score));
            IntVec3 bestCell = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            for (int i = 0; i < cands.Count && i < topN; i++)
            {
                float adj = expensive != null ? expensive(cands[i].c) : 0f;
                if (float.IsNaN(adj)) continue;
                float total = cands[i].score + adj;
                if (total < bestScore) { bestScore = total; bestCell = cands[i].c; }
            }
            return bestCell;
        }
    }
}
