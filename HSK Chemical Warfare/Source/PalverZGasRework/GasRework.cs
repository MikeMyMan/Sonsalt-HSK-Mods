using System.Collections.Generic;
using GasFrameWork;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PalverZGasRework
{
    [StaticConstructorOnStartup]
    public static class GasReworkStartup
    {
        static GasReworkStartup()
        {
            new Harmony("sonsalt.hsk.chemicalwarfare").PatchAll();
        }
    }

    internal static class GasLog
    {
        private static readonly HashSet<string> reported = new HashSet<string>();

        // Gas effects run twice a second for every pawn, so each distinct problem is logged once.
        public static void ErrorOnce(string key, string text)
        {
            if (reported.Add(key)) Log.Error("[Gas Rework] " + text);
        }
    }

    public enum GasTargets { Any, Biological, Mechanoid }

    public enum GasIff { Any, HostileToPlayer, NotHostileToPlayer }

    /// <summary>How an agent gets in, and so what stops it.
    /// Inhaled: a gas mask (or any sealed headgear) stops it completely.
    /// Skin: only a sealed suit stops it; masks and ordinary clothes don't.
    /// InhaledAndSkin: skinShare of the dose comes through the skin, the rest through the lungs.
    /// None: no protection (friend-or-foe recognition, physical cuts).</summary>
    public enum GasRoute { Inhaled, Skin, InhaledAndSkin, None }

    /// <summary>What counts as a sealed suit: one worn garment that covers every listed body part
    /// group and gives toxic environment resistance (like HSK's chemical protection suit).</summary>
    public class SealedSuitSettings
    {
        public List<BodyPartGroupDef> requiredCoverage = new List<BodyPartGroupDef>();
        public float minToxicEnvironmentResistance = 0.01f;
        public List<ThingDef> alwaysSealed = new List<ThingDef>();
    }

    public static class GasPawnUtility
    {
        public static bool Matches(Pawn pawn, GasTargets targets)
        {
            switch (targets)
            {
                case GasTargets.Biological: return pawn.RaceProps.IsFlesh;
                case GasTargets.Mechanoid: return pawn.RaceProps.IsMechanoid;
                default: return true;
            }
        }

        public static bool Matches(Pawn pawn, GasIff iff)
        {
            if (iff == GasIff.Any) return true;
            bool hostile = pawn.HostileTo(Faction.OfPlayer);
            return iff == GasIff.HostileToPlayer ? hostile : !hostile;
        }

        public static bool WearsSealedSuit(Pawn pawn, SealedSuitSettings settings)
        {
            if (settings == null || pawn.apparel == null) return false;
            List<BodyPartGroupDef> coverage = settings.requiredCoverage;
            if (coverage.NullOrEmpty()) coverage = DefaultCoverage;
            StatDef toxEnv = StatDefOf.ToxicEnvironmentResistance;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                ThingDef def = worn[i].def;
                if (settings.alwaysSealed != null && settings.alwaysSealed.Contains(def)) return true;
                List<BodyPartGroupDef> groups = def.apparel?.bodyPartGroups;
                if (groups == null) continue;
                bool coversAll = true;
                for (int g = 0; g < coverage.Count; g++)
                {
                    if (!groups.Contains(coverage[g])) { coversAll = false; break; }
                }
                if (!coversAll) continue;
                if (toxEnv == null) return true;
                if (def.equippedStatOffsets != null && def.equippedStatOffsets.GetStatOffsetFromList(toxEnv) >= settings.minToxicEnvironmentResistance)
                    return true;
            }
            return false;
        }

        private static List<BodyPartGroupDef> defaultCoverage;
        private static List<BodyPartGroupDef> DefaultCoverage =>
            defaultCoverage ?? (defaultCoverage = new List<BodyPartGroupDef> { BodyPartGroupDefOf.Torso, BodyPartGroupDefOf.Legs });

        // ------------------------------------------------------------ protection

        private static readonly Dictionary<ThingDef, bool> sealedGear = new Dictionary<ThingDef, bool>();
        private static List<BodyPartGroupDef> respiratorGroups;

        /// <summary>Apparel rated against toxic environments: gas masks, sealed helmets, chemical suits.</summary>
        public static bool IsSealedGear(ThingDef def)
        {
            if (sealedGear.TryGetValue(def, out bool result)) return result;
            StatDef toxEnv = StatDefOf.ToxicEnvironmentResistance;
            result = def.apparel != null && toxEnv != null && def.equippedStatOffsets != null
                     && def.equippedStatOffsets.GetStatOffsetFromList(toxEnv) > 0f;
            sealedGear[def] = result;
            return result;
        }

        private static List<BodyPartGroupDef> RespiratorGroups
        {
            get
            {
                if (respiratorGroups != null) return respiratorGroups;
                respiratorGroups = new List<BodyPartGroupDef>();
                foreach (string n in new[] { "FullHead", "Mouth", "Teeth" })
                {
                    BodyPartGroupDef g = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(n);
                    if (g != null) respiratorGroups.Add(g);
                }
                return respiratorGroups;
            }
        }

        /// <summary>Sealed headgear over the mouth: a gas mask, a sealed helmet, a hood.</summary>
        public static bool WearsRespirator(Pawn pawn)
        {
            if (pawn.apparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                ThingDef def = worn[i].def;
                if (!IsSealedGear(def)) continue;
                List<BodyPartGroupDef> groups = def.apparel.bodyPartGroups;
                if (groups == null) continue;
                for (int g = 0; g < RespiratorGroups.Count; g++)
                {
                    if (groups.Contains(RespiratorGroups[g])) return true;
                }
            }
            return false;
        }

        /// <summary>The body's own resistance (genes, implants, drugs), without anything worn.</summary>
        public static float OwnResistance(Pawn pawn)
        {
            float tox = Mathf.Clamp01(pawn.GetStatValue(StatDefOf.ToxicResistance));
            float env = 0f;
            StatDef toxEnv = StatDefOf.ToxicEnvironmentResistance;
            if (toxEnv != null && ModsConfig.BiotechActive)
            {
                env = pawn.GetStatValue(toxEnv);
                if (pawn.apparel != null)
                {
                    List<Apparel> worn = pawn.apparel.WornApparel;
                    for (int i = 0; i < worn.Count; i++)
                        if (worn[i].def.equippedStatOffsets != null) env -= worn[i].def.equippedStatOffsets.GetStatOffsetFromList(toxEnv);
                }
                env = Mathf.Clamp01(env);
            }
            return 1f - (1f - tox) * (1f - env);
        }

        /// <summary>Share of a dose that reaches the body by this route.</summary>
        public static float ExposureFactor(Pawn pawn, GasRoute route, float skinShare, bool ownResistanceReduces, SealedSuitSettings suit)
        {
            float f;
            switch (route)
            {
                case GasRoute.Inhaled:
                    f = WearsRespirator(pawn) ? 0f : 1f;
                    break;
                case GasRoute.Skin:
                    f = WearsSealedSuit(pawn, suit ?? DefaultSuit) ? 0f : 1f;
                    break;
                case GasRoute.InhaledAndSkin:
                    f = (1f - skinShare) * (WearsRespirator(pawn) ? 0f : 1f) + skinShare * (WearsSealedSuit(pawn, suit ?? DefaultSuit) ? 0f : 1f);
                    break;
                default:
                    f = 1f;
                    break;
            }
            if (f > 0f && ownResistanceReduces) f *= 1f - OwnResistance(pawn);
            return f;
        }

        /// <summary>A full protective set: sealed suit plus sealed headgear.</summary>
        public static bool FullyProtected(Pawn pawn, SealedSuitSettings suit) => WearsRespirator(pawn) && WearsSealedSuit(pawn, suit ?? DefaultSuit);

        private static SealedSuitSettings defaultSuit;
        public static SealedSuitSettings DefaultSuit => defaultSuit ?? (defaultSuit = new SealedSuitSettings());

        private static readonly List<BodyPartRecord> tmpParts = new List<BodyPartRecord>();

        /// <summary>A random outer body part. onlyUncovered skips parts under any clothing; onlyUnsealed
        /// skips only parts under sealed gear (acid goes straight through ordinary clothes).</summary>
        public static BodyPartRecord RandomOuterPart(Pawn pawn, bool onlyUncovered, List<BodyPartDef> excluded, bool onlyUnsealed = false)
        {
            tmpParts.Clear();
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.depth != BodyPartDepth.Outside || part.coverageAbs <= 0f) continue;
                if (excluded != null && excluded.Contains(part.def)) continue;
                if (onlyUncovered && IsCovered(pawn, part, false)) continue;
                if (onlyUnsealed && IsCovered(pawn, part, true)) continue;
                tmpParts.Add(part);
            }
            BodyPartRecord result = tmpParts.RandomElementByWeightWithFallback(p => p.coverageAbs);
            tmpParts.Clear();
            return result;
        }

        /// <summary>A random part whose def is in the list (organs, at any depth), weighted by size.</summary>
        public static BodyPartRecord RandomPartOf(Pawn pawn, List<BodyPartDef> defs)
        {
            tmpParts.Clear();
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
                if (defs.Contains(part.def)) tmpParts.Add(part);
            BodyPartRecord result = tmpParts.RandomElementByWeightWithFallback(p => p.coverageAbsWithChildren + 0.01f);
            tmpParts.Clear();
            return result;
        }

        private static bool IsCovered(Pawn pawn, BodyPartRecord part, bool sealedOnly)
        {
            if (pawn.apparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                ThingDef def = worn[i].def;
                if (def.apparel == null || (sealedOnly && !IsSealedGear(def))) continue;
                if (def.apparel.CoversBodyPart(part)) return true;
            }
            return false;
        }

        public static void Text(Pawn pawn, string text, Color color)
        {
            if (pawn.Spawned && !text.NullOrEmpty()) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, text, color, 3.5f);
        }
    }
}
