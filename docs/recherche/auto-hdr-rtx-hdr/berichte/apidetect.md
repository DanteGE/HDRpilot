# HDRpilot: finding the graphics API and target EXE per game without touching the game process

## Bottom line

- **Classify each EXE, not each game, and only coarsely.** HDRpilot doesn't need "D3D11 vs D3D12". The useful classes are: **DXGI (D3D10–12)**, **D3D9**, **Vulkan**, **OpenGL**, and **legacy** (DDraw / D3D≤8 / Glide).
  - Windows Auto HDR officially covers D3D11/D3D12 [MS-AHDR]. D3D10/11 in a window only work through the flip-model upgrade ("Optimizations for windowed games") [MS-WOG].
  - RTX HDR officially covers DX9, DX11, DX12 and Vulkan [NV-RTXHDR]. NVIDIA does not list OpenGL. PCGamingWiki claims OpenGL works too [PCGW-HDR]; this is unverified.
  - Vulkan and OpenGL get Auto HDR only through the NVIDIA "Prefer layered on DXGI swapchain" present method (secondary source only) [PCGW-HDR].
- **No single source is reliable on its own.** PCGamingWiki gives the set of APIs a game supports, but not which one is the default. Static PE inspection is often inconclusive: it failed or misled on 9 of the 15 local EXEs I checked. Launch arguments, game config files and the learned runtime EXE resolve most of the remaining cases.
- **Use a ranked combination with confidence levels.** If HDRpilot ends up with several possible APIs, or none, it should pick the feature that covers all of them. On NVIDIA that is normally RTX HDR.
- **The two features are keyed differently.**
  - Auto HDR is keyed by the **exact full Win32 path of the process that renders**, for example the `*-Win64-Shipping.exe`, not the launcher stub.
  - NVIDIA DRS is keyed by **lower-case EXE name, or by a path suffix with forward slashes**. My read-only DRS probe showed that the predefined profile is missed if a game sits in a folder with a different name.

## 1. PCGamingWiki

**Cargo schema** (checked live on 2026-10-10 with `action=cargofields`) [PCGW-CF]:

- **Table `API`:**
  - API version lists: `Direct3D_versions`, `DirectDraw_versions`, `OpenGL_versions`, `Glide_versions` and `Vulkan_versions`. These are comma-delimited lists of String.
  - Single values: `WinG`, `Software_renderer`, `Mantle_support` and `Metal_support`.
  - Executable flags: `Windows_32bit_executable`, `Windows_64bit_executable` and `Windows_ARM_app`, plus Mac/Linux equivalents.
- **Table `Video`:** the HDR field is `HDR` (String). Values seen: true, false, limited, always on, hackable, unknown.
- **Table `Game`:** `Steam_AppID` and `GOGcom_ID` (lists of String) and `Engines` (list of Page). There is no table called `Infobox_game`.
- **Table `Mods`:** fields `ModName`, `GameName`, `GameNameEnc`, `Type`, `State`. `Type="HDR"` with `State="Playable"` lists HDR mods.

**How the values are normalised** (Template:API and Template:API/switch Direct3D) [PCGW-TAPI]:

- `Direct3D_versions` is stored from a normalised variable, with values such as `9.0`, `9.0c`, `9Ex`, `10`, `11`, `11.1`, `12`. Input "12u" is stored as "12".
- `Vulkan_versions` holds versions such as `1.1`, or `true` / `hackable`.
- Any D3D 9 or higher sets `HDRCompatibleAPI`. Template:Video then shows the "retrofit HDR" hint when `hdr` is false or unknown. So the wiki itself treats D3D9+ games as candidates for retrofitted HDR.

**Anonymous route (no account).** HDRpilot already downloads the full page wikitext, so reading these parameters costs no extra request:

- From `{{API`: `direct3d versions`, `direct3d notes`, `vulkan versions`, `vulkan notes`, `opengl versions`, `directdraw versions`, `glide versions`, `software mode`, `windows 64-bit exe`.
- From `{{Video`: `hdr`, `hdr notes`.
- From the infobox: `{{Infobox game/row/engine|…}}`, `steam appid`, `steam appid side`, `gogcom id`.

Anonymous `cargoquery` is rejected with *"You don't have permission to run arbitrary Cargo queries."* (tested 2026-10-10).

**Account route.** This extends HDRpilot's existing `Game,Video` join. Batch with `OR` over the installed AppIDs:

```
action=cargoquery&tables=Game,Video,API
&join_on=Game._pageID=Video._pageID,Game._pageID=API._pageID
&fields=Game._pageName=Page,Game.Steam_AppID=Steam,Game.GOGcom_ID=GOG,Game.Engines=Engines,Video.HDR=HDR,
        API.Direct3D_versions=D3D,API.Vulkan_versions=Vulkan,API.OpenGL_versions=OpenGL,
        API.DirectDraw_versions=DDraw,API.Glide_versions=Glide
&where=Game.Steam_AppID HOLDS "1091500"
```

**Coverage** (rendered list pages, 2026-10-10):

| API | Games listed |
|---|---|
| Direct3D 11 | 4,689 |
| Direct3D 9 | 3,355 |
| OpenGL | 1,656 |
| Direct3D 12 | 881 |
| Vulkan | 297 |

`Category:Games` has 55,659 pages.

- Overall, API data looks sparse. D3D12 in particular looks under-filled (my inference).
- For popular titles coverage is good: all 12 of 12 locally installed games that I found on the wiki had API data.
- **What the wiki cannot give you:**
  - The default API. It sits only in free-text notes, for example Palworld "DirectX12 requires the -dx12 launch argument" and Valheim "Default API on Windows".
  - Which EXE belongs to which API.
  - Title matching can fail. "LEGO Builder's Journey" had no page under that name, so match by AppID or GOG ID.

## 2. Static PE inspection (file on disk only)

I scanned the files read-only on this machine (import table, delay-import table, exports, DLL-name strings):

| EXE | What the static scan shows | Actual API |
|---|---|---|
| `bg3.exe` | No graphics imports; delay-loads `dxgi.dll`; string `vulkan-1.dll` | Vulkan (loaded dynamically) |
| `bg3_dx11.exe` | Imports `d3d11.dll` | D3D11. A **separate EXE per API**; GOG lists both as "launcher process" playTasks |
| `Cyberpunk2077.exe` | **No graphics imports at all**; exports `D3D12SDKVersion`/`D3D12SDKPath`; `D3D12\D3D12Core.dll` present | D3D12 |
| `bf6.exe` (protected: 1 import, `.vm_sec`/`.vlizer` sections), `afop.exe` (packed sections) | Delay-loads `d3d12` / imports `dxgi` only; Agility SDK exports | D3D12 |
| `DyingLightGame_TheBeast_x64_rwdi.exe` (1.5 MB) | No graphics imports; Agility SDK exports; renderer lives in `runtime_dx11.dll` / `runtime_dx12.dll` | Selectable in `Documents\dying light the beast\out\settings\video.scr` → `RendererMode("d3d12")` |
| Unreal `*-Win64-Shipping.exe` (Dead Island 2, Witchfire, Showgunners) | Imports **d3d9 + d3d11 + opengl32 + dxgi**, delay-loads d3d12 | Static result is meaningless. PCGW says DX12 for DI2 and Witchfire |
| Unreal root stubs (`DeadIsland.exe` is even x86, `Witchfire.exe` 229 KB) | Nothing graphics-related | Not the rendering process |
| Unity stubs | Import only `UnityPlayer.dll`, which imports only `opengl32` | Unity default not visible statically |
| `RDR2.exe` | Imports **only `d3d9.dll`** | Actually Vulkan/DX12. Documents `…\Settings\system.xml` has `<API>kSettingAPI_Vulkan</API>` |
| `swat4.exe` | Imports `ddraw.dll` | PCGW says D3D9 |
| `UnrailedGameEpic.exe` | 0 imports, only `.text`/`.rsrc` (packed); ANGLE `libGLESv2` in folder | Unknown statically |
| `C:\XboxGames\…\Wolkenhof-WinGDK-Shipping.exe` | **Access denied** for a normal user | — |

**Signals worth using:**

- A `D3D12SDKVersion` export plus `D3D12Core.dll` (default `.\D3D12\`, but it may sit next to the EXE) means "D3D12-capable". The export is required in the main EXE [MS-AGILITY]. It doesn't prove D3D12 is the active API: Dying Light also ships a DX11 renderer.
- A direct `d3d11.dll` import, on a single-API, non-Unreal EXE, means D3D11.
- `vulkan-1.dll` appearing only as a string means "Vulkan-capable".

**Pitfalls:**

- Dynamic loading through LoadLibrary, Vulkan loaders, render DLLs (Unity, Chrome Engine, Source), and engines that link every API.
- `d3d9` and `ddraw` imported only for utility functions.
- Packers and DRM.
- Xbox GDK access control.
- Local proxy DLLs such as `d3d11.dll`/`dxgi.dll` in the game folder (DXVK, ReShade, Special K). Worth flagging separately.

Reading the file is anti-cheat safe. Open with FileShare.ReadWrite|Delete, read-only.

## 3. Steam and other store metadata

**Steam `appcache\appinfo.vdf`.** On this machine it is format v41, magic `0x07564429`. Keys are indices into a string table at the offset in the header (since June 2024) [STEAMDB]. The relevant path is `appinfo/config/launch/<n>/` with `executable`, `arguments`, `type` (default/option1…), `description`, `config/oslist`, `config/osarch` and `config/BetaKey`.

Local examples:

- **Valheim:** `valheim.exe` "-force-vulkan" ("Valheim using Vulkan").
- **Ready or Not:** `ReadyOrNot.exe` "-dx12" / "-dx11". This is the Unreal stub.
- **Killing Floor 3:** `start_protected_game.exe`, the EAC bootstrapper.
- **Sniper Elite Resistance:** `bin\sniperresistance_dx12.exe`; the API is in the file name.
- **Hades II:** the normal branch carries `BetaKey="public"`.
- **Borderlands 4:** many internal BetaKey entries.

Filter rules: oslist includes windows, and BetaKey is absent, "public", or the user's own branch. User-set launch options sit in `userdata\<id>\config\localconfig.vdf` under `"LaunchOptions"`; one local value is `-dx12 -USEALLAVAILABLECORES`. HDRpilot's own `--wrap -- %command%` wrapper already sees the exact EXE and arguments at launch.

**Other stores:**

- **Epic** `.item`: `LaunchExecutable` is usually a stub (DeadIsland.exe, Witchfire.exe, PlayRDR2.exe).
- **GOG** `goggame-*.info`: `playTasks`, including non-primary "launcher process" entries.
- **Xbox** `MicrosoftGame.config`: `<Executable Name="Hof/Binaries/WinGDK/Wolkenhof-WinGDK-Shipping.exe">`. This file is readable even though the EXE is not.

## 4. How each feature is keyed

**Windows Auto HDR (per app):**

- Value under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`. The value name is the full Win32 EXE path (local `AlanWake2.exe` entry; ElevenForum tutorial).
- Registry key names and value names are not case sensitive [MS-REGKEY][MS-REGDEL].
- The value data is a list of tokens. Merge new tokens into it rather than overwriting.
- On this machine, Windows itself created `AppStatus=4096` entries keyed by the real rendering EXEs, the `…-Win64-Shipping.exe` files, not the stubs. Their meaning is **undocumented / unverified**.
- **Speculative:** path-form problems (junctions, subst drives, 8.3 names, NT→DOS path conversion). Check against entries Windows has already written.
- The meaning of `AutoHDREnable` values (2097 vs 4147/48 in the tutorial) is outside this topic.

**NVIDIA DRS** [NVAPI]: `NVDRS_APPLICATION_V4` has the fields `appName`, `launcher`, `fileInFolder` (":"-separated), `isMetro` and `isCommandLine`/`commandLine`. `FindApplicationByName` with a full path *"will always return the profile the driver will apply"*, and NVAPI recommends absolute paths for `CreateApplication`.

My read-only probe (the installed driver, no SaveSettings) showed:

- **Stored names are lower case and matching ignores case:** `BG3.EXE` matched `bg3.exe`.
- **Predefined entries can be path suffixes:** for example `deadisland2/deadisland/binaries/win64/deadisland-win64-shipping.exe`. Full paths with either slash direction resolve correctly.
- **A relocated install misses the profile.** `E:\Games\DI2\…\DeadIsland-Win64-Shipping.exe` and the bare name both return −166 `NVAPI_EXECUTABLE_NOT_FOUND`. GDK `Wolkenhof-WinGDK-Shipping.exe` has no profile at all.
- **Generic names map to shared profiles:** `game.exe` → profile "Game.exe", and `launcher.exe` → profile "launcher", even when given a full path. Never write settings there.
- **Profiles bundle launchers:** for example `cplauncher.exe` and `witchfire.exe` share the game's profile.
- **Known NVAPI bug:** `CreateApplication` with a full path fails with −167 ALREADY_IN_USE if the bare name is in another profile. Profile Inspector works around this [NPI].

## 5. Recommended approach for HDRpilot

**1. Target EXE.** Use the first available of:

1. The learned runtime EXE: the owner of the game window, found via SystemProcessIdInformation, which HDRpilot already does.
2. Store metadata with stub and launcher rules:
   - Unreal: if `X.exe` sits at the root and `<Name>\Binaries\Win64|WinGDK\<Name>-Win64|WinGDK-Shipping.exe` exists, target the Shipping EXE.
   - Skip launchers and anti-cheat bootstrappers such as `start_protected_game.exe`, `EAAntiCheat.GameServiceLauncher.exe`, `gamelaunchhelper.exe`, `*prelauncher*`, `LariLauncher`.
   - GOG non-primary playTasks.
3. Several candidates in parallel, until the first run confirms the right one.

Settings must be in place before the process starts, so the very first launch may miss. **Unverified:** whether either setting is read only at process start.

**2. API class per EXE.** Rank the evidence like this:

- **(a) Runtime ETW (optional).** PresentMon-style tracing gives Runtime DXGI/D3D9/Other and the present mode (flip model). It needs membership in "Performance Log Users" [PRESENTMON].
- **(b) Launch arguments.** From the wrapper command line, Steam LaunchOptions or appinfo options: `-dx11/-d3d11/-force-d3d11`, `-dx12/-d3d12/-force-d3d12`, `-vulkan/-force-vulkan`, `-force-glcore/-opengl`.
- **(c) Known config files.** Seen locally:
  - Unreal: `[D3DRHIPreference] bPreferD3D12InGame` / `PreferredRHI=dx12`, and `Engine.ini DefaultGraphicsRHI=DefaultGraphicsRHI_Vulkan`.
  - RDR2: `system.xml`.
  - Dying Light: `video.scr`.
- **(d) EXE name tokens:** `_dx11`, `_dx12`, `_vulkan`.
- **(e) PCGW API set plus engine.** If the game supports only one API, use it.
- **(f) Static PE:** only for Agility, D3D11-import or Vulkan-string hints, otherwise "unknown".

Store the class with its source and a confidence level, and offer a user override.

**3. Choosing the feature.**

| Class | Feature |
|---|---|
| DXGI | Auto HDR or RTX HDR, by preference |
| D3D9 or Vulkan | RTX HDR only |
| OpenGL | RTX HDR, not officially supported |
| Legacy | None |
| Mixed or unknown | RTX HDR (covers DX9–12 and Vulkan) |

Never enable both at once. NVIDIA and reviewers say Auto HDR should be off with RTX HDR (secondary sources).

**4. Applying it.**

- **Auto HDR:** write the registry value for the exact path, merging tokens.
- **RTX HDR:**
  1. Call `FindApplicationByName(fullPath)`.
  2. If the result is a game-specific profile, set RTX HDR there and record the previous values.
  3. If the result is −166 or a generic profile, create an HDRpilot-owned profile with a path-qualified application entry (mind the −167 bug).

  Not checked here: whether DRS writes need administrator rights.

## Unverified / open

- Whether the DXGI runtime looks up the per-app value by the GetModuleFileName path or the resolved path.
- What `AppStatus` means.
- Whether user-created DRS entries can use the path-suffix form.
- OpenGL support in RTX HDR.
- Whether D3D10 counts for Auto HDR is inferred from [MS-WOG] and PCGW tests only.

## Sources

Accessed 2026-10-10 unless noted.

- [PCGW-CF] https://www.pcgamingwiki.com/w/api.php?action=cargofields&table=API (also `Video`, `Game`, `Mods`), and `action=cargotables`
- [PCGW-TAPI] https://www.pcgamingwiki.com/w/api.php?action=parse&page=Template:API&prop=wikitext; also `Template:API/switch_Direct3D` and `Template:Video`
- [PCGW-HDR] https://www.pcgamingwiki.com/wiki/Glossary:High_dynamic_range_(HDR) (read via api.php); list pages `List_of_Direct3D_12_games` and related
- [MS-AHDR] https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/ (2021-03-17)
- [MS-WOG] https://support.microsoft.com/en-us/windows/optimizations-for-windowed-games-in-windows-11-3f006843-2c7e-4ed0-9a5e-f9389e535952
- [MS-AGILITY] https://devblogs.microsoft.com/directx/gettingstarted-dx12agility/ (2021-04-20)
- [MS-REGKEY] https://learn.microsoft.com/en-us/windows/win32/sysinfo/structure-of-the-registry (updated 2025-04-15)
- [MS-REGDEL] https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regdeletevaluea (updated 2025-07-01)
- ElevenForum Auto HDR tutorial (secondary source, updated 2026-09-16): https://www.elevenforum.com/t/turn-on-or-off-auto-hdr-in-windows-11.891/
- [NV-RTXHDR] https://www.nvidia.com/en-eu/geforce/news/nvidia-app-beta-update-g-sync-controls-rtx-hdr-multi-monitor/ (2024-10-01)
- [NVAPI] https://github.com/NVIDIA/nvapi `nvapi.h` (last commit 2026-09-18)
- [NPI] https://github.com/Orbmu2k/nvidiaProfileInspector: `DrsSettingsServiceBase.cs` (MitigateNvapiAlreadyInUseBug) and `NvapiDrsWrapper.cs` (status codes −166/−167/−182)
- [STEAMDB] https://github.com/SteamDatabase/SteamAppInfo README
- [PRESENTMON] https://github.com/GameTechDev/PresentMon README (2025-02-13) and README-ConsoleApplication.md
- Local read-only evidence on this machine: PE scans, `appinfo.vdf`, store manifests, `UserGpuPreferences`, and the DRS probe

The read-only research tools are in the scratchpad (`<Notizordner>`), all reusable for HDRpilot:
- `PeScan.cs` (PE scanner)
- `AppInfo.cs` (appinfo v41 parser)
- `apires\DrsProbe.cs` (DRS lookup)