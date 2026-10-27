# Gate G15.6: Bilder der Spielfarben (E94) mit erzeugten Spielbildern. Startet keine App, liest weder Konfiguration noch
# Bilder- oder Launcher-Caches und geht nicht ins Netz: Die WPF-Bildtests GameColorRenderTests zeichnen das
# Bibliotheksfenster mit erzeugten Logos, Covern und Titelbildern (rot/orange, weiß/blau, grau mit anderem Schema, bunt in
# Duoton und Tritonus, Hintergrund aus, Leuchten des Titelbilds, dunkle Markenfarbe, Gold im hellen Thema; Liste, Raster,
# Regal; hell und dunkel) und prüfen den Starten-Knopf gegen den erwarteten Verlauf.
#   - alle 9 Tests grün (TESTS_OK)
#   - alle 18 erwarteten Bilder da, genau 1600x900 und nicht einfarbig
#   PHASE15_GAMECOLORS_OK
param([string]$ShotsDir = (Join-Path $env:TEMP 'HDRpilot-Gates\phase15-gamecolors'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

if (Test-Path $ShotsDir) { Remove-Item (Join-Path $ShotsDir '*') -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $ShotsDir | Out-Null
$env:HDRPILOT_LIBRARY_RENDER_DIR = $ShotsDir
$ok = $true
try {
    $run = powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test.ps1') -Filter 'FullyQualifiedName~HdrPilot.App.Tests.Library.GameColorRender' -MinTests 9 | Out-String
} finally {
    Remove-Item Env:HDRPILOT_LIBRARY_RENDER_DIR -ErrorAction SilentlyContinue
}

Write-Output ($run -split "`r?`n" | Where-Object { $_ -match 'gesamt=|TESTS_OK' })
if ($run -notmatch 'TESTS_OK') { $ok = $false; Write-Output 'FEHLER Bildtests nicht grün' }

$expected = @(
    'gamecolors-rot-liste-dunkel.png', 'gamecolors-rot-liste-hell.png', 'gamecolors-rot-regal-dunkel.png', 'gamecolors-rot-regal-hell.png',
    'gamecolors-blau-liste-dunkel.png', 'gamecolors-blau-liste-hell.png',
    'gamecolors-grau-liste-dunkel.png', 'gamecolors-grau-liste-hell.png',
    'gamecolors-bunt-duo-raster-dunkel.png', 'gamecolors-bunt-duo-raster-hell.png',
    'gamecolors-bunt-tri-raster-dunkel.png', 'gamecolors-bunt-tri-raster-hell.png',
    'gamecolors-bg-aus-liste-dunkel.png', 'gamecolors-bg-aus-liste-hell.png',
    'gamecolors-hint-regal-dunkel.png',
    'gamecolors-navy-liste-dunkel.png', 'gamecolors-navy-liste-hell.png',
    'gamecolors-gold-liste-hell.png'
)
foreach ($name in $expected) {
    $path = Join-Path $ShotsDir $name
    if (-not (Test-Path $path)) { $ok = $false; Write-Output "FEHLER fehlt: $name"; continue }
    $stream = [IO.File]::OpenRead($path)
    try {
        $frame = [Windows.Media.Imaging.BitmapDecoder]::Create($stream, [Windows.Media.Imaging.BitmapCreateOptions]::None, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad).Frames[0]
    } finally { $stream.Dispose() }
    $converted = New-Object Windows.Media.Imaging.FormatConvertedBitmap($frame, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $pixels = New-Object byte[] ($converted.PixelWidth * $converted.PixelHeight * 4)
    $converted.CopyPixels($pixels, $converted.PixelWidth * 4, 0)
    $first = [BitConverter]::ToInt32($pixels, 0)
    $uniform = $true
    for ($i = 4; $i -lt $pixels.Length; $i += 4 * 97) {
        if ([BitConverter]::ToInt32($pixels, $i) -ne $first) { $uniform = $false; break }
    }

    $size = '{0}x{1}' -f $frame.PixelWidth, $frame.PixelHeight
    if ($size -ne '1600x900' -or $uniform) { $ok = $false; Write-Output "FEHLER $name`: $size, einfarbig=$uniform" }
    else { Write-Output "ok   $name ($size)" }
}

if ($ok) { Write-Output 'PHASE15_GAMECOLORS_OK'; exit 0 }
Write-Output 'PHASE15_GAMECOLORS_FAILED'
exit 1
