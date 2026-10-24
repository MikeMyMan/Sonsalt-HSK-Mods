using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>
    /// Vanilla "raiders satisfied, leaving" (Trigger_FractionColonyDamageTaken: building damage past 25-35 % of the colony's,
    /// min 900) - held off while a planned assault is running and going well (playtest, 2026-10-24: "take it when the
    /// assault stalls, after it has run a while, or once the defenders are down"). vg2: plunder during breach prep did the
    /// damage, and every village raid left just as its breach opened.
    /// - held: a Breach / Walk plan in its approach (the assault is being prepared, up to satisfiedPrepMaxTicks), or in
    ///   its assault while it makes progress;
    /// - released: the assault has run satisfiedAssaultTicks; it stalls (no defender down and no breach opened for
    ///   satisfiedStallTicks, or satisfiedLossShare of the raiders at H-hour lost); no defender left standing; withdrawal.
    /// - our own walls-and-doors damage never counts (the breach is the means, not the goal); plunder, arson and wrecking do,
    ///   so once released a raid that did enough damage leaves at once.
    /// </summary>
    public static class SatisfiedHold
    {
        public static int HeldTicks, Releases;
        public static string LastRelease = "";

        /// <summary>Damage to a player's wall or door during a planned assault on this map: credited to the plan.</summary>
        public static void WallDamage(Thing t, float amount)
        {
            if (amount <= 0f || t?.Map == null || t.Faction != Faction.OfPlayer) return;
            if (!(t is Building b) || !(b.def == ThingDefOf.Wall || b.def.IsDoor || (b.def.passability == Traversability.Impassable && b.def.building != null && !b.def.building.isNaturalRock))) return;
            foreach (var plan in AssaultPlanner.Plans.Values)
            {
                if ((plan.mode != PlanMode.Breach && plan.mode != PlanMode.Walk) || plan.phase == PlanPhase.Withdraw) continue;
                foreach (var m in plan.members) { if (m.MapHeld == t.Map) plan.wallDamage += amount; break; }
            }
        }

        /// <summary>True: keep the raid from leaving satisfied this tick.</summary>
        public static bool Hold(Lord lord, AssaultPlan plan, int now)
        {
            var s = TacticalAIMod.Settings;
            if (!s.satisfiedHold || (plan.mode != PlanMode.Breach && plan.mode != PlanMode.Walk)) return false;
            if (plan.phase == PlanPhase.Approach) return now - plan.createdTick <= s.satisfiedPrepMaxTicks || Release(plan, "prep took too long");
            if (plan.phase != PlanPhase.Assault || plan.hHourTick < 0) return false;
            if (plan.satReleased) return false;
            if (now - plan.hHourTick > s.satisfiedAssaultTicks) return Release(plan, "assault ran " + s.satisfiedAssaultTicks + " ticks");
            // progress, every 60 ticks: a defender fewer standing, a breach more open
            if (plan.satCheckTick < 0 || now - plan.satCheckTick >= 60)
            {
                plan.satCheckTick = now;
                int defs = Suppression.Defenders(lord.Map, plan.objective, 60f).Count;
                if (defs == 0) return Release(plan, "defenders down");
                int open = 0;
                foreach (var bp in plan.breaches) if (bp.WallsGone) open++;
                if (plan.satProgressTick < 0 || defs < plan.satDefenders || open > plan.satOpen) plan.satProgressTick = now;
                plan.satDefenders = defs;
                plan.satOpen = open;
                int alive = 0;
                foreach (var m in plan.members) if (m.Spawned && !m.Dead && !m.Downed) alive++;
                if (plan.aliveAtHHour > 0 && alive < plan.aliveAtHHour * (1f - s.satisfiedLossShare)) return Release(plan, "losses " + (plan.aliveAtHHour - alive) + "/" + plan.aliveAtHHour);
            }
            if (now - plan.satProgressTick > s.satisfiedStallTicks) return Release(plan, "stalled " + s.satisfiedStallTicks + " ticks");
            return true;
        }

        private static bool Release(AssaultPlan plan, string why)
        {
            if (!plan.satReleased)
            {
                plan.satReleased = true;
                Releases++;
                LastRelease = why;
                plan.capability += "; satisfied exit allowed: " + why;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Trigger_FractionColonyDamageTaken), nameof(Trigger_FractionColonyDamageTaken.ActivateOn))]
    public static class Patch_SatisfiedHold
    {
        public static bool Prefix(Trigger_FractionColonyDamageTaken __instance, Lord lord, TriggerSignal signal, ref bool __result)
        {
            if (signal.type != TriggerSignalType.Tick || lord?.Map == null) return true;
            if (!(__instance.data is TriggerData_FractionColonyDamageTaken d)) return true;
            if (!AssaultPlanner.Plans.TryGetValue(lord.loadID, out var plan)) return true;
            // our breach damage moves the baseline: it never counts towards "satisfied"
            if (plan.wallDamage > plan.wallDamageCredited)
            {
                d.startColonyDamage += plan.wallDamage - plan.wallDamageCredited;
                plan.wallDamageCredited = plan.wallDamage;
            }
            if (!SatisfiedHold.Hold(lord, plan, Find.TickManager.TicksGame)) return true;
            SatisfiedHold.HeldTicks++;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(DamageWatcher), "Notify_DamageTaken")]
    public static class Patch_SatisfiedWallDamage
    {
        public static void Postfix(Thing __0, float __1) => SatisfiedHold.WallDamage(__0, __1);
    }
}
