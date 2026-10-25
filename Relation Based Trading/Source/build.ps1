# Builds Relation Based Trading for RimWorld 1.5, 1.6 and 1.7 (needs .NET SDK 8+ and 10 for 1.7).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
foreach ($v in '1.5', '1.6', '1.7') {
    Remove-Item -Recurse -Force obj, bin -ErrorAction SilentlyContinue
    dotnet build -c Release "-p:RimVersion=$v" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $v" }
}
Remove-Item -Recurse -Force obj, bin -ErrorAction SilentlyContinue
