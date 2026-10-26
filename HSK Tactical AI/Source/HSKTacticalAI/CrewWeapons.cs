using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>One crew-served weapon: the gun carried packed by its gunner, ammo by one or two bearers.</summary>
    public class CswTeam
    {
        public Pawn gunner;
        public readonly List<Pawn> bearers = new List<Pawn>();
        public ThingDef turretDef;
        public IntVec3 post = IntVec3.Invalid;
        public Building_Turret gun;
        public int setupTick = -1, bearerWaitTicks;
        public bool deployed, abandoned;
        public readonly List<IntVec3> route = new List<IntVec3>();
        public bool Has(Pawn p) => p == gunner || bearers.Contains(p);
        public IEnumerable<Pawn> Crew => bearers.Prepend(gunner);
        public string Describe() => turretDef?.label + (deployed ? " firing" : abandoned ? " abandoned" : " carried") + " at " + post;
    }

    /// <summary>
    /// Crew-served weapons in normal raids (playtest, 2026-10-25: "smaller static weapons like HMGs can be carried by
    /// raiders and placed closer as part of a normal raid ... one of the raiders carries the gun and 1-2 the ammo, they
    /// stay together and deploy, the team attacks when everything is ready").
    /// - Industrial+ assault raids of 8+ bring one (16+: two): a manned, minifiable turret of at most 40 kg and at most
    ///   the faction's tech level (HSK: M240B, AGS-17, M2 ... whatever the modlist has), packed in the gunner's pack;
    ///   1-2 bearers carry three magazines of its CE ammo.
    /// - The team walks a covered route together to a post at 60-75 % of the gun's range from the defence, out of fire,
    ///   in cover, with a line of fire; sets it up (4 s), the gunner mans it, the bearers reload it from their packs.
    /// - H-hour waits until every team that still lives is firing (at most 50 s).
    /// - When the crew falls, or the raid withdraws, the gun stays where it stands, unowned: loot.
    /// Part of the base of fire (doctrine SupportFire).
    /// </summary>
    public static class CrewWeapons
    {
        public static int Teams, Deployed, Abandoned, Waits, Faced, CrewBehind;
        public static string Seen = "";
        private static List<ThingDef> candidates;

        private static List<ThingDef> Candidates()
        {
            if (candidates != null) return candidates;
            float maxMass = TacticalAIMod.Settings.cswMaxMass;
            candidates = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.building?.turretGunDef != null && d.minifiedDef != null && d.HasComp(typeof(CompMannable))
                            && d.size.x == 1 && d.size.z == 1 && d.GetStatValueAbstract(StatDefOf.Mass) <= maxMass
                            && !d.building.turretGunDef.Verbs.Any(v => v.verbClass != null && v.verbClass.Name.Contains("Mortar")))
                .OrderBy(d => d.defName).ToList();
            return candidates;
        }

        public static void Plan(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            if (!s.crewWeapons || plan.goal != RaidGoal.Assault || lord.faction == null || (plan.mode != PlanMode.Walk && plan.mode != PlanMode.Breach)) return;
            if (lord.faction.def.techLevel < TechLevel.Industrial || !Competence.Knows(plan, Doctrine.SupportFire)) return;
            var people = pawns.Where(p => p.RaceProps.Humanlike && !p.Downed && p.inventory != null && !p.WorkTagIsDisabled(WorkTags.Violent)).ToList();
            int n = people.Count >= 16 ? 2 : people.Count >= 8 ? 1 : 0;
            if (n == 0) return;
            var guns = Candidates().Where(d => d.techLevel <= lord.faction.def.techLevel).ToList();
            if (guns.Count == 0) return;
            var pool = people.Where(p => !plan.breachTeam.Contains(p) && p != plan.leader
                                         && (!plan.roleOf.TryGetValue(p, out var r) || r == RaidRole.Rifleman || r == RaidRole.Support || r == RaidRole.Marksman))
                             .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0).ToList();
            // a re-plan (the Lab plans again after matching its defenders): the teams form again around the guns already carried
            foreach (var carrier in people.Where(p => PackedGun(p) != null).ToList())
            {
                if (plan.csw.Count >= n) break;
                var again = new CswTeam { gunner = carrier, turretDef = PackedGun(carrier).InnerThing.def };
                pool.Remove(carrier);
                foreach (var bearer in pool.OrderBy(x => x.Position.DistanceToSquared(carrier.Position)).Take(pool.Count >= 3 ? 2 : 1).ToList()) { again.bearers.Add(bearer); pool.Remove(bearer); }
                Place(lord, plan, again);
            }
            for (int i = plan.csw.Count; i < n && pool.Count >= 2; i++)
            {
                ThingDef def = guns[Mathf.FloorToInt(Competence.Hash01(plan.lordId, 0xC5 + i) * guns.Count) % guns.Count];
                var team = new CswTeam { gunner = pool[0], turretDef = def };
                pool.RemoveAt(0);
                int bearers = pool.Count >= 3 ? 2 : 1;
                for (int b = 0; b < bearers && pool.Count > 0; b++)
                {
                    var bearer = pool.OrderBy(x => x.Position.DistanceToSquared(team.gunner.Position)).First();
                    team.bearers.Add(bearer);
                    pool.Remove(bearer);
                }
                // the gun, packed
                Thing turret = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                Thing packed = turret.TryMakeMinified();
                if (packed == null || !team.gunner.inventory.innerContainer.TryAdd(packed)) continue;
                // three magazines of its ammo, shared by the bearers
                ThingDef ammo = Launchers.TurretAmmo(def.building.turretGunDef, out int mag);
                if (ammo != null)
                {
                    int total = Mathf.Clamp(Mathf.Max(1, mag) * 3, 30, 600);
                    foreach (var bearer in team.bearers)
                    {
                        Thing a = ThingMaker.MakeThing(ammo);
                        a.stackCount = Mathf.Max(1, total / team.bearers.Count);
                        bearer.inventory.innerContainer.TryAdd(a);
                    }
                }
                Place(lord, plan, team);
                Teams++;
                if (!Seen.Contains(def.defName)) Seen += (Seen.Length > 0 ? " " : "") + def.defName;
            }
            if (plan.csw.Count > 0) plan.capability += "; crew weapons: " + string.Join(", ", plan.csw.Select(t => t.turretDef.label));
        }

        private static MinifiedThing PackedGun(Pawn p) =>
            p.inventory?.innerContainer.OfType<MinifiedThing>().FirstOrDefault(m => m.InnerThing?.def.building?.turretGunDef != null);

        /// <summary>The team's post and its covered route there; the team joins the plan.</summary>
        private static void Place(Lord lord, AssaultPlan plan, CswTeam team)
        {
            var s = TacticalAIMod.Settings;
            team.post = Post(lord.Map, plan, team.turretDef, team.gunner);
            var tm = MapComponent_TacticalAI.For(lord.Map)?.Threat;
            if (team.post.IsValid)
                using (var customizer = new ThreatCustomizer(lord.Map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
                    if (ThreatPathing.TryFindRoute(lord.Map, team.gunner.Position, team.post, customizer, out var nodes, out _))
                        team.route.AddRange(ThreatPathing.Waypoints(nodes, 8));
            plan.csw.Add(team);
        }

        /// <summary>60-75 % of the gun's range from the defence, out of fire, a line of fire to it, in cover, reachable.</summary>
        private static IntVec3 Post(Map map, AssaultPlan plan, ThingDef def, Pawn gunner)
        {
            var s = TacticalAIMod.Settings;
            float range = def.building.turretGunDef.Verbs.FirstOrDefault()?.range ?? 40f;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;
            IntVec3 best = IntVec3.Invalid;
            float bs = float.MaxValue;
            float from = AssaultPlanner.Bearing(plan.objective, plan.gather);
            // what it can fire at: the defenders, or the base itself (its walls and buildings) - the centre of a walled base
            // is hidden behind its walls (ld2: no post found in the fortress or the village)
            var defs = Suppression.Defenders(map, plan.objective, 60f);
            var buildings = map.listerBuildings.allBuildingsColonist.Where(b => b.Spawned && b.Position.DistanceTo(plan.objective) <= 40f)
                               .OrderBy(b => b.thingIDNumber).Take(40).ToList();
            bool Sees(IntVec3 c) => GenSight.LineOfSight(c, plan.objective, map, true)
                                    || defs.Any(d => d.Position.DistanceTo(c) <= range && GenSight.LineOfSight(c, d.Position, map, true))
                                    || buildings.Any(b => b.Position.DistanceTo(c) <= range && GenSight.LineOfSight(c, b.Position, map, true));
            // first out of fire, then (nothing found) in light fire
            foreach (float maxFire in new[] { s.safeFire * 4f, 0.5f })
            {
                for (float frac = 0.55f; frac <= 0.8f; frac += 0.08f)
                    for (int a = -75; a <= 75; a += 15)
                    {
                        float rad = (from + a) * Mathf.Deg2Rad, r = Mathf.Min(range * frac, 55f);
                        var c = new IntVec3(plan.objective.x + Mathf.RoundToInt(Mathf.Cos(rad) * r), 0, plan.objective.z + Mathf.RoundToInt(Mathf.Sin(rad) * r));
                        if (!c.InBounds(map)) continue;
                        c = CellFinder.StandableCellNear(c, map, 3f, x => x.GetEdifice(map) == null && !plan.csw.Any(t => t.post.DistanceTo(x) < 8f));
                        if (!c.IsValid || (haveThreat && tm.FireAt(c) > maxFire)) continue;
                        if (!gunner.CanReach(c, PathEndMode.OnCell, Danger.Deadly) || !Sees(c)) continue;
                        float score = Mathf.Abs(a) * 0.05f + (JobGiver_HSKTAI_Support.HasCover(c, plan.objective, map) ? 0f : 6f) + (haveThreat ? tm.FireAt(c) * 20f : 0f);
                        if (score < bs) { bs = score; best = c; }
                    }
                if (best.IsValid) break;
            }
            return best;
        }

        public static CswTeam TeamOf(AssaultPlan plan, Pawn p) => plan?.csw.FirstOrDefault(t => !t.abandoned && t.Has(p));

        /// <summary>H-hour waits for the guns: a living team whose gun isn't firing yet (at most cswWaitTicks).</summary>
        public static bool Holding(AssaultPlan plan, int now)
        {
            if (plan.csw.Count == 0 || now - plan.createdTick > TacticalAIMod.Settings.cswWaitTicks) return false;
            bool hold = plan.csw.Any(t => !t.abandoned && t.post.IsValid && (!t.deployed || t.gun == null || !(t.gun.GetComp<CompMannable>()?.MannedNow ?? false)));
            if (hold) Waits++;
            return hold;
        }

        /// <summary>Every 60 ticks: set up the gun when the gunner is at his post; abandon it when the crew falls.</summary>
        public static void Tick(Map map, Lord lord, AssaultPlan plan, int now)
        {
            foreach (var t in plan.csw)
            {
                if (t.abandoned) continue;
                bool crewAlive = t.Crew.Any(c => c != null && c.Spawned && !c.Downed && !c.Dead && c.GetLord() == lord);
                if (!crewAlive || plan.phase == PlanPhase.Withdraw || plan.phase == PlanPhase.Converted) { Abandon(t); continue; }
                if (t.deployed)
                {
                    // an idle gun points where the trouble is (playtest 2026-10-25: "not facing the target"): the nearest
                    // colonist in range, else the base
                    if (t.gun != null && t.gun.Spawned && !t.gun.CurrentTarget.IsValid)
                    {
                        float range = t.gun.AttackVerb?.verbProps.range ?? 40f;
                        Pawn near = Suppression.Defenders(map, t.gun.Position, range).OrderBy(d => d.Position.DistanceToSquared(t.gun.Position)).FirstOrDefault();
                        Face(t.gun, near != null ? near.Position : plan.objective);
                    }
                    continue;
                }
                var g = t.gunner;
                if (g == null || !g.Spawned || g.Downed || g.Dead || !t.post.IsValid) continue;
                if (g.Position.DistanceTo(t.post) > 1.5f) { t.setupTick = -1; continue; }
                if (t.setupTick < 0) { t.setupTick = now; continue; }
                if (now - t.setupTick < 240) continue;
                Deploy(map, lord, plan, t);
            }
        }

        private static void Deploy(Map map, Lord lord, AssaultPlan plan, CswTeam t)
        {
            var inv = t.gunner.inventory.innerContainer;
            var packed = inv.OfType<MinifiedThing>().FirstOrDefault(m => m.InnerThing?.def == t.turretDef);
            if (packed == null) { t.abandoned = true; return; }
            IntVec3 cell = t.gunner.Position;
            if (cell.GetEdifice(map) != null) cell = CellFinder.StandableCellNear(cell, map, 2f, x => x.GetEdifice(map) == null);
            if (!cell.IsValid) return;
            Thing inner = packed.InnerThing;
            inv.Remove(packed);
            packed.InnerThing = null;
            packed.Destroy();
            // facing the base, so the gunner's place (the interaction cell) is behind the gun, not in front of the barrel
            // (playtest 2026-10-25: "the crew seems to stand in front of the barrel"; Bearing is 0 = east, AngleFlat 0 = north)
            Rot4 rot = Rot4.FromAngleFlat((plan.objective - cell).AngleFlat);
            inner.SetFaction(lord.faction);
            GenSpawn.Spawn(inner, cell, map, rot);
            t.gun = inner as Building_Turret;
            t.deployed = true;
            Deployed++;
            Face(t.gun, plan.objective);
            if (t.gun != null && t.gun.def.hasInteractionCell && t.gun.InteractionCell.DistanceTo(plan.objective) > t.gun.Position.DistanceTo(plan.objective)) CrewBehind++;
            // step off the gun's cell to man it
            if (t.gunner.Position == cell)
            {
                IntVec3 off = CellFinder.StandableCellNear(cell, map, 1.5f, x => x != cell);
                if (off.IsValid) t.gunner.Position = off;
            }
            lord.CurLordToil?.UpdateAllDuties();
        }

        /// <summary>Turn the gun's top toward a cell (vanilla and CE turrets keep a TurretTop in the field "top").</summary>
        public static void Face(Building_Turret gun, IntVec3 at)
        {
            try
            {
                if (gun == null || !at.IsValid) return;
                var fTop = HarmonyLib.AccessTools.Field(gun.GetType(), "top");   // per type: vanilla and CE turrets differ
                object top = fTop?.GetValue(gun);
                if (top == null) return;
                var pTopRot = HarmonyLib.AccessTools.Property(top.GetType(), "CurRotation");
                pTopRot?.SetValue(top, (at.ToVector3Shifted() - gun.DrawPos).AngleFlat());
                Faced++;
            }
            catch { }
        }

        private static void Abandon(CswTeam t)
        {
            t.abandoned = true;
            if (t.gun != null && t.gun.Spawned) { t.gun.SetFaction(null); Abandoned++; }
        }
    }

    /// <summary>Crew: to the post together, set up, the gunner mans it, the bearers stay by it (CE reloads from their packs).</summary>
    public class JobGiver_HSKTAI_CSW : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            var t = CrewWeapons.TeamOf(plan, pawn);
            if (t == null || !t.post.IsValid) return null;
            Map map = pawn.Map;
            if (t.deployed && t.gun != null && t.gun.Spawned)
            {
                if (pawn == t.gunner || !(t.gun.GetComp<CompMannable>()?.MannedNow ?? true))
                {
                    if (pawn.CurJobDef == JobDefOf.ManTurret && pawn.CurJob.targetA.Thing == t.gun) return null;
                    if (pawn.CanReserveAndReach(t.gun, PathEndMode.InteractionCell, Danger.Deadly)) return JobMaker.MakeJob(JobDefOf.ManTurret, t.gun);
                }
                // only the gunner at the gun (playtest 2026-10-25: "they are stacked, only the shooter needs to be there, the
                // others cover and replace crew if down"): the bearers spread out into cover beside and behind it, cover it, and
                // reload it from their packs (the CE node before this one) or take it over when it stands unmanned
                IntVec3 cp = CoverPost(pawn, t, t.gun.Position, plan.objective, map);
                if (cp.IsValid && pawn.Position.DistanceTo(cp) > 1.5f) return Go(cp, LocomotionUrgency.Jog);
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 180, true);
            }
            if (pawn == t.gunner)
            {
                if (pawn.Position.DistanceTo(t.post) <= 1.5f) return JobMaker.MakeJob(JobDefOf.Wait, 120);   // setting it up
                while (t.route.Count > 0 && pawn.Position.DistanceTo(t.route[0]) <= 4f) t.route.RemoveAt(0);
                IntVec3 next = t.route.Count > 0 ? t.route[0] : t.post;
                if (!pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly)) next = t.post;
                // keep the bearers close: wait for them if they fall behind - 15 s at most, then on alone (1.6 Lab: a
                // bearer with no covered spot stood still, the gunner waited for him and the gun was never set up)
                if (t.bearerWaitTicks < 900 && t.bearers.Any(b => b.Spawned && !b.Downed && b.Position.DistanceTo(pawn.Position) > 12f))
                {
                    t.bearerWaitTicks += 60;
                    return JobMaker.MakeJob(JobDefOf.Wait, 60);
                }
                return Go(next, LocomotionUrgency.Jog);
            }
            // bearer: keeps up with the gunner, spread out to his side and back, in cover where there is some - else just
            // close behind him
            var g = t.gunner;
            if (g == null || !g.Spawned || g.Downed) return null;
            IntVec3 bp = CoverPost(pawn, t, g.Position, plan.objective, map);
            if (!bp.IsValid && pawn.Position.DistanceTo(g.Position) > 5f)
                bp = CellFinder.StandableCellNear(g.Position, map, 4f, x => x != g.Position && x.DistanceTo(g.Position) >= 2f && pawn.CanReach(x, PathEndMode.OnCell, Danger.Deadly));
            if (bp.IsValid && pawn.Position.DistanceTo(bp) > 1.5f) return Go(bp, LocomotionUrgency.Jog);
            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 60, true);
        }

        public static int CoverPosts;
        private static readonly Dictionary<Pawn, (IntVec3 anchor, IntVec3 cell)> coverOf = new Dictionary<Pawn, (IntVec3, IntVec3)>();

        /// <summary>A bearer's spot: 3 back and 5 to his side of the gun (or gunner), 3.5-9 cells off, 2.5+ from the crew, in cover.</summary>
        private static IntVec3 CoverPost(Pawn bearer, CswTeam t, IntVec3 anchor, IntVec3 objective, Map map)
        {
            if (coverOf.TryGetValue(bearer, out var c) && c.cell.IsValid && c.anchor.DistanceTo(anchor) <= 4f) return c.cell;
            int idx = Mathf.Max(0, t.bearers.IndexOf(bearer));
            Vector3 back = (anchor - objective).ToVector3();
            back = back.sqrMagnitude < 1f ? Vector3.forward : back.normalized;
            Vector3 side = new Vector3(-back.z, 0f, back.x) * (idx % 2 == 0 ? 1f : -1f);
            IntVec3 want = (anchor.ToVector3() + back * 3f + side * 5f).ToIntVec3();
            IntVec3 gunCell = t.gun != null ? t.gun.Position : IntVec3.Invalid, inter = t.gun != null ? t.gun.InteractionCell : IntVec3.Invalid;
            IntVec3 cell = AssaultPlanner.CoverSpot(map, want, 3f, objective,
                x => x != gunCell && x != inter && x.DistanceTo(anchor) >= 3.5f && x.DistanceTo(anchor) <= 9f
                     && t.Crew.All(o => o == bearer || o == null || o.Position.DistanceTo(x) >= 2.5f), bearer.thingIDNumber);
            coverOf[bearer] = (anchor, cell);
            if (cell.IsValid) CoverPosts++;
            return cell;
        }

        private static Job Go(IntVec3 c, LocomotionUrgency u)
        {
            Job go = JobMaker.MakeJob(JobDefOf.Goto, c);
            go.locomotionUrgency = u;
            go.expiryInterval = 240;
            go.checkOverrideOnExpire = true;
            return go;
        }
    }
}
