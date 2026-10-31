# Changelog

## 1.10.0 (2026-10-31): HSK Tactical AI 0.4.0

**HSK Tactical AI 0.4.0** (test release): feedback from the HSK dev team on dependencies, performance and the settings.

- **Bases nobody can reach:** a raid facing deep water (or other ground it can't cross) no longer plans a breach or a
  tunnel that ends at the water's edge. With no way in it besieges you (factions that can) or holds at range, shoots
  what it can see and leaves. Before, the raiders stood around issuing move orders the pathfinder couldn't serve, which
  cost game speed. Breaching kit is only handed out when there is a wall to break.
- **Only HSK, CE and vanilla items** (new setting, on by default): everything the AI hands to raiders or picks for them
  (breaching kit, crew-served guns, swapped weapons, digging tools) comes from RimWorld and its DLC, Combat Extended or
  the HSK pack. Tactical AI's own breach and mining charges are not used then; industrial raids breach with grenades
  and tools.
- **Safe after updates:** every patch is applied on its own. If an HSK, CE or RimWorld update renames something a
  feature hooks, only that feature switches off; the log gets one line saying which and why, and the settings window
  lists it. Optional mods that aren't loaded are listed in one log line.
- **Master switch:** "Tactical AI on" at the top of the settings. Off removes every patch of the mod at once, also in a
  running game; raids go back to vanilla duties.
- **Tactical competence off** now gives every raid the same ordinary command (50 %). Before, off meant every raid used
  every tactic.
- Switching the assault planner off mid-raid hands running raids back to vanilla at once. The No One Left Behind rescue
  filter has its own switch.
- **New settings window:** sections with headers and dividing lines, one row per setting (label left, control right),
  options that depend on another stay in place greyed out with the reason on hover, number fields next to the sliders,
  a marker on changed values, a reset per section (with confirmation) and a search box. Six options that had no
  checkbox get one (planned withdrawal, tunnelling, door breaching, grenade kit, hand grenades over walls, arson).
  Four unused settings removed.
- **Translations:** all settings, the raid letter lines and the messages are in Languages/English/Keyed, ready for other
  languages.
- **Fixes found in testing:**
  - a breacher whose breach axe went back into his pack (his bow back in hand, e.g. by a sidearm mod) no longer makes
    the raid give up;
  - a breach team that only looks out of means for a moment (reloading) no longer calls the raid off;
  - grenades that don't break a door (after twice the planned frags), or a breach team out of rounds with little wall
    left, give way to bashing the rest down;
  - a breach raid whose stack points are all under fire no longer waits forever for H-hour;
  - a crew-served gun is packed up by its own gunner instead of a second raider ("could not reserve" error).
- AI Lab: island scenarios, game speed and AI cost per phase of a raid (RimWorld's forced normal speed during
  fights switched off for measuring), a settings-window test, showcase mode for recording.
- **Tested** in the AI Lab on RimWorld 1.7.4985, about 60 runs in 7 rounds: island and island with a door, fortress
  with HSK grenades and with breach charges, crew-served guns, competence off, master switch off, killbox, tribal
  village, the settings window and a 219-mod HSK list. In the last round every breach raid got in, with no errors from
  the mod.
  - Island, tribal raid: with Tactical AI the raid holds at range and leaves (about 560 to 940 ticks/s on ultrafast).
    Without it, Smarter Raid AI's sapper keeps searching for a path (about 3,800 errors in 9,000 ticks, 350 to 530
    ticks/s) and the raid never leaves.
  - Average AI cost 0.02 to 0.09 ms per tick.
  - The 1.6 build compiles but wasn't tested in game.
## 1.9.0 (2026-10-27): HSK Chemical Warfare 0.1.0

**HSK Chemical Warfare 0.1.0** (new, test release, RimWorld 1.7): chemical warfare on PalverZ's Custom Gas Framework.
It started as a rework of PalverZ's Gas Pack and now replaces it.

- **Nine weapons:** deathbreath fog (down in under 2 s, dead in about 5), hexvein mist (nerve agent, about 11 s,
  insects included), slumbercloud (asleep for hours; a coma kills if left in thick gas), barbsmoke (barbs in the skin:
  pain shock and burns), dreadspore vapor (berserk or flee after about 6 s), gear-rot fume (rots worn gear within a
  minute), scourge fog (acid: kills plants, dissolves loot, eats walls; raiders path around it), aegis swarm
  (friend-or-foe drones that cut only hostiles; some cuts reach the organs), cypherphage cloud (stuns, blinds, melts
  down, scrambles or hijacks mechanoids).
- **Two farm chemicals:** Agent Orange (herbicide: grass, crops and bushes die within about 4 s; trees go bare and die
  after minutes; special trees are spared) and Verminbane (pesticide: dose scales with body size; rats die in about
  2 s, megascarabs in about 5, people only sicken; kills insect hives; its blood thinner makes wounds bleed harder and
  starts internal bleeding a few hours after a heavy dose).
- **Protection by route:** a gas mask stops anything breathed completely; only a chemical protection suit stops skin
  agents; hexvein and scourge's acid need both. Acid ignores ordinary clothes and armor. Nerve agents ignore general
  toxin resistance (HSK gives rats 99 %).
- **Delayed and lasting harm:** deathbreath poisoning (about 6 h), scourge's pulmonary edema (about 7 h) and
  verminbane's internal bleeding (about 17 h) kill unless a doctor treats them; a good tend reverses them. Survivors
  can keep brain or heart scarring (deathbreath, slumbercloud coma), permanent nerve damage (hexvein) or a scarred
  lung (scourge).
- **Research:** agrochemical aerosols, weaponized gas, sedative aerosols, nerve agents (also needs agrochemicals),
  terminal asphyxiants, corrosive agents, IFF nanite swarms, cypherphage nanites; with HSK also Chemistry II/III,
  Drugs and Robotics nodes. Prices follow HSK material values (canister 24 silver for Agent Orange up to 470 for
  cypherphage).
- **Delivery:** canister, gas trap, vent and mortar shell for every gas, trap implants for three; with Combat Extended
  also hand grenades, 40x46mm grenades and 81mm shells. Every gas draws its own coloured cloud with release puffs; every
  item has its own sprite.
- **Switching from PalverZ's Gas Pack:** uses the Gas Pack's def names, so a colony can remove the Gas Pack, add this
  mod and keep its items. Marked incompatible with the Gas Pack.
- Tested on RimWorld 1.7.4990 with HSK and Combat Extended (223 mods): 288 of 288 checks of its in-game test pass, with
  no errors from the mod. Not yet tested without HSK or without Combat Extended.

## 1.8.0 (2026-10-26): HSK Tactical AI 0.3.1, HSK 1.7 Compatibility Fixes 1.0.2

**HSK Tactical AI 0.3.1**

- **HSK primitive tools** (Hardcore-SK ee8bc72, on HSK `alpha` and coming to 1.7 with its next merge): the new
  stone-age pickaxe and hammer count as breach tools. At about 3 wall damage per second they get a tenth of the breach
  axe's budget. On their own they don't count as the raid's means of breaching, so a tribal raid with only those still
  brings its breach kit, and the tools help on the wall. The primitive pickaxe digs no faster than a steel one, so it
  doesn't make a raid tunnel.
- **HSK renamed its tools** in the same commit (`TFJ_Tool_Mining_Pickaxe` -> `Tool_Mining_Pickaxe`, `TFJ_Tool_Paxe` ->
  `Tool_Paxe`). Both names are recognised, so pickaxes and paxes keep working as breach tools on either HSK version.
- AI Lab on 1.7 (the tools aren't in HSK 1.7 yet): tribal and medieval fortress raids still bring their kit and breach,
  no errors.

**HSK 1.7 Compatibility Fixes 1.0.2**

- **HSK meal textures moved:** HSK moved its meal and canned-food textures from `Things/Item/...` to `Things/Food/...`
  ("Meals textures path update"). Addons that still used the old paths got "Collection cannot init: No textures found"
  and an invisible item. Seen with HSK Watcher Fallout3 (mashed potatoes). Any def that still points at one of the ten
  removed paths now gets the new one; nothing changes if no mod uses them. Tested with a full 1.7 HSK modlist
  (219 mods, Fallout3 included): 0 new errors.
- About: load after New 130 mm Rocket Missiles and HSK Watcher Fallout3; the description lists the 130 mm smoke fix too.

## 1.7.0 (2026-10-26): HSK Tactical AI 0.3.0 (ideas from Raid Tactics)

Raid Tactics (Funstab) does most of its work before a raid arrives. Tactical AI is marked incompatible with it, so its
best ideas now live here, built on our raid memory and tactical competence:
- **The raid letter says why:** a line on how the faction's last raid here went and what it does about it ("The last
  group they sent here, all 12 of them, never came back." / "This group is not using your entrance; it is coming
  through a wall." / "They are after people, not goods.").
- **Escalation:** a faction you beat here is angry for a while and sends a bigger raid (up to 30 % more points at full
  anger). A raid that goes its way calms it down; the anger fades with the memory. The raid now knows its own last
  result even when nobody came back (how many went, how many didn't return).
- **Slaving and punitive raids:** an assault now comes for goods (as before), for people or for revenge.
  - Slavers burn and loot nothing and aren't satisfied by damage done (the game's "leave after enough colony damage"
    goes; heavy losses still send them home). They grab a downed colonist within 25 cells (into your fire only when
    two riflemen can cover it, or when he lies right next to a slaver) and leave once one of them carries a captive.
  - A punitive raid only comes after a bad beating its faction knows the reason for. It takes nothing, does not
    withdraw and has its own break-off triggers removed; it leaves only when nobody of yours has stood for 40 s, or
    after a day.
- **Choosing the arrival:** a faction that knows your layout and failed to walk in comes from the map edge whose way to
  your base crosses the least of your fire. A spacer faction that failed both walking in and breaching drops in by pod
  at the edge, when its raid type allows pods.
- **Coordinated operations** (sharp commands, 20 % of raids, twice as likely after a failed raid here):
  - hammer and anvil: a third of the raid (factions that lay sieges) digs in at the siege spot and shells you; the rest
    waits out of range and goes in once the guns have been firing about 25 s (counted from the first gun standing; at
    most 50 s after the assault is otherwise ready);
  - armoured spearhead: the best-armoured quarter (2-6) goes in first; the rest fight from where they stand and follow
    once the spearhead is inside, down, or after 20 s.
- **Released prisoners report back:** a prisoner you release, or one who escapes over the map edge, tells his faction
  about your defences like a raider who got away (+30 % intel). Not tested in the AI Lab yet.
- **Drop-pod raids are planned:** the planner waits until the pods have opened (at most 10 s) instead of planning with
  raiders still inside, which made it fall back to vanilla.
- Tested in the AI Lab on RimWorld 1.7.4985: slavers took a captive and left with nothing looted; a punitive raid had
  12 break-off triggers removed and left only when nobody of ours stood; a quiet side was chosen in the village (no fire
  on the way vs 0.24 on the worst edge); spacers dropped in by pod and were planned once the pods opened; hammer and
  anvil split 5 into a siege camp with 2 mortars and the assault went in after the shelling; the spearhead went in first
  and the rest followed once it was inside; the raid letter and escalation (16 instead of 13 raiders) showed; one raid
  with the full 219-mod list (Fallout 3 included). No errors from the mod.
- Each has a switch (and weights) in the mod settings. AI Lab: `raidIntentOverride`, `arrivalOverride`,
  `operationOverride`; memory is seeded before the raid is sent; slaving and punitive runs go on 60 s after the defence
  is down; new counters.

## 1.6.0 (2026-10-26): HSK Tactical AI 0.2.2

- **The 1.7 builds catch up:** Tactical AI and Weapon Blueprints for 1.7 are rebuilt against RimWorld 1.7.4985, so the
  gun-team fix from 0.2.1 (bearers stay with the gunner, the gunner waits 15 s at most) is in the 1.7 build too. AI Lab
  on 1.7: both gun teams set up their guns in the patrol and hold raids that used to stall on 1.6.
- **Crew-served guns aren't left behind** (playtest): when the raid leaves - a planned withdrawal, the game's own "leave"
  after enough damage or losses, or our switch to a plain exit - the nearest able crew member packs the set-up gun up
  (4 s) and carries it out. If it stands in heavy fire he wrecks it instead (1.5 s, within 15 cells). Only when nobody
  can reach it, or the raid flees in panic, does it stay behind as loot. A gun still packed leaves with its gunner.
- **Covering fire for the wounded** (playtest): a raider carrying a downed comrade in the defenders' fire or sight gets up
  to two unhurt raiders with guns from within 25 cells, at once (they drop what they were doing). They take cover 3-8
  cells off him on the side toward the defence and fire at whoever threatens him until he is out of fire. No One Left Behind rescues into fire, blocked since 0.2.0,
  now go ahead when two such riflemen are there and the fire isn't extreme. Part of the overwatch doctrine (dull raids
  don't); switch "Cover the wounded".
- **Reacting to contact** (playtest: raiders kept walking, or went to pick up a wounded man, while a defender stepped into
  sight and opened fire): every second, a raider with a gun who gets a defender newly in sight within his range while
  walking, waiting or going for a downed comrade breaks it off, takes cover close by and fires back. Sharp commands react
  at once, dull ones up to 2 s late. A rescue into a defender's sight only goes ahead when two riflemen can cover it;
  the No One Left Behind block now also counts a defender's sight, not only the threat map (which lags behind a defender
  who just stepped out). Carriers keep carrying; breachers at their charges, gun crews, shield bearers and raiders
  already fighting keep at their task. Switch "React to contact".
- **Siege supplies by pack animal** (playtest): vanilla drops every siege's building material, shells and meals by drop
  pod, medieval kingdoms and tribes included. Tribal and medieval sieges now get them on pack animals (the faction's
  caravan carriers, else muffalo) from the map edge nearest the camp; the animals unload at the camp and leave.
  Industrial and later factions keep their drop pods. Switch "Siege supplies by pack animal".
- **Full range for guns** (playtest):
  - crew-served guns set up at up to 95 % of their range (was 55-80 %, at most 55 cells), the longest post out of fire
    preferred;
  - siege camps go as far out as their guns reach: 90 % of the shortest reach among the siege guns the faction can get,
    plus the 10 cells the guns may stand forward in the camp (35-110 cells; was a fixed 75), and need sight of a wall
    within that reach.
- The 1.6 build projects compile against the Krafs.Rimworld.Ref 1.6 reference package, so 1.6 builds work whichever
  game version is installed.
- AI Lab: `labDownRaiderTick` downs the most exposed raider at a set time (rescue and covering-fire tests); new counters
  for packed/wrecked guns, covering fire, covered rescues, siege caravans, siege reach and camp distance.

## 1.5.0 (2026-10-26): RimWorld 1.6 support

All seven mods now also run on **RimWorld 1.6 with HSK `alpha`** (the HSK 1.6 branch). Tested with HSK alpha's full
163-mod list: every mod loads with no errors from our mods. Tactical AI ran through the AI Lab raids (fortress, village,
sally, sabotage, scouting, stripped breach gear, gun teams). Weapon Blueprints and Relation Based Trading ran through
their test harnesses.
- **HSK Tactical AI 0.2.1:** a 1.6 build next to the 1.7 one (`LoadFolders.xml` picks it).
  - Gun teams: when a bearer found no covered spot he stood still, and the gunner waited for him forever, so the gun was
    never set up (seen on 1.6). The bearer now stays close behind the gunner, and the gunner waits at most 15 s.
  - The 1.7 build still has the 0.2.0 code (it can only be rebuilt and tested with the game on 1.7).
- **HSK Weapon Blueprints 0.1.1:** a 1.6 build next to the 1.7 one. No gameplay change.
- **Relation Based Trading 2.1.1:**
  - The tech limit steps at goodwill -50, 0 and 50 are now exact. Before, float rounding at exactly 50 could differ
    between builds and between the limit shown and the limit applied.
  - Pricing a book at the quality limit no longer rebuilds the book. Rebuilding generated a random author pawn for
    every book in a trader's stock, which was slow and in a big modlist could throw errors.
  - Rebuilt for 1.5, 1.6 and 1.7.
- **HSK Fortifications Rebalance 1.0.1:** the removed Aoba research projects are now also taken out of other projects'
  prerequisites. On HSK 1.6 (without XML Extensions) Aoba's patches still added FT_Chemistry as a prerequisite of
  Medicine Production, which left a broken research link.
- **HSK Outposts, HSK Zero Research Start, HSK 1.7 Compatibility Fixes:** 1.6 added to the supported versions; no other
  change.
- README: game version table and test status for 1.6.

## 1.4.0 (2026-10-25)

- **HSK Tactical AI 0.2.0** (test release, details below). Highlights:
  - tactical competence: the leader's intelligence and the faction's tech level decide how smart a raid acts;
  - every weapon at its own range;
  - breach gear by tech level;
  - scouting and sabotage raids;
  - plans that adapt when defenders come out;
  - grenade launchers firing over walls and staying out of the breach;
  - crew-served guns;
  - clearing the breach by fire and entering in waves;
  - siege guards and spotters;
  - cover wherever there is some.

  Tested in the AI Lab with defenders that patrol, sally, hold the door and work alone outside; no errors from the mod.
- **HSK 1.7 Compatibility Fixes 1.0.1:** New 130 mm Rocket Missiles' white phosphorus smoke no longer breaks Combat
  Extended turrets and mortars.
- README: Tactical AI 0.2.

## HSK 1.7 Compatibility Fixes 1.0.1 (2026-10-25)

- **New 130 mm Rocket Missiles:** its white phosphorus smoke inherits the gas category but is a different kind of
  thing, so Combat Extended errored every time a turret or mortar checked a line of fire through it. That was up to
  2,000 errors per siege, and the turret stopped working. The smoke is moved out of the gas category; it still burns,
  heats and fades as before.

## HSK Tactical AI 0.2.0 (2026-10-25)

**Tactical competence:** how smart a raid acts now depends on its leader and its faction's tech level, instead of every
raid using every tactic.
- **Competence (0-100 %)** = tech ceiling x (30 % + 70 % x the leader's intelligence). The 70/30 split is a setting.
  - **Tech ceiling** (the most a raid of that tech level can reach): tribal 55 %, medieval 70 %, industrial 85 %,
    spacer, ultra and archotech 100 %. Mechanoids think as a hive (fixed intelligence 75 %).
  - **Leader:** the raid's faction leader, else its leader kind, royal title or the pawn with the highest combat
    power. HSK has no intelligence stat, so intelligence comes from the Intellectual skill plus the learning factor,
    times consciousness. The learning factor carries the intelligence traits (too smart, slow learner, HSK's dumb,
    dunce and volatile genius), learning genes and brain implants; consciousness covers head wounds and drugs.
  - **Loss of command:** when the leader goes down, dies or leaves, the next in rank takes over. The raid is then at
    most as sharp as him, minus a fifth, and confused for about 20 s.
- **What it scales:**
  - how far ahead and how well the raid reads your fire for its routes (dull raids see only nearby, heavy fire and walk
    nearly straight in);
  - killbox and funnel avoidance (a new entrance instead of your gate, holding funnel mouths). A sharp raid that can't
    get round your killbox and can't breach besieges you (factions that can) or stands off; a dull one walks in and
    chases your defenders into the funnel;
  - **traps and IEDs:** vanilla 1.7 lets an assault know a share of your traps (0.3 for "smart" raids, else none). That
    share now follows competence, from none to 95 %, plus up to 45 % more when survivors of earlier raids told of traps.
    Known traps are walked around (also on the planned routes); unknown ones are walked into;
  - breach planning: dull raids pick breach points nearly at random and ignore which side your defence faces; several
    simultaneous breaches and door breaches take a sharp command;
  - envelopment from 2 or 3 sides, synchronised arrival and fire and movement, base of fire, overwatch, smoke, flanking
    and flank groups, focus fire, covered advances;
  - the 3:1 strength check (dull raids skip it, middling ones are overconfident), the open-ground crossing logic, the
    breach fallback;
  - withdrawal: sharp raids cut their losses in time; dull ones fight on and then run the vanilla way;
  - raid memory: only a command that listens uses what survivors told, and sharper ones adapt more of the raid;
  - **each weapon at its own distance** (playtest): every raider fights at a distance set by his weapon's range,
    from 45 % of it for short guns to 70 % for long ones. Bowmen and other pre-industrial shooters step back when you
    come within 8-10 cells, riflemen within about 13, snipers within about 22. Pistols and SMGs close in before they
    shoot, from cover to cover (never across your open fire), and only step back from melee. Melee raiders go for anyone near them in the open and don't turn to run
    when an enemy is close. Inside a breach, and for the assault role, only melee makes them step back. Dull raids
    fight from wherever they stand. Setting: "Keep the weapon's range".
  Each tactic has a competence band, and one roll per raid decides within the band. A raid that masters a harder
  tactic always masters the easier ones.
- **Settings:** master switch, intelligence vs tech weight, loss of command, and a readout. The readout shows a message
  per raid with its competence and why, a log line, and the raids on the map in the threat-map button's tooltip.
  Switched off, every raid uses all tactics (the 0.1 behaviour).

**Area weapons where they work** (playtest):
- Raiders (sieges included) no longer put gas down in the open, where it drifts off: no tox, tear gas, healing gas,
  insecticide or firefoam grenades, and no gas shells from launchers (the waster heavies' M79s load 40 mm tox
  grenades). Gas is used only in a gas raid against an enclosed, roofed room. Smoke, EMP and flashbangs are unchanged.
- **Explosive rounds instead of gas** (playtest): a raider whose launcher carries only gas rounds (the waster heavies'
  40 mm tox grenades) swaps three quarters of them one for one for the explosive round of the same calibre, so he can
  breach and shell. A quarter stays gas for an enclosed room. Setting: "Explosive rounds instead of gas".
- **Shields that block the gun** (playtest): an energy shield belt blocks every ranged shot while it is up, and CE flags a
  ballistic shield with a two-handed gun. At planning, from the raider's own gear: a shield bearer with a one-handed gun
  in the pack switches to it and stays a shield bearer; otherwise the shield goes into the pack so the rifle works.
- **Reserve:** a raider with no usable weapon no longer waits where he arrived. He keeps up with the raid 8 cells behind
  its armed fighters, out of fire, and takes up a fallen comrade's gun (searched within 30 cells).
- **The right round for the job:** a launcher counts for breaching only with a round that breaks walls, loaded or
  carried; fire (molotovs, incendiary rounds) does not count against stone or steel (wooden walls are set alight by
  hand instead). At the wall the breacher reloads a carried HE round if gas, smoke or EMP is loaded (the contact beam and
  M79 take 40 mm tox grenades too). A raider loaded with gas who carries another round reloads it. One whose only
  weapon fires gas counts as unarmed: he picks up a gun, plunders or holds back.
- **Grenadiers soften the defence first:** grenadiers with an explosive launcher (loaded or in the pack) join the base of
  fire. Before the assault moves they shell the defenders' positions and cover from a covered spot in range, together
  with the machine guns and marksmen, instead of waiting with the assault group. Launcher shots never go through their
  own men.
- **Doors broken down by hand:** a raid with nothing on your walls takes a door as its way in, with grenades, a charge,
  a launcher, or, as a last resort, 2 to 4 raiders with melee weapons breaking it down (vanilla raiders do; tribal and
  medieval raids without explosives stood off before).
- Building-destroying launchers (the M79 and the like) now also take your doors as breach points. In a funnel, a
  launcher shoots the door when the defender behind it can't be seen.
- **A raid out to break in brings the means** (playtest: "units would not engage a settlement without any means of
  breaching it"). If none of its raiders carries anything that opens a wall (a charge, a wall-breaking launcher round,
  grenades, a breach or digging tool), one raider in eight (1 to 3) brings the kit of the faction's tech level:
  - tribes: a breach axe;
  - medieval factions: stick bombs, or a breach axe for a bowman;
  - industrial factions: breach charges;
  - spacer factions: more charges and a mining charge.
  This comes on top of the loadout and doesn't depend on "extra equipment". Raids that can walk in, scout or
  sabotage bring none. Setting: "Raids bring breaching gear".

**Raids with other goals** (playtest: "other attacks where it is about sieging or scouting or sabotage etc where
breaching is not needed"):
- **Scouting raids:** a small party walks a covered route to posts 32-48 cells out, in cover and in sight of your
  base, and watches without firing. It leaves once it has seen enough, as soon as it is shot at or loses a man, or
  when its time runs out. Survivors give their faction up to 30 % more intel than other raids (raid memory).
- **Sabotage raids:** they set fires (one site per three raiders), shoot your livestock where your fire doesn't reach,
  and go for a colonist who is alone outside, with no one else within 15 cells and no roof over him. They leave when
  there is nothing left to wreck, when your defence turns out, after a quarter of them are down, or when their time
  runs out.
- Each raid rolls its goal. Assault weighs 1; scouting 0.10 and sabotage 0.15 by default. A raid well under even odds
  harasses about three times as often. A strong raid mostly attacks. A faction that knows little of you scouts twice
  as often, and a raid of more than 12 rarely scouts. Mechanoids, insects and animals always attack.
  Sieges stay as before.
- Neither goal teaches the faction anything about its approach (raid memory).
  Setting: "Scouting and sabotage raids", with the two weights.

**The plan adapts when the situation changes** (playtest: "when the defenders leave the base the attackers do not
adjust their attack accordingly"; "once the situation changes the plan needs to adapt, that is very important"):
- **Defenders who come out get fought where they are.** Colonists more than 15 cells from their base and within 40 of
  the raid turn it into a field fight. Everyone not busy at a wall turns on them:
  - shooters fire from where they stand if they have a shot at a sensible range, else take a covered spot at their
    weapon's distance;
  - melee raiders charge;
  - a sharp command sends every third rifleman round their side.

  Five seconds after the last one is back inside, the raid returns to its plan. Before this, the raid kept waiting at
  its breach and assault spots and only fired back when hit.
- **A defence beaten in the field is stormed at once.** If half the defenders are down after coming out, a walk-in
  raid stops waiting for its timetable and goes.
- **The objective follows the defence.** If the defenders still at home move 12 or more cells from where the plan
  expected them, the objective moves with them, and a walk-in re-plans its axes. A breach keeps its walls (its charges
  are on them).
- A doctrine (field fight): a dull raid sticks to the plan it made. Setting: "Fight defenders who come out".

**Grenade launchers fire over walls** (playtest: "M79 and alike weapons are great as indirect fire weapons to fire into
the base at different places"). CE fires 40 mm rounds on a flat line, so a wall stops them. A grenadier in the base of
fire or a siege camp, with an explosive round loaded and no direct shot, now lobs it high over the wall:
- targets: your colonists, then turrets, then workshops and power, all under open sky, between 8 cells and 95 % of the
  launcher's range;
- shots spread over the base, never land near his own men, and each shot uses one real round from the magazine, with
  the round's own damage and blast;
- scatter is 5-15 % of the distance, depending on his Shooting skill;
- he needs open sky himself, and a thin roof over the target takes the round.

A doctrine (indirect fire). Setting: "Grenade launchers fire over walls".
- **Launchers stay out of the breach** (playtest: "the M79 etc after they breach should stay and engage targets of
  opportunity, also try to engage the walls from a larger distance and if possible from cover"):
  - once his wall is open (walk-in: from H-hour), a raider with an explosive launcher doesn't follow the assault
    through the hole; he stays outside as base of fire and shells whatever shows itself, over the walls too;
  - he shoots at the wall from 55-100 % of his range, from cover from the defence where there is some, out of fire
    (was: at most 30 cells, from the spot nearest the stack point).

  Setting: "Grenade launchers stay out of the breach".
- **Cover wherever there is some** (playtest: "in general units should always seek cover if possible and available"):
  - waiting spots before a walk-in and beside a breach are picked in cover from the defence where the area has any;
  - in a field fight, a raider with a shot but in the open first steps to cover within 5 cells that keeps the shot;
  - stepping back from enemies who come too close prefers covered cells.

**Crew-served weapons** (playtest: "smaller static weapons like HMGs can be carried by raiders and placed closer as part
of a normal raid ... one of the raiders carries the gun and 1-2 the ammo, they stay together and deploy, the team
attacks when everything is ready"):
- Industrial and later assault raids of 8 or more bring one light crew-served gun (16 or more: two). It is a manned
  turret of up to 40 kg at or below the faction's tech level, from whatever the modlist has (HSK: M240B, AGS-17, ...).
- The gunner carries it packed; one or two bearers carry three magazines of its ammo.
- The team walks a covered route together, the gunner waiting for his bearers, to a post at 60-75 % of the gun's
  range: out of your fire, in cover, with a line of fire on your base. They set it up in 4 seconds and the gunner mans
  it. Only he stays at the gun: the bearers spread out into cover 4 to 8 cells beside and behind it, cover it, reload
  it from their packs, and take it over if the gunner falls. The gun is set up facing your base with
  the gunner behind it, and an idle gun turns toward the nearest colonist in range, or toward your base.
- The assault waits until every team still alive is firing, for at most 50 seconds.
- If the crew falls or the raid withdraws, the gun stays where it stands, unowned: loot.
- Part of the base of fire (a doctrine). Setting: "Crew-served weapons".

**Clear the breach by fire, then go in** (playtest: "better to be outside, cover the breach from cover and shoot inside
until there is no more enemy to shoot at, then slowly approach"): once a wall is open, nobody goes through while a
defender who isn't pinned (hunkering) still covers the gap. Gunners at the corner posts and on overwatch fire in;
whoever else waits within 8 cells fires too if he has a shot, or steps out of the line of fire through the gap. Shield
bearers still lead. At most 30 s, then in. From middling competence on.

**Snipers and machine guns hold overwatch before the breach** (playtest): from middling competence on (riflemen still
need a sharper command), they take covered positions facing where the breach will open, snipers at about 75 % of their
range (up to 49 cells), and cover the entrance while the wall comes down.

**Breach entry in waves** (playtest: "deadly funnel, all try to enter through the same breach at the same time"): every
raid, however dull, goes through an opening at most 3 at a time, about 4 s apart; the rest hold 6 cells back instead of
crowding the gap. While they wait for the wall to come down they stand 2+ cells apart (one blast took six
stackers waiting shoulder to shoulder). Inside they fan out left and right as before.

**Corner fire at the breach** (playtest): when a wall opens, gunners about to go in take the posts beside the hole on
their side of the wall (two per side) and lean out past the wall end to fire through the gap - RimWorld's and CE's
lean-out shot lines, with the wall covering them - before they go in (at most 10 s, at once when nobody inside is in
sight). Nobody stands in the hole itself. Setting: "Corner fire at the breach".

**Siege camp** (playtest):
- Raiders who besiege you and aren't building or manning a gun take posts in cover around the camp, facing your
  defenders, by role:
  - riflemen in the front arc;
  - machine guns on the flanks;
  - marksmen behind the camp centre;
  - melee close in front of the builders;
  - with 6 or more guards, one watching the rear.
  The posts turn with the threat when you try to flank. A guard moves to another covered spot when his cover stops
  working or he gets hit there, and fires from his post instead of chasing. Every siege does this; from middling
  competence the guards spread by role, and sharp ones turn with flanking defenders and keep a rear guard.
- Once the camp's own sandbags and barricades are finished, guards take post behind those on their side first.
- Our raids that turn to a siege set up camp on their side of your base, where it sees your nearest wall from within 80
  cells if there is such a spot, else about 75 cells out (vanilla: anywhere within 60 cells of where the raid stood,
  often far out of range and behind hills).
- **Direct-fire siege guns** (cannons, ordnance rifles, flak) stay inside the camp, at most 14 cells from its
  centre, so the camp's guards cover them: at a spot in range with a line of fire on your nearest wall, out of your
  fire if possible, else at the camp's front edge. Vanilla put them anywhere in the camp. Mortars and other guns that fire overhead are unchanged.
  With no one of yours in sight, explosive guns fire at your structures: turrets first, then the walls and doors
  nearest your defenders (vanilla and CE turrets only ever picked pawns unless they fire overhead). Machine guns, whose
  rounds do nothing to walls, take your animals in sight instead.
- **Forward observer:** CE scatters mortar fire beyond 75 cells or without sight of the target, unless a spotter has
  marked it. Vanilla sieges never spot, so their mortars fired almost at random. One guard with the best eye (a
  marksman first) now goes to a covered spot that sees what the siege's mortars are firing at, and marks it the way CE
  spotting does. The mark lasts 30 s and is renewed.
- Setting: "Siege camp: guards in cover, guns at range".

**Fixes**
- A raid that was winning inside (6 raiders against the last defender) let vanilla's "satisfied" exit go after 3,000
  ticks of assault and turned to kidnapping and looting under fire. The time and loss limits now release a raid only
  while it is not winning (it outnumbers the standing defenders 3:1 and took one down within the stall time); hard cap 3x.
- No One Left Behind compatibility: on the colony map, while a defender stands, raiders don't go for a downed comrade
  lying in the defenders' open fire (same rule as our medics and withdrawal carriers).
- A raid holding back from defenders holed up behind a funnel, with nothing to plunder, waited out the whole raid. It now
  leaves after the plunder idle time (planned withdrawal, or the vanilla way for dull raids).
- The siege mortar spotter never marked a target. It didn't see CE mortars at all, and once fixed it walked out to its
  post and back each time the mortar dropped its target between reloads. Now it keeps watching the last target for 15 s.

## 1.3.0 (2026-10-25)

- **Relation Based Trading 2.1.0** joins the release (`thelonetec.relationbasedtrading`, original mod by TheLoneTec,
  updated by Sonsalt; not covered by this repo's MIT license). Trader stock follows goodwill gradually instead of in
  fixed tech tiers: the most valuable item a faction sells grows from 150 silver at -75 to no limit at Ally (75);
  weapons, ammo and explosives only from 25 and counted 4x their price; quality from Normal up to Legendary; tech
  relative to the faction's own level. Settlement stock is trimmed when goodwill drops and regenerated after a rise of
  15. All limits are sliders in the mod settings. Also includes the 2.0.1 fixes (about 150 never-filtered items, mostly
  CE ammo; reliable stock updates after trades and with Dynamic Trade Interface). Tested in game with the full HSK list
  (214 mods): 88 automated checks pass and the mod logs no errors. Details in `Relation Based Trading/CHANGELOG.md`.
- README: Relation Based Trading section and credits.

## 1.2.0 (2026-10-25)

- **HSK Weapon Blueprints 0.1.0** joins the release (`sonsalt.hsk.weaponblueprints`, test release): Industrial and
  higher weapons have to be learned from a blueprint or by reverse engineering before they can be crafted. Blueprints
  come from traders, quest rewards, item stashes and ancient complexes; pawns study them while reading or as Research
  work. Workbench menus only list known weapons. New colony only. Tested in game with the full HSK list (219 mods): 97
  automated checks pass (gating, blueprint generation and distribution, reading and study work, reverse engineering
  odds, trading incl. Dynamic Trade Interface, save/load) and the mod logs no errors. Preview image and mod icon in the
  style of the other five. Source in `HSK Weapon Blueprints/Source/`.
- README: game version per mod (all six are for RimWorld 1.7 only).

## 1.1.0 (2026-10-25)

- **HSK Tactical AI 0.1.0** joins the release (test release, see below).
- New preview images and mod icons for all five mods (`About/Preview.png`, `About/ModIcon.png`): in-game scenes with a
  shared title frame. RimWorld shows the icon in the loading screen's mod summary and in Options > Mod settings.

## HSK Tactical AI 0.1.0 (2026-10-24)

First test release of **HSK Tactical AI** (`sonsalt.hsk.tacticalai`), a custom raid AI for HSK on RimWorld 1.7 with
Combat Extended. For the dev team to test; expect tuning changes. Every behaviour below has its own switch in the mod
settings. Tested on RimWorld 1.7 with HSK `1.7-super-unstable` and the full HSK modlist (208 mods).

**Planning the raid**
- **Threat map:** raiders know where your colonists and turrets can shoot (time-sliced line-of-sight map; overlay via
  the crosshair button in the bottom-right bar).
- **Assault planner:** gather out of sight, approach on two or more axes along covered routes, then attack together
  from several sides (start times staggered by route length, so the groups arrive at once).
- **Strength check:** an assault on a prepared position needs about 3:1 (force score: weapon, skill, armour, health).
  Weaker raids besiege, snipe from out of range, plunder or leave.
- **Killboxes and funnels:** raiders avoid killbox entrances and make their own way in. Inside the fight they don't
  follow defenders into narrow funnels; they throw a grenade in, hold the mouth and shoot whoever comes out.
- **Open ground:**
  - take a covered route if there is one;
  - otherwise rush only with 3:1 odds or behind a smoke screen, or send a flank group;
  - otherwise stay out of range and let marksmen and machine guns work;
  - after that, try another angle or a breach, then bound in by fire and movement at fair odds (1.5:1);
  - withdraw only when the attack is bound to fail.

**Breaching**
- **Raiders keep their loadout.** They breach with what they spawned with: launchers, thrown explosives, breach tools
  and grenades, fire against wooden walls (anyone can set it alight), tunnels through rock with their own tools. A raid
  with nothing that can open the wall besieges (if its faction can) or stands off, snipes and plunders.
- Several breach points at once, opened together; the method per wall is the best the raid's tech level and loadout
  allow.
- Optional **extra equipment** (setting, off by default) gives raiders breach gear for their tech level:
  - breach charges (industrial and up);
  - stick bombs (medieval);
  - breach axes (tribal, the bow goes in the pack);
  - mining charges and tools for tunnels;
  - some frag and smoke grenades;
  - masks and tox grenades for gas raids.
- Breach teams stack beside the wall under overwatch; shield bearers escort the breacher; carried smoke grenades go
  onto the opening.
- **Gas and burn-out:** defenders in a small enclosed room or bunker get the raid's tox grenades and fire through the
  breach (industrial and up).
- Grenades are lobbed over a single wall when there is no line of sight. Gas and fire grenades never count as
  breaching tools.
- A breach whose team has nothing left to open it with (a one-shot launcher spent) is given up at once, and the team
  joins an open breach. Raiders left without a weapon pick up a gun lying out of the defenders' fire, or plunder or
  hold back; they never assault with their fists.

**Combined arms**
- **Roles:** base of fire (machine guns, snipers) pins the defenders while the assault moves. The rest have roles too:
  - marksmen take exposed and dangerous targets;
  - overwatch covers the breach;
  - combat medics smoke, carry the wounded out of fire and tend them;
  - a raiding party plunders on large raids.
- **Shield wall:** ballistic-shield bearers lead, crouch between bounds and throw the grenades they carry once in range; gunners
  follow in their shadow. Shields last in a withdrawal.
- **Shield through the breach:** at an open breach the shield bearer throws first, if he carries a grenade. He throws
  from a covered spot beside the gap, as close to the defenders as he can reach. He waits beside the gap for the blast,
  then goes straight in and never stops in the gap. Nobody going in through that breach overtakes him (up to 900
  ticks). His followers stay in his shadow, and the rest fan out wide left and right of the opening.
- **Fire discipline** (CE aim and fire modes) by range and situation:
  - aimed single shots at long range;
  - aimed short bursts at medium range;
  - snap bursts or full auto up close;
  - machine guns fire short bursts at range;
  - one step down when ammo runs low.
- **Targets:** exposed (flanked) defenders first, then wounded, dangerous ones (MG, launcher, sniper) and focus fire.
  Flank positions and flank axes are preferred.
- **Combat micro:**
  - retreat to cover when outgunned;
  - evade incoming grenades, gas and fire;
  - duck into cover when aimed at;
  - melee raiders intercept defenders who attack their gunners, and come in from the side;
  - gunners step back from melee.

**Suppression and posture**
- **Realistic suppression** replaces CE's formula:
  - normal armour no longer stops the flinch (only armour that stops the round outright reduces it);
  - machine guns and automatic fire suppress more than single rifle shots, arrows less;
  - hand grenades suppress a lot, grenade and rocket launchers more;
  - suppression lingers a little longer than in CE (held 60 ticks after a near miss, then fading at half CE's rate).
- **Prone:** a fully suppressed raider without cover goes prone: a much smaller CE target, crawling at a quarter
  speed. He is drawn lying down (vanilla's crawl pose, weapon shown) with a "prone" label above him, so prone and
  downed raiders are easy to tell apart. Up again once the suppression ends.

**Raid goals**
- **Plunder instead of chasing:** raiders kill livestock, loot valuables, burn crops and wooden buildings and wreck
  valuable buildings (turrets, research benches, comms consoles, generators first), out of your fire, while the breach
  is prepared. They regroup for the go signal. They don't chase defenders into their corridors.
- **Finish the assault before leaving satisfied:** vanilla's "satisfied, leaving" waits while a planned assault is
  making progress. It fires once the assault stalls, has run a while, or the defenders are down. Damage from the
  raiders' own breach never counts.
- **Planned withdrawal** when the attack fails or ammunition runs low: shields form the rearguard and the wounded are
  carried out - except those lying in the defenders' fire without smoke over them (medics too: no rescues into the
  gun line).

**Raid memory**
- **Factions remember earlier raids**, saved with the game:
  - which approach failed (walk-in, breach, siege);
  - what downed their raiders (turrets, traps, explosions, melee, long-range fire);
  - how well armoured the colonists were.
  The memory fades by half in about two seasons.
- **Intel only through survivors:** a faction learns only from raiders who got off the map alive. Wipe a raid out
  and it learns nothing.
- **The next raid adapts its approach:** a walk-in that failed becomes a breach; a failed breach becomes a siege
  (factions that can).
- **The next raid adapts its loadout, within budget:** up to 30 % of its gunners swap gear against what hurt them.
  They take only weapons a pawn kind of the same raid carries, within their own weapon budget. Ammo is swapped, not
  added; medics and breachers keep their kit.

  | What hurt them | What they bring |
  |---|---|
  | turrets or a killbox | launchers; their carried frags become EMP grenades |
  | snipers | long rifles; half their grenades become smoke |
  | melee fighters | shotguns and SMGs |
  | heavily armoured colonists | armour-piercing ammo |
- **A message** tells the player when an adapted raid arrives ("... remember your turrets: this raid brings
  launchers"). Memory and loadout adaptation each have a switch.

**Performance:** the AI's own code costs about 0.02-0.09 ms per game tick (under 1 % of the tick time), measured up to a
10,000-point raid with 44 raiders. Under heavy load, its periodic checks run half as often.

**Compatibility:** works with Combat Extended, Smarter Raid AI (`pogo.ai`) and Enemy Self Preservation.
- **RunAndGun:** works too. Its "enable for AI" option makes every move order shoot on the move. For raiders under a
  plan, only riflemen and assault troops in the assault may do that; medics, breachers, shields, support fire,
  carriers and withdrawing raiders walk. Their shots on the move count for the AI.
- **Not with CAI 5000 or Raid Tactics:** they change the same raid AI, and this mod has its own raid memory.

Saves: raid plans are not saved (after a reload a raid plans anew); raid memory is.

**For testers:** `Source/HSKTacticalAI/AILab.cs` is a scripted test bench (scenarios, metrics). It is off unless a Lab
config file is present in the RimWorld config folder.

## 1.0.0 (2026-10-22)

First public release for RimWorld 1.7 / HSK `1.7-super-unstable`: Fortifications Rebalance, Outposts, Zero Research Start (with research gates), 1.7 Compatibility Fixes.
