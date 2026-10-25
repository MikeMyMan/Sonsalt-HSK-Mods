using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HSKTacticalAI
{
    /// <summary>Settings of one automated lab run (Config\HSKTacticalAI_Lab.json).</summary>
    public class LabConfig
    {
        public string runId = "run";
        public string scenario = "killbox";   // killbox | open
        public float points = 1500f;
        public int seed = 12345;
        public int defenders = 5;
        public int defenderShooting = 10;
        public string defenderWeapon = "";
        public string defenderGear = "matchRaid";   // matchRaid: defenders get the raiders' weapons, apparel and skills | fixed: defenderWeapon + defenderShooting
        public string faction = "";
        public int maxTicks = 60000;
        public string outDir = "";
        public bool quitWhenDone = true;
        public bool aiEnabled = true;
        public bool forceRatioCheck = true;
        public int shields = 0;   // give this many raiders a body shield (one-handed gun if needed): shield-wall tests on any faction
        public float attackRatio = 3f;   // raiders per defender (playtest: "the attack ratio should be about 3 to 1"); 0 = keep `defenders`
        public string startScenario = "NakedBrutality";   // ScenarioDef for the map (never Crashlanded: extra pawns)
        public Dictionary<string, string> settings = new Dictionary<string, string>();   // AI variant: setting name -> value (this run only)
        public Dictionary<string, float> memory = new Dictionary<string, float>();       // raid memory seed for the raid faction (FactionRaidMemory field -> value)
    }

    /// <summary>
    /// Dev-only test harness. Active only when the game was started with -quicktest AND the lab config file exists,
    /// so it can never touch a normal game.
    /// </summary>
    public static class AILab
    {
        public static readonly LabConfig Config;
        public static bool Active => Config != null;
        public static string ConfigPath => Path.Combine(GenFilePaths.ConfigFolderPath, "HSKTacticalAI_Lab.json");

        static AILab()
        {
            try
            {
                if (!GenCommandLine.CommandLineArgPassed("quicktest") || !File.Exists(ConfigPath)) return;
                Config = JsonSerializer.Deserialize<LabConfig>(File.ReadAllText(ConfigPath), new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true });
                if (Config != null && string.IsNullOrEmpty(Config.outDir)) Config.outDir = Path.Combine(GenFilePaths.ConfigFolderPath, "HSKTacticalAI_LabResults");
            }
            catch (Exception e)
            {
                Log.Error("[HSK Tactical AI] AI Lab config could not be read: " + e.Message);
                Config = null;
            }
        }
    }

    /// <summary>
    /// Lab runs: quicktest's setup with a deterministic world (seeded RNG) and a chosen start scenario instead of the
    /// hardcoded Crashlanded (which brings extra pawns and things that distract the raid). Default: Naked Brutality
    /// (HSK patches it into "Exiled": one naked colonist, some materials and ship debris scattered).
    /// </summary>
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    public static class Patch_QuickTestSeed
    {
        public static bool Prefix()
        {
            if (!AILab.Active) return true;
            Rand.PushState(AILab.Config.seed);
            try
            {
                Current.ProgramState = ProgramState.Entry;
                Game.ClearCaches();
                Current.Game = new Game();
                Current.Game.InitData = new GameInitData();
                ScenarioDef sd = DefDatabase<ScenarioDef>.GetNamedSilentFail(AILab.Config.startScenario ?? "")
                                 ?? DefDatabase<ScenarioDef>.GetNamedSilentFail("NakedBrutality") ?? ScenarioDefOf.Crashlanded;
                Current.Game.Scenario = sd.scenario;
                Find.Scenario.PreConfigure();
                Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
                Current.Game.World = WorldGenerator.GenerateWorld(0.3f, GenText.RandomSeedString(), OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, LandmarkDensity.Normal);
                Find.GameInitData.ChooseRandomStartingTile();
                Find.GameInitData.mapSize = 250;
                Find.Scenario.PostIdeoChosen();
                Log.Message("[HSK Tactical AI] AI Lab start scenario: " + sd.defName);
            }
            finally { Rand.PopState(); }
            return false;
        }
    }

    public class GameComponent_AILab : GameComponent
    {
        private enum State { Waiting, Settling, Running, Done, Quitting }

        private State state = State.Waiting;
        private Map map;
        private IntVec3 center;
        private CellRect killZone = CellRect.Empty;
        private readonly List<Pawn> defenders = new List<Pawn>();
        private readonly List<Pawn> raiders = new List<Pawn>();
        private readonly List<Pawn> livestock = new List<Pawn>();   // homestead: the colony's animals (plunder targets)
        private Faction raidFaction;
        private int stateTick, raidStartTick, firstDefenderDownTick = -1, firstRaiderDownTick = -1;
        private int doneFrames;
        private string outcome = "";
        private string notes = "";

        // metrics
        private double exposureSum;
        private long raiderTicksAlive, raiderTicksInFire, raiderTicksInKillZone;
        private long raiderProneTicks, raiderSuppressedTicks, raiderHunkerTicks;
        private double threatMsSum, threatMsMax;
        private long ticksRun;
        private readonly Stopwatch realTime = new Stopwatch();

        public GameComponent_AILab(Game game) { }

        public override void GameComponentUpdate()
        {
            if (!AILab.Active || Current.ProgramState != ProgramState.Playing) return;
            // keep the run moving: no pausing dialogs, maximum speed
            var ws = Find.WindowStack;
            if (ws != null)
            {
                foreach (Window w in ws.Windows.ToList())
                    if (w.forcePause || w.absorbInputAroundWindow) ws.TryRemove(w, false);
            }
            if (state == State.Settling || state == State.Running)
            {
                if (Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast) Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            }
            else if (state == State.Waiting && Find.TickManager.Paused)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
            if (state == State.Done && AILab.Config.quitWhenDone && ++doneFrames > 30)
            {
                state = State.Quitting;
                Log.Message("[HSK Tactical AI] AI Lab finished, quitting.");
                Root.Shutdown();
            }
        }

        public override void GameComponentTick()
        {
            if (!AILab.Active) return;
            try
            {
                int now = Find.TickManager.TicksGame;
                switch (state)
                {
                    case State.Waiting:
                        map = Find.CurrentMap;
                        if (map == null || now < 120) return;
                        BuildScenario();
                        state = State.Settling;
                        stateTick = now;
                        break;
                    case State.Settling:
                        if (now - stateTick < 300) return;
                        SpawnRaid();
                        state = raiders.Count > 0 ? State.Running : State.Done;
                        if (raiders.Count == 0) { outcome = "noRaid"; WriteResults(); }
                        raidStartTick = now;
                        realTime.Restart(); Perf.On = true;
                        break;
                    case State.Running:
                        RunTick(now);
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error("[HSK Tactical AI] AI Lab error: " + e);
                outcome = "labError";
                notes += e.Message + " ";
                state = State.Done;
                WriteResults();
            }
        }

        // ---------------- scenario ----------------

        private void BuildScenario()
        {
            var cfg = AILab.Config;
            center = FindOpenCenter();
            var area = CellRect.CenteredOn(center, cfg.scenario == "village" ? 30 : 22).ClipInsideMap(map);
            ClearArea(area);
            ThingDef stuffWall = GenStuff.DefaultStuffFor(ThingDefOf.Wall);
            ThingDef sandbags = DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            var positions = new List<IntVec3>();

            if (cfg.scenario == "fortress")
            {
                // closed 15x15 walled base with a door (raiders can't open it): the raid has to breach
                var bx = CellRect.CenteredOn(center, 7);
                foreach (IntVec3 c in bx.EdgeCells)
                {
                    if (c.x == bx.maxX && c.z == center.z) continue;
                    Place(ThingDefOf.Wall, c, stuffWall);
                }
                Place(ThingDefOf.Door, new IntVec3(bx.maxX, 0, center.z), GenStuff.DefaultStuffFor(ThingDefOf.Door));
                for (int dz = -3; dz <= 3; dz++)
                {
                    if (sandbags != null && dz != 0) Place(sandbags, new IntVec3(bx.maxX - 3, 0, center.z + dz));
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(bx.maxX - 4 - (Math.Abs(dz) % 2), 0, center.z + dz));
                }
                killZone = CellRect.Empty;
            }
            else if (cfg.scenario == "detour")
            {
                // a single wall between the raid and the defenders, open at both ends (~10 cells around):
                // breaking through it is pointless; a sensible raider walks around
                for (int dz = -10; dz <= 10; dz++) Place(ThingDefOf.Wall, center + new IntVec3(9, 0, dz), stuffWall);
                for (int dz = -6; dz <= 6; dz++)
                {
                    if (sandbags != null) Place(sandbags, center + new IntVec3(3, 0, dz));
                    if (Math.Abs(dz) % 2 == 0 && positions.Count < cfg.defenders) positions.Add(center + new IntVec3(2, 0, dz));
                }
                killZone = new CellRect(center.x + 8, center.z - 10, 3, 21).ClipInsideMap(map);
            }
            else if (cfg.scenario == "mountain")
            {
                // a base carved into a rock mass under thick roof: 37x37 rock, 13x13 room inside (every cell within 6.9 of a
                // roof holder, so the thick roof stays up), ONE 1-wide tunnel to the east (the funnel); defenders behind
                // sandbags facing the tunnel mouth
                ThingDef rock = NaturalRockDef();
                // asymmetric like real mountain bases: rock 4 thick to the west, 6 north/south, 12 on the tunnel side (east)
                var room = CellRect.CenteredOn(center, 6);
                var mass = CellRect.FromLimits(room.minX - 4, room.minZ - 6, room.maxX + 12, room.maxZ + 6).ClipInsideMap(map);
                foreach (IntVec3 c in mass)
                {
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick);
                    bool tunnel = c.z == center.z && c.x > room.maxX;
                    if (room.Contains(c) || tunnel) continue;
                    if (rock != null) GenSpawn.Spawn(ThingMaker.MakeThing(rock), c, map);
                }
                for (int dz = -3; dz <= 3; dz++)
                {
                    if (sandbags != null && dz != 0) Place(sandbags, new IntVec3(room.maxX - 3, 0, center.z + dz));
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(room.maxX - 4 - (Math.Abs(dz) % 2), 0, center.z + dz));
                }
                killZone = new CellRect(room.maxX - 3, center.z - 1, mass.maxX - room.maxX + 4, 3).ClipInsideMap(map);
                notes += "rock=" + (rock?.defName ?? "none") + " ";
            }
            else if (cfg.scenario == "bridge")
            {
                // a deep river across the whole map east of the defenders, one wooden bridge facing them and a shallow
                // ford 40 cells away; defenders behind sandbags facing the bridge
                TerrainDef deep = DefDatabase<TerrainDef>.GetNamedSilentFail("WaterDeep") ?? TerrainDefOf.WaterDeep;
                TerrainDef shallow = DefDatabase<TerrainDef>.GetNamedSilentFail("WaterShallow") ?? TerrainDefOf.WaterShallow;
                TerrainDef bridgeT = DefDatabase<TerrainDef>.GetNamedSilentFail("Bridge");
                int rx0 = center.x + 12, rx1 = center.x + 16;
                for (int z = 0; z < map.Size.z; z++)
                for (int x = rx0; x <= rx1; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map)) continue;
                    foreach (Thing t in c.GetThingList(map).ToList())
                        if (!(t is Pawn) && t.def.destroyable && !t.Destroyed) t.Destroy(DestroyMode.Vanish);
                    bool onBridge = Math.Abs(z - center.z) <= 1;
                    bool ford = Math.Abs(z - (center.z + 40)) <= 1 || Math.Abs(z - (center.z - 40)) <= 1;
                    map.terrainGrid.SetTerrain(c, onBridge && bridgeT != null ? bridgeT : ford ? shallow : deep);
                }
                for (int dz = -6; dz <= 6; dz++)
                {
                    if (sandbags != null) Place(sandbags, center + new IntVec3(5, 0, dz));
                    if (Math.Abs(dz) % 3 == 0 && positions.Count < cfg.defenders) positions.Add(center + new IntVec3(4, 0, dz));
                }
                killZone = new CellRect(rx0, center.z - 1, rx1 - rx0 + 1, 3).ClipInsideMap(map);
                notes += "bridge=" + (bridgeT?.defName ?? "none") + " ";
            }
            else if (cfg.scenario == "bunker")
            {
                // a closed, roofed 11x11 room with wooden floor and wooden furniture and one door (east): defenders
                // inside; for gas and heat tactics (and breaching)
                var bx = CellRect.CenteredOn(center, 5);
                TerrainDef woodFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
                ThingDef wood = DefDatabase<ThingDef>.GetNamedSilentFail("WoodLog");
                var edge = new HashSet<IntVec3>(bx.EdgeCells);
                foreach (IntVec3 c in bx)
                {
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    if (edge.Contains(c))
                    {
                        if (c.x == bx.maxX && c.z == center.z) continue;
                        Place(ThingDefOf.Wall, c, stuffWall);
                    }
                    else if (woodFloor != null) map.terrainGrid.SetTerrain(c, woodFloor);
                }
                Place(ThingDefOf.Door, new IntVec3(bx.maxX, 0, center.z), GenStuff.DefaultStuffFor(ThingDefOf.Door));
                string[] furniture = { "Table2x2c", "DiningChair", "Shelf", "Bed" };
                for (int i = 0; i < furniture.Length; i++)
                {
                    ThingDef f = DefDatabase<ThingDef>.GetNamedSilentFail(furniture[i]);
                    if (f == null) continue;
                    bool woody = f.MadeFromStuff && wood != null && f.stuffCategories != null && f.stuffCategories.Contains(StuffCategoryDefOf.Woody);
                    try { Place(f, new IntVec3(bx.minX + 2 + i * 2, 0, bx.minZ + 2), woody ? wood : null); } catch { }
                }
                for (int dz = -2; dz <= 2; dz++)
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(bx.maxX - 3, 0, center.z + dz));
                killZone = CellRect.Empty;
            }
            else if (cfg.scenario == "colony")
            {
                // defenders behind sandbags in the open, three wooden huts (north, south, west) and two ripening crop
                // fields: what an arson raid burns
                for (int dz = -6; dz <= 6; dz++)
                {
                    if (sandbags != null) Place(sandbags, center + new IntVec3(3, 0, dz));
                    if (Math.Abs(dz) % 3 == 0 && positions.Count < cfg.defenders) positions.Add(center + new IntVec3(2, 0, dz));
                }
                ThingDef woodWall = DefDatabase<ThingDef>.GetNamedSilentFail("ClapboardWall") ?? DefDatabase<ThingDef>.GetNamedSilentFail("LogWall") ?? ThingDefOf.Wall;
                ThingDef plank = DefDatabase<ThingDef>.GetNamedSilentFail("WoodPlank") ?? DefDatabase<ThingDef>.GetNamedSilentFail("WoodLog");
                ThingDef wallStuff = woodWall.MadeFromStuff ? (plank != null && GenStuff.AllowedStuffsFor(woodWall).Contains(plank) ? plank : GenStuff.DefaultStuffFor(woodWall)) : null;
                TerrainDef woodFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
                foreach (var off in new[] { new IntVec3(-4, 0, 14), new IntVec3(-4, 0, -14), new IntVec3(-16, 0, 0) })
                {
                    var hut = CellRect.CenteredOn(center + off, 2);
                    var hutEdge = new HashSet<IntVec3>(hut.EdgeCells);
                    foreach (IntVec3 c in hut)
                    {
                        if (!c.InBounds(map)) continue;
                        map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                        if (hutEdge.Contains(c)) { if (c != new IntVec3(hut.maxX, 0, hut.CenterCell.z)) Place(woodWall, c, wallStuff); }
                        else if (woodFloor != null) map.terrainGrid.SetTerrain(c, woodFloor);
                    }
                    Place(ThingDefOf.Door, new IntVec3(hut.maxX, 0, hut.CenterCell.z), plank);
                }
                ThingDef crop = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Corn");
                if (crop != null)
                    foreach (var off in new[] { new IntVec3(-10, 0, 22), new IntVec3(-10, 0, -22) })
                    {
                        var field = CellRect.CenteredOn(center + off, 3).ClipInsideMap(map);
                        var zone = new Zone_Growing(map.zoneManager);
                        map.zoneManager.RegisterZone(zone);
                        zone.SetPlantDefToGrow(crop);
                        foreach (IntVec3 c in field)
                        {
                            if (!c.Standable(map) || c.GetPlant(map) != null) continue;
                            zone.AddCell(c);
                            var p = (Plant)ThingMaker.MakeThing(crop);
                            p.Growth = 0.8f;
                            GenSpawn.Spawn(p, c, map);
                        }
                    }
                killZone = CellRect.Empty;
                notes += "woodWall=" + woodWall.defName + "/" + (wallStuff?.defName ?? "-") + " crop=" + (crop?.defName ?? "none") + " ";
            }
            else if (cfg.scenario == "village")
            {
                // a larger, more realistic colony (playtest: "the tests with the open ground are somewhat very isolated"): a
                // walled 25x25 compound (stone) with doors east and west and sandbag lines behind them, a roofed wooden
                // house inside, a stockpile and workshops; outside two wooden huts, two crop fields, livestock and
                // scattered trees (cover for flanking). Defenders behind both door lines.
                var bx = CellRect.CenteredOn(center, 12);
                foreach (IntVec3 c in bx.EdgeCells)
                {
                    if ((c.x == bx.maxX || c.x == bx.minX) && c.z == center.z) continue;
                    Place(ThingDefOf.Wall, c, stuffWall);
                }
                Place(ThingDefOf.Door, new IntVec3(bx.maxX, 0, center.z), GenStuff.DefaultStuffFor(ThingDefOf.Door));
                Place(ThingDefOf.Door, new IntVec3(bx.minX, 0, center.z), GenStuff.DefaultStuffFor(ThingDefOf.Door));
                for (int dz = -4; dz <= 4; dz++)
                {
                    if (sandbags != null && dz != 0)
                    {
                        Place(sandbags, new IntVec3(bx.maxX - 4, 0, center.z + dz));
                        Place(sandbags, new IntVec3(bx.minX + 4, 0, center.z + dz));
                    }
                }
                foreach (int dz in new[] { -3, 3, -1, 1, -2, 2, 0, 4 })
                {
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(bx.maxX - 5, 0, center.z + dz));
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(bx.minX + 5, 0, center.z + dz));
                }
                // house (roofed, wooden walls) in the north half
                ThingDef logV = DefDatabase<ThingDef>.GetNamedSilentFail("WoodLog");
                var house = CellRect.CenteredOn(center + new IntVec3(0, 0, 6), 3);
                foreach (IntVec3 c in house)
                {
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    if (house.EdgeCells.Contains(c) && c != new IntVec3(house.CenterCell.x, 0, house.minZ)) Place(ThingDefOf.Wall, c, logV);
                }
                Place(ThingDefOf.Door, new IntVec3(house.CenterCell.x, 0, house.minZ), logV);
                // stockpile and workshops in the south half
                var lootV = new (string def, int n)[] { ("Silver", 800), ("ComponentIndustrial", 20), ("MedicineIndustrial", 15), ("MealSurvivalPack", 30), ("Steel", 400) };
                for (int i = 0; i < lootV.Length; i++)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(lootV[i].def);
                    if (d == null) continue;
                    Thing t = ThingMaker.MakeThing(d);
                    t.stackCount = Math.Min(lootV[i].n, d.stackLimit);
                    GenPlace.TryPlaceThing(t, new IntVec3(center.x - 4 + i * 2, 0, center.z - 6), map, ThingPlaceMode.Near);
                }
                string[] shopsV = { "TableMachining", "ElectricSmithy", "SolarGenerator", "Battery" };
                for (int i = 0; i < shopsV.Length; i++)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(shopsV[i]);
                    if (d == null) continue;
                    try { Place(d, new IntVec3(center.x - 6 + i * 4, 0, center.z - 9), d.MadeFromStuff ? ThingDefOf.Steel : null); } catch { }
                }
                // outside: huts, fields, livestock, trees
                foreach (var off in new[] { new IntVec3(0, 0, 22), new IntVec3(0, 0, -22) })
                {
                    var hut = CellRect.CenteredOn(center + off, 2);
                    foreach (IntVec3 c in hut.EdgeCells)
                        if (c != new IntVec3(hut.maxX, 0, hut.CenterCell.z)) Place(ThingDefOf.Wall, c, logV);
                    Place(ThingDefOf.Door, new IntVec3(hut.maxX, 0, hut.CenterCell.z), logV);
                }
                ThingDef cropV = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Corn");
                if (cropV != null)
                    foreach (var off in new[] { new IntVec3(-22, 0, 14), new IntVec3(-22, 0, -14) })
                    {
                        var field = CellRect.CenteredOn(center + off, 3).ClipInsideMap(map);
                        var zone = new Zone_Growing(map.zoneManager);
                        map.zoneManager.RegisterZone(zone);
                        zone.SetPlantDefToGrow(cropV);
                        foreach (IntVec3 c in field)
                        {
                            if (!c.Standable(map) || c.GetPlant(map) != null) continue;
                            zone.AddCell(c);
                            var pl = (Plant)ThingMaker.MakeThing(cropV);
                            pl.Growth = 0.8f;
                            GenSpawn.Spawn(pl, c, map);
                        }
                    }
                foreach (var kind in new[] { "Muffalo", "Cow", "Chicken", "Chicken" })
                {
                    PawnKindDef k = DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Chicken");
                    if (k == null) continue;
                    try
                    {
                        Pawn a = PawnGenerator.GeneratePawn(k, Faction.OfPlayer);
                        GenSpawn.Spawn(a, CellFinder.RandomClosewalkCellNear(center + new IntVec3(-24, 0, 0), map, 4), map);
                        livestock.Add(a);
                    }
                    catch { }
                }
                ThingDef tree = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeOak") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreePoplar");
                if (tree != null)
                {
                    var rnd = new System.Random(cfg.seed * 31 + 7);
                    for (int i = 0; i < 40; i++)
                    {
                        float ang = (float)(rnd.NextDouble() * Math.PI * 2), r = 17f + (float)rnd.NextDouble() * 12f;
                        var c = new IntVec3(center.x + (int)(Math.Cos(ang) * r), 0, center.z + (int)(Math.Sin(ang) * r));
                        if (!c.InBounds(map) || !c.Standable(map) || c.GetPlant(map) != null || c.GetFirstBuilding(map) != null) continue;
                        try { var t = (Plant)ThingMaker.MakeThing(tree); t.Growth = 1f; GenSpawn.Spawn(t, c, map); } catch { }
                    }
                }
                killZone = new CellRect(bx.maxX - 4, center.z - 2, 10, 5).ClipInsideMap(map);
                notes += "village livestock=" + livestock.Count + " ";
            }
            else if (cfg.scenario == "homestead")
            {
                // plunder test: the defenders hold a walled, roofed 9x9 keep whose only way in is a 1-wide, 5-long corridor
                // (a funnel) on the east; around it, in the open: a stockpile of valuables (north), workshops and a generator
                // (south), two wooden huts, livestock (west) and two crop fields. Chasing the defenders means the funnel;
                // the colony itself is out of their field of fire.
                var keep = CellRect.CenteredOn(center, 4);
                foreach (IntVec3 c in keep)
                {
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    if (keep.EdgeCells.Contains(c) && !(c.x == keep.maxX && c.z == center.z)) Place(ThingDefOf.Wall, c, stuffWall);
                }
                for (int dx = 1; dx <= 5; dx++)
                {
                    Place(ThingDefOf.Wall, new IntVec3(keep.maxX + dx, 0, center.z + 1), stuffWall);
                    Place(ThingDefOf.Wall, new IntVec3(keep.maxX + dx, 0, center.z - 1), stuffWall);
                    map.roofGrid.SetRoof(new IntVec3(keep.maxX + dx, 0, center.z), RoofDefOf.RoofConstructed);
                }
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (sandbags != null && dz != 0) Place(sandbags, new IntVec3(keep.maxX - 2, 0, center.z + dz));
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(keep.maxX - 3, 0, center.z + dz));
                }
                killZone = new CellRect(keep.maxX, center.z - 1, 7, 3).ClipInsideMap(map);
                // stockpile north of the keep
                var loot = new (string def, int n)[] { ("Silver", 600), ("ComponentIndustrial", 15), ("MedicineIndustrial", 12), ("MealSurvivalPack", 20), ("Gold", 40), ("Steel", 300) };
                for (int i = 0; i < loot.Length; i++)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(loot[i].def);
                    if (d == null) continue;
                    Thing t = ThingMaker.MakeThing(d);
                    t.stackCount = Math.Min(loot[i].n, d.stackLimit);
                    GenPlace.TryPlaceThing(t, new IntVec3(center.x - 3 + i, 0, keep.maxZ + 4), map, ThingPlaceMode.Near);
                }
                // workshops south of the keep
                ThingDef steel = ThingDefOf.Steel;
                string[] shops = { "TableMachining", "ElectricSmithy", "SolarGenerator", "Battery", "FueledStove" };
                for (int i = 0; i < shops.Length; i++)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(shops[i]);
                    if (d == null) continue;
                    try { Place(d, new IntVec3(center.x - 6 + i * 4, 0, keep.minZ - 6), d.MadeFromStuff ? steel : null); } catch { }
                }
                // two wooden huts (west), livestock, crops
                ThingDef plankH = DefDatabase<ThingDef>.GetNamedSilentFail("WoodLog");
                foreach (var off in new[] { new IntVec3(-14, 0, 8), new IntVec3(-14, 0, -8) })
                {
                    var hut = CellRect.CenteredOn(center + off, 2);
                    foreach (IntVec3 c in hut.EdgeCells)
                        if (c != new IntVec3(hut.maxX, 0, hut.CenterCell.z)) Place(ThingDefOf.Wall, c, plankH);
                    Place(ThingDefOf.Door, new IntVec3(hut.maxX, 0, hut.CenterCell.z), plankH);
                }
                foreach (var kind in new[] { "Muffalo", "Cow", "Chicken", "Chicken" })
                {
                    PawnKindDef k = DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Chicken");
                    if (k == null) continue;
                    try
                    {
                        Pawn a = PawnGenerator.GeneratePawn(k, Faction.OfPlayer);
                        GenSpawn.Spawn(a, CellFinder.RandomClosewalkCellNear(center + new IntVec3(-20, 0, 0), map, 4), map);
                        livestock.Add(a);
                    }
                    catch { }
                }
                ThingDef cropH = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Corn");
                if (cropH != null)
                    foreach (var off in new[] { new IntVec3(-8, 0, 18), new IntVec3(-8, 0, -18) })
                    {
                        var field = CellRect.CenteredOn(center + off, 3).ClipInsideMap(map);
                        var zone = new Zone_Growing(map.zoneManager);
                        map.zoneManager.RegisterZone(zone);
                        zone.SetPlantDefToGrow(cropH);
                        foreach (IntVec3 c in field)
                        {
                            if (!c.Standable(map) || c.GetPlant(map) != null) continue;
                            zone.AddCell(c);
                            var pl = (Plant)ThingMaker.MakeThing(cropH);
                            pl.Growth = 0.8f;
                            GenSpawn.Spawn(pl, c, map);
                        }
                    }
                notes += "homestead livestock=" + livestock.Count + " ";
            }
            else if (cfg.scenario == "open")
            {
                for (int dz = -6; dz <= 6; dz++)
                {
                    if (sandbags != null) Place(sandbags, center + new IntVec3(3, 0, dz));
                    if (Math.Abs(dz) % 2 == 0 && positions.Count < cfg.defenders) positions.Add(center + new IntVec3(2, 0, dz));
                }
                killZone = CellRect.Empty;
            }
            else // killbox
            {
                // 15x15 walled base with one open gap on the east wall (a classic killbox entrance: the only way in
                // without breaching), an 8-cell corridor leading to it, defenders behind sandbags facing the gap
                var bx = CellRect.CenteredOn(center, 7);
                foreach (IntVec3 c in bx.EdgeCells)
                {
                    if (c.x == bx.maxX && c.z == center.z) continue;
                    Place(ThingDefOf.Wall, c, stuffWall);
                }
                for (int dx = 1; dx <= 8; dx++)
                {
                    Place(ThingDefOf.Wall, new IntVec3(bx.maxX + dx, 0, center.z + 1), stuffWall);
                    Place(ThingDefOf.Wall, new IntVec3(bx.maxX + dx, 0, center.z - 1), stuffWall);
                }
                killZone = new CellRect(bx.maxX - 3, center.z - 2, 12, 5).ClipInsideMap(map);
                for (int dz = -3; dz <= 3; dz++)
                {
                    if (sandbags != null && dz != 0) Place(sandbags, new IntVec3(bx.maxX - 3, 0, center.z + dz));
                    if (positions.Count < cfg.defenders) positions.Add(new IntVec3(bx.maxX - 4 - (Math.Abs(dz) % 2), 0, center.z + dz));
                }
            }
            SpawnDefenders(positions);
            notes += "center=" + center + " start=" + (Find.Scenario?.name ?? "?") + " ";
            TacticalAIMod.Settings.smartBreaching = cfg.aiEnabled;   // A/B switch, this run only (not saved)
            TacticalAIMod.Settings.breachKit = cfg.aiEnabled;
            TacticalAIMod.Settings.assaultPlanner = cfg.aiEnabled;
            TacticalAIMod.Settings.forceRatioCheck = cfg.aiEnabled && cfg.forceRatioCheck;
            ApplyVariant(cfg);
            wallsAtStart = CountPlayerWalls();
            flammablesAtStart = CountFlammables();
            colonyBuildingsAtStart = CountColonyBuildings();
            barriersAtStart = BarrierCount();
            TrackWalls();
            MapComponent_TacticalAI.For(map)?.Threat.ForceRebuild();
        }

        private int wallsAtStart, barriersAtStart, firstBreachTick = -1;
        private int colonyBuildingsAtStart;

        /// <summary>Colony buildings that are worth something to lose (not walls, doors, sandbags).</summary>
        private int CountColonyBuildings() =>
            map == null ? 0 : map.listerBuildings.allBuildingsColonist.Count(b => b.Spawned && b.def != ThingDefOf.Wall && !b.def.IsDoor && b.def.defName != "Sandbags");
        private readonly Dictionary<Pawn, int> contactSectorOf = new Dictionary<Pawn, int>();
        private readonly Dictionary<Pawn, string> lastJobOf = new Dictionary<Pawn, string>();
        private readonly Dictionary<Pawn, IntVec3> lastPosOf = new Dictionary<Pawn, IntVec3>();
        private readonly HashSet<Pawn> casualtyLogged = new HashSet<Pawn>();
        private readonly List<Dictionary<string, object>> casualties = new List<Dictionary<string, object>>();
        private readonly Dictionary<int, int> firstContactBySector = new Dictionary<int, int>();
        private long wallAttackTicks;
        private int raidersLostBeforeHHour = -1;

        /// <summary>Player walls and doors: the first one gone marks the first breach.</summary>
        private int BarrierCount()
        {
            int n = 0;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
                if (b.def == ThingDefOf.Wall || b.def.IsDoor) n++;
            return n;
        }

        private int CountPlayerWalls()
        {
            int n = 0;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
                if (b.def == ThingDefOf.Wall) n++;
            return n;
        }

        private IntVec3 FindOpenCenter()
        {
            IntVec3 best = map.Center;
            int bestBad = int.MaxValue;
            for (int ox = -60; ox <= 60; ox += 10)
            for (int oz = -60; oz <= 60; oz += 10)
            {
                var c = map.Center + new IntVec3(ox, 0, oz);
                var r = CellRect.CenteredOn(c, 22);
                if (!r.InBounds(map)) continue;
                int bad = 0;
                foreach (IntVec3 cell in r)
                {
                    if (cell.GetTerrain(map).IsWater) bad += 3;
                    Building ed = cell.GetEdifice(map);
                    if (ed != null && ed.def.mineable) bad += 2;
                    if (cell.Roofed(map)) bad += 1;
                }
                bad += (Math.Abs(ox) + Math.Abs(oz)) / 10;
                if (bad < bestBad) { bestBad = bad; best = c; }
            }
            return best;
        }

        /// <summary>AI variant for A/B tests: overrides TacticalAISettings fields by name for this run (not saved).</summary>
        private void ApplyVariant(LabConfig cfg)
        {
            if (cfg.settings == null || cfg.settings.Count == 0) return;
            var s = TacticalAIMod.Settings;
            foreach (var kv in cfg.settings)
            {
                var f = typeof(TacticalAISettings).GetField(kv.Key);
                if (f == null) { notes += "unknownSetting=" + kv.Key + " "; continue; }
                try
                {
                    object v = f.FieldType == typeof(bool) ? (object)bool.Parse(kv.Value)
                             : f.FieldType == typeof(int) ? int.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture)
                             : f.FieldType == typeof(float) ? float.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture)
                             : (object)kv.Value;
                    f.SetValue(s, v);
                    notes += "set " + kv.Key + "=" + kv.Value + " ";
                }
                catch (Exception e) { notes += "badSetting=" + kv.Key + "(" + e.GetType().Name + ") "; }
            }
        }

        /// <summary>The map's own natural rock (the Minerals mod replaces vanilla rock): the most common non-ore one.</summary>
        private ThingDef NaturalRockDef()
        {
            var counts = new Dictionary<ThingDef, int>();
            foreach (Building b in map.listerBuildings.allBuildingsNonColonist)
            {
                var bd = b.def.building;
                if (bd == null || !bd.isNaturalRock || bd.mineableThing != null && bd.mineableThing.IsStuff == false && bd.mineableThing.defName.IndexOf("Chunk", StringComparison.Ordinal) < 0) continue;
                counts[b.def] = counts.TryGetValue(b.def, out int n) ? n + 1 : 1;
            }
            if (counts.Count > 0) return counts.OrderByDescending(kv => kv.Value).First().Key;
            return DefDatabase<ThingDef>.GetNamedSilentFail("Granite")
                   ?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.building != null && d.building.isNaturalRock);
        }

        private void ClearArea(CellRect r)
        {
            foreach (IntVec3 c in r)
            {
                foreach (Thing t in c.GetThingList(map).ToList())
                    if (!(t is Pawn) && t.def.destroyable && !t.Destroyed) t.Destroy(DestroyMode.Vanish);
                if (map.roofGrid.RoofAt(c) != null) map.roofGrid.SetRoof(c, null);
                if (c.GetTerrain(map).IsWater) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            }
        }

        private void Place(ThingDef def, IntVec3 c, ThingDef stuff = null)
        {
            if (def == null || !c.InBounds(map)) return;
            if (def.MadeFromStuff && stuff == null) stuff = GenStuff.DefaultStuffFor(def);
            Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
            if (t.def.CanHaveFaction) t.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(t, c, map, Rot4.North);
        }

        private void SpawnDefenders(List<IntVec3> positions)
        {
            var cfg = AILab.Config;
            var existing = map.mapPawns.FreeColonistsSpawned.ToList();
            ThingDef weapon = PickWeapon(cfg.defenderWeapon);
            notes += "weapon=" + (weapon?.defName ?? "none") + " ";
            for (int i = 0; i < positions.Count; i++)
            {
                Pawn p = null;
                while (existing.Count > 0 && p == null)
                {
                    var e = existing[0];
                    existing.RemoveAt(0);
                    if (!e.WorkTagIsDisabled(WorkTags.Violent)) { p = e; p.DeSpawn(); }
                }
                for (int tries = 0; p == null && tries < 20; tries++)
                {
                    var g = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    if (!g.WorkTagIsDisabled(WorkTags.Violent)) p = g;
                }
                if (p == null) continue;
                GenSpawn.Spawn(p, positions[i], map);
                if (weapon != null)
                {
                    p.equipment.DestroyAllEquipment();
                    var gun = (ThingWithComps)ThingMaker.MakeThing(weapon, weapon.MadeFromStuff ? GenStuff.DefaultStuffFor(weapon) : null);
                    p.equipment.AddEquipment(gun);
                    GiveCEAmmo(p, gun, 200);
                }
                var sk = p.skills?.GetSkill(SkillDefOf.Shooting);
                if (sk != null) sk.Level = cfg.defenderShooting;
                if (p.drafter != null) p.drafter.Drafted = true;
                defenders.Add(p);
            }
            // everything else that could distract the raid is removed: remaining starting colonists, the scenario's
            // animals, and friendly visitors (only the defenders and the raid remain)
            foreach (var e in existing) if (e.Spawned) e.DeSpawn();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (defenders.Contains(p) || livestock.Contains(p)) continue;
                bool ours = p.Faction == Faction.OfPlayer;
                bool visitor = p.Faction != null && !p.Faction.HostileTo(Faction.OfPlayer) && p.RaceProps.Humanlike;
                if ((ours || visitor) && p.Spawned) p.DeSpawn();
            }
        }

        private static ThingDef PickWeapon(string wanted)
        {
            if (!string.IsNullOrEmpty(wanted))
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(wanted);
                if (d != null) return d;
            }
            foreach (var name in new[] { "Gun_AssaultRifle", "Gun_BoltActionRifle", "Gun_AK47" })
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (d != null) return d;
            }
            return DefDatabase<ThingDef>.AllDefs.Where(d => d.IsRangedWeapon && d.equipmentType == EquipmentType.Primary && d.techLevel == TechLevel.Industrial && !d.destroyOnDrop)
                .OrderByDescending(d => d.BaseMarketValue).FirstOrDefault();
        }

        /// <summary>Combat Extended guns need ammo in the inventory; found by reflection so CE stays optional.</summary>
        private static void GiveCEAmmo(Pawn p, ThingWithComps gun, int count)
        {
            foreach (var comp in gun.AllComps)
            {
                if (comp.GetType().Name != "CompAmmoUser") continue;
                object props = comp.props;
                object ammoSet = props?.GetType().GetField("ammoSet")?.GetValue(props);
                var list = ammoSet?.GetType().GetField("ammoTypes")?.GetValue(ammoSet) as System.Collections.IList;
                if (list == null || list.Count == 0) return;
                var ammo = list[0]?.GetType().GetField("ammo")?.GetValue(list[0]) as ThingDef;
                if (ammo == null) return;
                Thing t = ThingMaker.MakeThing(ammo);
                t.stackCount = count;
                p.inventory.innerContainer.TryAdd(t);
                return;
            }
        }

        /// <summary>Raid memory seed (two-run memory tests: run B starts with what run A learned).</summary>
        private void SeedMemory(Faction f)
        {
            var cfg = AILab.Config;
            if (cfg.memory == null || cfg.memory.Count == 0) return;
            var mem = GameComponent_RaidMemory.Instance?.For(f, true);
            if (mem == null) { notes += "memorySeed=noComponent "; return; }
            foreach (var kv in cfg.memory)
            {
                var fi = typeof(FactionRaidMemory).GetField(kv.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (fi == null) { notes += "memorySeed?" + kv.Key + " "; continue; }
                if (fi.FieldType == typeof(float)) fi.SetValue(mem, kv.Value);
                else if (fi.FieldType == typeof(int)) fi.SetValue(mem, (int)kv.Value);
            }
            mem.lastDecayTick = Find.TickManager.TicksGame;
            notes += "memorySeed=[" + mem.Describe() + "] ";
        }

        private void SpawnRaid()
        {
            var cfg = AILab.Config;
            // same helper the vanilla raid uses: a hostile faction that can field a combat group at these points;
            // some factions fail to generate a raid - retry with another one (up to 5 attempts)
            notes += "hostiles=" + string.Join("|", Find.FactionManager.AllFactionsListForReading.Where(f => !f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer)).Select(f => f.def.defName).Distinct()) + " ";
            var tried = new HashSet<Faction>();
            Rand.PushState(cfg.seed * 7 + 3);   // same faction choice in every variant of a seed
            try {
            for (int attempt = 0; attempt < 5 && raiders.Count == 0; attempt++)
            {
                bool Wanted(Faction f) => !tried.Contains(f) && f.def.humanlikeFaction
                                          && (string.IsNullOrEmpty(cfg.faction) ? f.def.techLevel >= TechLevel.Industrial : f.def.defName == cfg.faction);
                if (!PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroup(cfg.points, out raidFaction, Wanted, allowNonHumanlike: false)
                    && !PawnGroupMakerUtility.TryGetRandomFactionForCombatPawnGroup(cfg.points, out raidFaction, f => !tried.Contains(f) && f.def.humanlikeFaction, allowNonHumanlike: false))
                {
                    notes += "noHostileFaction ";
                    return;
                }
                tried.Add(raidFaction);
                TrySpawnRaidFrom(raidFaction);
            }
            } finally { Rand.PopState(); }
        }

        private void TrySpawnRaidFrom(Faction faction)
        {
            var cfg = AILab.Config;
            raidFaction = faction;
            var before = new HashSet<Pawn>(map.mapPawns.SpawnedPawnsInFaction(raidFaction));
            var parms = new IncidentParms
            {
                target = map,
                points = cfg.points,
                faction = raidFaction,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                spawnCenter = EastEntryCell(),
                forced = true
            };
            // same raid (faction pick aside) in every variant of a seed: seed the pawn generation
            Rand.PushState(cfg.seed * 104729 + raidFaction.loadID);
            bool ok;
            try { ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms); }
            finally { Rand.PopState(); }
            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(raidFaction))
                if (!before.Contains(p)) raiders.Add(p);
            notes += "faction=" + raidFaction.def.defName + " raidOk=" + ok + " entry=" + parms.spawnCenter + " ";
            // 3 to 1: the defenders are a third of the raid (at least one)
            if (raiders.Count > 0 && cfg.attackRatio > 0f)
            {
                int want = Math.Max(1, (int)Math.Round(raiders.Count(r => r.RaceProps.Humanlike) / cfg.attackRatio));
                while (defenders.Count > want) { var d = defenders[defenders.Count - 1]; defenders.RemoveAt(defenders.Count - 1); if (d.Spawned) d.DeSpawn(); }
                notes += "defenders=" + defenders.Count + " (raid " + raiders.Count + ", ratio " + cfg.attackRatio + ") ";
            }
            if (raiders.Count > 0 && cfg.defenderGear == "matchRaid") MatchDefendersToRaid();
            // shields after matching: defenders get the raid's original guns, never the shields or the swapped one-handers
            if (raiders.Count > 0 && cfg.shields > 0) GiveShields(cfg.shields);
            if (raiders.Count > 0 && cfg.defenderGear == "matchRaid") MapComponent_TacticalAI.For(map)?.Threat.RebuildNow();
            else MapComponent_TacticalAI.For(map)?.Threat.ForceRebuild();
            if (raiders.Count > 0 && cfg.defenderGear == "matchRaid")
            {
                // the raid planned the moment it spawned, against the old defenders: plan again against the matched ones
                Lord lord = raiders[0].GetLord();
                SeedMemory(raidFaction);   // after the defenders copied the raid's kit: the re-plan adapts from memory
                if (lord != null && AssaultPlanner.Plans.Remove(lord.loadID)) lord.CurLordToil?.UpdateAllDuties();
            }
            attackerScore = ForceEstimate.Attackers(raiders);
            defenderScore = defenders.Sum(ForceEstimate.PawnScore);
            attackerGear = raiders.Sum(GearValue);
            defenderGear = defenders.Sum(GearValue);
        }

        private float attackerScore, defenderScore, attackerGear, defenderGear;
        private long defenderSuppressedTicks, defenderHunkerTicks;
        private int withdrawStartTick = -1, lostAfterWithdraw;
        private readonly HashSet<int> throwsSeen = new HashSet<int>();   // CE opportunistic throws (ours and CE's own), by job id
        private int flammablesAtStart;

        /// <summary>Arson targets: flammable colony buildings and crops in growing zones.</summary>
        private int CountFlammables()
        {
            int n = 0;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
                if (b.Spawned && b.GetStatValue(StatDefOf.Flammability) >= 0.5f) n++;
            foreach (Zone z in map.zoneManager.AllZones)
                if (z is Zone_Growing)
                    foreach (IntVec3 c in z.Cells) if (c.GetPlant(map) is Plant p && p.Spawned) n++;
            return n;
        }

        private static float GearValue(Pawn p) =>
            (p.equipment?.Primary?.MarketValue ?? 0f) + (p.apparel?.WornApparel.Sum(a => a.MarketValue) ?? 0f);

        /// <summary>
        /// Realistic odds: each defender takes the kit of one of the raid's best-armed raiders (ranged first, by weapon value)
        /// - a copy of the weapon (with CE ammo), the apparel it can wear, and the Shooting / Melee skill levels.
        /// </summary>
        /// <summary>
        /// A gun a colony facing this raid would own: tech level up to the faction's (at least Industrial for gun
        /// factions, Medieval for low-tech raids), value closest to the raid's mean weapon value. Deterministic.
        /// </summary>
        private static ThingDef ColonyGunFor(Faction f, List<Pawn> armed)
        {
            float target = armed.Count > 0 ? armed.Average(r => r.equipment.Primary.MarketValue) : 300f;
            TechLevel tech = f?.def.techLevel ?? TechLevel.Industrial;
            if (tech < TechLevel.Medieval) tech = TechLevel.Medieval;
            // the faction's own gun pool (weapon tags its pawn kinds spawn with); else common gun tags
            var tags = new HashSet<string>();
            foreach (var gm in f?.def.pawnGroupMakers ?? new List<PawnGroupMaker>())
                foreach (var o in gm.options)
                    if (o.kind?.weaponTags != null) foreach (var t in o.kind.weaponTags) tags.Add(t);
            bool Gun(ThingDef d) => d.IsRangedWeapon && d.equipmentType == EquipmentType.Primary && !d.destroyOnDrop && !OneShot(d) && d.techLevel <= tech
                                     && d.techLevel >= TechLevel.Medieval && d.weaponTags != null && !d.defName.Contains("Flare")
                                     && !d.Verbs.Any(v => v.defaultProjectile?.projectile?.explosionRadius > 0f || (v.defaultProjectile?.projectile?.flyOverhead ?? false));
            var pool = DefDatabase<ThingDef>.AllDefs.Where(d => Gun(d) && d.weaponTags.Any(tags.Contains)).ToList();
            if (pool.Count == 0)
                pool = DefDatabase<ThingDef>.AllDefs.Where(d => Gun(d) && d.weaponTags.Any(t => t == "SimpleGun" || t == "IndustrialGunAdvanced")).ToList();
            return pool
                .OrderBy(d => Math.Abs(d.BaseMarketValue - target)).ThenBy(d => d.defName).FirstOrDefault();
        }

        /// <summary>A thrown or one-use weapon (grenades, rocket launchers): never a defender's main gun.</summary>
        private static bool OneShot(ThingDef d) =>
            d.Verbs.Any(v => (v.verbClass?.Name.Contains("OneUse") ?? false) || (v.verbClass?.Name.Contains("Throw") ?? false)) || d.IsStuff;

        private void MatchDefendersToRaid()
        {
            // fair test (playtest: "defenders still have better weapons than attackers"): defenders are never better armed than
            // the raid - kits only from the weaker half of the raid's guns (median and below), Shooting / Melee capped at the
            // raid's median, never a grenade or other one-shot as the "gun"; their only edge is the fortification
            // donors: the weaker half of the whole raid by fighting value (the planner's force score: weapon, skill, armour),
            // not of its gunners only - tl1 showed defenders kitted from the weaker half of the guns still scored twice the
            // raid's average per head (its melee fighters, grenadiers and medics pull the average down). A melee donor gives
            // a melee kit: the raid's own brawlers can't shoot either.
            var armed = raiders.Where(r => r.RaceProps.Humanlike && r.equipment?.Primary != null && !OneShot(r.equipment.Primary.def)).ToList();
            var donors = armed.OrderBy(ForceEstimate.PawnScore).ThenBy(r => r.thingIDNumber).Take(Math.Max(1, (armed.Count + 1) / 2)).ToList();
            if (donors.Count == 0) return;
            bool raidHasGuns = armed.Any(r => r.equipment.Primary.def.IsRangedWeapon);
            int MedianSkill(SkillDef sk)
            {
                var lv = raiders.Where(r => r.skills != null).Select(r => r.skills.GetSkill(sk)?.Level ?? 0).OrderBy(x => x).ToList();
                return lv.Count == 0 ? 20 : lv[lv.Count / 2];
            }
            int medShoot = MedianSkill(SkillDefOf.Shooting), medMelee = MedianSkill(SkillDefOf.Melee);
            Rand.PushState(AILab.Config.seed * 7919);
            try
            {
                ThingDef colonyGun = !raidHasGuns ? ColonyGunFor(raidFaction, armed) : null;   // guns only if the raid has none at all
                for (int i = 0; i < defenders.Count; i++)
                {
                    // spread over the weaker half, weakest first; more defenders than donors: cycle
                    int di = donors.Count >= defenders.Count ? Math.Min(donors.Count - 1, (int)((i + 0.5f) * donors.Count / defenders.Count)) : i % donors.Count;
                    Pawn d = defenders[i], src = donors[di];
                    try
                    {
                        var w = src.equipment.Primary;
                        d.equipment.DestroyAllEquipment();
                        // a raid without guns (melee / bows only): the colony's own gun of the raid's tech level and value
                        bool own = colonyGun != null;
                        var gun = own ? (ThingWithComps)ThingMaker.MakeThing(colonyGun, colonyGun.MadeFromStuff ? GenStuff.DefaultStuffFor(colonyGun) : null)
                                      : (ThingWithComps)ThingMaker.MakeThing(w.def, w.Stuff);
                        if (!own && w.TryGetQuality(out QualityCategory q)) gun.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
                        d.equipment.AddEquipment(gun);
                        if (gun.def.IsRangedWeapon) GiveCEAmmo(d, gun, 200);
                        d.apparel?.DestroyAll();
                        foreach (var a in src.apparel?.WornApparel ?? new List<Apparel>())
                        {
                            if (!a.def.apparel.PawnCanWear(d) || !ApparelUtility.HasPartsToWear(d, a.def)) continue;
                            if (a is Apparel && a.GetType().Name == "Apparel_Shield") continue;   // defenders fight from cover: never a raider's shield
                            var copy = (Apparel)ThingMaker.MakeThing(a.def, a.Stuff);
                            if (a.TryGetQuality(out QualityCategory aq)) copy.TryGetComp<CompQuality>()?.SetQuality(aq, ArtGenerationContext.Outsider);
                            if (d.apparel.CanWearWithoutDroppingAnything(copy.def)) d.apparel.Wear(copy, false);
                        }
                        foreach (var sk in new[] { SkillDefOf.Shooting, SkillDefOf.Melee })
                        {
                            var to = d.skills?.GetSkill(sk);
                            var from = src.skills?.GetSkill(sk);
                            if (to != null && from != null) to.Level = Math.Min(from.Level, sk == SkillDefOf.Shooting ? medShoot : medMelee);
                        }
                    }
                    catch (Exception e) { notes += "matchError(" + e.GetType().Name + ") "; }
                }
            }
            finally { Rand.PopState(); }
            notes += "defenderGear=matchRaid [" + string.Join(", ", defenders.Select(d => (d.equipment?.Primary?.def.defName ?? "none") + "/" + (d.equipment?.PrimaryEq?.PrimaryVerb?.GetType().Name ?? "noVerb") + (d.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == true ? "(melee)" : "") + (d.drafter?.Drafted == true ? "" : "(undrafted)"))) + "] ";
        }

        /// <summary>
        /// Shield-wall tests: n raiders (one-handed gunners first, then the rest) get a body shield of the faction's tech
        /// (CE ballistic shield, else the best other body shield); two-handed guns are swapped for the best one-handed gun
        /// the faction could own. Seeded.
        /// </summary>
        private void GiveShields(int n)
        {
            TechLevel tech = raidFaction?.def.techLevel ?? TechLevel.Industrial;
            ThingDef shield = DefDatabase<ThingDef>.GetNamedSilentFail("CE_Apparel_BallisticShield");
            if (shield == null || shield.techLevel > tech)
                shield = DefDatabase<ThingDef>.AllDefs.Where(d => d.IsApparel && ShieldWall.IsBodyShield(d) && d.techLevel <= tech)
                    .OrderByDescending(d => d.BaseMarketValue).ThenBy(d => d.defName).FirstOrDefault();
            if (shield == null) { notes += "shields=none "; return; }
            bool OneHanded(ThingDef d) => d.weaponTags?.Contains("CE_OneHandedWeapon") ?? false;
            var oneHands = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.IsRangedWeapon && OneHanded(d) && d.equipmentType == EquipmentType.Primary && !d.destroyOnDrop && d.techLevel <= tech && d.techLevel >= TechLevel.Medieval
                            && !d.Verbs.Any(v => v.defaultProjectile?.projectile?.explosionRadius > 0f) && !d.defName.Contains("Flare"))
                .OrderBy(d => d.defName).ToList();
            // the one-handed gun closest in value to the gun it replaces (the raid must not get stronger)
            ThingDef OneHandFor(ThingDef old) => oneHands.OrderBy(d => Math.Abs(d.BaseMarketValue - old.BaseMarketValue)).FirstOrDefault();
            ThingDef oneHand = null;
            var cands = raiders.Where(r => r.RaceProps.Humanlike && r.apparel != null && r.equipment?.Primary != null && r.equipment.Primary.def.IsRangedWeapon && !ShieldWall.HasBodyShield(r))
                               .OrderBy(r => OneHanded(r.equipment.Primary.def) ? 0 : 1).ThenBy(r => r.equipment.Primary.MarketValue).ThenBy(r => r.thingIDNumber).Take(n).ToList();
            int given = 0;
            foreach (var r in cands)
            {
                try
                {
                    if (!OneHanded(r.equipment.Primary.def))
                    {
                        oneHand = OneHandFor(r.equipment.Primary.def);
                        if (oneHand == null) continue;
                        r.equipment.DestroyAllEquipment();
                        var gun = (ThingWithComps)ThingMaker.MakeThing(oneHand, oneHand.MadeFromStuff ? GenStuff.DefaultStuffFor(oneHand) : null);
                        r.equipment.AddEquipment(gun);
                        GiveCEAmmo(r, gun, 200);
                    }
                    var sh = (Apparel)ThingMaker.MakeThing(shield, shield.MadeFromStuff ? GenStuff.DefaultStuffFor(shield) : null);
                    r.apparel.Wear(sh, false);
                    given++;
                }
                catch (Exception e) { notes += "shieldError(" + e.GetType().Name + ") "; }
            }
            notes += "shields=" + given + "x" + shield.defName + (oneHand != null ? "/" + oneHand.defName : "") + " ";
        }

        private IntVec3 EastEntryCell()
        {
            int x = map.Size.x - 1;
            for (int d = 0; d < map.Size.z / 2; d++)
            {
                foreach (int z in new[] { center.z + d, center.z - d })
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map) || !c.Standable(map)) continue;
                    if (map.reachability.CanReach(c, center, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) return c;
                }
            }
            return IntVec3.Invalid;
        }

        // ---------------- run & metrics ----------------

        private void RunTick(int now)
        {
            ticksRun++;
            var tm = MapComponent_TacticalAI.For(map)?.Threat;
            if (tm != null)
            {
                threatMsSum += tm.LastTickMs;
                if (tm.LastTickMs > threatMsMax) threatMsMax = tm.LastTickMs;
            }
            int raidersActive = 0, raidersLost = 0;
            foreach (Pawn r in raiders)
            {
                if ((r.Dead || r.Downed) && casualtyLogged.Add(r)) LogCasualty(r, now, tm);
                if (r.Dead || !r.Spawned || r.Downed) { if (r.Downed && firstRaiderDownTick < 0) firstRaiderDownTick = now - raidStartTick; if (r.Dead || r.Downed) raidersLost++; continue; }
                raidersActive++;
                lastJobOf[r] = r.CurJobDef?.defName ?? "none";
                if (r.CurJobDef?.defName == "OpportunisticAttack" && r.CurJob != null) throwsSeen.Add(r.CurJob.loadID);
                lastPosOf[r] = r.Position;
                // contact direction: first time this raider gets within 12 cells of the defenders
                if (!contactSectorOf.ContainsKey(r) && r.Position.DistanceTo(center) <= 12f)
                {
                    int sector = (int)(AssaultPlanner.Bearing(center, r.Position) / 60f) % 6;
                    contactSectorOf[r] = sector;
                    if (!firstContactBySector.ContainsKey(sector)) firstContactBySector[sector] = now - raidStartTick;
                }
                // hitting the player's walls
                Job cj = r.CurJob;
                if (cj != null && cj.targetA.Thing is Building wb && wb.Faction == Faction.OfPlayer && wb.def == ThingDefOf.Wall) wallAttackTicks++;
                raiderTicksAlive++;
                if (Posture.IsProne(r)) raiderProneTicks++;
                int rs = Suppression.State(r);
                if (rs >= 1) raiderSuppressedTicks++;
                if (rs == 2) raiderHunkerTicks++;
                float f = tm?.FireAt(r.Position) ?? 0f;
                exposureSum += f;
                if (f > 0.5f) raiderTicksInFire++;
                if (killZone.Contains(r.Position)) raiderTicksInKillZone++;
            }
            var plan = AssaultPlanner.LastPlan;
            if (plan != null && plan.hHourTick >= 0 && raidersLostBeforeHHour < 0) raidersLostBeforeHHour = raidersLost;
            if (withdrawStartTick < 0 && ((plan != null && plan.withdrawTick >= 0)
                || raiders.Any(r => r.Spawned && (r.MentalStateDef == MentalStateDefOf.PanicFlee || r.GetLord()?.CurLordToil is LordToil_PanicFlee))))
                withdrawStartTick = now - raidStartTick;
            if ((now - raidStartTick) % 300 == 0) Snapshot(now, tm);
            if (firstBreachTick < 0 && (now - raidStartTick) % 30 == 0 && BarrierCount() < barriersAtStart) firstBreachTick = now - raidStartTick;
            int defendersActive = 0;
            foreach (Pawn d in defenders)
            {
                if (d.Dead || d.Downed) { if (firstDefenderDownTick < 0) firstDefenderDownTick = now - raidStartTick; continue; }
                defendersActive++;
                int sup = Suppression.State(d);
                if (sup >= 1) defenderSuppressedTicks++;
                if (sup == 2) defenderHunkerTicks++;
            }
            if (raidersActive == 0)
            {
                // how the raid ended (fn2 / vg1: raiders that carried off downed colonists or left after enough damage were
                // scored as beaten): the lord's last transition decides - kidnap, satisfied (colony damage), planned withdrawal
                string last = Patch_LabTransitions.Last ?? "";
                outcome = last.Contains("Kidnap") ? "raidKidnap"
                        : last.Contains("FractionColonyDamageTaken") ? "raidSatisfied"
                        : Withdrawal.Started > 0 ? "raidWithdrew"
                        : "raidDefeated";
            }
            else if (defendersActive == 0) outcome = "defendersDown";
            else if (now - raidStartTick > AILab.Config.maxTicks)
                outcome = AssaultPlanner.LastPlan != null && AssaultPlanner.LastPlan.mode == PlanMode.Siege ? "siege" : "timeout";
            if (outcome.Length > 0)
            {
                // raid memory: write what the faction learned now (the Lab ends here; the game would do it when the last raider leaves)
                foreach (var pl in AssaultPlanner.Plans.Values) if (pl.map == map) FactionMemory.TryFinish(pl, force: true);
                realTime.Stop();
                state = State.Done;
                WriteResults();
            }
        }

        private readonly List<Dictionary<string, object>> timeline = new List<Dictionary<string, object>>();

        /// <summary>Every 300 ticks: where the raiders are, how exposed, and what they're doing.</summary>
        private void Snapshot(int now, ThreatMap tm)
        {
            int active = 0, inZone = 0, inFire = 0;
            double dist = 0;
            var jobs = new Dictionary<string, int>();
            var toils = new Dictionary<string, int>();
            var targets = new Dictionary<string, int>();
            foreach (Pawn r in raiders)
            {
                if (r.Dead || !r.Spawned || r.Downed) continue;
                active++;
                if (killZone.Contains(r.Position)) inZone++;
                if ((tm?.FireAt(r.Position) ?? 0f) > 0.5f) inFire++;
                dist += r.Position.DistanceTo(center);
                string j = r.CurJobDef?.defName ?? "none";
                jobs[j] = jobs.TryGetValue(j, out int n) ? n + 1 : 1;
                string t = r.GetLord()?.CurLordToil?.GetType().Name ?? "noLord";
                toils[t] = toils.TryGetValue(t, out int m) ? m + 1 : 1;
                string tg = TargetKind(r);
                if (tg != null) targets[tg] = targets.TryGetValue(tg, out int k) ? k + 1 : 1;
            }
            timeline.Add(new Dictionary<string, object>
            {
                ["t"] = now - raidStartTick,
                ["active"] = active,
                ["inKillZone"] = inZone,
                ["inFire"] = inFire,
                ["threatSources"] = tm?.LastSourceCount ?? -1,
                ["maxFire"] = tm != null ? Math.Round(tm.MaxFire, 2) : -1,
                ["meanDistToBase"] = active > 0 ? Math.Round(dist / active, 1) : 0,
                ["jobs"] = jobs,
                ["lordToils"] = toils,
                ["attackTargets"] = targets,
                ["wallDamage"] = WallDamage()
            });
        }

        /// <summary>What an attacking raider is hitting: pawn, one of the player's buildings, rock, plant, other.</summary>
        private static string TargetKind(Pawn r)
        {
            var job = r.CurJob;
            if (job == null || (job.def != JobDefOf.AttackMelee && job.def != JobDefOf.AttackStatic && job.def != JobDefOf.UseVerbOnThing)) return null;
            Thing t = job.targetA.Thing;
            if (t == null) return "cell";
            if (t is Pawn p) return p.Faction == Faction.OfPlayer ? "colonist" : "pawn";
            if (t.def.mineable) return "rock";
            if (t is Plant) return "plant";
            if (t is Building b && b.Faction == Faction.OfPlayer) return "player:" + b.def.defName;
            return "other:" + t.def.defName;
        }

        private readonly List<Building> trackedWalls = new List<Building>();
        private int trackedWallsMaxHp;

        private void TrackWalls()
        {
            trackedWalls.Clear();
            trackedWallsMaxHp = 0;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b.def != ThingDefOf.Wall) continue;
                trackedWalls.Add(b);
                trackedWallsMaxHp += b.MaxHitPoints;
            }
        }

        /// <summary>Hit points knocked off the player's walls since the raid started (destroyed walls count fully).</summary>
        private int WallDamage()
        {
            int left = 0;
            foreach (Building b in trackedWalls) if (!b.Destroyed) left += b.HitPoints;
            return trackedWallsMaxHp - left;
        }

        /// <summary>Where, when and doing what a raider went down (diagnostics for the breach team and the funnel).</summary>
        private void LogCasualty(Pawn r, int now, ThreatMap tm)
        {
            if (withdrawStartTick >= 0) lostAfterWithdraw++;
            IntVec3 pos = r.Spawned ? r.Position : (lastPosOf.TryGetValue(r, out var lp) ? lp : IntVec3.Invalid);
            var plan = AssaultPlanner.LastPlan;
            casualties.Add(new Dictionary<string, object>
            {
                ["t"] = now - raidStartTick,
                ["state"] = r.Dead ? "dead" : "downed",
                ["pos"] = pos.ToString(),
                ["fire"] = pos.IsValid && tm != null ? Math.Round(tm.FireAt(pos), 2) : -1,
                ["distToBase"] = pos.IsValid ? Math.Round(pos.DistanceTo(center), 1) : -1,
                ["inKillZone"] = pos.IsValid && killZone.Contains(pos),
                ["jobBefore"] = lastJobOf.TryGetValue(r, out var lj) ? lj : "none",
                ["breachTeam"] = plan != null && plan.breachTeam.Contains(r),
                ["afterWithdraw"] = withdrawStartTick >= 0,
                ["afterHHour"] = plan != null && plan.hHourTick >= 0
            });
        }

        private List<Dictionary<string, object>> RaiderStates()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (Pawn r in raiders)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["kind"] = r.kindDef?.defName,
                    ["weapon"] = r.equipment?.Primary?.def.defName ?? "none",
                    ["state"] = r.Dead ? "dead" : r.Downed ? "downed" : r.Spawned ? "active" : "left:" + (r.ParentHolder?.GetType().Name ?? "none"),
                    ["lastJob"] = r.CurJobDef?.defName ?? r.jobs?.curDriver?.job?.def?.defName ?? "none",
                    ["lord"] = r.GetLord()?.LordJob?.GetType().Name ?? "none",
                    ["mentalState"] = r.MentalStateDef?.defName ?? ""
                });
            }
            return list;
        }

        private void WriteResults()
        {
            var cfg = AILab.Config;
            var tm = map != null ? MapComponent_TacticalAI.For(map)?.Threat : null;
            int Count(List<Pawn> l, Func<Pawn, bool> f) => l.Count(f);
            var r = new Dictionary<string, object>
            {
                ["runId"] = cfg.runId,
                ["version"] = TacticalAIMod.Version,
                ["scenario"] = cfg.scenario,
                ["seed"] = cfg.seed,
                ["points"] = cfg.points,
                ["aiEnabled"] = cfg.aiEnabled,
                ["outcome"] = outcome,
                ["ticks"] = ticksRun,
                ["realSeconds"] = Math.Round(realTime.Elapsed.TotalSeconds, 1),
                ["ticksPerRealSecond"] = realTime.Elapsed.TotalSeconds > 0 ? Math.Round(ticksRun / realTime.Elapsed.TotalSeconds, 1) : 0,
                ["raiders"] = raiders.Count,
                ["raidersKilled"] = Count(raiders, p => p.Dead),
                ["raidersDowned"] = Count(raiders, p => !p.Dead && p.Downed),
                ["raidersFled"] = Count(raiders, p => !p.Dead && !p.Spawned),
                ["defenders"] = defenders.Count,
                ["defendersKilled"] = Count(defenders, p => p.Dead),
                ["defendersDowned"] = Count(defenders, p => !p.Dead && p.Downed),
                ["ticksToFirstDefenderDown"] = firstDefenderDownTick,
                ["ticksToFirstRaiderDown"] = firstRaiderDownTick,
                ["raiderTicksAlive"] = raiderTicksAlive,
                ["raiderTicksInFire"] = raiderTicksInFire,
                ["raiderTicksInKillZone"] = raiderTicksInKillZone,
                ["meanExposurePerRaiderTick"] = raiderTicksAlive > 0 ? Math.Round(exposureSum / raiderTicksAlive, 3) : 0,
                ["aiMsPerTick"] = ticksRun > 0 ? Math.Round(Perf.TotalMs / ticksRun, 4) : 0,
                ["perfBreakdown"] = Perf.Report(ticksRun),
                ["threatMsPerTickMean"] = ticksRun > 0 ? Math.Round(threatMsSum / ticksRun, 4) : 0,
                ["threatMsPerTickMax"] = Math.Round(threatMsMax, 3),
                ["threatCycles"] = tm?.CompletedCycles ?? 0,
                ["threatLastCycleMs"] = Math.Round(tm?.LastCycleMs ?? 0, 3),
                ["threatSources"] = tm?.LastSourceCount ?? 0,
                ["smartBreaching"] = TacticalAIMod.Settings.smartBreaching,
                ["wallsAtStart"] = wallsAtStart,
                ["wallsDestroyed"] = map != null ? wallsAtStart - CountPlayerWalls() : 0,
                ["wallDamage"] = map != null ? WallDamage() : 0,
                ["wallMaxHp"] = trackedWallsMaxHp,
                ["assaultPlanner"] = TacticalAIMod.Settings.assaultPlanner,
                ["plan"] = AssaultPlanner.LastPlan?.Describe() ?? "",
                ["contactSectors"] = firstContactBySector.Count,
                ["contactSpreadTicks"] = firstContactBySector.Count > 1 ? firstContactBySector.Values.Max() - firstContactBySector.Values.Min() : 0,
                ["firstContactBySector"] = firstContactBySector.ToDictionary(kv => (kv.Key * 60) + "deg", kv => kv.Value),
                ["wallAttackTicks"] = wallAttackTicks,
                ["raidersLostBeforeHHour"] = raidersLostBeforeHHour,
                ["breachKit"] = TacticalAIMod.Settings.breachKit,
                ["ticksToFirstBreach"] = firstBreachTick,
                ["chargesGiven"] = BreachKit.ChargesGiven,
                ["chargesPlanted"] = BreachKit.ChargesPlanted,
                ["thrownGiven"] = BreachKit.ThrownGiven,
                ["miningToolsGiven"] = BreachKit.MiningToolsGiven,
                ["miningChargesGiven"] = BreachKit.MiningChargesGiven,
                ["fragsGiven"] = Grenades.FragsGiven,
                ["smokesGiven"] = Grenades.SmokesGiven,
                ["fragOrders"] = Grenades.FragOrders,
                ["opportunisticFrags"] = Grenades.OpportunisticFrags,
                ["doorThrows"] = Grenades.DoorThrows,
                ["doorBreaches"] = AssaultPlanner.LastPlan?.breaches.Count(b => b.door) ?? 0,
                ["smokeOrders"] = Grenades.SmokeOrders,
                ["throwsTotal"] = throwsSeen.Count,
                ["toolsGiven"] = BreachKit.ToolsGiven,
                ["smokeDeployed"] = BreachKit.SmokeDeployed,
                ["defenderGearMode"] = cfg.defenderGear,
                ["defenderSuppressedTicks"] = defenderSuppressedTicks,
                ["defenderHunkerTicks"] = defenderHunkerTicks,
                ["supportBursts"] = HSKTAI_Stats.SupportBursts,
                ["withdrawStartTick"] = withdrawStartTick,
                ["arsonSites"] = Arson.SitesPlanned,
                ["igniteJobs"] = Arson.IgniteJobs,
                ["flammablesAtStart"] = flammablesAtStart,
                ["flammablesLost"] = map != null ? flammablesAtStart - CountFlammables() : 0,
                ["lostAfterWithdraw"] = lostAfterWithdraw,
                ["withdrawalsPlanned"] = Withdrawal.Started,
                ["lordTransitions"] = string.Join(" ; ", Patch_LabTransitions.Log),
                ["downedCarryJobs"] = Withdrawal.DownedCarried,
                ["pinnedAtHHour"] = AssaultPlanner.LastPlan != null ? Math.Round(AssaultPlanner.LastPlan.pinnedAtHHour, 2) : -1,
                ["overwatchers"] = AssaultPlanner.LastPlan?.overwatchOf.Count ?? 0,
                ["raiderCasts"] = FireDoctrine.RaiderCasts,
                ["defenderCasts"] = FireDoctrine.DefenderCasts,
                ["raiderHits"] = Patch_LabHitCounter.RaiderHits,
                ["defenderHits"] = Patch_LabHitCounter.DefenderHits,
                ["raiderHitsPerCast"] = FireDoctrine.RaiderCasts > 0 ? Math.Round((double)Patch_LabHitCounter.RaiderHits / FireDoctrine.RaiderCasts, 3) : 0,
                ["defenderHitsPerCast"] = FireDoctrine.DefenderCasts > 0 ? Math.Round((double)Patch_LabHitCounter.DefenderHits / FireDoctrine.DefenderCasts, 3) : 0,
                ["fireAimed"] = FireDoctrine.Count(k => k.Contains(":aimed")),
                ["fireSnap"] = FireDoctrine.Count(k => k.Contains(":snap")),
                ["fireSuppress"] = FireDoctrine.Count(k => k.Contains(":suppress")),
                ["fireSingle"] = FireDoctrine.Count(k => k.EndsWith("/single")),
                ["fireBurst"] = FireDoctrine.Count(k => k.EndsWith("/burst")),
                ["fireAuto"] = FireDoctrine.Count(k => k.EndsWith("/auto")),
                ["fireDecisions"] = string.Join(" ", FireDoctrine.Decisions.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)),
                ["fireSkipped"] = string.Join(" ", FireDoctrine.Skipped.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value)),
                ["funnelsSeen"] = Funnels.Seen,
                ["plunderLootJobs"] = Plunder.LootJobs,
                ["plunderLootValue"] = Math.Round(Plunder.LootValue),
                ["plunderKillJobs"] = Plunder.KillJobs,
                ["plunderBurnJobs"] = Plunder.BurnJobs,
                ["plunderWreckJobs"] = Plunder.WreckJobs,
                ["plunderWithdrawals"] = Plunder.Withdrawals,
                ["plunderPrepJobs"] = Plunder.PrepJobs,
                ["livestockAtStart"] = livestock.Count,
                ["livestockLost"] = livestock.Count(a => a.Dead || a.Downed || !a.Spawned),
                ["colonyBuildingsLost"] = map != null ? colonyBuildingsAtStart - CountColonyBuildings() : 0,
                ["funnelHoldJobs"] = Funnels.HoldJobs,
                ["funnelSmokes"] = Funnels.Smokes,
                ["shieldBearers"] = ShieldWall.Bearers,
                ["shieldFollowers"] = ShieldWall.Followers,
                ["shieldLeadJobs"] = ShieldWall.LeadJobs,
                ["shieldFollowJobs"] = ShieldWall.FollowJobs,
                ["shieldFollowShots"] = ShieldWall.FollowShots,
                ["shieldFragsGiven"] = Grenades.ShieldFragsGiven,
                ["shieldThrows"] = Grenades.ShieldThrows,
                ["shieldRearguardJobs"] = JobGiver_HSKTAI_Withdraw.RearguardJobs,
                ["shieldEscortJobs"] = JobGiver_HSKTAI_Shield.EscortJobs,
                ["proneEvents"] = Posture.ProneEvents,
                ["crossingMode"] = AssaultPlanner.LastPlan?.crossing.ToString() ?? "",
                ["openCells"] = AssaultPlanner.LastPlan?.openCells ?? 0,
                ["crossMass"] = Crossing.Mass,
                ["fireAndMovementShots"] = JobGiver_HSKTAI_Approach.FireAndMovementShots,
                ["axisStaggerTicks"] = AssaultPlanner.LastPlan != null && AssaultPlanner.LastPlan.axisGoTick.Count > 0 ? AssaultPlanner.LastPlan.axisGoTick.Values.Max() - AssaultPlanner.LastPlan.axisGoTick.Values.Min() : 0,
                ["crossSmoke"] = Crossing.Smoke,
                ["crossAttrition"] = Crossing.Attrition,
                ["crossAttritionGo"] = Crossing.AttritionGo,
                ["crossBound"] = Crossing.Bounds,
                ["memoryAfter"] = raidFaction != null ? (FactionMemory.Read(raidFaction)?.Describe() ?? "") : "",
                ["memoryOutcome"] = AssaultPlanner.LastPlan?.memoryOutcome ?? "",
                ["memoryThreat"] = AssaultPlanner.LastPlan?.memoryThreat ?? "",
                ["memoryAdaptations"] = AssaultPlanner.LastPlan != null ? string.Join(", ", AssaultPlanner.LastPlan.memoryAdaptations) : "",
                ["memoryDownedBy"] = AssaultPlanner.LastPlan != null ? string.Join(",", AssaultPlanner.LastPlan.downedBy.Values.GroupBy(v => v).Select(g => g.Key + "=" + g.Count())) : "",
                ["memoryApproachChanges"] = FactionMemory.ApproachChanges,
                ["shieldBreachLeads"] = JobGiver_HSKTAI_Shield.BreachLeads,
                ["shieldBlastWaits"] = JobGiver_HSKTAI_Shield.BlastWaits,
                ["memoryWeaponSwaps"] = FactionMemory.WeaponSwaps,
                ["memoryAmmoSwaps"] = FactionMemory.AmmoSwaps,
                ["memoryGrenadeSwaps"] = FactionMemory.GrenadeSwaps,
                ["memorySwapDebug"] = FactionMemory.SwapDebug,
                ["unarmedRearmed"] = JobGiver_HSKTAI_Unarmed.Rearmed,
                ["unarmedKept"] = JobGiver_HSKTAI_Unarmed.Kept,
                ["satisfiedHeldTicks"] = SatisfiedHold.HeldTicks,
                ["satisfiedReleases"] = SatisfiedHold.Releases,
                ["satisfiedRelease"] = SatisfiedHold.LastRelease,
                ["crossCovered"] = Crossing.Covered,
                ["crossChecks"] = Crossing.Checks,
                ["crossIdleAttrition"] = Crossing.IdleAttrition,
                ["crossNoApproach"] = Crossing.NoApproach,
                ["crossReangled"] = Crossing.Reangled,
                ["grenadeLobs"] = Grenades.Lobs,
                ["evades"] = Evasion.Evades,
                ["advances"] = JobGiver_HSKTAI_Advance.Advances,
                ["positionFloods"] = PositionFinder.Floods,
                ["retreats"] = Micro.Retreats,
                ["meleeIntercepts"] = Micro.Intercepts,
                ["meleeSideApproaches"] = Micro.SideApproaches,
                ["kites"] = Micro.Kites,
                ["raidingPartyMembers"] = JobGiver_HSKTAI_RaidingParty.Members,
                ["raidingPartyJobs"] = JobGiver_HSKTAI_RaidingParty.Jobs,
                ["ducks"] = Evasion.Ducks,
                ["crossFlanks"] = Crossing.Flanks,
                ["medicCarries"] = JobGiver_HSKTAI_Medic.Carries,
                ["medicTends"] = JobGiver_HSKTAI_Medic.Tends,
                ["medicSmokes"] = JobGiver_HSKTAI_Medic.Smokes,
                ["assaultFireShots"] = JobGiver_HSKTAI_AssaultFire.Shots,
                ["assaultFireExposed"] = JobGiver_HSKTAI_AssaultFire.ExposedShots,
                ["crossFlankGo"] = Crossing.FlankGo,
                ["crossBreachFallback"] = Crossing.BreachFallback,
                ["ammoWithdrawals"] = Withdrawal.AmmoWithdrawals,
                ["cappedBursts"] = FireDoctrine.CappedBursts,
                ["screenSmokes"] = Grenades.ScreenSmokes,
                ["suppressionEvents"] = Patch_RealSuppression.Applied,
                ["suppByClass"] = string.Join(" ", Patch_RealSuppression.ByClass.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + Math.Round(kv.Value))),
                ["gasMasksGiven"] = GasRaid.MasksGiven,
                ["gasToxGiven"] = GasRaid.ToxGiven,
                ["gasFireGiven"] = GasRaid.FireGiven,
                ["gasThrows"] = GasRaid.GasThrows,
                ["gasFireThrows"] = GasRaid.FireThrows,
                ["raiderProneTicks"] = raiderProneTicks,
                ["raiderSuppressedTicks"] = raiderSuppressedTicks,
                ["raiderHunkerTicks"] = raiderHunkerTicks,
                ["shieldHpLost"] = raiders.Sum(r => r.apparel?.WornApparel.Where(a => ShieldWall.IsBodyShield(a.def)).Sum(a => a.MaxHitPoints - a.HitPoints) ?? 0),
                ["raidersWithShield"] = raiders.Count(ShieldWall.HasBodyShield),
                ["defendersWithShield"] = defenders.Count(d => d.apparel?.WornApparel.Any(a => a.GetType().Name == "Apparel_Shield") ?? false),
                ["attackerScore"] = Math.Round(attackerScore, 1),
                ["defenderScore"] = Math.Round(defenderScore, 1),
                ["attackerGearValue"] = Math.Round(attackerGear),
                ["defenderGearValue"] = Math.Round(defenderGear),
                ["firesStarted"] = BreachKit.FiresStarted,
                ["forceRatioCheck"] = cfg.forceRatioCheck,
                ["breachToolsGiven"] = BreachKit.ToolsGiven,
                ["breachPlansWalk"] = BreachPlanner.DecisionsWalk,
                ["breachPlansBreach"] = BreachPlanner.DecisionsBreach,
                ["lastBreachPlan"] = BreachPlanner.DecisionsWalk + BreachPlanner.DecisionsBreach > 0 ? BreachPlanner.LastDecision.ToString() : "",
                ["notes"] = notes.Trim(),
                ["casualties"] = casualties,
                ["raiderStates"] = RaiderStates(),
                ["timeline"] = timeline
            };
            try
            {
                Directory.CreateDirectory(cfg.outDir);
                string path = Path.Combine(cfg.outDir, cfg.runId + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
                Log.Message("[HSK Tactical AI] AI Lab result written: " + path + " outcome=" + outcome);
            }
            catch (Exception e)
            {
                Log.Error("[HSK Tactical AI] AI Lab could not write results: " + e.Message);
            }
        }
    }
}

namespace HSKTacticalAI
{
    /// <summary>Lab only: every toil change of a hostile lord (from, to, the transition's triggers) - why a raid left.</summary>
    [HarmonyPatch(typeof(Transition), nameof(Transition.Execute))]
    public static class Patch_LabTransitions
    {
        public static readonly List<string> Log = new List<string>();
        public static string Last;

        public static void Prefix(Transition __instance, Lord lord)
        {
            if (!AILab.Active || lord?.faction == null || !lord.faction.HostileTo(Faction.OfPlayer) || __instance?.target == null) return;
            string from = lord.CurLordToil?.GetType().Name.Replace("LordToil_", "") ?? "none";
            string to = __instance.target.GetType().Name.Replace("LordToil_", "");
            if (from == to) return;
            string why = string.Join("|", __instance.triggers.Select(tr => tr.GetType().Name.Replace("Trigger_", "")));
            Last = to + " via " + why;
            if (Log.Count < 40) Log.Add(Find.TickManager.TicksGame + ": " + from + " -> " + Last);
        }
    }

    /// <summary>Lab only: effective ranged hits (damage dealt, so shield / armour stops don't count) by side.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Patch_LabHitCounter
    {
        public static int RaiderHits, DefenderHits;

        public static void Postfix(Pawn ___pawn, DamageInfo dinfo, float totalDamageDealt)
        {
            if (!AILab.Active || ___pawn == null || totalDamageDealt <= 0f) return;
            bool ranged = (dinfo.Weapon != null && dinfo.Weapon.IsRangedWeapon) || (dinfo.Def != null && dinfo.Def.isRanged);
            if (!ranged) return;
            Faction victim = ___pawn.Faction, by = dinfo.Instigator?.Faction;
            if (victim == null || by == null) return;
            if (victim == Faction.OfPlayer && by.HostileTo(Faction.OfPlayer)) RaiderHits++;
            else if (by == Faction.OfPlayer && victim.HostileTo(Faction.OfPlayer)) DefenderHits++;
        }
    }
}
