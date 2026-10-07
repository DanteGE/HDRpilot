using System.Windows;
using HdrPilot.App.Services;

namespace HdrPilot.App;

// GERÜST: wird durch Tray, Einstellungen, Autostart und Bildschirmereignisse ersetzt.
public sealed partial class AppServices
{
    private partial ITrayService CreateTrayService() => new NoTray();

    private partial IAutostartService CreateAutostartService() => new NoAutostart();

    private partial IDisplayEventsService CreateDisplayEventsService() => new NoDisplayEvents();

    private partial Window CreateSettingsWindow() => new Window { Title = "Einstellungen" };

    private sealed class NoTray : ITrayService
    {
        public void Initialize()
        {
        }

        public void Refresh()
        {
        }

        public void Notify(string title, string message, bool isWarning = false)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoAutostart : IAutostartService
    {
        public bool IsEnabled => false;

        public void SetEnabled(bool enabled)
        {
        }
    }

    private sealed class NoDisplayEvents : IDisplayEventsService
    {
        public event EventHandler? DisplaysChanged
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }
    }
}
