# Ausschlussliste für Hilfsprozesse

Klasse `HdrPilot.Core.Games.HelperExecutables`. In Phase 3 gilt jeder Prozess als Spiel, dessen EXE unter dem
Installationsordner eines Spiels liegt, außer er passt auf diese Liste. Dieselbe Liste filtert die EXE-Vorschläge
bei manuellen Ordnern und Suchordner-Kandidaten.

## Regeln für Muster

- Glob über den **Dateinamen**: `*` beliebig viele Zeichen, `?` genau ein Zeichen; ohne Groß-/Kleinschreibung.
- Die Endung `.exe` ist bei Name und Muster gleichgültig (`dxsetup` passt auf `DXSETUP.exe`).
- Ein Pfad als Eingabe ist erlaubt (`\`, `/`, Anführungszeichen); verglichen wird nur der Dateiname.
- Muster mit Ordnertrennern passen nie; leere Einträge werden übersprungen. `IsHelper` wirft nicht.
- `IsHelper(name)` nutzt `DefaultPatterns`, `IsHelper(name, eigeneListe)` nur die eigene Liste (für die spätere
  Einstellung „pflegbare Liste“).

## Abgleich am Zielrechner (2026-10-07, nur lesend)

Aufgelistet wurden alle EXE-Dateien (rekursiv) in den Spielordnern aller Launcher, ermittelt aus den Quellen selbst:
Steam (3 Bibliotheken, 637 Ordner aus `appmanifest_*.acf`), Epic (7 aus `*.item`), Xbox (1), GOG (6 Einträge, 5 Ordner),
Ubisoft (1 mit Ordner), EA (Battlefield 6 über `__Installer\installerdata.xml`). Battle.net: keine Spiele gefunden.

| Kennzahl | Wert |
|---|---|
| Einträge der Quellen | 653 (652 verschiedene Ordner: Cyberpunk 2077 und Phantom Liberty teilen sich bei GOG einen Ordner) |
| EXE-Dateien | 2110 (1237 verschiedene Namen) |
| davon als Hilfsprogramm erkannt | 797 Dateien, 86 verschiedene Namen |
| Spielordner, in denen danach **keine** Spiel-EXE übrig bleibt | genau einer: „Steamworks Common Redistributables“ (AppID 228980, nur Redistributables, kein Spiel) |
| Ordner ganz ohne EXE | 1: ein nicht heruntergeladener Steam-Playtest („The Midnight Walkers Playtest“) |

Nachprüfung im Review (2026-10-07): Inventar unabhängig neu erstellt (Steam-`appmanifest_*.acf` aller drei Bibliotheken,
Epic-`.item`, GOG- und Ubisoft-Registry, `C:\XboxGames\Wolkenhof\Content`, EA über `__Installer\installerdata.xml`) und
mit der gebauten `HdrPilot.Core.dll` klassifiziert: 653 Einträge, 2110 EXE, 1237 Namen, 797 Treffer mit 86 Namen –
alle Zahlen bestätigt. Die frühere Angabe „2 Einträge ganz ohne EXE“ war ungenau (der zweite war nur der doppelte
Cyberpunk-Ordner) und ist oben korrigiert. Alle 86 Treffernamen wurden einzeln mit Dateibeschreibung durchgesehen:
kein Spiel darunter. Bei den fünf `launcher.exe` liegt jeweils eine eigene Spiel-EXE daneben (GRAVEN `Praest.exe` und
`Praest-Win64-Shipping.exe`, HYPERVIOLENT `HYPERVIOLENT.exe`, Forgive Me Father 2 `FMF2-Win64-Shipping.exe`,
The Riftbreaker `riftbreaker_win_release.exe`).

Jedes Muster passt auf mindestens ein Beispiel (Test `Every_default_pattern_matches_at_least_one_real_example`);
die Beispiele stammen bis auf `BEService*` und `CrashHandler*` (Regeln aus dem Auftrag, am Zielrechner nicht vorhanden)
aus dieser Liste.

## Liste mit Begründung

### Absturzmelder

| Muster | Treffer am Zielrechner | Beispiel |
|---|---|---|
| `CrashReportClient` | 73 | Unreal Engine, `Engine\Binaries\Win64\CrashReportClient.exe` (auch in Wolkenhof) |
| `CrashReport*` | 81 | zusätzlich `CrashReport.exe` (Capcom), `CrashReporter.exe` |
| `*CrashReporter*` | 6 | Cyberpunk 2077 `bin\x64\CrashReporter\CrashReporter.exe`, `Sentry.CrashReporter.exe` |
| `crash_reporter*` | 2 | Space Marine 2, Toxic Commando |
| `UnityCrashHandler*` | 328 | Unity, 32 und 64 Bit |
| `CrashHandler*` | 0 | Regel aus dem Auftrag |
| `*CrashHandler*` | 329 | zusätzlich `BsUnityCrashHandler.exe` (Phantom Brigade) |
| `crashpad_handler` | 44 | u. a. Red Dead Redemption 2, Sentry-Plugin |
| `BsSndRpt*` | 1 | BugSplat (Phantom Brigade) |
| `*CrashDumper*` | 2 | Brigador, Unrailed |
| `CrashSender*` | 2 | CrashRpt („Crash Report Delivery Module“) |
| `CrashUploader*` | 1 | Mount & Blade II: Bannerlord |
| `*CrashMessage*` | 1 | Far Far West |
| `*ErrorReporter*` | 1 | Cyberpunk 2077 `REDEngineErrorReporter.exe` |
| `BugReportClient*` | 2 | Deep Rock Galactic: Rogue Core („GSG Bugreporter“) |
| `createdump` | 4 | „.NET Runtime Crash Dump Generator“ |
| `*minidump*` | 1 | `moon-minidump-stackwalk.exe` (No Rest for the Wicked) |
| `sentry-cli` | 3 | Sentry-Hochlader |
| `Watchdog` | 1 | Bannerlord-Absturzwächter (nur exakt dieser Name, `WatchDogs2.exe` passt nicht) |

### Anti-Cheat-Installer und -Dienste

| Muster | Treffer | Beispiel |
|---|---|---|
| `EasyAntiCheat*Setup*`, `EasyAntiCheat_EOS_Setup` | je 15 | `EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe` |
| `BEService*` | 0 | BattlEye-Dienst, Regel aus dem Auftrag |
| `*AntiCheat*Installer*` | 2 | `EAAntiCheat.Installer.exe` (Battlefield 6), `denuvoac\AntiCheatInstaller.exe` |
| `GGSetup*` | 1 | „nProtect GameGuard Service Installer“ (HELLDIVERS 2) |
| `Elytra-Setup*` | 1 | „Elytra Anti-Cheat Framework“ (WARDOGS) |
| `pbsvc*` | 1 | PunkBuster-Dienstinstaller (Medal of Honor) |

### Redistributables und Installer

| Muster | Treffer | Beispiel |
|---|---|---|
| `vc_redist*`, `vcredist*`, `vc8redist*` | 63, 12, 1 | Visual C++ Redistributables |
| `dxsetup`, `dxwebsetup` | 5, 1 | DirectX |
| `dotnetfx*`, `NDP4*` | 6, 4 | .NET Framework 3.5 bis 4.8 |
| `PhysX*SystemSoftware*` | 2 | NVIDIA PhysX |
| `*prereq*`, `UE4PrereqSetup*`, `UEPrereqSetup*` | 83, 52, 31 | Unreal-Voraussetzungen |
| `UE3Redist*`, `UnSetup*` | 4, 5 | Unreal Engine 3 |
| `oalinst` | 2 | OpenAL |
| `*WebView2Setup*` | 1 | Edge WebView2 |
| `VulkanRT*` | 1 | Vulkan-Laufzeit (RDR2) |
| `QuickTimeInstall*`, `ArcadeInstall*` | 2, 1 | SWAT 3, Painkiller |
| `EpicOnlineServicesInstaller*`, `CertsUpdater*` | 6, 1 | Epic Online Services |
| `UbisoftConnectInstaller*`, `UplayInstaller*` | 1, 1 | Ubisoft-Launcher-Installer im Spielordner |
| `Social-Club-Setup*`, `Rockstar-Games-*` | 1, 2 | `Redistributables\` von RDR2 („Rockstar Games Installer“) |
| `InstallerMessage*` | 4 | Capcom RE Engine |
| `RKSetup*` | 2 | „Rooks Keep Installation Utility“ |
| `Touchup`, `Cleanup` | je 10 | „EA Installer Touchup/Cleanup“ in `__Installer\` (EA app) |
| `unins*`, `*uninstall*`, `*uninst*` | 10, 4, 5 | Inno-Setup-Deinstaller, `gguninst.exe` |

### Reine Launcher-Fenster

Nur Launcher, die ein eigenes Fenster zeigen und auf einen Klick warten (sie können ohne Spiel offen bleiben):

| Muster | Fundort am Zielrechner |
|---|---|
| `REDprelauncher` | Cyberpunk 2077 (GOG), „REDprelauncher Application“ |
| `LariLauncher`, `DriverVersionChecker`, `LayersChecker` | Baldur's Gate 3, `Launcher\` |
| `TaleWorlds.MountAndBlade.Launcher` | Bannerlord, „BannerlordLauncher“ |
| `launcher` (nur genau dieser Name) | GRAVEN, HYPERVIOLENT, Forgive Me Father 2 („Launcher to start games.“), The Riftbreaker `bin\Launcher.exe`, Wallpaper Engine |

### Browser-Unterprozesse

`EpicWebHelper` (35), `UnrealCEFSubProcess` (10), `CefSharp.BrowserSubprocess` (3): eingebettete Browser, laufen nur
als Kindprozess neben dem Spiel. Ausschließen kostet keine Erkennung, hält aber die in Phase 3 gemerkten „tatsächlichen
Spiel-EXEs“ sauber.

## Bewusst nicht in der Liste

| Name(n) | Grund |
|---|---|
| `gamelaunchhelper.exe` (Xbox) | Vorgabe: früher Start-Hinweis für HDR. Liegt im Installationsordner `…\Content`. |
| `start_protected_game.exe` (8×), `*EAC.exe`, `EACLaunch.exe`, `EasyAntiCheat.exe`, `EAAntiCheat.GameServiceLauncher.exe`, `Launch_HLL.exe`, `PlayRDR2.exe`, `TheDivision2Launcher.exe`, `LaunchGame.exe` | Start-Helfer, die das Spiel selbst starten: wie gamelaunchhelper ein früher Start-Hinweis. |
| `setup.exe` | Am Zielrechner in keinem Spielordner vorhanden, also nicht begründbar. Ebenso kein allgemeines `*setup*`/`*install*`: „Installation01.exe“ wäre ein echtes Spiel. |
| `*crash*` | „Crashlands.exe“ ist ein Spiel. |
| `*eac*` | „BeachInvasion1944.exe“ enthält „eac“. |
| `*sentry*`, `*bug*` | „Sentry.exe“ (SENTRY), „KILLBUG.exe“, „Buggos.exe“ sind Spiele. |
| allgemein `*launcher*` | Schlösse Start-Helfer aus; bei unklaren Launchern (`SUPERHOT-Launcher.exe`, `X-Morph_Launcher.exe`, `Pizza Hero Launcher.exe`, `WardogsLauncher-Shipping.exe`, `Launcher.Native.exe`) ist nicht belegt, ob sie ein wartendes Fenster sind. Ein fälschlich ausgeschlossenes Spiel schaltet nie HDR, ein fälschlich mitgezählter Launcher hält HDR nur länger an. |
| `7z*.exe`, `ffmpeg.exe`, `java(w).exe`, SteamVR-Dienste | keine der vier Gruppen; `javaw.exe` ist bei Java-Spielen der Spielprozess. |
| Wallpaper Engine (`wallpaper32/64.exe`, `wallpaperservice*.exe`), SteamVR (`vrserver.exe`, `vrservice.exe` …) | Das sind keine Hilfsprozesse eines Spiels, sondern eigenständige Programme aus der Steam-Bibliothek, die dauerhaft bzw. ohne Spiel laufen. Werden sie als „Spiel“ gelistet, hielte Wallpaper Engine HDR ständig an. Sie gehören deshalb in den Filter des Steam-Scanners („Einträge ohne Spielcharakter“), nicht in diese Liste; die Ausschlussliste schlösse sonst nur einzelne Prozesse aus und ließe den Rest des Ordners als Spiel gelten. |

## Negativtests

`HelperExecutablesTests.Game_executables_and_start_helpers_are_not_helpers` prüft unter anderem `Cyberpunk2077.exe`,
`ForzaHorizon5.exe`, `game.exe`, `Wolkenhof-WinGDK-Shipping.exe`, `bg3.exe`, `RDR2.exe`, `bf6.exe`, `Crashlands.exe`,
`Sentry.exe`, `BeachInvasion1944.exe`, `WatchDogs2.exe`, `Installation01.exe` und alle Start-Helfer der Tabelle oben.
