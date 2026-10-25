using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WeaponBlueprints
{
    // "Reverse engineer weapon" bill. The weapon is not consumed by the bill itself; the outcome
    // is rolled when the work is done:
    //   success -> the weapon is learned and used up
    //   failure -> the weapon loses hit points, or is destroyed if too little is left
    public class RecipeWorker_ReverseEngineer : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            // Keep the weapon; Notify_IterationCompleted decides what happens to it.
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            Thing weapon = ingredients?.FirstOrDefault(t => t != null && !t.Destroyed && WeaponGate.IsGated(t.def));
            WBSGameComponent comp = WBSGameComponent.Instance;
            if (weapon == null || comp == null)
            {
                return;
            }
            Resolve(billDoer, weapon, comp);
        }

        // quiet: no messages, letters or skill XP (used by the test harness for statistics).
        public static ReverseEngineerResult Resolve(Pawn billDoer, Thing weapon, WBSGameComponent comp, bool quiet = false)
        {
            WBSSettings s = WBSMod.Settings;
            ThingDef def = weapon.def;
            if (comp.IsUnlocked(def))
            {
                if (!quiet)
                {
                    Messages.Message("WBS_REAlreadyKnown".Translate(def.label.Named("WEAPON")), new LookTargets(weapon), MessageTypeDefOf.NeutralEvent, historical: false);
                }
                return ReverseEngineerResult.AlreadyKnown;
            }

            float chance = StudyBalance.ReverseEngineerChance(billDoer, def);
            if (!quiet)
            {
                billDoer?.skills?.Learn(SkillDefOf.Intellectual, 0.05f * RecipeWorkAmount());
            }
            string pawnName = billDoer?.LabelShort ?? "?";

            if (Rand.Chance(chance))
            {
                comp.Unlock(def, billDoer, UnlockSource.ReverseEngineering, sendLetter: !quiet);
                weapon.Destroy(DestroyMode.Vanish);
                return ReverseEngineerResult.Success;
            }

            float fraction = Rand.Range(s.reDamageMin, s.reDamageMax);
            bool destroyed;
            if (def.useHitPoints)
            {
                int damage = Mathf.Max(1, Mathf.RoundToInt(weapon.MaxHitPoints * fraction));
                destroyed = weapon.HitPoints - damage <= 0;
                if (!destroyed)
                {
                    weapon.HitPoints -= damage;
                }
            }
            else
            {
                destroyed = Rand.Chance(fraction);
            }

            if (destroyed)
            {
                if (!quiet)
                {
                    Messages.Message("WBS_REFailedDestroyed".Translate(pawnName.Named("PAWN"), def.label.Named("WEAPON"), chance.ToStringPercent().Named("CHANCE")),
                        billDoer != null ? new LookTargets(billDoer) : LookTargets.Invalid, MessageTypeDefOf.NegativeEvent);
                }
                weapon.Destroy(DestroyMode.Vanish);
                return ReverseEngineerResult.Destroyed;
            }
            if (!quiet)
            {
                Messages.Message("WBS_REFailedDamaged".Translate(pawnName.Named("PAWN"), def.label.Named("WEAPON"), chance.ToStringPercent().Named("CHANCE")),
                    new LookTargets(weapon), MessageTypeDefOf.NegativeEvent);
            }
            return ReverseEngineerResult.Damaged;
        }

        private static float RecipeWorkAmount() => WBS_DefOf.WBS_ReverseEngineerWeapon?.workAmount ?? WBSMod.Settings.reWorkAmount;
    }

    public enum ReverseEngineerResult { Success, Damaged, Destroyed, AlreadyKnown }

    // Disallowed in the reverse-engineer bill: matches anything that can't be reverse engineered
    // (not gated, or already learned). Checked live, so learned weapons drop out at once.
    // allowedByDefault=true in XML so other filters in the game never disallow it.
    public class SpecialThingFilterWorker_NotReverseEngineerable : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            return t == null || !WeaponGate.IsLocked(t.def);
        }

        public override bool AlwaysMatches(ThingDef def)
        {
            if (!WeaponGate.IsGated(def))
            {
                return true;
            }
            WBSGameComponent comp = WBSGameComponent.Instance;
            return comp != null && comp.IsUnlocked(def);
        }
    }
}
