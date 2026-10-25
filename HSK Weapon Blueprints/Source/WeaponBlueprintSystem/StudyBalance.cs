using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WeaponBlueprints
{
    // All balance formulas in one place.
    public static class StudyBalance
    {
        public const float TicksPerHour = 2500f;

        // Study speed by Intellectual level. No minimum skill; low skill is just slow.
        private static readonly SimpleCurve IntellectualCurve = new SimpleCurve
        {
            new CurvePoint(0f, 0.25f),
            new CurvePoint(5f, 0.6f),
            new CurvePoint(10f, 1f),
            new CurvePoint(15f, 1.4f),
            new CurvePoint(20f, 1.8f)
        };

        private static readonly Dictionary<ThingDef, float> requiredCache = new Dictionary<ThingDef, float>();

        public static float StudyHours(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Spacer: return 24f;
                case TechLevel.Ultra: return 36f;
                case TechLevel.Archotech: return 48f;
                default: return 12f;
            }
        }

        public static float WeaponValue(ThingDef weapon)
        {
            return weapon == null ? 0f : weapon.BaseMarketValue;
        }

        public static float ValueFactor(ThingDef weapon)
        {
            return Mathf.Clamp(Mathf.Sqrt(Mathf.Max(WeaponValue(weapon), 1f) / 500f), 0.75f, 1.5f);
        }

        // Study points needed. One point ~ one tick of reading by a skill-10 pawn with a Normal blueprint.
        public static float RequiredStudy(ThingDef weapon)
        {
            if (weapon == null)
            {
                return 1f;
            }
            if (!requiredCache.TryGetValue(weapon, out float v))
            {
                v = StudyHours(weapon.techLevel) * TicksPerHour * ValueFactor(weapon);
                requiredCache[weapon] = v;
            }
            return v;
        }

        public static float QualityFactor(QualityCategory q)
        {
            return 0.5f + 0.25f * (int)q; // Awful 0.5 ... Normal 1.0 ... Legendary 2.0
        }

        public static float IntellectualFactor(Pawn pawn)
        {
            SkillRecord skill = pawn?.skills?.GetSkill(SkillDefOf.Intellectual);
            if (skill == null || skill.TotallyDisabled)
            {
                return IntellectualCurve.Evaluate(0f);
            }
            return IntellectualCurve.Evaluate(skill.Level);
        }

        // Rough "hours left" for the UI, assuming a skill-10 reader.
        public static float HoursLeft(ThingDef weapon, QualityCategory q, float studyFraction)
        {
            float rate = QualityFactor(q) * WBSMod.Settings.studySpeed;
            if (rate <= 0f)
            {
                return float.PositiveInfinity;
            }
            return RequiredStudy(weapon) * (1f - studyFraction) / rate / TicksPerHour;
        }

        public static IEnumerable<ResearchProjectDef> MissingResearch(RecipeDef recipe)
        {
            if (recipe == null)
            {
                yield break;
            }
            if (recipe.researchPrerequisite != null && !recipe.researchPrerequisite.IsFinished)
            {
                yield return recipe.researchPrerequisite;
            }
            if (recipe.researchPrerequisites != null)
            {
                foreach (ResearchProjectDef p in recipe.researchPrerequisites)
                {
                    if (p != null && !p.IsFinished)
                    {
                        yield return p;
                    }
                }
            }
        }

        // --- reverse engineering ---
        public static float ReverseEngineerChance(Pawn pawn, ThingDef weapon)
        {
            WBSSettings s = WBSMod.Settings;
            int crafting = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            int intellectual = pawn?.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            float chance = s.reBaseChance + s.reChancePerSkillLevel * (crafting + intellectual) - s.TechPenalty(weapon?.techLevel ?? TechLevel.Industrial);
            return Mathf.Clamp(chance, s.reMinChance, s.reMaxChance);
        }

        // --- distribution ---
        public static float TechPriceFactor(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Spacer: return 1.5f;
                case TechLevel.Ultra: return 2f;
                case TechLevel.Archotech: return 3f;
                case TechLevel.Industrial: return 1f;
                default: return 0.8f;
            }
        }

        public const float PriceSoftCap = 10000f;

        // Weapon value x tech factor; above 10,000 silver it grows logarithmically, so HSK's
        // 100k+ archotech weapons give expensive but still tradeable blueprints (1.5M raw -> ~60k).
        public static float BlueprintMarketValue(ThingDef weapon)
        {
            float raw = WeaponValue(weapon) * TechPriceFactor(weapon.techLevel);
            if (raw > PriceSoftCap)
            {
                raw = PriceSoftCap * (1f + Mathf.Log(raw / PriceSoftCap));
            }
            return Mathf.Max(80f, raw * WBSMod.Settings.priceMultiplier);
        }

        public static float TechRarityWeight(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Spacer: return 0.25f;
                case TechLevel.Ultra: return 0.1f;
                case TechLevel.Archotech: return 0.03f;
                default: return 1f;
            }
        }

        // How often a weapon's blueprint shows up in stock or loot: cheaper, lower-tech = more common.
        public static float SelectionWeight(ThingDef weapon)
        {
            float valueTerm = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(WeaponValue(weapon), 1f) / 500f), 0.5f, 3f);
            return TechRarityWeight(weapon.techLevel) / valueTerm;
        }

        internal static void ClearCaches()
        {
            requiredCache.Clear();
        }
    }
}
