using System.Collections.ObjectModel;
using System.Globalization;
using HdrPilot.App.Infrastructure;

namespace HdrPilot.App.Setup.UI;

/// <summary>Zustand eines Schritts in der Fortschrittsliste.</summary>
public enum StepState
{
    Pending,
    Running,
    Done,
    Failed,
}

/// <summary>Zeile der Fortschrittsliste: Text aus den Ressourcen, Zustand live.</summary>
public sealed class ProgressStepItem(int order, string text) : ObservableObject
{
    private StepState _state;

    public int Order { get; } = order;

    public string Text { get; } = text;

    public StepState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    /// <summary>Name für die Bedienungshilfen (die Listenzeile nimmt ToString des Eintrags).</summary>
    public override string ToString() => Text;
}

/// <summary>
/// Fortschrittsliste von Setup und Entfernen. Gemeldet wird der Beginn eines Schritts: der bisher laufende gilt dann
/// als erledigt. Schritte, die die Engine nicht meldet (nichts zu tun), gelten am Ende eines erfolgreichen Laufs als
/// erledigt; bei einem Fehler wird der laufende rot. Ein unerwarteter Schritt wird an seiner Stelle eingefügt.
/// </summary>
public sealed class StepList<TStep>
    where TStep : struct, Enum
{
    private readonly Func<TStep, string> _text;
    private readonly ObservableCollection<ProgressStepItem> _items = [];
    private readonly Dictionary<TStep, ProgressStepItem> _byStep = [];

    public StepList(Func<TStep, string> text)
    {
        _text = text;
        Items = new ReadOnlyObservableCollection<ProgressStepItem>(_items);
    }

    public ReadOnlyObservableCollection<ProgressStepItem> Items { get; }

    /// <summary>Neue Liste, alle Schritte offen (Beginn und erneuter Versuch).</summary>
    public void Reset(IEnumerable<TStep> steps)
    {
        _items.Clear();
        _byStep.Clear();
        foreach (var step in steps)
        {
            Add(step);
        }
    }

    public void Begin(TStep step)
    {
        foreach (var item in _items.Where(i => i.State == StepState.Running))
        {
            item.State = StepState.Done;
        }

        if (!_byStep.TryGetValue(step, out var current))
        {
            current = Add(step);
        }

        current.State = StepState.Running;
    }

    public void Complete()
    {
        foreach (var item in _items.Where(i => i.State is StepState.Pending or StepState.Running))
        {
            item.State = StepState.Done;
        }
    }

    public void Fail()
    {
        foreach (var item in _items.Where(i => i.State == StepState.Running))
        {
            item.State = StepState.Failed;
        }
    }

    /// <summary>Zustand eines Schritts; null, wenn er nicht in der Liste steht.</summary>
    public StepState? StateOf(TStep step) => _byStep.TryGetValue(step, out var item) ? item.State : null;

    private ProgressStepItem Add(TStep step)
    {
        var order = Convert.ToInt32(step, CultureInfo.InvariantCulture);
        var item = new ProgressStepItem(order, _text(step));
        _items.Insert(_items.TakeWhile(i => i.Order < order).Count(), item);
        _byStep[step] = item;
        return item;
    }
}

/// <summary>
/// Fortschritt auf dem Thread der Oberfläche: direkt, wenn die Meldung schon dort ankommt, sonst über den beim
/// Anlegen gültigen Kontext. (<see cref="Progress{T}"/> stellt immer nur ein; ohne Dispatcher käme die Meldung
/// verspätet auf einem Pool-Thread an.)
/// </summary>
public sealed class UiProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context is null || SynchronizationContext.Current == _context)
        {
            handler(value);
        }
        else
        {
            _context.Post(_ => handler(value), null);
        }
    }
}
