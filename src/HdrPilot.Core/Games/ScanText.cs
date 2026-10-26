using System.Globalization;

namespace HdrPilot.Core.Games;

/// <summary>
/// Meldungen der Scanner (E95): Vorlagen aus <c>Resources\ScanStrings.resx</c> in der Sprache der Oberfläche. Sie erscheinen
/// in der Bibliothek („Beim letzten Einlesen gab es … Probleme“) und in <c>hdrpilot scan</c>; das Protokoll bleibt deutsch.
/// </summary>
internal static class ScanText
{
    public static string Format(string template, params object?[] args) => string.Format(CultureInfo.CurrentCulture, template, args);
}
