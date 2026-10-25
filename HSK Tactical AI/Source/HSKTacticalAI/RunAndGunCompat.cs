using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// RunAndGun (memegoddess.runandgun) with its "enable for AI" option (on by default) turns every move order of a raider
    /// into shooting on the move - retreats, flanking, the medic's carry, the withdrawal - at a 40 % speed penalty, with its
    /// own target choice (research/newmods: RunAndGun A/B, 2026-10-25). For raiders under a plan its per-pawn switch
    /// (CompRunAndGun.isEnabled) is set by role: only riflemen and assault raiders in the assault shoot on the move;
    /// medics, breachers, shields and their followers, base of fire, carriers and anyone withdrawing walk. Does nothing
    /// without RunAndGun.
    /// </summary>
    public static class RunAndGunCompat
    {
        private static bool resolved;
        private static System.Type compType;
        private static PropertyInfo isEnabled;
        public static int Enabled, Disabled;

        private static bool Resolve()
        {
            if (!resolved)
            {
                resolved = true;
                compType = AccessTools.TypeByName("RunAndGun.CompRunAndGun");
                isEnabled = compType != null ? AccessTools.Property(compType, "isEnabled") : null;
            }
            return isEnabled != null;
        }

        /// <summary>Every 60 ticks for the members of plans on this map.</summary>
        public static void Tick(Map map)
        {
            var s = TacticalAIMod.Settings;
            if (!s.runAndGunByRole || !Resolve()) return;
            foreach (var plan in AssaultPlanner.Plans.Values)
            {
                if (plan.map != map || plan.memoryDone) continue;
                foreach (Pawn p in plan.members)
                {
                    if (!p.Spawned || p.Dead || p.Downed || p.Map != map) continue;
                    ThingComp comp = null;
                    foreach (var c in p.AllComps) if (compType.IsInstanceOfType(c)) { comp = c; break; }
                    if (comp == null) continue;
                    bool want = plan.phase == PlanPhase.Assault
                                && plan.roleOf.TryGetValue(p, out var r) && (r == RaidRole.Rifleman || r == RaidRole.Assault)
                                && p.carryTracker?.CarriedThing == null
                                && !ShieldWall.IsBearer(plan, p) && !plan.shieldOf.ContainsKey(p);
                    try
                    {
                        if ((bool)isEnabled.GetValue(comp) == want) continue;
                        isEnabled.SetValue(comp, want);
                        if (want) Enabled++; else Disabled++;
                    }
                    catch { }
                }
            }
        }

        /// <summary>RunAndGun starts its own stance and returns false from Verb.TryStartCastOn: a shot all the same.</summary>
        public static bool ShootingOnTheMove(Pawn p) => p?.stances?.curStance != null && p.stances.curStance.GetType().Name == "Stance_RunAndGun";
    }
}
