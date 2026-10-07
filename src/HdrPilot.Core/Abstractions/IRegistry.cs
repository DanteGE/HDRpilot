using Microsoft.Win32;

namespace HdrPilot.Core.Abstractions;

public enum RegistryHiveKind
{
    CurrentUser,
    LocalMachine,
}

public enum RegistryViewKind
{
    Default,
    Registry32,
    Registry64,
}

/// <summary>Registry-Zugriff hinter einer Schnittstelle, damit Scanner mit einer Attrappe testbar sind.</summary>
public interface IRegistry
{
    /// <summary>Öffnet einen Schlüssel nur lesend. Liefert null, wenn er fehlt oder nicht lesbar ist.</summary>
    IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default);

    /// <summary>Öffnet oder erzeugt einen Schlüssel schreibbar (nur für HKCU gedacht, etwa den Run-Schlüssel).</summary>
    IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath);
}

public interface IRegistryKey : IDisposable
{
    /// <summary>Vollständiger Name für Protokolle, etwa "HKEY_CURRENT_USER\Software\Valve\Steam".</summary>
    string Name { get; }

    IReadOnlyList<string> GetSubKeyNames();

    IReadOnlyList<string> GetValueNames();

    /// <summary>Rohwert (string, int, long, byte[], string[]) oder null, wenn er fehlt.</summary>
    object? GetValue(string name);

    /// <summary>Öffnet einen Unterschlüssel mit denselben Rechten. Liefert null, wenn er fehlt.</summary>
    IRegistryKey? OpenSubKey(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

public static class RegistryKeyExtensions
{
    public static string? GetString(this IRegistryKey key, string name) => key.GetValue(name) switch
    {
        string s => s,
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };
}

public sealed class WindowsRegistry : IRegistry
{
    public static WindowsRegistry Instance { get; } = new();

    public IRegistryKey? OpenKey(RegistryHiveKind hive, string subKeyPath, RegistryViewKind view = RegistryViewKind.Default)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(ToHive(hive), ToView(view));
            var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            return key is null ? null : new WindowsRegistryKey(key);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public IRegistryKey CreateKey(RegistryHiveKind hive, string subKeyPath)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(hive), RegistryView.Default);
        var key = baseKey.CreateSubKey(subKeyPath, writable: true)
            ?? throw new IOException("Registry-Schlüssel ließ sich nicht anlegen: " + subKeyPath);
        return new WindowsRegistryKey(key);
    }

    private static RegistryHive ToHive(RegistryHiveKind hive) => hive switch
    {
        RegistryHiveKind.CurrentUser => RegistryHive.CurrentUser,
        RegistryHiveKind.LocalMachine => RegistryHive.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(hive)),
    };

    private static RegistryView ToView(RegistryViewKind view) => view switch
    {
        RegistryViewKind.Default => RegistryView.Default,
        RegistryViewKind.Registry32 => RegistryView.Registry32,
        RegistryViewKind.Registry64 => RegistryView.Registry64,
        _ => throw new ArgumentOutOfRangeException(nameof(view)),
    };

    private sealed class WindowsRegistryKey(RegistryKey key) : IRegistryKey
    {
        public string Name => key.Name;

        public IReadOnlyList<string> GetSubKeyNames() => Guard(key.GetSubKeyNames, []);

        public IReadOnlyList<string> GetValueNames() => Guard(key.GetValueNames, []);

        public object? GetValue(string name) => Guard(() => key.GetValue(name), null);

        public IRegistryKey? OpenSubKey(string name)
        {
            var sub = Guard(() => key.OpenSubKey(name, writable: false), null);
            return sub is null ? null : new WindowsRegistryKey(sub);
        }

        public void SetValue(string name, string value) => key.SetValue(name, value, RegistryValueKind.String);

        public void DeleteValue(string name) => key.DeleteValue(name, throwOnMissingValue: false);

        public void Dispose() => key.Dispose();

        private static T Guard<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (System.Security.SecurityException)
            {
                return fallback;
            }
            catch (UnauthorizedAccessException)
            {
                return fallback;
            }
            catch (IOException)
            {
                return fallback;
            }
        }
    }
}
