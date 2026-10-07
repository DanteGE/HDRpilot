using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using HdrPilot.App.Resources;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Services;

/// <summary>Inhalt einer Verknüpfung (.lnk), gelesen über IShellLinkW.</summary>
public sealed record ShortcutInfo(string TargetPath, string Arguments, string WorkingDirectory, string IconPath, int IconIndex, string Description);

/// <summary>
/// Legt Verknüpfungen „HdrPilot.exe --run "&lt;Quelle:ID&gt;"“ an: auf dem Desktop und im Startmenü unter
/// Programme\HdrPilot. Symbol ist die EXE des Spiels. Über die Windows-Shell (IShellLinkW, COM), ohne Fremdbibliothek.
/// Die Zielordner lassen sich für Tests umlenken.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    public const string StartMenuFolderName = "HDRpilot";

    private readonly string _desktopDirectory;
    private readonly string _programsDirectory;
    private readonly Func<string?> _targetPath;
    private readonly Func<GameConfig, string?> _iconPath;
    private readonly string? _extraArguments;

    /// <param name="desktopDirectory">Zielordner für Desktop-Verknüpfungen (Standard: Desktop des Benutzers).</param>
    /// <param name="programsDirectory">Startmenü-Ordner „Programme“ (Standard: der des Benutzers); darin entsteht „HdrPilot“.</param>
    /// <param name="targetPath">Ziel der Verknüpfung (Standard: <see cref="Environment.ProcessPath"/>).</param>
    /// <param name="iconPath">EXE, deren Symbol die Verknüpfung zeigt (null: Symbol von HdrPilot).</param>
    /// <param name="extraArguments">Zusätzliche Argumente nach „--run“, etwa „--config-dir“ einer Testablage.</param>
    public ShortcutService(
        string? desktopDirectory = null,
        string? programsDirectory = null,
        Func<string?>? targetPath = null,
        Func<GameConfig, string?>? iconPath = null,
        string? extraArguments = null)
    {
        _desktopDirectory = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _programsDirectory = programsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        _targetPath = targetPath ?? (() => Environment.ProcessPath);
        _iconPath = iconPath ?? (_ => null);
        _extraArguments = string.IsNullOrWhiteSpace(extraArguments) ? null : extraArguments.Trim();
    }

    public string CreateDesktopShortcut(GameConfig game) => Create(_desktopDirectory, game);

    public string CreateStartMenuShortcut(GameConfig game) => Create(Path.Combine(_programsDirectory, StartMenuFolderName), game);

    /// <summary>Argumente für den Start eines Spiels über HdrPilot.</summary>
    public static string ArgumentsFor(GameConfig game, string? extraArguments = null) =>
        "--run \"" + game.Key + "\"" + (string.IsNullOrWhiteSpace(extraArguments) ? string.Empty : " " + extraArguments.Trim());

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
