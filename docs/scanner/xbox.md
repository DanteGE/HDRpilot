# Scanner: Xbox-App und Game Pass

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen. Spiele an
> Standardorten (`C:\XboxGames`, `…\Steam\steamapps\common`, `C:\Program Files\EA Games`, `…\GOG Galaxy\Games`)
> tragen erfundene Namen und Kennungen.

Klasse `HdrPilot.Core.Games.Scanners.XboxScanner` (Quelle `Xbox`), Hilfsklassen unter
`src/HdrPilot.Core/Games/Scanners/Xbox/`. Geprüft am 2026-10-07 am Zielrechner (Windows 11 25H2, Build 26200),
ausschließlich lesend: Es wurde kein Spiel und keine App gestartet und nichts verändert.

## 1. Angaben mit Vermerk (prüfen) und Ergebnis am Zielrechner

| # | Angabe aus Auftrag bzw. Hinweis | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Auf jedem Laufwerk die versteckte Datei `.GamingRoot`; sie führt zum Spieleordner, meist `<Laufwerk>:\XboxGames` (Dateiformat prüfen) | **bestätigt**, Ordnername aber frei wählbar | `C:\.GamingRoot` (28 Byte, Attribute `Hidden, Archive`) → `XboxGames`; `D:\.GamingRoot` → ein selbst gewählter Ordnername. F: (fest) ohne Datei, E: ist ein nicht bereiter Wechseldatenträger. |
| 2 | Format: 4 Byte `RGBX`, UInt32 = 1, nullterminierter UTF-16-Ordnername relativ zur Laufwerkswurzel | **bestätigt** | C: `52 47 42 58 01 00 00 00 58 00 62 00 6F 00 78 00 47 00 61 00 6D 00 65 00 73 00 00 00`. `GamingRoot_C.bin` ist byte-gleich mit der echten Datei (verglichen); `GamingRoot_D.bin` trägt vor der Veröffentlichung den erfundenen Ordnernamen `Xbox Bibliothek` (40 Byte, sonst gleich aufgebaut). |
| 3 | Können mehrere Einträge folgen? | **nicht prüfbar** | Beide Dateien enthalten genau einen Namen, der UInt32 ist in beiden 1. Der Scanner liest deshalb alle nullterminierten Namen bis Dateiende (siehe Entscheidung E8). |
| 4 | Je Spiel `<Spiel>\Content\MicrosoftGame.config` (XML) mit `ExecutableList/Executable` (`Name`, `Id`), dazu `Identity` und `ShellVisuals` | **bestätigt** | `C:\XboxGames\Wolkenhof\Content\MicrosoftGame.config`, UTF-8 mit BOM: `<Identity Name="Contoso.Wolkenhof" Publisher="CN=3F2A7C19-…" Version="1.4.2316.0" />`, `<ShellVisuals DefaultDisplayName="Wolkenhof" …/>`, `<Executable Name="Hof/Binaries/WinGDK/Wolkenhof-WinGDK-Shipping.exe" Id="AppHofShipping" OverrideDisplayName="ms-resource:AppDisplayName" TargetDeviceFamily="PC" />` |
| 5 | `Executable/@Name` ist die EXE „relativ zum Spielordner“; relativ wozu genau? | **abweichend**: relativ zum **Content**-Ordner, mit `/` | `Test-Path C:\XboxGames\Wolkenhof\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe` = True, `…\Wolkenhof\Hof\Binaries\…` = False |
| 6 | Wo laufen die Spielprozesse? | **unter `<Spiel>\Content`** | MuiCache (`HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache`): Wert `C:\xboxgames\wolkenhof\content\hof\binaries\wingdk\wolkenhof-wingdk-shipping.exe.FriendlyAppName`. Compatibility Assistant (`HKCU\…\AppCompatFlags\Compatibility Assistant\Store`): sechs Einträge `<Spieleordner>\<Spiel>\Content\gamelaunchhelper.exe` früherer Installationen auf C: und D:. Kein Eintrag unter `C:\Program Files\WindowsApps`. |
| 7 | Paketordner laut Windows | `InstallLocation` zeigt auf WindowsApps, ist aber eine **Junction** auf den Content-Ordner | `Get-AppxPackage Contoso.Wolkenhof`: `InstallLocation = C:\Program Files\WindowsApps\Contoso.Wolkenhof_1.4.2316.0_x64__50p2hsbcnaasp`; `Get-Item`: `LinkType = Junction`, `Target = C:\XboxGames\Wolkenhof\Content\`; `fsutil reparsepoint query`: Bereitstellungspunkt, Ersatzname `\??\C:\XboxGames\Wolkenhof\Content\` |
| 8 | Start: `shell:AppsFolder\<PackageFamilyName>!<Id>` (prüfen); welche Id gilt: `appxmanifest.xml Applications/Application/@Id` oder `Executable/@Id`? | **bestätigt** (ohne Start); beide Ids sind hier gleich | `Get-StartApps`: `Hof  Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping`. Shell-Namensraum `shell:AppsFolder` (Shell.Application) enthält ein Element mit `Path = Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping`. `Content\appxmanifest.xml`: `<Application Id="AppHofShipping" Executable="GameLaunchHelper.exe" EntryPoint="Windows.FullTrustApplication">`. `(Get-AppxPackage …).GetAppListEntries()` ist unter Windows PowerShell 5.1 nicht aufrufbar (Methode fehlt am Wrapper-Objekt), deshalb die beiden anderen Belege. |
| 9 | PackageFamilyName aus Identity Name und Publisher berechnen (SHA-256, Base32) und für jedes Spiel gegen `Get-AppxPackage` prüfen | **bestätigt** | Berechnet `50p2hsbcnaasp`, `Get-AppxPackage` meldet `PublisherId = 50p2hsbcnaasp`, `PackageFamilyName = Contoso.Wolkenhof_50p2hsbcnaasp`. Gegenprobe mit drei weiteren Paketen des Systems (Microsoft `8wekyb3d8bbwe`, Microsoft Windows `cw5n1h2txyewy`, NVIDIA `56jybvy8sckqj`): alle gleich, als Testfälle in `XboxFormatTests` übernommen. |
| 10 | `DefaultDisplayName` kann `ms-resource:…` sein | bei Wolkenhof **nicht**, aber an anderer Stelle | `DefaultDisplayName="Wolkenhof"` (Klartext). `OverrideDisplayName` und `VisualElements/@DisplayName` sind `ms-resource:AppDisplayName`. `SHLoadIndirectString("@{C:\XboxGames\Wolkenhof\Content\resources.pri?ms-resource://Contoso.Wolkenhof/Resources/AppDisplayName}")` → `Hof` (HRESULT 0), auch aus .NET auf Haupt- und Threadpool-Thread (MTA). Unbekannter Schlüssel → `0x80073B17`, fehlende PRI-Datei → `0x80070003`. |
| 11 | Häufig startet zuerst `gamelaunchhelper.exe` und erst danach die eigentliche Spiel-EXE aus demselben Ordner (prüfen) | **teilweise bestätigt, präzisiert** | `gamelaunchhelper.exe` (rund 100 KB, Beschreibung „GameLaunchHelper“, Microsoft) liegt in `Content\`, die Spiel-EXE in `Content\Hof\Binaries\WinGDK\` – also nicht im selben Ordner, aber beide unter dem Installationsordner. Das Paketmanifest startet `GameLaunchHelper.exe` (Application/@Executable); in `MicrosoftGame.config` ist sie **nicht** gelistet. Ausgeführt wurden beide (Compatibility Assistant bzw. MuiCache). Die zeitliche Reihenfolge wurde nicht gemessen, weil kein Spiel gestartet werden darf. |
| 12 | Liegt `gamelaunchhelper.exe` im Spielordner, ist sie in der Config gelistet? | liegt in `Content\`, **nicht** gelistet | siehe 11 |
| 13 | Zusatzinhalte (DLC) dürfen nicht als Spiel erscheinen | am Zielrechner **nicht prüfbar** (kein DLC installiert, `Get-AppxPackage -PackageTypeFilter Optional` liefert nichts); Format aus der GDK-Dokumentation übernommen | GDK-Dokumentation „Packaging downloadable content (DLC)“ und „MicrosoftGame.config additional reference“: DLC-Config ohne `ExecutableList`, mit `TargetDeviceFamilyForDLC` („DLC only“), `AllowedProducts` („the title … that this DLC belongs to“) und `DesktopRegistration/MainPackageDependency` („PC Desktop DLC only“). Siehe Entscheidung X12. |

Nachprüfung im Review (2026-10-07, nur lesend): Beide `.GamingRoot` byte-genau erneut ausgelesen (C: und D:,
je `Hidden, Archive`), F: ohne Datei, E: Wechseldatenträger nicht bereit. `Get-AppxPackage Contoso.Wolkenhof`:
`PublisherId = 50p2hsbcnaasp`, `InstallLocation` Junction mit `Target = C:\XboxGames\Wolkenhof\Content\`. `Get-StartApps`:
`Hof  Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping`. `SHLoadIndirectString` → `0x00000000 Hof`, unbekannter
Schlüssel `0x80073B17`, fehlende Datei `0x80070003`. MuiCache und Compatibility Assistant wie in Zeile 6 (Compatibility
Assistant führt andere Store-Apps unter `C:\Program Files\WindowsApps\…`, Spiele dagegen unter dem Spieleordner – ein
weiterer Hinweis, dass Spielprozesse mit dem XboxGames-Pfad laufen). Die sechs Ordner früherer Spiele existieren nicht.
Unter `C:\XboxGames` liegen genau die drei EXE-Dateien aus dem nächsten Absatz. Die echte `MicrosoftGame.config` (UTF-8
mit BOM) unterscheidet sich von der Beispieldatei nur durch gekürzte `Resources`-Liste und das fehlende `MSAAppId`.

Für Phase 3 (Prozessabgleich): Unter `C:\XboxGames\Wolkenhof` liegen drei EXE-Dateien:
`Content\gamelaunchhelper.exe` (früher Start-Hinweis, **kein** Hilfsprozess), `Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe`
(Spiel) und `Content\Engine\Binaries\Win64\CrashReportClient.exe` (Absturzmelder, steht in der Ausschlussliste).
Alle drei liegen unter dem Installationsordner `…\Content`.

## 2. Format-Details

### `.GamingRoot` (Laufwerkswurzel, versteckt)

| Versatz | Länge | Inhalt |
|---|---|---|
| 0 | 4 | Kennung `RGBX` (ASCII) |
| 4 | 4 | UInt32 little-endian, gemessen immer `1` |
| 8 | … | UTF-16LE-Ordnername(n), jeweils mit `00 00` abgeschlossen, relativ zur Laufwerkswurzel |

Fehlerbehandlung (`GamingRootFile.Parse`): kürzer als 8 Byte, falsche Kennung, Steuer- oder unzulässige Zeichen,
fehlender Nullabschluss (abgeschnitten) und „kein Ordnername“ ergeben einen `ScanError` mit Pfad der Datei. Bis zum
Fehler vollständig gelesene Namen werden trotzdem verwendet. Nullbytes am Ende (Auffüllung) sind erlaubt.

### Spieleordner

```
C:\XboxGames\
  GameSave\            (pgs, wgs – Spielstände, kein Content\, kein Spiel)
  Wolkenhof\
    Content\           ← Installationsordner; Ziel der Junction aus C:\Program Files\WindowsApps\<PackageFullName>
      MicrosoftGame.config, appxmanifest.xml, resources.pri, gamelaunchhelper.exe, Engine\, Hof\, …
    Shaders\           (versteckt)
    <GUID>.xvs/.xvi/.xct/.smd/.xsp   (versteckte Installationsdaten)
D:\Xbox Bibliothek\      (zurzeit leer)
```

### `Content\MicrosoftGame.config`

Wurzel `<Game configVersion="1">`, ohne XML-Namensraum. Gelesen werden `Identity/@Name`, `@Publisher`, `@Version`,
`ShellVisuals/@DefaultDisplayName` und je `ExecutableList/Executable` die Attribute `Name`, `Id`, `TargetDeviceFamily`,
`OverrideDisplayName`. `TargetDeviceFamily` ist bei PC-EXEs `PC` oder fehlt; `Scarlett`/`XboxOne` stehen für Konsolen.
Die Kodierung kommt aus BOM bzw. XML-Deklaration (am System UTF-8 mit BOM), deshalb liest der Scanner Bytes statt Text.
Zusätzlich geprüft werden die DLC-Merkmale `TargetDeviceFamilyForDLC`, `AllowedProducts` und
`DesktopRegistration/MainPackageDependency` (Beispieldatei `Fixtures/Xbox/Zusatzinhalt/` nach dem Muster der GDK-Dokumentation).

### `Content\appxmanifest.xml`

Paketmanifest mit Standard-Namensraum `http://schemas.microsoft.com/appx/manifest/foundation/windows10`. Gelesen
werden `Identity/@Name`, `@Publisher`, `Properties/DisplayName`, `Applications/Application/@Id`, `@Executable` und
`Dependencies/MainPackageDependency/@Name` (nur bei optionalen Paketen vorhanden).

### PackageFamilyName

`<Identity Name>_<PublisherId>`; PublisherId = die ersten 8 Byte von SHA-256 über den Publisher-String in UTF-16LE,
als 64 Bit plus ein Füllbit (0) in 13 Gruppen zu 5 Bit, Alphabet `0123456789abcdefghjkmnpqrstvwxyz`.

## 3. Scan-Ergebnis am Zielrechner und Abgleich mit zweiter Quelle

`dotnet run --project src\HdrPilot.Cli -- scan --source xbox` (27 ms, keine Fehler, `launcherDetected: true`):

| ID (PackageFamilyName) | Name | InstallDir | Start-Befehl |
|---|---|---|---|
| `Contoso.Wolkenhof_50p2hsbcnaasp` | Wolkenhof | `C:\XboxGames\Wolkenhof\Content` | `shell:AppsFolder\Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping` |

Executables: `C:\XboxGames\Wolkenhof\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe`.

**Zweite Quelle:** `Get-AppxPackage` über alle Pakete, gefiltert auf Pakete mit `MicrosoftGame.config` im
Paketordner oder einer Junction in einen der beiden Spieleordner → genau ein Paket, `Contoso.Wolkenhof`.
Ergänzend `Get-StartApps` und der Shell-Namensraum `shell:AppsFolder`.

| Merkmal | Scanner | Get-AppxPackage / Startmenü | Abweichung und Begründung |
|---|---|---|---|
| Anzahl Spiele | 1 | 1 Spielepaket (alle übrigen Store-Pakete sind Apps ohne MicrosoftGame.config) | keine |
| ID | `Contoso.Wolkenhof_50p2hsbcnaasp` | `PackageFamilyName` gleich | keine |
| Name | Wolkenhof | Startmenü: „Hof“ | Das Startmenü zeigt `VisualElements/@DisplayName` = `ms-resource:AppDisplayName`, aufgelöst „Hof“. Der Scanner nimmt den Paketnamen `ShellVisuals/@DefaultDisplayName` bzw. `Properties/DisplayName` = „Wolkenhof“, so heißt das Spiel im Store und in der Xbox-App. |
| Ordner | `C:\XboxGames\Wolkenhof\Content` | `InstallLocation` `C:\Program Files\WindowsApps\Contoso.Wolkenhof_1.4.2316.0_x64__50p2hsbcnaasp` | Derselbe Ordner: Der WindowsApps-Pfad ist eine Junction auf `…\Content`. Die Prozesse liefen laut MuiCache unter dem XboxGames-Pfad, deshalb ist dieser der Installationsordner. |
| Start | `…!AppHofShipping` | `Get-StartApps` AppID `Contoso.Wolkenhof_50p2hsbcnaasp!AppHofShipping` | keine |
| Frühere Spiele | – | Compatibility Assistant nennt fünf früher installierte Spiele (Namen vor der Veröffentlichung entfernt) | Ordner existieren nicht mehr, kein Paket registriert: deinstalliert, zu Recht nicht gelistet. |
| `GameSave` | – | – | Kein `Content\MicrosoftGame.config`, kein Spiel. |
| D: | 0 Spiele | 0 Pakete | Ordner `D:\Xbox Bibliothek` ist leer. |

## 4. Entscheidungen

- **X1 Installationsordner ist `<Spiel>\Content`**, nicht `<Spiel>`: `Executable/@Name` ist relativ zu Content, die
  Junction des registrierten Pakets zeigt genau dorthin, gamelaunchhelper.exe und alle EXEs liegen darunter, und die
  Prozesse liefen dort (Beleg 6). Neben Content liegen nur versteckte Installationsdaten ohne EXE.
- **X2 ID = PackageFamilyName, berechnet statt erfragt.** Die Berechnung braucht keine Windows-API, bleibt in Tests
  hermetisch und stimmt am System mit `Get-AppxPackage` überein (Beleg 9).
- **X3 App-Id aus dem Paketmanifest**, ersatzweise aus der ersten PC-`Executable` mit `Id`. Das Paketmanifest ist das
  von Windows registrierte (Junction); die AppUserModelId im Startmenü verwendet dessen `Application/@Id`. Bei mehreren
  Applications gilt die erste. Paketname und App-Id werden gegen das Manifest-Schema geprüft (3–50 Zeichen `A–Z a–z 0–9 . -`
  bzw. Buchstabe am Anfang, höchstens 64 Zeichen), sonst `ScanError`, weil der Start-Befehl sonst nicht funktioniert.
- **X4 Anzeigename**: zuerst Klartext aus `ShellVisuals/@DefaultDisplayName`, dann aus `Properties/DisplayName` des
  Paketmanifests; erst wenn beide `ms-resource:` sind, Auflösung über `Content\resources.pri` mit `SHLoadIndirectString`
  hinter der Schnittstelle `IIndirectStringResolver` (Tests verwenden eine Attrappe); zuletzt der Ordnername
  (die Xbox-App benennt ihn nach dem Spiel, etwa „Beispielspiel - PC“). Klartext zuerst, weil er ohne Windows-Ressourcen
  auskommt und am System den richtigen Namen liefert.
- **X5 Executables**: nur PC-EXEs (TargetDeviceFamily fehlt oder `PC`), relativ zu Content, `/` und `\` gleich
  behandelt, nur existierende Dateien, nichts außerhalb von Content (`..\`, absolute Pfade werden ignoriert), keine
  Duplikate, Reihenfolge wie in der Config. `gamelaunchhelper.exe` kommt nicht in die Liste, weil die Quelle sie nicht
  als Spiel-EXE nennt; über den Installationsordner wird sie in Phase 3 trotzdem erkannt und steht bewusst nicht in der
  Ausschlussliste (früher Start-Hinweis).
- **X6 Unvollständige Installation**: Nennt die Config PC-EXEs, von denen keine existiert, wird der Ordner still
  übersprungen (Protokoll Information), wie unvollständige Epic-Installationen. Nennt sie gar keine, aber das
  Paketmanifest eine App, wird das Spiel ohne Executables geliefert.
- **X7 Ordner ohne `Content\MicrosoftGame.config`** (etwa `GameSave`, angefangene Downloads) sind keine Spiele und
  erzeugen keinen Fehler.
- **X8 `.GamingRoot`**: alle nullterminierten Namen bis Dateiende werden gelesen, der UInt32 dient nur zur Kontrolle
  (Abweichung wird protokolliert). So funktioniert der Scanner, egal ob der Wert eine Anzahl oder eine Version ist.
  Absolute Namen würden unverändert übernommen, relative an die Laufwerkswurzel gehängt. Ergibt ein Name keinen
  vollständigen Pfad (etwa `C:Spiele`, relativ zum aktuellen Ordner des Laufwerks), wird er mit `ScanError` übersprungen;
  sonst entstünde ein relativer Installationsordner (im Review gefunden, Test
  `Broken_gaming_root_with_drive_relative_folder_is_reported_and_never_gives_relative_install_dir`).
- **X9 Fehler je Datei**: kaputte oder nicht lesbare `.GamingRoot`, `MicrosoftGame.config` oder `appxmanifest.xml`
  ergeben einen `ScanError` mit Pfad; die übrigen Spiele werden geliefert. Bei kaputtem Paketmanifest gelten die Angaben
  aus der Config (der Fehler sagt das). XML wird ohne DTD gelesen.
- **X10 Duplikate** (gleicher PackageFamilyName auf zwei Laufwerken, etwa Reste einer Verschiebung): Der erste Fund in
  Laufwerksreihenfolge gilt, der zweite wird protokolliert.
- **X11 Registry und Umgebung werden nicht gebraucht**: Laufwerke kommen aus `IFileSystem.GetFixedDriveRoots`; die
  Parameter stehen nur wegen des Konstruktor-Vertrags im Konstruktor.
- **X12 Zusatzinhalte (DLC) sind keine Spiele** und erzeugen keinen Fehler: Hat `MicrosoftGame.config` eines der
  DLC-Merkmale `TargetDeviceFamilyForDLC`, `AllowedProducts` oder `DesktopRegistration/MainPackageDependency`, oder nennt
  `appxmanifest.xml` eine `Dependencies/MainPackageDependency` (optionales MSIX-Paket), wird der Ordner still übersprungen
  (Protokoll Information). Die Config wird dabei vor dem Paketmanifest geprüft, damit ein defektes Manifest eines DLC
  keinen Fehler meldet. Grund (im Review gefunden): Eine DLC-Config hat laut GDK-Dokumentation keine `ExecutableList`;
  ohne diese Regel meldete der Scanner für jeden installierten Zusatzinhalt „Keine App-Id gefunden“ als Fehler in der
  Oberfläche, und ein DLC-Paket mit eigener App wäre als Spiel erschienen. Tests:
  `Incomplete_dlc_package_is_not_a_game_and_reports_no_error`,
  `Dlc_marker_in_config_skips_package_even_with_executable_and_broken_manifest`, `Dlc_marker_in_appxmanifest_skips_package`.

## 5. Bekannte Grenzen

- Ältere Installationen direkt unter `C:\Program Files\WindowsApps` (ohne `.GamingRoot`, nicht „modifizierbar“) und
  reine UWP-Spiele ohne `MicrosoftGame.config` findet der Scanner nicht. Am Zielrechner gibt es keine solchen Pakete.
- Ob `.GamingRoot` mehrere Ordner enthalten kann, war nicht prüfbar (Entscheidung X8 deckt beide Deutungen ab).
- „Unvollständig“ wird nur daran erkannt, dass keine gelistete EXE existiert; ein laufender Download, bei dem die EXE
  schon liegt, gilt als installiert.
- Ob ein Paket wirklich registriert ist, prüft der Scanner nicht (dafür bräuchte es die Paket-API). Ein Spielordner,
  dessen Paket abgemeldet wurde, erscheint deshalb weiter, sein Start-Befehl schlägt dann fehl.
- Der Start-Befehl wurde nicht ausgeführt (Vorgabe); belegt ist, dass die AppUserModelId im AppsFolder existiert.
- Nicht feste Laufwerke (USB, Netzlaufwerke) werden nicht durchsucht.
- Die DLC-Erkennung (X12) beruht auf der GDK-Dokumentation, nicht auf einer Installation am Zielrechner. Ob die Xbox-App
  Zusatzinhalte überhaupt als eigene Ordner im Spieleordner ablegt, ist hier nicht belegt; falls ja, werden sie erkannt.
- Phase 3: Belegt ist, dass die Spielprozesse mit dem Pfad unter `<Spieleordner>\<Spiel>\Content` laufen (MuiCache,
  Compatibility Assistant). Meldet Windows für einen Prozess doch den WindowsApps-Pfad der Junction, passt er nicht
  unter den Installationsordner; das muss der Prozessabgleich beim ersten echten Lauf protokollieren.
