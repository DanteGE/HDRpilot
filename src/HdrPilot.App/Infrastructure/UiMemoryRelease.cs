namespace HdrPilot.App.Infrastructure;

/// <summary>Bausteine von <see cref="UiMemoryRelease"/> (in Tests ersetzbar).</summary>
public sealed class UiMemoryReleaseOptions
{
    /// <summary>Ist gerade ein Fenster offen? Dann entfällt der zweite Durchgang (das Fenster braucht seinen Speicher).</summary>
    public required Func<bool> AnyWindowOpen { get; init; }

    /// <summary>Fertig, sobald der Bilddienst nach dem Schließen aufgeräumt hat (laufende Arbeit beendet, Speicher geleert).</summary>
    public required Func<Task> CleanupFinished { get; init; }

    /// <summary>Gibt den Grafikspeicher geschlossener Fenster frei (<see cref="GraphicsMemoryFlush"/>).</summary>
    public required Func<Task> FlushGraphics { get; init; }

    /// <summary>Voller GC; <c>true</c>: gründlich (großer Objektspeicher verdichtet, freier Speicher an Windows zurück).</summary>
    public required Action<bool> Collect { get; init; }

    /// <summary>Warten (Standard: <see cref="Task.Delay(TimeSpan)"/>).</summary>
    public Func<TimeSpan, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Fertig, sobald die Oberfläche nichts mehr zu tun hat (Standard: sofort).</summary>
    public Func<Task> UiIdle { get; init; } = () => Task.CompletedTask;

    /// <summary>Mindestens so lange nach dem Schließen: Ausblenden, Nachzügler der Oberfläche, Aufräumen des Bilddienstes.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Länger wartet der zweite Durchgang nicht auf das Aufräumen des Bilddienstes.</summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Speicher nach dem Schließen eines Fensters zurückgeben (G10.9). Sofort ein voller GC wie bisher. Dann genau einmal, wenn
/// alles zur Ruhe gekommen ist (Bilddienst aufgeräumt, mindestens <see cref="UiMemoryReleaseOptions.Settle"/> vergangen,
/// Oberfläche im Leerlauf) und kein Fenster mehr offen ist, ein zweiter Durchgang: Grafikspeicher des geschlossenen Fensters
/// freigeben und gründlich sammeln. Der erste GC kommt zu früh: Das Fenster, seine Bilder auf der Grafikkarte und die
/// letzten Ladevorgänge hängen dann noch. Gemessen nach 80 gewählten Spielen: ohne zweiten Durchgang blieben rund 380 MB
/// festgeschrieben, im Infobereich vor dem Öffnen rund 160 MB. Nichts läuft wiederholt; schließt vorher ein weiteres
/// Fenster, gilt nur dessen Durchgang.
/// </summary>
public sealed class UiMemoryRelease(UiMemoryReleaseOptions options)
{
    private int _run;

    /// <summary>Der zuletzt geplante zweite Durchgang (Tests).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Wie oft der zweite Durchgang wirklich lief (Tests).</summary>
    public int Completed { get; private set; }

    /// <summary>Ein Fenster ist zu (UI-Thread): sofort sammeln, den zweiten Durchgang planen.</summary>
    public void WindowClosed()
    {
        options.Collect(false);
        var run = ++_run;
        Pending = SettleAndReleaseAsync(run);
    }

    private async Task SettleAndReleaseAsync(int run)
    {
        var settle = options.Delay(options.Settle);
        try
        {
            await options.CleanupFinished().WaitAsync(options.CleanupTimeout);
        }
        catch (TimeoutException)
        {
            // Der Bilddienst hängt (etwa an einer langsamen Platte): trotzdem freigeben, was frei ist.
        }

        await settle;
        await options.UiIdle();
        if (!IsCurrent(run))
        {
            return;
        }

        await options.FlushGraphics();
        if (!IsCurrent(run))
        {
            return;
        }

        options.Collect(true);
        Completed++;
    }

    /// <summary>Noch der letzte geplante Durchgang, und kein Fenster ist (wieder) offen.</summary>
    private bool IsCurrent(int run) => run == _run && !options.AnyWindowOpen();
}
