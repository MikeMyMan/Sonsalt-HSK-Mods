using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WeaponBlueprints
{
    // Value-only fields: this is read inside the def-generation postfix, before any defs are resolved.
    public class WBSSettings : ModSettings
    {
        // --- need a restart (used while defs are generated) ---
        public TechLevel techThreshold = TechLevel.Industrial;
        public bool gateGrenades = true;
        public bool gateSurvivalTools = false;
        public bool studyBeforeResearch = true;

        // --- blueprints ---
        public float priceMultiplier = 1f;
        public float studySpeed = 1f;
        public bool tradersSellBlueprints = true;
        public int traderMin = 1;
        public int traderMax = 3;
        public int orbitalMin = 1;
        public int orbitalMax = 2;
        public bool rewardsAndLoot = true;

        // --- reverse engineering ---
        public int reWorkAmount = 4000;
        public float reBaseChance = 0.05f;
        public float reChancePerSkillLevel = 0.0225f;
        public float reMinChance = 0.05f;
        public float reMaxChance = 0.95f;
        public float rePenaltySpacer = 0.15f;
        public float rePenaltyUltra = 0.25f;
        public float rePenaltyArchotech = 0.40f;
        public float reDamageMin = 0.20f;
        public float reDamageMax = 0.60f;

        private Vector2 scroll;
        private float viewHeight = 1200f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref techThreshold, "techThreshold", TechLevel.Industrial);
            Scribe_Values.Look(ref gateGrenades, "gateGrenades", true);
            Scribe_Values.Look(ref gateSurvivalTools, "gateSurvivalTools", false);
            Scribe_Values.Look(ref studyBeforeResearch, "studyBeforeResearch", true);
            Scribe_Values.Look(ref priceMultiplier, "priceMultiplier", 1f);
            Scribe_Values.Look(ref studySpeed, "studySpeed", 1f);
            Scribe_Values.Look(ref tradersSellBlueprints, "tradersSellBlueprints", true);
            Scribe_Values.Look(ref traderMin, "traderMin", 1);
            Scribe_Values.Look(ref traderMax, "traderMax", 3);
            Scribe_Values.Look(ref orbitalMin, "orbitalMin", 1);
            Scribe_Values.Look(ref orbitalMax, "orbitalMax", 2);
            Scribe_Values.Look(ref rewardsAndLoot, "rewardsAndLoot", true);
            Scribe_Values.Look(ref reWorkAmount, "reWorkAmount", 4000);
            Scribe_Values.Look(ref reBaseChance, "reBaseChance", 0.05f);
            Scribe_Values.Look(ref reChancePerSkillLevel, "reChancePerSkillLevel", 0.0225f);
            Scribe_Values.Look(ref reMinChance, "reMinChance", 0.05f);
            Scribe_Values.Look(ref reMaxChance, "reMaxChance", 0.95f);
            Scribe_Values.Look(ref rePenaltySpacer, "rePenaltySpacer", 0.15f);
            Scribe_Values.Look(ref rePenaltyUltra, "rePenaltyUltra", 0.25f);
            Scribe_Values.Look(ref rePenaltyArchotech, "rePenaltyArchotech", 0.40f);
            Scribe_Values.Look(ref reDamageMin, "reDamageMin", 0.20f);
            Scribe_Values.Look(ref reDamageMax, "reDamageMax", 0.60f);
        }

        public float TechPenalty(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Spacer: return rePenaltySpacer;
                case TechLevel.Ultra: return rePenaltyUltra;
                case TechLevel.Archotech: return rePenaltyArchotech;
                default: return 0f;
            }
        }

        public void DoWindow(Rect inRect)
        {
            Rect view = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            Listing_Standard l = new Listing_Standard();
            l.Begin(view);

            l.Label(("WBS_Settings_RestartHeader".Translate()).Colorize(ColoredText.TipSectionTitleColor));
            if (l.ButtonTextLabeled("WBS_Settings_Threshold".Translate(), techThreshold.ToStringHuman().CapitalizeFirst()))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (TechLevel t in new[] { TechLevel.Neolithic, TechLevel.Medieval, TechLevel.Industrial, TechLevel.Spacer, TechLevel.Ultra, TechLevel.Archotech })
                {
                    TechLevel local = t;
                    opts.Add(new FloatMenuOption(t.ToStringHuman().CapitalizeFirst(), () => techThreshold = local));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            l.CheckboxLabeled("WBS_Settings_GateGrenades".Translate(), ref gateGrenades);
            l.CheckboxLabeled("WBS_Settings_GateTools".Translate(), ref gateSurvivalTools);
            l.CheckboxLabeled("WBS_Settings_StudyFirst".Translate(), ref studyBeforeResearch, "WBS_Settings_StudyFirstTip".Translate());
            priceMultiplier = Slider(l, "WBS_Settings_Price".Translate(), priceMultiplier, 0.25f, 4f, priceMultiplier.ToStringPercent());
            l.GapLine();

            l.Label(("WBS_Settings_BlueprintHeader".Translate()).Colorize(ColoredText.TipSectionTitleColor));
            studySpeed = Slider(l, "WBS_Settings_StudySpeed".Translate(), studySpeed, 0.1f, 5f, studySpeed.ToStringPercent());
            l.CheckboxLabeled("WBS_Settings_Traders".Translate(), ref tradersSellBlueprints);
            traderMin = (int)Slider(l, "WBS_Settings_TraderMin".Translate(), traderMin, 0, 10, traderMin.ToString());
            traderMax = Mathf.Max(traderMin, (int)Slider(l, "WBS_Settings_TraderMax".Translate(), traderMax, 0, 10, traderMax.ToString()));
            orbitalMin = (int)Slider(l, "WBS_Settings_OrbitalMin".Translate(), orbitalMin, 0, 10, orbitalMin.ToString());
            orbitalMax = Mathf.Max(orbitalMin, (int)Slider(l, "WBS_Settings_OrbitalMax".Translate(), orbitalMax, 0, 10, orbitalMax.ToString()));
            l.CheckboxLabeled("WBS_Settings_Rewards".Translate(), ref rewardsAndLoot);
            l.GapLine();

            l.Label(("WBS_Settings_REHeader".Translate()).Colorize(ColoredText.TipSectionTitleColor));
            reWorkAmount = (int)(Slider(l, "WBS_Settings_REWork".Translate(), reWorkAmount, 500, 20000, reWorkAmount.ToString()) / 100f) * 100;
            reBaseChance = Slider(l, "WBS_Settings_REBase".Translate(), reBaseChance, 0f, 0.5f, reBaseChance.ToStringPercent());
            reChancePerSkillLevel = Slider(l, "WBS_Settings_REPerSkill".Translate(), reChancePerSkillLevel, 0f, 0.05f, reChancePerSkillLevel.ToStringPercent("0.00"));
            reMinChance = Slider(l, "WBS_Settings_REMin".Translate(), reMinChance, 0f, 0.5f, reMinChance.ToStringPercent());
            reMaxChance = Mathf.Max(reMinChance, Slider(l, "WBS_Settings_REMax".Translate(), reMaxChance, 0.5f, 1f, reMaxChance.ToStringPercent()));
            rePenaltySpacer = Slider(l, "WBS_Settings_REPenaltySpacer".Translate(), rePenaltySpacer, 0f, 0.8f, rePenaltySpacer.ToStringPercent());
            rePenaltyUltra = Slider(l, "WBS_Settings_REPenaltyUltra".Translate(), rePenaltyUltra, 0f, 0.8f, rePenaltyUltra.ToStringPercent());
            rePenaltyArchotech = Slider(l, "WBS_Settings_REPenaltyArchotech".Translate(), rePenaltyArchotech, 0f, 0.9f, rePenaltyArchotech.ToStringPercent());
            reDamageMin = Slider(l, "WBS_Settings_REDamageMin".Translate(), reDamageMin, 0f, 1f, reDamageMin.ToStringPercent());
            reDamageMax = Mathf.Max(reDamageMin, Slider(l, "WBS_Settings_REDamageMax".Translate(), reDamageMax, 0f, 1f, reDamageMax.ToStringPercent()));
            l.Gap();
            if (l.ButtonText("WBS_Settings_Reset".Translate()))
            {
                ResetToDefaults();
            }

            viewHeight = l.CurHeight + 20f;
            l.End();
            Widgets.EndScrollView();
        }

        private static float Slider(Listing_Standard l, string label, float value, float min, float max, string valueText)
        {
            return l.SliderLabeled(label + ": " + valueText, value, min, max, 0.6f);
        }

        private void ResetToDefaults()
        {
            WBSSettings d = new WBSSettings();
            techThreshold = d.techThreshold; gateGrenades = d.gateGrenades; gateSurvivalTools = d.gateSurvivalTools;
            studyBeforeResearch = d.studyBeforeResearch; priceMultiplier = d.priceMultiplier; studySpeed = d.studySpeed;
            tradersSellBlueprints = d.tradersSellBlueprints; traderMin = d.traderMin; traderMax = d.traderMax;
            orbitalMin = d.orbitalMin; orbitalMax = d.orbitalMax; rewardsAndLoot = d.rewardsAndLoot;
            reWorkAmount = d.reWorkAmount; reBaseChance = d.reBaseChance; reChancePerSkillLevel = d.reChancePerSkillLevel;
            reMinChance = d.reMinChance; reMaxChance = d.reMaxChance; rePenaltySpacer = d.rePenaltySpacer;
            rePenaltyUltra = d.rePenaltyUltra; rePenaltyArchotech = d.rePenaltyArchotech;
            reDamageMin = d.reDamageMin; reDamageMax = d.reDamageMax;
        }
    }
}
