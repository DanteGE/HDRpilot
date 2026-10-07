using System.ComponentModel;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Gemeinsamer Schalter für Zier-Animationen der Grundbausteine (Controls.xaml): folgt der Windows-Einstellung
/// „Animationseffekte“ und der Hardware-Darstellung (über <see cref="ShellMotion.IsEnabled"/>, inklusive Override für
/// Tests und Bildschirmfotos). XAML bindet an <c>{Binding IsEnabled, Source={x:Static infra:Motion.Instance}}</c>.
/// </summary>
public sealed class Motion : INotifyPropertyChanged
{
    private Motion()
    {
        ShellMotion.StaticPropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static Motion Instance { get; } = new();

    public bool IsEnabled => ShellMotion.IsEnabled;
}
