using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HSKTacticalAI
{
    /// <summary>How a breach point's wall comes down.</summary>
    public enum BreachMethod { None, Charge, Launcher, Tool, Fire, Tunnel, Grenade, Bash }

    /// <summary>
    /// One breach point of a breach plan: the wall layer to open, where its group stacks, the covered route there, and
    /// the specialists who open it. A plan has 1-3 of these on different sides, opened at the same time.
    /// </summary>
    public class BreachPoint
    {
        public int index;
        public readonly List<Building> walls = new List<Building>();
        public float hp;
        public IntVec3 entry = IntVec3.Invalid;       // first wall cell: the way in once it's open
        public IntVec3 staging = IntVec3.Invalid;     // stack point beside the breach
        public IntVec3 plantSide = IntVec3.Invalid;   // route cell in front of the wall (raid's side)
        public readonly List<IntVec3> route = new List<IntVec3>();   // covered route to the stack point (waypoints)
        public readonly HashSet<Pawn> team = new HashSet<Pawn>();     // specialists assigned here
        public readonly HashSet<Pawn> reachedStaging = new HashSet<Pawn>();
        public Pawn planter;                          // one charge planter / fire starter at a time
        public BreachMethod method = BreachMethod.None;
        public bool flammable;
        public bool rock;                             // the layer is natural rock: dig a tunnel
        public bool door;                             // the breach point is a player door (cheap: grenades, Bomb x2 vs doors)
        public readonly List<IntVec3> line = new List<IntVec3>();   // cells in walking order (4-connected) through the layer
        public readonly List<IntVec3> line2 = new List<IntVec3>();  // tunnel: second lane beside the first (2-wide exit, 2 faces)
        public readonly List<Building> walls2 = new List<Building>();
        public readonly Dictionary<Pawn, int> laneOf = new Dictionary<Pawn, int>();
        public float etaTicks;                        // tunnel: estimated digging time
        public bool blasting;                          // tunnel with mining charges (Industrial+) instead of picks
        public bool abandoned, smoked;
        public int noMeansSince = -1;                 // first tick the team looked out of means (reloading, a sidearm swap): only a lasting lack counts
        public int fragsNeeded, fragsThrown;           // grenade door breach: frags planned (HP / fragDoorDamage) and frags thrown so far
        public float bearing;                         // from the objective, degrees
        public float cost;
        public float score;

        public Building CurrentWall()
        {
            foreach (var b in walls) if (b != null && !b.Destroyed && b.Spawned) return b;
            return null;
        }

        public bool WallsGone => CurrentWall() == null;

        public bool TeamAlive => team.Any(p => p.Spawned && !p.Dead && !p.Downed);

        public string Describe() =>
            "#" + index + " " + method + (flammable ? "(wood)" : "") + (rock ? "(rock " + walls.Count + " cells, eta " + etaTicks.ToString("0") + ")" : "") + " " + Mathf.RoundToInt(bearing) + "deg hp=" + hp.ToString("0") +
            " team=" + team.Count + " stack=" + staging + (abandoned ? " ABANDONED" : WallsGone ? " open" : "");
    }

    /// <summary>
    /// Force ratio (doctrine: an assault on a prepared position wants about 3:1 locally). A rough, mod-agnostic
    /// fighting value per pawn - weapon (by market value), skill, armour, health - and per turret.
    /// </summary>
    public static class ForceEstimate
    {
        public static float PawnScore(Pawn p)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned) return 0f;
            if (p.RaceProps.Humanlike && p.WorkTagIsDisabled(WorkTags.Violent)) return 0f;
            float w;
            ThingWithComps eq = p.equipment?.Primary;
            bool ranged = eq != null && eq.def.IsRangedWeapon;
            if (!p.RaceProps.Humanlike)
                w = Mathf.Sqrt(Mathf.Max(10f, (p.kindDef?.combatPower ?? 50f) * 4f));   // animals, mechs
            else if (eq != null)
                w = Mathf.Sqrt(Mathf.Max(10f, eq.MarketValue)) * (ranged ? 1f : 0.6f);
            else
                w = 3f;
            float skill = 1f;
            if (p.skills != null && p.RaceProps.Humanlike)
                skill = 0.5f + (p.skills.GetSkill(ranged ? SkillDefOf.Shooting : SkillDefOf.Melee)?.Level ?? 5) / 20f;
            float armor = 1f + Mathf.Clamp01(p.GetStatValue(StatDefOf.ArmorRating_Sharp));
            float health = p.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            return w * skill * armor * health;
        }

        public static float TurretScore(Building_Turret t)
        {
            if (t == null || t.Destroyed || !t.Spawned) return 0f;
            if (t.def.building != null && t.def.building.IsMortar) return 0f;
            var power = t.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return 0f;
            return Mathf.Sqrt(Mathf.Max(10f, t.MarketValue)) * 1.2f;
        }

        public static float Attackers(IEnumerable<Pawn> pawns) => pawns.Sum(PawnScore);

        /// <summary>The player's fighters on the map: colonists, colony mechs and animals that fight, turrets.</summary>
        public static float Defenders(Map map)
        {
            float sum = 0f;
            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (p.IsPrisoner || p.IsSlave && !p.IsColonist) continue;
                if (!p.RaceProps.Humanlike && !p.RaceProps.IsMechanoid && p.training?.HasLearned(TrainableDefOf.Release) != true) continue;
                sum += PawnScore(p);
            }
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
                if (b is Building_Turret t) sum += TurretScore(t);
            return sum;
        }
    }
}
