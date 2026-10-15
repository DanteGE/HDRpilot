using HdrPilot.App.Services;
using HdrPilot.App.Theming;
using Microsoft.Extensions.Logging;

namespace HdrPilot.App;

/// <summary>Darstellung: Farbschema, Thema und Effektstufe der ganzen App, Farben des Fenstersymbols (E89).</summary>
public sealed partial class AppServices
{
    private partial IAppearanceService CreateAppearanceService()
    {
        var logger = LoggerFactory.CreateLogger("Darstellung");
        WindowIconSource.Shared.Logger = logger;
        return new AppearanceService(Config, Dispatcher, System.Windows.Application.Current, WindowIconSource.Shared, logger);
    }
}
