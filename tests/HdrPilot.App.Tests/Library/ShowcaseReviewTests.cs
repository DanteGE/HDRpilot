using System.ComponentModel;
using System.Xml.Linq;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Display;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Bibliothek nach der unabhängigen Prüfung (G10.10): helle Fassung nur, wenn sie zu sehen ist, „Symbol ändern…“ erneuert
/// auch die Bilder, die Werte der Bildschirm-Kapsel haben Namen für Bildschirmleser, sichtbare Texte nennen den Knopf
/// „Starten“.
/// </summary>
public sealed class ShowcaseReviewTests
{
    private static (TestLibraryContext Context, FakeArtworkService Art) Context()
    {
        var context = new TestLibraryContext(
            Game(GameSource.Steam, "1091500", "Cyberpunk 2077", isNew: false),
            Game(GameSource.Epic, "Crow", "Dead Island 2", isNew: false));
        var art = new FakeArtworkService();
        art.WithHero.Add("Steam:1091500");
        art.WithHero.Add("Epic:Crow");
        context.Artwork = art;
        return (context, art);
    }

    [Fact]
    public void Bright_variant_is_requested_only_while_hdr_shows() => Sta.Run(() =>
    {
        var (context, art) = Context();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        vm.SetWindowActive(true);
        vm.SelectedGame = vm.Games[0];

        // HDR aus: der Titelbereich zeigt das normale Bild, die helle Fassung wird nicht gleich berechnet (vorab erst, wenn
        // die Auswahl zur Ruhe gekommen ist, ShowcaseBoostTests).
        Assert.NotNull(vm.Hero!.Artwork);
        Assert.Null(vm.Hero.Artwork!.HeroBoost);
        Assert.Empty(art.BoostRequests);
        Assert.Empty(art.PrefetchRequests);

        // HDR an: jetzt wird sie gebraucht und kommt in den Titelbereich.
        context.Hdr.Displays[0].HdrOn = true;
        context.RaiseHdrStateChanged();
        Assert.NotNull(vm.Hero.Artwork!.HeroBoost);
        Assert.Equal(["Steam:1091500"], art.BoostRequests);

        // Spielwechsel bei HDR an: auch für das neue Titelbild.
        vm.SelectedGame = vm.Games[1];
        context.Dispatcher.RunDelayed();
        Assert.NotNull(vm.Hero!.Artwork!.HeroBoost);
        Assert.Equal(2, art.BoostRequests.Count);

        // HDR wieder aus, nächster Wechsel: keine neue Anfrage; nur nach der Ruhe vorab (niedrige Priorität).
        context.Hdr.Displays[0].HdrOn = false;
        context.RaiseHdrStateChanged();
        vm.SelectedGame = vm.Games[0];
        Assert.Equal(2, art.BoostRequests.Count);
        context.Dispatcher.RunDelayed(); // Titelbild bei schnellem Wechsel
        context.Dispatcher.RunDelayed(); // Ruhe vorbei
        Assert.Equal(2, art.BoostRequests.Count);
        Assert.Equal(["Steam:1091500"], art.PrefetchRequests);
        Assert.NotNull(vm.Hero!.Artwork!.HeroBoost);
    });

    [Fact]
    public void Changing_or_resetting_the_own_icon_reloads_the_images_of_the_game() => Sta.Run(async () =>
    {
        var (context, art) = Context();
        var dialogs = new FakeDialogs();
        dialogs.IconFiles.Enqueue(@"C:\Bilder\eigen.png");
        dialogs.IconFiles.Enqueue(@"C:\Bilder\eigen2.png");
        using var vm = new LibraryViewModel(context, dialogs);
        var item = vm.Games.Single(g => g.Key == "Epic:Crow");
        vm.SelectedGame = item;
        context.Dispatcher.RunDelayed();
        _ = item.Thumbnail; // Zeile sichtbar: Vorschaubild angefragt
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var heroRequests = art.ArtworkRequests.Count(k => k == "Epic:Crow");

        // Erstes eigenes Symbol, dann ein zweites unter demselben Dateinamen: beide Male neu.
        foreach (var _ in new[] { 1, 2 })
        {
            vm.ChangeIconCommand.Execute(null);
            await Sta.Finished(vm.ChangeIconCommand);
        }

        Assert.Equal(["Epic:Crow", "Epic:Crow"], art.Forgotten);
        Assert.Contains(nameof(GameItemViewModel.Thumbnail), changed); // Vorschaubild aus dem Symbol neu
        Assert.True(art.ArtworkRequests.Count(k => k == "Epic:Crow") >= heroRequests + 2); // Titelbereich und „Bilder“ neu

        vm.ResetIconCommand.Execute(null);
        await Sta.Finished(vm.ResetIconCommand);
        Assert.Equal(3, art.Forgotten.Count);
    });

    [Fact]
    public void Changed_icon_inputs_or_name_reload_thumbnail_and_cover() => Sta.Run(() =>
    {
        var (context, _) = Context();
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var item = vm.Games.Single(g => g.Key == "Epic:Crow");
        _ = item.Thumbnail;
        _ = item.Cover;
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        context.ConfigService.Update(c => c.FindGame("Epic:Crow")!.IconCandidates = [@"D:\Games\Crow\icon.png"]);

        Assert.Contains(nameof(GameItemViewModel.Thumbnail), changed);
        Assert.Contains(nameof(GameItemViewModel.Cover), changed);

        // Gegenprobe: eine Änderung ohne Einfluss auf die Bilder (zuletzt gespielt) lädt nichts neu.
        changed.Clear();
        _ = item.Thumbnail;
        _ = item.Cover;
        changed.Clear();
        context.ConfigService.Update(c => c.FindGame("Epic:Crow")!.LastPlayed = DateTimeOffset.UtcNow);
        Assert.DoesNotContain(nameof(GameItemViewModel.Thumbnail), changed);
    });

    [Fact]
    public void Capsule_values_have_names_for_screen_readers() => Sta.Run(() =>
    {
        var (context, _) = Context();
        var luminance = new FakeLuminanceService();
        context.Luminance = luminance;
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var readout = vm.Nits;
        Assert.Equal("Spitze in Nits: unbekannt", readout.PeakName);
        Assert.Equal("Vollbild in Nits: unbekannt", readout.FullFrameName);

        luminance.Set(new DisplayNits(FakeHdrController.Primary, "MAG 341C OLED", 456, 254, 0.04, 10, HdrActive: true, PeakMeasuredWithHdr: true));

        Assert.Equal("Spitze in Nits: 456", readout.PeakName);
        Assert.Equal("Vollbild in Nits: 254", readout.FullFrameName);
        Assert.StartsWith("Minimum in Nits: 0", readout.MinimumName, StringComparison.Ordinal);

        var xaml = File.ReadAllText(SourceFiles.MainWindowXaml);
        Assert.Contains("AutomationProperties.AutomationId=\"NitsPeak\" AutomationProperties.Name=\"{Binding Nits.PeakName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"NitsFullFrame\" AutomationProperties.Name=\"{Binding Nits.FullFrameName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"NitsMinimum\" AutomationProperties.Name=\"{Binding Nits.MinimumName}\"", xaml, StringComparison.Ordinal);
    });

    [Fact]
    public void View_switcher_shows_three_different_symbols_like_the_prototype()
    {
        // Ab Stufe 2 der Werkzeugleiste fehlen die Beschriftungen: Raster und Regal müssen am Symbol unterscheidbar sein.
        var xaml = XDocument.Load(SourceFiles.MainWindowXaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string Symbol(string button)
        {
            var radio = xaml.Descendants().Single(e => (string?)e.Attribute(x + "Name") == button);
            var icon = Assert.Single(radio.Descendants(), e => e.Name.LocalName == "LineIcon");
            return (string)icon.Attribute("Data")!;
        }

        var symbols = new[] { Symbol("ViewListButton"), Symbol("ViewGridButton"), Symbol("ViewShelfButton") };

        Assert.Equal(3, symbols.Distinct().Count());
        Assert.Equal("M3 6h18M3 18h18M6 9v6M10 9v6M14 9v6M18 9v6", symbols[2]); // Prototyp app.js: shelf
        Assert.Equal("M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z", symbols[1]); // grid
    }

    [Fact]
    public void Visible_texts_name_the_button_starten_and_the_file_hint_lists_bmp()
    {
        var resx = XDocument.Load(SourceFiles.InRepository("src", "HdrPilot.App", "Resources", "LibraryStrings.resx"));
        var values = resx.Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

        // „Mit HDR starten“ bleibt nur der Name für Bildschirmleser des Knopfs.
        Assert.Equal(["Launch_Button"], values.Where(v => v.Value.Contains("Mit HDR starten", StringComparison.Ordinal)).Select(v => v.Key));
        Assert.Contains("„Starten“", values["Pause_Tooltip"], StringComparison.Ordinal);
        Assert.Contains("„Starten“", values["Details_AutoHdrNeedsHdr"], StringComparison.Ordinal);

        // Der Hinweis nennt jedes Format, das der Dateidialog anbietet.
        var dialogs = File.ReadAllText(SourceFiles.InRepository("src", "HdrPilot.App", "Views", "Library", "LibraryDialogs.cs"));
        foreach (var format in new[] { "PNG", "JPG", "WebP", "BMP" })
        {
            Assert.Contains(format, values["Picker_PickFileHint"], StringComparison.Ordinal);
            Assert.Contains("*." + format.ToLowerInvariant(), dialogs + values.GetValueOrDefault("Picker_FileFilter", string.Empty), StringComparison.OrdinalIgnoreCase);
        }
    }
}
