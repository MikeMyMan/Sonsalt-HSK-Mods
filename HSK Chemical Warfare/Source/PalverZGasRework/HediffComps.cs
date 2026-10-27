using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace PalverZGasRework
{
    // ------------------------------------------------------------------ aftermath

    public class GasAftermathOption
    {
        public HediffDef hediff;
        /// <summary>Body part to put the hediff on; null = whole body.</summary>
        public BodyPartDef part;
        public float weight = 1f;
        /// <summary>Peak severity this option needs; below 0 = the comp's minPeakSeverity.</summary>
        public float minPeakSeverity = -1f;
        /// <summary>One option is picked per group, so a comp can add temporary ataxia and,
        /// past a higher peak, permanent nerve damage on top.</summary>
        public string group;
    }

    /// <summary>When the gas hediff wears off on a living pawn, adds one lasting hediff per option group
    /// whose peak-severity threshold was reached (scarring, ataxia, nerve damage).</summary>
    public class HediffCompProperties_GasAftermath : HediffCompProperties
    {
        public float minPeakSeverity = 0.4f;
        public List<GasAftermathOption> options = new List<GasAftermathOption>();
        /// <summary>Message key; gets PAWN, GAS and RESULT.</summary>
        public string messageKey;

        public HediffCompProperties_GasAftermath() { compClass = typeof(HediffComp_GasAftermath); }
    }

    public class HediffComp_GasAftermath : HediffComp, IGasExposureListener
    {
        public float peakSeverity;

        public HediffCompProperties_GasAftermath Props => (HediffCompProperties_GasAftermath)props;

        public void Notify_GasExposure() => Track();

        private void Track()
        {
            if (parent.Severity > peakSeverity) peakSeverity = parent.Severity;
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta) => Track();

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref peakSeverity, "peakSeverity", 0f);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.health == null || Props.options.NullOrEmpty()) return;
            var groups = new Dictionary<string, List<GasAftermathOption>>();
            foreach (GasAftermathOption o in Props.options)
            {
                float min = o.minPeakSeverity >= 0f ? o.minPeakSeverity : Props.minPeakSeverity;
                if (o.hediff == null || peakSeverity < min) continue;
                string key = o.group ?? "";
                if (!groups.TryGetValue(key, out List<GasAftermathOption> list)) groups[key] = list = new List<GasAftermathOption>();
                list.Add(o);
            }
            foreach (List<GasAftermathOption> list in groups.Values) Apply(pawn, list.RandomElementByWeight(o => o.weight));
        }

        private void Apply(Pawn pawn, GasAftermathOption option)
        {
            if (option?.hediff == null) return;
            BodyPartRecord part = null;
            if (option.part != null)
            {
                foreach (BodyPartRecord p in pawn.health.hediffSet.GetNotMissingParts())
                {
                    if (p.def == option.part) { part = p; break; }
                }
                if (part == null) return;
            }
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == option.hediff && hediffs[i].Part == part) return;
            }
            Hediff added = HediffMaker.MakeHediff(option.hediff, pawn, part);
            pawn.health.AddHediff(added, part);
            if (!Props.messageKey.NullOrEmpty() && PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message(Props.messageKey.Translate(pawn.Named("PAWN"), parent.def.label.Named("GAS"), added.LabelBase.Named("RESULT")).Resolve(),
                    pawn, MessageTypeDefOf.NegativeHealthEvent);
            }
        }

        public override string CompDebugString() => "peak severity: " + peakSeverity.ToString("0.00");
    }

    // ------------------------------------------------------------------ fade without exposure

    /// <summary>The hediff only holds while the pawn keeps getting hit by the gas; after graceTicks
    /// without a hit it drains fast. Models "continuous seconds of deprivation".</summary>
    public class HediffCompProperties_FadeWithoutExposure : HediffCompProperties
    {
        public int graceTicks = 90;
        public float severityLossPerSecond = 0.25f;

        public HediffCompProperties_FadeWithoutExposure() { compClass = typeof(HediffComp_FadeWithoutExposure); }
    }

    public class HediffComp_FadeWithoutExposure : HediffComp, IGasExposureListener
    {
        private int lastExposureTick = -1;

        public HediffCompProperties_FadeWithoutExposure Props => (HediffCompProperties_FadeWithoutExposure)props;

        public void Notify_GasExposure() => lastExposureTick = Find.TickManager.TicksGame;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            int now = Find.TickManager.TicksGame;
            if (lastExposureTick < 0) lastExposureTick = now;
            if (now - lastExposureTick > Props.graceTicks) severityAdjustment -= Props.severityLossPerSecond * delta / 60f;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastExposureTick, "lastExposureTick", -1);
        }
    }

    // ------------------------------------------------------------------ add a hediff at a severity

    /// <summary>Once the parent reaches severity, adds hediff (if the pawn doesn't have it yet).
    /// Slumbercloud uses it for a fixed-length sleep, so a pawn pulled out the moment it drops
    /// still sleeps for hours.</summary>
    public class HediffCompProperties_AddHediffAtSeverity : HediffCompProperties
    {
        public float severity = 0.4f;
        public HediffDef hediff;

        public HediffCompProperties_AddHediffAtSeverity() { compClass = typeof(HediffComp_AddHediffAtSeverity); }
    }

    public class HediffComp_AddHediffAtSeverity : HediffComp, IGasExposureListener
    {
        public HediffCompProperties_AddHediffAtSeverity Props => (HediffCompProperties_AddHediffAtSeverity)props;

        public void Notify_GasExposure() => TryAdd();

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (Pawn.IsHashIntervalTick(30, delta)) TryAdd();
        }

        private void TryAdd()
        {
            Pawn pawn = Pawn;
            if (Props.hediff == null || pawn.Dead || parent.Severity < Props.severity) return;
            if (pawn.health.hediffSet.HasHediff(Props.hediff)) return;
            pawn.health.AddHediff(Props.hediff);
        }
    }

    // ------------------------------------------------------------------ dread break

    /// <summary>At triggerSeverity, forces a mental state regardless of mood: violent humanlikes
    /// go berserk, other humanlikes and animals flee.</summary>
    public class HediffCompProperties_DreadBreak : HediffCompProperties
    {
        public float triggerSeverity = 0.6f;
        public List<TraitDef> violentTraits = new List<TraitDef>();
        public MentalStateDef violentState;
        public MentalStateDef fearfulState;
        public MentalStateDef animalState;
        public int checkIntervalTicks = 60;

        public HediffCompProperties_DreadBreak() { compClass = typeof(HediffComp_DreadBreak); }
    }

    public class HediffComp_DreadBreak : HediffComp, IGasExposureListener
    {
        public HediffCompProperties_DreadBreak Props => (HediffCompProperties_DreadBreak)props;

        public void Notify_GasExposure() => TryBreak();

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (Pawn.IsHashIntervalTick(Props.checkIntervalTicks, delta)) TryBreak();
        }

        private void TryBreak()
        {
            Pawn pawn = Pawn;
            if (parent.Severity < Props.triggerSeverity) return;
            if (pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.InMentalState) return;
            MentalStateHandler handler = pawn.mindState?.mentalStateHandler;
            if (handler == null) return;
            MentalStateDef state;
            if (pawn.RaceProps.Humanlike) state = IsViolent(pawn) ? Props.violentState : Props.fearfulState;
            else state = Props.animalState;
            if (state == null) return;
            handler.TryStartMentalState(state, "PGR_DreadsporeReason".Translate().Resolve(), forced: false, forceWake: true, causedByMood: false);
        }

        private bool IsViolent(Pawn pawn)
        {
            if (pawn.story?.traits == null || Props.violentTraits == null) return false;
            for (int i = 0; i < Props.violentTraits.Count; i++)
            {
                if (pawn.story.traits.HasTrait(Props.violentTraits[i])) return true;
            }
            return false;
        }
    }
}
