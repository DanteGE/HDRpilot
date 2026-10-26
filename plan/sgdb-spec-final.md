# Spezifikation: SteamGridDB-Suche im Dialog „Bild wählen…“ (E93, Phase 14) – endgültige Fassung

Status: final implementation spec, ready to build. Written on 2026-10-22 from the code on `phase5/release` (a4e996f,
version 1.5.0; its `src/` matches `main`). It replaces the draft `spec.md`. Both reviews of the draft (user/UX and
technical) were checked finding by finding against the code and the API reference; the verdicts are in Anhang A.
Writing it involved no build, no test, no app start, no key or config access and no call to the SteamGridDB API.

- All paths are relative to the repository root.
- Line numbers refer to a4e996f. They drift once the UI batch is merged, so locate code by symbol.
- The spec contains no local paths, user names or real game data. Keep it that way in everything built from it.

Wish of the user (2026-10-22, decisions taken): in „Bild wählen…“ each tab gets a section „STEAMGRIDDB“ under the
local images. The section shows the matched game („Spiel: <Name> ▾“) and a button „Bei SteamGridDB suchen“. Nothing goes
online before that click. Then:

- about 24 small previews per page, with „Mehr laden“ and „von <Autor>“ under each image;
- only static images, no NSFW or humor;
- match by Steam AppID, otherwise by name, with a chooser to correct a wrong match;
- the full image is downloaded only on „Übernehmen“ and stored like a custom image, so „Zurücksetzen“ keeps working;
- without a key: a hint and a link to the settings;
- with a key, the search also works while automatic filling is off. The switch only governs automatic filling.

It ships as its own test update after the current UI batch (the nine feedback items of 2026-10-22). The same test update
carries the parallel track „Farben folgen dem Spiel“ on its own branch. Every new element therefore takes its accent
colours through theme keys (`DynamicResource Hp.Brush.*`), so that track recolours it without changes here.

---

## 0. Abgrenzung

In scope:

- the online section in all three tabs (Titelbild, Logo, Cover);
- client, pacer, service and session layers;
- picker view model and dialog layout, including one tile geometry per image kind for local and online tiles;
- window height and placement rules for the dialog;
- high-contrast handling of the dialog;
- the settings card changes that follow from the key-versus-switch rule;
- a test fixture with generated images;
- snapshot scenes, one new UI gate, docs (E93, README, GATES Phase 14, stand);
- the side findings in §19.

Not in scope, and not built:

- icons, animated images, upload, voting;
- an NSFW/humor toggle, or style and dimension filters in the UI;
- platform IDs other than Steam (egs, gog, uplay, origin, bnet);
- a disk cache for previews;
- persisting where a picked image came from;
- a download progress display;
- changes to how the automatic run picks images, apart from the fixes in §19.

Licences: facts about the API come from the official OpenAPI file and the MIT wrappers. No code is taken from
GPL-licensed tools (steam-rom-manager, decky-steamgriddb); their behaviour was only read for comparison.

Where the work happens: every project targets `net10.0-windows` (WPF). Code, tests, gate scripts and docs can be written
in any session, including a cloud session. Building, running tests, the UI gates, snapshots and the render review need
Windows. They run locally: throttled (`dotnet` at BelowNormal, at most 12 of 24 threads), one workflow at a time, never
next to other heavy work.

---

## 1. Entscheidungen

| # | Topic | Decision | Reason |
|---|---|---|---|
| D1 | Key vs. switch | The key enables on-demand search. The switch only governs the background run. No key means no network at all. | User decision. It replaces the E89/G10.4 sentence "aus = kein Netzwerkzugriff", hence the new **E93**. |
| D2 | Key entry while the switch is off | Key field, „Prüfen“ and „Schlüssel entfernen“ are usable whatever the switch says. „Prüfen“ with the switch off saves the key but starts no run. | Otherwise a user who never turned the switch on could never search. |
| D3 | When the network is used | Only on: „Bei SteamGridDB suchen“, „Mehr laden“, „Erneut versuchen“, Enter or „Suchen“ in the game chooser, choosing a hit, and „Übernehmen“ with an online pick. Each tab needs its own click. For a remembered match, the first click also asks once for that game's name and year. | User decision "only the click goes online". |
| D4 | Tabs | Keep the order Titelbild, Logo, Cover. This deviates from the approved sketch (Cover first); see §2 and the veto list. | Matches the BILDER tiles, README and gates. A later reorder only touches three RadioButtons, one window test and the snapshots. |
| D5 | Paging | Request the server's default page (50, no `limit` sent) and show 24 at a time from memory. Fetch the next server page only when the buffer holds fewer than 24. The list ends on an empty page, or on a 404 for page ≥ 1. Per tab, at most 240 tiles and the memory bound of D7. | `limit`/`total` behaviour is unconfirmed. SteamGridDB's own plugin sends only `page` and stops on an empty page. |
| D6 | Pacing | API requests take an exclusive turn of the service-wide pacer: at least 1 s from the end of one API request to the start of the next. This holds across the automatic run, „Prüfen“ and every picker session. Image downloads from the CDN are not paced: at most 3 in parallel per dialog. | The current pacer is check-then-wait and lets two threads send at once (§19.3). Paced previews would take about 25 s per page. |
| D7 | Limits per open dialog | 40 API requests. 800 image requests and 128 MB of downloaded preview bytes (safety nets). Decoded previews held in memory ≤ 96 MB; hidden tabs release theirs first. ≤ 240 tiles per tab. 6 full downloads. A 60 s pause after HTTP 429. | Protects against loops and accidental load without blocking normal use. Three tabs with a few „Mehr laden“ use well under half. |
| D8 | Timeouts | 15 s until the response headers, 15 s without any body bytes, and in total 30 s for JSON and previews and 90 s for a full image. A timeout is a network error with its own text. | Today only the headers are bounded (§19.4). |
| D9 | Preview source | `thumb` only (allowed host, not animated). Without one, the tile shows „Keine Vorschau“ and stays selectable. The full image is never downloaded before „Übernehmen“. | User decision "full image only on Übernehmen". |
| D10 | Cover sizes | `dimensions=600x900,342x482,660x930` (portrait grids). The custom route crops to 600×900, and previews show that crop. | Same default as SteamGridDB's own plugin. More choice, about 6 % crop. |
| D11 | Match | Steam games: AppID, never a search. If SteamGridDB does not know the AppID: one name search. Other games: remembered ID, else an exact name hit, else the user picks from the hit list (name, year, "verifiziert"). Several exact hits: the first, flagged „Mehrere Spiele heißen so“. | User decision; same rule as the automatic run. |
| D12 | Correcting the match | A changed match applies to all tabs of the dialog. Online picks in other tabs are dropped, with a note. For non-Steam games the match is stored on „Übernehmen“, also when no image changed, and never on cancel or a failed apply. Storing it drops that game's old automatic images and misses; with the switch on and a key, a fill run follows (normal rules). Steam games: never stored. | `Fill` always uses `steam/<AppID>`. Images of the wrong game must not stay. |
| D13 | When the full image loads | On „Übernehmen“, while the dialog is still open: a busy line, then close on success. On failure the dialog stays open on the failing tab, with a message and the selection. „Abbrechen“ cancels; nothing throws. | The user sees errors where they can act on them. |
| D14 | Storage | Like an own file: `custom:<file>` in `art\custom\`, cover cropped to 600×900. It applies whatever the switch says, and „Zurücksetzen“ works as before. The origin is not stored. The custom-image labels become „Eigenes Bild“. | User decision. No index or config schema change. "Eigene Datei" would be wrong for a picked SteamGridDB image. |
| D15 | Previews in memory | Memory only, owned by the view model, released when the dialog closes. Nothing on disk, no HTTP cache, nothing reused across dialogs. | Privacy, no cleanup code, predictable memory use. |
| D16 | Game running | On-demand search is allowed while a game runs. | It is an explicit, small, bounded user action. Only the unattended run needs the "no load while gaming" guard. |
| D17 | Chooser UI | An inline panel that „Spiel: … ▾“ expands, not a popup. | Esc, focus, UI Automation and snapshots behave like the rest of the dialog. |
| D18 | Scrolling | The middle of the dialog (local and online sections) is one scroll region. The inner lists keep their template, pass the mouse wheel on (`ShellScroll.ChainWheel`) and never scroll themselves. The scroll bar sits in the right body margin, so it takes no content width. | 24 results do not fit at 1280×800. The wheel helper already exists and is proven in the settings. |
| D19 | Window height | Never taller than the work area of the monitor the library window is on. While the dialog is open it only grows, never shrinks, and it grows around its centre. | The user's rule "Größe bleibt": no jumping when tabs or states change. |
| D20 | Tile geometry | One geometry per image kind, the same for local and online tiles (including „Eigene Datei…“), with grids that fill the 830 DIP content width exactly: Cover 6 per row at 2:3, Titelbild 4 per row at about 3.1:1, Logo 4 per row on a checker plate, never cropped. | Today local covers are cropped square and local heroes show only their middle, so local and online images could not be compared fairly. Exact right edges, because spacing is checked by pixel. |
| D21 | Settings card | The card header names SteamGridDB without a switch. Key field, „Prüfen“, status line and „Schlüssel entfernen“ come first. Below, a row of its own with the visible label „Fehlende Bilder automatisch laden“, the switch, the count hint and „Fehlende Bilder jetzt laden“. | Otherwise the switch still looks like on/off for all of SteamGridDB while the picker searches. |
| D22 | Transparency | The settings status line names the latest network access, run or search, with its request count. With the switch off and a key stored, it says that the search runs only on click. | Keeps the G10.4 promise of a clear display of network access. |
| D23 | High contrast | The dialog maps every theme brush it uses to system colours. A test checks the contrast of every text. | Today the dialog has none, and the toolbar helper maps too few brushes. |

None of these needs the user's answer before building. §18 lists those the user may veto at acceptance.

---

## 2. Abweichungen von der abgenommenen Skizze

| Sketch | Build | Why |
|---|---|---|
| Tabs Cover, Titelbild, Logo | Titelbild, Logo, Cover (unchanged) | D4. Veto item. |
| „Spiel: <Spiel> ▾“ | Same text. How the game was matched (Steam-App-ID, gemerkt, Name, gewählt) is in the tooltip and help text | Keeps the label as approved. |
| Button left under the header | Same. A one-line note stands to its right | The note says "only on click" and "only static, no NSFW/humor". |
| „AUF DIESEM PC“ without a caption | One caption line under it | The source note moves there from the old info box; it must keep naming Quiver. |
| Three footer buttons on the right | „Zurücksetzen“ left, „Abbrechen“ and „Übernehmen“ right | The existing footer, unchanged. |
| „von A“ under each image | „von A“, plus a size line only for unusual sizes (logos always) | A size shows why a 342×482 cover is cropped. |

---

## 3. Architektur

```
SettingsWindow ──key──▶ SteamGridDbKeyStore (DPAPI, unchanged)
                                  │
ArtworkPickerDialog ◀─▶ ArtworkPickerViewModel ──▶ SteamGridDbSearchViewModel (per-tab state, chooser, paging, memory)
       │                         │                          │
       │                         │                          ▼
       │                         │                ISteamGridDbService.OpenSearch(game) → ISteamGridDbSearchSession
       │                         │                          │  (App: worker threads, budget, cancellation, preview decode)
       │                         ▼                          │
       │                 IArtworkService.SaveCustomImageData │
       │                                                    ▼
       └── LibraryViewModel.ChooseArtworkAsync      SteamGridDbClient (Core: HTTP, parsing, hosts, caps, timeouts)
                                                           │            └── RequestPacer.Enter (one API turn at a time)
                                       HttpClientHandler (prod) | SteamGridDbFixtureHandler (snapshots, gate) | test fakes
```

What happens on a click of „Bei SteamGridDB suchen“:

1. The view model opens the session if it has none. Opening creates no handler and sends nothing.
2. If the match is still unknown, `ResolveAsync` runs: a name search (1 API request). For a Steam AppID or a remembered
   ID it sends nothing.
3. `GetAssetsAsync` fetches server page 0 (1 API request) and returns up to 50 parsed, filtered assets.
   - If the game is unknown there (404 on page 0), the view model calls `ResolveByNameAsync` (1 API request) and goes on
     with an exact hit or opens the chooser.
   - For a remembered match, the same click then asks `games/id/<id>` once and updates the label with name and year.
4. The view model shows 24 tiles and queues their previews. The session downloads them on at most 3 worker threads,
   decodes them small and hands frozen images back.
5. The view model sets each tile's preview on the UI thread.

---

## 4. Kern: `SteamGridDbClient` und `RequestPacer` (src/HdrPilot.Core)

### 4.1 Models (new file `Artwork/SteamGridDbSearchModels.cs`; `SteamGridDbAsset` extended in place)

```csharp
// Existing record, extended with optional fields (source-compatible with all current calls).
// Width/Height are the full image size from the API, never the thumbnail size.
public sealed record SteamGridDbAsset(long Id, string Url, int Width, int Height, string? Mime,
    string? ThumbUrl = null, string? Author = null, string? Style = null);

/// One page of an asset list (picker). RawCount = entries in "data" before filtering.
public sealed record SteamGridDbAssetPage(IReadOnlyList<SteamGridDbAsset> Items, int Page, int RawCount, int Skipped,
    bool GameUnknown)
{
    /// The list goes on until a page comes back empty (or 404 for page >= 1).
    public bool HasMore => !GameUnknown && RawCount > 0;
    public static SteamGridDbAssetPage Unknown() => new([], 0, 0, 0, true);
    public static SteamGridDbAssetPage End(int page) => new([], page, 0, 0, false);
}

/// A hit of search/autocomplete or games/id. Name cleaned like an author name (§4.4).
/// ReleaseYear from release_date (Unix seconds, UTC); null if absent, <= 0, or the year is outside 1950..2100.
public sealed record SteamGridDbGameHit(long Id, string Name, int? ReleaseYear, bool Verified);

/// Target of an asset request: exactly one of SteamAppId (ASCII digits only, non-empty) or GameId (> 0).
public readonly record struct SteamGridDbTarget
{
    public static SteamGridDbTarget Steam(string appId);   // ArgumentException unless non-empty ASCII digits
    public static SteamGridDbTarget Game(long id);         // ArgumentOutOfRangeException unless > 0
    internal string PathSegment { get; }                   // "steam/<appid>" | "game/<id>"
}

/// Thread-safe counters of one picker session (Interlocked). API requests, image requests and downloaded preview bytes
/// are counted separately; full downloads are limited by count in the session (§5.2).
public sealed class SteamGridDbBudget(int maxApiRequests, int maxImageRequests, long maxPreviewBytes)
{
    public int ApiRequests { get; }
    public int ImageRequests { get; }
    public long PreviewBytes { get; }
    internal bool TryTakeApi();
    internal bool TryTakeImage();
    internal bool TryAddPreviewBytes(long count);   // false once the total would exceed maxPreviewBytes
}

/// Timeouts of one client. Tests shrink them.
public sealed record SteamGridDbTimeouts(TimeSpan Headers, TimeSpan Idle, TimeSpan TotalJson, TimeSpan TotalPreview,
    TimeSpan TotalPicked)
{
    public static SteamGridDbTimeouts Default { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90));
}
```

### 4.2 New client members

```csharp
public const int DefaultPageSize = 50;                       // API default; "limit" is never sent
public const int MaxHits = 20;                               // chooser
public const long MaxPreviewBytes = 4L * 1024 * 1024;        // per preview
public const long MaxPickedImageBytes = 32L * 1024 * 1024;   // full image picked in the dialog (= ArtworkImaging.MaxFileBytes)

// Existing ctor gets optional parameters. budget == null: legacy behaviour (total cap maxRequests, as today).
public SteamGridDbClient(HttpMessageHandler handler, IClock clock, string apiKey, string userAgent,
    int maxRequests = MaxRequests, RequestPacer? pacer = null, SteamGridDbBudget? budget = null,
    SteamGridDbTimeouts? timeouts = null);

public int ApiRequestCount { get; }      // Interlocked; RequestCount stays = ApiRequestCount + ImageRequestCount
public int ImageRequestCount { get; }

/// Picker: one asset page. 404 on page 0 → Unknown(); 404 on page >= 1 → End(page). Throws SteamGridDbException otherwise.
public SteamGridDbAssetPage Assets(SteamGridDbAssetKind kind, SteamGridDbTarget target, int page, CancellationToken ct);

/// Picker chooser: all valid hits (at most MaxHits), in API order. 404 or empty data → empty list.
public IReadOnlyList<SteamGridDbGameHit> SearchGames(string term, CancellationToken ct);

/// Name and year of a remembered game (games/id/<id>). 404 → null.
public SteamGridDbGameHit? GameById(long id, CancellationToken ct);

/// Preview bytes from ThumbUrl only (Blocked without one); no key, not paced, cap MaxPreviewBytes, counted in the budget.
public byte[] DownloadPreview(SteamGridDbAsset asset, CancellationToken ct);

/// Full image with an explicit cap; paced: false for the picker. Existing Download(asset, ct) keeps
/// (MaxDownloadBytes, paced: true) for the automatic run.
public (byte[] Data, string Extension) Download(SteamGridDbAsset asset, long maxBytes, bool paced, CancellationToken ct);

/// Search term for a path segment: '/', '\\', '?', '#', '%', '+' and control characters become spaces; whitespace
/// collapsed; trimmed; cut to at most 100 characters at a text-element boundary (never a lone surrogate). Then encoded
/// once (Uri.EscapeDataString). Used by SearchGameId too.
public static string SearchTerm(string name);
```

### 4.3 Query strings (picker; `FirstAsset` of the automatic run stays unchanged)

| Kind | Request |
|---|---|
| Cover | `grids/<target>?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false&epilepsy=false&page=<n>` |
| Titelbild | `heroes/<target>?types=static&nsfw=false&humor=false&epilepsy=false&page=<n>` |
| Logo | `logos/<target>?types=static&nsfw=false&humor=false&epilepsy=false&page=<n>` |
| Chooser | `search/autocomplete/<SearchTerm(name)>` |
| Remembered game | `games/id/<id>` |

Further rules:

- `page` starts at 0.
- No `limit`, `styles`, `mimes` or `oneoftag`.
- Lists are comma-separated, never repeated.
- Commas never appear in `<target>`, because `SteamGridDbTarget` allows digits only. A comma list would return HTTP 207
  with only one image per game.

### 4.4 Parsing and defensive filters (per `data[]` entry)

An asset entry is kept only if all of these hold:

- it is an object;
- `id` > 0;
- `url` passes `IsAllowedImageUrl`;
- the image type is known (§4.5);
- none of the booleans `nsfw`, `humor`, `epilepsy` is `true` (when present);
- `tags` (when present) contains none of `nsfw`, `humor`, `epilepsy`, compared without regard to case.

Everything else counts in `Skipped`. Fields:

- `ThumbUrl` is set only when `thumb` passes `IsAllowedImageUrl` and its path does not end in `.webm`, `.mp4` or `.gif`.
  An entry without a usable thumb is kept; its tile shows „Keine Vorschau“.
- `Author` comes from `author.name` only. It is trimmed, control characters and bidi controls (U+200E, U+200F,
  U+202A–U+202E, U+2066–U+2069) are removed, it is cut to 64 characters at a text-element boundary, and an empty value
  becomes null. `author.steam64` and `author.avatar` are never read.
- `Style` is a string or null.
- `width`, `height` and `mime` are optional, as today. They are the full image size.

Game hits (search and games/id) use the same cleaning for `name`. A hit without `id` > 0 or without a non-empty cleaned
name is skipped. `verified` is false when absent.

`score`, `upvotes` and `downvotes` are ignored, and the API order is kept. Entries are de-duplicated by `id` within a
page; the view model also de-duplicates across pages.

### 4.5 Image types and extensions

- Accepted MIME types: `image/png`, `image/jpeg`, `image/jpg`, `image/webp`.
- If `mime` is absent, the extension of the URL path decides: `.png`, `.jpg`, `.jpeg`, `.webp`.
- WebP needs the Windows codec. If decoding fails, the preview shows „Vorschau nicht geladen“, and „Übernehmen“ reports
  „nicht lesbar“ (§7.7).

### 4.6 Paging

The API defines a page as follows: `page` is 0-based; the server default is 50, values above 50 are ignored; the response
carries `page`, `total`, `limit` and `data[]`.

- `total` and `limit` are not used (their meaning is unconfirmed).
- `HasMore` is true while the raw `data` count is above 0. The list ends on the first empty page.
- A 404 on page 0 is a `GameUnknown` page. A 404 on page 1 or later is the end of the list.
- The live test (G14.14) records whether a page with fewer than 50 entries is always the last. If it is, a later change
  may stop one request earlier; this spec does not.

### 4.7 Limits, timeouts and pacing

| | API (`www.steamgriddb.com/api/v2/…`) | Previews (CDN) | Full image in the picker | Automatic run |
|---|---|---|---|---|
| Key | Bearer header | never | never | API only (unchanged) |
| Spacing | exclusive pacer turn, ≥ 1 s from the end of the previous API request | none | none | API: pacer turn; image: pacer turn (as today) |
| Parallel | 1 at a time service-wide (pacer) | ≤ 3 per session | 1 | 1 |
| Size cap | 2 MB JSON | 4 MB | 32 MB, ≤ 100 MP, decodable at the save width (§6.2) | 15 MB (unchanged) |
| Budget | 40 per dialog | 800 requests and 128 MB per dialog | 6 per dialog | 200 per run, total (unchanged) |
| Timeouts | headers 15 s, idle 15 s, total 30 s | headers 15 s, idle 15 s, total 30 s | headers 15 s, idle 15 s, total 90 s | headers 15 s, idle 15 s, total 90 s (new) |

- `MaxResponseContentBufferSize` becomes `MaxPickedImageBytes`. Reads already go through `ReadLimited` with
  `ResponseHeadersRead`; the property is only a backstop.
- The budget is checked before the pacer is entered, so an exhausted budget never waits. An exhausted budget raises
  `SteamGridDbException(RequestLimit)`.

### 4.8 Reading with timeouts and cancellation

`ReadLimited` changes as follows:

1. `using var abort = ct.Register(response.Dispose)`. A transfer stuck in the socket ends as soon as the caller cancels.
2. Each chunk is read with `stream.ReadAsync(chunk, token).AsTask().GetAwaiter().GetResult()`. This is synchronous over
   async on a worker thread, which has no synchronization context. `token` links the caller's token with two timers:
   - a total timer started before the first read (`TotalJson`, `TotalPreview` or `TotalPicked`);
   - an idle timer reset after each chunk (`Idle`).
3. When the caller cancelled, the method throws `OperationCanceledException`. An `ObjectDisposedException` or
   `IOException` after the caller cancelled counts as that cancellation.
4. When a timer fired, it throws `SteamGridDbException(Network)` with `Data["timeout"] = true`.
5. When the size cap is exceeded, it throws `Blocked`, as today.

The header phase keeps `HttpClient.Timeout = Headers`. A header timeout also sets `Data["timeout"] = true`.

### 4.9 Allowed hosts and status codes

`IsAllowedImageUrl` stays as it is: https, default port, no user info, host `steamgriddb.com` or `*.steamgriddb.com`.

- It now applies to `thumb` as well as `url`.
- The final response address is checked again after any redirect, for previews and full downloads.
- Production handlers keep `AllowAutoRedirect = false`. A 3xx response is an `HttpError`, so the tile gets a
  placeholder.
- `s3.amazonaws.com` (seen in old samples) stays blocked, because a shared host cannot be restricted by name.
- **Only API requests can produce `Unauthorized`.** An image request answered with 401 or 403 is an `HttpError`, with
  `Data["status"]` set. Object storage often answers 403 for a missing file, and that must not read as a rejected key.
  This also fixes the automatic run (§19.2).

### 4.10 Pacer turns and thread safety

`RequestPacer` (in `HdrSupport/PcgwClient.cs`) gets an exclusive turn. `WaitTurn`/`MarkDone` stay unchanged for
`PcgwClient`.

```csharp
/// Exclusive turn: at most one holder at a time; starts no earlier than Interval after the end of the previous turn.
/// Dispose records the end and releases the turn (exactly once). Cancellation while waiting releases nothing it does not hold.
public RequestTurn Enter(CancellationToken ct);
public readonly struct RequestTurn : IDisposable { public void Dispose(); }
```

- `Enter` waits on a `SemaphoreSlim(1, 1)` with the token, then waits the remaining interval with `clock.Delay`. If that
  delay is cancelled, it releases the semaphore before rethrowing.
- `Dispose` sets the last-done time and releases.
- `SteamGridDbClient.Send` takes `using var turn = _pacer.Enter(ct)` for every paced request, and holds it until the
  response headers arrive or the send fails (the window that `MarkDone` covers today).
- The automatic run, „Prüfen“ and every session share the service's single pacer. The 1 s promise therefore holds across
  threads.
- `Send` gets a `bool paced` parameter and a request class (API or image). Preview and picker downloads never enter the
  pacer.
- `RequestCount`, the new counters and `SteamGridDbService.HandlersCreated` use `Interlocked`.

### 4.11 Unchanged for the automatic run

The automatic run keeps `FirstAsset`, `SearchGameId` (exact match), `Download(asset, ct)` with 15 MB and pacing, and the
200 cap. What changes for it:

- `SearchTerm()` (the same sanitizing for both paths);
- the pacer turn (§4.10);
- body timeouts (§4.8);
- image 401/403 as `HttpError` (§4.9);
- the store guard and the empty-AppID guard (§19.1, §5.5).

---

## 5. App: Dienst und Sitzung (src/HdrPilot.App/Services/Artwork)

### 5.1 Contract additions (`SteamGridDbContracts.cs`, additive with defaults as the interface comment requires)

```csharp
public interface ISteamGridDbService : IDisposable
{
    // … existing members unchanged …

    /// <summary>Key state without side effects (no CountMissing, no network). Default: Missing.</summary>
    SteamGridDbKeyState KeyState => SteamGridDbKeyState.Missing;

    /// <summary>
    /// Search for „Bild wählen…“ (E93): null without a readable key. Creates no HTTP handler; the first request does.
    /// Works with the automatic switch off. Default: null.
    /// </summary>
    ISteamGridDbSearchSession? OpenSearch(GameConfig game) => null;

    /// <summary>Store a match the user chose and applied (non-Steam games only; Steam: no-op). Default: nothing.</summary>
    void RememberMatch(GameConfig game, long steamGridDbGameId)
    {
    }
}

public enum SteamGridDbMatchKind { SteamAppId, Remembered, ExactName, Chosen }

public sealed record SteamGridDbMatch(SteamGridDbMatchKind Kind, SteamGridDbTarget Target, string DisplayName,
    int? Year, bool Verified, bool Ambiguous = false);

public sealed record SteamGridDbResolveResult(SteamGridDbMatch? Match, IReadOnlyList<SteamGridDbGameHit> Hits,
    bool SteamAppUnknown);

public sealed record SteamGridDbPreview(ImageSource Image, long DecodedBytes);
public sealed record SteamGridDbImageData(byte[] Data, string Extension, int Width, int Height);
public sealed record SteamGridDbSessionUsage(int ApiRequests, int ImageRequests, long PreviewBytes, int FullDownloads);

public interface ISteamGridDbSearchSession : IDisposable
{
    string GameName { get; }
    bool IsSteamGame { get; }
    /// <summary>Match known without network (Steam AppID or remembered ID, label = library name); null otherwise.</summary>
    SteamGridDbMatch? InitialMatch { get; }
    SteamGridDbSessionUsage Usage { get; }
    DateTimeOffset? BlockedUntil { get; }   // after HTTP 429

    /// <summary>Name search for a game without a known target. One shared in-flight task (session token); a failed or
    /// cancelled resolve is not kept. The caller's token only stops this caller from waiting.</summary>
    Task<SteamGridDbResolveResult> ResolveAsync(CancellationToken ct);

    /// <summary>One name search after a GameUnknown page (unknown AppID or stale remembered ID).</summary>
    Task<SteamGridDbResolveResult> ResolveByNameAsync(CancellationToken ct);

    /// <summary>Name and year of a remembered match (games/id); null on 404 (stale ID).</summary>
    Task<SteamGridDbGameHit?> DescribeAsync(long gameId, CancellationToken ct);

    Task<IReadOnlyList<SteamGridDbGameHit>> SearchGamesAsync(string term, CancellationToken ct);
    Task<SteamGridDbAssetPage> GetAssetsAsync(SteamGridDbMatch match, ArtworkKind kind, int page, CancellationToken ct);

    /// <summary>Queue a preview. <paramref name="queueToken"/> removes the job while it waits; once running, the job
    /// finishes (only the session token stops a transfer) and the task completes with its result.</summary>
    Task<SteamGridDbPreview> LoadPreviewAsync(SteamGridDbAsset asset, int decodeWidth, CancellationToken queueToken);

    Task<SteamGridDbImageData> DownloadAsync(SteamGridDbAsset asset, ArtworkKind kind, CancellationToken ct);
}
```

`SteamGridDbStatus` gets init-only properties, so the positional constructor and its callers stay unchanged:

- `public DateTimeOffset? LastSearch { get; init; }` – time of the last request of the last finished search session;
- `public int LastSearchRequests { get; init; }` – API plus image requests of that session;
- `public string? LastSearchError { get; init; }` – set only when a search session got `Unauthorized`; cleared by a later
  successful API response, `SaveKey` and `RemoveKey`.

`SteamGridDbServiceOptions` gets:

- `public SteamGridDbSearchLimits Search { get; init; } = SteamGridDbSearchLimits.Default;` – values of D7/D8, so tests
  can shrink them: `MaxApiRequests` 40, `MaxImageRequests` 800, `MaxPreviewBytes` 128 MB, `MaxFullDownloads` 6,
  `MaxParallelPreviews` 3, `RateLimitPause` 60 s, `PreviewIdleExit` 2 s, `Timeouts` = `SteamGridDbTimeouts.Default`;
- `public Action<string>? TestLog { get; init; }` – set by `AppServices` only with `--config-dir` (the existing
  `TestLog()`), never in normal use.

`NullSteamGridDbService` keeps the defaults (Missing, null, no-op).

### 5.2 `SteamGridDbService` (new partial `SteamGridDbService.Search.cs`) and `SteamGridDbSearchSession` (new)

Renames and small changes in the service:

- The private method `KeyState()` becomes `LoadKeyState()`. A property and a method of the same name do not compile
  (CS0102). The new public property `KeyState` returns `LoadKeyState()`. It does not read `Status`, so it starts no
  `CountMissing`.
- `HandlersCreated` counts with `Interlocked.Increment`, because clients are now created on worker threads.

`OpenSearch(game)`:

1. Returns null when `_disposed` is set or the key cannot be loaded (`_keys.Load`). In the second case the cached key
   state is updated.
2. Otherwise it builds a `SteamGridDbSearchSession` with:
   - the game name and Steam AppID (`Source == Steam` and `SourceId` non-empty ASCII digits);
   - the remembered ID (`_options.Artwork.SteamGridDbGameId(game)`), only for non-Steam games;
   - a client factory that calls `CreateClient(key, budget, timeouts)`. That factory increments `HandlersCreated` and
     uses the shared `_pacer`, `HandlerFactory`, the user agent, a
     `SteamGridDbBudget(Search.MaxApiRequests, Search.MaxImageRequests, Search.MaxPreviewBytes)` and `Search.Timeouts`;
   - a cancellation source linked to `_lifetime`;
   - `Search` limits, `Clock`, `Logger` and `TestLog`.
3. Registers the session in `_sessions`.
4. **Ignores the switch.** `MayContinue` is not used.

The decrypted key lives only inside the client factory closure and the client, never on the session's public surface.

Session internals:

- **Client:** created lazily on the first request. On dispose, the session cancels its token and disposes the client at
  once. Running transfers then abort through `ct.Register(response.Dispose)` (§4.8). A late `ObjectDisposedException` or
  `IOException` after cancellation is mapped to `OperationCanceledException`.
- **API calls** (`ResolveAsync`, `ResolveByNameAsync`, `DescribeAsync`, `SearchGamesAsync`, `GetAssetsAsync`):
  - serialized per session by a `SemaphoreSlim(1)`;
  - run on a new background thread per call ("HDRpilot SteamGridDB", `BelowNormal`) through the session's own
    `RunWorker`, **not** the service's `RunOnWorker`;
  - `RunWorker` logs a `SteamGridDbException` at Information as „SteamGridDB-Suche: {Kind}“, ignores cancellation, and
    logs other exceptions with `LogError` (those are bugs);
  - the fallback after a GameUnknown page is a separate call (`ResolveByNameAsync`), never nested inside
    `GetAssetsAsync`, so nothing waits on its own semaphore.
- **Shared resolve:** `ResolveAsync` starts one task on the session token and hands it out; callers await it with
  `WaitAsync(ct)`. A faulted or cancelled task is dropped, so „Erneut versuchen“ resolves again.
- **Previews:** a FIFO queue served by up to `MaxParallelPreviews` worker threads ("HDRpilot SteamGridDB Vorschau",
  `BelowNormal`, background). Each job:
  1. is removed while still waiting if its queue token is cancelled (the task is cancelled);
  2. checks the budget (`TryTakeImage`);
  3. `client.DownloadPreview` (adds its bytes to the budget; over the byte cap → `RequestLimit`);
  4. `ArtworkImaging.Decode(bytes, decodeWidth)` (§6.2);
  5. completes a `TaskCompletionSource` (`RunContinuationsAsynchronously`) with the frozen image and
     `PixelWidth × PixelHeight × 4`.

  A worker exits after `PreviewIdleExit` without work, or at once on dispose. It runs
  `Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown()` in `finally`, as `ArtworkService.cs:574` does.
  `internal int ActiveWorkers` exposes the live count for tests. When the last worker of a disposed session ends,
  `TestLog` gets „Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0“.
- **`DownloadAsync(asset, kind, ct)`:**
  1. counts against `MaxFullDownloads` (over it → `RequestLimit`);
  2. one worker call: `client.Download(asset, MaxPickedImageBytes, paced: false, ct)`;
  3. `ArtworkImaging.Validate(data, ArtworkImaging.MaxSourcePixels, ArtworkService.CustomSaveWidth(kind))` (§6.2);
  4. returns bytes, extension and size.

  `ImageTooLargeException` and decode failures propagate unchanged for the view model to map.
- **Rate limit:** any `RateLimited` sets `BlockedUntil = Clock.UtcNow + RateLimitPause`. Calls before that throw
  `SteamGridDbException(RateLimited)` without network. Queued previews fail fast with the same kind.
- **`Unauthorized`** (API only): the service sets `_lastSearchError = ArtworkStrings.SteamGridDb_Error_Unauthorized` and
  calls `RaiseChanged()` (posted to the UI), so the settings show it. The key is not deleted. A later successful API
  response in any session clears `_lastSearchError`.
- **Dispose:** cancels the session token, empties the preview queue (waiting jobs cancelled), disposes the client and
  unregisters. If the session sent anything, it records `LastSearch` (time of the last request) and
  `LastSearchRequests` (API plus image requests) in the service, then calls `RaiseChanged()`.

Service-side changes:

- `RemoveKey()` and `Dispose()` cancel and dispose all registered sessions before deleting the key or finishing.
  `RemoveKey()` and `SaveKey()` clear `_lastSearchError`.
- `SetEnabled(false)` does **not** touch sessions (D1).
- `CheckKeyAsync` is unchanged, apart from the pacer turn.
- `SaveKey` is unchanged: it still starts a run only when the switch is on.
- `Status` fills `LastSearch`, `LastSearchRequests` and `LastSearchError`.

### 5.3 Resolving the match

| Situation | Requests | Result |
|---|---|---|
| Steam game (AppID) | 0 | `SteamAppId`, display = library name. If page 0 of the first `GetAssetsAsync` is `GameUnknown`, the view model calls `ResolveByNameAsync` (1 request): an exact `NormalizeName` hit becomes `ExactName` (session only); otherwise `SteamAppUnknown` with the hits. |
| Remembered ID (non-Steam) | 0 before the click | `Remembered`, display = library name. After the first asset page of the first click, `DescribeAsync` (1 request) updates the label to the SteamGridDB name and year. A `GameUnknown` page or a 404 from `games/id` leads to `ResolveByNameAsync` instead. |
| Otherwise | 1 search | First exact `NormalizeName` hit → `ExactName` (display = hit name and year). If more than one hit is exact, `Ambiguous = true` and the hits are kept for the chooser. No exact hit → `Match = null` with up to 20 hits. Empty name or no hits → `Match = null`, no hits. |
| User picks a hit, or the Steam or remembered entry | 0 | `Chosen` (hit), or back to `SteamAppId` / `Remembered`. |

### 5.4 Cancellation matrix

| Event | API request in flight | Waiting previews | Running previews | Results kept |
|---|---|---|---|---|
| Tab switch | continues; the result is cached for its tab | that tab's waiting previews cancelled (queue token) | finish and are cached on their tiles | yes, per tab |
| Back to the tab | — | missing previews of its tiles re-queued (also those released for memory, §9) | — | yes |
| „Mehr laden“ while a page loads | the button is busy; a second execute is ignored | — | — | — |
| „Mehr laden“ while previews load | allowed; new previews queue behind | — | — | yes |
| Chooser: new search while one runs | previous chooser search cancelled | — | — | — |
| Match changed | that tab's API op cancelled | all tabs' waiting previews cancelled | finish, results discarded | cleared, except the current tab's online pick (§7.6) |
| „Übernehmen“ (download) | — | queue paused | finish | yes |
| „Abbrechen“, X, Esc, dialog close | cancelled | cancelled | aborted | released (§9) |
| Settings link | as close | as close | as close | released |
| Key removed elsewhere (tray → settings) | cancelled by the service | cancelled | aborted | released; section → NoKey |
| App exit | cancelled (`_lifetime`) | cancelled | aborted | — |

### 5.5 Remembering a match (`RememberMatch`)

- Steam games and `id <= 0` are a no-op.
- `ISteamGridDbArtwork` gets `void ReplaceSteamGridDbGame(GameConfig game, long id) => SetSteamGridDbGameId(game, id);`
  as a default.
- `RememberMatch` first calls `CancelRun()`, so a running automatic run stops before the index changes.
- `ArtworkService` implements `ReplaceSteamGridDbGame` like this. If `GameId != id`, under the index lock it:
  1. collects the file names in `Files`;
  2. clears `Files` and `Checked`;
  3. sets `GameId = id`.

  Then, outside the lock, it deletes the collected files from `art\sgdb\`, calls `ForgetInMemory(game.Key)` and
  `Raise(game.Key)`.
- The service then calls `FlushIndex()`. If the switch is on and the key is `Stored`, it calls `RunNowAsync()`. The run
  keeps its rules: it is skipped while a game runs and records that, as today.
- IO errors are logged as a warning and swallowed: the apply result does not depend on them.
- `Fill` (automatic run) gets a store guard. Before `StoreSteamGridDbImage` it calls `ThrowIfCancellationRequested()`.
  For a non-Steam game it then re-reads `SteamGridDbGameId(game)` and skips the store when the ID has changed since the
  run resolved it. Without that guard, a run that was mid-download could store an image of the old match.
- Limitation, documented in E93: an art index with another version starts empty (`ArtworkIndex.cs:230-254`), and then
  the remembered ID is gone.

### 5.6 Logging and privacy in the service

Information level:

- „SteamGridDB-Suche geöffnet.“
- „SteamGridDB-Suche beendet: {Api} Anfragen, {Images} Bilder.“
- „SteamGridDB-Suche: {Kind}“ for each failed request.

Debug level: the image host names (only the host, never the path), so the live test can confirm the thumbnail host.

Never logged: the key, search terms, game names, author names, URLs or paths.

---

## 6. Bilddienst

### 6.1 `IArtworkService.SaveCustomImageData` (additive, with a default)

```csharp
/// <summary>Like SaveCustomImage, from bytes (a SteamGridDB download). Not on the UI thread.
/// Default: not supported (no temp files with third-party bytes).</summary>
string SaveCustomImageData(GameConfig game, ArtworkKind kind, byte[] data) =>
    throw new NotSupportedException("Eigene Bilder aus Daten speichert nur der Bilddienst.");
```

`ArtworkService` implements it without a temp file. It shares its body with `SaveCustomImage`:

- the source comes from `ArtworkImaging.Decode(data, CustomSaveWidth(kind))`;
- the same `CoverFill(600, 900)`, `Save(…, 92)`, `ForgetInMemory`, `Raise` and token `custom:<file>`.

`internal static int CustomSaveWidth(ArtworkKind kind)` returns 3840 (Titelbild), 1280 (Logo) or 1800 (Cover,
`ArtworkRenderer.CoverWidth * 3`); `SaveCustomImage` uses it too. `NullArtworkService` keeps the default.

### 6.2 `ArtworkImaging`

- New `Decode(byte[] data, int maxWidth)` with the same rules as `Decode(path, …)`:
  - `MaxFileBytes` check;
  - `FrameSize` (header first, then the decoder without pixels);
  - `DecodePixelWidth`, `IgnoreColorProfile`, `OnLoad`, `Freeze`.

  It works on any thread, and it reuses the private `FrameSize` and `CheckSize`.
- `Validate(byte[] data, long maxPixels, int saveWidth = 0)` gets the optional `saveWidth`. When it is above 0, it also
  runs `CheckSize(width, height, saveWidth)`. An image the save would refuse (over `MaxPixels` after scaling to the save
  width) is then rejected in the dialog, before it closes.

### 6.3 Labels of custom images

The origin is not stored (D14), so the labels name the kind of choice and no longer "file". In
`ArtworkStrings.resx`:

| Key | Old | New |
|---|---|---|
| `Source_Custom` | Eigene Datei | Eigenes Bild |
| `Candidate_CustomFile` | Eigene Datei | Eigenes Bild |
| `Summary_Custom` | Eigene Datei · nur auf diesem PC | Eigenes Bild · nur auf diesem PC |

The button „Eigene Datei…“ and the pending choice „Eigene Datei: {0}“ stay, because they do mean a file.
`ArtworkServiceTests.cs:273` and `README.md:201` change with it.

---

## 7. ViewModel

### 7.1 Structure

`ArtworkChoiceViewModel` (same file) gets:

- `bool IsOnline`, `SteamGridDbAsset? Asset`, `ArtworkKind Kind`;
- `string? Author` and `string AuthorText` („von X“ or „Autor unbekannt“);
- `PreviewState PreviewState` (`None`, `Loading`, `Loaded`, `Failed`, `Missing`), where `Missing` means "no thumb" and
  shows „Keine Vorschau“;
- `long DecodedBytes`;
- `bool ShowSize` (online: size known and not the kind's standard size, i.e. not 600×900 for covers and not 1920×620
  for heroes; logos always when known; local: as today);
- `string AutomationName` and `string? AutomationHelp`;
- `Preview` becomes settable with change notification (internal `SetPreview`, `ReleasePreview`);
- internal `byte[]? PendingData` and `string? PendingExtension`.

The constructor stays internal and gains optional parameters.

`ArtworkPickerViewModel` (existing) becomes `IDisposable` and gets:

- `SteamGridDbSearchViewModel Online { get; }`;
- `bool IsApplying`, `string? BusyText`;
- `bool HasChanges` (some tab's choice differs from the stored token, or has a pending file or pending data);
- `bool SettingsRequested` and `event EventHandler? CloseRequested`;
- `Task<bool> PrepareApplyAsync()`;
- `internal Task<ApplyOutcome> ApplyWithOutcomeAsync()` with `enum ApplyOutcome { Applied, Unchanged, Failed }`. The
  existing `Task<bool> ApplyAsync()` stays as a wrapper (`== Applied`), so current tests keep working;
- `void CommitMatch(ApplyOutcome outcome)`, `void CloseSession()`.

`SteamGridDbSearchViewModel` (new file `ViewModels/Library/SteamGridDbSearchViewModel.cs`) is owned by the picker. It
keeps a `Dictionary<ArtworkKind, KindState>` and exposes the **current tab's** values for binding:

| Member | Meaning |
|---|---|
| `State` | `SteamGridDbSearchState` of the current tab (§7.2) |
| `StatusText`, `StatusTone` (`StatusKind.Info`/`Warning`/`Error`), `HasStatus` | text for the state; tone for `Lib.StatusTint` |
| `Announcement` | the text for the live region: changes only on a state change and once when a batch of previews is done (§8.8) |
| `Results` | `ObservableCollection<ArtworkChoiceViewModel>`: shown tiles of the current tab |
| `ShowResults`, `ShowSkeleton`, `ShowSearchButton`, `IsSearchBusy`, `ShowSettingsLink`, `SettingsLinkText`, `ShowRetry`, `CanRetry`, `ShowMore` | visibility and state |
| `CountText` | „24 Cover von SteamGridDB“ (not live) |
| `ProgressText` | „Vorschauen 12 von 24“ while previews load (not live, separate TextBlock) |
| `MatchText`, `MatchToolTip`, `MatchAutomationName` | „Spiel: Nebelfahrt“; how it was matched (§7.3) |
| `IsMatchAmbiguous`, `MatchAmbiguousText` | „Mehrere Spiele heißen so – prüfe das Jahr.“ |
| `IsMatchOpen`, `MatchSearchText`, `MatchEntries`, `MatchHint`, `IsMatchSearching`, `ShowCommitHint` | the inline chooser |
| `SearchCommand`, `MoreCommand`, `RetryCommand`, `OpenSettingsCommand`, `SearchGamesCommand`, `ChooseMatchCommand(entry)`, `ToggleMatchCommand` | actions |
| `PreviewScale` (set by the view from the DPI) | decode width = min(512, ceil(tile width × scale)) |
| `FocusRequest` event (`FirstResult`, `FirstNewResult`, `Retry`, `SettingsLink`, `MatchSearch`, `MatchList`, `Tile(item)`) | keyboard focus hand-off to the view |

Per-tab `KindState` holds:

- state, error kind and the timeout flag;
- shown items, plus a buffer of fetched but unshown assets;
- `nextServerPage` and `serverHasMore`;
- the set of seen asset IDs;
- an op token (API) and a queue token (previews);
- `IsBusy` (an API page is loading);
- `LastShown` (for releasing memory, §9);
- `LimitReached` (tile limit or memory bound, §7.5).

Shared across tabs: the session (opened lazily on the first network action), the match, the hit list, the
`MatchChangedByUser` flag and the decoded bytes held.

### 7.2 States per tab and exact texts

| State | Shown | Text (resx key) | Actions | Network |
|---|---|---|---|---|
| `NoKey` | hint, info tone | `Picker_SgdbNoKey` | **Zu den Einstellungen** (same row) | none |
| `KeyUnreadable` | hint, warning tone | `Picker_SgdbKeyUnreadable` | **Zu den Einstellungen** | none |
| `Ready` | match row; button with the note to its right | `Picker_SgdbReadyNote` | **Bei SteamGridDB suchen**, ▾ | none until a click |
| `Resolving` | button busy („Sucht …“), skeleton row, status | `Picker_SgdbResolving` | ▾ (disabled) | API |
| `Loading` | button busy, skeleton row, status | `Picker_SgdbLoading` ({0} = kind plural) | ▾ | API |
| (either, after 4 s) | status changes once | `Picker_SgdbSlow` | as above | as above |
| `Results` | grid, count line, progress line, **Mehr laden** | `Picker_SgdbResults` / `Picker_SgdbResultsOne`; `Picker_SgdbEnd` when the list ended; `Picker_SgdbTabLimit` at the limit | select, **Mehr laden**, ▾ | previews; API for the next server page |
| `NoMatch` | chooser expanded with hits | `Picker_SgdbNoMatch` ({0} = searched name) or `Picker_SgdbSteamUnknown` | pick a hit, search | chooser search on Enter |
| `NoHits` | chooser expanded, empty list | `Picker_SgdbNoHits` ({0} = term) | search another name | as above |
| `NoImages` | text | `Picker_SgdbNoImages` ({0} = kind plural) | ▾ to pick another game | none |
| `Unauthorized` | error tone | `Picker_SgdbUnauthorized` | **Zu den Einstellungen** | none further this session |
| `RateLimited` | warning tone | `Picker_SgdbRateLimited` | **Erneut versuchen**, disabled until `BlockedUntil` (one-shot `Ui.Delay`, no ticking) | blocked 60 s |
| `Offline` | warning tone | `Picker_SgdbOffline`, or `Picker_SgdbTimeout` when `Data["timeout"]` is set | **Erneut versuchen** | on click |
| `ServerError` (HttpError, InvalidResponse) | warning tone | `Picker_SgdbServerError` | **Erneut versuchen** | on click |
| `BudgetExhausted` | info tone | `Picker_SgdbBudget` | — (shown tiles stay selectable) | none further this session |

Further rules:

- The search button never disappears while loading. It stays focusable, shows „Sucht …“, sets
  `AutomationProperties.ItemStatus` and ignores clicks. It is replaced by the grid only when results arrive.
- The slow text is set once by a one-shot `Ui.Delay(4 s)` and cancelled when the op ends.
- An error during „Mehr laden“ keeps the grid. The status shows under the grid, and **Erneut versuchen** repeats that
  page.
- An error during resolve or the first page replaces the skeleton.
- A preview that fails (Blocked, HttpError, decode error, network) only marks its tile `Failed`: placeholder glyph plus
  `Picker_SgdbPreviewFailed`. The tile stays selectable, and the status stays `Results`.
- `RateLimited` or `RequestLimit` from a preview keeps the grid too. The tab shows the rate-limit or budget status under
  the grid, and the remaining waiting previews of the session fail fast.
- **Erneut versuchen** also re-queues failed previews.

Mapping from `SteamGridDbErrorKind`:

| Error kind | State |
|---|---|
| `Unauthorized` (API only) | `Unauthorized` |
| `RateLimited` | `RateLimited` |
| `Network` | `Offline` (timeout text when flagged) |
| `HttpError`, `InvalidResponse` | `ServerError` |
| `RequestLimit` | `BudgetExhausted` |
| `Blocked` on an API call (JSON over 2 MB) | `ServerError` |

### 7.3 Texte für resx (exact values)

`LibraryStrings.resx`, new keys:

| Key | Value |
|---|---|
| `Picker_LocalHeader` | `AUF DIESEM PC` |
| `Picker_SgdbHeader` | `STEAMGRIDDB` |
| `Picker_SgdbSectionName` | `Bilder von SteamGridDB` |
| `Picker_KindPluralHero` | `Titelbilder` |
| `Picker_KindPluralLogo` | `Logos` |
| `Picker_KindPluralCover` | `Cover` |
| `Picker_SgdbMatch` | `Spiel: {0}` |
| `Picker_SgdbMatchYear` | `{0} ({1})` |
| `Picker_SgdbMatchName` | `Spiel bei SteamGridDB: {0}. Anderes Spiel wählen` |
| `Picker_SgdbMatchHowSteam` | `Zugeordnet über die Steam-App-ID aus deiner Bibliothek.` |
| `Picker_SgdbMatchHowRemembered` | `Zugeordnet über das Spiel, das du früher bei SteamGridDB gewählt hast.` |
| `Picker_SgdbMatchHowName` | `Zugeordnet über den Namen. Stimmt das Spiel nicht, wähle hier ein anderes.` |
| `Picker_SgdbMatchHowChosen` | `Von dir gewählt.` |
| `Picker_SgdbMatchAmbiguous` | `Mehrere Spiele heißen so – prüfe das Jahr.` |
| `Picker_SgdbMatchSearchName` | `Spielname für die Suche bei SteamGridDB` |
| `Picker_SgdbMatchSearch` | `Suchen` |
| `Picker_SgdbMatchHint` | `Enter oder „Suchen“ fragt SteamGridDB nach diesem Namen.` |
| `Picker_SgdbMatchSearching` | `Sucht bei SteamGridDB …` |
| `Picker_SgdbMatchCommitHint` | `Deine Wahl merkt sich HDRpilot für dieses Spiel, wenn du übernimmst.` |
| `Picker_SgdbMatchListName` | `Spiele bei SteamGridDB` |
| `Picker_SgdbSteamEntry` | `{0} (Steam, aus deiner Bibliothek)` |
| `Picker_SgdbRememberedEntry` | `{0} (gemerkt)` |
| `Picker_SgdbVerified` | `verifiziert` |
| `Picker_SgdbEntryCurrent` | `gewählt` |
| `Picker_SgdbHitName` | `{0}, {1}` |
| `Picker_SgdbHitNameVerified` | `{0}, verifiziert` |
| `Picker_SgdbSearch` | `Bei SteamGridDB suchen` |
| `Picker_SgdbSearchName` | `Bei SteamGridDB nach {0} suchen` |
| `Picker_SgdbSearchHelp` | `Fragt erst jetzt mit deinem API-Schlüssel bei SteamGridDB an. Gezeigt werden nur unbewegte Bilder ohne NSFW-, Humor- oder Epilepsie-Markierung; das gewählte Bild lädt HDRpilot erst beim Übernehmen.` |
| `Picker_SgdbSearching` | `Sucht …` |
| `Picker_SgdbReadyNote` | `Erst auf Klick · nur unbewegte Bilder ohne NSFW- und Humor-Markierung` |
| `Picker_SgdbNoKey` | `Mit eigenem API-Schlüssel von steamgriddb.com suchst du hier nach weiteren Bildern.` |
| `Picker_SgdbKeyUnreadable` | `Der gespeicherte SteamGridDB-Schlüssel ist nicht lesbar (anderes Windows-Konto?).` |
| `Picker_SgdbOpenSettings` | `Zu den Einstellungen` |
| `Picker_SgdbOpenSettingsDiscard` | `Zu den Einstellungen (Auswahl geht verloren)` |
| `Picker_SgdbOpenSettingsHelp` | `Schließt „Bild wählen“ ohne Änderungen und öffnet Darstellung › Bilder beim Schlüsselfeld.` |
| `Picker_SgdbResolving` | `Sucht das Spiel bei SteamGridDB …` |
| `Picker_SgdbLoading` | `Lädt {0} von SteamGridDB …` |
| `Picker_SgdbSlow` | `SteamGridDB antwortet langsam …` |
| `Picker_SgdbResults` | `{0} {1} von SteamGridDB` |
| `Picker_SgdbResultsOne` | `1 {0} von SteamGridDB` |
| `Picker_SgdbPreviewProgress` | `Vorschauen {0} von {1}` |
| `Picker_SgdbEnd` | `Das sind alle passenden {0} bei SteamGridDB.` |
| `Picker_SgdbTabLimit` | `Mehr {0} zeigt „Bild wählen“ nicht auf einmal. Passt keins, wähle unter „Spiel“ ein anderes Spiel.` |
| `Picker_SgdbMore` | `Mehr laden` |
| `Picker_SgdbMoreName` | `Weitere {0} von SteamGridDB laden` |
| `Picker_SgdbNoMatch` | `SteamGridDB kennt kein Spiel, das genau „{0}“ heißt. Wähle das passende Spiel aus der Liste oder suche nach einem anderen Namen.` |
| `Picker_SgdbSteamUnknown` | `SteamGridDB kennt diese Steam-App nicht. Wähle das passende Spiel aus der Liste oder suche nach einem anderen Namen.` |
| `Picker_SgdbNoHits` | `Zu „{0}“ hat SteamGridDB kein Spiel gefunden. Versuche einen anderen Namen.` |
| `Picker_SgdbNoImages` | `Für dieses Spiel hat SteamGridDB keine passenden {0} (nur unbewegte Bilder ohne NSFW- und Humor-Markierung).` |
| `Picker_SgdbUnauthorized` | `SteamGridDB hat deinen API-Schlüssel abgelehnt. Prüfe ihn in den Einstellungen.` |
| `Picker_SgdbRateLimited` | `SteamGridDB meldet zu viele Anfragen. Versuche es in einer Minute erneut.` |
| `Picker_SgdbOffline` | `SteamGridDB ist nicht erreichbar. Prüfe die Internetverbindung und versuche es erneut.` |
| `Picker_SgdbTimeout` | `SteamGridDB antwortet gerade nicht. Versuche es später erneut.` |
| `Picker_SgdbServerError` | `SteamGridDB hat nicht wie erwartet geantwortet. Versuche es später erneut.` |
| `Picker_SgdbBudget` | `Für dieses Fenster ist die Höchstzahl an Anfragen an SteamGridDB erreicht. Die gezeigten Bilder bleiben wählbar.` |
| `Picker_SgdbRetry` | `Erneut versuchen` |
| `Picker_SgdbAuthor` | `von {0}` |
| `Picker_SgdbAuthorUnknown` | `Autor unbekannt` |
| `Picker_SgdbItemName` | `{0} von {1}` |
| `Picker_SgdbItemNameNoAuthor` | `{0}, Autor unbekannt` |
| `Picker_SgdbItemSize` | `{0}, {1} × {2} Pixel` |
| `Picker_SgdbItemHelp` | `Bild von SteamGridDB. Es wird beim Übernehmen geladen und wie ein eigenes Bild gespeichert.` |
| `Picker_SgdbPreviewFailed` | `Vorschau nicht geladen` |
| `Picker_SgdbNoPreview` | `Keine Vorschau` |
| `Picker_SgdbDownloading` | `Lädt das gewählte Bild von SteamGridDB …` |
| `Picker_SgdbDownloadingMany` | `Lädt {0} gewählte Bilder von SteamGridDB …` |
| `Picker_SgdbDownloadFailed` | `Das Bild ließ sich nicht von SteamGridDB laden. Prüfe die Verbindung und versuche es erneut oder wähle ein anderes.` |
| `Picker_SgdbDownloadBlocked` | `Dieses Bild ist größer als 32 MB oder liegt an einer unzulässigen Adresse. Wähle ein anderes.` |
| `Picker_SgdbDownloadNotAnImage` | `Das Bild von SteamGridDB ist nicht lesbar. Wähle ein anderes.` |
| `Picker_SgdbPicksReset` | `Deine Wahl von SteamGridDB in den anderen Reitern gilt nicht mehr, weil du ein anderes Spiel gewählt hast.` |
| `Picker_MatchRemembered` | `Spiel bei SteamGridDB für „{0}“ gemerkt.` |

Changed key:

| Key | New value |
|---|---|
| `Picker_SourceNote` | `Aus den Launchern auf diesem PC (Steam, GOG Galaxy, Ubisoft, Xbox-Paket, Quiver), sonst aus Symbol und Name erzeugt.` |

Notes:

- The singular kind names reuse `Picker_TabHero`, `Picker_TabLogo` and `Picker_TabCover`; `Picker_SgdbResultsOne` and
  `Picker_SgdbItemName` use them („Cover von Testautor 03“).
- `Picker_TooLarge` is reused for an image over 100 MP or one the save would refuse.
- `Picker_SourceNote` becomes the one-line caption under „AUF DIESEM PC“ (about 700 DIP at 12.5 px). It keeps naming
  Quiver (`QuiverLibraryTests.cs:413`).
- Hit entries are named for screen readers as `Picker_SgdbHitName` (name, year), then `Picker_SgdbHitNameVerified` when
  verified, e.g. „Kupferwald, 2019, verifiziert“.
- During a rate-limit pause, „Erneut versuchen“ stays disabled; its tooltip uses `Picker_SgdbRateLimited`.

`ShellStrings.resx`: see §10.

### 7.4 Game chooser („Spiel: <Name> ▾“)

- The header row reads `STEAMGRIDDB` followed by the match button. It is hidden in `NoKey` and `KeyUnreadable`.
  - The match button is `lib:ExpandToggle`, a ToggleButton whose automation peer offers both Toggle and ExpandCollapse,
    like `ToolbarTogglePeer`.
  - Its content is `Picker_SgdbMatch` and a chevron glyph (E70D). Its tooltip and help text say how the game was
    matched (`Picker_SgdbMatchHow…`).
  - When `IsMatchAmbiguous` is set, a small warning-tone line with `Picker_SgdbMatchAmbiguous` follows it.
- **Label before any network:** the library name, for every kind of match. After a resolve, a choice or `DescribeAsync`
  it is the SteamGridDB name, with its year (`Picker_SgdbMatchYear`) when known.
- The **inline panel** `ArtworkSgdbMatchPanel` sits under the header, inside the scroll region. It contains:
  - a TextBox `ArtworkSgdbMatchSearch`, prefilled with the library name;
  - a Button „Suchen“;
  - the hint `Picker_SgdbMatchHint`, or `Picker_SgdbMatchSearching` while a search runs;
  - for non-Steam games, `Picker_SgdbMatchCommitHint`;
  - the list `ArtworkSgdbMatchList`: a `shell:GroupBorder` around an ItemsControl of entry buttons
    (`KeyboardNavigation.DirectionalNavigation="Contained"`). Each entry is „Name (Jahr)“, with a muted „verifiziert“
    tag when set. The entry of the current match shows a check glyph and `ItemStatus` `Picker_SgdbEntryCurrent`.
    Buttons instead of a ListBox: moving through the list with the arrow keys must not choose (and so query) every
    entry, and UI Automation can invoke an entry.

  The list starts with the entry that needs no network:
  - for Steam games, `Picker_SgdbSteamEntry` with the library name;
  - for remembered games, `Picker_SgdbRememberedEntry` with the SteamGridDB name once it is known (else the library
    name).

  That entry is selected while its match applies. Then follow up to 20 hits, from the last search or the resolve.
- **Opening the panel sends no request.** A search runs only on Enter in the box or on „Suchen“. A new search cancels
  the previous one.
- **Choosing an entry** (click, Enter, Space or UIA Invoke):
  1. sets the match: `Chosen`, or `SteamAppId`/`Remembered` for the first entry;
  2. sets `MatchChangedByUser` when the target differs from the initial one;
  3. collapses the panel;
  4. resets the tabs (§7.6): results are cleared, the current tab loads at once (choosing is a click), and the other tabs
     return to `Ready` with the new label.
- **Keyboard:**
  - Enter, Space, Alt+Down or F4 on the button toggles the panel. Opening it moves focus to the search box.
  - Down from the box goes to the first entry; the arrow keys move between entries.
  - Enter in the box searches and is handled, so it does **not** trigger the default button „Übernehmen“.
  - Enter or Space on an entry chooses it; it is handled too.
  - Esc in the panel collapses it, returns focus to the button and is handled, so it does **not** close the dialog.
- `NoMatch` and `NoHits` open the panel automatically with the hits. Focus moves to the first entry, or to the box when there are
  no hits, but only while keyboard focus is inside the section.

### 7.5 Paging, „Mehr laden“ and limits

- First load: fetch server page 0, then show the first 24 items not seen before.
- „Mehr laden“:
  1. If the buffer holds fewer than 24 and `serverHasMore`, fetch the next server page (one API request).
  2. Append up to 24 new, de-duplicated items.
- `ShowMore` = (buffer non-empty or `serverHasMore`) and not `LimitReached`.
- At the end, `Picker_SgdbEnd` is shown. With the fixture, the third click on „Mehr laden“ fetches an empty page 2,
  adds nothing and ends the list.
- **Tile limit.** `LimitReached` is set when one of these holds:
  - the tab shows 240 tiles;
  - after releasing hidden tabs (§9), the held decoded bytes plus the estimate for 24 more previews would exceed 96 MB.
    The estimate is decode width × kind aspect (cover 1.5, hero 0.33, logo 0.6) × 4 bytes.

  Then `Picker_SgdbTabLimit` replaces the count line's tail, and „Mehr laden“ disappears.
- `IsBusy` disables „Mehr laden“ only while an API page is loading, not while previews load.
- Only tiles shown in the current tab request previews, in visual order. Assets without a thumb get `Missing` and no
  request.

### 7.6 Selection semantics (one selection per tab)

1. `_chosen[kind]` holds exactly one `ArtworkChoiceViewModel`: a local candidate or an online result.
2. The `Selected` setter accepts a value from the current tab's `Candidates` or `Online.Results` (today it accepts only
   `Candidates`). It still ignores null: a ListBox that does not contain the item pushes null back, and that is ignored.
3. On a change, `IsSelected` is updated in both lists. Both ListBoxes bind `SelectedItem` to `Selected`, so the list
   without the item shows no selection.
4. The default selection is unchanged: current, else automatic, else the first local entry. Online results are never
   selected automatically.
5. **Match change:**
   - If the current tab's `_chosen` is online, that item stays chosen. It is inserted at index 0 of the reloaded results
     (de-duplicated by asset ID) and keeps its preview.
   - Online picks in the **other** tabs fall back to their default selection. If any did, the message row shows
     `Picker_SgdbPicksReset` (info tone).
   - „Erneut versuchen“ and „Mehr laden“ never clear the grid, so they need no pinning.
6. `Show(list)` (existing) treats an online pick of the current tab as valid when it checks whether the choice still
   exists (`ArtworkPickerViewModel.cs:370-378`).
7. „Zurücksetzen“ selects „Automatisch“ in the local list, as today. The online tile loses its ring.
8. Switching tabs never carries a choice over (E89). On a tab change the online list is swapped together with the local
   one.

### 7.7 „Übernehmen“

The dialog's `OnApply` becomes async and can never throw or set `DialogResult` on a closing window:

```csharp
private bool _closing, _applying;   // _closing set in OnClosing

private async void OnApply(object sender, RoutedEventArgs e)
{
    if (_applying || _closing || DataContext is not ArtworkPickerViewModel vm) return;
    _applying = true;
    try
    {
        if (await vm.PrepareApplyAsync() && !_closing) DialogResult = true;
    }
    finally
    {
        _applying = false;
    }
}
```

`PrepareApplyAsync()` never throws:

1. If no `_chosen` entry is online without `PendingData`, return true.
2. Set `IsApplying`. This disables:
   - the tabs, both lists and „Eigene Datei…“;
   - the match button and the chooser;
   - search, „Mehr laden“, retry and the settings link;
   - „Zurücksetzen“ and „Übernehmen“.

   „Abbrechen“ stays enabled. Show `BusyText` (`Picker_SgdbDownloading`, or `…Many` with the count) in the footer, and
   pause the preview queue.
3. For each kind with an online pick and no data, call `session.DownloadAsync(asset, kind, token)` one after the other,
   and store bytes and extension in the choice.
4. On cancellation (dialog closing), return false.
5. On the first failure:
   1. set `Message`:

      | Failure | Text |
      |---|---|
      | `Blocked` | `Picker_SgdbDownloadBlocked` |
      | `ImageTooLargeException` | `Picker_TooLarge` |
      | decode failure / `InvalidDataException` | `Picker_SgdbDownloadNotAnImage` |
      | `RateLimited` | `Picker_SgdbRateLimited` |
      | `RequestLimit` | `Picker_SgdbBudget` |
      | anything else | `Picker_SgdbDownloadFailed` |

   2. clear `IsApplying`, and keep the selection and any data already downloaded;
   3. switch to the failing tab (`Kind = failingKind`) and raise `FocusRequest.Tile(choice)`;
   4. return false. The dialog stays open.
6. On success, return true.

„Abbrechen“, Esc or X during the download: the dialog closes with false, `CloseSession()` cancels the download, and
nothing is written.

`ApplyWithOutcomeAsync()` (the existing `ApplyAsync` body, after the dialog closed):

- For a choice with `PendingData`:
  `token = await Task.Run(() => artwork.SaveCustomImageData(config, kind, data))`. The `saved` list, the error mapping
  (`Picker_TooLarge`, `Picker_NotAnImage`, `Picker_SaveFailed`), the rollback through `Unreferenced`, `SameChoice` and
  the deletion of replaced custom files all work as for own files.
- **Guard:** an online choice without `PendingData` leaves that kind unchanged and logs a warning. It is never written as
  `null` (automatic): `Token` is null for online items.
- It returns `Applied` where `ApplyAsync` returned true today, `Failed` on every error path, and `Unchanged` when nothing
  had to be written.

`CommitMatch(outcome)`:

- does nothing for Steam games, without `MatchChangedByUser`, or when `outcome == Failed`;
- otherwise calls `RememberMatch(config, id)`;
- on `Unchanged` it also shows `Picker_MatchRemembered` in the status bar. On `Applied`, the existing „übernommen“
  status stays.

`ChooseArtworkAsync` (`LibraryViewModel.Showcase.cs:918-932`) becomes:

```csharp
var picker = new ArtworkPickerViewModel(this, item, …);
try
{
    var load = picker.LoadAsync();
    if (_dialogs.ShowArtworkPicker(picker))
    {
        await load;
        picker.CommitMatch(await picker.ApplyWithOutcomeAsync());
    }
    else if (picker.SettingsRequested)
    {
        _context.ShowSettings(SettingsTarget.Artwork);
    }
}
finally
{
    picker.Dispose();               // online part only (§7.9)
}
```

### 7.8 Link to the settings

- `OpenSettingsCommand` sets `SettingsRequested = true` and raises `CloseRequested`. The dialog sets
  `DialogResult = false`.
- `SettingsLinkText` is `Picker_SgdbOpenSettingsDiscard` while `HasChanges`, otherwise `Picker_SgdbOpenSettings`.
- A new enum `SettingsTarget { Start, Artwork }` (namespace `HdrPilot.App.ViewModels`). The name `SettingsSection` is
  already taken by the static class `Views/Shell/SettingsSection.cs`.
- `ILibraryContext` gets two defaults:
  - `void ShowSettings(SettingsTarget target) => ShowSettings();`
  - `ISteamGridDbService SteamGridDb => NullSteamGridDbService.Instance;`
- `AppLibraryContext`:
  - `SteamGridDb => _services.SteamGridDb`;
  - `ShowSettings(target) => _services.ShowSettingsWindow(target)`;
  - a constructor overload with an `ISteamGridDbService` override, used only by snapshot scenes.
- `AppServices.ShowSettingsWindow(SettingsTarget target = SettingsTarget.Start)`: after `Show`/`BringToFront`, for
  `Artwork` it calls `SettingsWindow.ShowTarget(target)` at `DispatcherPriority.Loaded`. That method:
  1. runs `ScrollToSection("NavAppearanceArtwork")`;
  2. focuses `SteamGridDbKey` (`Focus()` and `Keyboard.Focus`);
  3. writes „Prüfung: Einstellungen bei SectionArtwork, Fokus SteamGridDbKey“ to the test log, if `AppServices` gave the
     window one (only with `--config-dir`).
- Why the dialog closes: a modal dialog disables the main window and its owned settings window, so the settings cannot
  be used next to the dialog.

### 7.9 Close and dispose

- `ArtworkPickerDialog.Closed`: call `vm.CloseSession()`, then set `DataContext = null` (as today).
- `CloseSession()` touches only the online part:
  - sets `_onlineClosed`;
  - unsubscribes from `ISteamGridDbService.Changed`;
  - disposes the session, which cancels everything;
  - clears `Online.Results` and the per-tab buffers;
  - calls `ReleasePreview()` on every online choice that is not chosen.

  Chosen online items keep `PendingData` for `ApplyWithOutcomeAsync`.
- `Dispose()` (from `ChooseArtworkAsync`, or the snapshot scene) calls `CloseSession()` if not done yet and drops
  `PendingData`.
- **Local functions stay usable after `Dispose`.** `LoadAsync`, `PickFileCommand`, `Selected` and `ApplyAsync` on local
  choices work as before. `ShowcaseIntegrationTests.cs:126-219` and `ShowcaseViewModelTests.cs` drive the picker after
  `ChooseArtworkAsync` returned, because their fake dialog returns false.
- After `_onlineClosed`, late online task results are ignored.
- While the dialog is open, the view model listens to `ISteamGridDbService.Changed`:
  - `KeyState` becomes `Missing` or `Unreadable` (key removed through tray → settings): all tabs switch to
    `NoKey`/`KeyUnreadable`;
  - `KeyState` becomes `Stored`: `NoKey` tabs become `Ready`.

---

## 8. Ansicht (ArtworkPickerDialog.xaml, Themes/Library.xaml)

### 8.1 Dialog structure

Width stays 880, `SizeToContent="Height"`, `ResizeMode="NoResize"`. The content width inside the 1 DIP frame and the
24 DIP body margins is **830 DIP**; every row aligns to it. The body becomes a Grid:

| Row | Content | Height |
|---|---|---|
| 0 | title, subtitle (unchanged) | Auto |
| 1 | tabs (unchanged; order Titelbild, Logo, Cover) | Auto |
| 2 | `ScrollViewer x:Name="Scroll"` (vertical Auto, horizontal Disabled) with the local section, a divider and the online section (below) | `*` |
| 3 | message line (`ArtworkPickerMessage`, unchanged) and the busy line `ArtworkPickerBusy` (neutral tone) | Auto |
| 4 | buttons (unchanged) | Auto |

Row 2 contents, top to bottom:

1. „AUF DIESEM PC“ (`Lib.SectionTitle`) and the one-line caption `Picker_SourceNote` (12.5, `TextSecondary`,
   ellipsis, full text in the tooltip).
2. The local list and „Eigene Datei…“ in the tile geometry of §8.3.
3. A 1 DIP `Hp.Brush.Line` divider, margin 0,16,0,16.
4. The online section (`shell:GroupBorder` `ArtworkSgdbSection`):
   - header row: cloud glyph, `STEAMGRIDDB`, match button, and the ambiguity line when set;
   - the chooser panel when expanded;
   - state area: the button and the note in one row (left-aligned; the note wraps under the button below 600 DIP), or
     the hint with its link in one row, or the status with „Erneut versuchen“;
   - skeleton row or result grid;
   - footer row: count and progress on the left, „Mehr laden“ on the right.

The old bordered info box for `SourceNote` (`ArtworkPickerDialog.xaml:106-112`) goes away.

The scroll bar takes no content width. The ScrollViewer reaches 20 DIP into the right body margin, its content stays
830 DIP wide, and the bar sits centred in that strip (8 DIP track, 6 DIP thumb). It uses `Lib.DialogScrollBar` (new;
thumb `Hp.Brush.LineStrong`, `Hp.Brush.TextMuted` on hover, transparent track, no arrow buttons). If the UI batch has
brought a reusable thin scroll bar style by then, use that instead.

### 8.2 Height and placement

- **Work area of the owner's monitor.** A new helper `Views/Library/DialogPlacement.cs` provides:
  - `static Rect WorkAreaFor(Window owner)`: `MonitorFromWindow(…, MONITOR_DEFAULTTONEAREST)` plus `GetMonitorInfo`
    (user32, no admin), with `rcWork` converted to DIP through the owner's `CompositionTarget.TransformFromDevice`. The
    fallback is `SystemParameters.WorkArea`;
  - `static double MaxHeightFor(Rect work, double minHeight) => Math.Max(minHeight, work.Height - 24)`;
  - `static double GrowTop(Rect work, double top, double oldHeight, double newHeight)`: first
    `top - (newHeight - oldHeight) / 2`, then clamped to `[work.Top + 12, work.Bottom - 12 - newHeight]`.

  These are pure functions, and unit tests cover them. A window that is never shown does not apply `SizeToContent`.
- On construction, and through `internal void FitToWorkArea(Rect work)` for tests and snapshots:
  `MaxHeight = MaxHeightFor(work, MinHeight)`.
- **Grow only.** On `SizeChanged` with a larger height:
  - `MinHeight = ActualHeight`, so a later state or tab with less content never shrinks the window (the extra room goes
    to row 2);
  - `Top = GrowTop(work, Top, old, new)`.

  The window does not move otherwise. When the content outgrows `MaxHeight`, row 2 gets the remaining height and
  scrolls.

Approximate sizes (DIP):

- fixed rows ≈ 245: chrome 32, header 65, tabs 56, footer 54, bottom margin 20, top margins;
- Ready state, one local row of covers: ≈ 410 in row 2, so ≈ 655 in all;
- at a 1280×800 work area at 100 % (≈ 760, max 736) everything fits before a search;
- at 125 % (≈ 616, max 592) the button row needs a short scroll. The visible scroll bar shows it, and Tab reaches it.
  The `-klein` snapshot documents this.

```
┌ Bild wählen ───────────────────────────────────────────── 880 ┐
│ Bild wählen                                                    │
│ Nebelfahrt · Manuell                                           │
│ [ Titelbild ][ Logo ][ Cover ]                                 │
│ ┌ Scroll ─────────────────────────────────────────────────┐ ▐  │
│ │ AUF DIESEM PC                                            │ ▐  │
│ │ Aus den Launchern auf diesem PC (…), sonst … erzeugt.    │    │
│ │ [Automatisch][Erzeugt][Eigene Datei …]                   │    │
│ │ ─────────────────────────────────────────────────────    │    │
│ │ ☁ STEAMGRIDDB   Spiel: Nebelfahrt ▾                      │    │
│ │ (Spielwahl, wenn aufgeklappt)                            │    │
│ │ [ ☁ Bei SteamGridDB suchen ]  Erst auf Klick · nur …     │    │
│ │ ── nach dem Klick ──                                     │    │
│ │ [img][img][img][img][img][img]                           │    │
│ │  von A  von B  von C  …                                  │    │
│ │ 24 Cover von SteamGridDB              [ Mehr laden ]     │    │
│ └──────────────────────────────────────────────────────────┘    │
│ (Meldung / „Lädt das gewählte Bild …“)                         │
│ [⟲ Zurücksetzen]                      [Abbrechen] [✓ Übernehmen]│
└────────────────────────────────────────────────────────────────┘
```

### 8.3 Tile geometry per kind (local and online alike)

Gap 10 DIP (item margin 0,0,10,10; each list has a right margin of −10, so the last column ends exactly at 830).

| Kind | Per row | Tile width | Preview box | Fill | Row height incl. gap |
|---|---|---|---|---|---|
| Cover | 6 | 130 | 130 × 195 (2:3) | UniformToFill (= the stored crop) | 255 |
| Titelbild | 4 | 200 | 200 × 65 (≈ 1920:620) | UniformToFill | 125 |
| Logo | 4 | 200 | 200 × 104 checker plate | Uniform, 12 inset, never cropped | 164 |

Width check: 6 × 130 + 5 × 10 = 830 and 4 × 200 + 3 × 10 = 830.

This applies to local candidates and to the „Eigene Datei…“ tile too. Today local covers are 150 × 150 (cropped square)
and local heroes 150 × 96 (only their middle).

Tile parts:

- `Lib.ChoiceItem` geometry (`CornerRadius` 16) for both lists; the width comes from the kind (a style trigger on
  `IsCover`/`IsLogo`/`IsHero`). The selection ring switches instantly, as today.
- **Caption with two fixed lines** (padding 9,8,9,10, so all rows have the same height):
  - Line 1, 12.5 SemiBold, `Hp.Brush.Text`, ellipsis with a tooltip when trimmed: the local `Label`, or online
    „von <Autor>“ / „Autor unbekannt“.
  - Line 2, 11, `Hp.Brush.TextSecondary`, ellipsis: locally the size, or else the note (full note in the tooltip and
    `AutomationProperties.HelpText`); online the size when `ShowSize`, else empty.
- The „AKTUELL“ badge stays as today.
- Placeholders (online):
  - `Failed`: glyph E91B in `TextMuted` plus `Picker_SgdbPreviewFailed`, 11 px;
  - `Missing`: the same glyph plus `Picker_SgdbNoPreview`;
  - `Loading`: the empty plate.
- Skeleton while a page loads: one row of plates of the kind's shape in `Hp.Brush.Plate`. They are static, with no
  shimmer.
- Count and progress line (12, `TextSecondary`) on the left under the grid, „Mehr laden“ (`Lib.PaneButton`) on the right.
- The search button: `Lib.PaneButton` with the cloud glyph in `Hp.Brush.AccentText`, 36 high, left-aligned.

### 8.4 Plates, checker, theme brushes

- New theme token `Hp.Brush.Plate`: dark `#FF0B0C11` (today's colour), light `#FFE4E7EE`. It goes into
  `Tokens.Dark.xaml`, `Tokens.Light.xaml` and `ThemeKeys`; `ThemeKeysTests` covers it. It replaces the hard-coded
  `#FF0B0C11` (`ArtworkPickerDialog.xaml:18`) for all plates and the skeleton. Light `Surface2` is pure white and would
  make the skeleton invisible.
- New in `Themes/Library.xaml`: `Lib.LogoPlate`, a frozen `DrawingBrush` checker with 8 px cells (Viewport 16×16
  absolute, `TileMode=Tile`) in two neutral mid-greys, `#FF6B6F78` and `#FF7C8089`. Both themes use it, because white and
  black logos both stay readable on it (contrast about 5:1 and 4:1).
- The logo tab uses `Lib.LogoPlate` with `Stretch=Uniform` for local and online tiles. `lib:ImageFill` gets an attached
  `ImageFill.Stretch` (default `UniformToFill`).
- The section icon uses `Hp.Brush.AccentText`. Everything else uses existing `Hp.Brush.*` keys through
  `DynamicResource`, so light, dark and the game-colour track need no extra work.

### 8.5 Scrolling

- Both picker lists keep `Lib.ListBox` with `ScrollViewer.VerticalScrollBarVisibility="Disabled"` (as today), and get
  `shell:ShellScroll.ChainWheel="True"`. The inner viewer cannot scroll, so the wheel goes on to the dialog's scroll
  region.
- Keyboard navigation in a `WrapPanel` stays spatial: the arrow keys move between rows and columns. `BringIntoView`
  reaches the outer ScrollViewer through the inner one.
- After a search, focus moves to the first result (`FocusRequest.FirstResult`), and after „Mehr laden“ to the first new
  tile. Both happen only while keyboard focus is still inside the online section (on the search button, „Mehr laden“ or
  a tile). `BringIntoView` then scrolls the tile into view, before its reveal starts.

### 8.6 Effects by level (`LibraryMotion`)

| Element | Voll | Reduziert | Aus |
|---|---|---|---|
| Dialog | `FadeIn` as today | as today | none |
| New result tiles (on insertion) | fade and rise (offsetY 10, 220 ms), staggered 18 ms per tile within the batch (≤ 24 tiles, ≤ 650 ms total), one-shot, `FillBehavior.Stop` | fade 150 ms, `offsetY: 0`, no stagger | none |
| A preview arriving on its tile | image opacity 0→1 in 160 ms | same | none |
| Chooser chevron | rotate 180° in 160 ms | instant | instant |
| Chooser panel | content fade 160 ms (height not animated) | instant | instant |
| Selection ring | instant (as today) | instant | instant |
| Loading | static skeleton plus text, the slow text after 4 s (no spinner, no shimmer) | same | same |

There are no looping animations, no `BlurEffect`, no `DropShadowEffect` and no blend modes. Nothing runs once a reveal
is over, and nothing at all while the dialog is closed (G10.6 rules).

### 8.7 High contrast and themes

Today the dialog has no high-contrast handling, and `ToolbarToggle.UseSystemColors` maps only ten brushes (window
background, glows, surfaces 1 and 3, AccentText, status brushes and others are missing).

New helper `Views/Library/DialogContrast.cs`:

- `static void Apply(ResourceDictionary resources, HighContrastPalette? palette)` sets or removes the overrides.
  `HighContrastPalette.FromSystem()` reads `SystemColors`.
- The dialog calls it in its constructor with `SystemParameters.HighContrast`. It subscribes to
  `SystemParameters.StaticPropertyChanged` and unsubscribes in `Closed`.
- `internal void SetHighContrast(HighContrastPalette? palette)` exists for snapshots and tests.

| Theme keys | High contrast |
|---|---|
| `WindowBackground`, `TitleBar`, `Surface`, `Surface1`, `Surface2`, `Surface3`, `SurfaceInput`, `Panel`, `Plate` | `Window` |
| `BackgroundGlow`, `BackgroundGlow2`, `AccentShine`, `AtmosphereSoft`, `Shine`, `AccentSoft`, `SelectionFill`, `SuccessSoft`, `WarningSoft`, `DangerSoft` | Transparent |
| `Text`, `TextSecondary`, `Line`, `LineStrong`, `Border`, `BorderStrong`, `Danger`, `Warning`, `Success` | `WindowText` (meaning comes from the glyph and the text) |
| `TextMuted` | `GrayText` |
| `Accent`, `AccentStrong`, `AccentSolid`, `SelectionRing`, `FocusRing` | `Highlight` |
| `TextOnAccent` | `HighlightText` |
| `AccentText` | `HotTrack` (link colour) |
| `Lib.LogoPlate`, skeleton | `Window` with a 1 DIP `WindowText` border |
| status tint (`Lib.StatusTint`) | transparent, with a 1 DIP `WindowText` border |

The selection ring is `Highlight` at 2 DIP; the focus ring is as today.

Light and dark: every new element resolves through theme keys. Window tests assert that no new element has a literal
colour except `Lib.LogoPlate`.

Keyboard focus rings show only after keyboard input. Use the keyboard-only focus cue from the UI batch (feedback item 4
of 2026-10-22); branching after that batch makes it available.

### 8.8 Accessibility

**Automation IDs** (`ArtworkPickerDialog`; the existing eight stay):

| Automation ID | Control | Name |
|---|---|---|
| `ArtworkSgdbSection` | `shell:GroupBorder` (Group) | `Picker_SgdbSectionName` |
| `ArtworkSgdbMatchButton` | `lib:ExpandToggle` (Toggle and ExpandCollapse) | `Picker_SgdbMatchName` (formatted); HelpText = how matched |
| `ArtworkSgdbAmbiguous` | TextBlock | `Picker_SgdbMatchAmbiguous` |
| `ArtworkSgdbMatchPanel` | `shell:GroupBorder` | `Picker_SgdbMatchListName` |
| `ArtworkSgdbMatchSearch` | TextBox | `Picker_SgdbMatchSearchName` |
| `ArtworkSgdbMatchSearchButton` | Button | `Picker_SgdbMatchSearch` |
| `ArtworkSgdbMatchList` | `shell:GroupBorder` with entry Buttons (Invoke) | `Picker_SgdbMatchListName`; entries named as in §7.3, the current one with ItemStatus „gewählt“ |
| `ArtworkSgdbSearchButton` | Button | `Picker_SgdbSearchName` with the kind plural; HelpText `Picker_SgdbSearchHelp`; ItemStatus „Sucht …“ while busy |
| `ArtworkSgdbResults` | ListBox | `Picker_SgdbSectionName` |
| result items | ListBoxItem | `AutomationName`: `Picker_SgdbItemName` or `…NoAuthor`, plus the size through `Picker_SgdbItemSize` when known („Cover von Testautor 03, 600 × 900 Pixel“); HelpText `Picker_SgdbItemHelp` |
| `ArtworkSgdbMoreButton` | Button | `Picker_SgdbMoreName` |
| `ArtworkSgdbRetryButton` | Button | `Picker_SgdbRetry` |
| `ArtworkSgdbSettingsLink` | Button (`Lib.LinkButton`) | `SettingsLinkText`; HelpText `Picker_SgdbOpenSettingsHelp` |
| `ArtworkSgdbStatus` | TextBlock | `StatusText` (not a live region itself) |
| `ArtworkSgdbAnnouncement` | TextBlock (visually collapsed to zero size, not `Collapsed`) | `LiveSetting=Polite`; the code-behind raises `LiveRegionChanged` only when `Announcement` changes |
| `ArtworkSgdbProgress` | TextBlock | `ProgressText`, never live |
| `ArtworkPickerBusy` | TextBlock | `LiveSetting=Polite` |

`Announcement` changes on a state change (for example „Lädt Cover von SteamGridDB …“ → „24 Cover von SteamGridDB“) and
once when the previews of a batch are done. It never changes per preview.

`Lib.ChoiceItem` names items from `Label` (`Library.xaml:1891`). Online items use a derived style that sets
`AutomationProperties.Name` to `AutomationName`.

**Focus order** (Tab):

1. tabs
2. local list
3. „Eigene Datei…“
4. match button
5. (match panel: search box, „Suchen“, list)
6. search button, settings link or retry, whichever is visible
7. result list
8. „Mehr laden“
9. „Zurücksetzen“
10. „Abbrechen“
11. „Übernehmen“

On open, focus stays on the local list (`Candidates.Focus()`, unchanged).

**Keys:**

- arrows inside lists;
- Enter on a tile → „Übernehmen“ (default button, as today); for an online pick this starts the download;
- Esc closes the dialog, except inside the match panel (§7.4);
- Alt+Down or F4 on the match button.

**Privacy:** no automation name, help text or tooltip ever contains the key. Author names are shown and spoken, never
logged.

---

## 9. Vorschauen: Speicher und Platte

- **Memory only.** Previews are frozen `BitmapSource` objects, referenced only by `ArtworkChoiceViewModel.Preview`.
  They are not in `ImageMemoryCache` and not counted in its 192 MB.
- **Decode size:** decode width = min(512, ceil(tile width × DPI scale)). At 100 % a page of 24 covers holds about
  2.4 MB; at 200 % about 9.7 MB.
- **Held bound (96 MB).** The view model sums `DecodedBytes` of all tiles that hold a preview. Before a batch of new
  previews is queued, and after a tab switch:
  1. while the sum would exceed 96 MB, release the previews of the hidden tab shown least recently, never the chosen
     tiles; released tiles go back to `None` and re-queue when their tab is shown again;
  2. if the current tab alone would still exceed it, set `LimitReached` (§7.5).
- **Download safety nets:** 800 image requests and 128 MB of preview bytes per dialog (D7). Re-fetching released
  previews counts against them.
- **Released on close:** `CloseSession()` drops every reference that is not chosen. The garbage collector frees the rest
  in its normal course. There is no forced collection, because the main window stays open.
- **Disk:** none. No preview cache, no HTTP cache (`HttpClientHandler` has none), no temp files. The full image is held
  as bytes until `ApplyWithOutcomeAsync` writes it through the custom route.

Why not a disk cache: it would add cleanup rules, store third-party images unasked, and save only a few small CDN
requests when a dialog is reopened.

---

## 10. Einstellungen (Darstellung › Bilder)

### 10.1 Card layout (`SettingsWindow.xaml:1171-1233`)

```
[☁]  SteamGridDB (online)  [OPTIONAL]
     Weitere Bilder aus dem Netz, nur mit deinem eigenen API-Schlüssel.
[🔑 API-Schlüssel von steamgriddb.com ……………] [Prüfen]
Automatisches Laden aus · Suche in „Bild wählen…“ nur auf Klick
[Schlüssel entfernen]
─────────────────────────────────────────────
Fehlende Bilder automatisch laden                                   [ ◯ ]
Füllt nur fehlende Bilder: 14 Titelbilder, 22 Logos, 17 Cover. …
[Fehlende Bilder jetzt laden]
ⓘ Standard: aus. Der Schlüssel wird verschlüsselt …
```

- The header loses the docked switch. Its caption becomes the new `SteamGridDbSubtitle`.
- Key area:
  - `KeyField` (`:1196`) and `SteamGridDbCheckButton` (`:1204`) lose `IsEnabled="{Binding SteamGridDbEnabled}"`;
  - the status line (`SteamGridDbStatus`) stays under the key;
  - „Schlüssel entfernen“ moves under the status line.
- Automatic row:
  - a DockPanel with the visible label `SteamGridDbAutoLabel` (13, SemiBold) and the switch (`SteamGridDbToggle`, same
    automation ID and binding, name `SteamGridDbToggleName`) docked right;
  - under it `SteamGridDbHint` (the counts) and „Fehlende Bilder jetzt laden“.
- The note (`SteamGridDbKeyNote`) stays last.
- All automation IDs stay, so `phase10-settings-ui.ps1` keeps finding them.

### 10.2 Status line (`AppearancePageViewModel.SteamGridDbStatusText`, `:466-505`)

The check/save message (`_steamGridDbMessage`) no longer sticks until „Schlüssel entfernen“. With the message, the view
model stores a snapshot of the status: `(Enabled, Key, IsRunning, LastRun, LastSearch, LastError, LastSearchError)`.
When a later `Changed` brings a different snapshot, the message is dropped. The order:

1. A message whose snapshot is unchanged → the message.
2. Key `Unreadable` → `SteamGridDbStatusUnreadable` (error colour).
3. Switch off:
   1. key `Missing` → `SteamGridDbStatusOff`;
   2. `LastSearchError` set → `SteamGridDbStatusError` with it (error colour);
   3. `LastSearch` set → `SteamGridDbStatusLastSearch`;
   4. otherwise → `SteamGridDbStatusOffWithKey`.
4. Switch on:
   1. key `Missing` → `SteamGridDbStatusNoKey`;
   2. `IsRunning` → `SteamGridDbStatusRunning`;
   3. if `LastSearch` is later than `LastRun` (or there is no run): `LastSearchError` → error (error colour), else
      `SteamGridDbStatusLastSearchOn`;
   4. otherwise as today: `LastError` → `SteamGridDbStatusError`, `LastRun` → `SteamGridDbStatusLastRun`, else
      `SteamGridDbStatusReady`.

`SteamGridDbStatusIsError` is true for an error message, for an unreadable key, and when the line shows the
`Unauthorized` text. Old run errors (for example „ein Spiel läuft“) never show while the switch is off.

### 10.3 Texts (`ShellStrings.resx`)

| Key | Value |
|---|---|
| `SteamGridDbSubtitle` (new) | `Weitere Bilder aus dem Netz, nur mit deinem eigenen API-Schlüssel.` |
| `SteamGridDbAutoLabel` (new, visible) | `Fehlende Bilder automatisch laden` |
| `SteamGridDbToggleName` (changed) | `Fehlende Bilder automatisch von SteamGridDB laden` |
| `SteamGridDbHint` | unchanged (`Füllt nur fehlende Bilder: {0} Titelbilder, {1} Logos, {2} Cover. Lokale Bilder bleiben unangetastet.`) |
| `SteamGridDbKeyNote` (changed) | `Standard: aus. Der Schlüssel wird verschlüsselt (DPAPI) für dein Windows-Konto gespeichert und nie ins Protokoll geschrieben. Mit Schlüssel kannst du in „Bild wählen…“ selbst bei SteamGridDB suchen, auch wenn das automatische Laden aus ist.` |
| `SteamGridDbStatusOff` (changed) | `Ohne Schlüssel · kein Netzwerkzugriff` |
| `SteamGridDbStatusOffWithKey` (new) | `Automatisches Laden aus · Suche in „Bild wählen…“ nur auf Klick` |
| `SteamGridDbStatusLastSearch` (new) | `Automatisches Laden aus · letzte Suche {0:g} · {1} Anfragen` |
| `SteamGridDbStatusLastSearchOn` (new) | `Letzte Suche in „Bild wählen…“ {0:g} · {1} Anfragen` |

### 10.4 Jump to the key field

`SettingsWindow.ShowTarget(SettingsTarget target)` is new (§7.8). `AppServices` gives the window the test log only with
`--config-dir`.

---

## 11. Datenschutz und Sicherheit (Prüfliste)

- No key means no session, no handler and no request (`HandlersCreated == 0`).
- With a key, there is no request before one of the D3 actions. Opening the dialog, switching tabs, opening the chooser
  and typing are all free.
- The key is sent only as `Authorization: Bearer` to `www.steamgriddb.com/api/v2/`. It never goes to the CDN, and it
  never appears in:
  - logs or the test log;
  - exception messages or `Data`;
  - view-model properties or automation properties;
  - the fixture log.
- Images come only from https on `steamgriddb.com` and its subdomains, without redirects, with the final address checked,
  the size capped and the transfer time bounded (§4.7, §4.8).
- Image 401/403 never marks the key as rejected (§4.9).
- Third-party text (author, game names) only reaches `TextBlock`s: trimmed, control and bidi characters removed. It is
  never used as a format string, never logged and never stored.
- Neither the avatar nor the Steam64 ID of authors is ever read.
- The user agent stays the same as today, name and version only (E92).
- No admin rights, no new dependency, no GPL or AGPL code. The fixture's PNG writer computes CRC32 by hand;
  `System.IO.Hashing` would be a NuGet package.
- Tests and gates never reach the real network (§12, §15).

---

## 12. Tests

All tests use fake handlers or the fixture, never the real network. They use invented game names („Nebelfahrt“,
„Kupferwald“), invented IDs and generated images (`ArtworkFixture` or the fixture's PNG writer). Fake handlers override
the synchronous `Send`, because the client is synchronous. `test.ps1` runs throttled as before, and the gates run one at
a time.

Stalled transfers are simulated with a response stream whose `ReadAsync` completes only when cancelled and whose `Read`
blocks until disposed. Pacer races use a clock whose `Delay` blocks until the test releases it.

### 12.1 Core: `tests/HdrPilot.Tests/Artwork/SteamGridDbSearchClientTests.cs` (reuses `SgdbHandler` and `FakeClock`)

1. `Asset_pages_send_the_static_safe_filters_the_page_and_no_limit` – exact query per kind; the Bearer header only on API calls.
2. `Asset_pages_parse_thumb_author_size_mime_and_style` – author trimmed, control and bidi characters removed, 64-character cap at a text-element boundary; `author.steam64` and `avatar` absent from the record.
3. `Asset_pages_drop_foreign_insecure_animated_or_flagged_entries_and_count_them` (Theory: http, foreign host, port 8443, user info, s3.amazonaws.com, `.webm` thumb dropped as thumb only, `nsfw`/`humor`/`epilepsy` true, tag `NSFW` in capitals, unknown MIME type; `image/jpg` accepted).
4. `Has_more_until_an_empty_page` (Theory: 50 → true; 22 → true; 0 → false; `total` and `limit` ignored).
5. `Unknown_game_only_on_page_zero_and_a_later_404_ends_the_list`.
6. `Game_search_returns_all_valid_hits_with_year_and_verified` (missing, 0, negative or out-of-range `release_date` → null; names cleaned; at most 20; invalid entries skipped; order kept).
7. `Game_by_id_returns_name_and_year_or_null_for_404`.
8. `Search_terms_are_sanitized_encoded_once_and_never_split_a_surrogate` (Theory: „Half/Life“, „Spiel+“, „100%“, „Name?#“, control characters, 300 characters, an emoji at position 100).
9. `Previews_use_the_thumb_without_key_and_without_api_pacing` (no pacer turn for image requests; ≥ 1 s between API requests).
10. `Previews_need_a_thumb_and_stay_under_four_megabytes` (no thumb → Blocked without a request; declared 5 MB → Blocked; streamed 5 MB without a length → Blocked).
11. `Preview_and_picked_downloads_reject_a_redirect_to_another_host`.
12. `Image_401_or_403_is_an_http_error_not_a_rejected_key` (Theory: preview, picked download, automatic `Download`).
13. `Picked_images_may_reach_32_mb_while_the_automatic_run_keeps_15_mb`.
14. `Budget_counts_api_requests_images_and_bytes_separately_and_stops_without_waiting` (no pacer wait once exhausted; the legacy total cap still works without a budget).
15. `Parallel_image_downloads_are_counted_exactly` (32 parallel → `ImageRequestCount` 32, `RequestCount` consistent).
16. `Errors_map_to_kinds_for_pages_and_search` (Theory: 401, 403, 429, 500, invalid JSON, `success:false`, JSON over 2 MB).
17. `Cancelling_sends_nothing_before_the_request_and_aborts_a_stalled_body_at_once`.
18. `A_stalled_body_times_out_as_network_with_the_timeout_flag` (short `SteamGridDbTimeouts`; idle and total).
19. `The_key_never_appears_in_any_exception` (all error kinds: `Message`, `ToString()`, `Data`).
20. `Target_accepts_only_digits_or_positive_ids_and_never_a_comma`.
21. `Pacer_turns_exclude_each_other_across_threads_and_keep_one_second_from_the_end` (two threads, blocking clock).
22. `Pacer_turn_is_released_when_waiting_is_cancelled`.

The existing `SteamGridDbClientTests` (9) and `PcgwClientTests` stay green unchanged.

### 12.2 App: service, session, fixture, custom data

`tests/HdrPilot.App.Tests/Artwork/SteamGridDbSearchSessionTests.cs` reuses the helpers of `SteamGridDbServiceTests`
(`Service(...)`, `CountingHandler`, `FakeArtwork`, `Png`); `Respond` gains `thumb` and `author`.

1. `Without_a_key_there_is_no_session_and_no_handler`.
2. `Opening_a_session_creates_no_handler_until_the_first_request`.
3. `Search_works_with_the_switch_off_and_the_automatic_run_still_does_not` (E93).
4. `Key_state_reads_no_status_and_counts_nothing` (no `CountMissing` thread, `Status` untouched).
5. `Steam_games_match_by_app_id_without_a_search`.
6. `Remembered_ids_ask_once_for_name_and_year_after_the_first_page`.
7. `Other_games_use_an_exact_hit_or_offer_the_hits_and_flag_several_exact_hits`.
8. `An_unknown_steam_app_or_a_stale_id_falls_back_to_one_name_search` (Theory).
9. `Two_tabs_resolving_at_once_share_one_request_and_a_failed_resolve_is_not_kept`.
10. `Api_requests_keep_one_second_across_the_automatic_run_the_key_check_and_sessions` (concurrent threads).
11. `At_most_three_previews_load_at_once_without_the_one_second_gap` (a blocking fake handler counts concurrency).
12. `A_cancelled_queue_token_drops_waiting_previews_but_running_ones_finish`.
13. `Session_limits_stop_api_requests_images_bytes_and_downloads` (small limits through `Search`).
14. `Rate_limit_blocks_the_session_for_a_minute_without_network` (`FakeClock` advance; then allowed again).
15. `Removing_the_key_cancels_open_sessions_at_once`.
16. `Disposing_a_session_aborts_a_stalled_transfer_and_releases_the_client_at_once`.
17. `Disposing_the_service_cancels_open_sessions`.
18. `Switching_the_automatic_switch_off_leaves_an_open_search_running` (D1).
19. `A_rejected_key_in_a_search_shows_in_the_status_without_deleting_the_key_and_a_cdn_403_does_not`.
20. `The_last_search_shows_in_the_status_after_the_session_ends`.
21. `Remember_match_replaces_the_id_drops_old_images_and_fills_again_when_on`, plus a Steam no-op and an `id <= 0` no-op.
22. `A_running_fill_never_stores_an_image_of_the_old_match`.
23. `Preview_threads_end_and_shut_down_their_dispatcher` (`ActiveWorkers` 0, test-log line written).
24. `Routine_search_errors_are_logged_as_information_without_authors_terms_names_urls_or_key` (capturing logger).
25. `Downloads_reject_images_the_save_would_refuse` (`Validate` with the save width).
26. `Picked_image_cap_equals_the_decoder_limit` (`MaxPickedImageBytes == ArtworkImaging.MaxFileBytes`).

`tests/HdrPilot.App.Tests/Artwork/SteamGridDbFixtureTests.cs`:

1. `Fixture_is_used_only_with_config_dir_and_the_variable` (Theory over the four combinations plus wrong values).
2. `Fixture_answers_the_documented_endpoints_and_404_for_others`.
3. `Fixture_has_no_inner_handler_and_opens_no_socket` (reflection on the type plus behaviour).
4. `Fixture_handler_overrides_the_synchronous_send`.
5. `Fixture_images_are_generated_pngs_of_the_documented_sizes` (header check, alpha for logos).
6. `Fixture_log_names_host_path_and_key_presence_but_never_the_key_and_survives_parallel_writes`.
7. `Fixture_rejects_the_rejected_keys_and_limits_stalls_or_drops_the_marker_terms`.
8. `Fixture_pages_hold_48_then_22_valid_entries_then_an_empty_page`.

`tests/HdrPilot.App.Tests/Artwork/ArtworkCustomDataTests.cs`:

1. `Decoding_bytes_follows_the_file_rules` (size limits, `DecodePixelWidth`, frozen, any thread).
2. `Saving_custom_image_data_matches_saving_a_file` (same token, same file and size; cover 600×900; logo PNG with alpha).
3. `Default_save_custom_image_data_is_not_supported`.

`SteamGridDbServiceTests` gains `Fill_treats_a_steam_game_with_an_empty_app_id_like_a_non_steam_game` (§19.1).

### 12.3 ViewModel: `tests/HdrPilot.App.Tests/Library/ArtworkPickerSearchTests.cs`

These use a scripted `FakeSteamGridDbSearch : ISteamGridDbService` with a controllable session, plus integration tests
with the real `SteamGridDbService` and the fixture handler. `TestLibraryContext` gets a settable `SteamGridDb` (default
`NullSteamGridDbService`) and records `ShowSettings(target)`. Timers go through `Ui.Delay` and `FakeClock`.

1. `Without_a_key_the_section_shows_the_hint_and_the_link_and_opens_no_session`.
2. `An_unreadable_key_shows_its_own_hint`.
3. `Opening_switching_tabs_expanding_and_typing_send_nothing`.
4. `Search_click_shows_24_tiles_with_author_size_and_automation_name`.
5. `More_shows_the_next_24_from_memory_then_fetches_the_next_page` (API request count 1, then 1, then 2).
6. `The_list_ends_on_an_empty_page_and_more_ignores_a_second_click_while_a_page_loads`.
7. `Each_tab_searches_only_after_its_own_click_and_keeps_its_results`.
8. `Switching_tabs_cancels_waiting_previews_and_resumes_them_on_return`.
9. `Hidden_tabs_release_their_previews_when_memory_runs_short_and_reload_them_on_return`.
10. `A_tab_stops_offering_more_at_the_tile_limit_or_the_memory_bound`.
11. `One_selection_per_tab_across_local_and_online_lists`.
12. `Selected_accepts_only_items_of_the_current_tab`.
13. `Reset_selects_automatic_after_an_online_pick`.
14. `A_match_change_keeps_the_current_tabs_pick_and_resets_other_tabs_with_a_note`.
15. `No_match_opens_the_chooser_with_hits_and_a_choice_loads_its_images`.
16. `Several_exact_hits_take_the_first_and_flag_the_match`.
17. `A_remembered_match_shows_its_name_after_the_first_click_and_is_offered_first`.
18. `Chooser_searches_only_on_enter_or_button_and_cancels_the_previous_search`.
19. `A_changed_match_resets_all_tabs_but_loads_only_the_current_one`.
20. `Steam_match_changes_are_never_remembered`.
21. `Other_games_remember_a_changed_match_after_apply_or_unchanged_apply_never_after_cancel_or_failure`.
22. `Error_kinds_show_the_exact_texts` (Theory over every state in §7.2, with the resx string formatted).
23. `An_error_during_more_keeps_the_grid_and_retry_repeats_that_page`.
24. `Rate_limit_disables_search_more_and_retry_until_the_pause_ends`.
25. `A_slow_answer_shows_the_slow_text_once`.
26. `Budget_exhaustion_shows_its_text_and_keeps_the_tiles_selectable`.
27. `Missing_or_failed_previews_show_a_placeholder_and_stay_selectable`.
28. `Prepare_apply_downloads_every_online_pick_before_the_dialog_closes`.
29. `Prepare_apply_failure_switches_to_the_failing_tab_and_keeps_message_selection_and_data` (Theory: network, blocked, not an image, too large, rate limited).
30. `Prepare_apply_returns_false_on_cancel_without_throwing`.
31. `Apply_stores_an_online_pick_as_a_custom_image_and_deletes_the_replaced_one`.
32. `An_online_pick_without_data_is_never_written_as_automatic`.
33. `Cancel_during_the_download_writes_nothing`.
34. `Closing_cancels_the_session_and_releases_unchosen_previews_but_local_apply_still_works`.
35. `The_settings_link_closes_without_changes_opens_the_artwork_section_and_names_lost_choices`.
36. `Removing_the_key_while_open_switches_every_tab_to_the_hint`.
37. `Search_works_with_automatic_filling_off` (integration, real service).
38. `Late_results_after_close_are_ignored`.
39. `Only_state_changes_and_the_finished_batch_reach_the_announcement`.
40. `The_search_button_stays_focusable_and_busy_while_loading`.

### 12.4 Window: `tests/HdrPilot.App.Tests/Library/ArtworkPickerSearchWindowTests.cs`

The WPF test host lays the window out and never shows it (pattern of `RemoveSettingsWindowTests`). It runs in
`ShellSerialCollection`.

1. `The_dialog_never_exceeds_the_work_area_and_only_the_middle_scrolls`. Work areas: 1280×760 at 100 %, 1024×616 (1280×800 at 125 %), 1920×1040, 3440×1400.
2. `Placement_grows_around_the_centre_and_stays_inside_the_work_area` (`DialogPlacement.GrowTop`, also with a work area whose top is not 0).
3. `The_window_never_shrinks_while_open` (tab back to `Ready` after results; `MinHeight` lock).
4. `Tiles_fill_the_content_width_exactly_per_kind` (local and online, 830, gap 10; the „Eigene Datei…“ tile too).
5. `Logos_sit_uniformly_on_the_checker_plate_and_are_never_cropped` (online and local).
6. `Mouse_wheel_over_tiles_scrolls_the_dialog`.
7. `The_scroll_bar_takes_no_content_width`.
8. `Tab_order_follows_the_spec`.
9. `Focus_moves_to_the_first_result_only_while_focus_is_in_the_section`.
10. `Esc_in_the_chooser_collapses_it_and_keeps_the_dialog_open`.
11. `Enter_in_the_chooser_box_searches_and_does_not_apply`.
12. `Automation_ids_names_patterns_and_live_regions_are_set` (all IDs of §8.8 present in the UIA tree; Group and ExpandCollapse patterns; names equal the resx values).
13. `High_contrast_keeps_every_text_readable` (Theory: two simulated palettes modelled on the Windows themes „Nachthimmel“ and „Wüste“, each over the dark and the light app theme; every visible TextBlock has a contrast of at least 4.5:1 to its effective background, and plates, rings and borders use system colours).
14. `Effect_level_off_starts_no_animation_and_full_reveals_once` (no animated properties afterwards).
15. `Every_new_element_uses_theme_resources_in_light_and_dark` (no literal colour except `Lib.LogoPlate`).
16. `Apply_busy_state_disables_everything_but_cancel`.
17. `Apply_after_the_dialog_started_closing_never_sets_the_dialog_result`.

### 12.5 Settings

- `SettingsAppearancePageTests.cs:207`
  (`Appearance_page_turns_steamgriddb_on_only_when_asked_and_checks_only_on_click`): change it to check without switching
  on. The expected calls become `["check:geheim", "save:geheim"]`; the switch is still never touched.
- New in `SettingsAppearancePageTests`:
  - `Appearance_page_offers_the_key_check_with_the_switch_off`;
  - `Appearance_page_status_says_search_on_click_when_off_with_a_key`;
  - `Appearance_page_status_names_the_latest_run_or_search`;
  - `Appearance_page_drops_the_check_message_on_the_next_new_status`;
  - `Appearance_page_shows_a_rejected_key_from_a_search_in_red_and_ignores_old_run_errors_while_off`.
- `RecordingSteamGridDb` (`:346`) needs nothing, because the new members have defaults. It overrides `KeyState` for the
  status tests.
- New class `tests/HdrPilot.App.Tests/Shell/SettingsSteamGridDbWindowTests.cs`:
  - `Key_field_and_check_are_enabled_with_the_switch_off`;
  - `The_switch_has_a_visible_label_row_of_its_own`;
  - `Show_target_artwork_scrolls_to_the_section_and_focuses_the_key_field`;
  - `Show_target_writes_the_test_log_line_only_with_a_test_log`.

### 12.6 Existing fakes and tests to update

- `FakeArtworkService` (`ShowcaseTestSupport.cs:14`): implements `SaveCustomImageData` and records `SavedData` (key,
  kind, length).
- `FakeDialogs`: no change; `OnArtworkPicker` already lets tests drive the view model.
- `LeaseCountingArtwork` and `ExampleArtwork`: no change, thanks to the defaults.
- `ShowcaseIntegrationTests` and `ShowcaseViewModelTests`: stay green, because `Dispose` leaves the local functions
  usable (§7.9). The online section defaults to `NoKey` with `NullSteamGridDbService`.
- `QuiverLibraryTests.cs:413`: stays green, because the new `Picker_SourceNote` still names Quiver.
- `ArtworkServiceTests.cs:273`: expects „Eigenes Bild · nur auf diesem PC“.
- Tile-size assertions in existing picker tests, if any, follow §8.3.

### 12.7 Gegenproben (mutants each test group must catch)

| Mutant | Caught by |
|---|---|
| key also sent on image requests | 12.1 #9, the existing `Key_goes_only_to_the_api…` |
| thumb host not checked | 12.1 #3 |
| full image used as preview fallback | 12.1 #10, 12.3 #27 |
| image 403 mapped to Unauthorized | 12.1 #12, 12.2 #19 |
| previews paced by the API pacer | 12.1 #9, 12.2 #11 |
| pacer back to check-then-wait | 12.1 #21, 12.2 #10 |
| body read without timeout or without `ct.Register` | 12.1 #17 and #18, 12.2 #16 |
| budget not enforced, or waits before failing | 12.1 #14, 12.2 #13 |
| `HasMore` from `total` or from a short page | 12.1 #4, 12.3 #6 |
| 404 on page 1 treated as unknown game | 12.1 #5 |
| handler created in `OpenSearch` | 12.2 #2 |
| picker checks the switch instead of the key | 12.2 #3, 12.3 #37 |
| `SetEnabled(false)` cancels sessions | 12.2 #18 |
| failed resolve kept | 12.2 #9 |
| fill stores an image of the old match | 12.2 #22 |
| selection not cleared across the two lists | 12.3 #11 |
| online pick without data written as automatic | 12.3 #32 |
| download after close (old flow) | 12.3 #28 |
| `RememberMatch` for Steam, on cancel or after a failure | 12.3 #20 and #21 |
| `Dispose` blocks local apply | 12.3 #34, `ShowcaseIntegrationTests` |
| live region per preview | 12.3 #39 |
| fixture active without `--config-dir` | 12.2 fixture #1 |
| Esc in the chooser not handled | 12.4 #10 |
| wheel not chained over tiles | 12.4 #6 |
| window shrinks on a tab change | 12.4 #3 |
| high-contrast map missing a brush | 12.4 #13 |
| author or term logged | 12.2 #24 |
| sticky settings message | 12.5 `…drops_the_check_message…` |

The evidence of each gate names how many mutants were caught.

---

## 13. Testdaten-Lieferant (Fixture) und wie er hineinkommt

`src/HdrPilot.App/Services/Artwork/SteamGridDbFixture.cs` (new, internal) ships in the app, like the snapshot machinery
and `FixedDisplayLuminanceService`. It never runs in normal use.

- **`SteamGridDbFixtureHandler : HttpMessageHandler`** answers synthesized responses only. It has no inner handler, so it
  cannot open a socket.
  - It overrides the synchronous `Send` (the base class throws `NotSupportedException`, which the client would map to
    "offline") and `SendAsync`.
  - Unknown paths and hosts get 404. It never sets a redirect.
- **Activation** goes through
  `static Func<HttpMessageHandler>? SteamGridDbFixture.HandlerFactoryFor(string? configDir, Func<string, string?> environment, string? logFile)`.
  It returns non-null only when `configDir` is set **and** `HDRPILOT_TEST_STEAMGRIDDB == "fixture"`.
  - `AppServices.Artwork.cs` passes the result as `HandlerFactory` to the real `SteamGridDbService`.
  - Without `--config-dir` the variable is ignored, the same pattern as `HDRPILOT_TEST_ART_FILE`
    (`AppServices.Library.cs:51-56`).
  - The constant goes next to `TestArtFileVariable`.
  - Production code paths are unchanged: real handler type, host checks, caps, timeouts, pacing and the key path all run
    as usual; only the transport is replaced.
- **Snapshot use:** `SteamGridDbFixtureService : ISteamGridDbService`.
  - Its `KeyState` is configurable.
  - `OpenSearch` builds a real `SteamGridDbSearchSession` over the fixture handler with the invented key
    `snapshot-fixture-key`.
  - `HoldFullDownloads` (a gate the scene releases) keeps a full download pending for the „Übernehmen“ scene.
  - It writes no log and needs no key file.
- **Images** come from a small managed PNG writer (`ZLibStream` plus a hand-written CRC32, RGBA8, no WPF), so any thread
  can generate them. They are gradients and bands with an index-based hue; logos are transparent outside a rounded band.
  They are made lazily, cached per (kind, size, index), and contain no game art.

  | Kind | Thumbnail (CDN) | Full image (CDN and `width`/`height` in JSON) |
  |---|---|---|
  | Cover | 200 × 300 | 600 × 900 |
  | Titelbild | 400 × 129 | 1920 × 620 |
  | Logo | 300 × 116 | 800 × 310 |

- **Data:**
  - `search/autocomplete/<t>`:
    - hits 9101 „<t>“ (2019, verified), 9102 „<t>: Neuauflage“ (2023) and 9103 „<t> II“ (no year);
    - a term containing `Doppelt` adds a second exact hit 9104 „<t>“ (2005), for the ambiguity flag;
    - `OhneTreffer` gives empty `data`;
    - `Zuviel` gives 429;
    - `Offline` throws `HttpRequestException`;
    - `Langsam` sends the headers and then stalls the body until cancelled.
  - `games/id/<9101…9104>`: the hit as above; other IDs 404.
  - `grids|heroes|logos/game/9101|9102|9104` and `…/steam/<any digits except 9990404>`:
    - page 0 has 50 raw entries; entry 7 has its `url` on a foreign host and entry 13 an `http://` url, so 48 are valid;
    - page 1 has 22 valid entries; page 2 and later are empty;
    - entries 10, 20, 30, … have no `thumb` (tile shows „Keine Vorschau“).
  - `game/9103`: empty `data`, which exercises `NoImages`. `steam/9990404`: 404, which exercises the unknown-Steam path.
  - Hosts: the API answers only on `www.steamgriddb.com`. Images live on
    `cdn2.steamgriddb.com/thumb/fixture-<kind>-<n>.png` and `cdn2.steamgriddb.com/<kind>/fixture-<n>.png`. All image
    responses are `image/png` with `Content-Length`.
  - Authors „Testautor 01“ to „Testautor 72“ (number = entry index). Entry 3 has a 48-character name, which exercises
    trimming. Entry 5 has none, which exercises „Autor unbekannt“.
- **Keys:**
  - `HDRPILOT-FIXTURE-ABGELEHNT`: 401 on every API call, so „Prüfen“ reports invalid and nothing is saved.
  - `HDRPILOT-FIXTURE-SPAETER-ABGELEHNT`: the check search `search/autocomplete/HDRpilot` answers 200, so the key is
    saved, but every other API call gets 401.
  - Any other plausible key: 200.
- **Request log**, only when `logFile` is set (`<config-dir>\sgdb-fixture.log`): one line per request,
  `GET <host> <path?query> schluessel=<ja|nein>`.
  - Writes are serialized by a lock; the file is opened with shared read access.
  - A failed write is ignored and never fails the request.
  - The log never contains the key, author names or anything else.

---

## 14. Bildschirmfotos (Snapshot-Szenen)

New scenes in `AppServices.Library.cs`, next to `hauptfenster-bildwahl` (`:98`). All use `Resizable = false` and
`Settle = 2 s`.

- Each builds the picker over `new AppLibraryContext(this, steamGridDb: new SteamGridDbFixtureService(...))`, so the
  scene never touches the real service.
- A `Prepare` step drives the view model's commands and waits until all previews have arrived.
- The scene disposes the picker when its window closes. The existing `CreatePickerScene` (`:418-434`) gets the same fix.

| Scene ID | Tab | State shown |
|---|---|---|
| `hauptfenster-bildwahl-sgdb-ohne-schluessel` | Titelbild | `NoKey` hint with the link |
| `hauptfenster-bildwahl-sgdb-bereit` | Cover | `Ready`, match label |
| `hauptfenster-bildwahl-sgdb-titelbild` | Titelbild | 24 results, tile 2 selected |
| `hauptfenster-bildwahl-sgdb-cover` | Cover | after one „Mehr laden“ (48), scrolled to the end |
| `hauptfenster-bildwahl-sgdb-logo` | Logo | 24 logos on the checker plate, one without a preview |
| `hauptfenster-bildwahl-sgdb-spielwahl` | Cover | chooser expanded with three hits and the year, ambiguity line |
| `hauptfenster-bildwahl-sgdb-fehler` | Titelbild | `Unauthorized` with the link |
| `hauptfenster-bildwahl-sgdb-uebernehmen` | Cover | busy line during the download (held by the fixture) |
| `hauptfenster-bildwahl-sgdb-klein` | Cover | `Ready` with `FitToWorkArea(1024×616)`, so the scroll region shows |
| `hauptfenster-bildwahl-sgdb-kontrast` | Logo | results with the simulated „Nachthimmel“ palette |

Plus one settings scene, `einstellungen-bilder-sgdb`: Darstellung › Bilder scrolled to the SteamGridDB card, in the
state of the gate's configuration (key stored, switch off).

The game is the first library game whose name starts with „HDRpilot-Prüfspiel“. Without one, the scene returns an empty
window and the gate's non-empty check fails; it never falls back to another game. The gate seeds only invented games
(§15), so every image in these scenes is generated. Renders use `--snapshot-themes dunkel,hell`, and the images land in
the gate's shots folder, never in the repository.

For review, crop and compare the renders at 1280×800, 1920×1080 and 3440×1440 (light and dark). Pixel-measure tile
sizes, gaps, the 830 DIP right edge and the air above and below the section divider, then send the user a cropped
comparison before the test update (G14.10).

---

## 15. UI-Gates

### 15.1 New `tools/gates/phase14-sgdb-ui.ps1` → `PHASE14_SGDB_UI_OK`

Why a new script: no existing gate starts the app with SteamGridDB data. The fixture needs its own variable, its own
seeded games and its own request log.

The script follows the `phase12-quiver-ui.ps1` pattern:

- own test folder (`New-TestConfig`, no `-Import`);
- only windows of its own process ID;
- **no mouse or keyboard input**: UI Automation patterns only (Invoke, Toggle, ExpandCollapse, SelectionItem, Value).
  Esc and Enter in the chooser are covered by window tests 12.4 #10 and #11;
- HDR is never switched: the global target is a display that does not exist, and the log must contain no HDR switch;
- focus checks read test-log lines („Prüfung: …“), never UIA keyboard focus.

Setup:

- Seed two invented games:
  - „HDRpilot-Prüfspiel Nebelfahrt“: manual, via `New-StandInGame`;
  - „HDRpilot-Prüfspiel Kupferwald“: source Steam, AppID `9990150`, not installed (the invented range of the existing
    tests).
- Set `$env:HDRPILOT_TEST_STEAMGRIDDB = 'fixture'`; the log is `<cfg>\sgdb-fixture.log`.
- Clear the variable in `finally`.
- "Settled" below means: no new fixture-log line for 2 s.

Checks, in order. About 70 checks; each line is one `Check`.

A. **Settings, key with the switch off:**
   - the switch is off, and the visible label `SteamGridDbAutoLabel` exists;
   - `SteamGridDbKey` and `SteamGridDbCheckButton` are enabled;
   - type `HDRPILOT-FIXTURE-0001` through `ValuePattern`;
   - invoke „Prüfen“ → the status shows `SteamGridDbCheckValid`;
   - the key file exists in the test folder;
   - the log has exactly one line: `www.steamgriddb.com /api/v2/search/autocomplete/HDRpilot schluessel=ja`;
   - the switch is still off, and no further request follows within 5 s (no automatic run).
B. **No request before the click:**
   - select Nebelfahrt and invoke `ChooseArtworkButton`;
   - `ArtworkSgdbSection`, `ArtworkSgdbMatchButton` and `ArtworkSgdbSearchButton` are present;
   - switch through all three tabs; expand the chooser (ExpandCollapse), set text in `ArtworkSgdbMatchSearch`, collapse
     it;
   - the dialog is still open and the log is unchanged.
C. **Search and „Mehr laden“ (Titelbild):**
   - invoke the search → `ArtworkSgdbResults` has 24 items; the first is named
     „Titelbild von Testautor 01, 1920 × 620 Pixel“, and entry 5's is „Titelbild, Autor unbekannt, 1920 × 620 Pixel“;
   - the log gained 1 search line and 1 `heroes/game/9101…page=0` line (both `schluessel=ja`), plus exactly 22 thumb lines
     with `schluessel=nein` (entries 10 and 20 have no thumb);
   - „Mehr laden“ → 48 items, and the API line count is unchanged;
   - „Mehr laden“ → 70 items and one new `page=1` line; `ArtworkSgdbMoreButton` is still there;
   - „Mehr laden“ → one `page=2` line, still 70 items, `ArtworkSgdbMoreButton` gone, the status shows `Picker_SgdbEnd`;
   - no log line names a host other than `www.steamgriddb.com` and `cdn2.steamgriddb.com`, and none is `http://`.
D. **Per-tab click:** wait until settled; switch to Cover → the search button is present (`Ready`), and the log is still
   unchanged 2 s later.
E. **Apply (Cover):**
   - invoke the search, select the 3rd tile (SelectionItem), invoke „Übernehmen“;
   - the dialog closes;
   - `games[].artwork.cover` = `custom:<…>-cover.jpg`, and the file exists at 600×900;
   - exactly one full-download line, `/cover/fixture-3.png` with `schluessel=nein`;
   - the status bar shows `Picker_Applied`;
   - the settings status line starts with „Automatisches Laden aus · letzte Suche“.
F. **Reset:**
   - reopen → the Cover tab shows „Eigenes Bild“ with AKTUELL;
   - „Zurücksetzen“, „Übernehmen“ → `artwork.cover` is gone and the file is deleted.
G. **Cancel:** search, select tile 4, „Abbrechen“ → the config and `art\custom` are unchanged, and there is no
   `/cover/fixture-4.png` line.
H. **Correcting the match:**
   - Nebelfahrt:
     - reopen, expand the chooser, set `OhneTreffer`, invoke „Suchen“ → `Picker_SgdbNoHits`;
     - set the original name, invoke „Suchen“, invoke the entry for hit 9102 → one `heroes/game/9102…page=0` line;
     - invoke „Übernehmen“ without an image change → the dialog closes, `art\index.json` records `gameId` 9102 for the
       game, and the status bar shows `Picker_MatchRemembered`.
   - Kupferwald: open, search in the chooser, invoke hit 9101, invoke „Übernehmen“ → its index entry has no `gameId`.
I. **NoImages:** reopen Nebelfahrt, choose hit 9103 through the chooser → `Picker_SgdbNoImages`.
J. **Rate limit and slow answers:**
   - chooser term `Zuviel` → `Picker_SgdbRateLimited`, and „Erneut versuchen“ is disabled;
   - chooser term `Langsam` → `Picker_SgdbSlow` within 6 s, then `Picker_SgdbTimeout` within 20 s.
K. **Rejected key:**
   - in the settings, invoke „Schlüssel entfernen“ → the key file is gone; the reopened picker shows `NoKey`;
   - invoke `ArtworkSgdbSettingsLink` → the dialog closes, the settings window opens, and the test log contains
     „Prüfung: Einstellungen bei SectionArtwork, Fokus SteamGridDbKey“;
   - enter `HDRPILOT-FIXTURE-SPAETER-ABGELEHNT`, invoke „Prüfen“ → saved;
   - picker search → `Picker_SgdbUnauthorized` plus the link; cancel the dialog;
   - the settings status shows `SteamGridDbStatusError` with the „Schlüssel abgelehnt“ text.
L. **No real network and no leaks:**
   - the app log contains neither „SteamGridDB nicht erreichbar“, nor any fixture key value, nor „Testautor“;
   - the fixture log contains no key value;
   - after each dialog close, the test log has „Prüfung: SteamGridDB-Suche beendet, Vorschau-Threads 0“ within 5 s.
M. **Snapshots:** run
   `--config-dir <seed> --snapshot <shots> --snapshot-scenes <the 11 scenes of §14> --snapshot-themes dunkel,hell`.
   - 22 PNGs; the dialog ones are 880 DIP wide at the DPI scale;
   - `-klein` at most 592 DIP high;
   - none empty (pixel variance above a threshold).

### 15.2 Changes to existing gates

- `tools/gates/phase10-settings-ui.ps1:108-109`: replace the check „„Prüfen“ ist ohne SteamGridDB gesperrt“ with
  „Schlüsselfeld und „Prüfen“ sind auch bei ausgeschaltetem Schalter bedienbar (und werden hier nie gedrückt)“. The "off
  by default" check (`:104-107`) stays, and the toggle keeps its automation ID.
- `tools/gates/phase10-ui.ps1:498-521`: no change needed. Without a key the section shows `NoKey`, and Cancel writes
  nothing. Optionally add `ArtworkSgdbSection` to the ID list.
- `tools/gates/phase11-uninstalled-ui.ps1:121-122`: no change.

---

## 16. Dokumentation (Entwürfe)

Bold labels in the README drafts exist in resx (`phase5-readme.ps1`): **Bild wählen…**, **Titelbild**, **Logo**,
**Cover**, **Automatisch**, **Eigene Datei…**, **Übernehmen**, **Zurücksetzen**, **Bei SteamGridDB suchen**,
**Mehr laden**, **Zu den Einstellungen**, **Suchen**, **Prüfen**, **Schlüssel entfernen**,
**Fehlende Bilder automatisch laden**, **Fehlende Bilder jetzt laden**, **SteamGridDB (online)**.

### 16.1 `docs/entscheidungen.md`: new E93

E93 goes after E92. If the UI batch or the game-colour track takes E93 first, use the next free number and adjust every
reference.

> **E93 SteamGridDB-Suche in „Bild wählen…“ (Wunsch des Nutzers 2026-10-22, G14):** Im Dialog „Bild wählen…“ steht je
> Reiter (Titelbild, Logo, Cover) unter den lokalen Bildern („AUF DIESEM PC“) der Abschnitt „STEAMGRIDDB“ mit dem
> zugeordneten Spiel („Spiel: <Name> ▾“) und **Bei SteamGridDB suchen**.
> - Nur auf Klick: Öffnen, Reiterwechsel, Aufklappen der Spielwahl und Tippen fragen nichts ab. Online gehen nur
>   **Bei SteamGridDB suchen** (bei einer gemerkten Zuordnung dazu einmal Name und Jahr des Spiels), **Mehr laden**,
>   **Erneut versuchen**, die Suche der Spielwahl (Enter oder **Suchen**), die Wahl eines Treffers und **Übernehmen**
>   mit einem Bild von SteamGridDB. Jeder Reiter sucht erst nach seinem eigenen Klick. Auch während ein Spiel läuft, ist
>   die Suche erlaubt (ausdrücklicher Klick, klein und begrenzt).
> - Schlüssel statt Schalter (ändert E89 und G10.4): Ohne Schlüssel gibt es weiterhin keinen Netzwerkzugriff; der
>   Abschnitt zeigt dann einen Hinweis und **Zu den Einstellungen** (schließt den Dialog ohne Änderungen, öffnet
>   Darstellung › Bilder mit dem Fokus im Schlüsselfeld). Mit gespeichertem Schlüssel sucht der Dialog auch bei
>   ausgeschaltetem Schalter. Der Schalter **Fehlende Bilder automatisch laden** regelt nur das automatische Füllen und
>   steht in einer eigenen Zeile unter Schlüsselfeld, **Prüfen** und Statuszeile. Schlüsselfeld und **Prüfen** sind
>   immer bedienbar; „Prüfen“ bei ausgeschaltetem Schalter speichert nur.
> - Zuordnung: Steam-Spiele über ihre App-ID (kennt SteamGridDB sie nicht, einmal über den Namen), andere über die
>   gemerkte SteamGridDB-ID oder die Namenssuche. Ein Treffer mit gleichem Namen zählt; heißen mehrere so, der erste mit
>   dem Hinweis „Mehrere Spiele heißen so“. Sonst wählt der Nutzer aus bis zu 20 Treffern mit Jahr. Eine geänderte
>   Zuordnung gilt für alle Reiter; eine Wahl von SteamGridDB in den anderen Reitern gilt dann nicht mehr. Bei Spielen
>   ohne Steam-App-ID merkt HDRpilot sie mit **Übernehmen**, auch ohne geändertes Bild, nie nach **Abbrechen** oder einem
>   Fehler. Bisher automatisch geladene Bilder und Fehlanzeigen dieses Spiels gelten dann nicht mehr; ist das
>   automatische Laden an, füllt ein Lauf sie neu. Bei Steam-Spielen merkt HDRpilot nichts (die App-ID bleibt
>   maßgeblich). Ein Verzeichnis anderer Version beginnt leer und vergisst damit auch die gemerkte Zuordnung.
> - Ergebnisse: nur unbewegte Bilder ohne NSFW-, Humor- oder Epilepsie-Markierung, Cover im Hochformat (600×900,
>   342×482, 660×930; gespeichert auf 600×900 zugeschnitten, die Vorschau zeigt diesen Zuschnitt). Je Seite 24
>   Vorschauen mit „von <Autor>“, **Mehr laden** zeigt die nächsten 24 (abgefragt wird die Standardseite von 50; die
>   Liste endet mit einer leeren Seite), höchstens 240 je Reiter. Ohne Vorschau-Adresse zeigt die Kachel „Keine
>   Vorschau“; das volle Bild lädt nie vorher. Avatar und Steam-Kennung der Autoren liest HDRpilot nicht.
> - Kacheln: Lokale und Online-Kacheln haben je Bildart dieselbe Form und füllen die Breite genau (Cover 6 je Reihe im
>   Hochformat 2:3, Titelbild 4 je Reihe, Logo 4 je Reihe ganz auf einem Schachbrett, nie beschnitten).
> - Grenzen: Anfragen an die Schnittstelle haben mindestens 1 s Abstand vom Ende der vorigen, gemeinsam mit dem
>   automatischen Füllen und „Prüfen“ (es läuft immer nur eine). Vorschauen kommen ohne Abstand, höchstens 3
>   gleichzeitig. Je geöffnetem Dialog gelten höchstens 40 Anfragen, 800 Bildabrufe mit zusammen 128 MB und 6 volle
>   Bilder; dekodierte Vorschauen belegen höchstens 96 MB (zuerst geben verborgene Reiter ihre frei). Nach HTTP 429 folgt
>   eine Minute Pause, ohne automatische Wiederholung. Zeitgrenzen: 15 s bis zur Antwort, 15 s ohne Daten, insgesamt
>   30 s (90 s für ein volles Bild). Vorschauen sind höchstens 4 MB groß und kommen nur von https-Adressen unter
>   steamgriddb.com, ohne Weiterleitungen und ohne Schlüssel. HTTP 401/403 eines Bildes gilt nicht als abgelehnter
>   Schlüssel (auch beim automatischen Füllen). Vorschauen liegen nur im Speicher und werden beim Schließen freigegeben.
> - Übernehmen: Das volle Bild (höchstens 32 MB und 100 Megapixel) lädt HDRpilot erst beim **Übernehmen**, noch im
>   Dialog. Scheitert es, bleibt der Dialog im betroffenen Reiter mit einer Meldung offen; **Abbrechen** bricht ab.
>   Gespeichert wird es wie eine eigene Datei (`custom:`, `art\custom\`); es gilt unabhängig vom Schalter, und
>   **Zurücksetzen** wirkt wie bei eigenen Dateien. Die Herkunft wird nicht gespeichert; eigene Bilder heißen deshalb
>   „Eigenes Bild“.
> - Fenster: nie höher als die Arbeitsfläche des Bildschirms, auf dem die Bibliothek steht. Solange es offen ist, wächst
>   es nur (zur Mitte hin) und schrumpft nicht; nur die Mitte scrollt. Hoher Kontrast nutzt die Systemfarben.
> - Der Schlüssel geht nur an `www.steamgriddb.com`, nie ins Protokoll, in Meldungen oder an Bildschirmleser. Autoren,
>   Suchbegriffe und Spielnamen von SteamGridDB landen nicht im Protokoll; die Kennung beim Abruf bleibt wie in E92. Die
>   Einstellungen zeigen den letzten Netzwerkzugriff, Lauf oder Suche, mit der Zahl der Anfragen; ausgeschaltet mit
>   Schlüssel steht dort „Automatisches Laden aus · Suche in „Bild wählen…“ nur auf Klick“.
> - Prüfläufe: `HDRPILOT_TEST_STEAMGRIDDB=fixture` wirkt nur mit `--config-dir` und ersetzt nur die Übertragung durch
>   erzeugte Antworten (erzeugte Bilder, erfundene Autoren, kein Netz). Bildschirmfotos nutzen dieselben Daten. Neues
>   Gate `phase14-sgdb-ui.ps1`.

In the E89 SteamGridDB paragraph (`docs/entscheidungen.md:807-810`), add after the sentence ending „keinen
Netzwerkzugriff dafür“: „(Seit E93 regelt der Schalter nur das automatische Füllen; mit gespeichertem Schlüssel sucht
„Bild wählen…“ auf Klick auch bei ausgeschaltetem Schalter.)“

### 16.2 README

**Spielebibliothek, paragraph „Bild wählen“ (`README.md:118-123`)**, replace the text from „Ein anderes Bild wählst du …“:

> Ein anderes Bild wählst du per Rechtsklick › **Bild wählen…** oder im Abschnitt „BILDER“ der Details: Je Reiter
> **Titelbild**, **Logo** und **Cover** stehen unter „AUF DIESEM PC“ **Automatisch**, weitere lokale Bilder (etwa der
> Startbildschirm eines Xbox-Pakets), das erzeugte Bild und **Eigene Datei…** (PNG, JPG, WebP, BMP) zur Wahl;
> **Übernehmen** speichert, **Zurücksetzen** wählt im aktuellen Reiter wieder **Automatisch**. Eigene Bilder legt
> HDRpilot verkleinert in seinem Datenordner ab, die gewählte Datei bleibt unverändert. Mit eigenem API-Schlüssel von
> SteamGridDB sucht **Bei SteamGridDB suchen** im Abschnitt „STEAMGRIDDB“ nach weiteren Bildern des Spiels, immer nur
> auf Klick: 24 Vorschauen je Seite mit Autor, **Mehr laden** zeigt weitere; stimmt das Spiel nicht, wählst du unter
> „Spiel“ ein anderes. Das gewählte Bild lädt HDRpilot erst beim **Übernehmen** und speichert es als eigenes Bild. Ohne
> Schlüssel führt **Zu den Einstellungen** zum Schlüsselfeld (siehe „Einstellungen“).

**Details, „BILDER“ (`README.md:200-201`):** „Eigene Datei · nur auf diesem PC“ becomes „Eigenes Bild · nur auf diesem
PC“.

**Settings table row (`:264`):**

> | SteamGridDB (online) | Mit eigenem API-Schlüssel: Suche in **Bild wählen…** auf Klick; **Fehlende Bilder automatisch laden** füllt zusätzlich fehlende Titelbilder, Logos und Cover | aus |

**SteamGridDB (online), settings paragraph (`:318-324`):**

> **SteamGridDB (online)** ist die einzige Bildquelle aus dem Netz. Ohne eigenen API-Schlüssel von steamgriddb.com gibt
> es keinen Netzwerkzugriff dafür. Den Schlüssel trägst du ein und bestätigst ihn mit **Prüfen**; Feld und Knopf sind
> immer bedienbar, **Schlüssel entfernen** löscht ihn wieder und beendet auch eine laufende Suche. Mit Schlüssel kannst
> du in **Bild wählen…** selbst suchen, immer nur auf Klick. Darunter lädt **Fehlende Bilder automatisch laden**
> (Standard: aus) zusätzlich Titelbilder, Logos und Cover, die lokal fehlen und die du nicht selbst gewählt hast: nach
> dem Einschalten, nach dem Prüfen eines Schlüssels, mit **Fehlende Bilder jetzt laden** und höchstens einmal am Tag
> eine Minute nach dem Start, nie während eines Spiels. Der Schlüssel wird verschlüsselt (DPAPI) für dein Windows-Konto
> gespeichert und nie ins Protokoll geschrieben; die Zeile unter dem Schlüssel zeigt den letzten Netzwerkzugriff, Lauf
> oder Suche, mit der Zahl der Anfragen. Ausschalten oder ein Spielstart halten einen laufenden automatischen Abruf
> sofort an; ausgeschaltet zeigt HDRpilot wieder die lokalen bzw. erzeugten Bilder, die automatisch geladenen bleiben
> für ein erneutes Einschalten liegen. Bilder, die du in **Bild wählen…** übernommen hast, gelten wie eigene Bilder
> unabhängig vom Schalter.

**So funktioniert es, „SteamGridDB:“ (`:451-455`):**

> **SteamGridDB:** nur mit eigenem Schlüssel (siehe „Einstellungen“); automatisch nur eingeschaltet, in **Bild wählen…**
> nur auf Klick. Gefragt wird `www.steamgriddb.com`, Steam-Spiele über ihre App-ID, andere über den Namen (automatisch
> zählt nur ein Treffer mit gleichem Namen; in **Bild wählen…** wählst du sonst aus der Trefferliste, und bei Spielen
> ohne Steam-App-ID merkt HDRpilot die Wahl mit **Übernehmen**), und nur nach unbewegten Bildern ohne NSFW-, Humor- oder
> Epilepsie-Markierung. Bilder lädt HDRpilot nur von https-Adressen unter steamgriddb.com, ohne Weiterleitungen und ohne
> Schlüssel. Anfragen an die Schnittstelle haben mindestens 1 s Abstand vom Ende der vorigen, und jede Übertragung hat
> Zeitgrenzen. Automatisch gelten höchstens 15 MB je Bild und 200 Anfragen je Lauf, und was SteamGridDB nicht hat, fragt
> es 30 Tage lang nicht erneut; geladene Bilder liegen in `art\sgdb\`. In **Bild wählen…** gelten je Fenster höchstens
> 40 Anfragen; Vorschauen sind höchstens 4 MB groß, liegen nur im Speicher und laden höchstens zu dritt gleichzeitig. Ein
> übernommenes Bild ist höchstens 32 MB groß und liegt danach in `art\custom\`.

**Network sentence (`:570-571`):**

> Netzwerk nutzt es nur für die HDR-Liste und, mit eigenem Schlüssel, für SteamGridDB (automatisch nur, wenn du es
> einschaltest; in **Bild wählen…** nur auf Klick).

„Speicherorte“ (`:604-605`) needs no change: picked images are own images under `art\custom\`.

### 16.3 `GATES.md`

**Change G10.4** (`GATES.md:400`). New text; CHECK and EVIDENCE unchanged:

> - [x] G10.4: SteamGridDB optional: standardmäßig aus, nur mit eigenem API-Schlüssel (verschlüsselt gespeichert wie
>   das PCGamingWiki-Bot-Passwort), füllt automatisch nur fehlende Bilder, klare Anzeige des Netzwerkzugriffs; ist das
>   automatische Füllen aus, gibt es nachweislich keinen automatischen Netzwerkzugriff dafür (seit E93 sucht „Bild
>   wählen…“ mit gespeichertem Schlüssel nur auf Klick, siehe G14.2), ohne Schlüssel gar keinen; per Unit-Tests mit
>   Attrappe (nie das echte Netz in Tests) belegt

**New Phase 14** (after Phase 13; renumber if the UI batch or the game-colour track takes Phase 14):

```markdown
## Phase 14: SteamGridDB-Suche in „Bild wählen…“ (Wunsch des Nutzers 2026-10-22, E93)

Scope: Im Dialog „Bild wählen…“ je Reiter (Titelbild, Logo, Cover) ein Abschnitt „STEAMGRIDDB“ unter den lokalen
Bildern: Spielzuordnung „Spiel: <Name> ▾“ mit Spielwahl, **Bei SteamGridDB suchen** nur auf Klick, 24 Vorschauen je
Seite mit Autor, **Mehr laden**, nur unbewegte Bilder ohne NSFW-/Humor-Markierung; das volle Bild lädt erst
**Übernehmen** und speichert es wie eine eigene Datei. Ohne Schlüssel Hinweis und **Zu den Einstellungen**; mit
Schlüssel auch bei ausgeschaltetem automatischem Füllen. Dazu: eine Kachelform je Bildart für lokale und Online-Bilder,
Fenster nie höher als die Arbeitsfläche und ohne Schrumpfen, hoher Kontrast, Schalter der Einstellungen in eigener
Zeile, ein gemeinsamer Abstand aller Anfragen und Zeitgrenzen für jede Übertragung. Kein Netz in Tests und Prüfläufen
(Attrappen, erzeugte Testdaten), keine Spielbilder in Testdaten und Bildschirmfotos. Spezifikation: siehe E93. Als
eigenes Test-Update nach den UI-Wünschen vom 2026-10-22.

- [ ] G14.1: Schnittstelle im Kern: Seiten mit Vorschau, Autor, Größe und Filtern (static, nsfw/humor/epilepsy false,
  Cover 600×900/342×482/660×930, page ohne limit), Ende erst mit leerer Seite (404 ab Seite 1 = Ende), Trefferliste und
  games/id mit Jahr, bereinigte Suchbegriffe und Namen, erlaubte Rechner auch für Vorschauen und nach Weiterleitungen,
  Vorschau nur aus thumb, HTTP 401/403 eines Bildes kein abgelehnter Schlüssel, getrennte Budgets, Vorschauen ohne
  1-s-Abstand, gemeinsamer exklusiver Abstand der Schnittstelle über Threads, Zeitgrenzen und sofortiger Abbruch
  hängender Übertragungen, Größengrenzen 4 MB/32 MB/15 MB, Fehlerarten, Schlüssel nie in Ausnahmen; mit Gegenproben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDbSearchClient" -MinTests 30
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.2: Dienst, Sitzung und Bilddaten: ohne Schlüssel keine Sitzung und kein Handler, mit Schlüssel kein Handler vor
  dem ersten Klick; Suche auch bei ausgeschaltetem Schalter, automatischer Lauf weiter nur eingeschaltet; Zuordnung
  (App-ID, gemerkt mit Name und Jahr, gleicher Name, mehrdeutig, Trefferliste), geteilte Auflösung ohne gemerkte
  Fehlschläge, ein Abstand für Lauf, Prüfen und Sitzungen, höchstens 3 Vorschauen gleichzeitig, Warteschlange abbrechbar,
  Grenzen je Dialog, 60 s Pause nach 429, Schlüssel entfernen und Beenden brechen sofort ab, Zuordnung nur ohne
  Steam-App-ID gemerkt und danach neu gefüllt, kein Bild der alten Zuordnung gespeichert, keine
  Autoren/Begriffe/Namen/Adressen/Schlüssel im Protokoll; Bilder aus Daten speichern wie Dateien; Testdaten-Lieferant
  nur mit --config-dir, ohne echten Handler; mit Gegenproben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDbSearchSession|FullyQualifiedName~SteamGridDbFixture|FullyQualifiedName~ArtworkCustomDataTests" -MinTests 37
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.3: ViewModel des Dialogs: Zustände und Texte aus resx (ohne Schlüssel, nicht lesbar, bereit, lädt, langsam,
  Ergebnisse, Ende, Grenze je Reiter, kein Treffer, keine Bilder, Schlüssel abgelehnt, zu viele Anfragen, offline,
  Zeitüberschreitung, Grenze erreicht), je Reiter eigener Klick, Mehr laden bis zur leeren Seite, Speicher verborgener
  Reiter freigegeben, Spielwahl, eine Wahl je Reiter über lokale und Online-Liste, Spielwechsel mit Hinweis,
  Herunterladen beim Übernehmen noch im Dialog mit Meldung im betroffenen Reiter, Abbrechen ohne Absturz, Speichern als
  eigenes Bild, Zurücksetzen, Abbrechen schreibt nichts, Schließen gibt frei und lässt die lokale Wahl bedienbar, Link zu
  den Einstellungen, Ansage nur bei Zustandswechseln; mit Gegenproben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~ArtworkPickerSearchTests" -MinTests 40
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.4: Fenster: nie höher als die Arbeitsfläche des Bildschirms der Bibliothek (1280×800 bei 100 und 125 %,
  1920×1080, 3440×1440), wächst nur und zur Mitte hin, nur die Mitte scrollt (auch per Mausrad über Kacheln), die
  Bildlaufleiste kostet keine Breite, Kacheln füllen die 830 DIP genau (lokal und online), Logos ganz auf Schachbrett,
  Tab-Reihenfolge, Fokus nach Suche und Mehr laden nur aus dem Abschnitt heraus, Esc und Enter in der Spielwahl,
  Kennungen, Muster und Namen für Bildschirmleser, Live-Ansage, hoher Kontrast mit lesbarem Text, Effektstufen
  Aus/Reduziert/Voll, helles und dunkles Thema, Übernehmen nach Schließen ohne Fehler
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~ArtworkPickerSearchWindowTests" -MinTests 17
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.5: Einstellungen: Schlüsselfeld und „Prüfen“ auch bei ausgeschaltetem Schalter, „Prüfen“ speichert dann ohne
  Lauf, Schalter in eigener Zeile mit sichtbarer Beschriftung, Statuszeile „Automatisches Laden aus · Suche in „Bild
  wählen…“ nur auf Klick“ bzw. letzter Zugriff (Lauf oder Suche), Prüfmeldung bleibt nicht hängen, abgelehnter Schlüssel
  aus einer Suche rot, Sprung zum Schlüsselfeld
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SettingsAppearancePageTests|FullyQualifiedName~SettingsSteamGridDbWindowTests" -MinTests 20
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.6: Bisherige Zusagen von G10.4 bleiben (aus: kein automatischer Netzwerkzugriff, ohne Schlüssel gar keiner),
  zusammen mit den neuen SteamGridDB-Tests
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~SteamGridDb" -MinTests 90
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.7: Testinstanz mit erzeugten SteamGridDB-Antworten (kein Netz, nur UI-Automation): Schlüssel bei
  ausgeschaltetem Schalter prüfen und speichern ohne Lauf; keine Anfrage vor dem Klick; 24/48/70 Ergebnisse mit Autor,
  Ende mit leerer Seite; nur www./cdn2.steamgriddb.com, Schlüssel nur an die Schnittstelle; Übernehmen speichert ein
  600×900-Cover als eigenes Bild, Zurücksetzen entfernt es, Abbrechen schreibt nichts; Spielwahl, gemerkte Zuordnung nur
  ohne Steam-App-ID; Fehlerzustände samt Zeitüberschreitung; Link zum Schlüsselfeld; keine Vorschau-Threads nach dem
  Schließen; Bildschirmfotos aller Zustände hell und dunkel, auch klein und in hohem Kontrast, dazu die
  Einstellungskarte
  CHECK: ps tools\gates\phase14-sgdb-ui.ps1
  EXPECT: PHASE14_SGDB_UI_OK
  EVIDENCE: pending.

- [ ] G14.8: Einstellungen per UI-Automation weiter grün, mit der angepassten Prüfung „Prüfen auch bei ausgeschaltetem
  Schalter bedienbar“
  CHECK: ps tools\gates\phase10-settings-ui.ps1
  EXPECT: PHASE10_SETTINGS_UI_OK
  EVIDENCE: pending.

- [ ] G14.9: Bibliothek und Dialog ohne Schlüssel weiter grün (Abbrechen schreibt nichts, kein Netz)
  CHECK: ps tools\gates\phase10-ui.ps1
  EXPECT: PHASE10_UI_OK
  EVIDENCE: pending.

- [ ] G14.10: Bilder geprüft: Bildschirmfotos aus G14.7 bei 1280×800, 1920×1080 und 3440×1440, hell und dunkel,
  zugeschnitten und vermessen (Kachelgrößen, Abstände, rechte Kante bei 830 DIP, Luft um die Trennlinie); Vergleich an
  den Nutzer geschickt, bevor das Test-Update installiert wird
  EVIDENCE: pending.

- [ ] G14.11: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und alle Gates ohne echte HDR-Umschaltung grün
  (nacheinander, gedrosselt)
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: pending.

- [ ] G14.12: README, E93 und GATES passen zu den resx-Texten (fett gesetzte Bezeichnungen, Einstellungstabelle)
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  EVIDENCE: pending.

- [ ] G14.13: Keine lokalen Pfade, Namen oder echten Spieldaten in neuen Dateien (Testdaten-Lieferant, Tests, Doku)
  CHECK: ps tools\gates\publish-check.ps1
  EXPECT: PUBLISH_CHECK_OK
  EVIDENCE: pending.

- [ ] G14.14: Livetest nur mit dem Nutzer und seinem eigenen Schlüssel (der Nutzer klickt; Protokollstufe vorher auf
  „Ausführlich (Debug)“, danach zurück): Rechner der Vorschauen (das Debug-Protokoll nennt nur Rechnernamen), Seite 1 unterscheidet
  sich von Seite 0, ob eine Seite mit weniger als 50 Einträgen immer die letzte ist, was eine Seite hinter dem Ende
  liefert, ob `limit` in der Antwort steht, was der CDN für eine fehlende Vorschau antwortet (403 oder 404),
  Logo-Vorschauen behalten ihre Durchsichtigkeit, Namen mit „+“, „/“, Apostroph und Umlauten finden das Spiel,
  342×482-Cover sehen zugeschnitten gut aus, WebP wird gezeigt, nie HTTP 429 bei normaler Nutzung
  EVIDENCE: pending. Nur zusammen mit dem Nutzer.

- [ ] G14.15: Abnahme durch den Nutzer am echten System (Aussehen bei 1280×800, 1920×1080 und 3440×1440, hell und
  dunkel, Bedienung mit Tastatur)
  EVIDENCE: pending.
```

### 16.4 `docs/stand.md`

Add a dated entry: spec written, gates G14.x open, the branch name, "ships as its own test update after the UI batch,
together with the game-colour track", and the split between cloud session and local Windows work (§17).

---

## 17. Umsetzung

**Branch:** `feature/sgdb-search` from `main`, **after** the UI batch (`ui/details`, `ui/hero`, `ui/scroll`) is merged.
Those worktrees have uncommitted changes in files this feature also touches:

- `LibraryStrings.resx`
- `Themes/Library.xaml`
- `LibraryViewModel.cs`
- `AppServices.Library.cs`
- `MainWindow.*`

The keyboard-only focus cue and, if it exists then, a reusable thin scroll bar style come from that batch. The
game-colour track touches the theme tokens; whichever branch merges second adds `Hp.Brush.Plate` (or its own tokens) to
the other's token files.

**Where:** code, tests, gate script and docs can be written in the cloud session. Per `docs/stand.md` it has no WPF
start, no UI gates, no setup and no acceptance. Every step marked "local" runs on the user's Windows PC: one workflow at a
time, `dotnet` throttled (BelowNormal, at most 12 of 24 threads), and never next to other heavy work.

**Order** (each step with its tests):

1. Core: models, client, pacer turn, timeouts, the 401/403 rule, with tests (G14.1).
2. Artwork service: `Decode(byte[])`, `Validate(…, saveWidth)`, `SaveCustomImageData`, `CustomSaveWidth`,
   `ReplaceSteamGridDbGame`, the „Eigenes Bild“ labels, with tests.
3. Service, session, fixture, and the `Fill` guards (G14.2, G14.6).
4. Picker view model, `ChooseArtworkAsync`, `ILibraryContext` members, `SettingsTarget` (G14.3).
5. Dialog layout and tile geometry, `DialogPlacement`, `DialogContrast`, `ExpandToggle`, styles, `Hp.Brush.Plate`
   (G14.4). Renders are checked by eye locally at three sizes, light and dark.
6. Settings card, status logic, `ShowTarget` (G14.5, G14.8).
7. Snapshot scenes and `phase14-sgdb-ui.ps1` (G14.7, local).
8. Docs: E93, the E89 pointer, README, GATES, stand (G14.12, G14.13).
9. Locally: the full run (G14.9, G14.11), an independent review with counter-checks, the render review with the user
   (G14.10), the test update (backup and compare settings as usual), the live test G14.14 with the user, and
   acceptance G14.15.

**Files** (all repo-relative):

- Core:
  - `src/HdrPilot.Core/Artwork/SteamGridDbClient.cs`
  - `src/HdrPilot.Core/Artwork/SteamGridDbSearchModels.cs` (new)
  - `src/HdrPilot.Core/HdrSupport/PcgwClient.cs` (`RequestPacer.Enter`)
- App services:
  - `Services/Artwork/SteamGridDbContracts.cs`
  - `SteamGridDbService.cs` and the new `SteamGridDbService.Search.cs`
  - `SteamGridDbSearchSession.cs` (new)
  - `SteamGridDbFixture.cs` (new)
  - `ArtworkContracts.cs`, `ArtworkImaging.cs`, `ArtworkService.Custom.cs`, `ArtworkService.Coverage.cs`
- App wiring:
  - `AppServices.cs` (`ShowSettingsWindow(target)`, test log for the settings window)
  - `AppServices.Artwork.cs` (fixture hook, `TestLog`)
  - `AppServices.Library.cs` (scenes, picker disposal)
  - `AppServices.Shell.cs` (settings scene)
- View models:
  - `ViewModels/SettingsTarget.cs` (new)
  - `ViewModels/Library/ILibraryContext.cs`, `AppLibraryContext.cs`
  - `ArtworkPickerViewModel.cs`
  - `SteamGridDbSearchViewModel.cs` (new)
  - `LibraryViewModel.Showcase.cs`
  - `ViewModels/Shell/AppearancePageViewModel.cs`
- Views:
  - `Views/Library/ArtworkPickerDialog.xaml(.cs)`
  - `Views/Library/LibraryControls.cs` (`ImageFill.Stretch`, `ExpandToggle`)
  - `Views/Library/DialogPlacement.cs`, `Views/Library/DialogContrast.cs` (new)
  - `Themes/Library.xaml`, `Themes/Tokens.Dark.xaml`, `Themes/Tokens.Light.xaml`, `Theming/ThemeKeys.cs`
  - `Views/Shell/SettingsWindow.xaml(.cs)`
- Resources: `LibraryStrings.resx`, `ShellStrings.resx`, `ArtworkStrings.resx`.
- Tests: §12.
- Gates: `tools/gates/phase14-sgdb-ui.ps1` (new), `phase10-settings-ui.ps1`.
- Docs: `README.md`, `docs/entscheidungen.md`, `GATES.md`, `docs/stand.md`.
- Doc comments that become false and must change: `SteamGridDbContracts.cs:41-45`; `SteamGridDbService.cs:67`
  (`HandlerFactory`, „nur mit Schalter an“) and `:86-94` (class summary); `ArtworkPickerViewModel.cs:87` (`SourceNote`);
  `SteamGridDbClient.cs:54-61` (pacing and Download wording).

**Version:** it is a feature, so a minor bump, set at release time together with the UI batch's version.

---

## 18. Vetopunkte und offene Fragen

No question blocks the build. These decisions were taken without asking, and the user may veto them at acceptance:

1. Tab order stays Titelbild, Logo, Cover, unlike the sketch's Cover first (D4).
2. The full image loads before the dialog closes, and the dialog stays open on errors (D13).
3. A corrected match for a game without a Steam AppID drops that game's earlier automatic SteamGridDB images; with the
   switch on, a fill run follows (D12).
4. Custom images, including picked SteamGridDB images, are called „Eigenes Bild“ (D14).
5. Covers also offer 342×482 and 660×930, cropped to 2:3 (D10).
6. The uniform tile geometry changes the local tiles too: covers 2:3 at 6 per row, heroes 4 per row (D20).
7. The settings card gets the switch in its own labelled row (D21).
8. The list ends only with an empty page, so the last „Mehr laden“ can bring nothing new (D5, may change after G14.14).

Questions the user must decide now: none.

---

## 19. Nebenbefunde (beheben im selben Zweig)

1. **`Fill` with an empty Steam `SourceId`** (`SteamGridDbService.cs:565`): `"".All(char.IsAsciiDigit)` is true, so
   `appId` becomes `""`, the name search is skipped and `FirstAsset` throws `ArgumentException`, which `Run` does not
   catch. It cannot happen with today's data, because `ConfigStore.cs:110` drops games with an empty `SourceId` on load.
   Fix it as a defensive guard (`!string.IsNullOrEmpty(game.SourceId) && …`) with a test; the session uses the same
   guard.
2. **An image 401/403 ends the automatic run as "key rejected"** (`SteamGridDbClient.cs:338`, `Fill` catches only
   Blocked/InvalidResponse/HttpError). One missing CDN file answered with 403 stops the whole run and shows
   „Schlüssel abgelehnt“. Fixed by §4.9.
3. **The pacer is check-then-wait** (`PcgwClient.cs:52-72`). Two threads (a run and „Prüfen“) can send at the same
   moment. Fixed by §4.10 for SteamGridDB; PCGamingWiki keeps its sequential use.
4. **Body reads are unbounded and not cancellable** (`SteamGridDbClient.cs:346-371`). `HttpClient.Timeout` ends with the
   headers, and the synchronous `Read` ignores the token, so a stalled response blocks a run until the server closes it.
   Fixed by §4.8.
5. **The settings check message sticks** (`AppearancePageViewModel.cs:470-473`, cleared only at `:532-533`). It hides
   every later status until „Schlüssel entfernen“. Fixed by §10.2.
6. **Previews are cropped in the picker** (`LibraryControls.cs:169`, `ArtworkPickerDialog.xaml:17,35-37`): logos are
   cropped, local covers become squares, and local heroes show only their middle. Fixed by §8.3 and §8.4.
7. **Hard-coded plate colour** `#FF0B0C11` (`ArtworkPickerDialog.xaml:18`) ignores the light and high-contrast themes.
   Replaced by `Hp.Brush.Plate` (§8.4).
8. **No high-contrast handling in the dialog.** Added (§8.7).
9. **`CreatePickerScene` never disposes its picker** (`AppServices.Library.cs:418-434`). Fixed with the new scenes
   (§14).
10. **„Aus · kein Netzwerkzugriff“ would be wrong** with a stored key under E93. The text now applies only without a key
    and says so (§10.3).
11. **`IsLoading` of the picker is not bound anywhere.** It stays as it is; the online section has its own states.

---

## Anhang A: Prüfung der Kritik

Each finding of the two reviews of `spec.md` was checked against the code at a4e996f and the local API reference
(OpenAPI file, the MIT wrappers, SteamGridDB's own Decky plugin for comparison).

### A.1 Nutzer- und UX-Kritik

| # | Finding | Verdict | Where / why |
|---|---|---|---|
| H1 | Window height jumps and shrinks | accepted | D19, §8.2: grow only, centre-keeping, `MinHeight` lock. No reserved message row: with grow-only, a message makes the window grow once and never jump back, and after the first search the window is usually at its maximum anyway |
| H2 | Switch still looks like on/off for all of SteamGridDB | accepted | D21, §10.1 (confirmed: `SettingsWindow.xaml:1174-1177`, the label exists only as an automation name) |
| H3 | High-contrast map incomplete | accepted | D23, §8.7 (confirmed: `ToolbarToggle.cs:281-307` maps ten keys) |
| H4 | No body timeout, no cancellation | accepted, without a progress display | §4.8; bounded times make a progress bar unnecessary |
| M1 | Ready height underestimated; button below the fold | accepted | §8.1/§8.2: button left under the header, one-line note, caption and hint; new estimate ≈ 655 DIP; at 125 % on 1280×800 a short scroll stays and is documented |
| M2 | Local and online previews crop differently | accepted | D20, §8.3 (exact 830 DIP grids with 10 DIP gaps) |
| M3 | Deviations from the sketch; no preview before building | partly | Deviations listed (§2), render review with the user before install (G14.10). Not accepted: asking about the tab order now, because a later reorder is cheap (veto item 1) |
| M4 | Budget likely exhausted; advice self-defeating | accepted | D7, §9: held-memory bound with release of hidden tabs, tile limit per tab, API cap kept, text without "close and reopen" |
| M5 | Live region floods screen readers | accepted | §8.8 (`Announcement` separate from progress) |
| M6 | Shared pacer does not guarantee 1 s | accepted | D6, §4.10 (confirmed: `PcgwClient.cs:52-72`) |
| M7 | Corrected match leaves the game without images; commit not shown | accepted | D12, §5.5 (fill run when on), §7.4 commit hint, `Picker_MatchRemembered` |
| M8 | Several games with the same name | accepted | D11, §5.3, `Picker_SgdbMatchAmbiguous` |
| M9 | `RunOnWorker` logs routine errors as errors | accepted | §5.2 (confirmed: `SteamGridDbService.cs:709-712`) |
| M10 | Settings status logic does not fit the view model | accepted | §10.2 (confirmed: sticky `_steamGridDbMessage`; `SteamGridDbStatusIsError` ignores `LastError`) |
| L1 | Remembered match not shown or offered | accepted | §5.3 (`DescribeAsync`, one request in the same click), §7.4 first entry |
| L2 | Failing kind in another tab not named | accepted | §7.7: switch to the failing tab and focus its tile |
| L3 | Focus lost while loading | accepted | §7.2: the button stays, busy |
| L4 | Settings link discards choices silently | accepted | §7.8: `Picker_SgdbOpenSettingsDiscard` while `HasChanges` |
| L5 | Light Surface2 is white | accepted | §8.4 `Hp.Brush.Plate` (confirmed: `Tokens.Light.xaml:44`) |
| L6 | Motion details | accepted | §8.6 (image fade on arrival, `offsetY: 0` for Reduziert, BringIntoView before the reveal) |
| L7 | Slow calls look frozen; timeout text | partly | Slow text after 4 s and a timeout text accepted; an activity indicator rejected (no looping animations) |
| L8 | Wording | accepted | „Humor“, Steam entry with the name, how-matched in the tooltip, size line only for unusual sizes |
| L9 | „Eigene Datei“ for picked images | accepted | §6.3 „Eigenes Bild“ (veto item 4) |
| L10 | Corner radius and ring fade differ | accepted | §8.3: radius 16 and an instant ring for both |
| L11 | 830 not 832; no scroll bar style | accepted | §8.1 (confirmed: `MaxWidth="830"`, no `ScrollBar` style in Themes) |
| L12 | Primary monitor only | accepted | §8.2 `DialogPlacement.WorkAreaFor(owner)` |
| L13 | Empty-SourceId bug unreachable | accepted | §19.1 (confirmed: `ConfigStore.cs:110`) |
| L14 | Stale doc comments | accepted | §17 file list |
| L15 | `IsApplying` list incomplete | accepted | §7.7 |
| L16 | Over-engineering | accepted | Pins simplified (§7.6), `ShellScroll.ChainWheel` instead of a new template (§8.5), ring fade dropped, cumulative decode budget replaced (§9) |

### A.2 Technische Kritik

| # | Finding | Verdict | Where / why |
|---|---|---|---|
| 1 | CDN 401/403 read as a rejected key | accepted | §4.9, §19.2 (confirmed: `SteamGridDbClient.cs:338`) |
| 2 | Async `OnApply` can crash on cancel | accepted | §7.7 |
| 3 | `ShowcaseIntegrationTests` would break | accepted | §7.9: `Dispose` touches only the online part (confirmed: tests drive the picker after a false dialog result) |
| 4 | Stalled download keeps threads and client | accepted | §4.8, §5.2 |
| 5 | Pacer race | accepted | §4.10 |
| 6 | Preview fallback contradicts the user's decision | accepted | D9: thumb only, „Keine Vorschau“, preview byte cap |
| 7 | Contract cannot carry the name fallback; deadlock risk | accepted | `ResolveByNameAsync` as a separate call (§5.1, §5.2) |
| 8 | 404 on a later page | accepted | §4.6 |
| 9 | Settings status will not show the new texts | accepted | §10.2 |
| 10 | Gate expects the thumbnail size | accepted | §13 full sizes in JSON, §15 step C |
| 11 | Gate steps need keyboard input | accepted | §15.1: UIA patterns only; chooser entries are buttons (§7.4) |
| 12 | Idle gate and thread count prove nothing | accepted | `ActiveWorkers` test and a test-log line; `phase3-idle` stays only in the full run |
| 13 | Steam AppIDs could be real; scene fallback | accepted | 9990150/9990404; no fallback game (§14) |
| 14 | `SettingsSection` and `KeyState` name clashes | accepted | `SettingsTarget`, `LoadKeyState()` (confirmed: `Views/Shell/SettingsSection.cs:11`, `SteamGridDbService.cs:362`) |
| 15 | Cancellation rules contradict each other | accepted | §5.1/§5.2 shared resolve, queue token versus session token (§5.4) |
| 16 | Primary-monitor work area | accepted | §8.2 |
| 17 | Too many announcements | accepted | §8.8 |
| 18 | Fixture traps (sync `Send`, log races) | accepted | §13 (confirmed: the test fakes override `Send`) |
| 19 | Routine errors logged as errors | accepted | §5.2 |
| 20 | End-of-list rule relies on unconfirmed behaviour | accepted | End on an empty page, as the official plugin does (§4.6); G14.14 records short pages |
| LOW | Decoded cap mapping; `CommitMatch` three-way; deletes outside the lock and the old-match store; hit-name cleanup, year range, `image/jpg`, tag case; surrogate-safe cut; save-width check; no temp-file default; `Ui.Delay` for the retry timer; unsubscribe in `CloseSession` and scene disposal; Group peer, ExpandCollapse, focus only from inside the section; author-less item name; gate steps D/E/G/K; G14.5 class; `Interlocked`; hand-written CRC32; stale comments; latest of run or search; visible switch caption; G14.14 additions; D4 stated as a deviation | all accepted | §§4–17 as cited in each section |

