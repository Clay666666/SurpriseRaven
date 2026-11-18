[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDir,
    [Parameter(Mandatory)][string]$SpecPath,
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$OutputDir,
    [string]$PackPrefix = 'SurpriseRaven/raven'
)
# Copies the selected third-party media into a staging tree for PCK packing.
# Validates image and WAV structure; the PCK builder validates MP3 decoding.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'raven-media-validation.ps1')

$SourceDir = [IO.Path]::GetFullPath($SourceDir)
if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
    throw "Raven assets not found: $SourceDir. Set RAVEN_ASSETS_DIR in .env or pass -RavenAssetsDir."
}
$spec = Get-Content -LiteralPath $SpecPath -Raw -Encoding UTF8 | ConvertFrom-Json
$config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($spec.format -ne 'raven-assets-v1' -or $config.format -ne 'raven-frontend-v1') { throw 'Unsupported raven asset spec or frontend config format.' }
$root = Join-Path $OutputDir ($PackPrefix -replace '/', '\')
New-Item -ItemType Directory -Path $root -Force | Out-Null
$ids = @{}; $targets = @{}; $entries = @()
foreach ($asset in @($spec.assets)) {
    $id = [string]$asset.id; $target = [string]$asset.target
    if ($ids.ContainsKey($id) -or $targets.ContainsKey($target)) { throw "Duplicate raven asset id/target: $id" }
    if ($target -notmatch '^[a-z0-9_]+(/[a-z0-9_.]+)+$' -or $target.Contains('..')) { throw "Invalid raven asset target: $target" }
    if ($asset.kind -notin 'png', 'webp', 'wav', 'mp3') { throw "Unsupported raven asset kind: $($asset.kind)" }
    if ([IO.Path]::GetExtension($target) -cne ".$($asset.kind)") { throw "Raven asset target extension does not match its kind: $id" }
    $ids[$id] = [string]$asset.kind; $targets[$target] = $true
    $source = [IO.Path]::GetFullPath((Join-Path $SourceDir ([string]$asset.source)))
    if (-not $source.StartsWith($SourceDir + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Raven asset escapes the source directory: $id" }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing raven asset $id`: $source" }
    $bytes = [IO.File]::ReadAllBytes($source)
    $entry = [ordered]@{ id = $id; path = $target; kind = [string]$asset.kind; size = $bytes.Length }
    if ($asset.kind -eq 'wav') {
        $wav = Get-WavInfo $bytes $id
        $entry.durationSeconds = $wav.Duration; $entry.sampleRate = $wav.Rate; $entry.channels = $wav.Channels
    } elseif ($asset.kind -in 'png', 'webp') {
        $size = Get-ImageSize $bytes ([string]$asset.kind) $id
        if ($size[0] -ne [int]$asset.width -or $size[1] -ne [int]$asset.height) { throw "Raven asset $id is $($size[0])x$($size[1]); expected $($asset.width)x$($asset.height)." }
        $entry.width = $size[0]; $entry.height = $size[1]
        if ($asset.PSObject.Properties.Name -contains 'grid') {
            $grid = @($asset.grid)
            if ($size[0] % [int]$grid[0] -ne 0 -or $size[1] % [int]$grid[1] -ne 0) { throw "Raven asset $id does not divide into a $($grid -join 'x') grid." }
            $entry.grid = @([int]$grid[0], [int]$grid[1])
        }
    }
    $entry.sha256 = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    $destination = Join-Path $root ($target -replace '/', '\')
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    [IO.File]::WriteAllBytes($destination, $bytes)
    $entries += [pscustomobject]$entry
}
foreach ($event in $config.events.PSObject.Properties) {
    foreach ($clip in @($event.Value.clips)) {
        if ($ids[[string]$clip] -notin 'wav', 'mp3') { throw "Frontend event $($event.Name) references missing or non-audio clip $clip." }
    }
}
$utf8 = [Text.UTF8Encoding]::new($false)
$manifest = [ordered]@{ format = 'raven-asset-manifest-v1'; assets = $entries }
[IO.File]::WriteAllText((Join-Path $root 'manifest.json'), ($manifest | ConvertTo-Json -Depth 6), $utf8)
[IO.File]::WriteAllText((Join-Path $root 'frontend.json'), (Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8), $utf8)
Write-Host "Staged $($entries.Count) raven assets into $root"
