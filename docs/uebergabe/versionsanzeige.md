# Versionsnummer sichtbar (G8.1): Stand und Prüfbericht

> **Status: angehalten am 2026-10-10 auf Wunsch des Nutzers, fast fertig.**
> - Zweig `feature/version-display`, Arbeitsordner `.worktrees/s9-version`.
> - Zweigt von `c1198d4` ab. Den Eintrag G8.1 in `GATES.md` gibt es nur auf `phase5/release`.

## Wunsch des Nutzers
„Bitte für die App noch einplanen, irgendwo wo es sinnvoll ist, die Versionsnummer mit einzubauen.“

## Commits
| Commit | Inhalt | Geprüft |
|---|---|---|
| `67d9136` | Umsetzung, Details unten | ja, vom Umsetzer und vom Prüfer selbst gelaufen. Marker: <ul><li>BUILD_OK_0_WARNINGS</li><li>TESTS_OK 1947</li><li>TESTS_OK 10 (Filter AppVersion)</li><li>PHASE4_RESOURCES_OK</li><li>PHASE6_NAME_OK</li><li>PHASE5_README_OK</li><li>PHASE4_UI_OK, PHASE4_PERSIST_OK</li></ul>9 Gegenproben, alle rot |
| `85ba0db` | WIP der Korrekturrunde: Punkt in der Pille durch Info-Symbol ersetzt, Kommentar im Tray-Menü | **nein**, nicht gebaut |

**Umsetzung in `67d9136`:**
- `src/HdrPilot.App/Infrastructure/AppVersion.cs`: einzige Quelle der Version, dreistellig, gleiche Regel wie das Setup
  (`SetupSupport.Normalize`).
- Text `AppVersionFormat` = „HDRpilot {0}“ in `CommonStrings.resx`.
- **Einstellungen:** Pille rechts im Kopf, AutomationId `SettingsVersion`.
- **Tray-Menü:** graue, nicht anklickbare erste Zeile unter dem Kopf, AutomationId `TrayVersion`, danach ein Trenner.
  Die Pfeiltasten überspringen sie.
- **Setup:** `SetupEngine` nutzt dieselbe Quelle.
- **Tests:** `AppVersionTests` und drei Tests in `ShellViewTests`.
- **Gate:** `phase4-ui.ps1` vergleicht mit der Dateiversion der EXE.
- **README:** drei Sätze.

Bildschirmfotos der Umsetzung: `%TEMP%\hdrpilot-version-shots\` (Einstellungen und Tray, hell und dunkel).

## Funde der Prüfung (alle niedrig, nichts blockiert)
1. **Punkt in der Versions-Pille sieht wie eine Statusanzeige aus.** Es ist die Akzentfarbe, dieselbe wie „ungespeicherte
   Änderungen“. → **Im WIP `85ba0db` behoben:** Info-Symbol E946 in TextMuted. Noch bauen und die Bilder neu ansehen.
2. **Die Tray-Zeile ist an keiner echten Testinstanz geprüft.** G8.1 verlangt UI-Automation an einer Testinstanz.
   → Offen.
   - In `tools/gates/phase6-traymenu.ps1`, Schritt 7, nach dem Öffnen und vor Pfeil nach oben `TrayVersion` lesen und
     prüfen:
     - Name ist „HDRpilot “ plus die Dateiversion der EXE.
     - `IsEnabled` ist falsch.
     - Nach Pfeil nach oben hat die Zeile keinen Tastaturfokus.
   - Achtung: Dieses Gate bewegt echte Maus und Tastatur. Es nur am echten Desktop laufen lassen, wenn der Nutzer
     Bescheid weiß.
   - Optional zusätzlich ein STA-Test, der Down/Up auslöst und `Keyboard.FocusedElement` prüft.
3. **Eine unsichtbare Pille oder Zeile fiele kaum auf.**
   - Der Pixeltest (`ShellViewTests.cs:359`) würde auch vom Hintergrundverlauf erfüllt, bei `Opacity=0` bliebe er grün.
   - Die Tray-Zeile prüft nur `ActualHeight > 0` (`:312`).
   - → Offen: Pixel in Textfarbe zählen, oder mit einem Bild ohne Text vergleichen.
4. **Die Prüfung gegen die Titelzeilen-Schaltflächen in `phase4-ui.ps1:102` kann ohne Aussage grün werden.** Werden die
   Schaltflächen nicht gefunden, ist die Liste leer und die Prüfung meldet „ok“. → Offen: mindestens `CaptionClose`
   verlangen.
5. **README:125 sagt „ganz oben im Menü“.** Tatsächlich steht die Zeile unter dem Menükopf „HDRpilot“. → Der
   XAML-Kommentar ist im WIP angepasst, **die README noch nicht.**
   - Offene Frage an den Nutzer: Soll im Tray „HDRpilot“ doppelt stehen (Kopf und Versionszeile)? Oder reicht dort
     „Version 1.2.0“?

**Hinweise ohne Handlungsbedarf:**
- Das Startprotokoll schreibt die Version vierstellig („1.2.0.0“), siehe `AutomationHost.cs:150`.
- Zwischen `Setup` und `Infrastructure` gibt es einen kleinen Ringbezug.

## Weitermachen
1. In `.worktrees/s9-version` bauen und testen. Dann die Funde 2–5 erledigen und die Gegenproben ansehen. Danach die WIP-
   und Korrektur-Commits sauber abschließen.
2. Nach `phase5/release` zusammenführen. Dabei die G8.1-Nachweise in `GATES.md` eintragen und alle Gates neu laufen
   lassen.
3. Zusammen mit G8.2 und G8.3 als 1.3.0 veröffentlichen, siehe `docs/stand.md`.
