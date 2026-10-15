using HdrPilot.App.Theming;

namespace HdrPilot.App;

/// <summary>Darstellung: Farbschema, Thema und Effektstufe der ganzen App (E89).</summary>
public sealed partial class AppServices
{
    private partial IAppearanceService CreateAppearanceService() =>
        new AppearanceService(Config, Dispatcher, System.Windows.Application.Current);
}
