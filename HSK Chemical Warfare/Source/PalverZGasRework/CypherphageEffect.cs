using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PalverZGasRework
{
    public enum CypherOutcome { LoopStun, ComponentMeltdown, SensorBlind, SubroutineScramble, FirmwareHijack }

    public class CypherphageOutcome
    {
        public CypherOutcome outcome;
        public float weight = 1f;
        /// <summary>LoopStun: stun length.</summary>
        public IntRange stunTicks = new IntRange(240, 360);
        /// <summary>SensorBlind: the blindness hediff. ComponentMeltdown: the leak hediff (needs a working reprocessor).</summary>
        public HediffDef hediff;
        /// <summary>SubroutineScramble: the mental state.</summary>
        public MentalStateDef mentalState;
        /// <summary>ComponentMeltdown: fire at the mech's feet and internal burn damage.</summary>
        public float fireSize = 0.5f;
        public DamageDef burnDamageDef;
        public FloatRange burnDamage = new FloatRange(4f, 8f);
        /// <summary>Translation key for the floating text.</summary>
        public string textKey;
        public Color textColor = Color.white;
    }

    /// <summary>Every rollIntervalTicks a mechanoid spends in dense cloud, it rolls one malfunction.</summary>
    public class GasEffect_Cypherphage : PawnGasEffectProps
    {
        public int rollIntervalTicks = 180;
        public List<CypherphageOutcome> outcomes = new List<CypherphageOutcome>();

        public GasEffect_Cypherphage()
        {
            workerClass = typeof(GasEffectWorker_Cypherphage);
            targets = GasTargets.Mechanoid;
            minDensity = 100;
        }
    }

    public class GasEffectWorker_Cypherphage : PawnGasEffectWorker
    {
        private class Exposure
        {
            public int ticks;
            public int lastSeen = -99999;
        }

        // Transient: exposure timers reset on load, which only delays the next roll.
        private static readonly Dictionary<int, Exposure> exposure = new Dictionary<int, Exposure>();
        private static readonly List<int> tmpStale = new List<int>();

        protected override void Apply(Pawn pawn, float densityPercent, byte density)
        {
            var p = (GasEffect_Cypherphage)props;
            int now = Find.TickManager.TicksGame;
            if (!exposure.TryGetValue(pawn.thingIDNumber, out Exposure e))
            {
                e = new Exposure();
                exposure[pawn.thingIDNumber] = e;
            }
            int dt = now - e.lastSeen;
            if (dt > 75) e.ticks = 0; // left the thick cloud: the count starts over
            else e.ticks += dt;
            e.lastSeen = now;
            if (exposure.Count > 512) Prune(now);
            if (e.ticks < p.rollIntervalTicks) return;
            e.ticks = 0;
            Roll(pawn, p);
        }

        private static void Prune(int now)
        {
            tmpStale.Clear();
            foreach (KeyValuePair<int, Exposure> kv in exposure)
            {
                if (now - kv.Value.lastSeen > 600) tmpStale.Add(kv.Key);
            }
            for (int i = 0; i < tmpStale.Count; i++) exposure.Remove(tmpStale[i]);
            tmpStale.Clear();
        }

        private static void Roll(Pawn pawn, GasEffect_Cypherphage p)
        {
            if (p.outcomes.NullOrEmpty()) return;
            // An outcome can be impossible (a colony mech can't be hijacked again); re-roll a few times.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                CypherphageOutcome o = p.outcomes.RandomElementByWeight(x => x.weight);
                if (pawn.Dead || !pawn.Spawned) return;
                if (TryApply(pawn, o))
                {
                    if (!o.textKey.NullOrEmpty() && pawn.Spawned) GasPawnUtility.Text(pawn, o.textKey.Translate().Resolve(), o.textColor);
                    return;
                }
            }
        }

        private static bool TryApply(Pawn pawn, CypherphageOutcome o)
        {
            switch (o.outcome)
            {
                case CypherOutcome.LoopStun:
                    if (pawn.stances?.stunner == null) return false;
                    pawn.stances.stunner.StunFor(o.stunTicks.RandomInRange, null, addBattleLog: false, showMote: true);
                    return true;

                case CypherOutcome.ComponentMeltdown:
                {
                    Map map = pawn.Map;
                    IntVec3 pos = pawn.Position;
                    if (o.burnDamageDef != null)
                    {
                        // Heat in the hull, not a random internal part: one roll must not snipe a processor.
                        BodyPartRecord core = pawn.RaceProps.body.corePart;
                        pawn.TakeDamage(new DamageInfo(o.burnDamageDef, o.burnDamage.RandomInRange, 0f, -1f, null, core));
                    }
                    if (map != null && o.fireSize > 0f) FireUtility.TryStartFireIn(pos, map, o.fireSize, null);
                    if (o.hediff != null && !pawn.Dead && !pawn.health.hediffSet.HasHediff(o.hediff) && HasWorkingReprocessor(pawn))
                        pawn.health.AddHediff(o.hediff);
                    return true;
                }

                case CypherOutcome.SensorBlind:
                {
                    if (o.hediff == null) return false;
                    Hediff old = pawn.health.hediffSet.GetFirstHediffOfDef(o.hediff);
                    if (old != null) pawn.health.RemoveHediff(old); // restart the timer
                    pawn.health.AddHediff(o.hediff);
                    return true;
                }

                case CypherOutcome.SubroutineScramble:
                {
                    MentalStateHandler handler = pawn.mindState?.mentalStateHandler;
                    if (handler == null || o.mentalState == null) return false;
                    if (pawn.MentalStateDef == o.mentalState) return false;
                    return handler.TryStartMentalState(o.mentalState, "PGR_CypherphageReason".Translate().Resolve(), forced: true, forceWake: true, causedByMood: false);
                }

                case CypherOutcome.FirmwareHijack:
                {
                    if (pawn.Faction == Faction.OfPlayer) return false;
                    pawn.MentalState?.RecoverFromState();
                    RecruitUtility.Recruit(pawn, Faction.OfPlayer);
                    TryAssignOverseer(pawn);
                    Messages.Message("PGR_FirmwareHijacked".Translate(pawn.LabelShort, pawn.Named("PAWN")).Resolve(), pawn, MessageTypeDefOf.PositiveEvent);
                    return true;
                }
            }
            return false;
        }

        private static bool HasWorkingReprocessor(Pawn pawn)
        {
            List<BodyPartRecord> parts = pawn.RaceProps.body.GetPartsWithTag(BodyPartTagDefOf.BloodFiltrationSource);
            for (int i = 0; i < parts.Count; i++)
            {
                if (!pawn.health.hediffSet.PartIsMissing(parts[i])) return true;
            }
            return false;
        }

        // A hijacked mech joins the first mechanitor on the map with bandwidth to spare.
        // Without one it stays an uncontrolled colony mech, as vanilla handles it.
        private static void TryAssignOverseer(Pawn mech)
        {
            if (!ModsConfig.BiotechActive || mech.Map == null || mech.OverseerSubject == null) return;
            try
            {
                foreach (Pawn colonist in mech.Map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist.mechanitor == null || !MechanitorUtility.CanControlMech(colonist, mech).Accepted) continue;
                    colonist.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                    return;
                }
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("overseer", "Assigning a hijacked mech to a mechanitor failed: " + e);
            }
        }
    }
}
