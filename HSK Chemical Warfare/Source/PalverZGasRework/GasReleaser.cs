using GasFrameWork;
using Verse;

namespace PalverZGasRework
{
    /// <summary>How a landed releaser lets its gas out: one burst on landing, then optional pulses.</summary>
    public class GasReleaseExtension : DefModExtension
    {
        public GasTypeDef gasType;
        public int initialAmount = 255;
        public float initialRadius = 2.9f;
        public int pulses;
        public int pulseAmount = 120;
        public float pulseRadius = 1.5f;
        public int pulseIntervalTicks = 60;
        /// <summary>How long the spent casing stays visible after the last pulse.</summary>
        public int lingerTicks = 300;
    }

    /// <summary>Spawned by Combat Extended grenades and shells (postExplosionSpawnSingleThingDef).
    /// CE projectiles can't carry the framework's custom gas, so the projectile leaves this behind
    /// and it puts the gas on the map.</summary>
    public class GasReleaser : ThingWithComps
    {
        private int pulsesDone;
        private int ticksToNext;
        private int lingerLeft = -1;

        private GasReleaseExtension Ext => def.GetModExtension<GasReleaseExtension>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad) return;
            GasReleaseExtension e = Ext;
            if (e?.gasType == null)
            {
                Log.ErrorOnce($"[Gas Rework] {def.defName} has no GasReleaseExtension with a gasType.", def.shortHash ^ 0x5a17);
                return;
            }
            CustomGasUtility.AddGasRadial(Position, map, e.gasType, e.initialAmount, e.initialRadius);
            ticksToNext = e.pulseIntervalTicks;
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned) return;
            GasReleaseExtension e = Ext;
            if (e?.gasType == null)
            {
                Destroy();
                return;
            }
            if (pulsesDone < e.pulses)
            {
                if (--ticksToNext > 0) return;
                CustomGasUtility.AddGasRadial(Position, Map, e.gasType, e.pulseAmount, e.pulseRadius);
                pulsesDone++;
                ticksToNext = e.pulseIntervalTicks;
                return;
            }
            if (lingerLeft < 0) lingerLeft = e.lingerTicks;
            if (--lingerLeft <= 0) Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pulsesDone, "pulsesDone", 0);
            Scribe_Values.Look(ref ticksToNext, "ticksToNext", 0);
            Scribe_Values.Look(ref lingerLeft, "lingerLeft", -1);
        }
    }
}
