using HarmonyLib;
using RimWorld;
using Verse;

namespace WeaponBlueprints
{
    // Hide locked weapon recipes. Every bill menu (vanilla, Categorized Crafting Bills, Dubs Mint
    // Menus, Better Workbench Management paste) and the AI's "can the player make this" checks use this.
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    public static class Patch_RecipeDef_AvailableNow
    {
        public static void Postfix(RecipeDef __instance, ref bool __result)
        {
            if (__result && WeaponGate.IsRecipeLocked(__instance))
            {
                __result = false;
            }
        }
    }

    // Block bills for locked weapons that already exist (old saves, pasted bills, unfinished guns).
    // Vanilla never re-checks AvailableNow once a bill is placed.
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    public static class Patch_Bill_Production_ShouldDoNow
    {
        public static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (__result && WeaponGate.IsRecipeLocked(__instance.recipe))
            {
                __result = false;
            }
        }
    }
}
