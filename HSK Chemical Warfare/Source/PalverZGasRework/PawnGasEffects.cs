using System;
using System.Collections.Generic;
using GasFrameWork;
using RimWorld;
using UnityEngine;
using Verse;

namespace PalverZGasRework
{
    /// <summary>Shared filters for effects that act on pawns standing in the gas.
    /// The framework calls AffectPawn every 30 ticks for every spawned pawn in the cloud.</summary>
    public abstract class PawnGasEffectProps : GasEffectProperties
    {
        public GasTargets targets = GasTargets.Any;
        public GasIff iff = GasIff.Any;
        public int minDensity;
        public bool affectDowned = true;
        /// <summary>When set, pawns in a sealed suit are immune to this effect.</summary>
        public SealedSuitSettings blockedBySealedSuit;
    }

    public abstract class PawnGasEffectWorker : GasEffectWorker
    {
        public override void AffectPawn(Pawn pawn, float densityPercent, byte density)
        {
            if (!(props is PawnGasEffectProps p)) return;
            if (density < p.minDensity) return;
            if (!GasPawnUtility.Matches(pawn, p.targets) || !GasPawnUtility.Matches(pawn, p.iff)) return;
            if (!p.affectDowned && pawn.Downed) return;
            if (p.blockedBySealedSuit != null && GasPawnUtility.WearsSealedSuit(pawn, p.blockedBySealedSuit)) return;
            try
            {
                Apply(pawn, densityPercent, density);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce(GetType().Name + "/" + gas?.defName, $"{GetType().Name} in {gas?.defName} failed on {pawn}: {e}");
            }
        }

        protected abstract void Apply(Pawn pawn, float densityPercent, byte density);
    }

    // ------------------------------------------------------------------ hediff

    public class GasEffect_Hediff : PawnGasEffectProps
    {
        public HediffDef hediff;
        /// <summary>Severity added per hit (two hits per second) at full density.</summary>
        public FloatRange severityPerHit = new FloatRange(0.05f, 0.05f);
        public bool scaleWithDensity = true;
        /// <summary>How the agent gets in; decides whether a gas mask, a sealed suit or both stop it.</summary>
        public GasRoute route = GasRoute.Inhaled;
        /// <summary>For InhaledAndSkin: share of the dose that soaks through the skin.</summary>
        public float skinShare = 0.3f;
        /// <summary>The body's own toxic resistance (genes, implants) reduces the dose.</summary>
        public bool toxicResistanceReduces = true;
        /// <summary>Only applies to pawns that already carry this hediff at this severity or more.</summary>
        public HediffDef requiresHediff;
        public float requiresSeverity;
        /// <summary>Dose by body mass: amount x (1 / body size)^exponent, capped at maxBodySizeFactor.
        /// A rat (0.2) gets 25 times a person's dose at exponent 2; a muffalo (2.4) a sixth. 0 = off.</summary>
        public float bodySizeExponent;
        public float maxBodySizeFactor = 50f;
        /// <summary>Extra factor for insectoids.</summary>
        public float insectoidFactor = 1f;

        public GasEffect_Hediff() { workerClass = typeof(GasEffectWorker_Hediff); }
    }

    public class GasEffectWorker_Hediff : PawnGasEffectWorker
    {
        protected override void Apply(Pawn pawn, float densityPercent, byte density)
        {
            var p = (GasEffect_Hediff)props;
            if (p.hediff == null) return;
            if (p.requiresHediff != null)
            {
                Hediff required = pawn.health.hediffSet.GetFirstHediffOfDef(p.requiresHediff);
                if (required == null || required.Severity < p.requiresSeverity) return;
            }
            float amount = p.severityPerHit.RandomInRange;
            if (p.scaleWithDensity) amount *= densityPercent;
            if (p.bodySizeExponent != 0f)
                amount *= Mathf.Min(p.maxBodySizeFactor, Mathf.Pow(1f / Mathf.Max(0.05f, pawn.BodySize), p.bodySizeExponent));
            if (p.insectoidFactor != 1f && pawn.RaceProps.Insect) amount *= p.insectoidFactor;
            amount *= GasPawnUtility.ExposureFactor(pawn, p.route, p.skinShare, p.toxicResistanceReduces, p.blockedBySealedSuit);
            if (amount <= 0f) return;
            HealthUtility.AdjustSeverity(pawn, p.hediff, amount);
            if (pawn.Dead) return;
            GasExposure.Notify(pawn.health.hediffSet.GetFirstHediffOfDef(p.hediff));
        }
    }

    // ------------------------------------------------------------------ damage

    public class GasEffect_Damage : PawnGasEffectProps
    {
        public DamageDef damageDef;
        public FloatRange damagePerHit = new FloatRange(1f, 1f);
        public bool scaleWithDensity = true;
        public float armorPenetration;
        public bool ignoreArmor;
        public bool onlyUncoveredParts;
        /// <summary>Acid: ordinary clothes don't help, sealed gear does. Hits only parts not under sealed
        /// gear (a mask shields the head, a chemical suit the body); a full set (suit plus mask) stops it.</summary>
        public bool sealedGearProtects;
        public List<BodyPartDef> excludedParts = new List<BodyPartDef>();
        /// <summary>Hit only these parts, at any depth (aegis drones reaching the organs). Empty = outer parts.</summary>
        public List<BodyPartDef> onlyParts = new List<BodyPartDef>();
        /// <summary>Chance per hit (two hits per second).</summary>
        public float chance = 1f;

        public GasEffect_Damage() { workerClass = typeof(GasEffectWorker_Damage); }
    }

    public class GasEffectWorker_Damage : PawnGasEffectWorker
    {
        protected override void Apply(Pawn pawn, float densityPercent, byte density)
        {
            var p = (GasEffect_Damage)props;
            if (p.damageDef == null) return;
            if (p.chance < 1f && !Rand.Chance(p.chance)) return;
            float amount = p.damagePerHit.RandomInRange;
            if (p.scaleWithDensity) amount *= densityPercent;
            if (amount < 0.05f) return;
            if (p.sealedGearProtects && GasPawnUtility.FullyProtected(pawn, p.blockedBySealedSuit)) return;
            BodyPartRecord part = p.onlyParts.NullOrEmpty()
                ? GasPawnUtility.RandomOuterPart(pawn, p.onlyUncoveredParts, p.excludedParts, p.sealedGearProtects)
                : GasPawnUtility.RandomPartOf(pawn, p.onlyParts);
            if (part == null) return;
            var dinfo = new DamageInfo(p.damageDef, amount, p.armorPenetration, -1f, null, part);
            if (p.ignoreArmor) dinfo.SetIgnoreArmor(true);
            pawn.TakeDamage(dinfo);
        }
    }

    // ------------------------------------------------------------------ gear-rot

    /// <summary>Strips a share of every worn item's hit points per hit; gear at 0 HP falls apart.</summary>
    public class GasEffect_GearRot : PawnGasEffectProps
    {
        /// <summary>Share of max hit points lost per hit (two hits per second).</summary>
        public FloatRange hitPointFractionPerHit = new FloatRange(0.01f, 0.025f);
        public bool includeEquipment;

        public GasEffect_GearRot()
        {
            workerClass = typeof(GasEffectWorker_GearRot);
            minDensity = 100;
        }
    }

    public class GasEffectWorker_GearRot : PawnGasEffectWorker
    {
        protected override void Apply(Pawn pawn, float densityPercent, byte density)
        {
            var p = (GasEffect_GearRot)props;
            if (pawn.apparel != null)
            {
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int i = worn.Count - 1; i >= 0; i--)
                {
                    if (i < worn.Count) Corrode(pawn, worn[i], p);
                }
            }
            if (p.includeEquipment && pawn.equipment?.Primary != null) Corrode(pawn, pawn.equipment.Primary, p);
        }

        private static void Corrode(Pawn pawn, Thing thing, GasEffect_GearRot p)
        {
            if (thing == null || thing.Destroyed || !thing.def.useHitPoints) return;
            int loss = Mathf.Max(1, Mathf.RoundToInt(thing.MaxHitPoints * p.hitPointFractionPerHit.RandomInRange));
            thing.HitPoints -= loss;
            if (thing.HitPoints > 0) return;
            string label = thing.LabelNoCount;
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message("PGR_GearRotDestroyed".Translate(pawn.LabelShort, label, pawn.Named("PAWN")).Resolve(), pawn, MessageTypeDefOf.NegativeEvent);
            else
                GasPawnUtility.Text(pawn, "PGR_GearRotMote".Translate(label).Resolve(), new Color(0.9f, 0.55f, 0.2f));
            thing.Destroy();
        }
    }

    // ------------------------------------------------------------------ hediff comp hook

    public interface IGasExposureListener
    {
        void Notify_GasExposure();
    }

    public static class GasExposure
    {
        public static void Notify(Hediff hediff)
        {
            if (!(hediff is HediffWithComps withComps) || withComps.comps == null) return;
            for (int i = 0; i < withComps.comps.Count; i++)
            {
                if (withComps.comps[i] is IGasExposureListener listener) listener.Notify_GasExposure();
            }
        }
    }
}
