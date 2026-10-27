using System;
using GasFrameWork;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PalverZGasRework
{
    // Pieces that used to come from PalverZ's Gas Pack, so the mod only needs Custom Gas Framework.

    // ------------------------------------------------------------------ trap implant

    /// <summary>On a trap implant's hediff: when the host dies, the implant ruptures and releases its gas.</summary>
    public class HediffCompProperties_GasReleaseOnDeath : HediffCompProperties
    {
        public GasTypeDef gasType;
        public int gasAmount = 255;
        public float gasRadius = 3f;
        public SoundDef soundDef;

        public HediffCompProperties_GasReleaseOnDeath() { compClass = typeof(HediffComp_GasReleaseOnDeath); }
    }

    public class HediffComp_GasReleaseOnDeath : HediffComp
    {
        public HediffCompProperties_GasReleaseOnDeath Props => (HediffCompProperties_GasReleaseOnDeath)props;

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            Map map = Pawn.MapHeld;
            if (map == null || Props.gasType == null) return;
            IntVec3 cell = Pawn.PositionHeld;
            try
            {
                Props.soundDef?.PlayOneShot(new TargetInfo(cell, map));
                CustomGasUtility.AddGasRadial(cell, map, Props.gasType, Props.gasAmount, Props.gasRadius);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("implant/" + parent.def.defName, $"Trap implant {parent.def.defName} failed to release gas: {e}");
            }
        }
    }

    // ------------------------------------------------------------------ leaking reprocessor (cypherphage meltdown)

    /// <summary>Settings for Hediff_ReprocessorLeak, on its HediffDef.</summary>
    public class ReprocessorLeakExtension : DefModExtension
    {
        public int damageIntervalTicks = 120;
        /// <summary>Damage to the reprocessor per interval: base + severity x perSeverity.</summary>
        public float damageBase = 0.2f;
        public float damagePerSeverity = 1.5f;
        public DamageDef damageDef;
        /// <summary>Ticks between chemfuel drips: base - severity x perSeverity, at least min.</summary>
        public int dripBaseTicks = 100;
        public float dripPerSeverity = 90f;
        public int dripMinTicks = 10;
        public float deathExplosionRadius = 3.9f;
        public int deathExplosionDamage = 40;
        public DamageDef deathExplosionDamageDef;
    }

    /// <summary>A mechanoid's fluid reprocessor eaten through by cypherphage: it drips chemfuel, burns from the
    /// inside until the reprocessor fails, and the mechanoid goes up in flames when it dies.</summary>
    public class Hediff_ReprocessorLeak : HediffWithComps
    {
        private int nextDamageTick = -1;
        private int nextDripTick = -1;
        private BodyPartRecord reprocessor;
        private ReprocessorLeakExtension ext;

        private ReprocessorLeakExtension Ext => ext ?? (ext = def.GetModExtension<ReprocessorLeakExtension>() ?? new ReprocessorLeakExtension());

        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);
            if (pawn.Dead) return;
            int now = Find.TickManager.TicksGame;
            if (reprocessor == null || pawn.health.hediffSet.PartIsMissing(reprocessor)) reprocessor = FindReprocessor();
            if (reprocessor == null)
            {
                // Nothing left to leak from: the reprocessor has failed.
                pawn.Kill(new DamageInfo(Ext.deathExplosionDamageDef ?? DamageDefOf.Flame, Ext.deathExplosionDamage, 0f, -1f, pawn));
                return;
            }
            if (nextDamageTick < 0) nextDamageTick = now + Ext.damageIntervalTicks;
            if (now >= nextDamageTick)
            {
                nextDamageTick = now + Ext.damageIntervalTicks;
                float amount = Ext.damageBase + Severity * Ext.damagePerSeverity;
                // Burns from the inside: armor plating doesn't help (CE armor stopped every hit otherwise).
                var dinfo = new DamageInfo(Ext.damageDef ?? DamageDefOf.Burn, amount, 1f, -1f, pawn, reprocessor);
                dinfo.SetIgnoreArmor(true);
                pawn.TakeDamage(dinfo);
                if (pawn.Dead) return;
            }
            if (nextDripTick < 0) nextDripTick = now + DripInterval();
            if (now >= nextDripTick)
            {
                nextDripTick = now + DripInterval();
                if (pawn.Spawned) FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, ThingDefOf.Filth_Fuel);
            }
        }

        private int DripInterval() => Mathf.Max(Ext.dripMinTicks, Mathf.RoundToInt(Ext.dripBaseTicks - Severity * Ext.dripPerSeverity));

        private BodyPartRecord FindReprocessor()
        {
            foreach (BodyPartRecord part in pawn.RaceProps.body.GetPartsWithTag(BodyPartTagDefOf.BloodFiltrationSource))
                if (!pawn.health.hediffSet.PartIsMissing(part)) return part;
            return null;
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            Map map = pawn.MapHeld;
            if (map == null) return;
            GenExplosion.DoExplosion(pawn.PositionHeld, map, Ext.deathExplosionRadius, Ext.deathExplosionDamageDef ?? DamageDefOf.Flame,
                pawn, Ext.deathExplosionDamage);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextDamageTick, "nextDamageTick", -1);
            Scribe_Values.Look(ref nextDripTick, "nextDripTick", -1);
        }
    }
}
