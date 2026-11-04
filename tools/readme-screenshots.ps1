# Erzeugt die Bildschirmfotos der README (docs\images\*.png) aus einer erfundenen Demo-Bibliothek.
#
# Ablauf:
#   1. Legt unter -WorkDir eine eigene Ablage an (config.json, hdr-support.json, art\custom, icons): 14 erfundene Spiele
#      aus Steam, Epic, GOG, Xbox, EA, Battle.net und einem eigenen Eintrag, mit selbst gezeichneten, abstrakten Bildern
#      (Titelbild 1920×620, Logo, Cover 600×900, Symbol 96 px; System.Drawing, Verläufe und Formen, der erfundene Titel als
#      Schriftzug). Die Bilder gelten als eigene Bildwahl („custom:“), wie nach „Bild wählen…“ → „Eigene Datei…“.
#   2. Rendert die Fenster unsichtbar mit dem Bildschirmfoto-Modus der App (--snapshot, E54/E89, nur mit --config-dir) auf
#      Englisch, dunkel, in fester Größe (Standard 1600×900) und mit festen Werten eines erfundenen Bildschirms
#      (--snapshot-display). Zwei Durchgänge: ohne und mit „Colors follow the game“ (E94).
#   3. Prüft jeden Durchgang: Exit-Code 0, die Ablage enthält danach genau die Demo-Spiele, das Protokoll zeigt kein
#      Einlesen der Launcher, keine HDR-Schaltung und keinen Fehler, art\index.json kennt kein Bild aus einem Launcher-Cache.
#   4. Verkleinert die gewählten Bilder (Standard 1280 px breit), bringt sie auf 256 Farben mit Fehlerstreuung (je Bild rund
#      200 bis 300 KB statt rund 1 MB) und legt sie unter festen englischen Namen in -OutDir ab (Liste unten bei den Zuständen).
# Die Pixelgröße ist fest; wie groß Schrift und Bedienelemente darin wirken, hängt von der Anzeigeskalierung des Rechners ab
# (das Fenster bekommt 1600×900 Bildschirmpixel). Die Datumsangaben („today“, „yesterday“) gelten relativ zum Lauf.
#
# Nie berührt: die echte Ablage (%APPDATA%\HDRpilot, %LOCALAPPDATA%\HDRpilot), die installierte App und echte Spiele. Das
# Skript bricht ab, wenn -WorkDir darauf zeigt; die App selbst rendert nur mit eigener Ablage. Im Bildschirmfoto-Modus startet
# weder die Überwachung noch das Einlesen (App.xaml.cs, RunSnapshotModeAsync). Die globale Zielanzeige der Demo ist ein
# Bildschirm, den es nicht gibt: Die Fenster nennen so keinen echten Bildschirm, und HDR wird nie geschaltet. IDs und Pfade
# sind erfunden (Steam 99902x0, weit über den vergebenen App-IDs); fände der Bilderdienst trotzdem ein Bild in einem
# Launcher-Cache dieses Rechners, bricht Schritt 3 ab.
#
# Aufruf (Windows PowerShell 5.1, aus dem Projektordner):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\readme-screenshots.ps1
#   … -NoCopy                  nur rendern und prüfen, nichts nach docs\images kopieren
#   … -AppExe <HDRpilot.exe>   andere Programmdatei (Standard: Debug-, sonst Release-Ausgabe; fehlt beides, wird gedrosselt gebaut)
#   README_SCREENSHOTS_OK
param(
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'hdrpilot-readme-screenshots'),
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\images'),
    [string]$AppExe = '',
    [string]$Size = '1600x900',
    [int]$OutputWidth = 1280,
    [switch]$NoCopy
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Note([string]$text) { Write-Host ("{0:HH:mm:ss} {1}" -f (Get-Date), $text) }

# --- Sicherheit: nie die echte Ablage ---
# Verglichen wird die Schreibweise und der endgültige Pfad (8.3-Namen, Junctions und subst aufgelöst, wie
# PathUtil.SameDirectory der App), damit auch ein anderer Weg zur echten Ablage auffällt.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class HdrPilotReadmePath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr handle, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    // Endgültiger Pfad eines vorhandenen Ordners; null, wenn er sich nicht öffnen lässt. Nur Attribute, kein Lesen.
    public static string Final(string directory)
    {
        var handle = CreateFileW(directory, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var text = new StringBuilder(1024);
            uint length = GetFinalPathNameByHandleW(handle, text, (uint)text.Capacity, 0);
            if (length == 0 || length >= text.Capacity)
            {
                return null;
            }

            var path = text.ToString();
            if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4);
            return path;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
'@
function Full([string]$path) { [IO.Path]::GetFullPath($path).TrimEnd('\') }
function Resolved([string]$path) {
    # Endgültiger Pfad des nächsten vorhandenen Ordners, dahinter der noch nicht vorhandene Rest.
    $current = Full $path
    $tail = @()
    while ($current -and -not (Test-Path -LiteralPath $current -PathType Container)) {
        $tail = @(Split-Path $current -Leaf) + $tail
        $current = Split-Path $current -Parent
    }
    if (-not $current) { return (Full $path) }
    $final = [HdrPilotReadmePath]::Final($current)
    if (-not $final) { $final = $current }
    foreach ($part in $tail) { $final = Join-Path $final $part }
    return (Full $final)
}
function Is-Inside([string]$path, [string]$folder) {
    $p = (Full $path) + '\'; $f = (Full $folder) + '\'
    return $p.StartsWith($f, [StringComparison]::OrdinalIgnoreCase)
}
# True, wenn einer der Ordner im anderen liegt oder beide gleich sind, in Schreibweise oder endgültigem Pfad.
function Overlaps([string]$a, [string]$b) {
    foreach ($x in @((Full $a), (Resolved $a))) {
        foreach ($y in @((Full $b), (Resolved $b))) {
            if ((Is-Inside $x $y) -or (Is-Inside $y $x)) { return $true }
        }
    }
    return $false
}
$WorkDir = Full $WorkDir
$real = @(
    (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'HDRpilot'),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HDRpilot'),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\HDRpilot'))
foreach ($r in $real) {
    if (Overlaps $WorkDir $r) { throw "WorkDir zeigt auf die echte Ablage oder enthält sie: $WorkDir" }
}
if (Overlaps $WorkDir $root) { throw "WorkDir und Projektordner überschneiden sich; Arbeitsdateien gehören nicht ins Repo: $WorkDir" }
$marker = Join-Path $WorkDir '.hdrpilot-readme-demo'
if (Test-Path -LiteralPath $WorkDir) {
    $content = @(Get-ChildItem -LiteralPath $WorkDir -Force)
    if ($content.Count -gt 0 -and -not (Test-Path -LiteralPath $marker)) { throw "WorkDir ist nicht leer und stammt nicht von diesem Skript: $WorkDir" }
    Remove-Item -LiteralPath $WorkDir -Recurse -Force
}
$demo = Join-Path $WorkDir 'demo'
$shots = Join-Path $WorkDir 'shots'
New-Item -ItemType Directory -Force $demo, $shots, (Join-Path $demo 'art\custom'), (Join-Path $demo 'icons') | Out-Null
[IO.File]::WriteAllText($marker, 'Arbeitsordner von tools\readme-screenshots.ps1', (New-Object Text.UTF8Encoding $false))

# --- Programmdatei ---
if (-not $AppExe) {
    $candidates = @(
        (Join-Path $root 'src\HdrPilot.App\bin\Debug\net10.0-windows\HDRpilot.exe'),
        (Join-Path $root 'src\HdrPilot.App\bin\Release\net10.0-windows\win-x64\HDRpilot.exe'))
    $AppExe = @($candidates | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
    if (-not $AppExe) {
        # Gedrosselt bauen (halbe Kerne, niedrige Priorität), danach den Build-Server beenden.
        Note 'Keine gebaute App gefunden: baue Debug (gedrosselt)'
        $env:DOTNET_PROCESSOR_COUNT = '12'
        $build = Start-Process dotnet -ArgumentList @('build', "`"$(Join-Path $root 'src\HdrPilot.App\HdrPilot.App.csproj')`"", '-c', 'Debug', '-m:1', '-nologo', '-v:q') -NoNewWindow -PassThru
        $null = $build.Handle   # sonst fehlt der Exit-Code nach WaitForExit
        try { $build.PriorityClass = 'BelowNormal' } catch {}
        $build.WaitForExit()
        & dotnet build-server shutdown | Out-Null
        if ($build.ExitCode -ne 0) { throw 'Build fehlgeschlagen' }
        $AppExe = $candidates[0]
    }
}
if (-not (Test-Path -LiteralPath $AppExe)) { throw "Programmdatei fehlt: $AppExe" }
Note "App: $AppExe ($((Get-Item -LiteralPath $AppExe).VersionInfo.ProductVersion))"

# --- Bilder zeichnen (System.Drawing) ---
Add-Type -ReferencedAssemblies System.Drawing, System.IO.Compression -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;

// Abstrakte Bilder für erfundene Spiele: Himmel, Sonne, Planeten, Berge, Wald, Meer, Stadt, Dünen, Kristalle. Alles aus
// Verläufen und Formen, gesteuert über eine Farbpalette und einen festen Zufallswert (gleiche Eingaben, gleiche Bilder).
public static class HdrPilotReadmeArt
{
    public const int HeroWidth = 1920;
    public const int HeroHeight = 620;
    public const int CoverWidth = 600;
    public const int CoverHeight = 900;
    public const int IconSize = 96;

    private sealed class Palette
    {
        public Color Deep;
        public Color Glow;
        public Color Accent;
        public Color Dark;
    }

    private static Palette ToPalette(string[] hex)
    {
        var p = new Palette();
        p.Deep = ColorTranslator.FromHtml(hex[0]);
        p.Glow = ColorTranslator.FromHtml(hex[1]);
        p.Accent = ColorTranslator.FromHtml(hex[2]);
        p.Dark = ColorTranslator.FromHtml(hex[3]);
        return p;
    }

    private static Color A(Color c, int alpha)
    {
        return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
    }

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Max(0.0, Math.Min(1.0, t));
        return Color.FromArgb(
            (int)Math.Round(a.A + (b.A - a.A) * t),
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static float F(Random rnd)
    {
        return (float)rnd.NextDouble();
    }

    private static void Quality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    private static LinearGradientBrush VerticalBrush(RectangleF r, Color[] colors, float[] positions)
    {
        var bounds = new RectangleF(r.X, r.Y - 1, Math.Max(1f, r.Width), Math.Max(1f, r.Height + 2));
        var b = new LinearGradientBrush(bounds, colors[0], colors[colors.Length - 1], 90f);
        var blend = new ColorBlend(colors.Length);
        blend.Colors = colors;
        blend.Positions = positions;
        b.InterpolationColors = blend;
        b.WrapMode = WrapMode.TileFlipXY;
        return b;
    }

    private static void Vertical(Graphics g, RectangleF r, Color[] colors, float[] positions)
    {
        using (var b = VerticalBrush(r, colors, positions))
        {
            g.FillRectangle(b, r);
        }
    }

    private static void Glow(Graphics g, float cx, float cy, float rx, float ry, Color c, int alpha)
    {
        if (rx < 1 || ry < 1)
        {
            return;
        }

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
            using (var b = new PathGradientBrush(path))
            {
                b.CenterPoint = new PointF(cx, cy);
                b.CenterColor = A(c, alpha);
                b.SurroundColors = new[] { A(c, 0) };
                var blend = new Blend(4);
                blend.Factors = new[] { 0f, 0.15f, 0.5f, 1f };
                blend.Positions = new[] { 0f, 0.35f, 0.7f, 1f };
                b.Blend = blend;
                g.FillPath(b, path);
            }
        }
    }

    private static void Disc(Graphics g, float cx, float cy, float r, Color top, Color bottom, float angle)
    {
        var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        using (var b = new LinearGradientBrush(rect, top, bottom, angle))
        {
            b.WrapMode = WrapMode.TileFlipXY;
            g.FillEllipse(b, rect);
        }
    }

    private static void Mist(Graphics g, RectangleF r, float y, float height, Color c, int alpha)
    {
        Vertical(g, new RectangleF(r.Left, y - height, r.Width, height * 2), new[] { A(c, 0), A(c, alpha), A(c, 0) }, new[] { 0f, 0.5f, 1f });
    }

    private static void Stars(Graphics g, RectangleF area, Random rnd, int count, Color tint)
    {
        for (int i = 0; i < count; i++)
        {
            float x = area.Left + F(rnd) * area.Width;
            float y = area.Top + F(rnd) * area.Height;
            float s = (float)(0.8 + Math.Pow(rnd.NextDouble(), 3) * 2.6);
            var c = Mix(Color.White, tint, rnd.NextDouble() * 0.5);
            using (var b = new SolidBrush(A(c, 90 + rnd.Next(150))))
            {
                g.FillEllipse(b, x, y, s, s);
            }

            if (s > 2.8f)
            {
                Glow(g, x + s / 2, y + s / 2, s * 4, s * 4, c, 90);
            }
        }
    }

    // Grat aus Mittelpunktverschiebung, Höhen 0..1.
    private static float[] Ridge(Random rnd, double roughness)
    {
        const int size = 64;
        var h = new double[size + 1];
        h[0] = rnd.NextDouble();
        h[size] = rnd.NextDouble();
        double scale = 0.9;
        for (int step = size; step > 1; step /= 2)
        {
            for (int i = 0; i < size; i += step)
            {
                h[i + step / 2] = (h[i] + h[i + step]) / 2 + (rnd.NextDouble() - 0.5) * scale;
            }

            scale *= roughness;
        }

        double min = h[0], max = h[0];
        foreach (var v in h)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var result = new float[size + 1];
        for (int i = 0; i <= size; i++)
        {
            result[i] = (float)((h[i] - min) / Math.Max(1e-6, max - min));
        }

        return result;
    }

    private static void Mountains(Graphics g, RectangleF r, Random rnd, float baseY, float amplitude, double roughness, Color top, Color bottom)
    {
        var h = Ridge(rnd, roughness);
        var pts = new PointF[h.Length + 2];
        pts[0] = new PointF(r.Left - 4, r.Bottom + 4);
        for (int i = 0; i < h.Length; i++)
        {
            pts[i + 1] = new PointF(r.Left - 4 + (r.Width + 8) * i / (h.Length - 1), baseY - h[i] * amplitude);
        }

        pts[h.Length + 1] = new PointF(r.Right + 4, r.Bottom + 4);
        var bounds = new RectangleF(r.Left, baseY - amplitude - 2, r.Width, Math.Max(4f, r.Bottom - baseY + amplitude + 6));
        using (var b = new LinearGradientBrush(bounds, top, bottom, 90f))
        {
            b.WrapMode = WrapMode.TileFlipXY;
            g.FillPolygon(b, pts);
        }
    }

    // Insel: Grat mit einer Hüllkurve, die zu den Rändern auf null fällt.
    private static void Island(Graphics g, Random rnd, float cx, float baseY, float width, float height, Color c)
    {
        var h = Ridge(rnd, 0.6);
        var pts = new List<PointF>();
        for (int i = 0; i < h.Length; i++)
        {
            double t = (double)i / (h.Length - 1);
            float env = (float)Math.Sin(Math.PI * t);
            pts.Add(new PointF(cx - width / 2 + width * (float)t, baseY - (0.35f + 0.65f * h[i]) * height * env));
        }

        pts.Add(new PointF(cx + width / 2, baseY + 1));
        pts.Add(new PointF(cx - width / 2, baseY + 1));
        using (var b = new SolidBrush(c))
        {
            g.FillPolygon(b, pts.ToArray());
        }
    }

    private static void Pine(Graphics g, float x, float baseY, float h, Color c)
    {
        float w = h * 0.42f;
        using (var b = new SolidBrush(c))
        {
            g.FillRectangle(b, x - w * 0.04f, baseY - h * 0.12f, w * 0.08f, h * 0.14f);
            for (int k = 0; k < 4; k++)
            {
                float top = baseY - h * (1f - 0.2f * k);
                float bottom = top + h * 0.4f;
                float half = w * 0.5f * (0.42f + 0.19f * k);
                g.FillPolygon(b, new[] { new PointF(x, top), new PointF(x + half, bottom), new PointF(x - half, bottom) });
            }
        }
    }

    private static void Paint(Graphics g, RectangleF r, int motif, Palette p, Random rnd, bool portrait)
    {
        float fx = r.Left + r.Width * (portrait ? 0.5f : 0.72f);
        float unit = portrait ? r.Width : r.Height;
        switch (((motif % 8) + 8) % 8)
        {
            case 0: Peaks(g, r, p, rnd, fx, unit, portrait); break;
            case 1: Orbit(g, r, p, rnd, fx, unit, portrait); break;
            case 2: Synth(g, r, p, rnd, fx, unit, portrait); break;
            case 3: Ocean(g, r, p, rnd, fx, unit, portrait); break;
            case 4: Forest(g, r, p, rnd, fx, unit, portrait); break;
            case 5: Shards(g, r, p, rnd, fx, unit, portrait); break;
            case 6: City(g, r, p, rnd, fx, unit, portrait); break;
            default: Dunes(g, r, p, rnd, fx, unit, portrait); break;
        }
    }

    // 0: Berge im Gegenlicht.
    private static void Peaks(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        float horizon = r.Top + r.Height * (portrait ? 0.55f : 0.6f);
        float hp = (horizon - r.Top) / r.Height;
        Vertical(g, r, new[] { p.Deep, Mix(p.Deep, p.Glow, 0.55), p.Glow, Mix(p.Glow, p.Dark, 0.5) }, new[] { 0f, hp * 0.6f, hp, 1f });
        Stars(g, new RectangleF(r.Left, r.Top, r.Width, r.Height * 0.3f), rnd, (int)(r.Width * r.Height / 9000), p.Accent);
        float sy = horizon - unit * 0.1f;
        Glow(g, fx, sy, unit * 1.1f, unit * 0.7f, p.Glow, 190);
        Glow(g, fx, sy, unit * 0.36f, unit * 0.36f, p.Accent, 220);
        Disc(g, fx, sy, unit * 0.12f, Mix(p.Accent, Color.White, 0.55), p.Accent, 90f);
        const int layers = 4;
        for (int i = 0; i < layers; i++)
        {
            double t = (i + 1.0) / layers;
            float baseY = horizon + (r.Bottom - horizon) * (float)(t * 0.9 - 0.2);
            float amp = r.Height * (0.2f - 0.03f * i);
            var top = Mix(Mix(p.Glow, p.Deep, 0.5), p.Dark, t * 0.9);
            Mountains(g, r, rnd, baseY, amp, 0.55, top, Mix(top, p.Dark, 0.6));
            if (i < layers - 1)
            {
                Mist(g, r, baseY, r.Height * 0.05f, p.Glow, 60);
            }
        }
    }

    // 1: Planet mit Ring im Nebel.
    private static void Orbit(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        Vertical(g, r, new[] { p.Dark, p.Deep, Mix(p.Deep, p.Dark, 0.6) }, new[] { 0f, 0.55f, 1f });
        for (int i = 0; i < 6; i++)
        {
            Glow(g, r.Left + F(rnd) * r.Width, r.Top + F(rnd) * r.Height, unit * (0.4f + F(rnd) * 0.6f), unit * (0.25f + F(rnd) * 0.4f), i % 2 == 0 ? p.Glow : p.Accent, 55 + rnd.Next(45));
        }

        Stars(g, r, rnd, (int)(r.Width * r.Height / 2600), p.Accent);
        float cy = r.Top + r.Height * (portrait ? 0.36f : 0.52f);
        float radius = unit * (portrait ? 0.3f : 0.4f);
        var ring = new RectangleF(-radius * 1.9f, -radius * 0.42f, radius * 3.8f, radius * 0.84f);
        var outer = RectangleF.Inflate(ring, radius * 0.16f, radius * 0.05f);
        var state = g.Save();
        g.TranslateTransform(fx, cy);
        g.RotateTransform(-14f);
        using (var pen = new Pen(A(p.Accent, 130), radius * 0.07f))
        {
            g.DrawArc(pen, ring, 180, 180);
        }

        using (var pen = new Pen(A(p.Glow, 80), radius * 0.025f))
        {
            g.DrawArc(pen, outer, 180, 180);
        }

        g.Restore(state);
        Glow(g, fx, cy, radius * 1.6f, radius * 1.6f, p.Glow, 120);
        Disc(g, fx, cy, radius, Mix(p.Accent, Color.White, 0.25), p.Deep, 60f);
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(fx - radius, cy - radius, radius * 2, radius * 2);
            var inner = g.Save();
            g.SetClip(clip);
            for (int i = 0; i < 7; i++)
            {
                float y = cy - radius + 2 * radius * (i + 0.5f) / 7f + (F(rnd) - 0.5f) * radius * 0.1f;
                using (var b = new SolidBrush(A(i % 2 == 0 ? p.Glow : p.Dark, 50)))
                {
                    g.FillEllipse(b, fx - radius * 1.3f, y - radius * 0.06f, radius * 2.6f, radius * (0.1f + F(rnd) * 0.08f));
                }
            }

            using (var b = new LinearGradientBrush(new RectangleF(fx - radius, cy - radius, radius * 2, radius * 2), A(p.Dark, 0), A(p.Dark, 235), 35f))
            {
                b.WrapMode = WrapMode.TileFlipXY;
                g.FillEllipse(b, fx - radius, cy - radius, radius * 2, radius * 2);
            }

            g.Restore(inner);
        }

        state = g.Save();
        g.TranslateTransform(fx, cy);
        g.RotateTransform(-14f);
        using (var pen = new Pen(A(p.Accent, 200), radius * 0.07f))
        {
            g.DrawArc(pen, ring, 0, 180);
        }

        using (var pen = new Pen(A(p.Glow, 110), radius * 0.025f))
        {
            g.DrawArc(pen, outer, 0, 180);
        }

        g.Restore(state);
        float mx = fx + (portrait ? -radius * 0.9f : -radius * 2.1f);
        float my = cy - radius * (portrait ? 1.25f : 0.85f);
        Glow(g, mx, my, radius * 0.4f, radius * 0.4f, p.Accent, 90);
        Disc(g, mx, my, radius * 0.12f, Mix(p.Accent, Color.White, 0.6), Mix(p.Deep, p.Accent, 0.4), 45f);
    }

    // 2: Sonne mit Streifen über einem Leuchtgitter.
    private static void Synth(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        float horizon = r.Top + r.Height * (portrait ? 0.56f : 0.64f);
        var sky = new RectangleF(r.Left, r.Top, r.Width, horizon - r.Top);
        var skyColors = new[] { p.Dark, p.Deep, Mix(p.Deep, p.Glow, 0.7) };
        var skyStops = new[] { 0f, 0.5f, 1f };
        Vertical(g, sky, skyColors, skyStops);
        Stars(g, new RectangleF(r.Left, r.Top, r.Width, sky.Height * 0.6f), rnd, (int)(r.Width * r.Height / 6000), p.Accent);
        float radius = unit * (portrait ? 0.3f : 0.36f);
        float cy = horizon - radius * 0.45f;
        Glow(g, fx, cy, radius * 2.4f, radius * 1.6f, p.Glow, 150);
        var state = g.Save();
        g.SetClip(sky);
        Disc(g, fx, cy, radius, p.Accent, p.Glow, 90f);
        using (var b = VerticalBrush(sky, skyColors, skyStops))
        {
            for (int i = 0; i < 6; i++)
            {
                float y = cy + radius * (0.05f + i * 0.16f);
                float th = radius * (0.025f + i * 0.02f);
                g.FillRectangle(b, fx - radius - 2, y, radius * 2 + 4, th);
            }
        }

        g.Restore(state);
        Mountains(g, new RectangleF(r.Left, r.Top, r.Width, horizon - r.Top + 1), rnd, horizon, r.Height * 0.08f, 0.6, Mix(p.Deep, p.Dark, 0.35), Mix(p.Deep, p.Dark, 0.75));
        var ground = new RectangleF(r.Left, horizon, r.Width, r.Bottom - horizon);
        Vertical(g, ground, new[] { Mix(p.Dark, p.Deep, 0.55), p.Dark }, new[] { 0f, 1f });
        state = g.Save();
        g.SetClip(ground);
        using (var pen = new Pen(A(p.Accent, 150), Math.Max(1.5f, unit * 0.004f)))
        {
            const int rows = 14;
            for (int i = 1; i <= rows; i++)
            {
                float t = (float)i / rows;
                float y = horizon + (r.Bottom - horizon) * t * t;
                pen.Color = A(p.Accent, (int)(50 + 160 * t));
                g.DrawLine(pen, r.Left, y, r.Right, y);
            }

            const int cols = 30;
            float top = r.Width / cols * 0.45f;
            float bottom = r.Width / cols * 3.4f;
            pen.Color = A(p.Accent, 140);
            for (int i = -cols; i <= cols; i++)
            {
                g.DrawLine(pen, fx + i * top, horizon, fx + i * bottom, r.Bottom);
            }
        }

        g.Restore(state);
        Glow(g, fx, horizon, r.Width * 0.55f, r.Height * 0.045f, p.Accent, 170);
    }

    // 3: Mond über dem Meer mit Spiegelung.
    private static void Ocean(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        float horizon = r.Top + r.Height * (portrait ? 0.52f : 0.6f);
        var sky = new RectangleF(r.Left, r.Top, r.Width, horizon - r.Top);
        Vertical(g, sky, new[] { p.Dark, p.Deep, Mix(p.Deep, p.Glow, 0.6), p.Glow }, new[] { 0f, 0.35f, 0.8f, 1f });
        Stars(g, new RectangleF(r.Left, r.Top, r.Width, sky.Height * 0.55f), rnd, (int)(r.Width * r.Height / 7000), p.Accent);
        float my = r.Top + sky.Height * 0.42f;
        float radius = unit * 0.1f;
        Glow(g, fx, my, unit * 0.9f, unit * 0.6f, p.Glow, 140);
        Glow(g, fx, my, radius * 3.2f, radius * 3.2f, p.Accent, 200);
        Disc(g, fx, my, radius, Mix(p.Accent, Color.White, 0.75), Mix(p.Accent, Color.White, 0.25), 90f);
        var island = Mix(p.Deep, p.Dark, 0.55);
        Island(g, rnd, r.Left + r.Width * (portrait ? 0.18f : 0.3f), horizon, unit * (portrait ? 0.5f : 0.9f), unit * 0.09f, island);
        Island(g, rnd, r.Left + r.Width * (portrait ? 0.9f : 0.93f), horizon, unit * 0.45f, unit * 0.05f, island);
        var sea = new RectangleF(r.Left, horizon, r.Width, r.Bottom - horizon);
        Vertical(g, sea, new[] { Mix(p.Glow, p.Deep, 0.55), Mix(p.Deep, p.Dark, 0.4), p.Dark }, new[] { 0f, 0.45f, 1f });
        for (int i = 0; i < 120; i++)
        {
            float y = horizon + 2 + F(rnd) * (r.Bottom - horizon);
            float len = unit * (0.05f + F(rnd) * 0.25f);
            float x = r.Left + F(rnd) * r.Width;
            using (var pen = new Pen(A(p.Glow, 28), 1.2f))
            {
                g.DrawLine(pen, x, y, x + len, y);
            }
        }

        var light = Mix(p.Accent, Color.White, 0.4);
        for (int i = 0; i < 150; i++)
        {
            double t = rnd.NextDouble();
            float y = horizon + 1 + (r.Bottom - horizon) * (float)Math.Pow(t, 1.4);
            float spread = unit * (0.04f + 0.3f * (float)t);
            float x = fx + (F(rnd) - 0.5f) * 2 * spread;
            float len = unit * (0.02f + 0.07f * F(rnd)) * (0.5f + (float)t);
            using (var b = new SolidBrush(A(light, (int)(210 * (1 - t * 0.7)))))
            {
                g.FillRectangle(b, x - len / 2, y, len, Math.Max(1.5f, unit * 0.004f * (1 + (float)t * 2)));
            }
        }
    }

    // 4: Wald im Nebel mit Lichtstrahlen.
    private static void Forest(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        Vertical(g, r, new[] { p.Deep, Mix(p.Deep, p.Glow, 0.6), p.Glow, Mix(p.Glow, p.Dark, 0.3) }, new[] { 0f, 0.32f, 0.6f, 1f });
        float sy = r.Top + r.Height * (portrait ? 0.4f : 0.46f);
        Glow(g, fx, sy, unit * 1.0f, unit * 0.7f, p.Accent, 170);
        Disc(g, fx, sy, unit * 0.075f, Mix(p.Accent, Color.White, 0.65), p.Accent, 90f);
        for (int i = 0; i < 7; i++)
        {
            double a = Math.PI * (0.18 + 0.64 * (i + F(rnd) * 0.5) / 7.0);
            double spread = 0.035 + rnd.NextDouble() * 0.03;
            float len = unit * 3f;
            var p1 = new PointF(fx + (float)Math.Cos(a - spread) * len, sy + (float)Math.Sin(a - spread) * len);
            var p2 = new PointF(fx + (float)Math.Cos(a + spread) * len, sy + (float)Math.Sin(a + spread) * len);
            using (var b = new SolidBrush(A(p.Accent, 20)))
            {
                g.FillPolygon(b, new[] { new PointF(fx, sy), p1, p2 });
            }
        }

        const int rows = 5;
        for (int row = 0; row < rows; row++)
        {
            double t = (row + 1.0) / rows;
            float baseY = r.Top + r.Height * (float)((portrait ? 0.52 : 0.5) + 0.52 * t);
            float treeH = r.Height * (float)((portrait ? 0.12 : 0.18) + (portrait ? 0.22 : 0.3) * t);
            var c = Mix(Mix(p.Glow, p.Deep, 0.4), p.Dark, 0.2 + 0.8 * t);
            float x = r.Left - treeH * 0.3f;
            while (x < r.Right + treeH * 0.3f)
            {
                float th = treeH * (0.6f + F(rnd) * 0.6f);
                if (Math.Abs(x - fx) > unit * 0.12f || row > 1 || F(rnd) < 0.3f)
                {
                    Pine(g, x, baseY + F(rnd) * treeH * 0.08f, th, c);
                }

                x += th * (0.16f + F(rnd) * 0.2f);
            }

            using (var b = new SolidBrush(c))
            {
                g.FillRectangle(b, r.Left, baseY, r.Width, r.Bottom - baseY + 2);
            }

            if (row < rows - 1)
            {
                Mist(g, r, baseY - treeH * 0.15f, treeH * 0.35f, p.Glow, 75);
            }
        }
    }

    // 5: Kristallsplitter um ein helles Zentrum.
    private static void Shards(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        Vertical(g, r, new[] { p.Dark, p.Deep, p.Dark }, new[] { 0f, 0.55f, 1f });
        float cy = r.Top + r.Height * (portrait ? 0.38f : 0.5f);
        Glow(g, fx, cy, unit * 1.3f, unit * 0.95f, p.Glow, 150);
        Stars(g, r, rnd, (int)(r.Width * r.Height / 8000), p.Accent);
        var light = Mix(p.Accent, Color.White, 0.3);
        var edge = Mix(p.Accent, Color.White, 0.6);
        for (int i = 0; i < 34; i++)
        {
            double angle = rnd.NextDouble() * Math.PI * 2;
            float len = unit * (0.22f + F(rnd) * 0.55f);
            float wid = len * (0.07f + F(rnd) * 0.1f);
            float off = unit * F(rnd) * 0.12f;
            var dir = new PointF((float)Math.Cos(angle), (float)Math.Sin(angle));
            var per = new PointF(-dir.Y, dir.X);
            var b0 = new PointF(fx + dir.X * off, cy + dir.Y * off);
            var tip = new PointF(b0.X + dir.X * len, b0.Y + dir.Y * len);
            var pts = new[]
            {
                new PointF(b0.X + per.X * wid, b0.Y + per.Y * wid),
                tip,
                new PointF(b0.X - per.X * wid, b0.Y - per.Y * wid),
                new PointF(b0.X - dir.X * wid * 0.8f, b0.Y - dir.Y * wid * 0.8f),
            };
            using (var b = new LinearGradientBrush(b0, tip, A(light, 205), A(p.Deep, 110)))
            {
                b.WrapMode = WrapMode.TileFlipXY;
                g.FillPolygon(b, pts);
            }

            using (var pen = new Pen(A(edge, 110), 1.4f))
            {
                g.DrawPolygon(pen, pts);
            }
        }

        Glow(g, fx, cy, unit * 0.28f, unit * 0.28f, Color.White, 170);
        Glow(g, fx, cy, unit * 0.6f, unit * 0.6f, p.Accent, 120);
    }

    // 6: Stadt in der Dämmerung mit erleuchteten Fenstern.
    private static void City(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        float horizon = r.Top + r.Height * (portrait ? 0.74f : 0.84f);
        float hp = (horizon - r.Top) / r.Height;
        Vertical(g, r, new[] { p.Dark, p.Deep, Mix(p.Deep, p.Glow, 0.7), p.Glow, p.Dark }, new[] { 0f, hp * 0.35f, hp * 0.75f, hp, 1f });
        Stars(g, new RectangleF(r.Left, r.Top, r.Width, r.Height * 0.35f), rnd, (int)(r.Width * r.Height / 9000), p.Accent);
        float my = r.Top + r.Height * (portrait ? 0.22f : 0.26f);
        Glow(g, fx, my, unit * 0.5f, unit * 0.5f, p.Accent, 150);
        Disc(g, fx, my, unit * 0.085f, Mix(p.Accent, Color.White, 0.7), p.Accent, 90f);
        for (int row = 0; row < 3; row++)
        {
            double t = row / 2.0;
            var c = Mix(Mix(p.Glow, p.Deep, 0.55), p.Dark, 0.3 + 0.7 * t);
            float baseY = horizon + r.Height * 0.05f * row;
            float maxH = r.Height * ((portrait ? 0.42f : 0.55f) - 0.1f * row);
            float x = r.Left - 10;
            while (x < r.Right)
            {
                float bw = unit * (0.04f + F(rnd) * 0.08f) * (1 + row * 0.35f);
                float bh = maxH * (0.25f + F(rnd) * 0.75f);
                using (var b = new SolidBrush(c))
                {
                    g.FillRectangle(b, x, baseY - bh, bw, bh + r.Height);
                    if (F(rnd) < 0.25f)
                    {
                        g.FillRectangle(b, x + bw * 0.45f, baseY - bh - unit * 0.05f, Math.Max(1.5f, bw * 0.06f), unit * 0.05f);
                    }
                }

                if (row > 0)
                {
                    float step = Math.Max(5f, unit * 0.018f);
                    using (var lit = new SolidBrush(A(p.Accent, 120 + 60 * row)))
                    {
                        for (float wy = baseY - bh + step; wy < baseY - step; wy += step)
                        {
                            for (float wx = x + step * 0.5f; wx < x + bw - step * 0.6f; wx += step)
                            {
                                if (F(rnd) < 0.22f)
                                {
                                    g.FillRectangle(lit, wx, wy, step * 0.45f, step * 0.55f);
                                }
                            }
                        }
                    }
                }

                x += bw + unit * 0.004f;
            }

            if (row < 2)
            {
                Mist(g, r, baseY - maxH * 0.15f, maxH * 0.25f, p.Glow, 55);
            }
        }
    }

    // 7: Dünen vor einer großen Sonne.
    private static void Dunes(Graphics g, RectangleF r, Palette p, Random rnd, float fx, float unit, bool portrait)
    {
        float horizon = r.Top + r.Height * (portrait ? 0.5f : 0.58f);
        float hp = (horizon - r.Top) / r.Height;
        Vertical(g, r, new[] { p.Deep, Mix(p.Deep, p.Glow, 0.5), p.Glow, Mix(p.Glow, p.Accent, 0.4) }, new[] { 0f, hp * 0.5f, hp, 1f });
        Stars(g, new RectangleF(r.Left, r.Top, r.Width, r.Height * 0.25f), rnd, (int)(r.Width * r.Height / 12000), p.Accent);
        float sy = horizon - unit * 0.06f;
        Glow(g, fx, sy, unit * 1.2f, unit * 0.8f, p.Glow, 190);
        Glow(g, fx, sy, unit * 0.45f, unit * 0.45f, p.Accent, 200);
        Disc(g, fx, sy, unit * 0.2f, Mix(p.Accent, Color.White, 0.5), p.Accent, 90f);
        for (int i = 0; i < 4; i++)
        {
            double t = (i + 1.0) / 4;
            float baseY = horizon + (r.Bottom - horizon) * (float)(t - 0.3);
            float amp = r.Height * 0.12f;
            var pts = new List<PointF>();
            const int n = 5;
            for (int k = 0; k <= n; k++)
            {
                pts.Add(new PointF(r.Left - 60 + (r.Width + 120) * k / n, baseY - F(rnd) * amp));
            }

            using (var path = new GraphicsPath())
            {
                path.AddCurve(pts.ToArray(), 0.55f);
                path.AddLine(r.Right + 60, baseY, r.Right + 60, r.Bottom + 10);
                path.AddLine(r.Right + 60, r.Bottom + 10, r.Left - 60, r.Bottom + 10);
                path.CloseFigure();
                var lit = Mix(Mix(p.Glow, p.Accent, 0.25), p.Dark, 0.15 + t * 0.6);
                var shade = Mix(Mix(p.Deep, p.Dark, 0.35), p.Dark, t * 0.5);
                using (var b = new LinearGradientBrush(new RectangleF(r.Left, baseY - amp - 2, r.Width, r.Bottom - baseY + amp + 14), lit, shade, 90f))
                {
                    b.WrapMode = WrapMode.TileFlipXY;
                    g.FillPath(b, path);
                }
            }
        }
    }

    private static FontFamily Family(string name)
    {
        foreach (var f in FontFamily.Families)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }

        return new FontFamily("Segoe UI");
    }

    private static FontStyle StyleFor(FontFamily f, bool bold)
    {
        if (bold && f.IsStyleAvailable(FontStyle.Bold)) return FontStyle.Bold;
        if (f.IsStyleAvailable(FontStyle.Regular)) return FontStyle.Regular;
        return FontStyle.Bold;
    }

    // Lange Titel in zwei Zeilen, am Leerzeichen nahe der Mitte.
    private static string[] SplitTitle(string title)
    {
        if (title.Length <= 13 || title.IndexOf(' ') < 0)
        {
            return new[] { title };
        }

        int best = -1;
        double mid = title.Length / 2.0;
        for (int i = 0; i < title.Length; i++)
        {
            if (title[i] == ' ' && (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid)))
            {
                best = i;
            }
        }

        return new[] { title.Substring(0, best), title.Substring(best + 1) };
    }

    private static GraphicsPath TitlePath(string[] lines, FontFamily family, FontStyle style, float em, float gap)
    {
        var parts = new List<GraphicsPath>();
        float maxW = 0, capH = 0;
        foreach (var line in lines)
        {
            var lp = new GraphicsPath();
            lp.AddString(line, family, (int)style, em, new PointF(0, 0), StringFormat.GenericTypographic);
            var b = lp.GetBounds();
            maxW = Math.Max(maxW, b.Width);
            capH = Math.Max(capH, b.Height);
            parts.Add(lp);
        }

        var result = new GraphicsPath();
        float y = 0;
        foreach (var lp in parts)
        {
            var b = lp.GetBounds();
            using (var m = new Matrix())
            {
                m.Translate((maxW - b.Width) / 2 - b.X, y - b.Y);
                lp.Transform(m);
            }

            result.AddPath(lp, false);
            y += capH + gap;
            lp.Dispose();
        }

        return result;
    }

    private static void PaintTitle(Graphics g, GraphicsPath path, Color top, Color bottom, Color outline, float outlineWidth, float shadowOffset)
    {
        var b = path.GetBounds();
        using (var shadow = (GraphicsPath)path.Clone())
        using (var m = new Matrix())
        {
            m.Translate(shadowOffset * 0.6f, shadowOffset);
            shadow.Transform(m);
            using (var sb = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                g.FillPath(sb, shadow);
            }
        }

        using (var pen = new Pen(outline, outlineWidth))
        {
            pen.LineJoin = LineJoin.Round;
            g.DrawPath(pen, path);
        }

        using (var fill = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1, Math.Max(1f, b.Width), b.Height + 2), top, bottom, 90f))
        {
            fill.WrapMode = WrapMode.TileFlipXY;
            g.FillPath(fill, path);
        }
    }

    private static void SaveJpeg(Bitmap bmp, string path, long quality)
    {
        ImageCodecInfo codec = null;
        foreach (var c in ImageCodecInfo.GetImageEncoders())
        {
            if (c.MimeType == "image/jpeg")
            {
                codec = c;
            }
        }

        using (var parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bmp.Save(path, codec, parameters);
        }
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Hero(string path, int motif, string[] palette, int seed)
    {
        var p = ToPalette(palette);
        using (var bmp = new Bitmap(HeroWidth, HeroHeight, PixelFormat.Format24bppRgb))
        using (var g = Graphics.FromImage(bmp))
        {
            Quality(g);
            var r = new RectangleF(0, 0, HeroWidth, HeroHeight);
            Paint(g, r, motif, p, new Random(seed), false);
            // Links etwas dunkler: Dort steht in der App das Logo.
            using (var b = new LinearGradientBrush(new RectangleF(-1, 0, HeroWidth * 0.5f + 2, HeroHeight), A(p.Dark, 120), A(p.Dark, 0), 0f))
            {
                b.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(b, 0, 0, HeroWidth * 0.5f, HeroHeight);
            }

            SaveJpeg(bmp, path, 90L);
        }
    }

    public static void Cover(string path, string title, int motif, string[] palette, int seed, string fontName, bool bold)
    {
        var p = ToPalette(palette);
        using (var bmp = new Bitmap(CoverWidth, CoverHeight, PixelFormat.Format24bppRgb))
        using (var g = Graphics.FromImage(bmp))
        {
            Quality(g);
            var r = new RectangleF(0, 0, CoverWidth, CoverHeight);
            Paint(g, r, motif, p, new Random(seed), true);
            Vertical(g, new RectangleF(0, CoverHeight * 0.55f, CoverWidth, CoverHeight * 0.45f), new[] { A(p.Dark, 0), A(p.Dark, 150), A(p.Dark, 235) }, new[] { 0f, 0.5f, 1f });
            var family = Family(fontName);
            // Titel über dem unteren Sechstel: Dort legt die App in den Kacheln Quelle, Schalter und Kennzeichen darüber.
            using (var tp = TitlePath(SplitTitle(title.ToUpperInvariant()), family, StyleFor(family, bold), 80f, 14f))
            {
                var b = tp.GetBounds();
                float scale = Math.Min(1f, (CoverWidth - 90f) / b.Width);
                using (var m = new Matrix())
                {
                    m.Scale(scale, scale);
                    tp.Transform(m);
                }

                b = tp.GetBounds();
                using (var m = new Matrix())
                {
                    m.Translate((CoverWidth - b.Width) / 2 - b.X, CoverHeight * 0.83f - b.Height - b.Y);
                    tp.Transform(m);
                }

                PaintTitle(g, tp, Mix(p.Accent, Color.White, 0.55), p.Accent, A(p.Dark, 230), 5f, 4f);
            }

            SaveJpeg(bmp, path, 90L);
        }
    }

    public static void Logo(string path, string title, string[] palette, string fontName, bool bold)
    {
        var p = ToPalette(palette);
        var family = Family(fontName);
        using (var tp = TitlePath(SplitTitle(title.ToUpperInvariant()), family, StyleFor(family, bold), 150f, 26f))
        {
            var b = tp.GetBounds();
            float scale = Math.Min(1f, 1180f / b.Width);
            using (var m = new Matrix())
            {
                m.Scale(scale, scale);
                tp.Transform(m);
            }

            b = tp.GetBounds();
            const int pad = 28;
            int w = (int)Math.Ceiling(b.Width) + pad * 2;
            int h = (int)Math.Ceiling(b.Height) + pad * 2;
            using (var m = new Matrix())
            {
                m.Translate(pad - b.X, pad - b.Y);
                tp.Transform(m);
            }

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                Quality(g);
                g.Clear(Color.Transparent);
                PaintTitle(g, tp, Mix(p.Accent, Color.White, 0.55), p.Accent, A(Mix(p.Dark, p.Deep, 0.3), 235), 9f, 7f);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    public static void Icon(string path, string title, string[] palette)
    {
        var p = ToPalette(palette);
        var words = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string initials = words.Length > 1 ? words[0].Substring(0, 1) + words[1].Substring(0, 1) : title.Substring(0, Math.Min(2, title.Length));
        using (var bmp = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            Quality(g);
            g.Clear(Color.Transparent);
            var rect = new RectangleF(2, 2, IconSize - 4, IconSize - 4);
            using (var rr = Rounded(rect, 20f))
            {
                using (var b = new LinearGradientBrush(rect, Mix(p.Glow, p.Deep, 0.25), p.Deep, 60f))
                {
                    b.WrapMode = WrapMode.TileFlipXY;
                    g.FillPath(b, rr);
                }

                var state = g.Save();
                g.SetClip(rr);
                Glow(g, rect.Right * 0.72f, rect.Top + rect.Height * 0.28f, 54, 54, p.Accent, 170);
                g.Restore(state);
            }

            var family = Family("Segoe UI Semibold");
            using (var tp = TitlePath(new[] { initials.ToUpperInvariant() }, family, StyleFor(family, false), 44f, 0f))
            {
                var b = tp.GetBounds();
                using (var m = new Matrix())
                {
                    m.Translate((IconSize - b.Width) / 2 - b.X, (IconSize - b.Height) / 2 - b.Y);
                    tp.Transform(m);
                }

                PaintTitle(g, tp, Color.White, Mix(p.Accent, Color.White, 0.6), A(p.Dark, 150), 2f, 1.5f);
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }

}

// Bildschirmfotos für die README klein halten: bikubisch auf die Zielbreite verkleinern, auf 256 Farben bringen (Median-Cut
// über ein 15-Bit-Histogramm, mit k-Means nachgeschärft, Fehlerstreuung nach Floyd-Steinberg gegen Streifen in Verläufen) und
// als PNG mit Palette schreiben (eigener Kodierer: der von GDI+ packt deutlich schlechter).
public static class HdrPilotReadmePng
{
    public const double DitherStrength = 0.8;

    public static void Save(string source, string target, int width)
    {
        int w, h;
        byte[] rgb;
        using (var scaled = Scale(source, width))
        {
            w = scaled.Width;
            h = scaled.Height;
            rgb = Rgb(scaled);
        }

        var palette = Palette(rgb, 256);
        var indices = Map(rgb, w, h, palette);
        WriteIndexed(w, h, palette, indices, target);
    }

    private static Bitmap Scale(string source, int width)
    {
        using (var src = new Bitmap(source))
        {
            int w = Math.Min(width, src.Width);
            int h = (int)Math.Round(src.Height * (double)w / src.Width);
            var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            return dst;
        }
    }

    private static byte[] Rgb(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            var rgb = new byte[bmp.Width * bmp.Height * 3];
            for (int y = 0; y < bmp.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, data.Stride);
                for (int x = 0; x < bmp.Width; x++)
                {
                    int o = (y * bmp.Width + x) * 3;
                    rgb[o] = row[x * 3 + 2];
                    rgb[o + 1] = row[x * 3 + 1];
                    rgb[o + 2] = row[x * 3];
                }
            }

            return rgb;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private sealed class Box
    {
        public List<int> Bins;
        public long Weight;
    }

    private static int Channel(int bin, int axis, long[] sumR, long[] sumG, long[] sumB, long[] count)
    {
        return (int)((axis == 0 ? sumR[bin] : axis == 1 ? sumG[bin] : sumB[bin]) / count[bin]);
    }

    private static byte[] Palette(byte[] rgb, int colors)
    {
        var count = new long[32768];
        var sumR = new long[32768];
        var sumG = new long[32768];
        var sumB = new long[32768];
        int n = rgb.Length / 3;
        for (int i = 0; i < n; i++)
        {
            int r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
            int k = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            count[k]++;
            sumR[k] += r;
            sumG[k] += g;
            sumB[k] += b;
        }

        var bins = new List<int>();
        for (int k = 0; k < 32768; k++)
        {
            if (count[k] > 0) bins.Add(k);
        }

        var first = new Box { Bins = bins };
        foreach (var k in bins) first.Weight += count[k];
        var boxes = new List<Box> { first };
        while (boxes.Count < colors)
        {
            Box pick = null;
            double score = -1;
            int axis = 0;
            foreach (var box in boxes)
            {
                if (box.Bins.Count < 2) continue;
                var min = new[] { 255, 255, 255 };
                var max = new[] { 0, 0, 0 };
                foreach (var k in box.Bins)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int c = Channel(k, j, sumR, sumG, sumB, count);
                        if (c < min[j]) min[j] = c;
                        if (c > max[j]) max[j] = c;
                    }
                }

                int ax = 0, range = max[0] - min[0];
                if (max[1] - min[1] > range) { ax = 1; range = max[1] - min[1]; }
                if (max[2] - min[2] > range) { ax = 2; range = max[2] - min[2]; }
                double s = Math.Sqrt(box.Weight) * range;
                if (s > score) { score = s; pick = box; axis = ax; }
            }

            if (pick == null) break;
            int sortAxis = axis;
            pick.Bins.Sort((x, y) =>
            {
                int c = Channel(x, sortAxis, sumR, sumG, sumB, count).CompareTo(Channel(y, sortAxis, sumR, sumG, sumB, count));
                return c != 0 ? c : x.CompareTo(y);
            });
            long half = pick.Weight / 2, acc = 0;
            int cut = 1;
            for (int i = 0; i < pick.Bins.Count - 1; i++)
            {
                acc += count[pick.Bins[i]];
                if (acc >= half) { cut = i + 1; break; }
            }

            var a = new Box { Bins = pick.Bins.GetRange(0, cut) };
            foreach (var k in a.Bins) a.Weight += count[k];
            var b2 = new Box { Bins = pick.Bins.GetRange(cut, pick.Bins.Count - cut) };
            foreach (var k in b2.Bins) b2.Weight += count[k];
            boxes.Remove(pick);
            boxes.Add(a);
            boxes.Add(b2);
        }

        int m = boxes.Count;
        var pr = new double[m];
        var pg = new double[m];
        var pb = new double[m];
        for (int i = 0; i < m; i++)
        {
            long w = 0, r = 0, g = 0, b = 0;
            foreach (var k in boxes[i].Bins) { w += count[k]; r += sumR[k]; g += sumG[k]; b += sumB[k]; }
            pr[i] = (double)r / w;
            pg[i] = (double)g / w;
            pb[i] = (double)b / w;
        }

        for (int iter = 0; iter < 6; iter++)
        {
            var wr = new double[m];
            var wg = new double[m];
            var wb = new double[m];
            var ww = new double[m];
            foreach (var k in bins)
            {
                int best = Nearest(pr, pg, pb, (double)sumR[k] / count[k], (double)sumG[k] / count[k], (double)sumB[k] / count[k]);
                wr[best] += sumR[k];
                wg[best] += sumG[k];
                wb[best] += sumB[k];
                ww[best] += count[k];
            }

            for (int i = 0; i < m; i++)
            {
                if (ww[i] > 0) { pr[i] = wr[i] / ww[i]; pg[i] = wg[i] / ww[i]; pb[i] = wb[i] / ww[i]; }
            }
        }

        var palette = new byte[m * 3];
        for (int i = 0; i < m; i++)
        {
            palette[i * 3] = Clamp(pr[i]);
            palette[i * 3 + 1] = Clamp(pg[i]);
            palette[i * 3 + 2] = Clamp(pb[i]);
        }

        return palette;
    }

    private static byte[] Map(byte[] rgb, int width, int height, byte[] palette)
    {
        int m = palette.Length / 3;
        var pr = new double[m];
        var pg = new double[m];
        var pb = new double[m];
        for (int i = 0; i < m; i++) { pr[i] = palette[i * 3]; pg[i] = palette[i * 3 + 1]; pb[i] = palette[i * 3 + 2]; }
        var cache = new Dictionary<int, byte>();
        var indices = new byte[width * height];
        var err = new double[2, width + 2, 3];
        var e = new double[3];
        for (int y = 0; y < height; y++)
        {
            int cur = y & 1, next = cur ^ 1;
            for (int x = 0; x < width + 2; x++)
            {
                for (int c = 0; c < 3; c++) err[next, x, c] = 0;
            }

            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 3;
                int r = Clamp(rgb[o] + err[cur, x + 1, 0]);
                int g = Clamp(rgb[o + 1] + err[cur, x + 1, 1]);
                int b = Clamp(rgb[o + 2] + err[cur, x + 1, 2]);
                int key = (r << 16) | (g << 8) | b;
                byte index;
                if (!cache.TryGetValue(key, out index))
                {
                    index = (byte)Nearest(pr, pg, pb, r, g, b);
                    cache[key] = index;
                }

                indices[y * width + x] = index;
                e[0] = (r - palette[index * 3]) * DitherStrength;
                e[1] = (g - palette[index * 3 + 1]) * DitherStrength;
                e[2] = (b - palette[index * 3 + 2]) * DitherStrength;
                for (int c = 0; c < 3; c++)
                {
                    err[cur, x + 2, c] += e[c] * 7 / 16;
                    err[next, x, c] += e[c] * 3 / 16;
                    err[next, x + 1, c] += e[c] * 5 / 16;
                    err[next, x + 2, c] += e[c] * 1 / 16;
                }
            }
        }

        return indices;
    }

    private static byte Clamp(double v)
    {
        return (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
    }

    private static int Nearest(double[] pr, double[] pg, double[] pb, double r, double g, double b)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < pr.Length; i++)
        {
            double dr = pr[i] - r, dg = pg[i] - g, db = pb[i] - b;
            double d = dr * dr * 0.3 + dg * dg * 0.59 + db * db * 0.11;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }

        return best;
    }

    private static uint[] crcTable;

    private static uint Crc(byte[] type, byte[] data)
    {
        if (crcTable == null)
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            crcTable = table;
        }

        uint crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static void BigEndian(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        BigEndian(s, (uint)data.Length);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        BigEndian(s, Crc(t, data));
    }

    private static byte[] Zlib(byte[] raw)
    {
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x78);
            ms.WriteByte(0xDA);
            using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            uint a = 1, b = 0;
            foreach (var x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
            BigEndian(ms, (b << 16) | a);
            return ms.ToArray();
        }
    }

    private static void WriteIndexed(int width, int height, byte[] palette, byte[] indices, string path)
    {
        // Zeilen ohne Filter (bei Palettenbildern meist am kleinsten).
        var raw = new byte[(width + 1) * height];
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(indices, y * width, raw, y * (width + 1) + 1, width);
        }

        using (var fs = File.Create(path))
        {
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            using (var ihdr = new MemoryStream())
            {
                BigEndian(ihdr, (uint)width);
                BigEndian(ihdr, (uint)height);
                ihdr.Write(new byte[] { 8, 3, 0, 0, 0 }, 0, 5);
                Chunk(fs, "IHDR", ihdr.ToArray());
            }

            Chunk(fs, "PLTE", palette);
            Chunk(fs, "IDAT", Zlib(raw));
            Chunk(fs, "IEND", new byte[0]);
        }
    }
}
'@

# --- Demo-Bibliothek ---
# Erfundene Titel (keine echten Spiele), erfundene IDs und erfundene Pfade. Motiv: 0 Berge, 1 Planet,
# 2 Leuchtgitter, 3 Meer, 4 Wald, 5 Kristalle, 6 Stadt, 7 Dünen. Palette: tief, Leuchten, Akzent, dunkel. Played: vor so
# vielen Stunden zuletzt gespielt ($null: nie). Hdr: HDR laut PCGamingWiki (hier erfunden).
$games = @(
    @{ Name = 'Saltglass Lighthouse'; Source = 'Steam'; Id = '9990210'; Motif = 3; Pal = '#0d2a4a', '#f08a5d', '#ffd27a', '#071526'; Font = 'Georgia'; Bold = $true; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 2; New = $false }
    @{ Name = 'Starwake Cartographers'; Source = 'Steam'; Id = '9990220'; Motif = 1; Pal = '#1b1446', '#7b5cff', '#7fe7ff', '#07061a'; Font = 'Bahnschrift SemiBold'; Bold = $false; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 26; New = $false }
    @{ Name = 'Neon Kestrel 2087'; Source = 'Epic'; Id = 'kestrel2087'; Motif = 2; Pal = '#2a0b4a', '#ff3d8b', '#ffb347', '#0b0418'; Font = 'Bahnschrift SemiBold Condensed'; Bold = $false; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 74; New = $false }
    @{ Name = 'Hollowpine Wardens'; Source = 'Gog'; Id = '1999990104'; Motif = 4; Pal = '#14301f', '#b9d77a', '#fff2a8', '#06140c'; Font = 'Constantia'; Bold = $true; HdrAuto = $false; AutoHdr = $true; Hdr = 'None'; Played = 120; New = $false }
    @{ Name = 'Ember Vale Rally'; Source = 'Xbox'; Id = 'DemoStudio.EmberValeRally_q4vx2hdemo7kp'; Motif = 7; Pal = '#3a1020', '#ff7a3d', '#ffd36b', '#140508'; Font = 'Segoe UI Black'; Bold = $false; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 170; New = $false }
    @{ Name = 'Shardfall Conclave'; Source = 'Steam'; Id = '9990260'; Motif = 5; Pal = '#0f2a44', '#2fd3c8', '#b8fff6', '#050d18'; Font = 'Trebuchet MS'; Bold = $true; HdrAuto = $false; AutoHdr = $false; Hdr = 'Limited'; Played = 340; New = $false }
    @{ Name = 'Lanternport Nights'; Source = 'Epic'; Id = 'lanternport'; Motif = 6; Pal = '#1a1440', '#ff6f61', '#ffd166', '#0a0818'; Font = 'Franklin Gothic Medium'; Bold = $false; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 500; New = $false }
    @{ Name = 'Quietfrost Expedition'; Source = 'Steam'; Id = '9990280'; Motif = 0; Pal = '#1d3557', '#a8dadc', '#f1faee', '#0b1a2c'; Font = 'Segoe UI Semibold'; Bold = $false; HdrAuto = $false; AutoHdr = $false; Hdr = 'Unknown'; Played = $null; New = $true }
    @{ Name = 'Dune Choir'; Source = 'Gog'; Id = '1999990109'; Motif = 7; Pal = '#2d1b4e', '#f4a259', '#ffe8a3', '#120a1f'; Font = 'Palatino Linotype'; Bold = $true; HdrAuto = $false; AutoHdr = $true; Hdr = 'None'; Played = 820; New = $false }
    @{ Name = 'Copper Comet Derby'; Source = 'EA'; Id = 'Origin.OFR.50.0099910'; Motif = 1; Pal = '#3b1c0c', '#e36414', '#ffba49', '#120804'; Font = 'Impact'; Bold = $false; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 1100; New = $false }
    @{ Name = 'Mosslight Monastery'; Source = 'Xbox'; Id = 'DemoStudio.MosslightMonastery_q4vx2hdemo7kp'; Motif = 4; Pal = '#123c3a', '#6fd1b0', '#e6ffd9', '#061716'; Font = 'Cambria'; Bold = $true; HdrAuto = $false; AutoHdr = $false; Hdr = 'Unknown'; Played = 1500; New = $false }
    @{ Name = 'Glasswing Protocol'; Source = 'BattleNet'; Id = 'glwp'; Motif = 2; Pal = '#04283d', '#00b4d8', '#90f1ff', '#020c14'; Font = 'Consolas'; Bold = $true; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = 2100; New = $false }
    @{ Name = 'Riverglow Express'; Source = 'Manual'; Id = '5f2a9c1b7e3d4a60b8c1d2e3f4a5b6c7'; Motif = 0; Pal = '#3d1e6d', '#ff8fab', '#ffe5a0', '#160b2b'; Font = 'Georgia'; Bold = $true; HdrAuto = $false; AutoHdr = $false; Hdr = 'Unknown'; Played = $null; New = $true }
    @{ Name = 'Thundermoth Arena'; Source = 'Steam'; Id = '9990340'; Motif = 5; Pal = '#2b0f3a', '#c77dff', '#ffe066', '#0d0414'; Font = 'Segoe UI Black'; Bold = $false; HdrAuto = $true; AutoHdr = $false; Hdr = 'Native'; Played = $null; New = $true }
)

# Dateinamen wie die App sie für eigene Bilder und Symbole anlegt (ArtworkFiles.Stem, CustomIcons.FileNameFor).
function Stem([string]$key) {
    $readable = -join ($key.ToCharArray() | ForEach-Object { if ([char]::IsLetterOrDigit($_) -and [int]$_ -lt 128 -or $_ -eq '-' -or $_ -eq '.') { $_ } else { '_' } })
    $readable = $readable.Trim('.')
    if ($readable.Length -gt 80) { $readable = $readable.Substring(0, 80) }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key.ToUpperInvariant())) } finally { $sha.Dispose() }
    return $readable + '_' + (($hash[0..3] | ForEach-Object { $_.ToString('X2') }) -join '')
}

# Installationsordner: alle unter dem erfundenen Ordner D:\Demo Library, nie unter einem der üblichen Ordnernamen der
# Launcher: Die gibt es auf echten Rechnern, und das Bild „Colors follow the game“ zeigt den Pfad (INSTALL FOLDER).
# publish-check.ps1 prüft dieses Skript gegen die echten Installationsordner des Prüfrechners, die Bilder nicht.
function Install-Paths($g) {
    $folder = $g.Name -replace '[^A-Za-z0-9 ]', ''
    $exe = ($folder -replace ' ', '') + '.exe'
    $lib = 'D:\Demo Library'
    switch ($g.Source) {
        'Steam' { $dir = "$lib\Steam\steamapps\common\$folder"; $launch = "steam://rungameid/$($g.Id)" }
        'Epic' { $dir = "$lib\Epic\$($folder -replace ' ', '')"; $launch = "com.epicgames.launcher://apps/demo%3Ademo%3A$($g.Id)?action=launch&silent=true" }
        'Gog' { $dir = "$lib\GOG\$folder"; $launch = "$dir\$exe" }
        'Xbox' { $dir = "$lib\Xbox\$folder\Content"; $launch = "shell:AppsFolder\$($g.Id)!Game" }
        'EA' { $dir = "$lib\EA\$folder"; $launch = "origin2://game/launch/?offerIds=$($g.Id)&authCode=&cmdParams=" }
        'BattleNet' { $dir = "$lib\Battle.net\$folder"; $launch = "battlenet://$($g.Id)" }
        default { $dir = "$lib\Other\$folder"; $launch = "$dir\$exe" }
    }
    return @{ Dir = $dir; Exe = "$dir\$exe"; Launch = $launch }
}

$now = [DateTimeOffset]::Now
function Iso([DateTimeOffset]$t) { $t.ToString('yyyy-MM-ddTHH:mm:sszzz', [Globalization.CultureInfo]::InvariantCulture) }

Note 'Zeichne die Bilder der Demo-Spiele'
$seed = 4100
foreach ($g in $games) {
    $seed += 17
    $g.Key = "$($g.Source):$($g.Id)"
    $g.Stem = Stem $g.Key
    $custom = Join-Path $demo 'art\custom'
    [HdrPilotReadmeArt]::Hero((Join-Path $custom "$($g.Stem)-hero.jpg"), $g.Motif, [string[]]$g.Pal, $seed)
    [HdrPilotReadmeArt]::Cover((Join-Path $custom "$($g.Stem)-cover.jpg"), $g.Name, $g.Motif, [string[]]$g.Pal, $seed + 1, $g.Font, $g.Bold)
    [HdrPilotReadmeArt]::Logo((Join-Path $custom "$($g.Stem)-logo.png"), $g.Name, [string[]]$g.Pal, $g.Font, $g.Bold)
    [HdrPilotReadmeArt]::Icon((Join-Path $demo "icons\$($g.Stem).png"), $g.Name, [string[]]$g.Pal)
}

function Write-Demo([bool]$colorsFollowGame) {
    $list = foreach ($g in $games) {
        $paths = Install-Paths $g
        $entry = [ordered]@{
            source = $g.Source; sourceId = $g.Id; name = $g.Name; installDir = $paths.Dir
            knownExecutables = @($paths.Exe); detectedExecutables = @(); excludedExecutables = @(); extraExecutables = @()
            launchCommand = $paths.Launch
            hdrAuto = $g.HdrAuto; autoHdr = $g.AutoHdr; restoreAfter = $true; isNew = $g.New; isInstalled = $true
            firstSeen = Iso $now.AddDays(-60); lastSeen = Iso $now.AddHours(-1)
            iconCandidates = @(); customIcon = "$($g.Stem).png"
            artwork = [ordered]@{ hero = "custom:$($g.Stem)-hero.jpg"; logo = "custom:$($g.Stem)-logo.png"; cover = "custom:$($g.Stem)-cover.jpg" }
        }
        if ($null -ne $g.Played) { $entry.lastPlayed = Iso $now.AddHours(-$g.Played) }
        if ($g.Source -in 'Gog', 'Manual') { $entry.launchWorkingDirectory = $paths.Dir }
        $entry
    }
    $config = [ordered]@{
        version = 1
        settings = [ordered]@{
            # Ein Bildschirm, den es nicht gibt: kein echter Name in den Fenstern, nie eine HDR-Schaltung.
            targetDisplays = @('\\?\DISPLAY#HDRPILOTDEMO#0&0&0#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}')
            launcherSignals = $false; hdrListSync = $false; notifications = $false
            logLevel = 'Debug'; language = 'en'; librarySortOrder = 'LastPlayed'
            # Effekte wie bei aktivem Fenster (die Fotos entstehen in einem inaktiven) und unabhängig von der Windows-Einstellung.
            appearance = [ordered]@{ theme = 'Dark'; colorsFollowGame = $colorsFollowGame; effectLevel = 'Full'; pauseInBackground = $false; respectWindowsAnimations = $false }
            artwork = [ordered]@{ steamGridDb = $false }
        }
        games = @($list)
    }
    [IO.File]::WriteAllText((Join-Path $demo 'config.json'), ($config | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))

    $support = [ordered]@{}
    foreach ($g in $games) {
        if ($g.Hdr -ne 'Unknown') { $support[$g.Key] = [ordered]@{ status = $g.Hdr; pcgwTitle = $g.Name; checkedAt = Iso $now.AddHours(-3); name = $g.Name } }
    }
    $hdr = [ordered]@{ version = 1; lastRunAt = Iso $now.AddHours(-3); lastSuccessAt = Iso $now.AddHours(-3); accountMode = $false; games = $support }
    [IO.File]::WriteAllText((Join-Path $demo 'hdr-support.json'), ($hdr | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
}

function Read-Shared([string]$path) {
    $fs = New-Object IO.FileStream($path, 'Open', 'Read', 'ReadWrite, Delete')
    $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
    try { return $sr.ReadToEnd() } finally { $sr.Dispose() }
}

# Rendert einen Durchgang und prüft danach Ablage, Protokoll und Bilderverzeichnis.
function Render-Pass([string]$name, [bool]$colorsFollowGame, [string[]]$scenes) {
    Write-Demo $colorsFollowGame
    Get-ChildItem (Join-Path $demo 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force
    $passShots = Join-Path $shots $name
    New-Item -ItemType Directory -Force $passShots | Out-Null
    $arguments = @('--config-dir', "`"$demo`"", '--snapshot', "`"$passShots`"", '--snapshot-language', 'en',
        '--snapshot-sizes', $Size, '--snapshot-themes', 'dunkel', '--snapshot-scenes', ($scenes -join ','),
        '--snapshot-display', '"HDR Display;1000;400;0;10"')
    Note "Durchgang $name ($($scenes.Count) Zustände)"
    $p = Start-Process -FilePath $AppExe -ArgumentList $arguments -PassThru
    $null = $p.Handle
    try { $p.PriorityClass = 'BelowNormal' } catch {}
    if (-not $p.WaitForExit(600000)) { try { $p.Kill() } catch {}; throw "Durchgang ${name}: App hängt" }
    if ($p.ExitCode -ne 0) { throw "Durchgang ${name}: Exit-Code $($p.ExitCode)" }

    # Ablage: genau die Demo-Spiele (nichts aus einem Launcher dazugekommen, nichts verloren).
    $after = Read-Shared (Join-Path $demo 'config.json') | ConvertFrom-Json
    $keys = @($after.games | ForEach-Object { "$($_.source):$($_.sourceId)" } | Sort-Object)
    $expected = @($games | ForEach-Object { $_.Key } | Sort-Object)
    if ($keys.Count -ne $expected.Count -or (Compare-Object $keys $expected)) { throw "Durchgang ${name}: Die Ablage enthält andere Spiele als die Demo ($($keys.Count))" }

    # Protokoll: kein Einlesen, kein Scanner, keine HDR-Schaltung, kein Fehler.
    $log = (Get-ChildItem (Join-Path $demo 'logs') -Filter '*.log' | ForEach-Object { Read-Shared $_.FullName }) -join "`n"
    $bad = @($log -split "`r?`n" | Where-Object { $_ -match '\] (Scan|\w+Scanner): ' -or $_ -match 'Einlesen:' -or $_ -match 'HDR-Schaltung' -or $_ -match '\[(ERR|CRT)\]' })
    if ($bad.Count -gt 0) { throw "Durchgang ${name}: unerwartete Zeilen im Protokoll:`n$($bad -join "`n")" }

    # Bilderverzeichnis: kein Bild aus einem Launcher-Cache (nur die eigenen Bilder der Demo).
    $index = Read-Shared (Join-Path $demo 'art\index.json') | ConvertFrom-Json
    foreach ($entry in $index.games.PSObject.Properties) {
        if ($entry.Name -notin $expected) { throw "Durchgang ${name}: fremdes Spiel im Bilderverzeichnis: $($entry.Name)" }
        if ($entry.Value.located -and @($entry.Value.located.PSObject.Properties).Count -gt 0) { throw "Durchgang ${name}: Launcher-Bild für $($entry.Name) gefunden" }
    }
    $count = @($log -split "`r?`n" | Where-Object { $_ }).Count
    Note "  ok: $($keys.Count) Demo-Spiele in der Ablage, $count Protokollzeilen ohne Einlesen und HDR-Schaltung, $(@($index.games.PSObject.Properties).Count) Spiele im Bilderverzeichnis ohne Launcher-Bild"
    return $passShots
}

# Zustände (AppServices.Library.cs, AppServices.Shell.cs) und Zielnamen. Dateinamen der App:
# <fenster>-<zustand>-<größe>-dunkel-en.png.
#   library-shelf.png        Regal im HDR-Moment (bei 35 % angehalten, ohne HDR-Schaltung), zuletzt gespieltes Spiel
#   library-grid.png         Raster mit Spielprofil im Detailbereich
#   library-list.png         Liste mit Spielprofil im Detailbereich
#   game-colors.png          „Colors follow the game“: Liste, gewählt ein Spiel mit Windows Auto HDR, Akzent aus dessen Bildern
#   settings-appearance.png  Einstellungen › Darstellung (Farbschema, Vorlagen, Vorschau mit Bildern der Bibliothek)
$base = Render-Pass 'basis' $false @('hauptfenster-regal-hdr', 'hauptfenster-raster', 'hauptfenster-liste', 'einstellungen-darstellung')
$colors = Render-Pass 'spielfarben' $true @('hauptfenster-liste-autohdr')
$picks = @(
    @{ From = Join-Path $base "hauptfenster-regal-hdr-$Size-dunkel-en.png"; To = 'library-shelf.png' }
    @{ From = Join-Path $base "hauptfenster-raster-$Size-dunkel-en.png"; To = 'library-grid.png' }
    @{ From = Join-Path $base "hauptfenster-liste-$Size-dunkel-en.png"; To = 'library-list.png' }
    @{ From = Join-Path $colors "hauptfenster-liste-autohdr-$Size-dunkel-en.png"; To = 'game-colors.png' }
    @{ From = Join-Path $base "einstellungen-darstellung-$Size-dunkel-en.png"; To = 'settings-appearance.png' }
)
$final = Join-Path $WorkDir 'final'
New-Item -ItemType Directory -Force $final | Out-Null
foreach ($pick in $picks) {
    if (-not (Test-Path -LiteralPath $pick.From)) { throw "Bild fehlt: $($pick.From)" }
    [HdrPilotReadmePng]::Save($pick.From, (Join-Path $final $pick.To), $OutputWidth)
}

$total = 0
foreach ($pick in $picks) {
    $file = Get-Item -LiteralPath (Join-Path $final $pick.To)
    $total += $file.Length
    Note ("  {0,-26} {1,6:N0} KB" -f $pick.To, ($file.Length / 1KB))
}
Note ("  zusammen {0:N0} KB" -f ($total / 1KB))

if (-not $NoCopy) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    foreach ($pick in $picks) { Copy-Item -LiteralPath (Join-Path $final $pick.To) -Destination (Join-Path $OutDir $pick.To) -Force }
    Note "Kopiert nach $OutDir"
}
Write-Output 'README_SCREENSHOTS_OK'
