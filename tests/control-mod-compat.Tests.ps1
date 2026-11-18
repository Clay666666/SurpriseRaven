# Replays tracked patches from locked upstream commits and compiles both game APIs.
# Only creates local clones/build outputs; never deploys or starts the game.
[CmdletBinding()]
param([string]$GameDir = '', [string]$BaselineGameDir = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'tools\game-compatibility.ps1')
$envFile = Join-Path $repoRoot '.env'
if (Test-Path -LiteralPath $envFile) {
    foreach ($line in Get-Content -LiteralPath $envFile -Encoding UTF8) {
        if (-not $GameDir -and $line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') }
        if (-not $BaselineGameDir -and $line -match '^\s*BASELINE_GAME_DIR\s*=(.*)$') { $BaselineGameDir = $Matches[1].Trim().Trim('"') }
    }
}
if (-not $GameDir) { throw 'Pass -GameDir or configure GAME_DIR.' }
$target = Read-SupportedGame $GameDir
$baseline = Resolve-BaselineGame $BaselineGameDir $target
if ($target.Version -ne '0.113.1') { throw 'This two-version test requires a 0.113.1 target and a 0.111.0 baseline.' }
$testRoot = Join-Path $repoRoot ('artifacts\tests\controls-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'tools\toolchain.lock.json') -Raw | ConvertFrom-Json
$dotnet = Join-Path $repoRoot 'tools\dotnet\dotnet.exe'
$savedDotnetRoot = $env:DOTNET_ROOT
$results = @()

function Invoke-ControlCheck([string]$Command, [string[]]$Arguments, [string]$LogName) {
    $log = Join-Path $testRoot $LogName
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Command @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log | Out-Host
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($code -ne 0) { throw "Control compatibility check failed ($code). See $log" }
}

try {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    foreach ($control in @(@{ Name = 'STS2MCP'; Id = 'STS2_MCP' }, @{ Name = 'STS2MenuControl'; Id = 'STS2MenuControl' })) {
        $locked = @($lock.repositories | Where-Object id -eq $control.Name)
        if ($locked.Count -ne 1) { throw "Missing unique repository lock for $($control.Name)." }
        $clone = Join-Path $testRoot $control.Name
        Invoke-ControlCheck git @('clone', '--no-hardlinks', '--no-checkout', '--local', (Join-Path $repoRoot "tools\$($control.Name)"), $clone) "$($control.Name)-clone.log"
        Invoke-ControlCheck git @('-C', $clone, 'checkout', '--detach', $locked[0].commit) "$($control.Name)-checkout.log"
        $patches = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "patches\$($control.Name)") -Filter '*.patch' -File | Sort-Object Name)
        foreach ($patch in $patches) {
            Invoke-ControlCheck git @('-C', $clone, 'apply', '--check', $patch.FullName) "$($control.Name)-$($patch.Name)-check.log"
            Invoke-ControlCheck git @('-C', $clone, 'apply', $patch.FullName) "$($control.Name)-$($patch.Name)-apply.log"
        }
        foreach ($patch in $patches) {
            Invoke-ControlCheck git @('-C', $clone, 'apply', '--reverse', '--check', $patch.FullName) "$($control.Name)-$($patch.Name)-repeat.log"
        }
        foreach ($game in @($baseline, $target)) {
            $output = Join-Path $testRoot "build\$($game.ApiTarget)\$($control.Id)"
            $intermediate = (Join-Path $testRoot "obj\$($game.ApiTarget)\$($control.Id)").Replace('\', '/') + '/'
            Invoke-ControlCheck $dotnet @('build', (Join-Path $clone "$($control.Id).csproj"), '--configuration', 'Release', '--nologo',
                '--output', $output, "-p:STS2GameDir=$($game.Directory)", "-p:STS2GameDataDir=$($game.DataDirectory)",
                "-p:Sts2ApiTarget=$($game.ApiTarget)", "-p:BaseIntermediateOutputPath=$intermediate") "$($control.Name)-$($game.ApiTarget)-build.log"
            $dll = Join-Path $output "$($control.Id).dll"
            $results += [pscustomobject]@{
                mod_id = $control.Id; game_version = $game.Version; api_target = $game.ApiTarget
                source_commit = $locked[0].commit; reference_sha256 = $game.ReferenceHashes
                dll_sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; output = $dll
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $testRoot 'control-compatibility-report.json'),
        (@{ status = 'passed'; runtime_tested = $false; builds = $results } | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Host "PASS: locked patch replay, repeat detection and both API builds. Runtime not tested. Reports: $testRoot"
} finally { $env:DOTNET_ROOT = $savedDotnetRoot }
