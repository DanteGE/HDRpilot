using System.Diagnostics;
using Xunit.v3;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Sammelt Meldungen der WPF-Bindungs-Ablaufverfolgung (Warnung und Fehler). Die Ablaufverfolgung gilt für den ganzen
/// Testprozess, und alle WPF-Tests teilen sich den Dispatcher des <see cref="WpfTestHost"/>, den DoEvents verschachtelt
/// pumpt: Liefe ein anderer Test daneben, landeten dessen Bindungsfehler (etwa der absichtlich falsche Pfad in
/// <see cref="ShellBindingTests"/>) auch hier, und zwei Mitschreiber stellten sich gegenseitig die Stufe zurück. Deshalb
/// nur in Klassen einer Sammlung ohne Parallelität (<see cref="ShellSerialCollection"/>) benutzen; der Konstruktor prüft das.
/// Auf dem Host-Thread erzeugen.
/// </summary>
internal sealed class BindingErrorRecorder : TraceListener
{
    private readonly SourceLevels _previousLevel;
    private readonly List<string> _messages = [];

    public BindingErrorRecorder()
    {
        Assert.True(
            TestContext.Current.TestCollection is IXunitTestCollection { DisableParallelization: true },
            "BindingErrorRecorder hört prozessweit mit: Die Testklasse gehört in [Collection(ShellSerialCollection.Name)].");

        // Was frühere Tests auf dem gemeinsamen Dispatcher noch ausstehen haben, gehört nicht zu diesem Test.
        WpfTestHost.DoEvents();

        // Ohne Debugger schreibt WPF erst nach Refresh in die Ablaufverfolgung.
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        _previousLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Warning;
        source.Listeners.Add(this);
    }

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Remove(this);
        source.Switch.Level = _previousLevel;
        base.Dispose(disposing);
    }
}
