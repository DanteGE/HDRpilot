**Windows Auto HDR per-game control: findings for HDRpilot (research only, no settings changed)**

**Short answer:** yes, it is feasible without admin rights. Windows keeps a per-exe Auto HDR flag in HKCU, and Microsoft says it overrides the global switch. The global switch can stay off and HDRpilot can turn Auto HDR on only for the games it picks. Two limits:
- The flag only works on games Windows considers "eligible".
- Forcing Auto HDR on other games uses a second registry mechanism that Microsoft does not document.

There is no official API, so this is undocumented registry work.

---

## 1. Where Windows stores it

**Key:** `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. Every value there is REG_SZ in the form `Key=Value;Key=Value;`.

- **Global switch:** the value `DirectXUserGlobalSettings`.
  - `AutoHDREnable=1` means on and `0` means off.
  - `SwapEffectUpgradeEnable=1` is the "Optimizations for windowed games" switch.
  - Turning Auto HDR on also turns that optimization on. [MajorGeeks, undated](https://www.majorgeeks.com/content/page/how_to_turn_auto_hdr_on_or_off.html); [Microsoft Support "Optimizations for windowed games"](https://support.microsoft.com/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952)
- **Per-app values:** the value name is the exe's full path. Settings writes `AutoHDREnable=2097;` for on and `2096;` for off.
  - ForceAutoHDR.Net's source code calls these "Observed on Windows 11 26200". [Phoenix-/ForceAutoHDR.Net, 2026](https://github.com/Phoenix-/ForceAutoHDR.Net)
  - Your own Alan Wake 2 entry on build 26300 shows the same 2097.
  - Other tools read it as "odd = on". [universal-modder PR #97](https://github.com/rehan-remade/universal-modder/pull/97); [MaaEnd PR #5931](https://github.com/MaaEnd/MaaEnd/pull/5931)
- **Older encoding:** an ElevenForum tutorial gives `SwapEffectUpgradeEnable=1;AutoHDREnable=4147;` for on and `AutoHDREnable=48;` for off. [ElevenForum tutorial, undated](https://www.elevenforum.com/t/turn-on-or-off-auto-hdr-in-windows-11.891/)
- **What the bits mean (my guess, not documented anywhere):**
  - 2097 = 0x831, 2096 = 0x830, 4147 = 0x1033, 48 = 0x30.
  - Bit 0 is on/off in all four. Bits 0x10 and 0x20 are always set. 0x800 and 0x1000 differ between builds.
  - ForceAutoHDR.Net says "only bit 0 carries the on/off state". No source explains the higher bits.
- **Other fields in the same string:** `GpuPreference` (0 = let Windows decide, 1 = power saving, 2 = high performance; [NinjaOne](https://www.ninjaone.com/blog/restore-gpu-preferences-for-apps-in-windows/)), `SwapEffectUpgradeEnable`, `DXGIEffects` (Auto SR; [TheWindowsClub](https://www.thewindowsclub.com/enable-or-disable-automatic-super-resolution-asr)) and `AppStatus`.
  - I found no documentation for `AppStatus`. On your machine it appears as 0, 4 or 4096 on many game exes, so Windows probably writes it itself (unverified).
- **Things on your machine a parser must survive (read-only check):**
  - `GraphicsFeaturesNotificationConfig` is a DWORD, not an app.
  - The ScopeX.exe entry is the bare string `"0"`, not `Key=Value`.
  - The LosslessScaling.exe entry is REG_SZ with an embedded NUL and junk after it (`GpuPreference=0;\0` followed by two junk characters).

## 2. Global vs. per-app, and where the UI is

- **Per-app wins over global.** Microsoft: "your preference for the specific app will override the system-wide setting", and "even if you disable System-wide settings for a feature, you can enable it for a specific application". [DirectX blog, W. Ho, 2023-01-19](https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page/)
  - So global off plus per-game 2097 should work, as should global on plus per-game 2096.
  - I found no independent test of "global off, per-app on". It should be checked once on 26300.
- **Microsoft's own per-app defaults:** the same post says Microsoft "has disabled or enabled settings by default for some specific apps", and that the settings "only apply on eligible games".
- **UI locations:**
  - Per app: Settings > System > Display > Graphics > Custom options for apps > [app] > Options. Microsoft Support labels the control "Don't use Auto HDR", while the 2023 blog calls it a toggle.
  - This page exists since build 25281. [Windows Insider blog, 2023-01-19](https://blogs.windows.com/windows-insider/2023/01/19/announcing-windows-11-insider-preview-build-25281/)
  - Global: Settings > Display > HDR, the top of the Graphics page, or Game Bar > Settings > Gaming features > "Use Auto HDR with supported games".
  - Game Bar also has a per-game intensity slider; the far left equals off. [DirectX blog, 2022-04-25](https://devblogs.microsoft.com/directx/exciting-additions-to-your-auto-hdr-experience-on-pc/)
  - I could not find where the intensity value is stored.
- I found no Insider release note that changes Auto HDR for builds 26100, 26200 or 26300.

## 3. When changes take effect

- Microsoft says to save and then restart the game ([MS Support](https://support.microsoft.com/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952)). The Game Bar toggle also needs a relaunch ([DirectX blog, 2022-04-25](https://devblogs.microsoft.com/directx/exciting-additions-to-your-auto-hdr-experience-on-pc/)).
- Third-party tools write the value directly with no notification or refresh call, and report that it applies "the next time the game starts". [ForceAutoHDR.Net](https://github.com/Phoenix-/ForceAutoHDR.Net); [7gxycn08/ForceAutoHDR](https://github.com/7gxycn08/ForceAutoHDR)
- My inference: the Direct3D runtime reads it inside the game process when the game sets up rendering, so the value must be written before launch.
- **It must be the exe that actually renders.** Writing the setting to a launcher exe does nothing; ForceAutoHDR.Net's example is Riftbreaker's `Launcher.exe` vs `riftbreaker_win_release.exe`. [notes/gameconfigstore-is-the-real-game-list.md](https://raw.githubusercontent.com/Phoenix-/ForceAutoHDR.Net/HEAD/notes/gameconfigstore-is-the-real-game-list.md)

## 4. Requirements, limits, known problems

- **APIs:**
  - Microsoft says DirectX 10 through 12 ([2023 blog](https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page/)); the 2021 launch said DX11/DX12 ([DirectX blog, 2021-03-17](https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/)).
  - DX9, OpenGL and Vulkan are not supported natively.
  - Community workarounds: NVIDIA's "Vulkan/OpenGL present method: Prefer layered on DXGI swapchain" setting ([Special K forum, 2022](https://discourse.special-k.info/t/official-vulkan-support-eta/2421)), or a dgVoodoo2 wrapper for DX9 with mixed results ([Vogons](https://www.vogons.org/viewtopic.php?p=1308456)). Neither is something HDRpilot should do.
- **HDR must be on** before Auto HDR is available. [MS Support](https://support.microsoft.com/en-us/Windows/Hardware/Display-Graphics/use-auto-hdr-for-better-gaming-in-windows); [ElevenForum tutorial](https://www.elevenforum.com/t/turn-on-or-off-auto-hdr-in-windows-11.891/)
- **Windowed and borderless DX10/11 games** need the "Optimizations for windowed games" switch (`SwapEffectUpgradeEnable`), because it moves them to the newer presentation model that Auto HDR needs. [DirectX blog, 2022-02-16](https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/)
  - If a game's entry contains `SwapEffectUpgradeEnable=0`, Auto HDR will probably not engage (inference).
- **Microsoft's eligible-game list vs. forcing:**
  - Microsoft enables Auto HDR on "1000+" chosen games, some "hard to identify as Auto HDR eligible". [DirectX blog, 2021](https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/)
  - A forum summary of a Microsoft developer video says Windows also checks the game's output format. [Special K forum](https://discourse.special-k.info/t/how-does-windows-auto-hdr-compares-to-special-k-hdr-advantages-of-each/2122)
  - The per-app flag does not get around the eligible list.
  - **Forcing (undocumented):**
    - Key: `HKCU\Software\Microsoft\Direct3D\<subkey>`, with `Name` = the exe and `D3DBehaviors` = `BufferUpgradeOverride=1` (optionally `;BufferUpgradeEnable10Bit=1`). [ledoge/autohdr_force main.c](https://github.com/ledoge/autohdr_force/blob/master/main.c)
    - No admin needed.
    - It matches on the bare file name, so it hits every exe with that name. [ForceAutoHDR.Net notes](https://raw.githubusercontent.com/Phoenix-/ForceAutoHDR.Net/HEAD/notes/usergpupreferences-holds-non-app-values.md) ([Choum28](https://github.com/Choum28/AutoHDR) claims a full path works too; unverified)
    - Results are mixed even on DX11/12 games. [Steam, 2024-01](https://steamcommunity.com/app/1623730/discussions/0/4132683013937097833/)
- **Image quality:** community analysis says Windows shows SDR content in HDR mode with the piecewise sRGB curve instead of gamma 2.2, which raises blacks. [ledoge/win11hdr-srgb-to-gamma2.2-icm](https://github.com/ledoge/win11hdr-srgb-to-gamma2.2-icm); [Blur Busters](https://forums.blurbusters.com/viewtopic.php?p=111154)
  - That Auto HDR inherits this is community opinion ([OCUK forum](https://forums.overclockers.co.uk/threads/rtx-hdr-for-games.18984090/)), with no Microsoft measurement.
  - Other reported artifacts: over-bright menus, banding. [PCWorld, 2021-10-22, updated 2022-04-29](https://www.pcworld.com/article/546269/auto-hdr-deep-dive-how-and-why-to-use-windows-11s-eye-catching-game-feature.html)
- **Bugs:** a 24H2 issue made some games freeze or show wrong colors with Auto HDR on, and 24H2 was held back on such PCs. [TheFPSReview, 2024-12-19](https://www.thefpsreview.com/2024/12/19/windows-11s-auto-hdr-feature-may-cause-games-to-stop-responding-or-display-incorrect-colors-microsoft-reveals/) A forum copy of Microsoft's table lists it as resolved in February 2025 ([ElevenForum](https://elevenforum.com/t/known-and-resolved-issues-for-windows-11-version-24h2.29016/page-6)).
- **Performance:** Microsoft says it "does take some GPU compute power" ([2021](https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/)). One user on a 4080 measured it "within the margin of error", against 5–10% for RTX HDR ([r/nvidia, 2024](https://reddit.fabiomanganiello.com/r/nvidia/comments/1b3fds4/rtx_hdr_can_destroy_fine_picture_detail/)). These are anecdotes.
- **Anti-cheat:** this is an OS feature, and HDRpilot would only write HKCU values, so it needs no access to the game process (my inference; no source tests it).
  - One anecdote: Fortnite got Auto HDR when launched under BattlEye but not under Easy Anti-Cheat. [MS Q&A, 2022-05-12](https://learn.microsoft.com/en-us/answers/a/6502239)
  - My guess is that a different exe was started; this is speculation.
- **RTX HDR:** NVIDIA says Windows Auto HDR "should be disabled" when using RTX HDR, without saying globally or per game. [NVIDIA FAQ 5521, updated 2026-02-24](https://nvidia.custhelp.com/app/answers/detail/a_id/5521) Whether a per-game 2096 is enough is unverified.

## 5. Official API

None found: no documented API, Group Policy or Intune setting for per-app Auto HDR. Microsoft documents only the Settings UI. Both registry mechanisms are undocumented. The `AutoHDR.ScreenSplit` debug key (left half SDR, right half Auto HDR) is in HKLM and needs admin, so it is only useful as a manual check by the user. [DirectX blog, 2021](https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/)

## 6. How HDRpilot should do it (robust and reversible)

1. **Leave `DirectXUserGlobalSettings` alone.** Keep the global switch off and control everything per game.
2. **Target the rendering exe's full path**, which HDRpilot already learns. Write before launch. An exe learned during a session only takes effect from its next launch.
3. **Read safely:** read the raw value type and bytes. Only edit a REG_SZ that splits cleanly into `Key=Value;` tokens; cut it at the first NUL. Otherwise (DWORD, bare `"0"`, junk after a NUL) don't touch it and log why.
4. **Change only the `AutoHDREnable` token.** Keep token order, key casing, unknown keys and the trailing `;`. Add the token at the end if it is missing.
   - If a value already exists, keep its high bits and set only bit 0 (`(v & ~1) | on`); this is an inference.
   - Otherwise write 2097 or 2096, which this machine's own Alan Wake 2 entry confirms.
5. **Reversibility:**
   - Before writing, save the original raw type and bytes (or "value absent") plus the exact string written to HDRpilot's state file.
   - Restore only if the current value still equals what HDRpilot wrote. If not, the user changed it in Settings; keep their change and log it.
   - If the value was absent and Windows has added other tokens since, remove only HDRpilot's token rather than deleting the value.
   - Recover on startup after a crash, the same way the HDR toggle is presumably restored.
   - Simpler option: the flag only affects that one exe and only while HDR is on, so it can stay set permanently while the game is managed. Clean up when the game is removed or HDRpilot is uninstalled; no toggling per session.
6. **If enabling while the entry has `SwapEffectUpgradeEnable=0`:** warn the user, or set it to 1 and restore it afterwards (inference: windowed DX10/11 games need it).
7. **For games using RTX HDR:** write 2096 explicitly for that exe.
8. **Forcing (`D3DBehaviors`):** offer it only as an opt-in experimental option.
   - Track the subkeys HDRpilot creates and remove only those.
   - Warn about generic exe names like `Game.exe` or `Launcher.exe`.
9. **Checking it works:** HDRpilot cannot confirm Auto HDR is active from outside without hooking the game. Ask the user to check the Game Bar intensity slider (brightness should change) or the "Auto HDR" notification. [7gxycn08/ForceAutoHDR](https://github.com/7gxycn08/ForceAutoHDR)

**Still to test on build 26300:**
- Does "global off + per-app 2097" work on a game Microsoft lists as eligible?
- Does 2097 have any effect on a game that is not on the list?
- Which exact strings does Settings write when toggling a test exe? Toggle once and read the value back.