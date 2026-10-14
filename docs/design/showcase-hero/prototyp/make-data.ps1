<#
  make-data.ps1 – erzeugt data.json für den Showcase-Hero-Prototyp (v4)

  Liest NUR (schreibt ausschließlich data.json und art\*.png neben diesem Skript):
    - die Kopie der Bibliothek (config.json) und die HDR-Liste (hdr-support.json)
    - Steam:   C:\Program Files (x86)\Steam\appcache\librarycache\<appid>\  (rekursiv, Dateinamen; .ico aus steam\games)
    - GOG:     C:\ProgramData\GOG.com\Galaxy\webcache\<user>\gog\<id>\*_glx_bg_top_padding_7.webp (Titelbild),
               *_glx_vertical_cover.webp (Cover), *_glx_square_icon_v2.webp; goggame-<id>.ico im Spielordner
    - Ubisoft: Ubisoft Game Launcher\cache\configuration\configurations (Zuordnung Spiel -> Bilddateien) und cache\assets\*
               (background = Titelbild, logo, thumb = Hochformat, splash = Header), data\games\*.ico
    - Xbox:    <installDir>\Resources\Square480x480Logo.png (Kachelbild, deckend), SplashScreen.png (nur als Auswahl)
    - Epic/EA: kein lesbarer Bildcache – Symbol aus der Haupt-EXE (größtes Symbolbild, bis 256 px) -> art\<quelle>-<id>.png
  Steam-Hochformate werden geprüft: von Steam aus dem Header erzeugte, oben und unten verschmierte Kapseln
  (coverSmear) ersetzt die App durch einen Ausschnitt aus dem Titelbild.

  Pro Spiel wird festgehalten, welche lokalen Bilder es gibt, wie groß sie sind und unter welcher URL der lokale
  Prototyp-Server (server.ps1) sie ausliefert:
    /steam/<appid>/<datei>   Steam-Cache (der Server sucht rekursiv im appid-Ordner)
    /ico?path=<pfad>         Symboldatei unterhalb von C:\Program Files (x86)\Steam\
    /file?path=<pfad>        GOG-, Ubisoft- und Xbox-Dateien – der Server liefert NUR die Pfade aus data.json.allow aus
    /art/<datei>.png         aus EXE-Dateien gelesene Symbole (von diesem Skript erzeugt)

  Aufruf:  powershell -NoProfile -ExecutionPolicy Bypass -File make-data.ps1 [-Config <pfad>] [-Hdr <pfad>] [-NoExe]
#>
param(
  [string]$Config = (Join-Path $env:APPDATA 'HDRpilot\config.json'),          # nur gelesen; in der Claude-Sitzung (Container) eine Kopie mit -Config angeben
  [string]$Hdr    = (Join-Path $env:LOCALAPPDATA 'HDRpilot\hdr-support.json'),
  [string]$SteamRoot = 'C:\Program Files (x86)\Steam',
  [string]$GogCache  = 'C:\ProgramData\GOG.com\Galaxy\webcache',
  [string]$UbiRoot   = 'C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher',
  [switch]$NoExe
)
$ErrorActionPreference = 'Stop'
$t0 = Get-Date
$out = Join-Path $PSScriptRoot 'data.json'
$artDir = Join-Path $PSScriptRoot 'art'
$cache = Join-Path $SteamRoot 'appcache\librarycache'
$steamPrefix = ([IO.Path]::GetFullPath($SteamRoot).TrimEnd('\') + '\')

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.IO; using System.Text; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging;
using System.Runtime.InteropServices; using System.Text.RegularExpressions;
public static class HpArt {
  static FileStream Open(string p) { return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }

  // größtes Bild einer .ico-Datei (0 = keine gültige ICO)
  public static int IcoMax(string path) {
    try { using (var fs = Open(path)) using (var br = new BinaryReader(fs)) {
      if (br.ReadUInt16() != 0 || br.ReadUInt16() != 1) return 0;
      int n = br.ReadUInt16(), mx = 0;
      for (int i = 0; i < n && i < 256; i++) { int w = br.ReadByte(); br.ReadByte(); br.ReadBytes(14); if (w == 0) w = 256; if (w > mx) mx = w; }
      return mx; } } catch { return 0; }
  }
  // Breite x Höhe; WebP über den Dateikopf, sonst System.Drawing ohne Bilddaten zu prüfen
  public static int[] Size(string path) {
    try { using (var fs = Open(path)) {
      var b = new byte[32]; int r = fs.Read(b, 0, 32);
      if (r >= 30 && b[0] == 'R' && b[1] == 'I' && b[8] == 'W' && b[9] == 'E') {
        string f = Encoding.ASCII.GetString(b, 12, 4);
        if (f == "VP8X") return new int[] { 1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16) };
        if (f == "VP8 ") return new int[] { (b[26] | b[27] << 8) & 0x3FFF, (b[28] | b[29] << 8) & 0x3FFF };
        if (f == "VP8L") return new int[] { 1 + (((b[22] & 0x3F) << 8) | b[21]), 1 + (((b[24] & 0xF) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6)) };
        return new int[] { 0, 0 };
      }
      if (r >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) { int m = IcoMax(path); return new int[] { m, m }; }
      fs.Position = 0;
      using (var img = Image.FromStream(fs, false, false)) return new int[] { img.Width, img.Height };
    } } catch { return new int[] { 0, 0 }; }
  }
  // Helligkeit auf W×H verkleinert (+ Deckkraft, Farbigkeit)
  static double[,] Luma(string path, int W, int H, out bool opaque, out double chroma) {
    opaque = true; chroma = 0;
    using (var fs = Open(path)) using (var img = Image.FromStream(fs, false, false)) using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb)) {
      using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half; using (var ia = new ImageAttributes()) { ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY); g.DrawImage(img, new Rectangle(0, 0, W, H), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia); } }
      var d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      var buf = new byte[d.Stride * H]; Marshal.Copy(d.Scan0, buf, 0, buf.Length); int st = d.Stride; bmp.UnlockBits(d);
      var L = new double[W, H]; double cs = 0;
      for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
        int o = y * st + x * 4; int B = buf[o], G = buf[o + 1], R = buf[o + 2], A = buf[o + 3];
        if (A < 250) opaque = false;
        L[x, y] = 0.2126 * R + 0.7152 * G + 0.0722 * B;
        cs += Math.Max(R, Math.Max(G, B)) - Math.Min(R, Math.Min(G, B));
      }
      chroma = cs / (W * H);
      return L;
    }
  }
  static double[] Band(double[,] L, int y0, int y1) {
    int W = L.GetLength(0); double v = 0, h = 0, s = 0, s2 = 0; int nv = 0, nh = 0, n = 0;
    for (int y = y0; y <= y1; y++) for (int x = 0; x < W; x++) {
      double a = L[x, y]; s += a; s2 += a * a; n++;
      if (y < y1) { v += Math.Abs(L[x, y + 1] - a); nv++; }
      if (x < W - 1) { h += Math.Abs(L[x + 1, y] - a); nh++; }
    }
    double m = s / n; return new double[] { v / nv, h / nh, Math.Sqrt(Math.Max(0, s2 / n - m * m)) };
  }
  // Von Steam erzeugte Kapsel: Header in der Mitte, darüber und darunter senkrecht verschmiert (kaum senkrechte Änderung,
  // aber waagerechte Streifen und Farbe). Flache Flächen (schwarz, einfarbig) gelten NICHT als verschmiert.
  public static double[] SmearMetrics(string path) {
    bool op; double ch; var L = Luma(path, 60, 90, out op, out ch);
    var b = Band(L, 62, 89); var t = Band(L, 0, 8); return new double[] { b[0], b[1], b[2], t[0], t[1], t[2] };
  }
  public static bool Smeared(double[] m) {
    bool bot = m[0] < 1.4 && m[1] >= 1.6 * m[0] && m[1] < 6 && m[2] > 10;
    bool top = m[3] < 3.5 && m[5] > 3;
    return bot && top;
  }
  public static bool Opaque(string path) { try { bool op; double ch; Luma(path, 24, 24, out op, out ch); return op; } catch { return false; } }
  // mittlere Farbigkeit (0–255): Unreal-/Engine-Splash in Schwarzweiß ~0
  public static double Chroma(string path) { try { bool op; double ch; Luma(path, 48, 27, out op, out ch); return ch; } catch { return 0; } }

  // ---- Symbol aus einer EXE: größtes Bild der ersten Symbolgruppe ----
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string f, IntPtr h, uint flags);
  [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr h);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate bool EnumResNameProc(IntPtr m, IntPtr type, IntPtr name, IntPtr p);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr m, IntPtr type, EnumResNameProc cb, IntPtr p);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindResource(IntPtr m, IntPtr name, IntPtr type);
  [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr m, IntPtr ri);
  [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr rd);
  [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr m, IntPtr ri);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIcons(string f, int idx, int cx, int cy, IntPtr[] icons, uint[] ids, uint n, uint flags);
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int x; public int y; public IntPtr hbmMask; public IntPtr hbmColor; }
  [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

  public static int ExeIconMax(string exe) {
    IntPtr h = LoadLibraryEx(exe, IntPtr.Zero, 0x2 | 0x20);   // LOAD_LIBRARY_AS_DATAFILE | AS_IMAGE_RESOURCE: nichts wird ausgeführt
    if (h == IntPtr.Zero) return 0;
    int mx = 0;
    try {
      EnumResNameProc cb = delegate (IntPtr m, IntPtr type, IntPtr name, IntPtr p) {
        IntPtr ri = FindResource(m, name, (IntPtr)14); if (ri == IntPtr.Zero) return false;
        IntPtr pt = LockResource(LoadResource(m, ri)); uint sz = SizeofResource(m, ri);
        if (pt == IntPtr.Zero || sz < 6) return false;
        var b = new byte[sz]; Marshal.Copy(pt, b, 0, (int)sz);
        int n = b[4] | b[5] << 8;
        for (int i = 0; i < n && 6 + i * 14 + 14 <= sz; i++) { int w = b[6 + i * 14]; if (w == 0) w = 256; if (w > mx) mx = w; }
        return false;   // nur die erste Gruppe (= Programmsymbol)
      };
      EnumResourceNames(h, (IntPtr)14, cb, IntPtr.Zero);
      GC.KeepAlive(cb);
    } catch { } finally { FreeLibrary(h); }
    return mx;
  }
  static Bitmap IconBitmap(IntPtr hIcon) {
    ICONINFO ii;
    if (GetIconInfo(hIcon, out ii)) {
      try {
        using (var color = Image.FromHbitmap(ii.hbmColor)) {
          if (Image.GetPixelFormatSize(color.PixelFormat) == 32) {
            var d = color.LockBits(new Rectangle(0, 0, color.Width, color.Height), ImageLockMode.ReadOnly, color.PixelFormat);
            try {
              int rw = d.Width * 4; var buf = new byte[rw * d.Height];   // Stride kann negativ sein (DIB von unten nach oben)
              for (int y = 0; y < d.Height; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), buf, y * rw, rw);
              bool alpha = false; for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { alpha = true; break; }
              if (alpha) {
                var bmp = new Bitmap(d.Width, d.Height, PixelFormat.Format32bppArgb);
                var dd = bmp.LockBits(new Rectangle(0, 0, d.Width, d.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < d.Height; y++) Marshal.Copy(buf, y * rw, IntPtr.Add(dd.Scan0, y * dd.Stride), rw);
                bmp.UnlockBits(dd); return bmp;
              }
            } finally { color.UnlockBits(d); }
          }
        }
      } finally { if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor); if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask); }
    }
    using (var ico = Icon.FromHandle(hIcon)) return ico.ToBitmap();
  }
  // speichert das Symbol als PNG (Größe = größtes Bild, höchstens 256) und liefert die Kantenlänge, 0 = keins
  public static int ExeIcon(string exe, string outPng) {
    int mx = ExeIconMax(exe); if (mx <= 0) return 0;
    int sz = Math.Min(256, mx);
    var hs = new IntPtr[1]; var ids = new uint[1];
    uint got = PrivateExtractIcons(exe, 0, sz, sz, hs, ids, 1, 0);
    if (got == 0 || got == 0xFFFFFFFF || hs[0] == IntPtr.Zero) return 0;
    try { using (var bmp = IconBitmap(hs[0])) bmp.Save(outPng, ImageFormat.Png); } finally { DestroyIcon(hs[0]); }
    return sz;
  }

  // ---- Ubisoft: cache\configuration\configurations (Protobuf-Rahmen, darin YAML je Spiel) ----
  static long Varint(byte[] b, ref int p) { long v = 0; int s = 0; while (p < b.Length) { byte c = b[p++]; v |= (long)(c & 0x7F) << s; if ((c & 0x80) == 0) return v; s += 7; if (s > 56) break; } return -1; }
  public static Dictionary<string, Dictionary<string, string>> Ubi(string path) {
    var res = new Dictionary<string, Dictionary<string, string>>();
    if (!File.Exists(path)) return res;
    byte[] b; using (var fs = Open(path)) { b = new byte[fs.Length]; int o = 0; while (o < b.Length) { int n = fs.Read(b, o, b.Length - o); if (n <= 0) break; o += n; } }
    int p = 0;
    while (p < b.Length) {
      if (b[p] != 0x0A) { p++; continue; }
      int q = p + 1; long len = Varint(b, ref q);
      if (len <= 0 || q + len > b.Length) { p++; continue; }
      int end = q + (int)len, r = q; long id = -1; string yaml = null;
      while (r < end) {
        int tag = b[r++], field = tag >> 3, wt = tag & 7;
        if (wt == 0) { long v = Varint(b, ref r); if (field == 1) id = v; }
        else if (wt == 2) { long l = Varint(b, ref r); if (l < 0 || r + l > end) break; if (field == 3) yaml = Encoding.UTF8.GetString(b, r, (int)l); r += (int)l; }
        else break;
      }
      if (id >= 0 && yaml != null && yaml.Contains("root:")) {
        var loc = new Dictionary<string, string>();
        var lm = Regex.Match(yaml, @"\nlocalizations:\s*\n  default:\s*\n((?:    [^\n]*\n?)+)");
        if (lm.Success) foreach (Match m in Regex.Matches(lm.Groups[1].Value, @"(?m)^    ([A-Za-z0-9_]+):\s*(.+?)\s*$")) loc[m.Groups[1].Value] = m.Groups[2].Value.Trim('\'', '"');
        var d = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(yaml, @"(?m)^  (name|background_image|thumb_image|logo_image|splash_image|icon_image):\s*(.+?)\s*$")) {
          string v = m.Groups[2].Value.Trim('\'', '"'); string lv;
          if (!Regex.IsMatch(v, @"\.(jpg|jpeg|png|ico)$", RegexOptions.IgnoreCase) && loc.TryGetValue(v, out lv)) v = lv;
          d[m.Groups[1].Value] = v;
        }
        string k = id.ToString(); if (!res.ContainsKey(k)) res[k] = d;
      }
      p = end;
    }
    return res;
  }
}
'@

$cfg = [IO.File]::ReadAllText($Config, [Text.Encoding]::UTF8) | ConvertFrom-Json
$hdrList = [IO.File]::ReadAllText($Hdr, [Text.Encoding]::UTF8) | ConvertFrom-Json
$hdrMap = @{}
foreach ($p in $hdrList.games.PSObject.Properties) { $hdrMap[$p.Name.ToLowerInvariant()] = $p.Value.status }

# Steam: Dateien je Bildart in Vorzugsreihenfolge (deutsche Varianten zuerst)
$kinds = [ordered]@{
  hero   = @('library_hero_german.jpg', 'library_hero.jpg')
  logo   = @('logo_german.png', 'logo.png')
  cover  = @('library_600x900_german.jpg', 'library_600x900.jpg', 'library_capsule_german.jpg', 'library_capsule.jpg')
  header = @('header_german.jpg', 'header.jpg', 'library_header_german.jpg', 'library_header.jpg')
  blur   = @('library_hero_blur_german.jpg', 'library_hero_blur.jpg')
}
$srcName = @{ steam = 'Steam'; epic = 'Epic Games'; gog = 'GOG'; xbox = 'Xbox'; ea = 'EA app'; ubisoft = 'Ubisoft Connect'; battlenet = 'Battle.net'; manual = 'Manuell' }
$HELPER = '(start_protected_game|launcher|prelauncher|anticheat|crashhandler|crashreport|helper|unins|setup|install|redist|prereq|dxsetup|directx|dotnet|battleye|easyanti|eosbootstrap|epiconlineservices|uplay|ubisoftconnect|benchmark|debugger|^udk\.exe$|^cdb\.exe$|^7z|gamelaunchhelper|^play[a-z0-9]+\.exe$)'

$allow = New-Object System.Collections.Generic.List[string]
function Url-Enc([string]$s) { [Uri]::EscapeDataString($s) }
function Is-UnderSteam([string]$p) { try { $f = [IO.Path]::GetFullPath($p) } catch { return $false }; $f.StartsWith($steamPrefix, [StringComparison]::OrdinalIgnoreCase) }
function File-Url([string]$p) {   # Nicht-Steam-Datei -> Allowlist des Servers
  $f = [IO.Path]::GetFullPath($p); if (-not $allow.Contains($f)) { $allow.Add($f) }; '/file?path=' + (Url-Enc $f)
}
function Ico-Url([string]$p) { if (Is-UnderSteam $p) { '/ico?path=' + (Url-Enc $p) } else { File-Url $p } }
function Size-Of([string]$p) { $s = [HpArt]::Size($p); @($s[0], $s[1]) }

# GOG: Benutzerordner im Galaxy-Webcache
$gogUsers = @(); if ([IO.Directory]::Exists($GogCache)) { $gogUsers = [IO.Directory]::GetDirectories($GogCache) | Where-Object { [IO.Directory]::Exists((Join-Path $_ 'gog')) } }
# Ubisoft: Zuordnung Spiel-ID -> Bilddateien
$ubiMap = [HpArt]::Ubi((Join-Path $UbiRoot 'cache\configuration\configurations'))
$ubiAssets = Join-Path $UbiRoot 'cache\assets'
if (-not $NoExe -and -not [IO.Directory]::Exists($artDir)) { [void][IO.Directory]::CreateDirectory($artDir) }

$games = New-Object System.Collections.ArrayList
$cov = [ordered]@{}
$smearList = @(); $exeList = @()
$i = 0
foreach ($g in $cfg.games) {
  $src = [string]$g.source; $srcKey = $src.ToLowerInvariant(); $sid = [string]$g.sourceId
  if (-not $cov.Contains($src)) {
    $cov[$src] = [ordered]@{ name = $(if ($srcName[$srcKey]) { $srcName[$srcKey] } else { $src }); total = 0; installed = 0
      found = [ordered]@{ hero = 0; logo = 0; cover = 0; header = 0; icon = 0 }; used = [ordered]@{ hero = 0; logo = 0; cover = 0; header = 0; icon = 0 }
      cover600 = 0; smear = 0; exeIcons = 0; where = '' }
  }
  $c = $cov[$src]; $c.total++
  if ($g.isInstalled) { $c.installed++ }

  $img = [ordered]@{ hero = $null; heroFrom = $null; heroW = 0; heroH = 0; logo = $null; cover = $null; coverKind = $null; coverW = 0; coverH = 0; coverSmear = $false
    header = $null; blur = $null; icon = $null; iconKind = $null; iconPx = 0; iconOpaque = $false; alts = @() }
  $found = @{ hero = $false; logo = $false; cover = $false; header = $false; icon = $false }

  switch ($srcKey) {
    'steam' {
      $c.where = 'appcache\librarycache'
      $files = @{}
      if ($sid -match '^\d+$') {
        $dir = Join-Path $cache $sid
        if ([IO.Directory]::Exists($dir)) { foreach ($f in [IO.Directory]::EnumerateFiles($dir, '*', [IO.SearchOption]::AllDirectories)) { $n = [IO.Path]::GetFileName($f).ToLowerInvariant(); if (-not $files.ContainsKey($n)) { $files[$n] = $f } } }
        foreach ($k in $kinds.Keys) {
          foreach ($cand in $kinds[$k]) {
            if ($files.ContainsKey($cand)) {
              $img[$k] = '/steam/' + $sid + '/' + $cand; if ($found.ContainsKey($k)) { $found[$k] = $true }
              if ($k -eq 'cover') {
                $img.coverKind = $(if ($cand -like 'library_600x900*') { '600x900' } else { 'capsule' })
                $s = Size-Of $files[$cand]; $img.coverW = $s[0]; $img.coverH = $s[1]
                $m = [HpArt]::SmearMetrics($files[$cand])
                if ([HpArt]::Smeared($m)) { $img.coverSmear = $true; $smearList += ('{0} ({1:N1}/{2:N1}/{3:N0} · oben {4:N1}/{5:N0})' -f $g.name, $m[0], $m[1], $m[2], $m[3], $m[5]) }
              }
              if ($k -eq 'hero') { $img.heroFrom = 'steam'; $s = Size-Of $files[$cand]; $img.heroW = $s[0]; $img.heroH = $s[1] }
              break
            }
          }
        }
        $comm = $files.Keys | Where-Object { $_ -match '^[0-9a-f]{40}\.jpg$' } | Select-Object -First 1
        if ($comm) { $img.icon = '/steam/' + $sid + '/' + $comm; $img.iconKind = 'steam-cache'; $img.iconPx = 32; $found.icon = $true }
      }
    }
    'gog' {
      $c.where = 'GOG Galaxy\webcache'
      foreach ($u in $gogUsers) {
        $d = Join-Path $u ('gog\' + $sid)
        if (-not [IO.Directory]::Exists($d)) { continue }
        foreach ($f in [IO.Directory]::GetFiles($d)) {
          $n = [IO.Path]::GetFileName($f).ToLowerInvariant()
          if ($n -like '*_glx_bg_top_padding_7.webp' -or ($n -like '*_glx_background*.webp' -and -not $img.hero)) { $img.hero = File-Url $f; $img.heroFrom = 'gog'; $s = Size-Of $f; $img.heroW = $s[0]; $img.heroH = $s[1]; $found.hero = $true }
          elseif ($n -like '*_glx_vertical_cover.webp') { $img.cover = File-Url $f; $img.coverKind = 'gog'; $s = Size-Of $f; $img.coverW = $s[0]; $img.coverH = $s[1]; $found.cover = $true }
          elseif ($n -like '*_glx_logo*.webp') { $img.logo = File-Url $f; $found.logo = $true }
        }
        break
      }
    }
    'ubisoft' {
      $c.where = 'Ubisoft Game Launcher\cache\assets'
      $u = $ubiMap[$sid]
      if ($u) {
        $map = @{ background_image = 'hero'; logo_image = 'logo'; thumb_image = 'cover'; splash_image = 'header' }
        foreach ($k in $map.Keys) {
          if (-not $u.ContainsKey($k)) { continue }
          $f = Join-Path $ubiAssets $u[$k]
          if ([IO.File]::Exists($f)) {
            $t = $map[$k]; $img[$t] = File-Url $f; $found[$t] = $true
            $s = Size-Of $f
            if ($t -eq 'hero') { $img.heroFrom = 'ubisoft'; $img.heroW = $s[0]; $img.heroH = $s[1] }
            if ($t -eq 'cover') { $img.coverKind = 'ubisoft'; $img.coverW = $s[0]; $img.coverH = $s[1] }
          }
        }
      }
    }
    'xbox' {
      $c.where = '<Spielordner>\Resources'
      $res = Join-Path ([string]$g.installDir) 'Resources'
      $sp = Join-Path $res 'SplashScreen.png'
      if ([IO.File]::Exists($sp)) {
        # Splash ist kein verlässliches Titelbild (oft Engine-Logo oder Titelkarte mit mittigem Schriftzug): nur zur Auswahl
        $s = Size-Of $sp; $chroma = [HpArt]::Chroma($sp)
        $found.hero = $true
        $img.alts += [ordered]@{ t = 'hero'; url = (File-Url $sp); label = 'SplashScreen.png (Xbox-Paket)'; w = $s[0]; h = $s[1]; note = $(if ($chroma -lt 6) { 'Schwarzweiß – vermutlich Engine-Logo' } else { 'Titelkarte mit Schriftzug in der Mitte' }) }
      }
    }
    default { $c.where = 'kein lesbarer Bildcache' }
  }

  # Symbol: größtes Bild unter den Kandidaten der Bibliothek
  $best = $null; $bestPx = $img.iconPx
  foreach ($p in @($g.iconCandidates)) {
    if (-not $p -or -not [IO.File]::Exists($p)) { continue }
    $ext = [IO.Path]::GetExtension($p).ToLowerInvariant()
    if ($ext -notin '.ico', '.png', '.jpg', '.jpeg', '.webp') { continue }
    $s = Size-Of $p; $px = [Math]::Min($s[0], $s[1])
    if ($px -gt $bestPx) { $best = $p; $bestPx = $px }
  }
  if ($best) {
    $found.icon = $true
    $img.icon = Ico-Url $best; $img.iconPx = $bestPx
    $img.iconKind = $(if ($srcKey -eq 'xbox' -and $best -like '*Square480x480Logo.png') { 'xbox-tile' } elseif ($best -like '*.ico') { 'ico' } else { 'bild' })
    if ($best -match '\.(png|jpg|jpeg)$') { $img.iconOpaque = [HpArt]::Opaque($best) }
  }
  # Haupt-EXE: Hilfsprogramme nach hinten; ohne bekannte EXE im Spielordner suchen (zwei Ebenen)
  $excl = @($g.excludedExecutables) | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() }
  $exes = @(@($g.knownExecutables) + @($g.detectedExecutables) + @($g.extraExecutables)) | Where-Object { $_ -and ($excl -notcontains $_.ToLowerInvariant()) } | Select-Object -Unique
  # EXE-Symbol nur, wo es gebraucht wird: Epic/EA (kein Bildcache) und Spiele ohne Titelbild mit kleinem Symbol
  $needIcon = ($srcKey -in 'epic', 'ea') -or (-not $img.hero -and $img.iconPx -lt 128)
  if (-not $NoExe -and $needIcon) {
    $cands = @($exes)
    if ($g.installDir -and [IO.Directory]::Exists($g.installDir)) {
      $cands += @(Get-ChildItem -LiteralPath $g.installDir -Filter '*Shipping*.exe' -Recurse -Depth 4 -ErrorAction SilentlyContinue | Select-Object -First 4 | ForEach-Object { $_.FullName })
      if (-not @($exes).Count) { $cands += @(Get-ChildItem -LiteralPath $g.installDir -Filter *.exe -Recurse -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 24 | ForEach-Object { $_.FullName }) }
    }
    $cands = @($cands | Select-Object -Unique | Where-Object { [IO.File]::Exists($_) -and [IO.Path]::GetFileName($_) -notmatch $HELPER } | Sort-Object { if ($_ -match 'shipping|win64|x64|\\bin\\') { 0 } else { 1 } })
    $key = ($srcKey + '-' + ($sid -replace '[^A-Za-z0-9_.-]', '_')).ToLowerInvariant()
    foreach ($exe in ($cands | Select-Object -First 4)) {
      $tmp = Join-Path $artDir ($key + '.tmp.png')
      $px = 0; try { $px = [HpArt]::ExeIcon($exe, $tmp) } catch { $px = 0 }
      if ($px -gt $img.iconPx) {
        $dst = Join-Path $artDir ($key + '.png'); [IO.File]::Copy($tmp, $dst, $true)
        $img.icon = '/art/' + $key + '.png'; $img.iconPx = $px; $img.iconKind = 'exe'; $img.iconOpaque = $false; $img.iconExe = [IO.Path]::GetFileName($exe); $found.icon = $true
      }
      if ([IO.File]::Exists($tmp)) { [IO.File]::Delete($tmp) }
      if ($img.iconPx -ge 128) { break }
    }
    if ($img.iconKind -eq 'exe') { $c.exeIcons++; $exeList += ('{0}: {1} px aus {2}' -f $g.name, $img.iconPx, $img.iconExe) }
  }

  foreach ($k in 'hero', 'logo', 'cover', 'header', 'icon') { if ($found[$k]) { $c.found[$k]++ } }
  foreach ($k in 'hero', 'logo', 'header', 'icon') { if ($img[$k]) { $c.used[$k]++ } }
  if ($img.cover -and -not $img.coverSmear) { $c.used.cover++ }
  if ($img.coverSmear) { $c.smear++ }
  if ($img.coverW -ge 600) { $c.cover600++ }

  $key = ($src + ':' + $sid).ToLowerInvariant()
  $status = $hdrMap[$key]; if (-not $status) { $status = 'Unknown' }
  [void]$games.Add([ordered]@{
    i = $i; n = [string]$g.name; src = $src; sid = $sid; dir = [string]$g.installDir; cmd = [string]$g.launchCommand
    auto = [bool]$g.hdrAuto; rest = [bool]$g.restoreAfter; nw = [bool]$g.isNew; inst = [bool]$g.isInstalled
    lp = $(if ($g.lastPlayed) { [string]$g.lastPlayed } else { $null }); fs = [string]$g.firstSeen
    hdr = $status; exe = @($exes); ico = @(@($g.iconCandidates) | Where-Object { $_ } | ForEach-Object { [ordered]@{ path = [string]$_; url = $(if ([IO.File]::Exists($_)) { Ico-Url $_ } else { $null }); px = $(if ([IO.File]::Exists($_)) { [Math]::Min((Size-Of $_)[0], (Size-Of $_)[1]) } else { 0 }) } }); img = $img
  })
  $i++
}

$data = [ordered]@{
  generated = (Get-Date).ToString('s')
  source = 'Kopie der Bibliothek (nur gelesen)'
  monitor = [ordered]@{ name = 'MAG 341C OLED'; peak = 456; full = 254; min = '0,0000'; bits = 10; hdr = $false; via = 'DXGI' }
  coverage = $cov
  allow = @($allow)
  games = $games
}
# alte, nicht mehr benutzte EXE-Symbole aus art\ entfernen (nur Dateien, die dieses Skript selbst angelegt hat)
if ([IO.Directory]::Exists($artDir)) {
  $used = @{}; foreach ($gm in $games) { if ($gm.img.icon -like '/art/*') { $used[[IO.Path]::GetFileName($gm.img.icon).ToLowerInvariant()] = 1 } }
  foreach ($f in [IO.Directory]::GetFiles($artDir, '*.png')) { if (-not $used.ContainsKey([IO.Path]::GetFileName($f).ToLowerInvariant())) { [IO.File]::Delete($f) } }
}
$json = $data | ConvertTo-Json -Depth 7 -Compress
[IO.File]::WriteAllText($out, $json, (New-Object Text.UTF8Encoding $false))

'{0} Spiele -> {1} ({2:N0} KB, {3:N1} s)' -f $games.Count, $out, ((Get-Item $out).Length / 1KB), ((Get-Date) - $t0).TotalSeconds
'{0,-8} {1,4} | Titelbild gef./gen. | Logo | Cover | Header | Symbol | 600er | verschmiert | EXE-Symbole' -f 'Quelle', 'n'
foreach ($k in $cov.Keys) { $v = $cov[$k]; '{0,-8} {1,4} | {2,3}/{3,-3} | {4,3}/{5,-3} | {6,3}/{7,-3} | {8,3}/{9,-3} | {10,3}/{11,-3} | {12,3} | {13,3} | {14,3}' -f $k, $v.total, $v.found.hero, $v.used.hero, $v.found.logo, $v.used.logo, $v.found.cover, $v.used.cover, $v.found.header, $v.used.header, $v.found.icon, $v.used.icon, $v.cover600, $v.smear, $v.exeIcons }
'Verschmierte Kapseln: ' + ($smearList -join '; ')
'EXE-Symbole: ' + ($exeList -join '; ')
'Allowlist /file: ' + $allow.Count + ' Dateien'
