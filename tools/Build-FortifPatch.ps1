# Generates the "HSK Fortifications Rebalance" mod (packageId sonsalt.hsk.fortifications) in this repo.
# Re-run after changing numbers; it overwrites the mod folder's generated files. Then run Sync-ToGame.ps1 and test.
# tools\data\fortifications_deflist.tsv lists the defs of Fortifications - Industrial (type <tab> name columns 2 and 3).
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'HSK Fortifications Rebalance'
$utf8 = New-Object Text.UTF8Encoding $false
function Out-File2($rel, $text) { $p = Join-Path $root $rel; New-Item -ItemType Directory -Force (Split-Path $p) | Out-Null; [IO.File]::WriteAllText($p, $text, $utf8) }

# ---------- patch helpers ----------
$script:ops = New-Object Text.StringBuilder
function Op([string]$x) { [void]$script:ops.AppendLine($x) }
function Comment([string]$c) { Op "`n  <!-- $c -->" }
# Set a child node: replace if present, else add under its parent.
function SetNode([string]$def, [string]$path, [string]$valueXml, [string]$type = 'ThingDef') {
  $full = "Defs/$type[defName=`"$def`"]/$path"
  $parent = if ($path.Contains('/')) { "Defs/$type[defName=`"$def`"]/" + $path.Substring(0, $path.LastIndexOf('/')) } else { "Defs/$type[defName=`"$def`"]" }
  Op @"
  <Operation Class="PatchOperationConditional">
    <xpath>$full</xpath>
    <match Class="PatchOperationReplace"><xpath>$full</xpath><value>$valueXml</value></match>
    <nomatch Class="PatchOperationAdd"><xpath>$parent</xpath><value>$valueXml</value></nomatch>
  </Operation>
"@
}
# Same, addressing an abstract def by Name.
function SetNamed([string]$name, [string]$path, [string]$valueXml) {
  $full = "Defs/ThingDef[@Name=`"$name`"]/$path"; $parent = "Defs/ThingDef[@Name=`"$name`"]"
  Op @"
  <Operation Class="PatchOperationConditional">
    <xpath>$full</xpath>
    <match Class="PatchOperationReplace"><xpath>$full</xpath><value>$valueXml</value></match>
    <nomatch Class="PatchOperationAdd"><xpath>$parent</xpath><value>$valueXml</value></nomatch>
  </Operation>
"@
}
# Remove if present (never errors).
function RemoveIf([string]$xpath) {
  Op @"
  <Operation Class="PatchOperationConditional">
    <xpath>$xpath</xpath>
    <match Class="PatchOperationRemove"><xpath>$xpath</xpath></match>
  </Operation>
"@
}
function Stat([string]$def, [hashtable]$stats) { foreach ($k in $stats.Keys) { SetNode $def "statBases/$k" "<$k>$($stats[$k])</$k>" } }
function Flush([string]$rel) { Out-File2 $rel ("<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<Patch>`n" + $script:ops.ToString() + "</Patch>`n"); $script:ops = New-Object Text.StringBuilder }
function Or([string[]]$names, [string]$attr = 'defName') { ($names | % { "$attr=`"$_`"" }) -join ' or ' }

# =====================================================================
# About + LoadFolders
# =====================================================================
Out-File2 'About\About.xml' @'
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>HSK Fortifications Rebalance</name>
  <author>Sonsalt</author>
  <packageId>sonsalt.hsk.fortifications</packageId>
  <modVersion>1.0.0</modVersion>
  <url>https://github.com/MikeMyMan/Sonsalt-HSK-Mods</url>
  <supportedVersions>
    <li>1.7</li>
  </supportedVersions>
  <modDependencies>
    <li>
      <packageId>skyarkhangel.HSK</packageId>
      <displayName>Hardcore SK</displayName>
      <downloadUrl>https://github.com/HardcoreSK/Hardcore-SK</downloadUrl>
    </li>
  </modDependencies>
  <loadAfter>
    <li>skyarkhangel.HSK</li>
    <li>CETeam.CombatExtended</li>
    <li>ViTech.MedievalOverhaul</li>
    <li>Aoba.Fortress.Neolithic</li>
    <li>Aoba.Fortress.Medieval</li>
    <li>Aoba.Fortress.Industrial</li>
  </loadAfter>
  <description>Rebalances Aoba's "Fortifications - Medieval" and "Fortifications - Industrial" for Hardcore SK + Combat Extended.

- Removes everything that duplicates HSK (concrete economy, mixer, lathe, chemistry table, generator, radio, antenna, hydrant, ENIAC, floors, own research tree, duplicate turrets, Medieval's swivel gun).
- Remaining structures and turrets use HSK materials, HSK research, HSK crate-built turrets (heavy arms lathe) and HSK/CE ammunition.
- Restores the HSK research tree and floor costs that Fortifications - Industrial overrides.
- Works without AOBA.Framework (strips its purely visual comps).

Load after both Fortifications mods. Each part only loads if its mod is active.</description>
</ModMetaData>
'@
Out-File2 'LoadFolders.xml' @'
<?xml version="1.0" encoding="utf-8"?>
<loadFolders>
  <v1.6>
    <li>/</li>
    <li IfModActive="Aoba.Fortress.Industrial">Industrial</li>
    <li IfModActive="Aoba.Fortress.Medieval">Medieval</li>
  </v1.6>
  <v1.7>
    <li>/</li>
    <li IfModActive="Aoba.Fortress.Industrial">Industrial</li>
    <li IfModActive="Aoba.Fortress.Medieval">Medieval</li>
  </v1.7>
</loadFolders>
'@

# =====================================================================
# INDUSTRIAL: placeholder defs (let Industrial's 37 mm patches succeed; removed again below)
# =====================================================================
Out-File2 'Industrial\Defs\FTH_Placeholders.xml' @'
<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <!-- Fortifications - Industrial patches AmmoSet_37x223mmR / Ammo_37x223mmR_AP / _HE, which HSK's CE fork
       does not have. These empty stand-ins only exist during patching so those operations succeed;
       FTH_01_Remove.xml deletes them before defs are loaded. -->
  <CombatExtended.AmmoSetDef>
    <defName>AmmoSet_37x223mmR</defName>
    <ammoTypes />
  </CombatExtended.AmmoSetDef>
  <ThingDef>
    <defName>Ammo_37x223mmR_AP</defName>
    <graphicData />
  </ThingDef>
  <ThingDef>
    <defName>Ammo_37x223mmR_HE</defName>
    <graphicData />
  </ThingDef>
</Defs>
'@

# =====================================================================
# INDUSTRIAL: crates + crate recipes (HSK turret model: crate from the heavy arms lathe + metal stuff)
# =====================================================================
$crates = @(
  @{ def='FTH_QuadMG_Crate';  label='quad machine gun crate';        turret='quad-mounted machine gun'; mv=3300; mass=40;  work=12500; skill=8;  res='Heavy_turrets_C2'; ing=@(@('Weapon_Parts',16),@('Heavy_Component',1)); metal=50; rubber=0;  elec=0 },
  @{ def='FTH_QuadAA_Crate';  label='quad 20mm flak crate';          turret='quad 20mm flak emplacement'; mv=5600; mass=320; work=18500; skill=14; res='Heavy_turrets_C3'; ing=@(@('Weapon_Parts',24),@('Cannon_Component',2)); metal=70; rubber=30; elec=0 },
  @{ def='FTH_ATGun_Crate';   label='90mm anti-tank gun crate';      turret='90mm anti-tank gun'; mv=4600; mass=600; work=16500; skill=13; res='Artillery_C2'; ing=@(@('Weapon_Parts',16),@('Cannon_Component',2)); metal=60; rubber=25; elec=0 },
  @{ def='FTH_MGNest_Crate';  label='MG nest crate';                 turret='automated MG nest'; mv=3600; mass=35;  work=12500; skill=11; res='GunTurrets'; ing=@(@('Weapon_Parts',10),@('Heavy_Component',1),@('Electronics',5)); metal=30; rubber=0; elec=0 }
)
$cx = New-Object Text.StringBuilder
[void]$cx.AppendLine('<?xml version="1.0" encoding="utf-8"?>'); [void]$cx.AppendLine('<Defs>')
foreach ($c in $crates) {
  $ingXml = ($c.ing | % { "      <li><filter><thingDefs><li>$($_[0])</li></thingDefs></filter><count>$($_[1])</count></li>" }) -join "`n"
  $fixed  = ($c.ing | % { "        <li>$($_[0])</li>" }) -join "`n"
  $rub = if ($c.rubber -gt 0) { "      <li><filter><thingDefs><li>Rubber</li></thingDefs></filter><count>$($c.rubber)</count></li>" } else { '' }
  $rubFixed = if ($c.rubber -gt 0) { '        <li>Rubber</li>' } else { '' }
  [void]$cx.AppendLine(@"
  <ThingDef ParentName="ItemCrateBase">
    <defName>$($c.def)</defName>
    <label>$($c.label)</label>
    <description>Use this to build a $($c.turret) (Fortifications - Industrial, HSK conversion).</description>
    <graphicData>
      <texPath>Things/Item/Resource/WeaponCrate</texPath>
      <graphicClass>Graphic_Single</graphicClass>
    </graphicData>
    <soundInteract>Standard_Drop</soundInteract>
    <soundDrop>Standard_Drop</soundDrop>
    <tickerType>Normal</tickerType>
    <stackLimit>1</stackLimit>
    <techLevel>Industrial</techLevel>
    <statBases>
      <MarketValue>$($c.mv)</MarketValue>
      <MaxHitPoints>100</MaxHitPoints>
      <Flammability>1</Flammability>
      <DeteriorationRate>0</DeteriorationRate>
      <Mass>$($c.mass)</Mass>
      <Bulk>$([int]($c.mass * 1.2 + 10))</Bulk>
    </statBases>
    <thingCategories><li>WeaponCrate</li></thingCategories>
    <tradeTags><li>SimpleCrate</li></tradeTags>
  </ThingDef>
  <RecipeDef ParentName="cratecraftbase">
    <defName>Build$($c.def)</defName>
    <label>build $($c.label)</label>
    <description>Use this to build a $($c.turret).</description>
    <jobString>Building $($c.label).</jobString>
    <workAmount>$($c.work)</workAmount>
    <skillRequirements><Crafting>$($c.skill)</Crafting></skillRequirements>
    <ingredients>
$ingXml
      <li><filter><categories><li>Metallic</li></categories></filter><count>$($c.metal)</count></li>
$rub
    </ingredients>
    <products><$($c.def)>1</$($c.def)></products>
    <fixedIngredientFilter>
      <thingDefs>
$fixed
$rubFixed
      </thingDefs>
      <categories><li>SLDBar</li><li>USLDBar</li></categories>
    </fixedIngredientFilter>
    <researchPrerequisite>$($c.res)</researchPrerequisite>
  </RecipeDef>
"@)
}
[void]$cx.AppendLine('</Defs>'); Out-File2 'Industrial\Defs\FTH_Crates.xml' $cx.ToString()

Out-File2 'Industrial\Defs\FTH_SubCategories.xml' @'
<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <ArchitectSense.DesignationSubCategoryDef>
    <defName>SubCategory_FortressIndustrial</defName>
    <label>industrial fortifications</label>
    <description>Sandbag walls, concrete barriers and anti-vehicle obstacles.</description>
    <defNames>
      <li>FT_WallSandbag</li>
      <li>FT_Barrier</li>
      <li>FT_BarrierDiagonal</li>
      <li>FT_TankTrap</li>
      <li>FT_Tetrapods</li>
    </defNames>
    <designationCategory>Security</designationCategory>
    <debug>false</debug>
    <order>12</order>
    <types>
      <li>Fortifications</li>
    </types>
  </ArchitectSense.DesignationSubCategoryDef>
</Defs>
'@

# =====================================================================
# INDUSTRIAL 01: removals (duplicates of HSK, framework-dependent, or without HSK ammo)
# =====================================================================
$keepThings = 'FT_WallSandbag','FT_TankTrap','FT_Tetrapods','FT_Barrier','FT_BarrierDiagonal','FT_Maxim4M','FT_Gun_QuadMG',
              'FT_TurretQuadAA','FT_Gun_TurretQuadAA','FT_TurretAT_Plus','FT_Gun_TurretAT_Plus','FT_Turret_BunkerF','FT_Turret_BunkerC','FT_Gun_MG','Mote_CasingEject'
$rows = Get-Content (Join-Path $PSScriptRoot 'data\fortifications_deflist.tsv') | % { $c = $_ -split "`t"; [pscustomobject]@{ Type=$c[1]; Name=$c[2] } }
$thingNames = $rows | ? { $_.Type -eq 'ThingDef' -and $_.Name -notlike 'ABSTRACT:*' -and $keepThings -notcontains $_.Name } | % Name | Sort-Object -Unique
$thingNames = @($thingNames) + @('AmmoSet_37x223mmR_placeholder_unused')   # harmless extra
Comment 'Stand-ins from Industrial\Defs\FTH_Placeholders.xml (Industrial''s own 37 mm patches have run by now)'
RemoveIf 'Defs/CombatExtended.AmmoSetDef[defName="AmmoSet_37x223mmR"]'
RemoveIf ('Defs/ThingDef[' + (Or @('Ammo_37x223mmR_AP','Ammo_37x223mmR_HE')) + ']')
Comment 'Things: concrete economy, benches, utility buildings, duplicate/unsupported turrets, their ammo and projectiles'
RemoveIf ('Defs/ThingDef[' + (Or $thingNames) + ']')
Comment 'Abstract parents of removed things'
RemoveIf ('Defs/ThingDef[' + (Or @('128x958mmRShellBase','15cmNbW_Shell_Toxic_Releasing_Base','Base128x958mmRBullet','BaseFT105mmHowitzerShell','BaseFT155mmHowitzerShell','FT_BaseCannonBuilding_Deployable','FT_MinifiedTurret','FT_MinifiedTurretEquippable') '@Name') + ']')
foreach ($t in 'RecipeDef','ResearchProjectDef','TerrainDef','CombatExtended.AmmoSetDef','WorkGiverDef','JobDef','JoyGiverDef','JoyKindDef','ThingCategoryDef','ResearchTabDef') {
  $n = $rows | ? { $_.Type -eq $t -and $_.Name -notlike 'ABSTRACT:*' } | % Name | Sort-Object -Unique
  if ($n) { Comment "$t added by Industrial"; RemoveIf ("Defs/$t[" + (Or $n) + ']') }
}
RemoveIf 'Defs/RecipeDef[@Name="MakeConcreteBase"]'
Comment 'Research-bench facility links to the removed ENIAC'
RemoveIf 'Defs/ThingDef/comps/li[@Class="CompProperties_AffectedByFacilities"]/linkableFacilities/li[text()="FT_ENIAC"]'
Comment 'Framework (AOBA.Framework) visual comps on the kept guns'
RemoveIf 'Defs/ThingDef/comps/li[@Class="Fortified.CompProperties_CastFlecker" or @Class="Fortified.CompProperties_CastPushHeat"]'
Comment 'Difficulty cost variants that need the removed reinforced barrel'
RemoveIf ('Defs/ThingDef[' + (Or @('FT_TurretQuadAA','FT_TurretAT_Plus')) + ']/costListForDifficulty')
Comment 'Concrete barrier dropdown (ArchitectSense sub-category is used instead)'
RemoveIf ('Defs/ThingDef[' + (Or @('FT_TankTrap','FT_Tetrapods','FT_Barrier','FT_BarrierDiagonal')) + ']/designatorDropdown')
Flush 'Industrial\Patches\FTH_01_Remove.xml'

# =====================================================================
# INDUSTRIAL 02: restore HSK values that Industrial overwrites
# =====================================================================
Comment 'Machining ("Guns I"): Industrial sets FT_Lathe. HSK = Bow_B2 + Metals_C1; Vile''s Pre-Industrial = Muskets_B3 + Metals_C1'
Op @'
  <Operation Class="PatchOperationFindMod">
    <mods><li>HMC Vile's Pre-Industrial Overhaul</li></mods>
    <match Class="PatchOperationReplace">
      <xpath>Defs/ResearchProjectDef[defName="Machining"]/prerequisites</xpath>
      <value><prerequisites><li>Muskets_B3</li><li>Metals_C1</li></prerequisites></value>
    </match>
    <nomatch Class="PatchOperationReplace">
      <xpath>Defs/ResearchProjectDef[defName="Machining"]/prerequisites</xpath>
      <value><prerequisites><li>Bow_B2</li><li>Metals_C1</li></prerequisites></value>
    </nomatch>
  </Operation>
'@
Comment 'MicroelectronicsBasics ("Electronics I"): Industrial sets FT_ENIAC. HSK keeps vanilla Electricity'
SetNode 'MicroelectronicsBasics' 'prerequisites' '<prerequisites><li>Electricity</li></prerequisites>' 'ResearchProjectDef'
Comment 'Research view positions moved by Industrial (HSK values)'
SetNode 'Electricity' 'researchViewX' '<researchViewX>7.00</researchViewX>' 'ResearchProjectDef'
SetNode 'Electricity' 'researchViewY' '<researchViewY>15.00</researchViewY>' 'ResearchProjectDef'
SetNode 'HeavyTurrets' 'researchViewX' '<researchViewX>13.00</researchViewX>' 'ResearchProjectDef'
SetNode 'HeavyTurrets' 'researchViewY' '<researchViewY>33.00</researchViewY>' 'ResearchProjectDef'
Comment 'Floor costs replaced by Industrial with its own concrete (HSK values)'
SetNode 'Concrete' 'costList' '<costList><ConcreteResource>3</ConcreteResource></costList>' 'TerrainDef'
SetNode 'PavedTile' 'costList' '<costList><ConcreteResource>2</ConcreteResource><ComponentIndustrial>1</ComponentIndustrial></costList>' 'TerrainDef'
Flush 'Industrial\Patches\FTH_02_RestoreHSK.xml'

# =====================================================================
# INDUSTRIAL 03: structures
# =====================================================================
Comment 'Sandbag wall: full-height, impassable sandbag wall. HSK sandbags = 300 HP low cover, 5 fabric + 3 sand, Fortification_B1'
SetNode 'FT_WallSandbag' 'label' '<label>sandbag wall</label>'
SetNode 'FT_WallSandbag' 'description' '<description>Sandbags stacked shoulder-high and held by steel rods into a full wall. Blocks movement and line of fire, does not hold a roof. Cheap, quick and ugly; it soaks up small-arms fire well but crumbles under explosives.</description>'
SetNamed 'FT_SandBag_Base' 'stuffCategories' '<stuffCategories><li>Fabric</li><li>Leathery</li></stuffCategories>'
SetNode 'FT_WallSandbag' 'costStuffCount' '<costStuffCount>8</costStuffCount>'
SetNode 'FT_WallSandbag' 'costList' '<costList><SandResource>6</SandResource></costList>'
Stat 'FT_WallSandbag' @{ MaxHitPoints=400; WorkToBuild=750; Beauty=-3 }
SetNode 'FT_WallSandbag' 'researchPrerequisites' '<researchPrerequisites><li>Fortification_B2</li></researchPrerequisites>'
SetNode 'FT_WallSandbag' 'constructionSkillPrerequisite' '<constructionSkillPrerequisite>3</constructionSkillPrerequisite>'

Comment 'Concrete obstacles: HSK concrete / reinforced concrete, Concrete I + Fortification IV'
$res2 = '<researchPrerequisites><li>Concrete_C1</li><li>IEDs</li></researchPrerequisites>'
SetNode 'FT_TankTrap' 'costList' '<costList><ConcreteResource>8</ConcreteResource><Steel>6</Steel></costList>'
Stat 'FT_TankTrap' @{ MaxHitPoints=600; WorkToBuild=900 }
SetNode 'FT_TankTrap' 'researchPrerequisites' $res2
SetNode 'FT_TankTrap' 'constructionSkillPrerequisite' '<constructionSkillPrerequisite>4</constructionSkillPrerequisite>'

SetNode 'FT_Tetrapods' 'costList' '<costList><ConcreteResource>14</ConcreteResource></costList>'
Stat 'FT_Tetrapods' @{ MaxHitPoints=550; WorkToBuild=1200; Mass=300 }
SetNode 'FT_Tetrapods' 'researchPrerequisites' '<researchPrerequisites><li>Concrete_C1</li></researchPrerequisites>'
SetNode 'FT_Tetrapods' 'constructionSkillPrerequisite' '<constructionSkillPrerequisite>4</constructionSkillPrerequisite>'

foreach ($b in @(@{d='FT_Barrier';rc=10;hp=650;w=1000;m=120}, @{d='FT_BarrierDiagonal';rc=12;hp=700;w=1200;m=140})) {
  SetNode $b.d 'costList' "<costList><ReinforcedConcrete>$($b.rc)</ReinforcedConcrete></costList>"
  Stat $b.d @{ MaxHitPoints=$b.hp; WorkToBuild=$b.w; Mass=$b.m }
  SetNode $b.d 'researchPrerequisites' $res2
  SetNode $b.d 'constructionSkillPrerequisite' '<constructionSkillPrerequisite>5</constructionSkillPrerequisite>'
}
Flush 'Industrial\Patches\FTH_03_Structures.xml'

# =====================================================================
# INDUSTRIAL 04: turrets
# =====================================================================
Comment 'Gun-shield / cement-base emplacement: one damage table (Inherit=False) so it does not clash with HSK BuildingBase'
SetNamed 'FT_BaseCannonBuilding_WithShieldOrCementBase' 'damageMultipliers' @'
<damageMultipliers Inherit="False">
  <li><damageDef>Bullet</damageDef><multiplier>0.35</multiplier></li>
  <li><damageDef>Arrow</damageDef><multiplier>0.1</multiplier></li>
  <li><damageDef>Beanbag</damageDef><multiplier>0.1</multiplier></li>
  <li><damageDef>Optic</damageDef><multiplier>0.6</multiplier></li>
  <li><damageDef>Microwave</damageDef><multiplier>0.8</multiplier></li>
  <li><damageDef>Blunt</damageDef><multiplier>1.0</multiplier></li>
  <li><damageDef>Cut</damageDef><multiplier>0.5</multiplier></li>
  <li><damageDef>Stab</damageDef><multiplier>0.5</multiplier></li>
  <li><damageDef>Bite</damageDef><multiplier>0.4</multiplier></li>
  <li><damageDef>Scratch</damageDef><multiplier>0</multiplier></li>
  <li><damageDef>KineticPulse</damageDef><multiplier>1.2</multiplier></li>
  <li><damageDef>Bomb</damageDef><multiplier>0.75</multiplier></li>
  <li><damageDef>EMP</damageDef><multiplier>0</multiplier></li>
  <li><damageDef>Stun</damageDef><multiplier>0</multiplier></li>
  <li><damageDef>Electrical</damageDef><multiplier>0</multiplier></li>
  <li MayRequire="Ludeon.RimWorld.Biotech"><damageDef>MechBandShockwave</damageDef><multiplier>0</multiplier></li>
</damageMultipliers>
'@

function TurretBuilding($d, $crate, $stuff, $hp, $work, $mass, $res, $skill) {
  SetNode $d 'stuffCategories' '<stuffCategories><li>RuggedMetallic</li></stuffCategories>'
  SetNode $d 'costStuffCount' "<costStuffCount>$stuff</costStuffCount>"
  SetNode $d 'costList' "<costList><$crate>1</$crate></costList>"
  Stat $d @{ MaxHitPoints=$hp; WorkToBuild=$work; Mass=$mass; Flammability=0.5 }
  SetNode $d 'researchPrerequisites' "<researchPrerequisites><li>$res</li></researchPrerequisites>"
  SetNode $d 'constructionSkillPrerequisite' "<constructionSkillPrerequisite>$skill</constructionSkillPrerequisite>"
}
function Gun($g, $proj, $ammo, $mag, $reload, $range, $burst, $ticks, $warm, $cool, $sights, $spread, $sway, $aimedBurst) {
  SetNode $g 'verbs/li/defaultProjectile' "<defaultProjectile>$proj</defaultProjectile>"
  SetNode $g 'verbs/li/range' "<range>$range</range>"
  SetNode $g 'verbs/li/warmupTime' "<warmupTime>$warm</warmupTime>"
  if ($burst) { SetNode $g 'verbs/li/burstShotCount' "<burstShotCount>$burst</burstShotCount>"; SetNode $g 'verbs/li/ticksBetweenBurstShots' "<ticksBetweenBurstShots>$ticks</ticksBetweenBurstShots>" }
  SetNode $g 'comps/li[@Class="CombatExtended.CompProperties_AmmoUser"]/ammoSet' "<ammoSet>$ammo</ammoSet>"
  SetNode $g 'comps/li[@Class="CombatExtended.CompProperties_AmmoUser"]/magazineSize' "<magazineSize>$mag</magazineSize>"
  SetNode $g 'comps/li[@Class="CombatExtended.CompProperties_AmmoUser"]/reloadTime' "<reloadTime>$reload</reloadTime>"
  if ($aimedBurst) { SetNode $g 'comps/li[@Class="CombatExtended.CompProperties_FireModes"]/aimedBurstShotCount' "<aimedBurstShotCount>$aimedBurst</aimedBurstShotCount>" }
  Stat $g @{ RangedWeapon_Cooldown=$cool; SightsEfficiency=$sights; ShotSpread=$spread; SwayFactor=$sway }
}

Comment 'Quad-mounted machine gun: manned, 4x 7.62x51mm. More volume than the HSK M240B, less accuracy, huge ammo draw'
SetNode 'FT_Maxim4M' 'description' '<description>Four belt-fed 7.62x51mm machine guns on one tripod mount. Throws out far more lead than a single M240B, but the shared mount sways and the spread is wide. Eats ammunition at an alarming rate. Built from a crate made at the heavy arms lathe.</description>'
TurretBuilding 'FT_Maxim4M' 'FTH_QuadMG_Crate' 70 160 1500 80 'Heavy_turrets_C2' 10
SetNode 'FT_Maxim4M' 'building/turretBurstWarmupTime' '<turretBurstWarmupTime>1.0</turretBurstWarmupTime>'
SetNode 'FT_Maxim4M' 'building/turretBurstCooldownTime' '<turretBurstCooldownTime>1.2</turretBurstCooldownTime>'
Gun 'FT_Gun_QuadMG' 'Bullet_762x51mmNATO_FMJ' 'AmmoSet_762x51mmNATO' 400 9.5 55 16 3 1.0 1.2 1.1 0.12 1.4 8
SetNode 'FT_Gun_QuadMG' 'description' '<description>Four 7.62x51mm machine guns linked on one mount.</description>'

Comment 'Quad 20mm flak emplacement: shielded 3x3 position, HSK 20x102mm. Heavier and tougher than the Oerlikon, slower to traverse'
SetNode 'FT_TurretQuadAA' 'label' '<label>quad 20mm flak emplacement</label>'
SetNode 'FT_TurretQuadAA' 'description' '<description>Four 20mm autocannons on a shielded, sandbagged mount. Shreds light vehicles, mechanoids and massed infantry at long range. The gun shield shrugs off most small-arms fire. Fires 20x102mm. Built from a crate made at the heavy arms lathe.</description>'
TurretBuilding 'FT_TurretQuadAA' 'FTH_QuadAA_Crate' 160 320 2600 900 'Heavy_turrets_C3' 13
SetNode 'FT_TurretQuadAA' 'building/turretBurstCooldownTime' '<turretBurstCooldownTime>1.8</turretBurstCooldownTime>'
Gun 'FT_Gun_TurretQuadAA' 'Bullet_20x102mm_FMJ' 'AmmoSet_20x102mm' 120 10 75 12 3 1.2 1.8 1.8 0.10 1.0 6
SetNode 'FT_Gun_TurretQuadAA' 'label' '<label>quad 20mm autocannons</label>'
SetNode 'FT_Gun_TurretQuadAA' 'verbs/li/recoilAmount' '<recoilAmount>0.9</recoilAmount>'

Comment '90mm anti-tank gun (was 76mm): shielded direct-fire gun using HSK/CE 90mm cannon shells'
SetNode 'FT_TurretAT_Plus' 'label' '<label>90mm anti-tank gun</label>'
SetNode 'FT_TurretAT_Plus' 'description' '<description>A long-barrelled 90mm anti-tank gun behind a thick gun shield. Slow to traverse, but accurate and hard-hitting at long range against vehicles, mechanoids and fortifications. Fires 90mm cannon shells. Built from a crate made at the heavy arms lathe.</description>'
TurretBuilding 'FT_TurretAT_Plus' 'FTH_ATGun_Crate' 180 340 2400 1100 'Artillery_C2' 14
Stat 'FT_TurretAT_Plus' @{ ShootingAccuracyTurret=1.0; AimingAccuracy=1.0 }
SetNode 'FT_TurretAT_Plus' 'building/turretBurstWarmupTime' '<turretBurstWarmupTime>2.0</turretBurstWarmupTime>'
SetNode 'FT_TurretAT_Plus' 'building/turretBurstCooldownTime' '<turretBurstCooldownTime>2.0</turretBurstCooldownTime>'
Gun 'FT_Gun_TurretAT_Plus' 'Bullet_90mmCannonShell_HEAT' 'AmmoSet_90mmCannonShell' 1 6.5 110 $null $null 2.0 2.0 2.2 0.10 1.1 $null
SetNode 'FT_Gun_TurretAT_Plus' 'label' '<label>90mm anti-tank gun</label>'
SetNode 'FT_Gun_TurretAT_Plus' 'description' '<description>A 90mm anti-tank cannon on a stationary, shielded emplacement.</description>'

Comment 'Automated MG nests (sandbag and concrete): HSK auto-turret rules: crate, power, Autoturrets I'
SetNamed 'FT_MGNest' 'costList' '<costList><FTH_MGNest_Crate>1</FTH_MGNest_Crate></costList>'
SetNamed 'FT_MGNest' 'researchPrerequisites' '<researchPrerequisites><li>GunTurrets</li></researchPrerequisites>'
SetNamed 'FT_MGNest' 'constructionSkillPrerequisite' '<constructionSkillPrerequisite>12</constructionSkillPrerequisite>'
RemoveIf 'Defs/ThingDef[@Name="FT_MGNest"]/comps/li[@Class="CompProperties_Power"]'
Op @'
  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ThingDef[@Name="FT_MGNest"]/comps</xpath>
    <value>
      <li Class="SK.CompProperties_IdlePower">
        <compClass>CompPowerTrader</compClass>
        <basePowerConsumption>900</basePowerConsumption>
      </li>
    </value>
  </Operation>
'@
SetNode 'FT_Turret_BunkerF' 'label' '<label>sandbag MG nest</label>'
SetNode 'FT_Turret_BunkerF' 'description' '<description>An automated 7.62x51mm machine gun dug into a ring of sandbags. Its simple targeting brain cannot be controlled, so beware of friendly fire. Needs power. Built from a crate made at the heavy arms lathe.</description>'
SetNode 'FT_Turret_BunkerF' 'stuffCategories' '<stuffCategories><li>Fabric</li><li>Leathery</li></stuffCategories>'
SetNode 'FT_Turret_BunkerF' 'costStuffCount' '<costStuffCount>60</costStuffCount>'
SetNode 'FT_Turret_BunkerF' 'costList' '<costList><FTH_MGNest_Crate>1</FTH_MGNest_Crate><SandResource>25</SandResource></costList>'
Stat 'FT_Turret_BunkerF' @{ MaxHitPoints=320; WorkToBuild=2200 }

SetNode 'FT_Turret_BunkerC' 'label' '<label>concrete MG bunker</label>'
SetNode 'FT_Turret_BunkerC' 'description' '<description>An automated 7.62x51mm machine gun inside a cast reinforced-concrete bunker. Much tougher than a sandbag nest. Its simple targeting brain cannot be controlled, so beware of friendly fire. Needs power. Built from a crate made at the heavy arms lathe.</description>'
RemoveIf 'Defs/ThingDef[defName="FT_Turret_BunkerC"]/stuffCategories'
SetNode 'FT_Turret_BunkerC' 'costStuffCount' '<costStuffCount>0</costStuffCount>'
SetNode 'FT_Turret_BunkerC' 'costList' '<costList><FTH_MGNest_Crate>1</FTH_MGNest_Crate><ReinforcedConcrete>40</ReinforcedConcrete><ConcreteResource>20</ConcreteResource></costList>'
Stat 'FT_Turret_BunkerC' @{ MaxHitPoints=520; WorkToBuild=3200 }
SetNode 'FT_Turret_BunkerC' 'researchPrerequisites' '<researchPrerequisites Inherit="False"><li>GunTurrets</li><li>Concrete_C1</li></researchPrerequisites>'

Gun 'FT_Gun_MG' 'Bullet_762x51mmNATO_FMJ' 'AmmoSet_762x51mmNATO' 200 6.0 55 10 6 1.6 1.0 1.0 0.07 1.0 4
SetNode 'FT_Gun_MG' 'label' '<label>nest machine gun</label>'
Flush 'Industrial\Patches\FTH_04_Turrets.xml'

# =====================================================================
# INDUSTRIAL 05: architect sub-categories for the turrets (existing HSK groups)
# =====================================================================
foreach ($pair in @(@('SubCategory_Machineguns','FT_Maxim4M'), @('SubCategory_ACannons','FT_TurretQuadAA'), @('SubCategory_Cannons','FT_TurretAT_Plus'), @('SubCategory_SentryTurrets','FT_Turret_BunkerF'), @('SubCategory_SentryTurrets','FT_Turret_BunkerC'))) {
  Op @"
  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ArchitectSense.DesignationSubCategoryDef[defName="$($pair[0])"]/defNames</xpath>
    <value><li>$($pair[1])</li></value>
  </Operation>
"@
}
Flush 'Industrial\Patches\FTH_05_Architect.xml'

# =====================================================================
# MEDIEVAL
# =====================================================================
Out-File2 'Medieval\Defs\FTH_SubCategories_Medieval.xml' @'
<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <ArchitectSense.DesignationSubCategoryDef>
    <defName>SubCategory_FortressMedieval</defName>
    <label>medieval fortifications</label>
    <description>Castle walls, embrasures and stone-lined ditches.</description>
    <defNames>
      <li>FT_CastleWall</li>
      <li>FT_CastleWallEmbrasures</li>
      <li>FT_ReinforcedDitch</li>
    </defNames>
    <designationCategory>Security</designationCategory>
    <debug>false</debug>
    <order>2</order>
    <types>
      <li>Fortifications</li>
    </types>
  </ArchitectSense.DesignationSubCategoryDef>
</Defs>
'@
Comment 'Swivel gun: duplicate of Vile''s Pre-Industrial "38mm Swivel Gun" (Turret_Culverin); removed'
RemoveIf ('Defs/ThingDef[' + (Or @('FT_Turret_SwivelGun','FT_Gun_SwivelGun','FT_Proj_Cannon')) + ']')

Comment 'Reinforced ditch: stone-lined upgrade of the Neolithic ditch (750 HP, 2600 work, Fortification_A1)'
SetNode 'FT_ReinforcedDitch' 'stuffCategories' '<stuffCategories><li>Stony</li><li>Bricks</li></stuffCategories>'
SetNode 'FT_ReinforcedDitch' 'costStuffCount' '<costStuffCount>6</costStuffCount>'
Stat 'FT_ReinforcedDitch' @{ MaxHitPoints=650; WorkToBuild=3200 }
SetNode 'FT_ReinforcedDitch' 'researchPrerequisites' '<researchPrerequisites><li>Fortification_B1</li></researchPrerequisites>'
SetNode 'FT_ReinforcedDitch' 'constructionSkillPrerequisite' '<constructionSkillPrerequisite>4</constructionSkillPrerequisite>'

Comment 'Castle wall: thick curtain wall. HSK wall = 380 HP base, 6 stuff, 270 work. Castle wall ~1.8x HP for 2.3x stuff, 5x work'
SetNode 'FT_CastleWall' 'stuffCategories' '<stuffCategories><li>Stony</li><li>Bricks</li></stuffCategories>'
SetNode 'FT_CastleWall' 'costStuffCount' '<costStuffCount>14</costStuffCount>'
Stat 'FT_CastleWall' @{ MaxHitPoints=700; WorkToBuild=1400 }
SetNode 'FT_CastleWall' 'designationCategory' '<designationCategory>Security</designationCategory>'
SetNode 'FT_CastleWall' 'damageMultipliers' '<damageMultipliers><li><damageDef>Bomb</damageDef><multiplier>2</multiplier></li></damageMultipliers>'
SetNode 'FT_CastleWall' 'researchPrerequisites' '<researchPrerequisites><li>Fortification_B2</li></researchPrerequisites>'

Comment 'Castle embrasure: CE cover 0.85 -> 0.7 (HSK CE_Embrasure 0.7), +1 fasteners like Neolithic HSK embrasures'
SetNode 'FT_CastleWallEmbrasures' 'label' '<label>castle embrasure</label>'
SetNode 'FT_CastleWallEmbrasures' 'costStuffCount' '<costStuffCount>12</costStuffCount>'
SetNode 'FT_CastleWallEmbrasures' 'costList' '<costList><ComponentMedieval>1</ComponentMedieval></costList>'
Stat 'FT_CastleWallEmbrasures' @{ MaxHitPoints=560; WorkToBuild=1400 }
SetNode 'FT_CastleWallEmbrasures' 'fillPercent' '<fillPercent>0.7</fillPercent>'

Comment 'Rock box: colonist-triggered stone launcher. Medieval materials instead of industrial steel; siege engines research'
SetNode 'FT_Turret_RockBox' 'label' '<label>rock box</label>'
SetNode 'FT_Turret_RockBox' 'stuffCategories' '<stuffCategories><li>Woody</li><li>Stony</li></stuffCategories>'
SetNode 'FT_Turret_RockBox' 'costStuffCount' '<costStuffCount>60</costStuffCount>'
SetNode 'FT_Turret_RockBox' 'costList' '<costList><ComponentMedieval>8</ComponentMedieval><WoodLog>20</WoodLog></costList>'
Stat 'FT_Turret_RockBox' @{ MaxHitPoints=220; WorkToBuild=4500 }
SetNode 'FT_Turret_RockBox' 'researchPrerequisites' '<researchPrerequisites><li>Ballista_B1</li></researchPrerequisites>'
Op @'
  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ArchitectSense.DesignationSubCategoryDef[defName="SubCategory_MedievalTurrets"]/defNames</xpath>
    <value><li>FT_Turret_RockBox</li></value>
  </Operation>
'@
Flush 'Medieval\Patches\FTH_Medieval.xml'

"Generated: " + ((Get-ChildItem $root -Recurse -File | % { $_.FullName.Replace($root,'') }) -join ', ')
