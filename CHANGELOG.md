# Changelog

## HSK Tactical AI 0.2.0 (unreleased)

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
