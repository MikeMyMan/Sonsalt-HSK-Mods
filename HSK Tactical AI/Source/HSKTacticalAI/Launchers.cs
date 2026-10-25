using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>
    /// Area weapons used where they work (playtest, 2026-10-25: "they used gas grenades outside with no effect, the M79 is
    /// good for breaching and shooting into funnels and doorways as it has aoe it can destroy doors").
    /// - Gas and other non-lethal area grenades (tox, tear gas, healing gas, insecticide, firefoam, lubricant): a raider
    ///   under a plan doesn't use them unless the plan is a gas raid against an enclosed, roofed room (GasRaid) - out in the
    ///   open the cloud drifts off. Smoke stays (the planner throws it on purpose: screens, breaches, funnels).
    /// - Grenade and rocket launchers that destroy buildings: doors count as breach points (AssaultPlanner), and inside the
    ///   fight a launcher fires at the door of a funnel when the defender behind it can't be seen.
    /// </summary>
    public static class Launchers
    {
        public static int GasHeld, FunnelDoorShots;
        private static readonly Dictionary<ThingDef, bool> gasCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<Pawn, int> lastOrder = new Dictionary<Pawn, int>();

        /// <summary>A gas or other non-lethal area weapon by its own projectile (thrown grenades), not smoke.</summary>
        public static bool IsAreaGas(ThingDef d)
        {
            if (d == null || !d.IsRangedWeapon || Grenades.IsSmoke(d) || d.Verbs == null) return false;
            if (GasRaid.IsTox(d)) return true;
            foreach (var v in d.Verbs) if (IsGasProjectile(v.defaultProjectile)) return true;
            return false;
        }

        /// <summary>
        /// A gas shell or grenade: tox, tear gas, healing gas, insecticide, firefoam (gas extension, a gas other than smoke,
        /// gas / tox / extinguish damage, tox ammo such as the 40 mm tox grenades of the waster heavies). Smoke, EMP, flashbangs
        /// and explosives are not gas.
        /// </summary>
        public static bool IsGasProjectile(ThingDef proj)
        {
            var pp = proj?.projectile;
            if (pp == null) return false;
            if (gasCache.TryGetValue(proj, out bool g)) return g;
            bool gasExt = false;
            if (proj.modExtensions != null)
                foreach (var ext in proj.modExtensions) if (ext != null && ext.GetType().Name.Contains("Gas")) gasExt = true;
            string dmg = pp.damageDef?.defName ?? "";
            bool smokeOnly = !gasExt && (pp.postExplosionGasType == GasType.BlindSmoke || (dmg == "Smoke" && pp.postExplosionGasType == null))
                             && !proj.defName.Contains("Tox") && !proj.defName.Contains("Gas");
            g = !smokeOnly && (gasExt || pp.postExplosionGasType != null || dmg.Contains("Tox") || dmg.Contains("Gas") || dmg == "Extinguish"
                               || proj.defName.Contains("Tox") || proj.defName.Contains("Gas"));
            gasCache[proj] = g;
            return g;
        }

        private static bool ceResolved;
        private static System.Type ceVerbType;
        private static System.Reflection.PropertyInfo ceProjectile;

        /// <summary>The projectile the verb fires now: for CE guns the loaded ammo's (tox shells in a grenade launcher).</summary>
        public static ThingDef CurrentProjectile(Verb v)
        {
            if (!ceResolved)
            {
                ceResolved = true;
                ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
                ceProjectile = ceVerbType != null ? AccessTools.Property(ceVerbType, "Projectile") : null;
            }
            try
            {
                if (ceProjectile != null && ceVerbType.IsInstanceOfType(v)) return ceProjectile.GetValue(v) as ThingDef;
            }
            catch { }
            return v.GetProjectile();
        }

        /// <summary>This verb would put gas down (a gas grenade, or a launcher loaded with gas shells).</summary>
        public static bool IsGasVerb(Verb v) => v != null && !v.IsMeleeAttack && (IsAreaGas(v.EquipmentSource?.def) || IsGasProjectile(CurrentProjectile(v)));

        /// <summary>The raider's plan wants no gas from this verb: no enclosed, roofed room to gas (gas in the open drifts off).</summary>
        public static bool HoldGas(Pawn p, Verb v)
        {
            if (p?.Faction == null || !p.Spawned || p.Map == null || !p.Map.IsPlayerHome || !p.Faction.HostileTo(Faction.OfPlayer)) return false;
            // every hostile raider on the colony map, also sieges and other lords without a plan of ours: gas only for a gas
            // raid against an enclosed room
            AssaultPlan plan = AssaultPlanner.PlanOf(p);
            return (plan == null || plan.gasRoom == null) && IsGasVerb(v);
        }

        /// <summary>A raider whose only gun fires gas and has nothing to gas: as good as unarmed (rearm, plunder or hold back).</summary>
        public static bool GasOnlyArmed(Pawn p)
        {
            Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
            return v != null && HoldGas(p, v);
        }

        // ---------------- CE ammo: the right round for the job (playtest: "the contact beam fires gas grenades, check for
        // the right ammo for breaching") - reflection, CE stays optional ----------------

        private static bool ammoResolved;
        private static System.Type ammoUserType;
        private static System.Reflection.PropertyInfo pCurrentAmmo, pSelectedAmmo, pProps;
        private static System.Reflection.FieldInfo fAmmoSet, fAmmoTypes, fLinkAmmo, fLinkProj;
        private static System.Reflection.MethodInfo mUnload, mMakeReload, mReduce;
        private static System.Reflection.PropertyInfo pCurMag;
        public static int AmmoSwitches;

        private static void ResolveAmmo()
        {
            if (ammoResolved) return;
            ammoResolved = true;
            ammoUserType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
            if (ammoUserType == null) return;
            pCurrentAmmo = AccessTools.Property(ammoUserType, "CurrentAmmo");
            pSelectedAmmo = AccessTools.Property(ammoUserType, "SelectedAmmo");
            pProps = AccessTools.Property(ammoUserType, "Props");
            mUnload = AccessTools.Method(ammoUserType, "TryUnload", new[] { typeof(bool) });
            mMakeReload = AccessTools.Method(ammoUserType, "TryMakeReloadJob");
            mReduce = AccessTools.Method(ammoUserType, "TryReduceAmmoCount");
            pCurMag = AccessTools.Property(ammoUserType, "CurMagCount");
            fAmmoSet = AccessTools.Field(AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser"), "ammoSet");
            fAmmoTypes = AccessTools.Field(AccessTools.TypeByName("CombatExtended.AmmoSetDef"), "ammoTypes");
            var link = AccessTools.TypeByName("CombatExtended.AmmoLink");
            fLinkAmmo = AccessTools.Field(link, "ammo");
            fLinkProj = AccessTools.Field(link, "projectile");
        }

        private static ThingComp AmmoUser(Thing gun)
        {
            ResolveAmmo();
            if (ammoUserType == null || !(gun is ThingWithComps tw)) return null;
            foreach (var c in tw.AllComps) if (ammoUserType.IsInstanceOfType(c)) return c;
            return null;
        }

        /// <summary>(ammo, projectile) pairs of the gun's ammo set.</summary>
        private static List<(ThingDef ammo, ThingDef proj)> AmmoLinks(ThingComp comp)
        {
            var list = new List<(ThingDef, ThingDef)>();
            try
            {
                object set = fAmmoSet?.GetValue(pProps.GetValue(comp));
                if (set == null || !(fAmmoTypes?.GetValue(set) is System.Collections.IEnumerable links)) return list;
                foreach (object l in links) list.Add((fLinkAmmo.GetValue(l) as ThingDef, fLinkProj.GetValue(l) as ThingDef));
            }
            catch { }
            return list;
        }

        /// <summary>Siege guns: a round that damages walls - an explosive one, or a heavy shell (40+ damage, e.g. 90 mm HEAT).</summary>
        public static bool WallBreaker(ThingDef proj)
        {
            if (BreachRound(proj)) return true;
            var pp = proj?.projectile;
            if (pp?.damageDef == null || IsGasProjectile(proj) || !pp.damageDef.harmsHealth) return false;
            int dmg = 0;
            try { dmg = pp.GetDamageAmount((Thing)null); } catch { }
            return dmg >= 40;
        }

        /// <summary>A round that breaks walls: an explosive that harms (not gas, smoke, EMP, stun or firefoam).</summary>
        public static bool BreachRound(ThingDef proj)
        {
            var pp = proj?.projectile;
            if (pp?.damageDef == null || IsGasProjectile(proj) || !pp.damageDef.harmsHealth) return false;
            string dmg = pp.damageDef.defName;
            // fire does nothing to stone or steel (playtest: "firebomb molotov cocktail against steel wall or stone wall do not work") -
            // wooden walls are set alight by hand instead (BreachMethod.Fire)
            bool fire = dmg.Contains("Flame") || dmg.Contains("Fire") || dmg.Contains("Burn") || dmg.Contains("Incend") || dmg.Contains("Napalm")
                        || proj.defName.Contains("Molotov") || proj.defName.Contains("Incendiary") || proj.defName.Contains("Flame");
            return pp.explosionRadius > 0f && !fire && !dmg.Contains("EMP") && !dmg.Contains("Stun") && dmg != "Smoke" && dmg != "Extinguish";
        }

        /// <summary>Loaded with a wall-breaking round (or a gun without CE ammo whose own round breaks walls).</summary>
        public static bool LoadedForBreach(Verb v)
        {
            if (v == null) return false;
            ThingComp comp = AmmoUser(v.EquipmentSource);
            ThingDef cur = null;
            if (comp != null) try { cur = pCurrentAmmo.GetValue(comp) as ThingDef; } catch { }
            if (comp == null || cur == null) return BreachRound(CurrentProjectile(v));   // no CE ammo: the gun's own projectile
            foreach (var l in AmmoLinks(comp)) if (l.ammo == cur) return BreachRound(l.proj);
            return BreachRound(CurrentProjectile(v));
        }

        /// <summary>A wall-breaking round the raider carries for this gun (null if none).</summary>
        private static ThingDef CarriedRound(Pawn p, ThingComp comp, System.Func<ThingDef, bool> ok)
        {
            if (comp == null || p.inventory?.innerContainer == null) return null;
            ThingDef best = null, bestProj = null;
            foreach (var l in AmmoLinks(comp))
            {
                if (l.ammo == null || l.proj == null || !ok(l.proj) || !p.inventory.innerContainer.Any(t => t.def == l.ammo)) continue;
                if (best == null || (BreachRound(l.proj) && !BreachRound(bestProj))) { best = l.ammo; bestProj = l.proj; }
            }
            return best;
        }

        /// <summary>Can this launcher breach: loaded with a wall-breaking round, or the raider carries one for it.</summary>
        public static bool CanBreach(Pawn p, Verb v) =>
            v != null && (LoadedForBreach(v) || CarriedRound(p, AmmoUser(v.EquipmentSource), BreachRound) != null);

        /// <summary>A reusable launcher (not a one-use grenade) loaded with, or carrying, an explosive round.</summary>
        public static bool ExplosiveLauncher(Pawn p)
        {
            Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
            if (v == null || v.IsMeleeAttack || (v.verbProps.verbClass?.Name.Contains("OneUse") ?? false)) return false;
            return LoadedForBreach(v) || CarriedRound(p, AmmoUser(v.EquipmentSource), BreachRound) != null;
        }

        /// <summary>
        /// Loadout (playtest 2026-10-25: "we need to adjust the loadout here" - the waster heavies carry only 40 mm tox grenades,
        /// useless in the open): a raider whose launcher has only gas rounds swaps 75 % of them one for one into the explosive
        /// round of the same ammo set (no extra gear, same budget); a quarter stays gas for an enclosed room. Once per pawn.
        /// </summary>
        public static int GasRoundsSwapped;
        private static readonly HashSet<Pawn> swapped = new HashSet<Pawn>();

        public static void SwapGasAmmo(List<Pawn> pawns)
        {
            if (!TacticalAIMod.Settings.gasAmmoSwap) return;
            foreach (Pawn p in pawns)
            {
                Verb v = p.equipment?.PrimaryEq?.PrimaryVerb;
                if (v == null || v.IsMeleeAttack || p.inventory?.innerContainer == null || !swapped.Add(p)) continue;
                ThingComp comp = AmmoUser(v.EquipmentSource);
                if (comp == null) continue;
                var links = AmmoLinks(comp);
                ThingDef he = links.Where(l => l.ammo != null && BreachRound(l.proj)).Select(l => l.ammo).FirstOrDefault();
                if (he == null || p.inventory.innerContainer.Any(t => t.def == he)) continue;
                foreach (var l in links)
                {
                    if (l.ammo == null || !IsGasProjectile(l.proj)) continue;
                    foreach (Thing t in p.inventory.innerContainer.Where(t => t.def == l.ammo).ToList())
                    {
                        int n = Mathf.FloorToInt(t.stackCount * 0.75f);
                        if (n <= 0) continue;
                        Thing taken = t.SplitOff(n);
                        taken.Destroy();
                        Thing h = ThingMaker.MakeThing(he);
                        h.stackCount = n;
                        if (p.inventory.innerContainer.TryAdd(h)) GasRoundsSwapped += n;
                    }
                }
            }
        }

        /// <summary>A turret gun's CE ammo: the first non-gas round of its set and the magazine size (null without CE).</summary>
        public static ThingDef TurretAmmo(ThingDef gunDef, out int magSize)
        {
            magSize = 0;
            ResolveAmmo();
            if (gunDef?.comps == null || fAmmoTypes == null) return null;
            foreach (var cp in gunDef.comps)
            {
                if (cp.GetType().Name != "CompProperties_AmmoUser") continue;
                try
                {
                    var fMag = AccessTools.Field(cp.GetType(), "magazineSize");
                    if (fMag != null) magSize = (int)fMag.GetValue(cp);
                    object set = fAmmoSet.GetValue(cp);
                    if (set == null || !(fAmmoTypes.GetValue(set) is System.Collections.IEnumerable links)) return null;
                    ThingDef first = null;
                    foreach (object l in links)
                    {
                        var ammo = fLinkAmmo.GetValue(l) as ThingDef;
                        var proj = fLinkProj.GetValue(l) as ThingDef;
                        if (ammo == null || IsGasProjectile(proj)) continue;
                        if (first == null) first = ammo;
                    }
                    return first;
                }
                catch { return null; }
            }
            return null;
        }

        /// <summary>Rounds in the magazine (CE); -1 without CE ammo.</summary>
        public static int LoadedRounds(Verb v)
        {
            ThingComp comp = AmmoUser(v?.EquipmentSource);
            if (comp == null || pCurMag == null) return -1;
            try { return (int)pCurMag.GetValue(comp); } catch { return -1; }
        }

        /// <summary>Take one round from the magazine (CE); true without CE ammo.</summary>
        public static bool ConsumeRound(Verb v)
        {
            ThingComp comp = AmmoUser(v?.EquipmentSource);
            if (comp == null) return true;
            try
            {
                if (mReduce != null) { var ps = mReduce.GetParameters(); object r = mReduce.Invoke(comp, ps.Length == 0 ? null : new object[] { 1 }); return !(r is bool b) || b; }
                if (pCurMag != null && pCurMag.CanWrite) { int n = (int)pCurMag.GetValue(comp); if (n <= 0) return false; pCurMag.SetValue(comp, n - 1); return true; }
            }
            catch { }
            return false;
        }

        /// <summary>CE's reload job for the gun (null if none).</summary>
        public static Job ReloadJob(Verb v)
        {
            ThingComp comp = AmmoUser(v?.EquipmentSource);
            try { return comp != null ? mMakeReload?.Invoke(comp, null) as Job : null; } catch { return null; }
        }

        /// <summary>Still able to breach: ready with a breaching round, or carrying one to reload.</summary>
        public static bool CanStillBreach(Pawn p, Verb v) =>
            v != null && ((v.Available() && LoadedForBreach(v)) || CarriedRound(p, AmmoUser(v.EquipmentSource), BreachRound) != null);

        /// <summary>At the wall: loaded with the wrong round (gas, smoke, EMP) but carrying a breaching one - the reload job for it.</summary>
        public static Job ReloadForBreach(Pawn p, Verb v)
        {
            if (v == null || LoadedForBreach(v)) return null;
            ThingComp comp = AmmoUser(v.EquipmentSource);
            ThingDef ammo = CarriedRound(p, comp, BreachRound);
            return ammo != null ? SwitchJob(comp, ammo) : null;
        }

        /// <summary>Loaded with gas, carrying another round (breaching ones first): the reload job for it.</summary>
        public static Job ReloadFromGas(Pawn p, Verb v)
        {
            if (v == null || !IsGasProjectile(CurrentProjectile(v))) return null;
            ThingComp comp = AmmoUser(v.EquipmentSource);
            ThingDef ammo = CarriedRound(p, comp, proj => !IsGasProjectile(proj));
            return ammo != null ? SwitchJob(comp, ammo) : null;
        }

        private static Job SwitchJob(ThingComp comp, ThingDef ammo)
        {
            try
            {
                pSelectedAmmo.SetValue(comp, ammo);
                mUnload?.Invoke(comp, new object[] { true });   // the loaded rounds back into the pack
                Job j = mMakeReload?.Invoke(comp, null) as Job;
                if (j != null) AmmoSwitches++;
                return j;
            }
            catch { return null; }
        }

        /// <summary>
        /// Funnel with a door in it and nobody to see behind it: a building-destroying launcher shoots the door (a new line
        /// of fire, and the blast reaches whoever waits behind it). Null if there is no such door or no launcher.
        /// </summary>
        public static Job FunnelDoorShot(Pawn pawn, Funnels.Read f)
        {
            Verb v = AssaultPlanner.DestructiveRangedVerb(pawn);
            if (v == null || !v.Available() || f == null) return null;
            Map map = pawn.Map;
            Building door = null;
            float best = float.MaxValue;
            float reach = Mathf.Max(3f, f.mouth.DistanceTo(f.inner) + 2f);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(f.mouth, reach, true))
            {
                if (!c.InBounds(map) || !(c.GetEdifice(map) is Building_Door d) || d.Faction != Faction.OfPlayer) continue;
                float dist = d.Position.DistanceTo(pawn.Position);
                if (dist >= best || dist > v.verbProps.range || dist < (v.verbProps.defaultProjectile?.projectile?.explosionRadius ?? 2f) + 2f) continue;
                if (!v.CanHitTargetFrom(pawn.Position, d)) continue;
                best = dist;
                door = d;
            }
            if (door == null) return null;
            // a shot that can't go off now (cooldown, reload) ends at once: don't reorder it within 30 ticks
            int now = Find.TickManager.TicksGame;
            if (lastOrder.TryGetValue(pawn, out int last) && now - last < 30 && pawn.CurJobDef != JobDefOf.UseVerbOnThing)
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
            lastOrder[pawn] = now;
            Job shoot = JobMaker.MakeJob(JobDefOf.UseVerbOnThing, door);
            shoot.verbToUse = v;
            shoot.preventFriendlyFire = true;
            shoot.expiryInterval = 300;
            FunnelDoorShots++;
            return shoot;
        }
    }

    /// <summary>Gas weapons count as not ready while the raid has no enclosed room to gas (vanilla, CE and our own fight
    /// logic all ask Verb.Available before they use a weapon, so nobody throws it into the open).</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_NoGasInTheOpen
    {
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (!__result || TacticalAIMod.Settings == null || !TacticalAIMod.Settings.assaultPlanner) return;
            if (!(__instance.caster is Pawn p) || __instance.EquipmentSource == null) return;
            if (Launchers.HoldGas(p, __instance)) { __result = false; Launchers.GasHeld++; }
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>Last guard: a gas shot that slipped past Verb.Available (a job that skips it) is not started.</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Patch_NoGasCast
    {
        public static int Blocked;

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (TacticalAIMod.Settings == null || !TacticalAIMod.Settings.assaultPlanner || __instance?.EquipmentSource == null) return true;
            if (!(__instance.caster is Pawn p) || !Launchers.HoldGas(p, __instance)) return true;
            Blocked++;
            __result = false;
            return false;
        }
    }

    /// <summary>Tox packs (waster slashers): vanilla pops them whenever an enemy stands next to the wearer; in the open the
    /// cloud drifts off - only under a roof.</summary>
    [HarmonyPatch(typeof(CompToxPack), "ChanceToUse")]
    public static class Patch_ToxPackIndoorsOnly
    {
        public static int Held;

        public static void Postfix(Pawn wearer, ref float __result)
        {
            if (__result <= 0f || TacticalAIMod.Settings == null || !TacticalAIMod.Settings.assaultPlanner || wearer?.Map == null) return;
            if (!wearer.Map.IsPlayerHome || wearer.Faction == null || !wearer.Faction.HostileTo(Faction.OfPlayer)) return;
            if (wearer.Position.Roofed(wearer.Map)) return;
            __result = 0f;
            Held++;
        }
    }
}
