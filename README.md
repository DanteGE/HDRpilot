# HDRpilot

HDRpilot schaltet Windows-HDR automatisch ein, wenn du ein ausgewähltes Spiel startest, und stellt danach den
vorherigen Zustand wieder her. Es findet deine installierten Spiele selbst (Steam, Epic, Xbox/Game Pass, GOG,
Ubisoft Connect, EA app, Battle.net). Spiele ohne Launcher trägst du selbst ein, oder HDRpilot schlägt sie aus deinen
Suchordnern vor. Welche Spiele HDR unterstützen, zeigt es dir laut PCGamingWiki an. Im Alltag läuft es unauffällig im
Infobereich.

- Ein Setup ohne Administratorrechte, alternativ eine einzelne EXE ganz ohne Installation; kein Windows-Dienst.
- Keine Telemetrie und keine Eingriffe in Spielprozesse. Ins Netz geht HDRpilot nur für den Abgleich der HDR-Liste mit
  PCGamingWiki; das lässt sich abschalten.
- Im Hintergrund sparsam: eine Prozessabfrage pro Sekunde, gemessen rund 0,1–0,4 % eines CPU-Kerns und etwa 40–50 MB
  Arbeitsspeicher.

## Voraussetzungen

- Windows 11 oder Windows 10 ab 21H2, 64 Bit.
- Ein HDR-fähiger Bildschirm, bei dem sich unter *Einstellungen → System → Anzeige* „HDR verwenden“ einschalten lässt.
- Keine .NET-Installation nötig, die Laufzeit steckt in der EXE.

## Einrichtung

Empfohlen ist der Setup `HDRpilot-Setup-1.2.0.exe`. Er installiert nur für deinen Windows-Benutzer und braucht keine
Administratorrechte. Wer lieber ohne Installation arbeitet, nimmt den Ordner `HDRpilot` (siehe „Ohne Installation“).

1. `HDRpilot-Setup-1.2.0.exe` starten. Die Datei ist nicht signiert, deshalb fragt Windows SmartScreen beim ersten
   Start nach („Weitere Informationen“ → „Trotzdem ausführen“).
2. Der Setup zeigt den Programmordner `%LOCALAPPDATA%\Programs\HDRpilot` und drei Optionen: **Mit Windows starten**
   (vorbelegt), **Verknüpfung auf dem Desktop** (nicht vorbelegt) und **HDRpilot danach starten** (vorbelegt).
   **Installieren** kopiert HDRpilot dorthin, legt eine Verknüpfung im Startmenü an und trägt HDRpilot unter
   *Einstellungen → Apps → Installierte Apps* ein. Die Kommandozeile liegt danach in `<Programmordner>\cli`, daneben
   README und Lizenzhinweise.
3. Mit der dritten Option öffnet sich nach der Installation die Spielebibliothek (nicht, wenn der Setup mit
   Administratorrechten läuft; dann startest du HDRpilot über das Startmenü). Im Alltag läuft HDRpilot als Symbol
   im Infobereich (unten rechts neben der Uhr) und liest bei jedem Start deine Spiele ein. Siehst du das Symbol nicht,
   hat Windows es hinter dem Pfeil (˄) ausgeblendet; von dort kannst du es in die Taskleiste ziehen. Ein Doppelklick
   auf das Symbol (oder ein Start über das Startmenü) öffnet die Spielebibliothek.
4. Bei den Spielen, die HDR bekommen sollen, den Schalter **HDR automatisch** einschalten. Eingelesene Spiele sind
   anfangs immer aus; Spiele, die du selbst hinzufügst, haben den Schalter gleich an. Der Filter **HDR-fähig** zeigt
   dir, welche deiner Spiele laut PCGamingWiki HDR unterstützen.

Das Fenster zu schließen beendet HDRpilot nicht. Beenden über das Infobereich-Menü → **HDRpilot beenden**.

Einstellungen, Spieleliste, Protokolle und HDR-Liste liegen nicht im Programmordner, sondern in `%APPDATA%\HDRpilot`
und `%LOCALAPPDATA%\HDRpilot`. Der Setup fasst sie nie an: Hattest du HDRpilot schon, gelten sie nach der Installation
unverändert weiter. Lag deine bisherige `HDRpilot.exe` schon in `%LOCALAPPDATA%\Programs\HDRpilot`, ersetzt der Setup
sie dort, und Autostart, Verknüpfungen und Steam-Startoptionen zeigen weiter auf dieselbe EXE. Lag sie woanders,
beendet der Setup eine laufende alte Version und startet danach die neue; die alte EXE kannst du dann löschen, ihre
Verknüpfungen und Steam-Startoptionen legst du neu an.

### Aktualisieren

Den Setup der neuen Version starten. Er zeigt die installierte und die neue Version; Autostart und Desktop-Verknüpfung
sind wie bisher vorbelegt. **Aktualisieren** beendet ein laufendes HDRpilot kurz, ersetzt die Programmdateien und
startet HDRpilot wieder, wenn es vorher lief (wieder nur im Infobereich); lief es nicht, entscheidest du mit
**HDRpilot danach starten**. Schlägt dabei etwas fehl, bleibt die bisherige Version unverändert und lauffähig.
Einstellungen, Spieleliste, Protokolle und HDR-Liste bleiben erhalten, weil sie in `%APPDATA%\HDRpilot` und
`%LOCALAPPDATA%\HDRpilot` liegen. Denselben Setup noch einmal auszuführen repariert die Installation. Eine ältere
Version installiert er nur, wenn du **Trotzdem die ältere Version installieren** ankreuzt. Welche Version läuft, zeigen
die oberste Zeile des Infobereich-Menüs und der Kopf der Einstellungen.

### Ohne Installation

1. Den Ordner `HDRpilot` (mit `HDRpilot.exe` und `cli\hdrpilot.exe`) an einen dauerhaften Ort legen, etwa nach
   `%LOCALAPPDATA%\Programs\HDRpilot\`. Autostart, Verknüpfungen und Steam-Startoptionen zeigen auf diesen Ort.
   Verschiebst du die EXE später, korrigiert HDRpilot den Autostart-Eintrag beim nächsten Start selbst; Verknüpfungen
   und Steam-Startoptionen musst du neu anlegen.
2. `HDRpilot.exe` starten (SmartScreen wie oben). Es öffnet sich kein Fenster: HDRpilot meldet sich kurz im
   Infobereich und liest deine Spiele ein. Ein Doppelklick auf das Symbol oder ein zweiter Start der EXE öffnet die
   Spielebibliothek; weiter wie oben in Schritt 4.
3. Optional in den Einstellungen **Mit Windows starten** einschalten.

Auf eine Installation per Setup kannst du jederzeit wechseln; Einstellungen und Spieleliste bleiben dabei erhalten.

## Bedienung

### Spielebibliothek

- Links die Liste aller gefundenen Spiele mit Symbol, Name, Quelle und dem Schalter **HDR automatisch**, rechts die
  Details zum gewählten Spiel. Den Trenner dazwischen kannst du ziehen (oder mit den Pfeiltasten verschieben, per
  Doppelklick zurücksetzen); die Breite merkt sich HDRpilot.
- Ein Kennzeichen „HDR“ zeigt Spiele, die laut PCGamingWiki HDR unterstützen, „HDR*“ solche mit eingeschränktem HDR.
- Suche (Strg+F), Filter nach Quelle sowie die Filter **HDR automatisch**, **Neu** und **HDR-fähig**.
- Sortierung nach **Name**, **Zuletzt gespielt** oder **HDR-Unterstützung**; die Wahl bleibt gespeichert. „Zuletzt
  gespielt“ kennt Steam-Spiele aus Steams eigenen Daten und alle Spiele, die HDRpilot laufen sieht.
- Neu gefundene Spiele tragen die Markierung „NEU“. Sie verschwindet, sobald du das Spiel anklickst oder seinen
  Schalter änderst; das Augen-Symbol neben „Neu einlesen“ (**Alle als gesehen markieren**) entfernt sie bei allen.
- **Neu einlesen** liest alle Launcher erneut ein. Das passiert auch bei jedem Start von HDRpilot im Hintergrund.
  Deinstallierte Spiele bleiben mit dem Hinweis „Nicht installiert“ samt Einstellungen in der Liste; nach einer
  Neuinstallation gelten die Einstellungen wieder.
- **Spiel hinzufügen**: Spiele ohne Launcher selbst eintragen. Wähle am besten die Start-EXE des Spiels. Wählst du einen
  Ordner, kann HDRpilot das Spiel nur dann selbst starten, wenn direkt darin genau eine EXE liegt; die Erkennung beim
  Spielen funktioniert in beiden Fällen.
- **Vorschläge aus deinen Suchordnern**: Unterordner deiner Suchordner mit EXE-Dateien erscheinen oben in der
  Bibliothek. Übernommen wird nur, was du dort hinzufügst.
- Details zum gewählten Spiel, von oben nach unten:
  - **Mit HDR starten**: schaltet HDR ein, wartet die Bestätigung ab und startet dann das Spiel.
  - **Desktop** / **Startmenü**: legt eine Verknüpfung „<Spiel> (HDR)“ an, die das Spiel genauso über HDRpilot startet.
    Im Startmenü liegt sie im Ordner „HDRpilot“.
  - **Steam-Startoption kopieren** (nur bei Steam-Spielen): Notlösung für Spiele, die du lieber direkt aus Steam
    startest. Kopiert `"…\HDRpilot.exe" --wrap -- %command%`; in Steam per Rechtsklick auf das Spiel → Eigenschaften →
    Allgemein → Startoptionen einfügen. Dann ist HDR auch beim Start aus Steam an, bevor das Spiel startet
    (HDRpilot muss dafür laufen).
  - **HDR automatisch**, **Bildschirm** (welcher Bildschirm geschaltet wird, Standard: die globale Einstellung) und
    **Nach dem Spiel zurückstellen** (danach wieder den HDR-Zustand von vorher herstellen, Standard: an).
  - **EXE-Dateien**: Grundsätzlich zählt jedes Programm im Installationsordner als dieses Spiel, außer Hilfsprozessen.
    Die Liste zeigt die bekannten EXEs mit Herkunft („vom Launcher“, „beim Spielen erkannt“, „von dir hinzugefügt“).
    Du kannst EXEs hinzufügen (auch außerhalb des Spielordners), entfernen (dann zählen sie für dieses Spiel nie) und
    mit **Wieder aufnehmen** zurückholen. **Neu lernen** vergisst die beim Spielen erkannten EXEs.
  - **Aus der Bibliothek entfernen**: nur für selbst hinzugefügte Spiele.

### Infobereich

Das Symbol zeigt, ob HDR gerade an ist, und bei pausierter Automatik zusätzlich Pausenbalken. Der Tooltip nennt die
Spiele, für die HDRpilot gerade HDR geschaltet hat, ob die Automatik aktiv ist und gegebenenfalls, wie lange der
Neustart-Puffer HDR noch anlässt. Das Menü (Rechtsklick) bietet:

- **HDR jetzt einschalten** / **HDR jetzt ausschalten**
- **Automatik pausieren**: keine neuen automatischen Schaltungen; laufende Spiele werden noch normal zurückgestellt.
  „Mit HDR starten“, „(HDR)“-Verknüpfungen und die Steam-Startoption schalten weiterhin. Die Pause gilt, bis du sie
  aufhebst oder HDRpilot neu startet; sie wird nicht gespeichert.
- **Spieleliste öffnen**, **Einstellungen**, **Protokollordner öffnen**
- **HDRpilot beenden**

Ganz oben im Menü steht grau und nicht anklickbar die Version („HDRpilot“ mit Versionsnummer), dieselbe Nummer wie im
Setup und unter „Installierte Apps“.

Ein Klick auf einen Hinweis von HDRpilot öffnet ebenfalls die Spielebibliothek.

### Einstellungen

| Einstellung | Bedeutung | Standard |
|---|---|---|
| Bildschirme | Auf welchen Bildschirmen HDR geschaltet wird | Hauptbildschirm |
| Abfrageintervall | Wie oft nach Spielstarts gesehen wird | 1000 ms |
| Nachlaufzeit | Wartezeit nach dem Spielende, bevor zurückgestellt wird | 10 s |
| Neustart-Puffer | Endet ein Spiel weniger als 5 Minuten nach dem Start, bleibt HDR so lange an (Minuten und Sekunden, höchstens 30:00, 0:00 = aus; siehe „So funktioniert es“) | 1:00 |
| Startfrist | Bei „Mit HDR starten“: so lange wird auf den Spielprozess gewartet | 120 s |
| Mit Windows starten | Autostart über `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | aus |
| Hinweise anzeigen | Kurze Meldungen im Infobereich beim Schalten und bei Problemen; nie während Vollbild-Spielen, Präsentationen oder bei gesperrtem Bildschirm (dann nur im Protokoll). Aus: alle Meldungen nur im Protokoll | an |
| Suchordner | Ordner mit Spielen ohne Launcher; Unterordner mit EXE werden vorgeschlagen | keine |
| Hilfsprozesse ausschließen | Programme, die nie als Spiel zählen (Absturzmelder, Installer und Laufzeitpakete, Anti-Cheat-Installer und -Dienste, reine Launcher-Fenster …) | Standardliste |
| Bei jedem Start abgleichen | HDR-Liste von PCGamingWiki 30 s nach dem Start abgleichen, höchstens einmal pro Stunde (siehe „HDR-Liste“) | an |
| Protokollstufe (Erweitert) | Wie ausführlich protokolliert wird | Normal |
| Tastenkürzel als Notlösung (Erweitert) | Sendet Win+Alt+B, nur wenn die Windows-Schnittstelle scheitert und gerade kein Spiel läuft; braucht die Xbox Game Bar und schaltet alle HDR-fähigen Bildschirme um | aus |

Im Bereich „HDR-Liste (PCGamingWiki)“ gibt es außerdem **Jetzt abgleichen**, den Stand der Liste und das optionale
PCGamingWiki-Konto (siehe unten).

Nur in `config.json` einstellbar: `launcherSignals` (Frühwarnung aus den Protokollen von Epic und Steam, Standard
`true`). Den Neustart-Puffer speichert HDRpilot dort als `restartBufferSeconds` (in Sekunden); fehlt der Wert, gilt der
Standard von 1 Minute.

Rechts im Kopf der Einstellungen steht die Version von HDRpilot, dieselbe Nummer wie oben im Infobereich-Menü.

Die Darstellung folgt dem hellen oder dunklen App-Modus von Windows. Animationen laufen nur, wenn in Windows
„Animationseffekte“ an ist, und nur solange ein Fenster sichtbar ist.

## So funktioniert es

**Erkennen (Weg A):** HDRpilot fragt einmal pro Sekunde die Prozessliste ab. Startet ein Programm aus dem
Installationsordner eines Spiels mit „HDR automatisch“, schaltet HDRpilot HDR ein. Reine Launcher-Fenster,
Absturzmelder, Installer und Laufzeitpakete zählen nicht. Start-Helfer, die das Spiel selbst starten (etwa
`gamelaunchhelper.exe` oder `PlayRDR2.exe`), zählen bewusst schon als Spiel, damit HDR früh an ist. Die beim ersten
Lauf erkannten EXEs merkt sich HDRpilot und zeigt sie in den Details.

**Frühwarnung bei Epic und Steam:** Startest du ein Spiel mit „HDR automatisch“ aus dem Epic Games Launcher oder aus
Steam, liest HDRpilot das in deren eigenen Protokolldateien mit (nur lesend) und schaltet HDR schon 1–2 s vor dem
Spielprozess ein. Kommt das Spiel doch nicht, stellt die Startfrist HDR wieder zurück. Abschaltbar über
`launcherSignals` in `config.json`.

**Starten über HDRpilot (Weg B):** „Mit HDR starten“ oder eine „(HDR)“-Verknüpfung (`HDRpilot.exe --run <Quelle:ID>`)
schaltet HDR zuerst ein, wartet die Bestätigung ab und startet erst dann das Spiel: über den Launcher-Link der Quelle
(etwa `steam://`), GOG-Spiele und selbst eingetragene Spiele direkt über ihre EXE. Das ist der sichere Weg für Spiele,
die HDR nur beim Start prüfen. Erscheint innerhalb der Startfrist (Standard 120 s) kein erkannter Spielprozess, etwa
weil der Launcher noch aktualisiert, stellt HDRpilot HDR wieder zurück. Solange ein Programm aus dem Spielordner läuft
(etwa ein Launcher-Fenster, das auf die Anmeldung wartet), wartet HDRpilot weiter, höchstens 30 Minuten. Scheitert das
Einschalten, startet das Spiel trotzdem. Doppelte Startanforderungen (doppelter Doppelklick) übergeht HDRpilot.

**Steam-Startoption:** Mit `"…\HDRpilot.exe" --wrap -- %command%` in den Startoptionen eines Steam-Spiels startet Steam
zuerst HDRpilot. Läuft HDRpilot, schaltet es HDR ein, wartet die Bestätigung und noch 1,5 s ab, startet dann das Spiel
und beendet sich sofort (es hält nichts am Spiel fest). Wie bei „Mit HDR starten“ gilt das auch bei pausierter
Automatik und für Spiele ohne „HDR automatisch“. Läuft HDRpilot nicht oder ist das Spiel unbekannt, startet das Spiel
sofort ohne HDR.

**Zurückstellen:** Vor dem Schalten merkt sich HDRpilot den HDR-Zustand jedes betroffenen Bildschirms und speichert
ihn sofort. Wenn das letzte HDR-Spiel beendet ist und die Nachlaufzeit (10 s) verstrichen ist, stellt HDRpilot diesen
Zustand wieder her. Laufen mehrere Spiele, wird erst nach dem letzten zurückgestellt, und nur, wenn alle beteiligten
Spiele „Nach dem Spiel zurückstellen“ an haben. Diese Einstellung gilt ab dem nächsten Spielstart. War HDR schon vor
dem Spiel an, schaltet HDRpilot am Ende nichts. Schaltest du HDR während des Spiels von Hand um, überschreibt
HDRpilot das nicht; am Ende schaltet es HDR nur aus, wenn es vor dem Spiel aus war, und nie wieder ein.

**Neustart-Puffer:** Endet ein Spiel weniger als 5 Minuten nach dem Start (typisch für Hänger oder Abstürze) und hat
HDRpilot HDR dafür eingeschaltet, bleibt HDR noch eine Weile an statt nur die Nachlaufzeit: Standard 1 Minute,
einstellbar unter „Einstellungen“ in Minuten und Sekunden (0:00 bis 30:00, 0:00 schaltet den Puffer ab; ist er nicht
länger als die Nachlaufzeit, wirkt er wie 0:00). Eine Änderung gilt ab dem nächsten Spielende, ohne Neustart von
HDRpilot. Startest du das Spiel in dieser Zeit neu, ist HDR schon an (hast du es zwischendurch von Hand ausgeschaltet,
schaltet HDRpilot es bei Spielen mit „HDR automatisch“ wieder ein). Läuft beim Ablauf des Puffers eine
Vollbild-Anwendung, wartet HDRpilot mit dem Zurückstellen, bis sie endet (höchstens 30 Minuten; nur nach dem Puffer,
nie nach der Nachlaufzeit). Spiele, die HDRpilot nach einem eigenen Neustart übernimmt, gelten nie als kurz.

**Hängender Spielprozess:** Läuft ein Spiel nach dem Spielen unsichtbar weiter (kein Fenster, keine Rechenzeit), lässt
HDRpilot HDR an, weil das Spiel ja noch läuft. Nach 2 Minuten ohne jede Aktivität erscheint einmal ein Hinweis, dass das
Spiel im Hintergrund zu hängen scheint; läuft gerade ein Vollbild-Programm oder ist der Bildschirm gesperrt, wartet
der Hinweis, bis er wieder angezeigt werden kann. HDRpilot beendet nichts selbst.

**Beenden, Absturz oder Neustart:** Wird HDRpilot während eines Spiels oder in der Nachlaufzeit beendet (auch über
„HDRpilot beenden“, durch einen Absturz oder beim Abmelden), bleibt HDR zunächst an. Beim nächsten Start stellt
HDRpilot den gemerkten Zustand wieder her, sobald kein Spiel aus diesem Zustand mehr läuft.

**Bildschirm ohne HDR oder nicht angeschlossen:** HDRpilot meldet das (während eines Vollbild-Spiels nur im
Protokoll) und lässt das Spiel normal laufen; es blockiert nie einen Spielstart. Bei Änderungen an den Bildschirmen
(anschließen, Auflösung, Aufwachen aus dem Ruhezustand) holt HDRpilot Ausstehendes nach: Ein Bildschirm, der beim
Spielstart fehlte oder kein HDR konnte, wird dann eingeschaltet, und ein gescheitertes Zurückstellen wird wiederholt.

**Schalten:** über die Windows-Anzeige-Schnittstelle (DisplayConfig), danach wird der Zustand jedes Mal erneut gelesen
und so bestätigt. Ab Windows 11 24H2 (Build 26100) nutzt HDRpilot die neue HDR-Schnittstelle, sonst die ältere
„Advanced Color“-Schnittstelle. Meldet die neue Schnittstelle einen Fehler, weicht HDRpilot automatisch auf die ältere
aus.

## HDR-Liste (PCGamingWiki)

HDRpilot zeigt, welche deiner Spiele laut [PCGamingWiki](https://www.pcgamingwiki.com) HDR unterstützen. Abgeglichen
wird 30 s nach jedem Start im Hintergrund, höchstens einmal pro Stunde, und jederzeit über **Jetzt abgleichen**.

- **Ohne Konto (Standard):** HDRpilot fragt bei PCGamingWiki nach den Seiten deiner Spiele; dabei gehen die
  Spielnamen an PCGamingWiki, sonst nichts. Je Start höchstens 100 Spiele (2 Anfragen plus 2 Suchen, 2 s Abstand),
  neue und kürzlich gespielte zuerst, bekannte alle 14 Tage neu. Bei einer großen Bibliothek ist nach einigen Starts
  alles geprüft. Steam- und GOG-Spiele werden über ihre Laden-ID zugeordnet, andere über den Namen.
- **Mit eigenem PCGamingWiki-Konto:** Unter Einstellungen → „HDR-Liste (PCGamingWiki)“ führen **Konto anlegen** und
  **Bot-Passwörter** zu PCGamingWiki. Dort ein Bot-Passwort mit dem Namen HDRpilot anlegen und nur das Recht „Create,
  query and delete data through the Cargo extension“ wählen. Benutzername (`Name@HDRpilot`) und Bot-Passwort in
  HDRpilot eintragen und **Speichern und testen**. Dann lädt HDRpilot die vollständige Liste und gleicht sie nur auf
  deinem PC ab; Spielnamen werden nicht gesendet. Das Passwort liegt mit Windows (DPAPI) verschlüsselt in
  `pcgw-account.dat` und ist nur für deinen Windows-Benutzer lesbar; **Konto entfernen** löscht es.

Ohne Netz bleibt der zuletzt geladene Stand erhalten. PCGamingWiki ist ein kleines Gemeinschaftsprojekt; HDRpilot hält
sich an dessen Anfragegrenzen und speichert die Ergebnisse zwischen. Die HDR-Angaben stehen unter der Lizenz
CC BY-NC-SA 3.0.

## Anti-Cheat

HDRpilot greift nicht in Spiele ein und ist so gebaut, dass Anti-Cheat-Systeme keinen Anlass zum Eingreifen haben:

- Es liest nur die Prozessliste von Windows. Den EXE-Pfad eines Programms erfragt es bei Windows, ohne das Programm
  selbst zu öffnen; ein Spiel bekommt von HDRpilot also gar nichts mit. Für den Hänger-Hinweis fragt es Rechenzeit und
  sichtbare Fenster ebenfalls nur bei Windows ab.
- Es liest und schreibt keinen Spielspeicher, schleust keinen Code ein, setzt keine Hooks, legt kein Overlay über das
  Spiel und ändert weder Priorität noch Prozessorzuteilung von Spielen. Die Protokolle von Epic und Steam liest es nur.
- Es lädt keinen Treiber und braucht keine Administratorrechte. Netzwerk nutzt es nur für die HDR-Liste.
- HDR schaltet es über die Anzeige-Einstellungen von Windows, wie der HDR-Schalter in den Windows-Einstellungen.
- Künstliche Tastendrücke gibt es nur in der Tastenkürzel-Notlösung. Sie ist ab Werk aus und sendet nie etwas,
  solange ein Spiel läuft oder eine Vollbild-Anwendung im Vordergrund ist, auch nicht beim Umschalten von Hand.
- Launcher-Spiele startet es über den Launcher-Link, sodass deren Anti-Cheat-Starter wie gewohnt läuft. Die
  Steam-Startoption startet genau den Befehl, den Steam vorgibt, und beendet sich sofort.

Ein automatischer Test stellt sicher, dass solche Techniken nicht in den Quelltext gelangen. Eine Garantie gibt es
trotzdem nicht, denn die Regeln der Anti-Cheat-Hersteller sind nicht öffentlich. Meldet ein Anti-Cheat HDRpilot (etwa
„Hack attempt detected“ bei nProtect GameGuard), HDRpilot vor diesem Spiel beenden und den Fall dem Spielhersteller
melden. Selbst eingetragene Spiele mit Anti-Cheat (etwa mit einem Ordner EasyAntiCheat oder BattlEye) über ihr
Start-Programm eintragen, zum Beispiel `start_protected_game.exe`, nicht über die eigentliche Spiel-EXE.

## Speicherorte

| Was | Wo |
|---|---|
| Programm (Setup): HDRpilot.exe, `cli\hdrpilot.exe`, README, Lizenzhinweise | `%LOCALAPPDATA%\Programs\HDRpilot\` |
| Eintrag unter „Installierte Apps“ (Setup) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HDRpilot` |
| Verknüpfung im Startmenü (Setup) | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot.lnk` |
| Protokoll von Setup und Entfernen | `%TEMP%\HDRpilot-Setup.log` |
| Einstellungen und Spieleliste | `%APPDATA%\HDRpilot\config.json` |
| Gemerkter HDR-Zustand während eines Spiels | `%LOCALAPPDATA%\HDRpilot\session-state.json` |
| Protokoll (täglich neue Datei, höchstens 5 MB je Datei, 14 Tage) | `%LOCALAPPDATA%\HDRpilot\logs\` |
| Protokoll der Steam-Startoption (eine Zeile je Start, 256 KB) | `%LOCALAPPDATA%\HDRpilot\logs\wrap.log` |
| HDR-Angaben je Spiel | `%LOCALAPPDATA%\HDRpilot\hdr-support.json` |
| Vollständige HDR-Liste (nur mit Konto) | `%LOCALAPPDATA%\HDRpilot\hdr-catalog.json` |
| PCGamingWiki-Konto (verschlüsselt, nur mit Konto) | `%LOCALAPPDATA%\HDRpilot\pcgw-account.dat` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Wert `HDRpilot` |
| Verknüpfungen | Desktop bzw. `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot\`, „<Spiel> (HDR).lnk“ |
| Entpackte Grafik-Bibliotheken (rund 8 MB je Version) | `%TEMP%\.net\HDRpilot\` |
| Dasselbe für den Setup (rund 8 MB je Version) | `%TEMP%\.net\HDRpilot-Setup-<Version>\` |
| Dasselbe für das Entfernen (rund 8 MB, löscht sich danach selbst) | `%TEMP%\.net\HDRpilot-Uninstall\` |

Wer schon eine frühere Version hatte: Windows unterscheidet bei Ordnernamen nicht nach Groß- und Kleinschreibung, die
bisherigen Ordner `HdrPilot` werden also weiter benutzt, auch als Programmordner des Setups. Setup und Entfernen
ändern nichts an Einstellungen und Daten; nur beim Entfernen mit Haken (siehe „Entfernen“) werden sie gelöscht. Einzige
Ausnahme: Das Entfernen stellt einen gemerkten HDR-Zustand wieder her und verwirft danach `session-state.json`.

Einstellungen gelten je Spiel über Quelle und ID (etwa `Steam:1091500`), nicht über den Namen. `config.json` wird
atomar geschrieben. Von Hand bearbeitest du sie am besten bei beendetem HDRpilot. Ist sie danach kein gültiges JSON,
sichert HDRpilot sie beim nächsten Start als `config.json.defekt-<Zeit>.bak` und beginnt mit Standardwerten; deine
Auswahl steckt dann nur noch in dieser Sicherung.

Die Grafik-Bibliotheken entpackt HDRpilot beim ersten Start und nach jedem Update auf eine neue Version. Ordner
älterer Versionen bleiben liegen und können bei beendetem HDRpilot gelöscht werden. Setup-Datei und die Kopie zum
Entfernen entpacken ihre eigenen (Ordner nach dem Namen der EXE). Den der Setup-Datei
(`%TEMP%\.net\HDRpilot-Setup-<Version>`) darfst du löschen, wenn gerade kein Setup läuft; den der Kopie zum Entfernen
(`%TEMP%\.net\HDRpilot-Uninstall`) löscht sie am Ende zusammen mit ihrem Temp-Ordner selbst.

## Befehlszeile

### HDRpilot.exe

Läuft HDRpilot schon, reicht ein zweiter Aufruf seine Befehle an die laufende Instanz weiter.

| Aufruf | Wirkung |
|---|---|
| `HDRpilot.exe` | Starten (nur im Infobereich); läuft es schon, öffnet sich die Spielebibliothek |
| `--minimized` | Starten (nur im Infobereich); läuft es schon, passiert nichts. So startet der Autostart |
| `--show` / `--settings` | Spielebibliothek bzw. Einstellungen öffnen |
| `--run <Quelle:ID>` | Spiel über HDRpilot starten (HDR vorher an) |
| `--toggle-hdr` | HDR jetzt umschalten |
| `--pause` / `--resume` | Automatik pausieren bzw. fortsetzen |
| `--rescan` | Spiele neu einlesen |
| `--exit` | Laufende Instanz beenden |
| `--wrap -- <befehl>` | Steam-Startoption (`--wrap -- %command%`): HDR einschalten lassen, dann den Befehl starten |
| `--config-dir <ordner>` | Eigene Ablage statt `%APPDATA%`/`%LOCALAPPDATA%` (Tests); eigene Instanz je Ordner |

Den Schlüssel `<Quelle:ID>` zeigt die Oberfläche nicht an. Du findest ihn in der ersten Spalte von
`cli\hdrpilot.exe games list` oder im Ziel einer „(HDR)“-Verknüpfung hinter `--run`. Meist ist es einfacher, die
Verknüpfung in den Details anlegen zu lassen.

### Setup-Datei und Entfernen

Für Skripte und die Fehlersuche; im Alltag reichen das Setup-Fenster und „Installierte Apps“.

| Aufruf | Wirkung |
|---|---|
| `HDRpilot-Setup-1.2.0.exe --quiet` | Installieren oder aktualisieren ohne Fenster; das Ergebnis ist der Exit-Code. Danach startet HDRpilot (lief es nicht, mit der Spielebibliothek), außer mit `--no-launch` oder mit Administratorrechten. Skripte, die auf das Ende warten oder die Ausgabe lesen, nehmen `--no-launch` |
| `--autostart on\|off` | Mit Windows starten, danach mit der installierten EXE (ohne Angabe: wie bisher, also an, wenn schon eine HDRpilot.exe mit Windows startet, auch aus einem anderen Ordner; bei einer neuen Installation an) |
| `--desktop-shortcut on\|off` | Desktop-Verknüpfung (ohne Angabe: wie bisher, bei einer neuen Installation aus) |
| `--no-launch` | HDRpilot danach nicht starten. Für Skripte, die auf das Ende warten (etwa `Start-Process -Wait`, das auch auf gestartete Programme wartet) oder die Ausgabe lesen |
| `--allow-downgrade` | Eine ältere Version über eine neuere installieren |
| `HDRpilot.exe --uninstall` | Entfernen wie aus „Installierte Apps“ (läuft aus einer Kopie im Temp-Ordner) |
| `--quiet` | Entfernen ohne Fenster: kehrt sofort zurück. 0 heißt, die Kopie im Temp-Ordner entfernt jetzt; ihr Ergebnis steht danach als `ERGEBNIS: <Exit-Code>` im Protokoll. 1 (Kopie nicht gestartet), 2 (Aufruf falsch) und 7 (Container) kommen direkt vom ersten Aufruf, ohne `ERGEBNIS`-Zeile; dann wurde nichts entfernt |
| `--remove-data` | Beim Entfernen auch Einstellungen, Spieleliste, Protokolle und HDR-Liste löschen |
| `--test-root <ordner>` | Prüfläufe: Programmordner, Verknüpfungen und Ablage unter diesem Ordner, dazu ein eigener Registry-Zweig unter `HKCU\Software\HDRpilot.Tests` |

Exit-Codes: 0 ok, 1 Fehler, 2 Aufruf falsch, 3 ältere Version abgelehnt, 4 HDRpilot ließ sich nicht beenden,
5 abgebrochen, 6 nicht installiert, 7 aus dem Container einer anderen App gestartet, nichts geändert (siehe
„Fehlersuche“). Beide schreiben ihr Protokoll nach `%TEMP%\HDRpilot-Setup.log`.

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

Die `games`-Befehle kannst du neben der laufenden App benutzen; HDRpilot liest die Änderungen selbst neu ein.
`hdrpilot watch` und `hdrpilot run` betreiben dagegen dieselbe Automatik wie die App und nutzen ohne `--config-dir`
dieselbe Ablage. Sie prüfen nicht, ob die App läuft: Dann schalten beide unabhängig und überschreiben sich den
gemerkten HDR-Zustand. Vorher HDRpilot beenden oder bei laufender App `HDRpilot.exe --run <Quelle:ID>` verwenden.

## Bekannte Grenzen

- **Erkennung per Abfrage:** Zwischen Spielstart und HDR liegt bis zu ein Abfrageintervall (Standard 1 s) plus die
  Schaltzeit, außer bei Epic und Steam mit Frühwarnung. Spiele, die HDR nur beim Start prüfen, über „Mit HDR starten“,
  eine „(HDR)“-Verknüpfung oder (bei Steam) die Steam-Startoption starten.
- **Spiel-EXE außerhalb des Installationsordners:** Manche Starter starten die eigentliche EXE woanders. Solche Spiele
  erkennt HDRpilot erst, wenn du die EXE in den Details hinzufügst; bis dahin stellt auch „Mit HDR starten“ HDR nach
  der Startfrist wieder zurück.
- **Mehrere Bildschirme:** HDRpilot erkennt nicht, auf welchem Bildschirm ein Spiel läuft, sondern schaltet die
  eingestellten (Standard: Hauptbildschirm). Läuft ein Spiel woanders, in den Details den richtigen **Bildschirm**
  wählen. Auf echter Hardware geprüft wurde nur mit einem Bildschirm.
- **Nicht automatisch gefunden** werden: in Steam eingetragene Fremdspiele, ältere Store-Spiele direkt unter
  `C:\Program Files\WindowsApps`, Xbox-Spiele auf USB- oder Netzlaufwerken, GOG-Spiele ohne Registry-Eintrag und
  Epic-Spiele ohne Manifest. Solche Spiele über „Spiel hinzufügen“ eintragen.
- **Start über die Launcher:** Mit echten Spielen geprüft: Steam (Dying Light: The Beast), Epic (Dead Island 2), GOG
  (Cyberpunk 2077), Ubisoft Connect (Avatar: Frontiers of Pandora), EA app (Battlefield 6, mit EA Javelin) und Xbox
  (Palworld), jeweils über „Mit HDR starten“ und über die Verknüpfung. HDR war jedes Mal an, bevor der Launcher das
  Spiel gestartet hat. Battle.net ist ungeprüft (siehe unten).
- **Umschalten während des Spielstarts:** Bei der automatischen Erkennung schaltet HDRpilot HDR, während das Spiel
  gerade hochfährt. Das verkraften fast alle Spiele; Dead Island 2 blieb im Test bei einem von zwei automatischen
  Starts hängen (ob wegen des Umschaltens, ist nicht sicher). Der Neustart-Puffer hilft beim zweiten Versuch, wenn der
  Neustart in seine Zeit fällt (Standard 1 Minute; wer länger für den Neustart braucht, stellt ihn unter „Einstellungen“
  höher); sicherer sind „Mit HDR starten“, die Verknüpfung oder die Steam-Startoption.
- **Frühwarnung:** nur für den Epic Games Launcher und Steam. Sie beruht auf deren Protokollformat, das sich mit einem
  Launcher-Update ändern kann; dann greift wieder die normale Erkennung.
- **Steam-Startoption:** wirkt nur, wenn HDRpilot läuft (Autostart empfohlen) und das Spiel beim letzten Einlesen schon
  bekannt war. Läuft Steam mit Administratorrechten, HDRpilot aber nicht, kann sich der Start um bis zu 8 s verzögern.
- **HDR-Liste:** Ohne Konto werden Spiele ohne Steam- oder GOG-ID nur gefunden, wenn ihr Name zur PCGamingWiki-Seite
  passt. Der Konto-Weg ist nur gegen nachgebildete Antworten getestet, weil beim Bau kein Konto vorhanden war. Die
  Angaben stammen von Freiwilligen und können lückenhaft sein.
- **Hänger-Hinweis:** Ältere Store-Spiele (UWP), deren Fenster einem Windows-Rahmenprozess gehört, könnten im
  minimierten Zustand fälschlich als hängend gemeldet werden. Es bleibt bei einem Hinweis.
- **GOG:** „Mit HDR starten“ startet die Spiel-EXE direkt, an GOG Galaxy vorbei. Cloud-Speicherstände, Overlay und
  Spielzeit von Galaxy sind dann womöglich nicht aktiv.
- **EA app:** Die Bibliotheksdatei der EA app ist verschlüsselt und an die Hardware gebunden; HDRpilot entschlüsselt
  sie bewusst nicht und findet EA-Spiele über die Installationsdaten der Spiele selbst.
- **Battle.net:** Am Testrechner war kein Blizzard-Spiel installiert. Das Einlesen von Blizzard-Spielen ist deshalb nur
  mit nachgebildeten Daten getestet, das Starten über Battle.net gar nicht. Fehlt ein Blizzard-Spiel, seinen Ordner
  über „Spiel hinzufügen“ eintragen.
- **Windows 10:** Die ältere Schnittstelle ist mit Tests abgedeckt und wurde auf echter Hardware unter Windows 11
  (Build 26200) mitgeprüft; ein echtes Windows 10 stand nicht zur Verfügung.
- **Tastenkürzel-Notlösung:** HDRpilot sendet Win+Alt+B nur, wenn sie eingeschaltet ist, die Windows-Schnittstelle
  gescheitert ist und gerade kein Spiel läuft (siehe „Anti-Cheat“). Das Kürzel braucht die Xbox Game Bar, schaltet nur
  um und wirkt auf alle HDR-fähigen Bildschirme; HDRpilot bestätigt aber nur den Zielbildschirm. Auf echter Hardware
  ist die Notlösung nicht getestet.
- **Nicht signiert:** SmartScreen und manche Virenscanner warnen bei unbekannten, unsignierten EXEs, auch beim Setup.
- **Nur auf Deutsch:** Oberfläche, Hinweise, Fehlermeldungen der Launcher-Scanner, Kommandozeile und Protokoll.

## Fehlersuche

- **HDR schaltet gar nicht:** Lässt sich HDR in den Windows-Anzeigeeinstellungen von Hand einschalten? Zeigt der
  Tooltip „kein HDR-fähiger Bildschirm“, meldet Windows für keinen Zielbildschirm HDR. Mit
  `cli\hdrpilot.exe hdr status` siehst du, was Windows meldet; `hdr on` und `hdr off` schalten zum Test von Hand.
  Hinweise während eines Vollbild-Spiels stehen nur im Protokoll.
- **Spiel wird nicht erkannt:** Ist „HDR automatisch“ an und die Automatik nicht pausiert? Liegt die laufende EXE im
  Installationsordner? Wenn nicht, die EXE in den Details hinzufügen. Ist sie dort als „ausgeschlossen“ markiert, mit
  „Wieder aufnehmen“ zurückholen. Steht sie unter „Hilfsprozesse ausschließen“, das Muster dort entfernen.
- **HDR kommt für ein Spiel zu spät oder das Spiel hängt beim automatischen Start:** über „Mit HDR starten“, eine
  „(HDR)“-Verknüpfung oder (bei Steam) die Steam-Startoption starten; dann ist HDR schon vor dem Spiel an.
- **Steam-Startoption wirkt nicht:** Läuft HDRpilot? In `logs\wrap.log` steht je Start, ob HDR vorbereitet wurde und
  warum nicht. Kennt HDRpilot das Spiel noch nicht, einmal „Neu einlesen“.
- **HDR bleibt nach dem Spiel an:** Endete das Spiel weniger als 5 Minuten nach dem Start, hält der Neustart-Puffer HDR
  noch an (Standard 1 Minute, der Tooltip zeigt die Restzeit). Ist dir das zu lang, stelle unter „Einstellungen“ beim
  Neustart-Puffer eine kürzere Zeit oder 0:00 (aus) ein. Sonst die Nachlaufzeit abwarten (Standard 10 s). Nennt der
  Tooltip das Spiel danach noch, läuft noch ein Programm aus seinem Ordner (etwa ein Launcher-Rest): im Task-Manager
  nachsehen und diese EXE in den Details entfernen. Manche Spiele beenden sich nicht vollständig und laufen unsichtbar
  weiter (im Test bei Palworld); HDRpilot weist nach 2 Minuten darauf hin. Dann den Spielprozess im Task-Manager
  beenden. Weitere Gründe: Ein anderes HDR-Spiel läuft noch; HDR war schon vor dem Spiel an; „Nach dem Spiel
  zurückstellen“ ist für dieses oder ein gleichzeitig gelaufenes Spiel aus. Nach „Mit HDR starten“ ohne Spielstart
  bleibt HDR bis zum Ende der Startfrist an.
- **„Mit HDR starten“ ist gesperrt:** Das Spiel ist nicht installiert, oder bei einem selbst eingetragenen Ordner kennt
  HDRpilot keine Start-EXE. Dann das Spiel über seine EXE hinzufügen.
- **Keine HDR-Kennzeichen in der Liste:** Der erste Abgleich läuft 30 s nach dem Start; ohne Konto sind je Start nur
  100 Spiele dran. Den Stand und den letzten Fehler zeigt der Bereich „HDR-Liste (PCGamingWiki)“ in den Einstellungen.
- **HDRpilot war abgestürzt oder beendet:** einfach neu starten, der gemerkte Zustand wird dann wiederhergestellt.
- **Setup oder Entfernen meldet einen Fehler:** Einzelheiten stehen in `%TEMP%\HDRpilot-Setup.log`; nach einem Fehler
  im Setup ist die bisherige Version unverändert. Lässt sich HDRpilot nicht beenden, im Infobereich „HDRpilot beenden“
  wählen und es erneut versuchen.
- **HDRpilot startet nicht von selbst, oder Einstellungen sind weg nach einem Start aus einer anderen App:** Manche
  Apps aus dem Microsoft Store oder als MSIX-Paket (etwa die Claude-App) lassen alles, was aus ihnen heraus startet, in
  ihrem eigenen Container laufen. Windows leitet dann Registry-Einträge wie den Autostart in den privaten Speicher dieser
  App um (`%LOCALAPPDATA%\Packages\<App>\LocalCache`), Einstellungen und Protokolle dann, wenn ihr Ordner außerhalb
  noch fehlte oder schon dort liegt. Außerhalb fehlt, was umgeleitet wurde, und der Autostart wirkt nicht. HDRpilot
  erkennt das, egal wo seine Ordner liegen: Beim Start steht eine Warnung „HDRpilot läuft im Container der App …“ im
  Protokoll (je nach Ordner in der umgeleiteten Kopie unter `…\LocalCache\Local\HDRpilot\logs`), Setup und Entfernen
  ändern nichts und sagen es (still: Exit-Code 7). Abhilfe: HDRpilot beenden und über das Startmenü oder die
  Verknüpfung starten, Setup-Dateien per Doppelklick im Explorer ausführen, Entfernen über „Installierte Apps“.
- **Ein Spiel fehlt in der Liste:** „Neu einlesen“ drücken; fehlt es weiter, über „Spiel hinzufügen“ eintragen oder
  seinen übergeordneten Ordner als Suchordner angeben. Fehler einzelner Launcher stehen in der Bibliothek und im
  Protokoll.
- **Protokoll:** Infobereich → „Protokollordner öffnen“. Für mehr Einzelheiten unter Einstellungen → Erweitert die
  Protokollstufe auf „Ausführlich“ stellen. Jede Schaltung steht dort mit Auslöser, Bildschirm, Zustand vorher und
  nachher.
- **Alles zurücksetzen:** HDRpilot beenden und `%APPDATA%\HDRpilot\config.json` löschen. Die Spiele werden beim
  nächsten Start neu eingelesen; deine Auswahl geht dabei verloren.

## Entfernen

Mit dem Setup installiert:

1. In Steam die Startoption `--wrap -- %command%` bei den Spielen löschen, bei denen du sie eingetragen hast
   (Rechtsklick auf das Spiel → Eigenschaften → Allgemein → Startoptionen), sonst starten diese Spiele nicht mehr.
   Das Entfernen nennt dir die Spiele, bei denen sie noch steht.
2. Unter *Einstellungen → Apps → Installierte Apps* bei HDRpilot *Deinstallieren* wählen. HDRpilot fragt nach: Ohne
   Haken bei **Auch Einstellungen, Spieleliste und Protokolle löschen** (Standard) bleiben `%APPDATA%\HDRpilot` und
   `%LOCALAPPDATA%\HDRpilot` erhalten und werden bei einer neuen Installation wieder benutzt; mit Haken sind sie weg,
   samt HDR-Liste und gespeichertem PCGamingWiki-Konto.
3. **Entfernen** beendet HDRpilot, stellt einen gemerkten HDR-Zustand wieder her und entfernt das Programm, seine
   Verknüpfungen und den Autostart. Das erledigt eine Kopie im Temp-Ordner, die danach ihren Ordner und ihre entpackten
   Grafik-Bibliotheken (`%TEMP%\.net\HDRpilot-Uninstall`) selbst löscht.
4. Bleiben „(HDR)“-Verknüpfungen für einzelne Spiele übrig, kannst du sie löschen, ebenso die entpackten
   Grafik-Bibliotheken in `%TEMP%\.net\HDRpilot` und `%TEMP%\.net\HDRpilot-Setup-<Version>` (rund 8 MB je Ordner).

Ohne Installation:

1. In den Einstellungen **Mit Windows starten** ausschalten und gegebenenfalls **Konto entfernen**.
2. In Steam die Startoption `--wrap -- %command%` bei den Spielen wieder löschen, bei denen du sie eingetragen hast.
3. Warten, bis kein Spiel mehr läuft und HDR zurückgestellt ist, dann im Infobereich **HDRpilot beenden**.
4. `HDRpilot.exe` und den Ordner `cli` löschen, dazu die Ordner `%APPDATA%\HDRpilot`, `%LOCALAPPDATA%\HDRpilot`,
   `%TEMP%\.net\HDRpilot`, den Startmenü-Ordner `%APPDATA%\Microsoft\Windows\Start Menu\Programs\HDRpilot` und die
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

`tools\publish.ps1` legt die fertige Auslieferung unter `artifacts\publish\HDRpilot\` ab und daneben den Setup
`artifacts\publish\HDRpilot-Setup-1.2.0.exe` (dieselbe EXE mit angehängtem Paket aus README, Lizenzhinweisen und
Kommandozeile, siehe E83 in `docs\entscheidungen.md`). Projekte und Namensräume im Quelltext heißen weiter
`HdrPilot.*`. Begründungen für Designentscheidungen stehen in `docs\entscheidungen.md`, die Prüfnachweise in
`GATES.md`.

## Lizenzen von Fremdbestandteilen

HDRpilot enthält die .NET-Laufzeit samt WPF und Windows Forms sowie die Pakete
`Microsoft.Extensions.Logging.Abstractions` und `Microsoft.Extensions.DependencyInjection.Abstractions`, alle unter
der MIT-Lizenz. Die HDR-Angaben stammen von PCGamingWiki (pcgamingwiki.com) unter der Lizenz CC BY-NC-SA 3.0; sie
werden zur Laufzeit abgerufen, nicht mitgeliefert. Die Lizenztexte und die Hinweise zu Fremdcode innerhalb der
.NET-Laufzeit liegen der Auslieferung als `THIRD-PARTY-NOTICES.txt` bei. Keine Bestandteile stehen unter GPL oder AGPL.
