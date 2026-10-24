using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Shield wall (playtest, 2026-10-23): raiders with a body shield (CE ballistic / bone shield: covers the torso)
    /// lead the assault straight at the enemy and draw the fire; 2 gunners per shield follow one cell behind and to
    /// the side (CE bullets hit the first body in their path, so the shield covers them while they keep a clear line
    /// of fire) and shoot past it. Through a breach the shield goes first; the rest of that group waits for it.
    /// </summary>
    public static class ShieldWall
    {
        private static readonly Dictionary<ThingDef, bool> bodyShield = new Dictionary<ThingDef, bool>();
        public static int Bearers, Followers, LeadJobs, FollowJobs, FollowShots;

        /// <summary>A shield that covers the torso against bullets (CE ShieldDefExtension.shieldCoverage, read by reflection).</summary>
        public static bool IsBodyShield(ThingDef def)
        {
            if (def == null) return false;
            if (bodyShield.TryGetValue(def, out bool b)) return b;
            b = false;
            foreach (var ext in def.modExtensions ?? new List<DefModExtension>())
            {
                if (ext.GetType().Name != "ShieldDefExtension") continue;
                var cov = ext.GetType().GetField("shieldCoverage")?.GetValue(ext) as List<BodyPartGroupDef>;
                b = cov != null && cov.Any(g => g == BodyPartGroupDefOf.Torso);
            }
            bodyShield[def] = b;
            return b;
        }

        public static bool HasBodyShield(Pawn p) =>
            p?.apparel != null && p.apparel.WornApparel.Any(a => IsBodyShield(a.def));

        public static bool Active(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && !p.InMentalState;

        /// <summary>Shield bearers and their followers (nearest gunners of the same breach group / axis, not base of fire).</summary>
        public static void Assign(AssaultPlan plan, List<Pawn> pawns)
        {
            plan.followersOf.Clear();
            plan.shieldOf.Clear();
            var s = TacticalAIMod.Settings;
            if (!s.shieldTactics) return;
            // breach-team bearers keep their breach job (shield-covered planter) and lead once the breach is open
            var bearers = pawns.Where(p => HasBodyShield(p)
                                           && (!plan.roleOf.TryGetValue(p, out var r) || r != RaidRole.Medic)).ToList();
            foreach (var b in bearers)
            {
                if (!plan.breachTeam.Contains(b)) plan.roleOf[b] = RaidRole.Shield;
                plan.followersOf[b] = new List<Pawn>();
            }
            Bearers = bearers.Count;
            foreach (var b in bearers)
            {
                int group = plan.breachOf.TryGetValue(b, out int gi) ? gi : plan.axisOf.TryGetValue(b, out int ai) ? ai : -1;
                // gunners first (they shoot past the shield), then melee (carried to close range behind it)
                var cands = pawns.Where(p => !plan.followersOf.ContainsKey(p) && !plan.shieldOf.ContainsKey(p) && !plan.breachTeam.Contains(p)
                                             && plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Rifleman || r == RaidRole.Assault))
                                 .OrderBy(p => SameGroup(plan, p, group) ? 0 : 1).ThenBy(p => IsGunner(p) ? 0 : 1).ThenBy(p => p.Position.DistanceToSquared(b.Position))
                                 .Take(s.shieldFollowers);
                foreach (var f in cands) { plan.followersOf[b].Add(f); plan.shieldOf[f] = b; }
            }
            Followers = plan.shieldOf.Count;
            Grenades.EquipShieldBearers(plan);
        }

        public static bool IsGunner(Pawn p) => p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon;

        private static bool SameGroup(AssaultPlan plan, Pawn p, int group) =>
            group < 0 || (plan.breachOf.TryGetValue(p, out int g) ? g : plan.axisOf.TryGetValue(p, out int a) ? a : -1) == group;

        public static bool IsBearer(AssaultPlan plan, Pawn p) => plan != null && plan.followersOf.ContainsKey(p);

        /// <summary>The live bearer this pawn follows, if any.</summary>
        public static Pawn BearerOf(AssaultPlan plan, Pawn p) =>
            plan != null && plan.shieldOf.TryGetValue(p, out Pawn b) && Active(b) && b.Map == p.Map ? b : null;

        /// <summary>Breach groups whose shield has not gone through yet: the rest of the group waits for it.</summary>
        public static bool GroupWaitsForShield(AssaultPlan plan, Pawn p)
        {
            var s = TacticalAIMod.Settings;
            if (!s.shieldTactics || plan == null || plan.mode != PlanMode.Breach || plan.phase != PlanPhase.Assault) return false;
            if (Find.TickManager.TicksGame - plan.hHourTick > s.shieldLeadTicks) return false;
            if (IsBearer(plan, p) || plan.entered.Contains(p)) return false;
            if (!plan.breachOf.TryGetValue(p, out int gi)) return false;
            return plan.followersOf.Keys.Any(b => Active(b) && !plan.entered.Contains(b) && plan.breachOf.TryGetValue(b, out int bi) && bi == gi);
        }

        public static Pawn NearestEnemy(Pawn p, float maxDist = 70f)
        {
            Pawn best = null;
            float bd = maxDist * maxDist;
            foreach (IAttackTarget t in p.Map.attackTargetsCache.GetPotentialTargetsFor(p))
            {
                if (t.ThreatDisabled(p) || !(t.Thing is Pawn e) || e.Downed) continue;
                float d = e.Position.DistanceToSquared(p.Position);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        /// <summary>
        /// Follower position: 1-2 cells behind the bearer (away from the enemy) and one to the side - covered by the shield,
        /// but with its own line of fire past it. Followers alternate left / right, a third goes straight behind.
        /// </summary>
        public static IntVec3 ShadowCell(AssaultPlan plan, Pawn follower, Pawn bearer, IntVec3 threat)
        {
            Map map = bearer.Map;
            float dx = bearer.Position.x - threat.x, dz = bearer.Position.z - threat.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 0.5f) return bearer.Position;
            dx /= len; dz /= len;
            // gunners: 1.5 behind, alternating right / left (own line of fire past the shield);
            // melee: 2.5 behind, the first straight in line, the others half a step to either side
            float back, side;
            var fl = plan.followersOf.TryGetValue(bearer, out var l) ? l : new List<Pawn>();
            if (IsGunner(follower))
            {
                int g = fl.Where(IsGunner).ToList().IndexOf(follower);
                back = g < 2 ? 1.5f : 2.5f;
                side = g == 0 ? 1f : g == 1 ? -1f : 0f;
            }
            else
            {
                int m = fl.Where(f => !IsGunner(f)).ToList().IndexOf(follower);
                back = 2.5f;
                side = m <= 0 ? 0f : m % 2 == 1 ? 1f : -1f;
            }
            var want = new IntVec3(bearer.Position.x + Mathf.RoundToInt(dx * back - dz * side), 0, bearer.Position.z + Mathf.RoundToInt(dz * back + dx * side));
            IntVec3 best = IntVec3.Invalid;
            float bd = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(want, 1.5f, true))
            {
                if (!c.InBounds(map) || c == bearer.Position || !c.Standable(map)) continue;
                if (c.DistanceToSquared(threat) < bearer.Position.DistanceToSquared(threat)) continue;   // never in front of the shield
                float d = c.DistanceToSquared(want);
                if (d < bd && follower.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) { bd = d; best = c; }
            }
            return best;
        }
    }

    /// <summary>
    /// Assault phase: shield bearers walk straight at the nearest defender (no cover-seeking; the shield is the cover),
    /// waiting briefly for their followers, and fight at close range; followers hold their shadow cell and shoot past
    /// the shield. In the breach approach the rest of a group waits until its shield has gone through.
    /// </summary>
    public class JobGiver_HSKTAI_Shield : ThinkNode_JobGiver
    {
        private static readonly JobGiver_AIFightEnemies fight = new JobGiver_AIFightEnemies();

        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            // breach work: the shield covers the breacher on its way to and at the wall
            if (s.shieldTactics && s.shieldEscort && plan != null && plan.mode == PlanMode.Breach && plan.phase == PlanPhase.Approach
                && plan.goTick >= 0 && ShieldWall.IsBearer(plan, pawn) && !plan.breachTeam.Contains(pawn))
            {
                Job e = Escort(pawn, plan);
                if (e != null) return e;
            }
            if (!s.shieldTactics || plan == null || plan.phase != PlanPhase.Assault) return null;
            if (ShieldWall.IsBearer(plan, pawn)) return Lead(pawn, plan);
            Pawn bearer = ShieldWall.BearerOf(plan, pawn);
            if (bearer != null && bearer.Position.DistanceTo(pawn.Position) < 30f) return Follow(pawn, plan, bearer);
            if (ShieldWall.GroupWaitsForShield(plan, pawn)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            return null;
        }

        public static int EscortJobs;

        /// <summary>
        /// Escort a breacher: 1.5 cells from it towards the defender that can see it (else the nearest), crouched, shooting
        /// if it can. Clear of an armed charge (back to the stack). Null when there is no breacher at work.
        /// </summary>
        private static Job Escort(Pawn pawn, AssaultPlan plan)
        {
            var bp = plan.BreachFor(pawn);
            if (bp == null || bp.abandoned || bp.rock || bp.WallsGone) return null;
            Map map = pawn.Map;
            foreach (Thing t in map.listerThings.ThingsOfDef(TAI_DefOf.HSKTAI_PlantedBreachCharge).Concat(map.listerThings.ThingsOfDef(TAI_DefOf.HSKTAI_PlantedMiningCharge)))
                if (t.Position.DistanceTo(bp.entry) <= 6f) return null;
            Pawn worker = bp.team.Where(ShieldWall.Active).OrderBy(w => w.Position.DistanceToSquared(bp.entry)).FirstOrDefault();
            if (worker == null || worker.Position.DistanceTo(pawn.Position) > 30f) return null;
            Pawn enemy = null;
            float bd = float.MaxValue;
            foreach (var d in Suppression.Defenders(map, plan.objective, 50f))
            {
                float dist = d.Position.DistanceToSquared(worker.Position) * (GenSight.LineOfSight(d.Position, worker.Position, map, true) ? 1f : 4f);
                if (dist < bd) { bd = dist; enemy = d; }
            }
            IntVec3 threat = enemy?.Position ?? plan.objective;
            Vector3 dir = (threat - worker.Position).ToVector3();
            IntVec3 want = dir.sqrMagnitude < 1f ? worker.Position : worker.Position + (dir.normalized * 1.5f).ToIntVec3();
            IntVec3 cell = IntVec3.Invalid;
            float cd = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(want, 1.5f, true))
            {
                if (!c.InBounds(map) || c == worker.Position || !c.Standable(map) || Grenades.InDanger(plan, c)) continue;
                float d2 = c.DistanceToSquared(want);
                if (d2 < cd && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) { cd = d2; cell = c; }
            }
            if (!cell.IsValid) return null;
            EscortJobs++;
            if (pawn.Position.DistanceTo(cell) > 1.2f)
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, cell);
                go.locomotionUrgency = LocomotionUrgency.Sprint;
                go.expiryInterval = 60;
                go.checkOverrideOnExpire = true;
                return go;
            }
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (enemy != null && verb != null && !verb.IsMeleeAttack && enemy.Position.DistanceTo(pawn.Position) <= verb.verbProps.range && verb.CanHitTarget(enemy))
                return Shoot(enemy, 45);
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
        }

        private static Job Lead(Pawn pawn, AssaultPlan plan)
        {
            // in throwing range: frag from behind the shield (also at the breach, before going through)
            Job frag = Grenades.TryShieldFrag(pawn, plan);
            if (frag != null) return frag;
            // breach: the normal approach takes the shield to and through the hole (it is first, the group waits)
            if (plan.mode == PlanMode.Breach && !plan.entered.Contains(pawn)) return null;
            Pawn enemy = ShieldWall.NearestEnemy(pawn);
            if (enemy == null) return null;
            float d = enemy.Position.DistanceTo(pawn.Position);
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            float closeIn = verb == null || verb.IsMeleeAttack ? 1.5f : Mathf.Clamp(verb.verbProps.range * 0.4f, 3f, 10f);
            if (d <= closeIn)
            {
                Job f = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                if (f != null) return f;
            }
            if (Grenades.InDanger(plan, pawn.Position + (enemy.Position - pawn.Position).ClampMagnitude(3)))
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            // keep the followers in the shadow: wait up to ~5 s per lead if they fall more than 4 cells behind
            if (plan.followersOf.TryGetValue(pawn, out var fl))
            {
                var live = fl.Where(ShieldWall.Active).ToList();
                int now = Find.TickManager.TicksGame;
                plan.shieldWaitTicks.TryGetValue(pawn, out int waited);
                if (live.Count > 0 && live.Average(f => f.Position.DistanceTo(pawn.Position)) > 4f && waited < 300)
                {
                    plan.shieldWaitTicks[pawn] = waited + 30;
                    // facing the enemy while waiting: shoot if it can, else stand
                    if (verb != null && !verb.IsMeleeAttack && d <= verb.verbProps.range && verb.CanHitTarget(enemy))
                        return Shoot(enemy, 30);
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
                }
                if (live.Count == 0 || live.Average(f => f.Position.DistanceTo(pawn.Position)) <= 3f) plan.shieldWaitTicks[pawn] = 0;
            }
            // bound and crouch: ~4 cells, then a second down behind the shield (CE: a crouching bearer's legs are covered
            // too; AttackStatic and Wait_Combat are crouch jobs) - shooting if it can - while the followers catch up
            int tick = Find.TickManager.TicksGame;
            bool canShoot = verb != null && !verb.IsMeleeAttack && d <= verb.verbProps.range && verb.CanHitTarget(enemy);
            if (plan.shieldCrouchUntil.TryGetValue(pawn, out int until) && tick < until)
                return canShoot ? Shoot(enemy, until - tick) : JobMaker.MakeJob(JobDefOf.Wait_Combat, until - tick, true);
            if (!plan.shieldBoundFrom.TryGetValue(pawn, out IntVec3 from)) plan.shieldBoundFrom[pawn] = from = pawn.Position;
            if (pawn.Position.DistanceTo(from) >= 4f)
            {
                plan.shieldBoundFrom[pawn] = pawn.Position;
                plan.shieldCrouchUntil[pawn] = tick + TacticalAIMod.Settings.shieldCrouchTicks;
                return canShoot ? Shoot(enemy, TacticalAIMod.Settings.shieldCrouchTicks) : JobMaker.MakeJob(JobDefOf.Wait_Combat, TacticalAIMod.Settings.shieldCrouchTicks, true);
            }
            // straight at it: a cell ~5 ahead on the line to the enemy, else the enemy's cell
            IntVec3 step = pawn.Position + (enemy.Position - pawn.Position).ClampMagnitude(5);
            if (!step.InBounds(pawn.Map) || !step.Standable(pawn.Map) || !pawn.CanReach(step, PathEndMode.OnCell, Danger.Deadly)) step = enemy.Position;
            Job go = JobMaker.MakeJob(JobDefOf.Goto, step);
            go.locomotionUrgency = LocomotionUrgency.Jog;
            go.expiryInterval = 90;
            go.checkOverrideOnExpire = true;
            ShieldWall.LeadJobs++;
            return go;
        }

        private static Job Follow(Pawn pawn, AssaultPlan plan, Pawn bearer)
        {
            Pawn enemy = ShieldWall.NearestEnemy(bearer);
            IntVec3 threat = enemy?.Position ?? plan.objective;
            if (!ShieldWall.IsGunner(pawn) && enemy != null
                && (enemy.Position.DistanceTo(bearer.Position) <= 5f || enemy.Position.DistanceTo(pawn.Position) <= 4f))
            {
                Job charge = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;   // out of the shadow and in
                if (charge != null) return charge;
            }
            IntVec3 cell = ShieldWall.ShadowCell(plan, pawn, bearer, threat);
            if (!cell.IsValid) return null;
            if (Grenades.InDanger(plan, cell)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            if (pawn.Position.DistanceTo(cell) > 1.5f)
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, cell);
                go.locomotionUrgency = LocomotionUrgency.Jog;
                go.expiryInterval = 60;
                go.checkOverrideOnExpire = true;
                ShieldWall.FollowJobs++;
                return go;
            }
            // in the shadow: shoot past the shield (nearest defender we can hit from here)
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (verb == null || verb.IsMeleeAttack) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            Pawn target = null;
            float bd = float.MaxValue;
            foreach (var e in Suppression.Defenders(pawn.Map, threat, 30f))
            {
                float d = e.Position.DistanceTo(pawn.Position);
                if (d > verb.verbProps.range || d >= bd || !verb.CanHitTarget(e)) continue;
                bd = d; target = e;
            }
            if (target == null) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            ShieldWall.FollowShots++;
            return Shoot(target, 90);
        }

        private static Job Shoot(Thing target, int expiry)
        {
            Job j = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            j.maxNumStaticAttacks = 2;
            j.endIfCantShootTargetFromCurPos = true;
            j.expiryInterval = expiry;
            j.checkOverrideOnExpire = true;
            return j;
        }
    }
}
