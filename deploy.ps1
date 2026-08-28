<#
    Deploys Research: Organized from this repo (the master) to the RimWorld Mods
    folder, which holds only what ships to the Steam Workshop.

        powershell -ExecutionPolicy Bypass -File .\deploy.ps1
        powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -WhatIf

    Ships an ALLOWLIST, not an exclusion list, so anything added to the repo
    later - Source, Tests, README, the stray TechTreeProgression.slnx - stays
    out of the Workshop download unless it is named in $Ship below.

    Build first: the DLL in 1.6\Assemblies is what ships, not Source\bin.
#>
param(
    [string]$Target = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Research Organized",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$Repo = $PSScriptRoot
$Ship = @("1.6", "About")

foreach ($item in $Ship) {
    if (-not (Test-Path (Join-Path $Repo $item))) { throw "Repo is missing '$item' - wrong folder?" }
}

# The shipped assembly is easy to forget after a rebuild, so say how old it is.
$dll = Join-Path $Repo "1.6\Assemblies\ResearchOrganized.dll"
$built = Join-Path $Repo "Source\bin\Release\ResearchOrganized.dll"
if ((Test-Path $dll) -and (Test-Path $built)) {
    if ((Get-FileHash $dll).Hash -ne (Get-FileHash $built).Hash) {
        Write-Host "WARNING: 1.6\Assemblies DLL differs from Source\bin\Release - did you forget to copy the new build?" -ForegroundColor Yellow
    }
}

if ($WhatIf) {
    Write-Host "Would deploy to: $Target" -ForegroundColor Cyan
    Write-Host "Would ship     : $($Ship -join ', ')"
    $extra = Get-ChildItem $Target -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin $Ship }
    if ($extra) { Write-Host "Would remove   : $(($extra | Select-Object -ExpandProperty Name) -join ', ')" -ForegroundColor Yellow }
    return
}

if (-not (Test-Path $Target)) { New-Item -ItemType Directory -Path $Target -Force | Out-Null }

# RimWorld keeps the mod assembly loaded, so deploying over a running game blocks on a
# locked file. Say so up front rather than letting robocopy sit on its retry loop.
if (Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw "RimWorld is running and holds 1.6\Assemblies\ResearchOrganized.dll - close the game and re-run."
}

foreach ($item in $Ship) {
    # /R:2 /W:2 instead of robocopy's default one million retries at 30s apart, which turns
    # any locked file into an effectively infinite hang.
    robocopy (Join-Path $Repo $item) (Join-Path $Target $item) /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed on '$item' (exit $LASTEXITCODE) - is the game running?" }
}

# strip anything the Workshop should not carry
foreach ($stray in (Get-ChildItem $Target | Where-Object { $_.Name -notin $Ship })) {
    Write-Host "removing non-shipping item: $($stray.Name)" -ForegroundColor Yellow
    Remove-Item $stray.FullName -Recurse -Force
}

# verify every shipped file matches the repo
$bad = 0; $n = 0
foreach ($item in $Ship) {
    Get-ChildItem (Join-Path $Repo $item) -Recurse -File | ForEach-Object {
        $n++
        $rel = $_.FullName.Substring($Repo.Length + 1)
        $tp  = Join-Path $Target $rel
        if (-not (Test-Path $tp)) { $bad++; Write-Host "MISSING: $rel" -ForegroundColor Red }
        elseif ((Get-FileHash $_.FullName).Hash -ne (Get-FileHash $tp).Hash) { $bad++; Write-Host "DIFFERS: $rel" -ForegroundColor Red }
    }
}
$size = "{0:N2}" -f ((Get-ChildItem $Target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
if ($bad -eq 0) { Write-Host "deployed $n files, $size MB, all verified" -ForegroundColor Green }
else { throw "$bad file(s) failed verification" }
