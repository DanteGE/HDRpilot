using System.ComponentModel;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Windows Auto HDR“ in der Spielebibliothek (E88): Schalter im Abschnitt „BILD“ der Details, unabhängig von „HDR
/// automatisch“, Hinweise (HDR automatisch aus, eigenes HDR laut PCGamingWiki), Stand der Einträge und das Kennzeichen
/// „AUTO HDR“ in der Liste (nur Anzeige).
/// </summary>
public sealed class AutoHdrLibraryTests
{
    private const string Plain = "Gog:1207658924";
    private const string Native = "Steam:3008130";
    private const string Limited = "Steam:1091500";

    private static TestLibraryContext SampleContext()
    {
        var context = new TestLibraryContext(
            Game(GameSource.Gog, "1207658924", "SDR-Spiel", isNew: false),
            Game(GameSource.Steam, "3008130", "Dying Light: The Beast", isNew: false),
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", hdrAuto: true, isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false),
            Game(GameSource.Xbox, "Palworld", "Palworld", isNew: false));
        context.HdrSupport[Native] = HdrSupportStatus.Native;
        context.HdrSupport[Limited] = HdrSupportStatus.Limited;
        context.HdrSupport["Epic:Crow"] = HdrSupportStatus.ModOnly;
        context.HdrSupport["Xbox:Palworld"] = HdrSupportStatus.None;
        return context;
    }

    private static GameDetailsViewModel Select(LibraryViewModel vm, string key)
    {
        vm.SelectedGame = vm.Games.Single(g => g.Key == key);
        return vm.Details!;
    }

    private static GameConfig Saved(TestLibraryContext context, string key) => context.Config.FindGame(key)!;

    private static List<string?> Track(INotifyPropertyChanged source)
    {
        var names = new List<string?>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }

    [Fact]
    public void Switch_writes_only_auto_hdr_and_leaves_hdr_automatic_alone() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        Assert.False(details.AutoHdr);

        details.AutoHdr = true;

        Assert.True(Saved(context, Plain).AutoHdr);
        Assert.False(Saved(context, Plain).HdrAuto); // Gegenprobe: „HDR automatisch“ bleibt aus
        Assert.False(details.Item.HdrAuto);
        Assert.False(Saved(context, Native).AutoHdr); // nur dieses Spiel

        // Mit „HDR automatisch“ an: Ausschalten lässt es an.
        var cyberpunk = Select(vm, Limited);
        cyberpunk.AutoHdr = true;
        cyberpunk.AutoHdr = false;
        Assert.False(Saved(context, Limited).AutoHdr);
        Assert.True(Saved(context, Limited).HdrAuto);
        Assert.Equal(3, context.Updates);
    });

    [Fact]
    public void Hdr_automatic_switch_never_changes_auto_hdr_in_either_direction() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.FindGame(Plain)!.AutoHdr = true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        Assert.True(details.AutoHdr);

        details.Item.HdrAuto = true; // Schalter in der Liste oder in den Details (beide binden an Item.HdrAuto)
        Assert.True(Saved(context, Plain).HdrAuto);
        Assert.True(Saved(context, Plain).AutoHdr);
        Assert.True(details.AutoHdr);

        details.Item.HdrAuto = false;
        Assert.False(Saved(context, Plain).HdrAuto);
        Assert.True(Saved(context, Plain).AutoHdr);
        Assert.True(details.AutoHdr);
        Assert.True(details.Item.AutoHdr);
    });

    [Fact]
    public void List_badge_follows_the_config_for_each_game() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var row = vm.Games.Single(g => g.Key == Plain);
        var other = vm.Games.Single(g => g.Key == Native);
        Assert.False(row.AutoHdr);
        var changes = Track(row);

        Select(vm, Plain).AutoHdr = true;
        Assert.True(row.AutoHdr);
        Assert.Contains(nameof(GameItemViewModel.AutoHdr), changes);
        Assert.False(other.AutoHdr); // Gegenprobe: nur die Zeile dieses Spiels

        // Von außen geändert (etwa „Auto-HDR-Einträge zurücknehmen“ in den Einstellungen): Liste zieht nach.
        context.ConfigService.Update(c => c.FindGame(Plain)!.AutoHdr = false);
        Assert.False(row.AutoHdr);
        context.ConfigService.Update(c => c.FindGame(Native)!.AutoHdr = true);
        Assert.True(other.AutoHdr);
        Assert.False(row.AutoHdr);
    });

    [Fact]
    public void Needs_hdr_hint_shows_exactly_while_hdr_automatic_is_off_and_follows_it_live() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        Assert.True(details.ShowAutoHdrNeedsHdrHint); // auch bei ausgeschaltetem Auto HDR (Abhängigkeit vorher klar)
        var changes = Track(details);

        details.Item.HdrAuto = true;
        Assert.False(details.ShowAutoHdrNeedsHdrHint);
        Assert.Contains(nameof(GameDetailsViewModel.ShowAutoHdrNeedsHdrHint), changes);

        details.AutoHdr = true; // Gegenprobe: der Hinweis hängt an „HDR automatisch“, nicht an Auto HDR
        Assert.False(details.ShowAutoHdrNeedsHdrHint);

        changes.Clear();
        context.ConfigService.Update(c => c.FindGame(Plain)!.HdrAuto = false); // etwa per Kommandozeile
        Assert.True(details.ShowAutoHdrNeedsHdrHint);
        Assert.Contains(nameof(GameDetailsViewModel.ShowAutoHdrNeedsHdrHint), changes);

        Assert.False(Select(vm, Limited).ShowAutoHdrNeedsHdrHint); // Cyberpunk hat „HDR automatisch“ an
    });

    [Fact]
    public void Native_and_limited_hdr_subdue_the_row_with_their_hint_but_keep_it_usable() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        var native = Select(vm, Native);
        Assert.True(native.IsAutoHdrSubdued);
        Assert.True(native.ShowAutoHdrNativeHint);
        Assert.Equal(LibraryStrings.Details_AutoHdrNative, native.AutoHdrNativeHint);
        native.AutoHdr = true; // bleibt bedienbar
        Assert.True(Saved(context, Native).AutoHdr);

        var limited = Select(vm, Limited);
        Assert.True(limited.IsAutoHdrSubdued);
        Assert.Equal(LibraryStrings.Details_AutoHdrNativeLimited, limited.AutoHdrNativeHint);
        Assert.NotEqual(native.AutoHdrNativeHint, limited.AutoHdrNativeHint);
    });

    [Theory]
    [InlineData(Plain)]
    [InlineData("Epic:Crow")]
    [InlineData("Xbox:Palworld")]
    public void Unknown_none_and_mod_only_are_not_subdued_and_show_no_native_hint(string key) => Sta.Run(() =>
    {
        using var vm = new LibraryViewModel(SampleContext(), new FakeDialogs());
        var details = Select(vm, key);

        Assert.False(details.IsAutoHdrSubdued);
        Assert.False(details.ShowAutoHdrNativeHint);
        Assert.Equal(string.Empty, details.AutoHdrNativeHint); // nie null (Bindung an Text)
    });

    [Fact]
    public void Native_hint_follows_a_finished_hdr_list_sync_while_the_game_is_shown() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        Assert.False(details.IsAutoHdrSubdued);
        var changes = Track(details);

        context.PublishHdrSupport((Plain, HdrSupportStatus.Native));

        Assert.True(details.IsAutoHdrSubdued);
        Assert.Equal(LibraryStrings.Details_AutoHdrNative, details.AutoHdrNativeHint);
        Assert.Contains(nameof(GameDetailsViewModel.IsAutoHdrSubdued), changes);
        Assert.Contains(nameof(GameDetailsViewModel.AutoHdrNativeHint), changes);
    });

    [Fact]
    public void Status_is_hidden_while_off_and_says_no_exe_known_without_targets() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        Assert.False(details.HasAutoHdrStatus);
        Assert.Null(details.AutoHdrStatusText);
        Assert.Empty(context.AutoHdr.StatusReads); // aus: der Stand wird gar nicht erst gelesen

        details.AutoHdr = true;

        Assert.True(details.HasAutoHdrStatus);
        Assert.Equal(LibraryStrings.Details_AutoHdrStatusNoExe, details.AutoHdrStatusText);
        Assert.Equal(StatusKind.Info, details.AutoHdrStatusKind);
        Assert.Contains(Plain, context.AutoHdr.StatusReads);

        details.AutoHdr = false;
        Assert.False(details.HasAutoHdrStatus);
    });

    [Fact]
    public void Status_text_names_every_state_with_its_count() => Sta.Run(() =>
    {
        var applied = GameDetailsViewModel.AutoHdrStatusOf(Status(AutoHdrTargetState.Applied, AutoHdrTargetState.AlreadyOn));
        Assert.Equal(LibraryText.Format(LibraryStrings.Details_AutoHdrStatusApplied, LibraryText.Format(LibraryStrings.Count_ExeMany, 2)), applied.Text);
        Assert.Equal(StatusKind.Success, applied.Kind);

        var one = GameDetailsViewModel.AutoHdrStatusOf(Status(AutoHdrTargetState.Applied));
        Assert.Equal(LibraryText.Format(LibraryStrings.Details_AutoHdrStatusApplied, LibraryStrings.Count_ExeOne), one.Text);

        var external = GameDetailsViewModel.AutoHdrStatusOf(Status(AutoHdrTargetState.ExternallyChanged));
        Assert.Equal(LibraryText.Format(LibraryStrings.Details_AutoHdrStatusExternal, LibraryStrings.Count_ExeOne), external.Text);
        Assert.Equal(StatusKind.Warning, external.Kind);

        var unsupported = GameDetailsViewModel.AutoHdrStatusOf(Status(AutoHdrTargetState.Unsupported));
        Assert.Equal(LibraryText.Format(LibraryStrings.Details_AutoHdrStatusUnsupported, LibraryStrings.Count_ExeOne), unsupported.Text);
        Assert.Equal(StatusKind.Warning, unsupported.Kind);

        var failed = GameDetailsViewModel.AutoHdrStatusOf(Status(AutoHdrTargetState.Failed, AutoHdrTargetState.Failed, AutoHdrTargetState.Failed));
        Assert.Equal(LibraryText.Format(LibraryStrings.Details_AutoHdrStatusFailed, LibraryText.Format(LibraryStrings.Count_ExeMany, 3)), failed.Text);
        Assert.Equal(StatusKind.Warning, failed.Kind);
        Assert.DoesNotContain("Wirkt ab dem nächsten Spielstart", failed.Text, StringComparison.Ordinal); // nichts eingetragen
    });

    [Fact]
    public void Status_text_combines_states_in_a_fixed_order() => Sta.Run(() =>
    {
        var mixed = GameDetailsViewModel.AutoHdrStatusOf(Status(
            AutoHdrTargetState.Failed, AutoHdrTargetState.Applied, AutoHdrTargetState.Unsupported, AutoHdrTargetState.ExternallyChanged, AutoHdrTargetState.AlreadyOn));

        var expected = string.Join(" ",
            LibraryText.Format(LibraryStrings.Details_AutoHdrStatusApplied, LibraryText.Format(LibraryStrings.Count_ExeMany, 2)),
            LibraryText.Format(LibraryStrings.Details_AutoHdrStatusExternal, LibraryStrings.Count_ExeOne),
            LibraryText.Format(LibraryStrings.Details_AutoHdrStatusUnsupported, LibraryStrings.Count_ExeOne),
            LibraryText.Format(LibraryStrings.Details_AutoHdrStatusFailed, LibraryStrings.Count_ExeOne));
        Assert.Equal(expected, mixed.Text);
        Assert.Equal(StatusKind.Warning, mixed.Kind); // eine Warnung genügt, auch wenn anderes eingetragen ist
        Assert.Equal(StatusKind.Info, GameDetailsViewModel.AutoHdrStatusOf(AutoHdrGameStatus.None).Kind);
    });

    [Fact]
    public void Status_refreshes_when_the_service_reports_a_change_and_only_for_the_shown_game() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.FindGame(Plain)!.AutoHdr = true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        Assert.Equal(LibraryStrings.Details_AutoHdrStatusNoExe, details.AutoHdrStatusText);
        Assert.Equal(1, context.AutoHdrChangedSubscribers);

        context.AutoHdr.Publish(Plain, AutoHdrTargetState.Applied);
        Assert.Equal(LibraryText.Format(LibraryStrings.Details_AutoHdrStatusApplied, LibraryStrings.Count_ExeOne), details.AutoHdrStatusText);
        Assert.Equal(StatusKind.Success, details.AutoHdrStatusKind);

        context.AutoHdr.Publish(Plain, AutoHdrTargetState.ExternallyChanged);
        Assert.Equal(StatusKind.Warning, details.AutoHdrStatusKind);

        // Gegenprobe: ein anderes Spiel gewählt, die alten Details lesen nichts mehr.
        var native = Select(vm, Native);
        var reads = context.AutoHdr.StatusReads.Count;
        context.AutoHdr.Publish(Plain, AutoHdrTargetState.Failed);
        Assert.Equal(reads, context.AutoHdr.StatusReads.Count); // Native ist aus: kein Lesen, auch nicht für Plain
        Assert.Null(native.AutoHdrStatusText);
    });

    [Fact]
    public void Failed_save_puts_the_switch_back_and_shows_the_error() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        context.FailNextUpdate = new IOException("Datenträger voll");

        details.AutoHdr = true;

        Assert.False(details.AutoHdr);
        Assert.False(details.HasAutoHdrStatus);
        Assert.False(Saved(context, Plain).AutoHdr);
        Assert.False(vm.Games.Single(g => g.Key == Plain).AutoHdr);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.Contains("Datenträger voll", vm.StatusMessage, StringComparison.Ordinal);

        details.AutoHdr = true; // Gegenprobe: der nächste Versuch klappt
        Assert.True(Saved(context, Plain).AutoHdr);
    });

    [Fact]
    public void Old_details_stop_listening_and_closing_unsubscribes_from_the_service() => Sta.Run(() =>
    {
        var context = SampleContext();
        var vm = new LibraryViewModel(context, new FakeDialogs());
        var first = Select(vm, Plain);
        var firstChanges = Track(first);
        Select(vm, Native);

        vm.Games.Single(g => g.Key == Plain).HdrAuto = true;
        Assert.Empty(firstChanges); // abgemeldet: die Zeile hält die alten Details nicht mehr fest

        var shown = vm.Details!;
        var shownChanges = Track(shown);
        vm.Dispose();
        Assert.Equal(0, context.AutoHdrChangedSubscribers);
        context.AutoHdr.Publish(Native, AutoHdrTargetState.Applied);
        vm.Games.Single(g => g.Key == Native).HdrAuto = true;
        Assert.DoesNotContain(nameof(GameDetailsViewModel.ShowAutoHdrNeedsHdrHint), shownChanges);
    });

    [Fact]
    public void Rollback_from_the_settings_switches_the_shown_game_off_without_writing() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.FindGame(Plain)!.AutoHdr = true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        var updates = context.Updates;

        context.ConfigService.Update(c => c.Games.ForEach(g => g.AutoHdr = false)); // so schaltet die Rücknahme aus

        Assert.False(details.AutoHdr);
        Assert.False(details.HasAutoHdrStatus);
        Assert.False(details.Item.AutoHdr);
        Assert.Equal(updates, context.Updates); // die Details schreiben den Stand nicht zurück
    });

    private static AutoHdrGameStatus Status(params AutoHdrTargetState[] states) =>
        new(states.Select((s, i) => new AutoHdrTargetStatus($@"D:\Games\Spiel\game{i}.exe", s)).ToList());
}
