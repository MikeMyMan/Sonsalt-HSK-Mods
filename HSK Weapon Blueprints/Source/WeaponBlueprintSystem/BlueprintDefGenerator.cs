using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WeaponBlueprints
{
    // Runs in a postfix on DefGenerator.GenerateImpliedDefs_PreResolve: all XML is loaded and
    // cross-referenced, implied Make_X recipes exist, nothing is resolved yet.
    public static class BlueprintDefGenerator
    {
        public const string TemplateDefName = "WBS_BlueprintTemplate";
        public const string CategoryDefName = "WBS_Blueprints";
        public const string ReverseEngineerRecipeDefName = "WBS_ReverseEngineerWeapon";
        public const string StudyWorkGiverDefName = "WBS_StudyBlueprint";

        private static readonly AccessTools.FieldRef<ThingFilter, List<ThingDef>> FilterThingDefs =
            AccessTools.FieldRefAccess<ThingFilter, List<ThingDef>>("thingDefs");
        private static readonly AccessTools.FieldRef<ThingFilter, List<string>> FilterCategories =
            AccessTools.FieldRefAccess<ThingFilter, List<string>>("categories");
        private static readonly System.Reflection.MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");

        public static void Run(bool hotReload)
        {
            WBSSettings s = WBSMod.Settings ?? new WBSSettings();
            WeaponGate.Build(s);

            ThingDef template = DefDatabase<ThingDef>.GetNamedSilentFail(TemplateDefName);
            ThingCategoryDef category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(CategoryDefName);
            if (template == null || category == null)
            {
                Log.Error("[Weapon Blueprints] Missing " + TemplateDefName + " or " + CategoryDefName + "; no blueprints generated.");
                return;
            }

            foreach (ThingDef weapon in WeaponGate.GatedWeaponsList)
            {
                ThingDef bp = MakeBlueprintDef(weapon, template, category, hotReload);
                DefGenerator.AddImpliedDef(bp, hotReload);
                WeaponGate.BlueprintOf[weapon] = bp;
                WeaponGate.WeaponOfBlueprint[bp] = weapon;
            }

            SetUpReverseEngineering();
            SetUpStudyWorkPriority(s);

            Log.Message($"[Weapon Blueprints] {WeaponGate.GatedWeapons.Count} weapons need a blueprint " +
                        $"({WeaponGate.GatedRecipes.Count} recipes on {WeaponGate.AllBenches.Count} benches, tech >= {s.techThreshold}).");
        }

        private static ThingDef MakeBlueprintDef(ThingDef weapon, ThingDef t, ThingCategoryDef category, bool hotReload)
        {
            // "_Blueprint" suffix: defNames must not end in a digit, and "Blueprint_" is the building-blueprint prefix.
            string defName = "WBS_" + weapon.defName + "_Blueprint";
            ThingDef d = hotReload ? (DefDatabase<ThingDef>.GetNamedSilentFail(defName) ?? new ThingDef()) : new ThingDef();

            d.defName = defName;
            d.label = "WBS_BlueprintLabel".Translate(weapon.label.Named("WEAPON"));
            d.description = "WBS_BlueprintDesc".Translate(weapon.label.Named("WEAPON"), weapon.techLevel.ToStringHuman().Named("TECH"));
            d.descriptionHyperlinks = new List<DefHyperlink> { new DefHyperlink(weapon) };
            d.modContentPack = t.modContentPack;
            d.techLevel = weapon.techLevel;

            // Item behaviour copied from the template (which inherits vanilla BookBase plus HSK/CE patches).
            d.thingClass = t.thingClass;
            d.category = t.category;
            d.selectable = t.selectable;
            d.rotatable = t.rotatable;
            d.altitudeLayer = t.altitudeLayer;
            d.tickerType = t.tickerType;
            d.alwaysHaulable = t.alwaysHaulable;
            d.burnableByRecipe = t.burnableByRecipe;
            d.useHitPoints = t.useHitPoints;
            d.healthAffectsPrice = t.healthAffectsPrice;
            d.drawGUIOverlay = t.drawGUIOverlay;
            d.tradeNeverStack = t.tradeNeverStack;
            d.neverMultiSelect = t.neverMultiSelect;
            d.stackLimit = t.stackLimit;
            d.tradeNeverGenerateStacked = t.tradeNeverGenerateStacked;
            d.possessionCount = t.possessionCount;
            d.pathCost = t.pathCost;
            d.drawerType = t.drawerType;
            d.smeltable = t.smeltable;
            d.deteriorateFromEnvironmentalEffects = t.deteriorateFromEnvironmentalEffects;
            d.resourceReadoutPriority = t.resourceReadoutPriority;
            d.soundInteract = t.soundInteract;
            d.soundDrop = t.soundDrop;
            d.soundPickup = t.soundPickup;
            d.uiIconPath = t.uiIconPath;
            d.generateCommonality = t.generateCommonality;
            d.tradeability = Tradeability.All;
            d.modExtensions = t.modExtensions?.ToList();
            d.inspectorTabs = t.inspectorTabs?.ToList();
            d.tradeTags = t.tradeTags?.ToList();
            d.thingSetMakerTags = null; // rewards come through ThingSetMaker_Blueprints only
            d.thingCategories = new List<ThingCategoryDef> { category };

            if (t.graphicData != null)
            {
                d.graphicData = new GraphicData();
                d.graphicData.CopyFrom(t.graphicData);
            }

            d.statBases = new List<StatModifier>();
            if (t.statBases != null)
            {
                foreach (StatModifier m in t.statBases)
                {
                    d.statBases.Add(new StatModifier { stat = m.stat, value = m.value });
                }
            }
            d.SetStatBaseValue(StatDefOf.MarketValue, 100f); // real price set in WBSStartup once stats work

            d.comps = new List<CompProperties>();
            foreach (CompProperties cp in t.comps)
            {
                if (cp is CompProperties_Book tb)
                {
                    d.comps.Add(CopyBookProps(tb, weapon));
                }
                else
                {
                    d.comps.Add((CompProperties)CloneMethod.Invoke(cp, null)); // Forbiddable, Quality, patched-in comps
                }
            }
            return d;
        }

        private static CompProperties_Book CopyBookProps(CompProperties_Book t, ThingDef weapon)
        {
            CompProperties_Book p = new CompProperties_Book
            {
                compClass = t.compClass,
                nameMaker = t.nameMaker,
                descriptionMaker = t.descriptionMaker,
                pickWeight = 0f, // keep blueprints out of vanilla "random book" generation
                ageYearsRange = t.ageYearsRange,
                questChance = 0f,
                mentalBreakIntensity = t.mentalBreakIntensity,
                developmentalStageFilter = t.developmentalStageFilter,
                doers = new List<ReadingOutcomeProperties> { new BookOutcomeProperties_Blueprint { weapon = weapon } }
            };
            if (t.openGraphic != null)
            {
                p.openGraphic = new GraphicData();
                p.openGraphic.CopyFrom(t.openGraphic);
            }
            if (t.verticalGraphic != null)
            {
                p.verticalGraphic = new GraphicData();
                p.verticalGraphic.CopyFrom(t.verticalGraphic);
            }
            return p;
        }

        // One generic bill on every bench that crafts a gated weapon; it only accepts gated weapons.
        private static void SetUpReverseEngineering()
        {
            RecipeDef re = DefDatabase<RecipeDef>.GetNamedSilentFail(ReverseEngineerRecipeDefName);
            if (re == null)
            {
                Log.Error("[Weapon Blueprints] Missing RecipeDef " + ReverseEngineerRecipeDefName);
                return;
            }
            re.recipeUsers = WeaponGate.AllBenches.ToList();
            List<ThingDef> weapons = WeaponGate.GatedWeaponsList.Where(w => !IsPersonaWeapon(w)).ToList();

            re.fixedIngredientFilter ??= new ThingFilter();
            FilterThingDefs(re.fixedIngredientFilter) = weapons.ToList();
            FilterCategories(re.fixedIngredientFilter) = null;

            ThingFilter ingredientFilter = new ThingFilter();
            FilterThingDefs(ingredientFilter) = weapons.ToList();
            IngredientCount ic = new IngredientCount { filter = ingredientFilter };
            ic.SetBaseCount(1f);
            re.ingredients = new List<IngredientCount> { ic };
        }

        private static bool IsPersonaWeapon(ThingDef def)
        {
            return def.comps != null && def.comps.Any(c => c?.compClass != null && typeof(CompBladelinkWeapon).IsAssignableFrom(c.compClass));
        }

        private static void SetUpStudyWorkPriority(WBSSettings s)
        {
            WorkGiverDef wg = DefDatabase<WorkGiverDef>.GetNamedSilentFail(StudyWorkGiverDefName);
            if (wg?.workType == null)
            {
                return;
            }
            List<int> others = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(g => g != wg && g.workType == wg.workType)
                .Select(g => g.priorityInType).ToList();
            if (others.Count == 0)
            {
                return;
            }
            wg.priorityInType = s.studyBeforeResearch ? others.Max() + 5 : others.Min() - 5;
        }
    }

    [HarmonyPatch(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PreResolve))]
    public static class Patch_DefGenerator_GenerateImpliedDefs_PreResolve
    {
        public static void Postfix(bool hotReload)
        {
            try
            {
                BlueprintDefGenerator.Run(hotReload);
            }
            catch (Exception e)
            {
                Log.Error("[Weapon Blueprints] Failed to generate blueprints: " + e);
            }
        }
    }
}
