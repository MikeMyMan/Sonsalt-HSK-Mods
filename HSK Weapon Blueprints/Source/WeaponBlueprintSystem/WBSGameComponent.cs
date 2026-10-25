using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WeaponBlueprints
{
    // Colony-wide (per game) blueprint state: which gated weapons are learned and how far
    // each weapon's blueprint study has come.
    public class WBSGameComponent : GameComponent
    {
        private static WBSGameComponent instance;

        private readonly Game game;
        private HashSet<ThingDef> unlocked = new HashSet<ThingDef>();
        private Dictionary<ThingDef, float> studyProgress = new Dictionary<ThingDef, float>();
        private bool initializedByMod;
        private bool warnedOldSave;

        public WBSGameComponent(Game game)
        {
            this.game = game;
            instance = this;
        }

        public static WBSGameComponent Instance
        {
            get
            {
                Game current = Current.Game;
                if (current == null)
                {
                    return null;
                }
                if (instance == null || instance.game != current)
                {
                    instance = current.GetComponent<WBSGameComponent>();
                }
                return instance;
            }
        }

        public IEnumerable<ThingDef> Unlocked => unlocked;

        public int UnlockedGatedCount => unlocked.Count(WeaponGate.IsGated);

        public bool IsUnlocked(ThingDef weapon) => weapon != null && unlocked.Contains(weapon);

        public float GetProgress(ThingDef weapon) =>
            weapon != null && studyProgress.TryGetValue(weapon, out float p) ? p : 0f;

        public float GetProgressFraction(ThingDef weapon)
        {
            if (IsUnlocked(weapon))
            {
                return 1f;
            }
            float required = StudyBalance.RequiredStudy(weapon);
            return required <= 0f ? 0f : UnityEngine.Mathf.Clamp01(GetProgress(weapon) / required);
        }

        // Returns true when this call finished the study.
        public bool AddProgress(ThingDef weapon, float amount, Pawn reader)
        {
            if (weapon == null || amount <= 0f || IsUnlocked(weapon))
            {
                return false;
            }
            float p = GetProgress(weapon) + amount;
            studyProgress[weapon] = p;
            if (p >= StudyBalance.RequiredStudy(weapon))
            {
                Unlock(weapon, reader, UnlockSource.Blueprint);
                return true;
            }
            return false;
        }

        public void Unlock(ThingDef weapon, Pawn by, UnlockSource source, bool sendLetter = true)
        {
            if (weapon == null || !unlocked.Add(weapon))
            {
                return;
            }
            studyProgress.Remove(weapon);
            if (sendLetter)
            {
                SendUnlockLetter(weapon, by, source);
            }
        }

        public void Lock(ThingDef weapon)
        {
            if (weapon != null)
            {
                unlocked.Remove(weapon);
                studyProgress.Remove(weapon);
            }
        }

        public void ResetAll()
        {
            unlocked.Clear();
            studyProgress.Clear();
        }

        private static void SendUnlockLetter(ThingDef weapon, Pawn by, UnlockSource source)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            string benches = WeaponGate.BenchesOf.TryGetValue(weapon, out List<ThingDef> list)
                ? list.Select(b => b.label).Distinct().ToCommaList()
                : "?";
            string key = source == UnlockSource.ReverseEngineering ? "WBS_LetterReverseEngineered" : "WBS_LetterStudied";
            string pawnName = by != null ? by.LabelShort : "WBS_TheColony".Translate().ToString();
            TaggedString text = key.Translate(pawnName.Named("PAWN"), weapon.label.Named("WEAPON"), benches.Named("BENCHES"));
            StringBuilder missing = new StringBuilder();
            foreach (RecipeDef r in WeaponGate.RecipesOf.TryGetValue(weapon, out List<RecipeDef> rs) ? rs : new List<RecipeDef>())
            {
                foreach (ResearchProjectDef p in StudyBalance.MissingResearch(r))
                {
                    missing.AppendLine(" - " + p.LabelCap);
                }
                break;
            }
            if (missing.Length > 0)
            {
                text += "\n\n" + "WBS_LetterNeedsResearch".Translate() + "\n" + missing.ToString().TrimEndNewlines();
            }
            Find.LetterStack.ReceiveLetter("WBS_LetterLabel".Translate(weapon.LabelCap.Named("WEAPON")), text,
                LetterDefOf.PositiveEvent, by != null && by.Spawned ? new LookTargets(by) : LookTargets.Invalid,
                hyperlinkThingDefs: new List<ThingDef> { weapon });
        }

        public override void StartedNewGame()
        {
            initializedByMod = true;
        }

        public override void LoadedGame()
        {
            if (!initializedByMod && !warnedOldSave)
            {
                warnedOldSave = true;
                Find.LetterStack.ReceiveLetter("WBS_OldSaveLabel".Translate(), "WBS_OldSaveText".Translate(WeaponGate.GatedWeapons.Count.Named("COUNT")),
                    LetterDefOf.NeutralEvent);
            }
            initializedByMod = true;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref unlocked, "wbsUnlocked", LookMode.Def);
            Scribe_Collections.Look(ref studyProgress, "wbsStudyProgress", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref initializedByMod, "wbsInitialized", false);
            Scribe_Values.Look(ref warnedOldSave, "wbsWarnedOldSave", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                unlocked ??= new HashSet<ThingDef>();
                unlocked.Remove(null);
                studyProgress ??= new Dictionary<ThingDef, float>(); // the loader already skips removed defs as keys
            }
        }
    }

    public enum UnlockSource { Blueprint, ReverseEngineering, Debug }
}
