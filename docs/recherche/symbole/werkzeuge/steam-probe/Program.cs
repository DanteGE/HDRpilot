// Read-only probe: Steam local icon sources coverage. Writes only to its own output folder (args[0]).
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

var outDir = args[0];
var configPath = args[1];
const string Steam = @"C:\Program Files (x86)\Steam";
var libCache = Path.Combine(Steam, @"appcache\librarycache");
var gamesDir = Path.Combine(Steam, @"steam\games");
var hex40 = new Regex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase);
var sb = new StringBuilder();
void Log(string s) { Console.WriteLine(s); sb.AppendLine(s); }

// ---------- config ----------
using var cfgDoc = JsonDocument.Parse(File.ReadAllBytes(configPath));
var steamGames = cfgDoc.RootElement.GetProperty("games").EnumerateArray()
    .Where(g => g.GetProperty("source").GetString() == "Steam")
    .Select(g => (Id: uint.Parse(g.GetProperty("sourceId").GetString()!), Name: g.GetProperty("name").GetString() ?? "",
        Installed: g.TryGetProperty("isInstalled", out var i) && i.GetBoolean()))
    .ToList();
Log($"Steam games in config: {steamGames.Count} (installed flag true: {steamGames.Count(g => g.Installed)})");

// ---------- appinfo.vdf ----------
var appinfoPath = Path.Combine(Steam, @"appcache\appinfo.vdf");
var sw = System.Diagnostics.Stopwatch.StartNew();
byte[] data;
using (var fs = new FileStream(appinfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
{
    data = new byte[fs.Length];
    fs.ReadExactly(data);
}
var readMs = sw.ElapsedMilliseconds;
var (magic, apps) = AppInfo.ParseAll(data);
Log($"appinfo.vdf: {data.Length} bytes, magic 0x{magic:X8}, apps parsed {apps.Count}, read {readMs} ms, parse(all, common strings) {sw.ElapsedMilliseconds - readMs} ms");

// ---------- installed set ----------
var libPaths = Regex.Matches(File.ReadAllText(Path.Combine(Steam, @"steamapps\libraryfolders.vdf")), "\"path\"\\s+\"([^\"]+)\"")
    .Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList();
var installed = new HashSet<uint>();
foreach (var lp in libPaths)
{
    var sa = Path.Combine(lp, "steamapps");
    if (!Directory.Exists(sa)) continue;
    foreach (var f in Directory.EnumerateFiles(sa, "appmanifest_*.acf"))
    {
        var n = Path.GetFileNameWithoutExtension(f)["appmanifest_".Length..];
        if (uint.TryParse(n, out var id)) installed.Add(id);
    }
}
Log($"Libraries: {string.Join(" | ", libPaths)}; appmanifests: {installed.Count}");

// ---------- steam\games ----------
var icoFiles = Directory.EnumerateFiles(gamesDir, "*.ico").ToDictionary(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), f => f);
var clientIconOwners = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
foreach (var (id, a) in apps)
    if (a.Common.TryGetValue("clienticon", out var h) && !string.IsNullOrEmpty(h))
    {
        if (!clientIconOwners.TryGetValue(h, out var l)) clientIconOwners[h] = l = [];
        l.Add(id);
    }
var icoOrphans = icoFiles.Keys.Count(k => !clientIconOwners.ContainsKey(k));
var icoOwnedInstalled = icoFiles.Keys.Count(k => clientIconOwners.TryGetValue(k, out var l) && l.Any(installed.Contains));
Log($"steam\\games: {icoFiles.Count} .ico; hash matches some appinfo clienticon: {icoFiles.Count - icoOrphans}; no appinfo owner: {icoOrphans}; owner installed: {icoOwnedInstalled}");

// ---------- librarycache name census ----------
var lcDirs = Directory.EnumerateDirectories(libCache).Select(d => Path.GetFileName(d)).Where(n => uint.TryParse(n, out _)).Select(uint.Parse).ToHashSet();
var nameCensus = new Dictionary<string, int>();
foreach (var id in lcDirs)
{
    var d = Path.Combine(libCache, id.ToString());
    foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(d, f);
        var parts = rel.Split('\\');
        var key = string.Join("/", parts.Select((p, i) => i < parts.Length - 1 ? (hex40.IsMatch(p) ? "<hash>" : p) : (hex40.IsMatch(Path.GetFileNameWithoutExtension(p)) ? "<hash>" + Path.GetExtension(p) : p)));
        nameCensus[key] = nameCensus.GetValueOrDefault(key) + 1;
    }
}
Log($"librarycache: {lcDirs.Count} appid folders. File-name census (hash names normalized):");
foreach (var kv in nameCensus.OrderByDescending(k => k.Value).Take(40)) Log($"   {kv.Value,6}  {kv.Key}");

// ---------- shortcuts ----------
var urlDirs = new List<string>
{
    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    System.Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Desktop"), System.Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\OneDrive\Desktop"), System.Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\OneDrive\Dokumente\Desktop"),
}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var urlByApp = new Dictionary<uint, List<(string File, string? IconFile)>>();
var urlTotal = 0; var urlSteam = 0;
foreach (var dir in urlDirs)
{
    IEnumerable<string> files;
    try { files = Directory.EnumerateFiles(dir, "*.url", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
    catch { continue; }
    foreach (var f in files)
    {
        urlTotal++;
        string? url = null, icon = null;
        foreach (var line in File.ReadLines(f))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..].Trim();
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) icon = line[9..].Trim();
        }
        var m = url is null ? null : Regex.Match(url, @"^steam://rungameid/(\d+)$", RegexOptions.IgnoreCase);
        if (m is { Success: true } && uint.TryParse(m.Groups[1].Value, out var aid))
        {
            urlSteam++;
            if (!urlByApp.TryGetValue(aid, out var l)) urlByApp[aid] = l = [];
            l.Add((f, icon));
        }
    }
}
Log($"Shortcut dirs searched: {string.Join(" | ", urlDirs)}");
Log($".url files: {urlTotal}, steam://rungameid: {urlSteam}, distinct appids: {urlByApp.Count}");

// ---------- registry ----------
string? RegDisplayIcon(uint id, RegistryHive hive, RegistryView view)
{
    try
    {
        using var b = RegistryKey.OpenBaseKey(hive, view);
        using var k = b.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {id}");
        return k?.GetValue("DisplayIcon") as string;
    }
    catch { return null; }
}
int regKeysTotal = 0;
try
{
    using var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
    using var u = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
    regKeysTotal = u?.GetSubKeyNames().Count(n => n.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)) ?? 0;
}
catch { }
Log($"HKLM WOW6432Node Uninstall 'Steam App *' keys: {regKeysTotal}");

static string StripIndex(string s)
{
    s = s.Trim().Trim('"');
    var m = Regex.Match(s, @"^(.*?)(,\s*-?\d+)?$");
    return m.Groups[1].Value.Trim().Trim('"');
}

// ---------- per game ----------
var rows = new List<Dictionary<string, object?>>();
int cAppinfo = 0, cIconField = 0, cClientIconField = 0, cLcDir = 0, cLcIcon = 0, cLcIconMatch = 0, cIco = 0, cReg = 0, cRegExists = 0, cUrl = 0, cUrlExists = 0, cUnion = 0, cUnionNoLc = 0, cIcoOrReg = 0, cTgaField = 0;
var lcDims = new Dictionary<string, int>(); var icoMaxDist = new Dictionary<int, int>(); var regKind = new Dictionary<string, int>(); var urlKind = new Dictionary<string, int>();
var icoHasPngEntry = 0; var icoSquare = 0; var lcSquare = 0; var lcBytes = new List<long>(); var icoBytes = new List<long>();
var missing = new List<string>();
foreach (var g in steamGames)
{
    var row = new Dictionary<string, object?> { ["appid"] = g.Id, ["name"] = g.Name, ["manifest"] = installed.Contains(g.Id) };
    apps.TryGetValue(g.Id, out var a);
    if (a is not null) cAppinfo++;
    var iconHash = a?.Common.GetValueOrDefault("icon");
    var clientIcon = a?.Common.GetValueOrDefault("clienticon");
    if (!string.IsNullOrEmpty(a?.Common.GetValueOrDefault("clienttga"))) cTgaField++;
    if (!string.IsNullOrEmpty(iconHash)) cIconField++;
    if (!string.IsNullOrEmpty(clientIcon)) cClientIconField++;
    row["type"] = a?.Common.GetValueOrDefault("type");
    row["icon"] = iconHash; row["clienticon"] = clientIcon;

    // librarycache
    var dir = Path.Combine(libCache, g.Id.ToString());
    string? lcIcon = null;
    if (Directory.Exists(dir))
    {
        cLcDir++;
        var cands = Directory.EnumerateFiles(dir, "*.jpg").Where(f => hex40.IsMatch(Path.GetFileNameWithoutExtension(f))).ToList();
        if (cands.Count > 1) row["lcIconCandidates"] = cands.Count;
        lcIcon = cands.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), iconHash, StringComparison.OrdinalIgnoreCase)) ?? cands.FirstOrDefault();
        if (lcIcon is not null)
        {
            cLcIcon++;
            if (string.Equals(Path.GetFileNameWithoutExtension(lcIcon), iconHash, StringComparison.OrdinalIgnoreCase)) cLcIconMatch++;
            var bytes = File.ReadAllBytes(lcIcon);
            lcBytes.Add(bytes.Length);
            var (w, h) = Img.JpegSize(bytes);
            lcDims[$"{w}x{h}"] = lcDims.GetValueOrDefault($"{w}x{h}") + 1;
            if (w == h && w > 0) lcSquare++;
            row["lcIcon"] = Path.GetFileName(lcIcon); row["lcIconSize"] = $"{w}x{h}";
        }
    }

    // steam\games ico
    string? ico = null;
    if (!string.IsNullOrEmpty(clientIcon) && icoFiles.TryGetValue(clientIcon.ToLowerInvariant(), out var icoPath))
    {
        ico = icoPath; cIco++;
        var bytes = File.ReadAllBytes(icoPath);
        icoBytes.Add(bytes.Length);
        var entries = Img.IcoEntries(bytes);
        var max = entries.Count == 0 ? 0 : entries.Max(e => Math.Max(e.W, e.H));
        icoMaxDist[max] = icoMaxDist.GetValueOrDefault(max) + 1;
        if (entries.Any(e => e.Png)) icoHasPngEntry++;
        if (entries.Count > 0 && entries.All(e => e.W == e.H)) icoSquare++;
        row["icoEntries"] = string.Join(",", entries.Select(e => $"{e.W}x{e.H}{(e.Png ? "p" : "")}/{e.Bpp}"));
    }

    // registry
    var reg = RegDisplayIcon(g.Id, RegistryHive.LocalMachine, RegistryView.Registry32) ?? RegDisplayIcon(g.Id, RegistryHive.LocalMachine, RegistryView.Registry64);
    string? regFile = null;
    if (reg is not null)
    {
        cReg++;
        var p = StripIndex(reg);
        var exists = File.Exists(p);
        if (exists) { cRegExists++; regFile = p; }
        var kind = p.StartsWith(gamesDir + "\\", StringComparison.OrdinalIgnoreCase)
            ? (string.Equals(Path.GetFileNameWithoutExtension(p), clientIcon, StringComparison.OrdinalIgnoreCase) ? "steam\\games\\<clienticon>.ico" : "steam\\games\\<other>.ico")
            : Path.GetExtension(p).ToLowerInvariant() is var ext && ext.Length > 0 ? "other " + ext : "other";
        kind += exists ? " (exists)" : " (missing)";
        regKind[kind] = regKind.GetValueOrDefault(kind) + 1;
        row["regDisplayIcon"] = reg;
    }

    // url
    string? urlFile = null;
    if (urlByApp.TryGetValue(g.Id, out var urls))
    {
        cUrl++;
        foreach (var u in urls)
        {
            var p = u.IconFile is null ? null : StripIndex(u.IconFile);
            var exists = p is not null && File.Exists(p);
            var kind = p is null ? "no IconFile" : p.StartsWith(gamesDir + "\\", StringComparison.OrdinalIgnoreCase) ? "steam\\games\\*.ico" : "other " + Path.GetExtension(p);
            kind += exists ? " (exists)" : " (missing)";
            urlKind[kind] = urlKind.GetValueOrDefault(kind) + 1;
            if (exists) urlFile ??= p;
        }
        if (urlFile is not null) cUrlExists++;
        row["urlIconFile"] = urls[0].IconFile;
    }

    var any = lcIcon is not null || ico is not null || regFile is not null || urlFile is not null;
    if (any) cUnion++;
    if (ico is not null || regFile is not null) cIcoOrReg++;
    if (lcIcon is null && (ico is not null || regFile is not null || urlFile is not null)) cUnionNoLc++;
    if (!any) missing.Add($"{g.Id} {g.Name} type={row["type"]} icon={iconHash} clienticon={clientIcon} lcDir={Directory.Exists(dir)}");
    row["any"] = any;
    rows.Add(row);
}
var total = steamGames.Count;
string P(int c) => $"{c}/{total} ({100.0 * c / total:F1}%)";
Log("");
Log("=== Coverage over Steam games in after-config.json ===");
Log($"appinfo entry present:             {P(cAppinfo)}");
Log($"appinfo common.icon set:           {P(cIconField)}");
Log($"appinfo common.clienticon set:     {P(cClientIconField)}");
Log($"appinfo common.clienttga set:      {P(cTgaField)}");
Log($"librarycache\\<appid>\\ exists:      {P(cLcDir)}");
Log($"(1) librarycache icon jpg:         {P(cLcIcon)}  (name == common.icon: {cLcIconMatch})");
Log($"(2) steam\\games\\<clienticon>.ico:  {P(cIco)}");
Log($"(3) Uninstall DisplayIcon value:   {P(cReg)}  (target file exists: {cRegExists})");
Log($"(4) .url shortcut found:           {P(cUrl)}  (IconFile exists: {cUrlExists})");
Log($"(2)|(3) .ico or registry:          {P(cIcoOrReg)}");
Log($"UNION of (1)-(4):                  {P(cUnion)}");
Log($"  union minus librarycache gains:  {cUnionNoLc}");
Log($"librarycache icon dims: {string.Join(", ", lcDims.OrderByDescending(k => k.Value).Select(k => $"{k.Key}:{k.Value}"))}; square {lcSquare}; bytes min/median/max {Stat(lcBytes)}");
Log($"steam\\games ico max entry edge: {string.Join(", ", icoMaxDist.OrderBy(k => k.Key).Select(k => $"{k.Key}px:{k.Value}"))}; all-square {icoSquare}/{cIco}; with PNG-compressed entry {icoHasPngEntry}; bytes min/median/max {Stat(icoBytes)}");
Log($"DisplayIcon target kinds: {string.Join("; ", regKind.OrderByDescending(k => k.Value).Select(k => $"{k.Key}:{k.Value}"))}");
Log($".url IconFile kinds: {string.Join("; ", urlKind.OrderByDescending(k => k.Value).Select(k => $"{k.Key}:{k.Value}"))}");
Log($"Games with NO local icon from any of these sources: {missing.Count}");
foreach (var m in missing) Log("   " + m);

// ---------- uninstalled / not-in-config ----------
Log("");
Log("=== Apps NOT installed (no appmanifest) ===");
var lcNotInst = lcDirs.Where(id => !installed.Contains(id)).ToList();
int Typed(IEnumerable<uint> ids, string t) => ids.Count(id => apps.TryGetValue(id, out var a) && string.Equals(a.Common.GetValueOrDefault("type"), t, StringComparison.OrdinalIgnoreCase));
bool HasLcIcon(uint id) => Directory.Exists(Path.Combine(libCache, id.ToString())) && Directory.EnumerateFiles(Path.Combine(libCache, id.ToString()), "*.jpg").Any(f => hex40.IsMatch(Path.GetFileNameWithoutExtension(f)));
bool HasIco(uint id) => apps.TryGetValue(id, out var a) && a.Common.TryGetValue("clienticon", out var h) && !string.IsNullOrEmpty(h) && icoFiles.ContainsKey(h.ToLowerInvariant());
var typeCensus = lcNotInst.GroupBy(id => apps.TryGetValue(id, out var a) ? (a.Common.GetValueOrDefault("type") ?? "(none)").ToLowerInvariant() : "(not in appinfo)")
    .OrderByDescending(gr => gr.Count()).Select(gr => $"{gr.Key}:{gr.Count()}");
Log($"librarycache folders for non-installed apps: {lcNotInst.Count}; by appinfo type: {string.Join(", ", typeCensus)}");
var lcNotInstGames = lcNotInst.Where(id => apps.TryGetValue(id, out var a) && string.Equals(a.Common.GetValueOrDefault("type"), "game", StringComparison.OrdinalIgnoreCase)).ToList();
Log($"  of those type=game: {lcNotInstGames.Count}; with icon jpg: {lcNotInstGames.Count(HasLcIcon)}; with steam\\games ico: {lcNotInstGames.Count(HasIco)}; either: {lcNotInstGames.Count(id => HasLcIcon(id) || HasIco(id))}");
var icoAppsNotInst = apps.Keys.Where(id => !installed.Contains(id) && HasIco(id)).ToList();
Log($"apps (any type) not installed whose clienticon .ico exists in steam\\games: {icoAppsNotInst.Count} (type=game: {Typed(icoAppsNotInst, "game")})");
var lcInstalledNotInConfig = installed.Where(id => !steamGames.Any(g => g.Id == id)).ToList();
Log($"appmanifests not in config (tools/redist etc.): {lcInstalledNotInConfig.Count}");

// localconfig owned approximation
foreach (var ud in Directory.EnumerateDirectories(Path.Combine(Steam, "userdata")))
{
    var lc = Path.Combine(ud, @"config\localconfig.vdf");
    if (!File.Exists(lc)) continue;
    var txt = File.ReadAllText(lc);
    var appsIdx = txt.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
    // count numeric keys followed by { anywhere under Software/Valve/Steam/apps (approximation)
    var ids = Regex.Matches(txt, "^\\t{5}\"(\\d+)\"\\s*$", RegexOptions.Multiline).Select(m => uint.Parse(m.Groups[1].Value)).ToHashSet();
    var idsGames = ids.Where(id => apps.TryGetValue(id, out var a) && string.Equals(a.Common.GetValueOrDefault("type"), "game", StringComparison.OrdinalIgnoreCase)).ToList();
    var notInst = idsGames.Where(id => !installed.Contains(id)).ToList();
    Log($"localconfig.vdf {Path.GetFileName(ud)}: depth-5 numeric app keys {ids.Count}, type=game {idsGames.Count}, not installed {notInst.Count}; of those with lc icon {notInst.Count(HasLcIcon)}, with .ico {notInst.Count(HasIco)}, either {notInst.Count(id => HasLcIcon(id) || HasIco(id))}");
}

File.WriteAllText(Path.Combine(outDir, "summary.txt"), sb.ToString());
File.WriteAllText(Path.Combine(outDir, "per-game.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));

static string Stat(List<long> v)
{
    if (v.Count == 0) return "-";
    var s = v.OrderBy(x => x).ToList();
    return $"{s[0]}/{s[s.Count / 2]}/{s[^1]}";
}

sealed record App(Dictionary<string, string> Common);

static class AppInfo
{
    public static (uint Magic, Dictionary<uint, App> Apps) ParseAll(byte[] d)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(d);
        var pos = 8;
        string[]? keys = null;
        var end = d.Length;
        if (magic == 0x07564429)
        {
            var off = (int)BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(8));
            pos = 16; end = off;
            var p = off;
            var cnt = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p)); p += 4;
            keys = new string[cnt];
            for (var i = 0; i < cnt; i++) keys[i] = CStr(d, ref p);
        }
        else if (magic is not (0x07564428 or 0x07564427)) throw new InvalidDataException($"magic {magic:X8}");
        var hdr = magic == 0x07564427 ? 40 : 60;
        var apps = new Dictionary<uint, App>();
        while (pos < end)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos)); pos += 4;
            if (id == 0) break;
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos)); pos += 4;
            var entryEnd = pos + size;
            var p = pos + hdr;
            var common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ReadObj(d, ref p, keys, 0, common);
            apps[id] = new App(common);
            pos = entryEnd;
        }
        return (magic, apps);
    }

    // level: 0 root, 1 appinfo, 2 common, >2 deeper/other
    static void ReadObj(byte[] d, ref int p, string[]? keys, int path, Dictionary<string, string> common)
    {
        while (true)
        {
            var t = d[p++];
            if (t is 0x08 or 0x0B) return;
            string key;
            if (keys is null) key = CStr(d, ref p);
            else { key = keys[BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p))]; p += 4; }
            switch (t)
            {
                case 0x00:
                    var next = (path, key) switch { (0, "appinfo") => 1, (1, "common") => 2, _ => 9 };
                    ReadObj(d, ref p, keys, next, common);
                    break;
                case 0x01:
                    var v = CStr(d, ref p);
                    if (path == 2) common[key] = v;
                    break;
                case 0x02 or 0x03 or 0x04 or 0x06: p += 4; break;
                case 0x07 or 0x0A: p += 8; break;
                case 0x05: while (!(d[p] == 0 && d[p + 1] == 0)) p += 2; p += 2; break;
                default: throw new InvalidDataException($"type {t:X2}");
            }
        }
    }

    static string CStr(byte[] d, ref int p)
    {
        var len = d.AsSpan(p).IndexOf((byte)0);
        var s = Encoding.UTF8.GetString(d, p, len);
        p += len + 1;
        return s;
    }
}

static class Img
{
    public static (int W, int H) JpegSize(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return (-1, -1);
        var p = 2;
        while (p + 9 < b.Length)
        {
            if (b[p] != 0xFF) { p++; continue; }
            var m = b[p + 1];
            if (m == 0xFF) { p++; continue; }
            if (m is 0xD8 or 0x01 || (m >= 0xD0 && m <= 0xD7)) { p += 2; continue; }
            var len = (b[p + 2] << 8) | b[p + 3];
            if (m is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return ((b[p + 7] << 8) | b[p + 8], (b[p + 5] << 8) | b[p + 6]);
            p += 2 + len;
        }
        return (-1, -1);
    }

    public static List<(int W, int H, bool Png, int Bpp)> IcoEntries(byte[] b)
    {
        var r = new List<(int, int, bool, int)>();
        if (b.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2)) != 1) return r;
        int n = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4));
        for (var i = 0; i < n && 6 + 16 * (i + 1) <= b.Length; i++)
        {
            var e = 6 + 16 * i;
            int w = b[e] == 0 ? 256 : b[e], h = b[e + 1] == 0 ? 256 : b[e + 1];
            int bpp = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(e + 6));
            var off = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(e + 12));
            var png = off + 24 <= b.Length && b[off] == 0x89 && b[off + 1] == 0x50 && b[off + 2] == 0x4E && b[off + 3] == 0x47;
            if (png)
            {
                w = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(off + 16));
                h = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(off + 20));
            }
            r.Add((w, h, png, bpp));
        }
        return r;
    }
}
