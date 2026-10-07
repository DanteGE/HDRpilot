using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Games;

public sealed class SearchFolderCandidateFinderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Missing_search_folder_is_skipped()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\A\a.exe", "MZ");

        var candidates = new SearchFolderCandidateFinder(fs).FindCandidates([@"Z:\Fehlt", @"D:\Spielordner", "", "relativ"], [], Ct);

        Assert.Equal(["A"], candidates.Select(c => c.Name));
    }

    [Fact]
    public void Direct_subfolders_with_exe_up_to_depth_3_are_candidates()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\Oben\Oben.exe", "MZ")
            .AddFile(@"D:\Spielordner\Tief\Projekt\Binaries\Win64\Tief-Win64-Shipping.exe", "MZ")
            .AddFile(@"D:\Spielordner\ZuTief\a\b\c\d\spiel.exe", "MZ")
            .AddFile(@"D:\Spielordner\lose.exe", "MZ");

        var candidates = new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner"], [], Ct);

        Assert.Equal(["Oben", "Tief"], candidates.Select(c => c.Name));
        Assert.Equal(@"D:\Spielordner\Tief", candidates[1].Folder);
        Assert.Equal([@"D:\Spielordner\Tief\Projekt\Binaries\Win64\Tief-Win64-Shipping.exe"], candidates[1].Executables);
    }

    [Fact]
    public void Executables_are_ordered_top_level_first_and_exclude_helpers()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\UE\Engine\Binaries\Win64\CrashReportClient.exe", "MZ")
            .AddFile(@"D:\Spielordner\UE\Engine\Extras\Redist\en-us\UEPrereqSetup_x64.exe", "MZ")
            .AddFile(@"D:\Spielordner\UE\Spiel\Binaries\Win64\Spiel-Win64-Shipping.exe", "MZ")
            .AddFile(@"D:\Spielordner\UE\Spiel.exe", "MZ")
            .AddFile(@"D:\Spielordner\UE\Alpha.exe", "MZ");

        var candidate = Assert.Single(new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner"], [], Ct));

        Assert.Equal(
            [@"D:\Spielordner\UE\Alpha.exe", @"D:\Spielordner\UE\Spiel.exe", @"D:\Spielordner\UE\Spiel\Binaries\Win64\Spiel-Win64-Shipping.exe"],
            candidate.Executables);
    }

    [Fact]
    public void Within_a_level_exes_named_like_the_folder_come_first()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"F:\GOG-Spiele\SWAT 4\Content\System\CreateLocalizationPackage.exe", "MZ")
            .AddFile(@"F:\GOG-Spiele\SWAT 4\Content\System\IGEffectsConfigurator.exe", "MZ")
            .AddFile(@"F:\GOG-Spiele\SWAT 4\Content\System\Swat4.exe", "MZ")
            .AddFile(@"F:\GOG-Spiele\SWAT 4\ContentExpansion\System\Swat4X.exe", "MZ");

        var candidate = Assert.Single(new SearchFolderCandidateFinder(fs).FindCandidates([@"F:\GOG-Spiele"], [], Ct));

        Assert.Equal(
            [
                @"F:\GOG-Spiele\SWAT 4\Content\System\Swat4.exe",
                @"F:\GOG-Spiele\SWAT 4\ContentExpansion\System\Swat4X.exe",
                @"F:\GOG-Spiele\SWAT 4\Content\System\CreateLocalizationPackage.exe",
                @"F:\GOG-Spiele\SWAT 4\Content\System\IGEffectsConfigurator.exe",
            ],
            candidate.Executables);
    }

    [Fact]
    public void Folders_equal_below_or_above_known_install_dirs_are_left_out()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\Bekannt\spiel.exe", "MZ")                    // gleich einem bekannten Ordner
            .AddFile(@"D:\Spielordner\Sammlung\Steam\common\X\x.exe", "MZ")        // darüber
            .AddFile(@"D:\Spielordner\Doppelt\d.exe", "MZ")                        // gleich, über ".." geschrieben
            .AddFile(@"D:\Spielordner\Neu\neu.exe", "MZ")
            .AddFile(@"E:\GOG\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe", "MZ"); // Suchordner liegt in einem Spielordner: alles darunter

        var candidates = new SearchFolderCandidateFinder(fs).FindCandidates(
            ["d:/spielordner/", @"E:\GOG\Cyberpunk 2077"],
            [@"D:\SPIELORDNER\bekannt", @"D:\Spielordner\Sammlung\Steam\common\X", "", "   ", @"D:\Spielordner\Doppelt\bin\..\", @"e:/gog/cyberpunk 2077"],
            Ct);

        Assert.Equal(["Neu"], candidates.Select(c => c.Name));
    }

    [Fact]
    public void Search_folder_inside_another_search_folder_is_not_its_own_candidate()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\Indie\Spiel1\a.exe", "MZ")
            .AddFile(@"D:\Spielordner\Indie\Spiel2\b.exe", "MZ")
            .AddFile(@"D:\Spielordner\Gross\g.exe", "MZ");

        var candidates = new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner", @"D:\Spielordner\Indie", @"D:\SPIELORDNER"], [], Ct);

        Assert.Equal(["Gross", "Spiel1", "Spiel2"], candidates.Select(c => c.Name));
    }

    [Fact]
    public void Incomplete_folder_with_only_helpers_or_no_exe_is_not_a_candidate()
    {
        var fs = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\NurHelfer\UnityCrashHandler64.exe", "MZ")
            .AddFile(@"D:\Spielordner\NurHelfer\_CommonRedist\vc_redist.x64.exe", "MZ")
            .AddFile(@"D:\Spielordner\Leer\daten.pak", "x")
            .AddDirectory(@"D:\Spielordner\GanzLeer");

        Assert.Empty(new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner"], [], Ct));
    }

    [Fact]
    public void Broken_enumeration_error_skips_only_that_folder()
    {
        var inner = new InMemoryFileSystem()
            .AddFile(@"D:\Spielordner\Kaputt\k.exe", "MZ")
            .AddFile(@"D:\Spielordner\Gut\g.exe", "MZ");
        var fs = new ThrowingFileSystem(inner, @"D:\Spielordner\Kaputt");

        var candidates = new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner"], [], Ct);

        Assert.Equal(["Gut"], candidates.Select(c => c.Name));
    }

    [Fact]
    public void Finder_only_reads_and_candidate_becomes_entry_only_on_request()
    {
        var inner = new InMemoryFileSystem().AddFile(@"D:\Spielordner\A\a.exe", "MZ");
        var fs = new ThrowingFileSystem(inner, failingDirectory: null); // wirft bei jedem Schreibzugriff

        var candidate = Assert.Single(new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner"], [], Ct));

        var entry = ManualGames.Create(inner, candidate.Folder, candidate.Name); // erst nach Bestätigung durch den Nutzer
        Assert.Equal(GameSource.Manual, entry.Source);
        Assert.Equal(@"D:\Spielordner\A\a.exe", entry.LaunchCommand);
    }

    [Fact]
    public void Cancellation_is_respected()
    {
        var fs = new InMemoryFileSystem().AddFile(@"D:\Spielordner\A\a.exe", "MZ");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new SearchFolderCandidateFinder(fs).FindCandidates([@"D:\Spielordner"], [], cts.Token));
    }

    /// <summary>
    /// Dateisystem, dessen Aufzählung in einem Ordner wie bei einem Gerätefehler wirft. Schreibzugriffe werfen immer,
    /// damit Tests belegen, dass der Finder nur liest.
    /// </summary>
    private sealed class ThrowingFileSystem(InMemoryFileSystem inner, string? failingDirectory) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern = "*", bool recursive = false) =>
            Fails(directory) ? throw new IOException("Das Gerät ist nicht bereit.") : inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern = "*", bool recursive = false) =>
            Fails(directory) ? throw new UnauthorizedAccessException("Zugriff verweigert.") : inner.EnumerateDirectories(directory, searchPattern, recursive);

        public void CreateDirectory(string path) => throw new InvalidOperationException("Schreibzugriff: " + path);

        public void WriteAllTextAtomic(string path, string contents) => throw new InvalidOperationException("Schreibzugriff: " + path);

        public void DeleteFile(string path) => throw new InvalidOperationException("Schreibzugriff: " + path);

        public IReadOnlyList<string> GetFixedDriveRoots() => inner.GetFixedDriveRoots();

        private bool Fails(string directory) =>
            failingDirectory is not null && string.Equals(directory.TrimEnd('\\'), failingDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
