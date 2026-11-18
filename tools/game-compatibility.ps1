# Read-only game identity checks shared by installation and daily builds.
function Read-SupportedGame([string]$Directory) {
    $directoryPath = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    $releasePath = Join-Path $directoryPath 'release_info.json'
    if (-not (Test-Path -LiteralPath $releasePath -PathType Leaf)) { throw "Game release metadata missing: $releasePath" }
    $release = Get-Content -LiteralPath $releasePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $version = ([string]$release.version) -replace '^v', ''
    $apiTarget = switch ($version) {
        '0.111.0' { '0.111.0' }
        '0.113.1' { '0.113.0' }
        default { throw "Unsupported game version '$version'; supported releases are 0.111.0 and 0.113.1." }
    }
    $dataDirectory = Join-Path $directoryPath 'data_sts2_windows_x86_64'
    $runtimePath = Join-Path $dataDirectory 'sts2.runtimeconfig.json'
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) { throw "Game runtime metadata missing: $runtimePath" }
    $runtime = Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($runtime.runtimeOptions.tfm -ne 'net9.0') { throw "Unsupported game runtime '$($runtime.runtimeOptions.tfm)'; expected net9.0." }
    $hashes = [ordered]@{}
    foreach ($assembly in @('sts2.dll', '0Harmony.dll', 'GodotSharp.dll')) {
        $path = Join-Path $dataDirectory $assembly
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing game assembly: $path" }
        $hashes[$assembly] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    return [pscustomobject]@{
        Directory = $directoryPath; DataDirectory = $dataDirectory
        Version = $version; ApiTarget = $apiTarget; ReferenceHashes = $hashes
    }
}

function Resolve-BaselineGame([string]$Directory, $TargetGame) {
    if (-not $Directory -and $TargetGame.Version -eq '0.111.0') { $Directory = $TargetGame.Directory }
    if (-not $Directory) { throw 'The 0.111.0 compile baseline is required. Pass -BaselineGameDir or set BASELINE_GAME_DIR in the local .env file.' }
    $baseline = Read-SupportedGame $Directory
    if ($baseline.Version -ne '0.111.0') { throw "Compile baseline must be game 0.111.0; found $($baseline.Version)." }
    if ($TargetGame.ApiTarget -eq $baseline.ApiTarget) {
        foreach ($assembly in $baseline.ReferenceHashes.Keys) {
            if ($TargetGame.ReferenceHashes[$assembly] -ne $baseline.ReferenceHashes[$assembly]) {
                throw "Target and baseline 0.111.0 references differ: $assembly. Select a matching baseline."
            }
        }
    }
    return $baseline
}
