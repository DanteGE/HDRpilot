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

    /// <summary>
    /// Stellt Texte und Formate des laufenden Tests vorübergehend um (etwa „en-US“, E95) und beim Dispose zurück. Gilt nur für
    /// diesen Ablauf (auch über await hinweg), nicht für andere, parallel laufende Tests.
    /// </summary>
    public static IDisposable Use(string name) => new Scope(CultureInfo.GetCultureInfo(name));

    [ModuleInitializer]
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = German;
        CultureInfo.DefaultThreadCurrentUICulture = German;
        CultureInfo.CurrentCulture = German;
        CultureInfo.CurrentUICulture = German;
    }

    private sealed class Scope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public Scope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
