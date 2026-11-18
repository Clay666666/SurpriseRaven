[CmdletBinding()]
param(
    [string]$GameDir = '', [string]$RitsuLibDir = '', [string]$PayloadDir = '', [string]$OutputDir = '',
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 120,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Assert-HeadlessChildPath([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not $full.StartsWith($parent + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes isolated test root: $full" }
    $cursor = $full
    while ($cursor.Length -ge $parent.Length) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing isolated path through a reparse point: $cursor" }
        if ($cursor -eq $parent) { break }
        $cursor = Split-Path -Parent $cursor
    }
    return $full
}
function Assert-HeadlessNoReparse([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing test resource link or junction: $Path" }
    if ($item.PSIsContainer) {
        foreach ($child in @(Get-ChildItem -LiteralPath $Path -Force)) { Assert-HeadlessNoReparse $child.FullName }
    }
}
function Write-HeadlessJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
function Add-HeadlessHardLinks([string]$Source, [string]$Destination, [string]$MirrorRoot) {
    $target = Assert-HeadlessChildPath $Destination $MirrorRoot
    $item = Get-Item -LiteralPath $Source -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing original resource reparse point: $Source" }
    if ($item.PSIsContainer) {
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($child in @(Get-ChildItem -LiteralPath $Source -Force)) { Add-HeadlessHardLinks $child.FullName (Join-Path $target $child.Name) $MirrorRoot }
    } else {
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        New-Item -ItemType HardLink -Path $target -Value $item.FullName | Out-Null
    }
}
function New-HeadlessMirror([string]$Source, [string]$Mirror, [string]$Runtime, [string]$Payload) {
    if (Test-Path -LiteralPath $Mirror) { throw "Refusing an existing game mirror: $Mirror" }
    $sourceFull = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    $mirrorFull = [IO.Path]::GetFullPath($Mirror).TrimEnd('\')
    if ([IO.Path]::GetPathRoot($sourceFull) -ne [IO.Path]::GetPathRoot($mirrorFull)) { throw 'The mirror must be on the source volume for hard links.' }
    if ($mirrorFull.StartsWith($sourceFull + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The mirror must be outside the real game installation.' }
    $resources = @('SlayTheSpire2.exe', 'SlayTheSpire2.pck', 'release_info.json', 'data_sts2_windows_x86_64')
    foreach ($relative in $resources) {
        if (-not (Test-Path -LiteralPath (Join-Path $Source $relative))) { throw "Missing original resource: $relative" }
    }
    Assert-HeadlessNoReparse $Runtime
    Assert-HeadlessNoReparse $Payload
    New-Item -ItemType Directory -Path $Mirror -Force | Out-Null
    Write-HeadlessJson (Join-Path $Mirror '.sts2-headless-test.json') @{ schema = 1; source_game = $sourceFull; mirror = $mirrorFull }
    if (Test-Path -LiteralPath (Join-Path $Source 'controller_config')) { $resources += 'controller_config' }
    $resources += @(Get-ChildItem -LiteralPath $Source -File -Filter '*.dll' | Select-Object -ExpandProperty Name)
    $resources += @(Get-ChildItem -LiteralPath $Source -File -Filter 'crashpad*.exe' | Select-Object -ExpandProperty Name)
    foreach ($relative in @($resources | Select-Object -Unique)) { Add-HeadlessHardLinks (Join-Path $Source $relative) (Join-Path $Mirror $relative) $Mirror }
    $mods = Join-Path $Mirror 'mods'
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    Copy-Item -LiteralPath $Runtime -Destination (Join-Path $mods 'STS2-RitsuLib') -Recurse -Force
    Copy-Item -LiteralPath $Payload -Destination (Join-Path $mods 'SurpriseRaven') -Recurse -Force
    return $mirrorFull
}
function Initialize-HeadlessUserData([string]$Mirror) {
    $data = Join-Path $Mirror 'test-user'
    $paths = [ordered]@{ APPDATA = Join-Path $data 'roaming'; LOCALAPPDATA = Join-Path $data 'local'; TEMP = Join-Path $data 'temp'; TMP = Join-Path $data 'temp' }
    foreach ($path in $paths.Values) {
        Assert-HeadlessChildPath $path $Mirror | Out-Null
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    $settingsDir = Join-Path $paths.APPDATA 'SlayTheSpire2\default\1'
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    Write-HeadlessJson (Join-Path $settingsDir 'settings.save') @{
        schema_version = 8; fps_limit = 60; skip_intro_logo = $true; seen_ea_disclaimer = $true
        mod_settings = @{ mods_enabled = $true; mod_list = @() }
    }
    return $paths
}
function New-HeadlessStartInfo([string]$Mirror, [string]$LogPath, $IsolatedEnvironment) {
    Assert-HeadlessChildPath $LogPath $Mirror | Out-Null
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $Mirror 'SlayTheSpire2.exe'
    $info.WorkingDirectory = $Mirror
    # This suppresses only a console; the mandatory --headless flag disables the game display.
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Arguments = '--headless --force-steam=off --log-file "' + $LogPath + '" --quit-after 5400 --max-fps 60'
    foreach ($key in $IsolatedEnvironment.Keys) { $info.EnvironmentVariables[$key] = $IsolatedEnvironment[$key] }
    $info.EnvironmentVariables['STS2_SURPRISE_RAVEN_HEADLESS_TEST'] = '1'
    $info.EnvironmentVariables['STS2_SURPRISE_RAVEN_TEST_ROOT'] = $Mirror
    return $info
}
function Assert-HeadlessResult([string]$LogText, [int]$ExitCode, [string]$Mirror, $RuntimeEvidence) {
    if ($ExitCode -ne 0) { throw "Headless game exited with code $ExitCode." }
    foreach ($pattern in @('Finished mod initialization[^\r\n]*\(STS2-RitsuLib\)', 'Finished mod initialization[^\r\n]*\(SurpriseRaven\)', 'RITSULIB_HEADLESS_PROBE_OK', 'RAVEN_HEADLESS_TESTS_OK')) {
        if ($LogText -notmatch $pattern) { throw "Missing headless runtime confirmation: $pattern" }
    }
    $probe = Get-Content -LiteralPath (Join-Path $Mirror 'headless-probe.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($probe.status -ne 'passed' -or $probe.mod_id -ne 'SurpriseRaven' -or $probe.display_server -ne 'headless' -or $probe.ritsulib_version -ne '0.6.14.0') { throw 'Headless probe returned unexpected runtime identity or display driver.' }
    if (-not $RuntimeEvidence -or $RuntimeEvidence.status -ne 'passed' -or $RuntimeEvidence.version -ne '0.6.14') { throw 'Missing successful RitsuLib runtime preflight evidence.' }
    $selectionPattern = '\[RitsuLib\.Loader\] Host=v?' + [regex]::Escape($RuntimeEvidence.game_version) + '; runtime=' + [regex]::Escape($RuntimeEvidence.compat_target) + '\.'
    if ($LogText -notmatch $selectionPattern) { throw 'RitsuLib did not confirm the expected host version and compatibility runtime.' }
    Assert-HeadlessChildPath $probe.data_directory $Mirror | Out-Null
    Assert-HeadlessChildPath $probe.cache_directory $Mirror | Out-Null
    return $probe
}
function Save-HeadlessPipeOutput($Task, [string]$Path, [int]$WaitMilliseconds = 2000) {
    if (-not $Task.Wait($WaitMilliseconds)) { throw "Timed out while capturing the headless process output pipe. Engine log is retained: $Path" }
    [IO.File]::WriteAllText($Path, $Task.Result, [Text.UTF8Encoding]::new($false))
}

# Fixture tests dot-source definitions without preparing or starting any game.
if ($MyInvocation.InvocationName -eq '.') { return }
$process = $null
$processStarted = $false
$stdoutTask = $null
$stderrTask = $null
$failure = $null
$report = [ordered]@{ started_utc = [DateTime]::UtcNow.ToString('o'); status = 'running'; runtime_check = 'headless' }
try {
    if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) { throw 'An existing game process is running. Verification never attaches to or stops it.' }
    if (-not $GameDir) {
        $envFile = Join-Path $projectRoot '.env'
        if (Test-Path -LiteralPath $envFile) {
            foreach ($line in Get-Content -LiteralPath $envFile -Encoding UTF8) { if ($line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') } }
        }
    }
    if (-not $GameDir) { throw 'Game directory is required. Pass -GameDir or set GAME_DIR in the local .env file.' }
    $GameDir = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
    if (-not $PayloadDir) {
        $last = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\last-build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $payloads = @($last.payloads | Where-Object { $_.Id -eq 'SurpriseRaven' })
        if ($payloads.Count -ne 1) { throw 'No unique main Mod payload in last-build.json; build first.' }
        $PayloadDir = $payloads[0].Directory
    }
    $PayloadDir = [IO.Path]::GetFullPath($PayloadDir)
    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
    if (-not $OutputDir) { $OutputDir = Join-Path $projectRoot "artifacts\verification\headless-$runId" }
    $OutputDir = [IO.Path]::GetFullPath($OutputDir)
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $runtimePath = Join-Path $OutputDir 'ritsulib-runtime.json'
    $checkArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'verify-ritsulib.ps1'), '-GameDir', $GameDir, '-PayloadDir', $PayloadDir, '-OutputPath', $runtimePath)
    if ($RitsuLibDir) { $checkArgs += @('-RitsuLibDir', $RitsuLibDir) }
    & powershell.exe @checkArgs
    $preflightExit = $LASTEXITCODE
    if (Test-Path -LiteralPath $runtimePath) {
        $report['ritsulib'] = Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    if ($preflightExit -ne 0) { throw "RitsuLib preflight failed. See $runtimePath" }
    $runtime = Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($runtime.status -ne 'passed') { throw 'RitsuLib preflight did not return passed.' }
    $base = Join-Path ([IO.Path]::GetPathRoot($GameDir)) 'SteamLibrary\sts2-mod-tests'
    New-Item -ItemType Directory -Path $base -Force | Out-Null
    $mirror = Assert-HeadlessChildPath (Join-Path $base $runId) $base
    New-HeadlessMirror $GameDir $mirror $runtime.runtime_directory $PayloadDir | Out-Null
    $isolated = Initialize-HeadlessUserData $mirror
    $engineLog = Join-Path $mirror 'headless-engine.log'
    $startInfo = New-HeadlessStartInfo $mirror $engineLog $isolated
    $report['mirror_directory'] = $mirror
    $report['payload_directory'] = $PayloadDir
    $report['runtime_directory'] = $runtime.runtime_directory
    $report['executable'] = $startInfo.FileName
    $report['arguments'] = $startInfo.Arguments
    $report['isolated_environment'] = $isolated
    Write-HeadlessJson (Join-Path $OutputDir 'launch-spec.json') $report
    if ($PrepareOnly) {
        $report.status = 'prepared'
        Write-Host 'Prepared isolated mirror and launch specification. No game process was started.'
    } else {
        # There is intentionally no alternate executable, Steam URI or GUI fallback.
        $process = New-Object Diagnostics.Process
        $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw 'Failed to start the isolated headless game.' }
        $processStarted = $true
        $report['game_pid'] = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            [void]$process.WaitForExit(10000)
            throw "Headless timeout after $TimeoutSeconds seconds; terminated only owned PID $($report.game_pid)."
        }
        $report['exit_code'] = $process.ExitCode
        Save-HeadlessPipeOutput $stdoutTask (Join-Path $OutputDir 'stdout.log')
        Save-HeadlessPipeOutput $stderrTask (Join-Path $OutputDir 'stderr.log')
        $text = Get-Content -LiteralPath $engineLog -Raw -Encoding UTF8
        $report['probe'] = Assert-HeadlessResult $text $process.ExitCode $mirror $runtime
        $report.status = 'passed'
        Write-Host 'PASS: true headless display, isolated user data, RitsuLib initialization and Mod registration.'
    }
} catch {
    $failure = $_
    $report.status = 'failed'
    $report['error'] = $_.Exception.Message
    [Console]::Error.WriteLine('HEADLESS VERIFICATION FAILED: ' + $_.Exception.Message)
} finally {
    if ($process) {
        if ($processStarted -and -not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(10000) }
        if ($OutputDir -and (Test-Path -LiteralPath $OutputDir)) {
            if ($stdoutTask -and $stdoutTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { [IO.File]::WriteAllText((Join-Path $OutputDir 'stdout.log'), $stdoutTask.Result, [Text.UTF8Encoding]::new($false)) }
            if ($stderrTask -and $stderrTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { [IO.File]::WriteAllText((Join-Path $OutputDir 'stderr.log'), $stderrTask.Result, [Text.UTF8Encoding]::new($false)) }
        }
        $process.Dispose()
    }
    $report['finished_utc'] = [DateTime]::UtcNow.ToString('o')
    if ($OutputDir -and (Test-Path -LiteralPath $OutputDir)) {
        if ($report.Contains('mirror_directory')) {
            $log = Join-Path $report.mirror_directory 'headless-engine.log'
            if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $OutputDir 'headless-engine.log') -Force }
            $probePath = Join-Path $report.mirror_directory 'headless-probe.json'
            if (Test-Path -LiteralPath $probePath) { Copy-Item -LiteralPath $probePath -Destination (Join-Path $OutputDir 'headless-probe.json') -Force }
            $checksPath = Join-Path $report.mirror_directory 'raven-headless.json'
            if (Test-Path -LiteralPath $checksPath) { Copy-Item -LiteralPath $checksPath -Destination (Join-Path $OutputDir 'raven-headless.json') -Force }
        }
        Write-HeadlessJson (Join-Path $OutputDir 'headless-report.json') $report
        Write-Host "Headless verification report: $OutputDir"
    }
}
if ($failure) { exit 1 }
