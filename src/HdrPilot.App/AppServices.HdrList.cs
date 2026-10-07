using System.Diagnostics;
using System.Windows.Threading;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App;

/// <summary>
/// HDR-Liste von PCGamingWiki (E73): Abgleich beim Start (30 s verzögert, höchstens einmal pro Stunde, nur mit
/// AppSettings.HdrListSync), „Jetzt abgleichen“ und das optionale Konto. Mit <c>--config-dir</c> (Tests, Prüfskripte,
/// Bildschirmfotos) startet kein Abgleich von selbst, damit Prüfläufe nicht ins Netz gehen.
/// </summary>
public sealed partial class AppServices
{
    /// <summary>Wartezeit nach dem Start, bevor abgeglichen wird (Start und Einlesen gehen vor).</summary>
    public static readonly TimeSpan HdrListStartupDelay = TimeSpan.FromSeconds(30);

    public HdrListService HdrList { get; private set; } = null!;

    private void CreateHdrList()
    {
        HdrList = new HdrListService(Paths, FileSystem, Host.Clock, () => Config.Current, LoggerFactory);
        HdrList.Changed += OnHdrListChanged;
    }

    private void StartHdrList()
    {
        if (UsesDefaultPaths)
        {
            HdrList.ScheduleStartupSync(HdrListStartupDelay);
        }
    }

    private void StopHdrList()
    {
        HdrList.Changed -= OnHdrListChanged;
        HdrList.Dispose();
    }

    /// <summary>Webseite im Standardbrowser öffnen (Links zu PCGamingWiki in den Einstellungen).</summary>
    public static void OpenWebPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Nur https-Adressen.", nameof(url));
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Nach einem Abgleich ohne offenes Fenster: Antwortpuffer (einige MB) zurückgeben.</summary>
    private void OnHdrListChanged(object? sender, EventArgs e)
    {
        if (HdrList.IsRunning)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_mainWindow is null && _settingsWindow is null && !HdrList.IsRunning)
            {
                ReleaseUiMemory();
            }
        });
    }
}
