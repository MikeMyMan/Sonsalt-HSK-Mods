using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>One place to set on fire: a cluster of flammable colony buildings and/or crops.</summary>
    public class ArsonSite
    {
        public IntVec3 center, approach = IntVec3.Invalid;
        public readonly List<Thing> targets = new List<Thing>();
        public readonly List<IntVec3> route = new List<IntVec3>();
        public readonly List<IntVec3> lit = new List<IntVec3>();
        public bool done;
        public float bearing;
        public string Describe() => Mathf.RoundToInt(bearing) + "deg/" + targets.Count + (done ? " done" : "") + (lit.Count > 0 ? " lit" + lit.Count : "");
    }

    /// <summary>
    /// Arson (playtest, 2026-10-23: "set fire … when they attack in many places … burning down the colony also crops"):
    /// a few raiders split off at the start, slip round out of the defenders' fire and set wooden buildings and crop
    /// fields alight in several places at once - the native ignite ability pyromaniacs use - then rejoin. Fires spread
    /// and draw the defenders away from their positions before the assault.
    /// </summary>
    public static class Arson
    {
        public static int SitesPlanned, IgniteJobs;

        private static bool Flammable(Thing t) => t.GetStatValue(StatDefOf.Flammability) >= 0.5f;

        /// <summary>Choose sites and arsonists for a plan (once, at planning).</summary>
        public static void Plan(Map map, AssaultPlan plan, List<Pawn> pawns, int wantOverride = -1, bool anySize = false)
        {
            var s = TacticalAIMod.Settings;
            plan.arsonSites.Clear();
            plan.arsonistOf.Clear();
            if (!s.arson || (!anySize && pawns.Count < s.arsonMinRaid)) return;
            if (map.weatherManager.RainRate > 0.3f) return;   // rain puts it out
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            bool haveThreat = tm != null && tm.CompletedCycles > 0;

            // candidate targets: flammable colony buildings and crops in growing zones
            var targets = new List<Thing>();
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
                if (b.Spawned && Flammable(b) && !(b is Building_Turret)) targets.Add(b);
            foreach (Zone z in map.zoneManager.AllZones)
                if (z is Zone_Growing)
                    foreach (IntVec3 c in z.Cells)
                    {
                        Plant p = c.GetPlant(map);
                        if (p != null && p.Spawned && Flammable(p)) targets.Add(p);
                    }
            if (targets.Count == 0) return;

            // clusters around targets, scored by how much burns there; an approach cell out of fire next to them
            var walk = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
            var sites = new List<ArsonSite>();
            foreach (Thing t in targets.InRandomOrder().Take(200))
            {
                if (sites.Any(x => x.center.DistanceTo(t.Position) < 8f)) continue;
                var site = new ArsonSite { center = t.Position, bearing = AssaultPlanner.Bearing(plan.objective, t.Position) };
                site.targets.AddRange(targets.Where(o => o.Position.DistanceTo(t.Position) <= 5f));
                foreach (IntVec3 c in GenRadial.RadialCellsAround(t.Position, 4f, true))
                {
                    if (!c.InBounds(map) || !c.Standable(map)) continue;
                    if (haveThreat && tm.FireAt(c) > s.safeFire) continue;
                    if (!map.reachability.CanReach(plan.gather, c, PathEndMode.OnCell, walk)) continue;
                    site.approach = c;
                    break;
                }
                if (site.approach.IsValid) sites.Add(site);
            }
            int want = wantOverride > 0 ? wantOverride : Mathf.Min(s.arsonSites, Mathf.Max(1, pawns.Count / 5));
            // many places at once: big clusters, far from the defenders (a diversion), different directions
            foreach (var site in sites.OrderByDescending(x => x.targets.Count + x.center.DistanceTo(plan.objective) * 0.2f))
            {
                if (plan.arsonSites.Count >= want) break;
                if (plan.arsonSites.Any(o => o.center.DistanceTo(site.center) < 15f)) continue;
                plan.arsonSites.Add(site);
            }
            if (plan.arsonSites.Count == 0) return;

            // arsonists: assault / rifle roles, not the breach team, base of fire, medic or leader
            var pool = pawns.Where(p => p.RaceProps.ToolUser && !p.RaceProps.IsMechanoid && p != plan.leader
                                        && (!plan.roleOf.TryGetValue(p, out var r) || r == RaidRole.Assault || r == RaidRole.Rifleman)).ToList();
            using (var customizer = new ThreatCustomizer(map, tm, s.routeCostPerFire, s.routeBaseCostInFire))
            {
                foreach (var site in plan.arsonSites.ToList())
                {
                    Pawn a = pool.OrderBy(p => p.Position.DistanceToSquared(site.approach)).FirstOrDefault();
                    if (a == null) { plan.arsonSites.Remove(site); continue; }
                    pool.Remove(a);
                    plan.arsonistOf[a] = site;
                    if (ThreatPathing.TryFindRoute(map, a.Position, site.approach, customizer, out var nodes, out _))
                        site.route.AddRange(ThreatPathing.Waypoints(nodes, 8));
                }
            }
            SitesPlanned += plan.arsonSites.Count;
        }

        public static bool IsActiveArsonist(AssaultPlan plan, Pawn p) =>
            plan.arsonistOf.TryGetValue(p, out var site) && !site.done;
    }

    /// <summary>Arsonist: covered route to the site, set up to arsonIgnitesPerSite fires spread over the cluster, rejoin.</summary>
    public class JobGiver_HSKTAI_Arson : ThinkNode_JobGiver
    {
        private static readonly JobGiver_AIFightEnemies fight = new JobGiver_AIFightEnemies();

        protected override Job TryGiveJob(Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null || !plan.arsonistOf.TryGetValue(pawn, out var site) || site.done) return null;
            var s = TacticalAIMod.Settings;
            Map map = pawn.Map;
            if (Find.TickManager.TicksGame - pawn.mindState.lastHarmTick < 240)
            {
                Job j = fight.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
                if (j != null && j.def != JobDefOf.Goto) return j;
            }
            // on the way
            if (pawn.Position.DistanceTo(site.center) > 6f)
            {
                IntVec3 next = site.approach;
                while (site.route.Count > 0 && pawn.Position.DistanceTo(site.route[0]) <= 4f) site.route.RemoveAt(0);
                if (site.route.Count > 0) next = site.route[0];
                if (!pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly)) next = site.approach;
                Job go = JobMaker.MakeJob(JobDefOf.Goto, next);
                go.locomotionUrgency = LocomotionUrgency.Jog;
                go.expiryInterval = 400;
                go.checkOverrideOnExpire = true;
                return go;
            }
            // at the site: light fires spread out (≥ 3 cells apart), unburned targets, reachable
            if (site.lit.Count >= s.arsonIgnitesPerSite) { Done(plan, pawn, site); return null; }
            Thing target = site.targets
                .Where(t => t != null && t.Spawned && !t.Destroyed && !t.IsBurning() && site.lit.All(l => l.DistanceTo(t.Position) >= 3f)
                            && pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            if (target == null) { Done(plan, pawn, site); return null; }
            site.lit.Add(target.Position);
            Arson.IgniteJobs++;
            Job ignite = JobMaker.MakeJob(JobDefOf.Ignite, target);
            ignite.expiryInterval = 600;
            return ignite;
        }

        private static void Done(AssaultPlan plan, Pawn pawn, ArsonSite site)
        {
            site.done = true;
            pawn.GetLord()?.CurLordToil?.UpdateAllDuties();   // back to the group
        }
    }
}
