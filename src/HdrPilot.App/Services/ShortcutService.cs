using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using HdrPilot.App.Resources;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Config;
using HdrPilot.Core.Util;

namespace HdrPilot.App.Services;

/// <summary>Inhalt einer Verknüpfung (.lnk), gelesen über IShellLinkW.</summary>
public sealed record ShortcutInfo(string TargetPath, string Arguments, string WorkingDirectory, string IconPath, int IconIndex, string Description);

/// <summary>HDRpilot-Verknüpfung eines Spiels (E90): Pfad der .lnk und die Symboldatei, die sie zeigt.</summary>
public sealed record GameShortcut(string Path, string IconPath);

/// <summary>
/// Legt Verknüpfungen „HdrPilot.exe --run "&lt;Quelle:ID&gt;"“ an: auf dem Desktop und im Startmenü unter
/// Programme\HdrPilot. Symbol ist das der Bibliothek, soweit eine Verknüpfung es zeigen kann (.ico oder .exe), sonst die
/// EXE des Spiels. Über die Windows-Shell (IShellLinkW, COM), ohne Fremdbibliothek. Findet und löscht für „Einstellungen
/// entfernen…“ (E90) nur genau solche Verknüpfungen (<see cref="FindFor"/>, <see cref="Delete"/>).
/// Die Zielordner lassen sich für Tests umlenken, ebenso Dateizugriff und Lesen der Verknüpfungen.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    public const string StartMenuFolderName = "HDRpilot";

    /// <summary>Dateiname des Ziels einer HDRpilot-Verknüpfung: Nur solche findet und löscht <see cref="FindFor"/> (E90).</summary>
    public const string ExecutableName = "HDRpilot.exe";

    private readonly string _desktopDirectory;
    private readonly string _programsDirectory;
    private readonly Func<string?> _targetPath;
    private readonly Func<GameConfig, string?> _iconPath;
    private readonly string? _extraArguments;
    private readonly IFileSystem _fileSystem;
    private readonly Func<string, ShortcutInfo> _read;
    private readonly IReadOnlyList<string> _otherShortcutDirectories;

    /// <param name="desktopDirectory">Zielordner für Desktop-Verknüpfungen (Standard: Desktop des Benutzers).</param>
    /// <param name="programsDirectory">Startmenü-Ordner „Programme“ (Standard: der des Benutzers); darin entsteht „HdrPilot“.</param>
    /// <param name="targetPath">Ziel der Verknüpfung (Standard: <see cref="Environment.ProcessPath"/>).</param>
    /// <param name="iconPath">EXE oder .ico, deren Symbol die Verknüpfung zeigt (null: Symbol von HdrPilot).</param>
    /// <param name="extraArguments">Zusätzliche Argumente nach „--run“, etwa „--config-dir“ einer Testablage.</param>
    /// <param name="fileSystem">Auflisten und Löschen für <see cref="FindFor"/> und <see cref="Delete"/> (Standard: echte Platte).</param>
    /// <param name="read">Verknüpfung lesen (Standard: <see cref="Read"/> über die Windows-Shell).</param>
    /// <param name="otherShortcutDirectories">
    /// Weitere Orte mit Verknüpfungen für <see cref="IsIconInUse"/> (Standard: gemeinsamer Desktop, gemeinsames Startmenü und
    /// „Quick Launch“ mit den angehefteten Verknüpfungen der Taskleiste).
    /// </param>
    public ShortcutService(
        string? desktopDirectory = null,
        string? programsDirectory = null,
        Func<string?>? targetPath = null,
        Func<GameConfig, string?>? iconPath = null,
        string? extraArguments = null,
        IFileSystem? fileSystem = null,
        Func<string, ShortcutInfo>? read = null,
        IReadOnlyList<string>? otherShortcutDirectories = null)
    {
        _desktopDirectory = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _programsDirectory = programsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        _targetPath = targetPath ?? (() => Environment.ProcessPath);
        _iconPath = iconPath ?? (_ => null);
        _extraArguments = string.IsNullOrWhiteSpace(extraArguments) ? null : extraArguments.Trim();
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
        _read = read ?? Read;
        _otherShortcutDirectories = otherShortcutDirectories ??
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch"),
        ];
    }

    public string CreateDesktopShortcut(GameConfig game) => Create(_desktopDirectory, game);

    public string CreateStartMenuShortcut(GameConfig game) => Create(Path.Combine(_programsDirectory, StartMenuFolderName), game);

    /// <summary>
    /// HDRpilot-Verknüpfungen eines Spiels (E90): .lnk direkt auf dem Desktop und im Startmenü-Ordner „HDRpilot“ mit Ziel
    /// <see cref="ExecutableName"/> und genau den Argumenten, die dieser Dienst schreibt (<c>--run "&lt;Schlüssel&gt;"</c>, in
    /// einer Testinstanz samt „--config-dir“). Liest nur; nicht lesbare Verknüpfungen zählen nicht. Nicht auf dem UI-Thread.
    /// </summary>
    public IReadOnlyList<GameShortcut> FindFor(string gameKey)
    {
        var found = new List<GameShortcut>();
        foreach (var directory in ShortcutDirectories())
        {
            List<string> files;
            try
            {
                files = _fileSystem.EnumerateFiles(directory, "*.lnk").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (Match(file, gameKey) is { } shortcut)
                {
                    found.Add(shortcut);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Löscht die Verknüpfungen, die beim Löschen noch genau zu diesem Spiel passen (erneut gelesen, nur in den Ordnern von
    /// <see cref="FindFor"/>); eine inzwischen geänderte oder ersetzte bleibt. Liefert die gelöschten. Andere Dateien löscht
    /// er nie, auch nicht die Symboldatei einer Verknüpfung.
    /// </summary>
    public IReadOnlyList<GameShortcut> Delete(string gameKey, IReadOnlyCollection<GameShortcut> shortcuts)
    {
        var deleted = new List<GameShortcut>();
        foreach (var shortcut in shortcuts)
        {
            var folder = Path.GetDirectoryName(shortcut.Path);
            if (folder is null || !ShortcutDirectories().Any(d => PathUtil.PathEquals(d, folder)) || Match(shortcut.Path, gameKey) is not { } current)
            {
                continue;
            }

            try
            {
                _fileSystem.DeleteFile(current.Path);
                deleted.Add(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gesperrt (etwa gerade geöffnet): bleibt liegen, der Aufrufer meldet es.
            }
        }

        return deleted;
    }

    /// <summary>
    /// Zeigt noch eine Verknüpfung diese Symboldatei? Liest (nur lesend) alle .lnk auf dem Desktop und im Startmenü samt
    /// Unterordnern und in den weiteren Orten (gemeinsamer Desktop und gemeinsames Startmenü, angeheftete Verknüpfungen der
    /// Taskleiste), egal wohin sie zeigen; nicht lesbare zählen nicht. Nicht auf dem UI-Thread.
    /// </summary>
    public bool IsIconInUse(string iconPath)
    {
        foreach (var directory in new[] { _desktopDirectory, _programsDirectory }.Concat(_otherShortcutDirectories).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            List<string> files;
            try
            {
                files = _fileSystem.EnumerateFiles(directory, "*.lnk", recursive: true).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    if (PathUtil.PathEquals(_read(file).IconPath, iconPath))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
                {
                    // Nicht lesbar: zählt nicht.
                }
            }
        }

        return false;
    }

    /// <summary>Argumente für den Start eines Spiels über HdrPilot.</summary>
    public static string ArgumentsFor(GameConfig game, string? extraArguments = null) => ArgumentsFor(game.Key, extraArguments);

    /// <inheritdoc cref="ArgumentsFor(GameConfig, string?)"/>
    public static string ArgumentsFor(string gameKey, string? extraArguments) =>
        "--run \"" + gameKey + "\"" + (string.IsNullOrWhiteSpace(extraArguments) ? string.Empty : " " + extraArguments.Trim());

    /// <summary>Dateiname ohne Endung: „Name (HDR)“, ungültige Zeichen ersetzt.</summary>
    public static string FileNameFor(GameConfig game)
    {
        var name = string.Format(System.Globalization.CultureInfo.CurrentCulture, LibraryStrings.Shortcut_FileName, game.Name.Trim());

        // Lesbar statt "_": "Dying Light: The Beast" → "Dying Light - The Beast", Schrägstriche → "-",
        // Anführungszeichen → Apostroph, sonstige unzulässige Zeichen entfallen.
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*:\s*", " - ");
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (Array.IndexOf(invalid, c) < 0)
            {
                clean.Append(c);
            }
            else if (c is '/' or '\\' or '|')
            {
                clean.Append('-');
            }
            else if (c == '"')
            {
                clean.Append('\'');
            }
        }

        var result = System.Text.RegularExpressions.Regex.Replace(clean.ToString(), @"\s{2,}", " ").Trim().TrimEnd('.');
        if (result.Length > 120)
        {
            result = result[..120].TrimEnd();
        }

        return result.Length == 0 ? game.SourceId : result;
    }

    /// <summary>Schreibt eine Verknüpfung (vorhandene gleichen Namens werden ersetzt).</summary>
    public static void Write(string shortcutPath, string targetPath, string arguments, string workingDirectory, string iconPath, int iconIndex, string description)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(targetPath);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDirectory);
            link.SetIconLocation(iconPath, iconIndex);
            link.SetDescription(description.Length > 250 ? description[..250] : description);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Liest eine Verknüpfung (Ziel, Argumente, Arbeitsordner, Symbol, Beschreibung).</summary>
    public static ShortcutInfo Read(string shortcutPath)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
            var arguments = new StringBuilder(1024);
            link.GetArguments(arguments, arguments.Capacity);
            var workingDirectory = new StringBuilder(1024);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
            var icon = new StringBuilder(1024);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            var description = new StringBuilder(1024);
            link.GetDescription(description, description.Capacity);
            // Die Shell legt Pfade teils mit Umgebungsvariablen ab (%USERPROFILE%\…): ausgeschrieben zurückgeben.
            return new ShortcutInfo(
                Environment.ExpandEnvironmentVariables(target.ToString()),
                arguments.ToString(),
                Environment.ExpandEnvironmentVariables(workingDirectory.ToString()),
                Environment.ExpandEnvironmentVariables(icon.ToString()),
                iconIndex,
                description.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Desktop und Startmenü-Ordner „HDRpilot“: nur dort legt HDRpilot Verknüpfungen an.</summary>
    private IEnumerable<string> ShortcutDirectories() => [_desktopDirectory, Path.Combine(_programsDirectory, StartMenuFolderName)];

    /// <summary>
    /// Die Verknüpfung, wenn sie eine HDRpilot-Verknüpfung genau dieses Spiels ist: Endung .lnk, Ziel <see cref="ExecutableName"/>
    /// und Argumente genau wie von <see cref="ArgumentsFor(string, string?)"/> (Groß- und Kleinschreibung egal wie beim
    /// Schlüssel); sonst oder nicht lesbar null.
    /// </summary>
    private GameShortcut? Match(string path, string gameKey)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ShortcutInfo info;
        try
        {
            info = _read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            return null;
        }

        return string.Equals(Path.GetFileName(info.TargetPath), ExecutableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(info.Arguments, ArgumentsFor(gameKey, _extraArguments), StringComparison.OrdinalIgnoreCase)
                ? new GameShortcut(path, info.IconPath)
                : null;
    }

    private string Create(string directory, GameConfig game)
    {
        var target = _targetPath();
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException(LibraryStrings.Shortcut_NoTarget);
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileNameFor(game) + ".lnk");
        var icon = _iconPath(game);
        Write(
            path,
            target,
            ArgumentsFor(game, _extraArguments),
            Path.GetDirectoryName(target) ?? string.Empty,
            string.IsNullOrWhiteSpace(icon) ? target : icon,
            0,
            string.Format(System.Globalization.CultureInfo.CurrentCulture, LibraryStrings.Shortcut_Description, game.Name));
        return path;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    /// <summary>IShellLinkW aus shobjidl_core.h (Reihenfolge der Methoden ist die der vtable).</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out ushort pwHotkey);

        void SetHotkey(ushort wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
