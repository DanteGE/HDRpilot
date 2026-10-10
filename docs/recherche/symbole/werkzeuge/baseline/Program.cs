// Baseline measurement for HDRpilot game icons.
// Uses the app's own compiled code (HDRpilot.dll / HdrPilot.Core.dll copied from the p4 worktree build):
//   IconExecutableResolver.Resolve(game, Settings.HelperExecutablePatterns)  and  GameIconService.ExtractIcon(exe, 48)
// Read-only: only reads config copy, game folders and EXE resources. Writes only to the output folder given as arg 2.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;
using SD = System.Drawing;

internal sealed class Row
{
    public required GameConfig G;
    public string? Exe;
    public string PickedBy = "";
    public string Cls = "";          // a, b, c, d
    public string Reason = "";
    public byte[]? Px;               // 48x48 BGRA, alpha==0 pixels normalised to 0
    public string? Hash;
    public ulong DH, AH, AM;          // perceptual hashes
    public int MeanR, MeanG, MeanB;
    public double ResolveMs, ExtractMs;
    public string Engine = "";
    public int Cluster = -1;          // exact-hash cluster id (only for clusters with >= 3 games)
    public int NearGroup = -1;        // perceptual near-duplicate group (>= 3 games, not an exact cluster)
    public int GroupIconCount = -1;   // RT_GROUP_ICON resources in the exe (-1 unknown)
    public int FirstGroupMaxSize = -1;// largest image size (px) in the first RT_GROUP_ICON
    public string ExtractError = "";
    public bool SuspectName;
    public long ExeSize;
}

internal static partial class Program
{
    private const int Size = GameIconService.IconSize; // 48

    [STAThread]
    private static int Main(string[] args)
    {
        var configPath = args[0];
        var outDir = args[1];
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "icons"));

        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), ConfigJson.Options)!;
        var patterns = config.Settings.HelperExecutablePatterns; // null in the user's config -> default list, as in the app
        var resolver = new IconExecutableResolver(PhysicalFileSystem.Instance);
        var rows = new List<Row>();
        var total = Stopwatch.StartNew();
        foreach (var g in config.Games)
        {
            var r = new Row { G = g };
            var sw = Stopwatch.StartNew();
            r.Exe = resolver.Resolve(g, patterns);
            r.ResolveMs = sw.Elapsed.TotalMilliseconds;
            r.Engine = EngineHint(g.InstallDir);
            if (r.Exe is null)
            {
                r.Cls = "a";
                r.Reason = WhyNoExe(g, patterns);
            }
            else
            {
                r.PickedBy = g.DetectedExecutables.Any(x => PathUtil.PathEquals(x, r.Exe)) ? "detected"
                    : g.KnownExecutables.Any(x => PathUtil.PathEquals(x, r.Exe)) ? "known" : "folder-scan";
                try { r.ExeSize = new FileInfo(r.Exe).Length; } catch { }
                r.SuspectName = SuspectExeName(Path.GetFileNameWithoutExtension(r.Exe));
                (r.GroupIconCount, r.FirstGroupMaxSize) = GroupIconInfo(r.Exe);
                sw.Restart();
                var img = GameIconService.ExtractIcon(r.Exe, Size);
                r.ExtractMs = sw.Elapsed.TotalMilliseconds;
                if (img is not BitmapSource bs)
                {
                    r.Cls = "b";
                    // Second call only to tell "no icon resource" from an exception swallowed by the app.
                    try
                    {
                        using var ic = SD.Icon.ExtractIcon(r.Exe, 0, Size);
                        r.ExtractError = ic is null ? "ExtractIcon returned null" : "icon on 2nd call?";
                    }
                    catch (Exception ex) { r.ExtractError = ex.GetType().Name + ": " + ex.Message; }
                    r.Reason = r.GroupIconCount == 0 ? "exe has no RT_GROUP_ICON" : r.ExtractError;
                }
                else
                {
                    r.Px = ToBgra(bs);
                    r.Hash = Convert.ToHexString(SHA256.HashData(r.Px))[..16];
                    PHash(r);
                }
            }

            rows.Add(r);
        }

        total.Stop();

        // ---- exact clusters ------------------------------------------------------------------------------
        var exactGroups = rows.Where(r => r.Hash is not null).GroupBy(r => r.Hash!)
            .Where(gr => gr.Select(x => x.G.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3)
            .OrderByDescending(gr => gr.Count()).ToList();
        int cid = 0;
        foreach (var gr in exactGroups)
        {
            cid++;
            foreach (var r in gr) { r.Cluster = cid; r.Cls = "c"; }
        }

        // ---- perceptual near-duplicates (union-find over distinct hashes) -------------------------------
        var distinct = rows.Where(r => r.Hash is not null).GroupBy(r => r.Hash!).Select(gr => gr.First()).ToList();
        var parent = Enumerable.Range(0, distinct.Count).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        for (int i = 0; i < distinct.Count; i++)
        for (int j = i + 1; j < distinct.Count; j++)
        {
            if (Near(distinct[i], distinct[j])) parent[Find(i)] = Find(j);
        }

        var hashToRoot = new Dictionary<string, int>();
        for (int i = 0; i < distinct.Count; i++) hashToRoot[distinct[i].Hash!] = Find(i);
        var nearGroups = rows.Where(r => r.Hash is not null).GroupBy(r => hashToRoot[r.Hash!])
            .Where(gr => gr.Select(x => x.Hash).Distinct().Count() >= 2 && gr.Count() >= 3)
            .OrderByDescending(gr => gr.Count()).ToList();
        int nid = 0;
        foreach (var gr in nearGroups)
        {
            nid++;
            foreach (var r in gr) r.NearGroup = nid;
        }

        foreach (var r in rows.Where(r => r.Px is not null && r.Cls == "")) r.Cls = "d";

        // ---- outputs --------------------------------------------------------------------------------------
        foreach (var d in distinct) SavePng(d.Px!, Path.Combine(outDir, "icons", d.Hash + ".png"));
        WriteCsv(rows, Path.Combine(outDir, "results.csv"));
        var report = BuildReport(rows, exactGroups, nearGroups, total.Elapsed, patterns);
        File.WriteAllText(Path.Combine(outDir, "report.txt"), report, new UTF8Encoding(false));
        ClusterSheet(exactGroups.Select((gr, i) => ($"C{i + 1}", gr.ToList())).ToList(), Path.Combine(outDir, "c-clusters-contact-sheet.png"));
        ClusterSheet(nearGroups.Select((gr, i) => ($"N{i + 1}", gr.ToList())).ToList(), Path.Combine(outDir, "near-duplicate-groups-sheet.png"));
        AllIconsSheets(rows, outDir);
        Console.WriteLine(report);
        if (args.Length >= 3)
        {
            var alt = AltPass(rows, args[2], patterns, outDir);
            File.WriteAllText(Path.Combine(outDir, "generic-and-alternatives.txt"), alt, new UTF8Encoding(false));
            Console.WriteLine(alt);
        }

        return 0;
    }

    // ------------------------------------------------------------------------------------------------------
    private static string WhyNoExe(GameConfig g, IReadOnlyList<string>? patterns)
    {
        if (!g.IsInstalled) return "not installed";
        var dir = PathUtil.Normalize(g.InstallDir);
        if (string.IsNullOrWhiteSpace(dir)) return "no installDir";
        if (dir.Length <= 3) return "installDir is drive root";
        if (dir.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return "WindowsApps";
        bool exists;
        try { exists = Directory.Exists(dir); } catch (Exception ex) { return "dir check threw " + ex.GetType().Name; }
        if (!exists) return "installDir missing";
        try { _ = Directory.EnumerateFileSystemEntries(dir).Take(1).ToList(); }
        catch (UnauthorizedAccessException) { return "access denied"; }
        catch (Exception ex) { return "enum threw " + ex.GetType().Name; }

        // What is there? top + 1 level (like the resolver, without its limits) and deeper (depth<=5) for diagnosis.
        int top = 0, oneLevel = 0, helpers = 0, subdirs = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.exe")) { top++; if (HelperExecutables.IsHelper(f, patterns)) helpers++; }
            foreach (var s in Directory.EnumerateDirectories(dir))
            {
                subdirs++;
                try { foreach (var f in Directory.EnumerateFiles(s, "*.exe")) { oneLevel++; if (HelperExecutables.IsHelper(f, patterns)) helpers++; } }
                catch { }
            }
        }
        catch { }

        var deeper = new List<string>();
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in Directory.EnumerateFiles(dir, "*.exe", opts))
            {
                var rel = Path.GetRelativePath(dir, f);
                if (rel.Count(c => c == '\\') >= 2 && !HelperExecutables.IsHelper(f, patterns)) deeper.Add(rel);
                if (deeper.Count >= 3) break;
            }
        }
        catch { }

        var limit = subdirs > IconExecutableResolver.MaxSubdirectories ? $" (subdirs {subdirs} > limit {IconExecutableResolver.MaxSubdirectories})" : "";
        if (top + oneLevel == 0)
        {
            return deeper.Count > 0 ? $"no exe within 1 level; deeper: {string.Join(" | ", deeper)}{limit}" : $"no exe at all (depth<=6){limit}";
        }

        return $"only helper/excluded exes within 1 level ({top + oneLevel} exe, {helpers} helper){(deeper.Count > 0 ? "; deeper: " + string.Join(" | ", deeper) : "")}{limit}";
    }

    private static bool SuspectExeName(string stem)
    {
        string[] words = ["setup", "install", "config", "setting", "editor", "server", "benchmark", "redist", "tool", "helper",
            "report", "update", "patch", "launcher", "dedicated", "modkit", "sdk", "crash", "test", "dxwebsetup", "directx", "x86", "unitycrash"];
        var s = stem.ToLowerInvariant();
        return words.Any(w => s.Contains(w));
    }

    private static string EngineHint(string installDir)
    {
        try
        {
            var dir = PathUtil.Normalize(installDir);
            if (dir.Length <= 3 || !Directory.Exists(dir)) return "";
            var files = Directory.EnumerateFiles(dir).Select(Path.GetFileName).Select(x => x!.ToLowerInvariant()).ToHashSet();
            var dirs = Directory.EnumerateDirectories(dir).Select(Path.GetFileName).Select(x => x!.ToLowerInvariant()).ToList();
            if (files.Contains("unityplayer.dll") || dirs.Any(d => d.EndsWith("_data") && File.Exists(Path.Combine(dir, d, "globalgamemanagers")))) return "Unity";
            if (dirs.Contains("engine") && Directory.Exists(Path.Combine(dir, "Engine", "Binaries"))) return "Unreal4/5";
            if (dirs.Any(d => Directory.Exists(Path.Combine(dir, d, "Binaries", "Win64")) || Directory.Exists(Path.Combine(dir, d, "Binaries", "WinGDK")))) return "Unreal4/5";
            if (files.Any(f => f.EndsWith(".u")) || dirs.Contains("system") && Directory.EnumerateFiles(Path.Combine(dir, "System"), "*.u").Any()) return "Unreal1-3";
            if (files.Contains("data.win")) return "GameMaker";
            if (files.Any(f => f.EndsWith(".pck"))) return "Godot";
            if (dirs.Contains("renpy")) return "RenPy";
            if (files.Contains("nw.dll") || files.Contains("nw_elf.dll")) return dirs.Contains("www") ? "RPGMakerMV/MZ(NW.js)" : "NW.js";
            if (files.Any(f => f.StartsWith("rgss") && f.EndsWith(".dll"))) return "RPGMaker(RGSS)";
            if (File.Exists(Path.Combine(dir, "resources", "app.asar"))) return "Electron";
            if (files.Contains("love.dll")) return "LOVE";
            if (files.Contains("monogame.framework.dll")) return "MonoGame";
            if (files.Contains("fna.dll")) return "FNA";
            if (files.Any(f => f.StartsWith("microsoft.xna"))) return "XNA";
            if (dirs.Contains("bin") && File.Exists(Path.Combine(dir, "bin", "engine.dll"))) return "Source";
            if (dirs.Contains("jre") || dirs.Contains("jre64") || files.Any(f => f.EndsWith(".jar"))) return "Java";
            if (files.Contains("cefsharp.dll") || files.Contains("libcef.dll")) return "CEF";
            return "";
        }
        catch { return ""; }
    }

    // ---- icon resource inspection (read-only, datafile mapping) ---------------------------------------
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2, LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryExW(string f, IntPtr h, uint flags);
    [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr h);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool EnumResourceNamesW(IntPtr h, IntPtr type, EnumResNameProc cb, IntPtr l);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr FindResourceW(IntPtr h, IntPtr name, IntPtr type);
    [DllImport("kernel32")] private static extern IntPtr LoadResource(IntPtr h, IntPtr res);
    [DllImport("kernel32")] private static extern IntPtr LockResource(IntPtr h);
    [DllImport("kernel32")] private static extern uint SizeofResource(IntPtr h, IntPtr res);
    private delegate bool EnumResNameProc(IntPtr h, IntPtr type, IntPtr name, IntPtr l);

    private static (int count, int maxSize) GroupIconInfo(string exe)
    {
        var h = LoadLibraryExW(exe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (h == IntPtr.Zero) return (-1, -1);
        try
        {
            var names = new List<IntPtr>();
            var strs = new List<IntPtr>();
            EnumResourceNamesW(h, 14, (m, t, n, l) =>
            {
                if (((long)n >> 16) == 0) names.Add(n);
                else { var p = Marshal.StringToHGlobalUni(Marshal.PtrToStringUni(n)); strs.Add(p); names.Add(p); }
                return true;
            }, IntPtr.Zero);
            int max = -1;
            if (names.Count > 0)
            {
                var res = FindResourceW(h, names[0], 14);
                if (res != IntPtr.Zero)
                {
                    var len = (int)SizeofResource(h, res);
                    var ptr = LockResource(LoadResource(h, res));
                    if (ptr != IntPtr.Zero && len >= 6)
                    {
                        var buf = new byte[len];
                        Marshal.Copy(ptr, buf, 0, len);
                        int count = BitConverter.ToUInt16(buf, 4);
                        for (int i = 0; i < count && 6 + i * 14 + 14 <= len; i++)
                        {
                            int w = buf[6 + i * 14];
                            if (w == 0) w = 256;
                            max = Math.Max(max, w);
                        }
                    }
                }
            }

            foreach (var p in strs) Marshal.FreeHGlobal(p);
            return (names.Count, max);
        }
        catch { return (-1, -1); }
        finally { FreeLibrary(h); }
    }

    // ---- pixels & hashes ------------------------------------------------------------------------------
    private static byte[] ToBgra(BitmapSource bs)
    {
        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
        if (conv.PixelWidth != Size || conv.PixelHeight != Size) throw new InvalidOperationException($"unexpected size {conv.PixelWidth}x{conv.PixelHeight}");
        var px = new byte[Size * Size * 4];
        conv.CopyPixels(px, Size * 4, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) { px[i] = px[i + 1] = px[i + 2] = 0; }
        }

        return px;
    }

    private static void PHash(Row r)
    {
        var px = r.Px!;
        var lum = new double[Size * Size];
        var alpha = new double[Size * Size];
        long sr = 0, sg = 0, sb = 0, sa = 0;
        for (int i = 0; i < Size * Size; i++)
        {
            double a = px[i * 4 + 3] / 255.0;
            double b = px[i * 4] * a + 128 * (1 - a), g = px[i * 4 + 1] * a + 128 * (1 - a), rr = px[i * 4 + 2] * a + 128 * (1 - a);
            lum[i] = 0.299 * rr + 0.587 * g + 0.114 * b;
            alpha[i] = px[i * 4 + 3];
            sr += (long)(px[i * 4 + 2] * a); sg += (long)(px[i * 4 + 1] * a); sb += (long)(px[i * 4] * a); sa += px[i * 4 + 3];
        }

        var area = Math.Max(1.0, sa / 255.0);
        r.MeanR = (int)(sr / area); r.MeanG = (int)(sg / area); r.MeanB = (int)(sb / area);
        var d = Downscale(lum, 9, 8);
        ulong dh = 0; int bit = 0;
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++, bit++) if (d[y * 9 + x] < d[y * 9 + x + 1]) dh |= 1UL << bit;
        var a8 = Downscale(lum, 8, 8);
        var mean = a8.Average();
        ulong ah = 0;
        for (int i = 0; i < 64; i++) if (a8[i] > mean) ah |= 1UL << i;
        var m8 = Downscale(alpha, 8, 8);
        ulong am = 0;
        for (int i = 0; i < 64; i++) if (m8[i] > 127) am |= 1UL << i;
        r.DH = dh; r.AH = ah; r.AM = am;
    }

    private static double[] Downscale(double[] src, int w, int h)
    {
        var o = new double[w * h];
        for (int oy = 0; oy < h; oy++)
        for (int ox = 0; ox < w; ox++)
        {
            int x0 = ox * Size / w, x1 = Math.Max(x0 + 1, (ox + 1) * Size / w), y0 = oy * Size / h, y1 = Math.Max(y0 + 1, (oy + 1) * Size / h);
            double s = 0; int n = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { s += src[y * Size + x]; n++; }
            o[oy * w + ox] = s / n;
        }

        return o;
    }

    private static bool Near(Row a, Row b) =>
        ulong.PopCount(a.DH ^ b.DH) <= 6 && ulong.PopCount(a.AH ^ b.AH) <= 6 && ulong.PopCount(a.AM ^ b.AM) <= 3
        && Math.Abs(a.MeanR - b.MeanR) + Math.Abs(a.MeanG - b.MeanG) + Math.Abs(a.MeanB - b.MeanB) <= 45;

    // ---- report -------------------------------------------------------------------------------------
    private static string BuildReport(List<Row> rows, List<IGrouping<string, Row>> exact, List<IGrouping<int, Row>> near, TimeSpan elapsed, IReadOnlyList<string>? patterns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HDRpilot icon baseline  {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Games: {rows.Count}, installed: {rows.Count(r => r.G.IsInstalled)}; helper patterns: {(patterns is null ? "default list (" + HelperExecutables.DefaultPatterns.Count + ")" : patterns.Count + " custom")}");
        sb.AppendLine($"Wall time (sequential, resolve+extract+resource probe+engine probe): {elapsed.TotalSeconds:F1} s");
        void Stats(string name, IEnumerable<double> v)
        {
            var l = v.OrderBy(x => x).ToList();
            if (l.Count == 0) return;
            sb.AppendLine($"  {name}: n={l.Count} median={l[l.Count / 2]:F1} ms p95={l[(int)(l.Count * 0.95)]:F1} ms max={l[^1]:F1} ms sum={l.Sum() / 1000:F1} s");
        }

        Stats("Resolve", rows.Select(r => r.ResolveMs));
        Stats("ExtractIcon(48)", rows.Where(r => r.Exe is not null).Select(r => r.ExtractMs));
        sb.AppendLine();
        sb.AppendLine("Classes: a = no EXE, b = EXE without icon, c = icon shared by >=3 games (exact pixel hash), d = looks game-specific");
        var sources = rows.Select(r => r.G.Source.ToString()).Distinct().OrderBy(s => s).ToList();
        sb.AppendLine($"{"source",-10} {"total",6} {"a",5} {"b",5} {"c",5} {"d",5}");
        foreach (var s in sources.Append("ALL"))
        {
            var set = s == "ALL" ? rows : rows.Where(r => r.G.Source.ToString() == s).ToList();
            sb.AppendLine($"{s,-10} {set.Count,6} {set.Count(r => r.Cls == "a"),5} {set.Count(r => r.Cls == "b"),5} {set.Count(r => r.Cls == "c"),5} {set.Count(r => r.Cls == "d"),5}");
        }

        foreach (var s in new[] { "BattleNet", "Manual" }.Where(s => !sources.Contains(s))) sb.AppendLine($"{s,-10} {0,6}  (no games of this source in the config)");
        sb.AppendLine();
        sb.AppendLine("EXE picked by: " + string.Join(", ", rows.Where(r => r.Exe is not null).GroupBy(r => r.PickedBy).Select(g => $"{g.Key}={g.Count()}")));
        var withIcon = rows.Where(r => r.Px is not null).ToList();
        sb.AppendLine($"Distinct icons (exact pixel hash) among {withIcon.Count} extracted: {withIcon.Select(r => r.Hash).Distinct().Count()}");
        sb.AppendLine($"Extracted icons whose first RT_GROUP_ICON has no image >= 48 px (upscaled): {withIcon.Count(r => r.FirstGroupMaxSize is > 0 and < 48)}  (<=32: {withIcon.Count(r => r.FirstGroupMaxSize is > 0 and <= 32)}, unknown: {withIcon.Count(r => r.FirstGroupMaxSize <= 0)})");
        sb.AppendLine($"Picked EXE name contains a suspicious word (setup/config/editor/server/launcher/tool/...): {rows.Count(r => r.SuspectName)}");
        sb.AppendLine();

        sb.AppendLine("=== (a) no EXE found ===");
        foreach (var r in rows.Where(r => r.Cls == "a").OrderByDescending(r => r.G.IsInstalled).ThenBy(r => r.G.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  [{r.G.Source}] {r.G.Name}{(r.G.IsInstalled ? "" : " (not installed)")} -- {r.Reason} -- {r.G.InstallDir}");
        sb.AppendLine("  reason summary: " + string.Join("; ", rows.Where(r => r.Cls == "a").GroupBy(r => r.Reason.Split(' ', 4) is var p && p.Length >= 3 ? string.Join(' ', p.Take(3)) : r.Reason).Select(g => $"{g.Key}={g.Count()}")));
        sb.AppendLine();
        sb.AppendLine("=== (b) EXE found, no icon ===");
        foreach (var r in rows.Where(r => r.Cls == "b").OrderByDescending(r => r.G.IsInstalled).ThenBy(r => r.G.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  [{r.G.Source}] {r.G.Name} -- {Path.GetFileName(r.Exe)} ({r.ExeSize / 1024 / 1024.0:F1} MB, {r.PickedBy}, engine={r.Engine}) -- {r.Reason}");
        sb.AppendLine();
        sb.AppendLine("=== (c) exact clusters (icon shared by >= 3 games) ===");
        int i = 0;
        foreach (var gr in exact)
        {
            i++;
            var l = gr.ToList();
            sb.AppendLine($"C{i}: {l.Count} games, {l.Select(r => r.Exe).Distinct(StringComparer.OrdinalIgnoreCase).Count()} distinct EXEs, hash {gr.Key}, engines: {string.Join(", ", l.GroupBy(r => r.Engine == "" ? "?" : r.Engine).Select(g => $"{g.Key}={g.Count()}"))}, firstGroupMaxSize={string.Join("/", l.Select(r => r.FirstGroupMaxSize).Distinct())}");
            sb.AppendLine($"    exe names: {string.Join(", ", l.Select(r => Path.GetFileName(r.Exe)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))}");
            foreach (var r in l.OrderBy(r => r.G.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"    - [{r.G.Source}] {r.G.Name}  ({Path.GetFileName(r.Exe)})");
        }

        sb.AppendLine();
        sb.AppendLine("=== perceptual near-duplicate groups (>= 3 games, >= 2 distinct exact hashes; informational, not counted as c unless also exact) ===");
        i = 0;
        foreach (var gr in near)
        {
            i++;
            var l = gr.ToList();
            sb.AppendLine($"N{i}: {l.Count} games, {l.Select(r => r.Hash).Distinct().Count()} variants, exact clusters inside: {string.Join(",", l.Where(r => r.Cluster > 0).Select(r => "C" + r.Cluster).Distinct())}, engines: {string.Join(", ", l.GroupBy(r => r.Engine == "" ? "?" : r.Engine).Select(g => $"{g.Key}={g.Count()}"))}");
            foreach (var r in l.OrderBy(r => r.G.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"    - [{r.G.Source}] {r.G.Name}  ({Path.GetFileName(r.Exe)}, {r.Hash}{(r.Cluster > 0 ? ", C" + r.Cluster : "")})");
        }

        sb.AppendLine();
        sb.AppendLine("=== engine hint (all games) x class ===");
        foreach (var g in rows.GroupBy(r => r.Engine == "" ? "?" : r.Engine).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Key,-22} total={g.Count(),4} a={g.Count(r => r.Cls == "a"),3} b={g.Count(r => r.Cls == "b"),3} c={g.Count(r => r.Cls == "c"),3} d={g.Count(r => r.Cls == "d"),4}");
        sb.AppendLine();
        sb.AppendLine("=== picked EXE with suspicious name (class d/c only) ===");
        foreach (var r in rows.Where(r => r.SuspectName && r.Px is not null).OrderBy(r => r.G.Name, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  [{r.G.Source}] {r.G.Name} -> {Path.GetRelativePath(PathUtil.Normalize(r.G.InstallDir), r.Exe!)} ({r.Cls})");
        return sb.ToString();
    }

    private static void WriteCsv(List<Row> rows, string path)
    {
        static string Q(object? o) { var s = o?.ToString() ?? ""; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var sb = new StringBuilder();
        sb.AppendLine("source,sourceId,name,isInstalled,class,reason,engine,pickedBy,exe,exeSizeBytes,groupIconCount,firstGroupMaxSize,suspectName,hash,cluster,nearGroup,resolveMs,extractMs,installDir");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Q(r.G.Source), Q(r.G.SourceId), Q(r.G.Name), Q(r.G.IsInstalled), Q(r.Cls), Q(r.Reason), Q(r.Engine), Q(r.PickedBy), Q(r.Exe),
                Q(r.ExeSize), Q(r.GroupIconCount), Q(r.FirstGroupMaxSize), Q(r.SuspectName), Q(r.Hash), Q(r.Cluster > 0 ? "C" + r.Cluster : ""), Q(r.NearGroup > 0 ? "N" + r.NearGroup : ""),
                Q(r.ResolveMs.ToString("F1")), Q(r.ExtractMs.ToString("F1")), Q(r.G.InstallDir)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    // ---- drawing --------------------------------------------------------------------------------------
    private static SD.Bitmap ToBitmap(byte[] px)
    {
        var bmp = new SD.Bitmap(Size, Size, SD.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new SD.Rectangle(0, 0, Size, Size), SD.Imaging.ImageLockMode.WriteOnly, SD.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < Size; y++) Marshal.Copy(px, y * Size * 4, data.Scan0 + y * data.Stride, Size * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    private static void SavePng(byte[] px, string path)
    {
        using var b = ToBitmap(px);
        b.Save(path, SD.Imaging.ImageFormat.Png);
    }

    private static void ClusterSheet(List<(string Id, List<Row> Rows)> groups, string path)
    {
        const int rowH = 128, width = 1500;
        int height = Math.Max(1, groups.Count) * rowH + 40;
        using var bmp = new SD.Bitmap(width, height, SD.Imaging.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        g.Clear(SD.Color.FromArgb(32, 32, 36));
        g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
        using var big = new SD.Font("Segoe UI", 12, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
        using var small = new SD.Font("Segoe UI", 11, SD.GraphicsUnit.Pixel);
        using var white = new SD.SolidBrush(SD.Color.White);
        using var grey = new SD.SolidBrush(SD.Color.FromArgb(190, 190, 190));
        using var light = new SD.SolidBrush(SD.Color.FromArgb(225, 225, 225));
        g.DrawString(Path.GetFileNameWithoutExtension(path) + "  (left: 48 px as the app renders it, on light and dark; right: variants)", big, white, 8, 8);
        int y = 36;
        foreach (var (id, rows) in groups)
        {
            var variants = rows.Where(r => r.Px is not null).GroupBy(r => r.Hash).Select(x => x.First()).Take(10).ToList();
            var first = variants[0];
            using (var ib = ToBitmap(first.Px!))
            {
                g.FillRectangle(light, 8, y, 56, 56);
                g.DrawImage(ib, 12, y + 4, Size, Size);
                g.InterpolationMode = SD.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = SD.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(ib, 70, y, 96, 96);
                g.InterpolationMode = SD.Drawing2D.InterpolationMode.Default;
                g.PixelOffsetMode = SD.Drawing2D.PixelOffsetMode.Default;
                g.DrawImage(ib, 12, y + 64, Size, Size);
            }

            int vx = 176;
            foreach (var v in variants.Skip(1))
            {
                using var vb = ToBitmap(v.Px!);
                g.DrawImage(vb, vx, y + 4, Size, Size);
                vx += 52;
            }

            int tx = Math.Max(176, vx + 8);
            var engines = string.Join(", ", rows.GroupBy(r => r.Engine == "" ? "?" : r.Engine).OrderByDescending(x => x.Count()).Select(x => $"{x.Key}={x.Count()}"));
            g.DrawString($"{id}: {rows.Count} games, {rows.Select(r => r.Exe).Distinct(StringComparer.OrdinalIgnoreCase).Count()} distinct EXEs, {variants.Count} pixel variant(s)  |  engine hint: {engines}", big, white, tx, y + 2);
            g.DrawString("EXE: " + string.Join(", ", rows.Select(r => Path.GetFileName(r.Exe)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8)), small, grey, new SD.RectangleF(tx, y + 22, width - tx - 8, 16));
            var names = string.Join("  ·  ", rows.Select(r => r.G.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            g.DrawString(names, small, grey, new SD.RectangleF(tx, y + 40, width - tx - 8, rowH - 46));
            y += rowH;
        }

        bmp.Save(path, SD.Imaging.ImageFormat.Png);
    }

    private static void AllIconsSheets(List<Row> rows, string outDir)
    {
        const int cols = 12, rowsPerPage = 9, cw = 124, ch = 96;
        var list = rows.Where(r => r.Px is not null).OrderBy(r => r.G.Name, StringComparer.OrdinalIgnoreCase).ToList();
        int pages = (list.Count + cols * rowsPerPage - 1) / (cols * rowsPerPage);
        var index = new StringBuilder();
        for (int p = 0; p < pages; p++)
        {
            var chunk = list.Skip(p * cols * rowsPerPage).Take(cols * rowsPerPage).ToList();
            using var bmp = new SD.Bitmap(cols * cw, rowsPerPage * ch, SD.Imaging.PixelFormat.Format32bppArgb);
            using var g = SD.Graphics.FromImage(bmp);
            g.Clear(SD.Color.FromArgb(32, 32, 36));
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
            using var f = new SD.Font("Segoe UI", 10, SD.GraphicsUnit.Pixel);
            using var grey = new SD.SolidBrush(SD.Color.FromArgb(200, 200, 200));
            using var red = new SD.Pen(SD.Color.OrangeRed, 2);
            using var blue = new SD.Pen(SD.Color.DeepSkyBlue, 2);
            for (int i = 0; i < chunk.Count; i++)
            {
                var r = chunk[i];
                int x = i % cols * cw, y = i / cols * ch;
                using var ib = ToBitmap(r.Px!);
                g.DrawImage(ib, x + (cw - Size) / 2, y + 4, Size, Size);
                if (r.Cls == "c") g.DrawRectangle(red, x + (cw - Size) / 2 - 2, y + 2, Size + 4, Size + 4);
                else if (r.NearGroup > 0) g.DrawRectangle(blue, x + (cw - Size) / 2 - 2, y + 2, Size + 4, Size + 4);
                var label = $"{p + 1}.{i + 1} {r.G.Name}";
                g.DrawString(label, f, grey, new SD.RectangleF(x + 2, y + 56, cw - 4, 38), new SD.StringFormat { Alignment = SD.StringAlignment.Center, Trimming = SD.StringTrimming.EllipsisCharacter });
                index.AppendLine($"{p + 1}.{i + 1}\t{r.Cls}\t{r.Hash}\t[{r.G.Source}] {r.G.Name}\t{r.Exe}");
            }

            bmp.Save(Path.Combine(outDir, $"all-icons-{p + 1:00}.png"), SD.Imaging.ImageFormat.Png);
        }

        File.WriteAllText(Path.Combine(outDir, "all-icons-index.tsv"), index.ToString(), new UTF8Encoding(true));
    }
}
