using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// Where the player's shooters and turrets can put direct fire, and how much.
    /// Built in cycles: at cycle start the sources are collected; each tick a few sources are shadowcast into the
    /// back buffer within a CPU budget; when all are done the buffers swap. Readers always see a complete grid.
    /// </summary>
    public class ThreatMap
    {
        public struct Source
        {
            public IntVec3 pos;
            public float range;
            public float minRange;
            public float weight;
        }

        private readonly Map map;
        private readonly int sizeX, sizeZ;
        private float[] front, back;
        private byte[] sightFront, sightBack;
        private readonly int[] blockStamp;
        private readonly bool[] blockVal;
        private int stamp = 1;

        private readonly List<Source> sources = new List<Source>();
        private int nextSource;
        private bool building;
        private int cycleStartTick = -100000;
        private readonly Stopwatch sw = new Stopwatch();
        private double cycleMsAccum;
        private Source cur;

        public int CompletedCycles { get; private set; }
        /// <summary>Positions of the shooters/turrets in the last completed cycle (the defenders).</summary>
        public readonly List<IntVec3> SourcePositions = new List<IntVec3>();
        public float MaxFire { get; private set; }
        public int LastSourceCount { get; private set; }
        public double LastCycleMs { get; private set; }
        public double LastTickMs { get; private set; }
        public event Action Swapped;

        public ThreatMap(Map map)
        {
            this.map = map;
            sizeX = map.Size.x;
            sizeZ = map.Size.z;
            int n = sizeX * sizeZ;
            front = new float[n];
            back = new float[n];
            sightFront = new byte[n];
            sightBack = new byte[n];
            blockStamp = new int[n];
            blockVal = new bool[n];
        }

        public int CellCount => front.Length;
        public float FireAt(int index) => front[index];
        public byte SightAt(int index) => sightFront[index];

        public float FireAt(IntVec3 c)
        {
            if (c.x < 0 || c.z < 0 || c.x >= sizeX || c.z >= sizeZ) return 0f;
            return front[c.z * sizeX + c.x];
        }

        public void Tick()
        {
            var s = TacticalAIMod.Settings;
            LastTickMs = 0;
            if (!s.enableThreatMap) return;
            int now = Find.TickManager.TicksGame;
            if (!building)
            {
                int since = now - cycleStartTick;
                if (since < s.cycleTicksCombat) return;
                if (since < s.cycleTicksPeace && !HostilesPresent()) return;
                StartCycle(now);
            }
            sw.Restart();
            while (nextSource < sources.Count)
            {
                Cast(sources[nextSource++]);
                if (sw.Elapsed.TotalMilliseconds >= s.budgetMsPerTick) break;
            }
            sw.Stop();
            LastTickMs = sw.Elapsed.TotalMilliseconds;
            cycleMsAccum += LastTickMs;
            if (nextSource >= sources.Count) FinishCycle();
        }

        /// <summary>Start a new cycle on the next tick (e.g. after the base or the shooters changed a lot).</summary>
        public void ForceRebuild()
        {
            if (!building) cycleStartTick = -100000;
        }

        /// <summary>Rebuild the whole map now, in one go (outside the time-sliced cycle; e.g. after the defenders' weapons changed).</summary>
        public void RebuildNow()
        {
            StartCycle(Find.TickManager.TicksGame);
            while (nextSource < sources.Count) Cast(sources[nextSource++]);
            FinishCycle();
        }

        private bool HostilesPresent()
        {
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!p.Downed && p.HostileTo(Faction.OfPlayer)) return true;
            }
            return false;
        }

        private void StartCycle(int now)
        {
            building = true;
            cycleStartTick = now;
            cycleMsAccum = 0;
            nextSource = 0;
            stamp++;
            Array.Clear(back, 0, back.Length);
            Array.Clear(sightBack, 0, sightBack.Length);
            CollectSources();
        }

        private void FinishCycle()
        {
            building = false;
            (front, back) = (back, front);
            (sightFront, sightBack) = (sightBack, sightFront);
            float max = 0f;
            for (int i = 0; i < front.Length; i++) if (front[i] > max) max = front[i];
            MaxFire = max;
            LastSourceCount = sources.Count;
            SourcePositions.Clear();
            foreach (var src in sources) SourcePositions.Add(src.pos);
            LastCycleMs = cycleMsAccum;
            CompletedCycles++;
            Swapped?.Invoke();
        }

        private void CollectSources()
        {
            sources.Clear();
            var s = TacticalAIMod.Settings;
            Faction player = Faction.OfPlayer;
            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(player))
            {
                if (p.Dead || p.Downed) continue;
                Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
                if (v == null || v.IsMeleeAttack || v.ProjectileFliesOverhead()) continue;
                float w = (p.drafter != null && p.drafter.Drafted) ? s.weightDrafted : s.weightUndrafted;
                if (!p.RaceProps.Humanlike) w *= 0.6f;
                Add(p.Position, v, w);
            }
            var buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is Building_Turret t)) continue;
                Verb v;
                try { v = t.AttackVerb; } catch { continue; }
                if (v == null || v.IsMeleeAttack || v.ProjectileFliesOverhead()) continue;
                float w = s.weightTurret;
                if (t is Building_TurretGun tg && !tg.Active) w *= 0.25f;
                var man = t.TryGetComp<CompMannable>();
                if (man != null && !man.MannedNow) w *= 0.3f;
                Add(t.Position, v, w);
            }
        }

        private void Add(IntVec3 pos, Verb v, float w)
        {
            float range;
            try { range = v.EffectiveRange; } catch { range = v.verbProps.range; }
            if (range < 1.5f || w <= 0f) return;
            sources.Add(new Source { pos = pos, range = Math.Min(range, 75f), minRange = v.verbProps.minRange, weight = w });
        }

        // ---------------- shadowcasting (recursive, 8 octants) ----------------

        private static readonly int[] MXX = { 1, 0, 0, -1, -1, 0, 0, 1 };
        private static readonly int[] MXY = { 0, 1, -1, 0, 0, -1, 1, 0 };
        private static readonly int[] MYX = { 0, 1, 1, 0, 0, -1, -1, 0 };
        private static readonly int[] MYY = { 1, 0, 0, 1, -1, 0, 0, -1 };

        private void Cast(Source src)
        {
            cur = src;
            int radius = (int)Math.Ceiling(src.range);
            Light(src.pos.x, src.pos.z, 0);
            for (int oct = 0; oct < 8; oct++)
                CastLight(src.pos.x, src.pos.z, 1, 1.0f, 0.0f, radius, MXX[oct], MXY[oct], MYX[oct], MYY[oct]);
        }

        private bool Blocks(int x, int z)
        {
            int i = z * sizeX + x;
            if (blockStamp[i] != stamp)
            {
                blockStamp[i] = stamp;
                blockVal[i] = !new IntVec3(x, 0, z).CanBeSeenOver(map);
            }
            return blockVal[i];
        }

        private void Light(int x, int z, int dist2)
        {
            float d = (float)Math.Sqrt(dist2);
            if (d < cur.minRange) return;
            float f = d / cur.range;
            float falloff = 1f - 0.6f * f * f;
            int i = z * sizeX + x;
            back[i] += cur.weight * falloff;
            if (sightBack[i] < 255) sightBack[i]++;
        }

        private void CastLight(int cx, int cz, int row, float start, float end, int radius, int xx, int xy, int yx, int yy)
        {
            if (start < end) return;
            int r2 = radius * radius;
            float newStart = 0f;
            for (int j = row; j <= radius; j++)
            {
                int dx = -j - 1, dy = -j;
                bool blocked = false;
                while (dx <= 0)
                {
                    dx++;
                    int X = cx + dx * xx + dy * xy;
                    int Z = cz + dx * yx + dy * yy;
                    float lSlope = (dx - 0.5f) / (dy + 0.5f);
                    float rSlope = (dx + 0.5f) / (dy - 0.5f);
                    if (start < rSlope) continue;
                    if (end > lSlope) break;
                    bool inMap = X >= 0 && Z >= 0 && X < sizeX && Z < sizeZ;
                    bool blk = !inMap || Blocks(X, Z);
                    int d2 = dx * dx + dy * dy;
                    if (inMap && !blk && d2 <= r2) Light(X, Z, d2);
                    if (blocked)
                    {
                        if (blk) { newStart = rSlope; continue; }
                        blocked = false;
                        start = newStart;
                    }
                    else if (blk && j < radius)
                    {
                        blocked = true;
                        CastLight(cx, cz, j + 1, start, lSlope, radius, xx, xy, yx, yy);
                        newStart = rSlope;
                    }
                }
                if (blocked) break;
            }
        }
    }
}
