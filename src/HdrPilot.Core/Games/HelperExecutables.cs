namespace HdrPilot.Core.Games;

/// <summary>
/// Pflegbare Ausschlussliste für Hilfsprozesse in Spielordnern: Absturzmelder, Anti-Cheat-Installer und -Dienste,
/// Redistributables und Installer, reine Launcher-Fenster und Browser-Unterprozesse. Ein Prozess unter dem
/// Installationsordner eines Spiels gilt nur dann als Spiel, wenn er nicht auf diese Liste passt.
/// <para>
/// Muster sind Globs über den Dateinamen (<c>*</c> beliebig viele Zeichen, <c>?</c> genau ein Zeichen), ohne
/// Groß-/Kleinschreibung. Die Endung „.exe“ ist bei Name und Muster gleichgültig: „dxsetup“ passt auf „DXSETUP.exe“.
/// Muster mit Ordnertrennern passen nie, weil nur Dateinamen verglichen werden.
/// </para>
/// <para>
/// Bewusst nicht enthalten sind Startprogramme, die das Spiel selbst starten (gamelaunchhelper.exe,
/// start_protected_game.exe, *EAC.exe, PlayRDR2.exe): Sie sind ein früher Start-Hinweis für HDR.
/// Abgleich mit allen EXE-Dateien in den Spielordnern am Zielrechner: docs/scanner/hilfsprozesse.md.
/// </para>
/// </summary>
public static class HelperExecutables
{
    public static IReadOnlyList<string> DefaultPatterns { get; } =
    [
        // Absturzmelder
        "CrashReportClient",
        "CrashReport*",
        "*CrashReporter*",
        "crash_reporter*",
        "UnityCrashHandler*",
        "CrashHandler*",
        "*CrashHandler*",
        "crashpad_handler",
        "BsSndRpt*",
        "*CrashDumper*",
        "CrashSender*",
        "CrashUploader*",
        "*CrashMessage*",
        "*ErrorReporter*",
        "BugReportClient*",
        "createdump",
        "*minidump*",
        "sentry-cli",
        "Watchdog",

        // Anti-Cheat-Installer und -Dienste
        "EasyAntiCheat*Setup*",
        "EasyAntiCheat_EOS_Setup",
        "BEService*",
        "*AntiCheat*Installer*",
        "GGSetup*",
        "Elytra-Setup*",
        "pbsvc*",

        // Redistributables und Installer
        "vc_redist*",
        "vcredist*",
        "vc8redist*",
        "dxsetup",
        "dxwebsetup",
        "dotnetfx*",
        "NDP4*",
        "PhysX*SystemSoftware*",
        "*prereq*",
        "UE4PrereqSetup*",
        "UEPrereqSetup*",
        "UE3Redist*",
        "UnSetup*",
        "oalinst",
        "*WebView2Setup*",
        "VulkanRT*",
        "QuickTimeInstall*",
        "ArcadeInstall*",
        "EpicOnlineServicesInstaller*",
        "CertsUpdater*",
        "UbisoftConnectInstaller*",
        "UplayInstaller*",
        "Social-Club-Setup*",
        "Rockstar-Games-*",
        "InstallerMessage*",
        "RKSetup*",
        "Touchup",
        "Cleanup",
        "unins*",
        "*uninstall*",
        "*uninst*",

        // Reine Launcher-Fenster (am Zielrechner in Spielordnern gefunden)
        "REDprelauncher",
        "LariLauncher",
        "DriverVersionChecker",
        "LayersChecker",
        "TaleWorlds.MountAndBlade.Launcher",
        "launcher",

        // Browser-Unterprozesse, die nur neben dem Spiel laufen
        "EpicWebHelper",
        "UnrealCEFSubProcess",
        "CefSharp.BrowserSubprocess",
    ];

    /// <summary>
    /// True, wenn der Dateiname (oder der Dateiname am Ende eines Pfads) auf eines der Muster passt.
    /// Ohne <paramref name="patterns"/> gilt <see cref="DefaultPatterns"/>. Wirft nicht.
    /// </summary>
    public static bool IsHelper(string exeFileNameOrPath, IEnumerable<string>? patterns = null)
    {
        var name = FileStem(exeFileNameOrPath);
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var raw in patterns ?? DefaultPatterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var pattern = raw.Trim();
            if (pattern.Contains('\\', StringComparison.Ordinal) || pattern.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            if (GlobMatch(name, StripExe(pattern)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Glob-Vergleich mit * und ?, ohne Groß-/Kleinschreibung (invariant).</summary>
    internal static bool GlobMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        int t = 0, p = 0, starP = -1, starT = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] != '*' && (pattern[p] == '?' || SameChar(pattern[p], text[t])))
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool SameChar(char a, char b) => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    private static string FileStem(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return string.Empty;
        }

        var s = fileNameOrPath.Trim().Trim('"');
        var slash = s.LastIndexOfAny(['\\', '/']);
        return StripExe(slash >= 0 ? s[(slash + 1)..] : s);
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
