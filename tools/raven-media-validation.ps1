# Structural validation before staging; the PCK builder also fully decodes PNG/WebP.
function Read-UInt16LE([byte[]]$b, [int]$o) { return [int]$b[$o] -bor ([int]$b[$o + 1] -shl 8) }
function Read-UInt24LE([byte[]]$b, [int]$o) { return (Read-UInt16LE $b $o) -bor ([int]$b[$o + 2] -shl 16) }
function Read-UInt32LE([byte[]]$b, [int]$o) { return [BitConverter]::ToUInt32($b, $o) }
function Read-UInt32BE([byte[]]$b, [int]$o) { return ([long]$b[$o] -shl 24) -bor ([long]$b[$o + 1] -shl 16) -bor ([long]$b[$o + 2] -shl 8) -bor [long]$b[$o + 3] }
function Ascii([byte[]]$b, [int]$o, [int]$n) { return [Text.Encoding]::ASCII.GetString($b, $o, $n) }

function Get-RiffChunks([byte[]]$b, [string]$kind, [string]$id) {
    if ($b.Length -lt 12 -or (Ascii $b 0 4) -ne 'RIFF' -or (Ascii $b 8 4) -ne $kind) { throw "Asset $id is not a $kind RIFF file." }
    if ([long](Read-UInt32LE $b 4) + 8 -ne $b.LongLength) { throw "Asset $id has a truncated or inconsistent RIFF length." }
    [long]$offset = 12
    while ($offset -lt $b.LongLength) {
        if ($offset + 8 -gt $b.LongLength) { throw "Asset $id has an incomplete RIFF chunk header." }
        [long]$size = Read-UInt32LE $b ([int]$offset + 4)
        [long]$next = $offset + 8 + $size + ($size % 2)
        if ($next -gt $b.LongLength) { throw "Asset $id has a RIFF chunk outside the file." }
        [pscustomobject]@{ Name = (Ascii $b ([int]$offset) 4); Offset = [int]$offset + 8; Size = $size }
        $offset = $next
    }
}

function Get-ImageSize([byte[]]$b, [string]$kind, [string]$id) {
    if ($kind -eq 'png') {
        if ($b.Length -lt 33 -or [BitConverter]::ToString($b, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A' -or
            (Ascii $b 12 4) -ne 'IHDR' -or (Read-UInt32BE $b 8) -ne 13) { throw "Asset $id is not a complete PNG header." }
        [long]$offset = 8; $hasData = $false; $hasEnd = $false
        while ($offset -lt $b.LongLength) {
            if ($offset + 12 -gt $b.LongLength) { throw "Asset $id has an incomplete PNG chunk." }
            [long]$size = Read-UInt32BE $b ([int]$offset)
            $chunk = Ascii $b ([int]$offset + 4) 4
            [long]$next = $offset + 12 + $size
            if ($next -gt $b.LongLength) { throw "Asset $id has a PNG chunk outside the file." }
            if ($chunk -eq 'IDAT' -and $size -gt 0) { $hasData = $true }
            if ($chunk -eq 'IEND') {
                if ($size -ne 0 -or $next -ne $b.LongLength) { throw "Asset $id has an invalid PNG end chunk." }
                $hasEnd = $true
            }
            $offset = $next
        }
        if (-not $hasData -or -not $hasEnd) { throw "Asset $id is missing PNG image data or end chunk." }
        return @((Read-UInt32BE $b 16), (Read-UInt32BE $b 20))
    }
    $chunks = @(Get-RiffChunks $b 'WEBP' $id)
    $imageChunks = @($chunks | Where-Object Name -in 'VP8 ', 'VP8L')
    if ($imageChunks.Count -ne 1) { throw "Asset $id must contain one static WebP image." }
    $first = $chunks[0]; $o = $first.Offset
    switch ($first.Name) {
        'VP8X' {
            if ($first.Size -ne 10) { throw "Asset $id has an invalid WebP extended header." }
            return @((1 + (Read-UInt24LE $b ($o + 4))), (1 + (Read-UInt24LE $b ($o + 7))))
        }
        'VP8 ' {
            if ($first.Size -lt 10 -or [BitConverter]::ToString($b, $o + 3, 3) -ne '9D-01-2A') { throw "Asset $id has an invalid WebP frame header." }
            return @(((Read-UInt16LE $b ($o + 6)) -band 0x3FFF), ((Read-UInt16LE $b ($o + 8)) -band 0x3FFF))
        }
        'VP8L' {
            if ($first.Size -lt 5 -or $b[$o] -ne 0x2F) { throw "Asset $id has an invalid WebP lossless header." }
            $bits = Read-UInt32LE $b ($o + 1)
            return @((($bits -band 0x3FFF) + 1), ((($bits -shr 14) -band 0x3FFF) + 1))
        }
        default { throw "Asset $id has an unknown WebP chunk." }
    }
}

function Get-WavInfo([byte[]]$b, [string]$id) {
    $chunks = @(Get-RiffChunks $b 'WAVE' $id)
    $formats = @($chunks | Where-Object Name -eq 'fmt ')
    $data = @($chunks | Where-Object Name -eq 'data')
    if ($formats.Count -ne 1 -or $data.Count -ne 1 -or $formats[0].Size -lt 16 -or $formats[0].Offset -gt $data[0].Offset) {
        throw "Asset $id must contain one complete WAV fmt chunk followed by one data chunk."
    }
    $o = $formats[0].Offset
    $tag = Read-UInt16LE $b $o; $channels = Read-UInt16LE $b ($o + 2)
    [long]$rate = Read-UInt32LE $b ($o + 4); [long]$byteRate = Read-UInt32LE $b ($o + 8)
    $align = Read-UInt16LE $b ($o + 12); $bits = Read-UInt16LE $b ($o + 14)
    if ($tag -ne 1 -or $bits -ne 16 -or $channels -notin 1, 2 -or $rate -le 0 -or
        $align -ne $channels * 2 -or $byteRate -ne $rate * $align -or $data[0].Size % $align -ne 0) {
        throw "Asset $id must be PCM16 mono/stereo with consistent sample rate, byte rate, block alignment and sample data."
    }
    $duration = [Math]::Round($data[0].Size / $byteRate, 4)
    if ($duration -lt 0.05) { throw "Asset $id is too short." }
    return @{ Rate = $rate; Channels = $channels; Duration = $duration; Offset = $data[0].Offset; Frames = $data[0].Size / $align }
}

# Preserve sample rate, channels and all samples between the short fades. Only the staged copy is edited.
function Get-TrimmedPcm16([byte[]]$bytes, $wav, $trim, [string]$id) {
    $rate = $wav.Rate; $align = 2 * $wav.Channels
    $start = [int][Math]::Round($rate * $trim.startMs / 1000.0)
    $end = if ($trim.PSObject.Properties.Name -contains 'endMs') { [int][Math]::Round($rate * $trim.endMs / 1000.0) } else { $wav.Frames }
    $frames = $end - $start
    $fadeIn = [int][Math]::Round($rate * $trim.fadeInMs / 1000.0)
    $fadeOut = [int][Math]::Round($rate * $trim.fadeOutMs / 1000.0)
    if ($start -lt 0 -or $end -gt $wav.Frames -or $frames -le 0 -or
        $fadeIn -lt 0 -or $fadeOut -lt 0 -or $fadeIn + $fadeOut -gt $frames) { throw "Invalid PCM trim or fades for $id." }
    $pcm = New-Object byte[] ($frames * $align)
    [Array]::Copy($bytes, $wav.Offset + $start * $align, $pcm, 0, $pcm.Length)
    foreach ($fade in @(@{ Length = $fadeIn; Start = 0; Step = 1 }, @{ Length = $fadeOut; Start = $frames - 1; Step = -1 })) {
        for ($i = 0; $i -lt $fade.Length; $i++) {
            $gain = $i / [double][Math]::Max(1, $fade.Length - 1)
            $frame = $fade.Start + $i * $fade.Step
            for ($channel = 0; $channel -lt $wav.Channels; $channel++) {
                $offset = $frame * $align + $channel * 2
                $sample = [int16][Math]::Round([BitConverter]::ToInt16($pcm, $offset) * $gain)
                [BitConverter]::GetBytes($sample).CopyTo($pcm, $offset)
            }
        }
    }
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36 + $pcm.Length))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([int]16); $writer.Write([int16]1)
        $writer.Write([int16]$wav.Channels); $writer.Write([int]$rate); $writer.Write([int]($rate * $align))
        $writer.Write([int16]$align); $writer.Write([int16]16); $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$pcm.Length); $writer.Write($pcm); $writer.Flush()
        return ,$stream.ToArray()
    } finally { $writer.Dispose(); $stream.Dispose() }
}
