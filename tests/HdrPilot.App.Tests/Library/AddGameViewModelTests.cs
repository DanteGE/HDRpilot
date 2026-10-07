using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Games;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Library;

public sealed class AddGameViewModelTests
{
    private readonly InMemoryFileSystem _files = new();
    private readonly FakeEnvironment _env = new();
    private readonly FakeDialogs _dialogs = new();

    public AddGameViewModelTests()
    {
        _env.Folders[Environment.SpecialFolder.Windows] = @"C:\Windows";
        _env.Folders[Environment.SpecialFolder.MyDocuments] = @"C:\Users\Test\Documents";
    }

    private AddGameViewModel Create(string path, string name = "")
    {
        var vm = new AddGameViewModel(_files, _env, _dialogs) { Path = path, Name = name };
        return vm;
    }

    [Fact]
    public void Exe_becomes_a_manual_entry_with_the_chosen_name()
    {
        _files.AddFile(@"D:\Spielordner\Hades II\Hades2.exe", "x");
        var vm = Create(@"D:\Spielordner\Hades II\Hades2.exe", "  Hades II  ");

        Assert.Equal("Hades2", vm.SuggestedName);
        Assert.True(vm.TryConfirm());

        var entry = vm.Result!;
        Assert.Equal(GameSource.Manual, entry.Source);
        Assert.Equal("Hades II", entry.Name);
        Assert.Equal(@"D:\Spielordner\Hades II", entry.InstallDir);
        Assert.Equal([@"D:\Spielordner\Hades II\Hades2.exe"], entry.Executables);
        Assert.Equal(@"D:\Spielordner\Hades II\Hades2.exe", entry.LaunchCommand);
        Assert.Empty(_dialogs.Confirms);
    }

    [Fact]
    public void Folder_uses_folder_name_when_no_name_is_given()
    {
        _files.AddFile(@"D:\Spielordner\Celeste\Celeste.exe", "x").AddFile(@"D:\Spielordner\Celeste\unins000.exe", "x");
        var vm = Create(@"D:\Spielordner\Celeste\");

        Assert.Equal("Celeste", vm.SuggestedName);
        Assert.Equal("Celeste", vm.NamePlaceholder);
        Assert.True(vm.TryConfirm());
        Assert.Equal("Celeste", vm.Result!.Name);
        Assert.Equal([@"D:\Spielordner\Celeste\Celeste.exe"], vm.Result.Executables);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Spiel.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Users\Test\Desktop\Spiel.exe")]
    public void Very_wide_folder_needs_confirmation(string exe)
    {
        _files.AddFile(exe, "x");
        var vm = Create(exe);

        _dialogs.Answers.Enqueue(false);
        Assert.False(vm.TryConfirm());
        Assert.Null(vm.Result);
        Assert.False(vm.HasError);
        Assert.Single(_dialogs.Confirms);
        Assert.Equal(LibraryStrings.WideFolder_Title, _dialogs.Confirms[0].Title);

        _dialogs.Answers.Enqueue(true);
        Assert.True(vm.TryConfirm());
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void Drive_root_is_refused_without_asking()
    {
        _files.AddFile(@"E:\Spiel.exe", "x");
        var vm = Create(@"E:\Spiel.exe");

        Assert.False(vm.TryConfirm());
        Assert.Equal(LibraryStrings.AddGame_ErrorDriveRoot, vm.Error);
        Assert.Empty(_dialogs.Confirms);

        _files.AddDirectory(@"F:\");
        vm.Path = @"F:\";
        Assert.False(vm.TryConfirm());
        Assert.Equal(LibraryStrings.AddGame_ErrorDriveRoot, vm.Error);
    }

    [Fact]
    public void Wrong_input_shows_a_clear_message()
    {
        _files.AddFile(@"D:\Spielordner\readme.txt", "x");
        var vm = Create(string.Empty);

        Assert.False(vm.TryConfirm());
        Assert.Equal(LibraryStrings.AddGame_ErrorNoPath, vm.Error);

        vm.Path = @"Spiele\Spiel.exe";
        Assert.False(vm.TryConfirm());
        Assert.Equal(LibraryStrings.AddGame_ErrorRelative, vm.Error);

        vm.Path = @"D:\Gibt\Es\Nicht.exe";
        Assert.False(vm.TryConfirm());
        Assert.Equal(LibraryStrings.AddGame_ErrorNotFound, vm.Error);

        vm.Path = @"D:\Spielordner\readme.txt";
        Assert.False(vm.TryConfirm());
        Assert.Equal(LibraryStrings.AddGame_ErrorNotExe, vm.Error);

        vm.Path = @"D:\Spielordner";
        Assert.Null(vm.Error); // neue Eingabe löscht die alte Meldung
    }

    [Fact]
    public void Browse_buttons_fill_the_path_and_commands_close_the_dialog()
    {
        _files.AddFile(@"D:\Spielordner\Hades\Hades.exe", "x");
        _dialogs.Executables.Enqueue(@"D:\Spielordner\Hades\Hades.exe");
        _dialogs.Folders.Enqueue(@"D:\Spielordner\Hades");
        var vm = Create(string.Empty);
        var closed = new List<bool>();
        vm.CloseRequested += (_, ok) => closed.Add(ok);

        vm.BrowseExeCommand.Execute(null);
        Assert.Equal(@"D:\Spielordner\Hades\Hades.exe", vm.Path);
        vm.BrowseFolderCommand.Execute(null);
        Assert.Equal(@"D:\Spielordner\Hades", vm.Path);
        vm.BrowseFolderCommand.Execute(null); // abgebrochen: bleibt
        Assert.Equal(@"D:\Spielordner\Hades", vm.Path);

        vm.ConfirmCommand.Execute(null);
        vm.CancelCommand.Execute(null);
        Assert.Equal([true, false], closed);
    }

    [Theory]
    [InlineData(@"D:\", FolderRisk.DriveRoot)]
    [InlineData(@"C:\Windows", FolderRisk.Wide)]
    [InlineData(@"C:\Windows\SysWOW64", FolderRisk.Wide)]
    [InlineData(@"C:\Program Files", FolderRisk.Wide)]
    [InlineData(@"C:\Program Files (x86)", FolderRisk.Wide)]
    [InlineData(@"C:\Users", FolderRisk.Wide)]
    [InlineData(@"C:\Users\Test", FolderRisk.Wide)]
    [InlineData(@"C:\Users\Test\Documents", FolderRisk.Wide)]
    [InlineData(@"C:\Program Files\Spiel", FolderRisk.Ok)]
    [InlineData(@"C:\Users\Test\Games\Spiel", FolderRisk.Ok)]
    [InlineData(@"D:\Spielordner\Hades", FolderRisk.Ok)]
    public void Folder_safety_classifies_wide_folders(string folder, FolderRisk expected) =>
        Assert.Equal(expected, FolderSafety.Classify(folder, _env));
}
