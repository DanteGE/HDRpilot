using System.ComponentModel;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Windows Auto HDR“ in der Spielebibliothek (E88): Schalter im Abschnitt „BILD“ der Details, unabhängig von „HDR
/// automatisch“, Hinweise (HDR automatisch aus, eigenes HDR laut PCGamingWiki) und das Kennzeichen „AUTO HDR“ in der
/// Liste (nur Anzeige). Der Schalter ändert nur die Konfiguration; in Windows wirkt er erst beim Start über HDRpilot.
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

        // Von außen geändert (etwa per Kommandozeile): Liste zieht nach.
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
    public void Failed_save_puts_the_switch_back_and_shows_the_error() => Sta.Run(() =>
    {
        var context = SampleContext();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        context.FailNextUpdate = new IOException("Datenträger voll");

        details.AutoHdr = true;

        Assert.False(details.AutoHdr);
        Assert.False(Saved(context, Plain).AutoHdr);
        Assert.False(vm.Games.Single(g => g.Key == Plain).AutoHdr);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.Contains("Datenträger voll", vm.StatusMessage, StringComparison.Ordinal);

        details.AutoHdr = true; // Gegenprobe: der nächste Versuch klappt
        Assert.True(Saved(context, Plain).AutoHdr);
    });

    [Fact]
    public void Old_details_stop_listening_to_their_row_and_closing_detaches_the_shown_ones() => Sta.Run(() =>
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
        vm.Games.Single(g => g.Key == Native).HdrAuto = true;
        Assert.DoesNotContain(nameof(GameDetailsViewModel.ShowAutoHdrNeedsHdrHint), shownChanges);
    });

    [Fact]
    public void Option_changed_elsewhere_updates_the_shown_game_without_writing() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.FindGame(Plain)!.AutoHdr = true);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var details = Select(vm, Plain);
        var updates = context.Updates;

        context.ConfigService.Update(c => c.Games.ForEach(g => g.AutoHdr = false)); // etwa per Kommandozeile

        Assert.False(details.AutoHdr);
        Assert.False(details.Item.AutoHdr);
        Assert.Equal(updates, context.Updates); // die Details schreiben den Stand nicht zurück
    });
}
