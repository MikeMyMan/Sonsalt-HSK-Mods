using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace WeaponBlueprints
{
    // Book outcome for a blueprint: reading it adds study progress toward its weapon.
    public class BookOutcomeProperties_Blueprint : BookOutcomeProperties
    {
        public ThingDef weapon;

        public override Type DoerClass => typeof(BookOutcomeDoer_Blueprint);
    }

    public class BookOutcomeDoer_Blueprint : BookOutcomeDoer
    {
        public new BookOutcomeProperties_Blueprint Props => (BookOutcomeProperties_Blueprint)props;

        // Null for the hidden template def.
        public ThingDef Weapon => Props?.weapon;

        public override bool DoesProvidesOutcome(Pawn reader)
        {
            return WeaponGate.IsLocked(Weapon);
        }

        public override void OnReadingTick(Pawn reader, float factor)
        {
            ThingDef weapon = Weapon;
            WBSGameComponent comp = WBSGameComponent.Instance;
            if (weapon == null || comp == null || !WeaponGate.IsGated(weapon) || comp.IsUnlocked(weapon))
            {
                return;
            }
            float amount = factor * StudyBalance.QualityFactor(Quality) * StudyBalance.IntellectualFactor(reader) * WBSMod.Settings.studySpeed;
            comp.AddProgress(weapon, amount, reader);
        }

        public override bool BenefitDetailsCanChange(Pawn reader = null) => true;

        public override string GetBenefitsString(Pawn reader = null)
        {
            ThingDef weapon = Weapon;
            if (weapon == null)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder();
            WBSGameComponent comp = WBSGameComponent.Instance;
            string teaches = "WBS_BenefitTeaches".Translate(weapon.LabelCap.Named("WEAPON"), weapon.techLevel.ToStringHuman().Named("TECH"));
            if (!WeaponGate.IsGated(weapon))
            {
                sb.AppendLine((" - " + teaches + " (" + "WBS_BenefitNotNeeded".Translate() + ")").Colorize(Color.gray));
                return sb.ToString().TrimEndNewlines();
            }
            if (comp != null && comp.IsUnlocked(weapon))
            {
                sb.AppendLine((" - " + teaches + " (" + "WBS_BenefitKnown".Translate() + ")").Colorize(Color.gray));
                return sb.ToString().TrimEndNewlines();
            }
            sb.AppendLine(" - " + teaches);
            if (comp != null)
            {
                float fraction = comp.GetProgressFraction(weapon);
                float hours = StudyBalance.HoursLeft(weapon, Quality, fraction);
                sb.AppendLine(" - " + "WBS_BenefitProgress".Translate(fraction.ToStringPercent().Named("PCT"), hours.ToString("0.#").Named("HOURS")));
            }
            else
            {
                float hours = StudyBalance.HoursLeft(weapon, Quality, 0f);
                sb.AppendLine(" - " + "WBS_BenefitStudyTime".Translate(hours.ToString("0.#").Named("HOURS")));
            }
            if (Current.Game != null && WeaponGate.RecipesOf.TryGetValue(weapon, out List<RecipeDef> recipes) && recipes.Count > 0)
            {
                List<ResearchProjectDef> missing = StudyBalance.MissingResearch(recipes[0]).ToList();
                if (missing.Count > 0)
                {
                    sb.AppendLine(" - " + "WBS_BenefitNeedsResearch".Translate(missing.Select(p => p.label).ToCommaList().Named("PROJECTS")));
                }
            }
            return sb.ToString().TrimEndNewlines();
        }

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetHyperlinks()
        {
            if (Weapon != null)
            {
                yield return new Dialog_InfoCard.Hyperlink(Weapon);
            }
        }

        public override IEnumerable<Rule_String> GetTopicRuleStrings()
        {
            ThingDef weapon = Weapon;
            if (weapon == null)
            {
                return null;
            }
            string kind = WeaponGate.IsGrenade(weapon) ? "WBS_KindGrenade".Translate()
                : weapon.IsRangedWeapon ? "WBS_KindRanged".Translate()
                : "WBS_KindMelee".Translate();
            return new List<Rule_String>
            {
                new Rule_String("weapon_label", weapon.label),
                new Rule_String("weapon_kind", kind),
                new Rule_String("weapon_tech", weapon.techLevel.ToStringHuman())
            };
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            ThingDef weapon = Weapon;
            if (DebugSettings.ShowDevGizmos && WeaponGate.IsLocked(weapon))
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Finish blueprint study",
                    action = () => WBSGameComponent.Instance?.Unlock(weapon, null, UnlockSource.Debug)
                };
            }
        }
    }
}
