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
    public enum PlanMode { Walk, Breach, Siege, Standoff, Scout, Sabotage }
    public enum PlanPhase { Approach, Assault, Converted, Withdraw }

    /// <summary>One raid's plan: envelopment from several directions, synchronised assault (design 3.8).</summary>
    public class AssaultPlan
    {
        public int lordId;
        public PlanMode mode;
        public PlanPhase phase;
        public IntVec3 gather, objective;
        public readonly List<IntVec3> axes = new List<IntVec3>();
        public readonly Dictionary<Pawn, int> axisOf = new Dictionary<Pawn, int>();
        public readonly Dictionary<Pawn, IntVec3> spotOf = new Dictionary<Pawn, IntVec3>();
        public readonly List<List<IntVec3>> waypoints = new List<List<IntVec3>>();   // per axis, wide-arc route
        public readonly List<List<IntVec3>> assaultRoutes = new List<List<IntVec3>>();   // per axis, H-hour: covered route in (fords, not bridges)
        public readonly Dictionary<Pawn, int> waypointIndex = new Dictionary<Pawn, int>();
        public float routeExposure;
        public int createdTick, hHourTick = -1, startCount;
        public string hHourReason = "";

        // breach mode (SOSRA): 1-3 breach points on different sides, opened together; each group stacks beside its own
        public readonly List<BreachPoint> breaches = new List<BreachPoint>();
        public readonly Dictionary<Pawn, int> breachOf = new Dictionary<Pawn, int>();
        public readonly HashSet<Pawn> breachTeam = new HashSet<Pawn>();   // all specialists (any breach point)
        public string capability = "";
        public bool pendingConvert;
        public int aliveAtHHour;
        public bool replanned;
        public int lastBreachProgressTick = -1, lastBreachDamage, breachReplans;
        public int firstReadyTick = -1, goTick = -1, firstThroughTick = -1;
        public readonly HashSet<Pawn> entered = new HashSet<Pawn>();

        // raid goal (0.2): assault, scout or sabotage (RaidGoals); breach gear brought for an assault (BreachKit.EnsureMeans)
        public RaidGoal goal = RaidGoal.Assault;
        public string goalWhy = "", goalLeaveWhy = "", breachGear = "";
        public int goalEndTick = -1, lastGoalJobTick = -1, observedTicks, scoutPostsWithSight;
        public Pawn ambushTarget;
        public readonly List<IntVec3> scoutPosts = new List<IntVec3>();
        public readonly List<IntVec3> seeingPosts = new List<IntVec3>();
        public readonly List<CswTeam> csw = new List<CswTeam>();   // crew-served weapons (0.2)
        // field fight (0.2): defenders out of their base
        public bool fieldFight;
        public int fieldLastSeen = -1;
        public bool defenceBroken;
        public int lastAdaptTick = -1;
        public readonly List<Pawn> fieldEnemies = new List<Pawn>();
        public readonly Dictionary<Pawn, IntVec3> flankOf = new Dictionary<Pawn, IntVec3>();
        public readonly Dictionary<Pawn, List<IntVec3>> goalRouteOf = new Dictionary<Pawn, List<IntVec3>>();

        // force ratio (attackers : fortified defenders)
        public float forceRatio = -1f, requiredRatio = -1f;
        public float defendedBearing = -1f;   // the way the defence faces (its funnel mouth / door), from the objective

        // combined arms (0.6)
        public readonly Dictionary<Pawn, RaidRole> roleOf = new Dictionary<Pawn, RaidRole>();
        public readonly Dictionary<Pawn, IntVec3> overwatchOf = new Dictionary<Pawn, IntVec3>();   // breach overwatch: covered firing position facing the breach
        public readonly Dictionary<Pawn, List<Pawn>> followersOf = new Dictionary<Pawn, List<Pawn>>();   // shield bearer -> gunners in its shadow
        public readonly Dictionary<Pawn, Pawn> shieldOf = new Dictionary<Pawn, Pawn>();                    // follower -> its shield bearer
        public readonly Dictionary<Pawn, int> shieldWaitTicks = new Dictionary<Pawn, int>();
        public readonly Dictionary<Pawn, IntVec3> shieldBoundFrom = new Dictionary<Pawn, IntVec3>();   // shield advance: start of the current bound
        public readonly Dictionary<Pawn, int> shieldCrouchUntil = new Dictionary<Pawn, int>();
        public readonly Dictionary<IntVec3, int> funnelSeenTick = new Dictionary<IntVec3, int>();   // inner funnels: first seen (hold timer per mouth)
        public readonly Dictionary<Pawn, IntVec3> funnelHoldOf = new Dictionary<Pawn, IntVec3>();
        public readonly HashSet<IntVec3> funnelSmoked = new HashSet<IntVec3>();
        public readonly Dictionary<Pawn, Plunder.Task> plunderTaskOf = new Dictionary<Pawn, Plunder.Task>();   // plunder: first choice per raider
        public int plunderStartTick = -1, lastPlunderJobTick = -1;
        public Room gasRoom;                         // gas raid: the defenders' enclosed room
        public readonly Dictionary<int, int> axisGoTick = new Dictionary<int, int>();       // walk assault: staggered start per axis
        public readonly Dictionary<Pawn, int> assaultElementOf = new Dictionary<Pawn, int>(); // fire and movement: half 0 / 1
        public readonly HashSet<Pawn> raidingParty = new HashSet<Pawn>();   // plunders from the start (big raids)
        public CrossingMode crossing;
        public int flankAxis = -1, flankTick = -1;   // crossing: the maneuver element's axis while the base of fire pins
        public bool breachTried;
        public bool reangled;                        // crossing: already looked for another angle once                // walk mode: how the open ground is crossed
        public int crossingTick = -1, openCells, defendersAtCrossing;
        public int lastShotTick = -1;   // a member last fired a ranged shot (attrition that fires nothing ends early)
        // raid memory (FactionMemory): who the raid was, what downed each member, what it learned / brought
        public Faction faction;
        public Map map;
        public float defArmour;
        public readonly Dictionary<Pawn, string> downedBy = new Dictionary<Pawn, string>();
        public bool memoryDone;
        // shield breach entry (playtest 2026-10-25): bearers through the gap, side counter for the wide clear points
        public readonly HashSet<Pawn> shieldThrough = new HashSet<Pawn>();
        public readonly Dictionary<IntVec3, int> clearCount = new Dictionary<IntVec3, int>();
        public string memoryOutcome = "", memoryThreat = "";
        public List<string> memoryAdaptations = new List<string>();
        // satisfied-exit hold (SatisfiedHold): progress since H-hour, our wall damage kept out of the colony-damage count
        public int satCheckTick = -1, satProgressTick = -1, satDefenders = -1, satOpen;
        public bool satReleased, satWinning;
        public float wallDamage, wallDamageCredited;
        public bool gasFlammable;
        public readonly HashSet<IntVec3> gasOpenings = new HashSet<IntVec3>();
        public Pawn leader;
        // tactical competence (Competence): tech ceiling x the leader's intelligence; drops on loss of command
        public float competence = 1f, competenceBase = 1f, competenceRoll, techCeiling = 1f, leaderIntel = 1f, trapMemoryBonus;
        public Pawn competenceLeader;
        public string competenceWhy;
        public bool commandLost;
        public int shockUntil = -1;
        public int suppressStartTick = -1;
        public float pinnedAtHHour = -1f;

        // planned withdrawal (0.6)
        public int withdrawTick = -1, movingElement, boundTick;
        public IntVec3 exitCell = IntVec3.Invalid;
        public readonly List<IntVec3> withdrawRoute = new List<IntVec3>();
        public readonly Dictionary<Pawn, int> elementOf = new Dictionary<Pawn, int>();

        // arson (0.7)
        public readonly List<ArsonSite> arsonSites = new List<ArsonSite>();
        public readonly Dictionary<Pawn, ArsonSite> arsonistOf = new Dictionary<Pawn, ArsonSite>();

        // own casualty count (the vanilla lord counter also counts pain-fleeing raiders) and the exchange
        public readonly HashSet<Pawn> members = new HashSet<Pawn>();
        public readonly Dictionary<Pawn, ThrowOrder> throwOrders = new Dictionary<Pawn, ThrowOrder>();
        public readonly HashSet<int> fragOutDone = new HashSet<int>();   // breach indices already cleared with grenades
        public int lastThrowTick = -1;                                   // last frag thrown (the rush waits for it to go off)
        public readonly List<(IntVec3 cell, float radius, int until)> dangerZones = new List<(IntVec3, float, int)>();   // fresh blasts: keep out
        public int defendersAtStart;

        public IEnumerable<BreachPoint> ActiveBreaches => breaches.Where(b => !b.abandoned);

        public BreachPoint BreachFor(Pawn p) =>
            breachOf.TryGetValue(p, out int i) && i >= 0 && i < breaches.Count ? breaches[i] : null;

        public string Describe() =>
            mode + (mode == PlanMode.Walk
                ? " axes=" + axes.Count + " [" + string.Join(" ", axes.Select(a => Mathf.RoundToInt(AssaultPlanner.Bearing(objective, a)) + "deg/" + Mathf.RoundToInt(a.DistanceTo(objective)) + "c")) + "] routeExposure=" + routeExposure.ToString("0.0")
                : mode == PlanMode.Breach ? " breaches=" + breaches.Count + " [" + string.Join("; ", breaches.Select(b => b.Describe())) + "]" : "") +
            " objective=" + objective + " [" + capability + "]" +
            (goalWhy.Length > 0 ? " goal=" + goalWhy : "") +
            (roleOf.Count > 0 ? " roles[" + RaidRoles.Describe(this) + "]" : "") +
            (arsonSites.Count > 0 ? " arson[" + string.Join(" ", arsonSites.Select(a => a.Describe())) + "]" : "") +
            (forceRatio >= 0f ? " force " + forceRatio.ToString("0.00") + " vs " + requiredRatio.ToString("0.00") : "") +
            (hHourTick >= 0 ? " H-hour+" + (hHourTick - createdTick) + " (" + hHourReason + ")" : "") +
            (Competence.Enabled && competenceWhy != null ? " " + Competence.Describe(this) : "");
    }

    public static class AssaultPlanner
    {
        public static readonly Dictionary<int, AssaultPlan> Plans = new Dictionary<int, AssaultPlan>();
        private static readonly HashSet<int> creating = new HashSet<int>();
        public static int ReentryBlocked;
        public static AssaultPlan LastPlan;

        public static float Bearing(IntVec3 from, IntVec3 to) =>
            (Mathf.Atan2(to.z - from.z, to.x - from.x) * Mathf.Rad2Deg + 360f) % 360f;

        private static float AngleDiff(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        public static bool Applies(Lord lord)
        {
            var s = TacticalAIMod.Settings;
            return s != null && s.assaultPlanner && lord?.faction != null && lord.faction != Faction.OfPlayer
                   && lord.faction.HostileTo(Faction.OfPlayer) && lord.Map != null && lord.Map.IsPlayerHome;
        }

        public static AssaultPlan GetOrCreate(Lord lord)
        {
            if (Plans.TryGetValue(lord.loadID, out var p)) return p;
            // planning hands out gear and changes loadouts: should that ever update the lord's duties and land here again,
            // one plan per raid (a safety net; not seen in the Lab - ReentryBlocked stayed 0)
            if (!creating.Add(lord.loadID)) { ReentryBlocked++; return null; }
            try { p = Create(lord); }
            finally { creating.Remove(lord.loadID); }
            if (p != null) { Plans[lord.loadID] = p; LastPlan = p; }
            return p;
        }

        private static AssaultPlan Create(Lord lord)
        {
            Map map = lord.Map;
            var pawns = lord.ownedPawns.Where(x => x.Spawned && !x.Downed).ToList();
            if (pawns.Count == 0) return null;
            var plan = new AssaultPlan { lordId = lord.loadID, createdTick = Find.TickManager.TicksGame, startCount = pawns.Count, faction = lord.faction, map = map };
            if (AILab.Active && AILab.Config.stripBreachMeans) LabDefenders.StripBreachMeans(pawns);   // Lab: kit tests
            foreach (var p in pawns) plan.members.Add(p);
            Competence.Init(plan, lord, pawns);
            var defsNow = Suppression.Defenders(map, map.Center, 999f);
            if (defsNow.Count > 0) plan.defArmour = defsNow.Average(d => d.GetStatValue(StatDefOf.ArmorRating_Sharp));
            int sx = 0, sz = 0;
            foreach (var x in pawns) { sx += x.Position.x; sz += x.Position.z; }
            plan.gather = new IntVec3(sx / pawns.Count, 0, sz / pawns.Count);
            if (!plan.gather.Standable(map)) plan.gather = pawns[0].Position;
            plan.goal = RaidGoals.Roll(lord, plan, pawns, out plan.goalWhy);
            // every route this raid plans reads the threat map as far and as well as its command can
            using (Competence.Perceive(plan, plan.gather)) return Build(lord, plan, pawns, map);
        }

        private static AssaultPlan Build(Lord lord, AssaultPlan plan, List<Pawn> pawns, Map map)
        {
            var s0 = TacticalAIMod.Settings;
            // raid memory only if the command makes use of what the survivors told
            string memWhy = null;
            PlanMode? remembered = Competence.Knows(plan, Doctrine.Memory) ? FactionMemory.PreferredMode(lord.faction, true, out memWhy) : null;
            LoadoutFix.Apply(pawns);        // shield + a gun it blocks: one-handed sidearm, else the shield into the pack
            Launchers.SwapGasAmmo(pawns);   // gas-only launchers take explosive rounds (before the breach planning counts launchers)
            plan.objective = Objective(map, plan.gather);
            if (plan.goal != RaidGoal.Assault)
            {
                // scouting and sabotage need no way in (playtest 2026-10-25)
                RaidGoals.Plan(lord, plan, pawns, map);
                plan.defendersAtStart = Suppression.Defenders(map, plan.objective, 40f).Count;
                RaidRoles.Assign(plan, pawns);
                Log(plan);
                return plan;
            }
            bool avoidFunnels = s0.avoidFunnels && Competence.Knows(plan, Doctrine.AvoidFunnels);

            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            bool canWalk;
            using (PawnPath direct = map.pathFinder.FindPathNow(plan.gather, plan.objective, walk, null, PathEndMode.Touch))
                canWalk = direct != null && direct.Found;
            // raids bring their breaching gear; whether they use it is the plan's decision
            BreachKit.EquipOnce(lord, force: true);
            Grenades.EquipOnce(lord, plan, pawns);
            if (!canWalk)
            {
                PlanBreach(lord, plan, pawns);
            }
            else
            {
                // a way in on foot exists - but the faction remembers walking in and failing: breach instead (if it can)
                if (remembered == PlanMode.Breach)
                {
                    PlanBreach(lord, plan, pawns, null, fallbackToWalk: true);
                    plan.capability = memWhy + (plan.mode == PlanMode.Breach ? "; " : " but cannot breach; ") + plan.capability;
                    if (plan.mode == PlanMode.Breach) FactionMemory.ApproachChanges++;
                }
                // a way in on foot exists - but if it's a covered funnel (the one entrance of a killbox), make new ones
                else if (avoidFunnels && TryFindNewEntrance(map, plan.gather, plan.objective, TacticalAIMod.Settings.newEntranceRatio, out var entryNodes, out string why, TacticalAIMod.Settings.minFunnelCells))
                {
                    PlanBreach(lord, plan, pawns, entryNodes, fallbackToWalk: true);
                    if (plan.mode == PlanMode.Breach) plan.capability = why + "; " + plan.capability;
                    else plan.capability = why + " but cannot breach; " + plan.capability;
                    // a sharp command doesn't walk into a killbox it can't get round: siege (factions that can) or stand off,
                    // snipe and plunder from out of reach (cp3 killbox: walked in, then sat out the run)
                    if (plan.mode == PlanMode.Walk && Competence.Knows(plan, Doctrine.FunnelCaution))
                    {
                        plan.mode = lord.faction.def.canSiege ? PlanMode.Siege : PlanMode.Standoff;
                        plan.pendingConvert = plan.mode == PlanMode.Siege;
                        plan.capability += " -> killbox, no way in: " + plan.mode;
                        if (plan.mode == PlanMode.Standoff) ChooseStandoff(map, plan, pawns);
                    }
                }
                else if (avoidFunnels && TacticalAIMod.Settings.tunnelling
                         && CountFunnelCells(map, plan.gather, plan.objective) >= TacticalAIMod.Settings.minFunnelCells)
                {
                    // the way in is a funnel but there is nothing of the player's to blow (mountain base: natural rock):
                    // a tunnel through thin rock, if the raid can dig it in time
                    PlanBreach(lord, plan, pawns, null, fallbackToWalk: true);
                    plan.capability = "funnel, no walls to breach" + (plan.mode == PlanMode.Breach ? " -> tunnel; " : "; ") + plan.capability;
                }
                if (plan.mode == PlanMode.Walk)
                {
                    ChooseAxes(map, plan, pawns.Count, walk);
                    if (plan.axes.Count == 0) plan.axes.Add(plan.objective);
                    Assign(plan, pawns);
                }
            }
            foreach (var p in pawns) plan.members.Add(p);
            plan.defendersAtStart = Suppression.Defenders(map, plan.objective, 40f).Count;
            RaidRoles.Assign(plan, pawns);
            FactionMemory.AdaptLoadouts(lord, plan, pawns);   // after the roles: medics and breachers keep their kit
            ShieldWall.Assign(plan, pawns);
            CrewWeapons.Plan(lord, plan, pawns);   // after the roles: gunner and bearers from the riflemen and base of fire
            Grenades.EquipOnce(lord, plan, pawns);
            GasRaid.Plan(lord, plan, pawns);
            JobGiver_HSKTAI_RaidingParty.Assign(plan, pawns);
            CheckForceRatioAtPlanning(lord, plan, pawns);
            // the faction remembers a breach that failed: siege (factions that can)
            if (remembered == PlanMode.Siege && (plan.mode == PlanMode.Breach || plan.mode == PlanMode.Walk))
            {
                ClearBreaches(plan);
                plan.axes.Clear(); plan.axisOf.Clear(); plan.spotOf.Clear(); plan.waypoints.Clear(); plan.waypointIndex.Clear();
                plan.mode = PlanMode.Siege;
                plan.pendingConvert = true;
                plan.capability += "; " + memWhy;
                FactionMemory.ApproachChanges++;
            }
            if (plan.mode == PlanMode.Breach || plan.mode == PlanMode.Walk || plan.mode == PlanMode.Standoff) Arson.Plan(map, plan, pawns);
            Log(plan);
            return plan;
        }

        private static void Log(AssaultPlan plan)
        {
            if (Prefs.DevMode) Verse.Log.Message("[HSK Tactical AI] assault plan: " + plan.Describe());
        }

        /// <summary>The defenders' centre (threat-map sources); else the colony's buildings; else the map centre.</summary>
        private static IntVec3 Objective(Map map, IntVec3 from)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            var src = tm?.SourcePositions;
            if (src != null && src.Count > 0)
            {
                int x = 0, z = 0;
                foreach (var c in src) { x += c.x; z += c.z; }
                return new IntVec3(x / src.Count, 0, z / src.Count);
            }
            var b = map.listerBuildings.allBuildingsColonist;
            if (b.Count > 0)
            {
                int x = 0, z = 0;
                foreach (var t in b) { x += t.Position.x; z += t.Position.z; }
                return new IntVec3(x / b.Count, 0, z / b.Count);
            }
            return map.Center;
        }

        // ---------------- force ratio ----------------

        /// <summary>
        /// Raw odds, attackers vs defenders (force scores), against the classic 3:1 for an assault (playtest, 2026-10-23: "in
        /// general the attack ratio should be about 3 to 1"; the 3:1 already allows for the defender's advantage).
        /// </summary>
        public static float ForceRatio(Map map, IEnumerable<Pawn> attackers, int directions, out float required)
        {
            var s = TacticalAIMod.Settings;
            required = s.assaultOdds;
            float d = ForceEstimate.Defenders(map);
            float a = ForceEstimate.Attackers(attackers);
            return d <= 0.01f ? 99f : a / d;
        }

        private static void CheckForceRatioAtPlanning(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            if (plan.mode != PlanMode.Breach && plan.mode != PlanMode.Walk) return;
            int dirs = plan.mode == PlanMode.Breach ? plan.breaches.Count : plan.axes.Count;
            plan.forceRatio = ForceRatio(lord.Map, pawns, dirs, out plan.requiredRatio);
            // dull commanders don't weigh the odds at all, middling ones are overconfident
            plan.requiredRatio *= Competence.OddsFactor(plan);
            if (!s.forceRatioCheck || !Competence.Knows(plan, Doctrine.ForceCheck) || plan.forceRatio >= plan.requiredRatio * s.commitRatioTolerance) return;   // "about" 3:1
            // too weak to storm the position: besiege it (factions that can), else hold out of reach and withdraw
            plan.capability += "; force " + plan.forceRatio.ToString("0.00") + " < " + plan.requiredRatio.ToString("0.00") + " -> no assault";
            ClearBreaches(plan);
            plan.axes.Clear(); plan.axisOf.Clear(); plan.spotOf.Clear(); plan.waypoints.Clear(); plan.waypointIndex.Clear();
            plan.mode = lord.faction.def.canSiege ? PlanMode.Siege : PlanMode.Standoff;
            plan.pendingConvert = plan.mode == PlanMode.Siege;
            if (plan.mode == PlanMode.Standoff) ChooseStandoff(lord.Map, plan, pawns);
        }

        // ---------------- breach / siege / standoff ----------------

        private static readonly HashSet<string> BreachTools = new HashSet<string>
        {
            "MeleeWeapon_BreachAxe", "MeleeWeapon_Sledgehammer", "MeleeWeapon_Warhammer", "HSK_GordonCrowbar",
            "PEMC_Tool_Jackhammer", "TFJ_Tool_Mining_Pickaxe", "TFJ_Tool_Paxe", "MeleeWeapon_Zeushammer"
        };

        public static bool HasBreachTool(Pawn p) => p.equipment?.Primary != null && BreachTools.Contains(p.equipment.Primary.def.defName);

        /// <summary>Wall HP one tool user takes down in reasonable time: breach axe full budget, other tools a fifth.</summary>
        public static float ToolBudget(Pawn p) =>
            !HasBreachTool(p) ? 0f : p.equipment.Primary.def.defName == "MeleeWeapon_BreachAxe" ? TacticalAIMod.Settings.toolHpBudget : TacticalAIMod.Settings.toolHpBudget * 0.2f;

        public static Verb DestructiveRangedVerb(Pawn p)
        {
            var eq = p.equipment?.PrimaryEq;
            if (eq == null) return null;
            foreach (Verb v in eq.AllVerbs) if (!v.IsMeleeAttack && v.verbProps.ai_IsBuildingDestroyer && Launchers.CanBreach(p, v)) return v;   // a round that breaks walls, loaded or carried (gas shells break none)
            return null;
        }

        private static readonly HashSet<string> StrongBreachTools = new HashSet<string> { "MeleeWeapon_BreachAxe", "MeleeWeapon_Sledgehammer", "MeleeWeapon_Warhammer", "MeleeWeapon_Zeushammer" };

        /// <summary>Breach specialists: charges, launchers / thrown explosives, real breaching tools (not mining tools: 4-16 wall dps).</summary>
        public static bool IsSpecialist(Pawn p) => BreachKit.CarriedCharge(p) != null || DestructiveRangedVerb(p) != null
            || (p.equipment?.Primary != null && StrongBreachTools.Contains(p.equipment.Primary.def.defName));

        /// <summary>
        /// "If there is one entrance, that's the one to avoid": compare the walking route (priced by the defenders'
        /// fire, with a heavy penalty for narrow cells under fire = funnels, doors, killbox corridors) with a route that
        /// makes its own entrance through the player's walls (priced by wall HP and the same fire costs).
        /// </summary>
        public static bool TryFindNewEntrance(Map map, IntVec3 from, IntVec3 objective, float ratio, out List<IntVec3> breachNodes, out string why, int minFunnelCells = 0)
        {
            var s = TacticalAIMod.Settings;
            breachNodes = null;
            why = "";
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0) return false;
            float walkCost, breachCost;
            int funnelCells = 0, wallsOnBreach = 0;
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
            {
                if (!ThreatPathing.TryFindRoute(map, from, objective, customizer, out var walkNodes, out walkCost)) return false;
                foreach (var c in walkNodes)
                    if (tm.FireAt(c) > s.safeFire && ThreatCustomizer.OpenNeighbours(map, c) <= 4) funnelCells++;
                walkCost += funnelCells * s.funnelCellPenalty;
            }
            if (!TryBreachRoute(map, from, objective, tm, out breachNodes, out breachCost)) return false;
            foreach (var c in breachNodes)
            {
                Building ed = c.GetEdifice(map);
                if (ed != null && ed.Faction == Faction.OfPlayer && (ed.def.passability == Traversability.Impassable || ed.def.IsDoor)) wallsOnBreach++;
            }
            why = "funnel cells " + funnelCells + ", walk " + walkCost.ToString("0") + " vs new entrance " + breachCost.ToString("0");
            // no funnel on the way in = no reason to make a new entrance (envelopment around open obstacles is better)
            return wallsOnBreach > 0 && funnelCells >= minFunnelCells && breachCost < walkCost * ratio;
        }

        /// <summary>Narrow cells under fire (corridor, doorway, gap, bridge) on the covered walking route in.</summary>
        public static int CountFunnelCells(Map map, IntVec3 from, IntVec3 objective)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0) return 0;
            int n = 0;
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
            {
                if (!ThreatPathing.TryFindRoute(map, from, objective, customizer, out var nodes, out _)) return 0;
                foreach (var c in nodes)
                    if (tm.FireAt(c) > s.safeFire && ThreatCustomizer.OpenNeighbours(map, c) <= 4) n++;
            }
            return n;
        }

        /// <summary>
        /// Cheapest way in through the player's walls: walls cost their HP, rock is practically closed, the defenders'
        /// fire and funnels (narrow cells under fire) cost as on every other route. Nodes in walking order.
        /// </summary>
        public static bool TryBreachRoute(Map map, IntVec3 from, IntVec3 to, ThreatMap tm, out List<IntVec3> nodes, out float cost)
        {
            var s = TacticalAIMod.Settings;
            nodes = new List<IntVec3>();
            cost = 0f;
            var tuning = new PathFinderCostTuning
            {
                costBlockedWallBase = s.newEntranceWallCost, costBlockedWallExtraPerHitPoint = s.wallHpPenalty,
                costBlockedDoor = s.newEntranceWallCost, costBlockedDoorPerHitPoint = s.wallHpPenalty,
                costBlockedWallExtraForNaturalWalls = 20000
            };
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire, s.funnelCellPenalty, s.safeFire))
            using (PawnPath p = map.pathFinder.FindPathNow(from, to, TraverseParms.For(TraverseMode.PassAllDestroyableThings, Danger.Deadly), tuning, PathEndMode.OnCell, customizer))
            {
                if (p == null || !p.Found) return false;
                nodes.AddRange(p.NodesReversed);
                nodes.Reverse();
                cost = p.TotalCost;
                return true;
            }
        }

        /// <summary>
        /// Breach fallback (playtest: leave only if there is "no angle where they can sneak around or breach"): a walk-mode raid
        /// whose open-ground approach failed tries a breach plan - if the player has walls or doors within 25 cells of the
        /// defenders. True: the plan is now a breach plan (approach phase). Once per raid.
        /// </summary>
        public static bool TryBreachInstead(Lord lord, AssaultPlan plan, List<Pawn> alive)
        {
            Map map = lord.Map;
            plan.breachTried = true;
            if (alive.Count == 0) return false;
            if (!map.listerBuildings.allBuildingsColonist.Any(b => (b.def == ThingDefOf.Wall || b.def.IsDoor) && b.Position.DistanceTo(plan.objective) <= 25f)) return false;
            var keepAxes = plan.axes.ToList();
            var keepWps = plan.waypoints.Select(w => w.ToList()).ToList();
            var keepAxisOf = plan.axisOf.ToDictionary(kv => kv.Key, kv => kv.Value);
            var keepSpots = plan.spotOf.ToDictionary(kv => kv.Key, kv => kv.Value);
            IntVec3 keepGather = plan.gather;
            int cx = 0, cz = 0;
            foreach (var p in alive) { cx += p.Position.x; cz += p.Position.z; }
            var centre = new IntVec3(cx / alive.Count, 0, cz / alive.Count);
            if (centre.Standable(map)) plan.gather = centre;
            PlanBreach(lord, plan, alive, null, fallbackToWalk: true);
            if (plan.mode == PlanMode.Breach)
            {
                plan.phase = PlanPhase.Approach;
                plan.suppressStartTick = -1;
                plan.goTick = plan.firstReadyTick = plan.firstThroughTick = -1;
                plan.crossing = CrossingMode.None;
                plan.hHourReason = "open ground failed -> breach";
                Log(plan);
                lord.CurLordToil?.UpdateAllDuties();
                return true;
            }
            // nothing to breach: the walk plan as it was
            plan.mode = PlanMode.Walk;
            plan.gather = keepGather;
            plan.axes.Clear(); plan.axes.AddRange(keepAxes);
            plan.waypoints.Clear(); plan.waypoints.AddRange(keepWps);
            plan.axisOf.Clear(); foreach (var kv in keepAxisOf) plan.axisOf[kv.Key] = kv.Value;
            plan.spotOf.Clear(); foreach (var kv in keepSpots) plan.spotOf[kv.Key] = kv.Value;
            return false;
        }

        private static void ClearBreaches(AssaultPlan plan)
        {
            plan.breaches.Clear();
            plan.breachOf.Clear();
            plan.breachTeam.Clear();
            plan.waypointIndex.Clear();
            plan.spotOf.Clear();
            plan.entered.Clear();
            plan.firstReadyTick = plan.goTick = plan.firstThroughTick = -1;
        }

        /// <summary>
        /// A breach point from a route (start -> objective): the first contiguous layer of the player's walls on it,
        /// a stack point beside it (out of fire, outside the cone the defenders see through once it's open) and the
        /// covered route there. Null if the route crosses no player wall.
        /// </summary>
        private static BreachPoint BuildBreachPoint(Map map, AssaultPlan plan, List<IntVec3> nodes, List<IntVec3> leadIn, ThreatMap tm, out bool rockOnPath, bool allowRock = false)
        {
            var s = TacticalAIMod.Settings;
            rockOnPath = false;
            var bp = new BreachPoint();
            int firstWall = -1, lastWall = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                Building ed = nodes[i].GetEdifice(map);
                bool blocks = ed != null && (ed.def.passability == Traversability.Impassable || ed.def.IsDoor);
                if (blocks && ed.def.mineable)
                {
                    rockOnPath = true;
                    // mountain base: the rock layer on this line is the breach point (a tunnel), if nothing of the
                    // player's is in front of it
                    if (allowRock && (firstWall < 0 || bp.rock))
                    {
                        if (firstWall < 0) { firstWall = i; bp.rock = true; }
                        bp.walls.Add(ed);
                        bp.hp += ed.HitPoints;
                        lastWall = i;
                        continue;
                    }
                    if (firstWall >= 0) break;
                    continue;
                }
                if (bp.rock) break;   // end of the rock layer
                // only the player's walls and doors are breach points (ruins and other obstacles on the way are not)
                if (blocks && ed.Faction != Faction.OfPlayer) { if (firstWall >= 0) break; continue; }
                if (blocks)
                {
                    if (firstWall < 0) firstWall = i;
                    bp.walls.Add(ed);
                    bp.hp += ed.HitPoints;
                }
                else if (firstWall >= 0) break;   // first contiguous layer of the player's walls only
            }
            if (firstWall < 0) return null;
            bp.entry = nodes[firstWall];
            if (bp.rock && firstWall > 0) bp.line.AddRange(nodes.GetRange(firstWall - 1, Mathf.Min(nodes.Count, lastWall + 2) - (firstWall - 1)));
            if (bp.rock && bp.line.Count > 1)
            {
                // second lane one cell to the side (across the main direction): two faces in parallel, a 2-wide exit
                IntVec3 a = bp.line[0], b = bp.line[bp.line.Count - 1];
                IntVec3 side = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.z - a.z) ? new IntVec3(0, 0, 1) : new IntVec3(1, 0, 0);
                foreach (var c in bp.line)
                {
                    IntVec3 c2 = c + side;
                    bp.line2.Add(c2);
                    if (c2.InBounds(map) && c2.GetEdifice(map) is Building r2 && r2.def.mineable) { bp.walls2.Add(r2); }
                }
            }
            bp.bearing = Bearing(plan.objective, bp.entry);
            bp.flammable = bp.walls.All(w => w.GetStatValue(StatDefOf.Flammability) >= 0.5f);
            // staging: walking back from the breach point, the first cell out of the defenders' fire (min 6 cells from
            // the blast); else stagingDistance back
            int back = Mathf.Max(0, firstWall - s.stagingDistance);
            if (tm != null && tm.CompletedCycles > 0)
                for (int i = firstWall - 6; i >= 0; i--)
                    if (tm.FireAt(nodes[i]) <= s.safeFire) { back = i; break; }
            bp.staging = CellFinder.StandableCellNear(nodes[back], map, 4f);
            if (!bp.staging.IsValid) bp.staging = nodes[back];
            if (leadIn != null) bp.route.AddRange(leadIn);
            bp.route.AddRange(ThreatPathing.Waypoints(nodes.GetRange(0, back + 1), s.waypointSpacing));
            IntVec3 stack = FindStackPoint(map, bp, plan.objective, tm, nodes[back]);
            if (stack.IsValid) { bp.staging = stack; bp.route.Add(stack); }
            // charges go on the raid's side of the wall, never the inside face reached through the funnel
            if (firstWall > 0) bp.plantSide = nodes[firstWall - 1];
            return bp;
        }

        /// <summary>
        /// Breach plan: one breach point on the cheapest way in (or the given new-entrance route), plus up to
        /// <c>maxBreaches</c>-1 more on other sides (≥ minAxisSeparation apart), each with a method its specialists can
        /// actually carry out against its wall (fire for wood, charges, launchers, breach tools). Else siege (if the
        /// faction can) or stand off - or keep walking if this was only an alternative to a funnel.
        /// </summary>
        private static void PlanBreach(Lord lord, AssaultPlan plan, List<Pawn> pawns, List<IntVec3> givenNodes = null, bool fallbackToWalk = false)
        {
            Map map = lord.Map;
            var s = TacticalAIMod.Settings;
            BreachKit.EquipOnce(lord, force: true);
            BreachKit.EnsureMeans(lord, plan, pawns);   // a raid out to breach never comes without the means (playtest 2026-10-25)
            ClearBreaches(plan);
            var tm = MapComponent_TacticalAI.For(map)?.Threat;

            // the direct way in (cheapest breach route from where the raid is) ...
            var nodes = givenNodes;
            float directCost = 0f;
            if (!TryBreachRoute(map, plan.gather, plan.objective, tm, out var direct, out directCost)) direct = null;
            if (direct != null) nodes = direct;
            if (nodes == null) nodes = new List<IntVec3>();
            BreachPoint primary = nodes.Count > 0 ? BuildBreachPoint(map, plan, nodes, null, tm, out bool rockOnPath) : null;
            bool rock = primary == null && nodes.Count > 0;

            // specialists and what they carry
            var pool = pawns.Where(IsSpecialist).ToList();
            foreach (var p in pool) plan.breachTeam.Add(p);
            int charges = pool.Sum(p => BreachKit.CarriedCharge(p)?.stackCount ?? 0);
            int launchers = pool.Count(p => DestructiveRangedVerb(p) != null);
            int tools = pool.Count(p => HasBreachTool(p));
            plan.capability = "charges " + charges + ", launchers " + launchers + ", tools " + tools + (rock ? ", rock" : "") + (plan.breachGear.Length > 0 ? ", brought " + plan.breachGear : "");

            // ... competes with breach points from every side: breach where the defence isn't facing (flank / rear),
            // and from several sides at once
            plan.defendedBearing = DefendedBearing(map, plan, tm);
            var cands = new List<BreachPoint>();
            int want = Mathf.Min(s.maxBreaches, Mathf.Max(1, pawns.Count / Mathf.Max(1, s.raidersPerBreach)));
            if (!Competence.Knows(plan, Doctrine.MultiBreach)) want = 1;   // several breaches opened together take a sharp command
            cands.AddRange(RingCandidates(map, plan, tm, s));
            // launchers (M79 and the like) blow doors as well as walls (playtest 2026-10-25): every outer door is a breach point
            // too, priced like a one-wall breach (the facing door still gets the defended-axis penalty)
            if (s.doorBreach && pool.Any(p => DestructiveRangedVerb(p) != null))
                foreach (var dp in DoorCandidates(map, plan, tm, s))
                {
                    if (cands.Any(o => o.entry.DistanceTo(dp.entry) < 3f)) continue;
                    dp.cost = dp.cost * 2f + s.newEntranceWallCost;
                    cands.Add(dp);
                }
            // the direct way in only if no side yields a breach point (its cost isn't comparable: it includes the walk inside)
            if (cands.Count == 0 && primary != null) { primary.cost = directCost; cands.Add(primary); }
            // competence: only a command that reads the defence avoids the face it is turned to; dull ones pick their
            // breach point more or less at random
            bool readsDefence = Competence.Knows(plan, Doctrine.DefendedAxis);
            foreach (var c in cands) c.score = c.cost + (readsDefence ? AxisPenalty(plan, c.bearing, s) : 0f) + Competence.BreachNoise(plan, c.bearing);
            if (readsDefence && plan.defendedBearing >= 0f) cands.RemoveAll(c => AngleDiff(plan.defendedBearing, c.bearing) < s.defendedAxisExclude && cands.Count > 1);
            int sepRejected = 0, methodRejected = 0;
            foreach (var bp in cands.OrderBy(b => b.score))
            {
                if (plan.breaches.Count >= want) break;
                if (plan.breaches.Any(b => AngleDiff(b.bearing, bp.bearing) < s.minAxisSeparation || b.entry.DistanceTo(bp.entry) < 8f)) { sepRejected++; continue; }
                if (!(bp.rock ? TryAssignTunnel(bp, plan, pawns, pool, lord.faction.def.techLevel, s) : TryAssignMethod(bp, pool, s))) { methodRejected++; continue; }
                bp.index = plan.breaches.Count;
                plan.breaches.Add(bp);
            }
            // doors: an extra simultaneous breach (grenades blow most doors: Bomb x2 vs doors); an existing entrance, so not
            // excluded for facing the defence - half the facing penalty
            int doorsUsed = 0;
            if (s.doorBreach && plan.breaches.Count > 0 && Competence.Knows(plan, Doctrine.MultiBreach))
                foreach (var dp in DoorCandidates(map, plan, tm, s).OrderBy(d => d.cost + AxisPenalty(plan, d.bearing, s) * 0.5f))
                {
                    if (doorsUsed >= 1) break;
                    if (plan.breaches.Any(b => b.entry.DistanceTo(dp.entry) < 6f)) continue;
                    if (!TryAssignDoor(dp, plan, pawns, pool, s)) continue;
                    dp.index = plan.breaches.Count;
                    plan.breaches.Add(dp);
                    doorsUsed++;
                }
            // nothing on the walls: a door is the way in (grenades, a charge, a launcher, or melee raiders breaking it down)
            if (plan.breaches.Count == 0 && s.doorBreach)
                foreach (var dp in DoorCandidates(map, plan, tm, s).OrderBy(d => d.cost + (readsDefence ? AxisPenalty(plan, d.bearing, s) * 0.5f : 0f) + Competence.BreachNoise(plan, d.bearing)))
                {
                    if (!TryAssignDoor(dp, plan, pawns, pool, s)) continue;
                    dp.index = plan.breaches.Count;
                    plan.breaches.Add(dp);
                    doorsUsed++;
                    break;
                }
            plan.capability += "; candidates " + cands.Count + " (too close " + sepRejected + ", no method " + methodRejected + ")" +
                               (plan.defendedBearing >= 0f ? "; defended axis " + Mathf.RoundToInt(plan.defendedBearing) + "deg" : "");
            if (plan.breaches.Count > 0)
            {
                AssignBreachGroups(plan, pawns);
                RaidRoles.Assign(plan, pawns);
                ShieldWall.Assign(plan, pawns);
                plan.capability += " -> breach x" + plan.breaches.Count;
                plan.mode = PlanMode.Breach;
                plan.lastBreachProgressTick = Find.TickManager.TicksGame;
                plan.lastBreachDamage = 0;
                return;
            }

            plan.capability += cands.Count == 0 ? " -> no breach point" : " -> cannot breach (hp " + cands.Min(c => c.hp).ToString("0") + ")";
            ClearBreaches(plan);
            if (fallbackToWalk)
            {
                plan.mode = PlanMode.Walk;
                return;
            }
            plan.mode = lord.faction.def.canSiege ? PlanMode.Siege : PlanMode.Standoff;
            plan.pendingConvert = plan.mode == PlanMode.Siege;
            if (plan.mode == PlanMode.Standoff) ChooseStandoff(map, plan, pawns);
        }

        /// <summary>
        /// Method for one breach point, taking its specialists out of the pool: charges (enough to bring the wall down),
        /// then launchers / thrown explosives (from a firing position), then breach tools (enough for the wall's HP).
        /// Last resort for wooden walls only: fire - any raider can set it alight, but it burns slowly (minutes per cell).
        /// </summary>
        /// <summary>
        /// Tunnel through a rock layer (mountain base): the two best miners not needed elsewhere dig one face with the
        /// tool their tech level gets from the kit (jackhammer Industrial+, pickaxe below). Only if the estimated digging
        /// time (HP / (80 x rock factor) hits x ticks per hit at the miner's speed) is within maxTunnelTicks.
        /// </summary>
        private static bool TryAssignTunnel(BreachPoint bp, AssaultPlan plan, List<Pawn> all, List<Pawn> pool, TechLevel tech, TacticalAISettings s)
        {
            if (!s.tunnelling || bp.line.Count < 2) return false;
            // miners: raiders without a gun first (they can hold the digging tool), then the best miners
            var miners = all.Where(p => p.RaceProps.Humanlike && !p.Downed && !p.WorkTypeIsDisabled(WorkTypeDefOf.Mining)
                                        && !pool.Contains(p) && !plan.breaches.Any(b => b.team.Contains(p)))
                            .OrderBy(p => p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon ? 1 : 0)
                            .ThenByDescending(p => p.skills?.GetSkill(SkillDefOf.Mining)?.Level ?? 0).Take(2).ToList();
            if (miners.Count == 0) return false;
            // honest estimate per lane: a tool only counts if it will be in hand (no gun to put away)
            float LaneTicks(Pawn m, IEnumerable<Building> rock)
            {
                bool inHand = true;   // gunners sling the gun and dig with the tool (EquipToolForDigging)
                float tool = inHand ? (s.equipRaiders ? Math.Max(BreachKit.OwnMiningGain(m), BreachKit.MiningToolFactor(tech)) : BreachKit.OwnMiningGain(m)) : 1f;   // without extra equipment: their own tools only
                float speed = Mathf.Max(0.6f, m.GetStatValue(StatDefOf.MiningSpeed) * (inHand ? tool / 0.8f : 1f));
                int ticksPerHit = Mathf.Max(1, Mathf.RoundToInt(100f / speed));
                float t = 0f;
                foreach (var w in rock)
                {
                    float factor = w.GetType().Name.Contains("Mineral") ? s.rockMineFactor : 1f;   // Minerals rock vs vanilla
                    t += Mathf.Ceil(w.HitPoints / (80f * factor)) * ticksPerHit;
                }
                return t;
            }
            // Industrial+: blast through with mining charges (one blast clears about two cells along the line, both lanes)
            bool blast = tech >= TechLevel.Industrial && s.breachKit && s.equipRaiders && TAI_DefOf.HSKTAI_MiningCharge != null;   // mining charges only come as extra equipment
            float eta = blast ? Mathf.Ceil(bp.walls.Count / 2f) * 750f
                              : LaneTicks(miners[0], bp.walls);
            if (!blast && miners.Count > 1 && bp.walls2.Count > 0) eta = Mathf.Max(eta, LaneTicks(miners[1], bp.walls2));
            bp.etaTicks = eta;
            if (eta > s.maxTunnelTicks) return false;
            if (blast)
            {
                bp.blasting = true;
                BreachKit.GiveMiningCharges(miners[0], bp.walls.Count);
                if (miners.Count > 1) BreachKit.GiveMiningCharges(miners[1], 2);   // backup sapper
            }
            for (int i = 0; i < miners.Count; i++)
            {
                BreachKit.GiveMiningTool(miners[i], tech);
                bp.team.Add(miners[i]);
                bp.laneOf[miners[i]] = i;
                plan.breachTeam.Add(miners[i]);
            }
            bp.method = BreachMethod.Tunnel;
            return true;
        }

        private static bool TryAssignMethod(BreachPoint bp, List<Pawn> pool, TacticalAISettings s)
        {
            int need = Mathf.Max(1, Mathf.CeilToInt(bp.hp * 0.9f / Mathf.Max(1f, s.chargeWallDamageEstimate)));
            var carriers = pool.Where(p => BreachKit.CarriedCharge(p) != null).OrderByDescending(p => BreachKit.CarriedCharge(p).stackCount).ToList();
            if (carriers.Sum(p => BreachKit.CarriedCharge(p).stackCount) >= need)
            {
                int have = 0;
                foreach (var p in carriers)
                {
                    if (have >= need && bp.team.Count >= 1) break;
                    bp.team.Add(p);
                    have += BreachKit.CarriedCharge(p).stackCount;
                }
                bp.method = BreachMethod.Charge;
            }
            else if (pool.Any(p => DestructiveRangedVerb(p) != null))
            {
                foreach (var p in pool.Where(p => DestructiveRangedVerb(p) != null).Take(1)) bp.team.Add(p);
                bp.method = BreachMethod.Launcher;
            }
            else
            {
                // breach axe (Demolish) counts fully; other "tools" do 4-16 wall damage per second: a fifth
                var toolers = pool.Where(HasBreachTool).OrderByDescending(ToolBudget).ToList();
                if (toolers.Count == 0 || bp.hp > toolers.Sum(ToolBudget))
                {
                    if (!bp.flammable || !s.fireBreaching) return false;
                    bp.method = BreachMethod.Fire;   // team filled from the group when the groups are assigned
                    return true;
                }
                float got = 0f;
                foreach (var p in toolers) { if (got >= bp.hp * 1.3f) break; bp.team.Add(p); got += ToolBudget(p); }
                bp.method = BreachMethod.Tool;
            }
            pool.RemoveAll(bp.team.Contains);
            return true;
        }

        /// <summary>
        /// Breach point candidates from every side: around the objective, every 20°, the closest dead-ground cell
        /// outside the walls (reachable on foot) is a launch point; the cheapest breach route from there gives a
        /// candidate (cost = breach route + covered walk to the launch point).
        /// </summary>
        private static List<BreachPoint> RingCandidates(Map map, AssaultPlan plan, ThreatMap tm, TacticalAISettings s)
        {
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            var cands = new List<BreachPoint>();
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire, s.funnelCellPenalty, s.safeFire))
            {
                for (int a = 0; a < 360; a += 10)
                {
                    float rad = a * Mathf.Deg2Rad;
                    IntVec3 launch = IntVec3.Invalid;
                    for (int r = 6; r <= 60; r += 2)
                    {
                        var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                        if (!c.InBounds(map)) break;
                        if (!c.Standable(map) || c.GetTerrain(map).IsWater) continue;
                        if (haveThreat && tm.FireAt(c) > s.safeFire) continue;
                        if (!map.reachability.CanReach(plan.gather, c, PathEndMode.OnCell, walk)) continue;
                        launch = c;
                        break;
                    }
                    if (!launch.IsValid) continue;
                    // the wall on THIS side: straight line from the launch point towards the defenders, first player wall
                    // layer on it (a pathfinder would walk round outside to the wall nearest the defenders, because
                    // crossing the base under fire costs more - that is the side they face)
                    List<IntVec3> nodes = Cardinalize(GenSight.BresenhamCellsBetween(launch, plan.objective));
                    if (!ThreatPathing.TryFindRoute(map, plan.gather, launch, customizer, out var lead, out float lcost)) continue;
                    var bp = BuildBreachPoint(map, plan, nodes, ThreatPathing.Waypoints(lead, s.waypointSpacing), tm, out bool rockFirst, allowRock: s.tunnelling);
                    if (bp == null) continue;
                    if (cands.Any(o => o.entry.DistanceTo(bp.entry) < 3f)) continue;   // same wall from a neighbouring bearing
                    // cost: covered walk there + the walls + the fire inside between the breach and the defenders
                    float inside = 0f;
                    bool past = false;
                    foreach (var c in nodes)
                    {
                        if (c == bp.entry) past = true;
                        else if (past && haveThreat) inside += tm.FireAt(c);
                    }
                    bp.cost = lcost + bp.walls.Count * s.newEntranceWallCost + bp.hp * s.wallHpPenalty + inside * s.routeBaseCostInFire;
                    // tunnels: the thinnest rock towards the base wins (each rock cell is a blast or hours of digging)
                    if (bp.rock) bp.cost = lcost * 0.3f + bp.walls.Count * 3000f + inside * s.routeBaseCostInFire;
                    cands.Add(bp);
                }
            }
            return cands;
        }

        /// <summary>A straight line made walkable step by step: diagonal steps get an intermediate cell (pawns can't pass
        /// diagonally between two rock corners, so a dug tunnel must be 4-connected).</summary>
        private static List<IntVec3> Cardinalize(List<IntVec3> line)
        {
            var r = new List<IntVec3>();
            foreach (var c in line)
            {
                if (r.Count > 0)
                {
                    var p = r[r.Count - 1];
                    if (p.x != c.x && p.z != c.z) r.Add(new IntVec3(c.x, 0, p.z));
                }
                r.Add(c);
            }
            return r;
        }

        /// <summary>
        /// Player doors as breach points: the outside cell next to the door (reachable on foot, away from the defenders),
        /// a stack point beside it, the covered walk there. Cost: half the walk + 0.3 per door HP.
        /// </summary>
        private static List<BreachPoint> DoorCandidates(Map map, AssaultPlan plan, ThreatMap tm, TacticalAISettings s)
        {
            var list = new List<BreachPoint>();
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
            {
                foreach (Building b in map.listerBuildings.allBuildingsColonist)
                {
                    if (!b.def.IsDoor || !b.Spawned) continue;
                    IntVec3 outside = IntVec3.Invalid;
                    float bestD = float.MaxValue;
                    foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(b))
                    {
                        if (!c.InBounds(map) || !c.Standable(map)) continue;
                        if (c.DistanceTo(plan.objective) <= b.Position.DistanceTo(plan.objective)) continue;   // the outside face
                        if (!map.reachability.CanReach(plan.gather, c, PathEndMode.OnCell, walk)) continue;
                        float d = c.DistanceToSquared(plan.gather);
                        if (d < bestD) { bestD = d; outside = c; }
                    }
                    if (!outside.IsValid) continue;
                    if (!ThreatPathing.TryFindRoute(map, plan.gather, outside, customizer, out var lead, out float lcost)) continue;
                    var bp = new BreachPoint { door = true, entry = b.Position, plantSide = outside, hp = b.HitPoints,
                                               bearing = Bearing(plan.objective, b.Position) };
                    bp.walls.Add(b);
                    bp.route.AddRange(ThreatPathing.Waypoints(lead, s.waypointSpacing));
                    IntVec3 stack = FindStackPoint(map, bp, plan.objective, tm, outside);
                    bp.staging = stack.IsValid ? stack : lead[Mathf.Max(0, lead.Count - 7)];
                    bp.route.Add(bp.staging);
                    bp.cost = lcost * 0.5f + bp.hp * s.wallHpPenalty;
                    list.Add(bp);
                }
            }
            return list;
        }

        /// <summary>A door: a charge if a carrier is free, else grenades (enough frags for its HP), else as a wall.</summary>
        private static bool TryAssignDoor(BreachPoint bp, AssaultPlan plan, List<Pawn> all, List<Pawn> pool, TacticalAISettings s)
        {
            if (pool.Any(p => BreachKit.CarriedCharge(p) != null) && TryAssignMethod(bp, pool, s)) return true;
            int need = Mathf.Max(1, Mathf.CeilToInt(bp.hp / Mathf.Max(1f, s.fragDoorDamage)));
            var throwers = all.Where(p => !p.Downed && Grenades.CarriedFrag(p) != null && !plan.breaches.Any(b => b.team.Contains(p)) && !pool.Contains(p))
                              .ToList();
            int have = 0;
            foreach (var p in throwers)
            {
                if (have >= need) break;
                bp.team.Add(p);
                plan.breachTeam.Add(p);
                have += Grenades.CarriedFrag(p).stackCount;
            }
            if (have >= need) { bp.method = BreachMethod.Grenade; return true; }
            foreach (var p in bp.team) plan.breachTeam.Remove(p);
            bp.team.Clear();
            if (TryAssignMethod(bp, pool, s)) return true;
            // last resort for a door: raiders with melee weapons break it down, as vanilla raiders do (playtest: "any strategy should be
            // viable" - cp5 tribal village: 24 tribals without explosives stood off and left)
            // melee weapons first, then anyone who can fight hand to hand (cp7 tribal village: every tribal carried a bow)
            var bashers = all.Where(p => !p.Downed && p.RaceProps.Humanlike && !p.WorkTagIsDisabled(WorkTags.Violent)
                                         && !plan.breaches.Any(b => b.team.Contains(p)) && !pool.Contains(p))
                             .OrderBy(p => p.equipment?.Primary != null && p.equipment.Primary.def.IsMeleeWeapon ? 0 : 1).Take(4).ToList();
            if (bashers.Count < 2 || bp.hp > 500f * bashers.Count) return false;
            foreach (var p in bashers) { bp.team.Add(p); plan.breachTeam.Add(p); }
            bp.method = BreachMethod.Bash;
            return true;
        }

        /// <summary>
        /// Which way the defence faces: the mouth of the funnel on the walking route in (killbox corridor, gate), else
        /// the player's door closest to the raid. -1 if neither.
        /// </summary>
        private static float DefendedBearing(Map map, AssaultPlan plan, ThreatMap tm)
        {
            var s = TacticalAIMod.Settings;
            if (tm != null && tm.CompletedCycles > 0)
            {
                using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
                {
                    if (ThreatPathing.TryFindRoute(map, plan.gather, plan.objective, customizer, out var walkNodes, out _))
                        foreach (var c in walkNodes)
                            if (tm.FireAt(c) > s.safeFire && ThreatCustomizer.OpenNeighbours(map, c) <= 4) return Bearing(plan.objective, c);
                }
            }
            Building door = null;
            float best = float.MaxValue;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (!b.def.IsDoor) continue;
                float d = b.Position.DistanceToSquared(plan.gather);
                if (d < best) { best = d; door = b; }
            }
            return door != null ? Bearing(plan.objective, door.Position) : -1f;
        }

        /// <summary>Extra cost for a breach point close to the way the defence faces (0 from 90° off, i.e. flank/rear).</summary>
        private static float AxisPenalty(AssaultPlan plan, float bearing, TacticalAISettings s)
        {
            if (plan.defendedBearing < 0f) return 0f;
            float off = AngleDiff(plan.defendedBearing, bearing);
            return s.defendedAxisPenalty * Mathf.Max(0f, 1f - off / 90f);
        }


        /// <summary>
        /// Everyone gets a breach point: specialists the one they're assigned to, the rest spread evenly (melee and
        /// gunners mixed). Leftover specialists go where their kit helps. Fire points without a team get one raider
        /// from their group as fire starter.
        /// </summary>
        private static void AssignBreachGroups(AssaultPlan plan, List<Pawn> pawns)
        {
            for (int i = 0; i < plan.breaches.Count; i++)
                foreach (var p in plan.breaches[i].team) plan.breachOf[p] = i;
            var rest = pawns.Where(p => !plan.breachOf.ContainsKey(p))
                            .OrderBy(p => p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon ? 1 : 0).ToList();
            foreach (var p in rest)
            {
                int smallest = Enumerable.Range(0, plan.breaches.Count).OrderBy(i => plan.breachOf.Count(kv => kv.Value == i)).First();
                plan.breachOf[p] = smallest;
            }
            foreach (var bp in plan.breaches)
            {
                if (bp.method == BreachMethod.Fire && bp.team.Count == 0)
                {
                    var starter = plan.breachOf.Where(kv => kv.Value == bp.index).Select(kv => kv.Key)
                                      .OrderBy(p => p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon ? 1 : 0).FirstOrDefault();
                    if (starter != null) { bp.team.Add(starter); plan.breachTeam.Add(starter); }
                }
            }
        }

        /// <summary>
        /// The fan of cells outside a breach that the defenders can see once the wall is gone: within
        /// <c>breachConeDegrees</c> of the line from the defenders through the breach point.
        /// </summary>
        public static bool InBreachCone(IntVec3 entry, IntVec3 objective, IntVec3 c)
        {
            if (!entry.IsValid) return false;
            float ox = entry.x - objective.x, oz = entry.z - objective.z;
            float vx = c.x - entry.x, vz = c.z - entry.z;
            float lo = Mathf.Sqrt(ox * ox + oz * oz), lv = Mathf.Sqrt(vx * vx + vz * vz);
            if (lo < 0.5f || lv < 0.5f) return true;
            float cos = (ox * vx + oz * vz) / (lo * lv);
            return cos > Mathf.Cos(TacticalAIMod.Settings.breachConeDegrees * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Stack point (CQB): 5-9 cells from the breach, out of the defenders' fire now AND outside the breach cone,
        /// preferably hugging a wall. Reachable from the end of the covered route.
        /// </summary>
        private static IntVec3 FindStackPoint(Map map, BreachPoint bp, IntVec3 objective, ThreatMap tm, IntVec3 routeEnd)
        {
            var s = TacticalAIMod.Settings;
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(bp.entry, 9f, false))
            {
                float d = c.DistanceTo(bp.entry);
                if (d < 5f || !c.InBounds(map) || !c.Standable(map)) continue;
                if (tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > s.safeFire) continue;
                if (InBreachCone(bp.entry, objective, c)) continue;
                if (!map.reachability.CanReach(routeEnd, c, PathEndMode.OnCell, walk)) continue;
                bool hugsWall = false;
                foreach (IntVec3 a in GenAdj.CellsAdjacentCardinal(c, Rot4.North, IntVec2.One))
                    if (a.InBounds(map) && a.GetEdifice(map) is Building b && b.def.passability == Traversability.Impassable) { hugsWall = true; break; }
                // close to the breach (short final dash), against a wall, and close to where the route ends
                float score = Mathf.Abs(d - 6f) + (hugsWall ? 0f : 3f) + c.DistanceTo(routeEnd) * 0.15f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>Stand off: positions out of the defenders' fire, as close as that allows.</summary>
        private static void ChooseStandoff(Map map, AssaultPlan plan, List<Pawn> pawns)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            var cells = new List<IntVec3>();
            foreach (int r in new[] { 30, 38, 46 })
            {
                for (int a = 0; a < 360; a += 30)
                {
                    float rad = a * Mathf.Deg2Rad;
                    var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                    if (!c.InBounds(map)) continue;
                    c = CellFinder.StandableCellNear(c, map, 3f);
                    if (!c.IsValid || (tm != null && tm.FireAt(c) > 0.05f)) continue;
                    if (!map.reachability.CanReach(plan.gather, c, PathEndMode.OnCell, walk)) continue;
                    cells.Add(c);
                }
                if (cells.Count >= 2) break;
            }
            if (cells.Count == 0) cells.Add(plan.gather);
            foreach (var c in cells.OrderBy(c => c.DistanceToSquared(plan.gather)).Take(2)) plan.axes.Add(c);
            Assign(plan, pawns);
        }

        /// <summary>Leave the vanilla way (no planned withdrawal).</summary>
        public static void ExitMap(Lord lord, AssaultPlan plan) => Convert(lord, plan, new LordJob_ExitMapBest(LocomotionUrgency.Jog, false, true));

        /// <summary>Siege (HSK's siege AI brings artillery) or withdrawal, done outside the lord's own update.</summary>
        private static void Convert(Lord lord, AssaultPlan plan, LordJob job)
        {
            var pawns = lord.ownedPawns.ToList();
            foreach (var p in pawns) lord.RemovePawn(p);
            Lord nl = LordMaker.MakeNewLord(lord.faction, job, lord.Map, pawns);
            Competence.RegisterConverted(nl, plan);   // the siege camp keeps the raid's competence
            plan.phase = PlanPhase.Converted;
            plan.hHourReason = job.GetType().Name;
            Log(plan);
        }

        private struct Cand { public IntVec3 cell; public float score, bearing, exposure; public List<IntVec3> route; }

        private static void ChooseAxes(Map map, AssaultPlan plan, int raiders, TraverseParms walk)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            var cands = new List<Cand>();
            var defsForFlank = Suppression.Defenders(map, plan.objective, 40f);
            bool flankAware = Competence.Knows(plan, Doctrine.Flanking);
            float fireScale = Competence.FireScale(plan);
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
            {
                for (int a = 0; a < 360; a += 20)
                {
                    // along this bearing: the closest cell in dead ground (no defender fire), i.e. the assault position
                    float rad = a * Mathf.Deg2Rad;
                    IntVec3 best = IntVec3.Invalid;
                    float bestFire = float.MaxValue;
                    for (int r = 10; r <= 80; r += 3)
                    {
                        var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                        if (!c.InBounds(map)) break;
                        c = CellFinder.StandableCellNear(c, map, 2f);
                        if (!c.IsValid || c.GetTerrain(map).IsWater) continue;
                        float f = haveThreat ? tm.FireAt(c) : 0f;
                        if (f < bestFire) { bestFire = f; best = c; }
                        if (f <= s.safeFire && r >= 12) break;   // first safe cell outward = as close as dead ground allows
                    }
                    if (!best.IsValid) continue;
                    // an assault position needs a DIRECT line of approach: walking from it to the defenders must be about
                    // as short as the straight line (dead ground behind an obstacle would force the assault through a funnel)
                    float straight = best.DistanceTo(plan.objective);
                    int approachLen;
                    using (PawnPath ap = map.pathFinder.FindPathNow(best, plan.objective, walk, null, PathEndMode.Touch))
                        approachLen = ap != null && ap.Found ? ap.NodesLeftCount : int.MaxValue;
                    if (approachLen > straight * s.maxApproachDetour + 6) continue;
                    if (!ThreatPathing.TryFindRoute(map, plan.gather, best, customizer, out var nodes, out float cost)) continue;
                    float exposure = 0f;
                    if (haveThreat) foreach (var n in nodes) exposure += tm.FireAt(n);
                    // flank: the defenders' cover does not face this side
                    float flank = s.flanking && flankAware ? Flanking.ExposedShare(best, defsForFlank) : 0f;
                    float score = cost - 120f * Cover(map, best) + 8f * best.DistanceTo(plan.objective) + 400f * fireScale * bestFire - s.flankAxisBonus * flank;
                    cands.Add(new Cand { cell = best, score = score, bearing = a, route = nodes, exposure = exposure });
                }
            }
            if (cands.Count == 0) return;
            int k = raiders < 3 ? 1 : raiders < 6 ? 2 : raiders <= 15 ? 2 : 3;
            k = Competence.MaxAxes(plan, Mathf.Min(k, s.maxAxes));   // envelopment from several sides takes a sharp command
            foreach (var c in cands.OrderBy(x => x.score))
            {
                if (plan.axes.Count >= k) break;
                bool separated = plan.axes.All(a => AngleDiff(Bearing(plan.objective, a), c.bearing) >= s.minAxisSeparation);
                if (!separated) continue;
                plan.axes.Add(c.cell);
                plan.waypoints.Add(ThreatPathing.Waypoints(c.route, s.waypointSpacing));
                plan.routeExposure += c.exposure;
            }
        }

        /// <summary>Cover around a cell: adjacent walls, rock, trees, sandbags and the like (0-3).</summary>
        private static int Cover(Map map, IntVec3 c)
        {
            int n = 0;
            foreach (var d in GenAdj.CardinalDirections)
            {
                var a = c + d;
                if (!a.InBounds(map)) continue;
                foreach (Thing t in a.GetThingList(map))
                {
                    if (t.def.fillPercent >= 0.4f || (t.def.plant != null && t.def.plant.IsTree)) { n++; break; }
                }
            }
            return Mathf.Min(n, 3);
        }

        public static void Assign(AssaultPlan plan, List<Pawn> pawns)
        {
            // mix melee and gunners over the axes
            var ordered = pawns.OrderBy(p => p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon ? 1 : 0).ToList();
            for (int i = 0; i < ordered.Count; i++) AssignOne(plan, ordered[i], i % plan.axes.Count);
        }

        public static void AssignOne(AssaultPlan plan, Pawn p, int axis)
        {
            plan.axisOf[p] = axis;
            IntVec3 center = plan.axes[axis];
            // lateral spread (CAI idea): 2+ cells from the others' spots, so one grenade does not catch the group
            IntVec3 spot = CoverSpot(p.Map, center, 5f, plan.objective, c => !plan.spotOf.Any(kv => kv.Key != p && kv.Value.DistanceTo(c) < 2f), p.thingIDNumber);
            plan.spotOf[p] = spot.IsValid ? spot : CellFinder.RandomClosewalkCellNear(center, p.Map, 5, c => !plan.spotOf.Any(kv => kv.Key != p && kv.Value.DistanceTo(c) < 2f));
        }

        /// <summary>
        /// H-hour, walk mode: each group goes in along a covered route from its assault position - priced by fire and
        /// funnels (fords and gaps rather than the bridge or the gate), half the fire cost of the approach (speed now
        /// matters) - and fights once close or shot at.
        /// </summary>
        private static void PlanAssaultRoutes(Map map, AssaultPlan plan)
        {
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            plan.assaultRoutes.Clear();
            plan.waypointIndex.Clear();
            plan.entered.Clear();
            var lengths = new List<int>();
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire * 0.5f, s.routeBaseCostInFire, s.funnelCellPenalty, s.safeFire))
                foreach (var a in plan.axes)
                {
                    bool ok = ThreatPathing.TryFindRoute(map, a, plan.objective, customizer, out var n, out _);
                    plan.assaultRoutes.Add(ok ? ThreatPathing.Waypoints(n, 6) : new List<IntVec3>());
                    lengths.Add(ok ? n.Count : 0);
                }
            // timing (playtest: "timing also matters when the different groups attack and from how many angles"): the axes
            // start staggered so they reach the defenders together; inside each group two halves alternate - one moves,
            // the other fires - so the fire never stops
            plan.axisGoTick.Clear();
            plan.assaultElementOf.Clear();
            if (!s.syncedAssault || !Competence.Knows(plan, Doctrine.SyncedAssault)) return;
            int now = Find.TickManager.TicksGame, longest = lengths.Count > 0 ? lengths.Max() : 0;
            for (int i = 0; i < lengths.Count; i++)
                plan.axisGoTick[i] = now + Mathf.RoundToInt((longest - lengths[i]) * s.assaultTicksPerCell);
            foreach (var g in plan.axisOf.GroupBy(kv => kv.Value))
            {
                int k = 0;
                foreach (var kv in g.OrderBy(kv => kv.Key.thingIDNumber)) plan.assaultElementOf[kv.Key] = k++ % 2;
            }
        }

        /// <summary>A breach point that is open AND leads in on foot (an inner wall layer may still block it).</summary>
        private static bool Through(Map map, AssaultPlan plan, BreachPoint bp)
        {
            if (bp.abandoned || !bp.WallsGone) return false;
            IntVec3 from = bp.staging.IsValid ? bp.staging : plan.gather;
            using (PawnPath p = map.pathFinder.FindPathNow(from, plan.objective, TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly), null, PathEndMode.Touch))
                return p != null && p.Found;
        }

        /// <summary>Frag out at every open breach not cleared yet (tunnels: at the inner exit).</summary>
        private static void FragOut(Map map, AssaultPlan plan, List<Pawn> alive)
        {
            foreach (var bp in plan.breaches)
            {
                if (bp.abandoned || !bp.WallsGone || !plan.fragOutDone.Add(bp.index)) continue;
                IntVec3 opening = bp.rock && bp.walls.Count > 0 ? bp.walls[bp.walls.Count - 1].Position : bp.entry;
                var group = alive.Where(p => plan.BreachFor(p) == bp).ToList();
                if (group.Count == 0) group = alive;
                Grenades.OrderAtOpening(map, plan, group, opening);
            }
        }

        /// <summary>The breach a pawn goes in through: its own if open, else the nearest open one.</summary>
        public static BreachPoint EntryFor(AssaultPlan plan, Pawn p, Map map)
        {
            var own = plan.BreachFor(p);
            if (own != null && !own.abandoned && own.WallsGone) return own;
            BreachPoint best = null;
            float bd = float.MaxValue;
            foreach (var bp in plan.breaches)
            {
                if (bp.abandoned || !bp.WallsGone) continue;
                float d = bp.entry.DistanceToSquared(p.Position);
                if (d < bd) { bd = d; best = bp; }
            }
            return best;
        }

        /// <summary>Set the duties for the plan. Returns false if the plan doesn't take over (vanilla duties then).</summary>
        public static bool ApplyDuties(Lord lord, AssaultPlan plan)
        {
            var s = TacticalAIMod.Settings;
            if (plan == null || plan.phase == PlanPhase.Converted) return false;
            if (plan.mode == PlanMode.Siege) return false;   // converted on the next map tick
            if (plan.phase == PlanPhase.Withdraw)
            {
                foreach (Pawn p in lord.ownedPawns)
                {
                    if (p.mindState == null) continue;
                    BreachKit.RestoreWeapon(p);
                    // arsonists away from the fight finish their fires first
                    if (Arson.IsActiveArsonist(plan, p)) p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Arson, plan.arsonistOf[p].center);
                    else p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Withdraw, plan.exitCell);
                }
                return true;
            }
            if (plan.phase != PlanPhase.Approach) foreach (Pawn p in lord.ownedPawns) BreachKit.RestoreWeapon(p);
            foreach (Pawn p in lord.ownedPawns)
            {
                if (p.mindState == null) continue;
                if (plan.mode == PlanMode.Scout || plan.mode == PlanMode.Sabotage) { RaidGoals.Duty(plan, p); continue; }
                var crew = CrewWeapons.TeamOf(plan, p);
                if (crew != null && crew.post.IsValid) { p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_CSW, crew.post); continue; }
                if (plan.mode == PlanMode.Breach && !plan.breachOf.ContainsKey(p) && plan.breaches.Count > 0)
                {
                    // reinforcements / late joiners: the smallest active group
                    var act = plan.breaches.Where(b => !b.abandoned).ToList();
                    if (act.Count == 0) act = plan.breaches;
                    plan.breachOf[p] = act.OrderBy(b => plan.breachOf.Count(kv => kv.Value == b.index)).First().index;
                }
                if (Arson.IsActiveArsonist(plan, p))
                {
                    p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Arson, plan.arsonistOf[p].center);
                    continue;
                }
                // base of fire: from the suppression phase until supportTicks after H-hour, pin the defenders
                bool supporting = s.supportFire && RaidRoles.IsBaseOfFire(plan, p) && plan.suppressStartTick >= 0 && Competence.Knows(plan, Doctrine.SupportFire)
                                  && (plan.phase == PlanPhase.Approach || Find.TickManager.TicksGame - plan.hHourTick < s.supportTicks);
                // breach open: overwatching riflemen near it move up to the corner posts beside the hole (MGs and marksmen keep
                // the base of fire)
                if (supporting && s.cornerFire && plan.mode == PlanMode.Breach && plan.phase == PlanPhase.Assault && plan.overwatchOf.ContainsKey(p)
                    && !(plan.roleOf.TryGetValue(p, out var owRole) && (owRole == RaidRole.Support || owRole == RaidRole.Marksman))
                    && Competence.Knows(plan, Doctrine.Overwatch) && EntryFor(plan, p, lord.Map) is BreachPoint owBp && p.Position.DistanceTo(owBp.entry) <= 25f)
                    supporting = false;
                if (supporting)
                {
                    p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Support, plan.objective);
                    continue;
                }
                // launchers stay out (playtest 2026-10-25: "the M79 etc after they breach they should stay and engage targets of
                // opportunity"): once his wall is open (walk-in: from H-hour) a raider with an explosive launcher doesn't follow the
                // assault through the hole - he is base of fire, shelling what shows itself and lobbing over the walls
                if (s.launchersStayOut && Competence.Knows(plan, Doctrine.SupportFire) && !plan.entered.Contains(p)
                    && (plan.phase == PlanPhase.Approach || plan.phase == PlanPhase.Assault)
                    && (plan.mode == PlanMode.Breach ? plan.BreachFor(p) is BreachPoint lbp && (lbp.WallsGone || lbp.abandoned) && (plan.breachTeam.Contains(p) || plan.phase == PlanPhase.Assault) : plan.phase == PlanPhase.Assault)
                    && Launchers.ExplosiveLauncher(p))
                {
                    LaunchersHeldOut++;
                    p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Support, plan.objective);
                    continue;
                }
                if (plan.phase == PlanPhase.Assault)
                {
                    // through our own breach: first go to the breach point, then fight inside
                    BreachPoint way = plan.mode == PlanMode.Breach && !plan.entered.Contains(p) ? EntryFor(plan, p, lord.Map) : null;
                    if (way != null)
                        p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Approach, way.entry, 2f);
                    else if (plan.mode == PlanMode.Walk && !plan.entered.Contains(p) && plan.assaultRoutes.Count > 0)
                        p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Approach, plan.objective, 8f);   // covered route in, then fight
                    else
                        p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Assault, plan.objective);
                    continue;
                }
                if (plan.mode == PlanMode.Breach)
                {
                    // the breach team works its breach point; everyone else stacks beside it
                    var bp = plan.BreachFor(p);
                    if (bp == null) continue;
                    // overwatch: gunners take covered firing positions facing the breach and hold - they shoot the moment
                    // the wall drops and pin the defenders; only melee / assault stack beside it to go in
                    if (s.overwatch && !bp.rock && !bp.team.Contains(p) && RaidRoles.IsRangedFighter(plan, p)
                        && (Competence.Knows(plan, Doctrine.Overwatch) || (plan.roleOf.TryGetValue(p, out var owR) && (owR == RaidRole.Marksman || owR == RaidRole.Support) && Competence.Knows(plan, Doctrine.SupportFire))))
                    {
                        if (!plan.overwatchOf.TryGetValue(p, out IntVec3 ow))
                        {
                            ow = plan.roleOf.TryGetValue(p, out var rr) && rr == RaidRole.Marksman ? MarksmanOverwatch(plan, bp, p) : OverwatchCell(plan, bp, p);
                            if (ow.IsValid) plan.overwatchOf[p] = ow;
                        }
                        if (ow.IsValid)
                        {
                            p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Support, plan.objective);
                            continue;
                        }
                    }
                    if (bp.team.Contains(p))
                        p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Breach, bp.staging, bp.staging);
                    else
                    {
                        if (!plan.spotOf.ContainsKey(p)) plan.spotOf[p] = StackSpot(plan, bp, p);
                        p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Approach, plan.spotOf[p], 2.5f);
                    }
                    continue;
                }
                if (!plan.axisOf.ContainsKey(p))
                {
                    int smallest = Enumerable.Range(0, plan.axes.Count).OrderBy(i => plan.axisOf.Count(kv => kv.Value == i)).First();
                    AssignOne(plan, p, smallest);
                }
                p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Approach, plan.spotOf[p], 2.5f);
            }
            return true;
        }

        /// <summary>
        /// Overwatch position for a gunner: facing the breach (inside its future line of fire, so it can shoot through the
        /// hole the moment it opens), overwatchMinDist..overwatchMaxDist from it (and within 80 % of the weapon's range), out
        /// of the defenders' fire now, line of sight to the wall face, reachable; cover towards the breach preferred, spread
        /// 2+ cells from the other overwatchers.
        /// </summary>
        private static IntVec3 OverwatchCell(AssaultPlan plan, BreachPoint bp, Pawn p) =>
            OverwatchCell(plan, bp.entry, bp.plantSide.IsValid ? bp.plantSide : bp.entry, p);

        /// <summary>
        /// Overwatch on any watch point (breach, door, gap): facing it (inside its cone), at about 60 % of the weapon's
        /// range (snipers far back, riflemen closer; overwatchMinDist..min(85 % range, overwatchMaxDist)), out of fire now,
        /// line of sight to its outer face; a position WITH cover towards it strongly preferred.
        /// </summary>
        private static IntVec3 OverwatchCell(AssaultPlan plan, IntVec3 watch, IntVec3 face, Pawn p)
        {
            var s = TacticalAIMod.Settings;
            Map map = p.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            float range = p.equipment.Primary.def.Verbs.FirstOrDefault(v => !v.IsMeleeAttack)?.range ?? 20f;
            // snipers further back (playtest: "sniper should stay in overwatch before breach ... further away from where the breach will be")
            bool marks = plan.roleOf.TryGetValue(p, out var owRole) && owRole == RaidRole.Marksman;
            float maxD = Mathf.Min(range * 0.85f, marks ? Mathf.Max(49f, s.overwatchMaxDist) : s.overwatchMaxDist);
            if (maxD < s.overwatchMinDist) return IntVec3.Invalid;
            float want = Mathf.Clamp(range * (marks ? 0.75f : 0.6f), s.overwatchMinDist, maxD);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(watch, maxD, true))
            {
                float d = c.DistanceTo(watch);
                if (d < s.overwatchMinDist || !c.InBounds(map) || !c.Standable(map)) continue;
                if (!InBreachCone(watch, plan.objective, c)) continue;
                if (tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > s.safeFire) continue;
                if (plan.overwatchOf.Values.Any(o => o.DistanceTo(c) < 2f)) continue;
                float score = Mathf.Abs(d - want) * 0.5f + (JobGiver_HSKTAI_Support.HasCover(c, watch, map) ? 0f : 10f) + c.DistanceTo(p.Position) * 0.05f;
                if (score >= bestScore) continue;
                if (!GenSight.LineOfSight(c, face, map, true)) continue;
                if (!p.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                bestScore = score;
                best = c;
            }
            return best;
        }

        /// <summary>
        /// Where a marksman watches: the player doors and breach points nobody (or the fewest) watches yet - defenders
        /// coming out through any of them get shot. Its own breach if nothing else needs watching.
        /// </summary>
        private static IntVec3 MarksmanOverwatch(AssaultPlan plan, BreachPoint own, Pawn p)
        {
            Map map = p.Map;
            var points = new List<(IntVec3 watch, IntVec3 face)>();
            // outer doors only (one side opens to the outdoors), aimed at the cell in front of the door
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (!b.def.IsDoor || !b.Spawned || plan.breaches.Any(x => x.entry == b.Position)) continue;
                foreach (IntVec3 d in GenAdj.CardinalDirections)
                {
                    IntVec3 o = b.Position + d;
                    if (!o.InBounds(map) || !o.Walkable(map) || o.Roofed(map)) continue;
                    if (o.GetRoom(map)?.PsychologicallyOutdoors != true) continue;
                    points.Add((b.Position, o));
                    break;
                }
            }
            if (points.Count > 6) points = points.OrderBy(pt => pt.watch.DistanceToSquared(own.entry)).Take(6).ToList();
            foreach (var x in plan.breaches) if (!x.rock && !x.abandoned) points.Add((x.entry, x.plantSide.IsValid ? x.plantSide : x.entry));
            foreach (var pt in points.OrderBy(pt => plan.overwatchOf.Values.Count(o => o.DistanceTo(pt.watch) <= 40f && InBreachCone(pt.watch, plan.objective, o)))
                                     .ThenBy(pt => pt.watch.DistanceToSquared(own.entry)))
            {
                IntVec3 c = OverwatchCell(plan, pt.watch, pt.face, p);
                if (c.IsValid) return c;
            }
            return IntVec3.Invalid;
        }

        public static int CoverSpots, CoveredSpots;

        /// <summary>
        /// A spot near `center` that passes `ok`, in cover from `threat` if there is one (playtest 2026-10-25: "in general
        /// units should always seek cover if possible and available"); spread at random among the covered ones.
        /// </summary>
        public static IntVec3 CoverSpot(Map map, IntVec3 center, float radius, IntVec3 threat, Func<IntVec3, bool> ok, int salt)
        {
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            var cells = GenRadial.RadialCellsAround(center, radius, true)
                .Where(c => c.InBounds(map) && c.Standable(map) && ok(c) && map.reachability.CanReach(center, c, PathEndMode.OnCell, walk)).ToList();
            if (cells.Count == 0) return IntVec3.Invalid;
            var covered = cells.Where(c => JobGiver_HSKTAI_Support.HasCover(c, threat, map)).ToList();
            var pool = covered.Count > 0 ? covered : cells;
            CoverSpots++;
            if (covered.Count > 0) CoveredSpots++;
            return pool[Mathf.FloorToInt(Competence.Hash01(salt, center.x * 31 + center.z) * pool.Count) % pool.Count];
        }

        /// <summary>A waiting spot near a stack point: out of fire and outside the breach cone.</summary>
        private static IntVec3 StackSpot(AssaultPlan plan, BreachPoint bp, Pawn p)
        {
            var tm = MapComponent_TacticalAI.For(p.Map)?.Threat;
            float safe = TacticalAIMod.Settings.safeFire;
            // spread out (cp7 dull fortress: one blast took six stackers waiting shoulder to shoulder): 2+ cells from the next man
            IntVec3 c = CoverSpot(p.Map, bp.staging, 5f, plan.objective, x => !InBreachCone(bp.entry, plan.objective, x) && (tm == null || tm.FireAt(x) <= safe)
                && !plan.spotOf.Any(kv => kv.Key != p && kv.Value.DistanceTo(x) < 2f), p.thingIDNumber);
            return c.IsValid ? c : bp.staging;
        }

        public static AssaultPlan PlanOf(Pawn p)
        {
            Lord lord = p.GetLord();
            return lord != null && Plans.TryGetValue(lord.loadID, out var plan) ? plan : null;
        }

        /// <summary>Move a group to another breach point (its own was abandoned).</summary>
        private static void Regroup(AssaultPlan plan, BreachPoint from, BreachPoint to)
        {
            foreach (var p in plan.breachOf.Where(kv => kv.Value == from.index).Select(kv => kv.Key).ToList())
            {
                plan.breachOf[p] = to.index;
                plan.spotOf.Remove(p);
                plan.waypointIndex.Remove(p);
                if (from.team.Contains(p) && p.Spawned && !p.Downed) to.team.Add(p);
            }
        }

        public static int Adaptations, DefenceBroken;
        public static int LaunchersHeldOut, BreachShotsFromCover;

        /// <summary>
        /// The situation changed (playtest 2026-10-25: "once the situation changes the plan needs to adapt that is very
        /// important"), approach phase:
        /// - the defence came out and lost half its people in the field (FieldFight): go now (walk-in), returns the reason;
        /// - the defence moved (its centre, those still at home, 12+ cells from the objective): the objective follows; a
        ///   walk-in re-plans its axes. A breach keeps its walls (the charges are on them) but aims for the new centre.
        /// A doctrine (FieldFight): a dull command sticks to the plan it made.
        /// </summary>
        private static string Adapt(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, int now)
        {
            var s = TacticalAIMod.Settings;
            if (plan.phase != PlanPhase.Approach || !s.fieldFight || !Competence.Knows(plan, Doctrine.FieldFight)) return null;
            var defs = Suppression.Defenders(map, plan.objective, 999f);
            if (plan.defendersAtStart > 0 && plan.fieldLastSeen >= 0 && defs.Count <= plan.defendersAtStart * 0.5f)
            {
                if (!plan.defenceBroken) { plan.defenceBroken = true; DefenceBroken++; plan.capability += "; defence broken in the field (" + defs.Count + "/" + plan.defendersAtStart + " left) -> go"; Log(plan); }
                return "defence broken in the field";
            }
            if (now - plan.lastAdaptTick < 300) return null;
            plan.lastAdaptTick = now;
            var home = defs.Where(d => !plan.fieldEnemies.Contains(d)).ToList();
            if (home.Count == 0) return null;
            var c = new IntVec3((int)home.Average(d => d.Position.x), 0, (int)home.Average(d => d.Position.z));
            if (c.DistanceTo(plan.objective) < 12f || !c.InBounds(map)) return null;
            plan.objective = c;
            Adaptations++;
            plan.capability += "; defence moved -> objective " + c;
            if (plan.mode == PlanMode.Walk)
            {
                var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
                plan.axes.Clear(); plan.axisOf.Clear(); plan.spotOf.Clear(); plan.waypoints.Clear(); plan.waypointIndex.Clear();
                ChooseAxes(map, plan, alive.Count, walk);
                if (plan.axes.Count == 0) plan.axes.Add(plan.objective);
                Assign(plan, alive);
                lord.CurLordToil?.UpdateAllDuties();
            }
            Log(plan);
            return null;
        }

        /// <summary>Every 60 ticks: synchronise the breach teams, and start the assault (H-hour) at the right moment.</summary>
        public static void Tick(Map map)
        {
            if (Plans.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            var s = TacticalAIMod.Settings;
            CrewWeapons.TickLeaving(map);   // raids leaving the map (any way): pack up or wreck their set-up guns
            foreach (Lord lord in map.lordManager.lords.ToList())
            {
                if (!Plans.TryGetValue(lord.loadID, out var plan)) continue;
                var alive = lord.ownedPawns.Where(p => p.Spawned && !p.Downed).ToList();
                if (alive.Count == 0) continue;
                Competence.CheckCommand(lord, plan, alive, now);
                int ox = 0, oz = 0;
                foreach (var p in alive) { ox += p.Position.x; oz += p.Position.z; }
                using (Competence.Perceive(plan, new IntVec3(ox / alive.Count, 0, oz / alive.Count)))
                {

                if (plan.csw.Count > 0) CrewWeapons.Tick(map, lord, plan, now);
                if (plan.phase == PlanPhase.Withdraw) { Withdrawal.Tick(plan, now); continue; }
                if ((plan.phase == PlanPhase.Approach || plan.phase == PlanPhase.Assault) && (plan.mode == PlanMode.Breach || plan.mode == PlanMode.Walk)
                    && Withdrawal.ShouldWithdraw(lord, plan, out string wwhy))
                {
                    Withdrawal.Begin(map, lord, plan, alive, wwhy);
                    Log(plan);
                    lord.CurLordToil?.UpdateAllDuties();
                    continue;
                }
                if (plan.phase == PlanPhase.Approach || plan.phase == PlanPhase.Assault) FieldFight.Tick(map, plan, alive, now);
                if (plan.mode == PlanMode.Scout || plan.mode == PlanMode.Sabotage) { RaidGoals.Tick(map, lord, plan, alive, now); continue; }

                // walk-mode assault bleeding out (e.g. in a killbox entrance): pull back and make new entrances
                if (plan.phase == PlanPhase.Assault && plan.mode == PlanMode.Walk && !plan.replanned && s.avoidFunnels && Competence.Knows(plan, Doctrine.AvoidFunnels)
                    && alive.Count <= plan.aliveAtHHour * (1f - s.replanLossFraction))
                {
                    plan.replanned = true;
                    int gx = 0, gz = 0;
                    foreach (var p in alive) { gx += p.Position.x; gz += p.Position.z; }
                    IntVec3 regroup = new IntVec3(gx / alive.Count, 0, gz / alive.Count);
                    if (!regroup.Standable(map)) regroup = alive[0].Position;
                    if (TryFindNewEntrance(map, regroup, plan.objective, 2f, out var nodes, out string why, Mathf.Max(1, s.minFunnelCells)))
                    {
                        plan.gather = regroup;
                        PlanBreach(lord, plan, alive, nodes, fallbackToWalk: true);
                        if (plan.mode == PlanMode.Breach)
                        {
                            plan.phase = PlanPhase.Approach;
                            plan.hHourReason = "re-plan after losses: new entrance (" + why + ")";
                            Log(plan);
                            lord.CurLordToil?.UpdateAllDuties();
                        }
                    }
                    continue;
                }
                // walk assault: on the covered route until close to the defenders (or after the sync timeout), then fight
                if (plan.phase == PlanPhase.Assault && plan.mode == PlanMode.Walk && plan.assaultRoutes.Count > 0)
                {
                    bool changedW = false;
                    foreach (var p in alive)
                    {
                        if (plan.entered.Contains(p)) continue;
                        if (p.Position.DistanceTo(plan.objective) <= 12f || now - plan.hHourTick > s.syncTimeoutTicks) { plan.entered.Add(p); changedW = true; }
                    }
                    if (changedW) lord.CurLordToil?.UpdateAllDuties();
                    continue;
                }
                // breach assault: raiders who reached their breach point switch from "go to the breach" to fighting
                // inside; breach points that open late are smoked and used too
                if (plan.phase == PlanPhase.Assault && plan.mode == PlanMode.Breach)
                {
                    bool changed = false;
                    foreach (var bp in plan.breaches)
                        if (!bp.abandoned && bp.WallsGone && !bp.smoked) { BreachKit.ObscureBreach(map, lord, plan, bp); changed = true; }
                    FragOut(map, plan, alive);
                    foreach (var p in alive)
                    {
                        if (plan.entered.Contains(p)) continue;
                        var way = EntryFor(plan, p, map);
                        if (CornerFire.Holding(p, plan)) continue;   // firing from a corner post beside the hole
                        if (way != null && !ShieldWall.IsBearer(plan, p) && CornerFire.GapHot(plan, way.entry)) continue;   // clearing the gap by fire first
                        if (way == null || p.Position.DistanceTo(way.entry) <= 3f || now - plan.hHourTick > s.syncTimeoutTicks) { plan.entered.Add(p); changed = true; }
                    }
                    if (changed) lord.CurLordToil?.UpdateAllDuties();
                    continue;
                }
                if (plan.phase != PlanPhase.Approach) continue;

                if (plan.mode == PlanMode.Siege && plan.pendingConvert)
                {
                    plan.pendingConvert = false;
                    float points = Mathf.Max(60f, alive.Sum(p => p.kindDef?.combatPower ?? 50f) * 0.25f);
                    Convert(lord, plan, new LordJob_Siege(lord.faction, SiegeGuards.SiegeSpot(plan, map), points));
                    continue;
                }
                if (plan.mode == PlanMode.Standoff)
                {
                    if (now - plan.createdTick > s.standoffTicks) Convert(lord, plan, new LordJob_ExitMapBest(LocomotionUrgency.Jog, false, true));
                    continue;
                }
                string adapted = Adapt(map, lord, plan, alive, now);
                string reason = null;
                if (plan.mode == PlanMode.Breach)
                {
                    if (!TickBreach(map, lord, plan, alive, now, s, ref reason)) continue;
                }
                else if (adapted != null && plan.mode == PlanMode.Walk) reason = adapted;
                // a crossing decision in progress (smoke, flank group, attrition): the crossing logic decides when to go
                else if (plan.mode == PlanMode.Walk && plan.crossing != CrossingMode.None && plan.crossing != CrossingMode.Covered && plan.crossing != CrossingMode.Mass && plan.crossing != CrossingMode.Bound) reason = "crossing";
                else if (alive.Count < plan.startCount * 0.75f) reason = "casualties";
                else if (now - plan.createdTick > Competence.SyncTimeout(plan, s.syncTimeoutTicks)) reason = "timeout";
                else
                {
                    bool allReady = plan.axes.Count > 0;
                    for (int axis = 0; axis < plan.axes.Count && allReady; axis++)
                    {
                        var members = alive.Where(p => plan.axisOf.TryGetValue(p, out int a) && a == axis && !Arson.IsActiveArsonist(plan, p)).ToList();
                        if (members.Count == 0) continue;
                        int arrived = members.Count(p => plan.spotOf.TryGetValue(p, out var sp) && p.Position.DistanceTo(sp) <= 4f);
                        if (arrived < members.Count * 0.75f) allReady = false;
                    }
                    if (allReady) reason = "groups in position";
                }
                if (reason == null) continue;
                if (CrewWeapons.Holding(plan, now)) continue;   // the team attacks when the guns are firing

                // last look before committing: still strong enough to storm the position?
                if (s.forceRatioCheck && Competence.Knows(plan, Doctrine.ForceCheck))
                {
                    int dirs = plan.mode == PlanMode.Breach ? Mathf.Max(1, plan.breaches.Count(b => Through(map, plan, b))) : plan.axes.Count;
                    plan.forceRatio = ForceRatio(map, alive, dirs, out plan.requiredRatio);
                    plan.requiredRatio *= Competence.OddsFactor(plan);
                    if (plan.forceRatio < plan.requiredRatio * s.commitRatioTolerance)
                    {
                        plan.capability += "; at H-hour force " + plan.forceRatio.ToString("0.00") + " < " + (plan.requiredRatio * s.commitRatioTolerance).ToString("0.00") + " -> withdraw";
                        if (Competence.PlannedWithdrawal(plan)) { Withdrawal.Begin(map, lord, plan, alive, "too weak at H-hour"); Log(plan); lord.CurLordToil?.UpdateAllDuties(); }
                        else Convert(lord, plan, new LordJob_ExitMapBest(LocomotionUrgency.Jog, false, true));
                        continue;
                    }
                }
                // open ground: don't walk into the fire - covered approach, massed rush, smoke screen or attrition
                if (plan.mode == PlanMode.Walk && s.openCrossing && Competence.Knows(plan, Doctrine.OpenCrossing) && Crossing.Hold(map, lord, plan, alive, now, ref reason)) continue;
                // suppress, then move: the base of fire pins the defenders first; the assault goes once enough of the
                // defenders in view are suppressed / hunkering, or after suppressMaxTicks at the latest
                bool haveFire = s.supportFire && Competence.Knows(plan, Doctrine.SupportFire) && Suppression.Available && alive.Any(p => RaidRoles.IsBaseOfFire(plan, p));
                if (plan.mode == PlanMode.Breach && plan.suppressStartTick < 0) FragOut(map, plan, alive);
                if (haveFire || plan.throwOrders.Count > 0 || plan.suppressStartTick >= 0)
                {
                    // pre-assault: the base of fire pins, grenadiers frag the opening; the rush waits for both
                    if (plan.suppressStartTick < 0)
                    {
                        plan.suppressStartTick = now;
                        plan.hHourReason = "suppressing (" + reason + ")";
                        Log(plan);
                        lord.CurLordToil?.UpdateAllDuties();
                        continue;
                    }
                    float pinned = haveFire ? Suppression.PinnedFraction(map, plan, alive.Where(p => RaidRoles.IsBaseOfFire(plan, p))) : 1f;
                    bool throwsPending = plan.throwOrders.Count > 0 || (plan.lastThrowTick >= 0 && now - plan.lastThrowTick < s.fragSettleTicks);
                    if ((pinned < s.pinnedToMove || throwsPending) && now - plan.suppressStartTick < s.suppressMaxTicks) continue;
                    if (Grenades.FragOrders + Grenades.SmokeOrders > 0) reason += ", grenades thrown";
                    plan.pinnedAtHHour = pinned;
                    reason += ", " + Mathf.RoundToInt(pinned * 100f) + "% pinned after " + (now - plan.suppressStartTick) + " ticks";
                }
                plan.phase = PlanPhase.Assault;
                plan.hHourTick = now;
                plan.hHourReason = reason;
                plan.aliveAtHHour = alive.Count;
                if (plan.mode == PlanMode.Breach) FragOut(map, plan, alive);
                if (plan.mode == PlanMode.Walk) PlanAssaultRoutes(map, plan);
                if (plan.mode == PlanMode.Breach)
                    foreach (var bp in plan.breaches)
                        if (!bp.abandoned && bp.WallsGone) BreachKit.ObscureBreach(map, lord, plan, bp);
                Log(plan);
                lord.CurLordToil?.UpdateAllDuties();
                }   // perception scope
            }
        }

        /// <summary>
        /// Breach plan in the approach phase. Returns false if the lord was converted / re-planned (skip the rest).
        /// - Groups whose breach team is gone with the wall standing join another breach point; none left -> withdraw.
        /// - Go signal: the teams start on their walls together once every team is at its stack point (or after
        ///   breachSyncTicks), so the breaches open at about the same time.
        /// - H-hour: every active breach is open ("breaches open"), or one is and the others are late.
        /// - No progress / only inner walls left: plan the next breach points.
        /// </summary>
        /// <summary>Can the living team still open this breach point with what it carries (fire, tunnels: always)?</summary>
        private static bool HasMeans(BreachPoint bp)
        {
            var team = bp.team.Where(p => p.Spawned && !p.Downed && !p.Dead).ToList();
            switch (bp.method)
            {
                case BreachMethod.Launcher: return team.Any(p => Launchers.CanStillBreach(p, DestructiveRangedVerb(p)));
                case BreachMethod.Charge: return team.Any(p => BreachKit.CarriedCharge(p) != null);
                case BreachMethod.Tool: return team.Any(HasBreachTool);
                case BreachMethod.Grenade: return team.Any(p => Grenades.CarriedFrag(p) != null || DestructiveRangedVerb(p) != null);
                default: return true;
            }
        }

        private static bool TickBreach(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, int now, TacticalAISettings s, ref string reason)
        {
            bool dutiesChanged = false;
            foreach (var bp in plan.breaches)
            {
                if (bp.abandoned || bp.WallsGone) continue;
                foreach (var gone in bp.team.Where(p => p.Dead || p.Destroyed || p.GetLord() != lord).ToList())
                {
                    plan.capability += "; breacher " + gone.LabelShort + " out (" + (gone.Dead ? "dead" : gone.Destroyed ? "destroyed" : "left: lord=" + (gone.GetLord()?.LordJob?.GetType().Name ?? "none") + " mental=" + (gone.MentalStateDef?.defName ?? "-") + " job=" + (gone.CurJobDef?.defName ?? "-") + " spawned=" + gone.Spawned) + ")";
                    bp.team.Remove(gone);
                }
                // ff2 (full modlist): a one-shot launcher spent on the first wall left breach #1 with a living team and
                // nothing to open it with; the raid waited for it until the timeout. Out of means = given up now.
                bool outOfMeans = bp.TeamAlive && !BreachKit.BreachUnderWay(map, bp) && !HasMeans(bp);
                if (outOfMeans)
                {
                    bp.abandoned = true;
                    var toM = plan.breaches.Where(b => !b.abandoned).OrderBy(b => b.entry.DistanceToSquared(bp.entry)).FirstOrDefault();
                    if (toM != null) Regroup(plan, bp, toM);
                    plan.capability += "; breach #" + bp.index + " out of means";
                    dutiesChanged = true;
                    continue;
                }
                if (bp.TeamAlive || BreachKit.BreachUnderWay(map, bp)) continue;
                plan.capability += "; breach #" + bp.index + " team not alive [" + string.Join(",", bp.team.Select(p => p.LabelShort + (p.Spawned ? "" : ":unspawned") + (p.Downed ? ":downed" : ""))) + "]";
                // fire: anyone in the group can light it
                if (bp.method == BreachMethod.Fire)
                {
                    var starter = alive.FirstOrDefault(p => plan.breachOf.TryGetValue(p, out int i) && i == bp.index);
                    if (starter != null) { bp.team.Add(starter); plan.breachTeam.Add(starter); dutiesChanged = true; continue; }
                }
                bp.abandoned = true;
                var to = plan.breaches.Where(b => !b.abandoned).OrderBy(b => b.entry.DistanceToSquared(bp.entry)).FirstOrDefault();
                if (to != null) Regroup(plan, bp, to);
                plan.capability += "; breach #" + bp.index + " team lost";
                dutiesChanged = true;
            }
            var active = plan.breaches.Where(b => !b.abandoned).ToList();
            if (active.Count == 0)
            {
                // every breach team gone with the walls still standing: nobody else can do it -> withdraw
                plan.capability += "; all breach teams lost";
                if (Competence.PlannedWithdrawal(plan)) { Withdrawal.Begin(map, lord, plan, alive, "all breach teams lost"); Log(plan); lord.CurLordToil?.UpdateAllDuties(); }
                else Convert(lord, plan, new LordJob_ExitMapBest(LocomotionUrgency.Jog, false, true));
                return false;
            }

            // go signal: all teams at their stack points
            if (plan.goTick < 0)
            {
                // ... and the overwatch is in position (at least 75 % at their cells)
                var ows = plan.overwatchOf.Where(kv => kv.Key.Spawned && !kv.Key.Downed).ToList();
                bool owReady = ows.Count == 0 || ows.Count(kv => kv.Key.Position.DistanceTo(kv.Value) <= 2f) >= ows.Count * 0.75f;
                bool allReady = owReady && active.All(b => b.WallsGone || b.reachedStaging.Any(p => p.Spawned && !p.Downed));
                bool anyReady = active.Any(b => b.reachedStaging.Any(p => p.Spawned && !p.Downed));
                if (anyReady && plan.firstReadyTick < 0) plan.firstReadyTick = now;
                if (allReady || (plan.firstReadyTick >= 0 && now - plan.firstReadyTick > s.breachSyncTicks))
                {
                    plan.goTick = now;
                    plan.lastBreachProgressTick = now;
                    plan.hHourReason = allReady ? "go: all breach teams ready" : "go: sync timeout";
                    Log(plan);
                }
            }

            int through = active.Count(b => Through(map, plan, b));
            if (through > 0 && plan.firstThroughTick < 0) plan.firstThroughTick = now;
            if (through > 0 && through == active.Count) reason = "breaches open (" + through + "/" + plan.breaches.Count + ")";
            else if (through > 0)
            {
                // others still coming down (their walls lost HP recently): wait for them up to breachSyncTicks; else grace
                bool othersProgressing = now - plan.lastBreachProgressTick < s.breachOpenGraceTicks;
                int wait = othersProgressing ? s.breachSyncTicks : s.breachOpenGraceTicks;
                if (now - plan.firstThroughTick > wait) reason = "breach open (" + through + "/" + plan.breaches.Count + "), not waiting";
            }

            // progress: wall damage across all breach points
            int dmg = 0;
            foreach (var bp in active) foreach (var w in bp.walls.Concat(bp.walls2)) dmg += w == null || w.Destroyed ? w?.MaxHitPoints ?? 0 : w.MaxHitPoints - w.HitPoints;
            if (dmg > plan.lastBreachDamage) { plan.lastBreachDamage = dmg; plan.lastBreachProgressTick = now; }
            bool stalled = plan.goTick >= 0 && now - plan.lastBreachProgressTick > s.breachStallTicks;
            bool innerOnly = through == 0 && active.All(b => b.WallsGone);
            if (reason == null && (stalled || innerOnly))
            {
                plan.breachReplans++;
                if (plan.breachReplans > s.maxBreachReplans)
                {
                    reason = "breach failed " + plan.breachReplans + "x, direct assault";
                }
                else
                {
                    var anchor = active.FirstOrDefault(b => b.WallsGone) ?? active[0];
                    plan.gather = anchor.staging.IsValid ? anchor.staging : plan.gather;
                    PlanBreach(lord, plan, alive);
                    plan.lastBreachProgressTick = now;
                    plan.lastBreachDamage = 0;
                    plan.hHourReason = (innerOnly ? "next wall layer" : "breach stalled") + " #" + plan.breachReplans;
                    Log(plan);
                    if (plan.mode == PlanMode.Breach) lord.CurLordToil?.UpdateAllDuties();
                    return false;
                }
            }
            if (dutiesChanged && reason == null) lord.CurLordToil?.UpdateAllDuties();
            return true;
        }
    }

    // ---------------- hooks ----------------

    [HarmonyPatch(typeof(LordToil_AssaultColonyBreaching), nameof(LordToil_AssaultColonyBreaching.UpdateAllDuties))]
    public static class Patch_Breaching_AssaultPlan
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(LordToil_AssaultColonyBreaching __instance) => !Take(__instance.lord);

        public static bool Take(Lord lord)
        {
            if (!AssaultPlanner.Applies(lord)) return false;
            try
            {
                var plan = AssaultPlanner.GetOrCreate(lord);
                return AssaultPlanner.ApplyDuties(lord, plan);
            }
            catch (Exception e)
            {
                Verse.Log.ErrorOnce("[HSK Tactical AI] assault planner failed, using vanilla: " + e, 0x48534B33);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(LordToil_AssaultColony), nameof(LordToil_AssaultColony.UpdateAllDuties))]
    public static class Patch_Assault_AssaultPlan
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(LordToil_AssaultColony __instance) => !Patch_Breaching_AssaultPlan.Take(__instance.lord);
    }

    // ---------------- job givers ----------------

    /// <summary>Go to my spot at the group's assault position and hold; fight only when shot at.</summary>
    public class JobGiver_HSKTAI_Approach : ThinkNode_JobGiver
    {
        public static int FireAndMovementShots;
        private static readonly JobGiver_AIFightEnemies fight = new JobGiver_AIFightEnemies();

        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState?.duty;
            if (duty == null) return null;
            IntVec3 spot = duty.focus.Cell;
            Job throwJ = Grenades.TryThrowJob(pawn, AssaultPlanner.PlanOf(pawn));
            if (throwJ != null) return throwJ;
            // breach preparation: stackers plunder near their stack point (out of fire) until their breach is about to open
            var ps = TacticalAIMod.Settings;
            AssaultPlan prep = AssaultPlanner.PlanOf(pawn);
            // out of range (attrition): whoever is not pinning the defenders plunders the outskirts, out of fire
            if (ps.plunder && prep != null && prep.mode == PlanMode.Walk && prep.phase == PlanPhase.Approach
                && prep.crossing == CrossingMode.Attrition && !RaidRoles.IsBaseOfFire(prep, pawn) && prep.spotOf.TryGetValue(pawn, out IntVec3 waitAt))
            {
                long tp0 = Perf.Start();
                Job pw = Plunder.TryPlunder(pawn, prep, waitAt, 30f, 1);
                Perf.Stop("approach.plunder", tp0);
                if (pw != null) { Plunder.PrepJobs++; return pw; }
            }
            if (ps.plunder && ps.plunderDuringBreach && prep != null && prep.mode == PlanMode.Breach && prep.phase == PlanPhase.Approach
                && !prep.breachTeam.Contains(pawn) && !prep.overwatchOf.ContainsKey(pawn) && prep.spotOf.TryGetValue(pawn, out IntVec3 stackAt)
                && !Plunder.Recalled(prep, prep.BreachFor(pawn)))
            {
                long tp1 = Perf.Start();
                Job pj = Plunder.TryPlunder(pawn, prep, stackAt, ps.plunderPrepRadius, 1);
                Perf.Stop("approach.plunder", tp1);
                if (pj != null) { Plunder.PrepJobs++; return pj; }
            }
            bool atSpot = spot.IsValid && pawn.Position.DistanceTo(spot) <= Mathf.Max(duty.radius, 1.5f);
            ThreatMap tm = MapComponent_TacticalAI.For(pawn.Map)?.Threat;
            float fireHere = tm?.FireAt(pawn.Position) ?? 0f;

            // shot at: fight only if the enemy is close or we're already in position; otherwise keep moving out of fire
            // breach assault: never stop in the hole (the fatal funnel). At the breach, go straight through and clear
            // the opening sideways - left/right alternating - then fight inside
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            bool breachAssault = plan != null && plan.mode == PlanMode.Breach && plan.phase == PlanPhase.Assault;
            // never run into our own grenades: hold short of a fresh blast area
            if (Grenades.InDanger(plan, spot) || Grenades.InDanger(plan, pawn.Position + (spot - pawn.Position).ClampMagnitude(3)))
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            if (breachAssault && !plan.entered.Contains(pawn))
            {
                // corner fire: gunners lean out from the wall ends beside the hole
                Job corner = CornerFire.TryCornerJob(pawn, plan, spot);
                if (corner != null) return corner;
                // clear by fire (playtest: "better to be outside, cover the breach from cover and shoot inside until there is no more
                // enemy to shoot at, then slowly approach"): nobody goes through while a defender who isn't pinned covers the gap
                if (pawn.Position.DistanceTo(spot) <= 8f && !ShieldWall.IsBearer(plan, pawn) && CornerFire.GapHot(plan, spot))
                    return CornerFire.HoldAndFire(pawn, plan, spot);
                // waves (playtest: "deadly funnel, all try to enter through the same breach at the same time"): every raid, however
                // dull, goes through a breach at most 3 at a time, 240 ticks apart; the rest hold 6 cells back, not in the gap
                if (pawn.Position.DistanceTo(spot) <= 6f && !BreachWaves.MayGo(plan, spot, pawn))
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            }
            if (breachAssault && pawn.Position.DistanceTo(spot) <= 3f)
            {
                plan.entered.Add(pawn);
                BreachWaves.Went(plan, spot, pawn);
                pawn.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_Assault, plan.objective);
                IntVec3 clear = ClearPoint(pawn, spot, plan.objective);
                if (clear.IsValid)
                {
                    Job through = JobMaker.MakeJob(JobDefOf.Goto, clear);
                    through.locomotionUrgency = LocomotionUrgency.Sprint;
                    through.expiryInterval = 240;
                    return through;
                }
                return null;
            }
            // walk assault (open ground): engage defenders in range while advancing - fire and movement - like vanilla raiders
            bool walkAssault = plan != null && plan.mode == PlanMode.Walk && plan.phase == PlanPhase.Assault;
            if (walkAssault && TacticalAIMod.Settings.syncedAssault && Competence.Knows(plan, Doctrine.SyncedAssault))
            {
                int tick = Find.TickManager.TicksGame;
                // staggered start: shorter routes wait so every axis arrives together
                if (plan.axisOf.TryGetValue(pawn, out int ax) && plan.axisGoTick.TryGetValue(ax, out int goAt) && tick < goAt)
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, Mathf.Min(60, goAt - tick), true);
                // fire and movement: the covering half stops and shoots while the other half moves
                if (plan.assaultElementOf.TryGetValue(pawn, out int el) && !ShieldWall.IsBearer(plan, pawn))
                {
                    int moving = ((tick - plan.hHourTick) / Mathf.Max(60, TacticalAIMod.Settings.assaultBoundTicks)) % 2;
                    Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                    if (el != moving && v != null && !v.IsMeleeAttack)
                    {
                        Pawn target = Targeting.Best(pawn, plan, v, 50f);
                        if (target != null)
                        {
                            Job cover = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                            cover.maxNumStaticAttacks = 2;
                            cover.endIfCantShootTargetFromCurPos = true;
                            cover.expiryInterval = 90;
                            cover.checkOverrideOnExpire = true;
                            FireAndMovementShots++;
                            return cover;
                        }
                    }
                }
            }
            if (walkAssault && EnemyInRange(pawn))
            {
                Job j = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                if (j != null) return j;
            }
            if (!breachAssault && Find.TickManager.TicksGame - pawn.mindState.lastHarmTick < 240 && (atSpot || EnemyWithin(pawn, 10f)))
            {
                Job j = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                if (j != null) return j;
            }
            if (atSpot) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 180, true);

            long tm0 = Perf.Start();
            Job mv = MoveAlongRoute(pawn, spot, fireHere);
            Perf.Stop("approach.move", tm0);
            return mv;
        }

        /// <summary>
        /// Where to go just inside a breach: 3 cells in, 2-3 cells to one side (by pawn, so the group splits left and
        /// right instead of piling up in the opening). Standable and reachable, else just inside.
        /// </summary>
        private static IntVec3 ClearPoint(Pawn pawn, IntVec3 entry, IntVec3 objective)
        {
            float dx = objective.x - entry.x, dz = objective.z - entry.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1f) return IntVec3.Invalid;
            dx /= len; dz /= len;
            // wide (playtest: "others follow and go around wide angle"): alternate left / right per opening, 4 then 5.5
            // cells out, 2.5 in; narrower if the room is tight
            var plan = AssaultPlanner.PlanOf(pawn);
            int k = 0;
            if (plan != null) { plan.clearCount.TryGetValue(entry, out k); plan.clearCount[entry] = k + 1; }
            else k = pawn.thingIDNumber;
            int side = (k & 1) == 0 ? 1 : -1;
            Map map = pawn.Map;
            foreach (float lateral in new[] { (k / 2) % 2 == 0 ? 4f : 5.5f, 3f, 2f })
            {
                var c = new IntVec3(entry.x + Mathf.RoundToInt(dx * 2.5f - dz * lateral * side), 0, entry.z + Mathf.RoundToInt(dz * 2.5f + dx * lateral * side));
                if (c.InBounds(map) && c.Standable(map) && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) return c;
            }
            var inside = new IntVec3(entry.x + Mathf.RoundToInt(dx * 3f), 0, entry.z + Mathf.RoundToInt(dz * 3f));
            return inside.InBounds(map) && inside.Standable(map) && pawn.CanReach(inside, PathEndMode.OnCell, Danger.Deadly) ? inside : IntVec3.Invalid;
        }

        /// <summary>Goto the next waypoint of the pawn's covered route (wide arc / breach route), else the spot.</summary>
        public static Job MoveAlongRoute(Pawn pawn, IntVec3 spot, float fireHere)
        {
            IntVec3 next = NextWaypoint(pawn, spot);
            if (!next.IsValid || !pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly)) next = spot;
            if (Grenades.InDanger(AssaultPlanner.PlanOf(pawn), next)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            if (!next.IsValid || !pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly))
            {
                // spot unreachable (e.g. walled in): fall back to a reachable cell near it
                next = spot.IsValid ? CellFinder.RandomClosewalkCellNear(spot, pawn.Map, 6, c => pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) : IntVec3.Invalid;
                if (!next.IsValid || !pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
            }
            Job g = JobMaker.MakeJob(JobDefOf.Goto, next);
            AssaultPlan rp = AssaultPlanner.PlanOf(pawn);
            bool breachRush = rp != null && rp.mode == PlanMode.Breach && rp.phase == PlanPhase.Assault;   // through the breach fast
            g.locomotionUrgency = fireHere > 0.3f || breachRush ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            g.expiryInterval = 300;
            g.checkOverrideOnExpire = true;
            return g;
        }

        /// <summary>Follow the group's covered route: next waypoint not yet reached (the last one is the pawn's own spot).</summary>
        private static IntVec3 NextWaypoint(Pawn pawn, IntVec3 spot)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null) return spot;
            List<IntVec3> wps = null;
            if (plan.mode == PlanMode.Breach)
            {
                // the covered route leads to the stack point; at H-hour the way in is the short dash to the breach
                var bp = plan.BreachFor(pawn);
                if (bp == null || plan.phase != PlanPhase.Approach || !bp.staging.IsValid || spot.DistanceTo(bp.staging) > 8f) return spot;
                wps = bp.route;
            }
            else if (plan.mode == PlanMode.Walk && plan.phase == PlanPhase.Assault && plan.axisOf.TryGetValue(pawn, out int aaxis) && aaxis < plan.assaultRoutes.Count)
                wps = plan.assaultRoutes[aaxis];
            else if (plan.mode == PlanMode.Walk && plan.axisOf.TryGetValue(pawn, out int axis) && axis < plan.waypoints.Count)
                wps = plan.waypoints[axis];
            if (wps == null || wps.Count == 0) return spot;
            if (!plan.waypointIndex.TryGetValue(pawn, out int i))
            {
                // join the route at the nearest waypoint (late arrivals, reinforcements)
                i = 0;
                float best = float.MaxValue;
                for (int k = 0; k < wps.Count; k++)
                {
                    float d = wps[k].DistanceToSquared(pawn.Position);
                    if (d < best) { best = d; i = k; }
                }
            }
            if (i >= wps.Count) i = wps.Count - 1;
            while (i < wps.Count - 1 && pawn.Position.DistanceTo(wps[i]) <= 3f) i++;
            plan.waypointIndex[pawn] = i;
            return i >= wps.Count - 1 ? spot : wps[i];
        }

        /// <summary>A hostile the pawn's weapon can hit from where it stands.</summary>
        private static bool EnemyInRange(Pawn pawn)
        {
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null || v.IsMeleeAttack) return EnemyWithin(pawn, 6f);
            float r = v.verbProps.range;
            foreach (IAttackTarget t in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
                if (!t.ThreatDisabled(pawn) && t.Thing.Position.DistanceTo(pawn.Position) <= r && v.CanHitTarget(t.Thing)) return true;
            return false;
        }

        private static bool EnemyWithin(Pawn pawn, float dist)
        {
            foreach (IAttackTarget t in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
                if (!t.ThreatDisabled(pawn) && t.Thing.Position.DistanceTo(pawn.Position) <= dist) return true;
            return false;
        }
    }

    /// <summary>
    /// Breach team (SOSRA "reduce"): get to the stack point on the covered route, wait for the go signal (all breach
    /// points start together), then open THIS breach point by its method - fire for wood, charges (repeatedly, one
    /// planter at a time, keeping back while one is armed), launchers from a firing position, breach tools.
    /// </summary>
    public class JobGiver_HSKTAI_Breach : ThinkNode_JobGiver
    {
        private static readonly JobGiver_AIFightEnemies fight = new JobGiver_AIFightEnemies();
        private static readonly Dictionary<Pawn, int> lastLauncherOrder = new Dictionary<Pawn, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            BreachPoint bp = plan?.BreachFor(pawn);
            IntVec3 staging = bp?.staging ?? pawn.mindState?.duty?.focus.Cell ?? IntVec3.Invalid;
            Building wall = bp?.CurrentWall();
            if (wall == null) return Hold(pawn, staging);

            // first get to the stack point along the covered route, like everyone else
            if (staging.IsValid && !bp.reachedStaging.Contains(pawn))
            {
                if (pawn.Position.DistanceTo(staging) > 6f) return Hold(pawn, staging);
                bp.reachedStaging.Add(pawn);
            }
            // all breach points start together
            if (plan.goTick < 0) return Hold(pawn, staging);

            // charge armed / wall burning: stay clear until it's done
            if (BreachKit.BreachUnderWay(pawn.Map, bp)) return Hold(pawn, staging);

            IntVec3 prefer = bp.plantSide.IsValid ? bp.plantSide : staging;
            switch (bp.method)
            {
                case BreachMethod.Grenade:
                {
                    Job gj = Grenades.ThrowAtBuilding(pawn, plan, wall);
                    if (gj != null) return gj;
                    return Hold(pawn, staging);
                }
                case BreachMethod.Tunnel:
                {
                    if (bp.blasting && BreachKit.CarriedMiningCharge(pawn) != null)
                    {
                        // blast through: one sapper plants at the face, everyone keeps back until it goes off, repeat
                        if (!OnePerBreach(bp, pawn, staging, p => BreachKit.CarriedMiningCharge(p) != null)) return Hold(pawn, staging);
                        int bi = bp.line.FindIndex(c => c.InBounds(pawn.Map) && c.GetEdifice(pawn.Map) is Building eb && eb.def.mineable);
                        if (bi < 0) return Hold(pawn, staging);
                        Building face0 = (Building)bp.line[bi].GetEdifice(pawn.Map);
                        IntVec3 spotB = bi > 0 && bp.line[bi - 1].Standable(pawn.Map) ? bp.line[bi - 1] : IntVec3.Invalid;
                        if (!spotB.IsValid && !BreachKit.TryFindPlantSpot(pawn, face0, prefer, out spotB)) return Hold(pawn, staging);
                        Job plantM = JobMaker.MakeJob(TAI_DefOf.HSKTAI_PlantBreachCharge, face0, spotB);
                        plantM.expiryInterval = 2000;
                        return plantM;
                    }
                    // one miner per lane; stand in the last opened cell of my lane (or beside it in lane 1), dig the next rock
                    if (!bp.laneOf.TryGetValue(pawn, out int lane)) { lane = bp.laneOf.Count < 2 ? bp.laneOf.Count : 0; bp.laneOf[pawn] = lane; }
                    var cells = lane == 1 && bp.line2.Count > 0 ? bp.line2 : bp.line;
                    int wi = cells.FindIndex(c => c.InBounds(pawn.Map) && c.GetEdifice(pawn.Map) is Building eb && eb.def.mineable);
                    if (wi < 0) { BreachKit.RestoreWeapon(pawn); return Hold(pawn, staging); }   // my lane is through
                    Building rockAt = (Building)cells[wi].GetEdifice(pawn.Map);
                    if (rockAt.Map.reservationManager.IsReservedByAnyoneOf(rockAt, pawn.Faction) && !rockAt.Map.reservationManager.ReservedBy(rockAt, pawn)) return Hold(pawn, staging);
                    wall = rockAt;
                    IntVec3 face = wi > 0 && cells[wi - 1].Standable(pawn.Map) ? cells[wi - 1] : (wi > 0 && wi - 1 < bp.line.Count ? bp.line[wi - 1] : prefer);
                    if (face.IsValid && face.Standable(pawn.Map) && pawn.Position != face && pawn.CanReach(face, PathEndMode.OnCell, Danger.Deadly))
                    {
                        Job go = JobMaker.MakeJob(JobDefOf.Goto, face);
                        go.locomotionUrgency = LocomotionUrgency.Jog;
                        go.expiryInterval = 600;
                        return go;
                    }
                    BreachKit.EquipToolForDigging(pawn);
                    Job mine = JobMaker.MakeJob(JobDefOf.Mine, wall);
                    mine.expiryInterval = 2000;
                    mine.ignoreDesignations = true;
                    return mine;
                }
                case BreachMethod.Fire:
                {
                    if (!OnePerBreach(bp, pawn, staging, p => true)) return Hold(pawn, staging);
                    if (!BreachKit.TryFindPlantSpot(pawn, wall, prefer, out IntVec3 spot)) break;
                    if (pawn.Position != spot)
                    {
                        Job go = JobMaker.MakeJob(JobDefOf.Goto, spot);
                        go.locomotionUrgency = LocomotionUrgency.Jog;
                        go.expiryInterval = 600;
                        return go;
                    }
                    Job ignite = JobMaker.MakeJob(JobDefOf.Ignite, wall);
                    ignite.expiryInterval = 600;
                    BreachKit.FiresStarted++;
                    return ignite;
                }
                case BreachMethod.Bash:
                {
                    // break the door down from the raid's side, all bashers at once
                    if (BreachKit.TryFindPlantSpot(pawn, wall, prefer, out IntVec3 bashSpot) && pawn.Position != bashSpot && !pawn.Position.AdjacentTo8WayOrInside(wall))
                    {
                        Job goB = JobMaker.MakeJob(JobDefOf.Goto, bashSpot);
                        goB.locomotionUrgency = LocomotionUrgency.Jog;
                        goB.expiryInterval = 600;
                        return goB;
                    }
                    Job bash = JobMaker.MakeJob(JobDefOf.AttackMelee, wall);
                    bash.expiryInterval = 600;
                    return bash;
                }
                case BreachMethod.Charge:
                {
                    if (BreachKit.CarriedCharge(pawn) == null) break;
                    if (!OnePerBreach(bp, pawn, staging, p => BreachKit.CarriedCharge(p) != null)) return Hold(pawn, staging);
                    if (BreachKit.TryFindPlantSpot(pawn, wall, prefer, out IntVec3 spot))
                    {
                        Job plant = JobMaker.MakeJob(TAI_DefOf.HSKTAI_PlantBreachCharge, wall, spot);
                        plant.expiryInterval = 2000;
                        return plant;
                    }
                    break;
                }
            }
            Verb launcher = AssaultPlanner.DestructiveRangedVerb(pawn);
            // the right round for the wall (playtest: the contact beam fired its tox grenades at it): reload a breaching one
            Job reloadB = Launchers.ReloadForBreach(pawn, launcher);
            if (reloadB != null) return reloadB;
            if (launcher != null && launcher.Available())   // out of ammo: the other ways below
            {
                // fire from a covered position: in range, line of sight to the wall, out of the defenders' fire, clear
                // of our own blast - not from wherever the cast-position finder would walk to
                IntVec3 fp = FiringPosition(pawn, bp, wall, launcher);
                if (fp.IsValid && fp != pawn.Position)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, fp);
                    go.locomotionUrgency = LocomotionUrgency.Jog;
                    go.expiryInterval = 600;
                    return go;
                }
                // final test (full modlist): "started 10 jobs in one tick" - a shot that can't go off now (no ammo, cooling
                // down) ends at once and was ordered again in the same tick. No ammo: other ways in; and wait a
                // moment instead of reordering the same shot within 30 ticks.
                int nowT = Find.TickManager.TicksGame;
                if ((lastLauncherOrder.TryGetValue(pawn, out int lastT) && nowT - lastT < 30 && pawn.CurJobDef != JobDefOf.UseVerbOnThing))
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
                lastLauncherOrder[pawn] = nowT;
                Job shoot = JobMaker.MakeJob(JobDefOf.UseVerbOnThing, wall);
                shoot.verbToUse = launcher;
                shoot.preventFriendlyFire = true;
                shoot.expiryInterval = 600;
                return shoot;
            }
            if (AssaultPlanner.HasBreachTool(pawn))
            {
                // from the raid's side of the wall, never the inside face reached through the funnel
                if (BreachKit.TryFindPlantSpot(pawn, wall, prefer, out IntVec3 hitSpot) && pawn.Position != hitSpot && !pawn.Position.AdjacentTo8WayOrInside(wall))
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, hitSpot);
                    go.locomotionUrgency = LocomotionUrgency.Jog;
                    go.expiryInterval = 600;
                    return go;
                }
                Job hit = JobMaker.MakeJob(JobDefOf.AttackMelee, wall);
                hit.expiryInterval = 600;
                return hit;
            }
            return Hold(pawn, staging);
        }

        private static readonly Dictionary<Pawn, (Building wall, IntVec3 cell)> firingPos = new Dictionary<Pawn, (Building, IntVec3)>();

        /// <summary>
        /// Firing position against a breach wall: within ~85 % of the verb's range (max 30), at least blast radius + 3
        /// from the wall, standable, out of the defenders' fire, line of sight to the wall, reachable; closest to the
        /// pawn's stack point. Cached per pawn and wall; the pawn's own cell if it already qualifies.
        /// </summary>
        private static IntVec3 FiringPosition(Pawn pawn, BreachPoint bp, Building wall, Verb verb)
        {
            Map map = pawn.Map;
            var s = TacticalAIMod.Settings;
            ThreatMap tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            // from further out and from cover (playtest 2026-10-25: "try to engage the walls from a larger distance and if
            // possible from cover"): 55-100 % of 85 % of the range, covered from the defence (was: at most 30 cells, the
            // spot nearest the stack point)
            float range = Mathf.Min(verb.verbProps.range * 0.85f, 50f);
            float blast = (verb.verbProps.defaultProjectile?.projectile?.explosionRadius ?? 2f) + 3f;
            float far = Mathf.Max(blast, range * 0.55f);
            IntVec3 threat = AssaultPlanner.PlanOf(pawn)?.objective ?? wall.Position;
            bool Basic(IntVec3 c)
            {
                if (!c.InBounds(map) || !c.Standable(map)) return false;
                float d = c.DistanceTo(wall.Position);
                if (d > range || d < blast) return false;
                return !haveThreat || tm.FireAt(c) <= s.safeFire;
            }
            bool Covered(IntVec3 c) => JobGiver_HSKTAI_Support.HasCover(c, threat, map);
            if (Basic(pawn.Position) && pawn.Position.DistanceTo(wall.Position) >= far && Covered(pawn.Position) && verb.CanHitTargetFrom(pawn.Position, wall)) return pawn.Position;
            if (firingPos.TryGetValue(pawn, out var cached) && cached.wall == wall && cached.cell.IsValid && Basic(cached.cell) && verb.CanHitTargetFrom(cached.cell, wall)) return cached.cell;
            IntVec3 anchor = bp.staging.IsValid ? bp.staging : pawn.Position;
            var cands = new List<(IntVec3 c, float score)>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(wall.Position, range, true))
            {
                if (!Basic(c)) continue;
                float d = c.DistanceTo(wall.Position);
                cands.Add((c, (d < far ? (far - d) * 2f : 0f) + (Covered(c) ? 0f : 10f) + c.DistanceTo(anchor) * 0.15f + (haveThreat ? tm.FireAt(c) * 30f : 0f)));
            }
            IntVec3 best = IntVec3.Invalid;
            int tested = 0;
            foreach (var cand in cands.OrderBy(x => x.score))
            {
                if (++tested > 40) break;
                if (!verb.CanHitTargetFrom(cand.c, wall) || !pawn.CanReach(cand.c, PathEndMode.OnCell, Danger.Deadly)) continue;
                best = cand.c;
                if (Covered(best)) AssaultPlanner.BreachShotsFromCover++;
                break;
            }
            // nothing in the top 40: as before, any spot in range with a shot
            if (!best.IsValid && Basic(pawn.Position) && verb.CanHitTargetFrom(pawn.Position, wall)) best = pawn.Position;
            firingPos[pawn] = (wall, best);
            return best;
        }

        /// <summary>One worker at the wall at a time (charge planter, fire starter); the others cover and take over.</summary>
        private static bool OnePerBreach(BreachPoint bp, Pawn pawn, IntVec3 staging, Func<Pawn, bool> canDo)
        {
            Pawn cur = bp.planter;
            if (cur == null || cur.Dead || cur.Downed || !cur.Spawned || cur.GetLord() != pawn.GetLord() || !canDo(cur)
                || (staging.IsValid && !bp.reachedStaging.Contains(cur)))
                bp.planter = cur = pawn;
            return cur == pawn;
        }

        private static Job Hold(Pawn pawn, IntVec3 staging)
        {
            if (Find.TickManager.TicksGame - pawn.mindState.lastHarmTick < 240)
            {
                Job j = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                if (j != null) return j;
            }
            if (staging.IsValid && pawn.Position.DistanceTo(staging) > 4f && pawn.CanReach(staging, PathEndMode.OnCell, Danger.Deadly))
            {
                float fire = MapComponent_TacticalAI.For(pawn.Map)?.Threat?.FireAt(pawn.Position) ?? 0f;
                return JobGiver_HSKTAI_Approach.MoveAlongRoute(pawn, staging, fire);
            }
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
        }
    }

    /// <summary>Last resort in the assault: move on the objective.</summary>
    public class JobGiver_HSKTAI_GotoFocus : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            IntVec3 c = pawn.mindState?.duty?.focus.Cell ?? IntVec3.Invalid;
            if (!c.IsValid || pawn.Position.DistanceTo(c) < 3f) return null;
            if (!pawn.CanReach(c, PathEndMode.Touch, Danger.Deadly)) return null;
            Job g = JobMaker.MakeJob(JobDefOf.Goto, c);
            g.locomotionUrgency = LocomotionUrgency.Jog;
            g.expiryInterval = 300;
            g.checkOverrideOnExpire = true;
            return g;
        }
    }
}
