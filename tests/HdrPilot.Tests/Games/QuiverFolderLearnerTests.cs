using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.Quiver;
using HdrPilot.Core.Hosting;
using HdrPilot.Core.Processes;
using HdrPilot.Tests.Scanners;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Games;

/// <summary>
/// Lernen des Quiver-Ordners aus der Prozessüberwachung (E91): Startet …\current\QuiverLauncher.exe eines echten
/// Quiver-Ordners, merkt HDRpilot den Ordner (versteckte Einstellung quiverFolders) und meldet ihn einmal; das nächste Einlesen
/// findet die Spiele dort ohne weiteren Hinweis.
/// </summary>
public sealed class QuiverFolderLearnerTests
{
    private const string ConfigPath = @"C:\Cfg\config.json";
    private const string Root = @"G:\Werkzeuge\QuiverLauncher";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new();
    private readonly ListLogger<QuiverFolderLearner> _log = new();
    private readonly ConfigService _config;
    private readonly QuiverFolderLearner _learner;
    private readonly List<string> _learned = [];

    public QuiverFolderLearnerTests()
    {
        _config = new ConfigService(new ConfigStore(_fs, ConfigPath, _clock), _clock);
        _learner = new QuiverFolderLearner(_fs, _config, _log);
        _learner.Learned += (_, folder) => _learned.Add(folder);
    }

    [Fact]
    public void Quiver_folder_is_learned_when_current_quiver_launcher_starts()
    {
        AddQuiver(Root);

        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe")]);

        Assert.Equal([Root], _config.Current.Settings.QuiverFolders);
        Assert.Equal([Root], _learned);
        Assert.Contains("\"quiverFolders\"", _fs.ReadAllText(ConfigPath), StringComparison.Ordinal);
        Assert.True(_log.Contains(LogLevel.Information, "Ordner " + Root + " gelernt"));
    }

    [Fact]
    public void Quiver_learning_checks_each_path_once_and_writes_only_new_folders()
    {
        AddQuiver(Root);
        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe")]);
        var revision = _config.Revision;

        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe", 43)]);
        _learner.Observe([Process(Root + @"\QuiverLauncher.exe", 44)]); // Starter von Velopack: derselbe Ordner

        Assert.Equal(revision, _config.Revision);
        Assert.Single(_learned);
    }

    [Fact]
    public void Quiver_known_folder_is_not_written_again()
    {
        AddQuiver(Root);
        _config.Update(c => c.Settings.QuiverFolders = [Root.ToLowerInvariant() + @"\"]);
        var revision = _config.Revision;

        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe")]);

        Assert.Equal(revision, _config.Revision);
        Assert.Empty(_learned);
    }

    [Theory]
    [InlineData(@"G:\Fremd\current\QuiverLauncher.exe")] // gleicher Name, kein Kennzeichen von Quiver
    [InlineData(@"G:\Werkzeuge\QuiverLauncher\current\Andere.exe")] // anderes Programm im Quiver-Ordner
    [InlineData(null)] // Pfad nicht lesbar
    public void Quiver_other_programs_are_not_learned(string? path)
    {
        AddQuiver(Root);
        _fs.AddFile(@"G:\Fremd\current\QuiverLauncher.exe", "MZ");
        _fs.AddFile(@"G:\Fremd\apps.json", "{ \"apps\": [] }");

        _learner.Observe([new ProcessInfo(42, Path.GetFileName(path) ?? "QuiverLauncher.exe", path)]);

        Assert.Null(_config.Current.Settings.QuiverFolders);
        Assert.Empty(_learned);
    }

    [Fact]
    public void Quiver_new_folder_without_library_is_learned_too()
    {
        // Erster Start eines frisch entpackten Quiver: apps.json gibt es noch nicht, der Ordner zählt trotzdem.
        _fs.AddFixture(@"Quiver\sq.version", Root + @"\current\sq.version");

        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe")]);

        Assert.Equal([Root], _learned);
    }

    [Fact]
    public void Quiver_old_quiver_exe_is_learned()
    {
        const string old = @"G:\Alt\Quiver";
        _fs.AddFile(old + @"\Quiver.exe", "MZ").AddFile(old + @"\apps.json", "{ \"apps\": [] }").AddDirectory(old + @"\Apps");

        _learner.Observe([new ProcessInfo(7, "Quiver.exe", old + @"\Quiver.exe")]);

        Assert.Equal([old], _config.Current.Settings.QuiverFolders);
    }

    [Fact]
    public void Quiver_learning_never_throws_when_saving_fails()
    {
        AddQuiver(Root);
        _fs.FailWriting(ConfigPath, new IOException("Datenträger voll"));

        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe")]);

        Assert.Empty(_learned);
        Assert.True(_log.Contains(LogLevel.Warning, "nicht gelernt"));
    }

    [Fact]
    public void Quiver_learned_folder_is_scanned_without_other_hints()
    {
        AddQuiver(Root);
        _fs.AddFile(Root + @"\Apps\Black-BlackXBERecomp\BLACK.exe", "MZ");
        Assert.Empty(Scan().Games); // vorher: weder Protokoll noch Suchordner, G:\ ist kein festes Laufwerk

        _learner.Observe([Process(Root + @"\current\QuiverLauncher.exe")]);

        Assert.Equal("Quiver:Black-BlackXBERecomp", Assert.Single(Scan().Games).Key);
    }

    [Fact]
    public async Task Quiver_host_learns_the_folder_in_its_poll_cycle()
    {
        // Verdrahtung in AutomationHost: Jeder Abfragezyklus reicht die neuen Prozesse an das Lernen weiter. Ablage und
        // Protokolle liegen in einem Temp-Ordner, die Konfiguration enthält kein Spiel (es wird nie HDR geschaltet), die
        // Überwachung selbst läuft nicht.
        var dir = Path.Combine(Path.GetTempPath(), "hdrpilot-quiver-host-" + Guid.NewGuid().ToString("N"));
        AddQuiver(Root);
        var learned = new List<string>();
        try
        {
            await using (var host = new AutomationHost(AppPaths.InDirectory(dir), _fs, new FakeRegistry(), new FakeEnvironment(), _clock))
            {
                host.QuiverFolders.Learned += (_, folder) => learned.Add(folder);
                var quiver = Process(Root + @"\current\QuiverLauncher.exe");

                await host.OnPollAsync(new ProcessChanges([quiver], [], [quiver]), TestContext.Current.CancellationToken);

                Assert.Equal([Root], host.Config.Current.Settings.QuiverFolders);
                Assert.Equal([Root], AutomationHost.ScanOptionsFor(host.Config.Current).QuiverFolders);
            }

            Assert.Equal([Root], learned);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(@"G:\Q\current\QuiverLauncher.exe", @"G:\Q")]
    [InlineData(@"G:\Q\QuiverLauncher.exe", @"G:\Q")]
    [InlineData(@"G:\Q\Quiver.exe", @"G:\Q")]
    [InlineData(@"G:\Q\Spiel.exe", null)]
    public void Quiver_folder_of_an_exe(string exe, string? folder) => Assert.Equal(folder, QuiverFolderLearner.FolderOf(exe));

    private ScanResult Scan() =>
        new QuiverScanner(new ReadOnlyFileSystem(_fs), new FakeRegistry(), new FakeEnvironment())
            .Scan(AutomationHost.ScanOptionsFor(_config.Current), TestContext.Current.CancellationToken);

    private void AddQuiver(string root)
    {
        _fs.AddFixture(@"Quiver\sq.version", root + @"\current\sq.version");
        _fs.AddFile(root + @"\current\QuiverLauncher.exe", "MZ");
        _fs.AddFile(root + @"\QuiverLauncher.exe", "MZ");
        _fs.AddFixture(@"Quiver\apps.json", root + @"\apps.json");
        _fs.AddFixture(@"Quiver\settings.json", root + @"\settings.json");
    }

    private static ProcessInfo Process(string path, int pid = 42) => new(pid, Path.GetFileName(path), path);
}
