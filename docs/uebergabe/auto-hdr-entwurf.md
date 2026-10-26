# HDRpilot Phase 9: Windows Auto HDR per game (G9.1 to G9.6, E88): design, skeleton, work split

Branch `feature/autohdr`. Skeleton worktree: `.worktrees\s10-autohdr`.
Skeleton commit: `13ec86b` "Auto HDR: Entwurf und Gerüst (G9)", on top of `b2187c3`. Both leaves start from that commit,
work in parallel, and touch only the files they own (section 15). The gates are in `GATES.md` section "Phase 9" (G9.1
to G9.6). G9.6 is the later experiment with the user and is out of scope.

Skeleton verification on 13ec86b:
- build.ps1 gave BUILD_OK_0_WARNINGS.
- test.ps1 gave TESTS_OK 2210 (base 2193, plus 17 new).
- test.ps1 with filter `AutoHdr` gave TESTS_OK 17. The filters `AutoHdrRegistry` and `AutoHdrGlobal` still count 0.
- phase5-readme.ps1 gave PHASE5_README_OK.
- phase7-setup.ps1 gave PHASE7_SETUP_OK.

## 0. What the feature is (binding user decisions, 2026-10-12)

- **Two different things:**
  - "HDR automatisch" (existing, `GameConfig.HdrAuto`): HDRpilot turns Windows HDR on when the game starts and restores it afterwards.
  - "Windows Auto HDR" (new, `GameConfig.AutoHdr`): Windows converts the SDR picture of a game without its own HDR to HDR.
    HDRpilot only writes the per-game Windows setting.
- **Placement:**
  - Game details pane: a NEW section "BILD" below the section "AUTOMATIK". It holds the switch "Windows Auto HDR"
    (AutomationId `DetailAutoHdr`) and an explanation.
  - Game list: only a small badge "AUTO HDR" next to the game name, for display only. No second switch column.
- **Independent of "HDR automatisch":** the switch can be turned on by itself. When "HDR automatisch" is off, a hint
  below the switch says that Auto HDR only works while Windows HDR is on.
- **Native HDR according to PCGamingWiki** (`HdrSupportStatus.Native` or `Limited`, the same rule as the list badge
  "HDR"/"HDR*"): the row is shown subdued (lower opacity) but stays fully usable (IsEnabled stays true). A hint says the
  game has its own HDR and it is better to turn it on in the game.
- **Two technical ways are both built:**
  - (a) Per-game registry values. Always on while the option is on.
  - (b) Global fallback. A setting, default off, labelled "experimental".
- **Rollback in three places:**
  - The Settings button "Auto-HDR-Einträge zurücknehmen".
  - Uninstall through the setup.
  - Removing a game (it reverts that game's own values).
- **Documentation:** a README section and decision E88. RTX HDR stays out of scope.

Evidence that shaped the restore rule (local read-only readout from the research, 2026-10-10, in
`<Notizordner>\autohdr\gpuprefs.txt`): Windows itself writes tokens into the per-EXE values. Many game EXEs carry
`AppStatus=0;`, `AppStatus=4;` or `AppStatus=4096;` without any user setting. There are also a REG_DWORD
(`GraphicsFeaturesNotificationConfig`) and a bare `0` (ScopeX.exe) in the same key. The global value currently reads
`AutoHDREnable=0;SwapEffectUpgradeEnable=1;`.

## 1. What the skeleton commit already contains

All of the following compiles and passes the gates listed in the header. The engine is a no-op: nothing is written to
the registry yet.

| File | Content |
|---|---|
| `src/HdrPilot.Core/Config/AppConfig.cs` | `GameConfig.AutoHdr` (bool, default false, JSON `autoHdr`) and `AppSettings.AutoHdrGlobalFallback` (bool, default false, JSON `autoHdrGlobalFallback`). Old configs load unchanged; missing properties mean false. |
| `src/HdrPilot.Core/Config/AppPaths.cs` | `AutoHdrStateFile` = `<DataDirectory>\autohdr-state.json` and `AutoHdrGlobalFile` = `<DataDirectory>\autohdr-global.json` |
| `src/HdrPilot.Core/AutoHdr/AutoHdrRegistryLocation.cs` | Where Auto HDR reads and writes (see section 3). FROZEN. |
| `src/HdrPilot.Core/AutoHdr/GpuPreferenceStore.cs` | `GpuPreferenceValueKind`, `GpuPreferenceValue(Kind, Text)`, `IGpuPreferenceStore { KeyName; Read; WriteString; Delete }`, and `RegistryGpuPreferenceStore(location)`, a real implementation that is HKCU only, always at the location's key, refuses embedded NUL, and reads the kind with `GetValueKind` and the text with `DoNotExpandEnvironmentNames` |
| `src/HdrPilot.Core/AutoHdr/AutoHdrContracts.cs` | The frozen UI contract: `IAutoHdrService`, `AutoHdrGameStatus`, `AutoHdrTargetStatus`, `AutoHdrTargetState`, `AutoHdrRollbackResult`. Also the session hooks `IAutoHdrSessionHooks` and `NoAutoHdrSessionHooks`, which CORE owns. |
| `src/HdrPilot.Core/AutoHdr/AutoHdrService.cs` | A no-op facade that implements `IAutoHdrService` and `IAutoHdrSessionHooks`, plus `Start()`, `OnConfigChanged()` and `Shutdown()`. CORE replaces the body. |
| `src/HdrPilot.Core/Sessions/SessionManagerOptions.cs` | `IAutoHdrSessionHooks? AutoHdr { get; init; }`. Null means without Auto HDR, which is what all existing tests use. |
| `src/HdrPilot.Core/Hosting/AutomationHost.cs` | Creates `AutoHdrService` with a `RegistryGpuPreferenceStore` at the resolved location. Both ctors gained optional parameters: short ctor `autoHdrLocation`; full ctor `autoHdrLocation`, `autoHdrStore` (tests pass `FakeGpuPreferenceStore`). Passes `new SessionManagerOptions { AutoHdr = _autoHdr }` to `SessionManager`. Calls `_autoHdr.Start()` in `StartAsync` before `Sessions.InitializeAsync`, `_autoHdr.OnConfigChanged()` in `OnConfigChanged`, and `_autoHdr.Shutdown()` in `DisposeAsync` after `Watcher.StopAsync`. Exposes `IAutoHdrService AutoHdr`. |
| `src/HdrPilot.App/AppArguments.cs` | `--autohdr-test-root <name>`, giving `AutoHdrTestRoot`. It is never forwarded (`ToForwardedArgs` leaves it out) and is not in `HasCommand`. |
| `src/HdrPilot.App/App.xaml.cs` | Normal start and snapshot mode both pass `AutoHdrRegistryLocation.Resolve(paths, AppPaths.Default(SystemEnvironment.Instance), args.AutoHdrTestRoot)`. |
| `src/HdrPilot.App/AppServices.cs` | `public IAutoHdrService AutoHdr => Host.AutoHdr;` |
| `src/HdrPilot.App/Setup/SetupContracts.cs` | `UninstallStep.RestoreAutoHdr` (between `RestoreHdr` and `RemoveFiles`) and `SetupWarning.AutoHdrNotRestored` |
| `src/HdrPilot.App/Setup/UI/SetupText.cs` | Maps the step to `SetupStrings.UninstallStepRestoreAutoHdr` |
| `src/HdrPilot.App/Resources/SetupStrings.resx` | `UninstallStepRestoreAutoHdr` = "Auto-HDR-Einträge zurücknehmen" and `UninstallAutoHdrNotRestored` (result add-on text). The second key is not used yet; CORE uses it. |
| `src/HdrPilot.App/Setup/SetupLocations.cs` | `AutoHdrLocation`: Real for a normal install, `ForConfigDirectory(ConfigDirectoryArgument)` for a test root |
| `README.md` | Rows only, so phase5-readme stays green. Speicherorte: `autohdr-state.json`, `autohdr-global.json`, the registry key. Befehlszeile: `--autohdr-test-root`. |
| `tests/HdrPilot.Tests/TestSupport/FakeGpuPreferenceStore.cs` | In-memory store. `SetString`/`Set` (setup without logging), `GetString`, `Operations` ("set name=value" / "delete name"), `Reads`, `FailWrites`, `FailReads`, `Values` |
| `tests/HdrPilot.Tests/AutoHdr/AutoHdrLocationTests.cs` | 15 tests on location resolution: no test name can escape the root; the host with its own storage uses a test root |
| `tests/HdrPilot.App.Tests/AppArgumentsTests.cs`, `Setup/Engine/SetupLocationsTests.cs`, `Setup/UI/SetupWindowViewTests.cs` | Parsing of the new switch; setup and app agree on the test root; the uninstall progress now has 7 steps |

### Frozen contract (UI codes against it; CORE must not change these signatures or semantics)

```csharp
namespace HdrPilot.Core.AutoHdr;
public enum AutoHdrTargetState { Applied, AlreadyOn, ExternallyChanged, Unsupported, Failed }
public sealed record AutoHdrTargetStatus(string ExePath, AutoHdrTargetState State);
public sealed record AutoHdrGameStatus(IReadOnlyList<AutoHdrTargetStatus> Targets)
{ public static AutoHdrGameStatus None { get; } public int Count(AutoHdrTargetState state); }
public sealed record AutoHdrRollbackResult(int GamesSwitchedOff, int Restored, int LeftAsIs, int Failed, string? Error = null)
{ public bool Success => Error is null && Failed == 0; }
public interface IAutoHdrService
{
    AutoHdrRegistryLocation Location { get; }
    AutoHdrGameStatus GetStatus(string gameKey);          // from memory, never throws, no registry access
    event EventHandler? Changed;                           // any thread; after a reconcile or rollback that changed a status
    Task<AutoHdrRollbackResult> RollbackAllAsync(CancellationToken cancellationToken = default); // never throws
}
public sealed partial record AutoHdrRegistryLocation   // Real, ForTest(name), ForConfigDirectory(dir), IsValidTestName, Resolve(paths, defaultPaths, testName)
{ const RealKeyPath, TestKeyRoot, GlobalValueName; string KeyPath; string? TestName; bool IsTest; string KeyName; }
```

## 2. Terms

- **Target EXE:** a full path of a game EXE for which HDRpilot writes the per-game value (section 5).
- **Value name:** the target EXE path exactly as HDRpilot normalizes it (`PathUtil.Normalize`). Registry value names are case-insensitive.
- **Token:** a `Key=Value` pair inside the REG_SZ. **Our token** is `AutoHDREnable`. The per-game "on" value is `2097`; the global "on" value is `1`.
- **Clean value:** a REG_SZ that passes the parser in section 4. Only clean values (or missing values) are ever read, merged, written or restored.

## 3. Registry location and test roots (implemented in the skeleton)

- **Real:** `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. It is used only by an instance with the default storage
  (`AppPaths.Default`), for both the app and the CLI. Never HKLM, never admin.
- **Test root:** `HKCU\Software\HDRpilot.Tests\AutoHdr\<name>`. The per-EXE values and `DirectXUserGlobalSettings`
  both live directly in that key.
  - With `--config-dir` and `--autohdr-test-root <name>`: that name. The name must match `\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z`.
  - With `--config-dir` but no valid name: the name is derived. It is the first 16 hex characters of SHA-256 over
    UTF-8 of the upper-cased full config dir path, without a trailing separator. This equals `SetupLocations.ForTestRoot`
    and `Phase7Common.ps1 PathHash`.
  - So a test instance can never reach the real key.
  - `--autohdr-test-root` without `--config-dir` is ignored, and the real key is used.
- **CLI:** `hdrpilot watch/run --config-dir X` gets the derived test root automatically through `AutomationHost`'s short ctor.
- **Setup:** `SetupLocations.AutoHdrLocation` is Real for a normal install, and for a test root it is the derived root
  of `<root>\Config`. This is exactly the root the installed test app uses, because the setup starts it with
  `--config-dir` only.
- **State files** record `registryKey` (= `IGpuPreferenceStore.KeyName`). If a state file was written for a different key
  than the current store, the per-game engine and the global fallback stay **inert** for this run: no registry
  reads or writes, one warning in the log, every target reported `Failed`, and `RollbackAllAsync` returns
  `Error = "Zustand gehört zu <key>"`.

## 4. Value format: parsing and merging rules (CORE; pure code, fully unit tested)

**Clean, decided by the suggested `GpuPreferenceText.TryParse(GpuPreferenceValue, out GpuPreferenceTokens)`.** A value is clean only if all of these hold:
1. `Kind == String` (REG_SZ). REG_EXPAND_SZ, MULTI_SZ, DWORD, QWORD, BINARY and Other are never touched.
2. The text is not empty and contains no `\0` (the store keeps embedded NULs), no control character and no whitespace.
3. Split on `;`. If the text ends with `;`, `TrailingSeparator = true` and the final empty piece is dropped. Every
   remaining piece is non-empty and has exactly one `=`. The key is non-empty `[A-Za-z0-9_]+`; the value is non-empty
   with no `=`. So `;;`, `A=1;;B=2;`, `=1;`, `A=;` and `A=1=2;` are not clean.
4. Keys are unique, compared OrdinalIgnoreCase. `A=1;a=2;` is not clean.
5. At least one pair. So `0` (ScopeX.exe), `` and `;` are not clean.

**Serialization:**
- Pairs are joined with `;`, then `;` is appended if `TrailingSeparator` is set.
- Round trip: for every clean text, `ToString(Parse(text)) == text` byte for byte. Test this.

**Merge (on):**
- Set our token. If it exists, its value is replaced in place, keeping the key's casing and position; otherwise it is
  appended at the end. All other pairs keep their order, and the terminator is kept as it was.
- A missing value becomes `AutoHDREnable=2097;` (global: `AutoHDREnable=1;`).
- Per game the value is set to exactly `2097` (user decision). The research's "keep the high bits" idea is not used.

**Already on:**
- If the merge result equals the current text, nothing is written and no state entry is made. The target state is `AlreadyOn`.

**Restore (off), for an entry HDRpilot wrote.** Read the current value, then apply the first rule that matches:
- **R1 (exact):** the current value is clean and its text equals `written`. If the original was Missing, delete the
  value; otherwise write `original.text`.
- **R2 (token level):** the current value is clean, differs from `written`, and its `AutoHDREnable` value still equals
  the value HDRpilot wrote (`2097` or `1`, compared OrdinalIgnoreCase on the key and Ordinal on the value).
  - Set the token back to `originalToken`, or remove it when `originalToken` is null. Everything else stays as it is now.
  - If no pair is left: delete the value if the original was Missing, otherwise write `original.text`.
  - Log "andere Einträge extern geändert, nur AutoHDREnable zurückgenommen".
  - Rationale: Windows itself adds `AppStatus=…` to per-EXE values (see the evidence in section 0). Without R2, switching off would
    silently leave Auto HDR on for every game that ran once. E88 must state this interpretation. HDRpilot never
    discards anything it did not write.
- **R3 (externally changed):** anything else. This covers a missing value, a value that is no longer clean, and our
  token removed or set to another value (for example the user chose "Don't use Auto HDR" in Windows Settings, which
  writes 2096). Leave the value untouched, drop the entry, and log at Information level
  "Auto HDR: <exe> extern geändert (jetzt „…“), HDRpilot lässt den Wert stehen." The words "extern geändert" must appear.

**Table of test cases.** These are the minimum set; each needs a negative check, and `F` stands for the fake store.

| # | Before | Written | Restore given | Result |
|---|---|---|---|---|
| 1 | missing | `AutoHDREnable=2097;` | unchanged | value deleted |
| 2 | `GpuPreference=2;` | `GpuPreference=2;AutoHDREnable=2097;` | unchanged | `GpuPreference=2;` |
| 3 | `AutoHDREnable=2096;` | `AutoHDREnable=2097;` | unchanged | `AutoHDREnable=2096;` |
| 4 | `AutoHDREnable=2097;` | nothing (AlreadyOn) | n/a | no operation at all |
| 5 | `SwapEffectUpgradeEnable=1;AutoHDREnable=2096;GpuPreference=0;` | token replaced in the middle | unchanged | original exactly |
| 6 | `GpuPreference=2` (no `;`) | `GpuPreference=2;AutoHDREnable=2097` | unchanged | `GpuPreference=2` |
| 7 | `autohdrenable=0;` | `autohdrenable=2097;` (casing kept) | unchanged | `autohdrenable=0;` |
| 8 | DWORD 1, REG_EXPAND_SZ, MULTI_SZ, BINARY | none (Unsupported) | n/a | untouched, F.Operations empty |
| 9 | `0`, ``, `;;`, `A=1;;B=2;`, `A=1;a=2;`, `GpuPreference=0;\0xy`, ` A=1;` | none (Unsupported) | n/a | untouched |
| 10 | missing | `AutoHDREnable=2097;` | Windows made it `AutoHDREnable=2097;AppStatus=4;` | `AppStatus=4;` (R2) |
| 11 | `GpuPreference=2;` | … | `GpuPreference=2;AutoHDREnable=2097;AppStatus=0;` | `GpuPreference=2;AppStatus=0;` (R2) |
| 12 | any | … | user set `AutoHDREnable=2096;` | untouched, "extern geändert" (R3) |
| 13 | any | … | value deleted | stays deleted, "extern geändert" (R3) |
| 14 | any | … | value became a DWORD or got a NUL | untouched (R3) |

## 5. Target EXE selection (CORE: `AutoHdrTargets.For(GameConfig game, IReadOnlyList<string>? helperPatterns)`, pure)

1. **Candidates**, in this order: `DetectedExecutables`, `KnownExecutables`, `ExtraExecutables`.
   - Skip blank entries and entries without a path separator, such as bare file names in `ExtraExecutables`.
   - Normalize with `PathUtil.Normalize`. Require `Path.IsPathFullyQualified` and the extension `.exe` (OrdinalIgnoreCase).
   - De-duplicate OrdinalIgnoreCase. Keep the first occurrence, so the order is stable for the status display.
2. **Excluded:**
   - Any candidate matched by `game.ExcludedExecutables`. Use the same rule as `GameMatcher`: an entry with a separator
     is compared with `PathUtil.PathEquals`, otherwise by file name OrdinalIgnoreCase. Copy the small helper; do not
     reference App code.
   - `HelperExecutables.IsHelper(path, cfg.Settings.HelperExecutablePatterns)`: crash reporters, installers,
     redistributables, launcher windows.
   - `IconExcludedExecutables.IsExcluded(path, game.InstallDir)`: anti-cheat bootstrappers (`start_protected_game`,
     `*_BE`, `*EAC*` as a word, BattlEye/EasyAntiCheat folders), EOS bootstrapper, tools and editors, redistributable
     folders.
   - Nothing else. In particular there is no generic "*launcher*" rule: a value on a launcher is harmless clutter,
     while missing the rendering EXE would defeat the feature.
3. **Desired map:** for every game with `AutoHdr == true`, whether installed or not, map each target to its owning game
   keys. One EXE can belong to several games; it stays desired while any of them wants it.
4. **No file system access.** Existence is not checked.

## 6. Per-game engine (CORE: `AutoHdrRegistryEngine`, suggested name)

### State file `autohdr-state.json`

The state file is in the data folder, is machine-local, and is written atomically with
`IFileSystem.WriteAllTextAtomic` using `ConfigJson.Options`. It is deleted when no entries are left.

```json
{
  "version": 1,
  "registryKey": "HKEY_CURRENT_USER\\Software\\Microsoft\\DirectX\\UserGpuPreferences",
  "entries": [
    {
      "valueName": "D:\\Steam-Bibliothek\\steamapps\\common\\Game\\Game.exe",
      "games": [ "Steam:123" ],
      "status": "Applied",
      "original": { "kind": "String", "text": "GpuPreference=2;" },
      "originalToken": null,
      "written": "GpuPreference=2;AutoHDREnable=2097;",
      "windowsBuild": 26300,
      "writtenAt": "2026-10-12T17:00:00+00:00"
    }
  ]
}
```

- `status` is `Pending`, `Applied` or `ExternallyChanged`.
- `original.kind` is `String` or `Missing`. Only clean originals are ever stored; for a clean REG_SZ the raw data is
  exactly its text.
- `originalToken` is the original value of `AutoHDREnable`, for example `"2096"`, or null if the token was absent.
- `windowsBuild` comes from `IAppEnvironment.WindowsBuild`.
- If a different Windows build is found at start, log it as Information ("Windows-Build seit dem Eintragen geändert;
  Format ungeprüft"). Behavior does not change.
- A corrupt or unreadable file is backed up as `autohdr-state.json.defekt-<yyyyMMdd-HHmmss>.bak`. The engine then
  continues with an empty state and logs a warning that earlier entries cannot be restored automatically. Atomic writes
  make this unlikely.

### Write order (crash safety)

1. Read the current value. If it is not clean and not missing, the target is `Unsupported`; log it once per run and stop.
2. Merge. If the result is unchanged, the target is `AlreadyOn` and nothing more happens.
3. Add the entry with `status: Pending` (original, originalToken, written, games) and **save the state file**. If the save fails, write **nothing**, report `Failed`, and log a warning.
4. `WriteString`. If it throws, remove the entry, save, report `Failed`, and retry at the next reconcile.
5. Set `status: Applied` and save. If this save fails, the next start sees current == written and fixes it.

### Reconcile

`Reconcile(cfg, onlyGameKey = null)` runs under the engine lock. It is triggered by `Start()`, by `OnConfigChanged()`
when the desired-map signature changed, and by `OnStartAnnounced(game)` for that game only.

- **Entries no longer desired** (option off, game removed, EXE excluded, helper list changed): restore them with
  R1/R2/R3. A Pending entry is handled like this:
  - current == original: nothing was written; drop the entry.
  - current == written: treat it as Applied and restore.
  - otherwise: R2 if our token is present, else R3.
  - If restore throws, keep the entry and report `Failed`.
- **Desired targets with an entry:**
  - `Applied`:
    - current == written: `Applied`.
    - Clean and our token == `2097`: `Applied`, with no state change. Do not adopt `written` (see R2).
    - Otherwise: set the entry to `ExternallyChanged`, save, and log "extern geändert". It is never rewritten
      automatically; the user can turn the option off and on again to re-apply.
  - `Pending`:
    - current == original: continue with step 3 above (write now).
    - current == written: `Applied`.
    - Otherwise: as for Applied.
  - `ExternallyChanged`: leave it alone; the target state is `ExternallyChanged`.
- **Desired targets without an entry:** the write order above.
- **Change detection:** `OnConfigChanged()` computes a signature of the desired map (sorted value names with their game
  keys). When it equals the last reconciled signature, there is **no registry access at all**. `Start()` always does a
  full reconcile.
- **Idle:** if no game has `AutoHdr` and there is no state file and no global marker, `Start()` costs one `FileExists`
  per file and zero registry access. phase3-idle must stay unaffected.
- **Status map:** after every reconcile, store gameKey → targets with their state in memory. `GetStatus` returns the
  targets of games whose option is on, and `None` otherwise. Raise `Changed` outside the lock when any status changed.

### Logging (German, logger category "AutoHdr")

Use concise lines with game name, EXE, before and after values. Examples:
- „Auto HDR: „{Game}“ – {Exe} eingetragen (AutoHDREnable=2097), vorher „{Before}“.“
- „Auto HDR: {Exe} zurückgenommen (wieder „{Original}“ / gelöscht).“
- „Auto HDR: {Exe} ist kein sauberer REG_SZ ({Kind}), nicht angefasst.“
- the "extern geändert" line from R3.

## 7. Global fallback (CORE: `AutoHdrGlobalFallback`, suggested name)

**Value:**
- `DirectXUserGlobalSettings` in the same key. The token is `AutoHDREnable=1`. Parsing, merging and R1/R2/R3 are the
  same as in section 4, with the value `1`.
- Only that token is set. `SwapEffectUpgradeEnable` is not touched.
- If the current token is already `1`, the fallback does nothing and writes no marker. Global Auto HDR is already on,
  and the user's own setting is never undone.

**Marker `autohdr-global.json`.** It exists only while HDRpilot holds the token. It is written before the registry write,
and deleted after a successful restore or after an R3 decision.

```json
{ "version": 1, "registryKey": "...", "valueName": "DirectXUserGlobalSettings", "status": "Pending|Applied",
  "original": { "kind": "String", "text": "AutoHDREnable=0;SwapEffectUpgradeEnable=1;" }, "originalToken": "0",
  "written": "AutoHDREnable=1;SwapEffectUpgradeEnable=1;", "games": ["Steam:123"], "windowsBuild": 26300, "setAt": "..." }
```

**Activity tracking.** All of it is in memory and fed only by the hooks:
- **Announced starts** (`OnStartAnnounced` for a game with `AutoHdr`): key → time.
  - It expires after `cfg.Settings.LaunchTimeoutSeconds` without a process.
- **Running entries** (`OnCycle`, keys whose game currently has `AutoHdr`): key → FirstSeen, MissingSince, HoldUntil.
  - When the process is gone: `HoldUntil = now + hold`. The hold mirrors `SessionManager.BeginEnding`:
    - If `restartBuffer > grace` and the run was shorter than `SessionManagerOptions.ShortSessionLimit` (5 min):
      `restartBuffer`, which is `cfg.Settings.RestartBufferSeconds` clamped, or else
      `SessionManagerOptions.Default.RestartBuffer` (1:00).
    - Otherwise: `grace` (`GracePeriodSeconds`).
  - Entries adopted at recovery have no FirstSeen and are never short.
  - A restart within the hold clears it.
  - When paused, keys that are not yet tracked are ignored; tracked ones run out normally.
- **Desired:** `cfg.Settings.AutoHdrGlobalFallback`, and at least one announced or running entry whose game still has
  `AutoHdr` (a removed game counts as not having it).

**State machine:**
- **Idle and desired → Activate:**
  1. Read the value.
  2. If it is not clean and not missing: log, mark it unsupported for this run, and do nothing.
  3. If the token is already 1: do nothing.
  4. Otherwise write the marker as Pending, write the registry value, then mark the marker Applied.
  - If the marker save fails, do not write and set a backoff of 30 s.
  - If the registry write fails, delete the marker and set a backoff of 30 s.
- **Active and not desired → Release:** apply R1/R2/R3, then delete the marker.
  - If the restore throws, keep the marker, set a backoff of 30 s, and retry on the next cycle and at the next start.
- **Settings switch turned off while active:** release immediately, from `OnConfigChanged`.
- **Orderly shutdown** (`Shutdown()`): release. HDRpilot cannot track the end afterwards, and the value only matters
  for game processes that start later. It is read at process start, so the running game is unaffected.
- **Crash recovery** (`OnRecovery`, first cycle):
  - Load the marker.
  - If its `registryKey` differs from the current key, stay inert.
  - If the marker exists, the fallback is Active with the marker data. Running games that have `AutoHdr` are adopted
    as running entries (never short).
  - If nothing is desired: release now, with the log reason "Wiederanlauf nach Absturz".
  - A `Pending` marker follows the Pending rules from section 6.
- **Never active while no such game runs.** Test this explicitly, including that the setting is ON while no game runs.

**Logging:**
- „Auto HDR global (Notlösung): AutoHDREnable=1 gesetzt für „{Game}“ ({Trigger}), vorher „{Before}“.“
- „Auto HDR global: zurückgestellt auf „{Original}“ ({Reason}).“
- "extern geändert" lines as in section 4.

## 8. Hooks into session and launch paths (CORE)

All hook calls happen inside `SessionManager` under its lock. The hooks are synchronous, fast, never throw, and never
call back into `SessionManager`. Field: `_autoHdr = _options.AutoHdr ?? NoAutoHdrSessionHooks.Instance`.

- **`BeginStartAsync`** (used by both `LaunchAsync` and `PrepareAsync`): call
  `_autoHdr.OnStartAnnounced(game, trigger, _clock.UtcNow)` as the **first** statement. That is before any HDR switch
  and before `_launcher.Launch(game)`.
  - This covers "Mit HDR starten", shortcuts (`--run`, which goes through `LaunchAsync`), the Steam launch option
    (wrapper → `--prepare` → `PrepareAsync(SteamLaunchOption)`) and the launcher early warning for HdrAuto games
    (`PrepareAsync(LauncherSignal)`).
  - `OnStartAnnounced` also re-checks that game's per-game targets: it writes missing ones and retries failed ones.
- **`InitializeAsync` and the first `UpdateAsync`:** directly after `RecoverCoreAsync`, call
  `_autoHdr.OnRecovery(RunningGames().Keys, cfg, now)`, exactly once.
- **Every later `UpdateAsync`:** after `Learn`, before the HDR session loop, call
  `_autoHdr.OnCycle(running.Keys, cfg, Paused, now)`. `running` includes every matched library game, also those without
  "HDR automatisch". The matcher covers installed games.
- **Early warning for games with `AutoHdr` but without `HdrAuto`:**
  - Add `Task AnnounceAutoHdrStartAsync(string gameKey, SessionTrigger trigger, CancellationToken ct = default)` to
    `SessionManager`. It takes the lock, requires the manager to be initialized, does nothing for an unknown game, and
    only calls `OnStartAnnounced`; it creates no HDR session and switches nothing.
  - Add it to `ILauncherSignalSessions` and `SessionManagerSignalTarget`, and update the test fakes.
  - In `LauncherSignalHandler.HandleAsync`, the `!game.HdrAuto` branch becomes: if `game.AutoHdr` and not paused and
    initialized, call `AnnounceAutoHdrStartAsync(game.Key, SessionTrigger.LauncherSignal)` and return a new result
    `LauncherSignalResult.AutoHdrOnly`. Otherwise return `NotAutomatic` as before. Disabled, Unknown and Paused keep
    their current order and meaning.
- **New EXEs learned:** `RecordDetectedExecutables`, then `Config.Changed`, then `AutomationHost.OnConfigChanged`, then
  `_autoHdr.OnConfigChanged()`, then reconcile. That call runs on the poll thread inside the `SessionManager` lock; the
  engine lock is separate, so there is no deadlock.
- **No other changes to HDR behavior.** All existing SessionManager and LauncherSignal tests must stay green unchanged,
  apart from fakes gaining the new interface member.

## 9. Service facade and threading (CORE: fill `AutoHdrService`)

- `AutoHdrService` composes the engine and the fallback. CORE may change its constructor; only `AutomationHost`
  constructs it. It may also take `SessionManagerOptions` for thresholds, or a clock-based backoff.
- **Locking:** one private lock for the engine and one for the fallback, or one for both. Never hold a lock while
  raising `Changed`.
- **Errors:** every public method catches all exceptions, logs them, and never throws to callers. That includes
  `ConfigService.Changed` subscribers and UI `Task.Run`.
- **Before `Start()` has completed**, `OnConfigChanged()` and the hooks are no-ops.
- **`RollbackAllAsync`** runs on `Task.Run`:
  1. `ConfigService.Update`: set `AutoHdr = false` on every game, counting those that were true. On
     `ConfigSaveException`, return `Error` and do nothing else.
  2. Restore **every** state entry, whether desired or not, with R1/R2/R3, counting `Restored`, `LeftAsIs` (R3 or
     ExternallyChanged) and `Failed` (an exception; the entry is kept). The `Config.Changed` from step 1 may already
     have restored some entries; count those too. Keep a per-rollback tally inside the engine.
  3. Release the global fallback if it is active.
  4. Save the state, raise `Changed`, and return the result.
- **Status:** `GetStatus` from memory; while inert, every target is `Failed`.

## 10. Setup uninstall (CORE)

- **Step:** in `UninstallEngine`, after `RestoreHdr` and before `RemoveFiles`, run `RestoreAutoHdr`.
  - Report the step only when `autohdr-state.json` or `autohdr-global.json` exists in `Locations.DataPaths`.
  - This happens regardless of `RemoveData`.
- **Restore:**
  - Use the store `new RegistryGpuPreferenceStore(Locations.AutoHdrLocation)`. Inject it through a new optional ctor
    parameter `IGpuPreferenceStore? autoHdrStore = null`; tests use `FakeGpuPreferenceStore`.
  - Call a Core helper that needs no config, for example
    `AutoHdrCleanup.RestoreAll(IGpuPreferenceStore store, IFileSystem fs, AppPaths paths, Action<string> log)`
    returning `(Restored, LeftAsIs, Failed)`. It uses the same R1/R2/R3 code as the engine and handles the global marker
    as well.
- **Key check:** the state files' `registryKey` must equal `store.KeyName`. Otherwise skip, keep the files, and add the
  warning `SetupWarning.AutoHdrNotRestored`.
- **Afterwards:**
  - When nothing failed, delete both files. Together with `session-state.json` these are the only data files the
    uninstall changes without the "remove data" box.
  - When something failed, keep the files, add the warning `SetupWarning.AutoHdrNotRestored`, and the uninstall still
    succeeds.
  - In `UninstallProgressViewModel`, mark `RestoreAutoHdr` as failed and append `SetupStrings.UninstallAutoHdrNotRestored`
    to the result text, just as for `HdrNotRestored`.
- **Order matters:** the running app is stopped first (`Stop` over the command path). Its orderly `Shutdown()` has
  already released the global fallback, and the uninstall reverts the per-game entries.

## 11. UI (UI leaf)

### Details pane (`MainWindow.xaml`, `GameDetailsViewModel`)

- **Section:** `<Border x:Name="PictureSection" Style="{StaticResource Lib.Section}">`, placed directly after
  `AutomationSection` and before `ExecutablesSection`. Its title is `Details_SectionPicture` ("BILD").
- **Switch row:** built like the "HDR automatisch" row (label, caption, `Lib.ToggleSwitch`).
  - The CheckBox has AutomationId `DetailAutoHdr`, `AutomationProperties.Name` = `Details_AutoHdr`, and
    `IsChecked="{Binding AutoHdr}"`.
  - The caption is `Details_AutoHdrHint`.
  - The row is subdued (opacity about 0.55) when `IsAutoHdrSubdued`, that is when the PCGW status is Native or
    Limited. The CheckBox stays enabled.
- **Below the row**, as captions:
  - `DetailAutoHdrNativeHint`: visible if Native or Limited. Text `Details_AutoHdrNative` or `Details_AutoHdrNativeLimited`.
  - `DetailAutoHdrNeedsHdrHint`: visible **whenever `Item.HdrAuto` is off**, as decided by the user. Text
    `Details_AutoHdrNeedsHdr`. It updates live when "HDR automatisch" is toggled in the list or in the details.
  - `DetailAutoHdrStatus`: visible when the option is on. Text from `IAutoHdrService.GetStatus` via the context:
    - No targets: `Details_AutoHdrStatusNoExe`.
    - Applied + AlreadyOn > 0: `Details_AutoHdrStatusApplied` with the count text.
    - Append one sentence each for ExternallyChanged, Unsupported and Failed counts.
    - It refreshes on the context's `AutoHdrChanged` and on `Apply(config)`.
- **View model** (`GameDetailsViewModel`): `bool AutoHdr { get; set; }`. The setter writes
  `_owner.WriteGameAsync(Item.Key, g => g.AutoHdr = value)` and sets nothing else; in particular it does not touch
  `HdrAuto`. On a write failure, revert silently, as with the existing pattern. Also provide:
  - `bool IsAutoHdrSubdued`
  - `bool ShowAutoHdrNativeHint`, `string AutoHdrNativeHint`
  - `bool ShowAutoHdrNeedsHdrHint`
  - `string? AutoHdrStatusText`, `bool HasAutoHdrStatus`
  - Hooks into `Apply(GameConfig)` and into `Item.PropertyChanged` for `HdrAuto` and `HdrSupport`.

### List row (`GameRowTemplate`, `GameItemViewModel`)

- **Badge:** on the first line directly after the name (the name trims, the badge stays), a small badge with text
  `Badge_AutoHdr` ("AUTO HDR").
  - The `Border` has AutomationId `AutoHdrBadge` and `AutomationProperties.Name` + ToolTip `Badge_AutoHdrTooltip`.
  - It is visible only if `GameItemViewModel.AutoHdr`, a read-only value from the config updated in `Apply`.
  - Its style must look different from the PCGW badge "HDR" and from the switch; for example an outline badge in the
    accent colour. Add the style to `Themes/Library.xaml`.
- **No other change to the row:** still exactly one CheckBox (`HdrAutoToggle`). No new column, no new header.

### Settings (`SettingsWindow.xaml`, `SettingsViewModel`, `IShellContext`)

- **Section:** `<HeaderedContentControl x:Name="AutoHdrSection" Style="{StaticResource Hp.Shell.Section}" Header="{x:Static res:ShellStrings.SectionAutoHdr}" Tag="<glyph>">`.
  - Placement: the left column after `StartupSection`. Left holds Displays, Automation and Startup; right holds Search,
    Helpers and Advanced, so this balances them.
  - If the existing Shell layout tests require it, the right column after `AdvancedSection` is acceptable instead.
- **Contents:**
  - The intro caption `AutoHdrIntro`.
  - A setting row with the toggle `AutoHdrGlobalFallbackToggle` (label `AutoHdrGlobalFallbackLabel`, caption
    `AutoHdrGlobalFallbackHint`, a warning glyph like the hotkey fallback). It is bound to `AutoHdrGlobalFallback`,
    which joins the Snapshot/Save/Cancel logic.
  - The button `AutoHdrRollbackButton` (`AutoHdrRollbackButton` text, `AutoHdrRollbackHint` caption), which acts
    immediately like "Jetzt abgleichen" and runs `RollbackAutoHdrCommand` (AsyncRelayCommand, disabled while running).
  - The TextBlock `AutoHdrRollbackMessage`, which shows the result: `AutoHdrRollbackDone`, `AutoHdrRollbackNothing`
    (all counts 0), plus `AutoHdrRollbackLeftAsIs`, `AutoHdrRollbackFailed`, or `AutoHdrRollbackError`.
- **No confirm dialog.** The Settings window has no dialog service, and the button text plus caption are explicit.
- **Interfaces:**
  - `IShellContext` gains `IAutoHdrService AutoHdr { get; }`; `AppServices.Shell.cs`'s `ShellHost` implements it with
    `app.AutoHdr`.
  - `ILibraryContext` gains `AutoHdrGameStatus GetAutoHdrStatus(string gameKey)` and `event EventHandler? AutoHdrChanged`
    (UI thread). In `AppLibraryContext`, subscribe to `services.AutoHdr.Changed`, marshal with `RaiseOnUi`, and
    unsubscribe in `Dispose`. `TestLibraryContext` and `FakeShellContext` get a `FakeAutoHdrService` (UI-owned test
    support) that records rollback calls and returns preset statuses and results.

### resx keys

German texts. Keys and AutomationIds are fixed; the UI leaf may polish the wording but must keep the README consistent.

`LibraryStrings.resx`:
| Key | Text |
|---|---|
| Details_SectionPicture | BILD |
| Details_AutoHdr | Windows Auto HDR |
| Details_AutoHdrHint | Windows rechnet das SDR-Bild in HDR um. Nur für Spiele ohne eigenes HDR (DirectX 11/12). Wirkt beim nächsten Start. |
| Details_AutoHdrNeedsHdr | Wirkt nur, solange Windows-HDR an ist. „HDR automatisch“ ist für dieses Spiel aus: HDR selbst einschalten oder „Mit HDR starten“ benutzen. |
| Details_AutoHdrNative | Dieses Spiel hat laut PCGamingWiki eigenes HDR – besser im Spiel einschalten. |
| Details_AutoHdrNativeLimited | Dieses Spiel hat laut PCGamingWiki eingeschränktes eigenes HDR – meist besser im Spiel einschalten. |
| Details_AutoHdrStatusApplied | Für {0} eingetragen. Wirkt ab dem nächsten Spielstart. |
| Details_AutoHdrStatusNoExe | Noch keine Spiel-EXE bekannt: HDRpilot trägt Auto HDR ein, sobald es das Spiel einmal laufen gesehen hat. |
| Details_AutoHdrStatusExternal | {0} außerhalb von HDRpilot geändert: bleibt so. Zum erneuten Eintragen aus- und wieder einschalten. |
| Details_AutoHdrStatusUnsupported | {0} mit Windows-Eintrag in unbekanntem Format: bleibt unverändert. |
| Details_AutoHdrStatusFailed | {0} ließ sich nicht eintragen; neuer Versuch beim nächsten Abgleich (Einzelheiten im Protokoll). |
| Count_ExeOne / Count_ExeMany | 1 EXE-Datei / {0} EXE-Dateien |
| Badge_AutoHdr | AUTO HDR |
| Badge_AutoHdrTooltip | Windows Auto HDR für dieses Spiel an |

`ShellStrings.resx`:
| Key | Text |
|---|---|
| SectionAutoHdr | Windows Auto HDR |
| AutoHdrIntro | „Windows Auto HDR“ schaltest du je Spiel in der Spielebibliothek ein (Abschnitt „Bild“). HDRpilot trägt es dann für die EXE-Dateien des Spiels in den Windows-Grafikeinstellungen ein. |
| AutoHdrGlobalFallbackLabel | Notlösung: Auto HDR global während des Spiels (experimentell) |
| AutoHdrGlobalFallbackHint | Nur falls der Eintrag je Spiel nicht greift: Solange ein Spiel mit „Windows Auto HDR“ läuft, schaltet HDRpilot Auto HDR in Windows für alle Spiele ein und danach wieder zurück. Standard aus. |
| AutoHdrRollbackButton | Auto-HDR-Einträge zurücknehmen |
| AutoHdrRollbackHint | Schaltet „Windows Auto HDR“ bei allen Spielen aus und stellt die Windows-Einträge wieder her, die HDRpilot geschrieben hat. Wirkt sofort. |
| AutoHdrRollbackDone | Zurückgenommen: {0} Einträge; bei {1} Spielen ausgeschaltet. |
| AutoHdrRollbackNothing | Keine Auto-HDR-Einträge von HDRpilot vorhanden. |
| AutoHdrRollbackLeftAsIs | {0} außerhalb von HDRpilot geänderte Einträge bleiben, wie sie sind. |
| AutoHdrRollbackFailed | {0} Einträge ließen sich nicht zurücknehmen; neuer Versuch beim nächsten Start (Einzelheiten im Protokoll). |
| AutoHdrRollbackError | Nicht zurückgenommen: {0} |

Insert entries without converting line endings. `CommonStrings.resx` has mixed endings; the Library and Shell resx files are LF.

## 12. README and E88 (UI leaf)

### README

**New section `## Windows Auto HDR`**, placed after `## HDR-Liste (PCGamingWiki)`, with:
- What it is: Windows converts the SDR picture to HDR.
- A short comparison table:

  | | „HDR automatisch“ | „Windows Auto HDR“ |
  |---|---|---|
  | What it does | HDRpilot switches Windows HDR on at game start and back afterwards | Windows converts SDR to HDR; HDRpilot only writes the per-game Windows setting |
  | Where | section AUTOMATIK + list switch | section BILD + list badge „AUTO HDR“ |
  | Needs | an HDR-capable display | Windows HDR on (use it together with „HDR automatisch“, „Mit HDR starten“ or HDR switched by hand) |

- How it works: the per-EXE value `AutoHDREnable=2097` in `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`.
  Only that token is set. Values Windows writes stay. Which EXEs are used (known game EXEs without helpers, anti-cheat
  starters, crash reporters, installers). The first start of a new game is learned, so it takes effect from the next
  start.
- Restore rules in plain words: HDRpilot only takes back what it wrote. Changes made in Windows stay. Unknown formats
  are never touched.
- The native-HDR hint.
- The experimental fallback:
  - The setting and its default (off).
  - When it is set: for starts via HDRpilot, shortcut, Steam launch option or early warning, it is set before the game;
    otherwise when the game is detected.
  - When it is restored: after the game, including after the restart buffer. Any paragraph in "So funktioniert es",
    "Bekannte Grenzen" or "Fehlersuche" that mentions "Neustart-Puffer" must contain "Standard 1 Minute" (phase5 gate).
  - It is also restored when HDRpilot exits, or at the next start after a crash.
- Rollback: the Settings button, the uninstall, removing a game.

**Additions to other sections:**
- Bedienung/Spielebibliothek: the BILD section and the badge, with bold labels that exactly match resx, for example
  **Windows Auto HDR**.
- The settings table: a row whose first column is exactly `AutoHdrGlobalFallbackLabel`, with default "Aus". The button
  label **Auto-HDR-Einträge zurücknehmen** may appear in bold; it exists in ShellStrings and SetupStrings.
- Entfernen: the uninstall also takes back the Auto HDR values and deletes `autohdr-state.json` and
  `autohdr-global.json`. Extend the "Einzige Ausnahme" sentence in Speicherorte accordingly.
- Bekannte Grenzen:
  - Only DirectX 11/12; DX9, OpenGL and Vulkan are not supported.
  - Only games Windows considers eligible.
  - It takes effect only at the next start.
  - It needs Windows HDR on.
  - The format is undocumented and may change with a Windows update.
  - Per-game on with global off is not yet confirmed on 26H2 (experiment G9.6 pending, and with it the default of the
    fallback).
  - Anti-cheat: HDRpilot only writes HKCU values and never touches the game process. There is no vendor statement.
  - The check is the Game Bar (Win+G) intensity slider.
- RTX HDR is not supported.

**Keep phase5-readme green:**
- Bold labels in Einrichtung, Bedienung and Entfernen must be resx texts.
- Switches and storage locations are already documented by the skeleton.

### E88

Add E88 to `docs/entscheidungen.md`. The file is CRLF; append in the style of E85 to E87, with a bold title such as
"**E88 Windows Auto HDR je Spiel (Wunsch des Nutzers, G9):**". Content:
- The distinction from "HDR automatisch".
- The placement: section BILD, a badge only in the list, no switch column.
- Independence from "HDR automatisch", with the hint.
- The native-HDR rule.
- Both technical ways: per game always; the global fallback experimental and off by default, with the reason that G9.6
  will decide the default.
- The exact token rules and restore rules **including the R2 interpretation**: Windows adds `AppStatus=…` itself, so
  HDRpilot takes back only its own token when everything else changed.
- The state files and the test-root routing (`--autohdr-test-root`, and that `--config-dir` never reaches the real key).
- The uninstall step.
- That the global fallback is restored on HDRpilot exit.
- That RTX HDR stays deferred.

## 13. Gates

### `tools/gates/phase9-autohdr-ui.ps1` (new, UI leaf)

Marker `PHASE9_AUTOHDR_UI_OK`. UTF-8 **with** BOM, because the script contains umlauts. It follows `phase8-icons.ps1`
and `phase6-ui.ps1`, dot-sourcing `Phase4Common.ps1`.

1. Snapshot the **real** key `HKCU:\Software\Microsoft\DirectX\UserGpuPreferences` read-only: value names, kinds and
   data. The script never writes there.
2. Create the test storage:
   - `New-TestConfig 'phase9-autohdr'` (without `-Import`).
   - Three stand-in games via `New-StandInGame` or `games add` with `--hdr off`. They are **never started**: A "Auto
     HDR Test", B "Natives HDR Test", C "Eingeschränktes HDR Test".
   - Write `hdr-support.json` into the storage (`HdrSupportData` format: `"games": { "<keyB>": { "status": "Native" },
     "<keyC>": { "status": "Limited" } }`).
3. Start: `Start-App $cfg @('--show', '--autohdr-test-root', "Phase9Ui-$PID")`.
4. Game A:
   - `DetailAutoHdr` is Off.
   - `DetailAutoHdrNeedsHdrHint` is visible, because HdrAuto is off.
   - There is no `AutoHdrBadge` in A's row.
   - Toggle on: config.json shows `"autoHdr": true` for A and `hdrAuto` still false. The badge appears in A's row and
     only there (negative check: not in B's row). `DetailAutoHdrStatus` is visible.
   - Toggle `DetailHdrAuto` on: the needs-HDR hint disappears and `autoHdr` stays true. Toggle it off again.
     (Negative check: the hints follow HdrAuto, not AutoHdr.)
5. Games B and C:
   - `DetailAutoHdrNativeHint` is visible, with the Native and the Limited text respectively.
   - `DetailAutoHdr.IsEnabled` is true; toggling on then off writes config.
   - Negative check: game A shows no native hint.
6. Placement:
   - `DetailAutoHdr` lies below `DetailHdrAuto` (BoundingRectangle Y).
   - A list row contains exactly one toggle (`HdrAutoToggle`) and no element with AutomationId `DetailAutoHdr`.
   - The list header has no new column.
7. Settings:
   - Open via `SettingsButton`.
   - `AutoHdrGlobalFallbackToggle` is Off.
   - Toggle on and Cancel: config unchanged. Toggle on and Save: `"autoHdrGlobalFallback": true`. Reopen: On.
   - `AutoHdrRollbackButton` is enabled. Invoke it: `AutoHdrRollbackMessage` becomes non-empty.
   - Only a `Note`, not a `Check`, records the config and test-root state after the rollback, because the engine
     behavior is CORE's and is verified by its unit tests and phase7-setup.
8. Exit the app (Stop-Started or `--exit`). Read the test root `HKCU:\Software\HDRpilot.Tests\AutoHdr\Phase9Ui-$PID`
   and report its values as a Note, then delete that key.
9. **Check:** the real key is unchanged byte for byte. Remove the temp storage.

The gate never starts a game, never switches HDR (no running game, so HdrAuto toggles do nothing), and sends no input.

### `tools/gates/phase7-setup.ps1` + `Phase7Common.ps1` (extended by CORE for G9.4)

1. Before the app run in the test root, seed:
   - config.json: one manual game with `"autoHdr": true` and `knownExecutables` = `[<root>\Games\AutoHdr\game.exe]`.
     No real EXE is needed; it is never started.
   - The derived test key `HKCU:\Software\HDRpilot.Tests\AutoHdr\<PathHash(<root>\Config)>` with
     `"<that exe>" = "GpuPreference=2;"` and `DirectXUserGlobalSettings = "AutoHDREnable=0;SwapEffectUpgradeEnable=1;"`.
2. After the installed app has started:
   - Wait for `"GpuPreference=2;AutoHDREnable=2097;"` and `autohdr-state.json`.
   - The global value must be unchanged (fallback off and no game running).
3. After uninstall (both the quiet path and the `--remove-data` path where applicable):
   - The value is back to exactly `"GpuPreference=2;"`.
   - The state files are gone.
   - The uninstall log mentions the step.
4. Check that the real key is unchanged, using a read-only snapshot before and after.
5. Cleanup deletes the test key.
6. Adjust the "Ablage byte-gleich" comparisons consciously. The app now writes `autohdr-state.json` into the storage, and
   the uninstall deletes it. Treat it like `session-state.json`: capture "before" after the app has reconciled, or exclude
   exactly these files with a comment. Do not weaken the other comparisons.

### Who runs what

- **Both leaves:** `build.ps1` (BUILD_OK_0_WARNINGS) and `test.ps1` (TESTS_OK). Known pre-existing flakiness:
  `TrayServiceTests.Many_state_changes_do_not_leak_gdi_or_user_handles` measures process-wide GDI objects and fails
  about 1 run in 3 on the unchanged base (verified on b2187c3). Rerun before blaming your change; never "fix" it by
  weakening it inside this feature.
- **CORE:**
  - `test.ps1 -Filter "FullyQualifiedName~AutoHdrRegistry" -MinTests 15` (G9.2)
  - `-Filter "FullyQualifiedName~AutoHdrGlobal" -MinTests 8` (G9.3)
  - `phase7-setup.ps1` (G9.4)
  - `phase3-idle.ps1`, `phase7-setup-ui.ps1`, `phase2-scanner-tests.ps1`
- **UI:**
  - `test.ps1 -Filter "FullyQualifiedName~AutoHdr" -MinTests 25` (G9.1)
  - `phase9-autohdr-ui.ps1`, `phase4-ui.ps1`, `phase6-ui.ps1`, `phase8-icons.ps1`, `phase4-resources.ps1`,
    `phase6-name.ps1`, `phase5-readme.ps1` (G9.5 part)
- **Forbidden:** the HDR-switching gates listed in the hard rules.

## 14. Test plan

Test classes must contain "AutoHdr". Per-game registry tests must contain "AutoHdrRegistry"; global fallback tests must
contain "AutoHdrGlobal". Every behavior needs a negative check.

**CORE (in `tests/HdrPilot.Tests/AutoHdr/` unless noted):**
- `AutoHdrRegistryTextTests`: clean/unclean parsing (table rows 8 and 9 as a Theory), round trip, merge cases 1 to 7,
  R1/R2/R3 helpers, keys compared case-insensitively. At least 10.
- `AutoHdrRegistryTargetsTests`:
  - Detected/Known/Extra order; dedupe; bare names skipped; non-.exe skipped.
  - Excluded by path and by name.
  - Helper patterns from the config: negative check with a custom list.
  - Icon exclusions such as `start_protected_game.exe`, `*_BE.exe`, an `EasyAntiCheat\` folder, `UnityCrashHandler64.exe`.
  - Games with AutoHdr off contribute nothing.
  - An EXE shared by two games.
  - At least 5.
- `AutoHdrRegistryEngineTests` (FakeGpuPreferenceStore + InMemoryFileSystem + real ConfigService over in-memory files):
  - On, off, the external-change cases, Windows adding AppStatus (R2), Unsupported never touched (`Operations` empty).
  - A failed state save means no registry write.
  - A failed registry write means no entry and `Failed`, then a retry.
  - Pending crash recovery, both ways.
  - Removing a game restores only its values.
  - Two games sharing an EXE.
  - The fingerprint skips registry access (`Reads` unchanged).
  - Idle start: zero reads, no file written.
  - Location mismatch makes the engine inert.
  - Corrupt state file is backed up.
  - Windows build recorded.
  - `GetStatus` values.
  - `Changed` is raised only on change.
  - `RollbackAllAsync`: counts, config switched off, ConfigSaveException returns Error and touches nothing.
  - At least 15.
- `AutoHdrRegistryStoreTests`: `RegistryGpuPreferenceStore` against a **real test key**
  `HKCU\Software\HDRpilot.Tests\AutoHdr\Unit-<guid>`, deleted in Dispose:
  - String round trip; DWORD and ExpandString kinds (create them with `Microsoft.Win32.Registry` inside the test key);
    a missing value and a missing key.
  - Embedded NUL is preserved on read and refused on write.
  - Delete of a missing value.
  - Negative check: the store cannot be built for any key outside Real or TestKeyRoot (only factories exist).
  - At least 3. Never open the real key in tests, not even read-only.
- `AutoHdrGlobalFallbackTests`:
  - The setting off never writes, even while running.
  - It is set on detection and on announce before the process. The order of a LaunchAsync hook call and of an
    `IHdrController.SetHdrAsync` call is observable through fakes.
  - Grace versus restart buffer (short run, restart buffer 0, a restart inside the hold).
  - Launch timeout expires.
  - Already on means no marker.
  - A missing global value means it is deleted on restore.
  - External change while active (R3).
  - Two overlapping games.
  - Settings off releases immediately.
  - Shutdown releases.
  - Crash recovery, both ways.
  - Pending marker.
  - A marker save failure means no write.
  - Backoff after a failure.
  - Paused ignores new detections.
  - A removed game counts as inactive.
  - At least 10.
- `AutoHdrGlobalSessionHookTests` (SessionManager with a recording `IAutoHdrSessionHooks` fake via
  `SessionManagerOptions.AutoHdr`):
  - `OnStartAnnounced` comes before HDR switching and before `IGameLauncher.Launch` for LaunchAsync, PrepareAsync
    (SteamLaunchOption) and PrepareAsync (LauncherSignal).
  - It is not called for an unknown game or a missing launch command.
  - `OnRecovery` exactly once.
  - `OnCycle` includes a running game without HdrAuto.
  - The paused flag is passed.
  - `AnnounceAutoHdrStartAsync` switches no HDR.
  - At least 4.
- `AutoHdrGlobalLauncherSignalTests`:
  - An AutoHdr-only game is announced; HdrAuto games still take PrepareAsync.
  - Paused, disabled and unknown announce nothing.
  - At least 3.
- `tests/HdrPilot.App.Tests/Setup/Engine/SetupUninstallAutoHdrTests.cs` (FakeGpuPreferenceStore and a temp folder,
  like the existing uninstall tests):
  - The step is reported only when the files exist.
  - Values are restored and the files deleted.
  - A failure keeps the files and adds the warning; the uninstall still succeeds.
  - A key mismatch is skipped.
  - The step runs before RemoveFiles and before RemoveData.
  - A progress view-model test for the warning.
  - At least 5.

**UI:**
- `tests/HdrPilot.Tests/Config/AutoHdrConfigTests.cs`:
  - An old config.json without the new properties loads with false, and a round trip keeps the other fields.
  - The JSON names are `autoHdr` and `autoHdrGlobalFallback`.
  - Negative check: true survives a save and load.
  - `Normalize` leaves them alone.
  - At least 4.
- `tests/HdrPilot.App.Tests/Library/AutoHdrLibraryTests.cs`:
  - The switch writes only `AutoHdr`, and HdrAuto does not change AutoHdr (both directions).
  - The badge follows the config.
  - The needs-HDR hint shows if and only if HdrAuto is off, and updates live.
  - The native and limited hints, with subdued true and the switch still enabled.
  - Unknown, ModOnly and None are not subdued.
  - Status text for every state and for combinations.
  - Status refreshes on `AutoHdrChanged`.
  - A write failure reverts the switch.
  - Dispose unsubscribes.
  - At least 12.
- `tests/HdrPilot.App.Tests/Library/AutoHdrLayoutTests.cs`, in the style of MainWindowLayoutTests:
  - PictureSection comes after AutomationSection; `DetailAutoHdr` is inside PictureSection.
  - The row template has exactly one CheckBox; `AutoHdrBadge` is in the row.
  - Every AutomationId exists.
  - All texts come from resx.
  - At least 3.
- `tests/HdrPilot.App.Tests/Shell/AutoHdrSettingsTests.cs`:
  - Default off; Save writes; Cancel discards; HasChanges.
  - The rollback calls the service once.
  - Message variants: done, nothing, left-as-is, failed, error.
  - The button is disabled while running.
  - At least 6.

## 15. File ownership

Exact. Never edit a file owned by the other leaf; anything not listed is out of scope.

One deliberate change from the original suggestion: the **setup uninstall step belongs to CORE, not UI**.
- It is pure restore logic: R1/R2/R3, state files, key check.
- G9.4 (phase7-setup) can only pass with the real restore engine. With CORE owning both, CORE can verify G9.4 end to
  end by itself.
- The uninstall step's UI surface (enum value, step label, warning text) is already complete in the skeleton.
- UI keeps the Settings rollback button. It codes against the frozen `IAutoHdrService` with a fake.

### Owned by CORE

- `src/HdrPilot.Core/AutoHdr/**` (new files plus `AutoHdrService.cs`, `GpuPreferenceStore.cs`, and the hooks part of
  `AutoHdrContracts.cs`). In `AutoHdrContracts.cs`, `IAutoHdrService` and the status/result records stay frozen.
  `AutoHdrRegistryLocation.cs` is frozen; bug fixes only, and report them.
- `src/HdrPilot.Core/Sessions/SessionManager.cs`, `SessionManagerOptions.cs`
- `src/HdrPilot.Core/Launchers/LauncherSignalHandler.cs`, `LauncherSignalService.cs`
- `src/HdrPilot.Core/Hosting/AutomationHost.cs`
- `src/HdrPilot.App/Setup/UninstallEngine.cs`, `Setup/UI/UninstallProgressViewModel.cs`, `Setup/SetupContracts.cs`
  (further additions only if needed), `Resources/SetupStrings.resx` (Auto HDR uninstall texts only)
- `src/HdrPilot.Core/Resources/CoreStrings.resx`, only if a tray notification is wanted. Not required, and preferably none.
- Tests: `tests/HdrPilot.Tests/AutoHdr/**`, `tests/HdrPilot.Tests/TestSupport/FakeGpuPreferenceStore.cs` and
  `SessionFakes.cs`, `tests/HdrPilot.Tests/Sessions/**`, `tests/HdrPilot.Tests/Launchers/**`,
  `tests/HdrPilot.Tests/Hosting/**`, `tests/HdrPilot.App.Tests/Setup/**`
- Gates: `tools/gates/phase7-setup.ps1`, `tools/gates/Phase7Common.ps1`

### Owned by UI

- `src/HdrPilot.App/MainWindow.xaml` (+ `.cs`), `Themes/Library.xaml`, `Themes/Shell.xaml`, `Themes/Controls.xaml` (if needed)
- `src/HdrPilot.App/ViewModels/Library/**` (GameItemViewModel, GameDetailsViewModel, LibraryViewModel, ILibraryContext,
  AppLibraryContext, LibraryText)
- `src/HdrPilot.App/ViewModels/Shell/**` (SettingsViewModel, IShellContext), `src/HdrPilot.App/Views/Shell/SettingsWindow.xaml` (+ `.cs`)
- `src/HdrPilot.App/Views/Library/**` (only if needed)
- `src/HdrPilot.App/AppServices.Library.cs`, `src/HdrPilot.App/AppServices.Shell.cs`
- `src/HdrPilot.App/Resources/LibraryStrings.resx`, `ShellStrings.resx`, `CommonStrings.resx` (if needed)
- Tests: `tests/HdrPilot.Tests/Config/AutoHdrConfigTests.cs` (new), `tests/HdrPilot.App.Tests/Library/**`,
  `tests/HdrPilot.App.Tests/Shell/**`
- Gates: `tools/gates/phase9-autohdr-ui.ps1` (new)
- `README.md`, `docs/entscheidungen.md`

### Frozen (completed in the skeleton; neither leaf edits)

- `src/HdrPilot.Core/Config/AppConfig.cs`, `AppPaths.cs`, `ConfigStore.cs`, `ConfigService.cs`
- `src/HdrPilot.App/App.xaml.cs`, `AppArguments.cs`, `AppServices.cs`
- `src/HdrPilot.App/Setup/SetupLocations.cs`, `Setup/UI/SetupText.cs`
- `tests/HdrPilot.App.Tests/AppArgumentsTests.cs`
- `GATES.md`, `docs/stand.md`
- The CLI
- `tests/HdrPilot.Tests/Architecture/AntiCheatSafetyTests.cs`

If a leaf believes it needs a frozen or foreign file, it stops and reports instead of editing.

**Merge:** the files are disjoint, so the order does not matter. Suggested: CORE first, then UI. Then run all gates
together (G9.5) and get an independent review.

## 16. Safety rules (repeat of the hard rules that matter most here)

- Never touch the real Auto HDR keys from tests, tools or gates. That means
  `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, including `DirectXUserGlobalSettings`. Gates may read the real
  key only to prove it is unchanged.
- Tests use `FakeGpuPreferenceStore` or a key under `HKCU\Software\HDRpilot.Tests\...` that they delete afterwards.
- Every app or CLI run uses `--config-dir <temp>`. Use `--autohdr-test-root` where the gate needs a known name.
- Never run the HDR-switching gates: phase1-*, phase3-standin, phase4-run, phase4-crash, phase6-wrap, phase6-traymenu.
- Never start stand-in games in the new UI gate.
- Never send input or post messages to other processes.
- Never touch the user's installed HDRpilot or real config.
- No new NuGet packages and no network. `AntiCheatSafetyTests` stays green and unchanged; registry writes are allowed,
  but no process handles.
- German comments and UI texts, and UI texts only in resx.
- Preserve line endings and BOMs: `docs/entscheidungen.md` is CRLF; PowerShell scripts with umlauts are UTF-8 with
  BOM; the Library, Shell and Setup resx files are LF; `CoreStrings.resx` is CRLF; `CommonStrings.resx` is mixed, so
  insert without converting. C# and XAML files are mostly LF and UTF-8 without BOM, but a few contain stray CR
  characters (App.xaml, FolderSafety.cs). Check each file before editing and never normalize a whole file.
- Do not edit `GATES.md` or `docs/stand.md`.
- Commit messages go through a UTF-8 file without BOM, committed with `git commit -F`, and end with a blank line and
  "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>".

## 17. Known limits and open points (for E88 and the report; no work item)

- Whether per-game 2097 works with global Auto HDR off on 26H2, and whether it works when HDRpilot turns HDR on only
  after the game start: this is G9.6, decided with the user later. It also decides the fallback default.
- The global "already on" ambiguity: if the user switches global Auto HDR on in Windows while HDRpilot holds it, the
  release turns it back off. This is logged and accepted.
- `hdrpilot watch` and the app on the same storage at the same time are unsupported. That was already the case for
  `session-state.json`.
- Forcing (`D3DBehaviors`/`BufferUpgradeOverride`) and the intensity (`AutoHDRStrength`) are not built.
