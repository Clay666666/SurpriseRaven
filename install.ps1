[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$GameDir = '',
    [Parameter(Position = 1)][ValidateSet('N', 'Y')][string]$DeepVerify = 'N',
    [switch]$NoDeploy,
    [string]$PythonExe = '',
    [string]$RitsuLibDir = '',
    [string]$BaselineGameDir = ''
)
# Private tools only: no machine PATH changes, log deletion, global process kills,
# or automatic removal of the installed development Mod.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProjectDir = [IO.Path]::GetFullPath($PSScriptRoot)
$ToolsDir = Join-Path $ProjectDir 'tools'
$DownloadsDir = Join-Path $ProjectDir 'artifacts\downloads'
$LockPath = Join-Path $ToolsDir 'toolchain.lock.json'
. (Join-Path $ToolsDir 'game-compatibility.ps1')
$SavedEnvironment = @{}
$EnvironmentKeys = @('PATH', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_CLI_HOME',
    'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'UV_PYTHON_DOWNLOADS', 'UV_CACHE_DIR', 'UV_LINK_MODE')
foreach ($key in $EnvironmentKeys) {
    $SavedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Assert-ProjectPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootPrefix = $ProjectDir.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside this checkout: $fullPath"
    }
    return $fullPath
}

function Get-Download($Package) {
    $fileName = [IO.Path]::GetFileName(([uri]$Package.url).AbsolutePath)
    $archivePath = Assert-ProjectPath (Join-Path $DownloadsDir $fileName)
    if (Test-Path -LiteralPath $archivePath) {
        if ((Get-FileHash -LiteralPath $archivePath -Algorithm $Package.algorithm).Hash -ieq $Package.hash) {
            Write-Host "Using verified download: $fileName"
            return $archivePath
        }
        throw "Cached download has the wrong hash: $archivePath. Move it aside and rerun."
    }
    $partialPath = Assert-ProjectPath ($archivePath + '.partial')
    Write-Host "Downloading $($Package.id) $($Package.version)..."
    try {
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            Invoke-Checked $curl.Source @('--fail', '--location', '--retry', '3', '--connect-timeout', '30', '--output', $partialPath, $Package.url)
        } else {
            $oldProgressPreference = $ProgressPreference
            try {
                $ProgressPreference = 'SilentlyContinue'
                Invoke-WebRequest -Uri $Package.url -OutFile $partialPath -UseBasicParsing
            } finally { $ProgressPreference = $oldProgressPreference }
        }
        if ((Get-FileHash -LiteralPath $partialPath -Algorithm $Package.algorithm).Hash -ine $Package.hash) {
            throw "Downloaded $fileName failed $($Package.algorithm) verification."
        }
        Move-Item -LiteralPath $partialPath -Destination $archivePath
    } catch {
        if (Test-Path -LiteralPath $partialPath) { Remove-Item -LiteralPath $partialPath -Force }
        throw
    }
    return $archivePath
}

function Expand-ToolArchive([string]$ArchivePath, $Package) {
    $destination = Assert-ProjectPath (Join-Path $ProjectDir $Package.destination)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $destinationPrefix = $destination.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $includePrefixes = @()
    if ($Package.PSObject.Properties['include_prefixes']) { $includePrefixes = @($Package.include_prefixes) }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $relativePath = $entry.FullName.Replace('\', '/')
            if ($includePrefixes.Count -gt 0) {
                $include = $false
                foreach ($prefix in $includePrefixes) {
                    if ($relativePath.StartsWith($prefix, [StringComparison]::Ordinal)) { $include = $true }
                }
                if (-not $include) { continue }
            }
            if ($Package.archive_prefix) {
                if (-not $relativePath.StartsWith($Package.archive_prefix, [StringComparison]::Ordinal)) { continue }
                $relativePath = $relativePath.Substring($Package.archive_prefix.Length)
            }
            if ([string]::IsNullOrWhiteSpace($relativePath)) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $destination $relativePath))
            if (-not $target.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Archive entry escapes its tool directory: $($entry.FullName)"
            }
            if ($relativePath.EndsWith('/')) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
            } else {
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        }
    } finally { $archive.Dispose() }
    if (-not (Test-Path -LiteralPath (Join-Path $destination $Package.marker))) {
        throw "Expected tool file missing after extracting $($Package.id)."
    }
}

function Apply-ToolPatches([string]$Repository, [string]$Name) {
    $patchDirectory = Join-Path $ProjectDir "patches\$Name"
    if (-not (Test-Path -LiteralPath $patchDirectory)) { return }
    foreach ($patch in @(Get-ChildItem -LiteralPath $patchDirectory -Filter '*.patch' -File | Sort-Object Name)) {
        $savedErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & git -C $Repository apply --reverse --check $patch.FullName 2>$null
            $alreadyApplied = $LASTEXITCODE -eq 0
        } finally { $ErrorActionPreference = $savedErrorPreference }
        if ($alreadyApplied) {
            Write-Host "Tool compatibility patch already applied: $Name/$($patch.Name)"
            continue
        }
        Invoke-Checked 'git' @('-C', $Repository, 'apply', '--check', $patch.FullName)
        Invoke-Checked 'git' @('-C', $Repository, 'apply', $patch.FullName)
    }
}

function Ensure-Repository($Repository) {
    $repositoryPath = Assert-ProjectPath (Join-Path $ToolsDir $Repository.id)
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryPath '.git'))) {
        if ((Test-Path -LiteralPath $repositoryPath) -and @(Get-ChildItem -LiteralPath $repositoryPath -Force).Count -gt 0) {
            throw "Existing non-Git directory will not be overwritten: $repositoryPath"
        }
        Invoke-Checked 'git' @('clone', '--no-checkout', $Repository.url, $repositoryPath)
        Invoke-Checked 'git' @('-C', $repositoryPath, 'checkout', '--detach', $Repository.commit)
    }
    $commit = & git -C $repositoryPath rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw "Cannot read repository HEAD: $repositoryPath" }
    if ($commit.Trim() -ne $Repository.commit) {
        throw "$($Repository.id) is not at the locked commit. Existing work is preserved; reconcile it before rerunning."
    }
    $origin = & git -C $repositoryPath remote get-url origin
    if ($LASTEXITCODE -ne 0 -or (($origin.Trim() -replace '\.git$', '') -ne ($Repository.url -replace '\.git$', ''))) {
        throw "Unexpected source remote for $($Repository.id)."
    }
    Write-Host "$($Repository.id): $($Repository.commit)"
}

try {
    if ($NoDeploy -and $DeepVerify -eq 'Y') { throw '-NoDeploy cannot be combined with -DeepVerify Y.' }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required on PATH.' }
    if (-not (Test-Path -LiteralPath $LockPath)) { throw "Missing tool version lock: $LockPath" }
    $lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
    $envFile = Join-Path $ProjectDir '.env'
    if (Test-Path -LiteralPath $envFile) {
        foreach ($line in Get-Content -LiteralPath $envFile) {
            if (-not $GameDir -and $line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') }
            if (-not $BaselineGameDir -and $line -match '^\s*BASELINE_GAME_DIR\s*=(.*)$') { $BaselineGameDir = $Matches[1].Trim().Trim('"') }
        }
    }
    if (-not $GameDir) { throw 'Game directory is required. Pass -GameDir or set GAME_DIR in the local .env file.' }
    $targetGame = Read-SupportedGame $GameDir
    $baselineGame = Resolve-BaselineGame $BaselineGameDir $targetGame
    $GameDir = $targetGame.Directory
    $BaselineGameDir = $baselineGame.Directory
    if (-not $NoDeploy -and (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue)) {
        throw 'Close the running game before deployment, or use -NoDeploy. No game process was stopped.'
    }
    if (-not $PythonExe) {
        $pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
        if (-not $pythonCommand) { throw 'No existing Python found. Pass -PythonExe with Python 3.11 or newer.' }
        $PythonExe = $pythonCommand.Source
    }
    $PythonExe = (Resolve-Path -LiteralPath $PythonExe).Path
    $pythonVersion = & $PythonExe -c "import sys; assert sys.version_info >= (3, 11), 'Python >=3.11 required'; assert sys.maxsize > 2**32, '64-bit Python required'; print('.'.join(map(str, sys.version_info[:3])))"
    if ($LASTEXITCODE -ne 0) { throw 'The selected existing Python does not meet the MCP requirements.' }
    Write-Host "Reusing Python $pythonVersion at $PythonExe"

    New-Item -ItemType Directory -Path $DownloadsDir -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Write-Step 'Installing locked local tools'
    foreach ($package in $lock.downloads) {
        $marker = Join-Path (Join-Path $ProjectDir $package.destination) $package.marker
        if (Test-Path -LiteralPath $marker) {
            Write-Host "$($package.id) $($package.version): installed"
        } else {
            Expand-ToolArchive (Get-Download $package) $package
        }
    }
    $dotnetRoot = Join-Path $ToolsDir 'dotnet'
    $dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_ROOT_X64 = $dotnetRoot
    $env:DOTNET_CLI_HOME = Join-Path $ToolsDir '.dotnet-home'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:PATH = "$dotnetRoot;$env:PATH"
    $env:UV_PYTHON_DOWNLOADS = 'never'
    $env:UV_CACHE_DIR = Join-Path $ProjectDir 'artifacts\uv-cache'
    $env:UV_LINK_MODE = 'copy'
    $sdkPackage = $lock.downloads | Where-Object { $_.id -eq 'dotnet-sdk' } | Select-Object -First 1
    Push-Location $ProjectDir
    try {
        $sdkVersion = & $dotnetExe --version
        $sdkExitCode = $LASTEXITCODE
    } finally { Pop-Location }
    if ($sdkExitCode -ne 0 -or -not $sdkVersion -or $sdkVersion.Trim() -ne $sdkPackage.version) { throw "Wrong SDK selected: $sdkVersion" }
    $runtimeList = & $dotnetExe --list-runtimes
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate local .NET runtimes.' }
    foreach ($requirement in @('Microsoft.NETCore.App 8.', 'Microsoft.WindowsDesktop.App 10.')) {
        if (-not ($runtimeList | Where-Object { $_.StartsWith($requirement) })) { throw "Local runtime missing: $requirement" }
    }
    $runtimeList | ForEach-Object { Write-Host $_ }
    $godotExe = Join-Path $ToolsDir 'godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    $godotVersion = & $godotExe --headless --version
    if ($LASTEXITCODE -ne 0 -or ($godotVersion -join '') -notmatch '^4\.5\.1') { throw "Wrong Godot version: $godotVersion" }
    $uvExe = Join-Path $ToolsDir 'uv\uv.exe'
    $uvVersion = & $uvExe --version
    $uvPackage = $lock.downloads | Where-Object { $_.id -eq 'uv' } | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or ($uvVersion -join '') -notmatch ('^uv ' + [regex]::Escape($uvPackage.version) + '(\s|$)')) {
        throw "Wrong uv version: $uvVersion"
    }
    Write-Host $uvVersion

    Write-Step 'Preparing locked helper repositories'
    foreach ($repository in $lock.repositories) { Ensure-Repository $repository }
    Write-Step 'Building ILSpy MCP'
    Apply-ToolPatches (Join-Path $ToolsDir 'ILSpy-Mcp') 'ILSpy-Mcp'
    Push-Location $ProjectDir
    try {
        Invoke-Checked $dotnetExe @('build', (Join-Path $ToolsDir 'ILSpy-Mcp\ILSpy.Mcp.csproj'), '--configuration', 'Release', '--nologo')
    } finally { Pop-Location }
    if (-not (Test-Path -LiteralPath (Join-Path $ToolsDir 'ILSpy-Mcp\bin\Release\net8.0\ILSpy.Mcp.dll'))) {
        throw 'ILSpy MCP output DLL missing.'
    }
    Write-Step 'Creating isolated MCP packages with the existing Python'
    $mcpPythonDirectory = Join-Path $ToolsDir 'STS2MCP\mcp'
    Invoke-Checked $uvExe @('sync', '--frozen', '--directory', $mcpPythonDirectory, '--python', $PythonExe, '--no-python-downloads')
    $venvPython = Join-Path $mcpPythonDirectory '.venv\Scripts\python.exe'
    Invoke-Checked $venvPython @('-c', "import mcp, httpx, sys; print('MCP Python:', sys.version.split()[0]); print('Base Python:', sys.base_prefix)")
    $launcher = @'
@echo off
setlocal
set "DOTNET_ROOT=%~dp0..\dotnet"
set "DOTNET_ROOT_X64=%DOTNET_ROOT%"
set "PATH=%DOTNET_ROOT%;%PATH%"
"%DOTNET_ROOT%\dotnet.exe" "%~dp0ILSpy.dll" %*
exit /b %ERRORLEVEL%
'@
    [IO.File]::WriteAllText((Join-Path $ToolsDir 'ilspy\LaunchILSpy.bat'), $launcher, [Text.Encoding]::ASCII)
    $localConfigPath = Join-Path $ProjectDir '.env'
    $localConfig = @()
    if (Test-Path -LiteralPath $localConfigPath) {
        foreach ($line in Get-Content -LiteralPath $localConfigPath -Encoding UTF8) {
            if (-not $RitsuLibDir -and $line -match '^\s*RITSULIB_DIR\s*=(.*)$') { $RitsuLibDir = $Matches[1].Trim().Trim('"') }
            if ($line -notmatch '^\s*(GAME_DIR|BASELINE_GAME_DIR|RITSULIB_DIR)\s*=') { $localConfig += $line }
        }
    }
    if (-not $RitsuLibDir) { throw 'RitsuLib directory is required. Pass -RitsuLibDir or set RITSULIB_DIR in the local .env file.' }
    $ritsuReport = Join-Path $ProjectDir 'artifacts\ritsulib-install-check.json'
    Invoke-Checked 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $ToolsDir 'verify-ritsulib.ps1'), '-GameDir', $GameDir, '-RitsuLibDir', $RitsuLibDir, '-OutputPath', $ritsuReport)
    $ritsuRuntime = Get-Content -LiteralPath $ritsuReport -Raw -Encoding UTF8 | ConvertFrom-Json
    $RitsuLibDir = $ritsuRuntime.runtime_directory
    $localConfig += "GAME_DIR=$GameDir"
    $localConfig += "BASELINE_GAME_DIR=$BaselineGameDir"
    $localConfig += "RITSULIB_DIR=$RitsuLibDir"
    [IO.File]::WriteAllText($localConfigPath, ($localConfig -join [Environment]::NewLine) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Step 'Building SurpriseRaven and the game control Mods'
    $buildArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $ToolsDir 'build-mod.ps1'),
        '-GameDir', $GameDir, '-BaselineGameDir', $BaselineGameDir, '-Configuration', 'Release', '-IncludeControlMods')
    if ($NoDeploy) { $buildArguments += '-BuildOnly' }
    Invoke-Checked 'powershell.exe' $buildArguments
    Write-Step 'Configuring project-local Codex MCP'
    Invoke-Checked 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $ToolsDir 'configure-codex.ps1'), '-PythonExe', $PythonExe)
    if ($DeepVerify -eq 'Y') {
        Write-Step 'Verifying isolated headless game loading and offline MCP interfaces'
        Invoke-Checked 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $ToolsDir 'verify-environment.ps1'), '-HeadlessGame', '-GameDir', $GameDir, '-RitsuLibDir', $RitsuLibDir)
    }
    $installed = [ordered]@{
        installed_at_utc = [DateTime]::UtcNow.ToString('o')
        game_directory = $GameDir
        game_target = 'net9.0'
        game_version = $targetGame.Version
        api_target = $targetGame.ApiTarget
        baseline_game_directory = $BaselineGameDir
        baseline_game_version = $baselineGame.Version
        baseline_reference_sha256 = $baselineGame.ReferenceHashes
        sdk_version = $sdkVersion.Trim()
        python_executable = $PythonExe
        python_version = $pythonVersion.Trim()
        venv_python = $venvPython
        tools_lock_sha256 = (Get-FileHash -LiteralPath $LockPath -Algorithm SHA256).Hash.ToLowerInvariant()
        deployed = -not [bool]$NoDeploy
        deep_verified = $DeepVerify -eq 'Y'
        verification_mode = if ($DeepVerify -eq 'Y') { 'headless' } else { 'offline' }
        gui_testing_allowed = $false
        ritsulib_directory = $RitsuLibDir
        ritsulib_version = $ritsuRuntime.version
    }
    [IO.File]::WriteAllText((Join-Path $ProjectDir 'artifacts\toolchain-installed.json'),
        ($installed | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Write-Host ''
    Write-Host 'Toolchain ready. Run install-mod.bat for daily builds.' -ForegroundColor Green
    Write-Host 'Codex configuration is project-local. Reload the chat tools if this session does not yet expose the new servers.'
} catch {
    [Console]::Error.WriteLine("Setup failed: $($_.Exception.Message)")
    exit 1
} finally {
    foreach ($key in $EnvironmentKeys) {
        [Environment]::SetEnvironmentVariable($key, $SavedEnvironment[$key], 'Process')
    }
}
