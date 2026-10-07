using HdrPilot.Core.Launch;
using HdrPilot.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Wrapper;

/// <summary>
/// Laufende Instanz: Vorbereitung für die Steam-Startoption ausführen und dem wartenden Wrapper antworten. Die Antwort
/// kommt immer, auch wenn die Vorbereitung scheitert oder wirft (sonst wartete der Wrapper vergeblich bis zur Frist):
/// „bereit“ nur, wenn HDR vorbereitet wurde, sonst „nichts vorbereitet“ (der Wrapper startet dann sofort).
/// </summary>
public static class PrepareRequestHandler
{
    public static async Task<PrepareOutcome?> HandleAsync(
        string gameKey, string? readyEvent, Func<string, Task<PrepareOutcome>> prepare, ILogger logger)
    {
        PrepareOutcome? outcome = null;
        try
        {
            logger.LogInformation("Start über die Steam-Startoption angefordert: {Key}", gameKey);
            outcome = await prepare(gameKey);
            logger.LogInformation(
                "Steam-Startoption für {Key}: {Result}. {Message}",
                gameKey, outcome.Prepared ? "HDR vorbereitet" : "nichts vorbereitet", outcome.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vorbereitung für die Steam-Startoption fehlgeschlagen: {Key}", gameKey);
        }
        finally
        {
            Answer(readyEvent, outcome?.Prepared == true, logger);
        }

        return outcome;
    }

    /// <summary>Setzt das Ereignis des Wrappers (nur Namen aus <see cref="PrepareSignal"/>, nie beliebige fremde).</summary>
    public static void Answer(string? readyEvent, bool prepared, ILogger logger)
    {
        if (readyEvent is null)
        {
            return;
        }

        if (!PrepareSignal.TrySignal(readyEvent, prepared))
        {
            logger.LogWarning("Antwort an die Steam-Startoption nicht möglich: Ereignis {Event} ist ungültig oder wartet nicht mehr.", readyEvent);
        }
    }
}
