# Übergabe an die Cloud-Sitzung (Stand 2026-10-26)

Dieser Zweig (`plan/naechstes-update`) enthält nur Arbeitsunterlagen im Ordner `plan/`. Er wird nie in `main`
zusammengeführt.

## Ausgangslage

- `main` auf GitHub ist der bereinigte, veröffentlichte Stand von Version 1.5.0. Die Commit-Kennungen unterscheiden
  sich von der privaten Arbeitskopie des Nutzers. Ältere Kennungen in Unterlagen, die nicht aus diesem Repo stammen,
  gelten hier nicht. Die beiden, die in diesem Ordner vorkommen:
  - `88a0ea5` in `items-en.txt` ist hier `3c277b3`, der gemeinsame erste Commit der drei Arbeitszweige.
  - `a4e996f` in `sgdb-spec-final.md` (Bezug der Zeilennummern) ist hier `3284edf`.
- Die Arbeitszweige `ui/hero`, `ui/scroll` und `ui/details` wurden aus der privaten Arbeitskopie auf diesen Stand
  übertragen. Alle drei beginnen mit demselben Commit „WIP: Tiefenwirkung im Titelbereich gleitet zurück …“ (Punkt 1).
- Ab jetzt entsteht Code hier, nicht mehr in der privaten Arbeitskopie.

## Aufteilung (Entscheidung des Nutzers vom 2026-10-22)

> „Bitte nur lokal umsetzen, was lokal gemacht werden muss, ansonsten jetzt den Fokus darauf, Änderungen in der
> Cloudsitzung vorzunehmen.“

- **Cloud-Sitzung (Linux):** Code, Unit-Tests, die ohne Windows-Desktop laufen, Doku, Zusammenführen der Zweige.
  Bauen geht mit `dotnet build -p:EnableWindowsTargeting=true`. Was sich nicht bauen oder testen lässt, steht im
  Bericht ausdrücklich als „lokal offen“.
- **Lokal (Windows, die ursprüngliche Sitzung):**
  - WPF-Fenstertests (`HdrPilot.App.Tests`) und alle Oberflächen-Gates (`phase4-ui`, `phase6-ui`, `phase8-icons`,
    `phase9-autohdr-ui`, `phase10-ui`, `phase10-settings-ui`, `phase11-uninstalled-ui`, `phase12-quiver-ui`)
  - Bildschirmfotos im Snapshot-Modus
  - Setup bauen, Test-Update installieren (Sicherung und Abgleich der Einstellungen)
  - echte HDR-Gates und Abnahmetests mit dem Nutzer

## Regeln (gelten unverändert)

- **Öffentliches Repo:** keine lokalen Pfade, keine Namen außer „Dante_GE“, keine echten Spielordner an
  Standardorten, keine 1:1 kopierten Werte vom Rechner des Nutzers (Zeitstempel, Größen, Build-Nummern …), nur
  erfundene. Vor jedem Push `tools/gates/publish-check.ps1` und `publish-check.ps1 -Rev HEAD` laufen lassen (unter
  Linux mit `pwsh`, sonst dieselben Prüfungen von Hand).
- **Commits:** Autor und Committer „Dante_GE <115689511+DanteGE@users.noreply.github.com>“. Die Nachricht endet mit
  einer Leerzeile und „Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>“. Nie force-push auf `main`.
- **Lizenz:** PolyForm Strict 1.0.0. Keine GPL/AGPL-Abhängigkeiten.
- **Netz der App:** nur PCGamingWiki und (optional) SteamGridDB. Keine Adminrechte, kein Dienst.
- **Sprache:** Oberfläche und Doku auf Deutsch, Texte nur in resx.
- **Stil:** kräftige, hochwertige Oberfläche, fast keine Hintergrundlast; Tastatur und UI Automation;
  Effektstufen Aus/Reduziert/Voll; hell, dunkel, hoher Kontrast.
- Tests nutzen nie das echte Netz (Fake-Handler) und nie echte Spielgrafiken (nur erzeugte Bilder).
- **Gründlichkeit vor Tempo:** Prüfungen aus mehreren Blickwinkeln, Mutationsproben, vollständige Gates.
- Bekannte Wackeltests (Zeitgrenzen): `EffectReviewTests`, `HdrListServiceTests.Empty_password…`,
  `UninstalledCleanupTests.Uninstalled_install_check_without_an_answer…`. Nur die betroffene Klasse einmal neu
  laufen lassen und im Bericht nennen.

## Aufgaben in dieser Reihenfolge

### 1. Test-Update aus den Rückmeldungen vom 2026-10-22 (10 Punkte)

Die Punkte stehen mit allen Annahmekriterien in `items-en.txt`. Stand der Zweige:

| Zweig | Punkte | Stand |
|---|---|---|
| `ui/scroll` | 3 (Pille wird Bildlaufleiste) | fertig; lokal gebaut und gefiltert getestet |
| `ui/details` | 2, 4, 7 (Auto-HDR-Texte, Filterknöpfe, Titel nach Quelle) | fertig; lokal gebaut und gefiltert getestet |
| `ui/hero` | 1, 5, 6, 8 (Rückweg der Tiefenwirkung, Griff in Raster/Regal, Kachelschein, Lasche mittig) | **unterbrochen**: der letzte Commit ist ein ungeprüfter, nicht gebauter Zwischenstand |
| — | 9 (Strg+F schaltet die Leiste um, Strg+Umschalt+F entfällt) | offen |
| — | 10 (Menüknöpfe schließen ihr offenes Menü beim zweiten Klick) | offen |

1. `ui/hero` fertigstellen. Den Zwischenstand zuerst kritisch prüfen, bevor darauf aufgebaut wird.
2. Zweig `ui/feedback` von `main`: `ui/hero`, `ui/scroll` und `ui/details` zusammenführen, dann Punkte 9 und 10
   umsetzen.
3. Unabhängige Prüfung (Code gegen den Wortlaut des Nutzers in `items-en.txt`), Funde beheben.
4. Lokal offen lassen: Fenstertests, Oberflächen-Gates, Bildschirmfotos, Setup (Version 1.5.1, falls das Setup
   dieselbe Version nicht erneut installiert), Installation.

### 2. Nächstes Test-Update: zwei Stränge parallel

- **SteamGridDB-Suche im „Bild wählen“-Dialog**, Bauplan `sgdb-spec-final.md`. Zweig `feature/sgdb-suche`,
  Entscheidung E93, Gates Phase 14.
- **„Farben folgen dem Spiel“**, Bauplan `spielfarben-spec-final.md`. Zweig `feature/spielfarben`,
  Entscheidung E94, Gates Phase 15.
- Beide Baupläne wurden nur lesend erstellt, auf Stand 1.5.0, und kritisch geprüft. Zeilennummern können sich
  durch das Test-Update verschoben haben.
- Beide Baupläne halten fest, welche Dateien der jeweils andere Strang besitzt.
- Am Ende alles auf `ui/feedback` aufbauen bzw. danach zusammenführen.

## Danach (lokal, mit dem Nutzer)

- Abnahme der Bildschirmfotos und Installation der Test-Updates
- Wackeltests und die Zeitgrenze in `UninstalledCleanupTests`
- offene Abnahmen G10.9, G10.11, G11.5, G12.7, G9.8, echtes Tray-Gate
- GitHub-Release erst nach der Abnahme
