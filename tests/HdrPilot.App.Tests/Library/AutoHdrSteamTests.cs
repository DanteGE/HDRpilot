using System.Runtime.InteropServices;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// G9.7: Schaltet der Nutzer „Windows Auto HDR“ für ein Steam-Spiel ein und fehlt die HDRpilot-Startoption, bietet
/// HDRpilot sie an (dieselbe Funktion wie „Steam-Startoption kopieren“). Ablehnen ändert nichts, Auto HDR bleibt an. Steam
/// liest dabei nur eine Attrappe oder ein Testordner im Temp-Ordner, nie das echte Steam.
/// </summary>
public sealed class AutoHdrSteamTests : IDisposable
{
    private const string Cyberpunk = "Steam:1091500";
    private const string Hades = "Steam:1145350";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.AutoHdrSteam." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp-Ordner: Aufräumen ist Kür.
        }
    }

    private static TestLibraryContext SampleContext() => new(
        Game(GameSource.Steam, "1091500", "Cyberpunk 2077", isNew: false),
        Game(GameSource.Steam, "1145350", "Hades II", isNew: false),
        Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false),
        Game(GameSource.Gog, "1207658924", "SDR-Spiel", isNew: false),
        Game(GameSource.Xbox, "Palworld", "Palworld", isNew: false),
        Game(GameSource.Manual, "m1", "Eigenes Spiel", isNew: false));

    private static GameDetailsViewModel Select(LibraryViewModel vm, string key)
    {
        vm.SelectedGame = vm.Games.Single(g => g.Key == key);
        return vm.Details!;
    }

    private static GameConfig Saved(TestLibraryContext context, string key) => context.Config.FindGame(key)!;

    [Fact]
    public void AutoHdrSteam_switching_on_without_the_launch_option_offers_it_and_confirming_copies_it() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ExecutablePath = @"E:\Apps\Hdr Pilot\HDRpilot.exe";
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);
        var details = Select(vm, Cyberpunk);

        details.AutoHdr = true;

        Assert.Equal(["1091500"], context.SteamLaunchOptionChecks);
        var (title, message) = Assert.Single(dialogs.Confirms);
        Assert.Equal(LibraryStrings.AutoHdrSteam_OfferTitle, title);
        Assert.Equal(LibraryText.Format(LibraryStrings.AutoHdrSteam_OfferText, "Cyberpunk 2077"), message);
        Assert.Contains("Startoptionen", message, StringComparison.Ordinal);

        // Die Frühwarnung aus Steams Protokoll kann den Start auch melden, nur nicht sicher vor dem Spielprozess.
        Assert.Contains("zuverlässig nur über die Steam-Startoption", message, StringComparison.Ordinal);
        Assert.Contains("sicher nur beim Start über HDRpilot", message, StringComparison.Ordinal);

        // Dieselbe Startoption wie „Steam-Startoption kopieren“, Rückmeldung im Abschnitt „BILD“
        Assert.Equal([SteamLaunchOption.Build(@"E:\Apps\Hdr Pilot\HDRpilot.exe")], context.FakeClipboard.Texts);
        Assert.Equal(StatusKind.Success, details.MessageKind);
        Assert.Equal(LibraryStrings.AutoHdrSteam_Copied, details.Message);
        Assert.True(details.HasPictureMessage);
        Assert.False(details.HasLaunchMessage);
        Assert.False(details.HasExecutablesMessage);

        Assert.True(details.AutoHdr);
        Assert.True(Saved(context, Cyberpunk).AutoHdr);
        Assert.False(Saved(context, Cyberpunk).HdrAuto);
        Assert.Equal(1, context.Updates); // nur der Schalter selbst
    });

    [Fact]
    public void AutoHdrSteam_declining_changes_nothing_and_auto_hdr_stays_on() => Sta.Run(() =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(false);
        using var vm = new LibraryViewModel(context, dialogs);
        var details = Select(vm, Cyberpunk);

        details.AutoHdr = true;

        Assert.Single(dialogs.Confirms);
        Assert.Empty(context.FakeClipboard.Texts);
        Assert.False(details.HasMessage);
        Assert.True(details.AutoHdr);
        Assert.True(Saved(context, Cyberpunk).AutoHdr);
        Assert.True(vm.Games.Single(g => g.Key == Cyberpunk).AutoHdr);

        // Nur der Schalter selbst ist gespeichert, sonst nichts an der Konfiguration
        Assert.Equal(1, context.Updates);
        Assert.Equal([true, false, false, false, false, false], context.Config.Games.Select(g => g.AutoHdr));
        Assert.All(context.Config.Games, g => Assert.False(g.HdrAuto));
    });

    [Fact]
    public void AutoHdrSteam_no_offer_when_the_launch_option_is_already_set() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.SteamLaunchOptionIds.Add("1091500");
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);

        Select(vm, Cyberpunk).AutoHdr = true;

        Assert.Equal(["1091500"], context.SteamLaunchOptionChecks);
        Assert.Empty(dialogs.Confirms);
        Assert.Empty(context.FakeClipboard.Texts);
        Assert.True(Saved(context, Cyberpunk).AutoHdr);

        // Gegenprobe: bei einem Steam-Spiel ohne Startoption wird gefragt
        Select(vm, Hades).AutoHdr = true;
        Assert.Equal(["1091500", "1145350"], context.SteamLaunchOptionChecks);
        Assert.Contains("Hades II", Assert.Single(dialogs.Confirms).Message, StringComparison.Ordinal);
    });

    [Theory]
    [InlineData("Epic:Crow")]
    [InlineData("Gog:1207658924")]
    [InlineData("Xbox:Palworld")]
    [InlineData("Manual:m1")]
    public void AutoHdrSteam_no_offer_for_games_from_other_sources(string key) => Sta.Run(() =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);

        Select(vm, key).AutoHdr = true;

        Assert.Empty(context.SteamLaunchOptionChecks);
        Assert.Empty(dialogs.Confirms);
        Assert.Empty(context.FakeClipboard.Texts);
        Assert.True(Saved(context, key).AutoHdr);
    });

    [Fact]
    public void AutoHdrSteam_no_offer_when_switching_off_or_after_a_failed_save() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.SteamLaunchOptionIds.Add("1091500");
        context.ConfigService.Update(c => c.FindGame(Hades)!.AutoHdr = true);
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);

        // Ausschalten fragt nie
        var hades = Select(vm, Hades);
        Assert.True(hades.AutoHdr);
        hades.AutoHdr = false;
        Assert.Empty(context.SteamLaunchOptionChecks);
        Assert.Empty(dialogs.Confirms);

        // Speichern gescheitert: Schalter zurück, keine Frage
        context.FailNextUpdate = new IOException("Datenträger voll");
        hades.AutoHdr = true;
        Assert.False(hades.AutoHdr);
        Assert.Empty(context.SteamLaunchOptionChecks);
        Assert.Empty(dialogs.Confirms);

        // Gegenprobe: der nächste Versuch klappt und fragt
        hades.AutoHdr = true;
        Assert.Equal(["1145350"], context.SteamLaunchOptionChecks);
        Assert.Single(dialogs.Confirms);
    });

    [Fact]
    public void AutoHdrSteam_no_offer_when_the_switch_or_the_game_changed_while_steam_was_read() => Sta.Run(async () =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);

        // Während Steam gelesen wird, wieder ausgeschaltet
        var pending = new TaskCompletionSource<bool>();
        context.PendingSteamLaunchOptionCheck = pending;
        var cyberpunk = Select(vm, Cyberpunk);
        cyberpunk.AutoHdr = true;
        cyberpunk.AutoHdr = false;
        pending.SetResult(false);
        await Task.Yield();
        Assert.Empty(dialogs.Confirms);

        // Während Steam gelesen wird, ein anderes Spiel gewählt
        pending = new TaskCompletionSource<bool>();
        context.PendingSteamLaunchOptionCheck = pending;
        cyberpunk.AutoHdr = true;
        Select(vm, Hades);
        pending.SetResult(false);
        await Task.Yield();
        Assert.Empty(dialogs.Confirms);
        Assert.True(Saved(context, Cyberpunk).AutoHdr);

        // Gegenprobe: dasselbe ohne Änderung dazwischen fragt
        pending = new TaskCompletionSource<bool>();
        context.PendingSteamLaunchOptionCheck = pending;
        var hades = vm.Details!;
        hades.AutoHdr = true;
        Assert.Empty(dialogs.Confirms); // noch nicht: Steam wird noch gelesen
        pending.SetResult(false);
        await Task.Yield();
        Assert.Contains("Hades II", Assert.Single(dialogs.Confirms).Message, StringComparison.Ordinal);
    });

    [Fact]
    public void AutoHdrSteam_offer_opens_only_once_while_it_is_shown() => Sta.Run(() =>
    {
        var context = SampleContext();
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(false);
        using var vm = new LibraryViewModel(context, dialogs);
        var details = Select(vm, Cyberpunk);

        // Während die Rückfrage offen ist, schaltet der Nutzer aus und gleich wieder ein
        dialogs.WhileConfirmOpen = () =>
        {
            dialogs.WhileConfirmOpen = null;
            details.AutoHdr = false;
            details.AutoHdr = true;
        };
        details.AutoHdr = true;

        Assert.Single(dialogs.Confirms);
        Assert.Equal(["1091500", "1091500"], context.SteamLaunchOptionChecks); // geprüft, aber nicht doppelt gefragt
        Assert.True(Saved(context, Cyberpunk).AutoHdr);

        // Gegenprobe: danach fragt das nächste Einschalten wieder
        details.AutoHdr = false;
        details.AutoHdr = true;
        Assert.Equal(2, dialogs.Confirms.Count);
    });

    [Fact]
    public void AutoHdrSteam_failed_copy_is_reported_under_the_switch_and_auto_hdr_stays_on() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.FakeClipboard.Failure = new COMException("OpenClipboard fehlgeschlagen", unchecked((int)0x800401D0));
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);
        var details = Select(vm, Cyberpunk);

        details.AutoHdr = true;

        Assert.Equal(StatusKind.Error, details.MessageKind);
        Assert.Equal(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, "OpenClipboard fehlgeschlagen"), details.Message);
        Assert.True(details.HasPictureMessage);
        Assert.True(details.AutoHdr);
        Assert.True(Saved(context, Cyberpunk).AutoHdr);

        // Dieselbe Bedingung wie beim Kopieren von Hand: Programmpfad bekannt
        context.FakeClipboard.Failure = null;
        context.ExecutablePath = null;
        var hades = Select(vm, Hades);
        hades.AutoHdr = true;
        Assert.Empty(context.FakeClipboard.Texts);
        Assert.Equal(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, LibraryStrings.Shortcut_NoTarget), hades.Message);
        Assert.True(hades.HasPictureMessage);
        Assert.True(Saved(context, Hades).AutoHdr);
    });

    [Fact]
    public void AutoHdrSteam_status_reads_the_launch_option_from_steam_without_writing()
    {
        var steam = Path.Combine(_root, "Steam");
        var file = WriteAccount(steam, "111", """
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
            						"LaunchOptions"		"\"C:\\Users\\X\\AppData\\Local\\Programs\\HDRpilot\\HDRpilot.exe\" --wrap -- %command%"
            					}
            					"570"
            					{
            						"LaunchOptions"		"-novid -high"
            					}
            					"999"
            					{
            						"LaunchOptions"		"\"C:\\Tools\\HDRpilot.exe\" -- %command%"
            					}
            				}
            			}
            		}
            	}
            }
            """);
        var bytes = File.ReadAllBytes(file);
        var time = File.GetLastWriteTimeUtc(file);
        const string Exe = @"D:\Programmordner\HDRpilot\HDRpilot.exe";

        Assert.True(SteamLaunchOptionStatus.IsSet(() => steam, Exe, "1091500"));
        Assert.True(SteamLaunchOptionStatus.IsSet(() => steam, Exe, " 1091500 "));
        Assert.True(SteamLaunchOptionStatus.IsSet(() => steam, null, "1091500")); // Pfad unbekannt: Name der installierten EXE
        Assert.False(SteamLaunchOptionStatus.IsSet(() => steam, Exe, "570")); // fremde Startoption
        Assert.False(SteamLaunchOptionStatus.IsSet(() => steam, Exe, "999")); // ohne --wrap
        Assert.False(SteamLaunchOptionStatus.IsSet(() => steam, Exe, "1145350")); // keine Startoption
        Assert.False(SteamLaunchOptionStatus.IsSet(() => steam, @"D:\Portable\HdrPilot-Test.exe", "1091500")); // andere EXE
        Assert.False(SteamLaunchOptionStatus.IsSet(() => steam, Exe, ""));
        Assert.False(SteamLaunchOptionStatus.IsSet(() => null, Exe, "1091500")); // kein Steam
        Assert.False(SteamLaunchOptionStatus.IsSet(() => Path.Combine(_root, "fehlt"), Exe, "1091500"));

        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(time, File.GetLastWriteTimeUtc(file));
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    private static string WriteAccount(string steam, string account, string content)
    {
        var path = Path.Combine(steam, "userdata", account, "config", "localconfig.vdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
