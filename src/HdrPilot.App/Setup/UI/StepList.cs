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
/// erledigt; bei einem Fehler wird genau der Schritt des Fehlers rot (sonst der laufende). Hinweise (etwa „HDR-Zustand
/// nicht wiederhergestellt“) färben ihren Schritt zusätzlich rot. Ein unerwarteter Schritt wird an seiner Stelle eingefügt.
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

        ItemFor(step).State = StepState.Running;
    }

    /// <summary>Erfolg: alle Schritte erledigt, außer denen in <paramref name="failed"/> (Hinweise), die rot werden.</summary>
    public void Complete(IEnumerable<TStep>? failed = null)
    {
        foreach (var item in _items.Where(i => i.State is StepState.Pending or StepState.Running))
        {
            item.State = StepState.Done;
        }

        MarkFailed(failed);
    }

    /// <summary>
    /// Fehler. Ist <paramref name="failedStep"/> bekannt, wird genau er rot: gemeldete Schritte davor und danach gelten als
    /// erledigt (das Entfernen macht nach Fehlern weiter), nicht gemeldete davor ebenso (es gab nichts zu tun), nicht
    /// gemeldete danach bleiben offen. Sonst wird der laufende rot. <paramref name="alsoFailed"/> (Hinweise) wird
    /// zusätzlich rot.
    /// </summary>
    public void Fail(TStep? failedStep = null, IEnumerable<TStep>? alsoFailed = null)
    {
        if (failedStep is { } step)
        {
            var failed = ItemFor(step);
            foreach (var item in _items.Where(i => i != failed && (i.State == StepState.Running || (i.State == StepState.Pending && i.Order < failed.Order))))
            {
                item.State = StepState.Done;
            }

            failed.State = StepState.Failed;
        }
        else
        {
            foreach (var item in _items.Where(i => i.State == StepState.Running))
            {
                item.State = StepState.Failed;
            }
        }

        MarkFailed(alsoFailed);
    }

    /// <summary>
    /// Nimmt einen Schritt aus der Liste, der in diesem Lauf bewusst nicht vorkam (etwa der Start bei Adminrechten): er
    /// ist weder erledigt noch fehlgeschlagen. Steht er nicht in der Liste, passiert nichts.
    /// </summary>
    public void Remove(TStep step)
    {
        if (_byStep.Remove(step, out var item))
        {
            _items.Remove(item);
        }
    }

    /// <summary>Zustand eines Schritts; null, wenn er nicht in der Liste steht.</summary>
    public StepState? StateOf(TStep step) => _byStep.TryGetValue(step, out var item) ? item.State : null;

    private void MarkFailed(IEnumerable<TStep>? steps)
    {
        foreach (var step in steps ?? [])
        {
            ItemFor(step).State = StepState.Failed;
        }
    }

    private ProgressStepItem ItemFor(TStep step) => _byStep.TryGetValue(step, out var item) ? item : Add(step);

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
