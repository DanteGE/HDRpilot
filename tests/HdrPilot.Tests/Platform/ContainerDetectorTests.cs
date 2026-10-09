using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Platform;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Platform;

/// <summary>
/// Container-Erkennung (E84) mit nachgebildetem Profil: %LOCALAPPDATA% samt Packages liegt in einem Temp-Ordner, die
/// Umleitung bildet ein Haken nach, der wie Windows umleitet (gemessen im Container der Claude-App): Neues landet nur
/// dann in <c>Packages\&lt;App&gt;\LocalCache\Local\&lt;relativer Pfad&gt;</c>, wenn es seinen Ordner dort gibt. Die Wurzel
/// gibt es dort immer, einen Ordner, der nur außerhalb besteht (etwa ein schon vorhandenes HDRpilot), nicht. Dazu ein Lauf
/// gegen das echte Profil, der nichts zurücklässt.
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
        foreach (var app in new[] { "Microsoft.WindowsTerminal_8wekyb3d8bbwe", Package, "Spotify_zpdnekdrzrea0" })
        {
            Directory.CreateDirectory(Path.Combine(Local, "Packages", app, "LocalCache", "Local"));
            Directory.CreateDirectory(Path.Combine(Local, "Packages", app, "LocalCache", "Roaming"));
        }
    }

    private string Local => Path.Combine(_root, "Local");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Probe_directly_in_local_app_data_names_the_app_and_is_deleted()
    {
        var result = Detector(Windows(Package)).Detect();

        Assert.True(result.Redirected);
        Assert.Equal(Package, result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.Equal(Local, result.Folder);
        var probe = Assert.Single(_probes);
        Assert.Equal(Local, Path.GetDirectoryName(probe));
        Assert.StartsWith(ContainerDetector.ProbePrefix, Path.GetFileName(probe), StringComparison.Ordinal);
        Assert.EndsWith(".tmp", probe, StringComparison.Ordinal);
        Assert.False(File.Exists(probe));
        Assert.Equal(["Packages"], Directory.EnumerateFileSystemEntries(Local).Select(Path.GetFileName)); // nichts angelegt, nichts liegen gelassen
    }

    [Fact]
    public void Container_is_found_even_when_the_data_folder_exists_only_outside_it()
    {
        // Befund der Gegenprüfung: Wer HDRpilot normal installiert hat, hat ein echtes %LOCALAPPDATA%\HDRpilot. Neues darin
        // schreibt Windows auch aus dem Container echt, die Registry (Eintrag, Autostart) aber nur virtuell. Eine Probe im
        // Datenordner hätte „kein Container“ gemeldet; die Erkennung darf deshalb nicht von ihm abhängen.
        var data = Path.Combine(Local, "HDRpilot");
        Directory.CreateDirectory(Path.Combine(data, "logs"));
        File.WriteAllText(Path.Combine(data, "session-state.json"), "{}");
        var inData = Path.Combine(data, "vorbedingung.tmp");
        File.WriteAllText(inData, string.Empty);
        Windows(Package)(inData);
        Assert.False(File.Exists(Path.Combine(Local, "Packages", Package, "LocalCache", "Local", "HDRpilot", "vorbedingung.tmp"))); // Modell wie gemessen
        File.Delete(inData);
        _probes.Clear();

        var result = Detector(Windows(Package)).Detect();

        Assert.Equal(Package, result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.Equal(Local, Path.GetDirectoryName(Assert.Single(_probes)));
        Assert.Equal(["logs", "session-state.json"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName).Order());
        Assert.False(Directory.Exists(Path.Combine(Local, "Packages", Package, "LocalCache", "Local", "HDRpilot")));
    }

    [Theory]
    [InlineData("Roaming", "")] // richtige App und Name, aber unter Roaming statt Local
    [InlineData("Local", "HDRpilot")] // anderer relativer Pfad
    [InlineData(null, null)] // keine Kopie: kein Container
    public void Copy_counts_only_under_LocalCache_Local_with_the_same_name(string? kind, string? relativeFolder)
    {
        Action<string> hook = kind is null
            ? _probes.Add
            : probe =>
            {
                _probes.Add(probe);
                var copy = Path.Combine(Local, "Packages", Package, "LocalCache", kind, relativeFolder!, Path.GetFileName(probe));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(probe, copy);
            };

        var result = Detector(hook).Detect();

        Assert.False(result.Redirected);
        Assert.Null(result.PackageFolder);
        Assert.Null(result.Problem);
        Assert.False(File.Exists(Assert.Single(_probes)));
    }

    [Fact]
    public void Missing_local_app_data_reports_a_problem_instead_of_throwing()
    {
        var missing = Path.Combine(_root, "Fehlt", "Local");
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = missing;

        var result = Detector(_probes.Add).Detect();

        Assert.False(result.Redirected);
        Assert.Equal(missing, result.Folder);
        Assert.Contains("Probe schreiben oder suchen: DirectoryNotFoundException", result.Problem, StringComparison.Ordinal);
        Assert.Empty(_probes);
        Assert.False(Directory.Exists(Path.Combine(_root, "Fehlt"))); // nichts angelegt
    }

    [Fact]
    public void Unknown_local_app_data_reports_a_problem_instead_of_throwing()
    {
        _environment.Folders[Environment.SpecialFolder.LocalApplicationData] = string.Empty;

        var result = Detector(_probes.Add).Detect();

        Assert.False(result.Redirected);
        Assert.Equal("%LOCALAPPDATA%", result.Folder);
        Assert.StartsWith("Ordner bestimmen: ArgumentException: ", result.Problem, StringComparison.Ordinal);
        Assert.Empty(_probes);
    }

    [Fact]
    public void Probe_is_deleted_even_when_the_search_fails()
    {
        var result = Detector(probe =>
        {
            _probes.Add(probe);
            throw new UnauthorizedAccessException("Packages nicht lesbar");
        }).Detect();

        Assert.False(result.Redirected);
        Assert.Contains("Packages nicht lesbar", result.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Assert.Single(_probes)));
    }

    [Fact]
    public void Without_a_Packages_folder_nothing_is_redirected()
    {
        Directory.Delete(Path.Combine(Local, "Packages"), recursive: true);

        var result = Detector(_probes.Add).Detect();

        Assert.False(result.Redirected);
        Assert.Null(result.Problem);
        Assert.False(File.Exists(Assert.Single(_probes)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Local));
    }

    [Fact]
    public void Real_local_app_data_records_the_container_and_leaves_nothing_behind()
    {
        // Im Container einer anderen App (etwa diese Testläufe aus der Claude-App) ist das Ergebnis „umgeleitet“, sonst
        // nicht; beides ist richtig und wird nur festgehalten. Prüfbar ist: kein Fehler, nichts bleibt liegen.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var probes = new List<string>();

        var result = new ContainerDetector(SystemEnvironment.Instance, probes.Add).Detect();

        TestContext.Current.TestOutputHelper?.WriteLine(result.Redirected
            ? "Dieser Testlauf läuft im Container der App " + result.PackageFolder
            : "Dieser Testlauf läuft in keinem Container");
        Assert.Null(result.Problem);
        Assert.Equal(Path.TrimEndingDirectorySeparator(local), result.Folder, ignoreCase: true);
        var probe = Assert.Single(probes);
        Assert.Equal(result.Folder, Path.GetDirectoryName(probe));
        Assert.False(File.Exists(probe));
        if (result.PackageFolder is { } package)
        {
            Assert.False(File.Exists(Path.Combine(local, "Packages", package, "LocalCache", "Local", Path.GetFileName(probe))));
        }
    }

    private ContainerDetector Detector(Action<string> afterProbeWritten) => new(_environment, afterProbeWritten);

    /// <summary>
    /// Bildet die Umleitung im Container von <paramref name="app"/> nach, wie gemessen: Kopie unter
    /// <c>Packages\&lt;App&gt;\LocalCache\Local\&lt;relativer Pfad&gt;</c>, aber nur, wenn es den Ordner dort schon gibt.
    /// </summary>
    private Action<string> Windows(string app) => file =>
    {
        _probes.Add(file);
        var copy = Path.Combine(Local, "Packages", app, "LocalCache", "Local", Path.GetRelativePath(Local, file));
        if (Directory.Exists(Path.GetDirectoryName(copy)))
        {
            File.Copy(file, copy);
        }
    };
}
