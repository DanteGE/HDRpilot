# Ubisoft-Connect-Scanner

> Vor der Veröffentlichung bereinigt: Installationsordner des Messrechners sind durch erfundene ersetzt (etwa
> `D:\Steam-Bibliothek`, `D:\GOG-Spiele`), vollständige Spielelisten durch erfundene Beispielzeilen.

Stand: 2026-10-07, gemessen am Zielrechner (Windows 11 25H2, Build 26200) durch reines Lesen von Registry und Dateien.
Ubisoft Connect laut Uninstall-Eintrag Version 169.2.12961 (`HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher`, `Version` = 13225).
Code: `src/HdrPilot.Core/Games/Scanners/UbisoftScanner.cs`, Hilfsklassen unter `src/HdrPilot.Core/Games/Scanners/Ubisoft/`
(`UbisoftConfigurationCache` mit `UbisoftProductConfig`, `UbisoftYaml`).
Tests: `tests/HdrPilot.Tests/Scanners/UbisoftScannerTests.cs`, `UbisoftConfigurationCacheTests.cs`,
`SteamUbisoftRobustnessTests.cs` (Zufallsbeschädigung); Beispieldateien
`tests/HdrPilot.Tests/Fixtures/Ubisoft/*.yml` (gekürzte Produktdaten aus dem Cache dieses Rechners; keine Konto- oder
Gerätedaten). Die Protobuf-Hülle baut `UbisoftCacheBuilder` im Test genau wie gemessen nach.

## 1 Angaben aus dem Auftrag und Prüfung am echten System

| Angabe im Auftrag | Ergebnis | Beleg am Zielrechner |
|---|---|---|
| Registry `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<ID>` | bestätigt | Unterschlüssel `1081` und `4740`. In der 64-Bit-Sicht gibt es `HKLM\SOFTWARE\Ubisoft` nicht. Der Scanner öffnet `SOFTWARE\Ubisoft\Launcher\Installs` über `RegistryViewKind.Registry32` (wie bei GOG), nicht über einen von Hand geschriebenen WOW6432Node-Pfad. |
| Wertname des Installationsordners (prüfen) | bestätigt: `InstallDir` | `Installs\4740`: `InstallDir` (REG_SZ) = `D:/Ubisoft-Spiele/AFOP/` (Schrägstriche, abschließender Trenner) und `Language` = `de-DE`. `Installs\1081` hat **keinen** `InstallDir`, nur `language` = `de-DE`: 1081 ist Assassin's Creed Shadows aus Steam (Produkt-Cache: `start_game.steam.steam_app_id: 3159330`, `third_party_platform.name: Steam`); der Steam-Scanner findet es als Steam-Spiel 3159330 in seiner Steam-Bibliothek (Ordner vor der Veröffentlichung entfernt). |
| Installs-Schlüssel enthält vermutlich keinen Namen | bestätigt | Nur `InstallDir` und `Language`/`language`. |
| Namensquelle Uninstall-Eintrag `Uplay Install <ID>` | vorhanden, aber schwach | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install 4740`: `DisplayName` = `AFOP`, `InstallLocation` = `D:/Ubisoft-Spiele/AFOP/`, `UninstallString` = `"…\UbisoftConnect.exe" uplay://uninstall/4740`, `URLInfoAbout` = `https://avatarfrontiersofpandora.com`. „AFOP“ ist nur das Kürzel (`shortcut_name`/`game_identifier`), nicht der Anzeigename. |
| Verlässliche Namensquelle am System suchen | gefunden: Produkt-Cache | `C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\cache\configuration\configurations` (rund 260 KB, 1 196 Produkte). Produkt 4740: `root.name: l1` und `localizations.default.l1: Avatar Frontiers of Pandora™`; `sort_string: 'Avatar: Frontiers of Pandora™'`. Deckt sich mit dem Namen der Desktop-Verknüpfung `Avatar Frontiers of Pandora.url`. |
| Start `uplay://launch/<ID>/0` (prüfen) | bestätigt | Protokoll registriert: `HKCR\uplay\Shell\Open\Command` (= `HKLM\SOFTWARE\Classes\uplay`) = `"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe" "%1"`, `URL Protocol` vorhanden. Von Ubisoft Connect angelegte Verknüpfung `%APPDATA%\Microsoft\Windows\Start Menu\Programs\AFOP.url`: `URL=uplay://launch/4740/0`. Ebenso Desktop `Games\Avatar Frontiers of Pandora.url` → `uplay://launch/4740/0` und eine Verknüpfung auf ein nicht installiertes Spiel (`<Name>.url` → `uplay://launch/<ID>/0`; Name vor der Veröffentlichung entfernt). Nicht ausgeführt. |
| Ordner, die nicht existieren, überspringen | umgesetzt | `D:\Ubisoft-Spiele\AFOP` existiert; Test `Incomplete_missing_game_folder_is_skipped_without_error`. |
| LauncherDetected anhand des Launcher-Schlüssels | umgesetzt | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher`: `InstallDir` = `C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\`, `Version` = 13225, `Installer Language` = 127. |

Weitere Beobachtungen: `HKCU\Software\Ubisoft\Launcher\Installs` nennt 1081, 4740, 4933, 62412 und 64181 mit
`InstallState` = 1, ist aber keine Installationsliste (siehe Abschnitt 3). `HKLM\SOFTWARE\WOW6432Node\Ubisoft\The Division 2 - Steam`
(`installdir` = `F:\Steam-Bibliothek\steamapps\common\Tom Clancy's The Division 2`) legt das Steam-Spiel selbst an; der Scanner
liest ihn nicht, das Spiel kommt über Steam (2221490).

## 2 Format-Details

**Registry.** `HKLM\SOFTWARE\Ubisoft\Launcher` (32-Bit-Sicht): `InstallDir` des Launchers. Darunter `Installs\<Produkt-ID>`
mit `InstallDir` (REG_SZ, gemessen mit `/` und abschließendem `/`). Windows vergleicht Wertnamen ohne Groß-/Kleinschreibung
(gemessen kommen `Language` und `language` vor); die Registry-Attrappe der Tests verhält sich genauso.

**Produkt-Cache `cache\configuration\configurations`.** Protobuf ohne Schema, gemessener Anfang:
`0A C3 0C 08 65 10 2E 1A BC 0C 76 65 72 73 69 6F 6E 3A 20 32 2E 30` = Feld 1 (Länge 1 603), darin Feld 1 = 101 (Produkt-ID),
Feld 2 = 46 (Version), Feld 3 = YAML-Text (Länge 1 596, „version: 2.0 …“). Die Datei ist eine Folge solcher Einträge.
Das YAML (Version 2.0) nutzt nur eine Teilmenge: Einrückung mit Leerzeichen, Zuordnungen, Listen (auch auf gleicher
Einrückung wie der Schlüssel), Zeichenketten ohne bzw. mit `'…'` und `"…"`, leere Flow-Werte `{}`/`[]`, Kommentare.
Gemessen über alle 1 196 Einträge: keine Einrückung mit Tabulatoren, keine Blocktexte (`|`, `>`), keine Anker, keine über
das Zeilenende offenen Anführungszeichen; 32 Kommentarzeilen (`# …`). Der Parser kann Blocktexte trotzdem überlesen.

Relevante Schlüssel je Produkt:

| Schlüssel | Bedeutung | Beispiel 4740 |
|---|---|---|
| `root.name` | Name oder Lokalisierungsschlüssel | `l1` |
| `localizations.default.<Schlüssel>` | englischer Text zum Schlüssel | `l1: Avatar Frontiers of Pandora™` |
| `root.start_game.<online/offline/steam>.executables[]` | Start-EXEs | ein Eintrag je Modus |
| `… .path.relative` | EXE relativ zum Arbeitsordner | `afop.exe` |
| `… .trial.relative` | Ubisoft+-Variante | `afop_plus.exe` |
| `… .working_directory.register` / `.append` | Arbeitsordner = Registry-Wert (+ Unterordner) | `HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\Launcher\Installs\4740\InstallDir` |
| `root.third_party_platform.name` | Fremdplattform | bei 1081: `Steam` |

Im Cache stehen 15 Produkte mit eigenem `Installs\<ID>\InstallDir`-Arbeitsordner, darunter Far Cry 4 (420) mit
`relative: FarCry4.exe` und `append: bin` sowie Far Cry 6 (5266) mit `relative: bin\FarCry6.exe`.

Bei der Nachprüfung zusätzlich gefunden: Watch Dogs 2 nennt `relative: '\bin\WatchDogs2.exe'` und
`trial.relative: '\bin_plus\WatchDogs2.exe'` mit **führendem Backslash**, `append: ''` und als Arbeitsordner
`register: HKEY_LOCAL_MACHINE\SOFTWARE\Ubisoft\WatchDogs2\InstallDir` (ein eigener Schlüssel statt `Installs\<ID>`).
Ubisoft hängt `relative` an den Arbeitsordner an; der führende Trenner macht den Pfad nicht absolut. Übersicht aller
`register`-Werte im Cache: 33 × `…\Launcher\Installs\<ID>\InstallDir`, 2 × `…\Ubisoft\WatchDogs2\InstallDir`,
5 × `HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam\InstallPath` und 5 × `HKEY_CURRENT_USER\SOFTWARE\Valve\Steam\Apps\<AppID>\Installed`
(Steam-Ausgaben, kein Ubisoft-Installationsordner).

## 3 Scan-Ergebnis am echten System

Aufruf: `dotnet run --project src\HdrPilot.Cli -- scan --source Ubisoft` (2026-10-07, 11:14 Uhr).
Ergebnis: `launcherDetected` = true, **1 Spiel**, **0 Fehler**, Laufzeit 20 ms.

| ID | Name | InstallDir | Start-Befehl | Executables |
|---:|---|---|---|---|
| 4740 | Avatar Frontiers of Pandora™ | `D:\Ubisoft-Spiele\AFOP` | `uplay://launch/4740/0` | `D:\Ubisoft-Spiele\AFOP\afop.exe`, `D:\Ubisoft-Spiele\AFOP\afop_plus.exe` |

Im Spielordner liegen außerdem `Tools\vc_redist\VC_redist.x64.exe`, `uplay_install.manifest` und `uplay_install.state`;
der Cache nennt nur `afop.exe` und `afop_plus.exe`.

### Vergleich mit unabhängigen Quellen

| Quelle | Inhalt | Abweichung zum Scan | Begründung |
|---|---|---|---|
| Uninstall `Uplay Install <ID>` (32-Bit-Sicht) | nur `Uplay Install 4740`, `InstallLocation` = `D:/Ubisoft-Spiele/AFOP/` (existiert) | Name: Uninstall `AFOP`, Scan `Avatar Frontiers of Pandora™` | Uninstall nutzt das Kürzel; der Scan nimmt den Namen aus dem Produkt-Cache (E-U3). ID und Ordner gleich. |
| `HKLM\…\Launcher\Installs` | 1081 (ohne `InstallDir`), 4740 | 1081 fehlt im Scan | Steam-Ausgabe von Assassin's Creed Shadows, kein Ubisoft-Installationsordner; über Steam (3159330) gefunden. |
| `HKCU\Software\Ubisoft\Launcher\Installs` | 1081, 4740, 4933, 62412, 64181 mit `InstallState` = 1 | 4 IDs nicht im Scan | 1081 und 62412 (Ghost Recon Breakpoint) sind laut Produkt-Cache Steam-Ausgaben; 62412 ist auch in Steam nicht installiert (`Apps\2231380\Installed` = 0). 4933 und 64181 stehen weder im HKLM-Schlüssel noch im Cache; ohne Ordner kein Spiel. Der HKCU-Schlüssel ist eine Merkliste des Launchers, keine Installationsliste. |
| Verknüpfungen (Startmenü/Desktop) | `AFOP.url` und `Avatar Frontiers of Pandora.url` → `uplay://launch/4740/0`; `<Name>.url` → `<ID>` (ein weiteres Spiel, Name vor der Veröffentlichung entfernt) | `<ID>` nicht im Scan | Das Spiel ist nicht installiert (kein `Installs\<ID>`, kein Uninstall-Eintrag; `data\<ID>` ist leer). |

## 4 Entscheidungen

**E-U1 32-Bit-Sicht.** Launcher und Installs werden über `RegistryViewKind.Registry32` gelesen, wie die GOG-Regel im
Auftrag. Unter x64 landet das in `WOW6432Node`; die Registry-Attrappe bildet beide Wege gleich ab (Test
`Installs_written_under_wow6432node_are_read_through_32bit_view`). Ein Schlüssel nur in der 64-Bit-Sicht zählt nicht.

**E-U2 Was ist installiert.** Ein Produkt zählt, wenn `Installs\<ID>` einen `InstallDir` hat und der Ordner existiert.
Fehlt `InstallDir` (Steam-Ausgaben) oder ist er leer bzw. der Ordner weg: Protokolleintrag, kein Fehler. `InstallDir`
wird über `PathUtil.Normalize` vereinheitlicht (`D:/Ubisoft-Spiele/AFOP/` → `D:\Ubisoft-Spiele\AFOP`). Zeigt `InstallDir` auf die
Wurzel eines Laufwerks oder einer Freigabe (`D:/`, `D:`, `\\server\freigabe`), wird der Eintrag mit `ScanError`
übersprungen: In Phase 3 gälte sonst jeder Prozess auf dem Laufwerk als Spiel (Nachprüfung, Test
`Broken_install_dir_at_drive_root_is_reported_and_not_returned`). Der Nutzer kann das Spiel manuell mit seinem Ordner eintragen.

**E-U3 Name.** Reihenfolge: Produkt-Cache (`root.name`, aufgelöst über `localizations.default`; ein unaufgelöster
Schlüssel wie `l1` gilt nicht als Name), dann `DisplayName` von `Uplay Install <ID>` (32-Bit-Sicht, nur als REG_SZ; ein
falsch typisierter DWORD-Wert ergäbe sonst eine Zahl als Namen), dann der Ordnername.
Die Sprache der Installation (`de-DE`) wird nicht genutzt: Für 4740 gibt es keine deutsche Lokalisierung, und die
Steam-Namen sind ebenfalls englisch.

**E-U4 Executables.** Aus dem Produkt-Cache alle `path.relative` und `trial.relative` aller Startmodi, nur wenn die Datei
existiert, ohne Duplikate. `relative` gilt relativ zum Arbeitsordner (`InstallDir` + `append`, gemessen bei Far Cry 4:
`FarCry4.exe` mit `append: bin`); fehlt die Datei dort, wird `InstallDir` selbst geprüft. Führende Trenner in `relative`
und `append` werden entfernt (Watch Dogs 2: `'\bin\WatchDogs2.exe'`; vor der Nachprüfung fiel diese EXE weg). Pfade mit
`..` oder Laufwerksangabe werden verworfen. Nennt der Cache nichts, bleibt die Liste leer.

**E-U5 Start-Befehl** exakt `uplay://launch/<ID>/0` (wie in den Verknüpfungen von Ubisoft Connect). Keine
`LaunchArguments`/`LaunchWorkingDirectory`, weil keine EXE gestartet wird.

**E-U6 Fehler oder Protokoll.** `ScanError` (deutsch, mit Pfad): Installs-Unterschlüssel ohne gültige Produkt-ID, nicht
lesbar, `InstallDir` mit falschem Registry-Typ, relativ oder Laufwerkswurzel; Produkt-Cache nicht lesbar oder beschädigt (bis zur
Schadstelle gelesene Einträge bleiben), YAML eines Produkts zu tief verschachtelt. Nur Protokoll: Launcher fehlt
(`LauncherDetected` = false), Installs fehlt, `InstallDir` fehlt/leer, Ordner fehlt, Cache fehlt, doppelter Ordner.

**E-U7 Duplikate.** Je Produkt-ID ein Eintrag (Registry-Schlüssel sind eindeutig) und je Installationsordner ein Eintrag;
bei gleichem Ordner gewinnt die kleinere Produkt-ID.

**E-U8 Eigene Parser.** Protobuf-Wire-Format (Varint, 64 Bit, Länge, 32 Bit) und YAML-Teilmenge sind selbst geschrieben,
weil keine neuen NuGet-Pakete erlaubt sind. Unbekanntes YAML wird übersprungen statt abzubrechen; nur Verschachtelung
über 64 Ebenen gilt als defekt.

## 5 Bekannte Grenzen

- Der Produkt-Cache ist ein Cache: Er wird nur aktualisiert, wenn Ubisoft Connect online Produktdaten lädt (gemessen
  zuletzt rund ein Jahr vor dieser Prüfung, trotzdem mit 4740). Fehlt ein Produkt dort, kommt der Name aus dem Uninstall-Eintrag (hier das
  Kürzel „AFOP“) oder dem Ordner, und `Executables` bleibt leer.
- Über Steam oder Epic gekaufte Ubisoft-Spiele erscheinen bei der jeweiligen Plattform, nicht hier.
- Der Start über `uplay://launch/<ID>/0` öffnet Ubisoft Connect; der eigentliche Spielprozess (laut Produkt-Cache
  `afop.exe` bzw. `afop_plus.exe`, nicht durch einen Start geprüft) liegt unter `InstallDir`. Hilfsprozesse wie `VC_redist.x64.exe` liegen ebenfalls darunter und gehören
  in Phase 3 auf die Ausschlussliste.
- Getestet am System nur mit einem installierten Ubisoft-Spiel; Varianten mit `append` (Far Cry 4) und mit führendem
  Backslash (Watch Dogs 2) sind nur über Beispieldaten aus dem Cache abgesichert.
- Ein laufender Download ist nicht von einer fertigen Installation zu unterscheiden: `Installs\<ID>\InstallDir` und der
  Ordner bestehen schon während des Herunterladens. Der Zustand steht vermutlich in `<Spielordner>\uplay_install.state`
  (Protobuf, gemessen unter 1 KB bei 4740, Aufbau unbekannt); der Scanner wertet die Datei nicht aus.
- Trüge Ubisoft Connect für eine Steam-Ausgabe einmal doch einen `InstallDir` ein, erschiene dasselbe Spiel bei Steam und
  bei Ubisoft mit demselben Ordner (am Zielrechner nicht der Fall: 1081 hat keinen `InstallDir`).

## 6 Nachprüfung (unabhängiger Prüfer, 2026-10-07)

Am echten System nur lesend nachgeprüft:

- `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs`: `1081` (nur `language` = `de-DE`) und `4740` (`Language`,
  `InstallDir` REG_SZ = `D:/Ubisoft-Spiele/AFOP/`); `HKLM\SOFTWARE\Ubisoft` in der 64-Bit-Sicht fehlt. Launcher-Schlüssel mit
  `InstallDir`, `Version` = 13225, `Installer Language` = 127. `Uplay Install 4740`: `DisplayName` = `AFOP`.
- `HKCR\uplay\Shell\Open\Command` = `"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe" "%1"`;
  `AFOP.url` (Startmenü) und `Desktop\Games\Avatar Frontiers of Pandora.url` → `URL=uplay://launch/4740/0`,
  die Verknüpfung auf das nicht installierte Spiel → `uplay://launch/<ID>/0` (`data\<ID>` leer).
- Produkt-Cache: rund 260 KB, zuletzt rund ein Jahr vor dieser Prüfung geschrieben, Anfang `0A C3 0C 08 65 10 2E 1A BC 0C`; 1 196 Produkte, alle
  ohne Fehler gelesen; 842 ohne Namen sind leere bzw. unsichtbare Zusatzinhalte (`is_ulc: yes`) ohne `start_game`; kein
  aufgelöster Name ist ein übrig gebliebener Lokalisierungsschlüssel. 4933 und 64181 stehen nicht im Cache, 62412 ist
  Ghost Recon Breakpoint (Steam, `Apps\2231380\Installed` = 0).
- Scan: 1 Spiel, 0 Fehler, 21 ms; Name, Ordner, Start-Befehl und beide EXE-Dateien wie in Abschnitt 3; `afop.exe` und
  `afop_plus.exe` liegen in `D:\Ubisoft-Spiele\AFOP`.
- Behoben (je ein Test, der vorher fehlschlug): führender Backslash in `relative` (Watch Dogs 2) ließ die EXE wegfallen
  (`Executables_with_leading_backslash_are_relative_to_install_dir`); `InstallDir` als Laufwerkswurzel wurde als Spiel
  geliefert (`Broken_install_dir_at_drive_root_is_reported_and_not_returned`); ein DWORD als Uninstall-`DisplayName` wurde
  zur Zahl als Name (`Broken_uninstall_display_name_with_wrong_value_type_is_not_used_as_name`).
- Neue Tests ohne Befund: Umlaut, `&` und abschließender Backslash im `InstallDir`, nicht vorhandenes Laufwerk, leerer
  Produkt-Cache, `InstallDir` des Launchers als DWORD, Zufallsbeschädigung von Cache und YAML (`SteamUbisoftRobustnessTests`).
