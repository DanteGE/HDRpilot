// Read-only probe 2: icon quality union, uninstalled coverage via registry/.url, decode tests.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

static class P2
{
    const string Steam = @"C:\Program Files (x86)\Steam";

    [STAThread]
    static void Main(string[] args)
    {
        var outDir = args[0];
        var sb = new StringBuilder();
        void Log(string s) { Console.WriteLine(s); sb.AppendLine(s); }
        var rows = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(outDir, "per-game.json"))).RootElement.EnumerateArray().ToList();
        string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        // installed set
        var libs = Regex.Matches(File.ReadAllText(Path.Combine(Steam, @"steamapps\libraryfolders.vdf")), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));
        var installed = libs.SelectMany(l => Directory.Exists(Path.Combine(l, "steamapps")) ? Directory.EnumerateFiles(Path.Combine(l, "steamapps"), "appmanifest_*.acf") : [])
            .Select(f => uint.Parse(Path.GetFileNameWithoutExtension(f)["appmanifest_".Length..])).ToHashSet();

        // registry (64-bit view, where the 64-bit Steam client writes)
        var reg = new Dictionary<uint, string?>();
        using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var u = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")!)
            foreach (var n in u.GetSubKeyNames().Where(n => n.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)))
                if (uint.TryParse(n[10..], out var id))
                    using (var k = u.OpenSubKey(n)) reg[id] = k?.GetValue("DisplayIcon") as string;
        var regNotInst = reg.Where(kv => !installed.Contains(kv.Key)).ToList();
        Log($"Uninstall 'Steam App *' keys (HKLM 64-bit view): {reg.Count}; app installed (manifest): {reg.Count - regNotInst.Count}; NOT installed: {regNotInst.Count}, of which DisplayIcon file exists: {regNotInst.Count(kv => Exists(kv.Value))}");

        // .url
        var urlDirs = new[] { System.Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Steam"), System.Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Desktop") };
        var urls = new Dictionary<uint, List<string?>>();
        foreach (var d in urlDirs.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(d, "*.url", SearchOption.AllDirectories))
            {
                var t = File.ReadAllText(f);
                var m = Regex.Match(t, @"^URL=steam://rungameid/(\d+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var ic = Regex.Match(t, @"^IconFile=(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                var id = uint.Parse(m.Groups[1].Value);
                if (!urls.TryGetValue(id, out var l)) urls[id] = l = [];
                l.Add(ic.Success ? ic.Groups[1].Value.Trim() : null);
            }
        var urlNotInst = urls.Where(kv => !installed.Contains(kv.Key)).ToList();
        Log($".url steam shortcuts (Start Menu\\Programs\\Steam + Desktop): distinct appids {urls.Count}; NOT installed: {urlNotInst.Count}, IconFile exists: {urlNotInst.Count(kv => kv.Value.Any(Exists))}");
        var anyNotInst = regNotInst.Select(kv => kv.Key).Concat(urlNotInst.Select(kv => kv.Key)).Distinct().ToList();
        Log($"Not-installed apps with registry or .url icon present: {anyNotInst.Count(id => (reg.TryGetValue(id, out var r) && Exists(r)) || (urls.TryGetValue(id, out var l) && l.Any(Exists)))}");

        // best ico per game across clienticon / DisplayIcon / .url
        var edgeDist = new SortedDictionary<int, int>();
        int ge48 = 0, ge256 = 0, anyIco = 0, has32bpp = 0;
        var extractOk = 0; var extractFail = new List<string>(); var extractMs = 0L; var extractSizes = new Dictionary<string, int>();
        var sw = new Stopwatch();
        foreach (var r in rows)
        {
            var id = r.GetProperty("appid").GetUInt32();
            var cands = new List<string>();
            var ci = S(r, "clienticon");
            if (!string.IsNullOrEmpty(ci)) cands.Add(Path.Combine(Steam, @"steam\games", ci + ".ico"));
            if (reg.TryGetValue(id, out var rv) && rv is not null) cands.Add(Strip(rv));
            if (urls.TryGetValue(id, out var ul)) cands.AddRange(ul.Where(x => x is not null).Select(x => Strip(x!)));
            cands = cands.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cands.Count == 0) continue;
            anyIco++;
            var best = cands.Select(c => (Path: c, E: Ico(File.ReadAllBytes(c)))).OrderByDescending(x => x.E.Count == 0 ? 0 : x.E.Max(e => Math.Min(e.W, e.H))).First();
            var edge = best.E.Count == 0 ? 0 : best.E.Max(e => Math.Min(e.W, e.H));
            edgeDist[edge] = edgeDist.GetValueOrDefault(edge) + 1;
            if (edge >= 48) ge48++;
            if (edge >= 256) ge256++;
            if (best.E.Any(e => e.Bpp == 32 || e.Png)) has32bpp++;

            // System.Drawing.Icon.ExtractIcon on the .ico (what GameIconService already uses for EXEs)
            sw.Restart();
            try
            {
                using var icon = System.Drawing.Icon.ExtractIcon(best.Path, 0, 48);
                sw.Stop();
                if (icon is null) extractFail.Add($"{id} null"); else { extractOk++; var k = $"{icon.Width}x{icon.Height}"; extractSizes[k] = extractSizes.GetValueOrDefault(k) + 1; }
            }
            catch (Exception ex) { sw.Stop(); extractFail.Add($"{id} {ex.GetType().Name}"); }
            extractMs += sw.ElapsedMilliseconds;
        }
        Log($"Games with at least one existing .ico (clienticon/DisplayIcon/.url): {anyIco}/{rows.Count}");
        Log($"  best ico largest square-ish entry (min(w,h)): {string.Join(", ", edgeDist.Select(kv => $"{kv.Key}:{kv.Value}"))}");
        Log($"  best ico >=48px: {ge48}; >=256px: {ge256}; with 32-bit/PNG (alpha) entry: {has32bpp}");
        Log($"  Icon.ExtractIcon(path,0,48): ok {extractOk}, fail {extractFail.Count} [{string.Join("; ", extractFail.Take(5))}], sizes {string.Join(", ", extractSizes.Select(kv => $"{kv.Key}:{kv.Value}"))}, total {extractMs} ms");

        // WPF decode of librarycache 32x32 jpg at 48 px
        int decOk = 0, decFail = 0; long decMs = 0; var decSizes = new Dictionary<string, int>();
        foreach (var r in rows)
        {
            var lc = S(r, "lcIcon");
            if (lc is null) continue;
            var path = Path.Combine(Steam, @"appcache\librarycache", r.GetProperty("appid").GetUInt32().ToString(), lc);
            sw.Restart();
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = 48;
                bi.EndInit();
                bi.Freeze();
                sw.Stop();
                decOk++;
                var k = $"{bi.PixelWidth}x{bi.PixelHeight} {bi.Format}";
                decSizes[k] = decSizes.GetValueOrDefault(k) + 1;
            }
            catch { sw.Stop(); decFail++; }
            decMs += sw.ElapsedMilliseconds;
        }
        Log($"WPF BitmapImage(DecodePixelWidth=48) on librarycache icon jpg: ok {decOk}, fail {decFail}, results {string.Join(", ", decSizes.Select(kv => $"{kv.Key}:{kv.Value}"))}, total {decMs} ms");

        File.WriteAllText(Path.Combine(outDir, "summary2.txt"), sb.ToString());
    }

    static string Strip(string s)
    {
        s = s.Trim().Trim('"');
        var m = Regex.Match(s, @"^(.*?)(,\s*-?\d+)?$");
        return m.Groups[1].Value.Trim().Trim('"');
    }

    static bool Exists(string? s) => !string.IsNullOrWhiteSpace(s) && File.Exists(Strip(s));

    static List<(int W, int H, bool Png, int Bpp)> Ico(byte[] b)
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
            if (png) { w = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(off + 16)); h = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(off + 20)); }
            r.Add((w, h, png, bpp));
        }
        return r;
    }
}
