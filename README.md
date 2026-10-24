# Sonsalt's HSK mods

Five mods for [Hardcore SK](https://github.com/HardcoreSK/Hardcore-SK) on RimWorld 1.7 (`1.7-super-unstable` branch).
Each mod is one folder; copy the folders you want into your `RimWorld\Mods` folder.

| Mod | packageId | Needs |
|---|---|---|
| [HSK Tactical AI](#hsk-tactical-ai) (0.1, test release) | `sonsalt.hsk.tacticalai` | HSK, Harmony (Combat Extended recommended) |
| [HSK Fortifications Rebalance](#hsk-fortifications-rebalance) | `sonsalt.hsk.fortifications` | HSK + Aoba's Fortifications - Medieval and/or - Industrial |
| [HSK Outposts](#hsk-outposts) | `sonsalt.hsk.outposts` | HSK, Vanilla Expanded Framework, Vanilla Outposts Expanded (VOE: Additional Outposts optional) |
| [HSK Zero Research Start](#hsk-zero-research-start) | `sonsalt.hsk.zeroresearch` | HSK |
| [HSK 1.7 Compatibility Fixes](#hsk-17-compatibility-fixes) | `sonsalt.hsk.compatfixes` | HSK (each fix only acts if its target mod is active) |

**Status:** tested on RimWorld 1.7.4985 (Steam unstable) with HSK `1.7-super-unstable` and ~200 mods: every mod loads
with zero errors. Gameplay testing is still light. HSK 1.7 itself is a dev sandbox, so expect changes.

**Load order:** after HSK and after the mods each one patches (the `loadAfter` lists in About.xml handle this if you use
the game's auto-sort). Put the compatibility fixes near the end.

---

## HSK Tactical AI

A custom raid AI for HSK (0.1 is a test release for the dev team; see the [changelog](CHANGELOG.md) for the full list).
Raiders plan their attack instead of walking into your guns:
- they read where your guns can shoot (threat map) and avoid killboxes and funnels;
- they breach several walls at once with what they carry (launchers, explosives, tools, fire, tunnels);
- they cross open ground only in cover, behind smoke, with 3:1 odds or by fire and movement;
- they fight as combined arms: base of fire, overwatch, shield bearers, medics, flanking, fire discipline by range;
- under realistic suppression they go prone (drawn lying down, labelled "prone");
- they plunder instead of chasing you through your corridors, and withdraw when the attack fails.

Every behaviour has a switch in the mod settings; the threat map overlay is the crosshair button in the bottom-right
bar. Works with Combat Extended, Smarter Raid AI and Enemy Self Preservation; not with CAI 5000. Load after HSK and
Combat Extended.

## HSK Fortifications Rebalance

Rebuilds Aoba's *Fortifications - Medieval* and *Fortifications - Industrial* to fit HSK's materials, research and
Combat Extended ammunition. The `Industrial` and `Medieval` parts each load only if that mod is active. AOBA.Framework is
not needed (its visual-only comps are stripped).

Design rules:
1. **No duplicates.** Anything HSK or Vile's already has is removed, and recipes point at the HSK item.
2. **HSK economy.** Turrets are built from a crate made at the heavy arms lathe (weapon parts, heavy/cannon components,
   bars, rubber) plus rugged metal, like HSK's own turrets. Cover uses HSK concrete, reinforced concrete and sand.
3. **HSK research.** The mods' own research tree is removed; every piece sits on an existing HSK project.
4. **HSK scale.** Turret HP 160-520 instead of 1,000-2,000. Cover HP close to HSK's barricades and embrasures.
5. **HSK/CE ammo only.** Guns use calibers HSK has (7.62x51, 20x102, 90 mm).

| Kept (rebalanced) | Research |
|---|---|
| Sandbag wall, tetrapod, tank trap, concrete barriers | Fortification III / Concrete I (+ Fortification IV) |
| Quad-mounted MG (7.62x51, suppression) | Heavy turrets II |
| Quad 20 mm flak emplacement (shielded) | Heavy turrets III |
| 90 mm anti-tank gun (was 76 mm, shielded) | Artillery II |
| Sandbag MG nest, concrete MG bunker (automated, 900 W) | Autoturrets I (+ Concrete I) |
| Castle wall, castle embrasure (cover 0.7), reinforced ditch, rock box | Fortification II / III, Siege engines |

Removed because HSK already has them: Industrial's concrete economy and concrete mixer, lathe, chemistry table,
generator, radio, antenna, hydrant, ENIAC, concrete floors, its own research tree, military sandbags, autocannons and AA
guns (use HSK Oerlikon/Vulcan), 37/128 mm guns, recoilless rifle, Nebelwerfer, howitzers and 60 mm mortar, artillery
spotter, leather/steel bunkers, Medieval's swivel gun (use Vile's). It also restores the HSK research links and floor
costs that Fortifications - Industrial overrides.

## HSK Outposts

Balances [Vanilla Outposts Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=2688941031) and, if active,
[VOE: Additional Outposts](https://steamcommunity.com/sharedfiles/filedetails/?id=2873841790) for HSK. Based on the HSK
patch in TheLoneTec's *Tec's Assorted Patches* (1.3-1.5), ported to 1.7 and extended.

- **Building outposts** costs *outpost construction materials* (crafting spot / fabrication bench: 100 planks, 100 stone
  blocks, 200 metal, 10 fasteners → 50 materials), plus kits for the artillery and science outposts, and needs minimum
  construction/cooking/other skills.
- **Food:** HSK and Vile's fine meals (HSK removes the Ideology meals VOE uses).
- **Mining** yields HSK ores by total mining skill (iron, coal, salt from 10 up to wolframite 40 and compacted machinery
  45) plus stone blocks, never finished steel or plasteel. **Logging** yields Vile's wood species.
- **Removed:** the production outpost (free components). **Reduced:** drilling (20 chemfuel per skill), trading
  (15 silver per skill), scavenging (every 40-80 days by team size).
- **Artillery outpost** shells: 120 Bomb, radius 2.5 (close to HSK's 81 mm HE instead of the weak vanilla mortar shell).
- **Additional Outposts:** build costs added; silver, drug, medicine and meal output roughly halved; mercenary pay and
  bank interest halved; their 12 outpost maps rebuilt with HSK/Vile's materials (cabin walls, cloth beds, plank furniture).
- `Settings/Mod_2023507013_OutpostsMod.xml` (optional): HSK values for the VEF outposts slider settings: pack-animal
  delivery instead of teleport, smaller border-post fines, lower church/prison/embassy/bank income. Copy it to
  `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\` if you have no outposts settings yet.

Outpost pawns with Combat Extended can be a weak spot in outpost battles; set them to auto-resolve in the VOE settings
if fights misbehave.

## HSK Zero Research Start

Start an HSK colony with **no research finished**.

- Scenarios **Lost Tribe (no research)** and **Crashlanded (no research)**.
- Player factions **new tribe (no research)** and **new arrivals (no research)** for your own scenarios
  (scenario editor → Player faction). They inherit from HSK's originals, so HSK updates carry over.
- First steps: build a primitive research table, then research *Basic knowledge of craft* and *Food Prep*.

**Research gates.** These buildings had no research at all and were buildable on day one. Each now needs the research
of its closest HSK peer (also in normal starts, where you usually have it already):

| Building | Now needs | Why |
|---|---|---|
| Spike trap (HSK deadfall) | Fortification II | medieval parts, mechanism and wire; same tier as sandbags |
| Bullets autoloader | Heavy turrets I | first turrets that use its calibers (M240B, M2 Browning) |
| Shells autoloader | Artillery I | first turrets that use its shells (81 mm mortar, 90 mm flak) |
| Illumination turret (Watcher 2.5) | Turrets I + Pistols II | first turret research; Pistols II unlocks flare ammo |
| Hand assembling workbench | Basic knowledge of craft | unlocks its core recipes (components, wires) |
| Active gas cleaning (Watcher 2.5) | Temperature control III + Drugs IV | same research as the vent; its fuel (activated carbon) needs Drugs IV |
| Rimatomics PPC, PPHC | Rimatomics | safety net: Rimatomics' own research (Energy Weapons, Hypercapacitors) still unlocks them |
| Rooftop rotary turbine vent | Temperature control III | it's a vent |
| Salt mine | Mining I | mining; other salt sources need research too |
| Silage pile | Animal husbandry II | silage at the butcher table/kitchen needs it |
| Leatherworker table | Apparel production I | nearly all its recipes need it |

Left open on purpose: the **plant processing bench**. Potatoes need no research, and with SeedsPlease it's the only
source of potato seeds.

## HSK 1.7 Compatibility Fixes

Small fixes for addons that throw errors on HSK 1.7. Each does nothing unless its mod is active, and each can go once
the upstream mod is fixed.

| Fix | Problem |
|---|---|
| ModularWeapons 2 + CE patch | hunting rifle's .338 Lapua magazine uses an ammo set HSK's CE doesn't have → 7.62x51 mm NATO |
| HSK Watcher Content 2.5 | the two claymore mines use classes missing from the DLL (one also needs an undefined item) → removed |
| Elldar's Patches + Vile's Biomes | smoother terraforming adds 5 terraform options Vile's Biomes removes (570 errors) → dropped with Vile's Biomes |

---

## For modders: tools

Some XML is generated. Edit the generator, not the XML:

| Script | Generates |
|---|---|
| `tools/Build-FortifPatch.ps1` | everything in `HSK Fortifications Rebalance` (uses `tools/data/fortifications_deflist.tsv`) |
| `tools/Build-AOLayouts.ps1` | `HSK Outposts/AdditionalOutposts/Patches/AO_Layouts_HSK.xml` (reads the installed Additional Outposts; `-AdditionalOutposts <folder>`) |
| `tools/Build-ResearchGates.ps1` | `HSK Zero Research Start/Patches/HSK_ResearchGates.xml` (one line per building) |
| `tools/Sync-ToGame.ps1` | mirrors the five mod folders into your RimWorld `Mods` folder (`-ModsFolder <path>`) |

## Credits

- [Hardcore SK team](https://github.com/HardcoreSK/Hardcore-SK) for HSK.
- **TheLoneTec**: the original HSK outposts design in *Tec's Assorted Patches*.
- **Aoba**: *Fortifications - Medieval / Industrial*.
- **Oskar Potocki and the Vanilla Expanded team**: Vanilla Outposts Expanded and the framework.
- **MrHydralisk**: VOE: Additional Outposts.
- **Watcher**, **Elldar**, **kaitorisenkou**, **Vile**: the mods the compatibility fixes and gates touch.

No textures or assets from other mods are included; the outpost items use HSK's own weapon-crate texture.

## License

[MIT](LICENSE). This covers the files in this repo. The mods these patches change keep their own licenses and are
not included.
