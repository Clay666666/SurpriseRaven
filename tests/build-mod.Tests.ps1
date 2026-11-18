# Dependency-free integration checks using an isolated fake game and native tool stubs.
# Run with Windows PowerShell 5.1; this never writes into the real game directory.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot ('artifacts\tests\build-' + [Guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $testRoot 'Project With Spaces'
$gameRoot = Join-Path $testRoot 'Game With Spaces'
$dataRoot = Join-Path $gameRoot 'data_sts2_windows_x86_64'
$buildScript = Join-Path $fixtureRoot 'tools\build-mod.ps1'
$oldFailBuild = $env:SURPRISE_RAVEN_TEST_FAIL_BUILD
$oldFailPck = $env:SURPRISE_RAVEN_TEST_FAIL_PCK
$oldCounter = $env:SURPRISE_RAVEN_TEST_COUNTER
. (Join-Path $PSScriptRoot 'raven-media-fixtures.ps1')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "TEST FAILED: $Message" }
}
function Write-TestJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}
function Run-Build([string[]]$Extra = @(), [bool]$ShouldSucceed = $true, [string]$Script = $buildScript) {
    $log = Join-Path $testRoot ('check-' + [Guid]::NewGuid().ToString('N') + '.log')
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script -GameDir $gameRoot @Extra 2>&1 |
            ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log -Encoding UTF8
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if (($code -eq 0) -ne $ShouldSucceed) {
        Get-Content -LiteralPath $log -Encoding UTF8 | Out-Host
        throw "Unexpected build exit code $code. Log: $log"
    }
    return $log
}

try {
    foreach ($path in @($dataRoot, (Join-Path $fixtureRoot 'src'), (Join-Path $fixtureRoot 'tools\dotnet'), (Join-Path $fixtureRoot 'tools\godot'), (Join-Path $fixtureRoot 'tools\pck_builder'))) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\build-mod.ps1') -Destination $buildScript
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\game-compatibility.ps1') -Destination (Join-Path $fixtureRoot 'tools')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\deployment-provenance.ps1') -Destination (Join-Path $fixtureRoot 'tools')
    Set-Content -LiteralPath (Join-Path $fixtureRoot '.gitignore') -Value '*'
    & git -C $fixtureRoot init --quiet
    & git -C $fixtureRoot -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty --quiet -m fixture
    Assert-True ($LASTEXITCODE -eq 0) 'Could not initialize the isolated Git fixture.'
    foreach ($file in @('SurpriseRaven.csproj', 'ModEntry.cs', 'SurpriseRaven.json', 'packages.lock.json')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "src\$file") -Destination (Join-Path $fixtureRoot 'src')
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\pck_builder\build_pck.gd') -Destination (Join-Path $fixtureRoot 'tools\pck_builder')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\stage-raven-assets.ps1') -Destination (Join-Path $fixtureRoot 'tools')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\raven-media-validation.ps1') -Destination (Join-Path $fixtureRoot 'tools')
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'assets\raven') -Force | Out-Null
    foreach ($file in @('raven-assets.json', 'raven-frontend.json')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "assets\raven\$file") -Destination (Join-Path $fixtureRoot 'assets\raven')
    }
    # Valid media uses small fixture dimensions while preserving all production ids and paths.
    $ravenSource = Join-Path $testRoot ([string]::Concat('Raven Assets ', [char]0x6D4B, [char]0x8BD5))
    $ravenSpec = Get-Content -LiteralPath (Join-Path $repoRoot 'assets\raven\raven-assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-RavenTestMedia $ravenSpec $ravenSource
    Write-TestJson (Join-Path $fixtureRoot 'assets\raven\raven-assets.json') $ravenSpec
    Set-Content -LiteralPath (Join-Path $fixtureRoot '.env') -Value "RAVEN_ASSETS_DIR=$ravenSource" -Encoding UTF8
    Write-TestJson (Join-Path $dataRoot 'sts2.runtimeconfig.json') @{ runtimeOptions = @{ tfm = 'net9.0' } }
    Write-TestJson (Join-Path $gameRoot 'release_info.json') @{ version = 'v0.111.0' }
    foreach ($file in @('sts2.dll', '0Harmony.dll', 'GodotSharp.dll')) { Set-Content -LiteralPath (Join-Path $dataRoot $file) -Value "fixture-$file" }
    $manifestPath = Join-Path $fixtureRoot 'src\SurpriseRaven.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest.dependencies = @(@{ id = 'FutureDependency' })
    $manifest.name = [string]::Concat([char]0x6D4B, [char]0x8BD5)
    Write-TestJson $manifestPath $manifest
    foreach ($control in @(@{ Folder = 'STS2MCP'; Id = 'STS2_MCP' }, @{ Folder = 'STS2MenuControl'; Id = 'STS2MenuControl' })) {
        $dir = Join-Path $fixtureRoot "tools\$($control.Folder)"
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir "$($control.Id).csproj") -Value '<Project><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>'
        Write-TestJson (Join-Path $dir 'mod_manifest.json') @{ id = $control.Id; name = $control.Id; has_dll = $true; has_pck = $false }
    }

    # Compiled .NET Framework executables let the real script exercise native exit
    # handling and Windows argument quoting without downloading an SDK or Godot.
    $stubSource = @'
using System;
using System.IO;
using System.Text;
public static class NativeToolStub {
    public static int Main(string[] args) {
        bool isDotnet = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) == "dotnet";
        if (isDotnet) {
            string id = Path.GetFileNameWithoutExtension(args[1]);
            if (Environment.GetEnvironmentVariable("SURPRISE_RAVEN_TEST_FAIL_BUILD") == id) {
                Console.Error.WriteLine("Injected compiler failure"); return 37;
            }
            string output = args[Array.IndexOf(args, "--output") + 1];
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "build-arguments.txt"), args);
            string counter = Environment.GetEnvironmentVariable("SURPRISE_RAVEN_TEST_COUNTER");
            int count = File.Exists(counter) ? Int32.Parse(File.ReadAllText(counter)) + 1 : 1;
            File.WriteAllText(counter, count.ToString());
            File.WriteAllText(Path.Combine(output, id + ".dll"), "fixture-build-" + count);
        } else {
            if (Environment.GetEnvironmentVariable("SURPRISE_RAVEN_TEST_FAIL_PCK") == "1") {
                Console.Error.WriteLine("Injected PCK failure"); return 29;
            }
            int separator = Array.IndexOf(args, "--");
            int userCount = args.Length - separator - 1;
            if (userCount == 4) {
                string manifest = args[separator + 1], root = args[separator + 2].TrimEnd('\\'), report = args[separator + 3];
                StringBuilder json = new StringBuilder("{\"entries\":[{\"path\":\"res://mod_manifest.json\",\"size\":" + new FileInfo(manifest).Length + "}");
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) {
                    string relative = file.Substring(root.Length + 1).Replace('\\', '/');
                    json.Append(",{\"path\":\"res://" + relative + "\",\"size\":" + new FileInfo(file).Length + "}");
                }
                File.WriteAllText(report, json.Append("]}").ToString());
            }
            byte[] pck = new byte[64];
            Encoding.ASCII.GetBytes("GDPC").CopyTo(pck, 0);
            File.WriteAllBytes(args[args.Length - 1], pck);
        }
        return 0;
    }
}
'@
    $stubExe = Join-Path $testRoot 'native-tool.exe'
    Add-Type -TypeDefinition $stubSource -OutputAssembly $stubExe -OutputType ConsoleApplication
    Copy-Item -LiteralPath $stubExe -Destination (Join-Path $fixtureRoot 'tools\dotnet\dotnet.exe')
    Copy-Item -LiteralPath $stubExe -Destination (Join-Path $fixtureRoot 'tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe')
    $env:SURPRISE_RAVEN_TEST_COUNTER = Join-Path $testRoot 'build-counter.txt'
    $env:SURPRISE_RAVEN_TEST_FAIL_BUILD = $null
    $env:SURPRISE_RAVEN_TEST_FAIL_PCK = $null

    $installedMain = Join-Path $gameRoot 'mods\SurpriseRaven'
    New-Item -ItemType Directory -Path $installedMain -Force | Out-Null
    $sentinel = Join-Path $installedMain 'keep-existing.txt'
    Set-Content -LiteralPath $sentinel -Value 'previous installation'

    Write-TestJson (Join-Path $dataRoot 'sts2.runtimeconfig.json') @{ runtimeOptions = @{ tfm = 'net10.0' } }
    Run-Build -ShouldSucceed $false | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'Incompatible game runtime must not modify installed files.'
    Write-TestJson (Join-Path $dataRoot 'sts2.runtimeconfig.json') @{ runtimeOptions = @{ tfm = 'net9.0' } }

    $env:SURPRISE_RAVEN_TEST_FAIL_BUILD = 'SurpriseRaven'
    Run-Build -ShouldSucceed $false | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'Compiler failure must not modify installed files.'
    $env:SURPRISE_RAVEN_TEST_FAIL_BUILD = $null
    $env:SURPRISE_RAVEN_TEST_FAIL_PCK = '1'
    Run-Build -ShouldSucceed $false | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'PCK failure must not modify installed files.'
    $env:SURPRISE_RAVEN_TEST_FAIL_PCK = $null

    Run-Build -Extra @('-RavenAssetsDir', (Join-Path $testRoot 'missing raven assets')) -ShouldSucceed $false | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'Missing raven assets must not modify installed files.'
    $flamePath = Join-Path $ravenSource 'textures\raven_lamp\effects\000_CandleFlame.png'
    $flameBytes = [IO.File]::ReadAllBytes($flamePath)
    $badFlame = [byte[]]$flameBytes.Clone(); $badFlame[19] = 7
    [IO.File]::WriteAllBytes($flamePath, $badFlame)
    Run-Build -ShouldSucceed $false | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'Wrong raven asset dimensions must not modify installed files.'
    [IO.File]::WriteAllBytes($flamePath, $flameBytes)

    $sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
    foreach ($case in @(@{ Kind = 'png'; Length = 24 }, @{ Kind = 'webp'; Length = 30 }, @{ Kind = 'wav'; Length = 44 })) {
        $asset = $ravenSpec.assets | Where-Object kind -eq $case.Kind | Select-Object -First 1
        $path = Join-Path $ravenSource ([string]$asset.source)
        $original = [IO.File]::ReadAllBytes($path)
        try {
            [IO.File]::WriteAllBytes($path, [byte[]]$original[0..($case.Length - 1)])
            $log = Run-Build -ShouldSucceed $false
            Assert-True ((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match 'Asset .* (PNG|RIFF)') "Truncated $($case.Kind) must fail at media validation."
            Assert-True ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -eq $sentinelHash) "Truncated $($case.Kind) must preserve the exact installed bytes."
        } finally { [IO.File]::WriteAllBytes($path, $original) }
    }
    $wavAsset = $ravenSpec.assets | Where-Object kind -eq 'wav' | Select-Object -First 1
    $wavPath = Join-Path $ravenSource ([string]$wavAsset.source)
    $wavBytes = [IO.File]::ReadAllBytes($wavPath)
    foreach ($case in @(@{ Offset = 40; Value = 1602; Name = 'data bound' }, @{ Offset = 28; Value = 1; Name = 'byte rate' }, @{ Offset = 24; Value = 0; Name = 'sample rate' }, @{ Offset = 16; Value = 8; Name = 'fmt bound' })) {
        try {
            $badWav = [byte[]]$wavBytes.Clone(); [BitConverter]::GetBytes([int]$case.Value).CopyTo($badWav, $case.Offset)
            [IO.File]::WriteAllBytes($wavPath, $badWav)
            Run-Build -ShouldSucceed $false | Out-Null
            Assert-True ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -eq $sentinelHash) "Invalid WAV $($case.Name) must preserve installed bytes."
        } finally { [IO.File]::WriteAllBytes($wavPath, $wavBytes) }
    }

    Run-Build -Extra @('-BuildOnly') | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'BuildOnly must leave installed files untouched.'
    $assetBuild = Get-Content -LiteralPath (Join-Path $fixtureRoot 'artifacts\last-build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $assetRun = Join-Path $fixtureRoot "artifacts\staging\$($assetBuild.run_id)"
    $stagedManifest = Get-Content -LiteralPath (Join-Path $assetRun 'assets\SurpriseRaven\SurpriseRaven\raven\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True (@($stagedManifest.assets).Count -eq @($ravenSpec.assets).Count) 'Every raven asset must be staged with a manifest entry.'
    Assert-True (Test-Path -LiteralPath (Join-Path $assetRun 'assets\SurpriseRaven\SurpriseRaven\raven\frontend.json')) 'Frontend config must be staged.'
    $packReport = Get-Content -LiteralPath (Join-Path $assetRun 'SurpriseRaven-pck-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True (@($packReport.entries | Where-Object { $_.path -eq 'res://SurpriseRaven/raven/textures/flame.png' }).Count -eq 1) 'PCK report must list staged raven media.'
    $env:SURPRISE_RAVEN_TEST_FAIL_BUILD = 'STS2MenuControl'
    Run-Build -Extra @('-IncludeControlMods') -ShouldSucceed $false | Out-Null
    Assert-True (Test-Path -LiteralPath $sentinel) 'Failure in the last control mod must not deploy the earlier successful builds.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'mods\STS2_MCP'))) 'No partial control-mod deployment is allowed.'
    $env:SURPRISE_RAVEN_TEST_FAIL_BUILD = $null

    Run-Build -Extra @('-IncludeControlMods') | Out-Null
    $deployedManifest = Get-Content -LiteralPath (Join-Path $installedMain 'SurpriseRaven.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($deployedManifest.dependencies[0].id -eq 'FutureDependency') 'Manifest dependencies must be preserved.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installedMain 'mod_manifest.json'))) 'PCK metadata must not be deployed as a second loose mod manifest.'
    $lastBuild = Get-Content -LiteralPath (Join-Path $fixtureRoot 'artifacts\last-build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $firstCommit = $lastBuild.source_commit
    $receipt = Get-ChildItem -LiteralPath (Join-Path $fixtureRoot '.git\sts2-deployments') -Filter '*.json' -File
    Assert-True ($lastBuild.source_clean -and @($receipt).Count -eq 1 -and
        (Get-Content -LiteralPath $receipt.FullName -Raw | ConvertFrom-Json).source_commit -eq $firstCommit) 'Successful deployment must record its source commit.'
    $metadataPath = Join-Path $fixtureRoot "artifacts\staging\$($lastBuild.run_id)\metadata\SurpriseRaven\mod_manifest.json"
    $pckManifest = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($pckManifest.name -eq $manifest.name) 'UTF-8 Chinese manifest text must survive Windows PowerShell and PCK metadata generation.'
    foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $gameRoot "mods\$id\$id.dll")) "Missing deployed mod $id"
    }
    Assert-True (Test-Path -LiteralPath (Join-Path $lastBuild.backup_directory 'SurpriseRaven\keep-existing.txt')) 'Existing installation must be backed up.'
    $controlConfig = Join-Path $gameRoot 'mods\STS2_MCP\STS2_MCP.conf'
    Set-Content -LiteralPath $controlConfig -Value 'port=15527' -Encoding UTF8
    $controlConfigHash = (Get-FileHash -LiteralPath $controlConfig).Hash
    $staleControlDll = Join-Path $gameRoot 'mods\STS2_MCP\obsolete.dll'
    Set-Content -LiteralPath $staleControlDll -Value 'old unmanaged binary'
    $oldDllHash = (Get-FileHash -LiteralPath (Join-Path $installedMain 'SurpriseRaven.dll')).Hash
    Run-Build -Extra @('-BuildOnly', '-IncludeControlMods') | Out-Null
    Assert-True ((Get-FileHash -LiteralPath $controlConfig).Hash -eq $controlConfigHash) 'BuildOnly must not change the installed STS2MCP configuration.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $installedMain 'SurpriseRaven.dll')).Hash -eq $oldDllHash) 'BuildOnly must not replace installed assemblies.'
    Set-Content -LiteralPath (Join-Path $dataRoot 'sts2.dll') -Value 'updated game reference'
    Run-Build -Extra @('-IncludeControlMods') | Out-Null
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $dataRoot 'sts2.dll')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'references\0.111.0\sts2.dll')).Hash) 'Game references must refresh within their version cache.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $installedMain 'SurpriseRaven.dll')).Hash -ne $oldDllHash) 'Repeated runs must actually build and deploy new output.'
    Assert-True ((Get-FileHash -LiteralPath $controlConfig).Hash -eq $controlConfigHash) 'Repeated deployment must preserve the exact STS2MCP configuration bytes.'
    Assert-True (-not (Test-Path -LiteralPath $staleControlDll)) 'Preserving a known config must not retain stale DLLs.'

    # Fail copying the final mod once; the script must roll all prior changes back.
    $beforeRollback = @{}
    foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
        $beforeRollback[$id] = (Get-FileHash -LiteralPath (Join-Path $gameRoot "mods\$id\$id.dll")).Hash
    }
    $rollbackInvoker = Join-Path $fixtureRoot 'invoke-rollback.ps1'
    @'
param([string]$GameDir)
$global:InjectCopyFailure = $true
$global:TestGameRoot = $GameDir
function Copy-Item {
    [CmdletBinding()]
    param([string]$LiteralPath, [string]$Destination, [switch]$Recurse, [switch]$Force)
    if ($global:InjectCopyFailure -and $Destination -eq (Join-Path $global:TestGameRoot 'mods\STS2MenuControl') -and $LiteralPath.Contains('\staging\')) {
        $global:InjectCopyFailure = $false
        throw 'Injected deployment copy failure'
    }
    Microsoft.PowerShell.Management\Copy-Item @PSBoundParameters
}
& (Join-Path $PSScriptRoot 'tools\build-mod.ps1') -GameDir $GameDir -IncludeControlMods
'@ | Set-Content -LiteralPath $rollbackInvoker -Encoding UTF8
    $rollbackLog = Run-Build -ShouldSucceed $false -Script $rollbackInvoker
    Assert-True ((Get-Content -LiteralPath $rollbackLog -Raw -Encoding UTF8).Contains('previous files were restored')) 'The injected failure must reach deployment and exercise rollback.'
    foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $gameRoot "mods\$id\$id.dll")).Hash -eq $beforeRollback[$id]) "Rollback must restore $id after deployment failure."
    }
    Assert-True ((Get-FileHash -LiteralPath $controlConfig).Hash -eq $controlConfigHash) 'Deployment rollback must also preserve STS2MCP configuration.'
    Assert-True ((Get-Content -LiteralPath $receipt.FullName -Raw | ConvertFrom-Json).files.'SurpriseRaven.dll' -eq $beforeRollback['SurpriseRaven']) 'Failed deployment must retain the old receipt.'

    # The same main Mod uses 111 references while helpers follow the selected host.
    $baselineRoot = Join-Path $testRoot 'Baseline 111 With Spaces'
    New-Item -ItemType Directory -Path $baselineRoot -Force | Out-Null
    Copy-Item -LiteralPath $dataRoot -Destination (Join-Path $baselineRoot 'data_sts2_windows_x86_64') -Recurse
    Write-TestJson (Join-Path $baselineRoot 'release_info.json') @{ version = 'v0.111.0' }
    $baselineHash = (Get-FileHash -LiteralPath (Join-Path $baselineRoot 'data_sts2_windows_x86_64\sts2.dll')).Hash
    Write-TestJson (Join-Path $gameRoot 'release_info.json') @{ version = 'v0.113.1' }
    Set-Content -LiteralPath (Join-Path $dataRoot 'sts2.dll') -Value '113 game reference'
    $missingBaselineLog = Run-Build -Extra @('-BuildOnly') -ShouldSucceed $false
    Assert-True ((Get-Content -LiteralPath $missingBaselineLog -Raw) -match 'compile baseline is required') '113 must not silently compile the main Mod against current references.'
    Run-Build -Extra @('-BuildOnly', '-BaselineGameDir', $gameRoot) -ShouldSucceed $false | Out-Null
    Add-Content -LiteralPath (Join-Path $fixtureRoot '.env') -Value "BASELINE_GAME_DIR=$baselineRoot" -Encoding UTF8
    Run-Build -Extra @('-BuildOnly', '-IncludeControlMods') | Out-Null
    $dualBuild = Get-Content -LiteralPath (Join-Path $fixtureRoot 'artifacts\last-build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($dualBuild.game_version -eq '0.113.1' -and $dualBuild.api_target -eq '0.113.0') 'Release and API versions must be mapped separately.'
    Assert-True ($dualBuild.baseline_game_version -eq '0.111.0' -and $dualBuild.baseline_reference_sha256.'sts2.dll' -eq $baselineHash) 'Report must retain the exact 111 compile baseline.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'references\0.111.0\sts2.dll')).Hash -eq $baselineHash) '113 reference refresh must preserve the 111 baseline cache.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'references\0.113.0\sts2.dll')).Hash -eq $dualBuild.reference_sha256.'sts2.dll') 'Target references must have their own cache.'
    foreach ($payload in $dualBuild.payloads) {
        $expectedApi = if ($payload.Id -eq 'SurpriseRaven') { '0.111.0' } else { '0.113.0' }
        $argumentsFile = Join-Path $fixtureRoot "artifacts\staging\$($dualBuild.run_id)\build\0.113.0\$($payload.Id)\build-arguments.txt"
        $actualArguments = @(Get-Content -LiteralPath $argumentsFile)
        Assert-True ($payload.CompileApiTarget -eq $expectedApi -and $actualArguments -contains "-p:Sts2ApiTarget=$expectedApi") "Wrong compile API for $($payload.Id)."
        Assert-True (@($actualArguments | Where-Object { $_ -like '-p:BaseIntermediateOutputPath=*/obj/0.113.0/*/' }).Count -eq 1) "Intermediate output must be isolated for $($payload.Id)."
        if ($payload.Id -eq 'SurpriseRaven') {
            Assert-True ($actualArguments -contains '-p:RestoreLockedMode=true') 'Normal main builds must retain locked restore.'
            Assert-True ($actualArguments -contains "-p:STS2GameDataDir=$($dualBuild.baseline_reference_directory)") 'Main build must receive only baseline game references.'
        }
    }
    Write-TestJson (Join-Path $gameRoot 'release_info.json') @{ version = 'v0.114.0' }
    $unknownLog = Run-Build -Extra @('-BuildOnly') -ShouldSucceed $false
    Assert-True ((Get-Content -LiteralPath $unknownLog -Raw) -match 'Unsupported game version') 'Unknown releases must fail closed.'
    foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $gameRoot "mods\$id\$id.dll")).Hash -eq $beforeRollback[$id]) "Compatibility validation must leave deployed $id intact."
    }
    Write-TestJson (Join-Path $gameRoot 'release_info.json') @{ version = 'v0.113.1' }
    & git -C $fixtureRoot -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty --quiet -m next
    Assert-True ($LASTEXITCODE -eq 0) 'Could not commit the successor fixture.'
    Run-Build | Out-Null
    $successor = Get-Content -LiteralPath $receipt.FullName -Raw | ConvertFrom-Json
    Assert-True ($successor.source_commit -ne $firstCommit) 'A succeeding source commit must update the receipt.'
    & git -C $fixtureRoot checkout --quiet --detach $firstCommit
    Assert-True ($LASTEXITCODE -eq 0) 'Could not select the stale fixture.'
    $staleLog = Run-Build -ShouldSucceed $false
    Assert-True ((Get-Content -LiteralPath $staleLog -Raw) -match 'is missing deployed commit') 'A stale checkout must fail the shared deployment guard.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $installedMain 'SurpriseRaven.dll')).Hash -eq $successor.files.'SurpriseRaven.dll') 'Stale builds must leave the current installation intact.'
    Write-Host 'PASS: dual-version baseline/target mapping, isolated references and intermediate output, unknown-release rejection, locked restore, native build/PCK failures, asset validation, deployment/config preservation and rollback.'
    Write-Host "Test fixtures and logs retained at: $testRoot"
} finally {
    $env:SURPRISE_RAVEN_TEST_FAIL_BUILD = $oldFailBuild
    $env:SURPRISE_RAVEN_TEST_FAIL_PCK = $oldFailPck
    $env:SURPRISE_RAVEN_TEST_COUNTER = $oldCounter
}
