using RimWorld;
using Verse;

namespace PalverZGasRework
{
    /// <summary>Settings for Hediff_InternalBleeding, on its HediffDef.</summary>
    public class InternalBleedingExtension : DefModExtension
    {
        /// <summary>Share of the blood lost per day at severity 1, before blood thinners multiply it.</summary>
        public float bleedRatePerSeverity = 1.5f;
        /// <summary>A tend cuts bleeding by this share times tend quality (internal bleeding can't be closed like a cut).</summary>
        public float tendReduction = 0.8f;
    }

    /// <summary>Bleeding with no wound to close: drains blood into ordinary blood loss at a rate that follows
    /// severity. Gets worse on its own; a doctor's treatment slows the bleeding and lets it heal.</summary>
    public class Hediff_InternalBleeding : HediffWithComps
    {
        private InternalBleedingExtension ext;
        private InternalBleedingExtension Ext => ext ?? (ext = def.GetModExtension<InternalBleedingExtension>() ?? new InternalBleedingExtension());

        public override float BleedRate
        {
            get
            {
                if (pawn == null || pawn.Dead) return 0f;
                float rate = Severity * Ext.bleedRatePerSeverity;
                HediffComp_TendDuration td = this.TryGetComp<HediffComp_TendDuration>();
                if (td != null && td.IsTended) rate *= 1f - Ext.tendReduction * td.tendQuality;
                return rate;
            }
        }

        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);
            // The pawn caches its total bleed rate; severity creeps within a stage, so refresh it now and then.
            if (pawn.IsHashIntervalTick(250, delta)) pawn.health.hediffSet.DirtyCache();
        }
    }

    // ------------------------------------------------------------------ delayed onset

    /// <summary>Once the parent reaches minSeverity, adds hediff after a delay, even if the pawn has left the
    /// gas by then (blood thinners start the bleeding hours after the dose).</summary>
    public class HediffCompProperties_DelayedHediff : HediffCompProperties
    {
        public float minSeverity = 0.25f;
        public IntRange delayTicks = new IntRange(5000, 10000);
        public HediffDef hediff;
        public string messageKey;

        public HediffCompProperties_DelayedHediff() { compClass = typeof(HediffComp_DelayedHediff); }
    }

    public class HediffComp_DelayedHediff : HediffComp
    {
        private int dueTick = -1;
        private bool done;

        public HediffCompProperties_DelayedHediff Props => (HediffCompProperties_DelayedHediff)props;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (done || Props.hediff == null || !Pawn.IsHashIntervalTick(60, delta)) return;
            int now = Find.TickManager.TicksGame;
            if (dueTick < 0)
            {
                if (parent.Severity >= Props.minSeverity) dueTick = now + Props.delayTicks.RandomInRange;
                return;
            }
            if (now < dueTick) return;
            done = true;
            Pawn pawn = Pawn;
            if (pawn.Dead || pawn.health.hediffSet.HasHediff(Props.hediff)) return;
            Hediff added = HediffMaker.MakeHediff(Props.hediff, pawn);
            pawn.health.AddHediff(added);
            if (!Props.messageKey.NullOrEmpty() && PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message(Props.messageKey.Translate(pawn.Named("PAWN"), added.LabelBase.Named("RESULT")).Resolve(), pawn, MessageTypeDefOf.NegativeHealthEvent);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref dueTick, "dueTick", -1);
            Scribe_Values.Look(ref done, "done", false);
        }

        public override string CompDebugString() => done ? "delayed hediff added" : dueTick < 0 ? "not triggered" : $"due in {dueTick - Find.TickManager.TicksGame} ticks";
    }
}
