using HdrPilot.App.Services;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// HDRpilot-Verknüpfungen eines nicht installierten Spiels finden und löschen (E90, G11.1), mit echten .lnk in Temp-Ordnern
/// über die Windows-Shell: nur Ziel HDRpilot.exe mit genau <c>--run "&lt;Schlüssel&gt;"</c>, nur direkt auf dem Desktop und
/// im Startmenü-Ordner „HDRpilot“; ähnliche Verknüpfungen (anderer Schlüssel, Zusatz, anderes Ziel, Unterordner) bleiben.
/// </summary>
public sealed class UninstalledShortcutTests : IDisposable
{
    private const string Key = "Steam:1304930";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HdrPilot.Tests.Uninstalled." + Guid.NewGuid().ToString("N"));
    private readonly string _target;
    private readonly string _other;
    private readonly string _desktop;
    private readonly string _programs;

    public UninstalledShortcutTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _programs = Path.Combine(_root, "Programs");
        _target = Path.Combine(_root, "App", "HDRpilot.exe");
        _other = Path.Combine(_root, "App", "Anderes.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_target)!);
        Directory.CreateDirectory(Path.Combine(_desktop, "Spiele"));
        Directory.CreateDirectory(Path.Combine(_programs, "HDRpilot"));
        Directory.CreateDirectory(Path.Combine(_programs, "Steam"));
        File.WriteAllBytes(_target, [0x4D, 0x5A]);
        File.WriteAllBytes(_other, [0x4D, 0x5A]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp-Ordner: Aufräumen ist Kür.
        }
    }

    private ShortcutService Service(string? extra = null) => new(_desktop, _programs, () => _target, extraArguments: extra);

    private string Link(string folder, string name, string arguments, string? target = null)
    {
        var path = Path.Combine(folder, name + ".lnk");
        ShortcutService.Write(path, target ?? _target, arguments, Path.GetDirectoryName(_target)!, target ?? _target, 0, "Test");
        return path;
    }

    /// <summary>Passende und ähnliche Verknüpfungen; liefert die passenden.</summary>
    private string[] CreateLinks()
    {
        var startMenu = Path.Combine(_programs, "HDRpilot");
        string[] matching =
        [
            Link(_desktop, "The Outlast Trials (HDR)", "--run \"" + Key + "\""),
            Link(startMenu, "The Outlast Trials (HDR)", "--run \"" + Key + "\""),
            Link(_desktop, "Kleinbuchstaben", "--run \"steam:1304930\""), // Schlüssel sind wie in der Konfiguration ohne Groß-/Kleinschreibung
        ];
        Link(_desktop, "Längere ID", "--run \"Steam:13049301\"");
        Link(_desktop, "Kürzere ID", "--run \"Steam:130493\"");
        Link(_desktop, "Mit Zusatz", "--run \"" + Key + "\" --config-dir \"D:\\Test\"");
        Link(_desktop, "Anderes Ziel", "--run \"" + Key + "\"", _other);
        Link(_desktop, "Ohne Argumente", string.Empty);
        Link(Path.Combine(_desktop, "Spiele"), "Im Unterordner", "--run \"" + Key + "\"");
        Link(Path.Combine(_programs, "Steam"), "Anderer Startmenü-Ordner", "--run \"" + Key + "\"");
        File.WriteAllText(Path.Combine(_desktop, "Kaputt.lnk"), "keine Verknüpfung");
        File.WriteAllText(Path.Combine(_desktop, "Notiz.txt"), "--run \"" + Key + "\"");
        return matching;
    }

    private List<string> AllFiles() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();

    [Fact]
    public void Uninstalled_shortcut_search_finds_only_exact_hdrpilot_shortcuts() => Sta.Run(() =>
    {
        var matching = CreateLinks();

        var found = Service().FindFor(Key);

        Assert.Equal(matching.Order(StringComparer.OrdinalIgnoreCase), found.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(found, f => Assert.Equal(_target, f.IconPath, ignoreCase: true));
    });

    [Fact]
    public void Uninstalled_shortcut_delete_removes_only_the_exact_shortcuts() => Sta.Run(() =>
    {
        var matching = CreateLinks();
        var before = AllFiles();
        var service = Service();

        var deleted = service.Delete(Key, service.FindFor(Key));

        Assert.Equal(matching.Order(StringComparer.OrdinalIgnoreCase), deleted.Select(d => d.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(before.Except(matching, StringComparer.OrdinalIgnoreCase), AllFiles());
    });

    [Fact]
    public void Uninstalled_shortcut_replaced_after_the_search_is_not_deleted() => Sta.Run(() =>
    {
        CreateLinks();
        var service = Service();
        var found = service.FindFor(Key);
        var desktop = found.Single(f => f.Path.EndsWith("The Outlast Trials (HDR).lnk", StringComparison.Ordinal) && f.Path.StartsWith(_desktop, StringComparison.OrdinalIgnoreCase));

        // Zwischen Rückfrage und Löschen: dieselbe Datei zeigt jetzt auf ein anderes Spiel.
        ShortcutService.Write(desktop.Path, _target, "--run \"Steam:553850\"", Path.GetDirectoryName(_target)!, _target, 0, "Test");
        var deleted = service.Delete(Key, found);

        Assert.Equal(2, deleted.Count);
        Assert.True(File.Exists(desktop.Path));
        Assert.Equal("--run \"Steam:553850\"", ShortcutService.Read(desktop.Path).Arguments);
    });

    [Fact]
    public void Uninstalled_shortcuts_of_a_test_instance_need_exactly_its_config_dir() => Sta.Run(() =>
    {
        CreateLinks();

        var found = Service("--config-dir \"D:\\Test\"").FindFor(Key);

        Assert.Equal(["Mit Zusatz.lnk"], found.Select(f => Path.GetFileName(f.Path)));
    });
}
