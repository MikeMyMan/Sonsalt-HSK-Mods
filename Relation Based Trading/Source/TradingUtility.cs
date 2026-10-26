using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RelationBasedTrading
{
    /// <summary>
    /// What a faction's traders may sell at one goodwill value. Every limit grows smoothly with goodwill (no tiers):
    /// t = (goodwill + 75) / 150, clamped to 0..1.
    /// </summary>
    public sealed class TradeLimits
    {
        public int Goodwill;
        public float T;
        public bool NoTrade;                 // below -75: nothing
        public float MaxValue;               // most expensive single item; PositiveInfinity from 75 (Ally)
        public bool DangerAllowed;           // weapons, ammo, explosives, weapon blueprints
        public float DangerFactor;           // dangerous items count this many times their value
        public bool LimitQuality;
        public QualityCategory MaxQuality;
        public bool LimitTech;
        public TechLevel FactionTech;
        public TechLevel MaxTech;

        public override string ToString() =>
            NoTrade ? $"goodwill {Goodwill}: no trade"
                : $"goodwill {Goodwill}: value <= {(float.IsPositiveInfinity(MaxValue) ? "any" : MaxValue.ToString("0"))}, " +
                  $"weapons {(DangerAllowed ? (float.IsPositiveInfinity(MaxValue) ? "all" : "<= " + (MaxValue / DangerFactor).ToString("0")) : "none")}, " +
                  $"quality <= {(LimitQuality ? MaxQuality.ToString() : "any")}, tech <= {(LimitTech ? MaxTech.ToString() : "any")} (faction {FactionTech})";
    }

    public static class TradingUtility
    {
        public static readonly Dictionary<ThingDef, TechLevel> ThingTechLevels = new Dictionary<ThingDef, TechLevel>();
        /// <summary>Food, raw resources and materials, silver, animals, plants: always on sale (above -75).</summary>
        public static readonly HashSet<ThingDef> AlwaysAvailable = new HashSet<ThingDef>();
        /// <summary>Weapons, ammo, shells and explosives, weapon blueprints.</summary>
        public static readonly HashSet<ThingDef> Dangerous = new HashSet<ThingDef>();

        public static bool HSKActive = false;

        private static readonly HarmonyLib.AccessTools.FieldRef<CompQuality, QualityCategory> QualityField =
            HarmonyLib.AccessTools.FieldRefAccess<CompQuality, QualityCategory>("qualityInt");

        public const int HostileBelow = -75;
        public const int AllyAt = 75;

        private static int techLevelFound, researchFound, recipeFound, guessedFound, naturalFound, alwaysAvailable;

        // ------------------------------------------------------------------ limits

        public static float T(int goodwill) => Mathf.Clamp01((goodwill - HostileBelow) / (float)(AllyAt - HostileBelow));

        /// <summary>
        /// Round(steps * T(goodwill)) in whole numbers. Some goodwill values sit exactly on a step (tech steps at -50, 0
        /// and 50), and float rounding there differed between builds.
        /// </summary>
        public static int Step(int goodwill, int steps)
        {
            int span = AllyAt - HostileBelow;
            int g = Mathf.Clamp(goodwill - HostileBelow, 0, span);
            return (2 * steps * g + span) / (2 * span);
        }

        public static TechLevel FactionTechOf(Faction faction)
        {
            TechLevel t = faction?.def?.techLevel ?? TechLevel.Undefined;
            return t <= TechLevel.Animal ? TechLevel.Industrial : t;
        }

        public static TradeLimits LimitsFor(int goodwill, TechLevel factionTech, RelationBasedTradingSettings s)
        {
            s ??= new RelationBasedTradingSettings();
            float t = T(goodwill);
            var l = new TradeLimits { Goodwill = goodwill, T = t, FactionTech = factionTech, NoTrade = goodwill < HostileBelow };
            float start = Mathf.Max(1f, s.startValue), end = Mathf.Max(start, s.valueAtAlly);
            l.MaxValue = goodwill >= AllyAt ? float.PositiveInfinity : start * Mathf.Pow(end / start, t);
            l.DangerAllowed = goodwill >= s.weaponGoodwill;
            l.DangerFactor = Mathf.Max(1f, s.weaponValueFactor);
            l.LimitQuality = s.limitQuality;
            l.MaxQuality = (QualityCategory)Mathf.Clamp((int)QualityCategory.Normal + Step(goodwill, 4), 0, (int)QualityCategory.Legendary);
            l.LimitTech = s.limitTech;
            int tech = Mathf.Clamp((int)factionTech - 2 + Step(goodwill, 3), (int)TechLevel.Undefined, (int)TechLevel.Ultra);
            if (tech == (int)TechLevel.Animal)
                tech = (int)TechLevel.Undefined; // same effect (only goods without research), clearer in the settings preview
            if (goodwill >= AllyAt && factionTech >= TechLevel.Spacer)
                tech = (int)TechLevel.Archotech; // archotech only from spacer factions and up, at Ally
            l.MaxTech = (TechLevel)tech;
            return l;
        }

        /// <summary>
        /// Goodwill with the player as the game shows it (Faction.GoodwillWith, which includes the Ideology goodwill
        /// cap), or null if there is no relation (avoids GoodwillWith error spam).
        /// </summary>
        public static int? PlayerGoodwill(Faction faction)
        {
            Faction player = Find.FactionManager?.OfPlayer;
            if (faction == null || player == null || faction == player)
                return null;
            if (faction.RelationWith(player, allowNull: true) == null)
                return null;
            return faction.GoodwillWith(player);
        }

        public static bool IsFilterable(Faction faction)
        {
            return faction != null && !faction.IsPlayer && !faction.temporary && !faction.Hidden;
        }

        /// <summary>Limits for this faction's stock right now, or null if its stock is not filtered.</summary>
        public static TradeLimits LimitsFor(Faction faction)
        {
            if (!IsFilterable(faction))
                return null;
            EnsureCacheRefreshed();
            int? goodwill = PlayerGoodwill(faction);
            return goodwill.HasValue ? LimitsFor(goodwill.Value, FactionTechOf(faction), RelationBasedTradingMod.Settings) : null;
        }

        public static TechLevel TechOf(ThingDef def) => ThingTechLevels.TryGetValue(def, out TechLevel l) ? l : def.techLevel;

        public static bool IsDangerous(ThingDef def) => def != null && Dangerous.Contains(def);

        /// <summary>
        /// Whether a stock thing may be sold under these limits. With <paramref name="allowDowngrade"/> (freshly
        /// generated stock) items above the quality limit are lowered to it instead of being rejected.
        /// </summary>
        public static bool Allows(TradeLimits l, Thing thing, bool allowDowngrade)
        {
            if (thing == null)
                return false;
            if (thing is Pawn)
                return true; // animals and slaves are not filtered
            if (l.NoTrade)
                return false;
            Thing inner = thing.GetInnerIfMinified();
            ThingDef def = inner.def;
            if (AlwaysAvailable.Contains(def))
                return true;
            if (def.IsMedicine)
                return !l.LimitTech || TechOf(def) <= l.FactionTech; // medicine: what the faction itself makes
            if (l.LimitTech && TechOf(def) > l.MaxTech)
                return false;
            bool danger = IsDangerous(def);
            if (danger && !l.DangerAllowed)
                return false;
            if (l.LimitQuality && inner.TryGetQuality(out QualityCategory q))
            {
                if (!allowDowngrade)
                {
                    if (q > l.MaxQuality)
                        return false;
                }
                else
                {
                    // Fresh stock: the game rerolls trader quality afterwards (CompQuality.PostPostGeneratedForTrader) and
                    // our PostGeneratedForTrader patch caps it at MaxQuality. Judge the value at that worst case.
                    CompQuality cq = inner.TryGetComp<CompQuality>();
                    if (cq == null)
                        return false;
                    if (q != l.MaxQuality)
                    {
                        // SetQuality on a book rebuilds it and generates a random author pawn, which is slow and can
                        // throw in big modlists. The trader reroll rebuilds it anyway, so only set the value here.
                        if (inner is Book)
                            QualityField(cq) = l.MaxQuality;
                        else
                            cq.SetQuality(l.MaxQuality, ArtGenerationContext.Outsider);
                    }
                }
            }
            if (float.IsPositiveInfinity(l.MaxValue))
                return true;
            float value = thing.MarketValue * (danger ? l.DangerFactor : 1f);
            return value <= l.MaxValue;
        }

        /// <summary>Def-level check without a thing (base market value, no quality). Kept for other mods.</summary>
        public static bool AllowsDef(TradeLimits l, ThingDef def)
        {
            if (def == null || l == null)
                return true;
            if (l.NoTrade)
                return false;
            if (AlwaysAvailable.Contains(def))
                return true;
            if (def.IsMedicine)
                return !l.LimitTech || TechOf(def) <= l.FactionTech;
            if (l.LimitTech && TechOf(def) > l.MaxTech)
                return false;
            bool danger = IsDangerous(def);
            if (danger && !l.DangerAllowed)
                return false;
            return float.IsPositiveInfinity(l.MaxValue) || def.BaseMarketValue * (danger ? l.DangerFactor : 1f) <= l.MaxValue;
        }

        // Kept for other mods that may call the 2.0.x API.
        public static bool ShouldIncludeItemBasedOnRelationship(ThingDef def, Faction faction)
        {
            TradeLimits l = LimitsFor(faction);
            return l == null || AllowsDef(l, def);
        }

        public static bool ShouldIncludeThing(Thing thing, Faction faction)
        {
            TradeLimits l = LimitsFor(faction);
            return thing != null && (l == null || Allows(l, thing, allowDowngrade: false));
        }

        // ------------------------------------------------------------------ item classification (cached)

        private static bool refreshedInGame;

        /// <summary>
        /// Rebuilds the caches once, the first time stock is filtered in a running game. Some mods change defs (tech
        /// level, tradeability) after our startup cache ran; with the HSK list about 150 tradeable items (mostly CE ammo)
        /// were missing from it. Takes about 25 ms.
        /// </summary>
        private static void EnsureCacheRefreshed()
        {
            if (refreshedInGame || Current.ProgramState != ProgramState.Playing)
                return;
            refreshedInGame = true;
            CacheTechLevels();
        }

        private static bool IsAlwaysAvailable(ThingDef d)
        {
            if (d == ThingDefOf.Silver || d.race != null || d.IsPlant || d.IsCorpse || d.IsStuff || d.deepCommonality > 0f)
                return true;
            if (IsRawFood(d))
                return true;
            // food: anything that feeds and is not a drug (meals, pemmican, kibble, raw food)
            if (d.IsIngestible && d.IsNutritionGivingIngestible && !d.IsDrug)
                return true;
            return false;
        }

        private static bool IsDangerousDef(ThingDef d)
        {
            // survival tools (pickaxes, sickles, hammers) count as weapons in their defs, but they are work gear
            if ((d.thingClass != null && d.thingClass.Name.IndexOf("Tool", StringComparison.OrdinalIgnoreCase) >= 0)
                || (d.thingCategories != null && d.thingCategories.Any(c => c != null && c.defName.IndexOf("Tool", StringComparison.OrdinalIgnoreCase) >= 0)))
                return false;
            if (d.IsWeapon || d.IsShell)
                return true;
            if (d.defName.StartsWith("Ammo_") || d.GetType().Name == "AmmoDef")
                return true; // Combat Extended ammo
            if (d.defName.StartsWith("WBS_") && d.defName.EndsWith("_Blueprint"))
                return true; // HSK Weapon Blueprints: a blueprint teaches a weapon
            if (d.thingCategories != null && d.thingCategories.Any(c => c != null && (c.defName.IndexOf("Explosive", StringComparison.OrdinalIgnoreCase) >= 0 || c.defName.IndexOf("Grenade", StringComparison.OrdinalIgnoreCase) >= 0)))
                return true;
            return false;
        }

        public static void CacheTechLevels()
        {
            ThingTechLevels.Clear();
            AlwaysAvailable.Clear();
            Dangerous.Clear();
            techLevelFound = researchFound = recipeFound = guessedFound = naturalFound = alwaysAvailable = 0;

            // Index recipes by product and benches by recipe once. The old code scanned every RecipeDef
            // for every ThingDef (O(things x recipes)), and RecipeDef.AllRecipeUsers scans all ThingDefs
            // on every call, which is slow with HSK's def count.
            benchesByRecipe = new Dictionary<RecipeDef, List<ThingDef>>();
            foreach (ThingDef td in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (td.race != null || td.recipes == null)
                    continue;
                foreach (RecipeDef r in td.recipes)
                    if (r != null)
                        AddBench(r, td);
            }
            var recipesByProduct = new Dictionary<ThingDef, List<RecipeDef>>();
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.recipeUsers != null)
                    foreach (ThingDef user in recipe.recipeUsers)
                        if (user != null && user.race == null)
                            AddBench(recipe, user);

                // Only recipes whose single product is the item count as "a way to make it".
                if (recipe.products == null || recipe.products.Count != 1)
                    continue;
                ThingDef product = recipe.products[0]?.thingDef;
                if (product == null)
                    continue;
                if (!recipesByProduct.TryGetValue(product, out var list))
                    recipesByProduct[product] = list = new List<RecipeDef>();
                list.Add(recipe);
            }

            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (thingDef.tradeability == Tradeability.None)
                    continue;
                ThingTechLevels[thingDef] = DetermineTechLevel(thingDef, recipesByProduct);
                if (IsAlwaysAvailable(thingDef)) { AlwaysAvailable.Add(thingDef); alwaysAvailable++; }
                else if (IsDangerousDef(thingDef)) Dangerous.Add(thingDef);
            }
            benchesByRecipe = null; // only needed while caching

            Log.Message($"[Relation Based Trading] Cached {ThingTechLevels.Count} tradeable items: {alwaysAvailable} always available, {Dangerous.Count} dangerous. " +
                        $"Tech level sources: {techLevelFound} techLevel, {researchFound} research, {recipeFound} recipes, {naturalFound} natural/food, {guessedFound} guessed from materials.");
        }

        private static TechLevel MaxResearchLevel(IEnumerable<ResearchProjectDef> projects)
        {
            TechLevel result = TechLevel.Undefined;
            if (projects == null)
                return result;
            foreach (ResearchProjectDef p in projects)
                if (p != null && p.techLevel > result)
                    result = p.techLevel;
            return result;
        }

        private static Dictionary<RecipeDef, List<ThingDef>> benchesByRecipe;

        private static void AddBench(RecipeDef recipe, ThingDef bench)
        {
            if (!benchesByRecipe.TryGetValue(recipe, out var list))
                benchesByRecipe[recipe] = list = new List<ThingDef>();
            if (!list.Contains(bench))
                list.Add(bench);
        }

        /// <summary>Tech level of making something with this recipe, or null if no building can do it.</summary>
        private static TechLevel? RecipeTechLevel(RecipeDef recipe)
        {
            if (!benchesByRecipe.TryGetValue(recipe, out var benches) || benches.Count == 0)
                return null; // orphan or pawn-only recipe: not a usable way to make the item

            TechLevel level = TechLevel.Undefined;
            if (recipe.researchPrerequisite != null)
                level = recipe.researchPrerequisite.techLevel;
            TechLevel multi = MaxResearchLevel(recipe.researchPrerequisites);
            if (multi > level)
                level = multi;

            TechLevel cheapestBench = benches.Min(b => MaxResearchLevel(b.researchPrerequisites));
            return cheapestBench > level ? cheapestBench : level;
        }

        private static TechLevel DetermineTechLevel(ThingDef thingDef, Dictionary<ThingDef, List<RecipeDef>> recipesByProduct)
        {
            if (thingDef == ThingDefOf.Silver)
                return TechLevel.Undefined;

            // Ores and deep-drillable resources: rarer means higher tier
            if (thingDef.deepCommonality > 0)
            {
                if (thingDef.deepCommonality >= 1.5f) return TechLevel.Undefined;
                if (thingDef.deepCommonality >= 1f) return TechLevel.Medieval;
                if (thingDef.deepCommonality >= 0.5f) return TechLevel.Industrial;
                return TechLevel.Spacer;
            }

            if (thingDef.race != null || thingDef.IsPlant || thingDef.IsCorpse || IsRawFood(thingDef))
            {
                naturalFound++;
                return TechLevel.Undefined;
            }

            if (thingDef.techLevel != TechLevel.Undefined && thingDef.techLevel != TechLevel.Animal)
            {
                techLevelFound++;
                return thingDef.techLevel;
            }

            if (thingDef.IsIngestible && thingDef.ingestible.IsMeal)
            {
                naturalFound++;
                return TechLevel.Undefined;
            }

            if (!thingDef.researchPrerequisites.NullOrEmpty())
            {
                researchFound++;
                return MaxResearchLevel(thingDef.researchPrerequisites);
            }

            if (thingDef.recipeMaker != null &&
                (thingDef.recipeMaker.researchPrerequisite != null || !thingDef.recipeMaker.researchPrerequisites.NullOrEmpty()))
            {
                researchFound++;
                TechLevel single = thingDef.recipeMaker.researchPrerequisite?.techLevel ?? TechLevel.Undefined;
                TechLevel multi = MaxResearchLevel(thingDef.recipeMaker.researchPrerequisites);
                return single > multi ? single : multi;
            }

            if (recipesByProduct.TryGetValue(thingDef, out var recipes) && recipes.Count > 0)
            {
                TechLevel? best = null;
                foreach (RecipeDef r in recipes)
                {
                    TechLevel? lvl = RecipeTechLevel(r);
                    if (lvl.HasValue && (!best.HasValue || lvl.Value < best.Value))
                        best = lvl;
                }
                if (best.HasValue)
                {
                    recipeFound++;
                    return best.Value;
                }
            }

            if ((thingDef.IsWeapon || thingDef.IsApparel) && thingDef.stuffCategories != null)
            {
                TechLevel? guess = GuessFromStuff(thingDef.stuffCategories);
                if (guess.HasValue)
                {
                    guessedFound++;
                    return guess.Value;
                }
            }

            if (thingDef.IsStuff)
            {
                naturalFound++;
                return TechLevel.Undefined;
            }

            return TechLevel.Industrial;
        }

        // ThingDef.IsRawFood() was removed in RimWorld 1.6; this works on 1.5 to 1.7.
        private static bool IsRawFood(ThingDef thingDef)
        {
            IngestibleProperties ing = thingDef.ingestible;
            return ing != null && (ing.preferability == FoodPreferability.RawBad || ing.preferability == FoodPreferability.RawTasty);
        }

        private static TechLevel? GuessFromStuff(List<StuffCategoryDef> cats)
        {
            if (HSKActive && cats.Any(t => t != null && (t == StuffCategoryDefOfLocal.RareMetallic || t == StuffCategoryDefOfLocal.Precious || t == StuffCategoryDefOfLocal.HF)))
                return TechLevel.Spacer;
            if (cats.Any(t => t != null && (t == StuffCategoryDefOf.Metallic || (HSKActive && (t == StuffCategoryDefOfLocal.SolidMetallic || t == StuffCategoryDefOfLocal.HeavyMetallic)))))
                return TechLevel.Industrial;
            if (cats.Any(t => t != null && (t == StuffCategoryDefOf.Fabric || t == StuffCategoryDefOf.Leathery || (HSKActive && (t == StuffCategoryDefOfLocal.StrongMetallic || t == StuffCategoryDefOfLocal.RuggedMetallic)))))
                return TechLevel.Medieval;
            if (cats.Any(t => t != null && (t == StuffCategoryDefOf.Woody || t == StuffCategoryDefOf.Stony || (HSKActive && t == StuffCategoryDefOfLocal.WoodLogs))))
                return TechLevel.Neolithic;
            return null;
        }
    }

    [DefOf]
    public static class StuffCategoryDefOfLocal
    {
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef RareMetallic;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef SolidMetallic;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef HeavyMetallic;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef StrongMetallic;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef RuggedMetallic;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef Precious;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef HF;
        [MayRequireAnyOf("skyarkhangel.HSK")] public static StuffCategoryDef WoodLogs;

        static StuffCategoryDefOfLocal()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(StuffCategoryDefOfLocal));
        }
    }
}
