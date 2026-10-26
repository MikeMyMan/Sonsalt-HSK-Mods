using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// Plunder (playtest, 2026-10-23: "on an attack one does not need to chase the defenders all the way, the attackers can
    /// do a lot of damage without having to chase the defenders they can loot, burn kill animals etc. forcing the defender
    /// to engage them. In the real game it is not about wiping out the defenders").
    ///
    /// In the assault, a raider that has no shot and whose nearest defender is far (plunderChaseDist), behind a funnel or
    /// out of reach turns on the colony instead of chasing: kill livestock, loot valuables (at most a share of the raid at
    /// once; they carry them off the map), set crops and wooden buildings alight, smash the valuable buildings. Targets
    /// inside the defenders' field of fire are skipped. Two raiders keep watching a funnel mouth (funnel caution); when
    /// nothing is left to take and the defenders stay holed up, the raid withdraws with what it has.
    /// </summary>
    public static class Plunder
    {
        public enum Task { Kill, Loot, Burn, Wreck }

        public static int LootJobs, BurnJobs, KillJobs, WreckJobs, Withdrawals, PrepJobs;

        // performance (Lab: a plunder search up to 24 ms on a big colony): the burnable and wreckable things are listed
        // once per map every 250 ticks (stats such as flammability and market value are the expensive part), and each
        // plunderer picks from the lists
        private class Targets { public int tick = -9999; public List<Thing> burn = new List<Thing>(); public List<(Building b, float value)> wreck = new List<(Building, float)>(); }
        private static readonly Dictionary<Map, Targets> targets = new Dictionary<Map, Targets>();

        // performance (pf6: approach.plunder up to 21 ms): flammability once per def and stuff, not a stat per building per rebuild
        private static readonly Dictionary<(ThingDef, ThingDef), bool> flammable = new Dictionary<(ThingDef, ThingDef), bool>();
        private static bool Flammable(Building b)
        {
            var key = (b.def, b.Stuff);
            if (!flammable.TryGetValue(key, out bool f)) flammable[key] = f = b.GetStatValue(StatDefOf.Flammability) >= 0.5f;
            return f;
        }

        private static Targets TargetsFor(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (targets.TryGetValue(map, out var t) && now - t.tick < 250 * Adaptive.Load) return t;
            t = new Targets { tick = now };
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (!b.Spawned || b.def.building == null || b.def.building.isNaturalRock) continue;
                if (b.def != ThingDefOf.Wall && !b.def.IsDoor && (b.def.fillPercent >= 0.2f || b is Building_Turret))
                    t.wreck.Add((b, b.MarketValue * (b is Building_Turret ? 3f
                        : b is Building_ResearchBench || b is Building_CommsConsole || b.GetComp<CompPowerPlant>() != null ? 2f : 1f)));   // high-value targets (CAI idea)
                if (Flammable(b)) t.burn.Add(b);
            }
            foreach (Zone z in map.zoneManager.AllZones)
                if (z is Zone_Growing)
                    foreach (IntVec3 c in z.Cells) { Plant pl = c.GetPlant(map); if (pl != null && pl.Growth > 0.3f) t.burn.Add(pl); }
            t.wreck.Sort((x, y) => y.value.CompareTo(x.value));
            targets[map] = t;
            return t;
        }
        public static float LootValue;

        /// <summary>The pawn's first choice: gunners shoot livestock, every third raider loots, melee wrecks or burns.</summary>
        public static Task TaskOf(AssaultPlan plan, Pawn p)
        {
            if (plan.plunderTaskOf.TryGetValue(p, out Task t)) return t;
            int i = plan.plunderTaskOf.Count;
            bool gunner = p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon;
            t = i % 3 == 2 ? Task.Loot : gunner ? Task.Kill : (i % 2 == 0 ? Task.Wreck : Task.Burn);
            plan.plunderTaskOf[p] = t;
            return t;
        }

        /// <summary>Under the defenders' fire (threat map): not a place to plunder.</summary>
        public static bool Exposed(Map map, IntVec3 c)
        {
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            return tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > TacticalAIMod.Settings.safeFire;
        }

        public static Job KillJob(Pawn p, IntVec3 anchor, float radius)
        {
            Map map = p.Map;
            // nearest first, reachability only until one passes
            Pawn best = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                .Where(a => a.RaceProps.Animal && !a.Dead && !a.Downed && a.Position.DistanceTo(anchor) <= radius && !Exposed(map, a.Position))
                .OrderBy(a => a.Position.DistanceToSquared(p.Position))
                .FirstOrDefault(a => p.CanReach(a, PathEndMode.Touch, Danger.Deadly));
            if (best == null) return null;
            Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
            Job j;
            if (v != null && !v.IsMeleeAttack)
            {
                if (best.Position.DistanceTo(p.Position) <= v.verbProps.range * 0.8f && v.CanHitTarget(best))
                {
                    j = JobMaker.MakeJob(JobDefOf.AttackStatic, best);
                    j.endIfCantShootTargetFromCurPos = true;
                    j.killIncappedTarget = true;
                }
                else
                {
                    j = JobMaker.MakeJob(JobDefOf.Goto, best.Position);
                    j.locomotionUrgency = LocomotionUrgency.Jog;
                }
            }
            else
            {
                j = JobMaker.MakeJob(JobDefOf.AttackMelee, best);
                j.killIncappedTarget = true;
            }
            j.expiryInterval = 240;
            j.checkOverrideOnExpire = true;
            KillJobs++;
            return j;
        }

        public static Job LootJob(Pawn p, AssaultPlan plan, int maxLooters, IntVec3 anchor, float radius)
        {
            Map map = p.Map;
            int looting = map.mapPawns.SpawnedPawnsInFaction(p.Faction).Count(o => o.CurJobDef == JobDefOf.Steal && plan.members.Contains(o));
            if (looting >= maxLooters) return null;
            if (!RCellFinder.TryFindBestExitSpot(p, out IntVec3 exit)) return null;
            if (!StealAIUtility.TryFindBestItemToSteal(anchor, map, radius, out Thing item, p) || Exposed(map, item.Position)) return null;
            Job j = JobMaker.MakeJob(JobDefOf.Steal);
            j.targetA = item;
            j.targetB = exit;
            j.count = Mathf.Max(1, Mathf.Min(item.stackCount, (int)(p.GetStatValue(StatDefOf.CarryingCapacity) / Mathf.Max(0.001f, item.def.VolumePerUnit))));
            LootJobs++;
            LootValue += item.MarketValue * j.count;
            return j;
        }

        public static Job BurnJob(Pawn p, IntVec3 anchor, float radius)
        {
            Map map = p.Map;
            if (map.weatherManager.RainRate > 0.25f || p.natives?.IgniteVerb == null) return null;
            // nearest first, reachability only until one passes (was: a reach check on every closer candidate, in list order)
            Thing best = TargetsFor(map).burn
                .Where(t => t != null && t.Spawned && !t.IsBurning() && t.FlammableNow && t.Position.DistanceTo(anchor) <= radius && !Exposed(map, t.Position))
                .OrderBy(t => t.Position.DistanceToSquared(p.Position)).Take(12)
                .FirstOrDefault(t => p.CanReach(t, PathEndMode.Touch, Danger.Deadly));
            if (best == null) return null;
            Job j = JobMaker.MakeJob(JobDefOf.Ignite, best);
            j.expiryInterval = 300;
            j.checkOverrideOnExpire = true;
            BurnJobs++;
            return j;
        }

        /// <summary>
        /// Breach preparation (playtest: "plunder while the breach is being prepared ... regroups for the go signal"): the
        /// stackers come back when their breach is about to open - fast methods (charges, launchers, frags) at the go
        /// signal, slow ones (tools, fire, tunnels) once `plunderRecallFraction` of the wall is down.
        /// </summary>
        public static bool Recalled(AssaultPlan plan, BreachPoint bp)
        {
            if (plan.phase != PlanPhase.Approach || bp == null) return true;
            bool fast = bp.method == BreachMethod.Charge || bp.method == BreachMethod.Launcher || bp.method == BreachMethod.Grenade || bp.method == BreachMethod.None;
            if (fast) return plan.goTick >= 0;
            int total = 0, dmg = 0;
            foreach (var w in bp.walls.Concat(bp.walls2))
            {
                if (w == null) continue;
                total += w.MaxHitPoints;
                dmg += w.Destroyed ? w.MaxHitPoints : w.MaxHitPoints - w.HitPoints;
            }
            return total == 0 || dmg >= total * TacticalAIMod.Settings.plunderRecallFraction;
        }

        /// <summary>The pawn's plunder job around an anchor: its first choice, then the other tasks. Null if nothing is left.</summary>
        public static Job TryPlunder(Pawn p, AssaultPlan plan, IntVec3 anchor, float radius, int maxLooters)
        {
            var s = TacticalAIMod.Settings;
            if (plan?.intent == RaidIntent.Slaving) return null;   // after people, not goods (RaidIntel): no plunder from any caller
            Task first = TaskOf(plan, p);
            var order = new List<Task> { first };
            foreach (Task t in new[] { Task.Kill, Task.Loot, Task.Wreck, Task.Burn })
                if (t != first) order.Add(t);
            foreach (var t in order)
            {
                if (t == Task.Loot && plan.intent == RaidIntent.Punitive) continue;   // revenge takes nothing
                Job j = t == Task.Kill ? KillJob(p, anchor, radius)
                      : t == Task.Loot ? LootJob(p, plan, maxLooters, anchor, radius)
                      : t == Task.Burn ? (s.arson ? BurnJob(p, anchor, radius) : null)
                      : WreckJob(p, anchor, radius);
                if (j != null) { plan.lastPlunderJobTick = Find.TickManager.TicksGame; return j; }
            }
            return null;
        }

        public static Job WreckJob(Pawn p, IntVec3 anchor, float radius)
        {
            Map map = p.Map;
            Building best = null;
            // most valuable first (list sorted): the first one in reach that is worth smashing wins
            foreach (var (b, v) in TargetsFor(map).wreck)
            {
                if (!b.Spawned || b.Position.DistanceTo(anchor) > radius || Exposed(map, b.Position)) continue;
                if (!TrashUtility.ShouldTrashBuilding(p, b, true) || !p.CanReach(b, PathEndMode.Touch, Danger.Deadly)) continue;
                best = b;
                break;
            }
            if (best == null) return null;
            Job j = TrashUtility.TrashJob(p, best, allowPunchingInert: true);
            if (j != null) WreckJobs++;
            return j;
        }
    }

    /// <summary>
    /// Raiding party (CAI's raid task forces against valuable targets, as part of our plunder): in a raid of 10+, a sixth
    /// of it (at least 2; melee and riflemen, not the breach team, base of fire, shields, medic or arsonists) goes for
    /// the colony's valuables from the start - out of the defenders' fire - while the rest attacks. With nothing left
    /// to take, a member rejoins the attack.
    /// </summary>
    public class JobGiver_HSKTAI_RaidingParty : ThinkNode_JobGiver
    {
        public static int Members, Jobs;

        public static void Assign(AssaultPlan plan, List<Pawn> pawns)
        {
            plan.raidingParty.Clear();
            var s = TacticalAIMod.Settings;
            if (!s.raidingParty || !s.plunder || pawns.Count < 10) return;
            int n = Mathf.Max(2, pawns.Count / 6);
            foreach (var p in pawns.Where(p => !plan.breachTeam.Contains(p) && !RaidRoles.IsBaseOfFire(plan, p) && !ShieldWall.IsBearer(plan, p)
                                               && !plan.shieldOf.ContainsKey(p) && !Arson.IsActiveArsonist(plan, p)
                                               && plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Assault || r == RaidRole.Rifleman))
                                   .OrderBy(p => p.thingIDNumber).Take(n))
                plan.raidingParty.Add(p);
            Members += plan.raidingParty.Count;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || !plan.raidingParty.Contains(pawn)) return null;
            if (plan.phase == PlanPhase.Withdraw || plan.phase == PlanPhase.Converted) { plan.raidingParty.Remove(pawn); return null; }
            Job j = Plunder.TryPlunder(pawn, plan, pawn.Position, 60f, 1);
            if (j == null) { plan.raidingParty.Remove(pawn); return null; }   // nothing left: rejoin the attack
            Jobs++;
            return j;
        }
    }

    public class JobGiver_HSKTAI_Plunder : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (!s.plunder || plan == null || plan.phase != PlanPhase.Assault) return null;
            if (plan.intent == RaidIntent.Slaving) return null;   // after people, not goods: no looting, no burning
            if (ShieldWall.IsBearer(plan, pawn) || ShieldWall.BearerOf(plan, pawn) != null) return null;
            if (plan.funnelHoldOf.ContainsKey(pawn)) return null;   // watching a funnel mouth
            Pawn enemy = ShieldWall.NearestEnemy(pawn, 80f);
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (enemy != null && verb != null && !verb.IsMeleeAttack && enemy.Position.DistanceTo(pawn.Position) <= verb.verbProps.range && verb.CanHitTarget(enemy))
                return null;   // a shot: fight
            // a dull command doesn't hold back from the funnel: it chases (vanilla-like)
            bool chase = enemy != null && enemy.Position.DistanceTo(pawn.Position) <= s.plunderChaseDist
                         && pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly) && (!Competence.Knows(plan, Doctrine.FunnelCaution) || Funnels.FunnelTo(pawn, enemy) == null);
            if (chase) return null;

            int now = Find.TickManager.TicksGame;
            if (plan.plunderStartTick < 0) plan.plunderStartTick = now;
            int alive = plan.members.Count(m => m.Spawned && !m.Dead && !m.Downed);
            Job pj = Plunder.TryPlunder(pawn, plan, pawn.Position, 45f, Mathf.Max(1, Mathf.FloorToInt(alive * s.plunderLooterShare)));
            if (pj != null) return pj;
            // nothing left to take and the defenders stay holed up: leave with what we have
            // (cp3 killbox: nothing to plunder at all left the raid waiting out the whole run - idle counts from the start)
            int lastActive = Mathf.Max(plan.lastPlunderJobTick, plan.plunderStartTick);
            if (now - lastActive > s.plunderIdleWithdrawTicks && plan.intent != RaidIntent.Punitive)
            {
                Lord lord = pawn.GetLord();
                var live = lord?.ownedPawns.Where(x => x.Spawned && !x.Downed).ToList();
                if (lord != null && live.Count > 0 && !Competence.PlannedWithdrawal(plan)) { AssaultPlanner.ExitMap(lord, plan); return null; }
                if (lord != null && live.Count > 0)
                {
                    Withdrawal.Begin(pawn.Map, lord, plan, live, plan.lastPlunderJobTick >= 0 ? "plundered, defenders holed up" : "nothing to take, defenders holed up");
                    Plunder.Withdrawals++;
                    lord.CurLordToil?.UpdateAllDuties();
                    return null;
                }
            }
            // idle: take cover, out of fire, and wait for the defenders to come out
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 90, true);
        }
    }
}
