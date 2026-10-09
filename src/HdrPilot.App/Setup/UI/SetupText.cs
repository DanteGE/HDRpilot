using System.Globalization;
using HdrPilot.App.Resources;

namespace HdrPilot.App.Setup.UI;

/// <summary>Texte von Setup und Entfernen aus <see cref="SetupStrings"/>: Versionen, Schritte, Fehler.</summary>
public static class SetupText
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>Version wie im Eintrag unter „Installierte Apps“: 1.1.0, die vierte Stelle nur, wenn sie gesetzt ist.</summary>
    public static string Version(Version? version) => version is null ? string.Empty
        : version.Revision > 0 ? version.ToString(4)
        : version.Build >= 0 ? version.ToString(3)
        : version.ToString(2);

    public static string Step(SetupStep step) => step switch
    {
        SetupStep.CheckSetupFile => SetupStrings.SetupStepCheckSetupFile,
        SetupStep.StopRunningApp => SetupStrings.SetupStepStopRunningApp,
        SetupStep.CopyFiles => SetupStrings.SetupStepCopyFiles,
        SetupStep.Shortcuts => SetupStrings.SetupStepShortcuts,
        SetupStep.Autostart => SetupStrings.SetupStepAutostart,
        SetupStep.Register => SetupStrings.SetupStepRegister,
        _ => SetupStrings.SetupStepLaunchApp,
    };

    public static string Step(UninstallStep step) => step switch
    {
        UninstallStep.StopRunningApp => SetupStrings.UninstallStepStopRunningApp,
        UninstallStep.RestoreHdr => SetupStrings.UninstallStepRestoreHdr,
        UninstallStep.RemoveFiles => SetupStrings.UninstallStepRemoveFiles,
        UninstallStep.RemoveShortcuts => SetupStrings.UninstallStepRemoveShortcuts,
        UninstallStep.RemoveAutostart => SetupStrings.UninstallStepRemoveAutostart,
        UninstallStep.Unregister => SetupStrings.UninstallStepUnregister,
        _ => SetupStrings.UninstallStepRemoveData,
    };

    /// <summary>
    /// Fehlertext je Art. Unerwartete Fehler nennen die technische Angabe (<paramref name="detail"/>, falls es eine gibt)
    /// und das Protokoll; liefert die Engine dafür schon den fertigen Text, bleibt er unverändert.
    /// </summary>
    public static string Error(SetupErrorKind? error, string? detail, string logPath) => error switch
    {
        SetupErrorKind.SetupFileDamaged => SetupStrings.SetupErrorSetupFileDamaged,
        SetupErrorKind.AppStillRunning => SetupStrings.SetupErrorAppStillRunning,
        SetupErrorKind.FilesInUse => SetupStrings.SetupErrorFilesInUse,
        SetupErrorKind.AccessDenied => SetupStrings.SetupErrorAccessDenied,
        SetupErrorKind.DiskFull => SetupStrings.SetupErrorDiskFull,
        SetupErrorKind.DowngradeNotAllowed => SetupStrings.SetupErrorDowngradeNotAllowed,
        SetupErrorKind.AnotherSetupRunning => SetupStrings.SetupErrorAnotherSetupRunning,
        SetupErrorKind.NotInstalled => SetupStrings.SetupErrorNotInstalled,
        _ => Unexpected(detail, logPath),
    };

    private static string Unexpected(string? detail, string logPath)
    {
        var prefix = SetupStrings.SetupErrorUnexpected.Split("{0}")[0];
        if (detail is not null && prefix.Length > 0 && detail.StartsWith(prefix, StringComparison.Ordinal))
        {
            return detail;
        }

        var text = detail?.Trim().TrimEnd('.').TrimEnd() ?? string.Empty;
        return text.Length == 0
            ? Format(SetupStrings.SetupErrorUnexpectedNoDetail, logPath)
            : Format(SetupStrings.SetupErrorUnexpected, text, logPath);
    }
}
