# Only removes files in a unique artificial game below artifacts/tests/.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot ('artifacts\tests\uninstall-' + [Guid]::NewGuid().ToString('N'))
$fakeGame = Join-Path $testRoot 'Game With Spaces'
$modsRoot = Join-Path $fakeGame 'mods'
$scriptPath = Join-Path $repoRoot 'tools\uninstall-mod.ps1'
New-Item -ItemType Directory -Path (Join-Path $fakeGame 'data_sts2_windows_x86_64') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fakeGame 'data_sts2_windows_x86_64\sts2.dll') -Value 'fake game'
foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl', 'OtherMod', 'STS2_MCP_Unrelated')) {
    $folder = Join-Path $modsRoot $id
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $folder "$id.dll") -Value 'test mod'
    Set-Content -LiteralPath (Join-Path $modsRoot "$id.json") -Value '{}'
}
Set-Content -LiteralPath (Join-Path $modsRoot 'mod_manifest.json') -Value 'unowned metadata'

# Process detection is isolated too: tests must not depend on a real game running.
$global:RavenUninstallTestRunning = $false
function Get-Process { param([string]$Name, $ErrorAction) if ($global:RavenUninstallTestRunning) { [pscustomobject]@{ Id = 99999 } } }

& $scriptPath -GameDir $fakeGame -WhatIf
foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
    if (-not (Test-Path -LiteralPath (Join-Path $modsRoot $id))) { throw 'WhatIf unexpectedly removed files.' }
}
$global:RavenUninstallTestRunning = $true
$blocked = $false
try { & $scriptPath -GameDir $fakeGame } catch { $blocked = $_.Exception.Message.Contains('game is running') }
if (-not $blocked) { throw 'Running-game protection did not reject removal.' }
$global:RavenUninstallTestRunning = $false
& $scriptPath -GameDir $fakeGame
foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
    if ((Test-Path -LiteralPath (Join-Path $modsRoot $id)) -or (Test-Path -LiteralPath (Join-Path $modsRoot "$id.json"))) {
        throw "Owned mod or legacy file was not removed: $id"
    }
}
foreach ($relative in @('OtherMod\OtherMod.dll', 'OtherMod.json', 'STS2_MCP_Unrelated\STS2_MCP_Unrelated.dll', 'STS2_MCP_Unrelated.json', 'mod_manifest.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $modsRoot $relative))) { throw "Unowned file was removed: $relative" }
}
& $scriptPath -GameDir $fakeGame
Write-Host 'PASS: WhatIf, running-game protection, exact owned IDs, legacy files, unrelated mod preservation, repeated uninstall.'
Write-Host "Artificial game retained at: $fakeGame"
