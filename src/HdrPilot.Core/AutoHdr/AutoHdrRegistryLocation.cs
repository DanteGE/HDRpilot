using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HdrPilot.Core.Config;

namespace HdrPilot.Core.AutoHdr;

/// <summary>
/// Wo HDRpilot die Auto-HDR-Werte liest und schreibt (E88): der Schlüssel von Windows
/// (HKCU\Software\Microsoft\DirectX\UserGpuPreferences) oder eine Testwurzel unter HKCU\Software\HDRpilot.Tests\AutoHdr.
/// Immer HKCU, nie HKLM. Nur die Fabrikmethoden legen Orte an, andere Schlüssel sind damit nicht erreichbar.
/// Eine Instanz mit eigener Ablage (<c>--config-dir</c>: Tests, Prüfskripte) erreicht den echten Schlüssel nie: Sie nimmt
/// den Namen aus <c>--autohdr-test-root</c>, sonst einen aus dem Ablageordner abgeleiteten.
/// </summary>
public sealed partial record AutoHdrRegistryLocation
{
    /// <summary>Schlüssel von Windows unter HKCU: je Programm ein REG_SZ, Wertname ist der volle EXE-Pfad.</summary>
    public const string RealKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>Wurzel aller Testorte unter HKCU.</summary>
    public const string TestKeyRoot = @"Software\HDRpilot.Tests\AutoHdr";

    /// <summary>Wert mit den globalen Schaltern im selben Schlüssel (etwa <c>AutoHDREnable=0;SwapEffectUpgradeEnable=1;</c>).</summary>
    public const string GlobalValueName = "DirectXUserGlobalSettings";

    private AutoHdrRegistryLocation(string keyPath, string? testName)
    {
        KeyPath = keyPath;
        TestName = testName;
    }

    /// <summary>Echter Schlüssel von Windows.</summary>
    public static AutoHdrRegistryLocation Real { get; } = new(RealKeyPath, null);

    /// <summary>Pfad unter HKCU.</summary>
    public string KeyPath { get; }

    /// <summary>Name der Testwurzel; null beim echten Schlüssel.</summary>
    public string? TestName { get; }

    public bool IsTest => TestName is not null;

    /// <summary>Vollständiger Name für Protokoll und Zustandsdateien, etwa „HKEY_CURRENT_USER\Software\…“.</summary>
    public string KeyName => @"HKEY_CURRENT_USER\" + KeyPath;

    /// <summary>Testwurzel HKCU\Software\HDRpilot.Tests\AutoHdr\&lt;name&gt;. Wirft bei ungültigem Namen.</summary>
    public static AutoHdrRegistryLocation ForTest(string name)
    {
        if (!IsValidTestName(name))
        {
            throw new ArgumentException("Ungültiger Name für die Auto-HDR-Testwurzel: " + name, nameof(name));
        }

        return new AutoHdrRegistryLocation(TestKeyRoot + @"\" + name, name);
    }

    /// <summary>
    /// Testwurzel einer Ablage: Name aus SHA-256 des vollen Pfads (ohne Trenner am Ende, in Großbuchstaben), die ersten
    /// 16 Hex-Zeichen, wie bei <c>SetupLocations.ForTestRoot</c>. So finden App und Entfernen im Prüflauf denselben Ort.
    /// </summary>
    public static AutoHdrRegistryLocation ForConfigDirectory(string configDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configDirectory));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
        return ForTest(hash);
    }

    /// <summary>Buchstaben, Ziffern, Punkt, Binde- und Unterstrich, 1 bis 64 Zeichen, Anfang Buchstabe oder Ziffer.</summary>
    public static bool IsValidTestName(string? name) => name is not null && TestNamePattern().IsMatch(name);

    /// <summary>
    /// Ort für eine Instanz: Standardablage → echter Schlüssel (ein Testname wird dann nicht beachtet); jede andere
    /// Ablage → Testwurzel mit <paramref name="testName"/>, falls gültig, sonst mit dem aus dem Ablageordner abgeleiteten Namen.
    /// </summary>
    public static AutoHdrRegistryLocation Resolve(AppPaths paths, AppPaths defaultPaths, string? testName = null)
    {
        if (paths == defaultPaths)
        {
            return Real;
        }

        return IsValidTestName(testName) ? ForTest(testName!) : ForConfigDirectory(paths.ConfigDirectory);
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TestNamePattern();
}
