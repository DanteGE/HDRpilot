using HdrPilot.Core.Util;

namespace HdrPilot.Core.Games;

/// <summary>
/// Ausschlussliste nur für Symbole (E86): EXEs, die nie das Symbol eines Spiels liefern, wohl aber als Spielprozess
/// zählen können. Deshalb stehen sie nicht in <see cref="HelperExecutables"/>, wo etwa start_protected_game.exe bewusst
/// als früher Start-Hinweis für HDR fehlt. Gemessen an der Bibliothek des Nutzers (647 Spiele): Diese EXEs lieferten das
/// Symbol des Anti-Cheats oder eines Werkzeugs statt des Spielsymbols (etwa bei Gears of War: E-Day und The Isle).
/// <list type="bullet">
/// <item>Anti-Cheat: EasyAntiCheat (start_protected_game, *EasyAntiCheat*, EAC als eigenes Wortteil wie in GZWClientEAC
/// oder EACLaunch, nicht in „BeachInvasion“), BattlEye (*_BE, BEService*, BELauncher, *BattlEye*), EA Anti-Cheat
/// (EAAntiCheat.*), GameGuard/nProtect (GameMon*, GameGuard*, *nProtect*, GGSetup, gguninst), PunkBuster (pbsvc*,
/// PnkBstr*), alles mit „AntiCheat“ im Namen.</item>
/// <item>Epic Online Services (EOSBootstrapper, EpicOnlineServices*, EOSOverlayRenderer*) und Starter mit fremdem Symbol
/// (LaunchGame, GodotWorkshopUtility).</item>
/// <item>Absturzmelder, Redistributables, Installer und Deinstaller (CrashReportClient, UnityCrashHandler*, BsSndRpt*,
/// UE*PrereqSetup*, vc_redist*, PhysX, dxsetup, Installer von Ubisoft Connect und Rockstar, unins* …) und
/// Browser-Unterprozesse; auch die aus der Standardliste der Hilfsprozesse, weil eine geänderte Liste sie ganz ersetzt.</item>
/// <item>Werkzeuge: P4PopulateDepot, elementviewer, *editor*, java/javaw, vconsole2, qc_eyes, die Werkzeuge von Unreal,
/// die UDK-Spiele in Binaries mitbringen (UnrealFrontend, ShaderKeyTool, StatsViewer …; gemessen: sonst bekamen Homesick
/// und Viscera Cleanup Detail: Shadow Warrior deren Symbol statt des Steam-Symbols), und die des Source SDK in bin
/// (hlfaceposer, hlmv, studiomdl, hammer …; gemessen an Portal).</item>
/// <item>Alles in Ordnern des Anti-Cheats oder der Redistributables unter dem Spielordner (BattlEye, GameGuard,
/// EasyAntiCheat, _CommonRedist, __Installer, Redist).</item>
/// </list>
/// Muster sind Globs über den Dateinamen ohne Endung, ohne Groß-/Kleinschreibung (wie bei <see cref="HelperExecutables"/>).
/// </summary>
public static class IconExcludedExecutables
{
    public static IReadOnlyList<string> Patterns { get; } =
    [
        // EasyAntiCheat
        "start_protected_game",
        "*EasyAntiCheat*",

        // BattlEye
        "*_BE",
        "BEService*",
        "BELauncher",
        "*BattlEye*",

        // EA Anti-Cheat und andere
        "EAAntiCheat*",
        "*AntiCheat*",

        // GameGuard / nProtect (samt Installer GGSetup und Deinstaller gguninst)
        "GameMon*",
        "GameGuard*",
        "*nProtect*",
        "GGSetup*",

        // PunkBuster
        "pbsvc*",
        "PnkBstr*",
        "*PunkBuster*",

        // Epic Online Services und Starter mit fremdem Symbol
        "EOSBootstrapper*",
        "EpicOnlineServices*",
        "EOSOverlayRenderer*",
        "LaunchGame",
        "GodotWorkshopUtility",

        // Absturzmelder, Redistributables, Installer, Browser-Unterprozesse. Doppelt zu HelperExecutables, weil eine in den
        // Einstellungen geänderte Liste der Hilfsprozesse die Standardliste ganz ersetzt.
        "CrashReport*",
        "UnityCrashHandler*",
        "*CrashHandler*",
        "*CrashReporter*",
        "crash_reporter*",
        "*CrashDumper*",
        "CrashSender*",
        "CrashUploader*",
        "crashpad_handler",
        "BsSndRpt*",
        "BugReportClient*",
        "*ErrorReporter*",
        "createdump",
        "*minidump*",
        "sentry-cli",
        "UE*PrereqSetup*",
        "*prereq*",
        "vc_redist*",
        "vcredist*",
        "*redist*",
        "dxsetup",
        "dxwebsetup",
        "dotnetfx*",
        "NDP4*",
        "PhysX*SystemSoftware*",
        "oalinst",
        "VulkanRT*",
        "*WebView2Setup*",
        "QuickTimeInstall*",
        "UplayInstaller*",
        "UbisoftConnectInstaller*",
        "Social-Club-Setup*",
        "Rockstar-Games-Launcher*",
        "Elytra-Setup*",
        "unins*",
        "*uninst*",
        "UnrealCEFSubProcess",
        "EpicWebHelper",
        "CefSharp.BrowserSubprocess",
        "QtWebEngineProcess",

        // Werkzeuge (Unreal-Werkzeuge einzeln: „Unreal*“ träfe auch Unreal.exe und UnrealTournament.exe)
        "P4PopulateDepot",
        "elementviewer",
        "*editor*",
        "java",
        "javaw",
        "vconsole2",
        "qc_eyes",
        "UnrealFrontend",
        "ShaderKeyTool",
        "*ShaderCompileWorker",
        "UnrealLightmass",
        "UnrealPak",
        "UnrealInsights",
        "UnrealVersionSelector*",
        "UnrealCommand",
        "UnrealConsole",
        "UnrealLoc",
        "UnSetup",
        "RKSetup",
        "UDKLift",
        "CookerSync",
        "GameplayProfiler",
        "NetworkProfiler",
        "MemLeakCheckDiffer",
        "MobileShaderAnalyzer",
        "PackageDiffFrontEnd",
        "RPCUtility",
        "StatsViewer",
        "StatsComparison",
        "StatsThreshold",
        "SwarmAgent",
        "SwarmCoordinator",

        // Werkzeuge des Source SDK in bin von Source-Spielen (gemessen: Portal; sonst gewann die größte EXE hlfaceposer)
        "bspzip",
        "captioncompiler",
        "demoinfo",
        "dmxconvert",
        "dmxedit",
        "glview",
        "hammer",
        "height2normal",
        "height2ssbump",
        "hlfaceposer",
        "hlmv",
        "mksheet",
        "motionmapper",
        "normal2ssbump",
        "pfm2tgas",
        "shadercompile",
        "splitskybox",
        "studiomdl",
        "vbsp",
        "vbspinfo",
        "vpk",
        "vrad",
        "vtex",
        "vtf2tga",
        "vvis",
    ];

    /// <summary>Ordner unter dem Spielordner, deren EXEs nie das Spielsymbol tragen.</summary>
    public static IReadOnlyList<string> Folders { get; } = ["BattlEye", "GameGuard", "EasyAntiCheat", "_CommonRedist", "CommonRedist", "__Installer", "Redist"];

    /// <summary>
    /// True, wenn die EXE nie als Symbolquelle dient: Name passt auf <see cref="Patterns"/>, enthält „EAC“ als eigenes
    /// Wortteil oder liegt unter <paramref name="installDir"/> in einem der <see cref="Folders"/> (ohne Spielordner: nur
    /// der direkte Ordner der EXE). Wirft nicht.
    /// </summary>
    public static bool IsExcluded(string? path, string? installDir = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileName(path.Trim().Trim('"'));
        var stem = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        if (stem.Length == 0)
        {
            return false;
        }

        foreach (var pattern in Patterns)
        {
            if (HelperExecutables.GlobMatch(stem, pattern))
            {
                return true;
            }
        }

        return HasEacWord(stem) || InExcludedFolder(path, installDir);
    }

    /// <summary>
    /// „EAC“ als eigenes Wortteil: getrennt durch Zeichen, die keine Buchstaben sind, oder durch den Wechsel der
    /// Schreibweise (GZWClientEAC, InsurgencyEAC, EACLaunch, eac_launcher). „BeachInvasion“, „PEACE“ und „EACH“ zählen nicht.
    /// </summary>
    internal static bool HasEacWord(string stem) => Words(stem).Any(w => string.Equals(w, "EAC", StringComparison.OrdinalIgnoreCase));

    /// <summary>Zerlegt einen Namen in Wortteile: an Nicht-Buchstaben, vor einem Großbuchstaben nach einem Kleinbuchstaben und vor dem letzten Großbuchstaben einer Folge, auf die ein Kleinbuchstabe folgt.</summary>
    internal static IEnumerable<string> Words(string stem)
    {
        var start = -1;
        for (var i = 0; i <= stem.Length; i++)
        {
            var c = i < stem.Length ? stem[i] : '\0';
            if (!char.IsLetter(c))
            {
                if (start >= 0)
                {
                    yield return stem[start..i];
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
                continue;
            }

            var previous = stem[i - 1];
            var next = i + 1 < stem.Length ? stem[i + 1] : '\0';
            var split = (char.IsUpper(c) && char.IsLower(previous))
                || (char.IsUpper(c) && char.IsUpper(previous) && char.IsLower(next));
            if (split)
            {
                yield return stem[start..i];
                start = i;
            }
        }
    }

    private static bool InExcludedFolder(string path, string? installDir)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        string relative;
        if (!string.IsNullOrWhiteSpace(installDir) && PathUtil.IsUnder(directory, installDir))
        {
            relative = Path.GetRelativePath(PathUtil.Normalize(installDir), PathUtil.Normalize(directory));
        }
        else
        {
            relative = Path.GetFileName(directory);
        }

        return relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => Folders.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
