using System.IO;
using HdrPilot.App.Resources;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Suche in „Bild wählen…“ (E93): eine Sitzung je Dialog, nur mit lesbarem Schlüssel und unabhängig vom Schalter (der regelt
/// nur das automatische Füllen). Die Sitzung erzeugt ihren Client erst bei der ersten Anfrage und teilt den Abstand des
/// Dienstes. Schlüssel entfernen und Beenden brechen offene Sitzungen sofort ab; Ausschalten nicht.
/// </summary>
public sealed partial class SteamGridDbService
{
    private readonly List<SteamGridDbSearchSession> _sessions = [];
    private DateTimeOffset? _lastSearch;
    private int _lastSearchRequests;
    private string? _lastSearchError;

    public ISteamGridDbSearchSession? OpenSearch(GameConfig game)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }
        }

        var key = _keys.Load(out var unreadable);
        if (key is null)
        {
            lock (_gate)
            {
                _keyState = unreadable ? SteamGridDbKeyState.Unreadable : SteamGridDbKeyState.Missing;
            }

            return null;
        }

        var (appId, remembered) = TargetOf(game);
        // Der entschlüsselte Schlüssel bleibt in dieser Fabrik und im Client; die Sitzung zeigt ihn nirgends.
        var limits = _options.Search;
        var session = new SteamGridDbSearchSession(new SteamGridDbSearchSetup
        {
            GameName = game.Name,
            SteamAppId = appId,
            RememberedId = remembered,
            CreateClient = budget => CreateClient(key, budget, limits.Timeouts),
            Limits = limits,
            Clock = _options.Clock,
            Logger = _logger,
            TestLog = _options.TestLog,
            Lifetime = _lifetime.Token,
            Unauthorized = OnSearchUnauthorized,
            Answered = OnSearchAnswered,
            Closed = OnSearchClosed,
        });
        lock (_gate)
        {
            if (!_disposed)
            {
                _sessions.Add(session);
            }
            else
            {
                session.Dispose();
                return null;
            }
        }

        _logger.LogInformation("SteamGridDB-Suche geöffnet.");
        return session;
    }

    public SteamGridDbMatch? PeekMatch(GameConfig game)
    {
        var (appId, remembered) = TargetOf(game);
        return SteamGridDbSearchSession.InitialMatchFor(game.Name, appId, remembered);
    }

    public void RememberMatch(GameConfig game, long steamGridDbGameId)
    {
        if (SteamAppId(game) is not null || steamGridDbGameId <= 0)
        {
            return;
        }

        // Ein laufender Lauf endet, bevor sich das Verzeichnis ändert; er speichert danach kein Bild der alten Zuordnung.
        CancelRun();
        Task? previous;
        lock (_gate)
        {
            previous = _running;
        }

        try
        {
            _options.Artwork.ReplaceSteamGridDbGame(game, steamGridDbGameId);
            _options.Artwork.FlushIndex();
            _logger.LogInformation("SteamGridDB: Zuordnung eines Spiels vom Nutzer geändert.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("SteamGridDB: Zuordnung nicht vollständig gemerkt ({Error}).", ex.GetType().Name);
        }

        if (_options.Current().Settings.Artwork?.SteamGridDb == true && LoadKeyState() == SteamGridDbKeyState.Stored)
        {
            // Die Bilder der neuen Zuordnung füllt ein Lauf nach den üblichen Regeln (nicht während eines Spiels).
            _ = RunAfterAsync(previous);
        }
    }

    /// <summary>Steam-AppID oder (nur ohne sie) die gemerkte SteamGridDB-ID; nur lokal, ohne Schlüssel und Netz.</summary>
    private (string? AppId, long? Remembered) TargetOf(GameConfig game)
    {
        var appId = SteamAppId(game);
        if (appId is not null)
        {
            return (appId, null);
        }

        try
        {
            return (null, _options.Artwork.SteamGridDbGameId(game) is > 0 and var id ? id : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("SteamGridDB: gemerkte Zuordnung nicht lesbar ({Error}).", ex.GetType().Name);
            return (null, null);
        }
    }

    /// <summary>Wartet, bis ein abgebrochener Lauf endet, und startet dann einen neuen.</summary>
    private async Task RunAfterAsync(Task? previous)
    {
        if (previous is not null)
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SteamGridDbException or IOException)
            {
                // Der alte Lauf endet so oder so.
            }
        }

        await RunNowAsync().ConfigureAwait(false);
    }

    /// <summary>Bricht alle offenen Suchen ab (Schlüssel entfernt, Dienst beendet).</summary>
    private void CloseSessions()
    {
        SteamGridDbSearchSession[] open;
        lock (_gate)
        {
            open = [.. _sessions];
        }

        foreach (var session in open)
        {
            session.Dispose();
        }
    }

    private void OnSearchUnauthorized()
    {
        lock (_gate)
        {
            _lastSearchError = ArtworkStrings.SteamGridDb_Error_Unauthorized;
        }

        RaiseChanged();
    }

    private void OnSearchAnswered()
    {
        bool cleared;
        lock (_gate)
        {
            cleared = _lastSearchError is not null;
            _lastSearchError = null;
        }

        if (cleared)
        {
            RaiseChanged();
        }
    }

    private void OnSearchClosed(SteamGridDbSearchSession session, SteamGridDbSessionUsage usage, DateTimeOffset? lastRequest)
    {
        var requests = usage.ApiRequests + usage.ImageRequests;
        lock (_gate)
        {
            _sessions.Remove(session);
            if (requests > 0)
            {
                _lastSearch = lastRequest ?? _options.Clock.UtcNow;
                _lastSearchRequests = requests;
            }
        }

        _logger.LogInformation("SteamGridDB-Suche beendet: {Api} Anfragen, {Images} Bilder.", usage.ApiRequests, usage.ImageRequests);
        if (requests > 0)
        {
            RaiseChanged();
        }
    }
}
