using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKTacticalAI
{
    public class TacticalAIMod : Mod
    {
        public const string Version = "0.1.0";
        public static TacticalAISettings Settings;
        public static Harmony Harmony;

        public TacticalAIMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TacticalAISettings>();
            Harmony = new Harmony("sonsalt.hsk.tacticalai");
            Harmony.PatchAll(typeof(TacticalAIMod).Assembly);
            Log.Message("[HSK Tactical AI] " + Version + " loaded" + (AILab.Active ? " - AI Lab run: " + AILab.Config.runId : ""));
        }

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
        public float maxApproachDetour = 1.5f;       // assault position: walk to the defenders ≤ this × straight line (+6)
        public bool avoidFunnels = true;             // one covered entrance (killbox): make a new one instead
        public float funnelCellPenalty = 2500f;      // extra cost per narrow route cell under fire (corridor, doorway, gap)
        public int newEntranceWallCost = 600;        // planning cost per wall to blow when making a new entrance
        public float newEntranceRatio = 0.85f;       // new entrance chosen if it costs less than this × the walking route
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
        public float assaultForceRatio = 1.2f;       // scaled strength ratio needed to assault from one direction (1.0 = about even)
        public float forceScale = 4f;                // calibration (Lab v14-v15): raw 0.25 with several breaches = about even
        public float fortificationFactor = 1.5f;     // defenders in prepared positions count this much more
        public float multiAxisRatioFactor = 0.85f;   // several simultaneous directions need this much less
        public float commitRatioTolerance = 0.8f;    // at H-hour, withdraw only below this share of the required ratio
        public float defendedAxisPenalty = 8000f;    // breach planning: extra cost for a breach point facing the defence (0 from 90° off)
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
            Scribe_Values.Look(ref assaultForceRatio, "assaultForceRatio", 1.2f);
            Scribe_Values.Look(ref forceScale, "forceScale", 4f);
            Scribe_Values.Look(ref fortificationFactor, "fortificationFactor", 1.5f);
            Scribe_Values.Look(ref multiAxisRatioFactor, "multiAxisRatioFactor", 0.85f);
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
        }

        private Vector2 scroll;

        public void DoWindow(Rect inRect)
        {
            var view = new Rect(0f, 0f, inRect.width - 16f, 1500f);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var l = new Listing_Standard();
            l.Begin(view);
            l.Label("HSK Tactical AI " + TacticalAIMod.Version + " - threat map and AI Lab (no behaviour changes yet).");
            l.GapLine();
            l.CheckboxLabeled("Compute the threat map", ref enableThreatMap,
                "Where your colonists and turrets can shoot. Later versions use it for raider tactics; 0.1 only shows it.");
            l.CheckboxLabeled("Show threat map overlay", ref showThreatOverlay,
                "Also toggleable with the button in the bottom-right play-settings bar.");
            l.Label("CPU budget per tick: " + budgetMsPerTick.ToString("0.0") + " ms");
            budgetMsPerTick = l.Slider(budgetMsPerTick, 0.2f, 4f);
            l.Label("Update interval with enemies present: " + cycleTicksCombat + " ticks");
            cycleTicksCombat = (int)l.Slider(cycleTicksCombat, 30, 600);
            l.Label("Update interval in peace: " + cycleTicksPeace + " ticks");
            cycleTicksPeace = (int)l.Slider(cycleTicksPeace, 120, 5000);
            l.Label("Weight: drafted shooter " + weightDrafted.ToString("0.00") + ", undrafted " + weightUndrafted.ToString("0.00") + ", turret " + weightTurret.ToString("0.00"));
            weightDrafted = l.Slider(weightDrafted, 0f, 3f);
            weightUndrafted = l.Slider(weightUndrafted, 0f, 3f);
            weightTurret = l.Slider(weightTurret, 0f, 3f);
            l.GapLine();
            l.CheckboxLabeled("Smart breaching: only break walls when walking around is worse", ref smartBreaching,
                "Breaching raids compare the open route (walking + exposure to your fire) with breaking through walls. " +
                "Off = vanilla/Smarter Raid AI behaviour (walls are almost free, so raiders cut through even with a way around).");
            l.Label("Cost of walking through your fire: " + exposureCost.ToString("0"));
            exposureCost = l.Slider(exposureCost, 0f, 300f);
            l.Label("Cost per wall to break: " + wallPenalty.ToString("0") + " + " + wallHpPenalty.ToString("0.00") + " per HP");
            wallPenalty = l.Slider(wallPenalty, 0f, 2000f);
            wallHpPenalty = l.Slider(wallHpPenalty, 0f, 2f);
            l.Label("Extra cost to tunnel through rock: " + naturalWallExtraCost + " (vanilla 20)");
            naturalWallExtraCost = (int)l.Slider(naturalWallExtraCost, 20, 5000);
            l.GapLine();
            l.CheckboxLabeled("Give raiders extra equipment", ref equipRaiders, "Off (default): raiders keep the loadout they spawned with, and pick their breach method from it and their tech level. On: breach specialists get charges, tools or stick bombs, some raiders get grenades and smoke, gas raids get masks and tox grenades.");
            l.CheckboxLabeled("RunAndGun: only assault troops shoot on the move", ref runAndGunByRole, "With RunAndGun installed: riflemen and assault raiders in the assault may shoot on the move; medics, breachers, shields, support fire, carriers and withdrawing raiders walk.");
            l.CheckboxLabeled("Factions remember earlier raids", ref raidMemory, "A faction whose raid failed and had survivors who got away learns from it: a walk-in that died becomes a breach next time, a failed breach a siege. Wiping a raid out teaches them nothing. Fades over about two seasons.");
            l.CheckboxLabeled("Adapt loadouts to what hurt them", ref adaptiveLoadouts, "With raid memory: up to 30 % of the next raid swaps gear against what hurt them (launchers vs turrets, long rifles and smoke vs snipers, shotguns and SMGs vs melee, armour-piercing ammo vs armour). Only weapons their kind may carry, worth no more than what they had; ammo is swapped, not added.");
            l.CheckboxLabeled("Breaching equipment", ref breachKit,
                "With extra equipment on: breaching raids bring tools for their tech level - Industrial and up carry breach charges they plant against walls " +
                "(shoot the charge to defuse it); tribal and medieval breachers get breach axes or stick bombs.");
            l.Label("Breach charges per specialist: " + chargesPerSpecialist);
            chargesPerSpecialist = (int)l.Slider(chargesPerSpecialist, 1, 5);
            l.GapLine();
            l.CheckboxLabeled("Assault planner: flank and attack from several directions", ref assaultPlanner,
                "Raids that can walk to your defenders split into groups, go around obstacles to assault positions on " +
                "different sides, wait for each other and attack at the same time. They don't hit walls they can walk around. " +
                "Raids that can't walk in breach instead (with breaching equipment).");
            l.Label("Maximum attack directions: " + maxAxes);
            maxAxes = (int)l.Slider(maxAxes, 1, 4);
            l.Label("Minimum angle between directions: " + minAxisSeparation.ToString("0") + "°");
            minAxisSeparation = l.Slider(minAxisSeparation, 30f, 150f);
            l.Label("Wait for the other groups at most: " + (syncTimeoutTicks / 60) + " s");
            syncTimeoutTicks = (int)l.Slider(syncTimeoutTicks, 600, 12000);
            l.Label("Wide arcs: extra route cost per unit of your fire: " + routeCostPerFire.ToString("0") + " (0 = straight routes)");
            routeCostPerFire = l.Slider(routeCostPerFire, 0f, 2000f);
            l.CheckboxLabeled("Avoid killbox entrances: make a new entrance instead", ref avoidFunnels,
                "If the only way in on foot is a narrow entrance covered by your fire, raids that can break walls blow " +
                "their own entrance elsewhere. A raid bleeding out in an entrance pulls back and does the same.");
            l.Label("Breaching: the rest of the raid waits " + stagingDistance + " cells back until the breach is open");
            stagingDistance = (int)l.Slider(stagingDistance, 6, 25);
            l.Label("Breach: wait beside the hole, more than " + breachConeDegrees.ToString("0") + "° off the defenders' line through it");
            breachConeDegrees = l.Slider(breachConeDegrees, 20f, 85f);
            l.CheckboxLabeled("Smoke on the breach", ref smokeOnBreach,
                "When the wall opens, an Industrial+ breach team throws smoke on the hole and just inside it before the raid goes in.");
            l.Label("Breach points on different sides, opened together: up to " + maxBreaches + " (one per " + raidersPerBreach + " raiders)");
            maxBreaches = (int)l.Slider(maxBreaches, 1, 4);
            l.CheckboxLabeled("Burn wooden walls", ref fireBreaching, "Raiders set wooden walls on fire instead of blowing them.");
            l.CheckboxLabeled("Shield wall", ref shieldTactics, "Raiders with ballistic or body shields lead the assault straight at the defenders; two gunners follow behind each shield and shoot past it. Through a breach the shield goes first.");
            l.CheckboxLabeled("Fire discipline", ref fireDoctrine, "Raiders choose aimed or snap shots and single, burst or full-auto fire by range and situation: aimed single shots at long range, aimed bursts at medium range, snap bursts up close, full auto in close quarters and to suppress defenders in cover. Marksmen always take aimed single shots; machine guns fire bursts.");
            l.CheckboxLabeled("Don't chase into funnels", ref funnelCaution, "When defenders fall back into a narrow corridor, raiders don't follow straight in: they throw grenades into it, hold covered spots beside its mouth and shoot whoever comes out, and go in after a while (smoke first). Shield bearers still lead in.");
            l.CheckboxLabeled("Plunder instead of chasing", ref plunder, "Raiders don't chase defenders who are far away or holed up: they kill livestock, loot valuables, burn crops and wooden buildings and smash workbenches, generators and turrets - out of the defenders' fire - forcing the defenders to come out. With nothing left to take they withdraw.");
            l.CheckboxLabeled("Finish the assault before leaving satisfied", ref satisfiedHold, "Vanilla raiders leave \"satisfied\" once they have damaged enough of the colony. While a planned assault is under way and making progress they finish it first; they leave once it stalls, has run a while, or the defenders are down. Damage from their own breach (walls and doors) never counts.");
            l.CheckboxLabeled("Go prone under fire", ref posture, "Suppressed raiders without cover drop flat: a much smaller target (Combat Extended hit profile), crawling, still shooting. They get up once the suppression ends. Not shield bearers, breachers planting, close quarters or a retreat.");
            l.CheckboxLabeled("Show prone raiders lying down", ref proneVisual, "Prone raiders are drawn in the crawl pose, weapon still shown. Visual only.");
            l.CheckboxLabeled("Label prone raiders", ref proneLabel, "Shows \"prone\" above a raider for as long as he lies flat, so prone and downed raiders are easy to tell apart.");
            l.CheckboxLabeled("Gas and burn-out", ref gasRaids, "Against defenders in a small enclosed room (bunker, mountain room, keep), Industrial raids bring gas masks, tox grenades and, if the room burns, molotovs. When the breach opens, they gas and burn the room first, hold the exits while it works, then go in masked.");
            l.CheckboxLabeled("Realistic suppression", ref realSuppression, "Changes Combat Extended's suppression for everyone: normal body armour no longer reduces the scare of a near miss (only armour the round cannot penetrate does, to 60 %), machine guns x1.5 and automatic weapons x1.2 for the volume of fire, arrows and bolts x0.3, hand grenades x3, grenade and rocket launchers x4, all of it x" + suppressionScale.ToString("0.0") + ". Heads stay down longer: suppression starts fading 1.5 s after the last near miss and fades a quarter as fast.");
            l.CheckboxLabeled("Don't cross open ground into fire", ref openCrossing, "Before assaulting over open ground under the defenders' guns, raiders take a covered route if there is one; otherwise they rush only with overwhelming force or behind a smoke screen, else they stay out of range and let their snipers and machine guns work, and withdraw if that achieves nothing.");
            l.CheckboxLabeled("Flanking", ref flanking, "Raiders shoot the defenders their cover does not protect first (snipers above all), pick firing positions from the side of the defenders' cover, and prefer assault directions the defences do not face.");
            l.CheckboxLabeled("Focus fire and covered advance", ref focusFire, "Raiders concentrate their fire on exposed, wounded and dangerous defenders (machine guns, launchers, snipers) and on the ones their comrades already shoot; in the assault they move on you along routes that avoid your fields of fire.");
            threatAdvance = focusFire;
            l.CheckboxLabeled("Combat micro", ref combatMicro, "Raiders under fire from several of your shooters fall back to cover, melee raiders intercept anyone hitting their gunners and come in from the side, and gunners step back from melee attackers instead of trading blows.");
            l.CheckboxLabeled("Raiding party", ref raidingParty, "In raids of 10 or more, a sixth of the raiders go for your valuables, livestock and crops from the start, out of your fire, while the rest attack.");
            l.CheckboxLabeled("Evade and duck", ref evasion, "Raiders get out of your tox gas, away from fire and from where your grenades and launched rounds will land; a raider in the open that you are aiming at ducks into cover or drops flat.");
            l.CheckboxLabeled("Combat medics", ref medics, "The raid's medic throws smoke on a comrade down in your field of fire, carries him to cover and tends him there.");
            l.CheckboxLabeled("Coordinated assault", ref syncedAssault, "Walk-in assaults from several directions arrive together (groups on shorter routes start later), and inside each group half moves while the other half stops and fires, so the defenders are never without fire on them.");
            l.CheckboxLabeled("Strength check before the assault", ref forceRatioCheck,
                "Raids compare their strength with your defenders (weapons, skill, armour, health, turrets). " +
                "Too weak for an assault: they besiege you (factions that can), snipe from out of range, plunder, or hold back and leave instead of storming in.");
            l.Label("Odds needed to assault: " + assaultOdds.ToString("0.0") + " : 1 (classic 3 : 1)");
            assaultOdds = l.Slider(assaultOdds, 1f, 5f);
            l.Label("A raid that can't break your walls holds " + (standoffTicks / 60) + " s, then withdraws (factions that can siege besiege you instead)");
            standoffTicks = (int)l.Slider(standoffTicks, 1200, 30000);
            l.End();
            Widgets.EndScrollView();
        }
    }
}
