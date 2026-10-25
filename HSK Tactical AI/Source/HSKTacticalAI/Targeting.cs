using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Raider focus fire (CAI has focus fire only for the player's auto-controlled pawns; this is for the raid): which
    /// defender to shoot. Lower score = better: nearest, then
    /// - exposed (not covered from the shooter) -40;
    /// - wounded: up to -30 (finish him);
    /// - dangerous weapon (machine gun, launcher, sniper rifle): -25;
    /// - comrades already shooting him: -8 each, up to 3 (concentrate fire instead of spreading it);
    /// - already pinned: +15 per suppression level (shoot the ones still shooting back).
    /// </summary>
    public static class Targeting
    {
        public static float Score(Pawn shooter, Pawn d, AssaultPlan plan)
        {
            var s = TacticalAIMod.Settings;
            float score = d.Position.DistanceTo(shooter.Position);
            if (s.flanking && Competence.Knows(plan, Doctrine.Flanking) && !Flanking.Covered(d, shooter.Position)) score -= 40f;
            if (!s.focusFire || !Competence.Knows(plan, Doctrine.FocusFire)) return score + Suppression.State(d) * 15f;   // concentrated fire takes a command that directs it
            score -= (1f - (d.health?.summaryHealth?.SummaryHealthPercent ?? 1f)) * 30f;
            if (Dangerous(d)) score -= 25f;
            int focus = 0;
            foreach (var m in plan.members)
            {
                if (m == shooter || !m.Spawned || m.Downed) continue;
                if (m.CurJob != null && m.CurJob.targetA.Thing == d && (m.CurJobDef == JobDefOf.AttackStatic || m.CurJobDef == JobDefOf.AttackMelee)) focus++;
                if (focus >= 3) break;
            }
            score -= focus * 8f;
            score += Suppression.State(d) * 15f;
            return score;
        }

        public static bool Dangerous(Pawn d)
        {
            ThingDef w = d.equipment?.Primary?.def;
            if (w == null || !w.IsRangedWeapon) return false;
            VerbProperties v = w.Verbs.FirstOrDefault(x => !x.IsMeleeAttack);
            if (v == null) return false;
            return v.burstShotCount >= 8 || (v.defaultProjectile?.projectile?.explosionRadius ?? 0f) > 0f || (v.range >= 55f && v.burstShotCount <= 2);
        }

        /// <summary>The best defender this pawn can hit from where it stands (null if none).</summary>
        public static Pawn Best(Pawn shooter, AssaultPlan plan, Verb v, float radius = 60f)
        {
            Pawn best = null;
            float bs = float.MaxValue;
            foreach (var d in Suppression.Defenders(shooter.Map, plan.objective, radius))
            {
                float dist = d.Position.DistanceTo(shooter.Position);
                if (dist > v.verbProps.range || !v.CanHitTarget(d)) continue;
                float sc = Score(shooter, d, plan);
                if (sc < bs) { bs = sc; best = d; }
            }
            return best;
        }
    }

    /// <summary>
    /// Threat-priced advance (better than CAI's visibility-weighted region search for the nearest hostile, and than vanilla's
    /// straight chase): in the assault, a raider with no shot moves on the nearest defender along a route priced by the
    /// defenders' fire (ThreatPathing), never straight down a firing lane. Routes are shared by raiders in the same 4x4
    /// block heading for the same defender (300 ticks). Falls back to the vanilla chase if there is no route.
    /// </summary>
    public class JobGiver_HSKTAI_Advance : ThinkNode_JobGiver
    {
        public static int Advances;
        private static readonly Dictionary<(int enemy, int gx, int gz), (List<IntVec3> wps, int tick)> routes = new Dictionary<(int, int, int), (List<IntVec3>, int)>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (!s.threatAdvance || plan == null || plan.phase != PlanPhase.Assault || !Competence.Knows(plan, Doctrine.ThreatAdvance)) return null;
            Map map = pawn.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm == null || tm.CompletedCycles == 0) return null;
            Pawn enemy = ShieldWall.NearestEnemy(pawn, 60f);
            if (enemy == null) return null;
            if (enemy.Position.DistanceTo(pawn.Position) <= 6f) return null;   // close: the fight nodes handle it
            int now = Find.TickManager.TicksGame;
            var key = (enemy.thingIDNumber, pawn.Position.x >> 2, pawn.Position.z >> 2);
            if (!routes.TryGetValue(key, out var r) || now - r.tick > 300 * Adaptive.Load)
            {
                if (routes.Count > 400) routes.Clear();
                IntVec3 goal = CellFinder.StandableCellNear(enemy.Position, map, 3f, c => c != enemy.Position && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly));
                List<IntVec3> wps = null;
                if (goal.IsValid)
                    using (Competence.Perceive(plan, pawn.Position))
                    using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire * 0.5f, s.routeBaseCostInFire, s.funnelCellPenalty, s.safeFire))
                        if (ThreatPathing.TryFindRoute(map, pawn.Position, goal, customizer, out var nodes, out _)) wps = ThreatPathing.Waypoints(nodes, 4);
                r = (wps, now);
                routes[key] = r;
            }
            if (r.wps == null || r.wps.Count == 0) return null;
            // the next waypoint not yet reached
            IntVec3 next = r.wps.FirstOrDefault(w => w.DistanceTo(pawn.Position) > 3f && w.DistanceTo(enemy.Position) < pawn.Position.DistanceTo(enemy.Position));
            if (!next.IsValid || !pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly)) return null;
            if (Grenades.InDanger(plan, next) || Evasion.InIncoming(map, next)) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            Job go = JobMaker.MakeJob(JobDefOf.Goto, next);
            go.locomotionUrgency = tm.FireAt(pawn.Position) > s.safeFire ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            go.expiryInterval = 120;
            go.checkOverrideOnExpire = true;
            Advances++;
            return go;
        }
    }
}
