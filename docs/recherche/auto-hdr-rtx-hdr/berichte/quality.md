# Native HDR, RTX HDR, Windows Auto HDR or SDR for HDRpilot (research, 2026-10-10)

**Bottom line:** For games without native HDR, NVIDIA RTX HDR generally looks better than Windows Auto HDR and works with more graphics APIs. It costs about 5–10% fps on average. Auto HDR costs almost nothing but has known black-level and coverage problems. Neither one is close to a native HDR implementation. HDRpilot can use a decision rule driven by data it already has: PCGamingWiki data, the GPU and driver, the display layout, and a "competitive" flag. Both features are built into the driver or the OS, so neither needs injection.

Coverage gap: I could not reach Digital Foundry or Eurogamer directly (my fetch tool is blocked there), so their findings are cited second-hand. I found no RTX HDR vs Auto HDR measurements from HDTVTest, Rtings, ComputerBase or Tom's Hardware. Tom's Hardware only covered the NVIDIA App filter-overhead issue.

## 1. Image quality

| Aspect | Auto HDR | RTX HDR |
|---|---|---|
| Highlight expansion | Weakest highlight separation, and it pushes secondary highlights hardest [S7] | "Slightly better" separation than Auto HDR, but still far behind RenoDX, which works before the game's tone mapping [S7] |
| Black level / gamma | Windows shows SDR content in HDR mode with the piecewise sRGB curve. Games mastered for gamma 2.2 therefore get lifted, washed-out shadows [S8][S7] | Uses a power-law gamma. Community analysis says the Contrast slider at +25 ≈ gamma 2.2 (0 ≈ 2.0, +50 ≈ 2.4), so it avoids the lift [S9][S10][S12]. The same author saw dark tones merging at +25 [S10] *(community)* |
| Banding / detail | More banding suspected, not verified [S7] | The NVIDIA App always runs the highest quality level, which includes a debanding pass that can erase fine detail and near-black detail [S10][S11] *(community)* |
| Color | One intensity slider. Saturation only through the Windows HDR Calibration app [S5][S33] | Default Saturation 0 over-saturates. Community says −50 is neutral for sRGB and −25 if you want a bit more color [S9] |
| Overall verdict | Generally ranked below RTX HDR | DF (Feb 2024, via secondary source): RTX HDR beats Auto HDR "in almost every metric" [S13]. HDRgameAnalysis (Aug 2026, one game) agrees [S7]. Community ranking: native > Special K/RenoDX > RTX HDR > Auto HDR *(opinion)* |

## 2. How each one is configured

- **Auto HDR**
  - Controls are an on/off switch and a per-game intensity slider in Game Bar. The game has to be restarted after a change [S5].
  - Peak brightness reportedly follows the Windows HDR Calibration result [S7] *(not stated officially)*.
  - A per-app setting overrides the system-wide one. So it can be on for one game while the global toggle stays off, as it is on this PC [S6].
- **RTX HDR**
  - Controls are Peak brightness, Middle grey, Contrast and Saturation, enabled per game in NVIDIA App > Graphics (Program Settings) [S1].
  - The peak slider is capped at the display's EDID value (maximum 2000 nits). If the EDID has no value the cap is 1500 nits. If the Windows HDR Calibration app was used, its values are used instead [S1].
- **Community starting values (contrast +25)** [S9]:

  | Display peak | Paper white | Middle grey |
  |---|---|---|
  | 400 nits | 101 nits | 22 |
  | 1000 nits | 203 nits | 44 |

  For a peak of about 470 nits, interpolation gives a middle grey of about 24 *(my interpolation)*.

## 3. The user's monitor (assumed to be MSI MAG 341CQP QD-OLED)

- The request says "MAG 341C OLED"; I assumed this is the MAG 341CQP QD-OLED. Please confirm.
- **Specs from MSI and Notebookcheck:** certified VESA DisplayHDR True Black 400, about 1000 nits peak claimed for the "Peak 1000" mode, 250 nits typical in SDR. Notebookcheck says it uses an older QD-OLED panel generation [S21][S22].
- **Measured values:** I found no peak-brightness numbers for this exact model. TFTCentral's review (2024-04-25) didn't show them on the page I could read [S23].
- **Sibling MSI QD-OLEDs measured by TFTCentral:**
  - True Black 400 tops out at about 471 nits.
  - Peak 1000 reaches about 1000 nits only in 1–5% windows. At 10% of the screen and above it matches True Black 400.
  - Peak 1000's brightness curve rolls off in larger windows, so True Black 400 often *looks* brighter [S19].
- **MSI's Peak-1000 firmware fix** did not list the MAG 341CQP [S20].
- **What HDRpilot should do:** set the RTX HDR peak to match the monitor's active mode (about 450–470 nits in True Black 400, about 1000 in Peak 1000). Read the value from DXGI MaxLuminance or the calibration result rather than hardcoding it *(inference)*.

## 4. Supported APIs and setup conditions

- **Auto HDR**
  - APIs: DX11/DX12 at launch [S4]. Microsoft wrote "DX10 thru DX12" in 2023 [S6].
  - Only works in games Microsoft has marked eligible; Microsoft itself says some titles are hard to identify as eligible [S4]. Forcing it via registry (the ForceAutoHDR tool) is unofficial [S25].
  - No DX9, OpenGL or Vulkan, except through DXVK/VKD3D wrappers per ForceAutoHDR [S25].
- **RTX HDR** (official requirements [S1]):
  - APIs: DX9–12 and Vulkan; OpenGL is not supported.
  - Hardware and software: RTX 20 series or newer, single GPU, driver 565.90 or newer, Windows 11 22H2 or newer.
  - Displays: multi-monitor works, but not NVIDIA Surround or Clone mode. In a mixed HDR/SDR setup the game must launch on the HDR display.
  - Game settings: fullscreen or IFLIP presentation. Borderless windowed needs Windows' "Optimizations for windowed games" (this PC already has `SwapEffectUpgradeEnable=1`). Fast Sync must be off. In-game HDR and Auto HDR must be off.
  - Side effect: enabling it disables Multiplane Overlay (MPO).
  - Older 2024 guides also say DSR/DLDSR and NVIDIA Image Scaling must be off. That is not in the current FAQ [unverified].

## 5. Performance and latency

- **Auto HDR:** Microsoft says it "does take some GPU compute power" but expects no significant impact (2021) [S4]. Community testing found the cost within margin of error *(forum)*.
- **RTX HDR** (no rigorous review benchmark exists):
  - Typical cost reported by users: 5–10% [S14][3DCenter forum].
  - On an RTX 5080 in 3DMark Steel Nomad: −8.4%, versus −4.2% for NvTrueHDR (a community tool that enables the same driver feature) at its "low" quality level [S15] *(one run, synthetic benchmark)*.
  - The modder behind NvTrueHDR measured 450 → 380 fps, which is about +0.41 ms per frame [S12].
  - Outlier: one Steam user reported more than a 50% drop in Dying Light: The Beast on an RTX 3080 Ti [S18].
- **NVIDIA App filter overhead in general:**
  - Dec 2024: enabled Game Filters cost up to 15% even when unused; version 11.0.1 made them off by default [S16].
  - July 2026 (TechSpot test reported by PCGH): the overlay itself has no measurable cost, filters cost up to 10%. RTX HDR was not measured separately [S17].
- **Latency:** no published measurements for either feature. *Inference:* the RTX HDR cost is a roughly fixed GPU time per frame (about 0.4–1 ms). The percentage loss therefore grows at high fps, which matters most in GPU-bound competitive play close to the 175 Hz refresh cap.

## 6. Known failure cases

- **Auto HDR**
  - Stays greyed out in some games (Arc Raiders, Hell Let Loose Vietnam, Wardogs) [S7].
  - One user report of Easy Anti-Cheat blocking it in Fortnite [S26] *(single report)*.
  - Microsoft left GTA IV off the eligible list because colors came out oversaturated *(secondary source)*.
  - Digital Foundry on Xbox (2020): HUD overlays brighter than the gameplay, white road markings that "glow" [S27].
  - Unreal Engine games look blown out on Windows 11 24H2 [S25].
  - Lifted shadows (see section 1).
- **RTX HDR**
  - Sometimes fails to activate; Discord's overlay was one reported cause [forum].
  - Black screens reported on LG C4 TVs [S30].
  - When enabled globally, it can apply on top of a game's native HDR [S29].
  - NVIDIA can disable filters per game: Arc Raiders, Jan 2026. Forcing them with Profile Inspector was reported to trigger "Blocked Application" [S24] *(unofficial)*.
  - Debanding can remove detail (section 1).
- **Both**
  - Neither knows where the game's UI is, so white HUD elements go to full peak brightness [S31]. Lowering the peak or middle grey reduces this.
- **VRR flicker:** no source ties flicker to either feature. OLED VRR flicker is a near-black gamma behavior of the panel [S32]. Auto HDR's lifted shadows *might* make it more visible [speculative].

## 7. Community tools HDRpilot excludes

Special K, RenoDX/Luma and ReShade-based inverse tone mapping (e.g. Lilium) often give the best SDR→HDR results [S7]. All of them inject into the game:
- The Special K wiki says to close it before multiplayer games.
- BattlEye blocks ReShade.
- Luma for Counter-Strike 2 requires `-insecure` (no VAC servers) [S28].

At most, HDRpilot could show "PCGamingWiki lists an HDR mod" as information.

## 8. Decision rule for HDRpilot (per game, evaluated at launch)

```
if native HDR (PCGW) and not user-flagged "bad"  -> NATIVE: HDR on, RTX HDR profile off, AutoHDR per-app off
elif NVIDIA RTX (Turing+) && driver>=565.90
     && API in {D3D9,D3D10,D3D11,D3D12,Vulkan} (not OpenGL-only)
     && no Surround/Clone && game display is HDR
     && !competitive && NVIDIA hasn't blocked filters for the title
                                                  -> RTX HDR (per program), AutoHDR per-app OFF (mutually exclusive [S1]),
                                                     peak = display/calibration max for active mode, contrast +25,
                                                     mid-grey from table, saturation -50..-25
elif API in {D3D10,D3D11,D3D12} && Auto HDR eligible -> AUTO HDR (per-app on; global stays off)
else                                              -> SDR: do NOT switch Windows HDR on
                                                     (SDR-in-HDR uses sRGB curve [S8])
```

**When to prefer Auto HDR over RTX HDR:**
- Competitive or high-fps titles that are GPU-bound near the refresh cap, since Auto HDR costs about 0% versus 5–10%. For truly competitive games, SDR is also a reasonable default *(my recommendation)*.
- Non-NVIDIA or pre-RTX GPUs.
- NVIDIA Surround or Clone mode, or a game running on an SDR monitor in a mixed setup.
- Games where RTX HDR won't activate or NVIDIA has blocked filters.
- A user preference for zero overhead.

**When to prefer RTX HDR over Auto HDR:**
- DX9 or Vulkan games (Auto HDR can't run them).
- Games where Auto HDR is not eligible or stays greyed out.
- Dark games mastered for gamma 2.2, where Auto HDR's lifted blacks show.
- Games where per-game tuning is wanted.

Keep a per-game user override in all cases.

## 9. Open questions for implementation (unverified)

- The format of the per-app `AutoHDREnable` value is undocumented. The observed value `AutoHDREnable=2097` is not explained by any source I found.
- The NVIDIA driver-profile setting IDs for RTX HDR are undocumented. They are the hidden "TrueHDR" profile settings first found by a modder.
- The PCGamingWiki Cargo field names for HDR support, API and anti-cheat need checking; my fetch of the PCGamingWiki glossary returned 403.

## Sources
- [S1] NVIDIA App FAQ, KB a_id 5521, updated 2026-02-24 — https://nvidia.custhelp.com/app/answers/detail/a_id/5521
- [S2] NVIDIA, NVIDIA App beta, 2024-02-22 — https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-download/
- [S3] NVIDIA, RTX HDR multi-monitor, 2024-10-01 — https://www.nvidia.com/en-eu/geforce/news/nvidia-app-beta-update-g-sync-controls-rtx-hdr-multi-monitor/
- [S4] Microsoft DirectX blog, 2021-03-17 — https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/
- [S5] Microsoft DirectX blog, 2022-04-25 — https://devblogs.microsoft.com/directx/exciting-additions-to-your-auto-hdr-experience-on-pc/
- [S6] Microsoft DirectX blog, 2023-01-19 — https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page/
- [S7] HDRgameAnalysis, Aug 2026 — https://hdrgameanalysis.com/auto-hdr-windows-pc
- [S8] dylanraga, GitHub — https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm
- [S9] PCGH forum — https://extreme.pcgameshardware.de/threads/rtx-hdr-optimale-einstellungen.647542/
- [S10] r/nvidia, defet_, 2024-02-29 — https://www.reddit.com/r/nvidia/comments/1b3fds4/rtx_hdr_can_destroy_fine_picture_detail/
- [S11] NvTrueHDR (emoose), Nexus Mods — https://www.nexusmods.com/site/mods/781
- [S12] PCGamesN, 2024-02-06 — https://www.pcgamesn.com/nvidia/rtx-game-hdr
- [S13] StealthOptional (cites Digital Foundry), 2024-02-18 — https://stealthoptional.com/article/nvidia-rtx-hdr-crushes-windows-autohdr
- [S14] XDA — https://www.xda-developers.com/i-dismissed-nvidia-app-until-i-tried-rtx-hdr/
- [S15] r/nvidia RTX 5080 Steel Nomad post, seen via mirror reddit.birdcat.cafe, undated, single run
- [S16] Tom's Hardware, Dec 2024 — https://tomshardware.com/pc-components/gpu-drivers/nvidia-has-a-fix-for-up-to-15-percent-gaming-performance-loss-caused-by-the-nvidia-app-disabling-feature-restores-performance ; and https://www.tomshardware.com/pc-components/gpu-drivers/nvidia-app-performance-issue-gets-fixed-with-release-11-0-1-but-all-it-has-done-is-turn-off-game-filters-by-default
- [S17] PCGH, Jul 2026 — https://www.pcgameshardware.de/Nvidia-Geforce-Grafikkarte-255598/News/App-Neuer-Test-kaum-Fps-Verlust-Filter-kosten-bis-zu-10-Prozent-1546900/
- [S18] Steam, Dying Light: The Beast — https://steamcommunity.com/app/3008130/discussions/0/599663989456463248/
- [S19] TFTCentral, 2024-03-14, updated 2024-06-07 — https://tftcentral.co.uk/articles/testing-hdr400-true-black-and-peak-1000-mode-brightness-on-new-oled-monitors
- [S20] TFTCentral, 2025-01-16 — https://tftcentral.co.uk/articles/the-new-msi-firmware-update-properly-fixes-the-peak-1000-hdr-mode-for-many-of-their-qd-oled-monitors
- [S21] MSI — https://www.msi.com/news/detail/Brighter-colors--sublime-black-levels---MAG-341-CQP-QD-OLED-143115
- [S22] Notebookcheck — https://www.notebookcheck.net/MSI-MAG-341CQP-QD-OLED-released-as-new-curved-gaming-monitor-with-175-Hz-refresh-rate.804136.0.html
- [S23] TFTCentral MAG 341CQP review, 2024-04-25 — https://tftcentral.co.uk/reviews/msi-mag-341cqp
- [S24] allthings.how, 2026-01-03 — https://allthings.how/arc-raiders-has-blocked-nvidia-game-filters-what-that-means-and-what-you-can-do/
- [S25] ForceAutoHDR README — https://github.com/7gxycn08/ForceAutoHDR
- [S26] Microsoft Q&A — https://learn.microsoft.com/en-us/answers/a/6502239
- [S27] GamePro (on Digital Foundry Xbox test, 2020) — https://www.gamepro.de/artikel/xbox-series-xs-feature-hdr,3362378.html
- [S28] Special K wiki — https://wiki.special-k.info/SpecialK/Global ; Luma CS2 — https://www.nexusmods.com/counterstrike2/mods/20
- [S29] NVIDIA forum — https://www.nvidia.com/en-us/geforce/forums/nvidia-app/129/544378/rtx-hdr-will-sometimes-enable-on-top-of-native-hdr/
- [S30] NVIDIA forum — https://www.nvidia.com/en-us/geforce/forums/game-ready-drivers/13/579194/complete-blackscreen-rtx-game-hdr-game-filters-no/
- [S31] Guru3D forum — https://forums.guru3d.com/threads/nvtruehdr-tool-to-enable-rtx-hdr-in-games.451108/page-7
- [S32] XDA — https://www.xda-developers.com/vrr-flicker-is-the-monitor-problem-nobody-warns-you-about/ ; Notebookcheck — https://www.notebookcheck.net/VRR-Gamma-problems-with-LG-CX-and-C9-OLED-TVs-may-leave-PlayStation-5-and-Xbox-Series-X-gamers-with-a-headache.502464.0.html
- [S33] Microsoft Support, HDR Calibration app — https://support.microsoft.com/en-us/windows/calibrate-your-hdr-display-using-the-windows-hdr-calibration-app-f30f4809-3369-43e4-9b02-9eabebd23f19
- 3DCenter forum — https://www.forum-3dcenter.org/vbulletin/archive/index.php/t-617622.html