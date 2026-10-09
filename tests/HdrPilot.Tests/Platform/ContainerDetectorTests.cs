using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Platform;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Platform;

/// <summary>
/// Container-Erkennung (E84) mit nachgebildetem Profil: %LOCALAPPDATA%, %APPDATA% und Packages liegen in einem
/// Temp-Ordner, die Umleitung bildet ein Haken nach, der die Probe dorthin kopiert, wo Windows sie im Container ablegt
/// (<c>Packages\&lt;App&gt;\LocalCache\Local|Roaming\&lt;relativer Pfad&gt;</c>). Dazu zwei Läufe gegen das echte Profil, die
/// nichts zurücklassen.
/// </summary>
public sealed class ContainerDetectorTests : IDisposable
{
    private const string Package = "Claude_pzs8sxrjxfjjc";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.Container-" + Guid.NewGuid().ToString("N"));
    private readonly FakeEnvironment _environment = new();
    private readonly List<string> _probes = [];

    public ContainerDetectorTests()
    {
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = Local;
        _environment.Folders[Environment.SpecialFolder.ApplicationData] = Roaming;
        Directory.CreateDirectory(Roaming);
        foreach (var app in new[] { "Microsoft.WindowsTerminal_8wekyb3d8bbwe", Package, "Spotify_zpdnekdrzrea0" })
        {
            Directory.CreateDirectory(Path.Combine(Local, "Packages", app, "LocalCache", "Local"));
            Directory.CreateDirectory(Path.Combine(Local, "Packages", app, "LocalCache", "Roaming"));
        }
    }

    private string Local => Path.Combine(_root, "Local");

    private string Roaming => Path.Combine(_root, "Roaming");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Redirected_local_folder_names_the_app_and_deletes_the_probe()
    {
        var folder = Path.Combine(Local, "HDRpilot");
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        File.WriteAllText(Path.Combine(folder, "session-state.json"), "{}");

        var result = Detector(Redirect(Package, "Local")).Detect(folder);

        Assert.True(result.Redirected);
        Assert.Equal(Package, result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.Equal(folder, result.Folder);
        var probe = Assert.Single(_probes);
        Assert.StartsWith(Path.Combine(folder, ContainerDetector.ProbePrefix), probe, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(probe));
        Assert.Empty(Directory.EnumerateFiles(folder, ContainerDetector.ProbePrefix + "*"));
        Assert.True(File.Exists(Path.Combine(folder, "session-state.json"))); // Vorhandenes bleibt
        Assert.True(Directory.Exists(Path.Combine(folder, "logs")));
    }

    [Fact]
    public void Redirected_roaming_folder_is_found_under_LocalCache_Roaming()
    {
        var folder = Path.Combine(Roaming, "HDRpilot");
        Directory.CreateDirectory(folder);

        var result = Detector(Redirect(Package, "Roaming")).Detect(folder);

        Assert.Equal(Package, result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.False(File.Exists(Assert.Single(_probes)));
    }

    [Theory]
    [InlineData("Roaming", "HDRpilot")] // richtige App und Pfad, aber unter Roaming statt Local
    [InlineData("Local", "Anderer")] // anderer relativer Pfad
    [InlineData(null, null)] // keine Kopie: kein Container
    public void Probe_counts_only_at_the_same_relative_path_under_the_right_kind(string? kind, string? relativeFolder)
    {
        var folder = Path.Combine(Local, "HDRpilot");
        Directory.CreateDirectory(folder);
        Action<string> hook = kind is null
            ? _probes.Add
            : probe =>
            {
                _probes.Add(probe);
                var copy = Path.Combine(Local, "Packages", Package, "LocalCache", kind, relativeFolder!, Path.GetFileName(probe));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(probe, copy);
            };

        var result = Detector(hook).Detect(folder);

        Assert.False(result.Redirected);
        Assert.Null(result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.False(File.Exists(Assert.Single(_probes)));
    }

    [Fact]
    public void Missing_folder_is_created_for_the_probe_and_removed_again()
    {
        var folder = Path.Combine(Local, "HDRpilot", "Neu");
        var existedDuringProbe = false;

        var result = Detector(probe =>
        {
            existedDuringProbe = Directory.Exists(folder);
            Redirect(Package, "Local")(probe);
        }).Detect(folder);

        Assert.True(existedDuringProbe);
        Assert.Equal(Package, result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.False(Directory.Exists(Path.Combine(Local, "HDRpilot"))); // auch der übergeordnete, selbst angelegte Ordner
        Assert.True(Directory.Exists(Local));
    }

    [Fact]
    public void Created_folder_that_got_other_content_is_kept_and_reported()
    {
        // Legt währenddessen jemand anderes etwas in den neuen Ordner, bleibt er samt Inhalt; das steht im Ergebnis.
        var folder = Path.Combine(Local, "HDRpilot");

        var result = Detector(probe =>
        {
            _probes.Add(probe);
            File.WriteAllText(Path.Combine(folder, "config.json"), "{}");
        }).Detect(folder);

        Assert.False(result.Redirected);
        Assert.Contains("Ordner bleibt, weil nicht leer: " + folder + " (config.json)", result.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Assert.Single(_probes)));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(folder, "config.json")));
    }

    [Fact]
    public void Folder_outside_AppData_is_not_probed_and_not_created()
    {
        var folder = Path.Combine(_root, "Projekte", "Ablage");

        var result = Detector(_probes.Add).Detect(folder);

        Assert.False(result.Redirected);
        Assert.Null(result.Problem);
        Assert.Empty(_probes);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Unusable_folder_reports_a_problem_instead_of_throwing()
    {
        // Eine Datei, wo der Ordner hin soll: Ordner und Probe lassen sich nicht anlegen.
        var folder = Path.Combine(Local, "HDRpilot");
        File.WriteAllText(folder, "keine Ablage");

        var result = Detector(_probes.Add).Detect(folder);

        Assert.False(result.Redirected);
        Assert.Contains("Probe schreiben oder suchen", result.Problem, StringComparison.Ordinal);
        Assert.Empty(_probes);
        Assert.Equal("keine Ablage", File.ReadAllText(folder));
    }

    [Fact]
    public void Probe_is_deleted_even_when_the_search_fails()
    {
        var folder = Path.Combine(Local, "HDRpilot");
        Directory.CreateDirectory(folder);

        var result = Detector(probe =>
        {
            _probes.Add(probe);
            throw new UnauthorizedAccessException("Packages nicht lesbar");
        }).Detect(folder);

        Assert.False(result.Redirected);
        Assert.Contains("Packages nicht lesbar", result.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Assert.Single(_probes)));
        Assert.True(Directory.Exists(folder)); // war schon da, bleibt
    }

    [Fact]
    public void Without_a_Packages_folder_nothing_is_redirected()
    {
        Directory.Delete(Path.Combine(Local, "Packages"), recursive: true);
        var folder = Path.Combine(Local, "HDRpilot");

        var result = Detector(_probes.Add).Detect(folder);

        Assert.False(result.Redirected);
        Assert.Null(result.Problem);
        Assert.False(File.Exists(Assert.Single(_probes)));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Real_temp_folder_is_not_redirected_and_leaves_nothing_behind()
    {
        // %TEMP% liegt unter %LOCALAPPDATA%, wird aber nie umgeleitet (auch nicht im Container der Claude-App).
        var folder = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.Container-" + Guid.NewGuid().ToString("N"));

        var result = new ContainerDetector(SystemEnvironment.Instance).Detect(folder);

        Assert.False(result.Redirected, "umgeleitet nach " + result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Real_local_app_data_folder_records_the_container_and_leaves_nothing_behind()
    {
        // Im Container einer anderen App (etwa diese Testläufe aus der Claude-App) ist das Ergebnis „umgeleitet“, sonst
        // nicht; beides ist richtig und wird nur festgehalten. Prüfbar ist: kein Fehler, nichts bleibt liegen.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var name = "HDRpilot.Tests.Container-" + Guid.NewGuid().ToString("N");
        var folder = Path.Combine(local, name);

        var result = new ContainerDetector(SystemEnvironment.Instance).Detect(folder);

        TestContext.Current.TestOutputHelper?.WriteLine(result.Redirected
            ? "Dieser Testlauf läuft im Container der App " + result.PackageFolder
            : "Dieser Testlauf läuft in keinem Container");
        Assert.Null(result.Problem);
        Assert.False(Directory.Exists(folder));
        if (result.PackageFolder is { } package)
        {
            Assert.False(Directory.Exists(Path.Combine(local, "Packages", package, "LocalCache", "Local", name)));
        }
    }

    private ContainerDetector Detector(Action<string> afterProbeWritten) => new(_environment, afterProbeWritten);

    /// <summary>Bildet die Umleitung nach: Kopie der Probe unter Packages\&lt;App&gt;\LocalCache\&lt;Local|Roaming&gt;\&lt;relativ&gt;.</summary>
    private Action<string> Redirect(string app, string kind) => probe =>
    {
        _probes.Add(probe);
        var relative = Path.GetRelativePath(kind == "Local" ? Local : Roaming, probe);
        var copy = Path.Combine(Local, "Packages", app, "LocalCache", kind, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(probe, copy);
    };
}
