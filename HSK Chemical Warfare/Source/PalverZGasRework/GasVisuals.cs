using System;
using System.Collections.Generic;
using GasFrameWork;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PalverZGasRework
{
    /// <summary>How a gas looks. Optional on any framework GasTypeDef; without it the gas colour and
    /// default puffs are used, so other mods' framework gases get coloured clouds too.</summary>
    public class GasVisualExtension : DefModExtension
    {
        /// <summary>Cloud colour; defaults to the GasTypeDef's gasColor.</summary>
        public Color? cloudColor;
        /// <summary>Multiplies cloud opacity (heavy fogs above 1, thin hazes below).</summary>
        public float opacity = 1f;
        public FleckDef puffFleck;
        public FleckDef wispFleck;
        /// <summary>Sparkle for nanite swarms and crystal barbs; null = none.</summary>
        public FleckDef glintFleck;
        public float glintChance;
        public Color? glintColor;
        public FloatRange puffScale = new FloatRange(1.3f, 2.3f);
        public FloatRange wispScale = new FloatRange(1.6f, 2.8f);
        public float wispSpeed = 0.18f;
    }

    [DefOf]
    public static class GasFleckDefOf
    {
        public static FleckDef PGR_GasPuff;
        public static FleckDef PGR_GasWisp;
        public static FleckDef PGR_GasGlint;

        static GasFleckDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(GasFleckDefOf)); }
    }

    public static class GasVisuals
    {
        /// <summary>Turn off to fall back to the framework's own (pale) overlay.</summary>
        public static bool Enabled = true;

        private static readonly AccessTools.FieldRef<MapComponent_CustomGasGrid, Dictionary<GasTypeDef, byte[]>> GasGrids =
            AccessTools.FieldRefAccess<MapComponent_CustomGasGrid, Dictionary<GasTypeDef, byte[]>>("gasGrids");

        private static readonly Dictionary<GasTypeDef, GasVisualExtension> extCache = new Dictionary<GasTypeDef, GasVisualExtension>();
        private static readonly GasVisualExtension Default = new GasVisualExtension();

        public static GasVisualExtension Ext(GasTypeDef gas)
        {
            if (!extCache.TryGetValue(gas, out GasVisualExtension ext))
            {
                ext = gas.GetModExtension<GasVisualExtension>() ?? Default;
                extCache[gas] = ext;
            }
            return ext;
        }

        // The framework was built against Unity's CoreModule; on 1.7 its Color resolves to UnityStubs at
        // runtime, so the field is read through a field ref instead of compiled against directly.
        private static readonly AccessTools.FieldRef<GasTypeDef, Color> GasColorField = AccessTools.FieldRefAccess<GasTypeDef, Color>("gasColor");

        public static Color CloudColor(GasTypeDef gas)
        {
            Color c = Ext(gas).cloudColor ?? GasColorField(gas);
            c.a = 1f;
            return c;
        }

        public static Dictionary<GasTypeDef, byte[]> Grids(MapComponent_CustomGasGrid grid) => GasGrids(grid);

        /// <summary>Density-weighted colour of every framework gas in the cell. Alpha follows total density,
        /// with a floor so thin edges still show.</summary>
        public static bool CellColor(Dictionary<GasTypeDef, byte[]> grids, int index, out Color color, out GasTypeDef dominant)
        {
            float r = 0, g = 0, b = 0, op = 0;
            int total = 0, best = 0;
            dominant = null;
            foreach (KeyValuePair<GasTypeDef, byte[]> kv in grids)
            {
                byte d = kv.Value[index];
                if (d == 0) continue;
                Color c = CloudColor(kv.Key);
                r += c.r * d; g += c.g * d; b += c.b * d;
                op += Ext(kv.Key).opacity * d;
                total += d;
                if (d > best) { best = d; dominant = kv.Key; }
            }
            if (total < 4)
            {
                color = default;
                return false;
            }
            float t = Mathf.Clamp01(total / 255f);
            float alpha = Mathf.Clamp01((0.2f + 0.68f * Mathf.Sqrt(t)) * (op / total));
            color = new Color(r / total, g / total, b / total, alpha);
            return true;
        }

        // ---------------------------------------------------------------- flecks

        public static void Burst(Map map, IntVec3 center, GasTypeDef gas, int amount, float radius)
        {
            if (!Enabled || map == null || gas == null || map != Find.CurrentMap) return;
            Vector3 c0 = center.ToVector3Shifted();
            if (!c0.ShouldSpawnMotesAt(map)) return;
            GasVisualExtension ext = Ext(gas);
            // A touch lighter than the cloud so the release reads as a burst against its own colour.
            Color color = Color.Lerp(CloudColor(gas), Color.white, 0.3f);
            bool small = radius <= 1.6f && amount <= 60; // vent emission
            int puffs = small ? 1 : Mathf.Clamp(Mathf.RoundToInt(radius * radius * 1.1f), 4, 26);
            for (int i = 0; i < puffs; i++)
            {
                Vector2 off = Rand.InsideUnitCircle * radius * (small ? 0.5f : 0.75f);
                Vector3 loc = c0 + new Vector3(off.x, 0f, off.y);
                if (!loc.ShouldSpawnMotesAt(map)) continue;
                float angle = off.sqrMagnitude > 0.01f ? Mathf.Atan2(off.x, off.y) * Mathf.Rad2Deg : Rand.Range(0f, 360f);
                Throw(map, ext.puffFleck ?? GasFleckDefOf.PGR_GasPuff, loc, ext.puffScale.RandomInRange * (small ? 0.7f : 1f),
                      WithAlpha(color, 0.8f), angle, small ? Rand.Range(0.1f, 0.25f) : Rand.Range(0.4f, 1.1f));
                if (ext.glintFleck != null && Rand.Chance(ext.glintChance))
                    Throw(map, ext.glintFleck, loc, Rand.Range(0.5f, 0.9f), GlintColor(gas, ext), Rand.Range(0f, 360f), Rand.Range(0.2f, 0.6f));
            }
            if (!small) Throw(map, ext.puffFleck ?? GasFleckDefOf.PGR_GasPuff, c0, ext.puffScale.max * 1.6f, WithAlpha(color, 0.9f), Rand.Range(0f, 360f), 0.05f);
        }

        public static void Wisp(Map map, Vector3 loc, GasTypeDef gas, Color cellColor)
        {
            GasVisualExtension ext = Ext(gas);
            Throw(map, ext.wispFleck ?? GasFleckDefOf.PGR_GasWisp, loc, ext.wispScale.RandomInRange,
                  WithAlpha(cellColor, Mathf.Clamp(cellColor.a + 0.15f, 0.25f, 0.75f)), Rand.Range(0f, 360f), Rand.Range(0.4f, 1f) * ext.wispSpeed);
            if (ext.glintFleck != null && Rand.Chance(ext.glintChance))
            {
                Vector2 off = Rand.InsideUnitCircle * 0.5f;
                Throw(map, ext.glintFleck, loc + new Vector3(off.x, 0f, off.y), Rand.Range(0.4f, 0.8f), GlintColor(gas, ext), Rand.Range(0f, 360f), Rand.Range(0.1f, 0.4f));
            }
        }

        private static void Throw(Map map, FleckDef def, Vector3 loc, float scale, Color color, float angle, float speed)
        {
            if (def == null) return;
            FleckCreationData data = FleckMaker.GetDataStatic(loc, map, def, scale);
            data.instanceColor = color;
            data.rotationRate = Rand.Range(-25f, 25f);
            data.rotation = Rand.Range(0f, 360f);
            data.velocityAngle = angle;
            data.velocitySpeed = speed;
            map.flecks.CreateFleck(data);
        }

        private static Color GlintColor(GasTypeDef gas, GasVisualExtension ext)
        {
            Color c = ext.glintColor ?? Color.Lerp(CloudColor(gas), Color.white, 0.55f);
            c.a = 1f;
            return c;
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }

    /// <summary>Coloured, animated clouds for every framework gas, drawn with vanilla's pollution-cloud
    /// shader (texture x vertex colour, with a scrolling fade texture). Replaces the framework's overlay,
    /// which renders pale and colourless on 1.7.</summary>
    [StaticConstructorOnStartup]
    public class SectionLayer_GasCloudColored : SectionLayer_Gas
    {
        private static Material mat;
        private Color cellColor;

        protected override FloatRange VertexScaleOffsetRange => new FloatRange(0.45f, 0.8f);
        protected override FloatRange VertexPositionOffsetRange => new FloatRange(-0.25f, 0.25f);

        public SectionLayer_GasCloudColored(Section section) : base(section)
        {
            relevantChangeTypes = MapMeshFlagDefOf.Gas;
        }

        public override bool Visible => GasVisuals.Enabled && (CustomGasUtility.GetGrid(Map)?.AnyGasActive ?? false);

        public override Material Mat
        {
            get
            {
                if (mat == null)
                {
                    Texture2D cloud = ContentFinder<Texture2D>.Get("Things/Gas/GasCloudThickA", false) ?? BaseContent.WhiteTex;
                    mat = MaterialPool.MatFrom(cloud, ShaderDatabase.PollutionCloud, Color.white, 3001);
                    Texture2D fade = ContentFinder<Texture2D>.Get("Other/PollutionCloudFade", false) ?? cloud;
                    mat.SetTexture(ShaderPropertyIDs.FadeTex, fade);
                    mat.SetVector(ShaderPropertyIDs.TexScrollSpeed, new Vector4(0.05f, 0.02f, 0f, 0f));
                    mat.SetVector(ShaderPropertyIDs.TexScale, new Vector4(0.3f, 0.3f, 0f, 0f));
                }
                return mat;
            }
        }

        public override Color ColorAt(IntVec3 cell) => cellColor;

        public override void Regenerate()
        {
            ClearSubMeshes(MeshParts.All);
            MapComponent_CustomGasGrid grid = CustomGasUtility.GetGrid(Map);
            if (grid == null || !grid.AnyGasActive || !GasVisuals.Enabled) return;
            Dictionary<GasTypeDef, byte[]> grids = GasVisuals.Grids(grid);
            LayerSubMesh sm = GetSubMesh(Mat);
            float altitude = AltitudeLayer.Gas.AltitudeFor();
            CellIndices indices = Map.cellIndices;
            foreach (IntVec3 c in section.CellRect)
            {
                int index = indices.CellToIndex(c);
                if (!GasVisuals.CellColor(grids, index, out cellColor, out _)) continue;
                AddCell(c, index, sm.verts.Count, sm, altitude);
            }
            if (sm.verts.Count > 0) sm.FinalizeMesh(MeshParts.All);
        }
    }

    /// <summary>Drifting wisps over clouds in view, a few per tenth of a second.</summary>
    public class MapComponent_GasVisuals : MapComponent
    {
        private float timer;

        public MapComponent_GasVisuals(Map map) : base(map) { }

        public override void MapComponentUpdate()
        {
            if (!GasVisuals.Enabled || map != Find.CurrentMap || Find.TickManager.Paused) return;
            timer += Time.deltaTime * Mathf.Min(Find.TickManager.TickRateMultiplier, 3f);
            if (timer < 0.1f) return;
            timer = 0f;
            try
            {
                MapComponent_CustomGasGrid grid = CustomGasUtility.GetGrid(map);
                if (grid == null || !grid.AnyGasActive) return;
                Dictionary<GasTypeDef, byte[]> grids = GasVisuals.Grids(grid);
                CellRect view = Find.CameraDriver.CurrentViewRect.ClipInsideMap(map);
                if (view.Area <= 0) return;
                int thrown = 0;
                for (int i = 0; i < 30 && thrown < 3; i++)
                {
                    IntVec3 c = view.RandomCell;
                    if (!GasVisuals.CellColor(grids, map.cellIndices.CellToIndex(c), out Color col, out GasTypeDef gas) || col.a < 0.3f) continue;
                    Vector3 loc = c.ToVector3Shifted() + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.4f, 0.4f));
                    if (!loc.ShouldSpawnMotesAt(map)) continue;
                    GasVisuals.Wisp(map, loc, gas, col);
                    thrown++;
                }
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("wisps", "Gas wisps failed: " + e);
                GasVisuals.Enabled = false;
            }
        }
    }

    [HarmonyPatch(typeof(SectionLayer_CustomGas), nameof(SectionLayer_CustomGas.Visible), MethodType.Getter)]
    public static class Patch_FrameworkGasLayer_Hide
    {
        public static void Postfix(ref bool __result)
        {
            if (GasVisuals.Enabled) __result = false;
        }
    }

    [HarmonyPatch(typeof(CustomGasUtility), nameof(CustomGasUtility.AddGasRadial))]
    public static class Patch_AddGasRadial_Burst
    {
        public static void Postfix(IntVec3 center, Map map, GasTypeDef gasType, int amountAtCenter, float radius)
        {
            try
            {
                GasVisuals.Burst(map, center, gasType, amountAtCenter, radius);
            }
            catch (Exception e)
            {
                GasLog.ErrorOnce("burst", "Gas release puffs failed: " + e);
            }
        }
    }
}
