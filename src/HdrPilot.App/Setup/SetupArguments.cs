namespace HdrPilot.App.Setup;

/// <summary>
/// Befehlszeile von Setup und Entfernen (E83).
/// <list type="bullet">
/// <item>Setup-Datei: <c>--quiet</c> ohne Fenster, <c>--autostart on|off</c>, <c>--desktop-shortcut on|off</c> (ohne Angabe:
/// bisheriger Stand, neu: Autostart an, Desktop aus), <c>--no-launch</c>, <c>--allow-downgrade</c>.</item>
/// <item>Installierte EXE: <c>--uninstall</c> (aus „Installierte Apps“), <c>--quiet</c>, <c>--remove-data</c>.</item>
/// <item>Intern: <c>--uninstall-continue --wait-pid &lt;pid&gt;</c> (die Kopie im Temp-Ordner setzt das Entfernen fort).</item>
/// <item>Prüfläufe: <c>--test-root &lt;ordner&gt;</c>, siehe <see cref="SetupLocations.ForTestRoot"/>; nur damit auch
/// <c>--ui-language de|en</c> (Sprache der Fenster statt der Anzeigesprache von Windows, E95).</item>
/// </list>
/// </summary>
public sealed record SetupArguments
{
    public bool Quiet { get; init; }

    public bool? Autostart { get; init; }

    public bool? DesktopShortcut { get; init; }

    public bool NoLaunch { get; init; }

    public bool AllowDowngrade { get; init; }

    public bool Uninstall { get; init; }

    public bool RemoveData { get; init; }

    public bool UninstallContinue { get; init; }

    public int? WaitPid { get; init; }

    public string? TestRoot { get; init; }

    /// <summary>Prüfläufe (E95): Sprache der Fenster ("de" oder "en"); gilt nur zusammen mit <see cref="TestRoot"/>.</summary>
    public string? UiLanguage { get; init; }

    /// <summary>
    /// Sprache von Setup und Entfernen (E95): wie die Anzeigesprache von Windows (Deutsch bei Deutsch, sonst Englisch), im
    /// Prüflauf auf Wunsch fest (<see cref="UiLanguage"/>). Die Einstellungen von HDRpilot liest der Setup dafür nie.
    /// </summary>
    public string EffectiveUiLanguage => TestRoot is not null && Core.Config.UiLanguage.Parse(UiLanguage) is { } fixedLanguage
        ? fixedLanguage
        : Core.Config.UiLanguage.ForNewConfig();

    public IReadOnlyList<string> Unknown { get; init; } = [];

    /// <summary>True, wenn die EXE als Entfernen läuft (sonst gelten die Schalter nur für eine Setup-Datei).</summary>
    public bool IsUninstall => Uninstall || UninstallContinue;

    public static SetupArguments Parse(IReadOnlyList<string> args)
    {
        var result = new SetupArguments();
        var unknown = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--quiet":
                    result = result with { Quiet = true };
                    break;
                case "--autostart" when i + 1 < args.Count && OnOff(args[i + 1]) is { } autostart:
                    result = result with { Autostart = autostart };
                    i++;
                    break;
                case "--desktop-shortcut" when i + 1 < args.Count && OnOff(args[i + 1]) is { } desktop:
                    result = result with { DesktopShortcut = desktop };
                    i++;
                    break;
                case "--no-launch":
                    result = result with { NoLaunch = true };
                    break;
                case "--allow-downgrade":
                    result = result with { AllowDowngrade = true };
                    break;
                case "--uninstall":
                    result = result with { Uninstall = true };
                    break;
                case "--remove-data":
                    result = result with { RemoveData = true };
                    break;
                case "--uninstall-continue":
                    result = result with { UninstallContinue = true };
                    break;
                case "--wait-pid" when i + 1 < args.Count && int.TryParse(args[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid) && pid > 0:
                    result = result with { WaitPid = pid };
                    i++;
                    break;
                case "--test-root" when i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]):
                    result = result with { TestRoot = args[++i] };
                    break;
                case "--ui-language" when i + 1 < args.Count && Core.Config.UiLanguage.Parse(args[i + 1]) is { } language:
                    result = result with { UiLanguage = language };
                    i++;
                    break;
                default:
                    unknown.Add(a);
                    break;
            }
        }

        return result with { Unknown = unknown };
    }

    /// <summary>Befehlszeile für die Kopie im Temp-Ordner, die das Entfernen fortsetzt.</summary>
    public IReadOnlyList<string> ToContinueArguments(int waitPid)
    {
        var list = new List<string> { "--uninstall-continue", "--wait-pid", waitPid.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (Quiet)
        {
            list.Add("--quiet");
        }

        if (RemoveData)
        {
            list.Add("--remove-data");
        }

        if (TestRoot is not null)
        {
            list.Add("--test-root");
            list.Add(TestRoot);
            if (UiLanguage is not null)
            {
                list.Add("--ui-language");
                list.Add(UiLanguage);
            }
        }

        return list;
    }

    private static bool? OnOff(string value) => value.ToLowerInvariant() switch
    {
        "on" or "an" or "true" or "1" => true,
        "off" or "aus" or "false" or "0" => false,
        _ => null,
    };
}

/// <summary>Exit-Codes von Setup und Entfernen (für <c>--quiet</c> und Prüfskripte).</summary>
public static class SetupExitCodes
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int BadArguments = 2;
    public const int DowngradeRefused = 3;
    public const int AppStillRunning = 4;
    public const int Cancelled = 5;
    public const int NotInstalled = 6;

    /// <summary>Läuft im Container einer anderen App (E84): Windows leitete Registry und AppData um, nichts geändert.</summary>
    public const int Container = 7;
}
