using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// What a raid's command may or may not master, in rising order of the understanding it needs. Each has a band of
    /// competence (lo..hi): never below lo, always above hi, in between by chance. One roll per raid decides them all, so
    /// a raid that masters a harder doctrine also masters the easier ones.
    /// </summary>
    public enum Doctrine
    {
        PlannedWithdrawal, ForceCheck, KeepRange, SupportFire, MultiAxis2, OpenCrossing, FocusFire, DefendedAxis, Memory,
        ThreatAdvance, AvoidFunnels, Smoke, SyncedAssault, Overwatch, FunnelCaution, BreachFallback, MultiBreach,
        Flanking, MultiAxis3, FlankManeuver
    }

    /// <summary>
    /// Tactical competence (0..1) per raid (playtest, 2026-10-25: "act and react smarter with higher intelligence of the
    /// raid's leader ... the lower the intelligence, the less smart the AI acts", plus a tech-level factor: "you can't get
    /// so advanced if you don't have the necessary knowledge").
    ///
    /// competence = tech ceiling x ((1 - w) + w x leader intelligence), w = competenceIntelWeight.
    /// - Tech ceiling: Neolithic 0.55, Medieval 0.70, Industrial 0.85, Spacer / Ultra / Archotech 1.0. A genius chief of a
    ///   tribe can't plan like a spacer staff officer.
    /// - Leader: the raid's leader (faction leader > leader kind > royal title > highest combat power). HSK has no
    ///   intelligence stat; intelligence = Intellectual skill + the learning factor (TooSmart, SlowLearner, HSK's Dumb,
    ///   Dunce and Volatile genius traits, learning genes, brain implants and injuries) x consciousness. Mechanoids think as
    ///   a hive (fixed 0.75), animals and insects barely plan.
    /// - Loss of command: the leader down, dead or gone - the next in rank takes over at most at his own competence x 0.8,
    ///   and for 20 s the raid acts at x 0.7 while command is sorted out.
    /// Low competence is close to vanilla (straight routes that only see the fire nearby, one axis, no strength check,
    /// walking into killboxes and traps, poorly chosen breach points, the vanilla panic run); high is the full planner.
    /// </summary>
    public static class Competence
    {
        private static readonly Vector2[] Bands =
        {
            new Vector2(0.20f, 0.50f),   // PlannedWithdrawal
            new Vector2(0.22f, 0.52f),   // ForceCheck
            new Vector2(0.24f, 0.54f),   // KeepRange (each weapon at its own distance: bows and long guns back off, short guns close in)
            new Vector2(0.28f, 0.58f),   // SupportFire
            new Vector2(0.28f, 0.58f),   // MultiAxis2
            new Vector2(0.30f, 0.60f),   // OpenCrossing
            new Vector2(0.30f, 0.60f),   // FocusFire
            new Vector2(0.32f, 0.62f),   // DefendedAxis
            new Vector2(0.32f, 0.62f),   // Memory
            new Vector2(0.35f, 0.65f),   // ThreatAdvance
            new Vector2(0.35f, 0.65f),   // AvoidFunnels
            new Vector2(0.38f, 0.68f),   // Smoke
            new Vector2(0.40f, 0.70f),   // SyncedAssault
            new Vector2(0.40f, 0.70f),   // Overwatch
            new Vector2(0.42f, 0.72f),   // FunnelCaution
            new Vector2(0.42f, 0.72f),   // BreachFallback
            new Vector2(0.45f, 0.75f),   // MultiBreach
            new Vector2(0.48f, 0.78f),   // Flanking
            new Vector2(0.55f, 0.82f),   // MultiAxis3
            new Vector2(0.55f, 0.85f),   // FlankManeuver
        };

        public const float MechHiveIntelligence = 0.75f;
        public const float LossOfCommandFactor = 0.8f;
        public const float CommandShockFactor = 0.7f;
        public const int CommandShockTicks = 1200;
        public const float BreachNoiseCost = 8000f;   // poorly chosen breach points: up to this much random cost at competence 0
        public const ushort KnownTrapRouteCost = 800;   // as vanilla's cost for a trap the pawn knows of

        public static int TrapSprings, CommandLosses;

        public static bool Enabled => TacticalAIMod.Settings != null && TacticalAIMod.Settings.tacticalCompetence;

        // ---------------- the value ----------------

        public static float TechCeiling(TechLevel t)
        {
            switch (t)
            {
                case TechLevel.Neolithic: return 0.55f;
                case TechLevel.Medieval: return 0.70f;
                case TechLevel.Industrial: return 0.85f;
                case TechLevel.Spacer:
                case TechLevel.Ultra:
                case TechLevel.Archotech: return 1f;
                default: return 0.4f;   // Animal / Undefined
            }
        }

        // Intellectual points (skill + learning bonus) -> intelligence: 6 is an ordinary raider, 10+ a sharp one
        private static readonly SimpleCurve PointsToIntelligence = new SimpleCurve
        {
            new CurvePoint(0f, 0.1f), new CurvePoint(3f, 0.3f), new CurvePoint(6f, 0.5f),
            new CurvePoint(10f, 0.7f), new CurvePoint(14f, 0.85f), new CurvePoint(18f, 1f)
        };

        /// <summary>The leader's intelligence 0..1 and how it was read.</summary>
        public static float Intelligence(Pawn p, out string why)
        {
            if (p == null) { why = "no leader"; return 0.5f; }
            if (p.RaceProps == null || !p.RaceProps.Humanlike)
            {
                if (p.RaceProps != null && p.RaceProps.IsMechanoid) { why = "mech hive mind"; return MechHiveIntelligence; }
                why = "animal";
                return 0.1f;
            }
            SkillRecord sk = p.skills?.GetSkill(SkillDefOf.Intellectual);
            float level = sk == null ? 5f : sk.TotallyDisabled ? 0f : sk.Level;
            float learn = 1f;
            try { learn = p.GetStatValue(StatDefOf.GlobalLearningFactor); } catch { }
            // traits and genes through the learning factor: too smart +6, slow learner -6, dumb -4 (and -3 Intellectual),
            // volatile genius +2.4, dunce -2 (and -4 Intellectual)
            float points = level + 8f * (learn - 1f);
            float iq = PointsToIntelligence.Evaluate(points);
            float cons = 1f;
            try { cons = p.health?.capacities?.GetLevel(PawnCapacityDefOf.Consciousness) ?? 1f; } catch { }
            iq = Mathf.Clamp01(iq * Mathf.Clamp(cons, 0.2f, 1f));
            why = p.LabelShort + ": Intellectual " + (sk == null ? "-" : sk.TotallyDisabled ? "incapable" : sk.Level.ToString()) +
                  ", learning x" + learn.ToString("0.00") + (cons < 0.99f ? ", consciousness " + Mathf.RoundToInt(cons * 100f) + "%" : "");
            return iq;
        }

        public static float Combine(float ceiling, float iq)
        {
            float w = Mathf.Clamp01(TacticalAIMod.Settings.competenceIntelWeight);
            return Mathf.Clamp01(ceiling * ((1f - w) + w * iq));
        }

        /// <summary>The raid's leader (also the role leader): faction leader > leader kind > royal title > highest combat power.</summary>
        public static Pawn PickLeader(List<Pawn> pawns)
        {
            // people lead, not their war beasts (cp4 medieval: the highest combat power was an animal)
            var people = pawns.Where(p => p.RaceProps.Humanlike).ToList();
            return PickFrom(people.Count > 0 ? people : pawns);
        }

        private static Pawn PickFrom(List<Pawn> pawns) =>
            pawns.FirstOrDefault(p => p.Faction?.leader == p)
            ?? pawns.FirstOrDefault(p => p.kindDef?.factionLeader == true)
            ?? pawns.FirstOrDefault(p => p.royalty?.MostSeniorTitle != null)
            ?? pawns.OrderByDescending(p => p.kindDef?.combatPower ?? 0f).ThenBy(p => p.thingIDNumber).FirstOrDefault();

        /// <summary>At planning: the raid's competence from its tech level and its leader.</summary>
        public static void Init(AssaultPlan plan, Lord lord, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            plan.competenceRoll = s.competenceRoll >= 0f ? Mathf.Clamp01(s.competenceRoll) : Hash01(plan.lordId, 0x5A17);
            if (!Enabled) { plan.competence = plan.competenceBase = 1f; plan.competenceWhy = "off"; return; }
            Pawn leader = PickLeader(pawns);
            plan.competenceLeader = leader;
            TechLevel tech = lord.faction?.def.techLevel ?? TechLevel.Industrial;
            plan.techCeiling = TechCeiling(tech);
            plan.leaderIntel = Intelligence(leader, out string why);
            plan.competenceBase = Combine(plan.techCeiling, plan.leaderIntel);
            if (s.competenceOverride >= 0f) { plan.competenceBase = Mathf.Clamp01(s.competenceOverride); why += ", override"; }
            plan.competence = plan.competenceBase;
            plan.competenceWhy = tech + " ceiling " + plan.techCeiling.ToString("0.00") + ", " + why + " -> intelligence " + plan.leaderIntel.ToString("0.00");
            // raid memory: survivors told of the traps that downed their comrades - a command that listens watches for them
            var mem = s.raidMemory ? FactionMemory.Read(lord.faction) : null;
            if (mem != null && mem.intel >= s.memoryIntelMin && mem.hitTrap >= 1f && Knows(plan, Doctrine.Memory))
            {
                plan.trapMemoryBonus = Mathf.Min(0.45f, 0.15f * mem.hitTrap);
                plan.competenceWhy += ", remembers traps +" + Pct(plan.trapMemoryBonus);
            }
            PublishTrapRatio(plan);
            if (s.competenceReadout)
            {
                string txt = (lord.faction?.Name ?? "Raid") + ": tactical competence " + Pct(plan.competence) + " (" + plan.competenceWhy + ")";
                Log.Message("[HSK Tactical AI] " + txt);
                if (Current.ProgramState == ProgramState.Playing && !AILab.Active)
                    Messages.Message(txt, leader != null ? new LookTargets(leader) : LookTargets.Invalid, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>Loss of command (every 60 ticks): the leader down, dead or gone - the next in rank takes over.</summary>
        public static void CheckCommand(Lord lord, AssaultPlan plan, List<Pawn> alive, int now)
        {
            var s = TacticalAIMod.Settings;
            if (!Enabled || !s.competenceLossOfCommand || plan.commandLost) return;
            Pawn l = plan.competenceLeader;
            if (l == null || !(l.Dead || l.Downed || l.Destroyed || !l.Spawned || l.GetLord() != lord)) return;
            plan.commandLost = true;
            CommandLosses++;
            Pawn next = alive.Where(p => p != l && p.RaceProps.Humanlike)
                             .OrderByDescending(p => p.kindDef?.combatPower ?? 0f).ThenBy(p => p.thingIDNumber).FirstOrDefault();
            if (next == null) next = alive.FirstOrDefault(p => p != l);
            float iq = Intelligence(next, out string why);
            float before = plan.competence;
            float successor = s.competenceOverride >= 0f ? Mathf.Clamp01(s.competenceOverride) : Combine(plan.techCeiling, iq);   // Lab: the override stands for every commander
            plan.competence = Mathf.Min(plan.competence, successor * LossOfCommandFactor);
            plan.shockUntil = now + CommandShockTicks;
            plan.competenceWhy += "; leader " + (l.Dead ? "dead" : l.Downed ? "down" : "gone") + " -> " + (next != null ? why : "nobody") + " takes over";
            PublishTrapRatio(plan);
            string txt = (lord.faction?.Name ?? "Raid") + " lost its leader " + l.LabelShort + ": tactical competence " + Pct(before) + " -> " + Pct(plan.competence);
            if (Prefs.DevMode || s.competenceReadout) Log.Message("[HSK Tactical AI] " + txt);
            if (s.competenceReadout && Current.ProgramState == ProgramState.Playing && !AILab.Active)
                Messages.Message(txt, next != null ? new LookTargets(next) : LookTargets.Invalid, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>The raid's competence now (1 = the full planner, also when the system is off or there is no plan).</summary>
        public static float Of(AssaultPlan plan)
        {
            if (plan == null || !Enabled) return 1f;
            float c = plan.competence;
            if (plan.shockUntil > 0 && Find.TickManager.TicksGame < plan.shockUntil) c *= CommandShockFactor;
            return c;
        }

        /// <summary>Does this raid's command use the doctrine? Always true without the system or a plan.</summary>
        public static bool Knows(AssaultPlan plan, Doctrine d)
        {
            if (plan == null || !Enabled) return true;
            Vector2 b = Bands[(int)d];
            return Smooth(b.x, b.y, Of(plan)) > plan.competenceRoll;
        }

        /// <summary>Planned withdrawal (bounding back under covering fire, carrying the wounded) instead of the vanilla panic run.</summary>
        public static bool PlannedWithdrawal(AssaultPlan plan) => TacticalAIMod.Settings.plannedWithdrawal && Knows(plan, Doctrine.PlannedWithdrawal);

        // ---------------- lords without a plan of ours (sieges: converted plans, or HSK's own siege raids) ----------------

        private static readonly Dictionary<int, (float c, float roll)> lordCompetence = new Dictionary<int, (float, float)>();

        /// <summary>A plan handed its raid to another lord (siege, exit): the new lord keeps the plan's competence and roll.</summary>
        public static void RegisterConverted(Lord newLord, AssaultPlan plan)
        {
            if (newLord != null && plan != null) lordCompetence[newLord.loadID] = (plan.competence, plan.competenceRoll);
        }

        /// <summary>Competence of any hostile lord: a plan's, a converted plan's, else from its leader and tech level.</summary>
        public static float OfLord(Lord lord, out float roll)
        {
            roll = 0f;
            if (lord == null || !Enabled) return 1f;
            if (AssaultPlanner.Plans.TryGetValue(lord.loadID, out var plan)) { roll = plan.competenceRoll; return Of(plan); }
            if (!lordCompetence.TryGetValue(lord.loadID, out var v))
            {
                var s = TacticalAIMod.Settings;
                var pawns = lord.ownedPawns.Where(p => p.Spawned && !p.Dead).ToList();
                float iq = Intelligence(PickLeader(pawns), out _);
                float c = s.competenceOverride >= 0f ? Mathf.Clamp01(s.competenceOverride) : Combine(TechCeiling(lord.faction?.def.techLevel ?? TechLevel.Industrial), iq);
                v = (c, s.competenceRoll >= 0f ? Mathf.Clamp01(s.competenceRoll) : Hash01(lord.loadID, 0x5A17));
                lordCompetence[lord.loadID] = v;
            }
            roll = v.roll;
            return v.c;
        }

        public static bool KnowsLord(Lord lord, Doctrine d)
        {
            if (lord == null || !Enabled) return true;
            float c = OfLord(lord, out float roll);
            Vector2 b = Bands[(int)d];
            return Smooth(b.x, b.y, c) > roll;
        }

        public static string Pct(float c) => Mathf.RoundToInt(c * 100f) + "%";

        public static string Describe(AssaultPlan plan) =>
            !Enabled || plan.competenceWhy == null || plan.competenceWhy == "off" ? "" :
            "competence " + Of(plan).ToString("0.00") + (plan.commandLost ? " (was " + plan.competenceBase.ToString("0.00") + ")" : "") +
            " roll " + plan.competenceRoll.ToString("0.00") + " [" + plan.competenceWhy + "]";

        // ---------------- graded behaviour ----------------

        /// <summary>Weight of the defenders' fire in route planning: 0.2 (barely) .. 1 (full threat map).</summary>
        public static float FireScale(AssaultPlan plan) => Mathf.Lerp(0.2f, 1f, Of(plan));

        /// <summary>How far ahead (cells from where the raid stands) the command reads the defenders' fire.</summary>
        public static float LookAhead(AssaultPlan plan) => Mathf.Lerp(25f, 200f, Of(plan));

        /// <summary>Weakest fire the command notices: dull leaders only see the drafted guns and turrets.</summary>
        public static float MinFireNoticed(AssaultPlan plan) => 0.5f * (1f - Of(plan));

        /// <summary>Share of the colony's traps the raid knows about and walks around (vanilla smart raids 0.3).</summary>
        public static float TrapRatio(AssaultPlan plan) =>
            Mathf.Min(0.95f, 0.95f * Smooth(0.2f, 0.95f, plan == null || !Enabled ? 1f : plan.competence) + (plan?.trapMemoryBonus ?? 0f));

        /// <summary>Required odds x this: dull commanders attack at worse odds (overconfidence).</summary>
        public static float OddsFactor(AssaultPlan plan) => Mathf.Lerp(0.55f, 1f, Of(plan));

        /// <summary>Withdrawal thresholds: dull commanders keep going long after the attack has failed.</summary>
        public static float WithdrawLoss(AssaultPlan plan, float smart, float dull) => Mathf.Lerp(dull, smart, Of(plan));

        public static int SyncTimeout(AssaultPlan plan, int ticks) => Mathf.RoundToInt(ticks * Mathf.Lerp(0.5f, 1f, Of(plan)));

        /// <summary>Random extra cost for a breach candidate: high for dull commanders (poorly chosen breach points).</summary>
        public static float BreachNoise(AssaultPlan plan, float bearing)
        {
            if (plan == null || !Enabled) return 0f;
            float dull = 1f - Of(plan);
            return dull * dull * BreachNoiseCost * Hash01(plan.lordId, Mathf.RoundToInt(bearing) + 977);
        }

        public static int MaxAxes(AssaultPlan plan, int k)
        {
            if (k >= 3 && !Knows(plan, Doctrine.MultiAxis3)) k = 2;
            if (k >= 2 && !Knows(plan, Doctrine.MultiAxis2)) k = 1;
            return k;
        }

        // ---------------- perception scope (threat-map reading for the routes the command plans) ----------------

        public static AssaultPlan Ctx;
        public static IntVec3 CtxOrigin = IntVec3.Invalid;

        public struct Scope : IDisposable
        {
            private readonly AssaultPlan prev;
            private readonly IntVec3 prevOrigin;
            public Scope(AssaultPlan plan, IntVec3 origin)
            {
                prev = Ctx; prevOrigin = CtxOrigin;
                Ctx = plan; CtxOrigin = origin;
            }
            public void Dispose() { Ctx = prev; CtxOrigin = prevOrigin; }
        }

        /// <summary>Routes planned inside this scope (ThreatCustomizer) read the threat map as the raid's command can.</summary>
        public static Scope Perceive(AssaultPlan plan, IntVec3 origin) => new Scope(plan, origin);

        /// <summary>Cells of the colony's traps this raid knows of (vanilla's own per-faction pick), for its planned routes.</summary>
        public static void KnownTrapCells(Map map, AssaultPlan plan, List<int> into)
        {
            Pawn rep = plan.members.FirstOrDefault(p => p.Spawned && !p.Dead && p.Map == map);
            if (rep == null) return;
            var all = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is Building_Trap trap) || !trap.Spawned) continue;
                bool knows;
                try { knows = trap.KnowsOfTrap(rep); } catch { continue; }
                if (!knows) continue;
                foreach (IntVec3 c in trap.OccupiedRect()) if (c.InBounds(map)) into.Add(map.cellIndices.CellToIndex(c));
            }
        }

        // ---------------- traps: vanilla's per-lord "avoid trap ratio" from competence ----------------

        // read from path jobs (worker threads): an immutable snapshot, replaced on write (main thread)
        private static Dictionary<int, float> trapRatio = new Dictionary<int, float>();

        public static void PublishTrapRatio(AssaultPlan plan)
        {
            var d = new Dictionary<int, float>(trapRatio);
            d[plan.lordId] = TrapRatio(plan);
            trapRatio = d;
        }

        public static bool TryTrapRatio(int lordId, out float r) => trapRatio.TryGetValue(lordId, out r);

        public static void ResetForNewGame() => trapRatio = new Dictionary<int, float>();

        // ---------------- helpers ----------------

        public static float Smooth(float lo, float hi, float x)
        {
            if (x <= lo) return 0f;
            if (x >= hi) return 1f;
            float t = (x - lo) / (hi - lo);
            return t * t * (3f - 2f * t);
        }

        public static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>Vanilla 1.7 asks the assault lord which share of the colony's traps its raiders know (0.3 for "smart"
    /// raids, else 0). With tactical competence that share follows the raid's command: sharp raids walk around most
    /// traps and IEDs, dull ones walk into them.</summary>
    [HarmonyPatch(typeof(LordJob_AssaultColony), nameof(LordJob_AssaultColony.AvoidTrapRatio), MethodType.Getter)]
    public static class Patch_AvoidTrapRatio
    {
        public static void Postfix(LordJob_AssaultColony __instance, ref float __result)
        {
            if (!Competence.Enabled) return;
            Lord lord = __instance.lord;
            if (lord != null && Competence.TryTrapRatio(lord.loadID, out float r)) __result = r;
        }
    }

    /// <summary>Counts traps sprung by raiders (AI Lab metric; an int increment otherwise).</summary>
    [HarmonyPatch(typeof(Building_Trap), nameof(Building_Trap.Spring))]
    public static class Patch_TrapSpringCount
    {
        public static void Prefix(Building_Trap __instance, Pawn p)
        {
            if (p?.Faction != null && __instance.Faction == Faction.OfPlayer && p.Faction.HostileTo(Faction.OfPlayer)) Competence.TrapSprings++;
        }
    }
}
