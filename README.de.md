# HDRpilot
Eine elegante, zu 100 % per Vibe-Coding entstandene Windows-App, die HDR für ausgewählte Spiele automatisch einschaltet. Mit optionalem Auto HDR für SDR-Spiele, Einbindung von Verknüpfungen und der Steam-Startoption, einer intuitiven Oberfläche und Unterstützung für Steam, Epic Games, Xbox/Game Pass, GOG, Ubisoft Connect, EA app, Battle.net und Quiver Launcher.

English: [README.md](README.md) · Vollständiges Handbuch: [docs/handbuch.md](docs/handbuch.md)

![Spielebibliothek von HDRpilot in der Ansicht Raster mit einer Demo-Bibliothek][shot-grid]

## Was es macht

HDRpilot schaltet Windows-HDR automatisch ein, wenn du ein ausgewähltes Spiel startest, und stellt danach den
vorherigen Zustand wieder her. Es findet deine installierten Spiele selbst und zeigt dir laut PCGamingWiki, welche
davon HDR unterstützen. Spiele, die HDR nur beim Start prüfen, startest du über HDRpilot; dann ist HDR schon an, wenn
sie starten. Im Alltag läuft HDRpilot unauffällig als Symbol im Infobereich.

## Funktionen

- **HDR automatisch** je Spiel: HDR geht beim Start des Spiels an und nach dem Spielende wieder auf den Stand davor.
- Mit HDR starten, bevor das Spiel läuft: **Starten** in der Bibliothek, „(HDR)“-Verknüpfungen auf dem Desktop oder im
  Startmenü oder die Steam-Startoption.
- Spielebibliothek mit den Ansichten Liste, Raster und Regal für Steam, Epic, Xbox/Game Pass, GOG, Ubisoft Connect, EA
  app, Battle.net und Quiver Launcher, dazu selbst hinzugefügte Spiele. Titelbilder, Logos und Cover kommen aus den
  Bild-Zwischenspeichern der Launcher auf deinem PC; fehlende erzeugt HDRpilot aus Symbol und Name des Spiels.
- HDR-Liste von PCGamingWiki (nativ, teilweise oder per Mod); der Filter **HDR-fähig** zeigt, welche Spiele HDR
  unterstützen.
- **Windows Auto HDR** je Spiel (wählbar) für SDR-Spiele mit DirectX 11 oder 12, die über HDRpilot starten.
- Bildsuche bei SteamGridDB mit eigenem API-Schlüssel (wählbar; **Bei SteamGridDB suchen** in **Bild wählen…**).
- **Farben folgen dem Spiel** (wählbar, Standard aus): Die Akzente der Bibliothek übernehmen die Farben von Logo und
  Cover des gewählten Spiels.
- Oberfläche auf Deutsch und Englisch; eine neue Installation startet bei deutschem Windows auf Deutsch, sonst auf
  Englisch (umstellbar in den Einstellungen).
- Keine Administratorrechte, kein Windows-Dienst, kein Treiber. Im Hintergrund sieht HDRpilot in die Prozessliste,
  standardmäßig einmal pro Sekunde.

## Bildschirmfotos

| Liste | Regal |
|---|---|
| ![Ansicht Liste mit den Details des gewählten Spiels][shot-list] | ![Ansicht Regal im Moment, in dem HDR angeht, mit großem Titelbild, Automatik-Karte und einer Reihe Cover][shot-shelf] |

| Einstellungen der Darstellung | Farben folgen dem Spiel |
|---|---|
| ![Einstellungen der Darstellung mit Farbschema, Vorlagen, Live-Vorschau und Thema][shot-settings] | ![Akzente der Bibliothek in den Farben des gewählten Spiels][shot-colors] |

Aus Urheberrechtsgründen zeigen die Bildschirmfotos statt echter Spielbilder eine Demo-Bibliothek mit erfundenen Spielen und erzeugten Bildern (in der englischen Oberfläche).

## Herunterladen und installieren

`HDRpilot-Setup-<Version>.exe` unter [GitHub Releases](https://github.com/DanteGE/HDRpilot/releases) herunterladen und
starten. Der Setup installiert HDRpilot nur für deinen Windows-Benutzer nach `%LOCALAPPDATA%\Programs\HDRpilot`, ohne
Administratorrechte. Er legt eine Verknüpfung im Startmenü an und trägt HDRpilot unter *Einstellungen → Apps →
Installierte Apps* ein.

Die Datei ist nicht signiert, deshalb fragt Windows SmartScreen beim ersten Start nach: „Weitere Informationen“ →
„Trotzdem ausführen“.

Zum Aktualisieren den Setup der neuen Version starten. Er ersetzt das Programm an Ort und Stelle und startet HDRpilot
wieder, wenn es vorher lief. Einstellungen, Spieleliste und Protokolle bleiben, weil sie in `%APPDATA%\HDRpilot` und
`%LOCALAPPDATA%\HDRpilot` liegen. Entfernen über *Einstellungen → Apps → Installierte Apps*; vorher die
Steam-Startoption bei den Spielen löschen, bei denen du sie eingetragen hast (siehe
[Entfernen](docs/handbuch.md#entfernen)).

## Schnellstart

1. Den Setup starten und **Installieren** klicken. Mit **HDRpilot danach starten** (vorbelegt) öffnet sich danach die
   Spielebibliothek.
2. HDRpilot findet die Spiele deiner Launcher selbst. Ein fehlendes Spiel fügst du mit **Spiel hinzufügen** hinzu.
3. Bei jedem Spiel, das HDR bekommen soll, **HDR automatisch** einschalten. Gefundene Spiele sind anfangs aus; der
   Filter **HDR-fähig** hilft bei der Auswahl.
4. Wie gewohnt spielen: HDRpilot schaltet HDR beim Start des Spiels ein und stellt danach den vorherigen Zustand wieder
   her. Prüft ein Spiel HDR nur beim Start, startest du es stattdessen mit **Starten** oder einer „(HDR)“-Verknüpfung.
5. Das Fenster zu schließen beendet HDRpilot nicht; es läuft im Infobereich weiter (unten rechts neben der Uhr). Ein
   Doppelklick auf das Symbol öffnet die Bibliothek; beenden mit Rechtsklick auf das Symbol → **HDRpilot beenden**.

## Voraussetzungen

- Windows 11 oder Windows 10 ab 21H2, 64 Bit.
- Ein HDR-fähiger Bildschirm, bei dem sich unter *Einstellungen → System → Anzeige* „HDR verwenden“ einschalten lässt.
- Keine .NET-Installation nötig, die Laufzeit steckt in der EXE.

## Datenschutz und Netz

- Keine Telemetrie und keine Eingriffe in Spielprozesse. HDRpilot liest die Prozessliste von Windows und die Daten der
  Launcher auf deinem PC, ohne sie zu ändern.
- PCGamingWiki (Standard an): 30 s nach jedem Start (höchstens einmal pro Stunde) und bei **Jetzt abgleichen** fragt
  HDRpilot bei PCGamingWiki nach, welche deiner Spiele HDR unterstützen. Ohne Konto gehen dabei die Namen deiner Spiele
  an PCGamingWiki, sonst nichts; mit eigenem PCGamingWiki-Konto lädt HDRpilot die vollständige Liste und gleicht sie
  auf deinem PC ab. **Bei jedem Start abgleichen** ausschalten beendet den automatischen Abgleich.
- SteamGridDB (nur mit eigenem API-Schlüssel): HDRpilot sucht nur, wenn du **Bei SteamGridDB suchen** klickst, und
  ergänzt fehlende Bilder im Hintergrund nur, wenn du **Fehlende Bilder automatisch laden** einschaltest. Dabei geht die
  Steam-App-ID oder der Name des Spiels an SteamGridDB. Der Schlüssel liegt verschlüsselt für dein Windows-Konto.
- Sonst geht HDRpilot nicht ins Netz. Ohne SteamGridDB kommen Spielbilder nur aus Dateien auf deinem PC.

## Dokumentation

Das [Handbuch](docs/handbuch.md) beschreibt alles ausführlich: [Einrichtung](docs/handbuch.md#einrichtung),
[Bedienung](docs/handbuch.md#bedienung), [So funktioniert es](docs/handbuch.md#so-funktioniert-es),
[Windows Auto HDR](docs/handbuch.md#windows-auto-hdr), [Befehlszeile](docs/handbuch.md#befehlszeile),
[Speicherorte](docs/handbuch.md#speicherorte), [Bekannte Grenzen](docs/handbuch.md#bekannte-grenzen),
[Fehlersuche](docs/handbuch.md#fehlersuche) und [Aus dem Quelltext bauen](docs/handbuch.md#aus-dem-quelltext-bauen).

Der Setup legt diese Datei auch in den Programmordner; dort gehen Bilder und relative Verweise nicht. Das Handbuch steht
im Netz unter <https://github.com/DanteGE/HDRpilot/blob/main/docs/handbuch.md>.

English: [README.md](README.md) · [Manual](docs/manual.md)

## Lizenz

HDRpilot steht unter der PolyForm Strict License 1.0.0, Copyright Dante_GE. Kurz gefasst: Erlaubt ist die Nutzung für
nicht-kommerzielle Zwecke; nicht erlaubt sind Änderungen, darauf aufbauende Werke, jede Weitergabe und die kommerzielle
Nutzung. Maßgeblich ist allein der Lizenztext in [`LICENSE`](LICENSE).

Fremdbestandteile behalten ihre eigenen Lizenzen: Die .NET-Laufzeit (samt WPF und Windows Forms) und die zwei Pakete
Microsoft.Extensions stehen unter der MIT-Lizenz, die HDR-Angaben von PCGamingWiki unter CC BY-NC-SA 3.0 (zur Laufzeit
abgerufen, nicht mitgeliefert; die Testdaten aus PCGamingWiki in diesem Repository stehen unter derselben Lizenz). Die
Lizenztexte liegen der Auslieferung als `THIRD-PARTY-NOTICES.txt` bei; siehe auch
[Lizenzen von Fremdbestandteilen](docs/handbuch.md#lizenzen-von-fremdbestandteilen).

<!-- Bildschirmfotos: alle Bilddateien an einer Stelle (erzeugt mit tools/readme-screenshots.ps1 aus einer Demo-Bibliothek) -->
[shot-grid]: docs/images/library-grid.png
[shot-list]: docs/images/library-list.png
[shot-shelf]: docs/images/library-shelf.png
[shot-settings]: docs/images/settings-appearance.png
[shot-colors]: docs/images/game-colors.png
