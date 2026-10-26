using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    public enum RaidOp { None, HammerAnvil, Vanguard }

    /// <summary>
    /// Coordinated operations (Raid Tactics ideas, adapted to our planner):
    /// - Hammer and anvil: a faction that can lay sieges splits a third of the raid off as a siege group that digs in at
    ///   the siege spot and shells you; the rest waits out of range at its assault positions and goes in once the guns
    ///   have been firing a while (at most 50 s after the assault is otherwise ready).
    /// - Armoured spearhead (vanguard): the best-armoured quarter of the assault goes in first at H-hour; the rest fight from
    ///   where they stand and follow once the spearhead is inside (or down, or after 20 s).
    /// A sharp command only (synced assault); rolled per raid (operationChance, twice as likely after a failed raid here).
    /// </summary>
    public static class Operations
    {
        public static int Hammers, HammerSplits, Vanguards, VanguardReleases, HammerHolds;
        public static string LastWhy = "";
        private static readonly Dictionary<int, RaidOp> chosen = new Dictionary<int, RaidOp>();
        private static readonly Dictionary<int, (int lordId, IntVec3 spot)> split = new Dictionary<int, (int, IntVec3)>();

        private static bool Able(Pawn p) => p != null && p.RaceProps.Humanlike && !p.Downed && !p.Dead && !p.InMentalState;

        public static void Plan(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            if (!s.operations || lord?.faction == null || plan.goal != RaidGoal.Assault || (plan.mode != PlanMode.Walk && plan.mode != PlanMode.Breach)) return;
            if (!Competence.Knows(plan, Doctrine.SyncedAssault)) return;
            var people = pawns.Where(Able).ToList();
            if (!chosen.TryGetValue(lord.loadID, out RaidOp op))
            {
                bool canHammer = lord.faction.def.canSiege && people.Count >= 10 && Competence.Knows(plan, Doctrine.SupportFire);
                bool canVanguard = people.Count >= 8;
                string forced = s.operationOverride?.ToLowerInvariant() ?? "";
                if (forced == "hammer") op = canHammer ? RaidOp.HammerAnvil : RaidOp.None;
                else if (forced == "vanguard") op = canVanguard ? RaidOp.Vanguard : RaidOp.None;
                else
                {
                    var mem = FactionMemory.Read(lord.faction);
                    float chance = s.operationChance * (mem != null && (mem.failWalk >= 0.3f || mem.failBreach >= 0.3f) ? 2f : 1f);
                    op = RaidOp.None;
                    if (Competence.Hash01(plan.lordId, 0x0FE1) < chance)
                    {
                        bool hammerFirst = Competence.Hash01(plan.lordId, 0x0FE2) < 0.5f;
                        op = canHammer && (hammerFirst || !canVanguard) ? RaidOp.HammerAnvil : canVanguard ? RaidOp.Vanguard : RaidOp.None;
                    }
                }
                chosen[lord.loadID] = op;
                if (op == RaidOp.HammerAnvil) Hammers++;
                if (op == RaidOp.Vanguard) Vanguards++;
            }
            plan.op = op;
            // a re-plan of the same raid (the Lab plans again) keeps where the hammer stands (rt2: split never happened)
            if (op == RaidOp.HammerAnvil)
            {
                if (split.TryGetValue(lord.loadID, out var sp)) { plan.hammerPending = false; plan.hammerLordId = sp.lordId; plan.hammerSpot = sp.spot; }
                else plan.hammerPending = true;
            }
            if (op == RaidOp.Vanguard) AssignVanguard(plan, people);
            if (op != RaidOp.None) plan.capability += "; operation: " + (op == RaidOp.HammerAnvil ? "hammer and anvil" : "armoured spearhead (" + plan.vanguard.Count + ")");
        }

        private static void AssignVanguard(AssaultPlan plan, List<Pawn> people)
        {
            plan.vanguard.Clear();
            var pool = people.Where(p => !plan.breachTeam.Contains(p) && CrewWeapons.TeamOf(plan, p) == null && !RaidRoles.IsBaseOfFire(plan, p)
                                         && !(plan.roleOf.TryGetValue(p, out var r) && r == RaidRole.Medic) && p != plan.leader).ToList();
            int n = Mathf.Clamp(Mathf.RoundToInt(pool.Count * 0.25f), 2, 6);
            foreach (var p in pool.OrderByDescending(p => p.GetStatValue(StatDefOf.ArmorRating_Sharp)).Take(n)) plan.vanguard.Add(p);
        }

        /// <summary>Every 60 ticks: hammer - split the siege group off (once); vanguard - release the rest once it is inside.</summary>
        public static void Tick(Map map, Lord lord, AssaultPlan plan, List<Pawn> alive, int now)
        {
            if (plan.op == RaidOp.HammerAnvil && plan.hammerPending && plan.phase == PlanPhase.Approach)
            {
                plan.hammerPending = false;
                int want = Mathf.Max(3, Mathf.RoundToInt(alive.Count * 0.3f));
                var shellers = alive.Where(p => Able(p) && !plan.breachTeam.Contains(p) && CrewWeapons.TeamOf(plan, p) == null && p != plan.leader)
                                    .OrderBy(p => p.kindDef?.combatPower ?? 50f).Take(want).ToList();
                if (shellers.Count < 3) { plan.op = RaidOp.None; return; }
                IntVec3 spot = SiegeGuards.SiegeSpot(plan, map);
                foreach (var p in shellers) { lord.RemovePawn(p); plan.members.Remove(p); }
                float pts = Mathf.Max(60f, shellers.Sum(p => p.kindDef?.combatPower ?? 50f) * 0.5f);
                Lord nl = LordMaker.MakeNewLord(lord.faction, new LordJob_Siege(lord.faction, spot, pts), map, shellers);
                Competence.RegisterConverted(nl, plan);
                plan.hammerLordId = nl.loadID;
                plan.hammerSpot = spot;
                split[lord.loadID] = (nl.loadID, spot);
                HammerSplits++;
                LastWhy = "hammer and anvil: " + shellers.Count + " dig in at " + spot + ", " + (alive.Count - shellers.Count) + " wait";
                plan.capability += "; " + LastWhy;
                lord.CurLordToil?.UpdateAllDuties();
            }
            if (plan.op == RaidOp.HammerAnvil) SeeGuns(map, plan, now);
            if (plan.op == RaidOp.Vanguard && plan.phase == PlanPhase.Assault && !plan.vanguardIn && plan.hHourTick >= 0)
            {
                bool inside = plan.vanguard.Any(v => Able(v) && v.Spawned && v.Map == map
                                                     && (v.Position.DistanceTo(plan.objective) <= 12f || (map.areaManager.Home[v.Position] && v.Position.Roofed(map))));
                bool gone = plan.vanguard.All(v => !Able(v) || !v.Spawned);
                if (inside || gone || now - plan.hHourTick > 1200)
                {
                    plan.vanguardIn = true;
                    VanguardReleases++;
                    LastWhy = "spearhead " + (inside ? "inside" : gone ? "down" : "20 s") + ": the rest commit";
                    plan.capability += "; " + LastWhy;
                    lord.CurLordToil?.UpdateAllDuties();
                }
            }
        }

        /// <summary>The siege group's first gun near its spot (checked every tick from Operations.Tick).</summary>
        private static void SeeGuns(Map map, AssaultPlan plan, int now)
        {
            if (plan.hammerGunTick >= 0 || plan.hammerLordId < 0) return;
            if (map.listerBuildings.allBuildingsNonColonist.Any(b => b.Faction == plan.faction && b is Building_Turret && b.Position.DistanceTo(plan.hammerSpot) <= 25f))
                plan.hammerGunTick = now;
        }

        /// <summary>
        /// Hammer: H-hour waits until the siege guns have been firing about 25 s - counted from when the first gun stood, so
        /// guns built while the breach was being opened count - at most 50 s after the assault was otherwise ready
        /// (rt5: the cap ran from the plan, and a breach that took 10 000 ticks found it long expired).
        /// </summary>
        public static bool Holding(Map map, AssaultPlan plan, int now, ref string reason)
        {
            if (plan.op != RaidOp.HammerAnvil || plan.hammerLordId < 0) return false;
            if (plan.hammerReadyTick < 0) plan.hammerReadyTick = now;
            SeeGuns(map, plan, now);
            if (plan.hammerGunTick >= 0 && now - plan.hammerGunTick >= 1500) { reason += ", after the shelling"; return false; }
            if (now - plan.hammerReadyTick > 3000) { reason += ", shelling not started in time"; return false; }
            HammerHolds++;
            return true;
        }

        public static bool Holds(AssaultPlan plan, Pawn p) =>
            plan != null && plan.op == RaidOp.Vanguard && plan.phase == PlanPhase.Assault && !plan.vanguardIn && plan.vanguard.Count > 0
            && !plan.vanguard.Contains(p) && !RaidRoles.IsBaseOfFire(plan, p) && !plan.breachTeam.Contains(p) && CrewWeapons.TeamOf(plan, p) == null;
    }

    /// <summary>Armoured spearhead: the rest fight from where they stand until the spearhead is inside.</summary>
    public class JobGiver_HSKTAI_Vanguard : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (!Operations.Holds(plan, pawn)) return null;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            Pawn foe = Suppression.Defenders(pawn.Map, pawn.Position, v?.verbProps.range ?? 20f)
                                  .Where(d => GenSight.LineOfSight(pawn.Position, d.Position, pawn.Map, true))
                                  .OrderBy(d => d.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            if (foe != null && v != null && !v.verbProps.IsMeleeAttack && v.CanHitTarget(foe))
            {
                Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, foe);
                shoot.maxNumStaticAttacks = 3;
                shoot.endIfCantShootTargetFromCurPos = true;
                shoot.expiryInterval = 120;
                shoot.checkOverrideOnExpire = true;
                return shoot;
            }
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
        }
    }
}
