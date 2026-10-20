using System.Diagnostics;
using System.Runtime.InteropServices;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Util;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Platform;

/// <summary>
/// Die echte Platte in einem eigenen Temp-Ordner (Prüfung 2026-10-20): Ersetzen der Konfiguration, während ein anderer Prozess
/// sie liest (Windows meldet dann „Zugriff verweigert“, auch wenn der Leser das Löschen freigibt), begrenzte Suche nach EXEs
/// ohne verknüpften Ordnern zu folgen, vollständiges Auflisten, das verknüpfte Ordner meldet, und derselbe Ordner auf anderem
/// Weg (Junction, 8.3-Name, <c>\\?\</c>). Junctions legt <c>mklink /J</c> an, nur im Temp-Ordner des Tests.
/// </summary>
public sealed partial class PhysicalFileSystemTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hdrpilot-dateisystem-" + Guid.NewGuid().ToString("N")[..8]);

    public PhysicalFileSystemTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            // Junctions zuerst als Verknüpfung entfernen, damit nie ihr Ziel geleert wird.
            foreach (var link in Directory.EnumerateDirectories(_dir, "*", SearchOption.AllDirectories).Where(d => new DirectoryInfo(d).LinkTarget is not null).ToList())
            {
                Directory.Delete(link);
            }

            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Put(string relative, string contents = "x")
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start)!)
        {
            process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(10_000), "mklink hängt");
        }

        Assert.True(new DirectoryInfo(link).LinkTarget is not null, "Junction nicht angelegt: " + link);
    }

    /// <summary>Ein Leser, der die Datei nach einer Weile schließt (auf dem Threadpool, während der Test synchron schreibt).</summary>
    private static async Task CloseLaterAsync(FileStream reader, int milliseconds)
    {
        await Task.Delay(milliseconds, TestContext.Current.CancellationToken).ConfigureAwait(false);
        await reader.DisposeAsync().ConfigureAwait(false);
    }

    private IEnumerable<string> Temps() => Directory.EnumerateFiles(_dir, "*.tmp", SearchOption.AllDirectories);

    [Fact]
    public async Task ConfigSave_atomic_write_waits_for_a_reader_that_holds_the_file_briefly()
    {
        // Wie das Prüfskript (Freigabe zum Lesen, Schreiben und Löschen): Windows verweigert das Ersetzen trotzdem. Das Schreiben
        // wartet, bis der Leser fertig ist, statt die Änderung zu verlieren.
        var path = Put("config.json", "alt");
        var release = CloseLaterAsync(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), 250);

        PhysicalFileSystem.Instance.WriteAllTextAtomic(path, "neu");
        await release;

        Assert.Equal("neu", File.ReadAllText(path));
        Assert.Empty(Temps());
    }

    [Fact]
    public void ConfigSave_atomic_write_keeps_the_old_file_whole_when_the_reader_stays()
    {
        // Liest einer länger als alle Versuche (wie File.ReadAllText ohne Freigabe zum Löschen): Die alte Datei bleibt ganz,
        // die temporäre verschwindet, und der Fehler geht an den Aufrufer.
        var path = Put("config.json", "alt");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var watch = Stopwatch.StartNew();

        Assert.Throws<UnauthorizedAccessException>(() => PhysicalFileSystem.Instance.WriteAllTextAtomic(path, "neu"));

        Assert.InRange(watch.Elapsed, FileReplace.MaxWait * 0.9, FileReplace.MaxWait * 3);
        Assert.Equal("alt", File.ReadAllText(path));
        Assert.Empty(Temps());
    }

    [Fact]
    public async Task ConfigSave_survives_a_reader_at_the_moment_of_replacing()
    {
        // Ende zu Ende: Die App speichert eine Einstellung, während ein anderer Prozess config.json gerade liest. Vorher scheiterte
        // das mit „Zugriff verweigert“, und die Änderung ging verloren.
        var clock = new FakeClock();
        var path = Path.Combine(_dir, "config.json");
        var service = new ConfigService(new ConfigStore(PhysicalFileSystem.Instance, path, clock), clock);
        var release = CloseLaterAsync(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), 200);

        service.Update(c => c.Settings.GracePeriodSeconds = 44);
        await release;

        Assert.Equal(44, service.Current.Settings.GracePeriodSeconds);
        Assert.Equal(44, new ConfigService(new ConfigStore(PhysicalFileSystem.Instance, path, clock), clock).Current.Settings.GracePeriodSeconds);
        Assert.Empty(Temps());
    }

    [Fact]
    public void Uninstalled_exe_search_is_bounded_and_does_not_follow_linked_folders()
    {
        // Prüfung vor „Einstellungen entfernen…“ (E90): EXEs bis vier Ebenen tief, nur nach Namen; einer Junction (etwa auf
        // einen anderen Spielordner oder ein anderes Laufwerk) folgt sie nicht.
        var deep = Put(@"Spiel\Vein\Binaries\Win64\L4\Tief.exe");
        Put(@"Spiel\a\b\c\d\e\ZuTief.exe");
        Put(@"Spiel\Vein\Saved\Logs\Vein.log");
        Put(@"Anderes\Fremd.exe");
        Junction(Path.Combine(_dir, @"Spiel\Verknuepft"), Path.Combine(_dir, "Anderes"));

        var found = PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, "Spiel"), "*.exe", 4).ToList();

        Assert.Equal([deep], found);
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, "Spiel"), "*.exe", 3));
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, "Fehlt"), "*.exe", 4));

        // Die Standardfassung (Attrappen) begrenzt die Tiefe genauso.
        var memory = new InMemoryFileSystem().AddFile(@"C:\Spiel\Oben.exe", "x").AddFile(@"C:\Spiel\a\b\c\d\Tief.exe", "x").AddFile(@"C:\Spiel\a\b\c\d\e\ZuTief.exe", "x");
        Assert.Equal([@"C:\Spiel\a\b\c\d\Tief.exe", @"C:\Spiel\Oben.exe"], ((IFileSystem)memory).EnumerateFilesUpTo(@"C:\Spiel", "*.exe", 4).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Uninstalled_complete_listing_reports_linked_folders_instead_of_skipping_them()
    {
        // Ob noch eine Verknüpfung die .ico zeigt (E90), darf nichts übersehen: Unterordner werden gelesen, ein verknüpfter
        // Ordner wird gemeldet (die .ico bleibt dann), ein fehlender Ordner ist leer.
        var inSub = Put(@"Startmenue\Spiele\Outlast (HDR).lnk");
        var top = Put(@"Startmenue\Steam.lnk");
        Put(@"Startmenue\Spiele\liesmich.txt");

        Assert.Equal([inSub, top], PhysicalFileSystem.Instance.EnumerateFilesCompletely(Path.Combine(_dir, "Startmenue"), "*.lnk").Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateFilesCompletely(Path.Combine(_dir, "Fehlt"), "*.lnk"));

        Put(@"Anderswo\Fremd.lnk");
        Junction(Path.Combine(_dir, @"Startmenue\Verknuepft"), Path.Combine(_dir, "Anderswo"));
        var ex = Assert.Throws<IOException>(() => PhysicalFileSystem.Instance.EnumerateFilesCompletely(Path.Combine(_dir, "Startmenue"), "*.lnk").ToList());
        Assert.Contains("Verknuepft", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiver_listing_without_links_skips_junctions()
    {
        // Suche nach dem Quiver-Ordner und nach der Start-EXE (E91): verknüpfte Ordner (etwa auf eine Netzfreigabe) fehlen.
        Put(@"Downloads\Echt\datei.txt");
        Put(@"Anderswo\Quiver\current\sq.version");
        Junction(Path.Combine(_dir, @"Downloads\Verknuepft"), Path.Combine(_dir, "Anderswo"));

        Assert.Equal([Path.Combine(_dir, @"Downloads\Echt")], PhysicalFileSystem.Instance.EnumerateDirectoriesWithoutLinks(Path.Combine(_dir, "Downloads")));
        Assert.Equal(2, PhysicalFileSystem.Instance.EnumerateDirectories(Path.Combine(_dir, "Downloads")).Count()); // Gegenprobe
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateDirectoriesWithoutLinks(Path.Combine(_dir, "Fehlt")));
    }

    [Fact]
    public void Quiver_game_folder_walk_does_not_follow_a_junction()
    {
        // Echter Quiver-Ordner im Temp-Ordner: Spiel mit bin\Game.exe und einer Junction „Mods“ auf einen Ordner mit einer
        // weiteren EXE. Folgte die Suche ihr, gäbe es zwei Kandidaten und keinen Start-Befehl.
        var root = Path.Combine(_dir, "Quiver");
        Put(@"Quiver\current\sq.version", File.ReadAllText(Path.Combine(InMemoryFileSystem.FixturesRoot, "Quiver", "sq.version")));
        Put(@"Quiver\apps.json", """{ "apps": [ { "folderName": "Spiel", "name": "Spiel" } ] }""");
        var game = Put(@"Quiver\Apps\Spiel\bin\Game.exe", "MZ");
        Put(@"Anderswo\Fremd.exe", "MZ");
        Junction(Path.Combine(root, @"Apps\Spiel\Mods"), Path.Combine(_dir, "Anderswo"));

        var result = new HdrPilot.Core.Games.Scanners.QuiverScanner(PhysicalFileSystem.Instance, new FakeRegistry(), new FakeEnvironment())
            .Scan(new HdrPilot.Core.Games.ScanOptions { QuiverFolders = [root] }, TestContext.Current.CancellationToken);

        Assert.Empty(result.Errors);
        var entry = Assert.Single(result.Games);
        Assert.Equal(game, entry.LaunchCommand);
        Assert.Equal([game], entry.Executables);
    }

    [Fact]
    public void Developer_switches_recognize_the_real_folder_on_another_way()
    {
        // HasOwnConfigDir (E90): Derselbe Ordner über eine Junction, mit kurzem 8.3-Namen oder mit \\?\ davor ist derselbe;
        // ein anderer Ordner nicht. Fehlt einer, entscheidet der Vergleich der Pfade.
        var real = Path.Combine(_dir, "EchteKonfiguration");
        Directory.CreateDirectory(real);
        var other = Path.Combine(_dir, "AndereAblage");
        Directory.CreateDirectory(other);
        var link = Path.Combine(_dir, "Umweg");
        Junction(link, real);

        Assert.True(PathUtil.SameDirectory(link, real));
        Assert.True(PathUtil.SameDirectory(@"\\?\" + real, real));
        Assert.True(PathUtil.SameDirectory(real.ToUpperInvariant() + @"\", real));
        Assert.False(PathUtil.SameDirectory(other, real));
        Assert.False(PathUtil.SameDirectory(Path.Combine(_dir, "Fehlt"), real));
        Assert.True(PathUtil.SameDirectory(Path.Combine(_dir, @"Fehlt\..\EchteKonfiguration"), real));

        var buffer = new char[1024];
        var length = GetShortPathName(real, buffer, (uint)buffer.Length);
        var shortPath = new string(buffer, 0, (int)length);
        Assert.True(length > 0, "Kurzer Name nicht lesbar");
        Assert.True(PathUtil.SameDirectory(shortPath, real), "8.3-Name: " + shortPath);

        // Gegenprobe zur Kennung: Den Umweg über die Junction erkennt der Vergleich der Pfade nicht.
        Assert.False(PathUtil.PathEquals(link, real));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathName(string lpszLongPath, [Out] char[] lpszShortPath, uint cchBuffer);
}
