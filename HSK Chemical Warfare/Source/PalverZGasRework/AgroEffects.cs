using System;
using System.Collections.Generic;
using GasFrameWork;
using RimWorld;
using Verse;

namespace PalverZGasRework
{
    // The framework visits every gas cell about once per 64 ticks (roughly once a second),
    // so per-second amounts below are applied once per visit.

    /// <summary>Herbicide: grass, bushes, crops and flowers in the cloud wither within seconds.
    /// Trees drop their leaves at once and die only after minutes of exposure. Trees with special
    /// abilities (anima, gauranlen, polux and the like) are spared.</summary>
    public class GasEffect_Herbicide : GasEffectProperties
    {
        public int minDensity = 15;
        /// <summary>Damage per second to a plant at full density.</summary>
        public FloatRange plantDamagePerSecond = new FloatRange(25f, 35f);
        /// <summary>Trees take this share of the damage.</summary>
        public float treeFactor = 0.05f;
        public bool defoliateTrees = true;
        public bool spareSpecialTrees = true;
        public DamageDef damageDef;

        public GasEffect_Herbicide() { workerClass = typeof(GasEffectWorker_Herbicide); }
    }

    public class GasEffectWorker_Herbicide : GasEffectWorker
    {
        private static readonly Dictionary<ThingDef, bool> special = new Dictionary<ThingDef, bool>();

        public override bool NeedsCellTick => true;

        public override void CellTick(Map map, IntVec3 cell, byte density)
        {
            try
            {
                Wither(map, cell, density);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("herbicide/" + gas?.defName, $"Herbicide in {gas?.defName} failed at {cell}: {e}");
            }
        }

        private void Wither(Map map, IntVec3 cell, byte density)
        {
            var p = (GasEffect_Herbicide)props;
            if (density < p.minDensity) return;
            Plant plant = cell.GetPlant(map);
            if (plant == null || plant.Destroyed || plant.def.plant == null) return;
            if (p.spareSpecialTrees && IsSpecialTree(plant.def)) return;
            float amount = p.plantDamagePerSecond.RandomInRange * density / 255f;
            if (plant.def.plant.IsTree)
            {
                // Poison on a tree without a leafless look, or one that dies when leafless, kills it outright.
                if (p.defoliateTrees && !plant.LeaflessNow && plant.def.plant.leaflessGraphic != null && !plant.def.plant.dieIfLeafless)
                    plant.MakeLeafless(Plant.LeaflessCause.Poison, sendMessage: false);
                amount *= p.treeFactor;
            }
            int dmg = GenMath.RoundRandom(amount);
            if (dmg > 0) plant.TakeDamage(new DamageInfo(p.damageDef ?? DamageDefOf.Rotting, dmg));
        }

        // Comps that mark a tree with powers: anima (meditation, subplants), gauranlen (connection),
        // polux (pollution pump), and the uncuttable ones. HSK gives plain trees comps too, so any comp won't do.
        private static readonly HashSet<string> SpecialComps = new HashSet<string>
        {
            "CompProperties_MeditationFocus", "CompProperties_SpawnSubplant", "CompProperties_PlantPreventCutting",
            "CompProperties_TreeConnection", "CompProperties_PollutionPump",
        };

        private static bool IsSpecialTree(ThingDef def)
        {
            if (special.TryGetValue(def, out bool result)) return result;
            result = false;
            if (def.plant.IsTree)
            {
                // Mods often give ordinary trees their own class, so only the harbinger tree is matched by class.
                if (def.thingClass != null && def.thingClass.Name.Contains("Harbinger")) result = true;
                else if (def.comps != null)
                {
                    for (int i = 0; i < def.comps.Count && !result; i++)
                        result = SpecialComps.Contains(def.comps[i].GetType().Name);
                }
            }
            special[def] = result;
            return result;
        }
    }

    /// <summary>Pesticide on the map: insect hives in the cloud lose a share of their hit points every second.
    /// The effect on creatures is a GasEffect_Hediff scaled by body size.</summary>
    public class GasEffect_Pesticide : GasEffectProperties
    {
        public int minDensity = 30;
        /// <summary>Share of a hive's max hit points lost per second at full density.</summary>
        public FloatRange hiveFractionPerSecond = new FloatRange(0.015f, 0.025f);
        public DamageDef damageDef;

        public GasEffect_Pesticide() { workerClass = typeof(GasEffectWorker_Pesticide); }
    }

    public class GasEffectWorker_Pesticide : GasEffectWorker
    {
        public override bool NeedsCellTick => true;

        public override void CellTick(Map map, IntVec3 cell, byte density)
        {
            var p = (GasEffect_Pesticide)props;
            if (density < p.minDensity) return;
            try
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (i >= things.Count || !(things[i] is Hive hive) || hive.Destroyed || !hive.def.useHitPoints) continue;
                    int dmg = GenMath.RoundRandom(hive.MaxHitPoints * p.hiveFractionPerSecond.RandomInRange * density / 255f);
                    if (dmg > 0) hive.TakeDamage(new DamageInfo(p.damageDef ?? DamageDefOf.Deterioration, dmg));
                }
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("pesticide/" + gas?.defName, $"Pesticide in {gas?.defName} failed at {cell}: {e}");
            }
        }
    }
}
