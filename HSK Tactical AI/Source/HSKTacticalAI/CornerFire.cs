using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Corner fire at an open breach (playtest, 2026-10-25: "Good breach, the attackers should move to the corners of the
    /// wall, there is a system that lets them lean over the corners to attack ... give them additional protection").
    /// RimWorld (and CE's shot lines) let a pawn standing right beside a wall end lean out past the corner (ShootLeanUtility)
    /// while the wall covers him. So when the wall drops, gunners about to go in first take the posts on the raid's side of
    /// the wall next to the edges of the hole - two per side, at most four per breach - and fire through the gap at every
    /// defender they can see inside. They go in once the entry has run cornerHoldTicks, or at once if nobody is in sight.
    /// Nobody stands in the hole itself.
    /// </summary>
    public static class CornerFire
    {
        public static int Posts, Shots;
        private static readonly Dictionary<BreachPoint, List<IntVec3>> postsOf = new Dictionary<BreachPoint, List<IntVec3>>();
        private static readonly Dictionary<Pawn, IntVec3> postOf = new Dictionary<Pawn, IntVec3>();

        private static IntVec3 Cardinal(IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x, dz = to.z - from.z;
            if (dx == 0 && dz == 0) return IntVec3.Invalid;
            return Mathf.Abs(dx) >= Mathf.Abs(dz) ? new IntVec3(System.Math.Sign(dx), 0, 0) : new IntVec3(0, 0, System.Math.Sign(dz));
        }

        /// <summary>The corner posts of an open breach: outside, behind the first standing wall cell on each side of the hole.</summary>
        public static List<IntVec3> PostsFor(Map map, BreachPoint bp, IntVec3 objective)
        {
            if (postsOf.TryGetValue(bp, out var list)) return list;
            list = new List<IntVec3>();
            postsOf[bp] = list;
            if (bp.rock || !bp.entry.IsValid) return list;
            IntVec3 inward = Cardinal(bp.plantSide.IsValid ? bp.plantSide : bp.entry - Cardinal(bp.entry, objective), bp.entry);
            if (!inward.IsValid) return list;
            IntVec3 outward = new IntVec3(-inward.x, 0, -inward.z), lateral = new IntVec3(inward.z, 0, inward.x);
            var hole = new HashSet<IntVec3>(bp.walls.Where(w => w != null).Select(w => w.Position)) { bp.entry };
            bool Standing(IntVec3 c) => c.InBounds(map) && c.GetEdifice(map) is Building b && b.def.passability == Traversability.Impassable;
            foreach (int sgn in new[] { 1, -1 })
            {
                // the hole's last cell on this side, then the first standing wall beyond it
                // (cp5: an HE blast takes more wall than the planned cells - walk the whole gap, up to 5 cells, to the wall end)
                IntVec3 edge = bp.entry;
                int steps = 0;
                while (steps++ < 5 && (edge + lateral * sgn).InBounds(map) && !Standing(edge + lateral * sgn)) edge += lateral * sgn;
                IntVec3 wallEnd = edge + lateral * sgn;
                if (!Standing(wallEnd)) continue;
                for (int k = 0; k < 2; k++)
                {
                    IntVec3 w = wallEnd + lateral * (sgn * k);
                    IntVec3 post = w + outward;
                    if (!Standing(w) || !post.InBounds(map) || !post.Standable(map)) break;
                    list.Add(post);
                }
            }
            Posts += list.Count;
            return list;
        }

        public static int GapHolds;
        private static readonly Dictionary<(int lord, IntVec3 entry), (int tick, bool hot)> hotCache = new Dictionary<(int, IntVec3), (int, bool)>();

        /// <summary>
        /// A defender still covers the gap: standing, not hunkering (CE), within 40 cells of the opening with sight of it. Only
        /// raids that know support fire hold for it, at most cornerHoldTicks after H-hour. Cached 60 ticks per opening.
        /// </summary>
        public static bool GapHot(AssaultPlan plan, IntVec3 entry)
        {
            var s = TacticalAIMod.Settings;
            int now = Find.TickManager.TicksGame;
            if (!s.cornerFire || now - plan.hHourTick > s.cornerHoldTicks || !Competence.Knows(plan, Doctrine.SupportFire) || plan.map == null) return false;
            var key = (plan.lordId, entry);
            if (hotCache.TryGetValue(key, out var c) && now - c.tick < 60) return c.hot;
            bool hot = Suppression.Defenders(plan.map, plan.objective, 50f)
                .Any(d => Suppression.State(d) < 2 && d.Position.DistanceTo(entry) <= 40f && GenSight.LineOfSight(entry, d.Position, plan.map, true));
            hotCache[key] = (now, hot);
            return hot;
        }

        /// <summary>Waiting at a hot breach: fire in if there is a shot, else step out of the line of fire through the gap, else wait.</summary>
        public static Job HoldAndFire(Pawn pawn, AssaultPlan plan, IntVec3 entry)
        {
            GapHolds++;
            Map map = pawn.Map;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v != null && !v.IsMeleeAttack && v.Available())
            {
                Pawn target = Targeting.Best(pawn, plan, v, 45f);
                if (target != null)
                {
                    Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                    shoot.maxNumStaticAttacks = 2;
                    shoot.endIfCantShootTargetFromCurPos = true;
                    shoot.expiryInterval = 120;
                    shoot.checkOverrideOnExpire = true;
                    Shots++;
                    return shoot;
                }
            }
            if (AssaultPlanner.InBreachCone(entry, plan.objective, pawn.Position))
            {
                IntVec3 aside = CellFinder.StandableCellNear(pawn.Position, map, 4f, c => !AssaultPlanner.InBreachCone(entry, plan.objective, c) && c.DistanceTo(entry) >= 2f && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly));
                if (aside.IsValid && aside != pawn.Position)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, aside);
                    go.locomotionUrgency = LocomotionUrgency.Sprint;
                    go.expiryInterval = 120;
                    return go;
                }
            }
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
        }

        /// <summary>Holding a corner post now (the planner does not count him as through the breach yet).</summary>
        public static bool Holding(Pawn p, AssaultPlan plan) =>
            postOf.TryGetValue(p, out IntVec3 c) && p.Position == c && Find.TickManager.TicksGame - plan.hHourTick <= TacticalAIMod.Settings.cornerHoldTicks;

        /// <summary>Breach assault: hold a corner post and fire through the gap; null = go in.</summary>
        public static Job TryCornerJob(Pawn pawn, AssaultPlan plan, IntVec3 entry)
        {
            var s = TacticalAIMod.Settings;
            if (!s.cornerFire || plan == null || plan.mode != PlanMode.Breach || plan.phase != PlanPhase.Assault) return null;
            if (Find.TickManager.TicksGame - plan.hHourTick > s.cornerHoldTicks) { postOf.Remove(pawn); return null; }
            if (!RaidRoles.IsRangedFighter(plan, pawn) || ShieldWall.IsBearer(plan, pawn) || !Competence.Knows(plan, Doctrine.Overwatch)) return null;
            BreachPoint bp = AssaultPlanner.EntryFor(plan, pawn, pawn.Map);
            if (bp == null) return null;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null || v.IsMeleeAttack || !v.Available()) return null;
            Map map = pawn.Map;
            var posts = PostsFor(map, bp, plan.objective);
            if (posts.Count == 0) return null;
            if (!postOf.TryGetValue(pawn, out IntVec3 post) || !posts.Contains(post))
            {
                post = posts.Where(c => !postOf.Any(kv => kv.Key != pawn && kv.Key.Spawned && !kv.Key.Downed && kv.Value == c))
                            .OrderBy(c => c.DistanceToSquared(pawn.Position)).FirstOrDefault();
                if (!post.IsValid || post == default(IntVec3)) return null;   // all taken: go in
                postOf[pawn] = post;
            }
            if (pawn.Position != post)
            {
                if (!pawn.CanReach(post, PathEndMode.OnCell, Danger.Deadly)) { postOf.Remove(pawn); return null; }
                Job go = JobMaker.MakeJob(JobDefOf.Goto, post);
                go.locomotionUrgency = LocomotionUrgency.Sprint;
                go.expiryInterval = 180;
                return go;
            }
            // at the corner: lean out and fire through the gap; nobody in sight - in we go
            Pawn target = Targeting.Best(pawn, plan, v, 45f);
            if (target == null) { postOf.Remove(pawn); return null; }
            Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            shoot.maxNumStaticAttacks = 2;
            shoot.endIfCantShootTargetFromCurPos = true;
            shoot.expiryInterval = 120;
            shoot.checkOverrideOnExpire = true;
            Shots++;
            return shoot;
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>Breach entry in waves: at most WaveSize raiders through one opening per WaveTicks (all competence levels).</summary>
    public static class BreachWaves
    {
        public const int WaveSize = 3, WaveTicks = 240;
        public static int Holds;
        private static readonly Dictionary<(int lord, IntVec3 entry), List<(int tick, Pawn p)>> went = new Dictionary<(int, IntVec3), List<(int, Pawn)>>();

        public static bool MayGo(AssaultPlan plan, IntVec3 entry, Pawn p)
        {
            if (!went.TryGetValue((plan.lordId, entry), out var list)) return true;
            int now = Find.TickManager.TicksGame;
            list.RemoveAll(e => now - e.tick > WaveTicks);
            if (list.Any(e => e.p == p) || list.Count < WaveSize) return true;
            Holds++;
            return false;
        }

        public static void Went(AssaultPlan plan, IntVec3 entry, Pawn p)
        {
            var key = (plan.lordId, entry);
            if (!went.TryGetValue(key, out var list)) went[key] = list = new List<(int, Pawn)>();
            list.Add((Find.TickManager.TicksGame, p));
        }
    }
}
