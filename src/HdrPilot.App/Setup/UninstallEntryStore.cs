using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace HdrPilot.App.Setup;

/// <summary>Eintrag unter „Installierte Apps“ (HKCU\…\Uninstall\HDRpilot).</summary>
/// <param name="EstimatedSizeKb">Größe der installierten Dateien in KB (REG_DWORD).</param>
/// <param name="InstallDate">Installationsdatum yyyyMMdd.</param>
public sealed record UninstallEntry(
    string DisplayName,
    string DisplayVersion,
    string Publisher,
    string DisplayIcon,
    string InstallLocation,
    string UninstallString,
    string QuietUninstallString,
    int EstimatedSizeKb,
    string InstallDate,
    bool NoModify = true,
    bool NoRepair = true)
{
    /// <summary>Eintrag für diese Installation; im Prüflauf mit <c>--test-root</c>, damit das Entfernen dort bleibt.</summary>
    public static UninstallEntry For(SetupLocations locations, Version version, long installedBytes, DateTime installDate)
    {
        var exe = locations.ExePath;
        var command = "\"" + exe + "\" --uninstall";
        if (SetupSupport.TestRootOf(locations) is { } root)
        {
            command += " --test-root \"" + root + "\"";
        }

        return new UninstallEntry(
            SetupSupport.ProductName,
            SetupSupport.Normalize(version).ToString(3),
            SetupSupport.ProductName,
            exe + ",0",
            locations.InstallDirectory,
            command,
            command + " --quiet",
            (int)Math.Min(int.MaxValue, (Math.Max(0, installedBytes) + 1023) / 1024),
            installDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
    }
}

/// <summary>Liest, schreibt und löscht den Eintrag unter „Installierte Apps“.</summary>
public interface IUninstallEntryStore
{
    /// <summary>Der Eintrag oder null, wenn der Schlüssel fehlt oder nicht lesbar ist.</summary>
    UninstallEntry? Read();

    void Write(UninstallEntry entry);

    /// <summary>Entfernt den ganzen Schlüssel (fehlt er, passiert nichts).</summary>
    void Delete();
}

/// <summary>
/// Eintrag in HKCU (Schlüssel <see cref="SetupLocations.UninstallKeyPath"/>). Direkt über Microsoft.Win32.Registry, weil
/// <c>IRegistry</c> nur Zeichenketten schreibt: NoModify, NoRepair und EstimatedSize sind REG_DWORD.
/// </summary>
public sealed class WindowsUninstallEntryStore(string keyPath) : IUninstallEntryStore
{
    public string KeyPath => keyPath;

    public UninstallEntry? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            if (key is null)
            {
                return null;
            }

            string Text(string name) => key.GetValue(name) as string ?? string.Empty;
            int Number(string name) => key.GetValue(name) is int value ? value : 0;
            return new UninstallEntry(
                Text("DisplayName"),
                Text("DisplayVersion"),
                Text("Publisher"),
                Text("DisplayIcon"),
                Text("InstallLocation"),
                Text("UninstallString"),
                Text("QuietUninstallString"),
                Number("EstimatedSize"),
                Text("InstallDate"),
                Number("NoModify") == 1,
                Number("NoRepair") == 1);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public void Write(UninstallEntry entry)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new IOException("Registry-Schlüssel ließ sich nicht anlegen: " + keyPath);
        key.SetValue("DisplayName", entry.DisplayName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", entry.DisplayVersion, RegistryValueKind.String);
        key.SetValue("Publisher", entry.Publisher, RegistryValueKind.String);
        key.SetValue("DisplayIcon", entry.DisplayIcon, RegistryValueKind.String);
        key.SetValue("InstallLocation", entry.InstallLocation, RegistryValueKind.String);
        key.SetValue("UninstallString", entry.UninstallString, RegistryValueKind.String);
        key.SetValue("QuietUninstallString", entry.QuietUninstallString, RegistryValueKind.String);
        key.SetValue("NoModify", entry.NoModify ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("NoRepair", entry.NoRepair ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", entry.EstimatedSizeKb, RegistryValueKind.DWord);
        key.SetValue("InstallDate", entry.InstallDate, RegistryValueKind.String);
    }

    public void Delete() => Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}
