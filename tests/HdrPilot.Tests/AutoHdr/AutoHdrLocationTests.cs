using System.Security.Cryptography;
using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hosting;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Ort der Auto-HDR-Werte (E88): nur die Standardablage erreicht den echten Schlüssel von Windows, jede eigene Ablage
/// (Tests, Prüfskripte) landet unter HKCU\Software\HDRpilot.Tests\AutoHdr. Keiner der Tests liest oder schreibt die Registry.
/// </summary>
public sealed class AutoHdrLocationTests
{
    private static readonly AppPaths Standard = new(@"C:\Users\X\AppData\Roaming\HDRpilot", @"C:\Users\X\AppData\Local\HDRpilot");

    [Fact]
    public void Standard_storage_uses_the_real_key_and_ignores_a_test_name()
    {
        var real = AutoHdrRegistryLocation.Resolve(Standard, Standard);
        Assert.Same(AutoHdrRegistryLocation.Real, real);
        Assert.Equal(@"Software\Microsoft\DirectX\UserGpuPreferences", real.KeyPath);
        Assert.Equal(@"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences", real.KeyName);
        Assert.False(real.IsTest);

        // Ein Testname allein lenkt die echte Ablage nicht in eine Testwurzel (der Zustand gehört zum echten Schlüssel).
        Assert.Same(AutoHdrRegistryLocation.Real, AutoHdrRegistryLocation.Resolve(Standard, new AppPaths(Standard.ConfigDirectory, Standard.DataDirectory), "Phase9"));
    }

    [Fact]
    public void Own_storage_never_reaches_the_real_key()
    {
        var own = AppPaths.InDirectory(@"C:\Temp\hdrpilot-test");
        var derived = AutoHdrRegistryLocation.Resolve(own, Standard);
        Assert.True(derived.IsTest);
        Assert.StartsWith(AutoHdrRegistryLocation.TestKeyRoot + @"\", derived.KeyPath, StringComparison.Ordinal);
        Assert.NotEqual(AutoHdrRegistryLocation.Real, derived);

        // Gegenprobe: dieselbe Ablage nur anders geschrieben (Trenner am Ende, Kleinbuchstaben) ergibt denselben Ort,
        // eine andere Ablage einen anderen.
        Assert.Equal(derived, AutoHdrRegistryLocation.ForConfigDirectory(@"c:\temp\HDRPILOT-TEST\"));
        Assert.NotEqual(derived, AutoHdrRegistryLocation.ForConfigDirectory(@"C:\Temp\hdrpilot-test2"));
    }

    [Fact]
    public void Derived_name_is_the_path_hash_used_by_the_setup_test_root()
    {
        // Wie SetupLocations.ForTestRoot und Phase7Common.ps1 (PathHash): SHA-256 über UTF-8 des vollen Pfads in
        // Großbuchstaben, ohne Trenner am Ende, die ersten 16 Hex-Zeichen.
        const string Dir = @"C:\Temp\Setup-Test\Config";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Dir.ToUpperInvariant())))[..16];
        var location = AutoHdrRegistryLocation.ForConfigDirectory(Dir);
        Assert.Equal(expected, location.TestName);
        Assert.Equal(@"Software\HDRpilot.Tests\AutoHdr\" + expected, location.KeyPath);
    }

    [Theory]
    [InlineData("Phase9Ui-1a2b", true)]
    [InlineData("a", true)]
    [InlineData("A.b_c-9", true)]
    [InlineData("", false)]
    [InlineData("-start", false)]
    [InlineData(@"..\Microsoft", false)]
    [InlineData(@"x\..\..\Microsoft\DirectX", false)]
    [InlineData("x/y", false)]
    [InlineData("x y", false)]
    [InlineData("name\n", false)]
    public void Test_names_are_checked(string name, bool valid)
    {
        Assert.Equal(valid, AutoHdrRegistryLocation.IsValidTestName(name));
        var own = AppPaths.InDirectory(@"C:\Temp\hdrpilot-test");
        var resolved = AutoHdrRegistryLocation.Resolve(own, Standard, name);
        if (valid)
        {
            Assert.Equal(AutoHdrRegistryLocation.TestKeyRoot + @"\" + name, resolved.KeyPath);
        }
        else
        {
            // Ungültig: der abgeleitete Name, nie ein Ausbruch aus der Testwurzel.
            Assert.Equal(AutoHdrRegistryLocation.ForConfigDirectory(own.ConfigDirectory), resolved);
            Assert.Throws<ArgumentException>(() => AutoHdrRegistryLocation.ForTest(name));
        }
    }

    [Fact]
    public void Name_longer_than_64_characters_is_rejected()
    {
        Assert.True(AutoHdrRegistryLocation.IsValidTestName(new string('a', 64)));
        Assert.False(AutoHdrRegistryLocation.IsValidTestName(new string('a', 65)));
    }

    [Fact]
    public async Task Host_with_own_storage_uses_a_test_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "HDRpilot.Tests.AutoHdrLocation-" + Guid.NewGuid().ToString("N"));
        var paths = AppPaths.InDirectory(Path.Combine(root, "Ablage"));
        var environment = new FakeEnvironment();
        environment.Folders[Environment.SpecialFolder.ApplicationData] = Path.Combine(root, "Roaming");
        environment.Folders[Environment.SpecialFolder.LocalApplicationData] = Path.Combine(root, "Local");
        try
        {
            await using var host = new AutomationHost(paths, PhysicalFileSystem.Instance, new FakeRegistry(), environment, SystemClock.Instance, autoHdrStore: new FakeGpuPreferenceStore());
            Assert.True(host.AutoHdr.Location.IsTest);
            Assert.Equal(AutoHdrRegistryLocation.ForConfigDirectory(paths.ConfigDirectory), host.AutoHdr.Location);
            Assert.False(host.AutoHdr.HoldsGlobalSwitch);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
