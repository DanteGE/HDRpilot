# Berichte des Ablaufs vom 2026-10-10 (Abend), unverändert übernommen

Der Ablauf wurde um ca. 20:05 auf Wunsch des Nutzers gestoppt (Wochenvolumen knapp). Die Korrekturrunde der Symbole (icons:fix) lief da noch; ihr unfertiger Stand ist als WIP-Commit auf `feature/icons` gesichert. Die Messung an der echten Bibliothek lief noch nicht.

## version:fix

G8.1 is finished on `feature/version-display` as commit **bc23f392edb808d12a8c9f4238be9cede2568106**, and the worktree is clean. Every gate I ran passed, every mutation turned the tests red, and every mutated file was restored. One thing is still unproven: I changed `phase6-traymenu.ps1` but did not run it, because it moves the real mouse.

**History:** I ran `git reset --soft 27f86d9` and committed everything as one commit, "G8.1: Korrekturen nach der Prüfung (Info-Symbol, Tastatur-Test, Sichtbarkeit, Gate-Robustheit, README)", with a German body listing the changes. The history is now 88235f5 → 27f86d9 → bc23f39. All edited files keep LF line endings, and both gate scripts keep their BOM.

**Changes (10 files):**
1. **Info symbol (Finding 1):** the WIP from 85ba0db builds and passes as it was. I checked the renders in light and dark: the muted info symbol E946 sits centred in front of "HDRpilot 1.2.0" and no longer looks like a status dot.
2. **Keyboard test (Finding 2a):** new file `tests\HdrPilot.App.Tests\Shell\TrayMenuKeyboardTests.cs`, which runs on its own. It opens the real `TrayMenu` with its view model and sends Down/Up key events to the focused element.
   - The open popup only accepts keyboard focus if the test thread already has a focused window. The test therefore creates an invisible helper window on its own thread. It does not take the foreground or send real input.
   - The test asserts `Keyboard.FocusedElement` after each press. Seven presses Down give TrayToggleHdr, TrayPause, TrayOpen, TraySettings, TrayLogs, TrayExit, TrayToggleHdr. Seven presses Up give TrayExit, TrayLogs, TraySettings, TrayOpen, TrayPause, TrayToggleHdr, TrayExit. After every press it also checks that TrayVersion does not have focus.
3. **Tray gate (Finding 2b):** in `tools\gates\phase6-traymenu.ps1` step 7, before the Up key, the script reads TrayVersion through UI Automation. It checks that the Name equals "HDRpilot " plus the three-part file version of the EXE and that IsEnabled is false. After Up it checks that TrayVersion has no keyboard focus. The script parses with 0 errors.
4. **Visibility (Finding 3):** in `ShellViewTests.cs`, a helper counts pixels in the TextSecondary colour inside the text area. Each check needs a minimum count and at most half the area.
   - The pill check replaces the old "more than 20 distinct colours" check. It needs at least 100 pixels; I measured 214 of 2875.
   - The new test `AppVersion_line_renders_readably_in_the_tray_menu_in_both_themes` needs at least 30; I measured 59 of 7975.
   - The counts are the same in light and dark.
5. **Caption buttons (Finding 4):** `tools\gates\phase4-ui.ps1` now records the caption buttons it finds and reports FEHLER if `CaptionClose` is missing.
6. **README wording (Finding 5):** the three README sentences now say the version line sits directly below the menu header. I also updated the comments in `AppVersion.cs` and `TrayMenuViewModel.cs`. The tray text "HDRpilot <version>" is unchanged.

**Gate markers (final run on bc23f39):**

| Command | Exit | Marker |
|---|---|---|
| build.ps1 | 0 | BUILD_OK_0_WARNINGS |
| test.ps1 (full) | 0 | TESTS_OK 1949 (was 1947; 2 new tests) |
| test.ps1 -Filter "FullyQualifiedName~AppVersion" -MinTests 4 | 0 | TESTS_OK 12 |
| phase4-resources.ps1 | 0 | PHASE4_RESOURCES_OK |
| phase6-name.ps1 | 0 | PHASE6_NAME_OK |
| phase5-readme.ps1 | 0 | PHASE5_README_OK (README self-check: 15 of 15 planted errors found) |
| phase4-ui.ps1 | 0 | PHASE4_UI_OK, PHASE4_PERSIST_OK |

- phase4-resources also prints `PRÜFEN: src\HdrPilot.App\Setup\UI\SetupRunner.cs:38`. That is an advisory line about a file I did not touch.
- phase4-ui reported: version 'HDRpilot 1.2.0' matches the EXE file version; caption buttons found: CaptionClose, CaptionMaximize, CaptionMinimize; overlapped: none.
- The display stayed active throughout, so no gate failed for environment reasons.

**Mutation results (all red):**

| Mutation | Run | Result |
|---|---|---|
| M1: version line style IsEnabled=True and Focusable=True | AppVersion filter | 2 red: `AppVersion_line_tops_the_tray_menu_and_is_skipped_by_the_keyboard` and the keyboard test |
| M1 again | keyboard test alone | red: "Versionszeile hat nach 1× Down den Tastaturfokus" |
| M2: `VersionPill` Opacity=0 | AppVersion filter | pill render test red |
| M3: `SettingsVersion` text Opacity=0 | AppVersion filter | red: "0 von 2875 Pixeln in Textfarbe" |
| M4: `TrayVersion` MenuItem Opacity=0 | AppVersion filter | red: "0 von 7975 Pixeln in Textfarbe" |
| M5: pill background set to TextSecondary | AppVersion filter | red: "2276 von 2875" (upper limit) |
| M6: `CaptionClose` AutomationId renamed in `Controls.xaml`, app rebuilt | phase4-ui.ps1 | exit 1: "FEHLER Titelzeilen-Schaltflächen gefunden: CaptionMaximize, CaptionMinimize" |

After M6 I restored `Controls.xaml` and the rebuild passed.

**Still open:**
- **Tray gate not run:** `phase6-traymenu.ps1` has to be run on the real desktop with the user present. Until then, the tray line is covered by unit tests only, not by UI Automation on a running test instance as G8.1 asks.
- **UX question:** the user has not yet decided whether the tray menu should show "HDRpilot" twice (header and version line) or just "Version 1.2.0".
- **Records not updated:** I did not edit GATES.md, `docs/stand.md` or the handoff `docs/uebergabe/versionsanzeige.md` on p4. The commit hash and markers above still need to go there.
- **Render folder path length:** saving the review images fails with a GDI+ error when `HDRPILOT_SHELL_RENDER_DIR` is too long. My scratch path was 258 characters. The short 8.3 form `<Notizordner, 8.3-Kurzform>\version-work` works, and normal test runs without that variable are unaffected.
- **Popup during tests:** the keyboard test briefly opens a real popup at the mouse pointer. It ignores mouse hover and was stable in 4 repeated runs plus 2 full runs.

No test instances are left running; only the installed HDRpilot (PID 24844) is running, untouched. This subagent did not shut down the computer; that is left to the orchestrator.

Files:
- .worktrees\s9-version\tests\HdrPilot.App.Tests\Shell\TrayMenuKeyboardTests.cs
- .worktrees\s9-version\tests\HdrPilot.App.Tests\Shell\ShellViewTests.cs
- .worktrees\s9-version\tools\gates\phase6-traymenu.ps1
- .worktrees\s9-version\tools\gates\phase4-ui.ps1
- .worktrees\s9-version\README.md
- <Notizordner, 8.3-Kurzform>\version-work\render-final (light and dark renders)

## version:review

**Ergebnis: G8.1 ist noch nicht erfüllt.** Der Code ist korrekt und die Tests fallen ohne ihn rot aus. Es fehlt aber die UI-Automation an einer Testinstanz für die Tray-Zeile, und die Tastatur-Prüfung im Tray-Gate sagt nichts aus.

## Probleme

**1. HOCH (fehlender Gate-Nachweis, kein Code-Fehler)**
- **Datei:** `tools\gates\phase6-traymenu.ps1:309-335`, gemessen an G8.1 in `p4\GATES.md:261`.
- **Szenario:** G8.1 verlangt für beide Stellen Unit-Tests und UI-Automation an einer Testinstanz. Für die Einstellungen belegt das `phase4-ui.ps1`. Die Tray-Zeile ist nur per Unit-Tests belegt, denn Schritt 7 von `phase6-traymenu.ps1` ist nie gelaufen. G8.1 darf deshalb nicht abgehakt werden.
- **Fix, Variante A:** `phase6-traymenu.ps1` am echten Desktop mit dem Nutzer laufen lassen. Erwartet wird `PHASE6_TRAYMENU_OK`.
- **Fix, Variante B (ohne SendInput):** ein eigenes Gate.
  - Es schickt per PostMessage `WM_USER+1024` mit lParam `WM_RBUTTONUP` an das NotifyIcon-Fenster der Testinstanz. Das Fenster findet `FindIcon` bereits. Der Weg in der App ist dann `NotifyIconSurface.OnMouseUp` → `ShowMenu`.
  - Danach liest es `TrayVersion` per UI-Automation (Name, IsEnabled) und beendet die Testinstanz.
  - Das ist eine synthetische Mausnachricht. Ob das zulässig ist, muss der Orchestrator oder der Nutzer entscheiden.

**2. NIEDRIG (Gate-Prüfung ohne Aussage)**
- **Datei:** `tools\gates\phase6-traymenu.ps1:320-328`.
- **Szenario:** Das Gate drückt nur Pfeil nach oben. Der springt immer zum letzten Eintrag, auch wenn die Versionszeile fokussierbar wäre. Das Überspringen der Zeile wird am echten Programm also nie geprüft.
  - Die Prüfung „Versionszeile hat nach Pfeil nach oben keinen Tastaturfokus“ ist immer wahr: Ein nicht aktives Element meldet nie `HasKeyboardFocus`. Außerdem folgt sie schon aus `$exitFocused`.
  - Rot würde das Gate unter M1 nur durch die Prüfung von IsEnabled, nicht durch die Tastatur.
- **Fix:** in Schritt 7 zuerst Pfeil nach unten drücken und prüfen, dass `TrayToggleHdr` den Fokus hat und `TrayVersion` nicht. Dann Pfeil nach oben: Das Menü springt an der Versionszeile vorbei zu `TrayExit`, genau wie im letzten Schritt des Unit-Tests. Danach wie bisher Eingabe.

**3. Offen (Entscheidung des Nutzers, kein Fehler)**
- „HDRpilot“ steht im Tray doppelt (Kopf und Versionszeile). Das entspricht dem Text von G8.1.
- GATES.md, `docs/stand.md` und die Übergabe auf p4 sind noch nicht nachgetragen.

**Hinweise ohne Handlungsbedarf:**
- Das Startprotokoll schreibt die Version vierstellig (`AutomationHost.cs:150`); das war schon vorher so.
- Die UIA-Gates lesen `AutomationProperties.Name`. Dass der sichtbare Text gleich ist, prüfen nur die Unit-Tests.
- `TrayMenuKeyboardTests` öffnet ein echtes Popup und braucht deshalb eine interaktive Desktop-Sitzung.

## Selbst ausgeführt (auf bc23f39)

| Befehl | Exit | Marker |
|---|---|---|
| `build.ps1` | 0 | BUILD_OK_0_WARNINGS |
| `test.ps1` (alle Tests) | 0 | TESTS_OK 1949 (gesamt 1949, fehler 0, übersprungen 0) |
| `test.ps1 -Filter "FullyQualifiedName~AppVersion" -MinTests 4` (2×) | 0 | TESTS_OK 12 |
| `phase4-ui.ps1` | 0 | PHASE4_UI_OK, PHASE4_PERSIST_OK |
| `phase4-resources.ps1` | 0 | PHASE4_RESOURCES_OK |
| `phase6-name.ps1` | 0 | PHASE6_NAME_OK |
| `phase5-readme.ps1` | 0 | PHASE5_README_OK (15 von 15, 3 von 3) |

- `phase4-ui.ps1` meldete:
  - „Version im Kopf der Einstellungen: 'HDRpilot 1.2.0' (Dateiversion der EXE: 'HDRpilot 1.2.0')“
  - „Titelzeilen-Schaltflächen gefunden: CaptionClose, CaptionMaximize, CaptionMinimize“
  - „überdeckt: keine“
  - Daneben steht „HdrStatus: Kein Bildschirm gefunden“; das ist nur ein Hinweis, keine Prüfung.
- Die `PRÜFEN`-Zeilen von `phase4-resources.ps1` betreffen nur ältere Protokolltexte, keine neuen Texte.
- Ein Bildschirm war aktiv (DISPLAY1 3440×1440), kein Gate scheiterte an der Umgebung.

## Gegenproben

Alle liefen in einer Kopie von HEAD (per `git archive`) im ignorierten Ordner `tools\out\rv`. Die Kopie ist danach gelöscht, der Arbeitsordner wurde nicht verändert. In der Kopie liefen ohne Änderung alle 12 Tests grün.

| Mutation | Ergebnis |
|---|---|
| M1: IsEnabled und Focusable auf True | 2 rot: `AppVersion_line_tops…` (Assert.False, `ShellViewTests.cs:311`) und der Tastatur-Test („Versionszeile hat nach 1× Down den Tastaturfokus“) |
| M4: TrayVersion Opacity=0 | rot: „0 von 7975 Pixeln in Textfarbe“ |
| M3: SettingsVersion Opacity=0 | rot: „0 von 2875“ |
| Eigene: Trenner unter der Versionszeile entfernt | rot (Assert.IsType) |
| Eigene: nur IsEnabled auf True | rot |
| Eigene: Setup-Version ungleich App-Version | rot: `AppVersion_is_what_the_setup_installs_and_registers` |
| Gate: `AppVersion.Of` liefert Build+1, dann `phase4-ui.ps1` | Exit 1: „FEHLER Version im Kopf der Einstellungen: 'HDRpilot 1.2.1' (Dateiversion der EXE: 'HDRpilot 1.2.0')“ |

## Nur gelesen

- **`phase6-traymenu.ps1`:** parst mit 0 Fehlern, hat BOM und LF. Nicht ausgeführt.
- **Version nirgends fest im Code:** Eine Versionsnummer kommt in `src` nur in Doc-Kommentaren („etwa 1.2.0“) vor.
- **Gleiche Zahl wie Setup und „Installierte Apps“:**
  - `AppVersion` ist die Assembly-Version nach der Regel des Setups, ein Test vergleicht sie mit der Dateiversion.
  - Das Setup übernimmt `AppVersion.Current`, `DisplayVersion` ist getestet.
- **Zeilenenden:** LF und BOM sind erhalten.
- **README:** stimmt mit dem Code überein.
- **Eigene Bilder von HEAD (Tray-Menü und Einstellungs-Kopf, hell und dunkel):**
  - Die Versionszeile ist lesbar.
  - Das Info-Symbol ist zurückgenommen und wirkt nicht wie ein Statuspunkt.
  - Nichts überdeckt etwas.

Arbeitsordner sauber auf bc23f39. Keine Testinstanz läuft noch; nur das installierte HDRpilot (PID 24844) läuft, unberührt. Den Rechner habe ich nicht heruntergefahren, das ist Sache des Orchestrators.

Dateien:
- .worktrees\s9-version\tools\gates\phase6-traymenu.ps1
- .worktrees\p4\GATES.md
- <Notizordner, 8.3-Kurzform>\version-work\render-rv
- <Notizordner, 8.3-Kurzform>\version-work\rv-mutate.ps1

## version:fix2

Commit `145b60a` is on `feature/version-display` as a new commit on top of bc23f39, and the worktree is clean. I fixed finding 2. Finding 1 is confirmed but I could not fix it, so G8.1 stays unchecked. Finding 3 needs no code change.

**Finding 1 (HIGH, missing UI-Automation evidence for the tray line): confirmed, still open.**
- The tray line is only covered by unit tests. Step 7 of `phase6-traymenu.ps1` has never run.
- Variant A is not possible now: that gate moves the real mouse and keyboard, and the hard rules forbid running it.
- Variant B was blocked. I tried to run a probe (`probe-tray.ps1` in the scratch folder). It would post `WM_USER+1024` with `WM_RBUTTONUP` to the NotifyIcon window of a `--config-dir` test instance. The permission system refused it ("Security Weaken") before it ran, so no test instance was started.
- I did not retry or work around it, and I wrote no Variant B gate script.
- The user has to choose: either run `phase6-traymenu.ps1` at the real desktop (expect `PHASE6_TRAYMENU_OK`), or allow the synthetic tray-message gate.

**Finding 2 (LOW, keyboard check in the tray gate proves nothing): confirmed and fixed.**
- **Confirmed:** I cut the keyboard test down to the old gate sequence (Arrow Up only) in a copy. With the version line made enabled and focusable, it still passed, because Arrow Up from the start always lands on the last entry.
- **Gate fix:** step 7 of `phase6-traymenu.ps1` now presses Arrow Down first. The check is that "HDR jetzt …" (`TrayToggleHdr`) has the focus and the version line does not. Then Arrow Up must skip the version line and land on `TrayExit`, then Enter. The redundant "Versionszeile hat nach Pfeil nach oben keinen Tastaturfokus" check is folded into these checks. The script parses with 0 errors and keeps its BOM and LF. I did not run it (forbidden).
- **Unit test:** `TrayMenuKeyboardTests` now runs the gate's exact sequence (Down, Up) on the open menu and expects `TrayToggleHdr`, then `TrayExit`. The helper now takes a list of keys.
- **Mutation check:** in a copy under `tools\out\mg` (deleted afterwards), with only the new block left in the test:

| Mutation | Result |
|---|---|
| None | green |
| Version line enabled and focusable | red: "Versionszeile hat nach Down den Tastaturfokus" |
| Arrow keys no longer cycle (`Contained`) | red: expected "TrayExit", got "TrayToggleHdr" |

- I also updated the comments in `Shell.xaml` and `ShellViewTests.cs` that described the old sequence.

**Finding 3 (user decisions, GATES.md / stand.md / handoff):** not a defect. "HDRpilot" appearing twice matches the G8.1 wording. GATES.md and `docs/stand.md` are not mine to edit.

**Gate results (on the committed content):**

| Command | Exit | Marker |
|---|---|---|
| `build.ps1` | 0 | BUILD_OK_0_WARNINGS |
| `test.ps1` (all tests) | 0 | TESTS_OK 1949 (1949 total, 0 failed, 0 skipped) |
| `test.ps1 -Filter "FullyQualifiedName~AppVersion" -MinTests 4` | 0 | TESTS_OK 12 |
| `phase4-resources.ps1` | 0 | PHASE4_RESOURCES_OK (78 "PRÜFEN" lines for manual review, none from my change) |
| `phase6-name.ps1` | 0 | PHASE6_NAME_OK |
| `phase5-readme.ps1` | 0 | PHASE5_README_OK (15 of 15, 3 of 3) |
| `phase4-ui.ps1` | 0 | PHASE4_UI_OK, PHASE4_PERSIST_OK (0 FEHLER; settings header shows 'HDRpilot 1.2.0', same as the EXE file version) |

- Windows still reported DISPLAY1 (3440×1440) as active, and no gate failed because of the environment.
- Afterwards no test instance was running; only the user's installed HDRpilot (PID 24844) was, untouched.
- I did not shut the computer down; that is the orchestrator's job.

Files:
- .worktrees\s9-version\tools\gates\phase6-traymenu.ps1
- .worktrees\s9-version\tests\HdrPilot.App.Tests\Shell\TrayMenuKeyboardTests.cs
- .worktrees\s9-version\tests\HdrPilot.App.Tests\Shell\ShellViewTests.cs
- .worktrees\s9-version\src\HdrPilot.App\Themes\Shell.xaml
- <Notizordner>\version-work\mg-mutate.ps1
- <Notizordner>\version-work\probe-tray.ps1 (the blocked probe, never ran)

## icons:impl

Commit **49ac571** on `feature/icons`: "G8.2/G8.3: Ersatz-Symbole aus lokalen Quellen und eigenes Symbol per Rechtsklick (E86)". It is a single commit on top of ad98e51, with the WIP folded in via `git reset --soft`. The worktree is clean afterwards. All required gates passed, plus a new UI gate for the custom icon. The full test suite failed once on one HDR-list test that this change does not touch, and passed on two later full runs.

I did not shut the PC down. That is left to the orchestrator.

**Measurement on the real library** (read-only `games import` into `tools\out\icon-measure`, now 650 games; the user's config was not touched):
- 650 of 650 games have an icon. 621 are sharp (≥ 48 px and not a known default icon), 0 show a default icon.
- 29 stay below 48 px: 28 Steam games whose only icon is 32 px, plus Dead Island 2 at 44 px.
- Gears of War: E-Day and The Isle now show their 256 px Steam icon. Palworld gets its Square480x480Logo, SWAT 3 and 4 the 256 px goggame `.ico`.
- 421 icons come from launcher hints, 229 from game EXEs.
- The G8.2 target (≥ 618 sharp) is met.

**What the WIP needed**
- It compiled, but the default-icon list held placeholder zeros instead of real fingerprints, and the library context menu (XAML) was missing.
- The test fakes lacked the new interface methods.
- The exclusion list did not cover BattlEye, EA Anti-Cheat, GameGuard, EOS, crash reporters or installers.
- The first measurement found two more wrong picks: Unreal 3 tools (`ShaderKeyTool.exe`, `StatsViewer.exe`) for Homesick and Viscera Cleanup Detail: Shadow Warrior. Those tools are now excluded too.

**Design**
- **Order:** custom icon, then launcher hints, then the game EXE. The EXE is only consulted when no hint is good. Steam's official icon comes first and the EXE replaces it only if the Steam icon is missing, a default icon, or under 48 px while the EXE has ≥ 48 px.
- **Default icons:** a fixed list of 10 fingerprints: the 8 measured ones, Unreal 3 (UDK) and the Windows app icon. Re-read from the game files, all 8 hashes match the research exactly. Of the 598 distinct EXE icons in the library, the closest real game icon is distance 29 (threshold is 8), Boltgun is 68, and none is wrongly flagged.
- **Exclusion list (icons only, not in `HelperExecutables`):** `src/HdrPilot.Core/Games/IconExcludedExecutables.cs`. Covers EasyAntiCheat, BattlEye, EA Anti-Cheat, GameGuard, EOS, crash reporters, redistributables/installers, the tools named in the spec, and the Unreal 3 tools.
- **EXE choice:** Unreal `[<Project>\]Binaries\Win64` comes first, `*-Shipping.exe` before the rest, the `Engine` folder never. The deeper search (3 levels, max 300 folders, 300 EXEs, 1 s) only runs when there is no real icon at all, and only on the icon worker thread.
- **Remembered choice:** `icon-cache.json` in the data folder. It is renewed when the game, the rules version or the file changes, or when the source is no longer allowed. A choice without a good icon is rechecked after 7 days.
- **Custom icon:** right-click on a row or click the big detail icon. "Symbol ändern…" (`ChangeIconMenuItem`) and "Symbol zurücksetzen" (`ResetIconMenuItem`, only enabled with a custom icon). Stored as a 96 px PNG in `<DataDirectory>\icons\`. The config holds only the file name, and anything else is dropped on load. The file dialog is injectable; with `--config-dir` the env var `HDRPILOT_TEST_ICON_FILE` supplies the file.
- **Config size:** `iconCandidates` is persisted so icons are right before the background scan finishes. That adds 89 KB (18 %) to `config.json` for 650 games.
- **Robustness:** UNC paths are never read; images over 32 MB or 36 megapixels are not decoded; decoder failures count as "no icon".

**Main files:** `GameIconService.cs` (chain and EXE resolver), `GenericIcons.cs`, `IconReader.cs`, `IconChoiceCache.cs`, `CustomIconStore.cs`, `IconFiles.cs`, `IconExcludedExecutables.cs`, `SteamIconLocator.cs`, the GOG/Xbox/Ubisoft scanners, `MainWindow.xaml(.cs)`, `Themes/Library.xaml`, `LibraryViewModel.cs`, the new gate `tools/gates/phase8-icons.ps1`, `README.md`, and E86 in `docs/entscheidungen.md` (kept CRLF). There are 11 new test files.

**Markers (exit 0 for every gate below)**

| Command | Result |
|---|---|
| `build.ps1` | BUILD_OK_0_WARNINGS |
| `test.ps1` (full) | TESTS_OK 2124 |
| `test.ps1 -Filter "FullyQualifiedName~Icon" -MinTests 30` | TESTS_OK 220 (33 of them existed before) |
| `test.ps1 -Filter "FullyQualifiedName~CustomIcon" -MinTests 6` | TESTS_OK 22 |
| `phase4-resources.ps1` | PHASE4_RESOURCES_OK |
| `phase6-name.ps1` | PHASE6_NAME_OK |
| `phase5-readme.ps1` | PHASE5_README_OK |
| `phase4-ui.ps1` | PHASE4_UI_OK and PHASE4_PERSIST_OK |
| `phase6-ui.ps1` | PHASE6_UI_OK |
| `phase8-icons.ps1` (new) | PHASE8_ICONS_OK |
| `phase2-scanner-tests.ps1` | TESTS_OK 446, PHASE2_SCANNER_TESTS_OK |
| `phase2-scan.ps1` | PHASE2_SCAN_OK (650 games) |

The UI gates worked with the monitor off; none failed for lack of a display.

**Mutation checks** (each applied, filtered tests run, then the file restored; all source hashes verified identical afterwards). All 18 turned the tests red:
- M1: BattlEye pattern `*_BE` removed.
- M2: "EAC" word detection disabled.
- M3: quality threshold 32 px instead of 48 px.
- M4: a good Steam icon no longer wins first.
- M5: deeper search also runs next to a real Steam icon.
- M6: Uninstall entry read from the 32-bit registry view.
- M7: `clienticon` not parsed from appinfo.vdf.
- M8: Shipping EXE not preferred.
- M9: deeper-search folder limit 3000 instead of 300.
- M10: deeper-search time limit removed.
- M11: default icons matched by exact hash only.
- M12: custom icon loses its priority.
- M13: custom icon stored at 48 px.
- M14: `customIcon` not validated on load.
- M15: "Symbol zurücksetzen" always enabled.
- M16: remembered source not re-checked.
- M17: UNC paths allowed as hints.
- M18: `Engine` folder not skipped.

**Open points**
- **Flaky test:** `HdrListServiceTests.Empty_password_reuses_the_stored_one_for_the_same_user_only` failed in one full run and in 2 of 3 runs on its own ("Sequence contains more than one matching element" — it looks like a background sync sending a second login). This branch does not change that test or the code it covers.
- **Visual check not done:** with the monitor off I could not look at the new menu and icon-button styles. Their behaviour is covered by the UI gate and unit tests.
- **Desktop/Start menu shortcuts:** they still take their icon from the EXE resolver (which now uses the exclusion list and Shipping preference), not from the Steam `.ico` or the custom icon.
- **Cosmetic:** Steam hint paths derived from the registry `SteamPath` are stored in lowercase.
- **Measurement tool:** it is not in the repo. Source is in `<Notizordner>\icons-impl\werkzeug` (built copy in the ignored `tools\out\icontool`). Results are in `<Notizordner>\icons-impl\messung-nachher\measure-after.txt` and `measure.json`.
- **Gate evidence:** `GATES.md` and `docs/stand.md` were not edited; G8.2/G8.3 evidence is still pending for you to record.

## icons:review:correctness

I re-ran both checks: the build is clean and all 220 icon tests pass. The review found one medium problem and four low ones; nothing blocks G8.2/G8.3.

**Markers (re-run in `.worktrees\s9-icons`, HEAD 49ac571, worktree still clean)**
- `build.ps1`: exit 0, `BUILD_OK_0_WARNINGS`.
- `test.ps1 -Filter "FullyQualifiedName~Icon" -MinTests 30`: exit 0, `TESTS_OK 220`.
- I ran no other gates.

**Extra read-only checks** (my own tool in the scratchpad at `<Notizordner>\icons-review-correctness\rtool`, built against the current Debug DLLs):
- **Launcher hints vs. default-icon detection:** all 654 hints in `tools\out\icon-measure\config.json` are readable, and 0 are flagged as default icons. The closest real hint is distance 26 (Infectonator 3 vs. Godot 4). That is closer than the 29 that E86 reports, but E86 only counts EXE icons, so it is not wrong. The threshold is 8.
- **Synthetic simple icons:** 4000 flat squares, circles and frames in random colours; none were flagged as default icons.
- **Fuzzing the size readers:** 30,000 mutated copies of `cmd.exe` through `IconFileSizes.LargestInPe` and 30,000 mutated Steam `.ico` through `LargestInIco`. No exceptions; the result is either 256 or -1.
- **Odd image shapes** (1×1, 4000×1, 1×4000, 10×1000, 1000×10): `DecodeFitted`, `IconReader.Read` and `CustomIconStore.Render` all return a 48 or 96 px square without throwing.

**Problems**

1. **MEDIUM: an older icon load can overwrite the icon that was just reloaded.**
   - **Where:** `src/HdrPilot.App/ViewModels/Library/LibraryViewModel.cs:1175` (`item.SetIcon(icon)` runs with no check that the request is still current), together with the new `GameItemViewModel.ReloadIcon` (`GameItemViewModel.cs:163`, called from `Apply` at `:187` and from `RefreshIcon`).
   - **Related in the service:** the guard at `GameIconService.cs:294-297` compares the old config's signature with itself, so it is almost always true. It writes `_byGame` and `icon-cache.json` (`:351`) with the old signature over the newer entry.
   - **Failure:** the first open after the update while the first scan is still running.
     1. Visible rows request icons with the old config, which has no `iconCandidates`. That means EXE probing and a deep search of up to 1 s each, on 2 workers.
     2. The scan adds the Steam hints, so `Apply` sees changed inputs and calls `ReloadIcon`. The new requests go on top of the LIFO queue and show the Steam icon.
     3. The older queued requests finish afterwards and call `SetIcon` with the EXE or fallback icon, replacing the Steam icon until the window is reopened.
   - The same happens with "Symbol ändern…" if the row's automatic load is still pending: the custom icon shows first and is then replaced by the automatic one.
   - **Fix:** keep a request counter or the requested `GameConfig` reference on the item. In `LoadIconAsync`, apply the result only if it is unchanged. In `Load`, only write `_byGame` and `_choices` if the signature still matches the latest config for that key.

2. **LOW: a remembered launcher path skips the UNC check.**
   - **Where:** `src/HdrPilot.App/Services/GameIconService.cs:415-417` (`IsAllowedSource`). The launcher branch accepts any path listed in `game.IconCandidates`, without `IconFiles.IsReadableCandidate`.
   - **Failure:** if both `config.json` (`iconCandidates`) and `icon-cache.json` are edited by hand to a `\\server\…` path, `_fileStamp` and `ReadPath` access the network share. This contradicts "UNC paths are never read". `Choose` itself is safe.
   - **Fix:** require `IconFiles.IsReadableCandidate(path)` before either branch.

3. **LOW: "Symbol zurücksetzen" deletes whatever file name the config holds.**
   - **Where:** `LibraryViewModel.cs:1344` and `CustomIconStore.Delete` (`CustomIconStore.cs:56`).
   - **Failure:** if `config.json` is edited by hand so that game A's `customIcon` is game B's PNG name, resetting A deletes B's PNG, and B silently falls back to its automatic icon.
   - **Fix:** only delete when `name == CustomIcons.FileNameFor(item.Key)`; otherwise just clear the config field.

4. **LOW: removing a manual game leaves its custom PNG behind.**
   - **Where:** `LibraryViewModel.cs:854` (`RemoveGameAsync`).
   - **Failure:** the PNG stays in `<DataDirectory>\icons` with no config entry pointing to it.
   - **Fix:** after a successful write, call `DeleteCustomIconQuietly(item.Config.CustomIcon)`.

5. **LOW: the Xbox variant choice can pick a smaller file.**
   - **Where:** `src/HdrPilot.Core/Games/Scanners/Xbox/PackageAssets.cs:60/72` (`Score`). It compares `scale-N` (a percentage) and `targetsize-N` (pixels) on one scale.
   - **Failure:** a package with only `Square44x44Logo.scale-100.png` (44 px) and `targetsize-48.png` picks the 44 px file, which counts as not sharp. The impact is small: this logo comes last in the candidate list, and GDK configs usually name exact files.
   - **Fix:** turn each value into pixels before comparing (scale × base size taken from the stem, e.g. 44 × N/100; targetsize = N).

**Checked and found OK:**
- **appinfo.vdf:** the v41 string table (magic 0x29) and reading `clienticon`/`icon` from `appinfo/common`. Only 40-character hex values are used, so a crafted name cannot become a path.
- **Steam uninstall entry:** `DisplayIcon` is read from the 64-bit registry view only.
- **`.url` files:** quotes, the `,index` suffix, CRLF line endings, and 64-bit non-Steam IDs are all handled.
- **GOG:** `goggame-<id>.ico` first, then the Galaxy `.webp`. A missing WebP codec just means no icon.
- **Ubisoft:** `DisplayIcon` from the 32-bit view.
- **Xbox:** logo paths cannot escape the package folder.
- **ICO/EXE size reading:** both are bounds-checked.
- **Default icons:** detection does not use frequency, so the three Boltgun entries are not flagged.
- **Custom icon file names:** a hash suffix keeps names unique, and invalid `customIcon` values are dropped on load.
- **Writes:** the custom PNG (temp file plus move) and `icon-cache.json` are written atomically.
- **Image limits:** 32 MB / 36 MP.
- **Old configs:** an old `config.json` loads, and unknown fields are ignored, so going back to the previous version also works.
- **Rescans:** `LibraryMerger` replaces the hints and keeps `CustomIcon`.

I did not shut the PC down; that is left to the orchestrator.

## icons:review:decisions

Review of feature/icons (ad98e51..49ac571) for G8.2/G8.3, read-only. I made no edits or commits; the worktree is still clean at 49ac571. I found one medium and four low problems.

**Markers I re-ran myself**
- `build.ps1`: exit 0, `BUILD_OK_0_WARNINGS`. My first attempt failed with 484 errors (missing `ref\*.dll`). That was a build running at the same time in the same worktree; the rerun was clean.
- `test.ps1 -Filter "FullyQualifiedName~Icon" -MinTests 30`: exit 0, `TESTS_OK 220`.
- `test.ps1 -Filter "FullyQualifiedName~AntiCheat|FullyQualifiedName~CustomIcon" -MinTests 6`: exit 0, `TESTS_OK 44`.
- `phase5-readme.ps1`: `PHASE5_README_OK`.
- `phase4-resources.ps1`: `PHASE4_RESOURCES_OK`.
- `phase2-scan.ps1`: `PHASE2_SCAN_OK` (650 games, 411 ms, Steam 132 ms).
- `phase8-icons.ps1`: exit 0, `PHASE8_ICONS_OK` with the monitor off. Both menu AutomationIds were found; "Symbol zurücksetzen" was disabled before and enabled after; a 96x96 PNG and `customIcon` were written, then both removed.

**Mutations re-run** (in a separate copy under the ignored `tools\out\rv-mut`, since deleted; files restored and hashes checked)
- Baseline: `TESTS_OK 220`.
- M4 (a good launcher hint no longer wins first): 1 test red, `Icon_chain_takes_the_official_steam_icon_when_it_is_sharp`.
- M1 (`*_BE` removed): 2 tests red, `Icon_exclusion_covers_BattlEye` (Arma3_BE, DayZ_BE).
- Extra M5 (deep search also next to a real Steam icon): 1 test red, `Icon_chain_searches_deeper_only_without_any_real_icon`.

**Real install folders** (read-only probe against the built DLLs)
- **The Isle:** the Steam `.ico` (256 px) wins. Without that hint the choice is `TheIsle\Binaries\Win64\TheIsleClient-Win64-Shipping.exe` (256 px, not a default icon). The root `TheIsle.exe` is recognised as the EasyAntiCheat default icon.
- **Gears of War: E-Day:** the Steam `.ico` (256 px) wins. Without it the choice is `FairlightConcept\Binaries\Win64\GoWEDay-Steam.exe` (256 px). `start_protected_game.exe` and `EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe` are excluded.

**Other checks that came out clean**
- The diff adds no network or process APIs.
- `HelperExecutables` is untouched.
- The deep search is only reached through `Choose`, which runs on the icon worker thread (`Select` is not used in product code).
- All 229 EXE-based picks in the measurement are real game EXEs.
- Installer and crash-reporter EXEs found in the library that are not on the icon exclusion list (`CrashSender`, `BsSndRpt`, `InstallerMessage`, `Social-Club-Setup`, `Rockstar-Games-*`, `VulkanRT`, `oalinst`, `GGSetup`, `Elytra-Setup`) are already covered by the default `HelperExecutables` patterns.
- `docs/entscheidungen.md` is still CRLF; `phase8-icons.ps1` has its BOM.

**Problems**

1. **Medium – "no icon" stays remembered for 7 days after the game folder comes back.** `src/HdrPilot.App/Services/GameIconService.cs:329-337` and `:351-353`
   - Scenario: an icon is requested while the install folder or EXE is missing. That happens for not-installed games, which the list still shows, for a disconnected drive, or for an Epic or Xbox game still downloading. `Choose` returns null and `icon-cache.json` stores `Path = null`.
   - After a reinstall to the same folder, the inputs that make up the cache key (folder, hints, EXE lists) are unchanged. `Automatic` therefore returns null for up to `RecheckAfter` (7 days) and the list shows only the launcher's placeholder.
   - Reproduced: with no folder → null. Folder plus EXE appear: a fresh choice gives `Game.exe` at 256 px, but a new instance reading `icon-cache.json` still returns null.
   - Fix: do not store a null (or not-good) choice when the install folder does not exist or no hint file exists. Alternatively add folder existence and `IsInstalled` to `IconSignature`, or keep null choices in memory only.

2. **Low – an older icon load can overwrite a newly set custom icon.** `src/HdrPilot.App/ViewModels/Library/LibraryViewModel.cs:1168-1176` with `GameItemViewModel.cs:153/163`
   - `ReloadIcon` starts a new request, but the older request is not cancelled or ignored. The queue is last-in-first-out and the config is copy-on-write, so the old request still holds the config without `CustomIcon`.
   - Scenario: the user sets a custom icon on a game whose first icon request is still queued, for example after fast scrolling. The new request finishes first and shows the custom icon. The old one finishes later and `SetIcon` replaces it with the automatic icon until the window is reopened.
   - Fix: give each item a request counter, raise it in `ReloadIcon`, and drop results from older requests (or compare `item.Config` with the config that was requested).

3. **Low – "Mit HDR starten" shortcuts still take their icon from `IconExecutableResolver.Resolve`.** `src/HdrPilot.App/AppServices.Library.cs:28`
   - `Resolve` does not check for default icons and ignores the Steam `.ico` and the custom icon.
   - Probe: with `DetectedExecutables = [Nebelinsel\Nebelinsel.exe]`, `Resolve` returns `Nebelinsel.exe`, which carries the EasyAntiCheat icon, while the library shows the Steam icon. This happens only if only the starter was learned; normally the larger Shipping EXE is learned too and wins.
   - Fix: build the shortcut icon from the library's choice off the UI thread (a `.ico` or `.exe` path can be used directly). Otherwise use `ResolveWithIcon` so default icons are skipped.

4. **Low – a remembered launcher path skips the UNC check.** `src/HdrPilot.App/Services/GameIconService.cs:415-417`
   - For a remembered launcher choice, `IsAllowedSource` only checks that the path is in `game.IconCandidates`, not `IconFiles.IsLocalPath`.
   - Scenario: a hand-edited `config.json` and `icon-cache.json` with a `\\server\x.ico` hint and a matching key make `_fileStamp` and `_read` open the UNC path (SMB access). This breaks the documented promise that a hint never leads to another computer.
   - Fix: also require `IconFiles.IsReadableCandidate(path)` for launcher-origin paths.

5. **Low – no test covers the right-click menu on list rows.** `src/HdrPilot.App/MainWindow.xaml:37-40` and `tools/gates/phase8-icons.ps1:29-35`
   - The gate only opens the menu through `DetailIconButton`. If the row style lost its context menu, no gate or unit test would notice.
   - Fix: add a small UI test that checks the `GameList` row style sets a `ContextMenu` containing `ChangeIconMenuItem` and `ResetIconMenuItem`.

Files are in <Notizordner>\icons-review-decisions:
- mutate.log
- p8.txt

