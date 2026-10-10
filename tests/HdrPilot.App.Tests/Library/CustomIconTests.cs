using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Tests.Shell;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.App.Views.Library;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Eigenes Symbol per Rechtsklick (G8.3, E86): als PNG mit 96 px im Datenordner, Vorrang vor allen automatischen Quellen,
/// „Symbol zurücksetzen“ nur mit eigenem Symbol, die Liste zeigt die Änderung sofort.
/// </summary>
public sealed class CustomIconTests : IDisposable
{
    private readonly IconFixtures _files = new();

    private string IconDir => _files.PathOf("icons");

    public void Dispose() => _files.Dispose();

    [Fact]
    public void CustomIcon_is_stored_as_96_px_png_in_the_icon_folder()
    {
        var source = _files.Png("bild.png", IconFixtures.Colorful(300, 150));
        var before = File.ReadAllBytes(source);

        var name = new CustomIconStore(IconDir).Save("Steam:1091500", source);

        Assert.Equal(CustomIcons.FileNameFor("Steam:1091500"), name);
        var stored = Path.Combine(IconDir, name);
        var frame = BitmapDecoder.Create(new Uri(stored), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(CustomIcons.StoredSize, frame.PixelWidth);
        Assert.Equal(CustomIcons.StoredSize, frame.PixelHeight);
        var pixels = new byte[96 * 96 * 4];
        new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 96 * 4, 0);
        Assert.Equal(0, pixels[3]); // Breitbild: oben durchsichtiger Rand
        Assert.Equal(255, pixels[((48 * 96) + 48) * 4 + 3]);
        Assert.Equal(before, File.ReadAllBytes(source)); // die gewählte Datei bleibt unverändert
        Assert.Single(Directory.GetFiles(IconDir)); // keine Reste der temporären Datei
    }

    [Fact]
    public void CustomIcon_can_come_from_an_ico_or_an_exe()
    {
        var store = new CustomIconStore(IconDir);
        var ico = _files.Ico("spiel.ico", IconFixtures.Colorful(32, 32), IconFixtures.Colorful(256, 256));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        var fromIco = store.Save("Gog:1", ico);
        var fromExe = store.Save("Epic:Crow", exe);

        foreach (var name in new[] { fromIco, fromExe })
        {
            var frame = BitmapDecoder.Create(new Uri(Path.Combine(IconDir, name)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(96, frame.PixelWidth);
        }
    }

    [Theory]
    [InlineData("text.txt")]
    [InlineData("kaputt.png")]
    [InlineData("kaputt.exe")]
    public void CustomIcon_rejects_files_without_an_image_and_writes_nothing(string name)
    {
        var path = _files.Bytes(name, Encoding.ASCII.GetBytes("kein Bild"));

        Assert.ThrowsAny<Exception>(() => new CustomIconStore(IconDir).Save("Steam:1", path));
        Assert.True(!Directory.Exists(IconDir) || Directory.GetFiles(IconDir).Length == 0);
    }

    [Fact]
    public void CustomIcon_reset_deletes_only_plain_names_inside_the_icon_folder()
    {
        var store = new CustomIconStore(IconDir);
        var name = store.Save("Steam:1", _files.Png("bild.png", IconFixtures.Colorful(64, 64)));
        var outside = _files.Png("draussen.png", IconFixtures.Colorful(8, 8));

        store.Delete(@"..\draussen.png");
        store.Delete(outside);
        store.Delete(null);
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(Path.Combine(IconDir, name)));

        store.Delete(name);
        Assert.False(File.Exists(Path.Combine(IconDir, name)));
        store.Delete(name); // fehlt schon: nichts passiert
    }

    [Fact]
    public void CustomIcon_has_priority_over_launcher_hint_and_exe()
    {
        var store = new CustomIconStore(IconDir);
        var game = IconSelectionTests.Game(_files.Ico("steam.ico", IconFixtures.Colorful(256, 256)));
        game.CustomIcon = store.Save(game.Key, _files.Png("eigen.png", IconFixtures.Colorful(128, 128)));
        var service = new GameIconService(new HdrPilot.Tests.TestSupport.InMemoryFileSystem(), iconDirectory: IconDir);

        var chosen = service.Select(game);

        Assert.Equal(IconOrigin.Custom, chosen!.Origin);
        Assert.Equal(Path.Combine(IconDir, game.CustomIcon), chosen.Path);
        Assert.Equal(96, chosen.Icon.NativeSize);
    }

    [Fact]
    public void CustomIcon_missing_or_broken_file_falls_back_to_the_automatic_icon()
    {
        var hint = _files.Ico("steam.ico", IconFixtures.Colorful(256, 256));
        var game = IconSelectionTests.Game(hint);
        game.CustomIcon = CustomIcons.FileNameFor(game.Key); // Datei fehlt (etwa auf einem anderen PC)
        var service = new GameIconService(new HdrPilot.Tests.TestSupport.InMemoryFileSystem(), iconDirectory: IconDir);

        Assert.Equal((IconOrigin.Launcher, hint), (service.Select(game)!.Origin, service.Select(game)!.Path));

        Directory.CreateDirectory(IconDir);
        File.WriteAllText(Path.Combine(IconDir, game.CustomIcon), "kaputt");
        Assert.Equal(IconOrigin.Launcher, service.Select(game)!.Origin);
    }

    [Fact]
    public void CustomIcon_change_command_saves_writes_config_and_reloads_the_icon() => Sta.Run(async () =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1091500", "Cyberpunk 2077"));
        var dialogs = new FakeDialogs();
        dialogs.IconFiles.Enqueue(@"C:\Bilder\cyberpunk.png");
        using var vm = new LibraryViewModel(context, dialogs);
        var item = vm.Games.Single();
        vm.SelectedGame = item;
        _ = item.Icon; // Zeile sichtbar: Symbol angefragt
        Assert.False(vm.ResetIconCommand.CanExecute(null));
        Assert.True(vm.ChangeIconCommand.CanExecute(null));

        vm.ChangeIconCommand.Execute(null);
        await Until(() => !vm.ChangeIconCommand.IsRunning);

        var name = CustomIcons.FileNameFor("Steam:1091500");
        Assert.Equal([("Steam:1091500", @"C:\Bilder\cyberpunk.png")], context.FakeIcons.Saved);
        Assert.Equal(name, context.Config.FindGame("Steam:1091500")!.CustomIcon);
        Assert.Contains("Steam:1091500", context.FakeIcons.Forgotten);
        Assert.True(item.HasCustomIcon);
        Assert.True(vm.ResetIconCommand.CanExecute(null));
        Assert.Equal(LibraryText.Format(LibraryStrings.Icon_Changed, "Cyberpunk 2077"), vm.StatusMessage);

        _ = item.Icon; // sofort neu angefragt, nicht erst beim nächsten Öffnen
        Assert.Equal(2, context.FakeIcons.Requested.Count(k => k == "Steam:1091500"));
    });

    [Fact]
    public void CustomIcon_change_cancelled_or_unreadable_changes_nothing() => Sta.Run(async () =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Spiel"));
        var dialogs = new FakeDialogs();
        using var vm = new LibraryViewModel(context, dialogs);
        vm.SelectedGame = vm.Games.Single();

        dialogs.IconFiles.Enqueue(null); // Dialog abgebrochen
        vm.ChangeIconCommand.Execute(null);
        await Until(() => !vm.ChangeIconCommand.IsRunning);
        Assert.Empty(context.FakeIcons.Saved);

        dialogs.IconFiles.Enqueue(@"C:\Bilder\kaputt.png");
        context.FakeIcons.SaveFailure = new InvalidDataException("kein Bild");
        vm.ChangeIconCommand.Execute(null);
        await Until(() => !vm.ChangeIconCommand.IsRunning);

        Assert.Null(context.Config.FindGame("Steam:1")!.CustomIcon);
        Assert.Equal(LibraryStrings.Icon_NotAnImage, vm.StatusMessage);
        Assert.Equal(StatusKind.Error, vm.StatusKind);
        Assert.False(vm.ResetIconCommand.CanExecute(null));
    });

    [Fact]
    public void CustomIcon_reset_command_removes_entry_and_file() => Sta.Run(async () =>
    {
        var game = Game(GameSource.Gog, "1", "SWAT 4");
        game.CustomIcon = CustomIcons.FileNameFor(game.Key);
        var context = new TestLibraryContext(game);
        using var vm = new LibraryViewModel(context, new FakeDialogs());
        var item = vm.Games.Single();
        vm.SelectedGame = item;
        Assert.True(vm.ResetIconCommand.CanExecute(null));

        vm.ResetIconCommand.Execute(null);
        await Until(() => !vm.ResetIconCommand.IsRunning);

        Assert.Null(context.Config.FindGame(game.Key)!.CustomIcon);
        Assert.Equal([game.CustomIcon], context.FakeIcons.Deleted);
        Assert.False(item.HasCustomIcon);
        Assert.False(vm.ResetIconCommand.CanExecute(null));
        Assert.Equal(LibraryText.Format(LibraryStrings.Icon_ResetDone, "SWAT 4"), vm.StatusMessage);
    });

    [Fact]
    public void CustomIcon_menu_is_on_every_row_and_on_the_detail_icon()
    {
        WpfTestHost.Run(() =>
        {
            EnsureLibraryResources();
            var game = Game(GameSource.Steam, "1091500", "Cyberpunk 2077");
            var context = new TestLibraryContext(game);
            var model = new LibraryViewModel(context, new FakeDialogs());
            model.SelectedGame = model.Games.Single();
            var window = new MainWindow();
            window.Attach(model);
            WpfTestHost.Layout(window, new Size(1280, 800));
            WpfTestHost.DoEvents();

            // Zeilen: Kontextmenü über den Zeilenstil (ganze Fläche der Zeile)
            var list = (ListBox)window.FindName("GameList");
            var rowStyle = list.ItemContainerStyle;
            Assert.Contains(rowStyle.Setters.OfType<Setter>(), s => s.Property == ContextMenuProperty() && s.Value is ContextMenu);

            // Großes Symbol: Schaltfläche mit demselben Menü, Einträge an den Befehlen der Bibliothek
            var button = (Button)window.FindName("DetailIconButton");
            Assert.Equal("DetailIconButton", AutomationProperties.GetAutomationId(button));
            Assert.Equal(LibraryStrings.Icon_ButtonName, AutomationProperties.GetName(button));
            var menu = button.ContextMenu!;
            menu.PlacementTarget = button;
            WpfTestHost.DoEvents();
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["ChangeIconMenuItem", "ResetIconMenuItem"], items.Select(AutomationProperties.GetAutomationId));
            Assert.Equal([LibraryStrings.Icon_Change, LibraryStrings.Icon_Reset], items.Select(i => (string)i.Header));
            Assert.Same(model.ChangeIconCommand, items[0].Command);
            Assert.Same(model.ResetIconCommand, items[1].Command);
            Assert.True(items[0].IsEnabled);
            Assert.False(items[1].IsEnabled); // kein eigenes Symbol: „Symbol zurücksetzen“ gesperrt
            Assert.Equal("ChangeIconCommand", BindingOperations.GetBinding(items[0], MenuItem.CommandProperty)!.Path.Path);
            window.Close();
        });
    }

    [Fact]
    public void CustomIcon_file_can_be_injected_into_the_dialog_for_test_runs()
    {
        WpfTestHost.Run(() =>
        {
            var dialogs = new LibraryDialogs(new Window(), @"C:\Test\symbol.png");

            Assert.Equal(@"C:\Test\symbol.png", dialogs.PickIconFile(null));
            Assert.Contains("*.webp", LibraryStrings.Icon_PickerFilter, StringComparison.Ordinal);
            Assert.Contains("*.exe", LibraryStrings.Icon_PickerFilter, StringComparison.Ordinal);
        });
    }

    private static DependencyProperty ContextMenuProperty() => FrameworkElement.ContextMenuProperty;

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Befehl läuft noch");
    }

    private static void EnsureLibraryResources()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("/Themes/Library.xaml", StringComparison.Ordinal) == true))
        {
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HdrPilot;component/Themes/Library.xaml", UriKind.Absolute) });
        }
    }
}
