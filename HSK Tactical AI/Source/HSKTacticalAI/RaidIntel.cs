using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>What an assault is after (Raid Tactics idea): goods (the default), people, or revenge.</summary>
    public enum RaidIntent { Plunder, Slaving, Punitive }

    /// <summary>
    /// What a faction does with its raid memory before the raid arrives (ideas from Raid Tactics, playtest 2026-10-26:
    /// "we go with our AI ... but we can learn from it and add the ability to our AI"):
    /// - Escalation: a faction beaten here is angry for a while and sends a bigger raid (up to escalationSize more points);
    ///   a raid that went its way calms it down; the anger fades with the memory half-life.
    /// - Intent: an assault comes for goods, for people (slaving: grab colonists, burn nothing, leave with a captive) or,
    ///   only after a bad beating they know the reason for, for revenge (punitive: takes nothing, never breaks off).
    /// - The raid letter says why the raid comes as it does ("the last group never came back, so ...").
    /// - Informants: a prisoner you release, or one who escapes over the map edge, reports to his faction like a survivor.
    /// </summary>
    public static class RaidIntel
    {
        public static int Escalated, Letters, Informants, IntentsRolled;
        public static string LastLetterLine = "";
        private static readonly Dictionary<int, (RaidIntent intent, int tick)> pending = new Dictionary<int, (RaidIntent, int)>();
        private static readonly HashSet<IncidentParms> scaled = new HashSet<IncidentParms>();

        private static bool Applies(IncidentParms parms) =>
            parms?.faction != null && parms.target is Map map && map.IsPlayerHome && parms.faction.HostileTo(Faction.OfPlayer)
            && !parms.faction.IsPlayer && parms.quest == null;

        // ---------------- before the raid is generated ----------------

        public static void BeforeGenerate(IncidentParms parms)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.raidMemory || !Applies(parms) || scaled.Contains(parms)) return;
            scaled.Add(parms);
            if (scaled.Count > 64) scaled.Clear();
            var mem = FactionMemory.Read(parms.faction);
            // anger: more people
            if (s.escalation && mem != null && mem.escalation > 0.05f)
            {
                parms.points *= 1f + mem.escalation * s.escalationSize;
                Escalated++;
            }
            // what this raid is after (consumed by RaidGoals.Roll when the raid plans)
            RaidIntent intent = RollIntent(parms.faction, mem, parms.points);
            pending[parms.faction.loadID] = (intent, Find.TickManager.TicksGame);
            IntentsRolled++;
        }

        public static RaidIntent RollIntent(Faction f, FactionRaidMemory mem, float points)
        {
            var s = TacticalAIMod.Settings;
            if (!s.raidIntents || f.def.humanlikeFaction == false) return RaidIntent.Plunder;
            if (!string.IsNullOrEmpty(s.raidIntentOverride))   // AI Lab only
                return s.raidIntentOverride.ToLowerInvariant() == "slaving" ? RaidIntent.Slaving
                     : s.raidIntentOverride.ToLowerInvariant() == "punitive" ? RaidIntent.Punitive : RaidIntent.Plunder;
            float wSlave = s.slavingShare, wPunish = 0f;
            if (f.def.techLevel >= TechLevel.Spacer) wSlave *= 0.5f;
            // revenge only after a bad beating they know the reason for
            if (mem != null && mem.escalation >= 0.5f && mem.intel >= s.memoryIntelMin) wPunish = s.punitiveShare * (0.5f + mem.escalation);
            float total = 1f + wSlave + wPunish;
            float r = Rand.Value * total;
            if (r < wPunish) return RaidIntent.Punitive;
            if (r < wPunish + wSlave) return RaidIntent.Slaving;
            return RaidIntent.Plunder;
        }

        /// <summary>The intent rolled for this faction's raid within the last half day (and forget it).</summary>
        public static RaidIntent TakeIntent(Faction f)
        {
            if (f == null || !pending.TryGetValue(f.loadID, out var p)) return RaidIntent.Plunder;
            pending.Remove(f.loadID);
            return Find.TickManager.TicksGame - p.tick <= GenDate.TicksPerDay / 2 ? p.intent : RaidIntent.Plunder;
        }

        public static RaidIntent PeekIntent(Faction f) => f != null && pending.TryGetValue(f.loadID, out var p) ? p.intent : RaidIntent.Plunder;

        private static readonly Dictionary<int, RaidIntent> byLord = new Dictionary<int, RaidIntent>();
        private static readonly Dictionary<int, int> noDefendersSince = new Dictionary<int, int>();
        public static int Captives, PunitiveExits, KidnapJobs, PunitiveTransitionsRemoved, SlavingTransitionsRemoved, KidnapNoExit, KidnapNoVictim, KidnapInFire;

        /// <summary>The raid's intent, the same on every re-plan of the same lord.</summary>
        public static RaidIntent IntentFor(Verse.AI.Group.Lord lord)
        {
            if (lord == null) return RaidIntent.Plunder;
            if (byLord.TryGetValue(lord.loadID, out var i)) return i;
            var s = TacticalAIMod.Settings;
            i = !string.IsNullOrEmpty(s.raidIntentOverride) ? RollIntent(lord.faction, FactionMemory.Read(lord.faction), 0f) : TakeIntent(lord.faction);
            byLord[lord.loadID] = i;
            return i;
        }

        /// <summary>
        /// A punitive raid doesn't break off: the lord's own retreats (losses, damage done, time) and kidnapping go.
        /// Slavers aren't satisfied by damage done (rt4: they left on "colony damage taken" before taking anyone): only that
        /// trigger goes; heavy losses still send them home.
        /// </summary>
        public static void ApplyIntent(Verse.AI.Group.Lord lord, AssaultPlan plan)
        {
            if (lord?.Graph?.transitions == null) return;
            if (plan.intent == RaidIntent.Punitive)
            {
                int removed = lord.Graph.transitions.RemoveAll(t => t.target is Verse.AI.Group.LordToil_ExitMap || t.target is LordToil_PanicFlee
                                                                   || t.target is LordToil_KidnapCover);
                plan.goalWhy += " (" + removed + " break-off transitions removed)";
                PunitiveTransitionsRemoved += removed;
            }
            else if (plan.intent == RaidIntent.Slaving)
            {
                int removed = lord.Graph.transitions.RemoveAll(t => t.triggers != null && t.triggers.Any(tr => tr is Trigger_FractionColonyDamageTaken));
                plan.goalWhy += " (" + removed + " damage-satisfied transitions removed)";
                SlavingTransitionsRemoved += removed;
            }
        }

        /// <summary>
        /// Every 60 ticks per planned raid: a slaving raid leaves once it has a captive; a punitive raid leaves only when no
        /// defender has stood for 40 s, or after a day.
        /// </summary>
        public static void Tick(Map map, Verse.AI.Group.Lord lord, AssaultPlan plan, List<Pawn> alive, int now)
        {
            if (plan.phase != PlanPhase.Approach && plan.phase != PlanPhase.Assault) return;
            if (plan.intent == RaidIntent.Slaving && !plan.captiveTaken
                && alive.Any(p => p.carryTracker?.CarriedThing is Pawn c && c.Faction == Faction.OfPlayer))
            {
                plan.captiveTaken = true;
                Captives++;
                plan.hHourReason = "slaving: captive taken";
                if (Competence.PlannedWithdrawal(plan)) Withdrawal.Begin(map, lord, plan, alive, "got a captive");
                else AssaultPlanner.ExitMap(lord, plan);
                lord.CurLordToil?.UpdateAllDuties();
                return;
            }
            if (plan.intent == RaidIntent.Punitive)
            {
                bool standing = Suppression.Defenders(map, plan.objective, 999f).Count > 0;
                if (standing) noDefendersSince.Remove(lord.loadID);
                else if (!noDefendersSince.ContainsKey(lord.loadID)) noDefendersSince[lord.loadID] = now;
                bool done = !standing && now - noDefendersSince[lord.loadID] > 2400;
                if (done || now - plan.createdTick > GenDate.TicksPerDay)
                {
                    PunitiveExits++;
                    plan.hHourReason = done ? "punitive: nobody left standing" : "punitive: a day has passed";
                    AssaultPlanner.ExitMap(lord, plan);
                }
            }
        }

        // ---------------- the raid letter ----------------

        /// <summary>A short paragraph for the raid letter: how their last raid went, what they do differently, what they are after.</summary>
        public static string LetterLine(Faction f)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.raidLetters || f == null) return null;
            var mem = FactionMemory.Read(f);
            var lines = new List<string>();
            if (mem != null && mem.raids > 0 && mem.lastStrength > 0)
            {
                if (mem.lastEscaped == 0) lines.Add("The last group they sent here, all " + mem.lastStrength + " of them, never came back.");
                else if (mem.lastSuccess) lines.Add("Their last attack here went their way.");
                else if (!mem.lastReached) lines.Add("Their last attack here cost them " + mem.lastLosses + " of " + mem.lastStrength + " and never reached the colony.");
                else if (mem.lastLosses >= mem.lastStrength * 0.4f) lines.Add("They took heavy losses here last time (" + mem.lastLosses + " of " + mem.lastStrength + ").");
                // what they do about it (only if someone told them)
                var mode = FactionMemory.PreferredMode(f, true, out _);
                if (mode == PlanMode.Siege) lines.Add("This time they mean to stay out of reach and besiege you.");
                else if (mode == PlanMode.Breach) lines.Add("This group is not using your entrance; it is coming through a wall.");
                else if (mem.lastEscaped == 0 && mem.intel < s.memoryIntelMin) lines.Add("They do not know what killed them.");
                if (s.escalation && mem.escalation >= 0.2f)
                    lines.Add("Angry about it, they have sent about " + Mathf.RoundToInt(mem.escalation * s.escalationSize * 100f) + "% more people than usual.");
                if (mem.informants > 0 && mem.lastInformantTick >= 0 && Find.TickManager.TicksGame - mem.lastInformantTick < GenDate.TicksPerDay * 30)
                    lines.Add("A prisoner who got away from you has told them about your defences.");
            }
            switch (PeekIntent(f))
            {
                case RaidIntent.Slaving: lines.Add("They are after people, not goods."); break;
                case RaidIntent.Punitive: lines.Add("They are not here to take anything, and they will not break off."); break;
            }
            if (lines.Count == 0) return null;
            Letters++;
            LastLetterLine = string.Join(" ", lines);
            return LastLetterLine;
        }

        // ---------------- informants ----------------

        public static void Informant(Pawn p)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.raidMemory || !s.prisonersReport || p?.Faction == null || p.Faction.IsPlayer || p.Dead || p.Downed) return;
            if (p.Map == null || !p.Map.IsPlayerHome || !p.RaceProps.Humanlike) return;
            if (!(p.IsPrisonerOfColony || (p.guest != null && p.guest.Released))) return;
            var mem = GameComponent_RaidMemory.Instance?.For(p.Faction, true);
            if (mem == null) return;
            mem.intel = Mathf.Clamp01(mem.intel + 0.3f);
            mem.informants++;
            mem.lastInformantTick = Find.TickManager.TicksGame;
            mem.lastLesson = "a prisoner who left your colony (" + p.LabelShort + ") told them about you";
            Informants++;
        }
    }

    /// <summary>Slaving raid: grab a downed colonist within 25 cells and carry him off (into fire only when two can cover it).</summary>
    public class JobGiver_HSKTAI_Kidnap : Verse.AI.ThinkNode_JobGiver
    {
        protected override Verse.AI.Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || plan.intent != RaidIntent.Slaving || plan.captiveTaken || pawn.carryTracker?.CarriedThing != null) return null;
            if (!pawn.RaceProps.Humanlike || pawn.Downed || pawn.InMentalState) return null;
            if (!RCellFinder.TryFindBestExitSpot(pawn, out IntVec3 exit)) { RaidIntel.KidnapNoExit++; return null; }
            if (!KidnapAIUtility.TryFindGoodKidnapVictim(pawn, 25f, out Pawn victim)) { RaidIntel.KidnapNoVictim++; return null; }
            // into fire only when covered - unless he lies right next to the slaver
            if (victim.Position.DistanceTo(pawn.Position) > 6f && CoverCarry.InFire(pawn.Map, victim.Position) && !CoverCarry.CoveredRescue(pawn, victim))
            { RaidIntel.KidnapInFire++; return null; }
            RaidIntel.KidnapJobs++;
            Verse.AI.Job job = JobMaker.MakeJob(JobDefOf.Kidnap, victim, exit);
            job.count = 1;
            return job;
        }
    }

    /// <summary>After the game picked the raid's faction and strategy (natural raids pick the faction only here), before
    /// the arrival mode, the spawn point and the pawns: escalation, intent, arrival choice.</summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), nameof(IncidentWorker_RaidEnemy.ResolveRaidStrategy))]
    public static class Patch_RaidEscalation
    {
        public static void Postfix(IncidentParms parms)
        {
            try { RaidIntel.BeforeGenerate(parms); RaidArrival.Choose(parms); }
            catch (Exception e) { Log.ErrorOnce("[HSK Tactical AI] raid escalation / arrival failed: " + e.Message, 0x48534B44); }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GetLetterText")]
    public static class Patch_RaidLetterWhy
    {
        public static void Postfix(IncidentParms parms, ref string __result)
        {
            try
            {
                if (parms?.faction == null || !(parms.target is Map map) || !map.IsPlayerHome) return;
                string line = RaidIntel.LetterLine(parms.faction);
                if (!line.NullOrEmpty()) __result += "\n\n" + line;
            }
            catch (Exception e) { Log.ErrorOnce("[HSK Tactical AI] raid letter line failed: " + e.Message, 0x48534B45); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Patch_PrisonerInformant
    {
        public static void Prefix(Pawn __instance)
        {
            try { RaidIntel.Informant(__instance); }
            catch (Exception e) { Log.ErrorOnce("[HSK Tactical AI] informant failed: " + e.Message, 0x48534B46); }
        }
    }
}
