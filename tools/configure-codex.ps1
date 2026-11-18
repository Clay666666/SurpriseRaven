[CmdletBinding()]
param([string]$PythonExe = "")

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot

try {
    if (-not $PythonExe) {
        $PythonExe = Join-Path $projectRoot 'tools\STS2MCP\mcp\.venv\Scripts\python.exe'
    }
    if (-not (Test-Path -LiteralPath $PythonExe -PathType Leaf)) {
        throw "Python not found: $PythonExe. Pass -PythonExe with an existing Python 3.11+ interpreter."
    }
    & $PythonExe (Join-Path $PSScriptRoot 'mcp_smoke.py') configure --root $projectRoot
    if ($LASTEXITCODE -ne 0) { throw "Codex configuration failed (exit $LASTEXITCODE)." }
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
