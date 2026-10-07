using HdrPilot.App.Services;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Tests.Library;

/// <summary>Verknüpfungen nur in Temp-Ordnern; gelesen wird über dieselbe Shell-Schnittstelle (IShellLinkW).</summary>
public sealed class ShortcutServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HdrPilot.Tests.Shortcuts." + Guid.NewGuid().ToString("N"));
    private readonly string _target;
    private readonly string _gameExe;

    public ShortcutServiceTests()
    {
        Directory.CreateDirectory(_root);
        // Echte Dateien, damit die Shell nichts „repariert“: Ziel ist eine Kopie-Attrappe von HdrPilot.exe.
        _target = Path.Combine(_root, "App", "HdrPilot.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_target)!);
        File.WriteAllBytes(_target, [0x4D, 0x5A]);
        _gameExe = Path.Combine(_root, "Games", "Cyberpunk 2077", "bin", "x64", "Cyberpunk2077.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_gameExe)!);
        File.WriteAllBytes(_gameExe, [0x4D, 0x5A]);
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

    private static GameConfig Game(string name = "Cyberpunk 2077") => new() { Source = GameSource.Gog, SourceId = "1423049311", Name = name };

    private ShortcutService Service(string? extra = null) => new(
        desktopDirectory: Path.Combine(_root, "Desktop"),
        programsDirectory: Path.Combine(_root, "Programs"),
        targetPath: () => _target,
        iconPath: _ => _gameExe,
        extraArguments: extra);

    [Fact]
    public void Desktop_shortcut_runs_hdrpilot_with_the_game_key_and_shows_the_game_icon() => Sta.Run(() =>
    {
        var path = Service().CreateDesktopShortcut(Game());

        Assert.Equal(Path.Combine(_root, "Desktop", "Cyberpunk 2077 (HDR).lnk"), path);
        Assert.True(File.Exists(path));
        var link = ShortcutService.Read(path);
        Assert.Equal(_target, link.TargetPath, ignoreCase: true);
        Assert.Equal("--run \"Gog:1423049311\"", link.Arguments);
        Assert.Equal(Path.GetDirectoryName(_target), link.WorkingDirectory, ignoreCase: true);
        Assert.Equal(_gameExe, link.IconPath, ignoreCase: true);
        Assert.Equal(0, link.IconIndex);
        Assert.Contains("Cyberpunk 2077", link.Description, StringComparison.Ordinal);
    });

    [Fact]
    public void Start_menu_shortcut_goes_into_the_hdrpilot_folder_and_is_replaced_on_repeat() => Sta.Run(() =>
    {
        var service = Service();

        var first = service.CreateStartMenuShortcut(Game());
        var second = service.CreateStartMenuShortcut(Game());

        Assert.Equal(Path.Combine(_root, "Programs", "HdrPilot", "Cyberpunk 2077 (HDR).lnk"), first);
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "Programs", "HdrPilot")));
        Assert.Equal("--run \"Gog:1423049311\"", ShortcutService.Read(first).Arguments);
    });

    [Fact]
    public void Invalid_file_name_characters_are_replaced_and_extra_arguments_appended() => Sta.Run(() =>
    {
        var path = Service("--config-dir \"D:\\Test Ablage\"").CreateDesktopShortcut(Game("Halo: Reach / MCC?"));

        Assert.Equal("Halo - Reach - MCC (HDR).lnk", Path.GetFileName(path));
        Assert.Equal("--run \"Gog:1423049311\" --config-dir \"D:\\Test Ablage\"", ShortcutService.Read(path).Arguments);
    });

    [Fact]
    public void Without_game_exe_the_hdrpilot_icon_is_used()
    {
        var service = new ShortcutService(Path.Combine(_root, "Desktop"), Path.Combine(_root, "Programs"), () => _target, _ => null);
        Sta.Run(() =>
        {
            var link = ShortcutService.Read(service.CreateDesktopShortcut(Game()));
            Assert.Equal(_target, link.IconPath, ignoreCase: true);
        });
    }

    [Fact]
    public void Arguments_quote_the_key()
    {
        Assert.Equal("--run \"Xbox:Contoso.Wolkenhof_50p2hsbcnaasp\"", ShortcutService.ArgumentsFor(new GameConfig { Source = GameSource.Xbox, SourceId = "Contoso.Wolkenhof_50p2hsbcnaasp" }));
        Assert.Equal("Spiel... (HDR)", ShortcutService.FileNameFor(new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = "  Spiel...  " }));
        Assert.Equal("Tom Clancy's The Division® 2 (HDR)", ShortcutService.FileNameFor(new GameConfig { Source = GameSource.Ubisoft, SourceId = "4932", Name = "Tom Clancy's The Division® 2" }));
    }

    [Theory]
    [InlineData("Dying Light: The Beast", "Dying Light - The Beast (HDR)")]
    [InlineData("STAR WARS Jedi: Survivor™", "STAR WARS Jedi - Survivor™ (HDR)")]
    [InlineData("Half-Life 2: Episode One", "Half-Life 2 - Episode One (HDR)")]
    [InlineData("AC/DC Rockband", "AC-DC Rockband (HDR)")]
    [InlineData("Who? *Really* \"Quoted\" <Game>", "Who Really 'Quoted' Game (HDR)")]
    [InlineData("Re:Zero", "Re - Zero (HDR)")]
    public void File_names_replace_invalid_characters_readably(string name, string expected) =>
        Assert.Equal(expected, ShortcutService.FileNameFor(new GameConfig { Source = GameSource.Steam, SourceId = "1", Name = name }));
}
