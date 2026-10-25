using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// AI Lab only: defenders that act instead of standing still (playtest, 2026-10-25: "make the defenders also move
    /// around and patrol etc"; "check what happens when the defenders leave the base to fight outside or move into the
    /// door"). Config `defenderMode`:
    /// - hold (default): fixed spots, as before;
    /// - patrol: each defender walks a loop of 3 spots near his own, inside the same room;
    /// - sally: once the raid is within 45 cells, the defenders go out to `sallyRange` cells of the nearest raider and fight there;
    /// - door: two defenders hold the door, and re-man any gap in the walls (inside, 2 cells back) as soon as it opens;
    /// - lone: one defender works alone outside, 20 cells out, walking between two spots;
    /// - mix: patrol + one lone worker.
    /// Orders are drafted, ordered goto jobs; drafted colonists shoot on their own.
    /// </summary>
    public static class LabDefenders
    {
        public static int Moves, SallyMoves, DoorMoves, LoneMoves, GapsManned;
        private static readonly Dictionary<Pawn, List<IntVec3>> loopOf = new Dictionary<Pawn, List<IntVec3>>();
        private static readonly Dictionary<Pawn, int> loopIdx = new Dictionary<Pawn, int>();
        private static readonly List<IntVec3> wallCells = new List<IntVec3>();
        private static Pawn lone;
        private static bool sallying;

        public static string Mode => AILab.Config?.defenderMode ?? "hold";

        /// <summary>At raid start: loops, the lone worker, the wall ring.</summary>
        public static void Init(Map map, IntVec3 center, List<Pawn> defenders)
        {
            string m = Mode;
            wallCells.Clear();
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
                if (b.def == ThingDefOf.Wall || b.def.IsDoor) wallCells.Add(b.Position);
            if (m == "patrol" || m == "mix")
                foreach (var d in defenders)
                {
                    var room = d.Position.GetRoom(map);
                    var loop = new List<IntVec3> { d.Position };
                    for (int i = 0; i < 2; i++)
                    {
                        IntVec3 c = CellFinder.RandomClosewalkCellNear(d.Position, map, 6, x => x.GetRoom(map) == room && !loop.Contains(x));
                        if (c.IsValid) loop.Add(c);
                    }
                    loopOf[d] = loop;
                    loopIdx[d] = 0;
                }
            if ((m == "lone" || m == "mix") && defenders.Count > 1)
            {
                lone = defenders.Last();
                var spots = new List<IntVec3>();
                foreach (var dir in new[] { new IntVec3(0, 0, 20), new IntVec3(-20, 0, 6) })
                {
                    IntVec3 c = CellFinder.StandableCellNear(center + dir, map, 4f, x => !x.Roofed(map));
                    if (c.IsValid) spots.Add(c);
                }
                if (spots.Count > 0)
                {
                    loopOf[lone] = spots;
                    loopIdx[lone] = 0;
                    Order(lone, spots[0]);
                    LoneMoves++;
                }
            }
        }

        /// <summary>Every 60 ticks while the raid runs.</summary>
        public static void Tick(Map map, IntVec3 center, List<Pawn> defenders, List<Pawn> raiders, int now)
        {
            string m = Mode;
            if (m == "hold" || string.IsNullOrEmpty(m)) return;
            var active = raiders.Where(r => r.Spawned && !r.Downed && !r.Dead).ToList();
            foreach (var d in defenders)
            {
                if (!d.Spawned || d.Downed || d.Dead) continue;
                if (d == lone) { Loop(d, now, 420, ref LoneMoves); continue; }
                switch (m)
                {
                    case "patrol":
                    case "mix":
                        Loop(d, now, 300, ref Moves);
                        break;
                    case "sally":
                        Sally(d, center, active, now);
                        break;
                    case "door":
                        Door(map, d, center, defenders, active, now);
                        break;
                }
            }
        }

        private static void Loop(Pawn d, int now, int every, ref int counter)
        {
            if (!loopOf.TryGetValue(d, out var loop) || loop.Count < 2 || now % every >= 60) return;
            int i = (loopIdx[d] + 1) % loop.Count;
            loopIdx[d] = i;
            Order(d, loop[i]);
            counter++;
        }

        private static void Sally(Pawn d, IntVec3 center, List<Pawn> raiders, int now)
        {
            if (raiders.Count == 0) return;
            Pawn near = raiders.OrderBy(r => r.Position.DistanceToSquared(center)).First();
            if (!sallying && near.Position.DistanceTo(center) > 45f) return;
            sallying = true;
            if (now % 120 >= 60) return;
            Pawn t = raiders.OrderBy(r => r.Position.DistanceToSquared(d.Position)).First();
            float range = AILab.Config.sallyRange;
            if (t.Position.DistanceTo(d.Position) <= range + 1.5f) return;   // close enough: fight
            Vector3 dir = (d.Position - t.Position).ToVector3().normalized;
            IntVec3 want = (t.Position.ToVector3() + dir * range).ToIntVec3();
            IntVec3 c = CellFinder.StandableCellNear(want, d.Map, 3f, x => d.CanReach(x, PathEndMode.OnCell, Danger.Deadly));
            if (!c.IsValid) return;
            Order(d, c);
            SallyMoves++;
        }

        /// <summary>Two defenders: the door, or the newest gap in the walls nearest the raid (2 cells inside).</summary>
        private static void Door(Map map, Pawn d, IntVec3 center, List<Pawn> defenders, List<Pawn> raiders, int now)
        {
            if (now % 120 >= 60) return;
            int idx = defenders.IndexOf(d);
            if (idx > 1) return;
            var gaps = wallCells.Where(c => c.GetEdifice(map) == null).ToList();
            IntVec3 target;
            if (gaps.Count > 0 && raiders.Count > 0)
            {
                var rc = raiders[0].Position;
                target = gaps.OrderBy(g => g.DistanceToSquared(rc)).First();
                GapsManned = Mathf.Max(GapsManned, 1);
            }
            else
            {
                var door = wallCells.FirstOrDefault(c => c.GetEdifice(map)?.def.IsDoor == true);
                if (!door.IsValid) return;
                target = door;
            }
            Vector3 inward = (center - target).ToVector3().normalized;
            IntVec3 want = (target.ToVector3() + inward * 2f).ToIntVec3() + (idx == 0 ? IntVec3.Zero : new IntVec3(Mathf.RoundToInt(-inward.z), 0, Mathf.RoundToInt(inward.x)));
            IntVec3 c = CellFinder.StandableCellNear(want, map, 2f, x => d.CanReach(x, PathEndMode.OnCell, Danger.Deadly));
            if (!c.IsValid || d.Position.DistanceTo(c) <= 1.5f) return;
            Order(d, c);
            DoorMoves++;
        }

        private static void Order(Pawn d, IntVec3 c)
        {
            if (d.drafter != null && !d.drafter.Drafted) d.drafter.Drafted = true;
            Job go = JobMaker.MakeJob(JobDefOf.Goto, c);
            go.locomotionUrgency = LocomotionUrgency.Jog;
            d.jobs?.TryTakeOrderedJob(go, JobTag.DraftedOrder);
        }

        // ---------------- strip the raid's own breaching means (kit tests) ----------------

        public static int Stripped;

        /// <summary>
        /// Lab `stripBreachMeans`: before planning, take away whatever of their own opens a wall (charges, frags,
        /// breach and digging tools, wall-breaking launchers). A launcher or tool is replaced by a plain ranged weapon of
        /// the faction's tech level, so the raid must bring the kit (BreachKit.EnsureMeans).
        /// </summary>
        private static readonly HashSet<Pawn> strippedPawns = new HashSet<Pawn>();

        public static void StripBreachMeans(List<Pawn> pawns)
        {
            // once per raider: the Lab plans again after matching the defenders, and must not take away the kit the first plan gave
            if (pawns.Any(p => strippedPawns.Contains(p))) return;
            foreach (var p in pawns) strippedPawns.Add(p);
            foreach (var p in pawns)
            {
                if (!p.RaceProps.Humanlike) continue;
                var inv = p.inventory?.innerContainer;
                if (inv != null)
                    foreach (Thing t in inv.ToList())
                        if (t.def == TAI_DefOf.HSKTAI_BreachCharge || t.def == TAI_DefOf.HSKTAI_MiningCharge || Grenades.IsFrag(t.def)) { inv.Remove(t); t.Destroy(); Stripped++; }
                var prim = p.equipment?.Primary;
                if (prim == null) continue;
                bool breaker = AssaultPlanner.DestructiveRangedVerb(p) != null || AssaultPlanner.HasBreachTool(p) || Grenades.IsFrag(prim.def) || BreachKit.OwnMiningGain(p) >= 1.3f;
                if (!breaker) continue;
                p.equipment.DestroyEquipment(prim);
                Stripped++;
                TechLevel tech = p.Faction?.def.techLevel ?? TechLevel.Industrial;
                ThingDef plain = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsRangedWeapon && d.equipmentType == EquipmentType.Primary && !d.destroyOnDrop && d.techLevel == tech
                                && d.Verbs.All(v => !v.ai_IsBuildingDestroyer && (v.defaultProjectile?.projectile?.explosionRadius ?? 0f) <= 0f)
                                && (d.weaponTags == null || !d.weaponTags.Any(w => w.Contains("Grenade"))))
                    .OrderBy(d => d.defName).FirstOrDefault();
                if (plain == null) continue;
                var gun = (ThingWithComps)ThingMaker.MakeThing(plain, plain.MadeFromStuff ? GenStuff.DefaultStuffFor(plain) : null);
                p.equipment.AddEquipment(gun);
                GameComponent_AILab.GiveCEAmmo(p, gun, 120);
            }
        }
    }
}
