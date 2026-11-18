$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repositoryRoot 'tools\test-headless-game.ps1')
$fixture = Join-Path $repositoryRoot ('artifacts\tests\headless-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null

function Assert-Test([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-Test $failed $Message
}
function Write-Fixture([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

$source = Join-Path $fixture 'source'
$runtime = Join-Path $fixture 'runtime'
$payload = Join-Path $fixture 'payload'
$mirror = Join-Path $fixture 'mirror'
foreach ($relative in @('SlayTheSpire2.exe', 'SlayTheSpire2.pck', 'release_info.json', 'data_sts2_windows_x86_64\sts2.dll', 'controller_config\controller.json', 'fmod.dll', 'crashpad_handler.exe', 'mods\PrivateMod\secret.txt', '.disabled-mods\private.txt')) {
    Write-Fixture (Join-Path $source $relative) "fixture:$relative"
}
Write-Fixture (Join-Path $runtime 'STS2-RitsuLib.dll') 'runtime-fixture'
Write-Fixture (Join-Path $runtime 'compat\0.111.0\library.dll') 'runtime-variant-fixture'
Write-Fixture (Join-Path $payload 'SurpriseRaven.dll') 'main-mod-fixture'
New-HeadlessMirror $source $mirror $runtime $payload | Out-Null
Assert-Test (Test-Path -LiteralPath (Join-Path $mirror 'data_sts2_windows_x86_64\sts2.dll')) 'Game data must be mirrored.'
Assert-Test (Test-Path -LiteralPath (Join-Path $mirror 'mods\STS2-RitsuLib\compat\0.111.0\library.dll')) 'Complete runtime must be copied.'
Assert-Test (Test-Path -LiteralPath (Join-Path $mirror 'mods\SurpriseRaven\SurpriseRaven.dll')) 'The game must find the new DLL under its published Mod ID.'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $mirror 'mods\PrivateMod'))) 'User Mods must never enter the mirror.'
Assert-Test (-not (Test-Path -LiteralPath (Join-Path $mirror '.disabled-mods'))) 'Disabled user Mods must never enter the mirror.'
Assert-Test (@(Get-ChildItem -LiteralPath (Join-Path $mirror 'mods') -Directory).Count -eq 2) 'The test mirror must contain exactly two Mods.'
Expect-Failure { New-HeadlessMirror $source $mirror $runtime $payload } 'Existing mirror reuse must be rejected.'
Expect-Failure { Assert-HeadlessChildPath (Join-Path $fixture 'escape') $mirror } 'Paths outside the mirror must be rejected.'

$parentAppData = $env:APPDATA
$isolated = Initialize-HeadlessUserData $mirror
$settings = Get-Content -LiteralPath (Join-Path $isolated.APPDATA 'SlayTheSpire2\default\1\settings.save') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Test ($settings.schema_version -eq 8 -and $settings.mod_settings.mods_enabled -and @($settings.mod_settings.mod_list).Count -eq 0) 'Fresh settings must enable Mods without copying a profile.'
$info = New-HeadlessStartInfo $mirror (Join-Path $mirror 'test.log') $isolated
Assert-Test ($info.Arguments -match '(^| )--headless( |$)' -and $info.Arguments -match '--force-steam=off') 'True headless and Steam-off flags are mandatory.'
Assert-Test ($info.Arguments -match '--quit-after 5400' -and $info.Arguments -match '--max-fps 60') 'The launch specification must contain a frame limit.'
Assert-Test (-not $info.UseShellExecute -and $info.WorkingDirectory -eq $mirror) 'The process must directly execute in the isolated mirror.'
Assert-Test ($info.EnvironmentVariables['APPDATA'] -eq $isolated.APPDATA -and $env:APPDATA -eq $parentAppData) 'Child environment must be isolated without changing the parent.'
Assert-Test ($info.EnvironmentVariables['STS2_SURPRISE_RAVEN_HEADLESS_TEST'] -eq '1') 'The test probe must be enabled only for the child.'
Assert-Test ($info.EnvironmentVariables['STS2_SURPRISE_RAVEN_TEST_ROOT'] -eq $mirror) 'The probe must receive the expected isolation root.'

$probe = @{ status = 'passed'; mod_id = 'SurpriseRaven'; display_server = 'headless'; ritsulib_version = '0.6.14.0'; data_directory = Join-Path $isolated.APPDATA 'SlayTheSpire2'; cache_directory = $isolated.LOCALAPPDATA }
Write-HeadlessJson (Join-Path $mirror 'headless-probe.json') $probe
$runtimeEvidence = @{ status = 'passed'; version = '0.6.14'; game_version = '0.111.0'; compat_target = '0.111.0' }
$displayName = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\SurpriseRaven.json') -Raw -Encoding UTF8 | ConvertFrom-Json).name
$initializationLog = "Finished mod initialization for 'RitsuLib' (STS2-RitsuLib).`nFinished mod initialization for '$displayName' (SurpriseRaven).`nRITSULIB_HEADLESS_PROBE_OK`nRAVEN_HEADLESS_TESTS_OK"
$successLog = "[RitsuLib.Loader] Host=v0.111.0; runtime=0.111.0.`n" + $initializationLog
Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence | Out-Null
Expect-Failure { Assert-HeadlessResult $successLog.Replace('(SurpriseRaven)', '(Sts2ModScaffold)') 0 $mirror $runtimeEvidence } 'Initialization of the retired Mod ID must not qualify the new release.'
Expect-Failure { Assert-HeadlessResult $successLog.Replace('RAVEN_HEADLESS_TESTS_OK', '') 0 $mirror $runtimeEvidence } 'Missing gameplay checks must fail.'
Expect-Failure { Assert-HeadlessResult $successLog 1 $mirror $runtimeEvidence } 'A nonzero exit must fail even when markers exist.'
Expect-Failure { Assert-HeadlessResult 'RITSULIB_HEADLESS_PROBE_OK' 0 $mirror $runtimeEvidence } 'A marker without initialization records must fail.'
Expect-Failure { Assert-HeadlessResult $successLog 0 $mirror $null } 'Missing preflight evidence must fail.'
$runtimeEvidence.game_version = '0.113.1'
$runtimeEvidence.compat_target = '0.113.0'
Expect-Failure { Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence } 'A previous host/runtime log must not pass the 113 check.'
$successLog = "[RitsuLib.Loader] Host=v0.113.1; runtime=0.113.0.`n" + $initializationLog
Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence | Out-Null
Expect-Failure { Assert-HeadlessResult $successLog.Replace('runtime=0.113.0', 'runtime=0.111.0') 0 $mirror $runtimeEvidence } 'A wrong compatibility runtime must fail even with the correct library version.'
$probe.ritsulib_version = '0.6.13.0'
Write-HeadlessJson (Join-Path $mirror 'headless-probe.json') $probe
Expect-Failure { Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence } 'Loaded 0.6.13 must fail even with a successful 0.6.14 preflight.'
$probe.ritsulib_version = '0.6.14.0'
Write-HeadlessJson (Join-Path $mirror 'headless-probe.json') $probe
$runtimeEvidence.version = '0.6.13'
Expect-Failure { Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence } 'A 0.6.13 preflight must fail even when the probe loaded 0.6.14.'
$runtimeEvidence.version = '0.6.14'
Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence | Out-Null
$probe.display_server = 'windows'
Write-HeadlessJson (Join-Path $mirror 'headless-probe.json') $probe
Expect-Failure { Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence } 'A GUI display driver must never pass.'
$probe.display_server = 'headless'
$probe.data_directory = Join-Path $fixture 'real-user-data'
Write-HeadlessJson (Join-Path $mirror 'headless-probe.json') $probe
Expect-Failure { Assert-HeadlessResult $successLog 0 $mirror $runtimeEvidence } 'A real/nonisolated user data path must never pass.'

$pendingPipe = New-Object 'System.Threading.Tasks.TaskCompletionSource[string]'
Expect-Failure { Save-HeadlessPipeOutput $pendingPipe.Task (Join-Path $fixture 'pending-pipe.log') 20 } 'An inherited/open output pipe must fail after a bounded wait.'
$pendingPipe.SetResult('captured output')
Save-HeadlessPipeOutput $pendingPipe.Task (Join-Path $fixture 'finished-pipe.log') 20
Assert-Test ((Get-Content -LiteralPath (Join-Path $fixture 'finished-pipe.log') -Raw) -eq 'captured output') 'Completed pipe output must be retained.'

$savedPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repositoryRoot 'tools\launch_sts2.ps1') 2>$null
    $launcherExit = $LASTEXITCODE
} finally { $ErrorActionPreference = $savedPreference }
Assert-Test ($launcherExit -ne 0) 'The retired GUI launcher must refuse before launching a process.'
Write-Host 'PASS: isolated mirror selection, no user Mods/profile copying, mandatory headless flags, isolated child environment, probe identity, fail-closed runtime checks and retired GUI entry.'
Write-Host "Fixtures retained at $fixture. No game was started."
