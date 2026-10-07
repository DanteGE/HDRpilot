using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

public sealed class LibraryLayoutTests
{
    private static TestLibraryContext SampleContext() => new(Game(GameSource.Steam, "1", "Hades II", isNew: false));

    [Theory]
    [InlineData(null, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(double.NaN, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(double.PositiveInfinity, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(0.0, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(-0.2, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(1.0, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(7.5, LibraryLayout.DefaultDetailsRatio)]
    [InlineData(0.02, LibraryLayout.MinDetailsRatio)]
    [InlineData(0.98, LibraryLayout.MaxDetailsRatio)]
    [InlineData(0.42, 0.42)]
    public void Stored_ratio_is_normalized(double? stored, double expected)
    {
        Assert.Equal(expected, LibraryLayout.Normalize(stored), 6);
    }

    [Fact]
    public void Restored_ratio_is_clamped_so_both_columns_keep_their_minimum_width()
    {
        // Breites Fenster: gemerkter Anteil gilt unverändert.
        Assert.Equal(0.5, LibraryLayout.Clamp(0.5, 2000), 6);

        // Schmales Fenster: Detailbereich zu breit für die Liste → so weit wie möglich, Liste behält ihr Minimum.
        var width = 900.0;
        var ratio = LibraryLayout.Clamp(0.7, width);
        Assert.Equal(LibraryLayout.ListMinWidth, width * (1 - ratio), 3);

        // Zu schmal für den Detailbereich → Mindestbreite des Detailbereichs.
        ratio = LibraryLayout.Clamp(0.16, width);
        Assert.Equal(LibraryLayout.DetailsMinWidth, width * ratio, 3);

        // Breite reicht nicht für beide Minima: anteilig, nichts negativ.
        ratio = LibraryLayout.Clamp(0.5, 500);
        Assert.InRange(ratio, 0.1, 0.9);
        Assert.Equal(LibraryLayout.DetailsMinWidth / (LibraryLayout.ListMinWidth + LibraryLayout.DetailsMinWidth), ratio, 6);

        // Unbekannte Breite (noch kein Layout): nur bereinigen.
        Assert.Equal(LibraryLayout.DefaultDetailsRatio, LibraryLayout.Clamp(null, 0), 6);
        Assert.Equal(0.3, LibraryLayout.Clamp(0.3, double.NaN), 6);
    }

    [Fact]
    public void Ratio_of_column_widths()
    {
        Assert.Equal(0.25, LibraryLayout.RatioOf(750, 250));
        Assert.Null(LibraryLayout.RatioOf(0, 0));
        Assert.Null(LibraryLayout.RatioOf(-1, 300));
        Assert.Null(LibraryLayout.RatioOf(double.NaN, 300));
    }

    [Fact]
    public void Details_ratio_is_saved_once_after_moving_and_on_close() => Sta.Run(() =>
    {
        var context = SampleContext();
        var vm = new LibraryViewModel(context, new FakeDialogs());
        Assert.Null(vm.DetailsRatio);
        var updates = context.Updates;

        // Mehrere Schritte mit den Pfeiltasten: ein Schreibvorgang nach der Wartezeit.
        vm.SetDetailsRatio(0.36);
        vm.SetDetailsRatio(0.3812345);
        Assert.Equal(updates, context.Updates);
        Assert.Equal(0.381, vm.DetailsRatio);
        context.Dispatcher.RunDelayed();
        Assert.Equal(updates + 1, context.Updates);
        Assert.Equal(0.381, context.Config.Settings.LibraryDetailsRatio);

        // Gleicher Wert: nichts zu tun.
        vm.SetDetailsRatio(0.381);
        Assert.Equal(0, context.Dispatcher.PendingDelays);

        // Doppelklick (Standard) und gleich danach schließen: wird beim Schließen noch gespeichert.
        vm.SetDetailsRatio(null);
        vm.Dispose();
        Assert.Null(context.Config.Settings.LibraryDetailsRatio);
        Assert.Equal(0, context.Dispatcher.PendingDelays);
    });

    [Fact]
    public void Details_ratio_is_read_from_the_configuration() => Sta.Run(() =>
    {
        var context = SampleContext();
        context.ConfigService.Update(c => c.Settings.LibraryDetailsRatio = 0.44);
        using var vm = new LibraryViewModel(context, new FakeDialogs());

        Assert.Equal(0.44, vm.DetailsRatio);
    });

    [Theory]
    [InlineData(1400, ToolbarMode.Wide, false)]
    [InlineData(1100, ToolbarMode.Wide, false)]
    [InlineData(1099, ToolbarMode.Compact, true)]
    [InlineData(1010, ToolbarMode.Compact, true)]
    [InlineData(1009, ToolbarMode.Stacked, false)]
    [InlineData(600, ToolbarMode.Stacked, false)]
    [InlineData(450, ToolbarMode.Stacked, true)]
    public void Toolbar_mode_follows_the_available_width(double available, ToolbarMode expected, bool iconOnly)
    {
        // Suche mindestens 200, Filter 600, Aktionen 300 (davon 90 Text bei „Neu einlesen“).
        var mode = LibraryToolbarLayout.Choose(available, 200, 600, 300, 90);

        Assert.Equal(expected, mode);
        Assert.Equal(iconOnly, LibraryToolbarLayout.RescanIconOnly(mode, available, 200, 300));
    }
}
