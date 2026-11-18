[CmdletBinding(SupportsShouldProcess = $true)]
param([Parameter(Position = 0)][string]$GameDir = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Assert-SafeRemoval([string]$Path, [string]$ModsRoot) {
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $fullRoot = [IO.Path]::GetFullPath($ModsRoot).TrimEnd('\')
    if (-not $fullPath.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the game's mods directory: $fullPath"
    }
    if ((Get-Item -LiteralPath $fullRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing a mods directory that is a reparse point: $fullRoot"
    }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($fullPath)
    while ($pending.Count -gt 0) {
        $item = Get-Item -LiteralPath $pending.Pop() -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to remove a reparse point or a directory containing one: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            foreach ($child in @(Get-ChildItem -LiteralPath $item.FullName -Force)) { $pending.Push($child.FullName) }
        }
    }
    return $fullPath
}

try {
    if (-not $GameDir) {
        $envFile = Join-Path $projectRoot '.env'
        if (Test-Path -LiteralPath $envFile) {
            foreach ($line in Get-Content -LiteralPath $envFile -Encoding UTF8) {
                if ($line -match '^\s*GAME_DIR\s*=(.*)$') { $GameDir = $Matches[1].Trim().Trim('"') }
            }
        }
    }
    if (-not $GameDir) { throw 'Game directory is required. Pass -GameDir or set GAME_DIR in the local .env file.' }
    $GameDir = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll') -PathType Leaf)) {
        throw "Game installation not found: $GameDir"
    }
    if (-not $WhatIfPreference -and (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)) {
        throw 'The game is running. Close it before uninstalling these development mods.'
    }
    $modsRoot = Join-Path $GameDir 'mods'
    if (-not (Test-Path -LiteralPath $modsRoot)) {
        Write-Host 'No mods directory exists; nothing to uninstall.'
        return
    }

    # These are the only IDs owned by this development setup. Never delete generic
    # mod_manifest.json files, Workshop content, or other mods sharing a prefix.
    $targets = @()
    foreach ($id in @('SurpriseRaven', 'STS2_MCP', 'STS2MenuControl')) {
        foreach ($relative in @($id, "$id.dll", "$id.json", "$id.pck")) {
            $candidate = Join-Path $modsRoot $relative
            if (Test-Path -LiteralPath $candidate) { $targets += Assert-SafeRemoval $candidate $modsRoot }
        }
    }
    foreach ($target in $targets) {
        if ($PSCmdlet.ShouldProcess($target, 'Remove SurpriseRaven development mod files')) {
            # Revalidate immediately before removal as well as during preflight.
            $checked = Assert-SafeRemoval $target $modsRoot
            Remove-Item -LiteralPath $checked -Recurse -Force
            Write-Host "Removed: $checked"
        }
    }
    if ($targets.Count -eq 0) { Write-Host 'No SurpriseRaven development mods are installed.' }
} catch {
    [Console]::Error.WriteLine('UNINSTALL FAILED: ' + $_.Exception.Message)
    throw
}
