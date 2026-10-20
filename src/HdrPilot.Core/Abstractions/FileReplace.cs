namespace HdrPilot.Core.Abstractions;

/// <summary>
/// Ersetzen einer Datei durch eine fertig geschriebene temporäre (<see cref="IFileSystem.WriteAllTextAtomic"/>), robust gegen
/// kurze Sperren: Hat ein anderer Prozess das Ziel gerade offen (ein Leser wie die Kommandozeile oder ein Prüfskript, ein
/// Virenscanner, die Windows-Suche), verweigert Windows das Ersetzen mit „Zugriff verweigert“, auch wenn der Leser das
/// Löschen freigibt, oder mit einer Freigabeverletzung. Dann versucht es nach kurzen, wachsenden Pausen erneut, zusammen
/// höchstens <see cref="MaxWait"/>; die temporäre Datei bleibt dafür liegen. Andere Fehler (Platte voll, Ordner fehlt) und
/// ein Ziel, das länger gesperrt bleibt, gehen an den Aufrufer, der die Änderung dann nicht übernimmt und den Fehler meldet
/// (für die Konfiguration <see cref="Config.ConfigSaveException"/>). Eine halbe Datei bleibt nie zurück: ersetzt wird im Ganzen.
/// </summary>
public static class FileReplace
{
    /// <summary>ERROR_SHARING_VIOLATION und ERROR_LOCK_VIOLATION (winerror.h): ein anderer Prozess hält die Datei.</summary>
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    /// <summary>Pausen zwischen den Versuchen, kurz zuerst (ein Leser hält die Datei meist nur Millisekunden).</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [.. new[] { 10, 20, 40, 80, 150, 250, 300, 350 }.Select(ms => TimeSpan.FromMilliseconds(ms))];

    /// <summary>Längste Wartezeit über alle Versuche (1,2 s).</summary>
    public static TimeSpan MaxWait => RetryDelays.Aggregate(TimeSpan.Zero, (sum, delay) => sum + delay);

    /// <summary>
    /// Führt <paramref name="replace"/> aus und wiederholt es bei einer Sperre (<see cref="IsLocked"/>) nach den Pausen von
    /// <see cref="RetryDelays"/>; danach oder bei einem anderen Fehler wirft es den letzten Fehler weiter.
    /// </summary>
    /// <param name="sleep">Warten zwischen den Versuchen (Tests: mitschreiben statt warten); Standard: <see cref="Thread.Sleep(TimeSpan)"/>.</param>
    public static void Run(Action replace, Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(replace);
        sleep ??= Thread.Sleep;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                replace();
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Count && IsLocked(ex))
            {
                sleep(RetryDelays[attempt]);
            }
        }
    }

    /// <summary>
    /// Gesperrt statt kaputt: „Zugriff verweigert“ (so meldet Windows ein Ziel, das ein anderer offen hält) oder eine Freigabe-
    /// bzw. Sperrverletzung. Nicht: Platte voll, Pfad nicht gefunden und Ähnliches, das ein zweiter Versuch nicht behebt.
    /// </summary>
    public static bool IsLocked(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException && (ex.HResult & 0xFFFF) is SharingViolation or LockViolation);
}
