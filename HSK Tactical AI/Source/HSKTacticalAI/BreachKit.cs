using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    [DefOf]
    public static class TAI_DefOf
    {
        public static ThingDef HSKTAI_BreachCharge;
        public static ThingDef HSKTAI_PlantedBreachCharge;
        public static ThingDef HSKTAI_MiningCharge;
        public static ThingDef HSKTAI_PlantedMiningCharge;
        public static JobDef HSKTAI_PlantBreachCharge;
        public static DutyDef HSKTAI_Approach;
        public static DutyDef HSKTAI_Assault;
        public static DutyDef HSKTAI_Breach;
        public static DutyDef HSKTAI_Support;
        public static DutyDef HSKTAI_Withdraw;
        public static DutyDef HSKTAI_Arson;
        public static DutyDef HSKTAI_SiegeGuard;
        public static DutyDef HSKTAI_Scout;
        public static DutyDef HSKTAI_Sabotage;
        public static JobDef HSKTAI_LobRound;
        public static DutyDef HSKTAI_CSW;
        static TAI_DefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(TAI_DefOf)); }
    }

    /// <summary>
    /// Breaching equipment: breaching raids bring the right tools for their tech level.
    /// Industrial+: breach charges planted against the wall. Neolithic/Medieval: breach tools for melee breachers.
    /// </summary>
    public static class BreachKit
    {
        public static int ChargesPlanted, ChargesGiven, ToolsGiven;
        private static readonly HashSet<int> equippedLords = new HashSet<int>();

        public static void EquipOnce(Lord lord, bool force = false)
        {
            var s = TacticalAIMod.Settings;
            // raiders keep the loadout they spawned with unless extra equipment is switched on (playtest: early-game raiders
            // turned up with grenades and explosives - breach methods follow tech level and the loadout instead)
            if (s == null || !s.breachKit || !s.equipRaiders || lord?.faction == null || lord.ownedPawns.Count == 0) return;
            if (lord.faction == Faction.OfPlayer) return;
            // a raid that can walk around (assault plan "walk") doesn't need breaching gear
            if (!force && AssaultPlanner.Plans.TryGetValue(lord.loadID, out var plan) && plan.mode != PlanMode.Breach) return;
            if (!equippedLords.Add(lord.loadID)) return;

            List<Pawn> pawns = lord.ownedPawns.Where(p => p.RaceProps.Humanlike && !p.Downed && p.inventory != null).ToList();
            if (pawns.Count == 0) return;
            int specialists = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(pawns.Count / 4f), 1, 4);
            TechLevel tech = lord.faction.def.techLevel;

            // prefer pawns without a gun (they would melee the wall anyway), then the toughest
            var order = pawns.OrderBy(p => p.equipment?.Primary != null && p.equipment.Primary.def.IsRangedWeapon ? 1 : 0)
                             .ThenByDescending(p => p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0).ToList();
            for (int i = 0; i < specialists && i < order.Count; i++) GiveKit(order[i], tech, s);
        }

        /// <summary>The breaching kit of a tech level: crude tools, then simple explosives, then charges (spacer: more, and a mining charge).</summary>
        private static string GiveKit(Pawn p, TechLevel tech, TacticalAISettings s)
        {
            if (tech >= TechLevel.Industrial)
            {
                int n = s.chargesPerSpecialist + (tech >= TechLevel.Spacer ? 2 : 0);
                Thing c = ThingMaker.MakeThing(TAI_DefOf.HSKTAI_BreachCharge);
                c.stackCount = n;
                if (!p.inventory.innerContainer.TryAdd(c)) return null;
                ChargesGiven += n;
                if (tech >= TechLevel.Spacer && TAI_DefOf.HSKTAI_MiningCharge != null)
                {
                    Thing m = ThingMaker.MakeThing(TAI_DefOf.HSKTAI_MiningCharge);
                    if (p.inventory.innerContainer.TryAdd(m)) MiningChargesGiven++;
                    return n + " breach charges + a mining charge";
                }
                return n + " breach charges";
            }
            if (tech >= TechLevel.Medieval)
            {
                // HSK medieval breachers carry stick bombs / Odin hammers (thrown, Bomb x4 vs walls); a bowman or crossbowman
                // (who keeps his gun) takes a breach axe instead and slings the bow
                string thrown = GiveThrown(p, "CE_Weapon_GrenadeStickBomb", "Norbhammar", s.thrownPerSpecialist);
                if (thrown != null) return thrown;
            }
            return GiveTool(p, "MeleeWeapon_BreachAxe", true);   // Demolish x7.5 vs walls, x10 vs doors; the bow goes in the pack (tl1: bows kept the kit to 1 axe)
        }

        public static int GearCarriers;
        private static readonly HashSet<int> meansChecked = new HashSet<int>();
        private static readonly Dictionary<int, string> gearOf = new Dictionary<int, string>();   // lord -> what it brought (a re-made plan keeps the note)

        /// <summary>Anything of its own that opens a wall: a charge, a wall-breaking launcher round, thrown explosives, a breach or digging tool.</summary>
        public static bool OwnMeans(Pawn p) =>
            CarriedCharge(p) != null || CarriedMiningCharge(p) != null || AssaultPlanner.DestructiveRangedVerb(p) != null || AssaultPlanner.HasBreachTool(p)
            || OwnMiningGain(p) >= 1.3f || Grenades.CarriedFrag(p) != null || (p.equipment?.Primary != null && Grenades.IsFrag(p.equipment.Primary.def));

        /// <summary>
        /// A raid out to breach brings the means (playtest 2026-10-25: "units would not engage a settlement without any means
        /// of breaching it ... at least one attacker needs to have it"): with nothing of its own, one raider per eight
        /// (1-3) carries the kit of the faction's tech level - on top of the loadout, independent of "extra equipment".
        /// </summary>
        public static void EnsureMeans(Lord lord, AssaultPlan plan, List<Pawn> pawns)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.breachGear || lord?.faction == null || plan == null || plan.goal != RaidGoal.Assault) return;
            if (equippedLords.Contains(lord.loadID)) return;   // the full kit (extra equipment) or already checked
            if (!meansChecked.Add(lord.loadID)) { if (gearOf.TryGetValue(lord.loadID, out string had)) plan.breachGear = had; return; }
            if (Find.TickManager.TicksGame - plan.createdTick > 120) return;   // at planning only: gear doesn't appear mid-fight
            var people = pawns.Where(p => p.RaceProps.Humanlike && !p.Downed && p.inventory != null && p.equipment != null && !p.WorkTagIsDisabled(WorkTags.Violent)).ToList();
            if (people.Count == 0) return;
            if (people.Any(OwnMeans)) { plan.breachGear = ""; return; }
            int n = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(people.Count / 8f), 1, 3);
            TechLevel tech = lord.faction.def.techLevel;
            var order = people.OrderBy(p => p.equipment.Primary != null && p.equipment.Primary.def.IsRangedWeapon ? 1 : 0)
                              .ThenByDescending(p => p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0).ToList();
            var given = new List<string>();
            for (int i = 0; i < n && i < order.Count; i++)
            {
                string what = GiveKit(order[i], tech, s);
                if (what == null) continue;
                GearCarriers++;
                given.Add(what);
            }
            plan.breachGear = given.Count == 0 ? "" : given.Count + " carrier" + (given.Count > 1 ? "s" : "") + " (" + string.Join("; ", given.Distinct()) + ")";
            gearOf[lord.loadID] = plan.breachGear;
        }

        public static int ThrownGiven;

        /// <summary>A thrown breaching weapon (stick bombs): one equipped, the rest in the pockets. Never replaces a gun.</summary>
        private static string GiveThrown(Pawn p, string defName, string fallback, int count)
        {
            if (p.equipment == null || p.inventory == null) return null;
            var primary = p.equipment.Primary;
            if (primary != null && primary.def.IsRangedWeapon) return null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName) ?? DefDatabase<ThingDef>.GetNamedSilentFail(fallback);
            if (def == null) return GiveTool(p, "MeleeWeapon_BreachAxe");
            if (primary != null) p.equipment.DestroyEquipment(primary);
            p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(def));
            if (count > 1 && def.stackLimit > 1)
            {
                Thing rest = ThingMaker.MakeThing(def);
                rest.stackCount = count - 1;
                p.inventory.innerContainer.TryAdd(rest);
            }
            ThrownGiven += count;
            return count + " " + def.label;
        }

        private static string GiveTool(Pawn p, string defName, bool stowGun = false)
        {
            if (p.equipment == null) return null;
            var primary = p.equipment.Primary;
            if (primary != null && primary.def.IsRangedWeapon)
            {
                // never take a gun away - except a tribal breacher, who stows the bow in the pack (it stays there)
                if (!stowGun || p.inventory == null || !p.equipment.TryTransferEquipmentToContainer(primary, p.inventory.innerContainer)) return null;
                primary = null;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName) ?? DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_BreachAxe");
            if (def == null) return null;
            if (primary != null) p.equipment.DestroyEquipment(primary);
            var tool = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            p.equipment.AddEquipment(tool);
            ToolsGiven++;
            return def.label;
        }

        public static int MiningToolsGiven;

        private static List<(ThingDef def, float gain)> miningTools;

        /// <summary>
        /// Every digging tool in the pack: equipped MiningSpeed offsets (HSK pickaxe +0.3, multitool +0.5, paxe +0.2)
        /// and Survival Tools Lite work-stat factors (e.g. jackhammer x2.0), as one speed gain (1 + offset) x factor.
        /// </summary>
        private static List<(ThingDef def, float gain)> MiningTools()
        {
            if (miningTools != null) return miningTools;
            miningTools = new List<(ThingDef, float)>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (d.category != ThingCategory.Item || d.destroyOnDrop) continue;
                float off = d.equippedStatOffsets?.FirstOrDefault(m => m.stat == StatDefOf.MiningSpeed)?.value ?? 0f;
                float fac = 1f;
                if (d.modExtensions != null)
                    foreach (var ext in d.modExtensions)
                    {
                        var f = ext.GetType().GetField("baseWorkStatFactors");
                        if (f?.GetValue(ext) is List<StatModifier> mods)
                            foreach (var m in mods) if (m.stat == StatDefOf.MiningSpeed) fac = Math.Max(fac, m.value);
                    }
                if (off <= 0f && fac <= 1f) continue;
                miningTools.Add((d, (1f + off) * fac));
            }
            return miningTools;
        }

        /// <summary>The best digging tool a raid of this tech level could carry (none: null).</summary>
        private static ThingDef MiningToolDef(TechLevel tech) =>
            MiningTools().Where(t => t.def.techLevel <= tech).OrderByDescending(t => t.gain).ThenBy(t => t.def.defName).Select(t => t.def).FirstOrDefault();

        /// <summary>Speed gain of the kit tool for a tech level (1 = bare hands).</summary>
        public static float MiningToolFactor(TechLevel tech)
        {
            ThingDef d = MiningToolDef(tech);
            return d == null ? 1f : MiningTools().First(t => t.def == d).gain;
        }

        /// <summary>Speed gain of the best digging tool the pawn already carries or holds (1 = none).</summary>
        public static float OwnMiningGain(Pawn p)
        {
            float best = 1f;
            foreach (var t in MiningTools())
                if (p.equipment?.Primary?.def == t.def || p.inventory?.innerContainer?.Any(x => x.def == t.def) == true) best = Math.Max(best, t.gain);
            return best;
        }

        /// <summary>A digging tool in the pockets (Survival Tools Lite applies tools carried in the inventory).</summary>
        public static void GiveMiningTool(Pawn p, TechLevel tech)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.breachKit || !s.equipRaiders || p.inventory == null) return;
            ThingDef def = MiningToolDef(tech);
            if (def == null || OwnMiningGain(p) >= MiningToolFactor(tech)) return;   // already has as good a tool
            Thing tool = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            // in hand if the miner has no gun to put away (Survival Tools Lite's auto-equip only runs for colonists): the
            // melee weapon goes to the pockets
            var primary = p.equipment?.Primary;
            if (p.equipment != null && tool is ThingWithComps twc && (primary == null || !primary.def.IsRangedWeapon))
            {
                if (primary != null) p.equipment.TryTransferEquipmentToContainer(primary, p.inventory.innerContainer);
                p.equipment.AddEquipment(twc);
                MiningToolsGiven++;
                return;
            }
            if (p.inventory.innerContainer.TryAdd(tool)) MiningToolsGiven++;
        }

        /// <summary>Sling the gun, take the digging tool from the pockets (miners with a gun).</summary>
        public static void EquipToolForDigging(Pawn p)
        {
            var prim = p.equipment?.Primary;
            if (p.inventory == null || prim == null || !prim.def.IsRangedWeapon) return;
            var tool = p.inventory.innerContainer.FirstOrDefault(t => MiningTools().Any(m => m.def == t.def)) as ThingWithComps;
            if (tool == null) return;
            p.equipment.TryTransferEquipmentToContainer(prim, p.inventory.innerContainer);
            p.inventory.innerContainer.Remove(tool);
            p.equipment.AddEquipment(tool);
        }

        /// <summary>Digging done: the gun back in hand, the tool to the pockets.</summary>
        public static void RestoreWeapon(Pawn p)
        {
            var prim = p.equipment?.Primary;
            if (p.inventory == null || prim == null || !MiningTools().Any(m => m.def == prim.def)) return;
            var gun = p.inventory.innerContainer.FirstOrDefault(t => t.def.IsRangedWeapon && t.def.equipmentType == EquipmentType.Primary) as ThingWithComps;
            if (gun == null) return;
            p.equipment.TryTransferEquipmentToContainer(prim, p.inventory.innerContainer);
            p.inventory.innerContainer.Remove(gun);
            p.equipment.AddEquipment(gun);
        }

        public static int SmokeDeployed, FiresStarted;

        /// <summary>
        /// SOSRA "obscure": the moment a breach opens, a breach team member near it throws smoke onto the hole and
        /// just inside it, so the defenders can't fire through the opening at the raiders coming in (Industrial+).
        /// </summary>
        public static void ObscureBreach(Map map, Lord lord, AssaultPlan plan, BreachPoint bp)
        {
            if (bp.smoked) return;
            bp.smoked = true;
            var s = TacticalAIMod.Settings;
            if (s == null || !s.breachKit || !s.smokeOnBreach || !bp.entry.IsValid || !Competence.Knows(plan, Doctrine.Smoke)) return;
            if (lord.faction == null || lord.faction.def.techLevel < TechLevel.Industrial) return;
            if (lord.ownedPawns.Any(p => plan.BreachFor(p) == bp && Grenades.CarriedSmoke(p) != null)) return;   // real smoke grenades are thrown instead
            if (!s.equipRaiders) return;   // no smoke out of nothing: only grenades the raiders carry
            Pawn thrower = lord.ownedPawns.FirstOrDefault(p => p.Spawned && !p.Downed && !p.Dead && p.Position.DistanceTo(bp.entry) <= 18f
                                                               && (bp.team.Contains(p) || plan.BreachFor(p) == bp));
            if (thrower == null) return;
            GasUtility.AddGas(bp.entry, map, GasType.BlindSmoke, s.smokeRadius);
            // second grenade a few cells inside, towards the defenders
            float dx = plan.objective.x - bp.entry.x, dz = plan.objective.z - bp.entry.z;
            float len = UnityEngine.Mathf.Sqrt(dx * dx + dz * dz);
            if (len > 1f)
            {
                var inside = new IntVec3(bp.entry.x + UnityEngine.Mathf.RoundToInt(dx / len * 3f), 0, bp.entry.z + UnityEngine.Mathf.RoundToInt(dz / len * 3f));
                if (inside.InBounds(map) && !inside.Impassable(map)) GasUtility.AddGas(inside, map, GasType.BlindSmoke, s.smokeRadius);
            }
            SmokeDeployed++;
        }

        /// <summary>A charge is armed at this breach point, or its wall is burning: keep clear and wait.</summary>
        public static bool BreachUnderWay(Map map, BreachPoint bp)
        {
            Building w = bp.CurrentWall();
            if (w == null) return false;
            if (ChargeAlreadyNear(map, w.Position)) return true;
            foreach (var b in bp.walls) if (b != null && !b.Destroyed && b.Spawned && b.IsBurning()) return true;
            return false;
        }

        public static Thing CarriedCharge(Pawn p) =>
            p.inventory?.innerContainer?.FirstOrDefault(t => t.def == TAI_DefOf.HSKTAI_BreachCharge);

        public static Thing CarriedMiningCharge(Pawn p) =>
            p.inventory?.innerContainer?.FirstOrDefault(t => t.def == TAI_DefOf.HSKTAI_MiningCharge);

        public static int MiningChargesGiven;

        /// <summary>Mining charges for a tunnel (Industrial+): one per two rock cells plus a spare, at most 6.</summary>
        public static bool GiveMiningCharges(Pawn p, int rockCells)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.breachKit || !s.equipRaiders || p.inventory == null || TAI_DefOf.HSKTAI_MiningCharge == null) return false;
            Thing c = ThingMaker.MakeThing(TAI_DefOf.HSKTAI_MiningCharge);
            c.stackCount = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(rockCells / 2f) + 1, 1, 6);
            if (!p.inventory.innerContainer.TryAdd(c)) return false;
            MiningChargesGiven += c.stackCount;
            return true;
        }

        public static bool ChargeAlreadyNear(Map map, IntVec3 c)
        {
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(c, map, 2.5f, true))
                if (t.def == TAI_DefOf.HSKTAI_PlantedBreachCharge || t.def == TAI_DefOf.HSKTAI_PlantedMiningCharge) return true;
            return false;
        }

        /// <summary>
        /// Planner version: a standable, reachable cell next to the wall on the raid's side (closest to
        /// <paramref name="prefer"/>, the route cell in front of the wall), avoiding cells under the defenders' fire.
        /// </summary>
        public static bool TryFindPlantSpot(Pawn pawn, Building target, IntVec3 prefer, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            if (!prefer.IsValid) return TryFindPlantSpot(pawn, target, out spot);
            Map map = pawn.Map;
            ThreatMap tm = MapComponent_TacticalAI.For(map)?.Threat;
            float best = float.MaxValue;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(target))
            {
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = c.DistanceToSquared(prefer) + (tm?.FireAt(c) ?? 0f) * 1000f;
                if (score < best) { best = score; spot = c; }
            }
            // the raid's side only: a spot far from the planned one is the far face of the wall
            return spot.IsValid && spot.DistanceTo(prefer) <= 2.5f;
        }

        /// <summary>A standable cell next to the wall, reachable, closest to the pawn.</summary>
        public static bool TryFindPlantSpot(Pawn pawn, Building target, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            float best = float.MaxValue;
            Map map = pawn.Map;
            foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(target))
            {
                if (!c.InBounds(map) || !c.Standable(map)) continue;
                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                float d = c.DistanceToSquared(pawn.Position);
                if (d < best) { best = d; spot = c; }
            }
            return spot.IsValid;
        }
    }

    /// <summary>Equip the raid when the breaching assault starts.</summary>
    [HarmonyPatch(typeof(LordToil_AssaultColonyBreaching), nameof(LordToil_AssaultColonyBreaching.UpdateAllDuties))]
    public static class Patch_Breaching_EquipKit
    {
        public static void Postfix(LordToil_AssaultColonyBreaching __instance)
        {
            try { BreachKit.EquipOnce(__instance.lord); }
            catch (System.Exception e) { Log.ErrorOnce("[HSK Tactical AI] breach kit failed: " + e, 0x48534B32); }
        }
    }

    /// <summary>A breacher about to hit a wall/door with a charge in its pockets plants the charge instead.</summary>
    [HarmonyPatch(typeof(JobGiver_AIBreaching), "TryGiveJob")]
    public static class Patch_AIBreaching_UseCharge
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            var s = TacticalAIMod.Settings;
            if (__result == null || s == null || !s.breachKit) return;
            if (__result.def != JobDefOf.AttackMelee && __result.def != JobDefOf.UseVerbOnThing) return;
            if (!(__result.targetA.Thing is Building b) || b.Destroyed) return;
            if (b.def.passability != Traversability.Impassable && !b.def.IsDoor) return;
            if (b.def.mineable) return;
            if (BreachKit.CarriedCharge(pawn) == null || BreachKit.ChargeAlreadyNear(pawn.Map, b.Position)) return;
            if (!BreachKit.TryFindPlantSpot(pawn, b, out IntVec3 spot)) return;
            Job plant = JobMaker.MakeJob(TAI_DefOf.HSKTAI_PlantBreachCharge, b, spot);
            plant.expiryInterval = 2000;
            __result = plant;
        }
    }

    public class JobDriver_PlantBreachCharge : JobDriver
    {
        private const int PlantTicks = 180;
        private IntVec3 Spot => job.targetB.Cell;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil plant = Toils_General.Wait(PlantTicks).WithProgressBarToilDelay(TargetIndex.B);
            plant.FailOnDespawnedOrNull(TargetIndex.A);
            yield return plant;
            yield return new Toil
            {
                initAction = () =>
                {
                    bool rock = job.targetA.Thing is Building tb && tb.def.mineable;
                    Thing carried = (rock ? BreachKit.CarriedMiningCharge(pawn) : null) ?? BreachKit.CarriedCharge(pawn);
                    if (carried == null) { EndJobWith(JobCondition.Incompletable); return; }
                    bool mining = carried.def == TAI_DefOf.HSKTAI_MiningCharge;
                    Thing one = carried.SplitOff(1);
                    one.Destroy();
                    Thing armed = ThingMaker.MakeThing(mining ? TAI_DefOf.HSKTAI_PlantedMiningCharge : TAI_DefOf.HSKTAI_PlantedBreachCharge);
                    armed.SetFaction(pawn.Faction);
                    GenSpawn.Spawn(armed, Spot, pawn.Map);
                    armed.TryGetComp<CompExplosive>()?.StartWick(pawn);
                    BreachKit.ChargesPlanted++;
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            // get clear of the blast
            Toil away = new Toil
            {
                initAction = () =>
                {
                    Map map = pawn.Map;
                    IntVec3 from = Spot;
                    if (CellFinder.TryFindRandomCellNear(from, map, 9, c => c.Standable(map) && c.DistanceTo(from) >= 5f && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out IntVec3 dest))
                        pawn.pather.StartPath(dest, PathEndMode.OnCell);
                    else EndJobWith(JobCondition.Succeeded);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            yield return away;
        }
    }
}
