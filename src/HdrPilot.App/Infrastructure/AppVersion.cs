using System.Globalization;
using System.Reflection;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Version der App an genau einer Stelle (G8.1): Major.Minor.Build der HDRpilot-Assembly (Version aus
/// Directory.Build.props) nach derselben Regel wie Setup und Eintrag unter „Installierte Apps“
/// (<see cref="SetupSupport.Normalize"/>: die vierte Stelle setzt der Build nicht). Der Setup übernimmt
/// <see cref="Current"/> als eigene Version. Angezeigt im Kopf der Einstellungen und im Tray-Menü als erste Zeile unter
/// dem Kopf, nie fest im Text.
/// </summary>
public static class AppVersion
{
    /// <summary>Version dieser App, etwa 1.2.0.</summary>
    public static Version Current { get; } = Of(typeof(AppVersion).Assembly);

    /// <summary>Versionsnummer wie im Setup und unter „Installierte Apps“, etwa „1.2.0“.</summary>
    public static string Number => Current.ToString(3);

    /// <summary>Anzeigetext, etwa „HDRpilot 1.2.0“ (Format aus <see cref="CommonStrings.AppVersionFormat"/>).</summary>
    public static string DisplayText => Format(Current);

    /// <summary>
    /// Kurzer Text ohne App-Namen, etwa „Version 1.2.0“ (<see cref="CommonStrings.AppVersionLabelFormat"/>): für das
    /// Tray-Menü, dessen Kopf den Namen schon zeigt.
    /// </summary>
    public static string LabelText => FormatLabel(Current);

    /// <summary>Version einer Assembly nach der Regel des Setups; ohne Versionsangabe 0.0.0.</summary>
    public static Version Of(Assembly assembly) => SetupSupport.Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Anzeigetext einer Version, immer dreistellig (Major.Minor.Build).</summary>
    public static string Format(Version version) => Format(CommonStrings.AppVersionFormat, version);

    /// <summary>Kurzer Text einer Version ohne App-Namen, immer dreistellig.</summary>
    public static string FormatLabel(Version version) => Format(CommonStrings.AppVersionLabelFormat, version);

    private static string Format(string format, Version version) =>
        string.Format(CultureInfo.CurrentCulture, format, SetupSupport.Normalize(version).ToString(3));
}
