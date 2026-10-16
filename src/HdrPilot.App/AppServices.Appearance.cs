using HdrPilot.App.Services;
using HdrPilot.App.Theming;
using HdrPilot.App.Views.Shell;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>Darstellung: Farbschema, Thema und Effektstufe der ganzen App, Farben des Fenstersymbols (E89).</summary>
public sealed partial class AppServices
{
    private partial IAppearanceService CreateAppearanceService()
    {
        var logger = LoggerFactory.CreateLogger("Darstellung");
        WindowIconSource.Shared.Logger = logger;
        var service = new AppearanceService(Config, Dispatcher, System.Windows.Application.Current, WindowIconSource.Shared, logger);

        // Einstellungen, Dialoge und Tray-Menü bewegen sich nach der wirksamen Effektstufe („Aus“: gar nicht).
        ShellMotion.Level = service.Current.EffectLevel;
        service.Changed += (_, _) => ShellMotion.Level = service.Current.EffectLevel;
        return service;
    }
}
