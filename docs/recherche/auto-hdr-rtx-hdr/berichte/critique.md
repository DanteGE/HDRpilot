## Fact-check: Windows Auto HDR and NVIDIA RTX HDR per game in HDRpilot (as of 2026-10-10)

**Bottom line:** Both are probably feasible without admin rights and without touching the game process. But **neither path is verified end-to-end on this PC**. Two points from the reports are contradicted or not shown:
- **Auto HDR, global off + per game on:** Microsoft's 2023 blog says it works. Today's Settings and Support page only offer a per-game *opt-out*.
- **RTX HDR set by a third-party program:** the setting IDs are confirmed, but nobody has shown that the NVIDIA App honours DRS (driver profile database) values written by another program. A 2024 source also contradicts the report's "never set Allow=1" rule.

The experiments at the end settle both.

**Environment corrections (read on this PC, read-only):**
- **Windows build:** the build is **26300.9550 (26H2)**. `HdrPilot\docs\umgebung.md` still says 26200.9550 (written 2026-10-07). The 2097/2096 values come from a 26200 source; nobody has observed them on 26300 yet. The Alan Wake 2 value may have been written before the upgrade.
- **Monitor:** HDRpilot's docs show "MAG 341C OLED" (EDID `MSI3DD0`) at **3440×1440, 143.993 Hz**. The quality report assumed a MAG 341CQP at 175 Hz; the model is unconfirmed and the active refresh rate is 144, not 175.
- **Claude's container:** this session's shell runs inside Claude's MSIX container, where HKCU\Software writes go to a private copy. Any registry experiment must run outside it (by the user, or through a process started outside the container). Otherwise it silently does nothing on the real system.

---

## A. Verified

**Auto HDR registry storage and behaviour**
1. **Per-game values:** these live under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, one REG_SZ per exe full path. ForceAutoHDR.Net (repo created 2026-09-07, last push 2026-10-01) has these constants and comments:
   - `AutoHdrEnabledValue="2097"` and `AutoHdrDisabledValue="2096"`, with the comment "Observed on Windows 11 26200: 2097 for on, 2096 for off".
   - "Only bit 0 carries the on/off state".
   - Clearing the setting deletes the value, "which is what the Settings page does too".
   - Sources: https://github.com/Phoenix-/ForceAutoHDR.Net and `src/ForceAutoHDR.Core/GpuPreferencesStore.cs`
   - What the higher bits mean is still undocumented.
2. **Microsoft's 2023 statement** (W. Ho, 2023-01-19, https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page/):
   - The per-app preference overrides the system-wide one.
   - "even if you disable System-wide settings for a feature, you can enable it for a specific application".
   - The settings "only apply on eligible games".
   - Microsoft has preset some apps on or off by default.
   - Auto HDR covers "DirectX 10 thru DirectX 12".
3. **Microsoft Support "Optimizations for windowed games"** (undated, © 2026, https://support.microsoft.com/en-us/windows/optimizations-for-windowed-games-in-windows-11-3f006843-2c7e-4ed0-9a5e-f9389e535952):
   - Turning on Auto HDR turns on windowed optimizations automatically.
   - The per-app control is a **"Don't use Auto HDR"** checkbox.
   - After a change: "Restart your game or app".
4. **Windowed and borderless DX10/11 games** need the flip-model upgrade before Auto HDR can work (DirectX blog, 2022-02-16, https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/).
5. **Launch blog** (2021-03-17, https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/):
   - DX11/DX12 games, "1000+" titles, some "hard to identify as Auto HDR eligible".
   - Auto HDR "does take some GPU compute power".
   - The split-screen debug key is HKLM `...\Control\GraphicsDrivers\AutoHDR.ScreenSplit` and needs admin.
6. **Game Bar intensity slider** (DirectX blog, 2022-04-25, https://devblogs.microsoft.com/directx/exciting-additions-to-your-auto-hdr-experience-on-pc/): saved per game; the far left equals off. The Game Bar on/off checkbox is global and needs a game relaunch.
7. **Forcing via `D3DBehaviors`:** ledoge/autohdr_force `main.c` writes these, HKCU only (https://github.com/ledoge/autohdr_force/blob/master/main.c):
   - Subkey `HKCU\SOFTWARE\Microsoft\Direct3D\Application%d`
   - `Name` = the string the user entered
   - `D3DBehaviors="BufferUpgradeOverride=1[;BufferUpgradeEnable10Bit=1]"`
   - RHI's `AutoHdrService.cs` uses the same mechanism (https://github.com/RankFTW/RHI).
8. **Rendering exe, not launcher:** Auto HDR is keyed to the process that owns the swapchain, so a value written for a launcher exe does nothing (ForceAutoHDR.Net note, 2026-09-08, https://raw.githubusercontent.com/Phoenix-/ForceAutoHDR.Net/main/notes/gameconfigstore-is-the-real-game-list.md).
9. **24H2 Auto HDR bug** (freezes and wrong colours):
   - The upgrade block (safeguard 55382406) was lifted with KB5051987 on 2025-02-11 (Windows Latest, 2025-02-12, https://windowslatest.com/2025/02/12/microsoft-unlocks-windows-11-24h2-for-more-gaming-pcs-with-feb-11-2025-update).
   - The ElevenForum mirror of Microsoft's table lists it as resolved on 2025-02-14.
   - I did not open Microsoft's release-health page itself.
10. **Fortnite anecdote:** Auto HDR worked under BattlEye but not under EAC. It is a single 2022 user report (https://learn.microsoft.com/en-us/answers/a/6502239).

**Auto HDR, read-only local evidence (from `reg query` run outside the container by an earlier agent)**
- `DirectXUserGlobalSettings = AutoHDREnable=0;SwapEffectUpgradeEnable=1;`
- `AlanWake2.exe = AutoHDREnable=2097;`
- Odd entries confirmed: the `ScopeX.exe` value is the bare string `0`, and `GraphicsFeaturesNotificationConfig` is a DWORD.
- **New finding:** `HKCU\Software\Microsoft\DirectX\GraphicsSettings\AutoHDROptOutApplicable = 1`. It is not documented anywhere I could find, and its name suggests the per-game control is an opt-out.

**RTX HDR**
11. **NVIDIA App FAQ 5521** (updated 2026-02-24, https://nvidia.custhelp.com/app/answers/detail/a_id/5521):
    - Requires Windows 11 22621 or later, RTX 20 series or newer on a single GPU, an HDR400 or better display, and driver 565.90 or later.
    - Covers "DirectX 9-12, and Vulkan (OpenGL is not supported)".
    - Windows Auto HDR, in-game HDR and Fast Sync should be off.
    - The game must run fullscreen or IFLIP; borderless needs windowed optimizations.
    - Multi-monitor works, but not Surround or Clone. In a mixed HDR/SDR setup the game must launch on the HDR display.
    - MPO (Multiplane Overlay) is turned off automatically.
    - Peak brightness comes from the EDID (max 2000 nits); if the EDID has none, 1500.
    - DSR, DLDSR, NIS and "Game Filters" are **not mentioned**.
12. **NVIDIA news, 2024-10-01** (https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-g-sync-controls-rtx-hdr-multi-monitor/): multi-monitor support with driver 565.90; APIs listed as "DX12, DX11, DX9, and Vulkan".
13. **Setting IDs:** Profile Inspector's `CustomSettingNames.xml` names all of these (https://github.com/Orbmu2k/nvidiaProfileInspector):
    - `0x00DD48FB` Enable (0/1)
    - `0x00DD48FC` Peak Brightness, `0x00DD48FD` Middle Grey, `0x00DD48FE` Contrast, `0x00DD48FF` Saturation
    - `0x00432F84` Driver Flags, values 0, 2, 3, 6, 0x0A, 0x23. Its description says that when driver flags are set, the "driver doesn't support customizing brightness/contrast/saturation".
    - `Reference.xml` adds `0x1077A11A` "RTX HDR Allow" (0 = Disallow, 1 = Allow, generic description) and hidden `0x00980896` "Game Filters Enable".
    - RHI uses the same IDs.
14. **NVAPI:**
    - The DRS functions and their IDs match `nvapi_interface.h`: CreateSession 0x0694d52e, LoadSettings 0x375dbd6b, SaveSettings 0xfcbc7e14, FindApplicationByName 0xeee566b2, SetSetting 0x577dd202, DeleteProfileSetting 0xe4a26362, RestoreProfileDefaultSetting 0x53f0381e.
    - `NvApiDriverSettings.h` contains no HDR setting.
    - The `FindApplicationByName` "fully qualified path ... will always return the profile the driver will apply" quote is correct.
    - The struct sizes are correct by the header layout: NVDRS_SETTING_V1 = 12,320 bytes, NVDRS_APPLICATION_V4 = 20,492 bytes.
    - RHI uses SetSetting variant `0x8A2CF5F5` ("R610+"), with the public ID as fallback.
    - RHI 2.2.6 says "Debanding is greyed out when not running as admin" (https://newreleases.io/project/github/RankFTW/RHI/release/RHI-2.2.6).
15. **DRS file permissions** (read-only `icacls` today): `C:\ProgramData\NVIDIA Corporation\Drs` and `nvdrsdb0.bin` give Everyone and ALL APPLICATION PACKAGES Full Control, with a Low integrity no-write-up label.
    - `nvdrsdb0.bin`/`nvdrsdb1.bin` were last written before the read-only probe (13:50–13:52) and the probe calls no Set or Save functions, so NVIDIA components write the database on their own.
16. **Probe output, spot-checked:** 8,011 profiles; 224 with `1077A11A=0` (predefined), one with `=1`; Alan Wake 2 resolves to a predefined profile.
17. **App-path dependency:** in a Guru3D thread (2026-01-13), the original poster says the RTX HDR profile settings "don't take effect" without the NVIDIA App plus overlay. A reply says the driver path runs without the App but peak is fixed at 1000 (https://forums.guru3d.com/threads/is-there-any-way-to-get-the-rtx-hdr-settings-to-take-effect-without-installing-the-nvidia-app.459032/).
    - emoose (2024-02-04): the driver-path defaults are 150 paper white, 1000 nits and 1.1 saturation.
    - Enabling it loads NVIDIA-signed `nvppex.dll` and `nvAIHDRx.dll` into the game (https://forums.guru3d.com/threads/nvtruehdr-tool-to-enable-rtx-hdr-in-games.451108/).
18. **Game Filters with driver 591.44+:** NVIDIA a_id 5752 (updated 2025-12-04) says Game Filters and Photo Mode need App 11.5.420 or newer. The page itself also says "11.0.5.420", so it is inconsistent, and it does not mention RTX HDR (https://nvidia.custhelp.com/app/answers/detail/a_id/5752).

**Quality and performance**
19. **HDRgameAnalysis** (Aug 2026, **one game: Kena**, https://hdrgameanalysis.com/auto-hdr-windows-pc):
    - RTX HDR has slightly better highlight separation than Auto HDR.
    - Windows' sRGB curve raises blacks.
    - Auto HDR banding is suspected but not verified.
    - The Game Bar slider stays greyed out in Arc Raiders, Hell Let Loose Vietnam and Wardogs.
20. **Measured costs:**
    - XDA (2026-07-21, RTX 4090): RTX HDR costs "usually around 7–10%" (https://www.xda-developers.com/i-dismissed-nvidia-app-until-i-tried-rtx-hdr/).
    - PCGH (2026-07-02, reporting a TechSpot test on an RTX 5060 Ti): the App and overlay cost nothing measurable; filters cost up to 10%; RTX HDR was not measured (https://www.pcgameshardware.de/Nvidia-Geforce-Grafikkarte-255598/News/App-Neuer-Test-kaum-Fps-Verlust-Filter-kosten-bis-zu-10-Prozent-1546900/).
21. **PCGamingWiki glossary** (community): RTX HDR uses gamma 2.2 instead of sRGB, so it avoids raised blacks. RHI 2.2.6 changed its default to "Gamma 2.2 … instead of the flat Gamma 2.0", which fits the community view that Contrast 0 ≈ gamma 2.0 and +25 ≈ 2.2.

**PCGamingWiki API (live check, 2026-10-10)**
22. `cargofields`:
    - `Video.HDR` is a String.
    - `API.Direct3D_versions`, `DirectDraw_versions`, `OpenGL_versions`, `Glide_versions` and `Vulkan_versions` are comma-delimited lists.
    - Also present: `Windows_64bit_executable` and the others the apidetect report named.
23. An anonymous `cargoquery` returns `permissiondenied`: "You don't have permission to run arbitrary Cargo queries."
24. `Template:Video` uses the parameters `hdr` and `hdr notes`, but **only `HDR` is stored in Cargo**. Notes are available only from the page wikitext.

---

## B. Corrected or contradicted

1. **"Global off + per game on works" is not established for current builds.**
   - Only the 2023 Insider-era blog says it.
   - Today's Support page documents only a "Don't use Auto HDR" opt-out.
   - The local `AutoHDROptOutApplicable=1` value and a low-quality guide (WiseChecker: the global switch is a "ceiling") point the other way.
   - Treat it as **contradicted and untested** (experiment E1/E2).
2. **Which DirectX versions Auto HDR covers:** the rtxhdr report's "DX11/DX12 only" (from background knowledge) is outdated. Microsoft's 2023 sources say DX10–12, the 2021 launch said DX11/12, and today's Support page still says "DirectX 11 or DirectX 12". DX9, OpenGL and Vulkan are not supported natively.
3. **RTX HDR on OpenGL:** NVIDIA says it is not supported (FAQ 2026-02-24). The PCGamingWiki glossary says "DX9, DX10, DX11, DX12, Vulkan, OpenGL", probably meaning via "Prefer layered on DXGI swapchain".
   - The apidetect rule "OpenGL → RTX HDR" should default to **none** (opt-in only).
   - **RTX HDR on DX10:** the FAQ says "DirectX 9-12"; NVIDIA news posts list only DX9, DX11, DX12 and Vulkan. Unverified.
4. **"RTX HDR Allow: preset 0 is a blocklist; never set Allow=1" (rtxhdr report) is an inference, and a source contradicts it.**
   - emoose (2024-02-03) wrote that `0x00DD48FB`, `0x00980896` **and** `0x1077A11A` all had to be set to 1.
   - So the App itself may write Allow=1 when it enables RTX HDR.
   - Keep "skip a preset 0" as a safety rule, but find out what the App writes (E7).
5. **Peak, Middle Grey, Contrast and Saturation IDs:** the rtxhdr report lists these as "RHI, unverified". Profile Inspector names them too, so the **IDs** are confirmed. The value encodings (stored as 100 + UI value; 400–2000; 10–100) are still RHI-only.
6. **`D3DBehaviors` matching by "bare file name":** ForceAutoHDR.Net infers this from stored `Name` values on one machine. That is not a test of what the D3D runtime does.
   - ledoge stores whatever the user typed.
   - RHI writes the **full path**.
   - Choum28 claims full paths work.
   - The runtime's matching rule is **unverified** (E3).
7. **Where the Auto HDR intensity is stored:** the autohdr report says unknown. RHI's code says a token `AutoHDRStrength=NN;` in the same per-exe value (0–100). I found no other source; it is unverified (E5).
8. **Checking whether Auto HDR is active:** the autohdr report suggests the "Auto HDR notification". 7gxycn08/ForceAutoHDR says forced games get no notification (https://github.com/7gxycn08/ForceAutoHDR). Use the Game Bar slider (greyed out or not, and whether brightness changes) instead.
   - The same README says to turn Auto HDR on in Settings first, which hints that the force method may need global on (unverified).
9. **PCGamingWiki template details (apidetect report):**
   - `HDRCompatibleAPI` is also set by **OpenGL** in `Template:API`, not only by D3D9 and later.
   - `9.0` is stored as `9`, and `12u` as `12`.
10. **RTX 50-series cost:** the quality report's "RTX 5080 Steel Nomad −8.4%" is one undated Reddit run via a mirror. The rtxhdr report's "no 50-series data" is closer to the evidence.
11. **Digital Foundry's verdict** ("better in almost every metric") is only second-hand (StealthOptional, Tom's Guide); I could not reach the primary source. "RTX HDR looks better" is community consensus plus one single-game analysis, not a measured result.
12. **Minimum driver for RTX HDR:** 551.52 at launch for single monitor (Wccftech 2024-02-22; PCGamingWiki), 565.90 in today's FAQ. Not relevant to the installed driver.

---

## C. Unverifiable (no primary source found)

- What `AppStatus` (0, 4, 4096) means.
  - Speculative pattern: Wardogs has `AppStatus=4096` here, and HDRgameAnalysis lists Wardogs as a game where the Auto HDR slider stays greyed out.
- Which bits other than bit 0 in `AutoHDREnable` matter; the 4147/48 encoding (ElevenForum returned 403 to me).
- Whether Windows HDR Calibration values feed Auto HDR peak brightness. Neither Microsoft Support nor the 2022 blog says so.
- Whether the NVIDIA App path needs the "Game Filters and Photo Mode" toggle.
- Whether DSR, DLDSR and NIS still break RTX HDR. Only launch-era sources say so (Wccftech 2024-02-22); today's FAQ doesn't mention them.
- Whether RTX HDR settings only take effect at the next game launch. Only an Overclockers forum post says so.
- Whether DRS writes for `DD48FB`–`DD48FF` and `1077A11A` succeed without admin. NVAPI's header lists no admin requirement for the DRS functions; only RHI's practice suggests these work without elevation.
- **Anti-cheat:** no vendor statement exists for either feature. Secondary evidence:
  - Arc Raiders (allthings.how, updated 2026-01-03): NVIDIA revoked Game Filters; forcing them via Profile Inspector reportedly triggers "Blocked Application" and the game closes; the RTX HDR toggle reportedly remains.
  - CoD permaban (Guru3D, Mar 2025): the user blamed a Profile Inspector DLSS override; causation was not shown (https://forums.guru3d.com/threads/i-get-permaban-in-cod-after-dlss-override.455671/page-2).
  - RTX HDR makes the **driver** load NVIDIA DLLs into the game. HDRpilot itself stays clean, but the game process is not untouched.
  - Recommendation: default both features off for games with kernel anti-cheat. Do **not** test this locally on real accounts.

---

## D. Experiments only this PC can answer

**General rules for all experiments:**
- Run every step **outside Claude's container**: by the user directly, or through a process started via WMI that writes its output to %TEMP%.
- Never test with anti-cheat multiplayer games.

**Before anything (E0, backups):**
- `reg export "HKCU\Software\Microsoft\DirectX\UserGpuPreferences" %TEMP%\ugp-before.reg`
- `reg export "HKCU\Software\Microsoft\Direct3D" %TEMP%\d3d-before.reg` (may not exist)
- `reg export "HKCU\Software\Microsoft\DirectX\GraphicsSettings" %TEMP%\gs-before.reg`
- Full read-only DRS dump of the test games' profiles. Extend `DrsProbe.cs` to list **all** settings of a profile with `EnumSettings`, still no Set or Save calls.
- Record the state of the "Game Filters and Photo Mode" toggle.

**Auto HDR experiments**

- **E1. What the Settings page writes on 26300.**
  1. With global Auto HDR **off**, open Settings > System > Display > Graphics > [single-player DX11/12 SDR game] > Options.
  2. Note whether an Auto HDR control exists, its label, and whether it is enabled.
  3. `reg query` the exe's value.
  4. Turn global Auto HDR on, tick and untick "Don't use Auto HDR", and `reg query` after each step (2096, 2097, or value deleted?).
  - Undo: turn global off again, `reg import %TEMP%\ugp-before.reg`, and confirm with `reg query`.

- **E2. Does global off + per-game 2097 work?**
  1. Turn HDR on (HDRpilot), global Auto HDR off, and write `AutoHDREnable=2097;` on the rendering exe's full path.
  2. Launch the game, open Win+G and check whether the Auto HDR intensity slider is active and whether moving it changes brightness.
  3. Control runs: global on + no value (expect active); global on + 2096 (expect greyed out).
  - Optional unambiguous check (user, admin): set `AutoHDR.ScreenSplit=1`, then remove it with `reg delete HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers /v AutoHDR.ScreenSplit /f`.
  - Undo: restore the original value from the backup.

- **E3. Ineligible game and forcing.**
  1. Pick a game whose slider stays greyed out. Try 2097 alone.
  2. Then try `Direct3D\ApplicationN` with `D3DBehaviors=BufferUpgradeOverride=1` in four separate runs: `Name` = bare exe name or full path, each with global Auto HDR on and off.
  - Undo: delete only the `ApplicationN` subkey you created.

- **E4. Timing.** Change the value while the game is running and see whether anything changes before a restart. Also check how many seconds before game start HDR must be on.

- **E5. Intensity storage.** Move the Game Bar slider for one game and diff the `UserGpuPreferences` value before and after (is there an `AutoHDRStrength=` token?).
  - Undo: put the slider back and restore the value.

- **E6. `AppStatus` correlation (read-only).** For 4–6 installed games, compare `AppStatus` with whether the Game Bar slider is active or greyed out.

**RTX HDR experiments (single-player test games only)**

- **E7. What the NVIDIA App writes.**
  1. Take a baseline DRS dump of the test game's profile, and note the modification times of the NVIDIA App's own config files.
  2. Enable RTX HDR for that game in NVIDIA App > Graphics > Program Settings.
  3. Dump again and diff: does the App write `DD48FB`, `1077A11A`, `00980896` or `DD48FC`–`DD48FF`? Does it create a user profile or application entry, or store state outside DRS?
  4. Turn it off and dump a third time: does the App delete the entries or leave them at 0?

- **E8. Does a third-party write take effect?**
  1. Outside the container and **non-elevated**, write exactly the E7 diff to a *different* test game's profile using the public `SetSetting` (0x577DD202), then `SaveSettings`. Log any `NVAPI_INVALID_USER_PRIVILEGE` per setting.
  2. Launch with the App and overlay running. Does RTX HDR engage (the App overlay shows it active, image changes)? Does the App's UI show it? Does the App later overwrite it?
  - Undo: `DeleteProfileSetting` for each ID written, `SaveSettings`, then dump to confirm it matches the baseline.

- **E9. Overlay dependency.** Repeat E8 with "Game Filters and Photo Mode" off and on.
  - Undo: restore the toggle's original state.

- **E10. Timing for RTX HDR.** Write the setting while the game is running and see whether it applies before a restart.

- **E11. Performance and quality on this PC.**
  - Same GPU-bound scene, uncapped, at 3440×1440 and 144 Hz, measured with PresentMon or the App overlay: SDR vs Auto HDR vs RTX HDR (default debanding).
  - Plus a visual check of near-black detail. No settings beyond the per-game toggles under test.

- **E12. Mutual exclusion.** RTX HDR on together with global Auto HDR on and per-game 2096: is there double processing (Game Bar slider active)? This decides whether a per-game 2096 is enough to satisfy NVIDIA's "should be disabled".