using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RelationBasedTrading
{
    public class RelationBasedTradingSettings : ModSettings
    {
        public bool enabled = true;
        public bool refillFilteredStock = true;
        public bool verboseLogging = false;

        // Gradual limits (2.1): see TradingUtility.LimitsFor.
        public float startValue = 150f;          // most expensive item at goodwill -75
        public float valueAtAlly = 6700f;        // ... just below 75; from 75 (Ally) there is no value limit
        public int weaponGoodwill = 25;          // weapons, ammo, explosives from this goodwill
        public float weaponValueFactor = 4f;     // dangerous items count this many times their value
        public bool limitQuality = true;
        public bool limitTech = true;
        public int restockRise = 15;             // goodwill rise since the stock was made that regenerates it

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref refillFilteredStock, "refillFilteredStock", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            Scribe_Values.Look(ref startValue, "startValue", 150f);
            Scribe_Values.Look(ref valueAtAlly, "valueAtAlly", 6700f);
            Scribe_Values.Look(ref weaponGoodwill, "weaponGoodwill", 25);
            Scribe_Values.Look(ref weaponValueFactor, "weaponValueFactor", 4f);
            Scribe_Values.Look(ref limitQuality, "limitQuality", true);
            Scribe_Values.Look(ref limitTech, "limitTech", true);
            Scribe_Values.Look(ref restockRise, "restockRise", 15);
        }

        public void Reset()
        {
            var d = new RelationBasedTradingSettings();
            startValue = d.startValue; valueAtAlly = d.valueAtAlly; weaponGoodwill = d.weaponGoodwill;
            weaponValueFactor = d.weaponValueFactor; limitQuality = d.limitQuality; limitTech = d.limitTech; restockRise = d.restockRise;
        }
    }

    public class RelationBasedTradingMod : Mod
    {
        public static RelationBasedTradingSettings Settings;

        public RelationBasedTradingMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RelationBasedTradingSettings>();
            TradingUtility.HSKActive = ModsConfig.IsActive("skyarkhangel.HSK");

            // PatchAll() without an assembly argument relies on the calling-assembly lookup,
            // which does not work on RimWorld 1.7 (Waylith/CoreCLR). Always pass the assembly.
            var harmony = new Harmony("thelonetec.relationbasedtrading");
            harmony.PatchAll(typeof(RelationBasedTradingMod).Assembly);

            Log.Message("[Relation Based Trading] Patched trader stock generation" + (TradingUtility.HSKActive ? " (HSK mode)." : "."));
        }

        public override string SettingsCategory() => "Relation Based Trading";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            RelationBasedTradingSettings s = Settings;
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.CheckboxLabeled("Enabled", ref s.enabled, "Filter faction trader stock by your goodwill with that faction.");
            list.CheckboxLabeled("Refill filtered stock", ref s.refillFilteredStock,
                "When items are removed, roll the same stock generator again (up to two extra passes) so traders do not arrive half empty.");
            list.CheckboxLabeled("Verbose logging", ref s.verboseLogging, "Log filtered and refilled item counts and every stock update.");
            list.GapLine();
            list.Label("Every limit grows smoothly with goodwill from -75 to 75. Below -75 traders sell nothing; from 75 (Ally) there is no value limit.");
            list.Label($"Most expensive item at goodwill -75: {s.startValue:0} silver");
            s.startValue = Mathf.Round(list.Slider(s.startValue, 25f, 1000f) / 5f) * 5f;
            list.Label($"Most expensive item just below 75: {s.valueAtAlly:0} silver");
            s.valueAtAlly = Mathf.Round(list.Slider(s.valueAtAlly, Mathf.Max(200f, s.startValue), 50000f) / 50f) * 50f;
            list.Label($"Weapons, ammo and explosives from goodwill {s.weaponGoodwill}");
            s.weaponGoodwill = Mathf.RoundToInt(list.Slider(s.weaponGoodwill, -75f, 75f));
            list.Label($"Weapons count {s.weaponValueFactor:0.0} times their value against the limit");
            s.weaponValueFactor = Mathf.Round(list.Slider(s.weaponValueFactor, 1f, 10f) * 2f) / 2f;
            list.Label($"Settlement stock is regenerated when goodwill rose by {s.restockRise} since it was made");
            s.restockRise = Mathf.RoundToInt(list.Slider(s.restockRise, 5f, 50f));
            list.CheckboxLabeled("Limit item quality (higher quality is lowered to the limit)", ref s.limitQuality);
            list.CheckboxLabeled("Limit tech level relative to the faction's own tech", ref s.limitTech);
            if (list.ButtonText("Reset limits to defaults"))
                s.Reset();
            list.GapLine();
            list.Label("Preview (an industrial faction):");
            foreach (int g in new[] { -75, -50, -25, 0, 25, 50, 75 })
            {
                TradeLimits l = TradingUtility.LimitsFor(g, TechLevel.Industrial, s);
                string value = float.IsPositiveInfinity(l.MaxValue) ? "any value" : $"up to {l.MaxValue:0} silver";
                string weapons = !l.DangerAllowed ? "no weapons" : float.IsPositiveInfinity(l.MaxValue) ? "all weapons" : $"weapons up to {l.MaxValue / l.DangerFactor:0}";
                list.Label($"   {g,4}: {value}, {weapons}, quality up to {(s.limitQuality ? l.MaxQuality.ToString() : "any")}, tech up to {(s.limitTech ? l.MaxTech.ToString() : "any")}");
            }
            list.Label("Food, raw materials, animals and plants are always available. Orbital traders and traders without a faction are never filtered.");
            list.End();
        }
    }

    [StaticConstructorOnStartup]
    public static class RelationBasedTradingStartup
    {
        // Runs after all defs are loaded and resolved; the old LongEventHandler call from the
        // Mod constructor could run before that and cache 0 items.
        static RelationBasedTradingStartup()
        {
            TradingUtility.CacheTechLevels();
        }
    }
}
