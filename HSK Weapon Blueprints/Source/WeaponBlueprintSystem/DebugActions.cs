using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace WeaponBlueprints
{
    public static class DebugActions
    {
        private const string Category = "Weapon Blueprints";

        [DebugAction(Category, "Unlock all weapons", allowedGameStates = AllowedGameStates.Playing)]
        private static void UnlockAll()
        {
            WBSGameComponent comp = WBSGameComponent.Instance;
            if (comp == null)
            {
                return;
            }
            foreach (ThingDef w in WeaponGate.GatedWeaponsList)
            {
                comp.Unlock(w, null, UnlockSource.Debug, sendLetter: false);
            }
            Messages.Message("Unlocked " + WeaponGate.GatedWeaponsList.Count + " weapons.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction(Category, "Unlock weapon...", allowedGameStates = AllowedGameStates.Playing)]
        private static void UnlockOne()
        {
            WBSGameComponent comp = WBSGameComponent.Instance;
            if (comp == null)
            {
                return;
            }
            List<DebugMenuOption> list = WeaponGate.GatedWeaponsList.Where(w => !comp.IsUnlocked(w))
                .Select(w => new DebugMenuOption(w.LabelCap + " (" + w.techLevel.ToStringHuman() + ")", DebugMenuOptionMode.Action,
                    () => comp.Unlock(w, null, UnlockSource.Debug)))
                .ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction(Category, "Lock weapon...", allowedGameStates = AllowedGameStates.Playing)]
        private static void LockOne()
        {
            WBSGameComponent comp = WBSGameComponent.Instance;
            if (comp == null)
            {
                return;
            }
            List<DebugMenuOption> list = WeaponGate.GatedWeaponsList.Where(comp.IsUnlocked)
                .Select(w => new DebugMenuOption(w.LabelCap, DebugMenuOptionMode.Action, () => comp.Lock(w)))
                .ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction(Category, "Reset all unlocks", allowedGameStates = AllowedGameStates.Playing)]
        private static void ResetAll()
        {
            WBSGameComponent.Instance?.ResetAll();
        }

        [DebugAction(Category, "Spawn blueprint...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnBlueprint()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (ThingDef weapon in WeaponGate.GatedWeaponsList)
            {
                if (!WeaponGate.BlueprintOf.TryGetValue(weapon, out ThingDef bp))
                {
                    continue;
                }
                list.Add(new DebugMenuOption(weapon.LabelCap + " (" + weapon.techLevel.ToStringHuman() + ")", DebugMenuOptionMode.Tool, delegate
                {
                    Book book = BookUtility.MakeBook(bp, ArtGenerationContext.Outsider, QualityGenerator.Reward);
                    GenPlace.TryPlaceThing(book, UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction(Category, "Log gated weapons", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogGated()
        {
            WBSGameComponent comp = WBSGameComponent.Instance;
            IEnumerable<string> lines = WeaponGate.GatedWeaponsList.Select(w =>
                $"{w.defName} [{w.techLevel}] value {w.BaseMarketValue:0} -> blueprint {WeaponGate.BlueprintOf.GetValueOrDefault(w)?.BaseMarketValue:0}, " +
                $"study {StudyBalance.RequiredStudy(w) / StudyBalance.TicksPerHour:0.#} h, benches {string.Join(",", WeaponGate.BenchesOf[w].Select(b => b.defName))}" +
                (comp != null ? (comp.IsUnlocked(w) ? " UNLOCKED" : $" {comp.GetProgressFraction(w):P0}") : ""));
            Log.Message("[Weapon Blueprints] Gated weapons:\n" + string.Join("\n", lines));
        }
    }
}
