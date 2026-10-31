using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKTacticalAI
{
    /// <summary>
    /// The mod settings window (0.4.0, after HSK dev feedback): sections with headers and dividing lines; one fixed-height row
    /// per setting, label on the left and control on the right; options that depend on another stay in place, greyed out,
    /// with the reason on hover; numbers as an input field next to a slider; a marker on values changed from the default and a
    /// reset per section (with confirmation); a search box. Labels and tooltips are Keyed (Languages/*/Keyed): label key
    /// HSKTAI_S_&lt;field&gt;, tooltip HSKTAI_S_&lt;field&gt;_Tip.
    /// </summary>
    public static class SettingsWindow
    {
        private enum Kind { Check, Slider }

        private class Row
        {
            public string field;
            public Kind kind;
            public float min, max;
            public float scale = 1f;          // shown value = stored value / scale (ticks shown as seconds: 60)
            public string unit = "";          // "%", " s", "Â°", " ms"
            public int decimals;
            public string[] needs = new string[0];   // bool settings that must be on
            public FieldInfo fi;
        }

        private class Section
        {
            public string key;
            public readonly List<Row> rows = new List<Row>();
        }

        private const float RowH = 30f, HeaderH = 36f, ControlW = 300f, FieldW = 72f;
        private static readonly List<Section> sections = Build();
        private static readonly TacticalAISettings defaults = new TacticalAISettings();
        private static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();
        private static Vector2 scroll;
        private static string search = "";
        private static float lastHeight = 2000f;

        // ---------------- the table ----------------

        private static List<Section> Build()
        {
            var list = new List<Section>();
            Section cur = null;
            void Sec(string key) { cur = new Section { key = key }; list.Add(cur); }
            void Check(string f, params string[] needs) => cur.rows.Add(new Row { field = f, kind = Kind.Check, needs = needs });
            void Num(string f, float min, float max, int dec, string unit, float scale, params string[] needs) =>
                cur.rows.Add(new Row { field = f, kind = Kind.Slider, min = min, max = max, decimals = dec, unit = unit, scale = scale, needs = needs });

            Sec("General");
            Check("enabled");
            Check("tacticalCompetence");
            Num("competenceIntelWeight", 0f, 100f, 0, "%", 0.01f, "tacticalCompetence");
            Check("competenceLossOfCommand", "tacticalCompetence");
            Check("competenceReadout", "tacticalCompetence");
            Check("hskItemsOnly");
            Check("equipRaiders");
            Check("breachGear", "assaultPlanner");

            Sec("ThreatMap");
            Check("enableThreatMap");
            Check("showThreatOverlay", "enableThreatMap");
            Num("budgetMsPerTick", 0.2f, 4f, 1, " ms", 1f, "enableThreatMap");
            Num("cycleTicksCombat", 30f, 600f, 0, "", 1f, "enableThreatMap");
            Num("cycleTicksPeace", 120f, 5000f, 0, "", 1f, "enableThreatMap");
            Num("weightDrafted", 0f, 3f, 2, "", 1f, "enableThreatMap");
            Num("weightUndrafted", 0f, 3f, 2, "", 1f, "enableThreatMap");
            Num("weightTurret", 0f, 3f, 2, "", 1f, "enableThreatMap");

            Sec("Planning");
            Check("assaultPlanner");
            Num("maxAxes", 1f, 4f, 0, "", 1f, "assaultPlanner");
            Num("minAxisSeparation", 30f, 150f, 0, "Â°", 1f, "assaultPlanner");
            Num("syncTimeoutTicks", 10f, 200f, 0, " s", 60f, "assaultPlanner");
            Num("routeCostPerFire", 0f, 2000f, 0, "", 1f, "assaultPlanner");
            Check("syncedAssault", "assaultPlanner");
            Check("forceRatioCheck", "assaultPlanner");
            Num("assaultOdds", 1f, 5f, 1, " : 1", 1f, "assaultPlanner", "forceRatioCheck");
            Num("standoffTicks", 20f, 500f, 0, " s", 60f, "assaultPlanner");
            Check("avoidFunnels", "assaultPlanner");
            Check("openCrossing", "assaultPlanner");
            Check("flanking", "assaultPlanner");
            Check("focusFire", "assaultPlanner");
            Check("raidingParty", "assaultPlanner");
            Check("plunder", "assaultPlanner");
            Check("satisfiedHold", "assaultPlanner");
            Check("funnelCaution", "assaultPlanner");
            Check("plannedWithdrawal", "assaultPlanner");

            Sec("Breaching");
            Check("smartBreaching");
            Num("exposureCost", 0f, 300f, 0, "", 1f, "smartBreaching");
            Num("wallPenalty", 0f, 2000f, 0, "", 1f, "smartBreaching");
            Num("wallHpPenalty", 0f, 2f, 2, "", 1f, "smartBreaching");
            Num("naturalWallExtraCost", 20f, 5000f, 0, "", 1f, "smartBreaching");
            Num("stagingDistance", 6f, 25f, 0, "", 1f, "assaultPlanner");
            Num("breachConeDegrees", 20f, 85f, 0, "Â°", 1f, "assaultPlanner");
            Num("maxBreaches", 1f, 4f, 0, "", 1f, "assaultPlanner");
            Check("smokeOnBreach", "assaultPlanner");
            Check("cornerFire", "assaultPlanner");
            Check("fireBreaching", "assaultPlanner");
            Check("tunnelling", "assaultPlanner");
            Check("doorBreach", "assaultPlanner");
            Check("breachKit", "assaultPlanner", "equipRaiders");
            Num("chargesPerSpecialist", 1f, 5f, 0, "", 1f, "assaultPlanner", "equipRaiders", "breachKit");
            Check("grenadeKit", "assaultPlanner", "equipRaiders");
            Check("gasRaids", "assaultPlanner");
            Check("gasAmmoSwap", "assaultPlanner");
            Check("indirectFire", "assaultPlanner");
            Check("lobOverWalls", "assaultPlanner");
            Check("launchersStayOut", "assaultPlanner");
            Check("arson", "assaultPlanner");

            Sec("Combat");
            Check("keepRange", "assaultPlanner");
            Check("fieldFight", "assaultPlanner");
            Check("combatMicro", "assaultPlanner");
            Check("evasion", "assaultPlanner");
            Check("medics", "assaultPlanner");
            Check("coverCarry", "assaultPlanner");
            Check("contactReaction", "assaultPlanner");
            Check("crewWeapons", "assaultPlanner");
            Check("shieldTactics", "assaultPlanner");
            Check("fireDoctrine", "assaultPlanner");
            Check("realSuppression");
            Check("posture");
            Check("proneVisual", "posture");
            Check("proneLabel", "posture");
            Check("runAndGunByRole");
            Check("nolbRescueFilter");

            Sec("Sieges");
            Check("siegeGuards");
            Check("siegePackAnimals");

            Sec("Memory");
            Check("raidMemory");
            Check("adaptiveLoadouts", "raidMemory");
            Check("prisonersReport", "raidMemory");
            Check("escalation", "raidMemory");
            Num("escalationSize", 0f, 100f, 0, "%", 0.01f, "raidMemory", "escalation");
            Check("arrivalChoice", "raidMemory");
            Check("raidLetters");
            Check("raidGoals", "assaultPlanner");
            Num("scoutShare", 0f, 1f, 2, "", 1f, "assaultPlanner", "raidGoals");
            Num("sabotageShare", 0f, 1f, 2, "", 1f, "assaultPlanner", "raidGoals");
            Check("raidIntents", "assaultPlanner");
            Num("slavingShare", 0f, 1f, 2, "", 1f, "assaultPlanner", "raidIntents");
            Num("punitiveShare", 0f, 2f, 2, "", 1f, "assaultPlanner", "raidIntents");
            Check("operations", "assaultPlanner");
            Num("operationChance", 0f, 100f, 0, "%", 0.01f, "assaultPlanner", "operations");

            foreach (var s in list)
                foreach (var r in s.rows)
                {
                    r.fi = typeof(TacticalAISettings).GetField(r.field, BindingFlags.Public | BindingFlags.Instance);
                    if (r.fi == null) Log.Error("[HSK Tactical AI] settings window: no setting named " + r.field);
                }
            foreach (var s in list) s.rows.RemoveAll(r => r.fi == null);
            return list;
        }

        // ---------------- values ----------------

        private static float Get(TacticalAISettings s, Row r)
        {
            object v = r.fi.GetValue(s);
            return v is int i ? i : v is float f ? f : v is bool b ? (b ? 1f : 0f) : 0f;
        }

        private static void Set(TacticalAISettings s, Row r, float stored)
        {
            if (r.fi.FieldType == typeof(int)) r.fi.SetValue(s, Mathf.RoundToInt(stored));
            else if (r.fi.FieldType == typeof(float)) r.fi.SetValue(s, stored);
            else if (r.fi.FieldType == typeof(bool)) r.fi.SetValue(s, stored > 0.5f);
        }

        private static bool IsDefault(TacticalAISettings s, Row r) => Mathf.Abs(Get(s, r) - Get(defaults, r)) < 0.0001f;

        private static string Shown(Row r, float stored)
        {
            float v = stored / r.scale;
            return v.ToString("F" + r.decimals) + r.unit;
        }

        private static string Label(Row r) => ("HSKTAI_S_" + r.field).Translate();

        private static string Tip(Row r)
        {
            string k = "HSKTAI_S_" + r.field + "_Tip";
            return k.CanTranslate() ? k.Translate().ToString() : "";
        }

        /// <summary>Why the row is greyed out, or null.</summary>
        private static string Blocked(TacticalAISettings s, Row r)
        {
            if (r.field != "enabled" && !s.enabled) return "HSKTAI_UI_ModOff".Translate();
            foreach (string n in r.needs)
            {
                var fi = typeof(TacticalAISettings).GetField(n);
                if (fi != null && !(bool)fi.GetValue(s)) return "HSKTAI_UI_Needs".Translate(("HSKTAI_S_" + n).Translate().ToString());
            }
            return null;
        }

        private static bool Visible(Section sec, Row r)
        {
            if (search.NullOrEmpty()) return true;
            string q = search.ToLowerInvariant();
            return Label(r).ToLowerInvariant().Contains(q) || Tip(r).ToLowerInvariant().Contains(q)
                   || ("HSKTAI_Sec_" + sec.key).Translate().ToString().ToLowerInvariant().Contains(q);
        }

        /// <summary>AI Lab settings-window test: scroll position and search text.</summary>
        public static void LabView(float scrollY, string searchText)
        {
            scroll = new Vector2(0f, Mathf.Min(scrollY, Mathf.Max(0f, lastHeight - 400f)));
            search = searchText ?? "";
        }

        // ---------------- drawing ----------------

        public static void Draw(TacticalAISettings s, Rect inRect)
        {
            // header: version, search
            Text.Font = GameFont.Small;
            var top = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            Widgets.Label(new Rect(top.x, top.y + 4f, top.width - 260f, 26f), "HSK Tactical AI " + TacticalAIMod.Version);
            var searchRect = new Rect(top.xMax - 250f, top.y, 250f, 28f);
            search = Widgets.TextField(searchRect, search ?? "");
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 6f, searchRect.y + 4f, searchRect.width, 24f), "HSKTAI_UI_Search".Translate());
                GUI.color = Color.white;
            }
            float y0 = top.yMax + 6f;

            // patches that could not be applied at load (renamed after an update)
            if (TacticalAIMod.FailedPatches.Count > 0)
            {
                string msg = "HSKTAI_UI_FailedPatches".Translate(string.Join(", ", TacticalAIMod.FailedPatches));
                float h = Text.CalcHeight(msg, inRect.width);
                GUI.color = new Color(1f, 0.45f, 0.4f);
                Widgets.Label(new Rect(inRect.x, y0, inRect.width, h), msg);
                GUI.color = Color.white;
                y0 += h + 4f;
            }

            var outer = new Rect(inRect.x, y0, inRect.width, inRect.yMax - y0);
            var view = new Rect(0f, 0f, outer.width - 16f, lastHeight);
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;
            foreach (var sec in sections)
            {
                var rows = sec.rows.Where(r => Visible(sec, r)).ToList();
                if (rows.Count == 0) continue;
                y = DrawHeader(s, sec, view.width, y);
                foreach (var r in rows) { DrawRow(s, r, new Rect(0f, y, view.width, RowH)); y += RowH; }
                y += 8f;
            }
            lastHeight = Mathf.Max(y, 10f);
            Widgets.EndScrollView();
            s.threatAdvance = s.focusFire;   // no checkbox of its own: the covered advance belongs to focus fire
        }

        private static float DrawHeader(TacticalAISettings s, Section sec, float width, float y)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y + 2f, width - 120f, HeaderH), ("HSKTAI_Sec_" + sec.key).Translate());
            Text.Font = GameFont.Small;
            int changed = sec.rows.Count(r => !IsDefault(s, r));
            var reset = new Rect(width - 110f, y + 6f, 110f, 24f);
            if (changed > 0 && Widgets.ButtonText(reset, "HSKTAI_UI_Reset".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "HSKTAI_UI_ResetConfirm".Translate(("HSKTAI_Sec_" + sec.key).Translate().ToString(), changed.ToString()),
                    () =>
                    {
                        foreach (var r in sec.rows)
                        {
                            if (r.field == "enabled") { if (!s.enabled) TacticalAIMod.SetEnabled(true); continue; }
                            Set(s, r, Get(defaults, r));
                            buffers.Remove(r.field);
                        }
                    }, destructive: true));
            }
            y += HeaderH;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawLineHorizontal(0f, y - 2f, width);
            GUI.color = Color.white;
            return y + 2f;
        }

        private static void DrawRow(TacticalAISettings s, Row r, Rect rect)
        {
            string blocked = Blocked(s, r);
            bool changed = !IsDefault(s, r);
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            string tip = Tip(r);
            if (changed) tip += (tip.Length > 0 ? "\n\n" : "") + "HSKTAI_UI_Default".Translate(r.kind == Kind.Check ? (Get(defaults, r) > 0.5f ? "HSKTAI_UI_On".Translate() : "HSKTAI_UI_Off".Translate()).ToString() : Shown(r, Get(defaults, r)));
            if (blocked != null) tip = blocked + (tip.Length > 0 ? "\n\n" + tip : "");
            if (tip.Length > 0) TooltipHandler.TipRegion(rect, tip);

            // changed-from-default marker
            if (changed)
            {
                GUI.color = new Color(1f, 0.85f, 0.3f);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 8f, 3f, RowH - 16f), GUI.color);
            }
            GUI.color = blocked != null ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - ControlW - 20f, rect.height), Label(r));
            Text.Anchor = TextAnchor.UpperLeft;

            var ctrl = new Rect(rect.xMax - ControlW, rect.y, ControlW, rect.height);
            if (r.kind == Kind.Check)
            {
                bool v = Get(s, r) > 0.5f, was = v;
                Widgets.Checkbox(new Vector2(ctrl.xMax - 24f, ctrl.y + (RowH - 24f) / 2f), ref v, 24f, blocked != null);
                if (v != was && blocked == null)
                {
                    if (r.field == "enabled") TacticalAIMod.SetEnabled(v);
                    else Set(s, r, v ? 1f : 0f);
                }
            }
            else DrawNumber(s, r, ctrl, blocked != null);
            GUI.color = Color.white;
        }

        private static void DrawNumber(TacticalAISettings s, Row r, Rect ctrl, bool disabled)
        {
            float stored = Get(s, r);
            float shown = stored / r.scale;
            var field = new Rect(ctrl.x, ctrl.y + 3f, FieldW, RowH - 6f);
            var unit = new Rect(field.xMax + 3f, ctrl.y, 36f, RowH);
            var slider = new Rect(unit.xMax + 4f, ctrl.y + 4f, ctrl.xMax - unit.xMax - 8f, RowH - 8f);
            if (disabled)
            {
                Widgets.Label(field, Shown(r, stored));
                return;
            }
            buffers.TryGetValue(r.field, out string buf);
            float edited = shown;
            Widgets.TextFieldNumeric(field, ref edited, ref buf, r.min, r.max);
            buffers[r.field] = buf;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(unit, r.unit.Trim());
            Text.Anchor = TextAnchor.UpperLeft;
            float slid = edited;
            Widgets.HorizontalSlider(slider, ref slid, new FloatRange(r.min, r.max), null, r.decimals == 0 ? 1f : Mathf.Pow(10f, -r.decimals));
            if (Mathf.Abs(slid - edited) > 0.00001f) { edited = slid; buffers.Remove(r.field); }
            if (Mathf.Abs(edited - shown) > 0.00001f) Set(s, r, edited * r.scale);
        }
    }
}
