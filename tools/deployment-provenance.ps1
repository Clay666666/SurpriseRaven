# Used by both regular builds and deployment of verified artifacts.
function Get-SourceIdentity([string]$Repository) {
    $commit = & git -C $Repository rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
    $changes = & git -C $Repository status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source changes.' }
    return @{ Commit = $commit; Clean = @($changes).Count -eq 0 }
}

function Get-PayloadHashes([string]$Path) {
    Assert-NoReparseTree $Path
    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $hashes = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $Path -File -Force -Recurse | Sort-Object FullName) {
        $hashes[$file.FullName.Substring($root.Length)] = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    }
    return $hashes
}

function Assert-ExactHashes([string]$Path, $Expected) {
    if ($Expected -isnot [Collections.IDictionary]) {
        $entry = $Expected
        $Expected = @{}
        foreach ($property in $entry.PSObject.Properties) { $Expected[$property.Name] = $property.Value }
    }
    $actual = Get-PayloadHashes $Path
    if ($Expected.Count -ne $actual.Count) { throw "Payload file set differs: $Path" }
    foreach ($name in $Expected.Keys) {
        if ($actual[$name] -ne $Expected[$name]) { throw "Payload file hash differs: $Path/$name" }
    }
}

function Get-DeploymentReceiptPath([string]$Repository, [string]$GameDir, [string]$ModId) {
    $common = & git -C $Repository rev-parse --path-format=absolute --git-common-dir
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the shared Git directory.' }
    $receipts = Join-Path $common 'sts2-deployments'
    New-Item -ItemType Directory -Path $receipts -Force | Out-Null
    $identity = [IO.Path]::GetFullPath($GameDir).TrimEnd('\').ToUpperInvariant() + '|' + $ModId.ToUpperInvariant()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))).Replace('-', '') }
    finally { $sha.Dispose() }
    return Join-Path $receipts "$key.json"
}

function Assert-DeploymentOrigin([string]$Repository, $Build, [string]$ReceiptPath, [string]$Installed) {
    if ($Build.source_clean -ne $true -or $Build.source_commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Deployment requires a build from a clean, committed checkout. Commit changes, then rebuild.'
    }
    & git -C $Repository cat-file -e "$($Build.source_commit)^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Build source commit is unavailable.' }
    if (-not (Test-Path -LiteralPath $ReceiptPath)) { return }
    $previous = Get-Content -LiteralPath $ReceiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($previous.source_commit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid deployment receipt.' }
    & git -C $Repository merge-base --is-ancestor $previous.source_commit $Build.source_commit
    if ($LASTEXITCODE -ne 0) {
        throw "Build $($Build.source_commit) is missing deployed commit $($previous.source_commit). Merge it before deploying."
    }
    if (Test-Path -LiteralPath $Installed) { Assert-ExactHashes $Installed $previous.files }
}

function Write-DeploymentReceipt([string]$Path, $Receipt) {
    $temporary = Join-Path (Split-Path -Parent $Path) "$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        Write-JsonFile $temporary $Receipt
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Assert-VerifiedBuild($Build, [string]$Index, $Game, [string]$ModId, [string]$Artifacts, [string]$Version) {
    if ($Build.game_dir -ne $Game.Directory -or $Build.game_version -ne $Game.Version -or
        $Build.configuration -ne 'Release' -or $Build.run_id -notmatch '^\d{8}-\d{6}-[0-9a-f]{8}$') {
        throw 'The verified build does not match the deployment target.'
    }
    foreach ($name in $Game.ReferenceHashes.Keys) {
        if ($Game.ReferenceHashes[$name] -ne $Build.reference_sha256.$name) { throw "Game reference changed: $name" }
    }
    $payloads = @($Build.payloads)
    if ($payloads.Count -ne 1 -or $payloads[0].Id -ne $ModId -or $payloads[0].CompileApiTarget -ne '0.111.0') {
        throw 'Expected one verified main Mod compiled against 0.111.0.'
    }
    $stage = Assert-ChildPath (Join-Path $Artifacts "staging\$($Build.run_id)") $Artifacts
    $payload = $payloads[0]
    $directory = Assert-ChildPath $payload.Directory (Join-Path $stage 'mods')
    if ($directory -ne (Join-Path $stage "mods\$ModId")) { throw 'Unexpected verified staging directory.' }
    $hashes = $payload.Files
    $names = @($hashes.PSObject.Properties.Name)
    if ($names.Count -ne 3 -or @("$ModId.dll", "$ModId.json", "$ModId.pck").Where({ $_ -notin $names }).Count) {
        throw 'Expected exactly the main DLL, manifest and PCK.'
    }
    Assert-ExactHashes $directory $hashes
    $manifest = Get-Content -LiteralPath (Join-Path $directory "$ModId.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.id -ne $ModId -or $manifest.version -ne $Version) { throw 'Verified manifest does not match the current Mod identity.' }
    Assert-PckReport (Join-Path $stage "assets\$ModId") (Join-Path $stage "$ModId-pck-report.json") | Out-Null
    $pairs = Get-Content -LiteralPath $Index -Raw -Encoding UTF8 | ConvertFrom-Json
    $assemblyVersion = [Version]($Version.Split('-')[0])
    if ($assemblyVersion.Revision -lt 0) { $assemblyVersion = [Version]"$Version.0" }
    foreach ($release in @('0.111.0', '0.113.1')) {
        $pair = @($pairs | Where-Object Version -eq $release)
        if ($pair.Count -ne 1 -or $pair[0].Directory -notmatch '^\d{8}-\d{6}-\d{3}$') {
            throw "Expected one headless report for $release."
        }
        $path = Join-Path $Artifacts "verification\$($pair[0].Directory)"
        $environment = Get-Content -LiteralPath (Join-Path $path 'environment-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $headless = Get-Content -LiteralPath (Join-Path $path 'headless\headless-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($environment.status -ne 'passed' -or $headless.status -ne 'passed' -or $headless.exit_code -ne 0 -or
            $headless.probe.display_server -ne 'headless' -or $headless.ritsulib.game_version -ne $release -or
            $headless.probe.mod_version -ne $assemblyVersion.ToString() -or
            ($Version.Contains('-') -and (!$headless.probe.PSObject.Properties['release_version'] -or $headless.probe.release_version -cne $Version)) -or
            $headless.probe.source_commit -ne $Build.source_commit -or
            $headless.ritsulib.main_dll_sha256 -ne $hashes."$ModId.dll" -or
            $headless.ritsulib.main_pck_sha256 -ne $hashes."$ModId.pck") {
            throw "Headless evidence does not match the verified build for $release."
        }
        Assert-ExactHashes (Join-Path $headless.mirror_directory "mods\$ModId") $hashes
    }
}
