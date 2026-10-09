using HdrPilot.App.Setup;
using HdrPilot.Core.Config;
using Microsoft.Win32;

namespace HdrPilot.App.Tests.Setup.Engine;

/// <summary>
/// Eintrag unter „Installierte Apps“ gegen einen echten Wegwerf-Schlüssel HKCU\Software\HDRpilot.Tests\Unit\&lt;GUID&gt;
/// (nie den echten Uninstall-Zweig); der Schlüssel wird am Ende gelöscht.
/// </summary>
public sealed class SetupUninstallEntryStoreTests : IDisposable
{
    private const string ParentPath = @"Software\HDRpilot.Tests\Unit";

    private readonly string _keyPath = ParentPath + @"\" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        try
        {
            Registry.CurrentUser.DeleteSubKey(ParentPath, throwOnMissingSubKey: false); // nur, wenn leer
        }
        catch (InvalidOperationException)
        {
            // Enthält noch Schlüssel eines anderen Laufs: bleibt.
        }
    }

    [Fact]
    public void Written_entry_reads_back_with_strings_and_dword_values()
    {
        var store = new WindowsUninstallEntryStore(_keyPath);
        var entry = UninstallEntry.For(SetupLocations.ForTestRoot(@"C:\Temp\Setup Test"), new Version(1, 1, 0, 7), 5_000_000, new DateTime(2026, 10, 9));

        store.Write(entry);

        Assert.Equal(entry, store.Read());
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath)!;
        foreach (var name in new[] { "NoModify", "NoRepair", "EstimatedSize" })
        {
            Assert.Equal(RegistryValueKind.DWord, key.GetValueKind(name));
        }

        foreach (var name in new[] { "DisplayName", "DisplayVersion", "Publisher", "DisplayIcon", "InstallLocation", "UninstallString", "QuietUninstallString", "InstallDate" })
        {
            Assert.Equal(RegistryValueKind.String, key.GetValueKind(name));
        }

        Assert.Equal(1, key.GetValue("NoModify"));
        Assert.Equal(1, key.GetValue("NoRepair"));
        Assert.Equal(4883, key.GetValue("EstimatedSize"));
    }

    [Fact]
    public void Delete_removes_the_whole_key_and_reading_a_missing_key_gives_null()
    {
        var store = new WindowsUninstallEntryStore(_keyPath);
        Assert.Null(store.Read());
        store.Delete(); // fehlt: nichts passiert

        store.Write(UninstallEntry.For(SetupLocations.ForTestRoot(@"C:\Temp\Setup Test"), new Version(1, 1, 0), 1, DateTime.Today));
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath + @"\Zusatz"))
        {
            key.SetValue("Fremd", "x");
        }

        store.Delete();

        Assert.Null(Registry.CurrentUser.OpenSubKey(_keyPath));
        Assert.Null(store.Read());
    }

    [Fact]
    public void Entry_of_a_test_installation_uninstalls_inside_the_test_root()
    {
        var locations = SetupLocations.ForTestRoot(@"C:\Temp\Setup Test");

        var entry = UninstallEntry.For(locations, new Version(1, 1, 0, 3), 1025, new DateTime(2026, 10, 9));

        var exe = @"C:\Temp\Setup Test\Programs\HDRpilot\HDRpilot.exe";
        Assert.Equal("HDRpilot", entry.DisplayName);
        Assert.Equal("HDRpilot", entry.Publisher);
        Assert.Equal("1.1.0", entry.DisplayVersion);
        Assert.Equal(exe + ",0", entry.DisplayIcon);
        Assert.Equal(@"C:\Temp\Setup Test\Programs\HDRpilot", entry.InstallLocation);
        Assert.Equal("\"" + exe + "\" --uninstall --test-root \"C:\\Temp\\Setup Test\"", entry.UninstallString);
        Assert.Equal(entry.UninstallString + " --quiet", entry.QuietUninstallString);
        Assert.Equal(2, entry.EstimatedSizeKb);
        Assert.Equal("20261009", entry.InstallDate);
        Assert.True(entry.NoModify);
        Assert.True(entry.NoRepair);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(1L, 1)]
    [InlineData(1024L, 1)]
    [InlineData(1025L, 2)]
    [InlineData(80_000_000L, 78125)]
    public void Estimated_size_is_rounded_up_to_kilobytes(long bytes, int kb) =>
        Assert.Equal(kb, UninstallEntry.For(SetupLocations.ForTestRoot(@"C:\Temp\Setup Test"), new Version(1, 1, 0), bytes, DateTime.Today).EstimatedSizeKb);

    [Fact]
    public void Entry_of_the_normal_installation_has_no_test_root()
    {
        var locations = new SetupLocations(
            @"C:\Users\U\AppData\Local\Programs\HDRpilot",
            @"C:\Users\U\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            @"C:\Users\U\Desktop",
            SetupLocations.WindowsRegistryRoot,
            new AppPaths(@"C:\Users\U\AppData\Roaming\HDRpilot", @"C:\Users\U\AppData\Local\HDRpilot"),
            null);

        var entry = UninstallEntry.For(locations, new Version(1, 1, 0), 0, DateTime.Today);

        Assert.Equal("\"C:\\Users\\U\\AppData\\Local\\Programs\\HDRpilot\\HDRpilot.exe\" --uninstall", entry.UninstallString);
        Assert.Equal(entry.UninstallString + " --quiet", entry.QuietUninstallString);
    }
}
