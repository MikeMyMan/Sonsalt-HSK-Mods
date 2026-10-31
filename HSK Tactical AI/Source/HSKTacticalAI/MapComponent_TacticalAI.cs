using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKTacticalAI
{
    public class MapComponent_TacticalAI : MapComponent
    {
        public readonly ThreatMap Threat;
        private ThreatOverlay overlay;

        public MapComponent_TacticalAI(Map map) : base(map)
        {
            Threat = new ThreatMap(map);
        }

        public static MapComponent_TacticalAI For(Map map) => map?.GetComponent<MapComponent_TacticalAI>();

        public override void MapComponentTick()
        {
            if (!TacticalAIMod.Settings.enabled) return;   // master switch: nothing of ours runs
            Adaptive.Tick();
            long t = Perf.Start();
            Threat.Tick();
            Perf.Stop("tick.threatMap", t);
            if (map.IsHashIntervalTick(60)) { t = Perf.Start(); AssaultPlanner.Tick(map); Perf.Stop("tick.planner", t); }
            if (map.IsHashIntervalTick(30 * Adaptive.Load)) { t = Perf.Start(); Posture.Tick(map); Perf.Stop("tick.posture", t); }
            if (map.IsHashIntervalTick(60)) RunAndGunCompat.Tick(map);
            if (map.IsHashIntervalTick(60)) SiegeCaravan.Tick(map);
            if (map.IsHashIntervalTick(60)) CoverCarry.Tick(map);
            if (map.IsHashIntervalTick(60)) { t = Perf.Start(); Contact.Tick(map); Perf.Stop("tick.contact", t); }
            // raid memory: a raid whose members are all dead, downed or gone writes what its faction learned
            if (map.IsHashIntervalTick(250))
                foreach (var plan in AssaultPlanner.Plans.Values)
                    if (!plan.memoryDone && plan.map == map) FactionMemory.TryFinish(plan);
        }

        public override void MapComponentUpdate()
        {
            if (!TacticalAIMod.Settings.enabled || !TacticalAIMod.Settings.showThreatOverlay || Find.CurrentMap != map) return;
            if (overlay == null) overlay = new ThreatOverlay(Threat, map);
            overlay.Update();
        }
    }

    /// <summary>Red = heavy fire, yellow = light fire. Only cells some shooter can hit are drawn.</summary>
    public class ThreatOverlay : ICellBoolGiver
    {
        private readonly ThreatMap threat;
        private readonly CellBoolDrawer drawer;
        private bool dirty = true;

        public ThreatOverlay(ThreatMap threat, Map map)
        {
            this.threat = threat;
            drawer = new CellBoolDrawer(this, map.Size.x, map.Size.z, 0.35f);
            threat.Swapped += () => dirty = true;
        }

        public Color Color => Color.white;

        public bool GetCellBool(int index) => threat.FireAt(index) > 0.05f;

        public Color GetCellExtraColor(int index)
        {
            float max = threat.MaxFire;
            float t = max > 0f ? Mathf.Clamp01(threat.FireAt(index) / max) : 0f;
            return Color.Lerp(new Color(1f, 0.95f, 0.2f), new Color(1f, 0.1f, 0.05f), t);
        }

        public void Update()
        {
            if (dirty)
            {
                drawer.SetDirty();
                dirty = false;
            }
            drawer.MarkForDraw();
            drawer.CellBoolDrawerUpdate();
        }
    }

    [StaticConstructorOnStartup]
    public static class TacticalAITex
    {
        public static readonly Texture2D ThreatIcon = ContentFinder<Texture2D>.Get("UI/HSKTacticalAI/ThreatMap", false) ?? BaseContent.WhiteTex;
    }

    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettings_ThreatToggle
    {
        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null || TacticalAIMod.Settings == null || !TacticalAIMod.Settings.enableThreatMap) return;
            string tip = "HSK Tactical AI: show the threat map (where your colonists and turrets can shoot; red = heavy fire).";
            if (TacticalAIMod.Settings.competenceReadout && Competence.Enabled && Find.CurrentMap != null)
                foreach (var plan in AssaultPlanner.Plans.Values)
                    if (plan.map == Find.CurrentMap && plan.phase != PlanPhase.Converted && plan.members.Any(p => p.Spawned && !p.Dead && !p.Downed))
                        tip += "\n\n" + (plan.faction?.Name ?? "Raid") + ": tactical competence " + Competence.Pct(Competence.Of(plan)) + " - " + plan.competenceWhy;
            row.ToggleableIcon(ref TacticalAIMod.Settings.showThreatOverlay, TacticalAITex.ThreatIcon, tip);
        }
    }
}
