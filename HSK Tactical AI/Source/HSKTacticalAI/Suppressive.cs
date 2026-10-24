using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// Suppression that follows real life (playtest, 2026-10-23: "I have a feeling that somehow bows and arrows suppress more
    /// than MGs. also hand grenades should have a big suppression factor").
    ///
    /// CE (ProjectileCE.ApplySuppression, per projectile passing within 3 cells or landing near): bullets and arrows
    /// suppress by damage x suppressionFactor x (1 - armour x 0.5 / penetration), i.e. NOTHING against a target whose
    /// sharp armour is twice the round's penetration; explosions by their damage, fading to zero at radius + 3. Measured:
    /// bow / crossbow raids suppressed the (unarmoured) defenders about twice as long as gun raids against defenders in
    /// the raid's armour.
    ///
    /// Here (same CE rules otherwise - same faction immune, active shield belts immune, CE's own ignore check):
    /// - normal armour no longer reduces the scare; only armour the round cannot penetrate does, to suppressionArmorFloor (0.6);
    /// - volume of fire: automatic weapons x1.2 (bursts of 3+), machine guns x suppressionAuto (bursts of 8+);
    /// - arrows and bolts x suppressionArrow (quiet, slow);
    /// - explosions (grenades, shells, charges) x suppressionExplosive.
    /// Game-wide (both sides), switchable.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_RealSuppression
    {
        private static Type tProj, tPropsCE, tExplosive, tExplosiveProps, tSuppressable;
        private static FieldInfo fOrigin, fEquip, fLauncher, fLanded, fAmount, fSuppFactor, fAirFactor, fPen, fExplRadius, fExplDamage;
        private static PropertyInfo pExact;
        private static MethodInfo mIgnore, mAdd;
        private static StatDef avgSharp;
        private static FieldInfo fDmgBase;

        public static bool Prepare()
        {
            tProj = AccessTools.TypeByName("CombatExtended.ProjectileCE");
            tPropsCE = AccessTools.TypeByName("CombatExtended.ProjectilePropertiesCE");
            tExplosive = AccessTools.TypeByName("CombatExtended.CompExplosiveCE");
            tExplosiveProps = AccessTools.TypeByName("CombatExtended.CompProperties_ExplosiveCE");
            tSuppressable = AccessTools.TypeByName("CombatExtended.CompSuppressable");
            if (tProj == null || tPropsCE == null || tSuppressable == null) return false;
            fOrigin = AccessTools.Field(tProj, "OriginIV3");
            fEquip = AccessTools.Field(tProj, "equipmentDef");
            fLauncher = AccessTools.Field(tProj, "launcher");
            fLanded = AccessTools.Field(tProj, "landed");
            fAmount = AccessTools.Field(tProj, "suppressionAmount");
            pExact = AccessTools.Property(tProj, "ExactPosition");
            fSuppFactor = AccessTools.Field(tPropsCE, "suppressionFactor");
            fAirFactor = AccessTools.Field(tPropsCE, "airborneSuppressionFactor");
            fPen = AccessTools.Field(tPropsCE, "armorPenetrationSharp");
            if (tExplosiveProps != null)
            {
                fExplRadius = AccessTools.Field(tExplosiveProps, "explosiveRadius");
                fExplDamage = AccessTools.Field(tExplosiveProps, "damageAmountBase");
            }
            mIgnore = AccessTools.Method(tSuppressable, "IgnoreSuppresion", new[] { typeof(IntVec3) });
            mAdd = AccessTools.Method(tSuppressable, "AddSuppression", new[] { typeof(float), typeof(IntVec3) });
            fTicksUntilDecay = AccessTools.Field(tSuppressable, "ticksUntilDecay");
            fCurrent = AccessTools.Field(tSuppressable, "currentSuppression");
            fIsSuppressed = AccessTools.Field(tSuppressable, "isSuppressed");
            return fOrigin != null && fLauncher != null && fLanded != null && pExact != null && fSuppFactor != null && fAirFactor != null && mAdd != null;
        }

        public static MethodBase TargetMethod() => AccessTools.Method(tProj, "ApplySuppression", new[] { typeof(Pawn), typeof(float) });

        public static int Applied;

        // the armour stat walks the whole body and gear (the most expensive part of a near miss): cached per pawn for 250 ticks
        private static readonly System.Collections.Generic.Dictionary<Pawn, (float armour, int tick)> armourCache = new System.Collections.Generic.Dictionary<Pawn, (float, int)>();

        private static float Armour(Pawn pawn)
        {
            if (avgSharp == null) return 0f;
            int now = Find.TickManager.TicksGame;
            if (armourCache.TryGetValue(pawn, out var c) && now - c.tick < 250) return c.armour;
            float a = pawn.GetStatValue(avgSharp);
            armourCache[pawn] = (a, now);
            if (armourCache.Count > 500) armourCache.Clear();
            return a;
        }
        /// <summary>Suppression added to player pawns, by weapon class (Lab).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, float> ByClass = new System.Collections.Generic.Dictionary<string, float>();
        private static FieldInfo fTicksUntilDecay, fCurrent, fIsSuppressed;

        public static bool Prefix(Thing __instance, Pawn pawn, float suppressionMultiplier)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.realSuppression) return true;
            long t0 = Perf.Start();
            try
            {
                if (pawn == null) return false;
                var props = __instance.def.projectile;
                if (props == null || !tPropsCE.IsInstanceOfType(props)) return true;
                float suppFactor = (float)fSuppFactor.GetValue(props), airFactor = (float)fAirFactor.GetValue(props);
                bool landed = (bool)fLanded.GetValue(__instance);
                if (suppFactor <= 0f || (!landed && airFactor <= 0f)) return false;
                // an active shield belt: no suppression (CE)
                CompShield shield = pawn.TryGetComp<CompShield>();
                if (pawn.RaceProps?.Humanlike == true && pawn.apparel != null)
                    foreach (var a in pawn.apparel.WornApparel) { var c = a.TryGetComp<CompShield>(); if (c != null) { shield = c; break; } }
                ThingComp sup = pawn.AllComps.FirstOrDefault(c => tSuppressable.IsInstanceOfType(c));
                if (sup == null) return false;
                Thing launcher = fLauncher.GetValue(__instance) as Thing;
                IntVec3 origin = (IntVec3)fOrigin.GetValue(__instance);
                if (pawn.Faction == launcher?.Faction) return false;
                if (shield != null && shield.ShieldState != ShieldState.Resetting) return false;
                if (mIgnore != null && (bool)mIgnore.Invoke(sup, new object[] { origin })) return false;

                if (fDmgBase == null) fDmgBase = AccessTools.Field(typeof(ProjectileProperties), "damageAmountBase");
                float amount = Convert.ToSingle(fDmgBase.GetValue(props)) * suppressionMultiplier * suppFactor;
                string cls = "other";
                if (!landed) amount *= airFactor;
                float radius = props.explosionRadius;
                if (radius == 0f && tExplosive != null)
                {
                    ThingComp ex = (__instance as ThingWithComps)?.AllComps.FirstOrDefault(c => tExplosive.IsInstanceOfType(c));
                    if (ex != null && tExplosiveProps.IsInstanceOfType(ex.props))
                    {
                        radius = (float)fExplRadius.GetValue(ex.props);
                        amount = Convert.ToSingle(fExplDamage.GetValue(ex.props));
                        // -1 = "the damage def's default" (fn2 village: launcher=-95, i.e. suppression taken away)
                        if (amount <= 0f) amount = Mathf.Max(1f, props.damageDef?.defaultDamage ?? 20);
                    }
                }
                if (radius == 0f)
                {
                    // bullets / arrows: normal armour does not stop the flinch (playtest: "not sure why armor prevents
                    // suppression ... at least not normal one should"); only armour that stops the round outright
                    // (armour >= penetration) takes the edge off, down to suppressionArmorFloor at twice the penetration
                    float pen = fPen != null ? (float)fPen.GetValue(props) : 0f;
                    if (avgSharp == null) avgSharp = DefDatabase<StatDef>.GetNamedSilentFail("AverageSharpArmor");
                    float armour = Armour(pawn);
                    float stop = pen <= 0f ? 2f : armour / pen;
                    amount *= stop <= 1f ? 1f : Mathf.Lerp(1f, s.suppressionArmorFloor, Mathf.Clamp01(stop - 1f));
                    ThingDef gun = fEquip?.GetValue(__instance) as ThingDef;
                    int burst = gun?.Verbs?.FirstOrDefault(v => !v.IsMeleeAttack)?.burstShotCount ?? 1;
                    bool arrow = __instance.def.defName.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0
                                 || __instance.def.defName.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) >= 0
                                 || (gun != null && (gun.defName.IndexOf("Bow", StringComparison.OrdinalIgnoreCase) >= 0 || gun.defName.Contains("Blowgun") || gun.defName.Contains("Arbalest")));
                    float gunRange = gun?.Verbs?.FirstOrDefault(v => !v.IsMeleeAttack)?.range ?? 0f;
                    if (arrow) { amount *= s.suppressionArrow; cls = "arrow"; }
                    else if (burst >= 8) { amount *= s.suppressionAuto; cls = "mg"; }
                    else if (burst >= 3) { amount *= 1.2f; cls = "auto"; }
                    else if (gunRange >= 50f) cls = "sniper";
                    else cls = "rifle";
                }
                else
                {
                    Vector3 exact = (Vector3)pExact.GetValue(__instance);
                    float dx = exact.x - pawn.DrawPos.x, dz = exact.z - pawn.DrawPos.z, r = radius + 3f;
                    // launched (grenade / rocket launcher, mortar): a ton; thrown (hand grenade): a lot
                    ThingDef gunE = fEquip?.GetValue(__instance) as ThingDef;
                    bool launched = gunE != null && gunE.IsRangedWeapon && !(gunE.Verbs?.Any(v => (v.verbClass?.Name.Contains("Throw") ?? false) || (v.verbClass?.Name.Contains("OneUse") ?? false)) ?? false);
                    amount *= Mathf.Clamp01(1f - (dx * dx + dz * dz) / (r * r)) * (launched ? s.suppressionLauncher : s.suppressionExplosive);
                    cls = launched ? "launcher" : "grenade";
                }
                amount *= s.suppressionScale;
                if (amount <= 0f) return false;
                fAmount?.SetValue(__instance, amount);
                mAdd.Invoke(sup, new object[] { amount, origin });
                // heads stay down a while after a near miss (CE: decay starts after 30 ticks)
                if (fTicksUntilDecay != null && (int)fTicksUntilDecay.GetValue(sup) < s.suppressionHoldTicks) fTicksUntilDecay.SetValue(sup, s.suppressionHoldTicks);
                Applied++;
                if (pawn.Faction == Faction.OfPlayer)
                {
                    ByClass.TryGetValue(cls, out float t);
                    ByClass[cls] = t + Mathf.Min(amount, 1050f);   // CE caps a pawn's suppression at 1050
                }
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[HSK Tactical AI] real suppression failed, using CE's: " + e, 0x48534B51);
                return true;
            }
            finally { Perf.Stop("ce.suppression", t0); }
        }
    }

    /// <summary>
    /// Suppression fades slower (CE: 4 per tick, 240 a second, from 30 ticks after the last near miss): suppressionDecay per
    /// tick, so a defender pinned by an MG burst or a few sniper rounds keeps his head down for seconds, not a blink.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SlowSuppressionDecay
    {
        private static System.Type tSup;
        // runs for every pawn every tick: compiled field accessors, not reflection
        private static AccessTools.FieldRef<object, float> current;
        private static AccessTools.FieldRef<object, bool> isSuppressed;

        public static bool Prepare()
        {
            tSup = AccessTools.TypeByName("CombatExtended.CompSuppressable");
            if (tSup == null || AccessTools.Field(tSup, "currentSuppression") == null || AccessTools.Field(tSup, "isSuppressed") == null) return false;
            current = AccessTools.FieldRefAccess<float>(tSup, "currentSuppression");
            isSuppressed = AccessTools.FieldRefAccess<bool>(tSup, "isSuppressed");
            return true;
        }

        public static MethodBase TargetMethod() => AccessTools.Method(tSup, "CompTickInterval", new[] { typeof(int) });

        public static void Prefix(ThingComp __instance, out float __state)
        {
            __state = current(__instance);
        }

        public static void Postfix(ThingComp __instance, int delta, float __state)
        {
            var s = TacticalAIMod.Settings;
            if (s == null || !s.realSuppression || __state <= 0f) return;
            long t0 = Perf.Start();
            try { Decay(__instance, delta, __state, s); } finally { Perf.Stop("ce.suppressionDecay", t0); }
        }

        private static void Decay(ThingComp __instance, int delta, float __state, TacticalAISettings s)
        {
            float now = current(__instance);
            float removed = __state - now;
            if (removed <= 0f) return;
            float keep = Mathf.Max(0f, removed - s.suppressionDecay * delta);
            if (keep <= 0f) return;
            float restored = now + keep;
            current(__instance) = restored;
            if (restored > 0f) isSuppressed(__instance) = true;
        }
    }
}
