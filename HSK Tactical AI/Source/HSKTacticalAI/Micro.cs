using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// The rest of CAI 5000's combat micro (research/ai/CAI5000.md section 1, items 5, 7, 10, 18), rebuilt on our plan,
    /// threat map and position finder:
    /// - retreat when outgunned: a raider that 2+ defenders aim at (or 1 while he is badly hurt), with no cover from them,
    ///   falls back to the nearest cell covered from them (position finder: reachable, least fire on the way);
    /// - melee: intercept a defender who is hitting one of our gunners; with the target backed up, come in from the side;
    /// - gunners kite: a melee defender closing in is not met with fists - step back and shoot.
    /// </summary>
    public static class Micro
    {
        public static int Retreats, Intercepts, SideApproaches, Kites;

        public static List<Pawn> Aimers(Pawn p, AssaultPlan plan) =>
            Suppression.Defenders(p.Map, plan.objective, 60f).Where(d => d.stances?.curStance is Stance_Warmup w && w.focusTarg.Thing == p).ToList();

        public static bool CoveredFromAll(IntVec3 c, List<Pawn> from, Map map) =>
            from.All(a => JobGiver_HSKTAI_Support.HasCover(c, a.Position, map));

        public static bool IsMelee(Pawn d) => d.equipment?.Primary == null || !d.equipment.Primary.def.IsRangedWeapon;
    }

    /// <summary>Retreat when outgunned (part of the evade node's family; runs after it in the same duties).</summary>
    public class JobGiver_HSKTAI_Retreat : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            if (!s.combatMicro) return null;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || plan.phase == PlanPhase.Withdraw || ShieldWall.IsBearer(plan, pawn)) return null;
            if (pawn.CurJobDef == TAI_DefOf.HSKTAI_PlantBreachCharge || pawn.carryTracker?.CarriedThing != null) return null;
            var aimers = Micro.Aimers(pawn, plan);
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (aimers.Count < 2 && !(aimers.Count == 1 && health < 0.5f)) return null;
            if (aimers.Any(a => a.Position.DistanceTo(pawn.Position) <= 6f)) return null;   // too close to run: fight
            // melee with an enemy close: closing in beats turning his back (playtest: "melee units should close in on targets")
            if (Micro.IsMelee(pawn) && pawn.equipment?.Primary != null && RangeBand.Knows(plan) && ShieldWall.NearestEnemy(pawn, 10f) != null) return null;
            Map map = pawn.Map;
            if (Micro.CoveredFromAll(pawn.Position, aimers, map)) return null;
            IntVec3 cell = PositionFinder.Find(pawn, 12f,
                c => c.c == pawn.Position || !Micro.CoveredFromAll(c.c, aimers, map) ? float.NaN : c.path + c.exposure * 4f, null, 1);
            if (!cell.IsValid) return null;
            Micro.Retreats++;
            MoteMaker.ThrowText(pawn.DrawPos, map, "cover me!", new Color(0.9f, 0.7f, 0.5f), 2f);
            Job go = JobMaker.MakeJob(JobDefOf.Goto, cell);
            go.locomotionUrgency = LocomotionUrgency.Sprint;
            go.expiryInterval = 180;
            return go;
        }
    }

    /// <summary>
    /// A raider without a weapon (a one-shot launcher spent on the wall, a gun lost) does not join the assault with his
    /// fists (ff2, full modlist: unarmed breachers walked into the breach and went down). He picks up a weapon lying
    /// nearby out of the defenders' fire; else he plunders out of fire; else he stays out of their fire.
    /// </summary>
    public class JobGiver_HSKTAI_Unarmed : ThinkNode_JobGiver
    {
        public static int Rearmed, Kept, ReserveMoves;

        /// <summary>8 cells behind the raid's armed fighters (towards where it came from), out of the defenders' fire.</summary>
        private static IntVec3 Reserve(Pawn pawn, AssaultPlan plan)
        {
            Map map = pawn.Map;
            int x = 0, z = 0, n = 0;
            foreach (var m in plan.members)
            {
                if (m == pawn || !m.Spawned || m.Downed || m.Dead || m.Map != map || m.equipment?.Primary == null || Launchers.GasOnlyArmed(m)) continue;
                x += m.Position.x; z += m.Position.z; n++;
            }
            if (n == 0) return IntVec3.Invalid;
            var c = new IntVec3(x / n, 0, z / n);
            float d = c.DistanceTo(plan.gather);
            if (d > 1f) c = new IntVec3(c.x + Mathf.RoundToInt((plan.gather.x - c.x) * Mathf.Min(1f, 8f / d)), 0, c.z + Mathf.RoundToInt((plan.gather.z - c.z) * Mathf.Min(1f, 8f / d)));
            return CellFinder.StandableCellNear(c, map, 6f, cell => !Withdrawal.InOpenFire(map, cell) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly));
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || (plan.phase != PlanPhase.Assault && plan.phase != PlanPhase.Approach)) return null;
            if (!pawn.RaceProps.Humanlike || pawn.equipment == null || (pawn.equipment.Primary != null && !Launchers.GasOnlyArmed(pawn)) || pawn.WorkTagIsDisabled(WorkTags.Violent)) return null;   // a gas-only gun with nothing to gas counts as no gun
            if (pawn.equipment.Primary != null)
            {
                Job reload = Launchers.ReloadFromGas(pawn, pawn.equipment.PrimaryEq.PrimaryVerb);   // gas loaded, another round carried
                if (reload != null) return reload;
            }
            if (plan.breachTeam.Contains(pawn) && plan.BreachFor(pawn) is BreachPoint bp && !bp.abandoned && !bp.WallsGone
                && (bp.method == BreachMethod.Tunnel || bp.method == BreachMethod.Fire)) return null;   // digging / lighting needs no weapon
            Map map = pawn.Map;
            Thing gun = GenClosest.ClosestThingReachable(pawn.Position, map, ThingRequest.ForGroup(ThingRequestGroup.Weapon), PathEndMode.OnCell,
                TraverseParms.For(pawn, Danger.Deadly), 30f,
                t => t.def.IsRangedWeapon && t.def.equipmentType == EquipmentType.Primary && !t.def.destroyOnDrop && t.Spawned
                     && !Withdrawal.InOpenFire(map, t.Position) && pawn.CanReserve(t) && EquipmentUtility.CanEquip(t, pawn));
            if (gun != null)
            {
                Rearmed++;
                return JobMaker.MakeJob(JobDefOf.Equip, gun);
            }
            Kept++;
            Job plunder = TacticalAIMod.Settings.plunder ? Plunder.TryPlunder(pawn, plan, plan.objective, 45f, 2) : null;
            if (plunder != null) return plunder;
            // the reserve (playtest: "the grenadelaunchers stay at the edge of the map"): keep up with the raid, out of fire,
            // 8 cells behind its armed fighters - close enough to take up a fallen comrade's gun
            IntVec3 reserve = Reserve(pawn, plan);
            if (reserve.IsValid && pawn.Position.DistanceTo(reserve) > 6f)
            {
                ReserveMoves++;
                Job toReserve = JobMaker.MakeJob(JobDefOf.Goto, reserve);
                toReserve.locomotionUrgency = LocomotionUrgency.Jog;
                toReserve.expiryInterval = 300;
                toReserve.checkOverrideOnExpire = true;
                return toReserve;
            }
            if (!Withdrawal.InOpenFire(map, pawn.Position)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
            IntVec3 safe = PositionFinder.Find(pawn, 15f, c => Withdrawal.InOpenFire(map, c.c) ? float.NaN : c.path + c.exposure * 3f, null, 1);
            if (!safe.IsValid) return null;
            Job go = JobMaker.MakeJob(JobDefOf.Goto, safe);
            go.locomotionUrgency = LocomotionUrgency.Sprint;
            return go;
        }
    }

    /// <summary>Melee micro (Assault duty, before the vanilla fight node) and kiting for gunners.</summary>
    public class JobGiver_HSKTAI_Melee : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            if (!s.combatMicro) return null;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || plan.phase != PlanPhase.Assault || ShieldWall.IsBearer(plan, pawn)) return null;
            Map map = pawn.Map;
            var defs = Suppression.Defenders(map, plan.objective, 60f);
            if (defs.Count == 0) return null;
            if (Micro.IsMelee(pawn))
            {
                // 1) intercept a defender who is hitting one of our gunners
                Pawn brawler = defs.FirstOrDefault(d => d.CurJobDef == JobDefOf.AttackMelee && d.CurJob.targetA.Thing is Pawn v
                                                        && plan.members.Contains(v) && !Micro.IsMelee(v) && d.Position.DistanceTo(pawn.Position) <= 15f
                                                        && pawn.CanReach(d, PathEndMode.Touch, Danger.Deadly));
                if (brawler != null)
                {
                    Micro.Intercepts++;
                    Job hit = JobMaker.MakeJob(JobDefOf.AttackMelee, brawler);
                    hit.expiryInterval = 240;
                    hit.checkOverrideOnExpire = true;
                    return hit;
                }
                // 2) the target is backed up (2+ other defenders within 6): come in from the side, not head-on
                Pawn target = ShieldWall.NearestEnemy(pawn, 30f);
                if (target == null || target.Position.DistanceTo(pawn.Position) <= 4f) return null;
                if (defs.Count(d => d != target && d.Position.DistanceTo(target.Position) <= 6f) < 2) return null;
                Vector3 to = (target.Position - pawn.Position).ToVector3();
                float dist = to.magnitude;
                if (dist < 1f) return null;
                Vector3 side = new Vector3(-to.z, 0f, to.x).normalized * Mathf.Clamp(dist * 0.4f, 3f, 6f) * ((pawn.thingIDNumber & 1) == 0 ? 1f : -1f);
                IntVec3 flank = (target.Position.ToVector3() + side).ToIntVec3();
                if (!flank.InBounds(map) || !flank.Standable(map) || !pawn.CanReach(flank, PathEndMode.OnCell, Danger.Deadly) || pawn.Position.DistanceTo(flank) <= 2f) return null;
                Micro.SideApproaches++;
                Job go = JobMaker.MakeJob(JobDefOf.Goto, flank);
                go.locomotionUrgency = LocomotionUrgency.Sprint;
                go.expiryInterval = 150;
                go.checkOverrideOnExpire = true;
                return go;
            }
            // gunner: a melee defender closing in (within 4) - step back and keep shooting
            Pawn closing = defs.Where(d => Micro.IsMelee(d) && d.Position.DistanceTo(pawn.Position) <= 4f).OrderBy(d => d.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            if (closing == null) return null;
            IntVec3 back = PositionFinder.Find(pawn, 7f,
                c => c.path < 3f ? float.NaN : -c.c.DistanceTo(closing.Position) + c.exposure * 3f, null, 1);
            if (!back.IsValid) return null;
            Micro.Kites++;
            Job kite = JobMaker.MakeJob(JobDefOf.Goto, back);
            kite.locomotionUrgency = LocomotionUrgency.Sprint;
            kite.expiryInterval = 90;
            return kite;
        }
    }
}
