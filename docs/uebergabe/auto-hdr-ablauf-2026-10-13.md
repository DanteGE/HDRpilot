# Auto HDR (Phase 9): Berichte des Ablaufs nach dem Livetest (2026-10-12/13), unverändert übernommen

## adapt

Done. I can't finish all of Phase 9 alone: decisions 4 and 6 (G9.7, G9.9) are for the next agent, and the final live test G9.8 needs you at the real PC. I did not shut down the PC; that should happen once at the very end of the whole run.

Worktree: `.worktrees\s10-autohdr`, branch `feature/autohdr`. All allowed gates passed; the full test suite passed on its third run (see Open points). GATES.md and docs/stand.md are not edited.

**Commits**
- `c1dad78` Core and UI rework: decisions 1, 2, 3.
- `459a2a0` Gates for G9.1, G9.3 and G9.4.
- `43c1bfe` README and decision E88 (decision 5; E88 kept CRLF).
- `b69a0f4` Extra test found by the mutation checks, plus a doc comment.

**WIP 282e92f**
- It did not compile (a missing `using`).
- Its content was status text per EXE and integration tests for the per-EXE path. Decision 2 made both obsolete, so it is deleted and replaced.

**What was removed**
- **Core, per-EXE path:**
  - Deleted: `AutoHdrRegistryEngine.cs`, `AutoHdrTargets.cs`, the state models, `autohdr-state.json` and `AppPaths.AutoHdrStateFile`.
  - Also gone: the setting `AutoHdrGlobalFallback`, `PerGameOn`, and the per-EXE half of the uninstall cleanup.
  - The old interface parts are gone too: `GetStatus`, `Changed`, `RollbackAllAsync` and the four status/result records.
- **Library UI:**
  - The per-EXE status line `DetailAutoHdrStatus` and its view-model and context plumbing.
  - `LibraryText.Executables` and the resx entries `Count_Exe*` and `Details_AutoHdrStatus*`.
- **Settings:**
  - The section with the fallback switch, the button "Auto-HDR-Einträge zurücknehmen" and its result message.
  - The matching view-model members, `IShellContext.AutoHdr` and the ShellStrings `AutoHdr*` entries.
- **Tests and gate:** the per-EXE engine and target tests, `FakeAutoHdrService`, the WIP integration tests and the gate's `-UiOnly` mode.
- **Docs:** the README and E88 text about the per-EXE path.

**Final mechanism**
- **When the switch is set:** only for starts HDRpilot knows in advance.
  - The session logic announces each start as its very first step, before HDR is switched and before the start command.
  - This covers "Mit HDR starten" and the "(HDR)" shortcuts, the Steam launch option wrapper, and the Steam/Epic early warning.
  - For early-warning games without "HDR automatisch", the switch is set but HDR is not.
  - Only the `AutoHDREnable` token is changed to `1`; other tokens, their order and the trailing `;` stay. Values in an unknown format are never touched.
  - A game that is only detected, without an announced start, never sets the switch and never keeps it on.
- **How long it stays on:**
  - The announcement stays valid while the HDR session waits for the game process; that wait includes the extension for launcher windows.
  - Without an HDR session it lasts until the start timeout. If the waiting session gives up, the switch is restored on the next cycle.
  - After the game ends: the grace period, or the restart buffer if the game ran under 5 minutes. A restart in that time keeps it on.
  - Several games share the switch; it goes back after the last one ends.
  - Turning the option off or removing the game restores it at once. Exiting HDRpilot also restores it.
- **Crash recovery:**
  - `autohdr-global.json` is written before the registry and deleted after the restore.
  - On the next start, still-running games with the option are taken over; otherwise the switch is restored right away.
  - Uninstall restores a switch a crash left on (step "Auto HDR zurückstellen").
- **Restore rules:**
  - If the text is exactly what HDRpilot wrote, the old value comes back (deleted if it was missing).
  - If only other tokens changed, only `AutoHDREnable` goes back.
  - If the user changed the token in Windows, it is left as is ("extern geändert").
  - If the switch was already on before, nothing is written and nothing is turned off. Every new start reads the switch again.
- **Hook for the tray entry (G9.9):**
  - `ReadGlobalSwitch()` and `SetGlobalSwitchByUser(bool)` on `IAutoHdrService`, reachable through `AppServices.AutoHdr`.
  - A change there counts as your choice: HDRpilot gives up the switch, restores nothing afterwards, and does not set it again until the running sessions end.

**Mutation checks** (each applied, filtered tests run, restored; worktree clean afterwards)

| # | Behaviour broken | Result |
|---|---|---|
| M01 | A game detected without an announced start holds the switch | Red (1 test); first run was green, hidden by the idle shortcut, so I added a test |
| M02 | An announced game without the option counts | Green: equivalent, because a later check already drops such games |
| M03 | No restart buffer | Red (1) |
| M04 | An announcement never expires | Red (1) |
| M05 | A waiting HDR session does not keep the announcement alive | Red (6) |
| M06 | A switch that was already on gets written anyway | Red (6) |
| M07 | Your change in Windows is overwritten | Red (6) |
| M08 | No restore at restart after a crash | Red (2) |
| M09 | Switched on in the tray during a session, but turned off afterwards | Red (1) |
| M10 | Switched off in the tray, but set again during the session | Red (1) |
| M11 | The start paths do not announce | Red (9) |
| M12 | A normal start takes over running games without a marker | Red (1) |
| M13 | Uninstall skips the registry key check | Red (1) |
| M14 | Option off and on again sets the switch without a new start | Red (1) |
| M15 | Uninstall keeps the marker file | Red (2) |
| M16 | The announcement comes after the HDR switch | Red (5) |

**Markers** (all exit 0)

| Check | Marker |
|---|---|
| build.ps1 | `BUILD_OK_0_WARNINGS` |
| test.ps1 (full) | `TESTS_OK 2359` |
| `-Filter AutoHdr -MinTests 25` | `TESTS_OK 166` |
| `-Filter AutoHdrGlobal -MinTests 10` | `TESTS_OK 61` |
| `-Filter AntiCheat` | `TESTS_OK 22` |
| phase9-autohdr-ui.ps1 | `PHASE9_AUTOHDR_UI_OK` |
| phase4-resources.ps1 | `PHASE4_RESOURCES_OK` |
| phase6-name.ps1 | `PHASE6_NAME_OK` |
| phase5-readme.ps1 | `PHASE5_README_OK` |
| phase4-ui.ps1 | `PHASE4_UI_OK`, `PHASE4_PERSIST_OK` |
| phase6-ui.ps1 | `PHASE6_UI_OK` |
| phase8-icons.ps1 | `PHASE8_ICONS_OK` |
| phase2-scanner-tests.ps1 | `TESTS_OK 450`, `PHASE2_SCANNER_TESTS_OK` |
| phase3-idle.ps1 | `PHASE3_IDLE_OK` (0.312 % of one core) |
| phase7-setup.ps1 | `PHASE7_SETUP_OK` |
| phase7-setup-ui.ps1 | `PHASE7_SETUP_UI_OK` |

**What the gates now prove**
- **phase9-autohdr-ui:**
  - G9.1 UI checks, and that toggling the switch alone writes nothing to the test root.
  - "Mit HDR starten" launches a harmless probe exe that only reads the test root. It saw `AutoHDREnable=1` at its own process start, and the log shows the switch set before the start command.
  - After the game and grace period the value is back exactly; the real key is byte for byte unchanged.
  - HDR was never switched: the game and global setting point to a display that doesn't exist, and the log has no HDR switch at all.
- **phase7-setup:** the running app writes nothing without a start. A simulated crash is restored by uninstall (d) and (f), and by the app itself at restart.
- **phase7-setup-ui:** the progress list shows "Auto HDR zurückstellen" and the value is back afterwards.

**Open points**
- **Flaky test:** `TrayServiceTests.Many_state_changes_do_not_leak_gdi_or_user_handles` ("USER-Objekte wachsen") failed in 2 of 4 full runs this session.
  - The third full run here passed, and the class passes alone (`TESTS_OK 10`).
  - It is a known pre-existing flaky test; I left it unchanged.
- **For G9.7:** the only existing Steam launch option feature on this branch is "copy to clipboard"; the uninstall scanner only reads `localconfig.vdf`. There is no feature that writes the option with "Steam must be closed".
  - The next agent needs to decide how the offer works.
  - The natural place to hook it is the `AutoHdr` setter in `GameDetailsViewModel`.
- **For G9.9:** `ITrayHost` needs the new members that pass through to `AppServices.AutoHdr`.
- **Known limit:** if you turn Auto HDR off and straight back on in Windows while HDRpilot holds it, HDRpilot can't tell and will turn it off after the game. This is documented in the README and E88.
- **To do outside this agent:**
  - Fill in the GATES.md evidence for G9.1, G9.3, G9.4 and G9.5.
  - G9.8 is a live test with you after installing with your consent.
  - `docs/uebergabe/auto-hdr-entwurf.md` still describes the old design; I left it as history.

The gate outputs and mutation logs are in `<Notizordner>\autohdr`:
- `v-*.out` and `v4-*.out`
- `mut2\results-run1.txt`, `mut2\results-run2.txt`, `mut2\results-run3.txt`

## steam

I finished G9.7 and G9.9 on `feature/autohdr` and committed them, but I can't finish all of Phase 9 alone, so I did not shut down the PC. Two things need you: the final live test G9.8 at the real PC, and the tray gate `phase6-traymenu.ps1`, which moves the real mouse and needs your consent. One more thing needs your decision: HDRpilot has no way to write the Steam launch option, so the G9.7 offer copies it instead (details below).

The full test suite is not fully green: `TrayServiceTests.Many_state_changes_do_not_leak_gdi_or_user_handles` ("GDI-Objekte wachsen") failed in all 4 full runs. The same test also fails on the commit before my changes (`b69a0f4`) when the Shell tests run together, so it is not caused by this work. It passes on its own (`TESTS_OK 10`). Without that one test, the full suite passes: `TESTS_OK 2379`.

Worktree: `.worktrees\s10-autohdr`, clean. GATES.md and docs/stand.md are not edited.

**Commits**
- `d00ac64` Offer the Steam launch option when Auto HDR is switched on (G9.7).
- `28fbd4d` Tray entry "Auto HDR" (G9.9), its tests and the `phase6-traymenu.ps1` edit.
- `3d703ee` README and E88 for both (E88 kept CRLF).

**G9.7: how the Steam offer works**
- **No setter exists:** HDRpilot has no function that writes the Steam launch option. The only existing feature is "Steam-Startoption kopieren" (copy to clipboard). The uninstall scanner only reads `localconfig.vdf`. So there is no "Steam must be closed" condition anywhere to reuse.
- **What I built:** the offer reuses the copy feature. HDRpilot never writes Steam's files.
- **Your decision:** setting the option directly in Steam would be a new feature that writes into Steam's `localconfig.vdf` (with Steam closed). That is your call.
- **When it asks:** after you switch Auto HDR on for a Steam game and the setting has saved, HDRpilot reads Steam's `localconfig.vdf` (all accounts, read-only, off the UI thread). If no launch option with HDRpilot's EXE name and `--wrap` is found, or Steam can't be read, it asks "Steam-Startoption kopieren?".
- **If you confirm:** it copies the option the same way as the existing button, with the same conditions (program path known, clipboard free). The result appears under "BILD" (`DetailAutoHdrMessage`).
- **If you decline, or copying fails:** nothing changes and Auto HDR stays on.
- **No question:**
  - for non-Steam games, or when switching off;
  - when the option is already set, or the save failed;
  - when the switch or the selected game changed while Steam was being read;
  - never two dialogs at once.
- **Not unit-tested:** the small app-side wrapper that reads the real Steam path from the registry. The checking logic behind it is tested against a temp folder.

**G9.9: tray entry "Auto HDR"**
- **Where:** a switch entry directly below "HDR jetzt …" (`TrayAutoHdr`).
- **What it shows:** Windows' global Auto HDR switch, read once when the menu opens. Nothing runs in the background.
- **What it does:** toggles only the `AutoHDREnable` token; other tokens stay.
- **Tooltip:** "gilt ab dem nächsten Spielstart, auch ohne HDRpilot".
- **Unreadable or unknown format:** the entry is disabled, the arrow keys skip it, and the tooltip says why.
- **Your choice wins:** a change here is treated as yours. An Auto HDR session never turns it back after the game, and if you switched it on before a start, HDRpilot never turns it off.
- **Keyboard:** unchanged. The version line is skipped, Down reaches "HDR jetzt …" first, and Up from the start reaches "HDRpilot beenden".
- **Gate edit (not run):** `phase6-traymenu.ps1` now seeds a test root (`--autohdr-test-root Phase6Tray-<PID>`). In step 7 it checks that the entry exists, is enabled, shows On, and sits between "HDR jetzt …" and "Automatik pausieren". It also checks that the test root and the real value are unchanged, and deletes the test root afterwards. The parser reports 0 errors, the BOM is kept and line endings stay LF.
- **Not unit-tested:** the two pass-through lines in `AppServices.Shell.cs`. Only `phase6-traymenu.ps1` covers them, and only on the real desktop.

**Markers** (all exit 0 unless noted)

| Check | Marker |
|---|---|
| build.ps1 | `BUILD_OK_0_WARNINGS` |
| test.ps1 (full), 4 runs | exit 1: 2379 of 2380 passed, the known leak test failed |
| full without the leak test | `TESTS_OK 2379` |
| `-Filter AutoHdrSteam -MinTests 4` | `TESTS_OK 12` |
| `-Filter AutoHdrTray -MinTests 5` | `TESTS_OK 9` |
| `-Filter AutoHdr -MinTests 25` | `TESTS_OK 187` |
| `-Filter AutoHdrGlobal` | `TESTS_OK 61` |
| `-Filter TrayMenu` | `TESTS_OK 2` |
| `-Filter AppVersion` | `TESTS_OK 13` |
| `-Filter AntiCheat` | `TESTS_OK 22` |
| phase9-autohdr-ui | `PHASE9_AUTOHDR_UI_OK` |
| phase4-resources | `PHASE4_RESOURCES_OK` |
| phase5-readme | `PHASE5_README_OK` |
| phase6-name | `PHASE6_NAME_OK` |
| phase6-ui | `PHASE6_UI_OK` |

**Mutation checks:** all 20 turned the filtered tests red. Each was applied, the filtered tests were run, then the file was restored.

| # | Behaviour broken | Failing tests |
|---|---|---|
| S01 | Offer also when switching off | 2 |
| S02 | Offer for every source | 4 |
| S03 | Launch option already set is ignored | 1 |
| S04 | Declining copies anyway | 1 |
| S05 | Declining switches Auto HDR off | 2 |
| S06 | Offer after a failed save | 1 |
| S07 | Offer after switch off or another game selected | 1 |
| S08 | A second dialog over an open one | 1 |
| S09 | Result shown under Starten instead of BILD | 2 |
| S10 | Any game with the option counts | 1 |
| S11 | Name of the running EXE ignored | 1 |
| T01 | Menu does not read the switch | 7 |
| T02 | An unreadable switch gets written | 2 |
| T03 | State not taken from the result | 1 |
| T04 | Entry never disabled | 2 |
| T05 | A click does not reach Windows | 1 |
| T06 | Tooltip ignores the unreadable state | 1 |
| T07 | Tray change during a session is turned back afterwards | 2 |
| T08 | The session overwrites a tray change | 1 |
| T09 | Entry not directly below "HDR jetzt …" | 1 |

The first T09 run used a stale build left over from T08, because restoring a file kept its old timestamp. I fixed the runner and reran T09 on a fresh build; that rerun is the result shown. The final verification above was also run on a fresh build.

**Changes to existing tests**
- The tray item lists and the keyboard order now include `TrayAutoHdr`.
- The failure test now expects 5 errors instead of 4, because reading Auto HDR at menu open is now reported too.
- `AutoHdrSettingsTests` allows exactly the three new tray text keys and still rejects any other Auto HDR key in ShellStrings.
- `FakeGpuPreferenceStore.cs` is now linked into the App test project.

**Still to do outside this agent**
- Fill in the GATES.md evidence for G9.7 and G9.9.
- Run `phase6-traymenu.ps1` on the real desktop with your consent.
- Run G9.8 with you.

Logs are in `<Notizordner>\autohdr`:
- `verify-final.log` and the `v6-*.out` files
- `mut3\runner.log`, `mut3\runner2.log`, `mut3\runner3.log`

## findings

### Lens registry
Found 5 problems on `feature/autohdr` at 3d703ee under the registry and restore lens: 1 medium, 4 low. None writes outside `DirectXUserGlobalSettings` or reaches the real key from tests or gates. I did not shut down the PC: this was only a review, and G9.8 and `phase6-traymenu.ps1` still need the user at the real PC.

**Checks run in my own detached worktree `rv-registry`** (now removed with `git worktree remove --force`; it was clean before removal)
- `build.ps1`: exit 0, `BUILD_OK_0_WARNINGS`.
- `test.ps1 -Filter "FullyQualifiedName~AutoHdr" -MinTests 25`: exit 0, `TESTS_OK 187` (2 projects, 187 of 187 passed).
- I re-ran three of the claimed mutations and added three of my own. Each was applied, the `AutoHdr` tests run, then restored with `git checkout`. All six turned the tests red:

| Mutation | What it breaks | Failing tests |
|---|---|---|
| M06 | A switch that was already on gets written anyway | 6 (claimed 6) |
| M07 | A change in Windows (R3) is overwritten | 6 (claimed 6) |
| M08 | No restore after a crash | 2 (claimed 2) |
| own X1 | Token-only restore (R2) brings back the whole original | 3 |
| own X2 | `;` is always added at the end | 2 |
| own X3 | A tray change while held keeps the crash-recovery file | 1 |

- I confirmed the findings below with a temporary probe test using the in-memory store, then deleted it.

**Problems**

1. **Medium** – `src/HdrPilot.Core/AutoHdr/AutoHdrGlobalSwitch.cs:116` (`_userChoice = false;` in `OnStartAnnounced`)
   - **Scenario:** game A starts through HDRpilot, so the switch goes from 0 to 1 and HDRpilot holds it. The user turns "Auto HDR" off in the tray. While A still runs, the user starts game B through HDRpilot. The value is `AutoHDREnable=1;SwapEffectUpgradeEnable=1;` again (probe P1). It goes back to 0 only after both games end, and any game started outside HDRpilot in that window gets Auto HDR.
   - **Why it matters:** this breaks the documented promise "setzt ihn bis zum Ende der laufenden Spiele/Sitzungen nicht neu" in `README.md:315`, `docs/entscheidungen.md:730` and the class doc at `AutoHdrGlobalSwitch.cs:25`. The existing test `Switched_off_by_the_user_while_held_is_not_set_again_during_the_session` only covers a start after the session has ended.
   - **Fix (user decides):** either do not clear `_userChoice` while other games are still active, or change the docs to "bis zum nächsten Start über HDRpilot".

2. **Low** – `src/HdrPilot.Core/AutoHdr/AutoHdrGlobalSwitch.cs:367` (`_marker is not null` returns before reading the switch again)
   - **Scenario:** while HDRpilot holds the switch for A, the user turns Auto HDR off in the Windows settings. Starting B through HDRpilot then leaves it off, and HDRpilot still reports it as held with its old recovery file (probe P2).
   - **Why it matters:** the comment at line 111 ("Stand frisch lesen") and E88 at `docs/entscheidungen.md:710` ("jeder neue Start liest den Schalter neu") are wrong whenever the switch is held. It is also the opposite of the tray case in finding 1.
   - **Fix:** use one rule for both. Suggestion: when a start is announced while the switch is held, read it again. If someone else changed it (R3), drop the recovery file and treat it as the user's choice, the same way `SetByUser` does. Otherwise correct the docs.

3. **Low** – `src/HdrPilot.Core/Sessions/SessionManager.cs:471` (`BeginStartAsync` announces before the duplicate check) and `:418-425` (`AnnounceAutoHdrStartAsync` never checks for a running process)
   - **Scenario:** a game started without HDRpilot is running. The user clicks "Mit HDR starten" (the button stays enabled, `GameDetailsViewModel.cs:60`), double-clicks the "(HDR)" shortcut, or an Auto-HDR-only launcher signal arrives late. The global switch is set and held for the whole run plus grace period (probe P3: set when announced, still 1 after 10 minutes, restored only after the end).
   - **Why it matters:** per the live test this has no effect on that game, but every other game started in that time gets Auto HDR.
   - **Fix:** announce only when `RunningGames()` has no process for the key, in both places. The announcement still comes before HDR is switched.

4. **Low** – `src/HdrPilot.Core/AutoHdr/GpuPreferenceText.cs:223-224` (token-only restore with nothing left falls back to the full original)
   - **Scenario:** the original value is `SwapEffectUpgradeEnable=1;`. During the hold, something outside HDRpilot changes it to `AutoHDREnable=1;`. The restore writes `SwapEffectUpgradeEnable=1;`, putting back an entry that was removed (probe P4).
   - **Why it matters:** this contradicts R2, which says everything except the token stays as it is now. It is very unlikely in practice.
   - **Fix:** when nothing is left after removing the token, delete the value. Adjust `Token_only_restore_deletes_when_nothing_is_left_and_the_value_was_missing` to match.

5. **Low** – `src/HdrPilot.Core/AutoHdr/AutoHdrGlobalSwitch.cs:335-336` (`SetByUser`)
   - **Scenario:** the user switches Auto HDR in the tray while HDRpilot holds it, and deleting the recovery file `autohdr-global.json` fails (for example, a virus scanner has it locked). The code clears it in memory anyway, but the file stays. At the next HDRpilot start this counts as a crash leftover. If the value still equals what HDRpilot wrote (the user had turned it on), R1 restores the original and turns off the user's choice.
   - **Fix:** if the delete fails, keep an "abandoned" mark (in memory or as a status in the file) and retry the delete. Recovery must never restore a file marked abandoned.

**Checked, no problem found**
- **Only the token changes:** the `AutoHDREnable` token is changed in place or added at the end. Other tokens, their order, the trailing `;` and the key spelling stay.
- **Odd values are never touched:** HDRpilot leaves alone anything that is not a clean REG_SZ (DWORD, expand string, empty, embedded NUL, `;;`). This holds when setting, in the tray and when restoring.
- **Already on is never turned off:** a switch that is already `1` is never written and never turned off.
- **Crash safety:** the recovery file is saved before the registry write, and a half-written state is resolved correctly.
- **Restore rules:** R1, R2, R3 and "already back to original" behave as documented.
- **Sessions:** several games share the switch, exiting HDRpilot restores it, and the first cycle after a crash takes over still-running games or restores at once.
- **Uninstall:** the restore runs before data is deleted and checks that the recovery file belongs to this key. The value name in that file is checked, so a tampered file cannot point the restore at another value.
- **No values per game EXE:** the only writes or deletes in that registry key are at `AutoHdrGlobalSwitch.cs:316` and `:433`, and `AutoHdrState.cs:198` and `:204`. All four target `DirectXUserGlobalSettings`.
- **Test isolation:**
  - Every test host uses its own temp folder, which maps to a test root, or the in-memory store.
  - The uninstall tests use the in-memory store.
  - The app and CLI with `--config-dir` use a test root.
  - The phase 7 and phase 9 gates and the edited `phase6-traymenu.ps1` only read the real key and compare it before and after.
  - One latent gap: the `AutomationHost` constructor would pick the real key if a test passed the standard folders built from a fake environment without the in-memory store. No test does that today.

### Lens session
The review found 3 low-severity problems; the start paths, restore, tray entry and Steam offer are otherwise correct. I could not finish the rest of Phase 9 alone: G9.8 and running `phase6-traymenu.ps1` need the user at the real PC. I did not shut down the PC; that belongs at the end of the whole run.

**Checks run in my own detached worktree `rv-session` at 3d703ee (now removed)**

| Check | Result |
|---|---|
| `build.ps1` | `BUILD_OK_0_WARNINGS`, exit 0 |
| `test.ps1 -Filter "FullyQualifiedName~AutoHdr" -MinTests 25` | `TESTS_OK 187`, exit 0 |
| `test.ps1 -Filter "FullyQualifiedName~AntiCheat"` | `TESTS_OK 22`, exit 0 |
| `phase3-idle.ps1` | `PHASE3_IDLE_OK` (0.338 % of one core, handles −3), exit 0 |
| `test.ps1` (full, one run) | `TESTS_OK 2380`, exit 0; the tray handle-leak test passed this time |
| Shell tests (`FullyQualifiedName~HdrPilot.App.Tests.Shell`), 3 runs | run 1: exit 1, one failure (see Notes); runs 2 and 3: `TESTS_OK 207` |

**Mutation checks** (each applied, filtered AutoHdr tests run, file restored with `git checkout`, worktree clean afterwards):
- **M16** (announcement moved after the HDR switch in `BeginStartAsync`): red, 5 failures, matches the claim.
- **T07** (`SetByUser` keeps the marker): red, 3 failures (1 Core test, 2 `AutoHdrTray*`). The original edit text was not on record, so I wrote this mutation myself.
- **T08** (`_userChoice` removed from the guard in `Evaluate`): red, 2 failures. Also my own edit.

I confirmed problems 1–3 with 5 temporary tests (`TESTS_OK 5`); they are deleted and not committed.

**Problems**

1. **Low: a start request for a game that is already running sets the switch and holds it.**
   - **Where:** `src/HdrPilot.Core/Sessions/SessionManager.cs:471` announces before checking for a running process. `src/HdrPilot.Core/Launchers/LauncherSignalHandler.cs:106-113` (the Auto-HDR-only early warning) has no running check. `src/HdrPilot.Core/AutoHdr/AutoHdrGlobalSwitch.cs:180` then adopts the running game because an announcement exists.
   - **Scenario (tested):** a game with "Windows Auto HDR" starts without HDRpilot and is detected; the switch correctly stays off. Then any of these sets `AutoHDREnable=1` immediately, and it stays on until the game ends plus the grace period (still on after 300 s):
     - the user clicks "Mit HDR starten" or the "(HDR)" shortcut;
     - a Steam/Epic signal arrives for that game;
     - for a game that also has "HDR automatisch", the duplicate start sends no start command but still writes the switch.
   - **Effect:** the running game gets nothing from it. Every other game started in that time gets Auto HDR. This contradicts E88 ("Ein bloß erkanntes Spiel setzt nichts") and README:324.
   - **Fix:** announce only when the game has no running process: in `BeginStartAsync`, work out `processes` first and call the hook only if `processes.Count == 0`; in `AnnounceAutoHdrStartAsync`, skip when `RunningGames().ContainsKey(gameKey)`. Alternatively, `AutoHdrGlobalSwitch` remembers last cycle's running keys and ignores an announcement for a game that is running but not in `_running`. Add a test.

2. **Low: after a failed launch the switch stays on until the start timeout.**
   - **Where:** `SessionManager.cs:340-347` ends the session as "Start fehlgeschlagen" in the same call; `AutoHdrGlobalSwitch.cs:206-219` expires an announcement only by `LaunchTimeoutSeconds` if it was never seen waiting.
   - **Scenario (tested):** the launcher returns a failure. The switch is still on after 60 s and off only after about 130 s (default 120 s; the setting allows up to 1800 s).
   - **Fix:** tell the hook the start was abandoned, for example a new `OnStartAbandoned(key)` call in the failure branch that removes the announcement and re-evaluates.

3. **Low: a tray change is re-set by a new start while sessions still run, which contradicts the docs.**
   - **Where:** `AutoHdrGlobalSwitch.cs:116` resets `_userChoice` on every announced start.
   - **Scenario (tested):** game A is held; the user unticks "Auto HDR" in the tray, so the value is off and the marker is dropped. While A still runs, the user starts game B through HDRpilot, and the switch is written on again. After B it goes back to off, so the user's choice wins in the end.
   - **Contradiction:** README.md:313-315, E88 (`docs/entscheidungen.md:729-731`) and the class comment at `AutoHdrGlobalSwitch.cs:25` all say HDRpilot does not set it again until the running sessions end. A change made in Windows Settings while HDRpilot holds the switch does block new starts, so the two paths behave differently.
   - **Fix:** decide which behaviour is wanted. Either keep `_userChoice` across announcements while sessions run (remove the reset at line 116), or reword README, E88 and the comment to "except for a new start through HDRpilot".

**Notes (not problems)**
- **Start paths and timing:**
  - "Mit HDR starten", the shortcuts and CLI `run` (all via `LaunchAsync`), the Steam wrapper and the early warning for games with "HDR automatisch" (via `PrepareAsync`) set the switch first, before HDR and before the start command.
  - The early warning for games with only "Windows Auto HDR" sets the switch without switching HDR. When paused, the early warning sets nothing; explicit starts still set it, the same as for HDR.
  - Cycles run regardless of pause, so a held switch is still restored after a session.
- **Restore:** grace period, restart buffer, several games sharing the switch, option off or game removed, app exit (`Shutdown` after the last cycle) and crash recovery are all handled in the code.
- **Tray entry:** a new view model is created on every menu open (`TrayService.cs:435`), so it shows the current state. Only the token changes (`GpuPreferenceText.SetToken`). An unknown value disables the entry. A tray change drops the marker, so it is never restored afterwards. Keyboard order is covered by tests.
- **G9.7 offer:** it reads Steam read-only off the UI thread and only copies to the clipboard; it never writes Steam files. There is no "Steam must be closed" condition to reuse because no feature that writes the launch option exists on any branch (searched with `git grep` across all branches). Setting the option directly in Steam is still the user's decision.
- **Idle and logging:** with nothing announced or held, `OnCycle` returns early without registry or file access, and nothing is logged per cycle. No test touches the real key (fakes or `HKCU\Software\HDRpilot.Tests`).
- **Flaky test:** `TrayMenuKeyboardTests.AppVersion_line_never_gets_the_focus_when_arrowing_through_the_open_tray_menu` failed once ("Menü nicht geöffnet", `TrayMenuKeyboardTests.cs:123`). It is a pre-existing test, but I did not check it on `phase5/release`.

Gate outputs and mutation logs are in `<Notizordner>\autohdr\rvs`.

### Lens ux
Review of `feature/autohdr` (3d703ee), focused on user decisions, UX and docs: I found no high-severity problems. One medium point needs your decision (G9.7), and there are seven low ones. I did not shut down the PC; that belongs at the very end of the whole run.

**Checks run in my own worktree `rv-ux`** (removed afterwards; no commits; temporary test files deleted before removal):

| Check | Result |
|---|---|
| `build.ps1` (run twice) | exit 0, `BUILD_OK_0_WARNINGS` |
| `test.ps1 -Filter "FullyQualifiedName~AutoHdr" -MinTests 25` | exit 0, `TESTS_OK 187` |
| `phase9-autohdr-ui.ps1` | exit 0, `PHASE9_AUTOHDR_UI_OK` |
| Claimed mutation S09 | red, 2 failing tests (matches the claim) |
| Claimed mutation T09 | red, 1 failing test (matches) |
| Claimed mutation T06 | red, 1 failing test (matches) |
| My own mutations U01–U04 (row not subdued, wrong binding for the "HDR automatisch" hint, badge bound to HdrAuto, start hint hidden) | all red |

- **Phase 9 gate in detail:** the probe game saw `'AutoHDREnable=1;SwapEffectUpgradeEnable=1;'` when its process started. The log line "gesetzt" comes before "Start-Befehl ausgeführt" (position 179 vs 1073). The test value was restored afterwards and the real key stayed unchanged. So the gate does prove G9.1 and that the switch is set before the launch.
- **Renders:** I looked at the details pane in light and dark (SDR game, native-HDR Steam game with the result message under BILD) and at the tray menu with Auto HDR on and unreadable, in both themes.
- **Already fine:**
  - BILD sits below AUTOMATIK, with the switch, explanation and the "only via HDRpilot" hint.
  - The "HDR automatisch" hint follows HdrAuto. The native-HDR row is subdued but usable.
  - The list badge is display-only, and each row still has exactly one switch.
  - The tray entry sits directly below "HDR jetzt …", shows the right checked state, is disabled when unreadable, and fits the menu style.
  - No fallback switch or per-EXE leftovers remain in `src`; resx texts are German; README and E88 cover every required point; E88 is still CRLF.

**Problems**

1. **Medium – G9.7 does not do what decision 4 says** (`src/HdrPilot.App/ViewModels/Library/GameDetailsViewModel.cs:497`, `GATES.md` G9.7)
   - The gate says HDRpilot offers to *set* the Steam launch option (e.g. with Steam closed). The code only offers to *copy* it to the clipboard.
   - I confirmed no function that writes the launch option exists on `phase5/release`, `main` or `feedback/steam-wrapper`.
   - Fix: you decide. Either accept the copy version (then the G9.7 wording and evidence must say "kopieren"), or build a new feature that writes `localconfig.vdf` while Steam is closed. Don't mark G9.7 done before that.

2. **Low – a start for a game that is already running turns the switch on and keeps it on** (`src/HdrPilot.Core/Sessions/SessionManager.cs:471`, also `:425`)
   - `BeginStartAsync` announces the start before it checks whether the game is already running.
   - Example: a game is running without HDRpilot, then you click "Mit HDR starten" or the (HDR) shortcut. The switch goes to 1 and stays on for the rest of that session. It does nothing for the running game, but applies to every other game started in that time.
   - I confirmed this with a temporary test (both HdrAuto on and off): after 60 s the value was still `AutoHDREnable=1`.
   - Fix: only announce when the game has no running process. Same check in `AnnounceAutoHdrStartAsync`.

3. **Low – the tray shows a checkmark while HDRpilot only holds the switch temporarily** (`src/HdrPilot.App/ViewModels/Shell/TrayMenuViewModel.cs:94`, `Views/Shell/TrayMenu.xaml:43`)
   - During a session the entry shows ✓ with the tooltip "gilt ab dem nächsten Spielstart, auch ohne HDRpilot", so it looks like a permanent setting. After the game it switches off.
   - To keep it on permanently you would have to switch it off and on again.
   - Fix: pass `IAutoHdrService.HoldsGlobalSwitch` through `ITrayHost` and show a resx hint such as "gerade von HDRpilot für ein Spiel eingeschaltet, danach wieder aus".

4. **Low – README and E88 describe the tray rule too strictly** (`README.md:314-315`, `docs/entscheidungen.md:730`)
   - Both say that after a change in the tray, HDRpilot does not set the switch again until the running games end.
   - In the code, every new announced start resets `_userChoice` (`AutoHdrGlobalSwitch.cs:116`) and sets it again, even while another game is still running.
   - Fix the wording: "…setzt ihn erst beim nächsten Start über HDRpilot wieder".

5. **Low – the README contradicts itself about Steam, and the dialog text repeats it** (`README.md:317-323`, `src/HdrPilot.App/Resources/LibraryStrings.resx:365`)
   - Both say HDRpilot learns of a start in Steam *only* through the launch option. The same README paragraph and the table also count the early warning.
   - Sentences about value format, per-EXE and HKCU are misplaced in the Steam paragraph.
   - `README.md` lines 174, 323 and 614 are longer than the file's usual 120 characters (205, 148, 152).
   - Fix: say "zuverlässig nur über die Steam-Startoption", move the misplaced sentences, and rewrap.

6. **Low – icons undercut the separation from "HDR automatisch"**
   - The list badge uses the sun glyph E706 (`MainWindow.xaml:71`). The "HDR automatisch" filter chip (`:389`) and "HDR jetzt" in the tray use the same glyph.
   - The tray entry uses E790 (`TrayMenu.xaml:49`), which is the glyph for "HDR-fähig" / PCGamingWiki (`MainWindow.xaml:405`, `SettingsWindow.xaml:413`).
   - Result: two different icons for the same feature, each borrowed from another feature. Fix: one icon for Windows Auto HDR that nothing else uses, in both places.

7. **Low – the subdued row is hard to read** (`MainWindow.xaml:763`, opacity 0.55)
   - Measured peak contrast of the explanation text: 2.5:1 in light and 3.2:1 in dark. Normal captions measure 6.8:1 and 7.8:1. In dark, the switch in the On state looks disabled.
   - Greyed out is your decision, but fix: apply the reduced opacity only to the title and switch (or use about 0.7), and keep the explanation at 4.5:1 or better.

8. **Low – the review render in light theme is misleading** (`tests/HdrPilot.App.Tests/Library/AutoHdrLayoutTests.cs:268`)
   - The test renders before the 160 ms switch animation finishes, so in `bibliothek-autohdr-hell.png` switches that are on look off. With a 600 ms wait the light On state renders correctly.
   - Fix: wait for the animation (or turn motion off) before rendering.

9. **Low – old per-EXE wording**
   - The log line at `src/HdrPilot.App/Setup/UninstallEngine.cs:209` ("nicht alle Auto-HDR-Werte sind zurückgenommen") and the doc comment at `Setup/UI/UninstallProgressViewModel.cs:13` still speak of several values, but there is only one global switch now.
   - Fix: "der globale Auto-HDR-Schalter ist nicht zurückgestellt".

10. **Low, optional – a lot of hint text** (`GameDetailsViewModel.cs:154`)
    - The "HDR automatisch ist aus" hint shows for every game with HDR automatisch off, even when Windows Auto HDR is off. With the start hint, that is about 9 lines in BILD for most games.
    - E88 says this is intentional. Only change it if you want: show the hint only when Auto HDR is on.

Logs and images are in `<Notizordner>\autohdr\`:
- `rvux-build.out`, `rvux-build2.out`, `rvux-test.out`, `rvux-phase9.out`, `rvux-dup.out`
- `rvux-mut\runner.log`
- `rvux-render\`, `rvux-render3\`

## fix

**Auto HDR review fixes: committed as fd6440e on `feature/autohdr` (one commit)**

All findings are fixed except two: G9.7 needs your decision, and the optional "less hint text" point I turned down. Build, the full test suite and every gate on the list pass. Each fix was checked by a mutation, and all 19 mutations turned the tests red. The worktree is clean and no `rv-*` worktrees are left.

**Can the rest be finished alone? No.** Three things need you:
- **G9.8:** the final test on the real PC.
- **`phase6-traymenu.ps1`:** I edited it but must never run it, because it moves the real mouse.
- **G9.7:** a decision on its wording (see below).

I did not shut down the PC. This is one step inside a larger run, so saving the state, ending tasks and shutting down belong at the end of the whole run. I did not edit GATES.md or docs/stand.md.

**Registry review**
1. **Medium, a new start sets the switch again after a tray change while a game runs:** I kept the code and corrected README, E88 and the code comments. A new start through HDRpilot sets the switch for its own game and restores your tray choice afterwards. This follows decision 1 (the option is the standard behaviour) and decision 6 (only the restore must keep your choice). If you'd rather have "not until all running games end", that is a one-line change. Mutation M01 red.
2. **Low, a held switch was not read again on a new start:** fixed. A change made in Windows meanwhile now counts as your choice, and the new start remembers that state. M02 red.
3. **Low, a start for a game already running set the switch:** fixed in both start paths. M03 and M04 red.
4. **Low, restoring only the token with nothing left brought back the old value:** fixed; the value is now deleted, as R2 says. M06 red.
5. **Low, a recovery file that cannot be deleted:** fixed. The file is marked as done, deletion is retried every 30 s and on exit, and neither the next start nor the uninstaller ever restores from it. A file that belongs to a new hold is never deleted. M07–M11 red.

**Session review**
- The already-running case and the tray-reset case are the same as registry 3 and 1.
- **Failed start command:** fixed. The switch now goes back at once instead of after the start timeout, unless another game still needs it. M05 red.

**UX review**
1. **Medium, G9.7 says "set" but the code only copies the launch option:** not changed, needs you. No function that writes the Steam launch option exists on any branch, and HDRpilot never writes Steam's files. Either accept the copy version (then G9.7 must say "kopieren"), or order a new feature that writes Steam's `localconfig.vdf` while Steam is closed. G9.7 should not be marked done before that.
2. **Duplicate start:** fixed (registry 3).
3. **Tray checkmark while HDRpilot only holds the switch for a game:** fixed. The tooltip now says it goes off again after the game. M12 red.
4. **README/E88 tray wording:** fixed.
5. **Steam contradiction:** fixed in README and the dialog text ("zuverlässig nur", "sicher nur"). The misplaced sentences moved out of the Steam paragraph, and README lines 174, 323 and 614 are re-wrapped. M17 red.
6. **Icons:** the list badge and the tray entry now share one sparkle icon that nothing else uses. M13a and M13b red.
7. **Faint native-HDR row:** now only the title and switch are faded (0.7); the explanation and hints stay fully readable. M14a and M14b red.
8. **Review image taken mid-animation:** the image test now waits until the switches have finished sliding. M15 red.
9. **Old per-EXE wording in the uninstall log and comment:** fixed. M16 red.
10. **Optional, less hint text:** not changed. E88 and decision 3 keep the hint always visible on purpose.

**Extra fix:** after these changes the existing tray handle-leak test failed in 5 of 7 runs. The cause is in the test: the number of graphics objects dropped during the test and the subtraction wrapped around, so it reported a leak that wasn't there. It now counts the same way as `TrayIconRendererTests`; a diagnostic run showed no growth while the tray icon refreshed. The full test suite and three runs of the tray/shell tests are green since.

**Markers**
| Check | Exit | Marker |
|---|---|---|
| `build.ps1` | 0 | `BUILD_OK_0_WARNINGS` |
| `test.ps1` (full) | 0 | `TESTS_OK 2397` |
| `test.ps1 -Filter AutoHdr -MinTests 25` | 0 | `TESTS_OK 204` |
| `test.ps1 -Filter AutoHdrGlobal -MinTests 10` | 0 | `TESTS_OK 74` |
| `test.ps1 -Filter AutoHdrSteam -MinTests 4` | 0 | `TESTS_OK 12` |
| `test.ps1 -Filter AutoHdrTray -MinTests 5` | 0 | `TESTS_OK 11` |
| `test.ps1 -Filter AntiCheat` | 0 | `TESTS_OK 22` |
| `phase9-autohdr-ui.ps1` | 0 | `PHASE9_AUTOHDR_UI_OK` |
| `phase4-resources.ps1` | 0 | `PHASE4_RESOURCES_OK` |
| `phase6-name.ps1` | 0 | `PHASE6_NAME_OK` |
| `phase5-readme.ps1` | 0 | `PHASE5_README_OK` |
| `phase4-ui.ps1` | 0 | `PHASE4_UI_OK`, `PHASE4_PERSIST_OK` |
| `phase6-ui.ps1` | 0 | `PHASE6_UI_OK` |
| `phase8-icons.ps1` | 0 | `PHASE8_ICONS_OK` |
| `phase2-scanner-tests.ps1` | 0 | `PHASE2_SCANNER_TESTS_OK` (`TESTS_OK 450`) |
| `phase3-idle.ps1` | 0 | `PHASE3_IDLE_OK` (0.208 % of one core, handles −3) |
| `phase7-setup.ps1` | 0 | `PHASE7_SETUP_OK` |
| `phase7-setup-ui.ps1` | 0 | `PHASE7_SETUP_UI_OK` |

Files are in `.worktrees\s10-autohdr`:
- `src\HdrPilot.Core\AutoHdr\AutoHdrGlobalSwitch.cs`
- `src\HdrPilot.Core\Sessions\SessionManager.cs`
- `src\HdrPilot.App\Infrastructure\AppGlyphs.cs` (new)
- `README.md`
- `docs\entscheidungen.md`

Mutation script and log are in `<Notizordner>\autohdr\fix\`:
- `mutate.ps1`
- `mutations.log`

## recheck

**Can the rest be finished alone? No.** Three things still need you: G9.8 (the final test on the real PC), running `phase6-traymenu.ps1` (it moves the real mouse, so I must not run it), and a decision on G9.7. I did not shut down the PC: this re-check is one step inside the larger run, so saving the state, ending tasks and shutting down belong at the end of that run.

I re-checked `feature/autohdr` at fd6440e without changing it. One medium point is still waiting for your decision (G9.7). Two new low findings came up; neither undoes a fix. The worktree `rv-recheck` was clean and is removed, and no `rv-*` worktrees are left.

**Checks run in `rv-recheck`**

| Check | Exit | Marker |
|---|---|---|
| `build.ps1` | 0 | `BUILD_OK_0_WARNINGS` |
| `test.ps1` (full) | 0 | `TESTS_OK 2397` |
| `test.ps1 -Filter AutoHdr -MinTests 25` | 0 | `TESTS_OK 204` |
| `test.ps1 -Filter AutoHdrGlobal -MinTests 10` | 0 | `TESTS_OK 74` |
| `test.ps1 -Filter AutoHdrSteam -MinTests 4` | 0 | `TESTS_OK 12` |
| `test.ps1 -Filter AutoHdrTray -MinTests 5` | 0 | `TESTS_OK 11` |
| `phase9-autohdr-ui.ps1` | 0 | `PHASE9_AUTOHDR_UI_OK` (switch set at log position 179, start command at 1073; real key unchanged) |
| `phase7-setup.ps1` | 0 | `PHASE7_SETUP_OK` (real `UserGpuPreferences` byte-identical) |

**Three of the fix's mutation checks, run again:** all turned the tests red, with the same failing tests as the fix's log.
- **M02** (`CheckHeld` removed): 1 failure, `Changed_in_windows_while_held_is_the_users_choice_and_a_new_start_keeps_it_afterwards`.
- **M05** (`OnStartAbandoned` call removed): 2 failures, `Failed_start_command_takes_the_announcement_back` and `Failed_start_command_restores_the_switch_at_once`.
- **M09** (`RetryDiscard` without the `_marker` guard): 1 failure, `Undeletable_marker_is_deleted_later_but_never_while_a_new_start_holds_the_switch`.

**The earlier findings**
- **Registry 1 (a new start sets the switch again after a tray change):** handled by changing README, E88 and the class comment instead of the code. That fits decisions 1 and 6, and the tray and Windows paths now follow the same rule. You may still want to confirm this behaviour.
- **Fixed, as far as I could check:**
  - Registry 2 (a held switch is read again on a new start).
  - Registry 3 / Session 1 / UX 2 (no announcement for a game that is already running).
  - Registry 4 (token-only restore with nothing left deletes the value).
  - Session 2 (the switch goes back at once after a failed start command).
  - UX 3–9 (tray hint while held, docs, Steam wording, one icon used nowhere else (U+F4A5, present in both icon fonts), fainter title and switch only, image test waits for the animation, uninstall wording).
- **Registry 5 (recovery file that cannot be deleted):** only partly fixed, see problem 3 below.
- **UX 10 (less hint text):** rightly turned down. E88 and decision 3 keep the hint visible on purpose.
- **No regressions found:**
  - Line endings and BOMs are unchanged; E88 is still all CRLF.
  - GATES.md and docs/stand.md were not touched.
  - The tray handle-leak test change only fixes the wrap-around; real growth would still fail it.

**Remaining problems**

1. **Medium (still open, your decision): G9.7 says "set", but the code only copies the Steam launch option.**
   - **Where:** `src/HdrPilot.App/ViewModels/Library/GameDetailsViewModel.cs` (the offer) and the G9.7 wording in GATES.md.
   - **Scenario:** decision 4 says HDRpilot offers to *set* the option. It only offers to copy it to the clipboard, and you enter it in Steam yourself. No function that writes the launch option exists on any branch.
   - **Fix:** either accept the copy version and change G9.7 to "kopieren", or order a new feature that writes `localconfig.vdf` while Steam is closed. G9.7 should not be marked done before that.

2. **Low (new, existed before the fix commit): after an error, the retry sets the switch for a game that is already running.**
   - **Where:** `src/HdrPilot.Core/AutoHdr/AutoHdrGlobalSwitch.cs:414-419` (`Evaluate` calls `Activate` once `_retryAfter` has passed), reached from `OnCycle` at `:265`.
   - **Scenario (confirmed with a temporary probe test, since deleted):**
     - At the announced start, saving the recovery file fails (or reading or writing the registry fails). The game starts with the switch off.
     - The game process appears, so its key moves from `_announced` to `_running`.
     - 30 s later the cycle's retry writes `AutoHDREnable=1` and holds it until the game ends plus the grace period.
     - Probe result: the value was still 1 at 65 s. The running game gains nothing, but every other game started in that time gets Auto HDR. This is the same effect the fix removed for the "already running" case, and it breaks decision 1's "set directly before the start".
   - **Fix:** in `Evaluate`, call `Activate` only if one of the active keys is still in `_announced` (not yet running). Running-only keys should only keep a switch that is already held. Add a test where the game runs, the retry is due, and nothing gets set.

3. **Low (the Registry 5 fix mostly cannot take effect): the "Released" mark cannot be saved when a lock blocks the delete.**
   - **Where:**
     - `AutoHdrGlobalSwitch.cs:556-568` (`DiscardMarker`).
     - `src/HdrPilot.Core/Abstractions/IFileSystem.cs:97` (`File.Move(temp, path, overwrite: true)`).
     - `tests/HdrPilot.Tests/TestSupport/InMemoryFileSystem.cs:70` and `:150` (`FailDeleting` blocks only the delete).
     - E88 at `docs/entscheidungen.md:719-720`.
   - **Scenario:**
     - On NTFS, a lock that blocks the delete also blocks the atomic replace. I tested this on temp files: with the file open by another handle (share Read or ReadWrite), both `DeleteFile` and `MoveFileEx(REPLACE_EXISTING)` failed (error 5). With share Delete, both succeeded.
     - So the file keeps its "Applied" status and only the in-memory retry (every 30 s and at exit) protects you.
     - If the lock outlasts HDRpilot (crash or exit while still locked), the next start or the uninstaller restores by R1. That turns off a tray choice of "on" made while HDRpilot held the switch (confirmed with a probe: the value was back to `AutoHDREnable=0` after restart).
     - The test `Users_choice_with_an_undeletable_marker_is_never_restored_after_a_crash` models a file system where delete fails but writing works, which does not happen on NTFS.
     - E88 claims without condition that the file "steht auf Released, wird nie mehr zurückgestellt, auch nicht vom Setup".
   - **Fix (pick one):**
     - Write the "done" mark to a separate small file (for example `autohdr-global.done` holding the `SetAt` and `Written` of the abandoned marker). A new file is not blocked by the lock on the old one. Recovery and `AutoHdrCleanup` then treat a matching marker as Released.
     - Or narrow E88 and the code comment to "nur wenn sich die Datei überschreiben lässt; sonst bleibt das wiederholte Löschen".
     - Either way, make the fake also fail the atomic write when the delete is blocked.

Gate outputs, the probe and mutation logs are in `<Notizordner>\autohdr\rc\`:
- `build.out`, `test-full.out`, `test-autohdr.out`, `test-global.out`, `test-steam.out`, `test-tray.out`, `phase9.out`, `phase7.out`, `probe.out`
- `mutate-rc.ps1`, `mutations-rc.log`


## Letzte Korrekturen (Wiederholversuch, Kennzeichen erledigt)

Both findings were real. Both are fixed, and every requested gate passed after the last code change. The work is committed as **bd1a507** on feature/autohdr, on top of fd6440e, and the worktree is clean.

**Finding 2 (retry turned the switch on for a game already running): confirmed and fixed.**
- **Bug:** `Evaluate` called `Activate` for any active key once the 30 s retry wait had passed, including games that were only running.
- **Fix:** `Activate` is now only reached if at least one active key is still announced (not yet running). A game that is only running can keep a switch HDRpilot already holds, but never causes a new set. The three "neuer Versuch in 30 s" warnings now add "falls der Start dann noch aussteht".

**Finding 3 (the "Released" mark could not be saved while a lock blocked deleting the marker): confirmed and fixed.**
- **Bug:** the same lock that blocks the delete also blocks the atomic replace, so the "Released" status was never written. The marker stayed "Applied", and the next start or the uninstaller would turn off a tray "on" choice.
- **Fix:** the "done" mark now goes into a separate file, `autohdr-global.done`, holding `SetAt` and `Written` of the abandoned marker.
- **App start and setup cleanup:** both treat a marker that matches the done file as finished. They never restore it and try to delete both files.
- **New behaviour to know about:**
  - A done file from an earlier set doesn't match a newer marker (different time).
  - The marker file is never deleted while a new start holds the switch.
  - A leftover done file with no marker is removed in the first cycle.
  - If the done file can't be read, nothing is touched: the app leaves Auto HDR off for that run, and the setup reports the AutoHdrNotRestored warning.
  - The "Released" status is still read from older files, but no longer written.
- **Fake file system:** when deleting is blocked, replacing the existing file now fails too, as on NTFS.
- **Docs:** E88 is corrected (CRLF kept), and the code comments are updated.
- **README:** I also changed it, which you didn't ask for. `phase5-readme` requires every file in `AppPaths` to be listed, so it now has a table row for `autohdr-global.done` and a mention in the uninstall paragraph.

**Changed files:**
- `src/HdrPilot.Core/AutoHdr/AutoHdrGlobalSwitch.cs`
- `src/HdrPilot.Core/AutoHdr/AutoHdrState.cs`
- `src/HdrPilot.Core/AutoHdr/AutoHdrCleanup.cs`
- `src/HdrPilot.Core/AutoHdr/AutoHdrService.cs`
- `src/HdrPilot.Core/Config/AppPaths.cs`
- `tests/HdrPilot.Tests/TestSupport/InMemoryFileSystem.cs`
- `tests/HdrPilot.Tests/AutoHdr/AutoHdrHarness.cs`
- `tests/HdrPilot.Tests/AutoHdr/AutoHdrGlobalSwitchTests.cs`
- `tests/HdrPilot.Tests/AutoHdr/AutoHdrGlobalUserSwitchTests.cs`
- `tests/HdrPilot.App.Tests/Setup/Engine/SetupUninstallAutoHdrTests.cs`
- `docs/entscheidungen.md`
- `README.md`

**Mutation checks:** every mutation turned its filtered tests red, and every file was restored with a matching hash.

| # | Mutation | Red tests |
|---|---|---|
| M1 | The old retry behaviour (any active key may trigger a set) | `Retry_after_a_failure_never_sets_the_switch_once_the_game_runs`: 3 of 3 (marker, read and write failures) |
| M2a | The done file is never written | `Users_choice_with_a_locked_marker_is_never_restored_after_hdrpilot_ends`: 1 of 1 |
| M2b | App start ignores the done file | same test, 1 of 1 |
| M2c | The old approach (save "Released" into the marker) | same test, 1 of 1 |
| M3 | Matching ignores `SetAt` | `Old_done_mark_does_not_protect_a_new_marker_after_a_crash` and `Done_mark_of_an_earlier_marker_does_not_stop_the_restore`: 2 of 2 |
| M4 | Setup cleanup ignores the done file | `Marker_with_a_matching_done_mark_is_only_deleted_and_never_restored`: 1 of 1 |
| M5 | Tidy deletes the marker even while the switch is held | `New_start_while_the_marker_is_locked_sets_nothing_and_a_later_one_holds_with_its_own_marker`: 1 of 1 |
| M6 | Fake file system no longer blocks replace when delete is locked | same test, 1 of 1 |
| M7 | A leftover done file at start is ignored | `Leftover_done_mark_without_a_marker_is_deleted_in_the_first_cycle`: 1 of 1 |
| M8 | Over-broad fix (no retry while any game runs) | `Retry_while_one_game_runs_still_sets_for_a_start_that_is_still_announced`: 1 of 1 |

**Gate markers** (all exit 0):
- build.ps1: `BUILD_OK_0_WARNINGS`
- full test.ps1: `TESTS_OK 2408`
- `-Filter AutoHdr -MinTests 25`: `TESTS_OK 215`
- `-Filter AutoHdrGlobal -MinTests 10`: `TESTS_OK 82`
- `-Filter AutoHdrTray -MinTests 5`: `TESTS_OK 11`
- `-Filter AutoHdrSteam -MinTests 4`: `TESTS_OK 12`
- phase9-autohdr-ui.ps1: `PHASE9_AUTOHDR_UI_OK`
- phase7-setup.ps1: `PHASE7_SETUP_OK`
- phase7-setup-ui.ps1: `PHASE7_SETUP_UI_OK`
- phase5-readme.ps1: `PHASE5_README_OK`
- phase4-resources.ps1: `PHASE4_RESOURCES_OK`, plus an informational "PRÜFEN" line about `src\HdrPilot.App\Setup\UI\SetupRunner.cs:38`, a file this change doesn't touch.
- phase3-idle.ps1: `PHASE3_IDLE_OK`

The commit message file is `%TEMP%\hp-msg.txt` (UTF-8 without BOM, ends with the Co-Authored-By line).

## Gegenprüfung der letzten Korrekturen

Both findings are fixed in the code paths they named. Two smaller problems remain, both in finding 3's area. HEAD of feature/autohdr is **bd1a507** (the fix commit on top of fd6440e), and that is what I checked. All requested gates passed and both mutation checks I re-ran turned red as reported.

**Finding 2 (retry turned the switch on for a running game): fixed.**
- `AutoHdrGlobalSwitch.cs:440-449` now calls `Activate` only if an active key is still announced. A game that is only running can keep a switch HDRpilot already holds, but never causes a new set.
- I checked every path that calls `Evaluate`: OnCycle (a game moves from announced to running in the same cycle, before `Evaluate`), OnStartAbandoned, OnConfigChanged, OnRecovery (switch already held, so no new set), the restart buffer (a new announcement sets, a restart without one does not), and the reset of the "user choice" and "already on" flags on each new start. No regression found.

**Finding 3 (the "done" mark blocked by a lock): fixed for the app and for uninstall without "delete data".**
- Confirmed on real NTFS with .NET 10.0.12: while a handle without FILE_SHARE_DELETE is open, deleting the marker fails with IOException and replacing it fails with UnauthorizedAccessException. Both are caught, and the fake file system's new behaviour matches this.
- The rest of the new logic holds up: marker deleted before the done file, the marker never deleted while a new start holds the switch, matching on SetAt plus Written, a leftover done file removed in the first cycle, and an unreadable done file making the app leave Auto HDR off for that run.

**Remaining problems**

1. **LOW: uninstalling with "delete data" deletes the done file but leaves the locked marker.**
   - **Where:** `src/HdrPilot.App/Setup/UninstallEngine.cs:185-189` and `:436` (`Directory.Delete(folder, recursive: true)`), together with `AutoHdrCleanup.cs:86-101`.
   - **Scenario:**
     1. While HDRpilot holds the switch, the user turns Auto HDR on in the tray.
     2. The marker is locked, so the app writes `autohdr-global.done`.
     3. The lock is still there at uninstall. With "delete data", `RestoreAll` matches the done file correctly, but deleting the marker fails, so it saves the done file again.
     4. `RemoveData` then deletes the folder. `Directory.Delete` keeps going after a failed file, so it removes `autohdr-global.done` and everything else and leaves `autohdr-global.json` alone. I confirmed this on .NET 10.
     5. After a reinstall, the app start (or a second cleanup) finds the marker with no done file and turns the user's "on" off.
   - **Probe:** a temporary test with a real `FileStream(FileShare.Read)` lock gave `marker=True done=False value=AutoHDREnable=0;…`.
   - **Context:** this is not a regression against fd6440e, which also left an "Applied" marker here. It is a gap in finding 3's fix.
   - **Fix:** in `RemoveData`, if `autohdr-global.json` still exists after `RestoreAutoHdr`, delete the data folder's contents but keep `autohdr-global.done`. Alternatively, have `AutoHdrCleanup` report that its done file must stay.
   - **Test:** add a SetupUninstallAutoHdrTests case with a real lock and `removeData: true`. After unlocking, `RestoreAll` must leave GlobalOn. Mutation check: a plain `Directory.Delete` turns it red.

2. **VERY LOW: a corrupt done file is handled differently by the app and the setup.**
   - **Where:** `AutoHdrGlobalSwitch.cs:765-767` vs `AutoHdrCleanup.cs:50-55`; `docs/entscheidungen.md:727`.
   - **Scenario:** a matching marker sits next to a corrupt `autohdr-global.done` (for example `{ kaputt`).
     - The app backs the done file up (which deletes it), treats the marker as not done and restores it. That can turn off a tray "on" choice.
     - The setup leaves both files alone and shows a warning.
     - E88:727 says that if the done file can't be read, everything stays as it is.
   - **Probe:** expected `AutoHDREnable=1`, got `AutoHDREnable=0`.
   - **Likelihood:** low, because the save uses WriteThrough, a flush and an atomic move. It needs outside damage to the file.
   - **Fix:** in `IsMarkedDone`, load the done file without a backup (`clock: null`) and treat Corrupt like Unreadable: return null, leave Auto HDR off for that run and touch nothing. That matches the setup and E88. Add a test with a mutation check.

**Gates** (all exit 0):
- build.ps1: `BUILD_OK_0_WARNINGS`
- full test.ps1: `TESTS_OK 2408`
- `-Filter AutoHdr -MinTests 25`: `TESTS_OK 215`
- `-Filter AutoHdrGlobal -MinTests 10`: `TESTS_OK 82`
- phase9-autohdr-ui.ps1: `PHASE9_AUTOHDR_UI_OK`
- phase7-setup.ps1: `PHASE7_SETUP_OK` (it reports the real UserGpuPreferences key byte-identical, all 36 values)

**Mutation checks re-run:**

| # | Mutation | Red tests | Restore |
|---|---|---|---|
| M1 | `?? active[0]` in `Evaluate` (old retry behaviour) | `Retry_after_a_failure_never_sets_the_switch_once_the_game_runs`: 3 of 3 (Lesen, Schreiben, Merker) | SHA-256 1E5081542EA88D72041218372BB433E9F25D21A23CB09DD758A63FFCC821D482 matches |
| M3 | `Matches` ignores SetAt | `Old_done_mark_does_not_protect_a_new_marker_after_a_crash` and `Done_mark_of_an_earlier_marker_does_not_stop_the_restore`: 2 of 2 | SHA-256 0F7279F3CBAAF65B8D19CE5DC33011F814988B579186CEE146E438229CBBD3B5 matches |

**Cleanup:** I deleted the probe tests, `git status` was clean, and I removed the review worktree with `worktree remove --force`. feature/autohdr is unchanged at bd1a50763cb9dedf6de9ed55656c00a55c5b07e2. The NTFS lock experiment is in `<Notizordner>\autohdr\deltest`.

Die beiden danach gemeldeten Randfaelle (defektes Kennzeichen in der App, gesperrter Merker beim Entfernen mit Daten loeschen) wurden mit Commit f81b56d behoben, Tests mit Gegenprobe.
