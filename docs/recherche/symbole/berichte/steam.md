All 633 Steam games in after-config.json get a usable icon from local Steam files, offline and read-only. Two things the task assumed were wrong on this machine:
- **Registry location:** the "Steam App <appid>" Uninstall entries are not under `WOW6432Node`. That view has 0 of them. All 798 are in the 64-bit view, `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App <appid>`, because `steam.exe` is now x64 (PE machine 0x8664).
- **Uninstalled games are already covered:** the config only contains installed games (633/633 have `isInstalled` true, and all have a manifest). The uninstalled numbers below come from Steam's own files.

## Method
I read the files in place and wrote nothing outside my folder. I compiled two small C# programs with Roslyn `csc.dll` against the .NET 10 reference packs; `dotnet build` failed because the scratch path is too long. A third set of checks was ad-hoc PowerShell.
- **Program 1:** reads after-config.json, parses all of `appinfo.vdf`, reads the appmanifest files from `libraryfolders.vdf`, lists `librarycache` and `steam\games`, reads the registry values, and reads `.url` files on the Desktop, Public Desktop and both Start Menus. It reads JPEG sizes from the SOF marker, and ICO sizes from the icon directory or, for PNG-compressed entries, the PNG header.
- **Program 2:** finds the best .ico per game, calls `System.Drawing.Icon.ExtractIcon(path, 0, 48)` on it, and decodes the librarycache JPEG with WPF `BitmapImage` (`DecodePixelWidth=48`), the WPF image decoder.
- **PowerShell checks:** the empty Uninstall keys, icon-JPEGs per folder, whether the file name equals its SHA-1, sample asset sizes and the `steam.exe` bitness.

## (1) Library cache: `C:\Program Files (x86)\Steam\appcache\librarycache`
- **Layout:** one folder per appid (4501 folders) plus one binary `assetcache.vdf` that lists the files per app. There are no flat `<appid>_icon.jpg` files any more.
- **The icon** is the only top-level `<40-hex>.jpg` in `<appid>\`. The name is the SHA-1 of the file content and equals `appinfo` `common/icon` in 628 of 628 cases.
  - It is always a 32×32 JPEG: square, no transparency, 417 to 2183 bytes, median 855.
  - WPF decoded all 628 to 48×48 in 352 ms total (602 colour, 26 greyscale).
  - Some folders have newer assets in `<hash>\` subfolders, but never the icon.
- **Other files**, none of them square: `header.jpg` 460×215, `library_600x900.jpg` (actually 300×450), `library_hero.jpg` 1920×620, `logo.png` 640×N with transparency. Some have `_german` variants.
- **Folders by icon count:** 1559 have no icon JPEG, 2890 have one, 52 have two or more. None of the config games are in the 52. When there are several, use `common/icon` to pick the right one.
- **Coverage:** 628 of 633 games (99.2%). The 5 without an icon are 3 Playtests, whose `appinfo` entry has no "common" block, and 2 Demos without a librarycache folder.

## (2) Client icons: `steam\games\<hash>.ico`
- **Count:** 1061 .ico files, 148 MB in total.
  - 941 belong to some app's `common/clienticon` in `appinfo`; 120 belong to none.
- **`appinfo.vdf`:** about 14.9 MB, magic 0x07564429 (version 29). After the magic come a UInt32 universe value and an Int64 offset to the key-name table (here about 14.8 MB into the file, holding about 18,000 names).
  - Each app record is: UInt32 appid, UInt32 size, a 60-byte header, then binary KeyValues. Each key is an Int32 index into that table.
  - Reading the whole file took 4 ms; parsing the "common" strings of all 5244 apps took 64 ms.
- **Parser:** the repo already has one that handles versions 27, 28 and 29: `src\HdrPilot.Core\Games\Scanners\Steam\SteamAppInfoReader.cs`. It only needs to also return `common/clienticon` and `common/icon` (it currently returns type and name). `common/clienttga` is set for 607 games, but no .tga files exist locally.
- **Coverage of `steam\games\<clienticon>.ico`:** 609 of 633 (96.2%).
  - The 24 misses are 3 Playtests and 21 games whose `clienticon` changed. For those 21 the old .ico is still there and the registry and shortcuts point to it.
- **Registry `DisplayIcon` (64-bit view):** 633 of 633 have a value, and 632 of those files exist.
  - 607 point to `steam\games\<current clienticon>.ico`; 25 point to an older .ico that still exists.
  - Half-Life: Alyx has an empty value but its clienticon .ico exists.
  - The other 159 keys belong to apps that are not installed and are empty.
- **`.url` shortcuts:** 1606 `.url` files in total, 1577 of them start a Steam game, covering 944 different appids.
  - 926 are on the Desktop (838 of them in `Desktop\Games`) and 651 in `%APPDATA%\...\Start Menu\Programs\Steam`. Public Desktop and ProgramData had none.
  - 633 of 633 games have one; 632 have an `IconFile` that exists, always pointing to `steam\games\*.ico`. They depend on the user's shortcut habits, so they are the weakest source.
- **Size of the best .ico per game** (current one, else registry, else shortcut):

| Largest image | Games |
|---|---|
| 32 px | 249 |
| 48 to 240 px | 87 |
| 256 to 1024 px | 297 |

  - 384 games have at least 48 px, 298 at least 256 px; 573 have a 32-bit or PNG image (transparency).
  - The existing `Icon.ExtractIcon(path, 0, 48)` works on these .ico files as it does on EXEs: 633 of 633 returned 48×48, 69 ms in total. No new decoding code is needed.

## (3) Coverage of the 633 Steam games

| Source | Games |
|---|---|
| librarycache JPEG | 628 (99.2%) |
| Current clienticon .ico | 609 (96.2%) |
| Registry `DisplayIcon` file exists | 632 |
| `.url` `IconFile` exists | 632 |
| Any .ico (clienticon, registry or shortcut) | 633 (100%) |
| All sources combined | 633 (100%) |

Games with no local icon at all: 0. On their own, the librarycache JPEGs are always 32 px, and the .ico files give no more than 32 px for 249 games. For those, the game's own EXE icon may still look better.

## Uninstalled owned games
- **librarycache:** 3866 folders belong to apps without a manifest. 2007 of them are games, and all 2007 have the 32×32 icon JPEG. `localconfig.vdf` lists 778 not-installed games, and 775 of those have the JPEG.
- **.ico:** only games installed at some point. 297 of the 2007 have their clienticon .ico, and 301 not-installed games across all of `appinfo` do. Of the 778, 288 have it.
- **Shortcuts:** leftover shortcuts cover 305 not-installed appids, all with an `IconFile` that exists. The registry has nothing useful: the 159 keys for uninstalled apps are empty.

## Suggested fallback order for Steam, all offline
1. `steam\games\<common/clienticon>.ico`
2. The registry `DisplayIcon` (64-bit view, with the 32-bit view as a fallback for older Steam clients)
3. `IconFile` from `Start Menu\Programs\Steam\*.url`
4. `librarycache\<appid>\<common/icon>.jpg`, or the only top-level 40-hex `.jpg`, decoded at 48 px. Note that this is a scaled-up 32 px image without transparency.

Paths 1 to 3 go through the existing `ExtractIcon`; only step 4 needs a WPF image decode. Steam's CDN also serves `<icon>.jpg`, but that would need network access, which is your decision.

Files are in `<Notizordner>\icons-research\steam-local\`:
- out\summary.txt
- out\summary2.txt
- out\per-game.json
- probe\Program.cs
- probe2\Program2.cs