[CmdletBinding()]
param(
    [string]$RitsuLibDir = '',
    [string]$GameDir = '',
    [string]$PayloadDir = '',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot

try {
    $localEnv = Join-Path $projectRoot '.env'
    if (Test-Path -LiteralPath $localEnv) {
        foreach ($line in Get-Content -LiteralPath $localEnv -Encoding UTF8) {
            if (-not $GameDir -and $line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') }
            if (-not $RitsuLibDir -and $line -match '^\s*RITSULIB_DIR\s*=(.*)$') { $RitsuLibDir = $Matches[1].Trim().Trim('"') }
        }
    }
    if (-not $GameDir) { throw 'Game directory is required. Pass -GameDir or set GAME_DIR in the local .env file.' }
    if (-not $RitsuLibDir) { throw 'RitsuLib directory is required. Pass -RitsuLibDir or set RITSULIB_DIR in the local .env file.' }
    $python = Join-Path $projectRoot 'tools\STS2MCP\mcp\.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
        $command = Get-Command python.exe -ErrorAction SilentlyContinue
        if (-not $command) { throw 'Existing Python 3.11+ is required for offline RitsuLib verification.' }
        $python = $command.Source
    }
    $arguments = @((Join-Path $PSScriptRoot 'ritsulib_check.py'), '--runtime', $RitsuLibDir, '--game', $GameDir)
    if ($PayloadDir) { $arguments += @('--payload', $PayloadDir) }
    if ($OutputPath) { $arguments += @('--output', $OutputPath) }
    & $python @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Offline RitsuLib validation failed. No runtime or game files were changed.' }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
