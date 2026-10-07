using System.Text;
using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Core.Games.Scanners.Steam;
using HdrPilot.Core.Games.Scanners.Ubisoft;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.Tests.Scanners;

/// <summary>
/// Robustheit der Steam- und Ubisoft-Leser gegen zufällig beschädigte Dateien (Bits gekippt, abgeschnitten, Bytes
/// eingefügt oder gelöscht). Erlaubt sind nur die dokumentierten Fehlerarten; ein Scan darf nie werfen.
/// Die Zufallsfolgen sind fest (Seed), damit ein Fehlschlag reproduzierbar ist.
/// </summary>
public sealed class SteamUbisoftRobustnessTests
{
    private const int Rounds = 1500;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Broken_fuzzed_keyvalues_text_only_throws_keyvalues_exception()
    {
        var sources = new[] { "libraryfolders.vdf", "appmanifest_9990110.acf", "appmanifest_228980.acf", "libraryfolders.old-format.vdf" }
            .Select(f => File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, "Steam", f)))
            .ToList();
        var random = new Random(20261007);
        for (var i = 0; i < Rounds; i++)
        {
            var bytes = Mutate(sources[i % sources.Count], random);
            var text = SteamTextDecoder.Decode(bytes, out _);
            try
            {
                KeyValuesParser.Parse(text);
            }
            catch (KeyValuesException)
            {
            }
        }
    }

    [Fact]
    public void Broken_fuzzed_appinfo_only_throws_invalid_data()
    {
        var sources = new[] { SteamAppInfoBuilder.V27, SteamAppInfoBuilder.V28, SteamAppInfoBuilder.V29 }
            .Select(m => new SteamAppInfoBuilder(m).Add(228980, "Tool", "Steamworks Common Redistributables").Add(9990110, "Game", "STERNENWACHT™ 2").Add(7, null, null).Build())
            .ToList();
        var wanted = new HashSet<uint> { 228980, 9990110, 7 };
        var random = new Random(4711);
        for (var i = 0; i < Rounds; i++)
        {
            var bytes = Mutate(sources[i % sources.Count], random);
            try
            {
                SteamAppInfoReader.Read(bytes, wanted, Ct);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void Broken_fuzzed_ubisoft_cache_and_yaml_only_report_errors()
    {
        var source = new UbisoftCacheBuilder().AddFixture(420).AddFixture(1081).AddFixture(4740).Build();
        var wanted = new HashSet<uint> { 420, 1081, 4740 };
        var random = new Random(1081);
        for (var i = 0; i < Rounds; i++)
        {
            var yamls = UbisoftConfigurationCache.Read(Mutate(source, random), wanted, out _);
            foreach (var yaml in yamls.Values)
            {
                try
                {
                    UbisoftProductConfig.FromYaml(yaml);
                }
                catch (FormatException)
                {
                }
            }
        }
    }

    [Fact]
    public void Broken_fuzzed_yaml_text_only_throws_format_exception()
    {
        var sources = new uint[] { 420, 1081, 4740 }
            .Select(id => File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, "Ubisoft", id + ".yml")))
            .ToList();
        var random = new Random(4740);
        for (var i = 0; i < Rounds; i++)
        {
            var yaml = Encoding.UTF8.GetString(Mutate(sources[i % sources.Count], random));
            try
            {
                UbisoftProductConfig.FromYaml(yaml);
            }
            catch (FormatException)
            {
            }
        }
    }

    [Fact]
    public void Broken_fuzzed_steam_files_never_make_the_scan_throw()
    {
        var library = File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, "Steam", "libraryfolders.vdf"));
        var manifest = File.ReadAllBytes(Path.Combine(InMemoryFileSystem.FixturesRoot, "Steam", "appmanifest_9990110.acf"));
        var appInfo = new SteamAppInfoBuilder().Add(9990110, "Game", "STERNENWACHT™ 2").Build();
        var random = new Random(9990110);
        for (var i = 0; i < 300; i++)
        {
            var fs = new InMemoryFileSystem()
                .AddFile(@"C:\Steam\steam.exe", "MZ")
                .AddFile(@"C:\Steam\steamapps\libraryfolders.vdf", Mutate(library, random))
                .AddFile(@"C:\Steam\steamapps\appmanifest_9990110.acf", Mutate(manifest, random))
                .AddFile(@"C:\Steam\appcache\appinfo.vdf", Mutate(appInfo, random))
                .AddDirectory(@"C:\Steam\steamapps\common\Sternenwacht 2");
            var registry = new FakeRegistry().Set(RegistryHiveKind.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/steam");

            var result = new SteamScanner(fs, registry, new FakeEnvironment()).Scan(ScanOptions.Default, Ct);

            Assert.True(result.LauncherDetected);
            Assert.All(result.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Path)));
            Assert.All(result.Games, g => Assert.True(PathUtilIsUnder(g.InstallDir, @"C:\Steam\steamapps\common")));
        }
    }

    [Fact]
    public void Broken_fuzzed_ubisoft_cache_never_makes_the_scan_throw()
    {
        var cache = new UbisoftCacheBuilder().AddFixture(420).AddFixture(4740).Build();
        var random = new Random(420);
        for (var i = 0; i < 300; i++)
        {
            var fs = new InMemoryFileSystem()
                .AddFile(@"C:\Ubi\cache\configuration\configurations", Mutate(cache, random))
                .AddFile(@"D:\Ubisoft-Spiele\AFOP\afop.exe", "MZ")
                .AddFile(@"E:\FC4\bin\FarCry4.exe", "MZ");
            var registry = new FakeRegistry()
                .Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Ubisoft\Launcher", "InstallDir", @"C:\Ubi\", RegistryViewKind.Registry32)
                .Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Ubisoft\Launcher\Installs\4740", "InstallDir", "D:/Ubisoft-Spiele/AFOP/", RegistryViewKind.Registry32)
                .Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Ubisoft\Launcher\Installs\420", "InstallDir", @"E:\FC4", RegistryViewKind.Registry32);

            var result = new UbisoftScanner(fs, registry, new FakeEnvironment()).Scan(ScanOptions.Default, Ct);

            Assert.Equal(["420", "4740"], result.Games.Select(g => g.SourceId));
            Assert.All(result.Games, g => Assert.All(g.Executables, e => Assert.True(PathUtilIsUnder(e, g.InstallDir))));
        }
    }

    private static bool PathUtilIsUnder(string path, string directory) => HdrPilot.Core.Util.PathUtil.IsUnder(path, directory);

    /// <summary>Eine bis drei zufällige Beschädigungen: Bits kippen, abschneiden, Bytes einfügen oder löschen.</summary>
    private static byte[] Mutate(byte[] source, Random random)
    {
        var data = new List<byte>(source);
        var count = random.Next(1, 4);
        for (var n = 0; n < count && data.Count > 0; n++)
        {
            var pos = random.Next(data.Count);
            switch (random.Next(5))
            {
                case 0:
                    data[pos] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    data.RemoveRange(pos, data.Count - pos);
                    break;
                case 2:
                    data.Insert(pos, (byte)random.Next(256));
                    break;
                case 3:
                    data.RemoveAt(pos);
                    break;
                default:
                    data[pos] = (byte)random.Next(256);
                    break;
            }
        }

        return [.. data];
    }
}
