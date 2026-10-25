using HarmonyLib;
using UnityEngine;
using Verse;

namespace WeaponBlueprints
{
    public class WBSMod : Mod
    {
        public const string Version = "0.1.0";
        public const string HarmonyId = "sonsalt.hsk.weaponblueprints";
        public static WBSSettings Settings;
        public static Harmony Harmony;

        public WBSMod(ModContentPack content) : base(content)
        {
            // Settings and patches must be ready before defs load: the blueprint defs are generated
            // in a postfix on DefGenerator.GenerateImpliedDefs_PreResolve.
            Settings = GetSettings<WBSSettings>();
            Harmony = new Harmony(HarmonyId);
            Harmony.PatchAll(typeof(WBSMod).Assembly);
            Log.Message("[Weapon Blueprints] " + Version + " loaded");
        }

        public override string SettingsCategory() => "WBS_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindow(inRect);

        public override void WriteSettings()
        {
            base.WriteSettings();
            WBSStartup.ApplyRuntimeSettings();
        }
    }
}
