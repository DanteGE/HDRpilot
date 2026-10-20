using System.Security.AccessControl;
using System.Security.Principal;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Verweigert dem eigenen Konto Rechte an Ordnern oder Dateien im Temp-Ordner eines Tests, wie bei einem fremden Systemordner
/// (etwa <c>C:\System Volume Information</c>): Windows meldet dann „Zugriff verweigert“. Nur für selbst angelegte Testdateien;
/// <see cref="Dispose"/> nimmt die Verweigerung zurück, damit der Test aufräumen kann (als Besitzer darf das Konto die Rechte
/// immer ändern).
/// </summary>
public sealed class AccessDenial : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("Kein Benutzerkonto.");

    private readonly List<(string Path, bool IsDirectory, FileSystemRights Rights)> _denied = [];

    /// <summary>Den Inhalt des Ordners nicht auflisten; dass es ihn gibt, sieht man weiter (wie System Volume Information).</summary>
    public AccessDenial DenyListing(string directory)
    {
        Assert.True(Directory.Exists(directory), "Kein Ordner: " + directory);
        return Deny(directory, isDirectory: true, FileSystemRights.ListDirectory);
    }

    /// <summary>
    /// Von der Datei oder dem Ordner nicht einmal die Attribute lesen. Zusammen mit <see cref="DenyListing"/> des Ordners
    /// darüber verweigert Windows jede Auskunft, auch ob es sie gibt (die Attribute gibt sonst das Auflisten preis).
    /// </summary>
    public AccessDenial DenyAttributes(string path)
    {
        var isDirectory = Directory.Exists(path);
        Assert.True(isDirectory || File.Exists(path), "Nicht da: " + path);
        return Deny(path, isDirectory, FileSystemRights.ReadAttributes | FileSystemRights.ReadData);
    }

    public void Dispose()
    {
        for (var i = _denied.Count - 1; i >= 0; i--)
        {
            var (path, isDirectory, rights) = _denied[i];
            Change(path, isDirectory, security => security.RemoveAccessRuleAll(new FileSystemAccessRule(Me, rights, AccessControlType.Deny)));
        }

        _denied.Clear();
    }

    private AccessDenial Deny(string path, bool isDirectory, FileSystemRights rights)
    {
        Change(path, isDirectory, security => security.AddAccessRule(new FileSystemAccessRule(Me, rights, AccessControlType.Deny)));
        _denied.Add((path, isDirectory, rights));
        return this;
    }

    private static void Change(string path, bool isDirectory, Action<FileSystemSecurity> change)
    {
        if (isDirectory)
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            change(security);
            directory.SetAccessControl(security);
            return;
        }

        var file = new FileInfo(path);
        var fileSecurity = file.GetAccessControl();
        change(fileSecurity);
        file.SetAccessControl(fileSecurity);
    }
}
