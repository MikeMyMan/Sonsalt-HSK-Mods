using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>A queued throw: go to the covered throw position, then throw the item at the aim cell (CE OpportunisticAttack).</summary>
    public class ThrowOrder
    {
        public Thing item;
        public IntVec3 cell = IntVec3.Invalid;   // aim point (CE hand grenades can target locations)
        public IntVec3 from = IntVec3.Invalid;
        public bool smoke;
        public int issuedTick;
    }

    /// <summary>
    /// Grenades (playtest, 2026-10-23: "check if grenades etc can be thrown in proximity of pawns to either make them move
    /// or injure them. Check the use of Smoke Grenades too"; then "it should throw it into the breach without exposing
    /// itself. Grenades have an aoe and it should maximize it"):
    /// - kit: Industrial+ raids carry frags (about one raider in three, 2 each) and smoke (one in five); Neolithic and
    ///   Medieval raids stick bombs. Carried in the inventory, this also switches on CE's own grenade AI (opportunistic
    ///   frags at targets beyond 8 cells; smoke thrown on a suppressed comrade who calls for it).
    /// - frag out: when a breach or tunnel opens, up to two frags are thrown from cover beside the opening (never from in
    ///   front of it) at the cell where the blast catches the most defenders, plus one smoke between their guns and the
    ///   opening - before the rush.
    /// - smoke: screens withdrawals (a cloud between us and the pursuers).
    /// </summary>
    public static class Grenades
    {
        public static int FragsGiven, SmokesGiven, FragOrders, SmokeOrders;
        private static readonly HashSet<int> equippedLords = new HashSet<int>();

        private static JobDef throwJob;
        private static JobDef ThrowJob => throwJob ?? (throwJob = DefDatabase<JobDef>.GetNamedSilentFail("OpportunisticAttack"));

        public static int Lobs;

        /// <summary>
        /// Can a grenade get from here to there? A clear line of sight, or a lob over one wall (playtest: "throw over walls
        /// works for these grenades ... AI can try to throw grenades over walls"): at least 5 cells, the target not under a
        /// roof, at most 2 blocking cells (one wall's thickness) on the line, not right next to the thrower.
        /// </summary>
        public static bool CanLob(Map map, IntVec3 from, IntVec3 to)
        {
            if (GenSight.LineOfSight(from, to, map, true)) return true;
            if (!TacticalAIMod.Settings.lobOverWalls || from.DistanceTo(to) < 5f || to.Roofed(map) || from.Roofed(map)) return false;
            int blocked = 0;
            foreach (IntVec3 c in GenSight.PointsOnLineOfSight(from, to))
            {
                if (c == from || c == to) continue;
                if (c.Roofed(map)) return false;
                if (c.CanBeSeenOver(map)) continue;
                if (c.DistanceTo(from) < 1.5f) return false;
                if (++blocked > 2) return false;
            }
            return true;
        }

        public static bool IsSmoke(ThingDef d) => d.weaponTags != null && d.weaponTags.Contains("GrenadeSmoke");

        /// <summary>A thrown explosive (frag, HE, stick bomb, Odin hammer...): a one-use thrown weapon with a blast.</summary>
        /// <summary>A fragmentation / HE thrown explosive. Never gas or fire (playtest: "gas grenades do not breach walls").</summary>
        public static bool IsFrag(ThingDef d) =>
            d.IsRangedWeapon && !IsSmoke(d) && !GasRaid.IsTox(d) && !GasRaid.IsFire(d) && d.Verbs.Any(v => (v.verbClass?.Name.Contains("Throw") ?? false) || (v.verbClass?.Name.Contains("OneUse") ?? false))
            && d.Verbs.Any(v => (v.defaultProjectile?.projectile?.explosionRadius ?? 0f) > 0f && v.defaultProjectile.projectile.damageDef != null
                                 && v.defaultProjectile.projectile.damageDef.harmsHealth);

        public static Thing CarriedFrag(Pawn p) => p.inventory?.innerContainer?.FirstOrDefault(t => IsFrag(t.def));
        public static Thing CarriedSmoke(Pawn p) => p.inventory?.innerContainer?.FirstOrDefault(t => IsSmoke(t.def));

        public static void EquipOnce(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.grenadeKit || !s.equipRaiders || lord?.faction == null || !equippedLords.Add(lord.loadID)) return;
            TechLevel tech = lord.faction.def.techLevel;
            ThingDef frag = tech >= TechLevel.Industrial
                ? DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeFrag") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeHighExplosive")
                : DefDatabase<ThingDef>.GetNamedSilentFail("CE_Weapon_GrenadeStickBomb") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Norbhammar");
            ThingDef smoke = tech >= TechLevel.Industrial ? DefDatabase<ThingDef>.GetNamedSilentFail("CE_Weapon_GrenadeSmoke") : null;
            // riflemen and assault first, then breachers (mountain raids are mostly diggers); never base of fire or medic
            var fighters = pawns.Where(p => p.RaceProps.Humanlike && p.inventory != null
                                            && (!plan.roleOf.TryGetValue(p, out var r) || (r != RaidRole.Support && r != RaidRole.Marksman && r != RaidRole.Medic)))
                                .OrderBy(p => plan.roleOf.TryGetValue(p, out var r2) && r2 == RaidRole.Breacher ? 1 : 0)
                                .ToList();
            int nFrag = Mathf.CeilToInt(pawns.Count / 3f), nSmoke = Mathf.CeilToInt(pawns.Count / 5f);
            if (frag != null)
                foreach (var p in fighters.Take(nFrag))
                {
                    if (CarriedFrag(p) != null) continue;
                    Thing g = ThingMaker.MakeThing(frag);
                    g.stackCount = Mathf.Min(2, frag.stackLimit);
                    if (p.inventory.innerContainer.TryAdd(g)) FragsGiven += g.stackCount;
                }
            if (smoke != null)
                foreach (var p in fighters.Skip(nFrag).Concat(fighters).Distinct().Take(nSmoke))
                {
                    if (CarriedSmoke(p) != null) continue;
                    Thing g = ThingMaker.MakeThing(smoke);
                    if (p.inventory.innerContainer.TryAdd(g)) SmokesGiven++;
                }
        }

        public static int ShieldFragsGiven, ShieldThrows;
        private static readonly HashSet<Pawn> shieldKitted = new HashSet<Pawn>();

        /// <summary>The faction's thrown explosive: frag (Industrial+), else stick bomb.</summary>
        public static ThingDef FragFor(TechLevel tech) =>
            tech >= TechLevel.Industrial
                ? DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeFrag") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeHighExplosive")
                : DefDatabase<ThingDef>.GetNamedSilentFail("CE_Weapon_GrenadeStickBomb") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Norbhammar");

        /// <summary>
        /// Shield + grenades (playtest, 2026-10-23: "they move into range absorbing the hits and then throwing
        /// handgrenades"): every shield bearer carries `shieldFrags` thrown explosives (topped up once per bearer).
        /// </summary>
        public static void EquipShieldBearers(AssaultPlan plan)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.grenadeKit || !s.equipRaiders || s.shieldFrags <= 0) return;
            foreach (Pawn b in plan.followersOf.Keys)
            {
                if (b.inventory == null || b.Faction == null || !shieldKitted.Add(b)) continue;
                ThingDef frag = FragFor(b.Faction.def.techLevel);
                if (frag == null) continue;
                int have = b.inventory.innerContainer.Where(t => IsFrag(t.def)).Sum(t => t.stackCount);
                if (have >= s.shieldFrags) continue;
                Thing g = ThingMaker.MakeThing(frag);
                g.stackCount = Mathf.Min(s.shieldFrags - have, frag.stackLimit);
                if (b.inventory.innerContainer.TryAdd(g)) ShieldFragsGiven += g.stackCount;
            }
        }

        /// <summary>
        /// A shield bearer's throw: from where it stands (the shield is its cover; the throw is a CE crouch job), at the
        /// cell that catches the most defenders - one is enough - with none of our own pawns (its followers) in the blast.
        /// Every `shieldThrowTicks`.
        /// </summary>
        public static Job TryShieldFrag(Pawn p, AssaultPlan plan)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.grenadeKit || ThrowJob == null || plan == null) return null;
            int now = Find.TickManager.TicksGame;
            if (lastThrow.TryGetValue(p, out int last) && now - last < s.shieldThrowTicks) return null;
            Thing f = CarriedFrag(p);
            if (f == null) return null;
            float range = Range(f), r = Blast(f);
            var defs = Suppression.Defenders(p.Map, plan.objective, 60f).Where(d => d.Position.DistanceTo(p.Position) <= range + r).ToList();
            if (defs.Count == 0) return null;
            int cx = 0, cz = 0;
            foreach (var d in defs) { cx += d.Position.x; cz += d.Position.z; }
            IntVec3 aim = BestBlastCell(p.Map, defs, new IntVec3(cx / defs.Count, 0, cz / defs.Count), 6f, r, p.Faction, p.Position, range, out int hits);
            if (!aim.IsValid || hits < 1) return null;
            lastThrow[p] = now;
            if (!GenSight.LineOfSight(p.Position, aim, p.Map, true)) Lobs++;
            NoteFrag(plan, aim, r);
            Job t = JobMaker.MakeJob(ThrowJob, f, aim);
            t.maxNumStaticAttacks = 1;
            t.expiryInterval = 300;
            FragOrders++;
            ShieldThrows++;
            return t;
        }

        /// <summary>Throw range of the pawn's first carried frag (0 if none).</summary>
        public static float FragRange(Pawn p) { Thing f = CarriedFrag(p); return f != null ? Range(f) : 0f; }

        /// <summary>Range of the thrown item (90 %, for a safe cast).</summary>
        private static float Range(Thing item) => (item.def.Verbs.Where(v => !v.IsMeleeAttack).Select(v => v.range).DefaultIfEmpty(10f).Max()) * 0.9f;

        /// <summary>Blast radius of the thrown item (frag ~2, smoke ~6.5).</summary>
        private static float Blast(Thing item) =>
            Mathf.Max(item.def.projectile?.explosionRadius ?? 0f,
                      Mathf.Max(item.def.Verbs.Select(v => v.defaultProjectile?.projectile?.explosionRadius ?? 0f).DefaultIfEmpty(0f).Max(), 1.5f));

        /// <summary>
        /// A throw from cover: positions ≤ 5 cells from the opening that the defenders can't shoot at (threat map) and that
        /// are outside the opening's cone (the threat map may not know the fresh hole yet), with line of sight to the aim
        /// cell and within range. Nearest to the thrower.
        /// </summary>
        private static IntVec3 CoveredThrowPosition(Pawn p, IntVec3 aim, IntVec3 opening, IntVec3 objective, float range)
        {
            Map map = p.Map;
            var s = TacticalAIMod.Settings;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            IntVec3 best = IntVec3.Invalid;
            float bd = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(opening, 5f, true))
            {
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                if (c.DistanceTo(aim) > range || c.DistanceTo(aim) < 3f) continue;
                if (tm != null && tm.CompletedCycles > 0 && tm.FireAt(c) > s.safeFire) continue;
                if (c != opening && AssaultPlanner.InBreachCone(opening, objective, c)) continue;
                if (!CanLob(map, c, aim)) continue;
                float d = c.DistanceToSquared(p.Position);
                if (d >= bd || !p.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                bd = d;
                best = c;
            }
            return best;
        }

        /// <summary>
        /// The aim cell that puts the most defenders inside the blast (radius + 1 for scatter); ties: nearest the
        /// defenders' centre. Cells within 10 of the opening that are not walls.
        /// </summary>
        private static IntVec3 BestBlastCell(Map map, List<Pawn> defs, IntVec3 opening, float radius, Faction ours = null)
        {
            return BestBlastCell(map, defs, opening, 10f, radius, ours, IntVec3.Invalid, 0f, out _);
        }

        /// <summary>
        /// Blast-maximising aim around a point: the most defenders within blast radius + 1 (scatter), no pawn of our
        /// faction within that radius (no friendly fire); optionally only cells visible and in range from a thrower.
        /// </summary>
        private static IntVec3 BestBlastCell(Map map, List<Pawn> defs, IntVec3 around, float search, float radius, Faction ours,
                                             IntVec3 from, float range, out int hits)
        {
            hits = 0;
            IntVec3 best = IntVec3.Invalid;
            if (defs.Count == 0) return best;
            int cx = 0, cz = 0;
            foreach (var d in defs) { cx += d.Position.x; cz += d.Position.z; }
            IntVec3 centre = new IntVec3(cx / defs.Count, 0, cz / defs.Count);
            var friends = ours == null ? new List<Pawn>() : map.mapPawns.SpawnedPawnsInFaction(ours).Where(p => !p.Dead).ToList();
            float bestTie = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(around, search, true))
            {
                if (!c.InBounds(map) || c.Impassable(map)) continue;
                int n = defs.Count(d => d.Position.DistanceTo(c) <= radius + 1f);
                if (n == 0 || n < hits) continue;
                if (friends.Any(f => f.Position.DistanceTo(c) <= radius + 1f)) continue;
                if (from.IsValid && (c.DistanceTo(from) > range || c.DistanceTo(from) < radius + 2f || !CanLob(map, from, c))) continue;
                float tie = c.DistanceToSquared(centre);
                if (n > hits || (n == hits && tie < bestTie)) { hits = n; bestTie = tie; best = c; }
            }
            return best;
        }

        private static readonly Dictionary<Pawn, int> lastThrow = new Dictionary<Pawn, int>();

        /// <summary>
        /// In the fight: a raider with a frag throws it where the blast catches 2+ defenders, or 1 in cover, from where it
        /// stands (line of sight, in range, no comrade in the blast) - at most every 600 ticks per raider.
        /// </summary>
        public static Job TryOpportunisticFrag(Pawn p, AssaultPlan plan)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.grenadeKit || ThrowJob == null || plan == null) return null;
            int now = Find.TickManager.TicksGame;
            if (lastThrow.TryGetValue(p, out int last) && now - last < 600) return null;
            Thing f = CarriedFrag(p);
            if (f == null) return null;
            float range = Range(f), r = Blast(f);
            var defs = Suppression.Defenders(p.Map, plan.objective, 60f).Where(d => d.Position.DistanceTo(p.Position) <= range + r).ToList();
            if (defs.Count == 0) return null;
            int cx = 0, cz = 0;
            foreach (var d in defs) { cx += d.Position.x; cz += d.Position.z; }
            IntVec3 aim = BestBlastCell(p.Map, defs, new IntVec3(cx / defs.Count, 0, cz / defs.Count), 6f, r, p.Faction, p.Position, range, out int hits);
            if (!aim.IsValid) return null;
            bool inCover = defs.Any(d => d.Position.DistanceTo(aim) <= r + 1f && GenAdj.CellsAdjacent8Way(d).Any(c => c.InBounds(p.Map) && c.GetEdifice(p.Map) is Building b && b.def.fillPercent >= 0.4f));
            if (hits < 2 && !inCover) return null;
            lastThrow[p] = now;
            if (!GenSight.LineOfSight(p.Position, aim, p.Map, true)) Lobs++;
            Job t = JobMaker.MakeJob(ThrowJob, f, aim);
            NoteFrag(plan, aim, r);
            t.maxNumStaticAttacks = 1;
            t.expiryInterval = 300;
            FragOrders++;
            OpportunisticFrags++;
            return t;
        }

        /// <summary>
        /// Blow a door (or any building) with frags: from a covered position beside it with line of sight, throw at it;
        /// repeat until it is gone (one throw per raider every 180 ticks). Null if no covered position or on cooldown.
        /// </summary>
        public static Job ThrowAtBuilding(Pawn p, AssaultPlan plan, Building target)
        {
            if (ThrowJob == null || target == null || target.Destroyed) return null;
            Thing f = CarriedFrag(p);
            if (f == null) return null;
            int now = Find.TickManager.TicksGame;
            if (lastThrow.TryGetValue(p, out int last) && now - last < 180) return null;
            IntVec3 pos = CoveredThrowPosition(p, target.Position, target.Position, plan.objective, Range(f));
            if (!pos.IsValid) return null;
            if (p.Position != pos)
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, pos);
                go.locomotionUrgency = LocomotionUrgency.Jog;
                go.expiryInterval = 300;
                return go;
            }
            lastThrow[p] = now;
            NoteFrag(plan, target.Position, Blast(f));
            Job t = JobMaker.MakeJob(ThrowJob, f, target);
            t.maxNumStaticAttacks = 1;
            t.expiryInterval = 300;
            FragOrders++;
            DoorThrows++;
            return t;
        }

        public static int DoorThrows;

        public static int OpportunisticFrags;

        /// <summary>Note a frag in the air: the rush waits for it, and its blast area is a no-go zone for a few seconds.</summary>
        private static void NoteFrag(AssaultPlan plan, IntVec3 aim, float blast)
        {
            int now = Find.TickManager.TicksGame;
            plan.lastThrowTick = now;
            plan.dangerZones.RemoveAll(z => z.until < now);
            plan.dangerZones.Add((aim, blast + 1f, now + TacticalAIMod.Settings.fragDangerTicks));
        }

        /// <summary>A cell inside the blast area of one of our own frags that is about to go off (or just did).</summary>
        public static bool InDanger(AssaultPlan plan, IntVec3 c)
        {
            if (plan == null || plan.dangerZones.Count == 0 || !c.IsValid) return false;
            int now = Find.TickManager.TicksGame;
            foreach (var z in plan.dangerZones) if (z.until >= now && z.cell.DistanceTo(c) <= z.radius) return true;
            return false;
        }

        /// <summary>
        /// Frag out at an opening (breach hole, tunnel exit): up to two frags aimed where the blast catches the most
        /// defenders, and one smoke between their guns and the opening - each thrown from cover beside the opening,
        /// never from in front of it. No covered position with line of sight: no throw.
        /// </summary>
        public static void OrderAtOpening(Map map, AssaultPlan plan, IEnumerable<Pawn> group, IntVec3 opening)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.grenadeKit) return;
            GasRaid.OrderAtOpening(map, plan, group, opening);   // gas raid: tox (and fire) first
            var defs = Suppression.Defenders(map, plan.objective, 40f).Where(d => d.Position.DistanceTo(opening) <= 16f).ToList();
            if (defs.Count == 0) return;
            int frags = 0;
            bool smoked = !Competence.Knows(plan, Doctrine.Smoke);   // smoking the opening takes a sharp command
            var aimed = new List<IntVec3>();
            foreach (var p in group.Where(p => p.Spawned && !p.Downed && !plan.throwOrders.ContainsKey(p))
                                   .OrderBy(p => ShieldWall.IsBearer(plan, p) ? 0 : 1).ThenBy(p => p.Position.DistanceToSquared(opening)))   // the shield throws first, then leads in
            {
                Thing f = frags < 2 ? CarriedFrag(p) : null;
                Thing sm = !smoked ? CarriedSmoke(p) : null;
                Thing item = f ?? sm;
                if (item == null) continue;
                IntVec3 aim;
                if (f != null)
                {
                    // the second frag goes where the first blast doesn't reach
                    float r = Blast(item);
                    var left = defs.Where(d => aimed.All(a => d.Position.DistanceTo(a) > r + 1f)).ToList();
                    aim = BestBlastCell(map, left.Count > 0 ? left : defs, opening, r, p.Faction);
                }
                else
                {
                    // smoke: 3 cells inside the opening towards the defenders (screens the entry from their guns)
                    float dx = plan.objective.x - opening.x, dz = plan.objective.z - opening.z;
                    float len = Mathf.Max(1f, Mathf.Sqrt(dx * dx + dz * dz));
                    aim = new IntVec3(opening.x + Mathf.RoundToInt(dx / len * 3f), 0, opening.z + Mathf.RoundToInt(dz / len * 3f));
                }
                if (!aim.IsValid || !aim.InBounds(map)) continue;
                IntVec3 pos = CoveredThrowPosition(p, aim, opening, plan.objective, Range(item));
                if (!pos.IsValid) continue;
                plan.throwOrders[p] = new ThrowOrder { item = item, cell = aim, from = pos, smoke = f == null, issuedTick = Find.TickManager.TicksGame };
                aimed.Add(aim);
                if (f != null) frags++; else smoked = true;
                if (frags >= 2 && smoked) break;
            }
        }

        /// <summary>Queue a throw of item at aim from a covered position beside the opening. False if there is none.</summary>
        public static bool TryOrderCoveredThrow(Pawn p, AssaultPlan plan, Thing item, IntVec3 aim, IntVec3 opening, bool smoke)
        {
            if (item == null || !aim.IsValid) return false;
            IntVec3 pos = CoveredThrowPosition(p, aim, opening, plan.objective, Range(item));
            if (!pos.IsValid) return false;
            if (!GenSight.LineOfSight(pos, aim, p.Map, true)) Lobs++;
            plan.throwOrders[p] = new ThrowOrder { item = item, cell = aim, from = pos, smoke = smoke, issuedTick = Find.TickManager.TicksGame };
            return true;
        }

        public static int ScreenSmokes;

        /// <summary>
        /// Smoke screens over open ground: per open stretch, smoke at about a third and two thirds of it (or as far as a
        /// carrier can throw), from where the carriers stand. Returns the throws ordered.
        /// </summary>
        public static int OrderSmokeScreen(Map map, AssaultPlan plan, List<Pawn> group, List<List<IntVec3>> stretches)
        {
            int n = 0;
            var carriers = group.Where(p => p.Spawned && !p.Downed && CarriedSmoke(p) != null && !plan.throwOrders.ContainsKey(p)).ToList();
            foreach (var open in stretches.Where(o => o.Count > 0).OrderByDescending(o => o.Count))
            {
                foreach (float frac in new[] { 0.3f, 0.7f })
                {
                    IntVec3 want = open[Mathf.Min(open.Count - 1, (int)(open.Count * frac))];
                    Pawn p = carriers.OrderBy(c => c.Position.DistanceToSquared(want)).FirstOrDefault();
                    if (p == null) return n;
                    Thing sm = CarriedSmoke(p);
                    float range = Range(sm);
                    IntVec3 aim = want;
                    if (aim.DistanceTo(p.Position) > range)
                    {
                        // as far along the open stretch as it can throw
                        aim = open.Where(c => c.DistanceTo(p.Position) <= range).OrderByDescending(c => open.IndexOf(c)).FirstOrDefault();
                        if (!aim.IsValid) continue;
                    }
                    if (!CanLob(map, p.Position, aim)) continue;
                    plan.throwOrders[p] = new ThrowOrder { item = sm, cell = aim, from = p.Position, smoke = true, issuedTick = Find.TickManager.TicksGame };
                    carriers.Remove(p);
                    ScreenSmokes++;
                    n++;
                }
            }
            return n;
        }

        /// <summary>Withdrawal: smoke carriers screen the pursuit - a cloud between us and the defenders, from where we are.</summary>
        public static void OrderScreen(Map map, AssaultPlan plan, IEnumerable<Pawn> group)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.grenadeKit || !Competence.Knows(plan, Doctrine.Smoke)) return;
            var defs = Suppression.Defenders(map, plan.objective, 60f);
            if (defs.Count == 0) return;
            foreach (var p in group.Where(p => p.Spawned && !p.Downed && CarriedSmoke(p) != null))
            {
                Thing sm = CarriedSmoke(p);
                Pawn near = defs.OrderBy(d => d.Position.DistanceToSquared(p.Position)).First();
                float dist = p.Position.DistanceTo(near.Position);
                float t = Mathf.Min(Range(sm), dist * 0.4f) / Mathf.Max(1f, dist);
                var aim = new IntVec3(p.Position.x + Mathf.RoundToInt((near.Position.x - p.Position.x) * t), 0, p.Position.z + Mathf.RoundToInt((near.Position.z - p.Position.z) * t));
                if (!aim.InBounds(map) || !CanLob(map, p.Position, aim)) continue;
                plan.throwOrders[p] = new ThrowOrder { item = sm, cell = aim, from = p.Position, smoke = true, issuedTick = Find.TickManager.TicksGame };
            }
        }

        /// <summary>The next job for a pawn with a queued throw (go to the covered position, then throw at the cell).</summary>
        public static Job TryThrowJob(Pawn p, AssaultPlan plan)
        {
            if (plan == null || !plan.throwOrders.TryGetValue(p, out var o)) return null;
            if (ThrowJob == null || o.item == null || o.item.Destroyed || !p.inventory.innerContainer.Contains(o.item)
                || !o.cell.IsValid || Find.TickManager.TicksGame - o.issuedTick > 900)
            {
                plan.throwOrders.Remove(p);
                return null;
            }
            if (o.from.IsValid && p.Position != o.from)
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, o.from);
                go.locomotionUrgency = LocomotionUrgency.Sprint;
                go.expiryInterval = 300;
                return go;
            }
            plan.throwOrders.Remove(p);
            Job t = JobMaker.MakeJob(ThrowJob, o.item, o.cell);
            if (!o.smoke) NoteFrag(plan, o.cell, Blast(o.item));
            t.maxNumStaticAttacks = 1;
            t.expiryInterval = 300;
            if (o.smoke) SmokeOrders++; else FragOrders++;
            return t;
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>First node of every HSK Tactical AI duty: queued throws (frag out, smoke), then opportunistic frags.</summary>
    public class JobGiver_HSKTAI_Throw : Verse.AI.ThinkNode_JobGiver
    {
        protected override Verse.AI.Job TryGiveJob(Verse.Pawn pawn)
        {
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            if (plan == null) return null;
            return Grenades.TryThrowJob(pawn, plan) ?? Grenades.TryOpportunisticFrag(pawn, plan);
        }
    }
}
