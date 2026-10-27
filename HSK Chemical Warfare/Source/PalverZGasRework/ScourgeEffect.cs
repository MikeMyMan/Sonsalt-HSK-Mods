using System;
using System.Collections.Generic;
using GasFrameWork;
using HarmonyLib;
using RimWorld;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace PalverZGasRework
{
    /// <summary>Per-cell corrosion: kills plants, eats buildings in the cloud and walls next to it.
    /// Wood and light metal go fast, stone slowly. Loot on the ground is handled by the gas's
    /// framework damage entry (damageGroundThings).</summary>
    public class GasEffect_Scourge : GasEffectProperties
    {
        public DamageDef damageDef;
        /// <summary>Damage per second to each building at full density, before material factors.</summary>
        public FloatRange buildingDamagePerSecond = new FloatRange(4f, 8f);
        public bool scaleWithDensity = true;
        public int plantKillMinDensity = 15;
        public bool damageAdjacentWalls = true;
        public float woodyFactor = 3f;
        public float metallicFactor = 1.5f;
        public float stonyFactor = 0.35f;
        public float otherFactor = 1f;
        public float resistantFactor = 0.25f;
        public List<ThingDef> resistantStuff = new List<ThingDef>();
        /// <summary>Damage per second to items lying in the cloud (weapons, apparel, stockpiles, corpses).</summary>
        public FloatRange itemDamagePerSecond = new FloatRange(1f, 2f);

        public GasEffect_Scourge() { workerClass = typeof(GasEffectWorker_Scourge); }
    }

    public class GasEffectWorker_Scourge : GasEffectWorker
    {
        // The framework visits each gas cell about once per 64 ticks; a wall next to several gas
        // cells would otherwise be hit several times per pass.
        private static readonly Dictionary<int, int> lastHitTick = new Dictionary<int, int>();
        private static readonly Dictionary<ThingDef, bool> emitterDefs = new Dictionary<ThingDef, bool>();
        private const int HitCooldownTicks = 55;

        public override bool NeedsCellTick => true;

        public override void CellTick(Map map, IntVec3 cell, byte density)
        {
            try
            {
                Corrode(map, cell, density);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("scourge/" + gas?.defName, $"Scourge corrosion in {gas?.defName} failed at {cell}: {e}");
            }
        }

        private void Corrode(Map map, IntVec3 cell, byte density)
        {
            var p = (GasEffect_Scourge)props;
            float densityFactor = p.scaleWithDensity ? density / 255f : 1f;
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (i >= things.Count) continue;
                Thing t = things[i];
                if (t is Plant plant)
                {
                    if (density >= p.plantKillMinDensity && !plant.Destroyed)
                        plant.Kill(new DamageInfo(p.damageDef ?? DamageDefOf.Deterioration, 99999f));
                }
                else if (t is Building building)
                {
                    TryCorrode(building, p, densityFactor);
                }
                else if (t.def.category == ThingCategory.Item && t.def.useHitPoints && !t.Destroyed && Ready(t))
                {
                    int dmg = GenMath.RoundRandom(p.itemDamagePerSecond.RandomInRange * densityFactor);
                    if (dmg > 0) t.TakeDamage(new DamageInfo(p.damageDef ?? DamageDefOf.Deterioration, dmg));
                }
            }
            if (!p.damageAdjacentWalls) return;
            for (int k = 0; k < 4; k++)
            {
                IntVec3 c = cell + GenAdj.CardinalDirections[k];
                if (!c.InBounds(map)) continue;
                Building edifice = c.GetEdifice(map);
                if (edifice != null && edifice.def.Fillage == FillCategory.Full) TryCorrode(edifice, p, densityFactor);
            }
        }

        private static void TryCorrode(Building b, GasEffect_Scourge p, float densityFactor)
        {
            if (b.Destroyed || !b.def.useHitPoints || b.def.building == null || b.def.building.isNaturalRock) return;
            if (IsGasEmitter(b.def) || !Ready(b)) return;
            int amount = GenMath.RoundRandom(p.buildingDamagePerSecond.RandomInRange * MaterialFactor(b, p) * densityFactor);
            if (amount <= 0) return;
            b.TakeDamage(new DamageInfo(p.damageDef ?? DamageDefOf.Deterioration, amount));
        }

        /// <summary>One hit per thing per pass, however many gas cells touch it.</summary>
        private static bool Ready(Thing t)
        {
            int now = Find.TickManager.TicksGame;
            if (lastHitTick.TryGetValue(t.thingIDNumber, out int last) && now - last < HitCooldownTicks) return false;
            lastHitTick[t.thingIDNumber] = now;
            if (lastHitTick.Count > 4096) Prune(now);
            return true;
        }

        private static float MaterialFactor(Thing t, GasEffect_Scourge p)
        {
            ThingDef stuff = t.Stuff;
            if (stuff == null) return p.otherFactor;
            if (p.resistantStuff != null && p.resistantStuff.Contains(stuff)) return p.resistantFactor;
            List<StuffCategoryDef> cats = stuff.stuffProps?.categories;
            if (cats == null) return p.otherFactor;
            if (cats.Contains(StuffCategoryDefOf.Woody)) return p.woodyFactor;
            if (cats.Contains(StuffCategoryDefOf.Metallic)) return p.metallicFactor;
            if (cats.Contains(StuffCategoryDefOf.Stony)) return p.stonyFactor;
            return p.otherFactor;
        }

        // Vents that release the gas are built to survive it.
        private static bool IsGasEmitter(ThingDef def)
        {
            if (emitterDefs.TryGetValue(def, out bool result)) return result;
            result = false;
            if (def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_GasEmitter) { result = true; break; }
                }
            }
            emitterDefs[def] = result;
            return result;
        }

        private static readonly List<int> tmpStale = new List<int>();

        private static void Prune(int now)
        {
            tmpStale.Clear();
            foreach (KeyValuePair<int, int> kv in lastHitTick)
            {
                if (now - kv.Value > 600) tmpStale.Add(kv.Key);
            }
            for (int i = 0; i < tmpStale.Count; i++) lastHitTick.Remove(tmpStale[i]);
            tmpStale.Clear();
        }
    }

    // ------------------------------------------------------------------ raider avoidance

    /// <summary>Put on a GasTypeDef: hostile raiders that use the avoid grid (the one they use to
    /// steer around turrets) treat cells with this gas as costly and path around them.
    /// Sappers and other lords that ignore the avoid grid walk straight through.</summary>
    public class GasAvoidanceExtension : DefModExtension
    {
        public int minDensity = 20;
        public int baseCost = 30;
        /// <summary>Extra cost at full density; the avoid grid value is multiplied by 8 for path cost.</summary>
        public int densityCost = 70;
    }

    public static class GasAvoidance
    {
        private static readonly AccessTools.FieldRef<AvoidGrid, NativeArray<byte>> AvoidGridArray =
            AccessTools.FieldRefAccess<AvoidGrid, NativeArray<byte>>("grid");
        private static readonly AccessTools.FieldRef<AvoidGrid, bool> AvoidGridDirty =
            AccessTools.FieldRefAccess<AvoidGrid, bool>("gridDirty");
        private static readonly AccessTools.FieldRef<MapComponent_CustomGasGrid, Dictionary<GasTypeDef, byte[]>> GasGrids =
            AccessTools.FieldRefAccess<MapComponent_CustomGasGrid, Dictionary<GasTypeDef, byte[]>>("gasGrids");

        public static bool AvoidedGasEverActive(Map map)
        {
            MapComponent_CustomGasGrid grid = CustomGasUtility.GetGrid(map);
            if (grid == null || !grid.AnyGasActive) return false;
            foreach (GasTypeDef g in grid.ActiveGasTypes)
                if (g.GetModExtension<GasAvoidanceExtension>() != null) return true;
            return false;
        }

        public static bool AnyAvoidedGas(Map map)
        {
            MapComponent_CustomGasGrid grid = CustomGasUtility.GetGrid(map);
            if (grid == null || !grid.AnyGasActive) return false;
            foreach (KeyValuePair<GasTypeDef, byte[]> kv in GasGrids(grid))
            {
                GasAvoidanceExtension ext = kv.Key.GetModExtension<GasAvoidanceExtension>();
                if (ext == null) continue;
                byte[] cells = kv.Value;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] >= ext.minDensity) return true;
                }
            }
            return false;
        }

        public static void AddCosts(AvoidGrid avoidGrid)
        {
            MapComponent_CustomGasGrid grid = CustomGasUtility.GetGrid(avoidGrid.map);
            if (grid == null || !grid.AnyGasActive) return;
            NativeArray<byte> target = AvoidGridArray(avoidGrid);
            foreach (KeyValuePair<GasTypeDef, byte[]> kv in GasGrids(grid))
            {
                GasAvoidanceExtension ext = kv.Key.GetModExtension<GasAvoidanceExtension>();
                if (ext == null) continue;
                byte[] cells = kv.Value;
                int n = Math.Min(cells.Length, target.Length);
                for (int i = 0; i < n; i++)
                {
                    byte d = cells[i];
                    if (d < ext.minDensity) continue;
                    int cost = ext.baseCost + ext.densityCost * d / 255;
                    target[i] = (byte)Math.Min(255, target[i] + cost);
                }
            }
        }

        public static void MarkDirty(Map map)
        {
            if (map?.avoidGrid != null) AvoidGridDirty(map.avoidGrid) = true;
        }
    }

    [HarmonyPatch(typeof(AvoidGrid), nameof(AvoidGrid.Regenerate))]
    public static class Patch_AvoidGrid_Regenerate
    {
        public static void Postfix(AvoidGrid __instance)
        {
            try
            {
                GasAvoidance.AddCosts(__instance);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("avoidgrid", "Adding gas costs to the avoid grid failed: " + e);
            }
        }
    }

    /// <summary>Marks the avoid grid stale every 2 seconds once an avoided gas has been on the map, so
    /// costs follow the cloud and vanish with it. The grid only rebuilds when a raider actually paths,
    /// so this costs nothing while nobody is moving.</summary>
    public class MapComponent_GasAvoidance : MapComponent
    {
        public MapComponent_GasAvoidance(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + map.uniqueID) % 120 != 0) return;
            try
            {
                if (GasAvoidance.AvoidedGasEverActive(map)) GasAvoidance.MarkDirty(map);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("avoidtick", "Gas avoidance check failed: " + e);
            }
        }
    }
}
