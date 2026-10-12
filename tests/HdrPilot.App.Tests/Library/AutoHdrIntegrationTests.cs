using HdrPilot.App.Resources;
using HdrPilot.App.Tests.Setup.Engine;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Zusammenspiel von Oberfläche und Auto-HDR-Automatik (E88): Bibliothek und Einstellungen über dem echten
/// <see cref="AutoHdrService"/>, verdrahtet wie in AutomationHost (jede Konfigurationsänderung gleicht ab). Der Schlüssel
/// UserGpuPreferences liegt im Speicher; die Registry wird nie berührt.
/// </summary>
public sealed class AutoHdrIntegrationTests
{
    private const string ExeSdr = @"D:\Games\Sdr\Sdr.exe";
    private const string ExeOther = @"D:\Games\Anderes\Anderes.exe";
    private const string ExeLearned = @"D:\Games\Neu\bin\Neu.exe";
    private const string GlobalOff = "AutoHDREnable=0;SwapEffectUpgradeEnable=1;";
    private const string GlobalOn = "AutoHDREnable=1;SwapEffectUpgradeEnable=1;";
    private const string StateFile = @"C:\Cfg\autohdr-state.json";
    private const string MarkerFile = @"C:\Cfg\autohdr-global.json";

    private static readonly string AppliedOne = LibraryText.Format(LibraryStrings.Details_AutoHdrStatusApplied, LibraryStrings.Count_ExeOne);

    private static GameConfig NewGame(GameSource source, string id, string? exe, bool autoHdr = false) => new()
    {
        Source = source,
        SourceId = id,
        Name = "Spiel " + id,
        InstallDir = exe is null ? $@"D:\Games\{id}" : Path.GetDirectoryName(exe)!,
        KnownExecutables = exe is null ? [] : [exe],
        AutoHdr = autoHdr,
        IsNew = false,
        IsInstalled = true,
        LaunchCommand = "steam://rungameid/" + id,
    };

    /// <summary>Bibliothek über dem echten Dienst; „Sdr.exe“ hat schon einen Windows-Eintrag, der globale Wert ist aus.</summary>
    private static (TestLibraryContext Context, AutoHdrService Service, SetupGpuPreferenceStore Store) Create(params GameConfig[] games)
    {
        var context = new TestLibraryContext(games);
        var location = AutoHdrRegistryLocation.ForTest("AppTests");
        var store = new SetupGpuPreferenceStore(location.KeyName);
        store.SetString(ExeSdr, "GpuPreference=2;");
        store.SetString(AutoHdrRegistryLocation.GlobalValueName, GlobalOff);
        var service = new AutoHdrService(
            context.ConfigService, store, location, context.Files, AppPaths.InDirectory(@"C:\Cfg"), context.Env, context.FakeClock);
        context.ConfigService.Changed += (_, _) => service.OnConfigChanged(); // wie AutomationHost.OnConfigChanged
        service.Start();
        context.UseAutoHdr(service);
        return (context, service, store);
    }

    private static GameDetailsViewModel Select(LibraryViewModel vm, string key)
    {
        vm.SelectedGame = vm.Games.Single(g => g.Key == key);
        return vm.Details!;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Bedingung nicht erreicht");
    }

    private static SettingsViewModel Settings(TestLibraryContext context, AutoHdrService service)
    {
        var shell = new FakeShellContext(context.ConfigService, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(context.ConfigService))
        {
            AutoHdrService = service,
        };
        return new SettingsViewModel(shell, new FakeFolderPicker());
    }

    [Fact]
    public void Switch_in_the_details_writes_the_windows_value_and_shows_the_status_of_the_service() => Sta.Run(() =>
    {
        var sdr = NewGame(GameSource.Gog, "1", ExeSdr);
        var other = NewGame(GameSource.Steam, "2", ExeOther);
        var (context, _, store) = Create(sdr, other);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, sdr.Key);
        Assert.Empty(store.Operations); // ohne Spiel mit der Option: nichts eingetragen

        details.AutoHdr = true;

        Assert.Equal("GpuPreference=2;AutoHDREnable=2097;", store.GetString(ExeSdr));
        Assert.Equal(AppliedOne, details.AutoHdrStatusText);
        Assert.Equal(StatusKind.Success, details.AutoHdrStatusKind);
        Assert.True(details.Item.AutoHdr); // Kennzeichen „AUTO HDR“ in der Liste
        Assert.False(context.Config.FindGame(sdr.Key)!.HdrAuto);
        Assert.True(context.Files.FileExists(StateFile));
        Assert.Null(store.GetString(ExeOther)); // Gegenprobe: das andere Spiel bleibt unberührt
        Assert.Equal(GlobalOff, store.GetString(AutoHdrRegistryLocation.GlobalValueName)); // Notlösung aus, kein Spiel läuft

        details.AutoHdr = false;

        Assert.Equal("GpuPreference=2;", store.GetString(ExeSdr)); // genau der alte Wert
        Assert.Null(details.AutoHdrStatusText);
        Assert.False(details.Item.AutoHdr);
        Assert.False(context.Files.FileExists(StateFile));
        Assert.Equal(["set " + ExeSdr + "=GpuPreference=2;AutoHDREnable=2097;", "set " + ExeSdr + "=GpuPreference=2;"], store.Operations);
    });

    [Fact]
    public void Status_appears_only_after_the_option_is_saved_instead_of_flashing_no_exe_known() => Sta.Run(async () =>
    {
        var sdr = NewGame(GameSource.Gog, "1", ExeSdr);
        var (context, _, store) = Create(sdr);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, sdr.Key);
        var release = new TaskCompletionSource();
        context.HoldNextUpdate = release;

        details.AutoHdr = true;

        // Noch nicht gespeichert, also auch nicht abgeglichen: kein Stand statt „Noch keine Spiel-EXE bekannt“.
        Assert.True(details.AutoHdr);
        Assert.Null(details.AutoHdrStatusText);
        Assert.False(details.HasAutoHdrStatus);
        Assert.Equal("GpuPreference=2;", store.GetString(ExeSdr));

        release.SetResult();
        await Until(() => details.AutoHdrStatusText is not null);

        Assert.Equal(AppliedOne, details.AutoHdrStatusText);
        Assert.Equal("GpuPreference=2;AutoHDREnable=2097;", store.GetString(ExeSdr));
    });

    [Fact]
    public void Exe_learned_at_the_first_run_is_written_and_the_status_follows() => Sta.Run(() =>
    {
        var fresh = NewGame(GameSource.Steam, "3", exe: null, autoHdr: true);
        var (context, _, store) = Create(fresh);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, fresh.Key);
        Assert.Equal(LibraryStrings.Details_AutoHdrStatusNoExe, details.AutoHdrStatusText);
        Assert.Empty(store.Operations);

        // Wie die Sitzungslogik beim ersten Lauf (RecordDetectedExecutables): neue EXE in der Konfiguration.
        context.ConfigService.Update(c => c.FindGame(fresh.Key)!.DetectedExecutables.Add(ExeLearned));

        Assert.Equal("AutoHDREnable=2097;", store.GetString(ExeLearned));
        Assert.Equal(AppliedOne, details.AutoHdrStatusText);
    });

    [Fact]
    public void Removing_a_game_in_the_library_takes_back_only_its_values() => Sta.Run(async () =>
    {
        var manual = NewGame(GameSource.Manual, "m1", ExeSdr, autoHdr: true);
        var other = NewGame(GameSource.Steam, "2", ExeOther, autoHdr: true);
        var (context, _, store) = Create(manual, other);
        Assert.Equal("GpuPreference=2;AutoHDREnable=2097;", store.GetString(ExeSdr)); // Abgleich beim Start
        Assert.Equal("AutoHDREnable=2097;", store.GetString(ExeOther));
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(true);
        using var vm = new LibraryViewModel(context, dialogs);
        var details = Select(vm, manual.Key);

        details.RemoveGameCommand.Execute(null);
        await Until(() => context.Config.Games.Count == 1);

        Assert.Equal("GpuPreference=2;", store.GetString(ExeSdr));
        Assert.Equal("AutoHDREnable=2097;", store.GetString(ExeOther)); // Gegenprobe: das andere Spiel behält seinen Eintrag
    });

    [Fact]
    public void Rollback_button_in_the_settings_switches_games_off_and_restores_every_value() => Sta.Run(async () =>
    {
        var sdr = NewGame(GameSource.Gog, "1", ExeSdr, autoHdr: true);
        var other = NewGame(GameSource.Steam, "2", ExeOther, autoHdr: true);
        var off = NewGame(GameSource.Epic, "3", ExeLearned);
        var (context, service, store) = Create(sdr, other, off);
        Assert.Equal("GpuPreference=2;AutoHDREnable=2097;", store.GetString(ExeSdr));
        using var settings = Settings(context, service);

        await settings.RollbackAutoHdrAsync();

        Assert.Equal(SettingsViewModel.AutoHdrRollbackText(new AutoHdrRollbackResult(2, 2, 0, 0)), settings.AutoHdrRollbackMessage);
        Assert.False(settings.AutoHdrRollbackIsWarning);
        Assert.Equal("GpuPreference=2;", store.GetString(ExeSdr));
        Assert.Null(store.GetString(ExeOther)); // fehlte vorher
        Assert.Null(store.GetString(ExeLearned)); // Gegenprobe: nie eingetragen, auch nicht angelegt
        Assert.All(context.Config.Games, g => Assert.False(g.AutoHdr));
        Assert.False(context.Files.FileExists(StateFile));
        Assert.Equal(GlobalOff, store.GetString(AutoHdrRegistryLocation.GlobalValueName));

        await settings.RollbackAutoHdrAsync(); // zweites Mal: nichts mehr von HDRpilot da
        Assert.Equal(ShellStrings.AutoHdrRollbackNothing, settings.AutoHdrRollbackMessage);
    });

    [Fact]
    public void Fallback_switch_in_the_settings_sets_and_releases_the_global_value_only_while_a_game_runs() => Sta.Run(() =>
    {
        var sdr = NewGame(GameSource.Gog, "1", ExeSdr, autoHdr: true);
        var (context, service, store) = Create(sdr);
        var now = context.FakeClock.UtcNow;

        // Notlösung an, aber kein Spiel läuft: global unberührt.
        using (var settings = Settings(context, service))
        {
            settings.AutoHdrGlobalFallback = true;
            Assert.True(settings.Save());
        }

        Assert.True(context.Config.Settings.AutoHdrGlobalFallback);
        service.OnRecovery([], context.Config, now);
        service.OnCycle([], context.Config, paused: false, now);
        Assert.Equal(GlobalOff, store.GetString(AutoHdrRegistryLocation.GlobalValueName));

        // Spiel mit „Windows Auto HDR“ erkannt: global an, Merker geschrieben.
        service.OnCycle([sdr.Key], context.Config, paused: false, now);
        Assert.Equal(GlobalOn, store.GetString(AutoHdrRegistryLocation.GlobalValueName));
        Assert.True(context.Files.FileExists(MarkerFile));

        // Notlösung in den Einstellungen aus: sofort zurück, obwohl das Spiel noch läuft.
        using (var settings = Settings(context, service))
        {
            Assert.True(settings.AutoHdrGlobalFallback);
            settings.AutoHdrGlobalFallback = false;
            Assert.True(settings.Save());
        }

        Assert.Equal(GlobalOff, store.GetString(AutoHdrRegistryLocation.GlobalValueName));
        Assert.False(context.Files.FileExists(MarkerFile));
        Assert.Equal("GpuPreference=2;AutoHDREnable=2097;", store.GetString(ExeSdr)); // der Eintrag je Spiel bleibt
    });
}
