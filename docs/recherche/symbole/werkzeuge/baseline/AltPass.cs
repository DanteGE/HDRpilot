// Second pass: (1) final "generic" set from visually confirmed reference hashes, (2) near-generic candidates,
// (3) for every game without a good icon: would a deeper local EXE search (depth <= 4, skip icon-less/generic) find one?
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using HdrPilot.App.Services;
using HdrPilot.Core.Games;
using HdrPilot.Core.Util;
using SD = System.Drawing;

internal static partial class Program
{
    private static int Dist(Row a, Row b) =>
        (int)(ulong.PopCount(a.DH ^ b.DH) + ulong.PopCount(a.AH ^ b.AH) + ulong.PopCount(a.AM ^ b.AM))
        + (Math.Abs(a.MeanR - b.MeanR) + Math.Abs(a.MeanG - b.MeanG) + Math.Abs(a.MeanB - b.MeanB)) / 15;

    private sealed record Alt(string Path, long Size, string Status, Row? Icon);

    private static string AltPass(List<Row> rows, string refsPath, IReadOnlyList<string>? patterns, string outDir)
    {
        var refs = File.ReadAllLines(refsPath).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t'))
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        var refRows = rows.Where(r => r.Hash is not null && refs.ContainsKey(r.Hash)).GroupBy(r => r.Hash!).Select(g => g.First()).ToList();
        bool IsGenericIcon(Row r) => r.Hash is not null && (refs.ContainsKey(r.Hash) || refRows.Any(x => Dist(x, r) <= 8));

        var sb = new StringBuilder();
        sb.AppendLine("=== Generic icons (exact match with visually confirmed reference hashes, generic-refs.txt) ===");
        foreach (var (hash, label) in refs)
        {
            var l = rows.Where(r => string.Equals(r.Hash, hash, StringComparison.OrdinalIgnoreCase)).ToList();
            sb.AppendLine($"  {hash} {label}: {l.Count} games: {string.Join(" | ", l.Select(r => r.G.Name).OrderBy(n => n))}");
        }

        var generic = rows.Where(r => r.Hash is not null && refs.ContainsKey(r.Hash)).ToList();
        sb.AppendLine($"  total generic (exact ref match): {generic.Count}; of these in class c: {generic.Count(r => r.Cls == "c")}, in class d (cluster < 3 games): {generic.Count(r => r.Cls == "d")}");
        sb.AppendLine($"  class c members that are NOT generic (same game family): {string.Join(" | ", rows.Where(r => r.Cls == "c" && !refs.ContainsKey(r.Hash!)).Select(r => r.G.Name))}");
        sb.AppendLine("  by source: " + string.Join(", ", generic.GroupBy(r => r.G.Source).Select(g => $"{g.Key}={g.Count()}")));
        sb.AppendLine();

        sb.AppendLine("=== Nearest non-reference icons to any reference (perceptual distance, lower = more similar; <= 8 would count as generic) ===");
        var near = rows.Where(r => r.Hash is not null && !refs.ContainsKey(r.Hash))
            .Select(r => (r, d: refRows.Count == 0 ? 999 : refRows.Min(x => Dist(x, r)), refHash: refRows.OrderBy(x => Dist(x, r)).FirstOrDefault()?.Hash))
            .OrderBy(t => t.d).Take(15).ToList();
        foreach (var (r, d, rh) in near) sb.AppendLine($"  d={d,3}  {r.G.Name} ({Path.GetFileName(r.Exe)}, {r.Hash}) ~ {rh} {refs.GetValueOrDefault(rh ?? "")}");
        sb.AppendLine();

        // ---- local alternatives ----
        var targets = rows.Where(r => r.Cls is "a" or "b" || (r.Hash is not null && refs.ContainsKey(r.Hash))).OrderBy(r => r.Cls).ThenBy(r => r.G.Name).ToList();
        sb.AppendLine($"=== Local alternatives: {targets.Count} games without a good icon (a + b + generic). Search: all non-helper, non-excluded *.exe up to 3 folder levels below installDir (MaxRecursionDepth=3, skip reparse points, max 300 EXEs), extract 48 px icon with GameIconService.ExtractIcon, skip none/generic; pick the largest remaining EXE ===");
        var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var found = new List<(Row Game, Alt Best)>();
        int fixedCount = 0;
        var times = new List<double>();
        foreach (var t in targets)
        {
            var sw = Stopwatch.StartNew();
            var dir = PathUtil.Normalize(t.G.InstallDir);
            var cands = new List<Alt>();
            int scanned = 0, noIcon = 0, gen = 0, unreadable = 0;
            if (dir.Length > 3 && Directory.Exists(dir))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*.exe", opts).Take(300).ToList(); } catch { files = []; }
                foreach (var f in files)
                {
                    if (HelperExecutables.IsHelper(f, patterns) || t.G.ExcludedExecutables.Any(x => string.Equals(x.Trim(), Path.GetFileName(f), StringComparison.OrdinalIgnoreCase) || PathUtil.PathEquals(x, f))) continue;
                    scanned++;
                    long size = 0;
                    try { size = new FileInfo(f).Length; } catch { }
                    try { using var s = File.OpenRead(f); } catch { unreadable++; continue; }
                    var img = GameIconService.ExtractIcon(f, Size);
                    if (img is not BitmapSource bs) { noIcon++; continue; }
                    var ir = new Row { G = t.G, Exe = f, Px = ToBgra(bs) };
                    ir.Hash = Convert.ToHexString(SHA256.HashData(ir.Px))[..16];
                    PHash(ir);
                    if (IsGenericIcon(ir)) { gen++; continue; }
                    cands.Add(new Alt(f, size, "ok", ir));
                }
            }

            times.Add(sw.Elapsed.TotalMilliseconds);
            var best = cands.OrderByDescending(c => c.Size).FirstOrDefault();
            var curName = t.Exe is null ? "-" : Path.GetFileName(t.Exe);
            if (best is not null)
            {
                fixedCount++;
                found.Add((t, best));
                var others = cands.Select(c => c.Icon!.Hash).Distinct().Count();
                sb.AppendLine($"  FOUND  [{t.Cls}] {t.G.Name}: {curName} -> {Path.GetRelativePath(dir, best.Path)} ({best.Size / 1048576.0:F1} MB; {cands.Count} usable EXEs, {others} distinct icons; scanned {scanned}, no icon {noIcon}, generic {gen}, unreadable {unreadable})");
            }
            else
            {
                sb.AppendLine($"  NONE   [{t.Cls}] {t.G.Name}: {curName} (scanned {scanned}, no icon {noIcon}, generic {gen}, unreadable {unreadable})");
            }
        }

        times.Sort();
        sb.AppendLine($"  => local alternative found for {fixedCount} of {targets.Count}; deeper scan time per game: median {(times.Count > 0 ? times[times.Count / 2] : 0):F0} ms, max {(times.Count > 0 ? times[^1] : 0):F0} ms, sum {times.Sum() / 1000:F1} s");
        AltSheet(found, rows, Path.Combine(outDir, "local-alternatives-sheet.png"));
        NearSheet(near.Select(x => x.r).ToList(), refRows, Path.Combine(outDir, "near-generic-candidates-sheet.png"));
        return sb.ToString();
    }

    private static void AltSheet(List<(Row Game, Alt Best)> found, List<Row> rows, string path)
    {
        const int rh = 60, width = 1100;
        using var bmp = new SD.Bitmap(width, Math.Max(1, found.Count) * rh + 30, SD.Imaging.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        g.Clear(SD.Color.FromArgb(32, 32, 36));
        g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new SD.Font("Segoe UI", 12, SD.GraphicsUnit.Pixel);
        using var w = new SD.SolidBrush(SD.Color.White);
        using var gr = new SD.SolidBrush(SD.Color.FromArgb(180, 180, 180));
        g.DrawString("current icon (empty = none)  ->  largest non-generic EXE icon found up to 3 levels deep", f, w, 8, 6);
        int y = 28;
        foreach (var (game, best) in found)
        {
            var cur = rows.First(r => ReferenceEquals(r.G, game.G));
            if (cur.Px is not null) { using var cb = ToBitmap(cur.Px); g.DrawImage(cb, 8, y, Size, Size); }
            g.DrawString("->", f, w, 64, y + 16);
            using (var ab = ToBitmap(best.Icon!.Px!)) g.DrawImage(ab, 90, y, Size, Size);
            g.DrawString(game.G.Name, f, w, 150, y + 4);
            g.DrawString(Path.GetRelativePath(PathUtil.Normalize(game.G.InstallDir), best.Path), f, gr, 150, y + 24);
            y += rh;
        }

        bmp.Save(path, SD.Imaging.ImageFormat.Png);
    }

    private static void NearSheet(List<Row> near, List<Row> refs, string path)
    {
        const int cw = 130, ch = 96;
        int n = refs.Count + near.Count;
        int cols = 8;
        using var bmp = new SD.Bitmap(cols * cw, ((n + cols - 1) / cols) * ch + 10, SD.Imaging.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        g.Clear(SD.Color.FromArgb(32, 32, 36));
        g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new SD.Font("Segoe UI", 10, SD.GraphicsUnit.Pixel);
        using var w = new SD.SolidBrush(SD.Color.FromArgb(210, 210, 210));
        int i = 0;
        foreach (var (r, label) in refs.Select(r => (r, "REF " + r.G.Name)).Concat(near.Select(r => (r, r.G.Name))))
        {
            int x = i % cols * cw, y = i / cols * ch;
            using var b = ToBitmap(r.Px!);
            g.DrawImage(b, x + (cw - Size) / 2, y + 4, Size, Size);
            g.DrawString(label, f, w, new SD.RectangleF(x + 2, y + 56, cw - 4, 36), new SD.StringFormat { Alignment = SD.StringAlignment.Center });
            i++;
        }

        bmp.Save(path, SD.Imaging.ImageFormat.Png);
    }
}
