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
    /// <summary>
    /// Siege camp security (playtest, 2026-10-25: "when preparing the siege the non active builders should take up defensive
    /// positions in cover towards the defenders and adjust when they try to flank ... according to their unit type, snipers
    /// more in the back ... smart placements with good coverage ... rotate when needed and cover flanks").
    ///
    /// Vanilla gives every siege raider who isn't building "Defend": wander near the camp and fight whoever comes. Here the
    /// guards (not the builders, not the mortar crews) get positions around the camp by role, facing the threat - the
    /// bearing from the camp to the nearest defender within 60 cells (else to the defenders' centre):
    /// - riflemen: the front arc (+-35 deg), 7-10 cells out;
    /// - machine guns: the two flanks (+-70 deg), 6-9 cells out;
    /// - marksmen: behind the camp centre, 3-6 cells back - long sight lines over the camp;
    /// - grenadiers and the rest: the front, a little behind the riflemen;
    /// - melee: close in front of the builders (3-4 cells), to catch whoever rushes them;
    /// - with 6+ guards one rifleman watches the rear, so a flanking colonist meets fire from every side.
    /// Each position is a standable cell with cover towards the threat (wall, sandbag, rock, tree), out of the defenders' fire
    /// if there is one, 2+ cells from the next guard. The slots turn with the threat: when the defenders come round a side,
    /// the arc follows. A guard rotates to another covered cell of his slot when his own lost its cover against the threat or
    /// he was hit there. In range of a defender he fires from his position instead of chasing.
    /// How sharp this is follows the raid's competence: positions in cover at all from "support fire", the role spread with
    /// flank and rear guards from "flanking".
    /// </summary>
    public static class SiegeGuards
    {
        public static int PlacementJobs, Rotations, Shots, Guards;
        public static int SpotsWithSight, SpotsBlind;

        public class Post
        {
            public IntVec3 cell = IntVec3.Invalid;
            public int slot, tick = -99999, rotated = -99999;
            public float bearing;
        }

        private static readonly Dictionary<Pawn, Post> posts = new Dictionary<Pawn, Post>();
        private static readonly Dictionary<int, (int tick, float bearing, IntVec3 point)> threatOf = new Dictionary<int, (int, float, IntVec3)>();

        public enum Kind { Rifle, Support, Marksman, Grenadier, Melee }

        public static Kind KindOf(Pawn p)
        {
            ThingWithComps eq = p.equipment?.Primary;
            if (eq == null || !eq.def.IsRangedWeapon) return Kind.Melee;
            VerbProperties vp = eq.def.Verbs.FirstOrDefault(v => !v.IsMeleeAttack);
            int burst = vp?.burstShotCount ?? 1;
            float range = vp?.range ?? 0f;
            if ((vp?.defaultProjectile?.projectile?.explosionRadius ?? 0f) > 0f) return Kind.Grenadier;
            if (burst >= 8) return Kind.Support;
            if (range >= 55f && burst <= 2) return Kind.Marksman;
            return Kind.Rifle;
        }

        /// <summary>Where the danger comes from, seen from the camp: the nearest defender within 60 cells, else the defenders' centre.</summary>
        public static float ThreatBearing(Lord lord, IntVec3 camp, bool nearest, out IntVec3 point)
        {
            int now = Find.TickManager.TicksGame;
            if (threatOf.TryGetValue(lord.loadID, out var t) && now - t.tick < 120) { point = t.point; return t.bearing; }
            Map map = lord.Map;
            var defs = Suppression.Defenders(map, camp, 999f);
            point = IntVec3.Invalid;
            if (defs.Count > 0)
            {
                Pawn near = defs.OrderBy(d => d.Position.DistanceToSquared(camp)).First();
                if (nearest && near.Position.DistanceTo(camp) <= 60f) point = near.Position;
                else point = new IntVec3((int)defs.Average(d => d.Position.x), 0, (int)defs.Average(d => d.Position.z));
            }
            if (!point.IsValid) point = map.Center;
            float b = AssaultPlanner.Bearing(camp, point);
            threatOf[lord.loadID] = (now, b, point);
            return b;
        }

        /// <summary>Slot for a guard: (bearing offset from the threat, distance from the camp centre).</summary>
        private static (float off, float min, float max) Slot(Kind k, int index, int count, bool spread)
        {
            if (!spread) return (((index % 3) - 1) * 25f, 5f, 9f);   // all in front, a loose line
            switch (k)
            {
                case Kind.Support: return ((index % 2 == 0 ? 1 : -1) * 70f, 6f, 9f);
                case Kind.Marksman: return (180f + ((index % 3) - 1) * 30f, 3f, 6f);
                case Kind.Melee: return (((index % 3) - 1) * 20f, 3f, 4.5f);
                case Kind.Grenadier: return (((index % 3) - 1) * 25f, 5f, 7f);
                default: return (((index % 5) - 2) * 17f, 7f, 10f);
            }
        }

        private static IntVec3 PointAt(IntVec3 camp, float bearing, float dist) =>
            new IntVec3(camp.x + Mathf.RoundToInt(Mathf.Cos(bearing * Mathf.Deg2Rad) * dist), 0, camp.z + Mathf.RoundToInt(Mathf.Sin(bearing * Mathf.Deg2Rad) * dist));

        /// <summary>The best covered cell of a slot: cover towards the threat, out of fire, away from other guards, reachable.</summary>
        private static IntVec3 FindCell(Pawn p, IntVec3 camp, float bearing, float min, float max, IntVec3 threat, IntVec3 avoid)
        {
            Map map = p.Map;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            float safe = TacticalAIMod.Settings.safeFire;
            IntVec3 want = PointAt(camp, bearing, (min + max) * 0.5f);
            // candidates: around the slot, plus every cell behind the camp's own finished sandbags and barricades on this side
            // (playtest: "when sandbags or defensive walls are ready attackers in siege should use them to take cover")
            var cands = new HashSet<IntVec3>(GenRadial.RadialCellsAround(want, 4.5f, true));
            var own = new HashSet<IntVec3>();
            foreach (Building b in map.listerBuildings.allBuildingsNonColonist)
            {
                if (b.Faction != p.Faction || !b.Spawned || b.def.fillPercent < 0.4f || b.Position.DistanceTo(camp) > 18f) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(AssaultPlanner.Bearing(camp, b.Position), bearing)) > 55f) continue;
                foreach (IntVec3 a in GenAdj.CellsAdjacent8Way(b))
                    if (a.InBounds(map) && a.DistanceToSquared(threat) > b.Position.DistanceToSquared(threat)) { cands.Add(a); own.Add(a); }
            }
            IntVec3 best = IntVec3.Invalid;
            float bs = float.MaxValue;
            foreach (IntVec3 c in cands)
            {
                if (!c.InBounds(map) || !c.Standable(map) || c == avoid) continue;
                float d = c.DistanceTo(camp);
                bool behindOwn = own.Contains(c);
                if (!behindOwn && (d < min - 1.5f || d > max + 1.5f)) continue;
                bool taken = false;
                foreach (var kv in posts)
                    if (kv.Key != p && kv.Key.Spawned && kv.Key.Map == map && kv.Value.cell.IsValid && kv.Value.cell.DistanceTo(c) < 2f) { taken = true; break; }
                if (taken) continue;
                bool cover = JobGiver_HSKTAI_Support.HasCover(c, threat, map);
                float fire = haveThreat ? tm.FireAt(c) : 0f;
                float score = c.DistanceTo(want) * (behindOwn ? 0.3f : 0.6f) + (cover ? 0f : 8f) - (behindOwn && cover ? 5f : 0f) + (fire > safe ? 4f + fire * 6f : 0f);
                if (score >= bs || !p.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                bs = score;
                best = c;
            }
            return best;
        }

        /// <summary>
        /// Where our raid sets up its siege camp: vanilla looks within 60 cells of the raid's position (often 100+ cells from the
        /// base: guns out of range, nobody in contact). We hand it a point about 75 cells from the objective on the raid's side,
        /// so the camp stays out of the defenders' fire but its guns reach.
        /// </summary>
        public static IntVec3 SiegeSpot(AssaultPlan plan, Map map)
        {
            IntVec3 from = plan.gather;
            if (TacticalAIMod.Settings.siegeGuards && plan.objective.IsValid)
            {
                float d = plan.gather.DistanceTo(plan.objective);
                if (d > 80f)
                {
                    float k = 75f / d;
                    var c = new IntVec3(plan.objective.x + Mathf.RoundToInt((plan.gather.x - plan.objective.x) * k), 0, plan.objective.z + Mathf.RoundToInt((plan.gather.z - plan.objective.z) * k));
                    c = CellFinder.StandableCellNear(c, map, 6f);
                    if (c.IsValid && map.reachability.CanReach(c, plan.gather, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)) from = c;
                }
            }
            // playtest: "no line of sight to base" - a camp spot that sees the colony's nearest wall, within 95 cells of it
            Building wall = null;
            float bd = float.MaxValue;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (!b.Spawned || (b.def != ThingDefOf.Wall && !b.def.IsDoor && b.def.fillPercent < 0.5f)) continue;
                float d2 = b.Position.DistanceToSquared(from);
                if (d2 < bd) { bd = d2; wall = b; }
            }
            if (TacticalAIMod.Settings.siegeGuards && wall != null)
            {
                IntVec3 w = wall.Position;
                bool Sees(IntVec3 c) => c.DistanceTo(w) <= 80f && GenSight.LineOfSight(c, w, map, true);   // guns in the camp reach the wall (90 mm: 92)
                IntVec3 seen = RCellFinder.FindSiegePositionFrom(from, map, errorOnFail: false, validator: Sees);
                if (seen.IsValid && Sees(seen)) { SpotsWithSight++; return seen; }
                SpotsBlind++;
            }
            return RCellFinder.FindSiegePositionFrom(from, map);
        }

        public static void Forget(Pawn p) => posts.Remove(p);

        private static bool OwnCover(Pawn p, IntVec3 c)
        {
            if (!c.IsValid) return false;
            foreach (IntVec3 a in GenAdj.CellsAdjacent8Way(new TargetInfo(c, p.Map)))
                if (a.InBounds(p.Map) && a.GetEdifice(p.Map) is Building b && b.Faction == p.Faction && b.def.fillPercent >= 0.4f) return true;
            return false;
        }

        // ---------------- forward observer (playtest: "why is the mortar so inaccurate? it seems to fire very randomly") ----------------
        // CE: mortar fire beyond 75 cells or without sight of the target scatters by the indirect-fire penalty x distance,
        // unless the target carries an artillery marker (CE spotting) - then the spotter's aim counts. Vanilla's siege never
        // spots. One guard with the best eye (marksman first, then Shooting) goes where he sees what the siege's mortars
        // are firing at, in cover, and marks it (as CE's own spotting does); the mark lasts 30 s and is renewed.

        public static int Marks, SpotterMoves;
        private static readonly Dictionary<int, Pawn> spotterOf = new Dictionary<int, Pawn>();
        private static readonly Dictionary<int, (LocalTargetInfo t, int tick)> lastMortarTarget = new Dictionary<int, (LocalTargetInfo, int)>();
        private static ThingDef markerDef;
        private static bool markerResolved;
        private static System.Reflection.FieldInfo fMarkAim, fMarkSights, fMarkCaster;
        private static StatDef aimStat;

        private static bool ResolveMarker()
        {
            if (!markerResolved)
            {
                markerResolved = true;
                markerDef = DefDatabase<ThingDef>.GetNamedSilentFail("ArtilleryMarker");
                var t = markerDef?.thingClass;
                if (t != null)
                {
                    fMarkAim = AccessTools.Field(t, "aimingAccuracy");
                    fMarkSights = AccessTools.Field(t, "sightsEfficiency");
                    fMarkCaster = AccessTools.Field(t, "caster");
                }
                aimStat = DefDatabase<StatDef>.GetNamedSilentFail("AimingAccuracy");
            }
            return markerDef != null && fMarkAim != null;
        }

        private static Pawn Spotter(Lord lord)
        {
            if (spotterOf.TryGetValue(lord.loadID, out Pawn s) && s != null && s.Spawned && !s.Downed && !s.Dead && s.GetLord() == lord
                && s.mindState?.duty?.def == TAI_DefOf.HSKTAI_SiegeGuard) return s;
            s = lord.ownedPawns.Where(g => g.Spawned && !g.Downed && g.mindState?.duty?.def == TAI_DefOf.HSKTAI_SiegeGuard && g.RaceProps.Humanlike)
                    .OrderByDescending(g => KindOf(g) == Kind.Marksman ? 1 : 0)
                    .ThenByDescending(g => g.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0).FirstOrDefault();
            spotterOf[lord.loadID] = s;
            return s;
        }

        /// <summary>What the lord's mortars (overhead guns) are firing at now.</summary>
        private static LocalTargetInfo MortarTarget(Lord lord)
        {
            foreach (Thing t in lord.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (!(t is Building_Turret tg) || tg.Faction != lord.faction || !tg.Spawned) continue;   // CE turrets (Building_TurretGunCE) derive from Building_Turret, not Building_TurretGun
                // CE mortars fire their ammo's shell (no default projectile): the loaded round or the mortar verb tells
                Verb av = tg.AttackVerb;
                if (av == null) continue;
                bool overhead = (Launchers.CurrentProjectile(av)?.projectile?.flyOverhead ?? false) || av.GetType().Name.Contains("Mortar");
                if (!overhead) continue;
                LocalTargetInfo cur = tg.CurrentTarget;
                if (cur.IsValid) return cur;
            }
            return LocalTargetInfo.Invalid;
        }

        private static bool Marked(Map map, LocalTargetInfo t)
        {
            foreach (Thing th in t.Cell.GetThingList(map)) if (th.def == markerDef) return true;
            if (t.HasThing && t.Thing.Spawned)
                foreach (Thing th in t.Thing.Position.GetThingList(map)) if (th.def == markerDef) return true;
            return false;
        }

        private static Job SpotterJob(Pawn pawn, Lord lord, IntVec3 camp)
        {
            if (!ResolveMarker()) return null;
            // CE mortars drop their target between their long reloads: the spotter keeps watching the last one for 15 s
            // (ld1 siege s3: 14 trips out to the post and back, no mark)
            LocalTargetInfo target = MortarTarget(lord);
            int now = Find.TickManager.TicksGame;
            if (target.IsValid) lastMortarTarget[lord.loadID] = (target, now);
            else if (lastMortarTarget.TryGetValue(lord.loadID, out var lt) && now - lt.tick < 900 && (!lt.t.HasThing || lt.t.Thing.Spawned)) target = lt.t;
            if (!target.IsValid) return null;
            Map map = pawn.Map;
            IntVec3 tc = target.Cell;
            bool sees = pawn.Position.DistanceTo(tc) <= 60f && GenSight.LineOfSight(pawn.Position, tc, map, true);
            if (sees)
            {
                if (!Marked(map, target))
                {
                    try
                    {
                        Thing m = ThingMaker.MakeThing(markerDef);
                        fMarkAim.SetValue(m, aimStat != null ? pawn.GetStatValue(aimStat) : 1f);
                        fMarkSights?.SetValue(m, 1f);
                        fMarkCaster?.SetValue(m, pawn);
                        GenSpawn.Spawn(m, tc, map);
                        if (target.HasThing && target.Thing.TryGetComp<CompAttachBase>() != null && m is AttachableThing at) at.AttachTo(target.Thing);
                        Marks++;
                    }
                    catch (System.Exception e) { Log.ErrorOnce("[HSK Tactical AI] artillery mark failed: " + e.Message, 0x48534B42); }
                }
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);   // watching the fall of shot
            }
            // an observation post: 35-55 cells from the target on the camp's side, sight of it, cover towards it, out of fire
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            float dist = Mathf.Min(45f, camp.DistanceTo(tc));
            float k = dist / Mathf.Max(1f, camp.DistanceTo(tc));
            var want = new IntVec3(tc.x + Mathf.RoundToInt((camp.x - tc.x) * k), 0, tc.z + Mathf.RoundToInt((camp.z - tc.z) * k));
            IntVec3 best = IntVec3.Invalid;
            float bs = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(want, 10f, true))
            {
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                float d = c.DistanceTo(tc);
                if (d > 58f || d < 20f) continue;
                float fire = tm != null && tm.CompletedCycles > 0 ? tm.FireAt(c) : 0f;
                float score = c.DistanceTo(want) * 0.4f + (JobGiver_HSKTAI_Support.HasCover(c, tc, map) ? 0f : 5f) + fire * 10f;
                if (score >= bs || !GenSight.LineOfSight(c, tc, map, true) || !pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                bs = score;
                best = c;
            }
            if (!best.IsValid) return null;
            SpotterMoves++;
            Job go = JobMaker.MakeJob(JobDefOf.Goto, best);
            go.locomotionUrgency = LocomotionUrgency.Jog;
            go.expiryInterval = 400;
            go.checkOverrideOnExpire = true;
            return go;
        }

        public static Job GuardJob(Pawn pawn)
        {
            var s = TacticalAIMod.Settings;
            Lord lord = pawn.GetLord();
            if (!s.siegeGuards || lord == null || !(lord.CurLordToil is LordToil_Siege siege) || pawn.mindState?.duty == null) return null;
            IntVec3 camp = siege.FlagLoc;
            int now = Find.TickManager.TicksGame;
            // sharp commands turn with the nearest defender (flanking colonists), dull ones face the defenders' centre
            bool sharp = Competence.KnowsLord(lord, Doctrine.Flanking);
            float bearing = ThreatBearing(lord, camp, sharp, out IntVec3 threat);
            if (!posts.TryGetValue(pawn, out var post)) { posts[pawn] = post = new Post(); Guards++; }
            // the forward observer marks what the mortars shoot at (CE spotting)
            if (Spotter(lord) == pawn)
            {
                Job sj = SpotterJob(pawn, lord, camp);
                if (sj != null) return sj;
            }

            // in range of a defender from where I stand (in cover): fire from the position, don't chase
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (v != null && !v.IsMeleeAttack && post.cell.IsValid && pawn.Position.DistanceTo(post.cell) <= 1.5f && v.Available())
            {
                Pawn target = ShieldWall.NearestEnemy(pawn, v.verbProps.range);
                if (target != null && v.CanHitTarget(target))
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

            // (re)place: no post yet, the threat turned (> 30 deg), the post lost its cover against the threat or I was hit
            // there; else every 600 ticks a fresh look
            // cp6 village siege: 191 moves - every hit moved a guard. After a move he holds 300 ticks unless his spot has no cover
            bool hit = now - pawn.mindState.lastHarmTick < 120 && post.cell.IsValid && pawn.Position.DistanceTo(post.cell) <= 1.5f && now - post.rotated > 300;
            bool turned = Mathf.Abs(Mathf.DeltaAngle(post.bearing, bearing)) > 30f;
            bool uncovered = post.cell.IsValid && !JobGiver_HSKTAI_Support.HasCover(post.cell, threat, pawn.Map);
            if (!post.cell.IsValid || turned || hit || (uncovered && now - post.tick > 180) || now - post.tick > 600)
            {
                bool spread = Competence.KnowsLord(lord, Doctrine.SupportFire);   // by role from a middling command on
                var guards = lord.ownedPawns.Where(g => g.Spawned && !g.Downed && g.mindState?.duty?.def == TAI_DefOf.HSKTAI_SiegeGuard)
                                 .OrderBy(g => g.thingIDNumber).ToList();
                Kind k = KindOf(pawn);
                int index = guards.Where(g => KindOf(g) == k).ToList().IndexOf(pawn);
                // the rear: with 6+ guards, the second rifleman faces the other way
                bool rear = sharp && guards.Count >= 6 && k == Kind.Rifle && index == 1;
                (float off, float min, float max) slot = rear ? (180f, 6f, 9f) : Slot(k, Mathf.Max(0, index), guards.Count, spread);
                IntVec3 avoid = hit || uncovered ? post.cell : IntVec3.Invalid;
                IntVec3 c = FindCell(pawn, camp, bearing + slot.off, slot.min, slot.max, rear ? PointAt(camp, bearing + 180f, 20f) : threat, avoid);
                // cp4t siege: 371 moves - guards left uncovered posts for other uncovered ones. Move for cover only to cover;
                // a hit or a turned threat moves him anyway
                IntVec3 threatNow = rear ? PointAt(camp, bearing + 180f, 20f) : threat;
                bool better = !post.cell.IsValid || hit || turned || (uncovered && JobGiver_HSKTAI_Support.HasCover(c, threatNow, pawn.Map))
                              || (c.IsValid && OwnCover(pawn, c) && !OwnCover(pawn, post.cell));   // a covered post is kept, unless the camp's own sandbags are finished
                if (c.IsValid && better)
                {
                    if (post.cell.IsValid && c != post.cell) { Rotations++; post.rotated = now; }
                    post.cell = c;
                }
                post.bearing = bearing;
                post.tick = now;
            }
            if (!post.cell.IsValid) return null;
            if (pawn.Position != post.cell)
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, post.cell);
                go.locomotionUrgency = pawn.Position.DistanceTo(post.cell) > 8f ? LocomotionUrgency.Jog : LocomotionUrgency.Walk;
                go.expiryInterval = 300;
                go.checkOverrideOnExpire = true;
                PlacementJobs++;
                return go;
            }
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 90, true);   // crouched in cover, watching the threat
        }
    }

    public class JobGiver_HSKTAI_SiegeGuard : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            long t0 = Perf.Start();
            Job j = SiegeGuards.GuardJob(pawn);
            Perf.Stop("siege.guard", t0);
            return j;
        }
    }

    /// <summary>After vanilla hands out the siege duties: the defenders (not the builders) become guards with posts.</summary>
    [HarmonyPatch(typeof(LordToil_Siege), nameof(LordToil_Siege.UpdateAllDuties))]
    public static class Patch_SiegeGuards
    {
        public static void Postfix(LordToil_Siege __instance)
        {
            var s = TacticalAIMod.Settings;
            Lord lord = __instance.lord;
            if (s == null || !s.siegeGuards || TAI_DefOf.HSKTAI_SiegeGuard == null || lord?.faction == null || lord.Map == null
                || !lord.Map.IsPlayerHome || !lord.faction.HostileTo(Faction.OfPlayer)) return;
            foreach (Pawn p in lord.ownedPawns)
            {
                if (p.mindState?.duty == null || p.mindState.duty.def != DutyDefOf.Defend) continue;
                p.mindState.duty = new PawnDuty(TAI_DefOf.HSKTAI_SiegeGuard, __instance.FlagLoc) { radius = p.mindState.duty.radius };
            }
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>
    /// Direct-fire siege guns (playtest: "if a siege has direct fire weapons it needs to place them at max range towards the
    /// base so it can shoot at the walls etc. now it is too far back"): vanilla sets every siege gun within 8 cells of the
    /// camp centre - fine for mortars, which fire overhead, useless for cannons with no line of fire. A gun whose round does
    /// not fly overhead is set up on the way from the camp to the colony's nearest wall or building: the first spot (from
    /// the camp outwards, at most 45 cells) within 90 % of its range with a clear line of fire on it - as far back as its
    /// range allows - out of the defenders' fire if there is such a spot. Mortars and other overhead guns stay as they are.
    /// </summary>
    [HarmonyPatch(typeof(SiegeBlueprintPlacer), "FindArtySpot")]
    public static class Patch_DirectFireArtySpot
    {
        public static int Placed, NoSpot;
        private static readonly AccessTools.FieldRef<IntVec3> Center = AccessTools.StaticFieldRefAccess<IntVec3>(AccessTools.Field(typeof(SiegeBlueprintPlacer), "center"));
        private static readonly System.Reflection.MethodInfo CanPlace = AccessTools.Method(typeof(SiegeBlueprintPlacer), "CanPlaceBlueprintAt");

        public static bool Prefix(ThingDef artyDef, Rot4 rot, Map map, ref IntVec3 __result)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.siegeGuards || map == null || !map.IsPlayerHome || CanPlace == null) return true;
            try
            {
                ThingDef gun = artyDef.building?.turretGunDef ?? artyDef;
                VerbProperties vp = gun.Verbs?.FirstOrDefault(v => !v.IsMeleeAttack);
                ProjectileProperties pp = vp?.defaultProjectile?.projectile;
                if (vp == null || pp == null || pp.flyOverhead) return true;   // mortars, howitzers: vanilla
                IntVec3 center = Center();
                Building target = null;
                float bd = float.MaxValue;
                foreach (Building b in map.listerBuildings.allBuildingsColonist)
                {
                    if (!b.Spawned) continue;
                    float d = b.Position.DistanceToSquared(center);
                    if (b.def != ThingDefOf.Wall && !b.def.IsDoor) d *= 1.3f;   // walls and doors first
                    if (d < bd) { bd = d; target = b; }
                }
                if (target == null) return true;
                IntVec3 t = target.Position;
                float range = Mathf.Min(vp.range, 120f) * 0.9f, min = vp.minRange + 2f;
                float total = center.DistanceTo(t);
                if (total < 1f) return true;
                float dx = (t.x - center.x) / total, dz = (t.z - center.z) / total;
                var tm = MapComponent_TacticalAI.For(map)?.Threat;
                bool haveThreat = tm != null && tm.CompletedCycles > 0;
                // inside the camp (playtest: "the 90mm is way too far forward, it needs to stay within the FOB so it is covered"):
                // at most 14 cells from its centre; pass 0 out of fire, pass 1 any, pass 2 the camp's front edge without sight
                for (int pass = 0; pass < 3; pass++)
                    for (float d = 0f; d <= Mathf.Min(14f, total - min); d += 2f)
                    {
                        var basePt = new IntVec3(center.x + Mathf.RoundToInt(dx * d), 0, center.z + Mathf.RoundToInt(dz * d));
                        foreach (IntVec3 c in GenRadial.RadialCellsAround(basePt, 2.5f, true))
                        {
                            if (!c.InBounds(map) || c.Roofed(map)) continue;
                            float dt = c.DistanceTo(t);
                            if (dt < min) continue;
                            if (pass < 2 && dt > range) continue;
                            if (pass == 0 && haveThreat && tm.FireAt(c) > s.safeFire) continue;
                            if (pass < 2 && !GenSight.LineOfSight(c, t, map, true)) continue;
                            if (pass == 2 && d < Mathf.Min(14f, total - min) - 4f) continue;   // front edge only
                            if (!map.reachability.CanReach(c, center, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly)) continue;
                            if (!(bool)CanPlace.Invoke(null, new object[] { c, rot, artyDef, map, ThingDefOf.Steel })) continue;
                            __result = c;
                            Placed++;
                            if (Prefs.DevMode) Log.Message("[HSK Tactical AI] siege: direct-fire " + artyDef.defName + " at " + c + ", " + Mathf.RoundToInt(dt) + " cells from " + target.LabelShort + " (range " + Mathf.RoundToInt(vp.range) + ")");
                            return false;
                        }
                    }
                NoSpot++;
                if (Prefs.DevMode) Log.Message("[HSK Tactical AI] siege: no forward spot for " + artyDef.defName + " (range " + Mathf.RoundToInt(vp.range) + ", camp " + Mathf.RoundToInt(total) + " cells from " + target.LabelShort + ")");
            }
            catch (System.Exception e) { Log.ErrorOnce("[HSK Tactical AI] siege gun placement failed, using vanilla: " + e.Message, 0x48534B41); }
            return true;
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>
    /// Direct-fire siege guns engage the colony's structures (playtest: "flack cannon might have had sight but does not
    /// engage wall"): vanilla and CE turrets only pick pawns unless their round flies overhead (mortars shell buildings at
    /// random). A raiders' turret whose round does not fly overhead and has no pawn to shoot takes a building it can hit:
    /// the colony's turrets first, then walls and doors - those nearest the defenders first. Checked every 120 ticks per
    /// gun, the 20 closest buildings in range only.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SiegeGunStructureTarget
    {
        public static int Picks, AnimalPicks, Asked;
        private static readonly HashSet<Thing> logged = new HashSet<Thing>();
        private static readonly Dictionary<Thing, (int tick, LocalTargetInfo t)> last = new Dictionary<Thing, (int, LocalTargetInfo)>();

        public static bool Prepare() => AccessTools.TypeByName("CombatExtended.Building_TurretGunCE") != null;

        public static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("CombatExtended.Building_TurretGunCE"), "TryFindNewTarget");

        public static int ShotChecksFailed;

        /// <summary>CE's line-of-fire check can throw on the way (cp9 siege: an InvalidCastException to Gas on some mod's gas
        /// thing in the line, 2,033 times - every tick, the turret dead). A failed check counts as no shot.</summary>
        private static bool SafeCanHit(Verb v, LocalTargetInfo t)
        {
            try { return v.CanHitTarget(t); }
            catch (System.Exception e)
            {
                ShotChecksFailed++;
                Log.WarningOnce("[HSK Tactical AI] siege gun line-of-fire check failed (counted as no shot): " + e.GetType().Name + ": " + e.Message, 0x48534B43);
                return false;
            }
        }

        public static void Postfix(Building_Turret __instance, ref LocalTargetInfo __result)
        {
            var s = TacticalAIMod.Settings;
            if (__result.IsValid || s == null || !s.siegeGuards || __instance?.Faction == null || __instance.Map == null) return;
            if (__instance.Faction.HostileTo(Faction.OfPlayer)) Asked++;
            Map map = __instance.Map;
            if (!map.IsPlayerHome || !__instance.Faction.HostileTo(Faction.OfPlayer)) return;
            Verb v = __instance.AttackVerb;
            if (v == null || v.ProjectileFliesOverhead()) return;
            int now = Find.TickManager.TicksGame;
            if (last.TryGetValue(__instance, out var l) && now - l.tick < 120)
            {
                if (l.t.IsValid && l.t.Thing != null && l.t.Thing.Spawned && !l.t.Thing.Destroyed && !(l.t.Thing is Pawn lp && (lp.Dead || lp.Downed))) __result = l.t;
                return;
            }
            float range = v.verbProps.range, min = v.verbProps.minRange;
            // only explosive rounds hurt walls; machine guns take the colony's animals in sight instead (playtest 2026-10-25)
            if (!Launchers.WallBreaker(Launchers.CurrentProjectile(v)))
            {
                Pawn animal = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                    .Where(a => !a.RaceProps.Humanlike && !a.Downed && !a.Dead && a.Position.DistanceTo(__instance.Position) <= range)
                    .OrderBy(a => a.Position.DistanceToSquared(__instance.Position)).Take(10)
                    .FirstOrDefault(a => SafeCanHit(v, a));
                LocalTargetInfo at = animal != null ? new LocalTargetInfo(animal) : LocalTargetInfo.Invalid;
                last[__instance] = (now, at);
                if (animal != null) { __result = at; AnimalPicks++; }
                return;
            }
            var defs = Suppression.Defenders(map, __instance.Position, 999f);
            IntVec3 dc = defs.Count > 0 ? new IntVec3((int)defs.Average(d => d.Position.x), 0, (int)defs.Average(d => d.Position.z)) : IntVec3.Invalid;
            Building pick = null;
            float best = float.MaxValue;
            foreach (Building b in map.listerBuildings.allBuildingsColonist
                         .Where(b => b.Spawned && (b is Building_Turret || b.def == ThingDefOf.Wall || b.def.IsDoor))
                         .Where(b => { float d = b.Position.DistanceTo(__instance.Position); return d <= range && d >= min + 1f; })
                         .OrderBy(b => b.Position.DistanceToSquared(__instance.Position)).Take(20))
            {
                float score = (b is Building_Turret ? -100f : 0f) + (dc.IsValid ? b.Position.DistanceTo(dc) : 0f);
                if (score >= best) continue;
                if (!SafeCanHit(v, b)) continue;
                best = score;
                pick = b;
            }
            LocalTargetInfo t = pick != null ? new LocalTargetInfo(pick) : LocalTargetInfo.Invalid;
            last[__instance] = (now, t);
            if (Prefs.DevMode && logged.Add(__instance)) Log.Message("[HSK Tactical AI] siege gun " + __instance.def.defName + " at " + __instance.Position + " looks for structures: " + (pick != null ? pick.LabelShort + " at " + pick.Position : "none in range / in sight"));
            if (pick == null) return;
            __result = t;
            Picks++;
        }
    }
}
