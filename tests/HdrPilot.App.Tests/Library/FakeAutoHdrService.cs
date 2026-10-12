using HdrPilot.Core.AutoHdr;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// Auto HDR ohne Registry (E88) für Bibliothek und Einstellungen: Stände und Ergebnis der Rücknahme vorgegeben, Aufrufe
/// werden mitgeschrieben. Berührt nie einen Registry-Schlüssel.
/// </summary>
public sealed class FakeAutoHdrService : IAutoHdrService
{
    public event EventHandler? Changed;

    public AutoHdrRegistryLocation Location { get; } = AutoHdrRegistryLocation.ForTest("AppTests");

    /// <summary>Stand je Spielschlüssel; fehlt einer, gilt <see cref="AutoHdrGameStatus.None"/>.</summary>
    public Dictionary<string, AutoHdrGameStatus> Statuses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> StatusReads { get; } = [];

    public int RollbackCalls { get; private set; }

    public AutoHdrRollbackResult NextRollback { get; set; } = new(0, 0, 0, 0);

    /// <summary>Wenn gesetzt, endet die Rücknahme erst, wenn der Test diese Aufgabe abschließt.</summary>
    public TaskCompletionSource<AutoHdrRollbackResult>? PendingRollback { get; set; }

    /// <summary>Wenn gesetzt, scheitert die Rücknahme damit (entgegen der Absprache; die Oberfläche muss es abfangen).</summary>
    public Exception? RollbackFailure { get; set; }

    /// <summary>Wenn gesetzt, scheitert das Lesen des Stands damit (entgegen der Absprache).</summary>
    public Exception? StatusFailure { get; set; }

    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    public AutoHdrGameStatus GetStatus(string gameKey)
    {
        StatusReads.Add(gameKey);
        if (StatusFailure is { } failure)
        {
            throw failure;
        }

        return Statuses.GetValueOrDefault(gameKey) ?? AutoHdrGameStatus.None;
    }

    public Task<AutoHdrRollbackResult> RollbackAllAsync(CancellationToken cancellationToken = default)
    {
        RollbackCalls++;
        if (RollbackFailure is { } failure)
        {
            return Task.FromException<AutoHdrRollbackResult>(failure);
        }

        return PendingRollback?.Task ?? Task.FromResult(NextRollback);
    }

    /// <summary>Wie nach einem Abgleich: Stand eines Spiels setzen und melden.</summary>
    public void Publish(string gameKey, params AutoHdrTargetState[] states)
    {
        Statuses[gameKey] = new AutoHdrGameStatus(states.Select((s, i) => new AutoHdrTargetStatus($@"D:\Games\{gameKey.Replace(':', '_')}\game{i}.exe", s)).ToList());
        RaiseChanged();
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
