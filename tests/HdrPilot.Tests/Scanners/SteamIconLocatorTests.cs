using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.Steam;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Offizielles Steam-Symbol als Symbol-Hinweis (E86, Entscheidung des Nutzers): steam\games\&lt;clienticon&gt;.ico aus
/// appinfo.vdf, sonst DisplayIcon von „Steam App &lt;ID&gt;“ in der 64-Bit-Sicht, sonst IconFile einer .url-Verknüpfung,
/// sonst das 32-px-JPEG im librarycache. Nur lesend (Attrappen, wie am Zielrechner gemessen).
/// </summary>
public sealed class SteamIconLocatorTests
{
    private const string SteamDir = @"C:\Program Files (x86)\Steam";
    private const string GamesIcons = SteamDir + @"\steam\games";
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App ";
    private const string ClientIcon = "0f3c5a7e9b1d2f4a6c8e0b2d4f6a8c0e2b4d6f8a";
    private const string OldIcon = "7b2e4d6f8a0c1e3b5d7f9a1c3e5b7d9f0a2c4e6b";
    private const string LibraryIcon = "4d6f8a0c2e4b6d8f0a1c3e5b7d9f1a3c5e7b9d0f";
    private const uint AppId = 9990150;

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeEnvironment _environment = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SteamAppInfoBuilder.V29)]
    [InlineData(SteamAppInfoBuilder.V28)]
    [InlineData(SteamAppInfoBuilder.V27)]
    public void Steam_icon_names_are_read_from_appinfo_common(uint magic)
    {
        var data = new SteamAppInfoBuilder(magic)
            .Add(AppId, "Game", "Funkenflug: Tag Eins", ClientIcon, LibraryIcon)
            .Add(2, "Game", "Ohne Symbole")
            .Build();

        var info = SteamAppInfoReader.Read(data, new HashSet<uint> { AppId, 2 }, Ct)!;

        Assert.Equal(new SteamAppInfo("Game", "Funkenflug: Tag Eins", ClientIcon, LibraryIcon), info[AppId]);
        Assert.Null(info[2].ClientIcon);
        Assert.Null(info[2].Icon);
    }

    [Fact]
    public void Steam_icon_official_clienticon_comes_first()
    {
        WithGame(clientIcon: ClientIcon);
        _fs.AddFile(GamesIcons + $@"\{ClientIcon}.ico", "ico");
        _fs.AddFile(GamesIcons + $@"\{OldIcon}.ico", "ico");
        _registry.Set(RegistryHiveKind.LocalMachine, Uninstall + AppId, "DisplayIcon", GamesIcons + $@"\{OldIcon}.ico", RegistryViewKind.Registry64);

        var game = Assert.Single(Scan().Games);

        Assert.Equal([GamesIcons + $@"\{ClientIcon}.ico"], game.IconCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_icon_falls_back_to_uninstall_DisplayIcon_in_the_64_bit_view()
    {
        // Geändertes clienticon: die neue .ico fehlt, der Uninstall-Eintrag zeigt noch auf die ältere (gemessen bei 21 Spielen).
        WithGame(clientIcon: ClientIcon);
        _fs.AddFile(GamesIcons + $@"\{OldIcon}.ico", "ico");
        _registry.Set(RegistryHiveKind.LocalMachine, Uninstall + AppId, "DisplayIcon", $"\"{GamesIcons}\\{OldIcon}.ico\",0", RegistryViewKind.Registry64);

        var game = Assert.Single(Scan().Games);

        Assert.Equal([GamesIcons + $@"\{OldIcon}.ico"], game.IconCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_icon_ignores_DisplayIcon_in_the_32_bit_view()
    {
        // Steam ist x64 und schreibt nicht nach WOW6432Node (gemessen: 798 Einträge in der 64-Bit-Sicht, 0 in der 32-Bit-Sicht).
        WithGame(clientIcon: null);
        _fs.AddFile(GamesIcons + $@"\{OldIcon}.ico", "ico");
        _registry.Set(RegistryHiveKind.LocalMachine, Uninstall + AppId, "DisplayIcon", GamesIcons + $@"\{OldIcon}.ico", RegistryViewKind.Registry32);

        var game = Assert.Single(Scan().Games);

        Assert.Empty(game.IconCandidates);
    }

    [Fact]
    public void Steam_icon_falls_back_to_url_shortcut_IconFile()
    {
        WithGame(clientIcon: null);
        _fs.AddFile(GamesIcons + $@"\{OldIcon}.ico", "ico");
        var programs = _environment.GetFolderPath(Environment.SpecialFolder.Programs);
        _fs.AddFile(programs + @"\Steam\Anderes Spiel.url", Url(999, GamesIcons + @"\anderes.ico"));
        _fs.AddFile(programs + @"\Steam\Funkenflug Tag Eins.url", Url(AppId, GamesIcons + $@"\{OldIcon}.ico"));

        var game = Assert.Single(Scan().Games);

        Assert.Equal([GamesIcons + $@"\{OldIcon}.ico"], game.IconCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_icon_reads_shortcuts_on_the_desktop_one_level_deep()
    {
        WithGame(clientIcon: null);
        _fs.AddFile(GamesIcons + $@"\{OldIcon}.ico", "ico");
        _fs.AddFile(@"C:\Users\Test\Desktop\Games\Funkenflug.url", Url(AppId, GamesIcons + $@"\{OldIcon}.ico"));

        Assert.Equal([GamesIcons + $@"\{OldIcon}.ico"], Assert.Single(Scan().Games).IconCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[InternetShortcut]\r\nURL=steam://rungameid/2771310\r\nIconIndex=0\r\nIconFile=C:\\Steam\\steam\\games\\a.ico\r\n", 2771310u, @"C:\Steam\steam\games\a.ico")]
    [InlineData("[InternetShortcut]\nIconFile=\"C:\\Steam\\steam\\games\\a.ico\"\nURL=steam://rungameid/42/\n", 42u, @"C:\Steam\steam\games\a.ico")]
    [InlineData("[InternetShortcut]\nURL=steam://rungameid/12345678901234567890\nIconFile=C:\\a.ico\n", null, null)]
    [InlineData("[InternetShortcut]\nURL=https://example.org/\nIconFile=C:\\a.ico\n", null, null)]
    [InlineData("[InternetShortcut]\nURL=steam://rungameid/42\n", null, null)]
    [InlineData("", null, null)]
    public void Steam_icon_url_shortcuts_are_parsed(string text, uint? appId, string? icon)
    {
        var parsed = SteamIconLocator.ParseShortcut(text);

        Assert.Equal(appId, parsed?.AppId);
        Assert.Equal(icon, parsed?.IconFile);
    }

    [Fact]
    public void Steam_icon_falls_back_to_librarycache_jpg_named_by_appinfo()
    {
        WithGame(clientIcon: ClientIcon, icon: LibraryIcon);
        var cache = SteamDir + @"\appcache\librarycache\" + AppId;
        _fs.AddFile(cache + @"\header.jpg", "jpg");
        _fs.AddFile(cache + $@"\{LibraryIcon}.jpg", "jpg");
        _fs.AddFile(cache + $@"\{OldIcon}.jpg", "jpg");

        Assert.Equal([cache + $@"\{LibraryIcon}.jpg"], Assert.Single(Scan().Games).IconCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_icon_without_appinfo_takes_the_only_hex_jpg_in_librarycache()
    {
        WithGame(clientIcon: null, icon: null);
        var cache = SteamDir + @"\appcache\librarycache\" + AppId;
        _fs.AddFile(cache + @"\library_600x900.jpg", "jpg");
        _fs.AddFile(cache + $@"\{LibraryIcon}.jpg", "jpg");

        Assert.Equal([cache + $@"\{LibraryIcon}.jpg"], Assert.Single(Scan().Games).IconCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_icon_names_that_are_not_hashes_are_never_used_as_paths()
    {
        WithGame(clientIcon: @"..\..\..\Windows\System32\shell32", icon: @"..\x");
        _fs.AddFile(@"C:\Windows\System32\shell32.ico", "ico");

        Assert.Empty(Assert.Single(Scan().Games).IconCandidates);
        Assert.False(SteamIconLocator.IsHash(@"..\..\x"));
        Assert.True(SteamIconLocator.IsHash(ClientIcon.ToUpperInvariant()));
    }

    [Fact]
    public void Steam_icon_without_any_source_leaves_the_hints_empty()
    {
        WithGame(clientIcon: ClientIcon);

        Assert.Empty(Assert.Single(Scan().Games).IconCandidates);
    }

    // ---------- Hilfen ----------

    private ScanResult Scan() => new SteamScanner(new ReadOnlyFileSystem(_fs), new ReadOnlyRegistry(_registry), _environment).Scan(ScanOptions.Default, Ct);

    private void WithGame(string? clientIcon, string? icon = null)
    {
        _registry.Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/program files (x86)/steam");
        _fs.AddFile(SteamDir + @"\steam.exe", "MZ");
        _fs.AddFile(SteamDir + @"\appcache\appinfo.vdf", new SteamAppInfoBuilder().Add(AppId, "Game", "Funkenflug: Tag Eins", clientIcon, icon).Build());
        _fs.AddFile(
            $@"{SteamDir}\steamapps\appmanifest_{AppId}.acf",
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{AppId}\"\n\t\"name\"\t\t\"Funkenflug: Tag Eins\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Funkenflug Tag Eins\"\n}}\n");
        _fs.AddDirectory(SteamDir + @"\steamapps\common\Funkenflug Tag Eins");
    }

    private static string Url(uint appId, string iconFile) =>
        new StringBuilder().Append("[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,0\r\n[InternetShortcut]\r\nIDList=\r\nIconIndex=0\r\n")
            .Append("URL=steam://rungameid/").Append(appId).Append("\r\nIconFile=").Append(iconFile).Append("\r\n").ToString();
}
