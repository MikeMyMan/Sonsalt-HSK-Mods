using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>What a raider does in the combined-arms plan. Inferred from its actual equipment (HSK has no role data).</summary>
    public enum RaidRole { Rifleman, Assault, Support, Marksman, Grenadier, Breacher, Medic, Shield }

    public static class RaidRoles
    {
        private static readonly Regex MgTag = new Regex(@"^(CE_MachineGun|MachineGun|CE_AI_Suppressive|Minigun|MG\d.*)$");
        private static readonly Regex SniperTag = new Regex(@"^(SniperRifle|SNIP\d.*|ASN\d.*|CE_AI_SR)$");
        private static readonly Regex AoeTag = new Regex(@"^(CE_AI_AOE|CE_AI_Launcher|GLT\d.*|RKT\d.*)$");

        public static RaidRole Classify(Pawn p, AssaultPlan plan)
        {
            if (plan.breachTeam.Contains(p)) return RaidRole.Breacher;
            ThingWithComps eq = p.equipment?.Primary;
            if (eq == null || !eq.def.IsRangedWeapon) return RaidRole.Assault;
            var tags = eq.def.weaponTags ?? new List<string>();
            VerbProperties vp = eq.def.Verbs.FirstOrDefault(v => !v.IsMeleeAttack);
            int burst = vp?.burstShotCount ?? 1;
            float range = vp?.range ?? 0f;
            bool aoe = (vp?.defaultProjectile?.projectile?.explosionRadius ?? 0f) > 0f || tags.Any(t => AoeTag.IsMatch(t));
            if (aoe) return RaidRole.Grenadier;
            if (tags.Any(t => MgTag.IsMatch(t)) && burst >= 8) return RaidRole.Support;
            if (tags.Any(t => SniperTag.IsMatch(t)) || (range >= 65f && burst <= 2)) return RaidRole.Marksman;
            return RaidRole.Rifleman;
        }

        /// <summary>
        /// Roles for the whole raid. Base of fire = MGs + marksmen; without them, the best-armed riflemen (one per five
        /// raiders). One medic (highest Medicine) in raids of 6+. Leader = faction leader / leader kind / highest
        /// combat power.
        /// </summary>
        public static void Assign(AssaultPlan plan, List<Pawn> pawns)
        {
            plan.roleOf.Clear();
            foreach (var p in pawns) plan.roleOf[p] = Classify(p, plan);
            // base of fire: at least one per five raiders, at most a third of the raid (the rest must be able to assault);
            // MGs first, then the longest-reaching marksmen, then the best-armed riflemen
            int minFire = Mathf.Max(1, pawns.Count / 5);
            int maxFire = Mathf.Max(1, Mathf.RoundToInt(pawns.Count / 3f));
            float Range(Pawn p) => p.equipment?.Primary?.def.Verbs.FirstOrDefault(v => !v.IsMeleeAttack)?.range ?? 0f;
            var fire = pawns.Where(p => plan.roleOf[p] == RaidRole.Support).OrderByDescending(p => p.equipment.Primary.MarketValue)
                .Concat(pawns.Where(p => plan.roleOf[p] == RaidRole.Marksman).OrderByDescending(Range))
                .ToList();
            foreach (var p in fire.Skip(maxFire)) plan.roleOf[p] = RaidRole.Rifleman;
            int have = Mathf.Min(fire.Count, maxFire);
            if (have < minFire)
                foreach (var p in pawns.Where(p => plan.roleOf[p] == RaidRole.Rifleman && !fire.Take(maxFire).Contains(p))
                                       .OrderByDescending(p => p.equipment.Primary.MarketValue).Take(minFire - have))
                    plan.roleOf[p] = RaidRole.Support;
            plan.leader = Competence.PickLeader(pawns);
            if (pawns.Count >= 6)
            {
                var medic = pawns.Where(p => plan.roleOf[p] == RaidRole.Rifleman || plan.roleOf[p] == RaidRole.Assault)
                                 .Where(p => p != plan.leader && p.skills != null)
                                 .OrderByDescending(p => p.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0).FirstOrDefault();
                if (medic != null) plan.roleOf[medic] = RaidRole.Medic;
            }
        }

        // grenadiers with an explosive launcher soften the defenders from cover before the assault (playtest 2026-10-25:
        // "the grenadiers did not move forward until already units were lost")
        public static bool IsBaseOfFire(AssaultPlan plan, Pawn p) =>
            (plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Support || r == RaidRole.Marksman || (r == RaidRole.Grenadier && Launchers.ExplosiveLauncher(p))))
            || plan.overwatchOf.ContainsKey(p);

        /// <summary>A gunner that can cover a breach from range (not the medic; range ≥ 15; not a grenade or launcher).</summary>
        public static bool IsRangedFighter(AssaultPlan plan, Pawn p)
        {
            var eq = p.equipment?.Primary;
            if (eq == null || !eq.def.IsRangedWeapon) return false;
            if (plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Medic || r == RaidRole.Grenadier || r == RaidRole.Breacher || r == RaidRole.Shield)) return false;
            if (plan.shieldOf.ContainsKey(p)) return false;   // follows a shield in the assault
            return (eq.def.Verbs.FirstOrDefault(v => !v.IsMeleeAttack)?.range ?? 0f) >= 15f;
        }

        public static string Describe(AssaultPlan plan) =>
            string.Join(" ", plan.roleOf.GroupBy(kv => kv.Value).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count()));
    }

    /// <summary>Combat Extended suppression state, read by reflection (CE stays optional).</summary>
    public static class Suppression
    {
        private static bool resolved;
        private static Type compType;
        private static FieldInfo isSuppressed;
        private static PropertyInfo isHunkering;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            compType = GenTypes.GetTypeInAnyAssembly("CombatExtended.CompSuppressable");
            if (compType == null) return;
            isSuppressed = compType.GetField("isSuppressed", BindingFlags.Public | BindingFlags.Instance);
            isHunkering = compType.GetProperty("IsHunkering", BindingFlags.Public | BindingFlags.Instance);
        }

        public static bool Available { get { Resolve(); return compType != null; } }

        /// <summary>0 = free, 1 = suppressed, 2 = hunkering.</summary>
        public static int State(Pawn p)
        {
            Resolve();
            if (compType == null || p == null) return 0;
            foreach (var c in p.AllComps)
            {
                if (!compType.IsInstanceOfType(c)) continue;
                try
                {
                    if (isHunkering != null && (bool)isHunkering.GetValue(c)) return 2;
                    if (isSuppressed != null && (bool)isSuppressed.GetValue(c)) return 1;
                }
                catch { }
                return 0;
            }
            return 0;
        }

        /// <summary>Player fighters near the objective that our base of fire can see.</summary>
        public static List<Pawn> Defenders(Map map, IntVec3 objective, float radius = 30f) =>
            map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
               .Where(p => !p.Downed && !p.Dead && p.RaceProps.Humanlike && p.Position.DistanceTo(objective) <= radius).ToList();

        /// <summary>Share of defenders (that some raider of the lord can see) who are suppressed or hunkering.</summary>
        public static float PinnedFraction(Map map, AssaultPlan plan, IEnumerable<Pawn> raiders)
        {
            var defs = Defenders(map, plan.objective);
            var seen = defs.Where(d => raiders.Any(r => r.Spawned && !r.Downed && GenSight.LineOfSight(r.Position, d.Position, map, true))).ToList();
            if (seen.Count == 0) return 0f;
            return seen.Count(d => State(d) > 0) / (float)seen.Count;
        }
    }

    /// <summary>
    /// Base of fire (support by fire): from a covered position with line of sight, lay continuous fire on the cover
    /// each defender uses (sandbag, wall, embrasure) - CE near-misses pin everything within 3 cells of each bullet -
    /// or on the defender itself. Prefers defenders not yet pinned and those nearest our movers.
    /// </summary>
    public class JobGiver_HSKTAI_Support : ThinkNode_JobGiver
    {
        private static readonly JobGiver_AIFightEnemies fight = new JobGiver_AIFightEnemies();

        protected override Job TryGiveJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            long tp = Perf.Start();
            Job throwS = Grenades.TryThrowJob(pawn, plan);
            Perf.Stop("support.throw", tp);
            if (throwS != null) return throwS;
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (plan == null || verb == null || verb.IsMeleeAttack) return null;
            Job reloadHE = Launchers.ReloadFromGas(pawn, verb);   // grenadier with gas loaded: the explosive round first
            if (reloadHE != null) return reloadHE;
            Map map = pawn.Map;
            bool holding = plan.overwatchOf.TryGetValue(pawn, out IntVec3 owCell);
            if (holding && pawn.Position.DistanceTo(owCell) > 1.5f)
            {
                Job goOw = JobMaker.MakeJob(JobDefOf.Goto, owCell);
                goOw.locomotionUrgency = LocomotionUrgency.Jog;
                goOw.expiryInterval = 400;
                goOw.checkOverrideOnExpire = true;
                return goOw;
            }
            var defs = Suppression.Defenders(map, plan.objective, 40f);
            if (defs.Count == 0) return holding ? JobMaker.MakeJob(JobDefOf.Wait_Combat, 60, true) : null;

            // a target we can shoot from here: unpinned first, then nearest the objective's entries
            Pawn target = null;
            float best = float.MaxValue;
            tp = Perf.Start();
            foreach (var d in defs)
            {
                if (d.Position.DistanceTo(pawn.Position) > verb.verbProps.range) continue;
                if (!verb.CanHitTargetFrom(pawn.Position, d)) continue;
                // unpinned first; exposed (flanked) defenders first - marksmen take them before anything else
                bool exposed = s.flanking && Competence.Knows(plan, Doctrine.Flanking) && !Flanking.Covered(d, pawn.Position);
                bool marks = plan.roleOf.TryGetValue(pawn, out var rl) && rl == RaidRole.Marksman;
                // base of fire: unpinned first (that is its job); marksmen: exposed and dangerous first; focus fire
                float score = Suppression.State(d) * 100f + d.Position.DistanceTo(pawn.Position) - (exposed ? (marks ? 250f : 40f) : 0f)
                              + (s.focusFire && Competence.Knows(plan, Doctrine.FocusFire) ? Targeting.Score(pawn, d, plan) - d.Position.DistanceTo(pawn.Position) : 0f);
                if (score < best) { best = score; target = d; }
            }
            Perf.Stop("support.target", tp);
            if (target == null)
            {
                if (holding) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 60, true);   // overwatch: hold the position, never relocate
                tp = Perf.Start();
                IntVec3 fp = SupportPosition(pawn, plan, verb, defs);
                Perf.Stop("support.position", tp);
                if (fp.IsValid && fp != pawn.Position)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, fp);
                    go.locomotionUrgency = LocomotionUrgency.Jog;
                    go.expiryInterval = 300;
                    go.checkOverrideOnExpire = true;
                    return go;
                }
                if (!fp.IsValid && plan.phase == PlanPhase.Approach) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);   // no safe firing position: hold, don't advance into their fire
                tp = Perf.Start();
                Job fj = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                Perf.Stop("support.vanillaFight", tp);
                return fj;
            }
            // fire at the defender's cover (between defender and us) if there is one: near-misses pin, hits on cover
            // don't waste the burst on armour
            LocalTargetInfo aim = target;
            Thing cover = CoverOf(target, pawn.Position, map);
            if (cover != null && verb.CanHitTargetFrom(pawn.Position, cover)) aim = cover;
            Job shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, aim);
            shoot.maxNumStaticAttacks = 3;
            if ((verb.verbProps.defaultProjectile?.projectile?.explosionRadius ?? 0f) > 0f) shoot.preventFriendlyFire = true;   // launchers: not through our own men
            shoot.endIfCantShootTargetFromCurPos = true;
            shoot.expiryInterval = 240;
            shoot.checkOverrideOnExpire = true;
            HSKTAI_Stats.SupportBursts++;
            return shoot;
        }

        /// <summary>The cover object next to the defender on our side (fillPercent ≥ 0.4), if any.</summary>
        private static Thing CoverOf(Pawn defender, IntVec3 from, Map map)
        {
            Thing best = null;
            float bd = float.MaxValue;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(defender))
            {
                if (!c.InBounds(map)) continue;
                Building b = c.GetEdifice(map);
                if (b == null || b.def.fillPercent < 0.4f) continue;
                float d = c.DistanceToSquared(from);
                if (d < bd && d < defender.Position.DistanceToSquared(from)) { bd = d; best = b; }
            }
            return best;
        }

        /// <summary>
        /// A firing position: within range of some defender with a clear shot, standable, reachable, preferably with
        /// cover next to it, close to the pawn. Searched around the pawn (≤ 20 cells).
        /// </summary>
        /// <summary>
        /// Stand-off firing position (playtest: "stay out of range and let snipers take shots"): a cell with a defender in
        /// range and view, at about 85 % of the weapon's range (outranging them where the gun allows), never in the
        /// defenders' fire in the open (in their fire only with cover), cover preferred, not far from where it stands.
        /// </summary>
        // performance: the chosen position per gunner is kept for supportPosTicks (the search is the most expensive call)
        private static readonly Dictionary<Pawn, (IntVec3 cell, int tick)> supportPosCache = new Dictionary<Pawn, (IntVec3, int)>();

        private static IntVec3 SupportPosition(Pawn pawn, AssaultPlan plan, Verb verb, List<Pawn> defs)
        {
            int now = Find.TickManager.TicksGame;
            if (supportPosCache.TryGetValue(pawn, out var cached) && now - cached.tick < 300 * Adaptive.Load && (!cached.cell.IsValid || cached.cell.Standable(pawn.Map)))
                return cached.cell;
            IntVec3 found = SearchSupportPosition(pawn, plan, verb, defs);
            supportPosCache[pawn] = (found, now);
            return found;
        }

        /// <summary>
        /// Stand-off firing position via the position finder (one flood from the gunner: reachable cells with the fire on
        /// the way): cheap score on distance band / fire / cover / exposure on the way, then line of sight and flank
        /// exposure only for the 30 best.
        /// </summary>
        private static IntVec3 SearchSupportPosition(Pawn pawn, AssaultPlan plan, Verb verb, List<Pawn> defs)
        {
            Map map = pawn.Map;
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            float range = verb.verbProps.range * 0.95f, want = verb.verbProps.range * 0.85f;
            return PositionFinder.Find(pawn, 25f,
                cell =>
                {
                    Pawn near = null;
                    float nd = float.MaxValue;
                    foreach (var d in defs) { float dd = d.Position.DistanceTo(cell.c); if (dd < nd) { nd = dd; near = d; } }
                    if (near == null || nd > range || nd < 6f) return float.NaN;
                    bool cover = HasCover(cell.c, near.Position, map);
                    float fire = haveThreat ? tm.FireAt(cell.c) : 0f;
                    if (fire > s.safeFire && !cover) return float.NaN;
                    return cell.path * 0.4f + Mathf.Abs(nd - want) * 0.5f - (cover ? 6f : 0f) + fire * 10f + cell.exposure * 3f;
                },
                c =>
                {
                    // pf5/pf6: up to 17 ms - CE's shot check is the cost. The plain line of sight first (cheap), then one
                    // CE check on the nearest defender in sight
                    Pawn seen = defs.Where(d => d.Position.DistanceTo(c) <= range && d.Position.DistanceTo(c) >= 6f)
                                    .OrderBy(d => d.Position.DistanceToSquared(c)).Take(3)
                                    .FirstOrDefault(d => GenSight.LineOfSight(c, d.Position, map, true));
                    if (seen == null || !verb.CanHitTargetFrom(c, seen)) return float.NaN;
                    return s.flanking && Competence.Knows(plan, Doctrine.Flanking) ? -s.flankPositionBonus * Flanking.ExposedFrom(c, defs, range, map) : 0f;
                }, 20);
        }

        public static bool HasCover(IntVec3 c, IntVec3 enemy, Map map)
        {
            foreach (IntVec3 a in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)))
            {
                if (!a.InBounds(map) || a.DistanceToSquared(enemy) >= c.DistanceToSquared(enemy)) continue;
                Building b = a.GetEdifice(map);
                if (b != null && b.def.fillPercent >= 0.4f) return true;
                if (a.GetPlant(map)?.def.plant?.IsTree == true) return true;
            }
            return false;
        }
    }

    /// <summary>Counters for the AI Lab.</summary>
    public static class HSKTAI_Stats
    {
        public static int SupportBursts;
    }
}
