using HdrPilot.Core.Abstractions;
using HdrPilot.Core.Games;
using HdrPilot.Core.Games.Scanners;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.Scanners;

public sealed class UbisoftScannerTests
{
    private const string LauncherKey = @"SOFTWARE\Ubisoft\Launcher";
    private const string InstallsKey = LauncherKey + @"\Installs";
    private const string LauncherDir = @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher";
    private const string CachePath = LauncherDir + @"\cache\configuration\configurations";
    private const string Afop = @"D:\Ubisoft-Spiele\AFOP";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeEnvironment _environment = new();
    private readonly ListLogger<UbisoftScanner> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Fehlender Launcher ----------

    [Fact]
    public void Missing_launcher_returns_empty_without_errors()
    {
        var result = Scan();

        Assert.Equal(GameSource.Ubisoft, result.Source);
        Assert.Equal("Ubisoft Connect", result.ScannerName);
        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Missing_launcher_in_32bit_view_is_not_detected_even_if_64bit_key_exists()
    {
        // Ubisoft Connect ist 32 Bit und schreibt nach WOW6432Node; ein Schlüssel nur in der 64-Bit-Sicht zählt nicht.
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\4740", "InstallDir", "D:/Ubisoft-Spiele/AFOP/", RegistryViewKind.Registry64);
        _fs.AddDirectory(Afop);

        var result = Scan();

        Assert.False(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Launcher_without_installs_key_is_detected_without_games()
    {
        WithLauncher();

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Games);
        Assert.Empty(result.Errors);
    }

    // ---------- Positive Fälle ----------

    [Fact]
    public void Finds_installed_game_with_name_from_product_cache_and_exact_launch_command()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();
        AddCache(new UbisoftCacheBuilder().AddFixture(1081).AddFixture(4740));

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal(GameSource.Ubisoft, game.Source);
        Assert.Equal("4740", game.SourceId);
        Assert.Equal("Ubisoft:4740", game.Key);
        Assert.Equal("Avatar Frontiers of Pandora™", game.Name);
        Assert.Equal(@"D:\Ubisoft-Spiele\AFOP", game.InstallDir);
        Assert.Equal([@"D:\Ubisoft-Spiele\AFOP\afop.exe", @"D:\Ubisoft-Spiele\AFOP\afop_plus.exe"], game.Executables);
        Assert.Equal("uplay://launch/4740/0", game.LaunchCommand);
        Assert.Null(game.LaunchArguments);
        Assert.Null(game.LaunchWorkingDirectory);
    }

    [Fact]
    public void Installs_written_under_wow6432node_are_read_through_32bit_view()
    {
        // So steht es am Zielrechner: HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\4740.
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir", LauncherDir + @"\");
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\4740", "InstallDir", "D:/Ubisoft-Spiele/AFOP/");
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\4740", "Language", "de-DE");
        _fs.AddDirectory(Afop);

        var game = Assert.Single(Scan().Games);

        Assert.Equal(@"D:\Ubisoft-Spiele\AFOP", game.InstallDir);
    }

    [Fact]
    public void Install_dir_value_name_is_case_insensitive_like_windows()
    {
        WithLauncher();
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\4740", "installdir", @"D:\Ubisoft-Spiele\AFOP", RegistryViewKind.Registry32);
        _fs.AddDirectory(Afop);

        Assert.Equal(@"D:\Ubisoft-Spiele\AFOP", Assert.Single(Scan().Games).InstallDir);
    }

    [Fact]
    public void Name_from_localization_key_and_executables_relative_to_working_directory()
    {
        WithLauncher();
        AddInstall(420, @"E:\Spiele\Far Cry 4\");
        _fs.AddFile(@"E:\Spiele\Far Cry 4\bin\FarCry4.exe", "MZ"); // IGE_WPF64.exe (Karteneditor) fehlt
        AddCache(new UbisoftCacheBuilder().AddFixture(420));

        var game = Assert.Single(Scan().Games);

        Assert.Equal("Far Cry® 4", game.Name);
        Assert.Equal(@"E:\Spiele\Far Cry 4", game.InstallDir);
        Assert.Equal([@"E:\Spiele\Far Cry 4\bin\FarCry4.exe"], game.Executables);
        Assert.Equal("uplay://launch/420/0", game.LaunchCommand);
    }

    [Fact]
    public void Executables_fall_back_to_install_dir_when_missing_in_working_directory()
    {
        WithLauncher();
        AddInstall(420, @"E:\Spiele\FC4");
        _fs.AddFile(@"E:\Spiele\FC4\FarCry4.exe", "MZ");
        AddCache(new UbisoftCacheBuilder().AddFixture(420));

        Assert.Equal([@"E:\Spiele\FC4\FarCry4.exe"], Assert.Single(Scan().Games).Executables);
    }

    [Fact]
    public void Executables_are_empty_when_cache_names_none()
    {
        WithLauncher();
        AddInstall(1081, @"E:\Spiele\Shadows");
        _fs.AddFile(@"E:\Spiele\Shadows\ACShadows.exe", "MZ");
        AddCache(new UbisoftCacheBuilder().AddFixture(1081));

        var game = Assert.Single(Scan().Games);

        Assert.Equal("Assassin’s Creed Shadows", game.Name);
        Assert.Empty(game.Executables);
    }

    [Fact]
    public void Install_dir_with_umlauts_and_trailing_backslash_is_normalized()
    {
        WithLauncher();
        AddInstall(5266, @"E:\Spiele\Für Honor & Ruhm\");
        _fs.AddFile(@"E:\Spiele\Für Honor & Ruhm\ForHonor.exe", "MZ");
        AddCache(new UbisoftCacheBuilder().Add(5266, "version: 2.0\nroot:\n  name: Für Honor\n  start_game:\n    online:\n      executables:\n      - path:\n          relative: ForHonor.exe\n"));

        var game = Assert.Single(Scan().Games);

        Assert.Equal(@"E:\Spiele\Für Honor & Ruhm", game.InstallDir);
        Assert.Equal("Für Honor", game.Name);
        Assert.Equal([@"E:\Spiele\Für Honor & Ruhm\ForHonor.exe"], game.Executables);
    }

    [Fact]
    public void Executables_with_leading_backslash_are_relative_to_install_dir()
    {
        // Gemessen im Produkt-Cache (Watch Dogs 2): relative: '\bin\WatchDogs2.exe', trial: '\bin_plus\WatchDogs2.exe', append: ''.
        WithLauncher();
        AddInstall(3353, @"E:\Ubisoft\Watch Dogs 2");
        _fs.AddFile(@"E:\Ubisoft\Watch Dogs 2\bin\WatchDogs2.exe", "MZ");
        _fs.AddFile(@"E:\Ubisoft\Watch Dogs 2\bin_plus\WatchDogs2.exe", "MZ");
        AddCache(new UbisoftCacheBuilder().Add(3353, """
            version: 2.0
            root:
              name: l1
              start_game:
                online:
                  executables:
                  - shortcut_name: WATCH_DOGS® 2
                    path:
                      relative: '\bin\WatchDogs2.exe'
                    trial:
                      relative: '\bin_plus\WatchDogs2.exe'
                      arguments: -eac_launcher_settings settings_plus.json
                    working_directory:
                      register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\WatchDogs2\InstallDir
                      append: ''
            localizations:
              default:
                l1: WATCH_DOGS® 2
            """));

        var game = Assert.Single(Scan().Games);

        Assert.Equal("WATCH_DOGS® 2", game.Name);
        Assert.Equal([@"E:\Ubisoft\Watch Dogs 2\bin\WatchDogs2.exe", @"E:\Ubisoft\Watch Dogs 2\bin_plus\WatchDogs2.exe"], game.Executables);
    }

    [Fact]
    public void Executables_outside_install_dir_are_ignored()
    {
        WithLauncher();
        AddInstall(9001, @"E:\Spiele\Boese");
        _fs.AddFile(@"C:\Windows\notepad.exe", "MZ");
        _fs.AddDirectory(@"E:\Spiele\Boese");
        AddCache(new UbisoftCacheBuilder().Add(9001, """
            version: 2.0
            root:
              name: Böse
              start_game:
                online:
                  executables:
                  - path:
                      relative: ..\..\..\Windows\notepad.exe
                  - path:
                      relative: C:\Windows\notepad.exe
                  - path:
                      relative: game.exe
                    working_directory:
                      append: ..\..\..\Windows
            """));

        var game = Assert.Single(Scan().Games);

        Assert.Equal("Böse", game.Name);
        Assert.Empty(game.Executables);
    }

    [Fact]
    public void Name_falls_back_to_uninstall_display_name_then_folder_name()
    {
        WithLauncher();
        AddInstall(4740, @"D:\Spielordner\AvatarOrdner");
        _fs.AddDirectory(@"D:\Spielordner\AvatarOrdner");
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install 4740", "DisplayName", "AFOP", RegistryViewKind.Registry32);

        Assert.Equal("AFOP", Assert.Single(Scan().Games).Name);

        var withoutUninstall = new FakeRegistry()
            .Set(RegistryHiveKind.LocalMachine, LauncherKey, "InstallDir", LauncherDir + @"\", RegistryViewKind.Registry32)
            .Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\4740", "InstallDir", @"D:\Spielordner\AvatarOrdner", RegistryViewKind.Registry32);
        Assert.Equal("AvatarOrdner", Assert.Single(new UbisoftScanner(_fs, withoutUninstall, _environment).Scan(ScanOptions.Default, Ct).Games).Name);
    }

    [Fact]
    public void Unresolved_localization_key_is_not_used_as_name()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);
        AddCache(new UbisoftCacheBuilder().Add(4740, "version: 2.0\nroot:\n  name: l1\nlocalizations: {}\n"));

        Assert.Equal("AFOP", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Launcher_dir_falls_back_to_program_files_x86_from_environment()
    {
        _registry.AddKey(RegistryHiveKind.LocalMachine, LauncherKey, RegistryViewKind.Registry32);
        _environment.Folders[Environment.SpecialFolder.ProgramFilesX86] = @"E:\Programme (x86)";
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();
        _fs.AddFile(@"E:\Programme (x86)\Ubisoft\Ubisoft Game Launcher\cache\configuration\configurations", new UbisoftCacheBuilder().AddFixture(4740).Build());

        Assert.Equal("Avatar Frontiers of Pandora™", Assert.Single(Scan().Games).Name);
    }

    [Fact]
    public void Launcher_dir_with_wrong_registry_type_falls_back_to_program_files_x86()
    {
        _registry.Set(RegistryHiveKind.LocalMachine, LauncherKey, "InstallDir", 13225, RegistryViewKind.Registry32);
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();
        AddCache(new UbisoftCacheBuilder().AddFixture(4740));

        var result = Scan();

        Assert.True(result.LauncherDetected);
        Assert.Empty(result.Errors);
        Assert.Equal("Avatar Frontiers of Pandora™", Assert.Single(result.Games).Name);
    }

    [Fact]
    public void Relative_launcher_dir_in_registry_falls_back_to_program_files_x86()
    {
        _registry.Set(RegistryHiveKind.LocalMachine, LauncherKey, "InstallDir", "Ubisoft Game Launcher", RegistryViewKind.Registry32);
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();
        AddCache(new UbisoftCacheBuilder().AddFixture(4740)); // FakeEnvironment: Programme (x86) = C:\Program Files (x86)

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal("Avatar Frontiers of Pandora™", Assert.Single(result.Games).Name);
    }

    [Fact]
    public void Games_are_ordered_by_product_id_and_duplicate_folders_returned_once()
    {
        WithLauncher();
        AddInstall(5266, @"D:\Ubisoft-Spiele\AFOP");
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddInstall(420, @"E:\FC4");
        _fs.AddDirectory(Afop).AddDirectory(@"E:\FC4");

        var result = Scan();

        Assert.Equal(["420", "4740"], result.Games.Select(g => g.SourceId));
        Assert.Empty(result.Errors);
    }

    // ---------- Unvollständige Installationen ----------

    [Fact]
    public void Incomplete_install_without_InstallDir_is_skipped_without_error()
    {
        WithLauncher();
        // Gemessen: Installs\1081 (Assassin's Creed Shadows über Steam) hat nur "language".
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\1081", "language", "de-DE", RegistryViewKind.Registry32);
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["4740"], result.Games.Select(g => g.SourceId));
        Assert.True(_logger.Contains(LogLevel.Information, "1081"));
    }

    [Fact]
    public void Incomplete_missing_game_folder_is_skipped_without_error()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddInstall(5266, @"E:\Ubisoft\Far Cry 6"); // Ordner fehlt
        _fs.AddDirectory(Afop);

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["4740"], result.Games.Select(g => g.SourceId));
        Assert.True(_logger.Contains(LogLevel.Information, "Far Cry 6"));
    }

    [Fact]
    public void Incomplete_empty_InstallDir_is_skipped_without_error()
    {
        WithLauncher();
        AddInstall(4740, "   ");

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Empty(result.Games);
    }

    [Fact]
    public void Incomplete_install_dir_on_missing_drive_is_skipped_without_error()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddInstall(5266, "Z:/Ubisoft/Far Cry 6/"); // Laufwerk gibt es nicht (etwa abgezogene USB-Platte)
        _fs.AddDirectory(Afop);

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal(["4740"], result.Games.Select(g => g.SourceId));
    }

    [Fact]
    public void Incomplete_empty_product_cache_uses_fallback_names_without_error()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();
        _fs.AddFile(CachePath, Array.Empty<byte>());
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install 4740", "DisplayName", "AFOP", RegistryViewKind.Registry32);

        var result = Scan();

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal("AFOP", game.Name);
        Assert.Empty(game.Executables);
    }

    [Fact]
    public void Incomplete_missing_product_cache_uses_fallback_names_without_error()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();

        var result = Scan();

        Assert.Empty(result.Errors);
        var game = Assert.Single(result.Games);
        Assert.Equal("AFOP", game.Name);
        Assert.Empty(game.Executables);
    }

    // ---------- Defekte Daten ----------

    [Fact]
    public void Broken_product_cache_with_binary_garbage_is_reported_and_games_still_returned()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);
        var garbage = new byte[2048];
        new Random(42).NextBytes(garbage);
        garbage[0] = 0xFF; // sicher kein gültiger Feldkopf
        garbage[1] = 0xFF;
        _fs.AddFile(CachePath, garbage);

        var result = Scan();

        Assert.Equal("AFOP", Assert.Single(result.Games).Name);
        var error = Assert.Single(result.Errors);
        Assert.Equal(CachePath, error.Path);
        Assert.Contains("beschädigt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_truncated_product_cache_keeps_entries_before_the_damage()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddInstall(420, @"E:\FC4");
        _fs.AddDirectory(Afop).AddDirectory(@"E:\FC4");
        var data = new UbisoftCacheBuilder().AddFixture(4740).AddFixture(420).Build();
        _fs.AddFile(CachePath, data[..(data.Length - 100)]);

        var result = Scan();

        // 420 steht hinter der Schadstelle: Name fällt auf den Ordner zurück; 4740 davor wurde noch gelesen.
        Assert.Equal(["FC4", "Avatar Frontiers of Pandora™"], result.Games.Select(g => g.Name));
        Assert.Contains("abgeschnitten", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_unreadable_product_cache_is_reported()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);
        AddCache(new UbisoftCacheBuilder().AddFixture(4740));
        _fs.FailReading(CachePath, new IOException("Die Datei wird von einem anderen Prozess verwendet."));

        var result = Scan();

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Contains("nicht lesbar", error.Message, StringComparison.Ordinal);
        Assert.Equal(CachePath, error.Path);
    }

    [Fact]
    public void Broken_too_deep_yaml_entry_is_reported_and_game_kept()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);
        var deep = string.Concat(Enumerable.Range(0, 100).Select(i => new string(' ', i * 2) + "k" + i + ":\n"));
        AddCache(new UbisoftCacheBuilder().Add(4740, deep));

        var result = Scan();

        Assert.Equal("AFOP", Assert.Single(result.Games).Name);
        Assert.Contains("4740", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1234)]
    [InlineData(new byte[] { 0x44, 0x3A })]
    public void Broken_install_dir_with_wrong_value_type_is_reported(object value)
    {
        WithLauncher();
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\5266", "InstallDir", value, RegistryViewKind.Registry32);
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);

        var result = Scan();

        Assert.Equal(["4740"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains("unerwarteten Typ", error.Message, StringComparison.Ordinal);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\5266", error.Path);
    }

    [Theory]
    [InlineData(4740)]
    [InlineData(new byte[] { 0x41, 0x00 })]
    public void Broken_uninstall_display_name_with_wrong_value_type_is_not_used_as_name(object value)
    {
        // Ohne Produkt-Cache: Ein DWORD als DisplayName ergäbe sonst den "Namen" 4740.
        WithLauncher();
        AddInstall(4740, @"D:\Spielordner\AvatarOrdner");
        _fs.AddDirectory(@"D:\Spielordner\AvatarOrdner");
        _registry.Set(RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install 4740", "DisplayName", value, RegistryViewKind.Registry32);

        var result = Scan();

        Assert.Empty(result.Errors);
        Assert.Equal("AvatarOrdner", Assert.Single(result.Games).Name);
    }

    [Theory]
    [InlineData("D:/")]
    [InlineData(@"D:\")]
    [InlineData("D:")]
    [InlineData(@"\\nas\spiele\")]
    public void Broken_install_dir_at_drive_root_is_reported_and_not_returned(string installDir)
    {
        // Ein Laufwerk als Installationsordner machte in Phase 3 jeden Prozess auf dem Laufwerk zum Spielprozess.
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddInstall(5266, installDir);
        _fs.AddDirectory(Afop).AddDirectory(@"\\nas\spiele\Far Cry 6");

        var result = Scan();

        Assert.Equal(["4740"], result.Games.Select(g => g.SourceId));
        var error = Assert.Single(result.Errors);
        Assert.Contains("Laufwerk", error.Message, StringComparison.Ordinal);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\5266", error.Path);
    }

    [Fact]
    public void Broken_non_numeric_install_id_is_reported()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\kaputt", "InstallDir", @"E:\x", RegistryViewKind.Registry32);
        _fs.AddDirectory(Afop);

        var result = Scan();

        Assert.Single(result.Games);
        var error = Assert.Single(result.Errors);
        Assert.Contains("kaputt", error.Message, StringComparison.Ordinal);
        Assert.EndsWith(@"Installs\kaputt", error.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_unreadable_install_key_is_reported()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddInstall(5266, @"E:\FC6");
        _registry.DenyAccess(RegistryHiveKind.LocalMachine, InstallsKey + @"\5266", RegistryViewKind.Registry32);
        _fs.AddDirectory(Afop).AddDirectory(@"E:\FC6");

        var result = Scan();

        Assert.Equal(["4740"], result.Games.Select(g => g.SourceId));
        Assert.Contains("nicht lesbar", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_relative_install_dir_is_reported()
    {
        WithLauncher();
        AddInstall(4740, @"Ubisoft\AFOP");

        var result = Scan();

        Assert.Empty(result.Games);
        Assert.Contains("kein absoluter Pfad", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    // ---------- Allgemeines ----------

    [Fact]
    public void Scan_honors_cancellation()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        _fs.AddDirectory(Afop);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => CreateScanner().Scan(ScanOptions.Default, cts.Token));
    }

    [Fact]
    public void Scanner_is_found_by_catalog()
    {
        Assert.Contains(typeof(UbisoftScanner), ScannerCatalog.ScannerTypes);
        Assert.Contains(
            ScannerCatalog.CreateAll(new InMemoryFileSystem(), new FakeRegistry(), new FakeEnvironment()),
            s => s is UbisoftScanner && s.Source == GameSource.Ubisoft);
    }

    [Fact]
    public void Scanner_does_not_write_registry_or_files()
    {
        WithLauncher();
        AddInstall(4740, "D:/Ubisoft-Spiele/AFOP/");
        AddAfopFiles();
        AddCache(new UbisoftCacheBuilder().AddFixture(4740));
        var spy = new SteamUbisoftGuards.WriteSpyFileSystem(_fs);

        var result = new UbisoftScanner(spy, new SteamUbisoftGuards.ReadOnlyRegistry(_registry), _environment).Scan(ScanOptions.Default, Ct);

        Assert.Single(result.Games);
        Assert.Equal(0, spy.Writes);
    }

    // ---------- Hilfen ----------

    private ScanResult Scan() => CreateScanner().Scan(ScanOptions.Default, Ct);

    private UbisoftScanner CreateScanner() => new(_fs, _registry, _environment, _logger);

    /// <summary>Launcher-Schlüssel wie am Zielrechner (32-Bit-Sicht, InstallDir mit abschließendem Backslash).</summary>
    private void WithLauncher()
    {
        _registry.Set(RegistryHiveKind.LocalMachine, LauncherKey, "InstallDir", LauncherDir + @"\", RegistryViewKind.Registry32);
        _registry.Set(RegistryHiveKind.LocalMachine, LauncherKey, "Version", "13225", RegistryViewKind.Registry32);
        _registry.AddKey(RegistryHiveKind.LocalMachine, InstallsKey, RegistryViewKind.Registry32);
        _fs.AddFile(LauncherDir + @"\UbisoftConnect.exe", "MZ");
    }

    private void AddInstall(uint id, string installDir)
    {
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\" + id, "Language", "de-DE", RegistryViewKind.Registry32);
        _registry.Set(RegistryHiveKind.LocalMachine, InstallsKey + @"\" + id, "InstallDir", installDir, RegistryViewKind.Registry32);
    }

    private void AddAfopFiles()
    {
        _fs.AddFile(Afop + @"\afop.exe", "MZ");
        _fs.AddFile(Afop + @"\afop_plus.exe", "MZ");
        _fs.AddFile(Afop + @"\Tools\vc_redist\VC_redist.x64.exe", "MZ");
        _fs.AddFile(Afop + @"\uplay_install.state", "x");
    }

    private void AddCache(UbisoftCacheBuilder builder) => _fs.AddFile(CachePath, builder.Build());
}
