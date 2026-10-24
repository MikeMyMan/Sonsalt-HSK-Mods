using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// Flanking (playtest, 2026-10-23: "try to flank enemies. when they are behind cover they are more protected you can see
    /// when they are outflanked they get taken down more"). A defender's cover protects only towards the side it faces
    /// (CE: a crouching pawn hides behind cover between him and the shooter). From the flank he is exposed.
    /// - targets: exposed defenders first (marksmen strongly, others moderately);
    /// - firing positions (base of fire, overwatch): cells from which more defenders are exposed;
    /// - walk-mode axes: assault positions on the side the defenders' cover does not face score better.
    /// </summary>
    public static class Flanking
    {
        /// <summary>Is the defender covered (fill >= 0.4 next to him, on the shooter's side) against fire from <paramref name="from"/>?</summary>
        public static bool Covered(Pawn defender, IntVec3 from) => FireDoctrine.InCover(defender, from);

        /// <summary>Defenders in reach from a cell that are NOT covered from it (line of sight checked last).</summary>
        public static int ExposedFrom(IntVec3 c, List<Pawn> defs, float range, Map map)
        {
            int n = 0;
            foreach (var d in defs)
            {
                if (d.Position.DistanceTo(c) > range || Covered(d, c)) continue;
                if (GenSight.LineOfSight(c, d.Position, map, true)) n++;
            }
            return n;
        }

        /// <summary>Share of the defenders whose cover does not face this cell (geometry only, no line of sight).</summary>
        public static float ExposedShare(IntVec3 c, List<Pawn> defs)
        {
            if (defs.Count == 0) return 0f;
            return defs.Count(d => !Covered(d, c)) / (float)defs.Count;
        }
    }
}
