using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// "Only HSK / CE / vanilla items" (HSK dev feedback 2026-10-30: raids should not bring items from outside HSK). When on,
    /// anything the AI hands to raiders or picks for them - kit, crew-served guns, siege guns, swapped weapons, digging
    /// tools - has to come from RimWorld or its DLC, Combat Extended, or the HSK pack. Our own breach and mining charges
    /// count as outside HSK: industrial raids then breach with explosives and tools HSK already has.
    /// HSK ships its bundled mods in folders named "..._SK", "... SK" or "HSK ..." (installed from HSK's repository), and
    /// HSK's own packages are skyarkhangel.*.
    /// </summary>
    public static class ItemSources
    {
        private static readonly Dictionary<ModContentPack, bool> hskOrBase = new Dictionary<ModContentPack, bool>();
        private static readonly Regex SkFolder = new Regex(@"(^|[ _\-])H?SK($|[ _\-])", RegexOptions.IgnoreCase);

        public static bool Restricted => TacticalAIMod.Settings?.hskItemsOnly ?? false;

        /// <summary>May the AI give or pick this def? Always true with the switch off.</summary>
        public static bool Allowed(ThingDef d)
        {
            if (d == null) return false;
            if (!Restricted) return true;
            ModContentPack m = d.modContentPack;
            if (m == null) return true;   // generated defs (minified, blueprints) belong to their source
            if (!hskOrBase.TryGetValue(m, out bool ok))
            {
                hskOrBase[m] = ok = IsHskOrBase(m);
            }
            return ok;
        }

        /// <summary>Our own charges (HSKTAI_BreachCharge, HSKTAI_MiningCharge) may be handed out.</summary>
        public static bool OwnChargesAllowed => !Restricted;

        private static bool IsHskOrBase(ModContentPack m)
        {
            if (m.IsOfficialMod) return true;
            if (m == TacticalAIMod.Content) return false;   // our charges are not HSK items
            string id = m.PackageId ?? "";
            if (id.Contains("combatextended") || id.StartsWith("skyarkhangel")) return true;
            return SkFolder.IsMatch(m.FolderName ?? "");
        }
    }
}
