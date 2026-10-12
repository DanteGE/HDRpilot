using System.Runtime.InteropServices;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

public sealed class SteamLaunchOptionTests
{
    [Theory]
    [InlineData(@"C:\Program Files\HdrPilot\HdrPilot.exe", "\"C:\\Program Files\\HdrPilot\\HdrPilot.exe\" --wrap -- %command%")]
    [InlineData(@"D:\Tools\HdrPilot.exe", "\"D:\\Tools\\HdrPilot.exe\" --wrap -- %command%")]
    public void Build_quotes_the_program_path_and_appends_the_wrap_arguments(string path, string expected)
    {
        Assert.Equal(expected, SteamLaunchOption.Build(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_refuses_an_empty_path(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => SteamLaunchOption.Build(path));
    }

    [Fact]
    public void Build_refuses_null()
    {
        Assert.ThrowsAny<ArgumentException>(() => SteamLaunchOption.Build(null!));
    }

    [Fact]
    public void Steam_game_copies_the_launch_option_and_explains_where_to_paste_it() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1091500", "Cyberpunk 2077", isNew: false));
        context.ExecutablePath = @"E:\Apps\Hdr Pilot\HdrPilot.exe";
        using var library = new LibraryViewModel(context, new FakeDialogs());
        library.SelectedGame = library.Games.Single();
        var details = library.Details!;
        var updates = context.Updates;

        Assert.True(details.IsSteam);
        Assert.True(details.CopySteamLaunchOptionCommand.CanExecute(null));
        details.CopySteamLaunchOptionCommand.Execute(null);

        Assert.Equal(["\"E:\\Apps\\Hdr Pilot\\HdrPilot.exe\" --wrap -- %command%"], context.FakeClipboard.Texts);
        Assert.Equal(StatusKind.Success, details.MessageKind);
        Assert.Equal(LibraryStrings.Steam_LaunchOptionCopied, details.Message);
        Assert.Contains("Startoptionen", details.Message, StringComparison.Ordinal);
        Assert.True(details.HasLaunchMessage);
        Assert.False(details.HasExecutablesMessage);
        Assert.Equal(updates, context.Updates); // nichts in der Konfiguration geändert
    });

    [Theory]
    [InlineData(GameSource.Epic)]
    [InlineData(GameSource.Gog)]
    [InlineData(GameSource.Manual)]
    public void Other_sources_do_not_offer_the_steam_launch_option(GameSource source) => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Game(source, "x1", "Anderes Spiel", isNew: false));
        using var library = new LibraryViewModel(context, new FakeDialogs());
        library.SelectedGame = library.Games.Single();
        var details = library.Details!;

        Assert.False(details.IsSteam);
        Assert.False(details.CopySteamLaunchOptionCommand.CanExecute(null));
        details.CopySteamLaunchOptionCommand.Execute(null);
        Assert.Empty(context.FakeClipboard.Texts);
        Assert.False(details.HasMessage);
    });

    [Fact]
    public void Busy_clipboard_is_reported_not_thrown() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Hades II", isNew: false));
        context.FakeClipboard.Failure = new COMException("OpenClipboard fehlgeschlagen", unchecked((int)0x800401D0));
        using var library = new LibraryViewModel(context, new FakeDialogs());
        library.SelectedGame = library.Games.Single();
        var details = library.Details!;

        details.CopySteamLaunchOptionCommand.Execute(null);

        Assert.Equal(StatusKind.Error, details.MessageKind);
        Assert.Equal(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, "OpenClipboard fehlgeschlagen"), details.Message);
        Assert.True(details.HasLaunchMessage);
    });

    [Fact]
    public void Unknown_program_path_is_reported_and_nothing_is_copied() => Sta.Run(() =>
    {
        var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Hades II", isNew: false)) { ExecutablePath = null };
        using var library = new LibraryViewModel(context, new FakeDialogs());
        library.SelectedGame = library.Games.Single();
        var details = library.Details!;

        details.CopySteamLaunchOptionCommand.Execute(null);

        Assert.Empty(context.FakeClipboard.Texts);
        Assert.Equal(StatusKind.Error, details.MessageKind);
        Assert.Equal(LibraryText.Format(LibraryStrings.Steam_LaunchOptionFailed, LibraryStrings.Shortcut_NoTarget), details.Message);
    });

    [Fact]
    public void Messages_appear_next_to_the_action_that_caused_them() => Sta.Run(async () =>
    {
        var dialogs = new FakeDialogs();
        dialogs.Executables.Enqueue(@"E:\Mods\Mod.exe");
        var context = new TestLibraryContext(Game(GameSource.Steam, "1", "Hades II", isNew: false));
        using var library = new LibraryViewModel(context, dialogs);
        library.SelectedGame = library.Games.Single();
        var details = library.Details!;
        var changed = new List<string?>();
        details.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        details.AddExeCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(DetailMessageArea.Executables, details.MessageArea);
        Assert.True(details.HasExecutablesMessage);
        Assert.False(details.HasLaunchMessage);

        changed.Clear();
        details.CreateDesktopShortcutCommand.Execute(null);
        await Sta.Finished(details.CreateDesktopShortcutCommand);
        Assert.Equal(DetailMessageArea.Launch, details.MessageArea);
        Assert.True(details.HasLaunchMessage);
        Assert.False(details.HasExecutablesMessage);
        Assert.Contains(nameof(GameDetailsViewModel.HasLaunchMessage), changed);
        Assert.Contains(nameof(GameDetailsViewModel.HasExecutablesMessage), changed);

        details.LaunchCommand.Execute(null);
        await Task.Yield();
        Assert.True(details.HasLaunchMessage);
    });
}
