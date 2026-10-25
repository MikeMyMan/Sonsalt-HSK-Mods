using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WeaponBlueprints
{
    public enum GatingMode { Always, Never }

    // Add to a weapon ThingDef (XML patch) to force it in or out of the blueprint system.
    public class WBS_GatingOverride : DefModExtension
    {
        public GatingMode mode = GatingMode.Always;
    }

    // Which weapons need a blueprint, which recipes make them and where they are crafted.
    // Built once while defs load (see BlueprintDefGenerator).
    public static class WeaponGate
    {
        public static readonly Dictionary<RecipeDef, ThingDef> GatedRecipes = new Dictionary<RecipeDef, ThingDef>();
        public static readonly HashSet<ThingDef> GatedWeapons = new HashSet<ThingDef>();
        public static readonly List<ThingDef> GatedWeaponsList = new List<ThingDef>();
        public static readonly Dictionary<ThingDef, List<ThingDef>> BenchesOf = new Dictionary<ThingDef, List<ThingDef>>();
        public static readonly Dictionary<ThingDef, List<RecipeDef>> RecipesOf = new Dictionary<ThingDef, List<RecipeDef>>();
        public static readonly HashSet<ThingDef> AllBenches = new HashSet<ThingDef>();
        public static readonly Dictionary<ThingDef, ThingDef> BlueprintOf = new Dictionary<ThingDef, ThingDef>();
        public static readonly Dictionary<ThingDef, ThingDef> WeaponOfBlueprint = new Dictionary<ThingDef, ThingDef>();

        public static bool IsGated(ThingDef weapon) => weapon != null && GatedWeapons.Contains(weapon);

        public static bool IsBlueprint(ThingDef def) => def != null && WeaponOfBlueprint.ContainsKey(def);

        public static ThingDef WeaponFor(ThingDef blueprint) =>
            blueprint != null && WeaponOfBlueprint.TryGetValue(blueprint, out ThingDef w) ? w : null;

        // Locked = gated and not yet learned in the current game. Outside a game nothing is locked.
        public static bool IsLocked(ThingDef weapon)
        {
            if (!IsGated(weapon))
            {
                return false;
            }
            WBSGameComponent comp = WBSGameComponent.Instance;
            return comp != null && !comp.IsUnlocked(weapon);
        }

        public static bool IsRecipeLocked(RecipeDef recipe)
        {
            return recipe != null && GatedRecipes.TryGetValue(recipe, out ThingDef weapon) && IsLocked(weapon);
        }

        public static bool ShouldGate(ThingDef def, WBSSettings s)
        {
            if (def == null || !def.IsWeapon)
            {
                return false;
            }
            WBS_GatingOverride ov = def.GetModExtension<WBS_GatingOverride>();
            if (ov != null)
            {
                return ov.mode == GatingMode.Always;
            }
            if (def.techLevel == TechLevel.Undefined || def.techLevel < s.techThreshold)
            {
                return false;
            }
            if (!s.gateSurvivalTools && IsSurvivalTool(def))
            {
                return false;
            }
            if (!s.gateGrenades && IsGrenade(def))
            {
                return false;
            }
            return true;
        }

        // Survival Tools (Lite) marks its tools with a mod extension or its own thingClass.
        public static bool IsSurvivalTool(ThingDef def)
        {
            if (def.thingClass != null && def.thingClass.Name.Contains("SurvivalTool"))
            {
                return true;
            }
            if (def.modExtensions != null)
            {
                foreach (DefModExtension ext in def.modExtensions)
                {
                    if (ext != null && ext.GetType().Name == "SurvivalToolProperties")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsGrenade(ThingDef def)
        {
            if (def.thingCategories != null && def.thingCategories.Any(c => c != null && c.defName == "Grenades"))
            {
                return true;
            }
            return def.weaponTags != null && def.weaponTags.Any(t => t != null && t.IndexOf("Grenade", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static void Clear()
        {
            GatedRecipes.Clear();
            GatedWeapons.Clear();
            GatedWeaponsList.Clear();
            BenchesOf.Clear();
            RecipesOf.Clear();
            AllBenches.Clear();
            BlueprintOf.Clear();
            WeaponOfBlueprint.Clear();
        }

        // Called in the PreResolve postfix: XML is loaded and cross-references resolved, implied
        // Make_X recipes exist. Benches come from recipeUsers plus benches that list the recipe.
        internal static void Build(WBSSettings s)
        {
            Clear();
            Dictionary<RecipeDef, List<ThingDef>> benchesByRecipe = new Dictionary<RecipeDef, List<ThingDef>>();
            foreach (ThingDef thing in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (thing.recipes == null || thing.category != ThingCategory.Building)
                {
                    continue;
                }
                foreach (RecipeDef r in thing.recipes)
                {
                    if (r == null)
                    {
                        continue;
                    }
                    if (!benchesByRecipe.TryGetValue(r, out List<ThingDef> list))
                    {
                        benchesByRecipe[r] = list = new List<ThingDef>();
                    }
                    list.Add(thing);
                }
            }

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                ThingDef weapon = recipe.ProducedThingDef;
                if (weapon == null || !ShouldGate(weapon, s))
                {
                    continue;
                }
                List<ThingDef> benches = new List<ThingDef>();
                if (recipe.recipeUsers != null)
                {
                    benches.AddRange(recipe.recipeUsers.Where(b => b != null && b.category == ThingCategory.Building));
                }
                if (benchesByRecipe.TryGetValue(recipe, out List<ThingDef> listed))
                {
                    benches.AddRange(listed);
                }
                benches = benches.Distinct().ToList();
                if (benches.Count == 0)
                {
                    continue; // recipe sits on no bench (e.g. HSK's leftover recipeMakers)
                }
                GatedRecipes[recipe] = weapon;
                if (GatedWeapons.Add(weapon))
                {
                    GatedWeaponsList.Add(weapon);
                    BenchesOf[weapon] = new List<ThingDef>();
                    RecipesOf[weapon] = new List<RecipeDef>();
                }
                RecipesOf[weapon].Add(recipe);
                foreach (ThingDef b in benches)
                {
                    if (!BenchesOf[weapon].Contains(b))
                    {
                        BenchesOf[weapon].Add(b);
                    }
                    AllBenches.Add(b);
                }
            }
            GatedWeaponsList.SortBy(w => (int)w.techLevel, w => w.defName);
        }
    }
}
