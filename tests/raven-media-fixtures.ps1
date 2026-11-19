# Self-created 8x8 image samples, encoded by Godot 4.5.1; no third-party media.
function Write-RavenTestMedia($Spec, [string]$SourceDir) {
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAACFJREFUGJVjtK/f/58BD2DCJ0kDBQcOHMCvwMHBgQZuAADbHwWMBhuhQwAAAABJRU5ErkJggg==')
    $webp = [Convert]::FromBase64String('UklGRkoAAABXRUJQVlA4ID4AAADQAQCdASoIAAgAAgA0JbAAA18PA+8fAAD+DsopEH//xDoAFi/27/5Lsr///wrrjZ25N3iUn/Wh/b0FHWAYAA==')
    # Six silent MPEG-1 Layer III frames (32 kbps, 44.1 kHz, mono, no padding).
    $mp3 = New-Object byte[] (6 * 104)
    $frameHeader = [byte[]]@(0xff, 0xfb, 0x10, 0xc0)
    for ($offset = 0; $offset -lt $mp3.Length; $offset += 104) { $frameHeader.CopyTo($mp3, $offset) }
    foreach ($asset in @($Spec.assets)) {
        $path = Join-Path $SourceDir ([string]$asset.source -replace '/', '\')
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        if ($asset.kind -in 'png', 'webp') {
            $asset.width = 8; $asset.height = 8
            $bytes = if ($asset.kind -eq 'png') { $png } else { $webp }
            [IO.File]::WriteAllBytes($path, $bytes)
        } elseif ($asset.kind -eq 'mp3') {
            [IO.File]::WriteAllBytes($path, $mp3)
        } else {
            $size = if ($asset.PSObject.Properties.Name -contains 'trim') { 32000 } else { 1600 }
            $stream = New-Object IO.MemoryStream; $writer = New-Object IO.BinaryWriter($stream)
            try {
                $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36 + $size)); $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
                $writer.Write([int]16); $writer.Write([int16]1); $writer.Write([int16]1); $writer.Write([int]8000); $writer.Write([int]16000); $writer.Write([int16]2); $writer.Write([int16]16)
                $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]$size); $writer.Write((New-Object byte[] $size))
                $writer.Flush(); [IO.File]::WriteAllBytes($path, $stream.ToArray())
            } finally { $writer.Dispose(); $stream.Dispose() }
        }
    }
}
