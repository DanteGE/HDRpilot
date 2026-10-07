using System.Text;
using HdrPilot.Core.Games.Scanners.Xbox;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>Bausteine des Xbox-Scanners: .GamingRoot, PublisherId, ms-resource, Manifeste.</summary>
public sealed class XboxFormatTests
{
    [Theory]
    [InlineData("GamingRoot_C.bin", "XboxGames")]
    [InlineData("GamingRoot_D.bin", "Xbox Bibliothek")]
    public void Gaming_root_real_files_are_parsed(string fixture, string expected)
    {
        var content = GamingRootFile.Parse(Fixture(fixture));

        Assert.Null(content.Error);
        Assert.Equal(1u, content.HeaderValue);
        Assert.Equal([expected], content.Folders);
    }

    [Fact]
    public void Gaming_root_with_two_entries_returns_both()
    {
        var content = GamingRootFile.Parse(Fixture("GamingRoot_zwei_Eintraege.bin"));

        Assert.Null(content.Error);
        Assert.Equal(2u, content.HeaderValue);
        Assert.Equal(["XboxGames", @"Spiele\Xbox"], content.Folders);
    }

    [Fact]
    public void Gaming_root_trailing_padding_is_accepted()
    {
        byte[] data = [.. Fixture("GamingRoot_C.bin"), 0, 0, 0, 0, 0];

        var content = GamingRootFile.Parse(data);

        Assert.Null(content.Error);
        Assert.Equal(["XboxGames"], content.Folders);
    }

    [Fact]
    public void Broken_gaming_root_truncated_keeps_complete_entries()
    {
        var full = Fixture("GamingRoot_zwei_Eintraege.bin");

        var content = GamingRootFile.Parse(full.AsSpan(0, full.Length - 5));

        Assert.Equal(["XboxGames"], content.Folders);
        Assert.Contains("abgeschnitten", content.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[0], "zu kurz")]
    [InlineData(new byte[] { 0x52, 0x47, 0x42 }, "zu kurz")]
    [InlineData(new byte[] { 0x52, 0x47, 0x42, 0x58, 1, 0, 0, 0 }, "keinen Ordnernamen")]
    [InlineData(new byte[] { 0x52, 0x47, 0x42, 0x58, 1, 0, 0, 0, 0x58, 0, 0x01, 0, 0, 0 }, "Ungültiger Ordnername")]
    [InlineData(new byte[] { 0x52, 0x47, 0x42, 0x58, 1, 0, 0, 0, 0x58, 0, 0x00, 0xD8, 0, 0 }, "Ungültiger Ordnername")]
    [InlineData(new byte[] { 0x72, 0x67, 0x62, 0x78, 1, 0, 0, 0, 0x58, 0, 0, 0 }, "RGBX")]
    public void Broken_gaming_root_variants_report_an_error(byte[] data, string expected)
    {
        var content = GamingRootFile.Parse(data);

        Assert.Contains(expected, content.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CN=3F2A7C19-6B4D-4E8A-9C21-5D7E0B4A8F63", "50p2hsbcnaasp")] // erfunden (GUID-Form wie bei Store-Spielen), Wert unabhängig nachgerechnet
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "8wekyb3d8bbwe")]
    [InlineData("CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "cw5n1h2txyewy")]
    [InlineData("CN=D6816951-877F-493B-B4EE-41AB9419C326", "56jybvy8sckqj")]
    [InlineData("CN=Contoso Spiele GmbH, O=Contoso Spiele GmbH, L=Berlin, C=DE", "t69f6hpx382ka")]
    public void Publisher_id_matches_windows(string publisher, string expected)
    {
        Assert.Equal(expected, PackageIdentity.ComputePublisherId(publisher));
    }

    [Fact]
    public void Publisher_id_is_case_sensitive_like_windows()
    {
        Assert.NotEqual(
            PackageIdentity.ComputePublisherId("CN=3F2A7C19-6B4D-4E8A-9C21-5D7E0B4A8F63"),
            PackageIdentity.ComputePublisherId("cn=3f2a7c19-6b4d-4e8a-9c21-5d7e0b4a8f63"));
    }

    [Fact]
    public void Family_name_joins_name_and_publisher_id()
    {
        Assert.Equal(
            "Contoso.Wolkenhof_50p2hsbcnaasp",
            PackageIdentity.FamilyName("Contoso.Wolkenhof", "CN=3F2A7C19-6B4D-4E8A-9C21-5D7E0B4A8F63"));
    }

    [Theory]
    [InlineData("Contoso.Wolkenhof", true)]
    [InlineData("Microsoft.624F8B84B80", true)]
    [InlineData("A-b.9", true)]
    [InlineData("ab", false)]
    [InlineData("Mit Leerzeichen", false)]
    [InlineData("Unter_strich", false)]
    [InlineData("Ümlaut.Paket", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Package_name_validation(string? name, bool expected) => Assert.Equal(expected, PackageIdentity.IsValidPackageName(name));

    [Theory]
    [InlineData("AppHofShipping", true)]
    [InlineData("Game", true)]
    [InlineData("App.Editor2", true)]
    [InlineData("1App", false)]
    [InlineData("App Id", false)]
    [InlineData("App!Id", false)]
    [InlineData("", false)]
    public void Application_id_validation(string id, bool expected) => Assert.Equal(expected, PackageIdentity.IsValidApplicationId(id));

    [Theory]
    [InlineData("ms-resource:AppDisplayName", "ms-resource://Pkg.Name/Resources/AppDisplayName")]
    [InlineData("ms-resource:Strings/Title", "ms-resource://Pkg.Name/Strings/Title")]
    [InlineData("ms-resource:/Resources/Title", "ms-resource://Pkg.Name/Resources/Title")]
    [InlineData("ms-resource:///Resources/Title", "ms-resource://Pkg.Name/Resources/Title")]
    [InlineData("ms-resource://Other.Pkg/Resources/Title", "ms-resource://Other.Pkg/Resources/Title")]
    [InlineData("MS-RESOURCE:AppDisplayName", "ms-resource://Pkg.Name/Resources/AppDisplayName")]
    public void Ms_resource_uri_is_completed_with_package_name(string value, string expected)
    {
        Assert.True(MsResource.IsReference(value));
        Assert.Equal(expected, MsResource.ToUri(value, "Pkg.Name"));
    }

    [Fact]
    public void Ms_resource_indirect_string_has_pri_path_and_uri()
    {
        Assert.Equal(
            @"@{C:\XboxGames\Wolkenhof\Content\resources.pri?ms-resource://Contoso.Wolkenhof/Resources/AppDisplayName}",
            MsResource.ToIndirectString(@"C:\XboxGames\Wolkenhof\Content\resources.pri", "ms-resource:AppDisplayName", "Contoso.Wolkenhof"));
        Assert.False(MsResource.IsReference("Wolkenhof"));
        Assert.False(MsResource.IsReference(null));
    }

    [Fact]
    public void Shell_resolver_returns_null_for_missing_pri_and_empty_input()
    {
        Assert.Null(ShellIndirectStringResolver.Instance.Resolve(string.Empty));
        Assert.Null(ShellIndirectStringResolver.Instance.Resolve(
            @"@{Z:\HdrPilot-Test-gibt-es-nicht\resources.pri?ms-resource://Pkg.Name/Resources/AppDisplayName}"));
    }

    [Fact]
    public void Config_parser_reads_identity_display_name_and_executables()
    {
        var config = MicrosoftGameConfig.Parse(Fixture(@"Beispielspiel\MicrosoftGame.config"));

        Assert.Equal("Contoso.Beispielspiel", config.IdentityName);
        Assert.Equal("CN=Contoso Spiele GmbH, O=Contoso Spiele GmbH, L=Berlin, C=DE", config.IdentityPublisher);
        Assert.Equal("2.3.104.0", config.IdentityVersion);
        Assert.Equal("ms-resource:ApplicationDisplayName", config.DefaultDisplayName);
        Assert.Equal(3, config.Executables.Count);
        Assert.Equal([true, true, false], config.Executables.Select(e => e.IsForPc));
        Assert.Equal(@"Tools\BeispielBenchmark.exe", config.Executables[1].Name);
        Assert.Equal("ms-resource:BenchmarkDisplayName", config.Executables[1].OverrideDisplayName);
    }

    [Fact]
    public void Manifest_parser_reads_namespaced_identity_and_applications()
    {
        var manifest = AppxManifest.Parse(Fixture(@"Wolkenhof\appxmanifest.xml"));

        Assert.Equal("Contoso.Wolkenhof", manifest.IdentityName);
        Assert.Equal("Wolkenhof", manifest.DisplayName);
        var app = Assert.Single(manifest.Applications);
        Assert.Equal("AppHofShipping", app.Id);
        Assert.Equal("GameLaunchHelper.exe", app.Executable);
    }

    [Fact]
    public void Dlc_markers_are_read_from_config_and_manifest()
    {
        var config = MicrosoftGameConfig.Parse(Fixture(@"Zusatzinhalt\MicrosoftGame.config"));
        var manifest = AppxManifest.Parse(Fixture(@"Zusatzinhalt\appxmanifest.xml"));

        Assert.True(config.IsDownloadableContent);
        Assert.Empty(config.Executables);
        Assert.Equal("Contoso.Beispielspiel", manifest.MainPackageDependency);
        Assert.Empty(manifest.Applications);

        Assert.False(MicrosoftGameConfig.Parse(Fixture(@"Wolkenhof\MicrosoftGame.config")).IsDownloadableContent);
        Assert.False(MicrosoftGameConfig.Parse(Fixture(@"Beispielspiel\MicrosoftGame.config")).IsDownloadableContent);
        Assert.Null(AppxManifest.Parse(Fixture(@"Wolkenhof\appxmanifest.xml")).MainPackageDependency);
    }

    [Fact]
    public void Broken_xml_with_dtd_is_rejected()
    {
        var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE Game [<!ENTITY x \"y\">]><Game><Identity Name=\"A.B\" Publisher=\"CN=x\" /></Game>");

        var ex = Assert.Throws<InvalidDataException>(() => MicrosoftGameConfig.Parse(xml));
        Assert.Contains("kein gültiges XML", ex.Message, StringComparison.Ordinal);
    }

    private static byte[] Fixture(string relative) => File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, "Xbox", relative));
}
