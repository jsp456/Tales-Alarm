Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TalesAlarm\Assets'
$wavPath = Join-Path $assetDirectory 'default-alarm.wav'
$iconPath = Join-Path $assetDirectory 'tales-alarm.ico'
[IO.Directory]::CreateDirectory($assetDirectory) | Out-Null

$sampleRate = 44100
$durationSeconds = 1.5
$sampleCount = [int]($sampleRate * $durationSeconds)

function Get-Tone(
    [double]$time,
    [double]$start,
    [double]$length,
    [double]$fundamental,
    [double]$upper) {
    $local = $time - $start
    if ($local -lt 0 -or $local -ge $length) { return 0.0 }
    $attack = [Math]::Min(1.0, $local / 0.012)
    $decay = [Math]::Exp(-4.2 * $local / $length)
    return $attack * $decay * (
        0.62 * [Math]::Sin(2 * [Math]::PI * $fundamental * $local) +
        0.25 * [Math]::Sin(2 * [Math]::PI * $upper * $local))
}

$samples = [System.Int16[]]::new($sampleCount)
for ($index = 0; $index -lt $sampleCount; $index++) {
    $time = $index / [double]$sampleRate
    $mixed = 0.72 * (
        (Get-Tone $time 0.03 0.62 659.25 987.77) +
        (Get-Tone $time 0.72 0.73 783.99 1174.66))
    $clamped = [Math]::Max(-1.0, [Math]::Min(1.0, $mixed))
    $samples[$index] = [System.Int16][Math]::Round($clamped * 32767)
}

$wavStream = [IO.File]::Open($wavPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$wavWriter = [IO.BinaryWriter]::new($wavStream, [Text.Encoding]::ASCII, $false)
try {
    $wavWriter.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
    $wavWriter.Write([int](36 + $sampleCount * 2))
    $wavWriter.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
    $wavWriter.Write([Text.Encoding]::ASCII.GetBytes('fmt '))
    $wavWriter.Write([int]16)
    $wavWriter.Write([System.Int16]1)
    $wavWriter.Write([System.Int16]1)
    $wavWriter.Write([int]$sampleRate)
    $wavWriter.Write([int]($sampleRate * 2))
    $wavWriter.Write([System.Int16]2)
    $wavWriter.Write([System.Int16]16)
    $wavWriter.Write([Text.Encoding]::ASCII.GetBytes('data'))
    $wavWriter.Write([int]($sampleCount * 2))
    foreach ($sample in $samples) {
        $wavWriter.Write([System.Int16]$sample)
    }
}
finally {
    $wavWriter.Dispose()
}

Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$pngStream = [IO.MemoryStream]::new()
try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $faceBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 32, 103, 191))
    $rimPen = [Drawing.Pen]::new([Drawing.Color]::White, 4)
    $handPen = [Drawing.Pen]::new([Drawing.Color]::White, 4)
    $handPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $centerBrush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    try {
        $graphics.FillEllipse($faceBrush, 3, 3, 58, 58)
        $graphics.DrawEllipse($rimPen, 5, 5, 54, 54)
        $graphics.DrawLine($handPen, 32, 32, 32, 16)
        $graphics.DrawLine($handPen, 32, 32, 45, 38)
        $graphics.FillEllipse($centerBrush, 28, 28, 8, 8)
        $bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $faceBrush.Dispose()
        $rimPen.Dispose()
        $handPen.Dispose()
        $centerBrush.Dispose()
    }
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

$pngBytes = $pngStream.ToArray()
$pngStream.Dispose()
$iconStream = [IO.File]::Open($iconPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$iconWriter = [IO.BinaryWriter]::new($iconStream, [Text.Encoding]::ASCII, $false)
try {
    $iconWriter.Write([uint16]0)
    $iconWriter.Write([uint16]1)
    $iconWriter.Write([uint16]1)
    $iconWriter.Write([byte]64)
    $iconWriter.Write([byte]64)
    $iconWriter.Write([byte]0)
    $iconWriter.Write([byte]0)
    $iconWriter.Write([uint16]1)
    $iconWriter.Write([uint16]32)
    $iconWriter.Write([uint32]$pngBytes.Length)
    $iconWriter.Write([uint32]22)
    $iconWriter.Write($pngBytes)
}
finally {
    $iconWriter.Dispose()
}
