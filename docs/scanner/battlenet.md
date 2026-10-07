# Scanner: Battle.net (`BattleNetScanner`, Quelle `BattleNet`)

Stand: 2026-10-07, Zielrechner (Battle.net-App mit Agent). Alle Angaben unten sind am echten
System nur lesend geprüft; nichts wurde gestartet oder geschrieben.

Code: `src/HdrPilot.Core/Games/Scanners/BattleNetScanner.cs`, Hilfsklassen unter
`src/HdrPilot.Core/Games/Scanners/BattleNet/` (eigener minimaler Protobuf-Leser, kein Google.Protobuf).
Tests: `tests/HdrPilot.Tests/Scanners/BattleNetScannerTests.cs`, Beispieldateien unter
`tests/HdrPilot.Tests/Fixtures/BattleNet/`.

**Befund in einem Satz:** product.db ist lesbar und ihr Aufbau ist am Zielrechner belegt, aber auf diesem Rechner ist
kein Blizzard-Spiel installiert; der Scanner liefert hier deshalb korrekt eine leere Liste ohne Fehler.

## 1. Prüfpunkte aus dem Auftrag

| Angabe | Ergebnis | Beleg |
|---|---|---|
| Spiele über die Datei product.db des Battle.net-Agenten lesen (Pfad prüfen) | **bestätigt** | `C:\ProgramData\Battle.net\Agent\product.db` = `%ProgramData%\Battle.net\Agent\product.db`, Attribute `-a-h--` (versteckt), unter 1 KB. Daneben `.product.db` (unter 1 KB, ältere Kopie, nicht verwendet). |
| Vermerk der Vorbereitung „product.db ist 0 Byte“ | **abweichend** | Die Datei ist nicht leer (unter 1 KB), Attribute `Hidden, Archive, NotContentIndexed`. Ohne `-Force` listet `Get-ChildItem` sie nicht (0 Treffer) und `Get-Item` wirft eine IOException; das erklärt vermutlich den Messwert. Gelesen mit `(Get-Item … -Force).Length` und `[IO.File]::ReadAllBytes`. Der Scanner behandelt eine leere Datei trotzdem korrekt (leere Datenbank, kein Fehler; Test `Empty_product_db_yields_no_games_without_errors`). |
| Aufbau Protobuf (prüfen) | **bestätigt** | Wire-Format ohne Kopf. Oberste Ebene: Feld 1 (Länge) wiederholt je Produkt. Im Produkt: Feld 1 `agent` / `battle.net` (uid), Feld 2 `agent` / `bna` (Produktcode), Feld 3 Einstellungen; darin Feld 1 `C:/ProgramData/Battle.net/Agent` bzw. `C:/Program Files (x86)/Battle.net` (Installationsordner, mit `/`), Feld 2 `us` (Region), Feld 6/7 `deDE` (Sprachen). Hex-Anfang: `0A BE 02 0A 05 61 67 65 6E 74 12 05 61 67 65 6E 74 1A 3E 0A 1F 43 3A 2F 50 72 6F …` = Feld 1 (318 Byte) { Feld 1 "agent", Feld 2 "agent", Feld 3 (62 Byte) { Feld 1 "C:/Pro…" } }. |
| Sind Blizzard-Spiele installiert? | **nein** | product.db enthält nur Agent und Battle.net-App. `%ProgramData%\Battle.net\Agent\aggregate.json` = `{"installed": []}`. `%AppData%\Battle.net\Battle.net.config`, Abschnitt `Games`, nennt nur `battle_net`. Uninstall-Einträge mit `Blizzard Uninstaller.exe`: nur `Battle.net`. `HKCU\Software\Blizzard Entertainment\Battle.net\Launch Options` hat nur den Unterschlüssel `BNA`. |
| Verknüpfung product.db ↔ Uninstall-Eintrag | **bestätigt (am Eintrag der Battle.net-App)** | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net`: `UninstallString` = `"C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe" --lang=deDE --uid=battle.net --displayname="Battle.net"`, `DisplayIcon` = `C:\Program Files (x86)\Battle.net\Battle.net.exe`, `InstallLocation` = `C:\Program Files (x86)\Battle.net`. `--uid=battle.net` = uid in product.db. |
| Start-Befehl `battlenet://<Code>` oder `Battle.net.exe --exec="launch <Code>"` | **Protokoll bestätigt, Spielstart nicht prüfbar** | `HKCR\battlenet\shell\open\command` = `"C:\Program Files (x86)\Battle.net\Battle.net.exe" --uri="%1"` (ebenso `HKCR\blizzard`). Ohne installiertes Spiel lässt sich weder prüfen, welcher Code ein Spiel startet, noch ob die URI das Spiel startet oder nur seine Seite öffnet. Hinweis aus dem Battle.net-Protokoll (`%LocalAppData%\Battle.net\Logs\battle.net-*.log`): uids im Katalog wie `prometheus`, `fenris`, `hs_beta`, daneben Start-Kürzel wie `Pro_retail`, `Fen_retail`, `WTCG_retail`; uid, Produktcode und Start-Kürzel sind also nicht immer gleich. |

## 2. Format

product.db ist eine Protobuf-Nachricht ohne Kopf und ohne Längenpräfix. Der eigene Leser (`ProtoWireReader`) kennt nur
die Wire-Typen 0 (Varint), 1 (64 Bit), 2 (Länge), 5 (32 Bit); Gruppen (3/4) gelten als defekt. Verwendet werden nur die
am Zielrechner belegten Felder:

| Ebene | Feld | Bedeutung |
|---|---|---|
| Datenbank | 1 (wiederholt, Länge) | Produkt |
| Produkt | 1 (Text) | uid, etwa `battle.net` → `SourceId` |
| Produkt | 2 (Text) | Produktcode, etwa `bna` → Start-URI |
| Produkt | 3 (Länge) | Einstellungen |
| Einstellungen | 1 (Text) | Installationsordner mit `/` → `InstallDir` (über `PathUtil.Normalize`) |

Alle anderen Felder (Zustand, Versionen, Build-Schlüssel, Sprachen, Felder 2–6 der obersten Ebene) werden übersprungen.
Texte müssen gültiges UTF-8 sein. Fehler in einem Produkteintrag betreffen nur diesen Eintrag; ist die Datei
abgeschnitten, bleiben die Einträge davor erhalten und es gibt einen Fehler mit Byte-Position.

Anzeigename und EXE stehen nicht in product.db. Sie kommen aus dem Uninstall-Eintrag (HKLM 32 Bit, HKLM 64 Bit, HKCU),
dessen `UninstallString` den Blizzard Uninstaller mit `--uid=<uid>` aufruft: `DisplayName` als Name, `DisplayIcon`
als EXE (nur wenn vorhanden und unterhalb des Installationsordners). Ohne Uninstall-Eintrag: Ordnername als Name, keine EXE.

## 3. Ergebnis am echten System

`dotnet run --project src\HdrPilot.Cli -- scan --source BattleNet` (Exit 0, 26 ms, `launcherDetected: true`, keine Fehler):

| ID | Name | InstallDir | Start-Befehl |
|---|---|---|---|
| – | – | – | – |

Keine Spiele. Gelesen wurden zwei Produkte (`agent`, `battle.net`), beide als Nicht-Spiel herausgefiltert.

Zweite Quellen und Vergleich:

| Zweite Quelle | Inhalt | Abweichung zum Scan |
|---|---|---|
| `%ProgramData%\Battle.net\Agent\aggregate.json` | `{"installed": []}` | keine |
| `%AppData%\Battle.net\Battle.net.config`, `Games` | nur `battle_net` (`ServerUid` `battle.net`) | keine (Battle.net-App ist kein Spiel) |
| Uninstall-Einträge mit `Blizzard Uninstaller.exe` | nur `Battle.net` (`--uid=battle.net`) | keine |
| Ordner `C:\Program Files (x86)\Battle.net` | nur Dateien der App (`Battle.net.exe`, `Battle.net Launcher.exe`, `Battle.net.<Build>\`, `.battle.net\`, `Launcher.db`, …) | keine (dort liegen keine Spielordner) |

## 4. Entscheidungen

- **Scanner umgesetzt statt weggelassen.** Der Auftrag sagt „so gut es geht über product.db“; Pfad und Aufbau ließen
  sich am Zielrechner belegen, der Leser ist klein (ein Wire-Leser, fünf Feldnummern) und ohne Fremdpaket. Grenzen siehe
  unten; wer ein Blizzard-Spiel nicht gefunden bekommt, trägt es manuell ein (Quelle Manuell, Ordner oder EXE).
- **Nur belegte Felder.** uid, Produktcode und Installationsordner, so wie die Vorgabe es verlangt. Das Feld
  „installiert“ im Zustandsblock wird nicht ausgewertet, weil am Zielrechner kein nicht installiertes Produkt zum
  Gegenprüfen existiert; stattdessen muss der Installationsordner existieren.
- **Nicht-Spiele:** Produktcode `agent` oder `bna`, uid `agent` oder `battle.net` (alle am Zielrechner gesehen).
- **SourceId = uid** (eindeutig je Installation, auch für Varianten wie PTR oder Beta), Start-Befehl aus dem Produktcode.
- **Start-Befehl `battlenet://<Produktcode>`.** Das Protokoll ist registriert und übergibt die URI an Battle.net.exe;
  die Variante `--exec="launch <Code>"` bräuchte das Start-Kürzel (etwa `Pro`, `WTCG`), das product.db nicht enthält
  und das am Zielrechner nicht ableitbar ist. Ein unbekannter Code öffnet schlimmstenfalls nur Battle.net. Die
  HDR-Umschaltung hängt davon nicht ab: In Phase 3 zählt jeder Prozess unter dem Installationsordner.
- **Kein Battle.net.config, kein Katalog-Cache.** Battle.net.config enthält Kontodaten (gespeicherte Anmeldenamen) und
  keine Installationsorte; der Cache ist ein Abbild des Online-Katalogs, nicht der Installationen.
- **Unlesbare oder defekte product.db** (gesperrt, abgeschnitten, Binärmüll, falsches UTF-8, Eintrag ohne uid oder
  Produktcode): Fehler mit Pfad, übrige Einträge werden geliefert, keine Ausnahme.

Beispieldateien: `product-launcher-only.db` ist der product.db dieses Rechners nachgebildet (gleicher Aufbau);
ersetzt sind die Länderfelder (`DEU`→`USA`, `DE`→`US`), Versionen, Build-Schlüssel, Größen und Zeitstempel,
jeweils mit gleicher Länge. `product-with-games.db` ist dieselbe Datei mit drei angehängten
Spiel-Einträgen im selben Format (uids `prometheus`, `wow`, `fenris`, frei gewählte Pfade); weil am Zielrechner kein
Spiel installiert ist, sind diese Einträge nachgebildet, nicht gemessen.

## 5. Bekannte Grenzen

- Am Zielrechner ist kein Blizzard-Spiel installiert. Feldnummern und Dateipfad sind belegt, aber ob Spiel-Einträge
  genau so aussehen wie die Einträge von Agent und Battle.net-App, ist nicht gegengeprüft (gleicher Nachrichtentyp
  „Produkt“ ist wahrscheinlich, aber nicht bewiesen).
- Der Start über `battlenet://<Produktcode>` ist nicht mit einem Spiel geprüft; je nach Spiel und Battle.net-Version
  öffnet er womöglich nur die Spielseite.
- Name und EXE hängen am Uninstall-Eintrag mit `--uid=`; fehlt er, erscheint der Ordnername und keine EXE.
- Spiele, die Battle.net nicht in product.db führt (etwa von Hand kopierte Ordner), findet der Scanner nicht; dafür
  gibt es manuelle Einträge und Suchordner.
- Ändert Blizzard die Feldnummern, liefert der Scanner Fehler bzw. keine Spiele, aber keine falschen Pfade.
