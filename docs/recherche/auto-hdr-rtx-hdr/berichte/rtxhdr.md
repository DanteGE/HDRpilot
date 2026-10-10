# RTX HDR per game: can a third-party app control it? (research for HDRpilot, as of 2026-10-10)

## Short answer

- **Yes, technically.** RTX HDR per game is stored in the NVIDIA driver profile database (DRS). A third-party app can set it through NVAPI's DRS functions without touching the game process.
- **Risk 1: undocumented settings.** NVIDIA's public NVAPI header does not include any RTX HDR setting IDs. The IDs used below come from the community and could change with any driver update.
- **Risk 2: the normal path depends on the NVIDIA App.** The App/overlay version of RTX HDR needs the NVIDIA overlay running. The overlay-free "driver path" ignores the brightness, contrast and saturation sliders, and it probably needs admin rights to switch on.
- **Admin rights:** most RTX HDR writes appear not to need admin, but I did not test writes on this machine.
- **No official SDK** exists for this.
- **Anti-cheat:** there is no official statement from NVIDIA or any anti-cheat vendor. No bans from changing driver profiles were found, but that is only absence of evidence.

## 1. How RTX HDR is enabled and where it is stored

**Ways to turn it on (official):** NVIDIA App > Graphics, either the global setting or per game under Program Settings. The sliders are also in the in-game overlay (Alt+Z > Game Filter). [FAQ 5521, updated 2026-02-24; NVIDIA news 2024-10-01]

**Where it is stored:** per-game values live in the driver profile database at `C:\ProgramData\NVIDIA Corporation\Drs\nvdrsdb0.bin`. This database is shared by all Windows users on the PC.

Setting IDs (none are in NVIDIA's public header; names are community labels):

| ID | Community name | Values | Where the name comes from |
|---|---|---|---|
| 0x00DD48FB | RTX HDR – Enable | 0 = off, 1 = on | Profile Inspector (2026-08), RHI, NvTrueHDR |
| 0x00DD48FC | RTX HDR – Peak Brightness | 400–2000 nits | RHI, unverified |
| 0x00DD48FD | RTX HDR – Middle Grey | 10–100, default 50 | RHI, unverified |
| 0x00DD48FE | RTX HDR – Contrast | stored as 100 + UI value (UI range −100 to +100) | RHI, unverified |
| 0x00DD48FF | RTX HDR – Saturation | stored as 100 + UI value | RHI, unverified |
| 0x1077A11A | RTX HDR Allow | 0 = disallow, 1 or 0x10 = allow | Profile Inspector Reference.xml, RHI |
| 0x00432F84 | RTX HDR – Driver Flags / Debanding | see below | Profile Inspector; emoose (NvTrueHDR author) |
| 0x00980896 | Game Filters Enable (hidden) | 0 or 1 | Profile Inspector; NvTrueHDR sets it to 1 |
| 0x1035DB89 | Freestyle/Ansel Allow (for comparison) | — | Public header, as `ANSEL_ALLOW_ID` |

**Driver Flags (0x00432F84) values:** 0 = off; 2 = VeryHigh debanding; 3 = VeryHigh plus an on-screen indicator; 0x0A = Medium; 0x06 = no debanding; 0x23 = indicator plus debug. Profile Inspector's description says that any non-zero value makes the driver apply HDR itself instead of through the NVIDIA overlay, and that in this mode the brightness, contrast and saturation settings are not supported.

**Two ways RTX HDR can run:**
- **App/overlay path:** Enable = 1, applied through the NVIDIA overlay, sliders work.
- **Driver path:** Driver Flags non-zero. Works without the App, but uses fixed values: about 1000 nits peak and paper white around 150 (per emoose in 2024; a Guru3D thread from 2026-01 also says peak "defaults to 1000"). If the NVIDIA overlay is enabled, the overlay version takes precedence.

**RTX Video HDR is a different feature.** It is for video playback in browsers and VLC, set under NVIDIA App > System > Video, and it is not per game. [NVIDIA 2024-08-06]

**Old drivers:** the flag meanings changed between drivers 531.18 and 551.23. This is proof that these IDs can break when NVIDIA updates the driver.

## 2. Can a third-party app set it through NVAPI?

**License:** the NVAPI SDK on GitHub (NVIDIA/nvapi, R615 branch, last push 2026-09-18) is MIT-licensed. That covers both the headers and the .lib files. The real implementation is in `nvapi64.dll`, which ships with the driver. A C# app can call `nvapi_QueryInterface` directly with the function IDs from `nvapi_interface.h` and does not need to link the .lib.

**Functions needed (all public):**
- `NvAPI_DRS_CreateSession` (0x0694D52E)
- `NvAPI_DRS_LoadSettings` (0x375DBD6B)
- `NvAPI_DRS_FindApplicationByName` (0xEEE566B2). Use the full exe path; it then returns the profile the driver will actually apply.
- If no profile exists: `NvAPI_DRS_CreateProfile` and `NvAPI_DRS_CreateApplication`
- `NvAPI_DRS_SetSetting` (0x577DD202)
- `NvAPI_DRS_SaveSettings` (0xFCBC7E14)
- To undo: `NvAPI_DRS_DeleteProfileSetting` or `NvAPI_DRS_RestoreProfileDefaultSetting`

**Struct sizes:** the setting record (`NVDRS_SETTING_V1`) is 12,320 bytes. The application record (`NVDRS_APPLICATION_V4`) is 20,492 bytes. I confirmed the second one by an actual call.

**The setting IDs are undocumented.** I searched NVIDIA's R615 header for "HDR" and for these IDs: only Ansel/Freestyle IDs exist, nothing for RTX HDR.

**Existing proof it works:** RHI ("ReShade HDR Installer", RankFTW/RHI, C#, GPL-3.0, active 2026-10) already sets RTX HDR Enable, Peak, Middle Grey, Contrast, Saturation, Allow and Debanding per game through raw NVAPI. Details from its code:
- It first tries an undocumented R610+ variant of SetSetting (0x8A2CF5F5) and falls back to the public one. Whether the public function alone is enough on current drivers is unverified.
- It notes that the popular C# wrapper NvAPIWrapper silently ignores these IDs, so raw calls are needed.

**Admin rights:**
- The NVAPI header lists no admin requirement for the DRS functions. Other NVAPI functions explicitly say they need admin.
- I checked the DRS folder and `nvdrsdb0.bin` permissions here (read-only). They give Full Control to "Everyone" and to "ALL APPLICATION PACKAGES".
- A non-elevated, medium-integrity process on this PC read the whole database without problems (8,011 profiles).
- RHI's README lists admin as needed only for ReBAR, Smooth Motion, Low Latency (Ultra), Vulkan ReShade and export/import. Its release notes add that Debanding (0x00432F84) needs elevation, and a code comment says writes to the base profile need it "on most systems".
- So the driver appears to enforce admin per setting (error `NVAPI_INVALID_USER_PRIVILEGE`). Enable and the slider settings look writable without admin.
- Profile Inspector always asks for admin (its manifest uses `requireAdministrator`), but that is a choice by the tool, not proof the API needs it.
- I did **not** test any writes, per your rules.
- Known failure: `NVAPI_ACCESS_DENIED` when the Drs folder is missing (Profile Inspector issue #504, 2026-08/09).

## 3. Requirements and limits (official FAQ, updated 2026-02-24)

**Requirements:**
- Windows 11 22H2 or later, with HDR switched on in Windows
- RTX 20 series or newer, single GPU (no SLI)
- An HDR400 or better display
- Driver 565.90 or newer
- DirectX 9–12 and Vulkan only; **OpenGL is not supported**

**Turn off for the game:**
- The game's own HDR
- Windows Auto HDR
- Fast Sync

**Display mode:**
- Fullscreen or exclusive fullscreen ("IFLIP").
- Borderless windowed needs "Optimizations for windowed games" on. Your registry has `SwapEffectUpgradeEnable=1`, which third-party docs map to that option being on.

**Multiple monitors:**
- Supported since App 10.0.3 with driver 565.90 (2024-10-01).
- Not supported with NVIDIA Surround or Clone mode.
- With mixed HDR and SDR displays, it only works when the game launches on the HDR display.
- The driver automatically turns off Multiplane Overlay (MPO).

**Peak brightness:** read from the display's EDID, up to 2000 nits. If the EDID gives no value, it defaults to 1500. Values from the Windows HDR Calibration app are used if present.

**DSR, DLDSR and NIS:**
- NVIDIA's guidance at launch (2024-02, via Wccftech) said to turn them off.
- They are no longer in the current FAQ.
- A Japanese guide from 2026-08 still lists them. Treat this restriction as probably still valid (unverified).

**G-SYNC:** I found no documented incompatibility.

**Does it need the NVIDIA App running?**
- The App path requires the NVIDIA overlay.
- Since App 11.0.1 (2024-12), "Game Filters and Photo Mode" is off by default. Whether RTX HDR needs that toggle is **unverified**; reports conflict.
- An NVIDIA support article (a_id 5752, 2025-12, read only through quotes) says driver 591.44+ needs a minimum App build for Game Filters to work.
- Changes take effect at the **next game launch**, so the setting has to be written before the game starts.

**Performance cost:**
- About 7–10% FPS on an RTX 4090 (XDA, 2026-07-21).
- 5–10%+ on an RTX 4080 (Reddit, 2024).
- With default (VeryHigh) debanding about 8%; with debanding off about 3% (community claim, TechOptimized, 2024-02). Debanding also removes some fine detail.
- I found no RTX 50-series measurements.
- Separately, when "Game Filters" is enabled the App itself has cost up to 15% even with no filter applied (Tom's Hardware, 2024-12). That is why NVIDIA now ships it off by default.

## 4. Anti-cheat

**No official statement exists** from NVIDIA, Epic/EAC, BattlEye, Riot/Vanguard or Activision/Ricochet about RTX HDR. Blizzard support said in 2024-02 that it "can't give a yes/no" answer; no bans were reported in that thread.

**How it works:** writing a driver profile happens only in HDRpilot's own process. This is the same channel the NVIDIA App and Control Panel use. While the game runs, the driver itself loads NVIDIA-signed DLLs into it (nvppex.dll and nvAIHDRx.dll, per emoose in 2024-02).

**Concrete cases (mostly weak evidence):**
- **Valorant:** Freestyle filters reported blocked since about 2021. An XDA article (2026-07) shows an "RTX HDR in Valorant" screenshot but gives no detail.
- **Arc Raiders (2026-01):** NVIDIA disabled the App's filters. The RTX HDR toggle reportedly remains (allthings.how, gamegpu). Reports that Profile Inspector triggers "Blocked Application" are unverified and concern the Inspector program itself, not profile values.
- **EAC (Dead by Daylight, 2024):** user reports say filters broke.
- **Injection tools** like TrueHDRTweaks or Special K are explicitly advised against for anti-cheat games. Only the pure profile-setting approach fits HDRpilot's design.

**What the driver database here shows (read-only, the installed driver):**
- "RTX HDR Allow" is preset to **0 in 224 profiles**. These are almost all non-games: browsers, Office, launchers including several Easy Anti-Cheat launchers, Discord, OBS, NVIDIA tools.
- Plus a few games: Street Fighter V, Marvel vs. Capcom: Infinite, Killer Instinct, Dirty Bomb.
- It is preset to 1 only for GeForce NOW.
- Valorant, Fortnite, CS2 and Apex have Freestyle Allow = 1 and no RTX HDR disallow entry.
- No user-set RTX HDR values exist anywhere, so the App's per-game RTX HDR has not been used on this PC.
- Alan Wake 2's exe resolves to the predefined "Alan Wake 2" profile.
- My conclusion (inference): "RTX HDR Allow" works as NVIDIA's per-game blocklist. HDRpilot should treat a preset 0 as "don't enable" and never force Allow = 1, which is what NvTrueHDR does.

## 5. Official SDK?

- **None.** The NVIDIA App release notes (up to 11.0.9) mention no public API; the only extension point is G-Assist plug-ins.
- NVAPI DRS itself is public and documented; only the RTX HDR setting IDs are not.

## 6. What this means for HDRpilot

**Feasible design:**
1. Before launch, find the profile with `FindApplicationByName` using the full exe path. If none exists, create a user profile.
2. Read 0x1077A11A. If NVIDIA's preset is 0, skip the game.
3. Set 0x00DD48FB = 1, optionally with the sliders, then call `SaveSettings`.
4. To undo, use `DeleteProfileSetting`.

**Points to handle:**
- **Pick a path:**
  - App/overlay: needs the NVIDIA App with the overlay on; sliders work.
  - Driver path (0x00432F84): likely needs admin; sliders ignored.
- **Conflicts:**
  - The NVIDIA App writes the same values, and forum reports say the App sometimes rewrites profiles.
  - Windows Auto HDR (including the per-app `AutoHDREnable`) and the game's own HDR must be off for that exe.
- **Possibly leave it on permanently:** the setting might be safe to leave enabled, since RTX HDR needs Windows HDR to be on (my inference, unverified). That would avoid the timing problem of writing at process start.
- **Choosing RTX HDR vs Windows Auto HDR:** no source gives an objective quality metric. A simple rule is to use RTX HDR for DirectX 9/10 and Vulkan games, since the official FAQ covers DX9–12 and Vulkan. The claim that Windows Auto HDR is limited to DX11/DX12 comes from my background knowledge and was not checked in this research.
- **Related:** RHI's `AutoHdrService.cs` also forces Windows Auto HDR per game via `HKCU\Software\Microsoft\Direct3D\ApplicationN` (the ledoge/autohdr_force method).

## Sources
- NVIDIA App FAQ (RTX HDR section), updated 2026-02-24: https://nvidia.custhelp.com/app/answers/detail/a_id/5521
- NVIDIA news, RTX HDR multi-monitor, 2024-10-01: https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-g-sync-controls-rtx-hdr-multi-monitor/
- NVIDIA news, RTX Video HDR sliders, 2024-08-06: https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-rtx-vsr-hdr-controls-and-more/
- NVIDIA App release highlights (up to 11.0.9): https://www.nvidia.com/en-us/software/nvidia-app/release-highlights/
- NVAPI SDK (MIT, R615, last push 2026-09-18): https://github.com/NVIDIA/nvapi — `nvapi.h`, `nvapi_interface.h`, `NvApiDriverSettings.h`, `License.txt`
- Profile Inspector setting names (last updated 2026-08-26): https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/CustomSettingNames.xml
- Profile Inspector reference file: https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/Reference.xml
- Profile Inspector manifest: https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/app.manifest
- Profile Inspector issue #504 (2026-08-31): https://github.com/Orbmu2k/nvidiaProfileInspector/issues/504
- RHI source (GPL-3.0, 2026): https://github.com/RankFTW/RHI — `RenoDXCommander/Services/DlssPresetService.DriverSettings.cs`, `DlssPresetService.cs`, `README.md`
- RHI 2.2.6 release notes: https://newreleases.io/project/github/RankFTW/RHI/release/RHI-2.2.6
- emoose, NvTrueHDR thread on Guru3D (2024-02-03/04): https://forums.guru3d.com/threads/nvtruehdr-tool-to-enable-rtx-hdr-in-games.451108/
- NvTrueHDR on Nexus Mods: https://www.nexusmods.com/site/mods/781
- Guru3D, RTX HDR without the NVIDIA App (2026-01-13): https://forums.guru3d.com/threads/is-there-any-way-to-get-the-rtx-hdr-settings-to-take-effect-without-installing-the-nvidia-app.459032/
- Guru3D, App 11.0.1 turns Game Filters off by default (2024-12-24): https://www.guru3d.com/story/nvidia-releases-v1101-update-disables-game-filter-feature-by-default
- Tom's Hardware, NVIDIA App performance problems (2024-12): https://tomshardware.com/pc-components/gpus/we-tested-nvidia-app-performance-problems-games-run-up-to-15-percent-slower-with-the-app
- XDA, RTX HDR experience (2026-07-21): https://www.xda-developers.com/i-dismissed-nvidia-app-until-i-tried-rtx-hdr/
- TechOptimized (Patreon), debanding cost (2024-02): https://www.patreon.com/posts/99110213
- Wccftech, RTX HDR launch (2024-02-22): https://wccftech.com/nvidia-rtx-hdr-brings-ai-powered-hdr-to-sdr-only-pc-games-new-game-ready-driver-is-optimized-for-nightingale/amp/
- Japanese RTX HDR troubleshooting guide (2026-08): https://gaming-st.com/trouble-settings/rtx-hdr-not-working-nvidia-app-fix/
- Arc Raiders filters (updated 2026-01-03): https://allthings.how/arc-raiders-has-blocked-nvidia-game-filters-what-that-means-and-what-you-can-do/
- Arc Raiders filters (2026-01-02): https://en.gamegpu.com/news/igry/nvidia-otklyuchila-filtry-v-arc-raiders-iz-za-zloupotreblenij
- Valorant filter block (undated): https://www.sportskeeda.com/valorant/riot-games-seems-disabled-nvidia-game-filters-valorant-due-game-breaking-flash-bug
- Blizzard forum, RTX HDR ban risk (2024-02-18): https://eu.forums.blizzard.com/en/wow/t/do-i-run-the-risk-of-a-ban-when-using-the-rtx-hdr-mod/496566
- Overclockers forum, changes apply at next launch: https://forums.overclockers.co.uk/threads/rtx-hdr-for-games.18984090/
- Winhance, `SwapEffectUpgradeEnable` mapping (third-party): https://winhance.net/docs/features/optimizations/gaming-performance.html
- On this PC, read-only: Drs folder permissions, plus a profile-database read that made no write or save calls. Script: `<Notizordner>\drsread.ps1`; output: `<Notizordner>\drsread_out2.txt` in the same folder.