[CmdletBinding()]
param(
    [switch]$HeadlessGame,
    [switch]$Game,
    [switch]$NoMenuRoundTrip,
    [string]$GameDir = '',
    [string]$RitsuLibDir = '',
    [string]$PayloadDir = ''
)

# Legacy GUI verification is deliberately rejected before any subprocess starts.
if ($Game -or $NoMenuRoundTrip) {
    [Console]::Error.WriteLine('GUI game testing is prohibited. Use -HeadlessGame for isolated, genuinely headless runtime verification.')
    exit 1
}
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot ('artifacts\verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$utf8 = New-Object System.Text.UTF8Encoding $false
$failure = $null
$report = [ordered]@{
    startedUtc = [DateTime]::UtcNow.ToString('o')
    status = 'running'
    runtime_check = 'offline'
    headless_requested = [bool]$HeadlessGame
    gui_allowed = $false
}
$savedEnvironment = @{}
foreach ($key in @('DOTNET_ROOT', 'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT')) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}

function Invoke-VersionCommand([string]$Executable, [string[]]$CommandArgs) {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Missing tool: $Executable" }
    $result = & $Executable @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "Tool command failed: $Executable $CommandArgs (exit $LASTEXITCODE)" }
    return ($result -join [Environment]::NewLine)
}

function Invoke-VerificationScript([string]$Script, [string[]]$ScriptArguments) {
    if (-not (Test-Path -LiteralPath $Script -PathType Leaf)) { throw "Missing verification script: $Script" }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @ScriptArguments
    if ($LASTEXITCODE -ne 0) { throw "Verification failed (exit $LASTEXITCODE): $Script" }
}

try {
    $envPath = Join-Path $projectRoot '.env'
    if (Test-Path -LiteralPath $envPath) {
        foreach ($line in Get-Content -LiteralPath $envPath -Encoding UTF8) {
            if (-not $GameDir -and $line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') }
            if (-not $RitsuLibDir -and $line -match '^\s*RITSULIB_DIR\s*=(.*)$') { $RitsuLibDir = $Matches[1].Trim().Trim('"') }
        }
    }
    if (-not $GameDir) { throw 'Game directory is required. Pass -GameDir or set GAME_DIR in the local .env file.' }
    if (-not $RitsuLibDir) { throw 'RitsuLib directory is required. Pass -RitsuLibDir or set RITSULIB_DIR in the local .env file.' }
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    $dotnet = Join-Path $projectRoot 'tools\dotnet\dotnet.exe'
    $godot = Join-Path $projectRoot 'tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    $python = Join-Path $projectRoot 'tools\STS2MCP\mcp\.venv\Scripts\python.exe'
    $uv = Join-Path $projectRoot 'tools\uv\uv.exe'
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $sdks = Invoke-VersionCommand $dotnet @('--list-sdks')
    $runtimes = Invoke-VersionCommand $dotnet @('--list-runtimes')
    if ($sdks -notmatch '(?m)^10\.0\.') { throw 'Local .NET 10 SDK is missing.' }
    if ($runtimes -notmatch '(?m)^Microsoft\.NETCore\.App 8\.0\.') { throw 'Local .NET 8 Core Runtime is missing.' }
    if ($runtimes -notmatch '(?m)^Microsoft\.WindowsDesktop\.App 10\.0\.') { throw 'Local .NET 10 WindowsDesktop Runtime is missing.' }
    $report['dotnetSdks'] = $sdks
    $report['dotnetRuntimes'] = $runtimes
    $report['godot'] = Invoke-VersionCommand $godot @('--headless', '--version')
    if ($report['godot'] -notmatch '4\.5\.1.*mono') { throw "Unexpected Godot version: $($report['godot'])" }
    $report['uv'] = Invoke-VersionCommand $uv @('--version')
    $pythonInfo = Invoke-VersionCommand $python @('-c', 'import json,sys; assert sys.version_info >= (3,11); print(json.dumps(dict(version=sys.version,executable=sys.executable,base_prefix=sys.base_prefix)))')
    $report['python'] = $pythonInfo | ConvertFrom-Json
    $ilspyPath = Join-Path $projectRoot 'tools\ilspy\ILSpy.exe'
    if (-not (Test-Path -LiteralPath $ilspyPath)) { throw "ILSpy installation is missing: $ilspyPath" }
    $report['ilspyVersion'] = (Get-Item -LiteralPath $ilspyPath).VersionInfo.FileVersion
    Write-Host 'PASS: local SDK, runtimes, Godot headless CLI, ILSpy files, uv and virtual-environment Python.'

    $ritsuReport = Join-Path $outputDir 'ritsulib-report.json'
    $ritsuArguments = @('-GameDir', $GameDir, '-RitsuLibDir', $RitsuLibDir, '-OutputPath', $ritsuReport)
    if ($PayloadDir) { $ritsuArguments += @('-PayloadDir', $PayloadDir) }
    try {
        Invoke-VerificationScript (Join-Path $PSScriptRoot 'verify-ritsulib.ps1') $ritsuArguments
    } finally {
        if (Test-Path -LiteralPath $ritsuReport) {
            $report['ritsulib'] = Get-Content -LiteralPath $ritsuReport -Raw -Encoding UTF8 | ConvertFrom-Json
        }
    }

    & $python (Join-Path $PSScriptRoot 'mcp_smoke.py') smoke --root $projectRoot --output-dir $outputDir
    if ($LASTEXITCODE -ne 0) { throw "Offline MCP verification failed (exit $LASTEXITCODE). See $outputDir" }
    if ($HeadlessGame) {
        $headlessOutput = Join-Path $outputDir 'headless'
        $headlessArguments = @('-GameDir', $GameDir, '-RitsuLibDir', $RitsuLibDir, '-OutputDir', $headlessOutput)
        if ($PayloadDir) { $headlessArguments += @('-PayloadDir', $PayloadDir) }
        $report['headless_report_directory'] = $headlessOutput
        $report['runtime_check'] = 'headless'
        Invoke-VerificationScript (Join-Path $PSScriptRoot 'test-headless-game.ps1') $headlessArguments
    }
    $report['status'] = 'passed'
} catch {
    $failure = $_
    $report['status'] = 'failed'
    $report['error'] = $_.Exception.Message
    [Console]::Error.WriteLine('Verification failed: ' + $_.Exception.Message)
} finally {
    foreach ($key in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process')
    }
    $report['finishedUtc'] = [DateTime]::UtcNow.ToString('o')
    if (Test-Path -LiteralPath $outputDir) {
        [IO.File]::WriteAllText((Join-Path $outputDir 'environment-report.json'), ($report | ConvertTo-Json -Depth 20), $utf8)
    }
    Write-Host "Verification report: $outputDir"
}
if ($failure) { exit 1 }
