# Changelog

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

**Performance:** the AI's own code costs about 0.02-0.09 ms per game tick (under 1 % of the tick time), measured up to a
10,000-point raid with 44 raiders. Under heavy load, its periodic checks run half as often.

**Compatibility:** works with Combat Extended, Smarter Raid AI (`pogo.ai`) and Enemy Self Preservation. Not with
CAI 5000 (both change the same AI systems). Saves: raid plans are not saved; after a reload a raid plans anew.

**For testers:** `Source/HSKTacticalAI/AILab.cs` is a scripted test bench (scenarios, metrics). It is off unless a Lab
config file is present in the RimWorld config folder.

## 1.0.0 (2026-10-22)

First public release for RimWorld 1.7 / HSK `1.7-super-unstable`: Fortifications Rebalance, Outposts, Zero Research Start (with research gates), 1.7 Compatibility Fixes.
