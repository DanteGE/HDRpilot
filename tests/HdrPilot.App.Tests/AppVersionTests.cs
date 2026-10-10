using System.Diagnostics;
using System.Globalization;
using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;
using HdrPilot.App.Setup;
using HdrPilot.App.Setup.UI;
using HdrPilot.App.Tests.Setup.Engine;

namespace HdrPilot.App.Tests;

/// <summary>
/// Versionsnummer der App (G8.1): eine Quelle (<see cref="AppVersion"/>), dreistellig aus der Assembly, dieselbe Zahl wie
/// Setup und „Installierte Apps“, Anzeigetext aus den Ressourcen. Einstellungen und Tray-Menü: ShellViewTests.AppVersion_*.
/// </summary>
public sealed class AppVersionTests
{
    /// <summary>Erwartung ohne <see cref="AppVersion"/>: Dateiversion der gebauten HDRpilot-Assembly, dreistellig.</summary>
    internal static Version BuiltFileVersion()
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(AppVersion).Assembly.Location);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    /// <summary>Erwarteter Anzeigetext, etwa „HDRpilot 1.2.0“.</summary>
    internal static string ExpectedDisplayText() => "HDRpilot " + BuiltFileVersion().ToString(3);

    [Fact]
    public void AppVersion_is_the_three_part_version_of_the_built_assembly()
    {
        var assembly = typeof(AppVersion).Assembly.GetName().Version!;

        Assert.Equal(new Version(assembly.Major, assembly.Minor, assembly.Build), AppVersion.Current);
        Assert.Equal(BuiltFileVersion(), AppVersion.Current);
        Assert.Equal(-1, AppVersion.Current.Revision);
        Assert.NotEqual(new Version(0, 0, 0), AppVersion.Current);
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Number);
        Assert.Equal(BuiltFileVersion().ToString(3), AppVersion.Number);
    }

    [Fact]
    public void AppVersion_is_what_the_setup_installs_and_registers()
    {
        using var harness = new SetupHarness();
        var engine = new SetupEngine(
            harness.Locations,
            harness.Bundle("1.0.0"),
            harness.Log,
            harness.Registry,
            harness.Entries,
            harness.App,
            ShellSetupShortcuts.Instance,
            versionReader: SetupHarness.ReadVersion);

        // Ohne Vorgabe ist die Version der Setup-Datei (Fenster „alt → neu“, Protokoll) die der App.
        var setupVersion = engine.Inspect().SetupVersion;
        Assert.Equal(AppVersion.Current, setupVersion);
        Assert.Equal(AppVersion.Number, SetupText.Version(setupVersion));

        // Eintrag unter „Installierte Apps“
        Assert.Equal(AppVersion.Number, UninstallEntry.For(harness.Locations, setupVersion, 0, DateTime.Now).DisplayVersion);

        // Ein späterer Setup liest die installierte EXE über ihre Dateiversion: dieselbe Zahl.
        Assert.Equal(AppVersion.Current, SetupSupport.ReadFileVersion(typeof(AppVersion).Assembly.Location));
    }

    [Fact]
    public void AppVersion_display_text_comes_from_the_resources()
    {
        // Nummer nie fest im Text: das Format hat einen Platzhalter und keine Ziffern.
        Assert.Contains("{0}", CommonStrings.AppVersionFormat, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d", CommonStrings.AppVersionFormat.Replace("{0}", string.Empty, StringComparison.Ordinal));
        Assert.StartsWith(CommonStrings.AppName + " ", CommonStrings.AppVersionFormat, StringComparison.Ordinal);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, CommonStrings.AppVersionFormat, AppVersion.Number), AppVersion.DisplayText);
        Assert.Equal(ExpectedDisplayText(), AppVersion.DisplayText);
    }

    [Theory]
    [InlineData("1.2.0", "HDRpilot 1.2.0")]
    [InlineData("9.8.7.6", "HDRpilot 9.8.7")]
    [InlineData("2.0", "HDRpilot 2.0.0")]
    [InlineData("10.11.12", "HDRpilot 10.11.12")]
    public void AppVersion_format_shows_major_minor_build_like_the_setup(string version, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(Version.Parse(version)));
        Assert.Equal(expected, "HDRpilot " + SetupText.Version(SetupSupport.Normalize(Version.Parse(version))));
    }
}
