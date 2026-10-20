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
/// Weg (Junction, 8.3-Name, <c>\\?\</c>). Junctions legt <c>mklink /J</c> an, nur im Temp-Ordner des Tests; verweigerte Rechte
/// (nicht lesbare Ordner und Dateien) setzt <see cref="AccessDenial"/> nur an eigenen Testdateien und nimmt sie wieder zurück.
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
    public void Uninstalled_exe_search_reports_folders_it_cannot_read_instead_of_skipping_them()
    {
        // Prüfung 2026-10-20: Einen Installationsordner, den Windows nicht auflisten lässt (wie C:\System Volume Information),
        // übersprang die Suche still; die Prüfung vor „Einstellungen entfernen…“ hielt ihn für „keine EXE“ statt für „nicht
        // prüfbar“ (E90). Jetzt liefert sie erst alle lesbaren Treffer und meldet danach den Ordner, den sie nicht lesen konnte.
        var spiel = Path.Combine(_dir, "Spiel");
        var exe = Put(@"Spiel\Spiel.exe");
        Put(@"Spiel\Gesperrt\Versteckt.exe");
        var gesperrt = Path.Combine(_dir, "Gesperrt");
        Put(@"Gesperrt\Spiel.exe");
        Put(@"Darunter\Spiel\Gesperrt\Versteckt.exe");
        Put(@"Tief\a\b\c\d\Gesperrt\Versteckt.exe");
        Put(@"Verborgen\Spiel\Spiel.exe");
        using var denial = new AccessDenial()
            .DenyListing(Path.Combine(spiel, "Gesperrt")).DenyListing(gesperrt).DenyListing(Path.Combine(_dir, @"Darunter\Spiel\Gesperrt"))
            .DenyListing(Path.Combine(_dir, @"Tief\a\b\c\d\Gesperrt"))
            .DenyAttributes(Path.Combine(_dir, @"Verborgen\Spiel")).DenyListing(Path.Combine(_dir, "Verborgen"));
        Assert.True(Directory.Exists(gesperrt)); // wie System Volume Information: da, aber nicht aufzulisten
        Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFiles(gesperrt));
        Assert.Empty(Directory.EnumerateFiles(gesperrt, "*.exe", new EnumerationOptions { IgnoreInaccessible = true })); // so sah es vorher aus

        // Ein Unterordner nicht lesbar: erst der lesbare Treffer, dann der Fehler; wer beim ersten Treffer aufhört, merkt nichts.
        var seen = new List<string>();
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            foreach (var file in PhysicalFileSystem.Instance.EnumerateFilesUpTo(spiel, "*.exe", 4))
            {
                seen.Add(file);
            }
        });
        Assert.Equal([exe], seen);
        Assert.True(PhysicalFileSystem.Instance.EnumerateFilesUpTo(spiel, "*.exe", 4).Any());

        // Der Ordner selbst oder nur ein Unterordner ohne lesbare EXE: nicht zu klären.
        Assert.Throws<UnauthorizedAccessException>(() => PhysicalFileSystem.Instance.EnumerateFilesUpTo(gesperrt, "*.exe", 4).Any());
        Assert.Throws<UnauthorizedAccessException>(() => PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, @"Darunter\Spiel"), "*.exe", 4).Any());

        // Der Ordner, dessen Ordner darüber nicht lesbar ist und der selbst keine Auskunft gibt: Directory.Exists sagt „nein“,
        // fehlen tut er nicht.
        Assert.False(Directory.Exists(Path.Combine(_dir, @"Verborgen\Spiel")));
        Assert.Throws<UnauthorizedAccessException>(() => PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, @"Verborgen\Spiel"), "*.exe", 4).Any());

        // Gegenproben: Unterhalb der Suchtiefe stört ein nicht lesbarer Ordner nicht (dort wird nicht gesucht), ebenso nicht
        // ohne Unterordner; ein fehlender Ordner und eine Datei statt eines Ordners sind leer.
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, "Tief"), "*.exe", 4));
        Assert.Equal([exe], PhysicalFileSystem.Instance.EnumerateFilesUpTo(spiel, "*.exe", 0));
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateFilesUpTo(Path.Combine(_dir, "Fehlt"), "*.exe", 4));
        Assert.Empty(PhysicalFileSystem.Instance.EnumerateFilesUpTo(exe, "*.exe", 4));
    }

    [Fact]
    public void Uninstalled_file_probe_tells_a_missing_file_from_one_windows_will_not_show()
    {
        // Prüfung 2026-10-20: File.Exists sagt auch „nein“, wenn Windows die Auskunft verweigert (eine gemerkte EXE in einem
        // nicht lesbaren Ordner). FileExistsOrThrow wirft dann; fehlt sie wirklich, sagt Windows das auch dort.
        var hidden = Put(@"Gesperrt\Spiel.exe");
        var open = Put(@"Offen\Spiel.exe");
        using var denial = new AccessDenial().DenyAttributes(hidden).DenyListing(Path.Combine(_dir, "Gesperrt"));
        var files = PhysicalFileSystem.Instance;

        Assert.False(File.Exists(hidden)); // so sah es vorher aus
        Assert.Throws<UnauthorizedAccessException>(() => files.FileExistsOrThrow(hidden));
        Assert.False(files.FileExistsOrThrow(Path.Combine(_dir, @"Gesperrt\Fehlt.exe")));

        // Gegenproben: vorhanden, fehlend, ein Ordner, ein fehlender Ordner, eine Datei als Ordner, ein ungültiger Name und ein
        // Laufwerk, das es nicht gibt.
        Assert.True(files.FileExistsOrThrow(open));
        Assert.False(files.FileExistsOrThrow(Path.Combine(_dir, @"Offen\Fehlt.exe")));
        Assert.False(files.FileExistsOrThrow(Path.Combine(_dir, "Offen")));
        Assert.False(files.FileExistsOrThrow(Path.Combine(_dir, @"Fehlt\Spiel.exe")));
        Assert.False(files.FileExistsOrThrow(open + @"\Spiel.exe"));
        Assert.False(files.FileExistsOrThrow(Path.Combine(_dir, "Spiel<1>.exe")));
        var drives = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var freeDrive = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).First(c => !drives.Contains(c) && !Directory.Exists(c + @":\"));
        Assert.False(files.FileExistsOrThrow(freeDrive + @":\Spiele\Spiel.exe"));
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
