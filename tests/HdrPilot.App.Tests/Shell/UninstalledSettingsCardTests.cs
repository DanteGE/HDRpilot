using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Tests.Library;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.Sessions;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Karte „Bibliothek“ in den Einstellungen (E90, G11.1/G11.2): Schalter „Nicht installierte Spiele ausblenden“ mit der Zahl
/// daneben, übernommen erst mit „Speichern“; „Einstellungen aller nicht installierten Spiele entfernen…“ nennt alle
/// betroffenen Spiele in der Rückfrage, entfernt nur sie (sofort, unabhängig von „Speichern“) und lässt inzwischen
/// wiedergefundene oder laufende Spiele stehen.
/// </summary>
public sealed class UninstalledSettingsCardTests
{
    private const string Outlast = "Steam:1304930";
    private const string Rolla = "Steam:3975830";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeIcons _icons = new();
    private readonly FakeShortcuts _shortcuts = new();
    private readonly FakeConfirm _confirm = new();

    private static GameConfig Game(GameSource source, string id, string name, bool installed, bool hdrAuto = false) => new()
    {
        Source = source,
        SourceId = id,
        Name = name,
        InstallDir = @"F:\Steam-Bibliothek\steamapps\common\" + name,
        IsInstalled = installed,
        IsNew = false,
        HdrAuto = hdrAuto,
    };

    private (SettingsViewModel Vm, FakeShellContext Context) Create(bool hide = false)
    {
        var config = ShellTestFactory.NewConfig(_fs, c =>
        {
            c.Settings.HideUninstalledGames = hide;
            c.Games.Add(Game(GameSource.Steam, "1304930", "The Outlast Trials", installed: false, hdrAuto: true));
            c.Games.Add(Game(GameSource.Steam, "3975830", "ROLLA", installed: false));
            c.Games.Add(Game(GameSource.Manual, "m1", "Eigenes Spiel", installed: false));
            c.Games.Add(Game(GameSource.Steam, "553850", "HELLDIVERS 2", installed: true, hdrAuto: true));
        });
        var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config))
        {
            Cleanup = new UninstalledGameCleanup(_fs, @"C:\Data\icons", @"C:\Data\art\custom", _icons, HdrPilot.App.Services.Artwork.NullArtworkService.Instance, _shortcuts),
        };
        return (new SettingsViewModel(context, new FakeFolderPicker(), confirm: _confirm), context);
    }

    [Fact]
    public void Uninstalled_card_shows_the_count_and_hiding_waits_for_save()
    {
        var (vm, context) = Create();
        Assert.Equal(3, vm.UninstalledCount); // auch das eigene Spiel ohne Ordner
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.UninstalledCount, 3), vm.UninstalledCountText);
        Assert.Equal(2, vm.RemovableCount);
        Assert.False(vm.HideUninstalledGames);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.LibraryCardSummary, 4, 2), vm.LibrarySummary);

        vm.HideUninstalledGames = true;
        Assert.True(vm.HasChanges);
        vm.Cancel();
        Assert.False(context.Config.Current.Settings.HideUninstalledGames);

        var (again, _) = Create();
        again.HideUninstalledGames = true;
        Assert.True(again.Save());
        Assert.True(ShellTestFactory.NewConfig(_fs).Current.Settings.HideUninstalledGames);
    }

    [Fact]
    public void Uninstalled_library_summary_leaves_out_hidden_games()
    {
        var (vm, _) = Create(hide: true);

        Assert.True(vm.HideUninstalledGames);
        Assert.False(vm.HasChanges);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.LibraryCardSummary, 1, 1), vm.LibrarySummary);
    }

    [Fact]
    public void Uninstalled_bulk_removal_names_both_games_and_removes_only_them() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, false));

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        var request = Assert.Single(_confirm.Requests);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_ConfirmTitleMany, 2), request.Title);
        Assert.Equal([new ConfirmDetail("ROLLA", "Laufwerk F: nicht verbunden"), new ConfirmDetail("The Outlast Trials", "Laufwerk F: nicht verbunden · „HDR automatisch“ ist an")], request.Details);
        Assert.Equal("Laufwerk F: ist nicht verbunden. Spiele dort sind vielleicht noch installiert.", request.Warning);
        Assert.Null(request.OptionText);
        Assert.Equal(["Manual:m1", "Steam:553850"], context.Config.Current.Games.Select(g => g.Key));
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_DoneMany, 2), vm.LibraryMessage);
        Assert.False(vm.LibraryMessageIsError);
        Assert.Equal((1, 0), (vm.UninstalledCount, vm.RemovableCount));
        Assert.False(vm.RemoveUninstalledCommand.CanExecute(null));
        Assert.Equal([Outlast, Rolla], _icons.ForgottenGames.Order(StringComparer.Ordinal));
        Assert.False(vm.HasChanges); // sofort gespeichert, „Speichern“ hat nichts mehr zu tun
    });

    [Fact]
    public void Uninstalled_bulk_removal_cancel_changes_nothing() => Sta.Run(async () =>
    {
        var (vm, _) = Create();
        _confirm.Answers.Enqueue(new ConfirmAnswer(false, false));
        var before = _fs.ReadAllBytes(ShellTestFactory.ConfigPath);

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Single(_confirm.Requests);
        Assert.Equal(before, _fs.ReadAllBytes(ShellTestFactory.ConfigPath));
        Assert.Empty(_icons.ForgottenGames);
        Assert.Null(vm.LibraryMessage);
        Assert.Equal(2, vm.RemovableCount);
    });

    [Fact]
    public void Uninstalled_bulk_removal_keeps_a_game_found_again_and_offers_the_shortcuts() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        _shortcuts.Existing[Outlast] = [new GameShortcut(@"C:\Users\Test\Desktop\Outlast (HDR).lnk", @"C:\Data\icons\x.ico")];
        _shortcuts.Existing[Rolla] = [new GameShortcut(@"C:\Users\Test\Desktop\ROLLA (HDR).lnk", @"C:\Data\icons\y.ico")];
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, true));
        _confirm.WhileOpen = () => context.Config.Update(c => c.FindGame(Rolla)!.IsInstalled = true); // ein Einlesen findet ROLLA wieder

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Equal("Auch die 2 HDRpilot-Verknüpfungen löschen", Assert.Single(_confirm.Requests).OptionText);
        Assert.NotNull(context.Config.Current.FindGame(Rolla));
        Assert.Null(context.Config.Current.FindGame(Outlast));
        Assert.Equal([@"C:\Users\Test\Desktop\Outlast (HDR).lnk"], _shortcuts.Deleted.Select(s => s.Path)); // nur die des entfernten Spiels
        Assert.Equal(LibraryStrings.RemoveAll_DoneOne + " " + LibraryStrings.RemoveAll_KeptOne, vm.LibraryMessage);
        Assert.True(vm.LibraryMessageIsError);
    });

    [Fact]
    public void Uninstalled_bulk_removal_leaves_out_a_running_game() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        context.SessionList.Add(new SessionInfo(Outlast, "The Outlast Trials", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [9]));
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, false));

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Equal([new ConfirmDetail("ROLLA", "Laufwerk F: nicht verbunden")], Assert.Single(_confirm.Requests).Details);
        Assert.Equal(LibraryStrings.RemoveAll_ConfirmTitleOne, _confirm.Requests[0].Title);
        Assert.NotNull(context.Config.Current.FindGame(Outlast));
        Assert.Null(context.Config.Current.FindGame(Rolla));
    });

    [Fact]
    public void Uninstalled_bulk_removal_needs_a_confirmation_dialog_and_removable_games()
    {
        var config = ShellTestFactory.NewConfig(_fs, c => c.Games.Add(Game(GameSource.Steam, "1", "Weg", installed: false)));
        var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
        Assert.False(new SettingsViewModel(context, new FakeFolderPicker()).RemoveUninstalledCommand.CanExecute(null)); // ohne Aufräumen und Rückfrage

        var (vm, _) = Create();
        Assert.True(vm.RemoveUninstalledCommand.CanExecute(null));
        Assert.True(vm.HasRemovable);
    }

    [Fact]
    public void Uninstalled_bulk_removal_keeps_a_game_whose_session_starts_while_confirming() => Sta.Run(async () =>
    {
        // Geprüft wird beim Schreiben: Startet The Outlast Trials, während die Rückfrage offen ist, bleibt es samt Dateien.
        var (vm, context) = Create();
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, false));
        _confirm.WhileOpen = () => context.SessionList.Add(new SessionInfo(Outlast, "The Outlast Trials", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [9]));

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Equal(2, Assert.Single(_confirm.Requests).Details.Count); // beim Fragen lief es noch nicht
        Assert.True(context.Config.Current.FindGame(Outlast)!.HdrAuto);
        Assert.Null(context.Config.Current.FindGame(Rolla));
        Assert.Equal([Rolla], _icons.ForgottenGames);
        Assert.Equal(LibraryStrings.RemoveAll_DoneOne + " " + LibraryStrings.RemoveAll_KeptOne, vm.LibraryMessage);
    });

    [Fact]
    public void Uninstalled_bulk_removal_says_so_when_every_removable_game_is_running() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        context.SessionList.Add(new SessionInfo(Outlast, "The Outlast Trials", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [9]));
        context.SessionList.Add(new SessionInfo(Rolla, "ROLLA", SessionPhase.Launching, SessionTrigger.SteamLaunchOption, DateTimeOffset.UnixEpoch, [], []));

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Empty(_confirm.Requests);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_RunningMany, 2), vm.LibraryMessage);
        Assert.True(vm.LibraryMessageIsError);
        Assert.Equal(2, context.Config.Current.Games.Count(g => !g.IsInstalled && !g.IsManual));

        context.SessionList.RemoveAt(1);
        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);
        Assert.Single(_confirm.Requests); // ROLLA läuft nicht mehr: gefragt wird nach ihm allein
        context.SessionList.Add(new SessionInfo(Rolla, "ROLLA", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [3]));
        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);
        Assert.Single(_confirm.Requests);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_RunningMany, 2), vm.LibraryMessage);
    });

    [Fact]
    public void Uninstalled_bulk_removal_says_which_game_runs_when_it_is_the_only_one() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        context.Config.Update(c => c.Games.RemoveAll(g => g.Key == Rolla));
        context.SessionList.Add(new SessionInfo(Outlast, "The Outlast Trials", SessionPhase.Running, SessionTrigger.ProcessDetected, DateTimeOffset.UnixEpoch, [], [9]));

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Empty(_confirm.Requests);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveSettings_Running, "The Outlast Trials"), vm.LibraryMessage);
    });

    [Fact]
    public void Uninstalled_bulk_removal_counts_the_library_again() => Sta.Run(async () =>
    {
        // Die Zahl unten in der Navigation („4 Spiele · 2 mit HDR automatisch“) folgt dem Sammel-Entfernen sofort.
        var (vm, _) = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, false));

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ShellStrings.LibraryCardSummary, 2, 1), vm.LibrarySummary);
        Assert.Contains(nameof(SettingsViewModel.LibrarySummary), changed);
    });

    [Fact]
    public void Uninstalled_bulk_removal_failed_save_after_the_change_deletes_nothing() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        _fs.AddFile(@"C:\Data\icons\" + CustomIcons.FileNameFor(Outlast), "png");
        _shortcuts.Existing[Outlast] = [new GameShortcut(@"C:\Users\Test\Desktop\Outlast (HDR).lnk", @"C:\Data\icons\x.ico")];
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, true));
        _confirm.WhileOpen = () => _fs.FailWriting(ShellTestFactory.ConfigPath, new IOException("Datenträger voll")); // das Lambda läuft, Speichern scheitert

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.NotNull(context.Config.Current.FindGame(Outlast));
        Assert.NotNull(context.Config.Current.FindGame(Rolla));
        Assert.True(_fs.FileExists(@"C:\Data\icons\" + CustomIcons.FileNameFor(Outlast)));
        Assert.Empty(_icons.ForgottenGames);
        Assert.Empty(_shortcuts.Deleted);
        Assert.True(vm.LibraryMessageIsError);
        Assert.Equal(2, vm.RemovableCount);
    });

    [Fact]
    public void Uninstalled_bulk_removal_with_nothing_removed_only_says_that_the_games_stay() => Sta.Run(async () =>
    {
        // Beide Spiele findet ein Einlesen während der Rückfrage wieder: nicht „Einstellungen von 0 Spielen entfernt“.
        var (vm, context) = Create();
        _confirm.Answers.Enqueue(new ConfirmAnswer(true, false));
        _confirm.WhileOpen = () => context.Config.Update(c =>
        {
            c.FindGame(Outlast)!.IsInstalled = true;
            c.FindGame(Rolla)!.IsInstalled = true;
        });

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_KeptMany, 2), vm.LibraryMessage);
        Assert.True(vm.LibraryMessageIsError);
        Assert.Empty(_icons.ForgottenGames);
    });

    [Fact]
    public void Uninstalled_bulk_removal_marks_games_whose_exe_is_still_there() => Sta.Run(async () =>
    {
        // Steam nicht gefunden: Seine Spiele gelten als nicht installiert, ihre EXEs sind aber da. Die Rückfrage nennt bei
        // jedem, dass die EXE noch da ist, und warnt; ein Restordner nur mit Spielständen bleibt ohne Hinweis.
        var (vm, context) = Create();
        context.Config.Update(c => c.FindGame(Rolla)!.InstallDir = @"C:\SteamLibrary\steamapps\common\ROLLA");
        context.Config.Update(c => c.FindGame(Outlast)!.InstallDir = @"C:\SteamLibrary\steamapps\common\Nachtprobe");
        _fs.AddFile(@"C:\SteamLibrary\steamapps\common\ROLLA\ROLLA.exe", "exe");
        _fs.AddFile(@"C:\SteamLibrary\steamapps\common\Nachtprobe\Saved\SaveGames\Profil.sav", "sav"); // Reste: deinstalliert

        vm.RemoveUninstalledCommand.Execute(null);
        await Sta.Finished(vm.RemoveUninstalledCommand);

        var request = Assert.Single(_confirm.Requests);
        Assert.Equal([new ConfirmDetail("ROLLA", "EXE\u2011Datei noch da"), new ConfirmDetail("The Outlast Trials", "„HDR automatisch“ ist an")], request.Details);
        Assert.Equal(LibraryStrings.RemoveAll_UnsureOne, request.Warning);
    });

    [Fact]
    public void Uninstalled_bulk_removal_names_why_each_game_may_still_be_installed()
    {
        // Je Spiel der Grund unter dem Namen, oben die Laufwerke und die Zahl der übrigen Spiele, die vielleicht noch
        // installiert sind (EXE noch da, bei Steam eingetragen, nicht zu prüfen).
        GameConfig G(string id, string name) => new() { Source = GameSource.Steam, SourceId = id, Name = name, IsInstalled = false };
        var request = RemoveSettingsText.ForGames(
            [G("1", "Eins"), G("2", "Zwei"), G("3", "Drei"), G("4", "Vier"), G("5", "Fünf")],
            [InstallTrace.MissingDrive("F:"), new InstallTrace(InstallSign.Program), new InstallTrace(InstallSign.SteamManifest), new InstallTrace(InstallSign.Unchecked), InstallTrace.None],
            shortcuts: 0);

        Assert.Equal(
            [
                new ConfirmDetail("Eins", "Laufwerk F: nicht verbunden"), new ConfirmDetail("Zwei", "EXE\u2011Datei noch da"),
                new ConfirmDetail("Drei", "bei Steam noch eingetragen"), new ConfirmDetail("Vier", "nicht prüfbar"), new ConfirmDetail("Fünf"),
            ],
            request.Details);
        Assert.Equal(
            "Laufwerk F: ist nicht verbunden. Spiele dort sind vielleicht noch installiert. " + LibraryText.Format(LibraryStrings.RemoveAll_UnsureMany, 3),
            request.Warning);
        Assert.Equal("Bei 3 Spielen steht unter dem Namen, warum sie vielleicht noch installiert sind.", LibraryText.Format(LibraryStrings.RemoveAll_UnsureMany, 3));

        // Einzeln: je Grund die eigene Warnung.
        var game = G("6", "Sechs");
        Assert.Equal(LibraryStrings.RemoveSettings_ProgramPresent, RemoveSettingsText.ForGame(game, new InstallTrace(InstallSign.Program), 0).Warning);
        Assert.Equal(LibraryStrings.RemoveSettings_SteamListed, RemoveSettingsText.ForGame(game, new InstallTrace(InstallSign.SteamManifest), 0).Warning);
        Assert.Equal(LibraryStrings.RemoveSettings_Unchecked, RemoveSettingsText.ForGame(game, new InstallTrace(InstallSign.Unchecked), 0).Warning);
        Assert.Null(RemoveSettingsText.ForGame(game, InstallTrace.None, 0).Warning);
    }

    [Fact]
    public void Uninstalled_bulk_removal_says_that_it_is_checking_while_a_drive_answers_slowly() => Sta.Run(async () =>
    {
        var (vm, context) = Create();
        var slow = new HangingFileSystem(_fs, @"F:\");
        context.Cleanup = new UninstalledGameCleanup(slow, @"C:\Data\icons", @"C:\Data\art\custom", _icons, HdrPilot.App.Services.Artwork.NullArtworkService.Instance, _shortcuts);
        string? messageWhileAsking = "nicht gefragt";
        _confirm.WhileOpen = () => messageWhileAsking = vm.LibraryMessage;
        try
        {
            vm.RemoveUninstalledCommand.Execute(null);

            // Nach CheckingDelay, sicher vor dem Ende der Prüfung (CheckTimeout); gewartet wird, bis es so weit ist, nicht eine
            // feste Zeit (Zeitgeber laufen unter Last im Threadpool verspätet).
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (vm.LibraryMessage != LibraryStrings.RemoveAll_Checking && watch.Elapsed < UninstalledGameCleanup.CheckTimeout * 0.9)
            {
                await Task.Delay(20);
            }

            Assert.InRange(watch.Elapsed, LibraryViewModel.CheckingDelay * 0.9, UninstalledGameCleanup.CheckTimeout);
            Assert.Equal(LibraryStrings.RemoveAll_Checking, vm.LibraryMessage);
            Assert.False(vm.LibraryMessageIsError);
            Assert.False(vm.RemoveUninstalledCommand.CanExecute(null));
            await Sta.Finished(vm.RemoveUninstalledCommand);
        }
        finally
        {
            slow.Release();
        }

        Assert.Null(messageWhileAsking);
        Assert.Equal(LibraryText.Format(LibraryStrings.RemoveAll_UnsureMany, 2), Assert.Single(_confirm.Requests).Warning); // F: antwortet nicht: bei beiden nicht zu klären
    });

    /// <summary>Rückfrage zum Mitlesen: Antworten der Reihe nach, sonst „Abbrechen“.</summary>
    private sealed class FakeConfirm : IConfirmDialogs
    {
        public List<ConfirmRequest> Requests { get; } = [];

        public Queue<ConfirmAnswer> Answers { get; } = new();

        public Action? WhileOpen { get; set; }

        public ConfirmAnswer Confirm(ConfirmRequest request)
        {
            Requests.Add(request);
            WhileOpen?.Invoke();
            return Answers.Count > 0 ? Answers.Dequeue() : new ConfirmAnswer(false, false);
        }
    }
}
