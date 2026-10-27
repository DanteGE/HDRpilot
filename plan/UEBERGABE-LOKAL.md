# Übergabe der Cloud-Sitzung an die lokale Sitzung (Stand 2026-10-27)

Dieser Zweig (`plan/naechstes-update`) enthält nur Arbeitsunterlagen. Er wird nie nach `main` zusammengeführt. Diese
Datei ersetzt für alles ab 2026-10-26 die frühere `UEBERGABE.md`; deren Regeln gelten weiter.

Die Cloud-Sitzung hat alles gepusht, nichts liegt nur dort. Fällt sie weg, verliert ihr nichts außer der Beobachtung von
PR #1 (siehe unten „Was die Cloud-Sitzung noch tat“).

## Kurzfassung

- Arbeitsstand: Zweig **`integration/naechstes-update`**, Version **1.6.0**, Entwurfs-PR **#1** nach `main`.
- Inhalt: die zehn Punkte der Rückmeldung zu 1.5.0, SteamGridDB-Suche (E93, Phase 14), Farben folgen dem Spiel (E94,
  Phase 15), englische Oberfläche mit Sprachwahl (E95, Phase 16), README englisch und `README.de.md` (beide werden
  installiert), Release-Notizen `docs/release/1.6.0.md`.
- In der Cloud lief nur, was ohne Windows geht: Build und `HdrPilot.Tests` unter Linux. Kein WPF-Test, kein Gate,
  kein Setup, keine Bildschirmfotos, kein publish-check. Alles davon läuft lokal zum ersten Mal.
- `main` ist unverändert (`b7b46d7`). Nach `main` kommt erst etwas nach der lokalen Prüfung und der Abnahme des Nutzers.

## Zweige (alle gepusht)

| Zweig | Kopf | Inhalt |
|---|---|---|
| `ui/scroll` | `a9dd93d` | Punkt 3 (unverändert übernommen) |
| `ui/details` | `42eb104` | Punkte 2, 4, 7 (unverändert übernommen) |
| `ui/hero` | `e19dab5` | Punkte 1, 5, 6, 8: Zwischenstand geprüft und fertiggestellt |
| `ui/feedback` | `68ea1d3` | die drei UI-Zweige zusammengeführt, Punkte 9 und 10, Funde der Prüfung |
| `feature/sgdb-search` | `a5536e4` | E93, Phase 14, auf `ui/feedback` |
| `feature/spielfarben` | `763a79a` | E94, Phase 15, auf `ui/feedback` |
| `feature/english` | `22e9f9c` | E95, Phase 16, auf `ui/feedback` |
| `integration/naechstes-update` | `7523bca` | alles zusammen, englische Texte der beiden Feature-Stränge nachgezogen, README englisch + `README.de.md`, Version 1.6.0 |

Nur `integration/naechstes-update` muss lokal weiterbearbeitet werden. Die übrigen Zweige sind Geschichte; Korrekturen
gehören auf den Integrationszweig.

## Was gemacht wurde (Einzelheiten in den Entscheidungen und Commits)

1. **Test-Update aus der Rückmeldung (10 Punkte, `items-en.txt`):** `ui/hero` kritisch geprüft (eine unabhängige
   Nachrechnung aller ungelaufenen Fenstertests fand keine Rechenfehler; behoben: Griffbereich im Regal ignorierte
   Hinweise; Doku zu 1, 5, 6, 8 mit der Luft je Ansicht in px). Punkt 9: Strg+F schaltet um, Strg+Umschalt+F entfernt,
   Strg+F1 still, Tastenkappe und Kürzel aus dem Suchfeld entfernt (samt Schwelle 204/216 in `ToolbarLayout`), alle Texte,
   README, `phase10-ui.ps1` (zweimal `--test-toolbar-search`). Punkt 10: `MenuButton` schaltet um; „···“ und die
   Symbol-Kachel im Detailbereich sind jetzt `MenuButton` (ihre Menüs stehen dadurch bündig 4 px unter dem Knopf, etwa
   8 px weiter links als vorher). Unabhängige Prüfung gegen den Wortlaut, Funde behoben.
2. **SteamGridDB-Suche (E93):** nach `sgdb-spec-final.md` gebaut, unabhängig geprüft, 12 Funde behoben (u. a. Übernehmen
   mit beschädigtem Bild, Hoher Kontrast der Reiter, Füllen überschrieb korrigierte Zuordnung, Sitzung erst beim Klick).
3. **Farben folgen dem Spiel (E94):** nach `spielfarben-spec-final.md` gebaut, geprüft, Funde behoben (u. a. DashRing
   fror den lebenden Pinsel ein → möglicher Absturz; „Schalter aus kostet nichts“ stimmte nicht).
   Entscheidungen des Nutzers 2026-10-27: 150°-Grenze zwischen Akzent und Glanz bleibt; schnelle Mausklicks halten die
   Farben wie die Tastatur.
4. **Englische Oberfläche (E95, Wunsch des Nutzers 2026-10-26):** `.en.resx` für alle Texte, neue `ScanStrings` und
   `CliStrings`, Einstellung `language`, Erkennung nur bei neuer Konfiguration (deutsch → Deutsch, sonst Englisch;
   bestehende Konfigurationen bleiben Deutsch), Setup nach Windows-Sprache, Sprachwahl mit „Jetzt neu starten“ (gesperrt
   während ein Spiel läuft), Glossar in E95 („HDR on launch“, „hero image“, „custom“ …). Geprüft, Funde behoben.
5. **README (Wunsch des Nutzers 2026-10-27):** `README.md` englisch, `README.de.md` deutsch, beide im Setup und im
   portablen Ordner; `phase5-readme.ps1` prüft beide (unter Linux mit pwsh grün, `PHASE5_README_OK`).
6. **Release vorbereitet:** Version 1.6.0, `docs/release/1.6.0.md` (englisch und deutsch, erstes Release auf GitHub),
   Entwurfs-PR #1 mit Checkliste.
7. **Build unter Linux:** `HdrPilot.App.csproj` nimmt die Ressourcen-Klassen im temporären WPF-Projekt über einen
   ausgeschriebenen Pfad (unter Linux hängte das SDK das Zielframework doppelt an); unter Windows ändert sich nichts.

## Lokal als Nächstes (Reihenfolge, je Schritt eine Arbeit, `dotnet` gedrosselt)

1. `git fetch origin` und `integration/naechstes-update` auschecken.
2. **publish-check** mit der privaten Liste: Arbeitsstand und `-Rev` für alle oben genannten Cloud-Zweige. Hinweis:
   Commits der Cloud tragen die Uhrzeit in UTC (`+0000`); falls die private Liste Zeitzonen prüft, das beachten.
3. **Build und alle Tests** (`tools\gates\test.ps1`). Zuerst hier nachsehen, wenn etwas rot ist:
   - `TileRoomTests` (Leuchten unter der Kachel, letzter Pixel ~1,0–1,25 gegen Grenze 2: knapp)
   - `ToolbarWindowTests` (Lasche mittig bei 125/150/175 % über `VisualTreeHelper.SetRootDpi`, sonst nirgends genutzt)
   - Spielfarben W3 (Stil-Setter); schlägt er fehl, die vorab erlaubte Ausweichlösung in `Themes/Library.xaml` nach §5.7
     des Bauplans
   - `GameAccentCostTests`: Speicher-Obergrenze ist ein Planwert (256 KB); gemessenen Mittelwert + 25 % eintragen und in
     G15.7 festhalten
   - `MenuToggleTests` simulieren die Maus nur; ein echter zweiter Klick auf „···“, Symbol-Kachel und Titel muss das Menü
     schließen und nicht wieder öffnen (von Hand oder per SendInput prüfen)
   - `HdrListServiceTests.Empty_password…` wackelt (auch unter Linux), nur die Klasse einmal neu laufen lassen
4. **Oberflächen-Gates:** u. a. `phase10-ui`, `phase10-settings-ui`, `phase14-sgdb-ui`, `phase15-gamecolors`,
   `phase16-english-ui`, `phase5-readme`, `phase5-publish`, `phase6-name`, `phase7-setup` (alle neuen bzw. geänderten
   wurden blind geschrieben; Selektoren und Zeiten nie gelaufen).
5. **Bildschirmfotos** deutsch und englisch (`--snapshot-language`), Sichtprüfung mit dem Nutzer: Lasche mittig auf der
   Bildkante (Pixel messen, Punkt 8), englische Texte nicht abgeschnitten, Dialog „Bild wählen…“ bei drei Größen hell und
   dunkel (`-klein` höchstens 592 DIP hoch), Farben folgen dem Spiel (Amber-Text `#7B5100` im hellen Thema ansehen).
6. **Setup 1.6.0** (`tools\publish.ps1`), Installation als Test-Update mit Sicherung und Abgleich der Einstellungen;
   prüfen, dass `README.de.md` dazukommt und die Sprache Deutsch bleibt.
7. **Livetest SteamGridDB** mit dem Schlüssel des Nutzers (G14.14), offene Abnahmen G10.9, G10.11, G11.5, G12.7, G9.8,
   echtes Tray-Gate.
8. **Abnahme durch den Nutzer.** Erst dann: PR #1 aus dem Entwurf nehmen, nach `main` zusammenführen (nie force-push auf
   `main`), Tag `v1.6.0`, GitHub-Release mit `HDRpilot-Setup-1.6.0.exe` und dem Text aus `docs/release/1.6.0.md`.

Die genaue Liste steht auch als Checkliste in PR #1 und oben in `docs/stand.md`.

## Offene Punkte und Vetopunkte für die Abnahme

- Englische Namen von Windows-Elementen in der README sind nicht an einem englischen Windows geprüft: „Use HDR“, „Run
  anyway“ (SmartScreen), „Animation effects“, Regler „Adjust HDR intensity“ der Game Bar.
- Englisches Datum ist immer Monat-Tag-Jahr (`MMM d, yyyy`), auch bei en-GB (E95 begründet es).
- Vetopunkte E93 (§18 des Bauplans): Reiterreihenfolge Titelbild, Logo, Cover; Vollbild lädt vor dem Schließen; neue
  Zuordnung verwirft alte automatische Bilder; „Eigenes Bild“; Cover auch 342×482/660×930; einheitliche Kacheln; eigene
  Zeile für den Schalter in der Karte; Liste endet erst mit leerer Seite.
- E94: Bildtabelle „bunt-tri“ des Bauplans widerspricht der 150°-Grenze (Grenze hat Vorrang, dokumentiert).
- Spielfarben im Regal: Die Lichtnaht läuft dort durch die Kapsel, nicht über die Bildkante (Ausnahme zu Punkt 8,
  dokumentiert, nicht vom Nutzer bestätigt).
- Ungenutzte resx-Schlüssel aus der Zeit vor diesem Update (z. B. `Hdr_Overline`, `Pause_Active`, `View_*Tooltip`) sind
  nicht entfernt.

## Regeln (unverändert, Kurzform)

- Öffentliches Repo: keine lokalen Pfade, keine Namen außer Dante_GE/DanteGE, keine claude.ai-Links, nur erfundene Werte.
  Vor jedem Push publish-check (lokal) bzw. Handprüfung.
- Commits: Autor und Committer „Dante_GE <115689511+DanteGE@users.noreply.github.com>“; Nachricht endet mit Leerzeile und
  „Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>“. Keine weiteren Zeilen danach (Vorsicht bei Merge-Konflikten:
  Git hängt sonst „# Conflicts:“ an).
- Zeilenenden je Datei erhalten: `docs/entscheidungen.md` und die resx unter `src/HdrPilot.Core/Resources` sind CRLF,
  fast alles andere LF; `tools/gates/phase5-publish.ps1` war schon vorher gemischt (5 CRLF-Zeilen).
- Oberfläche und Doku auf Deutsch, Texte nur in resx, jetzt mit `.en.resx`: jeder neue Schlüssel braucht einen englischen
  Wert (der Test `ResourceTranslationTests` prüft Vollständigkeit, Platzhalter, Glossar und Typografie). Ausnahme: README
  englisch, `README.de.md` deutsch, beide pflegen.
- Tests ohne echtes Netz und ohne echte Spielbilder; Gründlichkeit vor Tempo.

## Bauen und testen ohne Windows (falls wieder eine Cloud-Sitzung nötig wird)

- .NET-10-SDK über apt (`dotnet-sdk-10.0`); `dotnet build HdrPilot.slnx -p:EnableWindowsTargeting=true`.
- `dotnet test tests/HdrPilot.Tests -p:EnableWindowsTargeting=true --no-build`: rund 690 Tests scheitern unter Linux immer
  (Windows-APIs); verglichen wird gegen diese Liste, neu scheitern darf nichts.
- `HdrPilot.App.Tests` und alle Gates laufen nur unter Windows.

## Was die Cloud-Sitzung noch tat

- Sie beobachtete PR #1 (Kommentare, Reviews, Konflikte) und prüfte ihn alle paar Stunden mit einer geplanten Aufgabe
  „PR #1 prüfen“ in den geplanten Aufgaben des Kontos. Ist die Cloud-Sitzung weg, diese Aufgabe in claude.ai unter den
  geplanten Aufgaben löschen oder ignorieren; sie ändert nichts am Repo.
- Sie hat nichts gemergt und kein Release erstellt.
