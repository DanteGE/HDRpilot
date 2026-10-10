## HDRpilot: where to get missing game icons online

**Recommendation:** offer one opt-in, off by default: a PCGamingWiki cover as the fallback icon. Use it only for games that still have no icon after the EXE and local launcher sources. It stays on PCGamingWiki, the only host the user has approved. Store-CDN icons (Steam/GOG/Epic) are a second, separate opt-in that needs the user's own decision. Don't use SteamGridDB or IGDB.

### First: local launcher data already covers most of this library (read-only, no network)

Library: 647 games, all installed: Steam 633, Epic 6, GOG 5, Ubisoft 1, EA 1, Xbox 1 (grouped from `after-config.json`).

- **Steam `appcache\librarycache\<appid>\`:** 628 of the 633 Steam games have a folder. All 628 contain a 40-hex `.jpg` (the community icon, 32 px) and a `library_600x900` cover; 201 also have a `header`. Method: `Test-Path` plus a recursive file list per sourceId.
  - The 5 without a folder are 4 Playtests and 1 Demo. Store and wiki data are unlikely to exist for them online either.
- **Steam `steam\games\*.ico`:** 1061 files, named by each game's Steam client-icon hash. I read the ICO headers for the largest embedded size: 513 have 256 px, 401 only 32 px, 57 have 48 px, 48 have 128 px, 27 have 64 px, and the rest are scattered sizes.
  - Desktop `.url` shortcuts map AppID to file, e.g. 1473350 → `f09d…732a.ico`.
- **GOG:** `goggame-<id>.ico` exists in the install folder for 5 of 5 games.
- **Epic:** `C:\ProgramData\Epic\EpicGamesLauncher\Data\Catalog\catcache.bin` (base64 JSON, 1016 items) has cover-image entries for 6 of 6 Epic games. These are only URLs to `cdn1.epicgames.com` (a tall cover 1200×1600 and a wide one 2560×1440), so the image itself needs a download.

For this user, an online fallback would really only matter for Epic (6 games) and odd cases.

### Option comparison

**1. PCGamingWiki (already allowed host)**
- **Coverage:** any game with a wiki page, from any store, including games matched only by name.
- **Image type:** a portrait cover, not a square icon. Needs a centre-crop or letterbox to 48 px.
- **Verified details:**
  - The wiki has no `prop=pageimages` ("Unrecognized value"). Instead, `prop=pageprops` returns `image` = `…Special:Redirect/file/<Cover>&width=200&height=200`.
  - The wikitext already fetched by `QueryTitles` contains `|cover = Hades cover.jpg` in the game's infobox.
  - Files live on `images.pcgamingwiki.com` (originals: Hades 1,012,660 B at 1200×1600; Cyberpunk 5,721,190 B at 1440×2160). Thumbnails live on `thumbnails.pcgamingwiki.com/<h0>/<h0h1>/<File>/<W>px-<File>`, where h = md5 of the file name with underscores. I checked this for 2 files.
  - A 64 px thumbnail is 5,172 B, served via Cloudflare. `imageinfo` with `iiurlwidth` gives the thumbnail URL, up to 50 files per request.
  - `action=idlookup&field=steamappid&value=1473350` works without login and returns the page title.
- **Licence:** wiki text is CC BY-NC-SA 3.0 "unless otherwise noted". The Hades cover page says "Official art by Supergiant Games" with no licence template; the Cyberpunk cover page is empty. So covers are publisher-copyrighted and not licensed by the wiki. Showing a small thumbnail locally without redistributing it is low-risk, but it is not a licence (not legal advice). Add a credit line: "Cover: PCGamingWiki / © publisher".
- **Etiquette (from `PCGamingWiki:API`):**
  - 60 requests per minute; HTTP 429 means a 60-second IP block.
  - A custom User-Agent with contact info is required; a generic one can get a 403.
  - Cache as much as possible on your side.
  - Since August 2026, `cargoquery` needs a login.
- **Privacy:**
  - **Anonymous mode** already sends game names, so cover thumbnails reveal nothing new.
  - **Account mode** currently sends no game names. Fetching covers per game would reveal library titles, so the UI must say so.
- **Effort in the existing `PcgwClient`:** small, no new dependency.
  - Add `cover` to the field regex in `PcgwWikitext.cs` and add a `Cover` property to `PcgwPage`.
  - Store it in `HdrSupportEntry`. In anonymous mode this costs 0 extra API requests.
  - Account mode: the catalog only holds HDR-capable games. Either add `Game.Cover_URL` to the existing catalog `cargoquery` fields, or use one `titles=…&prop=pageprops&ppprop=image` request per 50 `PcgwTitle`s.
  - Add a `GetImage(Uri)` method that only allows `*.pcgamingwiki.com`, requires `image/*`, caps the size at about 512 KB, and uses the same UA and `RequestPacer`.
- **Caching:** keep a ~96 px PNG per game in the local app-data folder, keyed by the cover file name. Re-download only when the cover name changes. Remember misses for 14 days, matching the existing `RecheckAfter`.
- **Policy note:** the images come from the `thumbnails.` / `images.pcgamingwiki.com` subdomains, not `www`. Mention this when asking for consent.

**2. Steam (no key needed, but a new host)**
- **Coverage:** Steam games only.
- **Verified details:**
  - `api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=…include_assets:true` works without a key and accepts several AppIDs per request (2 tested). `Cache-Control: max-age=120`.
  - It returns `community_icon` (a hash) plus file names for cover, header, logo and hero images, some under hashed sub-paths, so they can't always be built by convention.
  - The community icon (`cdn.{fastly|cloudflare}.steamstatic.com/steamcommunity/public/images/apps/<appid>/<hash>.jpg`) is 32×32, 561 B. That is too small for 48 px.
  - The better client icon `.ico` at the same path (`/<clienticon>.ico`) returned 200 with 3,262 B, the same size as the local copy, and a 10-year cache. Its hash is **not** in GetItems; it only comes from local `appinfo.vdf` or the Steam folder, at which point the file is usually already on disk.
- **Terms:** the Steam Web API Terms (from 2010) set 100,000 calls per day. No published store limit.
- **Privacy:** sends AppIDs and IP to Valve.
- **Fit:** low value here (local cache covers 628 of 633); needs the user's own decision.

**3. SteamGridDB**
- Free API key per user; the user must create an account themselves.
- `/api/v2/icons/{steam|game|…}/{id}` plus name search (`/search/autocomplete/{query}`). Wrappers also mention lookups by Epic, Origin, Uplay and GOG IDs.
- Real square icons (PNG/ICO), but community uploads with unclear copyright.
- I couldn't read the terms or rate limits (the docs pages render empty to fetch). An undated LaunchBox issue reports that SteamGridDB asked them not to integrate the current API because a new one is planned.
- Sends names or IDs to a third party and needs key handling. Not recommended.

**4. IGDB / Twitch**
- Needs a Twitch account with two-factor auth, a developer app, and a client ID plus secret (client-credentials token that must be refreshed). The secret must not ship inside a desktop app, so each user would have to register their own app.
- About 4 requests per second per client ID (secondary sources).
- Usage falls under the Twitch Developer Agreement; commercial use reportedly needs a partnership and attribution.
- Covers only, no real icons. Not recommended.

**5. GOG and Epic CDNs**
- **GOG:** `api.gog.com/products/<id>` is public (`max-age=86400`) and returns `images.icon`, a square 128×128 RGBA PNG (32,156 B, from `images-2.gog-statics.com`). The user's 5 GOG games already have local `.ico` files.
- **Epic:** no public API is needed, because the URLs come from the local `catcache.bin`. `cdn1.epicgames.com` resizes on the fly: `?h=128&quality=medium&resize=1&w=96` returned 3,133 B instead of 793,022 B, served by the Akamai image server. It is a tall cover, not a square.
- **Privacy:** sends the store ID or image URL to the shop that sold the game. Both are new hosts and need the user's decision.

### Proposed opt-in
- **"Ersatz-Cover von PCGamingWiki laden" (load fallback covers from PCGamingWiki):** off by default, only for games with no icon after EXE and local sources, loaded when a row becomes visible, paced at 2 s, cached on disk. With the HDR account mode, the consent text says that the titles of the affected games become visible to PCGamingWiki.
- **Later, only if the user explicitly approves new hosts:** "Symbole von Steam/GOG/Epic laden" (load icons from Steam/GOG/Epic). The most useful part would be Epic covers through `cdn1` with the resize parameters.

### Network requests I made (exact)
- `www.pcgamingwiki.com/w/api.php`: 5 GET (pageprops for 3 titles; imageinfo for 2 files; wikitext of 5 pages; idlookup; API help).
- `thumbnails.pcgamingwiki.com`: 1 HEAD.
- `api.steampowered.com` GetItems: 1 GET (2 AppIDs).
- Steam CDN: 2 GET of the community-icon `.jpg` (561 B each, fastly and cloudflare) and 1 HEAD of the client-icon `.ico`.
- `api.gog.com`: 1 GET; `images-2.gog-statics.com`: 1 GET of the icon PNG.
- `cdn1.epicgames.com`: 2 HEAD.
- Some web searches and fetches for documentation.

Nothing was downloaded in bulk and no executables were fetched. Nothing was written outside the research folder.

Files are in `<Notizordner>\icons-research\online\`:
- pcgw1.json
- pcgw2.json
- pcgw3.json
- steam1.json
- gog1.json
- epic-url.txt
- img\