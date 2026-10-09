namespace HdrPilot.App.Setup;

// Platzhalter, bis die Engines umgesetzt sind (wird dabei gelöscht).
public static partial class SetupEngines
{
    public static partial ISetupEngine CreateSetup(SetupLocations locations, SetupBundle bundle, SetupLog log) =>
        throw new NotImplementedException("Setup-Engine fehlt noch.");

    public static partial IUninstallEngine CreateUninstall(SetupLocations locations, SetupLog log) =>
        throw new NotImplementedException("Entfernen-Engine fehlt noch.");
}
