using System.Reflection;
using HdrPilot.Core.AutoHdr;
using Microsoft.Win32;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// <see cref="RegistryGpuPreferenceStore"/> gegen einen eigenen Testschlüssel HKCU\Software\HDRpilot.Tests\AutoHdr\Unit-…,
/// der danach gelöscht wird. Der echte Schlüssel UserGpuPreferences wird nie geöffnet, auch nicht lesend.
/// </summary>
public sealed class AutoHdrStoreTests : IDisposable
{
    private readonly AutoHdrRegistryLocation _location = AutoHdrRegistryLocation.ForTest("Unit-" + Guid.NewGuid().ToString("N"));
    private readonly RegistryGpuPreferenceStore _store;

    public AutoHdrStoreTests()
    {
        Assert.True(_location.IsTest);
        Assert.StartsWith(AutoHdrRegistryLocation.TestKeyRoot + @"\Unit-", _location.KeyPath, StringComparison.Ordinal);
        _store = new RegistryGpuPreferenceStore(_location);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_location.KeyPath, throwOnMissingSubKey: false);

    [Fact]
    public void Missing_key_and_missing_value_read_as_missing_and_delete_is_no_error()
    {
        Assert.Equal(GpuPreferenceValue.Missing, _store.Read(@"C:\Spiel\Spiel.exe"));
        _store.Delete(@"C:\Spiel\Spiel.exe"); // Schlüssel fehlt
        Assert.Null(Registry.CurrentUser.OpenSubKey(_location.KeyPath));

        _store.WriteString("Anderer", "A=1;");
        _store.Delete(@"C:\Spiel\Spiel.exe"); // Wert fehlt
        Assert.Equal(GpuPreferenceValue.Missing, _store.Read(@"C:\Spiel\Spiel.exe"));
    }

    [Fact]
    public void Strings_round_trip_and_value_names_ignore_case()
    {
        const string name = AutoHdrRegistryLocation.GlobalValueName;

        _store.WriteString(name, "AutoHDREnable=1;SwapEffectUpgradeEnable=1;");

        Assert.Equal(GpuPreferenceValue.FromString("AutoHDREnable=1;SwapEffectUpgradeEnable=1;"), _store.Read(name));
        Assert.Equal(GpuPreferenceValue.FromString("AutoHDREnable=1;SwapEffectUpgradeEnable=1;"), _store.Read(name.ToUpperInvariant()));
        using (var key = Registry.CurrentUser.OpenSubKey(_location.KeyPath)!)
        {
            Assert.Equal(RegistryValueKind.String, key.GetValueKind(name));
        }

        _store.Delete(name);
        Assert.True(_store.Read(name).IsMissing);
    }

    [Fact]
    public void Other_kinds_are_reported_with_their_kind_and_unexpanded_text()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_location.KeyPath))
        {
            key.SetValue("GraphicsFeaturesNotificationConfig", 1, RegistryValueKind.DWord);
            key.SetValue("Erweitert", @"%TEMP%\x.exe", RegistryValueKind.ExpandString);
            key.SetValue("Mehrfach", new[] { "a", "b" }, RegistryValueKind.MultiString);
            key.SetValue("Binär", new byte[] { 1, 2 }, RegistryValueKind.Binary);
        }

        Assert.Equal(new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "1"), _store.Read("GraphicsFeaturesNotificationConfig"));
        Assert.Equal(new GpuPreferenceValue(GpuPreferenceValueKind.ExpandString, @"%TEMP%\x.exe"), _store.Read("Erweitert"));
        Assert.Equal(GpuPreferenceValueKind.MultiString, _store.Read("Mehrfach").Kind);
        Assert.Equal(GpuPreferenceValueKind.Binary, _store.Read("Binär").Kind);
        Assert.False(GpuPreferenceText.IsClean(_store.Read("Erweitert")));
    }

    [Fact]
    public void Embedded_nul_is_kept_on_read_and_refused_on_write()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_location.KeyPath))
        {
            key.SetValue("LosslessScaling.exe", "GpuPreference=0;\0xy", RegistryValueKind.String);
        }

        var value = _store.Read("LosslessScaling.exe");
        Assert.Equal(GpuPreferenceValueKind.String, value.Kind);
        Assert.Contains('\0', value.Text!);
        Assert.False(GpuPreferenceText.IsClean(value));

        Assert.Throws<ArgumentException>(() => _store.WriteString("LosslessScaling.exe", "A=1;\0"));
        Assert.Equal(value, _store.Read("LosslessScaling.exe"));
    }

    [Fact]
    public void Locations_exist_only_through_their_factories()
    {
        // Gegenprobe: kein öffentlicher Konstruktor, also kein Ort außerhalb des echten Schlüssels und der Testwurzel.
        Assert.Empty(typeof(AutoHdrRegistryLocation).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(AutoHdrRegistryLocation.RealKeyPath, AutoHdrRegistryLocation.Real.KeyPath);
        Assert.Throws<ArgumentException>(() => AutoHdrRegistryLocation.ForTest(@"..\..\Microsoft\DirectX\UserGpuPreferences"));
        Assert.Throws<ArgumentException>(() => AutoHdrRegistryLocation.ForTest(""));
        Assert.Equal(_location.KeyName, _store.KeyName);
        Assert.StartsWith(@"HKEY_CURRENT_USER\Software\HDRpilot.Tests\AutoHdr\", _store.KeyName, StringComparison.Ordinal);
    }
}
