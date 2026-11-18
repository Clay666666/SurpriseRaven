# Real engine decoder checks, independent of the fake native tools in build-mod.Tests.ps1.
# Uses only the standalone Godot CLI with --headless; never starts the game.
[CmdletBinding()]
param([string]$GodotExe = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $GodotExe) { $GodotExe = Join-Path $repoRoot 'tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe' }
if (-not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) { throw 'Install the pinned local Godot CLI before running decoder checks.' }
$testRoot = Join-Path $repoRoot ('artifacts\tests\raven-decode-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'source'
$staged = Join-Path $testRoot 'staged'
$utf8 = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'raven-media-fixtures.ps1')

function Assert-DecodeTest([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "TEST FAILED: $Message" }
}
function Invoke-TestPack([string]$Name, [bool]$ShouldSucceed) {
    $log = Join-Path $testRoot "$Name.log"
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $GodotExe --headless --path (Join-Path $repoRoot 'tools\pck_builder') --script (Join-Path $repoRoot 'tools\pck_builder\build_pck.gd') -- (Join-Path $repoRoot 'src\SurpriseRaven.json') $staged (Join-Path $testRoot "$Name-report.json") (Join-Path $testRoot "$Name.pck") 2>&1 |
            ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $log -Encoding UTF8
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    Assert-DecodeTest (($code -eq 0) -eq $ShouldSucceed) "Unexpected Godot exit $code for $Name. See $log"
    return $log
}

$fullSpec = Get-Content -LiteralPath (Join-Path $repoRoot 'assets\raven\raven-assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$spec = @{ format = 'raven-assets-v1'; assets = @() }
foreach ($kind in @('png', 'webp', 'wav', 'mp3')) { $spec.assets += $fullSpec.assets | Where-Object kind -eq $kind | Select-Object -First 1 }
Write-RavenTestMedia $spec $source
$specPath = Join-Path $testRoot 'spec.json'; $configPath = Join-Path $testRoot 'config.json'
[IO.File]::WriteAllText($specPath, ($spec | ConvertTo-Json -Depth 8), $utf8)
[IO.File]::WriteAllText($configPath, '{"format":"raven-frontend-v1","events":{}}', $utf8)
& (Join-Path $repoRoot 'tools\stage-raven-assets.ps1') -SourceDir $source -SpecPath $specPath -ConfigPath $configPath -OutputDir $staged
$mediaRoot = Join-Path $staged 'SurpriseRaven\raven'
Invoke-TestPack 'valid' $true | Out-Null
foreach ($asset in $spec.assets) {
    Assert-DecodeTest ((Get-FileHash -LiteralPath (Join-Path $source $asset.source)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $mediaRoot $asset.target)).Hash) 'Staging and decoding must preserve original media bytes.'
}
foreach ($kind in @('png', 'webp', 'mp3')) {
    $asset = $spec.assets | Where-Object kind -eq $kind
    $path = Join-Path $mediaRoot $asset.target
    $original = [IO.File]::ReadAllBytes($path)
    try {
        $corrupt = [byte[]]$original.Clone()
        if ($kind -eq 'png') {
            $idat = [Text.Encoding]::ASCII.GetString($corrupt).IndexOf('IDAT', [StringComparison]::Ordinal)
            Assert-DecodeTest ($idat -ge 0) 'The PNG fixture must have compressed pixels.'
            $corrupt[$idat + 4] = 0; $corrupt[$idat + 5] = 0
        } elseif ($kind -eq 'webp') {
            [Array]::Clear($corrupt, 30, $corrupt.Length - 30)
            $corrupt[20] = 0; $corrupt[21] = 0; $corrupt[22] = 0
        } else {
            [Array]::Clear($corrupt, 0, $corrupt.Length)
        }
        # The image fixtures retain valid container headers and boundaries.
        [IO.File]::WriteAllBytes($path, $corrupt)
        $log = Invoke-TestPack "corrupt-$kind" $false
        $diagnostic = if ($kind -eq 'mp3') { 'MP3 decode failed' } else { 'Image decode failed' }
        Assert-DecodeTest ((Get-Content -LiteralPath $log -Raw -Encoding UTF8).Contains($diagnostic)) "Damaged $kind data must be rejected by the real engine decoder."
    } finally { [IO.File]::WriteAllBytes($path, $original) }
}
Invoke-TestPack 'restored' $true | Out-Null
Write-Host 'PASS: real Godot PNG/WebP/MP3 decoding, damaged data rejected, original bytes preserved.'
Write-Host "Test fixtures and logs retained at: $testRoot"
