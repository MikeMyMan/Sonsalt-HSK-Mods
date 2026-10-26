using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// Siege supplies by pack animal for pre-industrial factions (playtest 2026-10-26: "check lower tier raids where they
    /// bring siege equipment with animals?"). Vanilla drops every siege's building material, shells and meals by drop pod -
    /// medieval kingdoms and tribes included. Below industrial tech the supplies now come in on the faction's pack animals
    /// (its trade caravans' carriers, else muffalo): from the map edge nearest the camp, unloaded at the camp, then they
    /// leave. Industrial and later factions keep their drop pods.
    /// </summary>
    public static class SiegeCaravan
    {
        public static int Animals, Deliveries, Unloaded;
        [ThreadStatic] private static LordToil_Siege current;
        private static readonly List<(Pawn animal, IntVec3 camp)> pending = new List<(Pawn, IntVec3)>();

        public static void Begin(LordToil_Siege toil) => current = toil;
        public static void End() => current = null;

        /// <summary>True when the drop was taken over by a pack-animal caravan.</summary>
        public static bool TryDeliver(IntVec3 camp, Map map, IEnumerable<Thing> things)
        {
            var toil = current;
            var s = TacticalAIMod.Settings;
            if (toil == null || s == null || !s.siegePackAnimals || map == null) return false;
            Faction faction = toil.lord?.faction;
            if (faction == null || faction.def.techLevel >= TechLevel.Industrial) return false;
            List<Thing> items = things.Where(t => t != null && !t.Destroyed && !t.Spawned).ToList();
            if (items.Count == 0) return false;
            PawnKindDef kind = PackKind(faction);
            IntVec3 entry = EntryFor(map, camp);
            if (kind == null || !entry.IsValid) return false;
            var animals = new List<Pawn>();
            Pawn NewAnimal()
            {
                Pawn a = PawnGenerator.GeneratePawn(kind, faction);
                GenSpawn.Spawn(a, CellFinder.RandomClosewalkCellNear(entry, map, 4), map);
                animals.Add(a);
                Animals++;
                return a;
            }
            foreach (Thing t in items.OrderByDescending(x => x.GetStatValue(StatDefOf.Mass) * x.stackCount))
            {
                Thing rest = t;
                while (rest != null && rest.stackCount > 0)
                {
                    float unit = Math.Max(0.01f, rest.GetStatValue(StatDefOf.Mass));
                    Pawn a = animals.FirstOrDefault(x => MassUtility.FreeSpace(x) >= unit) ?? (animals.Count < 12 ? NewAnimal() : null);
                    if (a == null) { a = animals[Rand.Range(0, animals.Count)]; }   // over capacity rather than lost
                    int fit = Math.Max(1, Math.Min(rest.stackCount, (int)(MassUtility.FreeSpace(a) / unit)));
                    Thing part = fit >= rest.stackCount ? rest : rest.SplitOff(fit);
                    a.inventory.innerContainer.TryAdd(part, true);
                    if (part == rest) rest = null;
                }
            }
            if (animals.Count == 0) return false;
            LordMaker.MakeNewLord(faction, new LordJob_TravelAndExit(camp), map, animals);
            foreach (var a in animals) pending.Add((a, camp));
            Deliveries++;
            if (Prefs.DevMode) Log.Message("[HSK Tactical AI] siege supplies for " + faction.Name + " by " + animals.Count + " " + kind.label + " from " + entry);
            return true;
        }

        /// <summary>Every 60 ticks: animals at the camp (or turned to leave) unload their packs there.</summary>
        public static void Tick(Map map)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var (a, camp) = pending[i];
                if (a == null || a.Destroyed || a.Dead || !a.Spawned) { pending.RemoveAt(i); continue; }
                if (a.Map != map) continue;
                bool there = a.Position.DistanceTo(camp) <= 7f;
                bool leaving = a.GetLord()?.CurLordToil is LordToil_ExitMap;
                if (!there && !leaving) continue;
                if (a.inventory.innerContainer.Count > 0)
                {
                    a.inventory.innerContainer.TryDropAll(a.Position, map, ThingPlaceMode.Near);
                    Unloaded++;
                }
                pending.RemoveAt(i);
            }
        }

        private static PawnKindDef PackKind(Faction f)
        {
            var carriers = f.def.pawnGroupMakers?.Where(m => m.kindDef == PawnGroupKindDefOf.Trader && m.carriers != null)
                            .SelectMany(m => m.carriers).Where(o => o.kind?.RaceProps != null && o.kind.RaceProps.packAnimal).ToList();
            if (carriers != null && carriers.Count > 0) return carriers.RandomElementByWeight(o => o.selectionWeight).kind;
            return DefDatabase<PawnKindDef>.GetNamedSilentFail("Muffalo")
                   ?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(k => k.RaceProps != null && k.RaceProps.Animal && k.RaceProps.packAnimal);
        }

        /// <summary>The reachable map edge cell nearest the camp.</summary>
        private static IntVec3 EntryFor(Map map, IntVec3 camp)
        {
            IntVec3 best = IntVec3.Invalid;
            float bd = float.MaxValue;
            foreach (IntVec3 c in CellRect.WholeMap(map).EdgeCells)
            {
                if (!c.Standable(map) || c.Fogged(map)) continue;
                float d = c.DistanceToSquared(camp);
                if (d >= bd) continue;
                if (!map.reachability.CanReach(c, camp, PathEndMode.OnCell, TraverseMode.PassDoors, Danger.Deadly)) continue;
                bd = d;
                best = c;
            }
            return best;
        }
    }

    [HarmonyPatch(typeof(LordToil_Siege), nameof(LordToil_Siege.Init))]
    public static class Patch_SiegeInitCaravan
    {
        public static void Prefix(LordToil_Siege __instance) => SiegeCaravan.Begin(__instance);
        public static void Finalizer() => SiegeCaravan.End();
    }

    [HarmonyPatch(typeof(LordToil_Siege), "DropSupplies")]
    public static class Patch_SiegeResupplyCaravan
    {
        public static void Prefix(LordToil_Siege __instance) => SiegeCaravan.Begin(__instance);
        public static void Finalizer() => SiegeCaravan.End();
    }

    [HarmonyPatch(typeof(DropPodUtility), nameof(DropPodUtility.DropThingGroupsNear))]
    public static class Patch_SiegeDropGroupsByAnimal
    {
        public static bool Prefix(IntVec3 dropCenter, Map map, List<List<Thing>> thingsGroups)
        {
            try { return !(thingsGroups != null && SiegeCaravan.TryDeliver(dropCenter, map, thingsGroups.SelectMany(g => g))); }
            catch (Exception e) { Log.ErrorOnce("[HSK Tactical AI] siege caravan failed, using drop pods: " + e.Message, 0x48534B43); return true; }
        }
    }

    [HarmonyPatch(typeof(DropPodUtility), nameof(DropPodUtility.DropThingsNear))]
    public static class Patch_SiegeDropByAnimal
    {
        public static bool Prefix(IntVec3 dropCenter, Map map, IEnumerable<Thing> things)
        {
            try { return !(things != null && SiegeCaravan.TryDeliver(dropCenter, map, things)); }
            catch (Exception e) { Log.ErrorOnce("[HSK Tactical AI] siege caravan failed, using drop pods: " + e.Message, 0x48534B43); return true; }
        }
    }
}
