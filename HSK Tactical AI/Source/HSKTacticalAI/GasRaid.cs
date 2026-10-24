using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// Gas and burn-out (playtest, 2026-10-23: "have a look at larger ranges of weapons like gas etc. that can be used in
    /// closed areas"; gas masks for gas raids only).
    ///
    /// Defenders in a small enclosed, roofed room (a bunker, a mountain room, a keep): Industrial+ raids become gas raids.
    /// - kit: every raider gets a gas mask if it fits with what it wears; two raiders get tox grenades; if the room is full
    ///   of flammables (wood floor, furniture), two get molotovs / incendiary grenades;
    /// - when the breach opens, the first throws are tox into the room, then fire if it burns (frags after that);
    /// - the gas cloud and the fire are no-go zones for gasHoldTicks: the raid holds at the hole and the overwatch shoots
    ///   whoever comes out; then it goes in, masked.
    /// </summary>
    public static class GasRaid
    {
        public static int MasksGiven, ToxGiven, FireGiven, GasThrows, FireThrows;
        private static readonly HashSet<int> kitted = new HashSet<int>();

        public static bool IsTox(ThingDef d) => d != null && d.defName.Contains("GrenadeTox");
        public static bool IsFire(ThingDef d) => d != null && (d.defName.Contains("Molotov") || d.defName == "Weapon_GrenadeFlame");
        public static Thing CarriedTox(Pawn p) => p.inventory?.innerContainer?.FirstOrDefault(t => IsTox(t.def));
        public static Thing CarriedFire(Pawn p) => p.inventory?.innerContainer?.FirstOrDefault(t => IsFire(t.def));

        /// <summary>
        /// The defenders' enclosed space: the roofed cells connected to the objective (flood fill over walkable roofed
        /// cells), if there are at most 400 - a bunker, a mountain room with its tunnel, a keep with its corridor (the game
        /// merges a doorless room with the outdoors, so its own room check misses those). Returns the room at the
        /// objective (for flammability) or null.
        /// </summary>
        public static Room EnclosedRoom(Map map, IntVec3 objective)
        {
            if (!objective.InBounds(map) || !objective.Roofed(map)) return null;
            var seen = new HashSet<IntVec3> { objective };
            var queue = new Queue<IntVec3>();
            queue.Enqueue(objective);
            while (queue.Count > 0)
            {
                IntVec3 c = queue.Dequeue();
                foreach (IntVec3 d in GenAdj.CardinalDirections)
                {
                    IntVec3 n = c + d;
                    if (!n.InBounds(map) || seen.Contains(n) || !n.Roofed(map) || !n.Walkable(map)) continue;
                    seen.Add(n);
                    if (seen.Count > 400) return null;
                    queue.Enqueue(n);
                }
            }
            if (seen.Count < 9) return null;
            return objective.GetRoom(map);
        }

        public static bool Flammable(Room r, Map map) =>
            r.ContainedAndAdjacentThings.Count(t => t.def.category == ThingCategory.Building && t.GetStatValue(StatDefOf.Flammability) >= 0.5f) >= 3
            || r.Cells.Count(c => c.GetTerrain(map).GetStatValueAbstract(StatDefOf.Flammability) > 0f) > r.CellCount / 2;

        public static void Plan(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            Map map = lord.Map;
            plan.gasRoom = null;
            if (!s.gasRaids || lord.faction == null || lord.faction.def.techLevel < TechLevel.Industrial) return;
            Room r = EnclosedRoom(map, plan.objective);
            if (r == null) return;
            plan.gasRoom = r;
            plan.gasFlammable = !r.PsychologicallyOutdoors && Flammable(r, map);
            plan.capability += "; gas raid (enclosed space" + (plan.gasFlammable ? ", flammable" : "") + ")";
            if (!s.equipRaiders || !kitted.Add(lord.loadID)) return;   // masks and grenades only as extra equipment; else what they carry
            ThingDef tox = DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeTox");
            ThingDef fire = DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeMolotov") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeFlame");
            ThingDef mask = DefDatabase<ThingDef>.GetNamedSilentFail("Hat_GasMask");
            var humans = pawns.Where(p => p.RaceProps.Humanlike && p.inventory != null && p.apparel != null).ToList();
            if (mask != null)
                foreach (var p in humans)
                {
                    if (!p.apparel.CanWearWithoutDroppingAnything(mask) || !ApparelUtility.HasPartsToWear(p, mask)) continue;
                    var m = (Apparel)ThingMaker.MakeThing(mask, mask.MadeFromStuff ? GenStuff.DefaultStuffFor(mask) : null);
                    p.apparel.Wear(m, false);
                    MasksGiven++;
                }
            // throwers: not base of fire, not medic; breach teams last (they're busy at the wall)
            var throwers = humans.Where(p => !plan.roleOf.TryGetValue(p, out var rr) || (rr != RaidRole.Support && rr != RaidRole.Marksman && rr != RaidRole.Medic))
                                 .OrderBy(p => plan.breachTeam.Contains(p) ? 1 : 0).ToList();
            if (tox != null)
                foreach (var p in throwers.Take(2))
                {
                    Thing g = ThingMaker.MakeThing(tox);
                    g.stackCount = Mathf.Min(2, tox.stackLimit);
                    if (p.inventory.innerContainer.TryAdd(g)) ToxGiven += g.stackCount;
                }
            if (fire != null && plan.gasFlammable)
                foreach (var p in throwers.Skip(2).Concat(throwers).Distinct().Take(2))
                {
                    Thing g = ThingMaker.MakeThing(fire);
                    g.stackCount = Mathf.Min(2, fire.stackLimit);
                    if (p.inventory.innerContainer.TryAdd(g)) FireGiven += g.stackCount;
                }
        }

        /// <summary>
        /// At a breach opening into the gas room: one tox throw (room centre / defenders' cluster) and, if the room burns,
        /// one fire throw - from covered positions beside the opening (Grenades.CoveredThrow). Returns the throws ordered.
        /// </summary>
        public static int OrderAtOpening(Map map, AssaultPlan plan, IEnumerable<Pawn> group, IntVec3 opening)
        {
            var s = TacticalAIMod.Settings;
            if (plan.gasRoom == null || plan.gasOpenings.Contains(opening)) return 0;
            plan.gasOpenings.Add(opening);
            var defs = Suppression.Defenders(map, plan.objective, 30f);
            IntVec3 aim = defs.Count > 0
                ? new IntVec3((int)defs.Average(d => d.Position.x), 0, (int)defs.Average(d => d.Position.z))
                : plan.objective;
            int n = 0;
            bool gas = false, burn = !plan.gasFlammable;
            foreach (var p in group.Where(p => p.Spawned && !p.Downed && !plan.throwOrders.ContainsKey(p)).OrderBy(p => p.Position.DistanceToSquared(opening)))
            {
                Thing item = !gas ? CarriedTox(p) : null;
                bool isGas = item != null;
                if (item == null && !burn) item = CarriedFire(p);
                if (item == null) continue;
                if (!Grenades.TryOrderCoveredThrow(p, plan, item, aim, opening, smoke: false)) continue;
                plan.dangerZones.Add((aim, isGas ? s.gasRadius : 3f, Find.TickManager.TicksGame + s.gasHoldTicks));
                if (isGas) { gas = true; GasThrows++; } else { burn = true; FireThrows++; }
                n++;
                if (gas && burn) break;
            }
            return n;
        }
    }
}
