[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$GameDir = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$BuildOnly,
    [switch]$IncludeControlMods,
    [string]$RitsuLibDir = '',
    [string]$RavenAssetsDir = '',
    [string]$BaselineGameDir = '',
    [string]$VerifiedBuildReport = '',
    [string]$VerificationIndex = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $artifactsRoot "staging\$runId"
$previousDotnetRoot = $env:DOTNET_ROOT
$previousPath = $env:PATH
. (Join-Path $PSScriptRoot 'game-compatibility.ps1')
. (Join-Path $PSScriptRoot 'deployment-provenance.ps1')

function Assert-ChildPath([string]$Path, [string]$Root) {
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not $fullPath.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing path outside the managed directory: $fullPath (root: $fullRoot)"
    }
    $cursor = $fullPath
    while ($cursor.Length -ge $fullRoot.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing to modify a reparse point: $cursor"
            }
        }
        if ($cursor -eq $fullRoot) { break }
        $cursor = Split-Path -Parent $cursor
    }
    return $fullPath
}

function Remove-ManagedDirectory([string]$Path, [string]$Root) {
    $checkedPath = Assert-ChildPath $Path $Root
    if (Test-Path -LiteralPath $checkedPath) {
        Assert-NoReparseTree $checkedPath
        Remove-Item -LiteralPath $checkedPath -Recurse -Force
    }
}

function Assert-NoReparseTree([string]$Path) {
    # Enumerate one directory at a time, checking links before entering them.
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $item = Get-Item -LiteralPath $pending.Pop() -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing a managed directory containing a reparse point: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            foreach ($child in @(Get-ChildItem -LiteralPath $item.FullName -Force)) { $pending.Push($child.FullName) }
        }
    }
}

function Write-JsonFile([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Assert-GameStopped {
    if (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue) {
        throw 'The game is running. Close it before deployment, or use -BuildOnly.'
    }
}

function Invoke-LoggedCommand([string]$Command, [string[]]$Arguments, [string]$LogPath) {
    Write-Host ('> ' + $Command + ' ' + ($Arguments -join ' '))
    $savedErrorPreference = $ErrorActionPreference
    try {
        # Native stderr alone is not failure; preserve it and check the exit code.
        $ErrorActionPreference = 'Continue'
        & $Command @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $LogPath | Out-Host
        $commandExitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedErrorPreference }
    if ($commandExitCode -ne 0) { throw "Command failed with exit code $commandExitCode. See $LogPath" }
}

function Apply-CompatibilityPatches([string]$Repository, [string]$Name) {
    $patchDir = Join-Path $projectRoot "patches\$Name"
    if (-not (Test-Path -LiteralPath $patchDir)) { return }
    foreach ($patch in @(Get-ChildItem -LiteralPath $patchDir -Filter '*.patch' -File | Sort-Object Name)) {
        $savedErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & git -C $Repository apply --reverse --check $patch.FullName 2>$null
            $alreadyApplied = $LASTEXITCODE -eq 0
        } finally { $ErrorActionPreference = $savedErrorPreference }
        if ($alreadyApplied) {
            Write-Host "Compatibility patch already applied: $Name/$($patch.Name)"
            continue
        }
        Invoke-LoggedCommand 'git' @('-C', $Repository, 'apply', '--check', $patch.FullName) (Join-Path $runRoot "$Name-patch-check.log")
        Invoke-LoggedCommand 'git' @('-C', $Repository, 'apply', $patch.FullName) (Join-Path $runRoot "$Name-patch-apply.log")
    }
}

function Read-ModSpec([string]$Project, [string]$Manifest, [bool]$HasPck) {
    if (-not (Test-Path -LiteralPath $Project -PathType Leaf)) { throw "Missing project: $Project" }
    if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) { throw "Missing manifest: $Manifest" }
    $xml = [xml](Get-Content -LiteralPath $Project -Raw -Encoding UTF8)
    $targetNode = $xml.SelectSingleNode('/Project/PropertyGroup/TargetFramework')
    if (-not $targetNode -or $targetNode.InnerText -ne 'net9.0') { throw "Project must target net9.0 to match the game: $Project" }
    $json = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $id = [string]$json.id
    if ($id -notmatch '^[A-Za-z_][A-Za-z0-9_.-]*$') { throw "Invalid mod ID: $id" }
    $assemblyNode = $xml.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
    $assemblyName = if ($assemblyNode) { $assemblyNode.InnerText } else { [IO.Path]::GetFileNameWithoutExtension($Project) }
    if ($id -ne $assemblyName) { throw "Manifest ID '$id' must match assembly name '$assemblyName'." }
    if ($json.has_dll -ne $true -or $json.has_pck -ne $HasPck) { throw "Incorrect has_dll/has_pck for $id" }
    if ($HasPck -and $json.pck_name -ne $id) { throw "pck_name must match the mod ID: $id" }
    return [pscustomobject]@{ Id = $id; Project = $Project; Manifest = $Manifest; Metadata = $json; HasPck = $HasPck }
}

function Assert-PckReport([string]$AssetRoot, [string]$ReportPath) {
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) { throw "PCK report missing: $ReportPath" }
    $report = Get-Content -LiteralPath $ReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $packed = @{}
    foreach ($entry in @($report.entries)) { $packed[[string]$entry.path] = [long]$entry.size }
    if (-not $packed.ContainsKey('res://mod_manifest.json')) { throw 'PCK report is missing res://mod_manifest.json.' }
    $files = @(Get-ChildItem -LiteralPath $AssetRoot -File -Recurse)
    foreach ($file in $files) {
        $relative = 'res://' + $file.FullName.Substring($AssetRoot.TrimEnd('\').Length + 1).Replace('\', '/')
        if (-not $packed.ContainsKey($relative) -or $packed[$relative] -ne $file.Length) { throw "PCK is missing or truncated: $relative" }
    }
    if ($packed.Count -ne $files.Count + 1) { throw "PCK entry count $($packed.Count) differs from staged assets $($files.Count + 1)." }
    return $files.Count
}

function Update-GameReferenceCache($Game) {
    $referenceDir = Assert-ChildPath (Join-Path $projectRoot "references\$($Game.ApiTarget)") $projectRoot
    if (Test-Path -LiteralPath $referenceDir) { Assert-NoReparseTree $referenceDir }
    New-Item -ItemType Directory -Path $referenceDir -Force | Out-Null
    foreach ($assembly in $Game.ReferenceHashes.Keys) {
        $destination = Join-Path $referenceDir $assembly
        if (-not (Test-Path -LiteralPath $destination) -or
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $Game.ReferenceHashes[$assembly]) {
            Copy-Item -LiteralPath (Join-Path $Game.DataDirectory $assembly) -Destination $destination -Force
            Write-Host "Refreshed game reference: $($Game.ApiTarget)/$assembly"
        }
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $Game.ReferenceHashes[$assembly]) {
            throw "Game reference changed during copy: $assembly. Retry after the game update has finished."
        }
    }
    return $referenceDir
}

function Install-StagedPayload($Result, $MainSpec) {
    $payloads = $Result.payloads
    $receiptPath = Get-DeploymentReceiptPath $projectRoot $GameDir $MainSpec.Id
    try { $lease = [IO.File]::Open("$receiptPath.lock", 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] { throw "Another deployment holds the lock for $GameDir." }
    try {
        Assert-GameStopped
        Assert-DeploymentOrigin $projectRoot $Result $receiptPath (Join-Path $GameDir "mods\$($MainSpec.Id)")
        foreach ($payload in $payloads) {
            Assert-ExactHashes $payload.Directory $payload.Files
        }
        if (@($MainSpec.Metadata.dependencies | Where-Object { $_.id -eq 'STS2-RitsuLib' }).Count -gt 0) {
            New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
            $checkArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $projectRoot 'tools\verify-ritsulib.ps1'),
                '-GameDir', $GameDir, '-PayloadDir', ($payloads | Where-Object Id -eq $mainSpec.Id).Directory,
                '-OutputPath', (Join-Path $runRoot 'ritsulib-runtime.json'))
            if ($RitsuLibDir) { $checkArgs += @('-RitsuLibDir', $RitsuLibDir) }
            Invoke-LoggedCommand 'powershell.exe' $checkArgs (Join-Path $runRoot 'ritsulib-runtime.log')
        }
        $modsRoot = Join-Path $GameDir 'mods'
        if ($MainSpec.Id -eq 'SurpriseRaven') {
            foreach ($legacyName in @('Sts2ModScaffold', 'Sts2ModScaffold.json', 'Sts2ModScaffold.dll', 'Sts2ModScaffold.pck')) {
                if (Test-Path -LiteralPath (Join-Path $modsRoot $legacyName)) {
                    throw 'The retired Sts2ModScaffold is still installed. Back up that Mod outside mods before upgrading to SurpriseRaven.'
                }
            }
        }
        $backupRoot = Join-Path $artifactsRoot "backups\$runId"
        $records = @()
        foreach ($payload in $payloads) {
            $target = Assert-ChildPath (Join-Path $modsRoot $payload.Id) $modsRoot
            foreach ($extension in @('.dll', '.json', '.pck')) {
                if (Test-Path -LiteralPath (Join-Path $modsRoot ($payload.Id + $extension))) {
                    throw "Legacy flat mod files exist for $($payload.Id). Move that installation aside before deploying a second copy."
                }
            }
            $backup = Assert-ChildPath (Join-Path $backupRoot $payload.Id) $artifactsRoot
            $records += [pscustomobject]@{ Id = $payload.Id; Target = $target; Source = $payload.Directory; Backup = $backup; HadExisting = (Test-Path -LiteralPath $target); Touched = $false }
        }
        # Complete every backup before touching any installed mod.
        foreach ($record in $records) {
            if ($record.HadExisting) {
                Assert-NoReparseTree $record.Target
                New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
                Copy-Item -LiteralPath $record.Target -Destination $record.Backup -Recurse -Force
                Assert-ExactHashes $record.Backup (Get-PayloadHashes $record.Target)
            }
        }
        # STS2MCP writes this user-editable configuration alongside its DLL.
        # Preserve only this known sidecar; never carry stale assemblies forward.
        foreach ($record in $records) {
            if ($record.Id -eq 'STS2_MCP' -and $record.HadExisting) {
                $configBackup = Join-Path $record.Backup 'STS2_MCP.conf'
                if (Test-Path -LiteralPath $configBackup) {
                    Assert-ChildPath $configBackup $backupRoot | Out-Null
                    Assert-NoReparseTree $configBackup
                    if (-not (Test-Path -LiteralPath $configBackup -PathType Leaf)) {
                        throw 'STS2_MCP.conf must be a regular configuration file.'
                    }
                    Copy-Item -LiteralPath $configBackup -Destination (Join-Path $record.Source 'STS2_MCP.conf') -Force
                }
            }
        }
        try {
            New-Item -ItemType Directory -Path $modsRoot -Force | Out-Null
            foreach ($record in $records) {
                Assert-GameStopped
                $record.Touched = $true
                Remove-ManagedDirectory $record.Target $modsRoot
                Copy-Item -LiteralPath $record.Source -Destination $record.Target -Recurse -Force
                $expected = if ($record.Id -eq $MainSpec.Id) {
                    ($payloads | Where-Object Id -eq $MainSpec.Id).Files
                } else { Get-PayloadHashes $record.Source }
                Assert-ExactHashes $record.Target $expected
                Write-Host "Deployed $($record.Id) to $($record.Target)"
            }
            $mainRecord = $records | Where-Object Id -eq $MainSpec.Id
            Write-DeploymentReceipt $receiptPath ([ordered]@{
                source_commit = $Result.source_commit
                files = Get-PayloadHashes $mainRecord.Target
                game_dir = $GameDir
                mod_id = $MainSpec.Id
                version = $MainSpec.Metadata.version
            })
        } catch {
            $deploymentError = $_
            $rollbackErrors = @()
            foreach ($record in @($records | Where-Object Touched)) {
                try {
                    Assert-GameStopped
                    Remove-ManagedDirectory $record.Target $modsRoot
                    if ($record.HadExisting) {
                        Copy-Item -LiteralPath $record.Backup -Destination $record.Target -Recurse -Force
                        Assert-ExactHashes $record.Target (Get-PayloadHashes $record.Backup)
                    }
                } catch { $rollbackErrors += $_.Exception.Message }
            }
            if ($rollbackErrors.Count -gt 0) { throw "Deployment failed: $deploymentError. Rollback also failed: $($rollbackErrors -join '; '). Backups: $backupRoot" }
            throw "Deployment failed and previous files were restored: $deploymentError"
        }
        $result.deployed = $true
        $result['backup_directory'] = $backupRoot
        Write-JsonFile (Join-Path $artifactsRoot "deployment-$runId.json") $result
    } finally { $lease.Dispose() }
}

try {
    $envFile = Join-Path $projectRoot '.env'
    if (Test-Path -LiteralPath $envFile) {
        foreach ($line in Get-Content -LiteralPath $envFile -Encoding UTF8) {
            if (-not $GameDir -and $line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') }
            if (-not $BaselineGameDir -and $line -match '^\s*BASELINE_GAME_DIR\s*=(.*)$') { $BaselineGameDir = $Matches[1].Trim().Trim('"') }
            if (-not $RavenAssetsDir -and $line -match '^\s*RAVEN_ASSETS_DIR\s*=(.*)$') { $RavenAssetsDir = $Matches[1].Trim().Trim('"') }
        }
    }
    # Third-party raven media stays local (ignored artifacts/); only the selection spec is tracked.
    if (-not $RavenAssetsDir) { $RavenAssetsDir = Join-Path $projectRoot 'artifacts\artifact' }
    if (-not [IO.Path]::IsPathRooted($RavenAssetsDir)) { $RavenAssetsDir = Join-Path $projectRoot $RavenAssetsDir }
    $RavenAssetsDir = [IO.Path]::GetFullPath($RavenAssetsDir).TrimEnd('\')
    if ($VerifiedBuildReport -or $VerificationIndex) {
        if (-not $VerifiedBuildReport -or -not $VerificationIndex -or $BuildOnly -or $IncludeControlMods) {
            throw 'Provide both -VerifiedBuildReport and -VerificationIndex for a verified main Mod deployment.'
        }
        $verified = Get-Content -LiteralPath $VerifiedBuildReport -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($GameDir -and [IO.Path]::GetFullPath($GameDir).TrimEnd('\', '/') -ne $verified.game_dir) {
            throw 'Configured game directory differs from the verified build target.'
        }
        $GameDir = $verified.game_dir
    }
    if (-not $GameDir) { throw 'Game directory is required. Pass -GameDir or set GAME_DIR in the local .env file.' }
    $targetGame = Read-SupportedGame $GameDir
    $baselineGame = Resolve-BaselineGame $BaselineGameDir $targetGame
    $GameDir = $targetGame.Directory
    $BaselineGameDir = $baselineGame.Directory
    if (-not $BuildOnly) { Assert-GameStopped }

    $dotnetRoot = Join-Path $projectRoot 'tools\dotnet'
    $dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
    $godotExe = Join-Path $projectRoot 'tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    foreach ($toolPath in @($dotnetExe, $godotExe)) {
        if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) { throw "Missing local tool: $toolPath. Run install.bat first." }
    }
    $projects = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.csproj' -File)
    $manifests = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.json' -File | Where-Object Name -ne 'packages.lock.json')
    if ($projects.Count -ne 1 -or $manifests.Count -ne 1) { throw 'Expected exactly one main project and one source manifest in src/.' }
    $mainSpec = Read-ModSpec $projects[0].FullName $manifests[0].FullName $true
    $entry = Get-Content -LiteralPath (Join-Path $projectRoot 'src\ModEntry.cs') -Raw -Encoding UTF8
    $entryId = [regex]::Match($entry, '\bModId\s*=\s*"([^"]+)"').Groups[1].Value
    if ($entryId -ne $mainSpec.Id) { throw "ModEntry.ModId '$entryId' does not match '$($mainSpec.Id)'." }

    if ($VerifiedBuildReport) {
        Assert-VerifiedBuild $verified $VerificationIndex $targetGame $mainSpec.Id $artifactsRoot $mainSpec.Metadata.version
        $result = [ordered]@{}
        foreach ($property in $verified.PSObject.Properties) { $result[$property.Name] = $property.Value }
        Install-StagedPayload $result $mainSpec
        return
    }

    $sourceIdentity = Get-SourceIdentity $projectRoot
    if (-not $BuildOnly -and -not $sourceIdentity.Clean) { throw 'Commit source changes before deployment, or use -BuildOnly.' }
    Assert-ChildPath $runRoot $artifactsRoot | Out-Null
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $specs = @($mainSpec)
    if ($IncludeControlMods) {
        foreach ($control in @(
            @{ Name = 'STS2MCP'; Project = 'STS2_MCP.csproj' },
            @{ Name = 'STS2MenuControl'; Project = 'STS2MenuControl.csproj' }
        )) {
            $source = Join-Path $projectRoot "tools\$($control.Name)"
            Apply-CompatibilityPatches $source $control.Name
            $specs += Read-ModSpec (Join-Path $source $control.Project) (Join-Path $source 'mod_manifest.json') $false
        }
    }

    $baselineReferenceDir = Update-GameReferenceCache $baselineGame
    $targetReferenceDir = Update-GameReferenceCache $targetGame
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = $dotnetRoot + ';' + $previousPath
    $payloads = @()
    Push-Location $projectRoot
    try {
        Invoke-LoggedCommand $dotnetExe @('run', '--project', (Join-Path $PSScriptRoot 'gameplay-odds\CheckGameplayOdds.csproj'),
            '--configuration', 'Release', '--', (Join-Path $projectRoot 'src\Config\gameplay-odds.json')) (Join-Path $runRoot 'gameplay-odds.log')
        foreach ($spec in $specs) {
            $compileApi = if ($spec.Id -eq $mainSpec.Id) { $baselineGame.ApiTarget } else { $targetGame.ApiTarget }
            $compileGame = if ($spec.Id -eq $mainSpec.Id) { $baselineGame.Directory } else { $GameDir }
            $compileReferences = if ($spec.Id -eq $mainSpec.Id) { $baselineReferenceDir } else { $targetReferenceDir }
            $output = Join-Path $runRoot "build\$($targetGame.ApiTarget)\$($spec.Id)"
            # A trailing backslash escapes the closing native-argument quote in
            # Windows PowerShell 5.1 when the checkout path contains spaces.
            $intermediate = (Join-Path $runRoot "obj\$($targetGame.ApiTarget)\$($spec.Id)").Replace('\', '/') + '/'
            New-Item -ItemType Directory -Path $output -Force | Out-Null
            $buildArgs = @('build', $spec.Project, '--configuration', $Configuration, '--output', $output, '--nologo',
                "-p:STS2GameDir=$compileGame", "-p:GameDir=$GameDir", "-p:Sts2ApiTarget=$compileApi",
                "-p:BaseIntermediateOutputPath=$intermediate", "-p:STS2GameDataDir=$compileReferences")
            if ($spec.Id -eq $mainSpec.Id) {
                $commitLabel = if ($sourceIdentity.Clean) { $sourceIdentity.Commit } else { "$($sourceIdentity.Commit)-dirty" }
                $buildArgs += @('-p:RestoreLockedMode=true', "-p:RavenSourceCommit=$commitLabel")
            }
            Invoke-LoggedCommand $dotnetExe $buildArgs (Join-Path $runRoot "$($spec.Id)-build.log")
            $unexpectedDlls = @(Get-ChildItem -LiteralPath $output -Filter '*.dll' -File | Where-Object {
                $_.BaseName -ne $spec.Id -and $_.BaseName -match '^(STS2-RitsuLib(?:\..*)?|GodotSharp|0Harmony|sts2)$'
            })
            if ($unexpectedDlls.Count -gt 0) { throw "Dependency DLLs must not be copied into Mod output: $($unexpectedDlls.Name -join ', ')" }
            $dll = Join-Path $output "$($spec.Id).dll"
            if (-not (Test-Path -LiteralPath $dll) -or (Get-Item -LiteralPath $dll).Length -eq 0) { throw "Build did not produce $dll" }
            $payloadDir = Join-Path $runRoot "mods\$($spec.Id)"
            New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
            Copy-Item -LiteralPath $dll -Destination $payloadDir
            Copy-Item -LiteralPath $spec.Manifest -Destination (Join-Path $payloadDir "$($spec.Id).json")
            if ($spec.HasPck) {
                $pckManifest = [ordered]@{}
                foreach ($key in @('pck_name', 'name', 'author', 'description', 'version')) { $pckManifest[$key] = $spec.Metadata.$key }
                # This metadata belongs inside the PCK. A loose mod_manifest.json
                # under mods/ is also scanned as a mod and has no loader ID.
                $metadataDir = Join-Path $runRoot "metadata\$($spec.Id)"
                New-Item -ItemType Directory -Path $metadataDir -Force | Out-Null
                $pckManifestPath = Join-Path $metadataDir 'mod_manifest.json'
                Write-JsonFile $pckManifestPath $pckManifest
                $assetRoot = Join-Path $runRoot "assets\$($spec.Id)"
                $stageArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $projectRoot 'tools\stage-raven-assets.ps1'),
                    '-SourceDir', $RavenAssetsDir, '-SpecPath', (Join-Path $projectRoot 'assets\raven\raven-assets.json'),
                    '-ConfigPath', (Join-Path $projectRoot 'assets\raven\raven-frontend.json'), '-OutputDir', $assetRoot,
                    '-PackPrefix', "$($spec.Id)/raven")
                Invoke-LoggedCommand 'powershell.exe' $stageArgs (Join-Path $runRoot "$($spec.Id)-assets.log")
                $pckPath = Join-Path $payloadDir "$($spec.Id).pck"
                $pckReport = Join-Path $runRoot "$($spec.Id)-pck-report.json"
                $pckBuilder = Join-Path $projectRoot 'tools\pck_builder'
                Invoke-LoggedCommand $godotExe @('--headless', '--path', $pckBuilder, '--script', (Join-Path $pckBuilder 'build_pck.gd'), '--', $pckManifestPath, $assetRoot, $pckReport, $pckPath) (Join-Path $runRoot "$($spec.Id)-pck.log")
                if (-not (Test-Path -LiteralPath $pckPath) -or (Get-Item -LiteralPath $pckPath).Length -lt 32) { throw "PCK output missing or too short: $pckPath" }
                $pckHeader = [IO.File]::ReadAllBytes($pckPath)
                if ([Text.Encoding]::ASCII.GetString($pckHeader, 0, 4) -ne 'GDPC') { throw "Invalid PCK header: $pckPath" }
                $packedAssets = Assert-PckReport $assetRoot $pckReport
                Write-Host "PCK verified: $packedAssets raven files plus manifest."
            }
            $payloads += [pscustomobject]@{ Id = $spec.Id; Directory = $payloadDir; CompileApiTarget = $compileApi; Files = Get-PayloadHashes $payloadDir }
        }
    } finally { Pop-Location }

    $finalSource = Get-SourceIdentity $projectRoot
    if ($finalSource.Commit -ne $sourceIdentity.Commit) { throw 'Source commit changed during build. Rebuild with a stable checkout.' }
    $result = [ordered]@{
        source_commit = $sourceIdentity.Commit
        source_clean = ($sourceIdentity.Clean -and $finalSource.Clean)
        run_id = $runId
        game_dir = $GameDir
        game_version = $targetGame.Version
        api_target = $targetGame.ApiTarget
        baseline_game_dir = $BaselineGameDir
        baseline_game_version = $baselineGame.Version
        baseline_api_target = $baselineGame.ApiTarget
        baseline_reference_directory = $baselineReferenceDir
        baseline_reference_sha256 = $baselineGame.ReferenceHashes
        target_reference_directory = $targetReferenceDir
        target_framework = 'net9.0'
        configuration = $Configuration
        reference_sha256 = $targetGame.ReferenceHashes
        payloads = $payloads
        deployed = $false
    }
    Write-JsonFile (Join-Path $runRoot 'build-result.json') $result
    if (-not $BuildOnly) {
        Install-StagedPayload $result $mainSpec
        Write-JsonFile (Join-Path $runRoot 'build-result.json') $result
    }
    Write-JsonFile (Join-Path $artifactsRoot 'last-build.json') $result
    Write-Host "Build successful. Logs and artifacts: $runRoot"
    if ($BuildOnly) { Write-Host 'BuildOnly: no files were deployed to the game.' }
} catch {
    [Console]::Error.WriteLine('BUILD FAILED: ' + $_.Exception.Message)
    throw
} finally {
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:PATH = $previousPath
}
