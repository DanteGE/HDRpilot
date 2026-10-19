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
        Assert.Equal(["ROLLA", "The Outlast Trials · „HDR automatisch“ ist an"], request.Details);
        Assert.Equal("Nicht verbunden: F:. Spiele dort sind vielleicht noch installiert.", request.Warning);
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

        Assert.Equal(["ROLLA"], Assert.Single(_confirm.Requests).Details);
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
