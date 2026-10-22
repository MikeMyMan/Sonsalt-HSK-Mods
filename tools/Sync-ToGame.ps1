# Mirrors the four mod folders of this repo into the RimWorld Mods folder (robocopy /MIR per mod folder only).
# Close RimWorld first. Then start the game and check the log.
param([string]$ModsFolder = 'D:\SteamLibrary\steamapps\common\RimWorld\Mods')
if (Get-Process RimWorld -ErrorAction SilentlyContinue) { throw 'Close RimWorld first.' }
$repo = Split-Path $PSScriptRoot -Parent
foreach ($mod in 'HSK Fortifications Rebalance', 'HSK Outposts', 'HSK Zero Research Start', 'HSK 1.7 Compatibility Fixes') {
  robocopy (Join-Path $repo $mod) (Join-Path $ModsFolder $mod) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $mod (exit $LASTEXITCODE)" }
  "synced $mod"
}
