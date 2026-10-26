using System.Globalization;
using System.Runtime.CompilerServices;

namespace HdrPilot.Tests.TestSupport;

/// <summary>
/// Feste Kultur für alle Tests (E95): Texte und Formate auf Deutsch (de-DE), unabhängig von der Sprache des Rechners. Die
/// Tests prüfen die deutschen Texte der neutralen Ressourcen; ohne diese Vorgabe zeigten sie auf einem englischen System
/// die englischen Übersetzungen. Gilt für den Thread, der die Assembly lädt, und für alle weiteren Threads (auch die
/// STA-Threads der Oberflächentests). Tests der Sprache stellen ihre Kultur selbst ein und wieder zurück.
/// </summary>
internal static class TestCulture
{
    public static CultureInfo German { get; } = CultureInfo.GetCultureInfo("de-DE");

    [ModuleInitializer]
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = German;
        CultureInfo.DefaultThreadCurrentUICulture = German;
        CultureInfo.CurrentCulture = German;
        CultureInfo.CurrentUICulture = German;
    }
}
