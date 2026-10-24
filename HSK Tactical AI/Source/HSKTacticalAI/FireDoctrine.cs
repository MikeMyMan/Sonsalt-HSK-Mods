using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Fire doctrine for raiders (playtest, 2026-10-23: "adjust the way attackers fire, snap shots vs aimed shots etc.
    /// depending on range and situation, also single fire vs burst vs auto use real life scenarios as a baseline, auto is
    /// more for suppressing and shorter range").
    ///
    /// Combat Extended picks an AI pawn's aim mode (aimed / snap / suppressive) and fire mode (single / burst / auto)
    /// in CompFireSelection.OptimizeModes, called after the cast has started (it applies to the next burst), and its
    /// default for most cases is aimed full auto. For raiders under an assault plan the doctrine below replaces it and
    /// is applied just before each cast (StartCastChecks), so it decides the burst being fired. Infantry baseline:
    /// - close quarters (≤ 8 cells): snap shots, full auto - room clearing, the breach, the shield push;
    /// - close (≤ 15): snap shots, short bursts;
    /// - medium (≤ 30 cells or half the weapon's effective range): aimed bursts (controlled pairs / 3-round bursts);
    /// - long: aimed single shots;
    /// - marksmen: aimed single shots at every range beyond close quarters;
    /// - machine guns (support with long bursts): aimed bursts; full auto only to suppress;
    /// - suppression (base of fire in its window, overwatch while the breach opens) at a defender in cover: suppressive
    ///   fire (CE ignores partial cover for it) - full auto for MGs, bursts for rifles;
    /// - suppressed ourselves: snap-shot bursts back (unaimed return fire);
    /// - shotguns: snap shots within 20 cells, else aimed single;
    /// - low ammo (under 1.5 magazines): one step down (auto -> burst, burst -> single beyond close range);
    /// - launchers, flares, AOE: CE's own choice.
    /// </summary>
    public static class FireDoctrine
    {
        public static readonly Dictionary<string, int> Decisions = new Dictionary<string, int>();
        /// <summary>Rounds per burst for a pawn whose gun has no burst mode but should fire short bursts (0 = none).</summary>
        public static readonly Dictionary<Pawn, int> burstCap = new Dictionary<Pawn, int>();
        public static int CappedBursts;
        public static int RaiderCasts, DefenderCasts;

        private static bool resolved;
        private static Type tFireModes, tAmmoUser, tAim, tFire, tProjPropsCE;
        private static PropertyInfo availAim, availFire, curAim, curFire, curMag, magsLeft, magSize;
        private static FieldInfo pelletCount;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            tFireModes = AccessTools.TypeByName("CombatExtended.CompFireModes");
            tAmmoUser = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
            tAim = AccessTools.TypeByName("CombatExtended.AimMode");
            tFire = AccessTools.TypeByName("CombatExtended.FireMode");
            tProjPropsCE = AccessTools.TypeByName("CombatExtended.ProjectilePropertiesCE");
            if (tFireModes != null)
            {
                availAim = AccessTools.Property(tFireModes, "AvailableAimModes");
                availFire = AccessTools.Property(tFireModes, "AvailableFireModes");
                curAim = AccessTools.Property(tFireModes, "CurrentAimMode");
                curFire = AccessTools.Property(tFireModes, "CurrentFireMode");
            }
            if (tAmmoUser != null)
            {
                curMag = AccessTools.Property(tAmmoUser, "CurMagCount");
                magsLeft = AccessTools.Property(tAmmoUser, "MagsLeftReadOnly") ?? AccessTools.Property(tAmmoUser, "MagsLeft");
                magSize = AccessTools.Property(tAmmoUser, "MagSize");
            }
            if (tProjPropsCE != null) pelletCount = AccessTools.Field(tProjPropsCE, "pelletCount");
        }

        public static bool Available
        {
            get { Resolve(); return curAim != null && curFire != null && availAim != null && availFire != null && tAim != null && tFire != null; }
        }

        private static ThingComp Comp(ThingWithComps t, Type type) =>
            type == null || t == null ? null : t.AllComps.FirstOrDefault(c => type.IsInstanceOfType(c));

        // AimMode: AimedShot 0, SuppressFire 1, Snapshot 2. FireMode: AutoFire 0, BurstFire 1, SingleFire 2.
        public const int Aimed = 0, Suppress = 1, Snap = 2;
        public const int Auto = 0, Burst = 1, Single = 2;

        /// <summary>Apply the doctrine for a raider about to fire at a target. False if CE's own choice should stand.</summary>
        public static bool Apply(Pawn p, Verb verb, LocalTargetInfo target)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.fireDoctrine) return Skip("off");
            if (!Available) return Skip("noCE");
            if (p == null || verb == null || !target.IsValid) return Skip("noTarget");
            AssaultPlan plan = AssaultPlanner.PlanOf(p);
            if (plan == null) return Skip("noPlan");
            ThingWithComps gun = verb.EquipmentSource;
            if (gun == null || verb.IsMeleeAttack) return Skip("noGun");
            ThingComp modes = Comp(gun, tFireModes);
            if (modes == null) return Skip("noFireModes");
            var proj = verb.verbProps.defaultProjectile?.projectile;
            if (proj == null) return Skip("noProjectile");
            if (proj.explosionRadius > 0f || gun.def.defName.Contains("Flare")) return Skip("launcher");   // launchers, flares: CE

            float d = target.Cell.DistanceTo(p.Position);
            float range = Mathf.Max(1f, verb.verbProps.range);
            int burstSize = verb.verbProps.burstShotCount;
            plan.roleOf.TryGetValue(p, out RaidRole role);
            bool marksman = role == RaidRole.Marksman;
            bool mg = role == RaidRole.Support && burstSize >= 8;
            int pellets = 1;
            if (pelletCount != null && tProjPropsCE != null && tProjPropsCE.IsInstanceOfType(proj))
                try { pellets = (int)pelletCount.GetValue(proj); } catch { }

            int aim;
            int[] fire;   // fire modes in order of preference (the first the weapon has is used)
            string why;
            int now = Find.TickManager.TicksGame;
            bool pinWindow = plan.suppressStartTick >= 0 && (plan.phase == PlanPhase.Approach || now - plan.hHourTick < s.supportTicks);
            // base of fire (MGs, support gunners, overwatch) pins a defender in cover whenever it engages; the pinning
            // window only matters for the rest
            bool suppressing = (RaidRoles.IsBaseOfFire(plan, p) && role != RaidRole.Marksman) || role == RaidRole.Support;   // snipers stay precise
            bool targetInCover = target.Thing is Pawn tp && InCover(tp, p.Position);

            if (pellets > 1)
            {
                if (d <= 20f) { aim = Snap; fire = new[] { Auto, Burst, Single }; why = "shotgun-close"; }
                else { aim = Aimed; fire = new[] { Single, Burst }; why = "shotgun-far"; }
            }
            else if (Suppression.State(p) >= 1)
            {
                // pinned ourselves: unaimed short bursts back (MGs: bursts, never single)
                aim = Snap; fire = mg ? new[] { Burst, Auto } : new[] { Burst, Auto, Single }; why = "suppressed";
            }
            else if (d <= s.fireCqbRange)
            {
                aim = Snap; fire = new[] { Auto, Burst, Single }; why = "cqb";
            }
            else if (suppressing && targetInCover)
            {
                // pin a defender in cover: MGs sustained auto, rifles bursts (auto if the rifle has no burst)
                aim = Suppress; fire = mg ? new[] { Auto, Burst } : new[] { Burst, Auto, Single }; why = mg ? "mg-suppress" : "suppress";
            }
            else if (marksman)
            {
                aim = Aimed; fire = new[] { Single, Burst }; why = "marksman";
            }
            else if (mg)
            {
                // MG at a point target: bursts (6-9 rounds), aimed beyond close range; auto only close in
                if (d <= s.fireCloseRange) { aim = Snap; fire = new[] { Auto, Burst }; why = "mg-close"; }
                else { aim = Aimed; fire = new[] { Burst, Auto }; why = "mg-burst" + (!pinWindow ? "[nowindow]" : !targetInCover ? "[open]" : ""); }
            }
            else if (d <= s.fireCloseRange)
            {
                aim = Snap; fire = new[] { Burst, Auto, Single }; why = "close";
            }
            else if (d <= Mathf.Min(s.fireMediumRange, range * 0.5f))
            {
                aim = Aimed; fire = new[] { Burst, Auto, Single }; why = "medium";   // no burst mode: auto capped to a 3-round burst
            }
            else
            {
                aim = Aimed; fire = new[] { Single, Burst, Auto }; why = "long";
            }

            // ammo discipline: under 1.5 magazines, one step down - MGs keep bursts, close quarters keeps its fire
            ThingComp ammo = Comp(gun, tAmmoUser);
            if (ammo != null && curMag != null && magsLeft != null && magSize != null && !why.Contains("suppress") && why != "cqb")
            {
                try
                {
                    int mag = Convert.ToInt32(magSize.GetValue(ammo));
                    int rounds = Convert.ToInt32(curMag.GetValue(ammo)) + Convert.ToInt32(magsLeft.GetValue(ammo)) * Mathf.Max(1, mag);
                    if (mag > 1 && rounds < mag * 1.5f)
                    {
                        if (mg) fire = new[] { Burst, Auto };
                        else if (fire[0] == Auto) fire = new[] { Burst, Single, Auto };
                        else if (fire[0] == Burst && d > s.fireCloseRange) fire = new[] { Single, Burst };
                        why += "-lowammo";
                    }
                }
                catch { }
            }

            int fireSet;
            try
            {
                aim = Set(modes, availAim, curAim, tAim, AimFallback[aim]);
                fireSet = Set(modes, availFire, curFire, tFire, fire);
            }
            catch (Exception e) { return Skip("setFailed:" + e.GetType().Name); }
            // short bursts on guns without a burst mode (nearly all HSK guns): full auto capped - MGs at range fire short
            // bursts (more hits per round, less ammo); rifles fire 3-round bursts. Suppression and close quarters: uncapped.
            int cap = 0;
            if (fireSet == Auto && fire[0] == Burst) cap = mg ? s.mgShortBurst : s.rifleShortBurst;
            if (cap > 0) burstCap[p] = cap; else burstCap.Remove(p);
            pending[p] = why + ":" + (aim == Aimed ? "aimed" : aim == Snap ? "snap" : "suppress") + "/" + (fireSet == Auto ? "auto" : fireSet == Burst ? "burst" : "single");
            return true;
        }

        public static readonly Dictionary<string, int> Skipped = new Dictionary<string, int>();

        private static bool Skip(string why)
        {
            Skipped.TryGetValue(why, out int n);
            Skipped[why] = n + 1;
            return false;
        }

        // when the weapon lacks a mode: aimed -> snap; suppressive -> snap -> aimed; snap -> aimed.
        // single -> burst -> auto; burst -> single -> auto (a bolt action has single only); auto -> burst -> single
        private static readonly int[][] AimFallback = { new[] { Aimed, Snap }, new[] { Suppress, Snap, Aimed }, new[] { Snap, Aimed } };

        /// <summary>Decision per pawn for the burst being started; counted only when the cast really starts.</summary>
        private static readonly Dictionary<Pawn, string> pending = new Dictionary<Pawn, string>();

        public static void CastStarted(Pawn p, bool started)
        {
            if (!pending.TryGetValue(p, out string key)) return;
            pending.Remove(p);
            if (!started) return;
            Decisions.TryGetValue(key, out int n);
            Decisions[key] = n + 1;
        }

        /// <summary>Set the first wanted mode the weapon has; returns the mode set (or the current one).</summary>
        private static int Set(ThingComp comp, PropertyInfo avail, PropertyInfo cur, Type enumType, int[] order)
        {
            var list = avail.GetValue(comp) as System.Collections.IList;
            foreach (int m in order)
            {
                object v = Enum.ToObject(enumType, (byte)m);
                if (list != null && !list.Contains(v)) continue;
                cur.SetValue(comp, v);
                return m;
            }
            return Convert.ToInt32(cur.GetValue(comp));
        }

        /// <summary>Rounds the pawn has left for its gun and its magazine size (CE); false without CE ammo.</summary>
        public static bool AmmoLeft(Pawn p, out int rounds, out int mag)
        {
            rounds = mag = 0;
            Resolve();
            ThingWithComps gun = p?.equipment?.Primary;
            ThingComp ammo = Comp(gun, tAmmoUser);
            if (ammo == null || curMag == null || magsLeft == null || magSize == null) return false;
            try
            {
                mag = Convert.ToInt32(magSize.GetValue(ammo));
                rounds = Convert.ToInt32(curMag.GetValue(ammo)) + Convert.ToInt32(magsLeft.GetValue(ammo)) * Mathf.Max(1, mag);
                return mag > 0;
            }
            catch { return false; }
        }

        /// <summary>A pawn behind something that stops bullets (fill ≥ 0.4) on the shooter's side.</summary>
        public static bool InCover(Pawn t, IntVec3 from)
        {
            Map map = t.Map;
            if (map == null) return false;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(t))
            {
                if (!c.InBounds(map) || c.DistanceToSquared(from) >= t.Position.DistanceToSquared(from)) continue;
                if (c.GetCover(map) is Thing cv && cv.def.fillPercent >= 0.4f) return true;
            }
            return false;
        }

        /// <summary>Counts per aim / fire mode family for the Lab.</summary>
        public static int Count(Func<string, bool> match) => Decisions.Where(kv => match(kv.Key)).Sum(kv => kv.Value);
    }

    /// <summary>
    /// Just before a pawn starts a burst (vanilla Verb.TryStartCastOn; CE's own tactics checks are skipped during
    /// tactical jobs, so this is the one hook every shot passes): raiders under a plan get the doctrine for this burst.
    /// Started bursts are counted per side.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_FireDoctrine_StartCast
    {
        public static void Prefix(Verb __instance, LocalTargetInfo castTarg)
        {
            Pawn p = __instance?.CasterPawn;
            if (p?.Faction == null || p.Faction == Faction.OfPlayer || __instance.IsMeleeAttack) return;
            long t0 = Perf.Start();
            FireDoctrine.Apply(p, __instance, castTarg);
            Perf.Stop("ce.fireDoctrine", t0);
        }

        public static void Postfix(Verb __instance, bool __result)
        {
            Pawn p = __instance?.CasterPawn;
            if (p != null) FireDoctrine.CastStarted(p, __result);
            if (!__result || p?.Faction == null || __instance.IsMeleeAttack) return;
            if (p.Faction == Faction.OfPlayer) FireDoctrine.DefenderCasts++;
            else if (p.Faction.HostileTo(Faction.OfPlayer))
            {
                FireDoctrine.RaiderCasts++;
                AssaultPlan plan = AssaultPlanner.PlanOf(p);
                if (plan != null) plan.lastShotTick = Find.TickManager.TicksGame;
            }
        }
    }

    /// <summary>CE's own after-cast choice would overwrite the doctrine for the next burst: skipped for planned raiders.</summary>
    [HarmonyPatch]
    public static class Patch_FireDoctrine_Optimize
    {
        public static bool Prepare() => AccessTools.TypeByName("CombatExtended.CompFireSelection") != null;

        public static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("CombatExtended.CompFireSelection"), "OptimizeModes");

        public static bool Prefix(Verb verb)
        {
            var s = TacticalAIMod.Settings;
            Pawn p = verb?.CasterPawn;
            return s == null || !s.fireDoctrine || !FireDoctrine.Available || p == null || AssaultPlanner.PlanOf(p) == null;
        }
    }

    /// <summary>Short bursts: caps a full-auto burst for raiders the doctrine wants on short bursts (see FireDoctrine.burstCap).</summary>
    [HarmonyPatch]
    public static class Patch_ShortBursts
    {
        private static Type tFire;

        public static bool Prepare()
        {
            tFire = AccessTools.TypeByName("CombatExtended.FireMode");
            return AccessTools.TypeByName("CombatExtended.Verb_ShootCE") != null && tFire != null;
        }

        public static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("CombatExtended.Verb_ShootCE"), "ShotsPerBurstFor");

        public static void Postfix(Verb __instance, object mode, ref int __result)
        {
            Pawn p = __instance?.CasterPawn;
            if (p == null || Convert.ToInt32(mode) != FireDoctrine.Auto || !FireDoctrine.burstCap.TryGetValue(p, out int cap) || cap <= 0) return;
            if (__result > cap) { __result = cap; FireDoctrine.CappedBursts++; }
        }
    }
}
