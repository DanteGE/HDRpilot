using HdrPilot.Core.Abstractions;

namespace HdrPilot.Core.Games.Scanners.Ea;

/// <summary>
/// Registry-Makro aus installerdata.xml, etwa
/// <c>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Eisenwall 6\Install Dir]SP/ew6.exe</c>:
/// Schlüssel und Wertname in eckigen Klammern, danach ein Pfad relativ zum Wert.
/// </summary>
internal readonly record struct EaRegistryMacro(RegistryHiveKind Hive, string KeyPath, string ValueName, string Rest)
{
    /// <summary>Zerlegt einen Text, der mit einem Makro beginnt. Liefert false bei Text ohne gültiges Makro.</summary>
    public static bool TryParse(string? text, out EaRegistryMacro macro)
    {
        macro = default;
        var t = text?.Trim() ?? string.Empty;
        if (!t.StartsWith('['))
        {
            return false;
        }

        var close = t.IndexOf(']', StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        var inner = t[1..close].Trim();
        var firstSeparator = inner.IndexOf('\\', StringComparison.Ordinal);
        var lastSeparator = inner.LastIndexOf('\\');
        if (firstSeparator <= 0 || lastSeparator <= firstSeparator + 1 || lastSeparator == inner.Length - 1)
        {
            return false;
        }

        RegistryHiveKind hive;
        switch (inner[..firstSeparator].ToUpperInvariant())
        {
            case "HKEY_LOCAL_MACHINE":
            case "HKLM":
                hive = RegistryHiveKind.LocalMachine;
                break;
            case "HKEY_CURRENT_USER":
            case "HKCU":
                hive = RegistryHiveKind.CurrentUser;
                break;
            default:
                return false;
        }

        macro = new EaRegistryMacro(hive, inner[(firstSeparator + 1)..lastSeparator], inner[(lastSeparator + 1)..], t[(close + 1)..].Trim());
        return true;
    }
}
