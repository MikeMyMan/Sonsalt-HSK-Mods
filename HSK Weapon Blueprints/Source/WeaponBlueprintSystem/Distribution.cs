using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WeaponBlueprints
{
    // Weighted blueprint choice shared by traders and rewards/loot.
    // Cheaper, lower-tech weapons are more common; weapons the colony already knows are rarer.
    public static class BlueprintPicker
    {
        public const float KnownWeaponWeightFactor = 0.25f;

        public static IEnumerable<ThingDef> AllBlueprints => WeaponGate.WeaponOfBlueprint.Keys;

        public static float Weight(ThingDef blueprint)
        {
            ThingDef weapon = WeaponGate.WeaponFor(blueprint);
            if (weapon == null)
            {
                return 0f;
            }
            float w = StudyBalance.SelectionWeight(weapon);
            WBSGameComponent comp = WBSGameComponent.Instance;
            if (comp != null && comp.IsUnlocked(weapon))
            {
                w *= KnownWeaponWeightFactor;
            }
            return w;
        }

        public static List<ThingDef> Pick(int count, Func<ThingDef, bool> allowed)
        {
            List<ThingDef> pool = AllBlueprints.Where(bp => allowed == null || allowed(bp)).ToList();
            List<ThingDef> result = new List<ThingDef>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                if (!pool.TryRandomElementByWeight(Weight, out ThingDef chosen))
                {
                    break;
                }
                result.Add(chosen);
                pool.Remove(chosen);
            }
            return result;
        }
    }

    // Added in code (WBSStartup) to traders that already sell gated weapons or books.
    public class StockGenerator_Blueprints : StockGenerator
    {
        public bool orbitalCounts;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            WBSSettings s = WBSMod.Settings;
            if (!s.tradersSellBlueprints)
            {
                yield break;
            }
            int count = orbitalCounts ? Rand.RangeInclusive(s.orbitalMin, s.orbitalMax) : Rand.RangeInclusive(s.traderMin, s.traderMax);
            foreach (ThingDef bp in BlueprintPicker.Pick(count, d => d.techLevel <= maxTechLevelGenerate))
            {
                foreach (Thing t in StockGeneratorUtility.TryMakeForStock(bp, 1, faction))
                {
                    yield return t; // quality (and the book's title) is set later by the trader stock code
                }
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return WeaponGate.IsBlueprint(thingDef) && thingDef.techLevel <= maxTechLevelBuy;
        }
    }

    // Quest rewards, item stashes and ancient-ruin loot (added as an option by XML patches).
    public class ThingSetMaker_Blueprints : ThingSetMaker
    {
        public IntRange count = IntRange.One;

        protected override bool CanGenerateSub(ThingSetMakerParams parms)
        {
            return WBSMod.Settings.rewardsAndLoot && Candidates(parms, float.MaxValue).Any();
        }

        protected override void Generate(ThingSetMakerParams parms, List<Thing> outThings)
        {
            if (!WBSMod.Settings.rewardsAndLoot)
            {
                return;
            }
            int n = Math.Max(1, parms.countRange?.RandomInRange ?? count.RandomInRange);
            float budget = parms.totalMarketValueRange?.RandomInRange ?? float.MaxValue;
            float massLeft = parms.maxTotalMass ?? float.MaxValue;
            QualityGenerator quality = parms.qualityGenerator ?? QualityGenerator.Reward;
            for (int i = 0; i < n; i++)
            {
                float budgetNow = budget;
                float massNow = massLeft;
                ThingDef chosen = BlueprintPicker.Pick(1, bp => Fits(bp, parms, budgetNow, massNow)).FirstOrDefault();
                if (chosen == null && i == 0)
                {
                    // Nothing fits the value budget: give the cheapest allowed blueprint rather than nothing.
                    chosen = Candidates(parms, float.MaxValue).OrderBy(bp => bp.BaseMarketValue).FirstOrDefault();
                }
                if (chosen == null)
                {
                    break;
                }
                Book book = BookUtility.MakeBook(chosen, ArtGenerationContext.Outsider, quality);
                if (book == null)
                {
                    break;
                }
                outThings.Add(book);
                budget -= book.MarketValue;
                massLeft -= book.GetStatValue(StatDefOf.Mass);
                if (budget <= 0f)
                {
                    break;
                }
            }
        }

        private static bool Fits(ThingDef bp, ThingSetMakerParams parms, float budget, float massLeft)
        {
            return Allowed(bp, parms) && bp.BaseMarketValue <= budget && bp.BaseMass <= massLeft;
        }

        private static bool Allowed(ThingDef bp, ThingSetMakerParams parms)
        {
            if (parms.techLevel.HasValue && bp.techLevel > parms.techLevel.Value)
            {
                return false;
            }
            if (parms.maxThingMarketValue.HasValue && bp.BaseMarketValue > parms.maxThingMarketValue.Value)
            {
                return false;
            }
            if (parms.filter != null && !parms.filter.Allows(bp))
            {
                return false;
            }
            if (parms.validator != null && !parms.validator(bp))
            {
                return false;
            }
            return true;
        }

        private static IEnumerable<ThingDef> Candidates(ThingSetMakerParams parms, float maxValue)
        {
            return BlueprintPicker.AllBlueprints.Where(bp => Allowed(bp, parms) && bp.BaseMarketValue <= maxValue);
        }

        protected override IEnumerable<ThingDef> AllGeneratableThingsDebugSub(ThingSetMakerParams parms)
        {
            return Candidates(parms, float.MaxValue);
        }
    }
}
