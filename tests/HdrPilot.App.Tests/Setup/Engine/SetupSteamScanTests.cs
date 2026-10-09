using System.Text.Json;
using HdrPilot.App.Setup;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>Steam-Startoptionen mit HDRpilot finden (nur lesen). Steam-Ordner und config.json als Dateien im Temp-Ordner.</summary>
public sealed class SetupSteamScanTests : IDisposable
{
    private const string AccountA = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"apps"
        				{
        					"1091500"
        					{
        						"LaunchOptions"		"\"C:\\Users\\X\\AppData\\Local\\Programs\\HdrPilot\\HdrPilot.exe\" --wrap -- %command%"
        						"LastPlayed"		"1759000000"
        					}
        					"570"
        					{
        						"LaunchOptions"		"-novid -high"
        					}
        					"999"
        					{
        						"LaunchOptions"		"\"C:\\Tools\\HDRpilot.exe\" -- %command%"
        					}
        					"1245620"
        					{
        						"cloud"
        						{
        							"last_sync_state"		"synchronized"
        						}
        					}
        				}
        			}
        		}
        	}
        }
        """;

    private const string AccountB = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"valve"
        		{
        			"steam"
        			{
        				"Apps"
        				{
        					"1245620"
        					{
        						"LaunchOptions"		"\"D:\\Tools\\HDRPILOT.EXE\" --wrap -- %command%"
        					}
        					"1091500"
        					{
        						"LaunchOptions"		"\"D:\\Tools\\HDRpilot.exe\" --wrap -- %command%"
        					}
        				}
        			}
        		}
        	}
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.SetupSteam." + Guid.NewGuid().ToString("N"));
    private readonly string _steam;
    private readonly string _config;

    public SetupSteamScanTests()
    {
        _steam = Path.Combine(_root, "Steam");
        _config = Path.Combine(_root, "Config", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_config)!);
        var config = new AppConfig
        {
            Games =
            [
                new GameConfig { Source = GameSource.Steam, SourceId = "1091500", Name = "Cyberpunk 2077" },
                new GameConfig { Source = GameSource.Epic, SourceId = "1245620", Name = "Falsche Quelle" },
            ],
        };
        File.WriteAllText(_config, JsonSerializer.Serialize(config, ConfigJson.Options));
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

    [Fact]
    public void Finds_games_of_all_accounts_with_names_from_the_config_and_without_duplicates()
    {
        WriteAccount("111", AccountA);
        WriteAccount("222", AccountB);

        var games = Scanner().FindGamesWithLaunchOption();

        Assert.Equal(["Cyberpunk 2077", "Steam-App 1245620"], games);
    }

    [Fact]
    public void Only_launch_options_that_wrap_through_hdrpilot_count()
    {
        WriteAccount("111", AccountA);

        Assert.Equal(["1091500"], Scanner().FindAppIds());
    }

    [Fact]
    public void Broken_account_file_is_skipped_and_the_others_still_count()
    {
        WriteAccount("111", "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n");
        WriteAccount("222", AccountB);
        var log = new SetupLog(Path.Combine(_root, "setup.log"));

        var ids = new SteamLaunchOptionScanner(() => _steam, _config, log).FindAppIds();

        Assert.Equal(["1245620", "1091500"], ids);
        Assert.Contains("nicht lesbar", File.ReadAllText(log.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_folder_comes_from_the_registry_with_forward_slashes()
    {
        WriteAccount("111", AccountA);
        var registry = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, SteamLaunchOptionScanner.SteamKeyPath, "SteamPath", _steam.Replace('\\', '/').ToLowerInvariant());

        Assert.Equal(["Cyberpunk 2077"], new SteamLaunchOptionScanner(registry, _config).FindGamesWithLaunchOption());
    }

    [Fact]
    public void Missing_steam_missing_userdata_or_missing_config_are_no_error()
    {
        Assert.Empty(new SteamLaunchOptionScanner(new FakeRegistry(), _config).FindGamesWithLaunchOption());
        Assert.Empty(new SteamLaunchOptionScanner(() => null, _config).FindGamesWithLaunchOption());
        Assert.Empty(Scanner().FindGamesWithLaunchOption()); // Steam-Ordner ohne userdata

        WriteAccount("111", AccountA);
        var games = new SteamLaunchOptionScanner(() => _steam, Path.Combine(_root, "fehlt.json")).FindGamesWithLaunchOption();
        Assert.Equal(["Steam-App 1091500"], games);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, HdrPilot.App.Resources.SetupStrings.SetupSteamAppFallback, "1091500"), Assert.Single(games)); // Text aus den Ressourcen
    }

    [Fact]
    public void Scanning_never_writes_steam_or_config_files()
    {
        var file = WriteAccount("111", AccountA);
        var steamBytes = File.ReadAllBytes(file);
        var steamTime = File.GetLastWriteTimeUtc(file);
        var configBytes = File.ReadAllBytes(_config);
        var configTime = File.GetLastWriteTimeUtc(_config);

        Scanner().FindGamesWithLaunchOption();

        Assert.Equal(steamBytes, File.ReadAllBytes(file));
        Assert.Equal(steamTime, File.GetLastWriteTimeUtc(file));
        Assert.Equal(configBytes, File.ReadAllBytes(_config));
        Assert.Equal(configTime, File.GetLastWriteTimeUtc(_config));
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    private SteamLaunchOptionScanner Scanner() => new(() => _steam, _config);

    private string WriteAccount(string account, string content)
    {
        var path = Path.Combine(_steam, "userdata", account, "config", "localconfig.vdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
