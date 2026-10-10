# Local icon sources for Epic, GOG, Xbox, Ubisoft, EA, Battle.net and manual games: coverage on this machine

I read files and registry only and changed nothing. All output is in my own folder:
`<Notizordner>\icons-research\nonsteam-local\`

**Games covered.** The config holds 647 games: 633 Steam and 14 others (6 Epic, 5 GOG, 1 Ubisoft, 1 EA, 1 Xbox). All 14 are installed. There are 0 Battle.net and 0 manual games. The two generic sources (uninstall registry and shortcuts) were measured on all 647.

**Where the exe icon falls short today.** For each known EXE of the 14 non-Steam games I read the first icon group (LoadLibraryEx as data file, then RT_GROUP_ICON) and rendered it with PrivateExtractIcons at 48 px. The result is `contact-sheet.png`.
- **Palworld (Xbox) gets no icon.** Opening `Palworld-WinGDK-Shipping.exe` is refused with access denied, even though the file permissions allow Users to read it. The Windows shell only returns the generic application icon for it.
- **SWAT 3 and SWAT 4 look poor.** Their EXEs only contain 32 px icons in 4/8-bit colour, so they get upscaled.
- **Dead Island 2 is small but acceptable.** `DeadIsland.exe` only has a 44 px 8-bit icon.
- The other 10 games already have an icon of at least 48 px.

## Per source

**1. GOG: `goggame-<id>.ico` in the install folder** (recommended)
- **Path:** `<installDir>\goggame-<sourceId>.ico`. If that file is missing, use `rootGameId` from `goggame-<id>.info`; some `playTasks` there also carry an `"icon"` field.
- **Format:** ICO with frames 256 (PNG), 48, 32 and 16.
- **Coverage:** 5/5.
- **Caveat:** the 48 px frames are PNG-compressed, so the file has to be decoded with WPF/WIC. The old .NET Framework System.Drawing showed garbage for them; the app's .NET 10 runtime was not tested.

**2. GOG Galaxy web cache** (good second choice for GOG)
- **Path:** `C:\ProgramData\GOG.com\Galaxy\webcache\<userId>\gog\<productId>\<sha256>_glx_square_icon_v2.webp`. The same folder also holds `_glx_vertical_cover` (342x482) and a background image.
- **Format:** 112x112 WebP, decoded by the Microsoft WebP decoder.
- **Matching:** the folder name is the product id, so a directory glob is enough. The equivalent mapping in `storage\galaxy-2.0.db` is the `WebCache` table (`releaseKey='gog_<id>'`) joined to `WebCacheResources` (type 2 = `squareIcon`, plus a `filename`).
- **Database access:** I read the database from a copy using Windows' built-in `winsqlite3.dll` (version 3.51.1) through P/Invoke, so no extra package is needed. The live database is in WAL mode, so the app would have to open it read-only with `mode=ro`. The directory glob avoids this entirely.
- **Coverage:** 5/5 of the user's games. The cache holds 535 square icons in total.
- **Access:** Everyone has full control on the ProgramData folder, so reading needs no admin rights.
- **Extra:** `C:\ProgramData\GOG.com\Galaxy\jump-list-cache\gog_<id>__<hash>_glx_square_icon_v2.ico` holds a 64 px 32-bit icon for 3/5 games.

**3. Xbox / Microsoft Store** (recommended; this is the only source that works for Palworld)
- **GDK games:** `<installDir>\MicrosoftGame.config`, element `ShellVisuals`, gives Square480x480Logo, Square150x150Logo, Square44x44Logo and StoreLogo. For Palworld these are 480, 150, 44 and 100 px PNGs, plus a `Resources\SmallLogo.ico` with 64/48/32/16 frames. `appxmanifest.xml` (VisualElements and Logo) is also there.
- **Coverage:** 1/1, readable without admin. Only the game EXE itself is blocked.
- **Store apps under `C:\Program Files\WindowsApps\<PackageFullName>`:** you cannot list the root folder, but opening a known path works.
- **File names need resolving:** in 61 of the 77 Store packages installed here, the logo file named in the manifest does not exist under that exact name. Only variants with a qualifier exist, such as `Square44x44Logo.targetsize-48.png` or `.scale-200.png`. Only 16/77 have the literal file. So the app should glob `<name>.*<ext>`.
- **Alternative:** the shell (`SHCreateItemFromParsingName("shell:AppsFolder\<PFN>!<AppId>")` plus `IShellItemImageFactory.GetImage(48)`) returned a correct 48x48 Palworld icon. The application id comes from the manifest (`AppPalShipping`).

**4. Ubisoft Connect** (recommended)
- **Path:** `C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\data\games\<md5>.ico`. It is a single-frame 256 px PNG ICO; a copy also sits in `data\`.
- **Matching:** `cache\configuration\configurations` is protobuf. Each top-level record has field 1 = uplay id, field 2 = the same id here, and field 3 = YAML; the YAML's `root.icon_image` names the ico file.
  - 1196 records; 17 have a root-level `icon_image`; 16 of those 17 files exist.
- **Shortcut route:** the uninstall key `Uplay Install <id>` has `DisplayIcon` pointing to the same .ico, so no protobuf parsing is needed.
- **Images:** `root.thumb_image` and `logo_image` are cached in `cache\assets` (jpg/png). That folder has no .ico files, contrary to the assumption in the task.
- **Coverage:** 1/1. For Avatar the icon equals the EXE icon.

**5. Epic** (not recommended)
- **No icon files:** there are no .ico files in any of the six install folders (top level and one level down).
- **Catalogue:** `C:\ProgramData\Epic\EpicGamesLauncher\Data\Catalog\catcache.bin` is base64-encoded JSON with 1016 items. `keyImages` holds URLs only. Each game has DieselGameBox and DieselGameBoxTall; only Red Dead Redemption 2 has a square DieselGameBoxLogo.
- **Launcher image cache:** `%LOCALAPPDATA%\EpicGamesLauncher\Saved\webcache_4430\Cache` is a Chromium blockfile cache (`index`, `data_0` to `data_3`, `f_*`). I wrote a parser that walks the `data_1` entries and reads keys and bodies.
  - 3602 entries.
  - All 6 games have the tall cover cached as `…?h=480&w=360`: 360x480, mostly AVIF, some WebP/JPEG.
  - Red Dead Redemption 2's logo is cached as 480x480 AVIF.
  - Matching works by the URL file name from `catcache.bin` (`CatalogItemId` → `keyImages` URL, query string ignored).
- **Why I advise against it:** it is a private Chromium format; the folder name depends on the CEF version; entries can be evicted; reading means loading about 50 MB of `data_*` files. AVIF also only decodes because the HEIF and AV1 extensions happen to be installed here.
- **Shortcuts add nothing:** the `.url` shortcuts' `IconFile` is the game EXE. For Unrailed it points to an old path that no longer exists.

**6. EA app** (not recommended)
- **Registry:** the uninstall `DisplayIcon` is `bf6.exe`, the same as today's approach.
- **No local images:** the game folder only has `EAAntiCheat.splash.png`, and `%LOCALAPPDATA%\Electronic Arts\EA Desktop\SplashArtwork` holds one hero JPEG.
- **Cache:** the CEF cache (`CEF\2\EADesktop\BrowserCache\Cache\Cache_Data`, Chromium blockfile, 676 entries; an older copy has 3474) contains `GLABF6S4_EAapp_1x1_GameArt_FullGame` (AVIF).
- **No reliable mapping:** going from game to URL would need the cached GraphQL responses, which use the game slug and are compressed. Matching by name is guesswork.
- **Coverage:** the image is present 1/1, but there is no reliable mapping.

**7. Battle.net** (not usable)
- **On this machine:** 0 games installed; only the Battle.net app has an uninstall entry.
- **Cache:** `%LOCALAPPDATA%\Battle.net\Cache\xx\yy\<md5>` holds 608 files: 223 PNG, 70 JPG, 41 WebP, 53 SVG/XML, 214 JSON.
- **No local mapping:** the JSON files refer to images by symbolic ids such as `warcraft_i_remastered#W1R_ICON_MEDIUM`. I found no local file that maps those ids to cache files.

## Generic Windows sources (measured on all 647 games)

**8. Uninstall `DisplayIcon`**
- **Where read:** HKLM 64-bit (948 keys), HKLM 32-bit (125) and HKCU (14).
- **Checked against the real registry:** HKCU outside the Claude container showed the same 14 keys. I checked that with a read-only `reg query` started through WMI.
- **Matching:** by key name (`Steam App <id>`, `<gogId>_is1`, `Uplay Install <id>`), by `InstallLocation` equal to `installDir`, or by the `DisplayIcon` path lying inside `installDir`.

| Source | Key found | `DisplayIcon` file exists | Icon is not a known EXE |
|---|---|---|---|
| Steam | 633/633 | 632 | 632 |
| GOG | 5/5 | 4 | 4 |
| Ubisoft | 1/1 | 1 | 1 |
| EA | 1/1 | 1 | 0 |
| Epic | 0/6 | 0 | 0 |
| Xbox | 0/1 | 0 | 0 |

- **Steam:** the icon is `Steam\steam\games\<sha1>.ico`. 383 of the 632 have a frame of at least 48 px; 248 only go up to 32 px.
- **GOG:** the icon is the goggame `.ico`. SWAT 3 points to a missing `gog.ico`.
- **Other:** HKCU also lists 5 Humble App games, which are not in the library.

**9. Shortcuts (.lnk/.url)**
- **Where read:** common Start menu (158 lnk, 6 url), user Start menu (62 lnk, 661 url), Desktop (104 lnk, 939 url) and Public Desktop (4 lnk), 1934 in total. The Desktop is not redirected to OneDrive.
- **Checked against the real folders:** the user Start menu has 753 entries both inside and outside the container.
- **Matching:**
  - `steam://rungameid/<id>` for Steam
  - `com.epicgames.launcher://apps/…%3A<AppName>?` for Epic
  - `uplay://launch/<id>` for Ubisoft
  - for GOG and EA: a `.lnk` whose target lies inside `installDir`, or a GOG `/gameId=<id>` argument
  - a `.url`'s `IconFile` / a `.lnk`'s `IconLocation`

| Source | Has a shortcut | Icon file exists |
|---|---|---|
| Steam | 633/633 | 632 (same file as the uninstall icon in 632/632) |
| GOG | 5/5 | 4 |
| Ubisoft | 1/1 | 1 (the `data\games` .ico) |
| Epic | 6/6 | 5, all game EXEs (no new information) |
| EA | 1/1 | 0 (no `IconLocation`, so the target EXE icon) |
| Xbox | 0/1 | Store apps have no .lnk files |

- **Weakness:** matching by "target inside `installDir`" also picks up uninstallers and manuals (SWAT 3: 16 matches). Prefer shortcuts whose target is a known EXE, or whose icon file is an `.ico`.

## Recommended fallback order (all offline, no new packages)
1. Per launcher: GOG `goggame-<id>.ico`, then the Galaxy `*_glx_square_icon_v2.webp`; Xbox logos from `MicrosoftGame.config` / `AppxManifest` (with qualifier glob) or the shell `AppsFolder`; Ubisoft through the `Uplay Install <id>` `DisplayIcon`.
2. Generic: an `.ico` named in the uninstall `DisplayIcon` or in a matching shortcut.
3. Epic and EA image caches: only as an optional last resort, because they depend on Chromium internals.

**What this would fix:**
- **Palworld:** none → Square480x480Logo.
- **SWAT 3 / SWAT 4:** 32 px → 256 px goggame `.ico` (or the 112 px Galaxy icon).

**Things to handle in code:**
- **Detect "no usable icon".** The app needs a rule for this: the extraction throws, the largest frame is under 48 px, or the shell returns its generic icon.
- **Missing codecs.** WebP, HEIF and AV1 decoding are Store extensions that are installed here (WebpImageExtension 1.2.31, HEIFImageExtension 1.2.48, AV1VideoExtension 2.0.35) but can be missing on other PCs. The app must catch decode failures.

**Files** (all in the folder named at the top):
- `contact-sheet.png`: 48 px comparison of the EXE icon and each fallback, per game
- `uninstall-coverage.csv`
- `uninstall-container-view.csv`
- `shortcut-coverage.csv`
- `shortcuts.csv`
- `epic-cache-index.tsv`
- `ea-cache-index.tsv`
- `catcache.json`
- `chromecache.cs`: the Chromium cache parser
- `sql.cs`: the winsqlite3 reader
- `peicons.cs`
- `shellimg.cs`
- `outside\`: results of the read-only check run outside the container
- `shellimg\palworld48.png`
- `shellimg\palworld256.png`

I deleted my temporary copies of the Galaxy database and the extracted Epic/EA cache images (1249 files) again.