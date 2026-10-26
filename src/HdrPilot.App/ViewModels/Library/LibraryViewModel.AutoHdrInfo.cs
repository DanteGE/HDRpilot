namespace HdrPilot.App.ViewModels.Library;

/// <summary>
/// Erklärungen zu „Windows Auto HDR“ im Detailbereich (Rückmeldung des Nutzers 2026-10-22): zugeklappt, auf Wunsch über
/// „Mehr erfahren“ aufgeklappt. Der Zustand gilt, solange das Fenster offen ist, auch beim Wechsel des Spiels (wer sie
/// liest, liest sie meist für mehrere Spiele), wird aber nicht gespeichert: Jedes neue Fenster beginnt zugeklappt, so
/// kommen die Texte, die der Nutzer weghaben wollte, nicht durch ein einmal vergessenes Aufklappen dauerhaft zurück, und
/// die Konfiguration bekommt kein Feld dafür.
/// </summary>
public sealed partial class LibraryViewModel
{
    private bool _autoHdrInfoExpanded;

    /// <summary>Erklärung und Hinweise zu „Windows Auto HDR“ aufgeklappt (nur für dieses Fenster).</summary>
    public bool IsAutoHdrInfoExpanded
    {
        get => _autoHdrInfoExpanded;
        set
        {
            if (SetProperty(ref _autoHdrInfoExpanded, value))
            {
                _details?.OnAutoHdrInfoExpandedChanged();
            }
        }
    }
}
