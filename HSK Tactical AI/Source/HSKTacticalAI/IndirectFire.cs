using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace HSKTacticalAI
{
    /// <summary>
    /// Indirect fire with grenade launchers (playtest, 2026-10-25: "M79 and alike weapons are great as indirect fire weapons
    /// to fire into the base at different places"). CE fires 40 mm rounds on a flat line, so a wall between the grenadier
    /// and the target stops them. A grenadier in the base of fire (or a siege camp) with an explosive round loaded and no
    /// direct shot lobs it high over the wall instead:
    /// - targets: defenders, then turrets, then other colony buildings, under open sky, in range (8 cells to 95 % of the
    ///   launcher's range), never within the blast of his own men, spread out (each impact makes that spot less
    ///   interesting for 15 s);
    /// - one real round from the magazine per shot, the round's own damage and blast; reload from the pack when empty;
    /// - scatter grows with distance and shrinks with Shooting skill (5 % - 15 % of the distance);
    /// - he must stand under open sky; a thin roof over the target takes the round (vanilla: it caves in there).
    /// A doctrine (IndirectFire): a dull command doesn't think of it.
    /// </summary>
    public static class IndirectFire
    {
        public static int Lobs, Reloads, NoTarget;
        private static readonly Dictionary<Pawn, int> nextLob = new Dictionary<Pawn, int>();
        private static readonly List<(IntVec3 c, int tick)> impacts = new List<(IntVec3, int)>();
        public static ThingDef LobbedDef => DefDatabase<ThingDef>.GetNamedSilentFail("HSKTAI_LobbedRound");

        public static bool Knows(Pawn p, AssaultPlan plan) =>
            TacticalAIMod.Settings.indirectFire && (plan != null ? Competence.Knows(plan, Doctrine.IndirectFire) : Competence.KnowsLord(p.GetLord(), Doctrine.IndirectFire));

        /// <summary>A reusable launcher with an explosive round loaded (or in the pack).</summary>
        public static Verb LobVerb(Pawn p)
        {
            Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null || v.IsMeleeAttack || (v.verbProps.verbClass?.Name.Contains("OneUse") ?? false)) return null;
            return Launchers.ExplosiveLauncher(p) ? v : null;
        }

        public static IntVec3 PickTarget(Pawn p, AssaultPlan plan, Verb v, out string what)
        {
            string label0 = null;
            Map map = p.Map;
            float range = v.verbProps.range * 0.95f, min = Mathf.Max(8f, v.verbProps.minRange + 1f);
            int now = Find.TickManager.TicksGame;
            impacts.RemoveAll(i => now - i.tick > 900);
            var raiders = p.GetLord()?.ownedPawns.Where(r => r.Spawned && !r.Downed).ToList() ?? new List<Pawn>();
            ThingDef proj = Launchers.CurrentProjectile(v);
            float blast = Mathf.Max(1.5f, proj?.projectile?.explosionRadius ?? 1.5f);
            IntVec3 best = IntVec3.Invalid;
            float bs = float.MaxValue;
            void Consider(IntVec3 c, float baseScore, string label)
            {
                float d = c.DistanceTo(p.Position);
                if (d < min || d > range || !c.InBounds(map) || c.Roofed(map)) return;
                if (raiders.Any(r => r.Position.DistanceTo(c) <= blast + d * 0.15f + 2f)) return;   // never on his own men (scatter included)
                float score = baseScore + d * 0.05f + impacts.Count(i => i.c.DistanceTo(c) <= 6f) * 8f;
                if (score >= bs) return;
                bs = score;
                best = c;
                label0 = label;
            }
            foreach (var d in Suppression.Defenders(map, p.Position, range))
            {
                if (v.CanHitTarget(d)) continue;   // a direct shot: the normal fire takes it
                Consider(d.Position, Targeting.Dangerous(d) ? -10f : 0f, "defender");
            }
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (!b.Spawned || b.Position.DistanceTo(p.Position) > range) continue;
                if (b is Building_Turret) { if (!v.CanHitTarget(b)) Consider(b.Position, 4f, "turret"); }
                else if (b.def.IsWorkTable || b.def.HasComp(typeof(CompPowerPlant)) || b.def.HasComp(typeof(CompPowerBattery)))
                    Consider(b.Position, 14f, "building");
            }
            what = label0;
            return best;
        }

        /// <summary>Launch: one round from the magazine, scatter by skill and distance, the lobbed round with the real round's blast.</summary>
        public static void Fire(Pawn p, IntVec3 aim)
        {
            Verb v = LobVerb(p);
            ThingDef ld = LobbedDef;
            if (v == null || ld == null || p.Map == null) return;
            ThingDef proj = Launchers.CurrentProjectile(v);
            if (!Launchers.BreachRound(proj) || !Launchers.ConsumeRound(v)) return;
            Map map = p.Map;
            float dist = aim.DistanceTo(p.Position);
            int skill = p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 5;
            float spread = dist * Mathf.Lerp(0.15f, 0.05f, skill / 20f);
            IntVec3 land = aim;
            for (int i = 0; i < 6; i++)
            {
                Vector2 o = Rand.InsideUnitCircle * spread;
                IntVec3 c = aim + new IntVec3(Mathf.RoundToInt(o.x), 0, Mathf.RoundToInt(o.y));
                if (c.InBounds(map)) { land = c; break; }
            }
            var round = (Projectile_Lobbed)GenSpawn.Spawn(ld, p.Position, map);
            round.CopyFrom(proj);
            round.Launch(p, p.DrawPos, land, land, ProjectileHitFlags.IntendedTarget, false, p.equipment?.Primary);
            impacts.Add((aim, Find.TickManager.TicksGame));
            Lobs++;
            if (v.verbProps.soundCast != null) v.verbProps.soundCast.PlayOneShot(new TargetInfo(p.Position, map));
        }

        public static void SetCooldown(Pawn p) => nextLob[p] = Find.TickManager.TicksGame + 150 + Rand.Range(0, 90);
        public static bool CoolingDown(Pawn p) => nextLob.TryGetValue(p, out int t) && Find.TickManager.TicksGame < t;
    }

    /// <summary>The lobbed round: flies over walls (flyOverhead), explodes with the loaded round's damage and blast.</summary>
    public class Projectile_Lobbed : Projectile
    {
        private DamageDef dmgDef;
        private int dmg = 20;
        private float radius = 1.5f, ap = -1f;
        private ThingDef shell;

        public void CopyFrom(ThingDef proj)
        {
            shell = proj;
            var pp = proj?.projectile;
            if (pp == null) return;
            dmgDef = pp.damageDef;
            radius = Mathf.Max(0.9f, pp.explosionRadius);
            try { dmg = pp.GetDamageAmount((Thing)null); } catch { }
            try { ap = pp.GetArmorPenetration((Thing)null); } catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref dmgDef, "dmgDef");
            Scribe_Defs.Look(ref shell, "shell");
            Scribe_Values.Look(ref dmg, "dmg", 20);
            Scribe_Values.Look(ref radius, "radius", 1.5f);
            Scribe_Values.Look(ref ap, "ap", -1f);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 c = Position;
            Destroy();
            if (map == null) return;
            GenExplosion.DoExplosion(c, map, radius, dmgDef ?? DamageDefOf.Bomb, launcher, damAmount: dmg, armorPenetration: ap,
                weapon: equipmentDef, projectile: shell ?? def, intendedTarget: intendedTarget.Thing);
        }
    }

    /// <summary>Aim at the cell (the verb's warm-up), lob, short cooldown.</summary>
    public class JobDriver_LobRound : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil aim = ToilMaker.MakeToil("HSKTAI_LobAim");
            aim.initAction = () => pawn.pather?.StopDead();
            aim.tickAction = () => pawn.rotationTracker.FaceCell(TargetA.Cell);
            aim.defaultCompleteMode = ToilCompleteMode.Delay;
            Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            aim.defaultDuration = Mathf.Max(45, Mathf.RoundToInt((v?.verbProps.warmupTime ?? 1f) * 60f) + 30);
            yield return aim;
            Toil fire = ToilMaker.MakeToil("HSKTAI_LobFire");
            fire.initAction = () => { IndirectFire.Fire(pawn, TargetA.Cell); IndirectFire.SetCooldown(pawn); };
            fire.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return fire;
            yield return Toils_General.Wait(40);
        }
    }

    /// <summary>Base of fire / siege camp: an explosive launcher, no direct shot at a target behind the walls: lob.</summary>
    public class JobGiver_HSKTAI_IndirectFire : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!TacticalAIMod.Settings.indirectFire || pawn.Map == null || !pawn.Map.IsPlayerHome) return null;
            AssaultPlan plan = AssaultPlanner.PlanOf(pawn);
            Lord lord = pawn.GetLord();
            bool siege = plan == null && lord?.CurLordToil is LordToil_Siege;
            if (plan == null && !siege) return null;
            if (plan != null && plan.phase != PlanPhase.Approach && plan.phase != PlanPhase.Assault) return null;
            // a breacher's launcher opens his wall first (the breach node); then he joins the shelling
            if (plan != null && plan.breachTeam.Contains(pawn) && plan.BreachFor(pawn) is BreachPoint bp && !bp.WallsGone && !bp.abandoned) return null;
            if (!IndirectFire.Knows(pawn, plan) || IndirectFire.CoolingDown(pawn)) return null;
            if (Find.TickManager.TicksGame - pawn.mindState.lastHarmTick < 120 || Suppression.State(pawn) >= 2) return null;   // under fire / hunkering
            if (pawn.Position.Roofed(pawn.Map)) return null;   // a lob needs open sky
            Verb v = IndirectFire.LobVerb(pawn);
            if (v == null) return null;
            int rounds = Launchers.LoadedRounds(v);
            if (rounds == 0 || !Launchers.BreachRound(Launchers.CurrentProjectile(v)))
            {
                Job r = Launchers.ReloadForBreach(pawn, v) ?? (rounds == 0 ? Launchers.ReloadJob(v) : null);
                if (r != null) IndirectFire.Reloads++;
                return r;
            }
            IntVec3 aim = IndirectFire.PickTarget(pawn, plan, v, out string what);
            if (!aim.IsValid) { IndirectFire.NoTarget++; IndirectFire.SetCooldown(pawn); return null; }
            Job j = JobMaker.MakeJob(TAI_DefOf.HSKTAI_LobRound, aim);
            j.expiryInterval = 400;
            return j;
        }
    }
}
