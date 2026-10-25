using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WeaponBlueprints
{
    [DefOf]
    public static class WBS_DefOf
    {
        public static JobDef WBS_StudyBlueprint;
        public static RecipeDef WBS_ReverseEngineerWeapon;
        public static ThingCategoryDef WBS_Blueprints;

        static WBS_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WBS_DefOf));
        }
    }

    // Runs after all defs are resolved: stats work now, trader stock generators can be inspected.
    [StaticConstructorOnStartup]
    public static class WBSStartup
    {
        public static int TradersWithBlueprints;

        static WBSStartup()
        {
            Step("setting blueprint prices", SetBlueprintPrices);
            Step("adding blueprints to traders", () => TradersWithBlueprints = AddTraderStock());
            Step("applying settings", ApplyRuntimeSettings);
            Log.Message($"[Weapon Blueprints] {WeaponGate.BlueprintOf.Count} blueprints; sold by {TradersWithBlueprints} trader kinds.");
        }

        private static void Step(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Error("[Weapon Blueprints] Failed " + what + ": " + e);
            }
        }

        public static void ApplyRuntimeSettings()
        {
            if (WBS_DefOf.WBS_ReverseEngineerWeapon != null && WBSMod.Settings != null)
            {
                WBS_DefOf.WBS_ReverseEngineerWeapon.workAmount = WBSMod.Settings.reWorkAmount;
            }
            StudyBalance.ClearCaches();
        }

        private static void SetBlueprintPrices()
        {
            foreach (KeyValuePair<ThingDef, ThingDef> kv in WeaponGate.BlueprintOf)
            {
                kv.Value.SetStatBaseValue(StatDefOf.MarketValue, StudyBalance.BlueprintMarketValue(kv.Key));
            }
        }

        // Traders that already sell gated weapons or books get a blueprint stock generator.
        // Done here, not at def generation, because category-based generators only know their
        // contents once categories are resolved.
        private static int AddTraderStock()
        {
            if (WeaponGate.GatedWeaponsList.Count == 0)
            {
                return 0;
            }
            TechLevel threshold = WeaponGate.GatedWeaponsList.Min(w => w.techLevel);
            List<ThingDef> books = new[] { "Schematic", "TextBook", "Novel" }
                .Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList();
            // Lowest tech level among the factions that send each trader kind: trader kinds that tribes
            // use (HSK also gives them to some "Industrial" tribal-style factions) get no blueprints.
            // Traders no faction lists (orbital ships, Hospitality guests...) are judged by their stock alone.
            Dictionary<TraderKindDef, TechLevel> factionTech = new Dictionary<TraderKindDef, TechLevel>();
            foreach (FactionDef f in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                foreach (List<TraderKindDef> list in new[] { f.caravanTraderKinds, f.visitorTraderKinds, f.baseTraderKinds, f.orbitalTraderKinds })
                {
                    if (list == null)
                    {
                        continue;
                    }
                    foreach (TraderKindDef tk in list)
                    {
                        if (tk != null && f.techLevel != TechLevel.Undefined && (!factionTech.TryGetValue(tk, out TechLevel t) || f.techLevel < t))
                        {
                            factionTech[tk] = f.techLevel;
                        }
                    }
                }
            }

            int added = 0;
            foreach (TraderKindDef trader in DefDatabase<TraderKindDef>.AllDefsListForReading)
            {
                if (trader.stockGenerators == null || trader.stockGenerators.Any(g => g is StockGenerator_Blueprints))
                {
                    continue;
                }
                TechLevel ownerTech = trader.faction != null ? trader.faction.techLevel
                    : factionTech.TryGetValue(trader, out TechLevel ft) ? ft : TechLevel.Undefined;
                if (ownerTech != TechLevel.Undefined && ownerTech < threshold)
                {
                    continue;
                }
                TechLevel maxWeaponTech = TechLevel.Undefined;
                foreach (ThingDef w in WeaponGate.GatedWeaponsList)
                {
                    if (w.techLevel > maxWeaponTech && trader.stockGenerators.Any(g => g.maxTechLevelGenerate >= w.techLevel && SafeHandles(g, w)))
                    {
                        maxWeaponTech = w.techLevel;
                    }
                }
                bool sellsBooks = books.Any(b => trader.stockGenerators.Any(g => SafeHandles(g, b)));
                if (maxWeaponTech == TechLevel.Undefined && !sellsBooks)
                {
                    continue;
                }
                StockGenerator_Blueprints gen = new StockGenerator_Blueprints
                {
                    orbitalCounts = trader.orbital,
                    maxTechLevelGenerate = trader.orbital ? TechLevel.Archotech
                        : maxWeaponTech > TechLevel.Industrial ? maxWeaponTech : TechLevel.Industrial
                };
                gen.ResolveReferences(trader);
                trader.stockGenerators.Add(gen);
                added++;
            }
            return added;
        }

        // Some generators throw for defs they don't expect (vanilla StockGenerator_Tomes on non-tomes).
        private static bool SafeHandles(StockGenerator g, ThingDef def)
        {
            try
            {
                return g.HandlesThingDef(def);
            }
            catch
            {
                return false;
            }
        }
    }
}
