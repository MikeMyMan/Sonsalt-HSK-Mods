using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// How the raid arrives, from what the faction knows (Raid Tactics idea: "keep wiping out one faction at the same
    /// killbox and they may try ... drop pods instead"). Only a faction that knows your layout (memory intel) changes it:
    /// - quiet side: a walk-in that failed before comes in from the map edge whose straight line to your base crosses the
    ///   least of your fire (threat map), instead of a random edge;
    /// - drop pods: a spacer faction that failed both walking in and breaching drops in at the edge, if its raid strategy
    ///   allows drop pods.
    /// Natural raids only (a raid whose spawn point another mod or a quest already set is left alone); AI Lab: arrivalOverride.
    /// </summary>
    public static class RaidArrival
    {
        public static int QuietSides, DropPods;
        public static string LastWhy = "";

        public static void Choose(IncidentParms parms)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.arrivalChoice || !(parms?.target is Map map) || !map.IsPlayerHome || parms.faction == null || parms.quest != null) return;
            if (!parms.faction.HostileTo(Faction.OfPlayer) || !parms.faction.def.humanlikeFaction) return;
            string forced = s.arrivalOverride?.ToLowerInvariant() ?? "";
            var mem = FactionMemory.Read(parms.faction);
            bool knows = mem != null && mem.intel >= s.memoryIntelMin;
            if (forced.Length == 0 && (!knows || parms.spawnCenter.IsValid)) return;
            // drop pods: spacers whose walk-ins and breaches both failed
            bool wantPods = forced == "droppods" || (forced.Length == 0 && parms.faction.def.techLevel >= TechLevel.Spacer && mem.failWalk >= 0.4f && mem.failBreach >= 0.4f);
            if (wantPods && TrySet(parms, PawnsArrivalModeDefOf.EdgeDrop))
            {
                DropPods++;
                LastWhy = "drop pods: walk-ins and breaches failed";
                return;
            }
            // quiet side: a failed walk-in, arriving on foot
            bool wantQuiet = forced == "quiet" || (forced.Length == 0 && mem.failWalk >= 0.3f);
            if (!wantQuiet) return;
            if (parms.raidArrivalMode == null && !TrySet(parms, PawnsArrivalModeDefOf.EdgeWalkIn)) return;
            if (parms.raidArrivalMode != PawnsArrivalModeDefOf.EdgeWalkIn && parms.raidArrivalMode != PawnsArrivalModeDefOf.EdgeWalkInGroups) return;
            IntVec3 cell = QuietEdge(map, out float fire, out float worst);
            // no clearly quieter side (or no threat reading yet - rt2: every edge 0, the corner picked): vanilla's edge
            if (!cell.IsValid || worst - fire < 0.05f) { LastWhy = "quiet side: no clearly quieter edge (fire " + fire.ToString("0.00") + "-" + worst.ToString("0.00") + "), vanilla edge"; return; }
            parms.spawnCenter = cell;
            QuietSides++;
            LastWhy = "quiet side: edge " + cell + " fire on the way " + fire.ToString("0.00") + " (worst edge " + worst.ToString("0.00") + ")";
        }

        private static bool TrySet(IncidentParms parms, PawnsArrivalModeDef mode)
        {
            if (mode == null || parms.raidStrategy?.arriveModes == null || !parms.raidStrategy.arriveModes.Contains(mode)) return false;
            if (mode.minTechLevel > parms.faction.def.techLevel || !mode.Worker.CanUseWith(parms)) return false;
            parms.raidArrivalMode = mode;
            return true;
        }

        /// <summary>The colony's middle: its buildings, else its colonists.</summary>
        public static IntVec3 ColonyCentre(Map map)
        {
            var b = map.listerBuildings.allBuildingsColonist.Where(x => x.Spawned).ToList();
            if (b.Count > 0) return new IntVec3(Mathf.RoundToInt((float)b.Average(x => x.Position.x)), 0, Mathf.RoundToInt((float)b.Average(x => x.Position.z)));
            var c = map.mapPawns.FreeColonistsSpawned;
            return c.Count > 0 ? c[0].Position : map.Center;
        }

        /// <summary>The reachable edge cell whose straight line to the colony crosses the least fire (every 6th edge cell).</summary>
        public static IntVec3 QuietEdge(Map map, out float bestFire, out float worstFire)
        {
            bestFire = worstFire = 0f;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0) return IntVec3.Invalid;
            IntVec3 centre = ColonyCentre(map);
            var walk = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly);
            // every 6th edge cell: mean fire on the straight line to the colony
            var scored = new List<(IntVec3 c, float fire, float len)>();
            float worst = 0f;
            int i = 0;
            foreach (IntVec3 c in CellRect.WholeMap(map).EdgeCells)
            {
                if (i++ % 6 != 0 || !c.Standable(map) || c.Fogged(map) || c.Roofed(map)) continue;
                float len = c.DistanceTo(centre);
                if (len < 20f) continue;
                float sum = 0f;
                int n = 0;
                for (float f = 0f; f <= 1f - 15f / len; f += 0.05f, n++)
                {
                    var p = new IntVec3(Mathf.RoundToInt(c.x + (centre.x - c.x) * f), 0, Mathf.RoundToInt(c.z + (centre.z - c.z) * f));
                    if (p.InBounds(map)) sum += tm.FireAt(p);
                }
                float fire = n > 0 ? sum / n : 0f;
                if (fire > worst) worst = fire;
                scored.Add((c, fire, len));
            }
            worstFire = worst;
            if (scored.Count == 0) return IntVec3.Invalid;
            // among the (nearly) quietest edges the shortest walk in - many cells tie at no fire, and the first of them
            // was the map corner (rt5)
            float min = scored.Min(x => x.fire);
            foreach (var x in scored.Where(x => x.fire <= min + 0.02f).OrderBy(x => x.len))
            {
                if (!map.reachability.CanReach(x.c, centre, PathEndMode.Touch, walk)) continue;
                bestFire = x.fire;
                return x.c;
            }
            return IntVec3.Invalid;
        }
    }
}
