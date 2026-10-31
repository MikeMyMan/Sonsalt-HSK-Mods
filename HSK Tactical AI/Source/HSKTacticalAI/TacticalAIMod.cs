using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKTacticalAI
{
    public class TacticalAIMod : Mod
    {
        public const string Version = "0.4.0";
        public static TacticalAISettings Settings;
        public static Harmony Harmony;
        public static ModContentPack Content;

        public TacticalAIMod(ModContentPack content) : base(content)
        {
            Content = content;
            Settings = GetSettings<TacticalAISettings>();
            Harmony = new Harmony("sonsalt.hsk.tacticalai");
            if (Settings.enabled) PatchEachFeature();
            Log.Message("[HSK Tactical AI] " + Version + " loaded" + (AILab.Active ? " - AI Lab run: " + AILab.Config.runId : "")
                        + (Settings.enabled ? "" : " - switched off in the mod settings, nothing patched"));
        }

        public static bool Patched { get; private set; }

        /// <summary>Master switch: off removes every patch of this mod (raids are vanilla, nothing of ours runs); on applies them again.</summary>
        public static void SetEnabled(bool on)
        {
            Settings.enabled = on;
            if (on)
            {
                if (!Patched) PatchEachFeature();
                return;
            }
            AssaultPlanner.DropAllPlans();   // raids get vanilla duties right away (our duty patches see "off" already)
            if (Patched) { Harmony.UnpatchAll(Harmony.Id); Patched = false; }
            Log.Message("[HSK Tactical AI] switched off: all patches removed");
        }

        /// <summary>Patch classes that failed to apply (target renamed or removed, e.g. after an HSK alpha update): shown in the settings.</summary>
        public static readonly System.Collections.Generic.List<string> FailedPatches = new System.Collections.Generic.List<string>();
        /// <summary>Patch classes whose Prepare() said no (the optional mod they hook isn't loaded).</summary>
        public static readonly System.Collections.Generic.List<string> SkippedPatches = new System.Collections.Generic.List<string>();
        public static int AppliedPatches;

        /// <summary>
        /// Harmony.PatchAll stops at the first patch whose target is missing, and every patch after it is lost. Here each patch
        /// class is applied on its own: one that fails switches off just that feature and logs one line saying which and why
        /// (HSK dev feedback 2026-10-30: "disable automatically with a log entry when a dependency is missing after an update").
        /// </summary>
        private static void PatchEachFeature()
        {
            FailedPatches.Clear(); SkippedPatches.Clear(); AppliedPatches = 0;
            Patched = true;
            foreach (System.Type t in AccessTools.GetTypesFromAssembly(typeof(TacticalAIMod).Assembly))
            {
                // patch classes only, as PatchAll does (Harmony finds Prepare/Cleanup/TargetMethod by name: a JobDriver's own
                // Cleanup would be taken for one)
                if (!t.IsDefined(typeof(HarmonyAttribute), false)) continue;
                System.Collections.Generic.List<System.Reflection.MethodInfo> done;
                try { done = Harmony.CreateClassProcessor(t).Patch(); }
                catch (System.Exception e)
                {
                    System.Exception inner = e;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    FailedPatches.Add(PatchName(t));
                    Log.Warning("[HSK Tactical AI] " + PatchName(t) + " disabled: " + inner.Message
                                + " (probably renamed in an HSK, CE or RimWorld update; the rest of the mod keeps working)");
                    continue;
                }
                if (done == null || done.Count == 0) SkippedPatches.Add(PatchName(t));   // Prepare() said no: its mod isn't loaded
                else AppliedPatches++;
            }
            if (SkippedPatches.Count > 0)
                Log.Message("[HSK Tactical AI] optional mod not loaded, skipped: " + string.Join(", ", SkippedPatches));
            if (FailedPatches.Count > 0)
                Log.Warning("[HSK Tactical AI] " + FailedPatches.Count + " of " + (AppliedPatches + FailedPatches.Count) + " patches could not be applied: " + string.Join(", ", FailedPatches));
        }

        private static string PatchName(System.Type t) => t.DeclaringType != null ? t.DeclaringType.Name + "." + t.Name : t.Name;

        public override string SettingsCategory() => "HSK Tactical AI";

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindow(inRect);
    }

    public class TacticalAISettings : ModSettings
    {
        public bool enableThreatMap = true;
        public bool showThreatOverlay;
        public float budgetMsPerTick = 1.0f;
        public int cycleTicksCombat = 120;
        public int cycleTicksPeace = 900;
        public float weightDrafted = 1.0f;
        public float weightUndrafted = 0.4f;
        public float weightTurret = 1.5f;

        // breach planning (0.2)
        public bool smartBreaching = true;
        public float exposureCost = 60f;            // path-cost per unit of player fire on a cell
        public float wallPenalty = 300f;            // extra cost per wall/door to destroy (time to break it)
        public float wallHpPenalty = 0.3f;          // extra cost per hit point of those walls
        public int naturalWallExtraCost = 1000;     // rock: vanilla 20 -> raiders tunnel through mountains
        public int blockedWallCostWhenWalking = 5000;

        // breach kit (0.3)
        public bool breachKit = true;
        public int chargesPerSpecialist = 3;
        public int thrownPerSpecialist = 4;          // stick bombs for medieval breach specialists

        // assault planner (0.4)
        public bool assaultPlanner = true;
        public int maxAxes = 3;
        public float minAxisSeparation = 70f;
        public int syncTimeoutTicks = 3600;
        public int stagingDistance = 12;             // cells back from the breach point where the rest of the raid waits
        public float chargeWallDamageEstimate = 800f; // wall HP one breach charge removes (planning estimate)
        public float toolHpBudget = 2500f;           // wall HP one raider with a breach tool can take down in reasonable time
        public int standoffTicks = 6000;             // a raid that can't break in holds this long, then withdraws
        public float safeFire = 0.05f;               // threat-map value counted as "dead ground" (out of the defenders' fire)
        public float routeCostPerFire = 400f;        // extra path cost per unit of defender fire on a route cell (wide arcs)
        public int routeBaseCostInFire = 60;         // extra path cost for any cell the defenders can shoot
        public int waypointSpacing = 8;              // cells between route waypoints
        public float maxApproachDetour = 1.5f;       // assault position: walk to the defenders ÃƒÂ¢Ã¢â‚¬Â°Ã‚Â¤ this ÃƒÆ’Ã¢â‚¬â€ straight line (+6)
        public bool avoidFunnels = true;             // one covered entrance (killbox): make a new one instead
        public float funnelCellPenalty = 2500f;      // extra cost per narrow route cell under fire (corridor, doorway, gap)
        public int newEntranceWallCost = 600;        // planning cost per wall to blow when making a new entrance
        public float newEntranceRatio = 0.85f;       // new entrance chosen if it costs less than this ÃƒÆ’Ã¢â‚¬â€ the walking route
        public float replanLossFraction = 0.3f;      // walk assault losing this share after H-hour -> pull back, make a new entrance
        public int breachStallTicks = 3600;          // breach plan without wall damage this long -> plan the next breach point
        public int minFunnelCells = 3;               // new entrance at planning only if the way in has this many funnel cells
        public int maxBreachReplans = 3;             // then give up breaching and assault directly
        public float breachConeDegrees = 60f;        // stack outside this angle from the line defenders -> breach (they see out once it opens)
        public bool smokeOnBreach = true;            // Industrial+ breach teams throw smoke on the breach when it opens
        public float smokeRadius = 3.5f;
        // several simultaneous breaches + force ratio (0.5)
        public int maxBreaches = 3;                  // breach points on different sides, opened together
        public int raidersPerBreach = 4;             // one breach point per this many raiders
        public int breachSyncTicks = 1800;           // teams wait this long for the others before starting on their walls
        public int breachOpenGraceTicks = 600;       // one breach open: wait this long for the others before going in
        public bool fireBreaching = true;            // wooden walls are set on fire instead of blown
        public bool forceRatioCheck = true;          // no assault without enough strength (siege or withdraw instead)
        public float commitRatioTolerance = 0.8f;    // at H-hour, withdraw only below this share of the required ratio
        public float defendedAxisPenalty = 8000f;    // breach planning: extra cost for a breach point facing the defence (0 from 90Ãƒâ€šÃ‚Â° off)
        public float defendedAxisExclude = 40f;      // no breach points within this angle of the way the defence faces
        // combined arms (0.6)
        public bool supportFire = true;              // base of fire (MGs, marksmen, best riflemen) pins the defenders before the assault moves
        public float pinnedToMove = 0.5f;            // share of visible defenders suppressed/hunkering before the assault goes
        public int suppressMaxTicks = 360;           // go anyway after this long
        public int supportTicks = 2400;              // base of fire keeps firing this long after H-hour, then joins the assault
        public bool overwatch = true;                // breach: gunners take covered positions facing the breach and fire as it opens
        public float overwatchMinDist = 8f;
        public float overwatchMaxDist = 40f;
        public bool plannedWithdrawal = true;        // withdraw in bounds under covering fire, carrying the wounded, instead of the panic run
        public float withdrawLossFraction = 0.5f;    // share of the raid lost (vanilla measure) that makes it withdraw
        public float withdrawLossFractionLeaderDown = 0.4f;
        public float withdrawExchangeRatio = 0.5f;   // "failing": the defenders' share down is below this x our share lost
        public float withdrawHardLoss = 0.7f;        // withdraw at this share lost regardless of the exchange
        public int boundTicks = 300;                 // bounding overwatch: each element moves this long, then covers
        // terrain (0.7)
        public bool tunnelling = true;               // dig through thin rock into mountain bases (sappers get mining tools by tech level)
        public int maxTunnelTicks = 20000;           // only if the estimated digging time is within this
        public float rockMineFactor = 0.8f;          // planning: Minerals rock mining factor (granite 0.7, sandstone 1.2)
        public bool arson = true;                    // a few raiders set wooden buildings and crops alight in several places at once
        public int arsonMinRaid = 5;                 // only raids at least this big detach arsonists
        public int arsonSites = 3;                   // at most this many places (one arsonist each; one per five raiders)
        public int arsonIgnitesPerSite = 4;          // fires set per place, spread out
        public bool grenadeKit = true;               // frags (1 in 3) and smoke (1 in 5) for Industrial+ raids, stick bombs below; frag out at openings
        public int fragSettleTicks = 180;            // the rush waits this long after the last frag is thrown
        public int fragDangerTicks = 240;            // a frag's blast area is a no-go zone for our raiders this long
        public bool doorBreach = true;               // player doors as an extra simultaneous breach (charge or grenades)
        public float fragDoorDamage = 140f;          // planning: damage one frag does to a door (Bomb x2 vs doors)
        public bool shieldTactics = true;            // body-shield bearers lead the assault, gunners follow in their shadow
        public int shieldFollowers = 3;              // followers per shield (gunners to the sides, melee straight behind)
        public int shieldLeadTicks = 900;            // breach: the rest of a group waits up to this long for its shield to go through first
        public int shieldCrouchTicks = 60;           // shield advance: crouch this long after each ~4-cell bound
        public int shieldFrags = 3;                  // thrown explosives every shield bearer carries (shield + grenades)
        public int shieldThrowTicks = 240;           // a shield bearer throws at most this often
        public bool shieldRearguard = true;          // in a withdrawal, shield bearers cover the rear and leave last
        public bool shieldEscort = true;             // breach work: a shield bearer covers the breacher on its way to and at the wall
        public bool equipRaiders = false;            // extra equipment (breach charges, tools, grenades, masks, smoke) on top of the raiders' own loadout
        public bool proneVisual = true;              // prone raiders drawn in the crawl pose (render only)
        public bool runAndGunByRole = true;          // RunAndGun installed: only riflemen / assault raiders in the assault shoot on the move
        public bool raidMemory = true;               // factions remember earlier raids (only through survivors) and adapt their approach
        public bool adaptiveLoadouts = true;         // ... and their loadout, within budget (swaps, never extra gear)
        public float memoryShare = 0.3f;             // at most this share of a raid swaps gear
        public float memoryIntelMin = 0.25f;         // intel needed before a faction adapts
        public float memoryHalfLifeDays = 30f;       // memory fades by half in this many days (about two seasons)
        public float memoryArmourHigh = 8f;          // colonists' mean sharp armour that counts as heavily armoured
        public bool memoryMessages = true;           // a message when a raid arrives adapted
        public float proneMoveFactor = 0.25f;        // prone raiders crawl at this share of their speed
        public bool proneLabel = true;               // "prone" shown above a prone raider while he is prone
        public bool posture = true;                  // suppressed raiders without cover go prone (CE profile x proneHeightFactor, crawl)
        public bool gasRaids = true;                 // defenders in a small enclosed room: masks, tox and fire into it at the breach
        public bool realSuppression = true;          // suppression: armour no longer cancels it, MGs and explosions count more, arrows less
        public bool openCrossing = true;             // walk assault over open ground: covered approach, massed rush, smoke screen or attrition
        public int openGroundCells = 8;              // route cells in fire without cover that make it open ground
        public float overwhelmOdds = 3f;             // massed rush over open ground: raw odds, attackers vs defenders
        public float assaultOdds = 3f;               // assault only at these raw odds (3:1); weaker raids siege, snipe, plunder or leave
        public float suppressionLauncher = 4f;       // grenade / rocket launchers, mortars
        public int smokeSettleTicks = 150;           // smoke screen: rush once it is up
        public int attritionTicks = 2700;            // out of range, base of fire only, this long; then bound in or withdraw
        public float boundOdds = 1.5f;               // after attrition: bounding assault at these odds (force score), below them withdraw
        public bool satisfiedHold = true;            // vanilla "satisfied, leaving" waits while a planned assault runs and goes well
        public int satisfiedAssaultTicks = 3000;     // ... at most this long after H-hour
        public int satisfiedStallTicks = 1200;       // ... released when no defender went down and no breach opened for this long
        public float satisfiedLossShare = 0.4f;      // ... or this share of the raiders at H-hour is lost
        public int satisfiedPrepMaxTicks = 15000;    // ... preparation (approach) held at most this long after the plan
        public bool flanking = true;                 // prefer exposed defenders, flank firing positions and flank axes
        public float flankAxisBonus = 600f;          // axis score bonus x share of defenders whose cover does not face it
        public float flankPositionBonus = 4f;        // firing position score bonus per defender exposed from it
        public bool syncedAssault = true;            // walk assault: axes arrive together; fire and movement inside each group
        public float assaultTicksPerCell = 3f;       // staggered starts: route length difference x this
        public int assaultBoundTicks = 240;          // fire and movement: each half moves this long, then covers
        public int mgShortBurst = 5;                 // MG at a point target beyond close range: rounds per burst (guns without a burst mode)
        public int rifleShortBurst = 3;              // rifles on burst without a burst mode
        public float suppressionScale = 2.5f;        // all suppression x this (CE thresholds: about 65 to pin, 650 to hunker)
        public bool ammoWithdraw = true;             // withdraw when most gunners are nearly out of ammunition
        public float ammoWithdrawShare = 0.5f;
        public bool flankManeuver = true;            // out of range: the maneuver element flanks by a covered approach while the base of fire pins
        public int flankTicks = 2400;                // go after this long even if the flank group is not all in position
        public bool breachFallback = true;
        public bool lobOverWalls = true;
        public bool combatMicro = true;              // retreat when outgunned, melee intercept / side approach, gunners kite melee
        public bool raidingParty = true;             // raids of 10+: a sixth plunders from the start while the rest attacks
        public bool focusFire = true;                // raiders concentrate fire: exposed, wounded, dangerous defenders, the ones comrades shoot
        public bool threatAdvance = true;            // assault: advance on defenders along fire-priced routes, not straight down a firing lane
        public bool evasion = true;                  // raiders leave gas, fire and the landing area of the defenders' explosives
        public bool duckWhenAimed = true;            // a raider in the open that a defender is aiming at takes cover or goes prone             // grenades may be lobbed over one wall into an unroofed area
        public bool medics = true;                   // the raid medic smokes, pulls the wounded out of fire and tends them
        public bool coverCarry = true;               // healthy gunners cover raiders who carry a downed comrade out of fire
        public bool siegePackAnimals = true;         // pre-industrial sieges get their supplies by pack animal instead of drop pod
        public bool contactReaction = true;          // a defender newly in sight: gunners break off walking / rescues, take cover, fire
        public float medicReach = 30f;           // open-ground approach failed: try a breach before leaving
        public float suppressionArmorFloor = 0.6f;    // near misses against armour the round cannot penetrate (x2 pen) still count this much
        public float suppressionAuto = 1.5f;         // machine guns (bursts of 8+); automatic weapons x1.2
        public float suppressionArrow = 0.3f;         // arrows and bolts barely make anyone duck
        public float suppressionExplosive = 3f;
        public float suppressionDecay = 2f;          // suppression fades by this per tick (CE: 4; 1 kept raiders pinned 13 % of a raid)
        public int suppressionHoldTicks = 60;        // and only this long after the last near miss (CE: 30)
        public float gasRadius = 4f;
        public int gasHoldTicks = 900;               // no-go time for the gas cloud / fire before the masked raid goes in
        public float proneHeightFactor = 0.35f;
        public int proneRecoverTicks = 180;          // up again this long after the suppression ends
        public bool fireDoctrine = true;             // raiders pick aimed / snap / suppressive and single / burst / auto by range, role and situation
        public float fireCqbRange = 8f;              // close quarters: snap shots, full auto
        public float fireCloseRange = 15f;           // close: snap shots, short bursts
        public float fireMediumRange = 30f;          // medium (also capped at half the weapon's range): aimed bursts; beyond: aimed single
        public bool funnelCaution = true;            // inside the fight: don't chase defenders into a funnel - grenade it, hold its mouth, go in after a while
        public int funnelMinCells = 3;               // narrow cells in a row that make a funnel
        public int funnelHoldTicks = 1200;           // hold the mouth this long before going in
        public int funnelHolders = 2;                // raiders that keep watching a funnel mouth (the rest plunder)
        public bool plunder = true;                  // don't chase far or holed-up defenders: kill livestock, loot, burn, wreck
        public float plunderChaseDist = 20f;         // chase defenders only this close (and reachable without a funnel)
        public float plunderLooterShare = 0.34f;     // at most this share of the raid carries loot off at once
        public int plunderIdleWithdrawTicks = 1800;  // nothing left to plunder this long: withdraw
        public bool plunderDuringBreach = true;      // while the breach is prepared, the stackers plunder near their stack point
        public float plunderPrepRadius = 25f;        // ... within this distance of it
        public float plunderRecallFraction = 0.6f;   // slow breaches (tools, fire, tunnels): back at the stack once this much wall is down
        // tactical competence (0.2): the leader's intelligence and the faction's tech level decide how smart the raid acts
        public bool tacticalCompetence = true;
        public float competenceIntelWeight = 0.7f;   // share of the leader's intelligence (the rest: the tech ceiling alone)
        public bool competenceLossOfCommand = true;  // leader down or dead: the next in rank takes over, less sharp, after a moment of confusion
        public bool competenceReadout;               // message + log line with each raid's competence; threat-map button tooltip
        public float competenceOverride = -1f;       // AI Lab only (not saved): fixed competence for every raid, -1 = off
        public float competenceRoll = -1f;           // AI Lab only (not saved): fixed doctrine roll, -1 = per raid
        public bool siegeGuards = true;              // siege camp: guards in cover by role facing the defenders; direct-fire siege guns inside the camp with a line of fire
        public bool cornerFire = true;               // open breach: gunners lean out from the wall ends beside the hole and fire in before going in
        public bool gasAmmoSwap = true;              // launchers with only gas rounds swap 75 % of them for the explosive round of the same ammo set
        public bool keepRange = true;                // each weapon at its own distance: bows and long guns back off, short guns close in, melee closes in
        public bool launchersStayOut = true;         // explosive launchers don't follow the assault through the breach: they stay out as base of fire
        public bool crewWeapons = true;              // industrial+ assault raids of 8+ bring a crew-served gun (carried packed, set up at range, manned)
        public float cswMaxMass = 40f;               // ... at most this heavy (kg)
        public int cswWaitTicks = 3000;              // H-hour waits for the guns at most this long
        public bool fieldFight = true;               // defenders out of their base: the raid turns and fights them there, then back to the plan
        public bool indirectFire = true;             // grenade launchers lob explosive rounds over walls at targets they can't hit directly
        public bool breachGear = true;               // a raid out to breach that has nothing of its own: 1-3 raiders carry the tech level's breaching kit
        public bool hskItemsOnly = true;             // everything the AI gives or picks comes from RimWorld/DLC, CE or the HSK pack (not our own charges)
        public bool enabled = true;                  // master switch: off = no patches at all, vanilla raids
        public bool nolbRescueFilter = true;         // No One Left Behind: raiders don't run into the defenders' fire to rescue someone
        public bool raidGoals = true;                // some raids come to scout or to sabotage instead of to assault
        public bool raidIntents = true;              // an assault comes for goods, for people (slaving) or for revenge (punitive)
        public float slavingShare = 0.15f;           // weight of a slaving raid (plunder = 1)
        public float punitiveShare = 0.5f;           // weight of a punitive raid after a bad beating they know the reason for
        public string raidIntentOverride = "";       // AI Lab only (not saved): plunder | slaving | punitive
        public bool escalation = true;               // a faction beaten here sends bigger raids for a while
        public float escalationSize = 0.3f;          // ... up to this much more raid points at full anger
        public bool raidLetters = true;              // the raid letter says why the raid comes as it does
        public bool prisonersReport = true;          // a prisoner you release, or one who escapes, reports to his faction
        public bool arrivalChoice = true;            // a faction that knows you picks its arrival: the quiet side, or drop pods (spacers)
        public string arrivalOverride = "";          // AI Lab only (not saved): quiet | droppods
        public bool operations = true;               // coordinated operations: hammer and anvil, armoured spearhead (sharp commands)
        public float operationChance = 0.2f;         // ... chance per raid (twice as likely after a failed raid here)
        public string operationOverride = "";        // AI Lab only (not saved): hammer | vanguard
        public float scoutShare = 0.10f;             // base weight of a scouting raid (assault = 1)
        public float sabotageShare = 0.15f;          // base weight of a sabotage raid
        public int scoutObserveTicks = 2500;         // a scouting party watches this long, then leaves
        public int sabotageTicks = 6000;             // a sabotage raid leaves after this long at the latest
        public int sabotageIdleTicks = 1500;         // ... or once it has found nothing to wreck for this long
        public string raidGoalOverride = "";        // AI Lab only (not saved): assault | scout | sabotage
        public int labDownRaiderTick = -1;           // AI Lab only (not saved): this many ticks into the raid, down the most exposed raider (rescue / covering-fire tests)
        public int cornerHoldTicks = 1800;           // ... and clearing the gap by fire before entry: at most this long after H-hour

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableThreatMap, "enableThreatMap", true);
            Scribe_Values.Look(ref showThreatOverlay, "showThreatOverlay", false);
            Scribe_Values.Look(ref budgetMsPerTick, "budgetMsPerTick", 1.0f);
            Scribe_Values.Look(ref cycleTicksCombat, "cycleTicksCombat", 120);
            Scribe_Values.Look(ref cycleTicksPeace, "cycleTicksPeace", 900);
            Scribe_Values.Look(ref weightDrafted, "weightDrafted", 1.0f);
            Scribe_Values.Look(ref weightUndrafted, "weightUndrafted", 0.4f);
            Scribe_Values.Look(ref weightTurret, "weightTurret", 1.5f);
            Scribe_Values.Look(ref smartBreaching, "smartBreaching", true);
            Scribe_Values.Look(ref exposureCost, "exposureCost", 60f);
            Scribe_Values.Look(ref wallPenalty, "wallPenalty", 300f);
            Scribe_Values.Look(ref wallHpPenalty, "wallHpPenalty", 0.3f);
            Scribe_Values.Look(ref naturalWallExtraCost, "naturalWallExtraCost", 1000);
            Scribe_Values.Look(ref blockedWallCostWhenWalking, "blockedWallCostWhenWalking", 5000);
            Scribe_Values.Look(ref breachKit, "breachKit", true);
            Scribe_Values.Look(ref chargesPerSpecialist, "chargesPerSpecialist", 3);
            Scribe_Values.Look(ref thrownPerSpecialist, "thrownPerSpecialist", 4);
            Scribe_Values.Look(ref assaultPlanner, "assaultPlanner", true);
            Scribe_Values.Look(ref maxAxes, "maxAxes", 3);
            Scribe_Values.Look(ref minAxisSeparation, "minAxisSeparation", 70f);
            Scribe_Values.Look(ref syncTimeoutTicks, "syncTimeoutTicks", 3600);
            Scribe_Values.Look(ref stagingDistance, "stagingDistance", 12);
            Scribe_Values.Look(ref chargeWallDamageEstimate, "chargeWallDamageEstimate", 800f);
            Scribe_Values.Look(ref toolHpBudget, "toolHpBudget", 2500f);
            Scribe_Values.Look(ref standoffTicks, "standoffTicks", 6000);
            Scribe_Values.Look(ref safeFire, "safeFire", 0.05f);
            Scribe_Values.Look(ref routeCostPerFire, "routeCostPerFire", 400f);
            Scribe_Values.Look(ref routeBaseCostInFire, "routeBaseCostInFire", 60);
            Scribe_Values.Look(ref waypointSpacing, "waypointSpacing", 8);
            Scribe_Values.Look(ref maxApproachDetour, "maxApproachDetour", 1.5f);
            Scribe_Values.Look(ref avoidFunnels, "avoidFunnels", true);
            Scribe_Values.Look(ref funnelCellPenalty, "funnelCellPenalty", 2500f);
            Scribe_Values.Look(ref newEntranceWallCost, "newEntranceWallCost", 600);
            Scribe_Values.Look(ref newEntranceRatio, "newEntranceRatio", 0.85f);
            Scribe_Values.Look(ref replanLossFraction, "replanLossFraction", 0.3f);
            Scribe_Values.Look(ref breachStallTicks, "breachStallTicks", 3600);
            Scribe_Values.Look(ref minFunnelCells, "minFunnelCells", 3);
            Scribe_Values.Look(ref maxBreachReplans, "maxBreachReplans", 3);
            Scribe_Values.Look(ref breachConeDegrees, "breachConeDegrees", 60f);
            Scribe_Values.Look(ref smokeOnBreach, "smokeOnBreach", true);
            Scribe_Values.Look(ref smokeRadius, "smokeRadius", 3.5f);
            Scribe_Values.Look(ref maxBreaches, "maxBreaches", 3);
            Scribe_Values.Look(ref raidersPerBreach, "raidersPerBreach", 4);
            Scribe_Values.Look(ref breachSyncTicks, "breachSyncTicks", 1800);
            Scribe_Values.Look(ref breachOpenGraceTicks, "breachOpenGraceTicks", 600);
            Scribe_Values.Look(ref fireBreaching, "fireBreaching", true);
            Scribe_Values.Look(ref forceRatioCheck, "forceRatioCheck", true);
            Scribe_Values.Look(ref commitRatioTolerance, "commitRatioTolerance", 0.8f);
            Scribe_Values.Look(ref defendedAxisPenalty, "defendedAxisPenalty", 8000f);
            Scribe_Values.Look(ref defendedAxisExclude, "defendedAxisExclude", 40f);
            Scribe_Values.Look(ref supportFire, "supportFire", true);
            Scribe_Values.Look(ref pinnedToMove, "pinnedToMove", 0.5f);
            Scribe_Values.Look(ref suppressMaxTicks, "suppressMaxTicks", 360);
            Scribe_Values.Look(ref supportTicks, "supportTicks", 2400);
            Scribe_Values.Look(ref overwatch, "overwatch", true);
            Scribe_Values.Look(ref overwatchMinDist, "overwatchMinDist", 8f);
            Scribe_Values.Look(ref overwatchMaxDist, "overwatchMaxDist", 40f);
            Scribe_Values.Look(ref plannedWithdrawal, "plannedWithdrawal", true);
            Scribe_Values.Look(ref withdrawLossFraction, "withdrawLossFraction", 0.5f);
            Scribe_Values.Look(ref withdrawLossFractionLeaderDown, "withdrawLossFractionLeaderDown", 0.4f);
            Scribe_Values.Look(ref withdrawExchangeRatio, "withdrawExchangeRatio", 0.5f);
            Scribe_Values.Look(ref withdrawHardLoss, "withdrawHardLoss", 0.7f);
            Scribe_Values.Look(ref boundTicks, "boundTicks", 300);
            Scribe_Values.Look(ref tunnelling, "tunnelling", true);
            Scribe_Values.Look(ref maxTunnelTicks, "maxTunnelTicks", 20000);
            Scribe_Values.Look(ref rockMineFactor, "rockMineFactor", 0.8f);
            Scribe_Values.Look(ref arson, "arson", true);
            Scribe_Values.Look(ref arsonMinRaid, "arsonMinRaid", 5);
            Scribe_Values.Look(ref arsonSites, "arsonSites", 3);
            Scribe_Values.Look(ref arsonIgnitesPerSite, "arsonIgnitesPerSite", 4);
            Scribe_Values.Look(ref grenadeKit, "grenadeKit", true);
            Scribe_Values.Look(ref fragSettleTicks, "fragSettleTicks", 180);
            Scribe_Values.Look(ref fragDangerTicks, "fragDangerTicks", 240);
            Scribe_Values.Look(ref doorBreach, "doorBreach", true);
            Scribe_Values.Look(ref fragDoorDamage, "fragDoorDamage", 140f);
            Scribe_Values.Look(ref shieldTactics, "shieldTactics", true);
            Scribe_Values.Look(ref shieldFollowers, "shieldFollowers", 3);
            Scribe_Values.Look(ref shieldLeadTicks, "shieldLeadTicks", 900);
            Scribe_Values.Look(ref shieldCrouchTicks, "shieldCrouchTicks", 60);
            Scribe_Values.Look(ref shieldFrags, "shieldFrags", 3);
            Scribe_Values.Look(ref shieldThrowTicks, "shieldThrowTicks", 240);
            Scribe_Values.Look(ref shieldRearguard, "shieldRearguard", true);
            Scribe_Values.Look(ref shieldEscort, "shieldEscort", true);
            Scribe_Values.Look(ref posture, "posture", true);
            Scribe_Values.Look(ref gasRaids, "gasRaids", true);
            Scribe_Values.Look(ref realSuppression, "realSuppression", true);
            Scribe_Values.Look(ref openCrossing, "openCrossing", true);
            Scribe_Values.Look(ref openGroundCells, "openGroundCells", 8);
            Scribe_Values.Look(ref overwhelmOdds, "overwhelmOdds", 3f);
            Scribe_Values.Look(ref assaultOdds, "assaultOdds", 3f);
            Scribe_Values.Look(ref suppressionLauncher, "suppressionLauncher", 4f);
            Scribe_Values.Look(ref smokeSettleTicks, "smokeSettleTicks", 150);
            Scribe_Values.Look(ref attritionTicks, "attritionTicks", 2700);
            Scribe_Values.Look(ref boundOdds, "boundOdds", 1.5f);
            Scribe_Values.Look(ref proneVisual, "proneVisual", true);
            Scribe_Values.Look(ref raidMemory, "raidMemory", true);
            Scribe_Values.Look(ref runAndGunByRole, "runAndGunByRole", true);
            Scribe_Values.Look(ref adaptiveLoadouts, "adaptiveLoadouts", true);
            Scribe_Values.Look(ref memoryShare, "memoryShare", 0.3f);
            Scribe_Values.Look(ref memoryIntelMin, "memoryIntelMin", 0.25f);
            Scribe_Values.Look(ref memoryHalfLifeDays, "memoryHalfLifeDays", 30f);
            Scribe_Values.Look(ref memoryArmourHigh, "memoryArmourHigh", 8f);
            Scribe_Values.Look(ref memoryMessages, "memoryMessages", true);
            Scribe_Values.Look(ref equipRaiders, "equipRaiders", false);
            Scribe_Values.Look(ref proneMoveFactor, "proneMoveFactor", 0.25f);
            Scribe_Values.Look(ref proneLabel, "proneLabel", true);
            Scribe_Values.Look(ref satisfiedHold, "satisfiedHold", true);
            Scribe_Values.Look(ref satisfiedAssaultTicks, "satisfiedAssaultTicks", 3000);
            Scribe_Values.Look(ref satisfiedStallTicks, "satisfiedStallTicks", 1200);
            Scribe_Values.Look(ref satisfiedLossShare, "satisfiedLossShare", 0.4f);
            Scribe_Values.Look(ref satisfiedPrepMaxTicks, "satisfiedPrepMaxTicks", 15000);
            Scribe_Values.Look(ref flanking, "flanking", true);
            Scribe_Values.Look(ref flankAxisBonus, "flankAxisBonus", 600f);
            Scribe_Values.Look(ref flankPositionBonus, "flankPositionBonus", 4f);
            Scribe_Values.Look(ref syncedAssault, "syncedAssault", true);
            Scribe_Values.Look(ref assaultTicksPerCell, "assaultTicksPerCell", 3f);
            Scribe_Values.Look(ref assaultBoundTicks, "assaultBoundTicks", 240);
            Scribe_Values.Look(ref mgShortBurst, "mgShortBurst", 5);
            Scribe_Values.Look(ref rifleShortBurst, "rifleShortBurst", 3);
            Scribe_Values.Look(ref suppressionScale, "suppressionScale", 2.5f);
            Scribe_Values.Look(ref ammoWithdraw, "ammoWithdraw", true);
            Scribe_Values.Look(ref ammoWithdrawShare, "ammoWithdrawShare", 0.5f);
            Scribe_Values.Look(ref flankManeuver, "flankManeuver", true);
            Scribe_Values.Look(ref flankTicks, "flankTicks", 2400);
            Scribe_Values.Look(ref breachFallback, "breachFallback", true);
            Scribe_Values.Look(ref lobOverWalls, "lobOverWalls", true);
            Scribe_Values.Look(ref evasion, "evasion", true);
            Scribe_Values.Look(ref focusFire, "focusFire", true);
            Scribe_Values.Look(ref combatMicro, "combatMicro", true);
            Scribe_Values.Look(ref raidingParty, "raidingParty", true);
            Scribe_Values.Look(ref threatAdvance, "threatAdvance", true);
            Scribe_Values.Look(ref duckWhenAimed, "duckWhenAimed", true);
            Scribe_Values.Look(ref medics, "medics", true);
            Scribe_Values.Look(ref coverCarry, "coverCarry", true);
            Scribe_Values.Look(ref siegePackAnimals, "siegePackAnimals", true);
            Scribe_Values.Look(ref contactReaction, "contactReaction", true);
            Scribe_Values.Look(ref medicReach, "medicReach", 30f);
            Scribe_Values.Look(ref suppressionArmorFloor, "suppressionArmorFloor", 0.6f);
            Scribe_Values.Look(ref suppressionAuto, "suppressionAuto", 1.5f);
            Scribe_Values.Look(ref suppressionArrow, "suppressionArrow", 0.3f);
            Scribe_Values.Look(ref suppressionExplosive, "suppressionExplosive", 3f);
            Scribe_Values.Look(ref suppressionDecay, "suppressionDecay", 2f);
            Scribe_Values.Look(ref suppressionHoldTicks, "suppressionHoldTicks", 60);
            Scribe_Values.Look(ref gasRadius, "gasRadius", 4f);
            Scribe_Values.Look(ref gasHoldTicks, "gasHoldTicks", 900);
            Scribe_Values.Look(ref proneHeightFactor, "proneHeightFactor", 0.35f);
            Scribe_Values.Look(ref proneRecoverTicks, "proneRecoverTicks", 180);
            Scribe_Values.Look(ref fireDoctrine, "fireDoctrine", true);
            Scribe_Values.Look(ref fireCqbRange, "fireCqbRange", 8f);
            Scribe_Values.Look(ref fireCloseRange, "fireCloseRange", 15f);
            Scribe_Values.Look(ref fireMediumRange, "fireMediumRange", 30f);
            Scribe_Values.Look(ref funnelCaution, "funnelCaution", true);
            Scribe_Values.Look(ref funnelMinCells, "funnelMinCells", 3);
            Scribe_Values.Look(ref funnelHoldTicks, "funnelHoldTicks", 1200);
            Scribe_Values.Look(ref funnelHolders, "funnelHolders", 2);
            Scribe_Values.Look(ref plunder, "plunder", true);
            Scribe_Values.Look(ref plunderChaseDist, "plunderChaseDist", 20f);
            Scribe_Values.Look(ref plunderLooterShare, "plunderLooterShare", 0.34f);
            Scribe_Values.Look(ref plunderIdleWithdrawTicks, "plunderIdleWithdrawTicks", 1800);
            Scribe_Values.Look(ref plunderDuringBreach, "plunderDuringBreach", true);
            Scribe_Values.Look(ref plunderPrepRadius, "plunderPrepRadius", 25f);
            Scribe_Values.Look(ref plunderRecallFraction, "plunderRecallFraction", 0.6f);
            Scribe_Values.Look(ref tacticalCompetence, "tacticalCompetence", true);
            Scribe_Values.Look(ref competenceIntelWeight, "competenceIntelWeight", 0.7f);
            Scribe_Values.Look(ref competenceLossOfCommand, "competenceLossOfCommand", true);
            Scribe_Values.Look(ref competenceReadout, "competenceReadout", false);
            Scribe_Values.Look(ref siegeGuards, "siegeGuards", true);
            Scribe_Values.Look(ref cornerFire, "cornerFire", true);
            Scribe_Values.Look(ref gasAmmoSwap, "gasAmmoSwap", true);
            Scribe_Values.Look(ref keepRange, "keepRange", true);
            Scribe_Values.Look(ref launchersStayOut, "launchersStayOut", true);
            Scribe_Values.Look(ref crewWeapons, "crewWeapons", true);
            Scribe_Values.Look(ref fieldFight, "fieldFight", true);
            Scribe_Values.Look(ref indirectFire, "indirectFire", true);
            Scribe_Values.Look(ref breachGear, "breachGear", true);
            Scribe_Values.Look(ref hskItemsOnly, "hskItemsOnly", true);
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref nolbRescueFilter, "nolbRescueFilter", true);
            Scribe_Values.Look(ref raidGoals, "raidGoals", true);
            Scribe_Values.Look(ref raidIntents, "raidIntents", true);
            Scribe_Values.Look(ref slavingShare, "slavingShare", 0.15f);
            Scribe_Values.Look(ref punitiveShare, "punitiveShare", 0.5f);
            Scribe_Values.Look(ref escalation, "escalation", true);
            Scribe_Values.Look(ref escalationSize, "escalationSize", 0.3f);
            Scribe_Values.Look(ref raidLetters, "raidLetters", true);
            Scribe_Values.Look(ref prisonersReport, "prisonersReport", true);
            Scribe_Values.Look(ref arrivalChoice, "arrivalChoice", true);
            Scribe_Values.Look(ref operations, "operations", true);
            Scribe_Values.Look(ref operationChance, "operationChance", 0.2f);
            Scribe_Values.Look(ref scoutShare, "scoutShare", 0.10f);
            Scribe_Values.Look(ref sabotageShare, "sabotageShare", 0.15f);
            Scribe_Values.Look(ref scoutObserveTicks, "scoutObserveTicks", 2500);
            Scribe_Values.Look(ref sabotageTicks, "sabotageTicks", 6000);
            Scribe_Values.Look(ref sabotageIdleTicks, "sabotageIdleTicks", 1500);
            Scribe_Values.Look(ref cornerHoldTicks, "cornerHoldTicks", 1800);
        }

        public void DoWindow(Rect inRect) => SettingsWindow.Draw(this, inRect);
    }
}
