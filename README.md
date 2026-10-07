# HdrPilot

HdrPilot schaltet Windows-HDR automatisch ein, wenn du ein ausgewähltes Spiel startest, und stellt danach den
vorherigen Zustand wieder her. Es findet deine installierten Spiele selbst (Steam, Epic, Xbox/Game Pass, GOG,
Ubisoft Connect, EA app, Battle.net). Spiele ohne Launcher trägst du selbst ein, oder HdrPilot schlägt sie aus deinen
Suchordnern vor. Im Alltag läuft es unauffällig im Infobereich.

- Eine einzelne EXE, keine Installation, keine Administratorrechte, kein Windows-Dienst.
- Kein Netzwerkzugriff, keine Telemetrie, keine Eingriffe in Spielprozesse (HdrPilot liest nur die Prozessliste).
- Im Hintergrund sparsam: eine Prozessabfrage pro Sekunde, gemessen rund 0,1–0,4 % eines CPU-Kerns und etwa 40–50 MB
  Arbeitsspeicher.

## Voraussetzungen

- Windows 11 oder Windows 10 ab 21H2, 64 Bit.
- Ein HDR-fähiger Bildschirm, bei dem sich unter *Einstellungen → System → Anzeige* „HDR verwenden“ einschalten lässt.
- Keine .NET-Installation nötig, die Laufzeit steckt in der EXE.

## Einrichtung

1. `HdrPilot.exe` in einen dauerhaften Ordner legen, etwa `%LOCALAPPDATA%\Programs\HdrPilot\`. Autostart und
   Verknüpfungen zeigen auf diesen Ort. Verschiebst du die EXE später, korrigiert HdrPilot den Autostart-Eintrag beim
   nächsten Start selbst; Verknüpfungen musst du neu anlegen.
2. `HdrPilot.exe` starten. Die EXE ist nicht signiert, deshalb fragt Windows SmartScreen beim ersten Start nach
   („Weitere Informationen“ → „Trotzdem ausführen“).
3. Es öffnet sich kein Fenster. HdrPilot läuft als Symbol im Infobereich (unten rechts neben der Uhr), meldet sich
   kurz und liest deine Spiele ein. Siehst du das Symbol nicht, hat Windows es hinter dem Pfeil (˄) ausgeblendet; von
   dort kannst du es in die Taskleiste ziehen. Ein Doppelklick auf das Symbol (oder ein zweiter Start der EXE) öffnet
   die Spielebibliothek.
4. Bei den Spielen, die HDR bekommen sollen, den Schalter **HDR automatisch** einschalten. Eingelesene Spiele sind
   anfangs immer aus; Spiele, die du selbst hinzufügst, haben den Schalter gleich an.
5. Optional in den Einstellungen **Mit Windows starten** einschalten.

Das Fenster zu schließen beendet HdrPilot nicht. Beenden über das Infobereich-Menü → **HdrPilot beenden**.

## Bedienung

### Spielebibliothek

- Liste aller gefundenen Spiele mit Symbol, Name, Quelle und dem Schalter **HDR automatisch**.
- Suche (Strg+F), Filter nach Quelle sowie die Filter **HDR automatisch** und **Neu**.
- Neu gefundene Spiele tragen die Markierung „NEU“. Sie verschwindet, sobald du das Spiel anklickst oder seinen
  Schalter änderst; das Augen-Symbol neben „Neu einlesen“ (**Alle als gesehen markieren**) entfernt sie bei allen.
- **Neu einlesen** liest alle Launcher erneut ein. Das passiert auch bei jedem Start von HdrPilot im Hintergrund.
  Deinstallierte Spiele bleiben mit dem Hinweis „Nicht installiert“ samt Einstellungen in der Liste; nach einer
  Neuinstallation gelten die Einstellungen wieder.
- **Spiel hinzufügen**: Spiele ohne Launcher selbst eintragen. Wähle am besten die Start-EXE des Spiels. Wählst du einen
  Ordner, kann HdrPilot das Spiel nur dann selbst starten, wenn direkt darin genau eine EXE liegt; die Erkennung beim
  Spielen funktioniert in beiden Fällen.
- **Vorschläge aus deinen Suchordnern**: Unterordner deiner Suchordner mit EXE-Dateien erscheinen oben in der
  Bibliothek. Übernommen wird nur, was du dort hinzufügst.
- Details zum gewählten Spiel:
  - **Bildschirm**: welcher Bildschirm geschaltet wird (Standard: die globale Einstellung).
  - **Nach dem Spiel zurückstellen**: danach wieder den HDR-Zustand von vorher herstellen (Standard: an).
  - **EXE-Dateien**: Grundsätzlich zählt jedes Programm im Installationsordner als dieses Spiel, außer Hilfsprozessen.
    Die Liste zeigt die bekannten EXEs mit Herkunft („vom Launcher“, „beim Spielen erkannt“, „von dir hinzugefügt“).
    Du kannst EXEs hinzufügen (auch außerhalb des Spielordners), entfernen (dann zählen sie für dieses Spiel nie) und
    mit **Wieder aufnehmen** zurückholen. **Neu lernen** vergisst die beim Spielen erkannten EXEs.
  - **Mit HDR starten**: schaltet HDR ein, wartet die Bestätigung ab und startet dann das Spiel.
  - **Desktop** / **Startmenü**: legt eine Verknüpfung „<Spiel> (HDR)“ an, die das Spiel genauso über HdrPilot startet.
    Im Startmenü liegt sie im Ordner „HdrPilot“.
  - **Aus der Bibliothek entfernen**: nur für selbst hinzugefügte Spiele.

### Infobereich

Das Symbol zeigt, ob HDR gerade an ist, und bei pausierter Automatik zusätzlich Pausenbalken. Der Tooltip nennt die
Spiele, für die HdrPilot gerade HDR geschaltet hat, und ob die Automatik aktiv ist. Das Menü (Rechtsklick) bietet:

- **HDR jetzt einschalten** / **HDR jetzt ausschalten**
- **Automatik pausieren**: keine neuen automatischen Schaltungen; laufende Spiele werden noch normal zurückgestellt.
  „Mit HDR starten“ und „(HDR)“-Verknüpfungen schalten weiterhin. Die Pause gilt, bis du sie aufhebst oder HdrPilot
  neu startet; sie wird nicht gespeichert.
- **Spieleliste öffnen**, **Einstellungen**, **Protokollordner öffnen**
- **HdrPilot beenden**

Ein Klick auf einen Hinweis von HdrPilot öffnet ebenfalls die Spielebibliothek.

### Einstellungen

| Einstellung | Bedeutung | Standard |
|---|---|---|
| Bildschirme | Auf welchen Bildschirmen HDR geschaltet wird | Hauptbildschirm |
| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | 1000 ms |
| Nachlaufzeit | Wartezeit nach dem Spielende, bevor zurückgestellt wird | 10 s |
| Startfrist | Bei „Mit HDR starten“: so lange wird auf den Spielprozess gewartet | 120 s |
| Mit Windows starten | Autostart über `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | aus |
| Hinweise anzeigen | Kurze Meldungen im Infobereich beim Schalten und bei Problemen; nie während Vollbild-Spielen, Präsentationen oder bei gesperrtem Bildschirm (dann nur im Protokoll). Aus: alle Meldungen nur im Protokoll | an |
| Suchordner | Ordner mit Spielen ohne Launcher; Unterordner mit EXE werden vorgeschlagen | keine |
| Hilfsprozesse ausschließen | Programme, die nie als Spiel zählen (Absturzmelder, Installer und Laufzeitpakete, Anti-Cheat-Installer und -Dienste, reine Launcher-Fenster …) | Standardliste |
| Protokollstufe (Erweitert) | Wie ausführlich protokolliert wird | Normal |
| Tastenkürzel als Notlösung (Erweitert) | Sendet Win+Alt+B, nur wenn die Windows-Schnittstelle scheitert; braucht die Xbox Game Bar und schaltet alle HDR-fähigen Bildschirme um | aus |

Die Darstellung folgt dem hellen oder dunklen App-Modus von Windows. Animationen laufen nur, wenn in Windows
„Animationseffekte“ an ist, und nur solange ein Fenster sichtbar ist.

## So funktioniert es

**Erkennen (Weg A):** HdrPilot fragt einmal pro Sekunde die Prozessliste ab. Startet ein Programm aus dem
Installationsordner eines Spiels mit „HDR automatisch“, schaltet HdrPilot HDR ein. Reine Launcher-Fenster,
Absturzmelder, Installer und Laufzeitpakete zählen nicht. Start-Helfer, die das Spiel selbst starten (etwa
`gamelaunchhelper.exe` oder `PlayRDR2.exe`), zählen bewusst schon als Spiel, damit HDR früh an ist. Die beim ersten
Lauf erkannten EXEs merkt sich HdrPilot und zeigt sie in den Details.

**Starten über HdrPilot (Weg B):** „Mit HDR starten“ oder eine „(HDR)“-Verknüpfung (`HdrPilot.exe --run <Quelle:ID>`)
schaltet HDR zuerst ein, wartet die Bestätigung ab und startet erst dann das Spiel: über den Launcher-Link der Quelle
(etwa `steam://`), GOG-Spiele und selbst eingetragene Spiele direkt über ihre EXE. Das ist der sichere Weg für Spiele,
die HDR nur beim Start prüfen. Erscheint innerhalb der Startfrist (Standard 120 s) kein erkannter Spielprozess, etwa
weil der Launcher noch aktualisiert, stellt HdrPilot HDR wieder zurück. Scheitert das Einschalten, startet das Spiel
trotzdem.

**Zurückstellen:** Vor dem Schalten merkt sich HdrPilot den HDR-Zustand jedes betroffenen Bildschirms und speichert
ihn sofort. Wenn das letzte HDR-Spiel beendet ist und die Nachlaufzeit (10 s) verstrichen ist, stellt HdrPilot diesen
Zustand wieder her. Laufen mehrere Spiele, wird erst nach dem letzten zurückgestellt, und nur, wenn alle beteiligten
Spiele „Nach dem Spiel zurückstellen“ an haben. Diese Einstellung gilt ab dem nächsten Spielstart. War HDR schon vor
dem Spiel an, schaltet HdrPilot am Ende nichts. Schaltest du HDR während des Spiels von Hand um, überschreibt
HdrPilot das nicht; am Ende schaltet es HDR nur aus, wenn es vor dem Spiel aus war, und nie wieder ein.

**Beenden, Absturz oder Neustart:** Wird HdrPilot während eines Spiels oder in der Nachlaufzeit beendet (auch über
„HdrPilot beenden“, durch einen Absturz oder beim Abmelden), bleibt HDR zunächst an. Beim nächsten Start stellt
HdrPilot den gemerkten Zustand wieder her, sobald kein ausgewähltes Spiel mehr läuft.

**Bildschirm ohne HDR oder nicht angeschlossen:** HdrPilot meldet das (während eines Vollbild-Spiels nur im
Protokoll) und lässt das Spiel normal laufen; es blockiert nie einen Spielstart. Bei Änderungen an den Bildschirmen
(anschließen, Auflösung, Aufwachen aus dem Ruhezustand) holt HdrPilot Ausstehendes nach: Ein Bildschirm, der beim
Spielstart fehlte oder kein HDR konnte, wird dann eingeschaltet, und ein gescheitertes Zurückstellen wird wiederholt.

**Schalten:** über die Windows-Anzeige-Schnittstelle (DisplayConfig), danach wird der Zustand jedes Mal erneut gelesen
und so bestätigt. Ab Windows 11 24H2 (Build 26100) nutzt HdrPilot die neue HDR-Schnittstelle, sonst die ältere
„Advanced Color“-Schnittstelle. Meldet die neue Schnittstelle einen Fehler, weicht HdrPilot automatisch auf die ältere
aus.

## Speicherorte

| Was | Wo |
|---|---|
| Einstellungen und Spieleliste | `%APPDATA%\HdrPilot\config.json` |
| Gemerkter HDR-Zustand während eines Spiels | `%LOCALAPPDATA%\HdrPilot\session-state.json` |
| Protokoll (täglich neue Datei, höchstens 5 MB je Datei, 14 Tage) | `%LOCALAPPDATA%\HdrPilot\logs\` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Wert `HdrPilot` |
| Verknüpfungen | Desktop bzw. `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HdrPilot\`, „<Spiel> (HDR).lnk“ |
| Entpackte Grafik-Bibliotheken (rund 8 MB je Version) | `%TEMP%\.net\HdrPilot\` |

Einstellungen gelten je Spiel über Quelle und ID (etwa `Steam:1091500`), nicht über den Namen. `config.json` wird
atomar geschrieben. Von Hand bearbeitest du sie am besten bei beendetem HdrPilot. Ist sie danach kein gültiges JSON,
sichert HdrPilot sie beim nächsten Start als `config.json.defekt-<Zeit>.bak` und beginnt mit Standardwerten; deine
Auswahl steckt dann nur noch in dieser Sicherung.

Die Grafik-Bibliotheken entpackt HdrPilot beim ersten Start und nach jedem Update auf eine neue Version. Ordner
älterer Versionen bleiben liegen und können bei beendetem HdrPilot gelöscht werden.

## Befehlszeile

### HdrPilot.exe

Läuft HdrPilot schon, reicht ein zweiter Aufruf seine Befehle an die laufende Instanz weiter.

| Aufruf | Wirkung |
|---|---|
| `HdrPilot.exe` | Starten (nur im Infobereich); läuft es schon, öffnet sich die Spielebibliothek |
| `--minimized` | Starten (nur im Infobereich); läuft es schon, passiert nichts. So startet der Autostart |
| `--show` / `--settings` | Spielebibliothek bzw. Einstellungen öffnen |
| `--run <Quelle:ID>` | Spiel über HdrPilot starten (HDR vorher an) |
| `--toggle-hdr` | HDR jetzt umschalten |
| `--pause` / `--resume` | Automatik pausieren bzw. fortsetzen |
| `--rescan` | Spiele neu einlesen |
| `--exit` | Laufende Instanz beenden |
| `--config-dir <ordner>` | Eigene Ablage statt `%APPDATA%`/`%LOCALAPPDATA%` (Tests); eigene Instanz je Ordner |

Den Schlüssel `<Quelle:ID>` zeigt die Oberfläche nicht an. Du findest ihn in der ersten Spalte von
`cli\hdrpilot.exe games list` oder im Ziel einer „(HDR)“-Verknüpfung hinter `--run`. Meist ist es einfacher, die
Verknüpfung in den Details anlegen zu lassen.

### cli\hdrpilot.exe

Werkzeug für Fehlersuche und Skripte, im Alltag nicht nötig.

```
hdrpilot hdr status [--json] [--api modern|legacy]
hdrpilot hdr on|off|toggle [--display <auswahl>] [--api modern|legacy] [--hotkey-fallback] [--json]
hdrpilot scan [--source <quelle>] [--search "<ordner>;<ordner>"]
hdrpilot games list [--json]
hdrpilot games import
hdrpilot games add --path <exe|ordner> [--name <name>] [--hdr on|off]
hdrpilot games set <schlüssel> [--hdr on|off] [--restore on|off] [--display <monitorDevicePath>|global]
hdrpilot watch [--verbose]
hdrpilot run <schlüssel>
```

`<auswahl>`: `primary` (Standard), `all`, Nummer aus `hdr status`, Teil des Namens oder `monitorDevicePath`.
`<quelle>`: Steam, Epic, Xbox, Gog, Ubisoft, EA, BattleNet. `games`, `watch` und `run` verstehen außerdem
`--config-dir <ordner>`. `hdrpilot --help` zeigt die vollständige Hilfe.

Exit-Codes: 0 ok, 1 Fehler, 2 Aufruf falsch, 3 Bildschirm nicht gefunden, 4 Bildschirm ohne HDR,
5 Schalten fehlgeschlagen, 6 Spiel nicht gefunden.

Die `games`-Befehle kannst du neben der laufenden App benutzen; HdrPilot liest die Änderungen selbst neu ein.
`hdrpilot watch` und `hdrpilot run` betreiben dagegen dieselbe Automatik wie die App und nutzen ohne `--config-dir`
dieselbe Ablage. Sie prüfen nicht, ob die App läuft: Dann schalten beide unabhängig und überschreiben sich den
gemerkten HDR-Zustand. Vorher HdrPilot beenden oder bei laufender App `HdrPilot.exe --run <Quelle:ID>` verwenden.

## Bekannte Grenzen

- **Erkennung per Abfrage:** Zwischen Spielstart und HDR liegt bis zu ein Abfrageintervall (Standard 1 s) plus die
  Schaltzeit. Die meisten Spiele kommen damit zurecht; Spiele, die HDR nur beim Start prüfen, über „Mit HDR starten“
  oder eine „(HDR)“-Verknüpfung starten.
- **Spiel-EXE außerhalb des Installationsordners:** Manche Starter starten die eigentliche EXE woanders. Solche Spiele
  erkennt HdrPilot erst, wenn du die EXE in den Details hinzufügst; bis dahin stellt auch „Mit HDR starten“ HDR nach
  der Startfrist wieder zurück.
- **Geschützte Spielprozesse:** Gibt Windows den Pfad eines Prozesses nicht heraus, erkennt HdrPilot ihn nur am
  EXE-Namen, und nur, wenn diese EXE dem Spiel bekannt ist. Dann die EXE in den Details hinzufügen.
- **Mehrere Bildschirme:** HdrPilot erkennt nicht, auf welchem Bildschirm ein Spiel läuft, sondern schaltet die
  eingestellten (Standard: Hauptbildschirm). Läuft ein Spiel woanders, in den Details den richtigen **Bildschirm**
  wählen. Auf echter Hardware geprüft wurde nur mit einem Bildschirm.
- **Nicht automatisch gefunden** werden: in Steam eingetragene Fremdspiele, ältere Store-Spiele direkt unter
  `C:\Program Files\WindowsApps`, Xbox-Spiele auf USB- oder Netzlaufwerken, GOG-Spiele ohne Registry-Eintrag und
  Epic-Spiele ohne Manifest. Solche Spiele über „Spiel hinzufügen“ eintragen.
- **Start über die Launcher:** Die Reihenfolge (HDR bestätigt vor dem Spielstart) ist automatisch mit einem
  Testprogramm geprüft. Die Start-Befehle der einzelnen Launcher wurden noch nicht mit echten Spielen ausgeführt.
- **GOG:** „Mit HDR starten“ startet die Spiel-EXE direkt, an GOG Galaxy vorbei. Cloud-Speicherstände, Overlay und
  Spielzeit von Galaxy sind dann womöglich nicht aktiv.
- **EA app:** Die Bibliotheksdatei der EA app ist verschlüsselt und an die Hardware gebunden; HdrPilot entschlüsselt
  sie bewusst nicht und findet EA-Spiele über die Installationsdaten der Spiele selbst.
- **Battle.net:** Am Testrechner war kein Blizzard-Spiel installiert. Das Einlesen von Blizzard-Spielen ist deshalb nur
  mit nachgebildeten Daten getestet, das Starten über Battle.net gar nicht. Fehlt ein Blizzard-Spiel, seinen Ordner
  über „Spiel hinzufügen“ eintragen.
- **Windows 10:** Die ältere Schnittstelle ist mit Tests abgedeckt und wurde auf echter Hardware unter Windows 11
  (Build 26200) mitgeprüft; ein echtes Windows 10 stand nicht zur Verfügung.
- **Tastenkürzel-Notlösung:** HdrPilot sendet Win+Alt+B nur, wenn sie eingeschaltet ist und die Windows-Schnittstelle
  gescheitert ist. Das Kürzel braucht die Xbox Game Bar, schaltet nur um und wirkt auf alle HDR-fähigen Bildschirme;
  HdrPilot bestätigt aber nur den Zielbildschirm. Auf echter Hardware ist die Notlösung nicht getestet.
- **Nicht signiert:** SmartScreen und manche Virenscanner warnen bei unbekannten, unsignierten EXEs.
- **Nur auf Deutsch:** Oberfläche, Hinweise, Fehlermeldungen der Launcher-Scanner, Kommandozeile und Protokoll.

## Fehlersuche

- **HDR schaltet gar nicht:** Lässt sich HDR in den Windows-Anzeigeeinstellungen von Hand einschalten? Zeigt der
  Tooltip „kein HDR-fähiger Bildschirm“, meldet Windows für keinen Zielbildschirm HDR. Mit
  `cli\hdrpilot.exe hdr status` siehst du, was Windows meldet; `hdr on` und `hdr off` schalten zum Test von Hand.
  Hinweise während eines Vollbild-Spiels stehen nur im Protokoll.
- **Spiel wird nicht erkannt:** Ist „HDR automatisch“ an und die Automatik nicht pausiert? Liegt die laufende EXE im
  Installationsordner? Wenn nicht, die EXE in den Details hinzufügen. Ist sie dort als „ausgeschlossen“ markiert, mit
  „Wieder aufnehmen“ zurückholen. Steht sie unter „Hilfsprozesse ausschließen“, das Muster dort entfernen.
- **HDR kommt für ein Spiel zu spät:** über „Mit HDR starten“ oder eine „(HDR)“-Verknüpfung starten.
- **HDR bleibt nach dem Spiel an:** Die Nachlaufzeit abwarten (Standard 10 s). Nennt der Tooltip das Spiel danach
  noch, läuft noch ein Programm aus seinem Ordner (etwa ein Launcher-Rest): im Task-Manager nachsehen und diese EXE in
  den Details entfernen. Weitere Gründe: Ein anderes HDR-Spiel läuft noch; HDR war schon vor dem Spiel an; „Nach dem
  Spiel zurückstellen“ ist für dieses oder ein gleichzeitig gelaufenes Spiel aus. Nach „Mit HDR starten“ ohne
  Spielstart bleibt HDR bis zum Ende der Startfrist an.
- **„Mit HDR starten“ ist gesperrt:** Das Spiel ist nicht installiert, oder bei einem selbst eingetragenen Ordner kennt
  HdrPilot keine Start-EXE. Dann das Spiel über seine EXE hinzufügen.
- **HdrPilot war abgestürzt oder beendet:** einfach neu starten, der gemerkte Zustand wird dann wiederhergestellt.
- **Ein Spiel fehlt in der Liste:** „Neu einlesen“ drücken; fehlt es weiter, über „Spiel hinzufügen“ eintragen oder
  seinen übergeordneten Ordner als Suchordner angeben. Fehler einzelner Launcher stehen in der Bibliothek und im
  Protokoll.
- **Protokoll:** Infobereich → „Protokollordner öffnen“. Für mehr Einzelheiten unter Einstellungen → Erweitert die
  Protokollstufe auf „Ausführlich“ stellen. Jede Schaltung steht dort mit Auslöser, Bildschirm, Zustand vorher und
  nachher.
- **Alles zurücksetzen:** HdrPilot beenden und `%APPDATA%\HdrPilot\config.json` löschen. Die Spiele werden beim
  nächsten Start neu eingelesen; deine Auswahl geht dabei verloren.

## Entfernen

1. In den Einstellungen **Mit Windows starten** ausschalten.
2. Warten, bis kein Spiel mehr läuft und HDR zurückgestellt ist, dann im Infobereich **HdrPilot beenden**.
3. `HdrPilot.exe` und den Ordner `cli` löschen, dazu die Ordner `%APPDATA%\HdrPilot`, `%LOCALAPPDATA%\HdrPilot`,
   `%TEMP%\.net\HdrPilot`, den Startmenü-Ordner `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HdrPilot` und die
   „(HDR)“-Verknüpfungen auf dem Desktop.

## Aus dem Quelltext bauen

Benötigt das .NET 10 SDK. Die Strukturtests brauchen außerdem vier Header des Windows SDK:
`tools\fetch-sdk-headers.ps1` lädt sie einmalig (rund 153 MB von nuget.org) nach `.sdk\`. Alternativ zeigt die
Umgebungsvariable `HDRPILOT_SDK_INCLUDE` auf den `um`-Ordner eines installierten Windows SDK. Ohne die Header
überspringt der Testlauf diese Tests, und `test.ps1` meldet einen Fehler.

```
dotnet build HdrPilot.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-sdk-headers.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\gates\test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1
```

`tools\publish.ps1` legt die fertige Auslieferung unter `artifacts\publish\HdrPilot\` ab. Begründungen für
Designentscheidungen stehen in `docs\entscheidungen.md`, die Prüfnachweise in `GATES.md`.

## Lizenzen von Fremdbestandteilen

HdrPilot enthält die .NET-Laufzeit samt WPF und Windows Forms sowie die Pakete
`Microsoft.Extensions.Logging.Abstractions` und `Microsoft.Extensions.DependencyInjection.Abstractions`, alle unter
der MIT-Lizenz. Die Lizenztexte und die Hinweise zu Fremdcode innerhalb der .NET-Laufzeit liegen der Auslieferung als
`THIRD-PARTY-NOTICES.txt` bei. Keine Bestandteile stehen unter GPL oder AGPL.
