# Checks run by Deploy-Mod before deploying: layout tests, translation keys match the source, no tiny fonts, preview size.
param([string]$RimWorldRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$failures = New-Object System.Collections.Generic.List[string]

# Layout core tests (console exe; exit code 0 means every check passed)
& dotnet run --project (Join-Path $root 'Tests\ResearchOrganized.Tests.csproj') -c Release --nologo | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { $failures.Add('Layout tests failed') }

$sourceText = (Get-ChildItem (Join-Path $root 'Source') -Recurse -Filter *.cs | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"

$used = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in [regex]::Matches($sourceText, '"(ResearchOrganized_[A-Za-z]+)"')) { [void]$used.Add($m.Groups[1].Value) }

$keyed = New-Object System.Collections.Generic.HashSet[string]
foreach ($file in Get-ChildItem (Join-Path $root 'Languages\English\Keyed') -Filter *.xml) {
    [xml]$xml = [IO.File]::ReadAllText($file.FullName)
    foreach ($node in $xml.LanguageData.ChildNodes) {
        if ($node.NodeType -eq 'Element' -and -not $keyed.Add($node.Name)) { $failures.Add("Duplicate key $($node.Name)") }
    }
}
foreach ($key in $used) { if (-not $keyed.Contains($key)) { $failures.Add("Missing translation: $key") } }
foreach ($key in $keyed) { if (-not $used.Contains($key)) { $failures.Add("Unused translation: $key") } }

if ($sourceText -match 'GameFont\.Tiny') { $failures.Add('GameFont.Tiny is used') }

$preview = Join-Path $root 'About\Preview.png'
if ((Get-Item $preview).Length -ge 1000000) { $failures.Add('About\Preview.png is 1,000,000 bytes or more') }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL $_" }
    exit 1
}
Write-Host "Test-All passed: layout tests, $($keyed.Count) keys checked"
