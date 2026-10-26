# Implementation spec (final): "Farben folgen dem Spiel" (per-game UI accents)

Status: final. This replaces `spec.md`. It includes the design critique and the tech critique; every finding was checked
read-only against `phase5/release` = `main` (1.5.0). Section 17 lists what was accepted and rejected.

Paths are relative to the repo root. `App` = `src/HdrPilot.App`, `Core` = `src/HdrPilot.Core`. The colour numbers come from
small Perl checks on generated colours. No real game art was used, and nothing was built, tested or started. C# timings are
estimates.

**Where to run (user instruction, 2026-10-22):** do the implementation in the cloud session. Run locally only what needs the
Windows desktop: the WPF window and render tests, the UI-automation gates, the idle gate and the user's acceptance check.
Local runs follow the resource limits: one workflow at a time, `dotnet` throttled (BelowNormal, at most 12 of 24 threads),
no artificial load.

---

## 0. Entscheidungen

1. **New switch.** "Farben folgen dem Spiel" (`colorsFollowGame`, default **off**) sits next to "Hintergrund folgt dem Spiel".
   The two switches are independent.
2. **Scope.** All accents of the library window follow the game (list in 1.1), including the capsule halo of the toolbar.
   These keep the scheme: the settings window (except its live preview), the tray menu, dialogs and icons. With high
   contrast, game colours are off.
3. **Source order.**
   - The logo wins when it is colourful enough: at least 10 % of its area is chromatic, or its hue is within 40° of the
     cover or hero colour, or cover and hero are gray.
   - Otherwise the cover, then the hero, then the scheme.
   - Atmosphere and shine come from secondary colours of the art.
4. **Gray art** switches hard to the **exact** app-level scheme palette. There is no blending.
5. **Colours are read on demand only:**
   - when the switch is on (library window), or
   - when the "Darstellung" page shows a preview game.

   Each game is read once per art signature, from small decodes on the artwork worker. The result is stored as `colors` in
   `art\index.json`. A user who leaves the switch off (the default) pays nothing.
6. **Mapping.** The game palette works like an automatically generated preset:
   - lightness band per hue
   - chroma floor 0.12, raised to 0.15 when a dark brand colour has to be lifted (so it doesn't turn into a dusty pastel)
   - derived atmosphere at ±40°, at least 30° from the accent
   - "mud" checks so gradients have no gray middle; in Tritone this includes accent to shine
   - the user's Duotone/Tritone choice is kept
7. **Contrast** follows the presets: 4.5 for text on accent, 4.5 for accent text, 3.0 for controls, in dark and light,
   against the scheme surfaces and the extreme base tones of the library. With automatic text colour, text on accents is
   always Ink. In the light theme, yellow and gold text and rings are darkened toward **amber** (hue 75°), not olive.
8. **Applying the colours:**
   - a window-level layer of persistent brushes
   - fades run in **OKLCH**, starting from the colour on screen, so they never pass through gray
   - the raw Color keys swap once, **at the start** of a fade
   - no resource dictionary is swapped per game
9. **Debounce with a leading edge:**
   - changes faster than 300 ms, and held-key repeats, keep the current colours
   - the last game is applied 300 ms after the last change
   - colours are applied **together with the game's art**, in step with the hero crossfade
10. **Durations:** Voll 0.9 s, Reduziert 0.3 s. Instant with Aus, while the window is hidden or minimised, during play with
    pause, and on a theme change.
11. **Both switches together:**
    - Farben an, Hintergrund aus: the background is a **calm, constant** version of the scheme (chroma at most 0.03).
    - Both on: gray hero art mixes with the game accents currently shown.
12. **No new ThemeKeys** and no edits to the library XAML files. One fallback is pre-authorised (5.7).
13. **Numbering:** decision record **E94**, gates **Phase 15**. Renumber if the parallel tracks claim different numbers.
14. **User questions: none.** The calm background (11) and the amber text (7) are look decisions; the user checks them in G15.10.

---

## 1. Behaviour

### 1.1 What follows the game when the switch is on

"Accents" means everything the scheme colours today in the library window:

- the hero accents: sweep and core of the HDR moment in `HeroStage`
- the Starten and HDR buttons, including their glows
- the details pane: section lines, tags, badges, accent text
- the selection in Liste, Raster and Regal: ring, fill, halo, `Lib.Brush.RowSelection`
- the toolbar: light seam, streak, and the capsule halo of `ToolbarToggle`.
  In 1.5.0 the capsule halo is hard-coded to the Abendrot colours (`ToolbarToggle.cs:326-341`). It now follows the accents
  shown, which also fixes it for every other scheme.
- switches, segment radios, chips, the focus ring, badges, the filter count plaques
- the window edge glow of the HDR moment, the HDR-moment particles, the Starten burst

These keep their colours: `Lib.Brush.NewBadge` (`#FF94B9`, a fixed "new" signal like Ok and Amber), the source dots, state
colours, text colours and surfaces.

### 1.2 How the two switches interact

| Farben folgen | Hintergrund folgt | Result |
|---|---|---|
| aus (default) | aus | As in 1.5.0. |
| aus | an (default) | As in 1.5.0. |
| an | aus | Game accents. The background (`AmbientBackdrop`, the window brushes derived from it, and the hero's rays, light points, bloom halo and flash) uses the **calm scheme**: the scheme's three colours with OKLCH chroma capped at 0.03, same L and h. It is the same for every game. |
| an | an | Game accents; background from the art. Gray hero art mixes 50 % with the **accents shown** (`_accents`) instead of the scheme. |

Why the calm scheme: the library shows the scheme's atmosphere and shine as large glows and hero rays. Measured in code:
`AmbientBackdrop.Apply` uses A2 at 34 %, A3 at 22 % and A1 at 30 %, and `HeroStage` draws rays at A3 32 % even when HDR is
off. Pink and orange glows under green buttons would clash. The calm scheme still never changes per game, which was what the
user decided "Hintergrund aus" means.

### 1.3 Gray art, missing art, empty selection

| Situation | Result |
|---|---|
| Colours are known but not usable (no source reaches confidence 0.5) | Fade to the exact app-level scheme palette. |
| Colours not yet known, or the selected game's art not yet loaded | Keep the colours shown now. They are applied when both have arrived. Never go through the scheme on the way. |
| No selection (search without a match) | Keep the colours shown now. |
| Generated images, name colours (`AmbientColors.FromName`), name-rendered logos | Never a source. A game with only those uses the scheme. |
| High contrast (`SystemParameters.HighContrast`, the existing `_highContrast` in `MainWindow.Toolbar.cs`) | The layer is removed at once and the scheme applies. The toolbar keeps its system colours. |
| The artwork service has no colour source (`NullArtworkService`, other fakes) | Scheme. |

### 1.4 Timing

| Trigger | Debounced | Duration |
|---|---|---|
| Selection change: list keys, grid/shelf keys, click, selection following the filter or search | yes (5.4), including held-key repeats | effect level |
| Colours or art for the current selection arrive or change (watcher, picker) | only while holding | effect level, starts together with the hero crossfade |
| Switch toggled; scheme, mode or automatic text colour changed | no | effect level |
| Theme dark ↔ light; high contrast changed | no | instant |
| Window opens and the colours are already known (memo, 3.2) | no | instant |
| Window opens and the colours are not known yet | no | They arrive like any colours. 0 while the window is not yet visible, otherwise the effect level in step with the hero crossfade. |

**Effect level, through `MotionController.Transition(AppearanceService.RecolorDuration)`:**
- Voll: 0.9 s, the same as `HeroStage.CrossfadeDuration`
- Reduziert: 0.3 s
- Aus: 0
- Also 0 when no one-shot effect is allowed: window hidden or minimised, or a game running with "Beim Spielen anhalten".

The fade path is OKLCH (5.2). The frame rate is `EffectPolicy.OneShotFrameRate`. The mouse wheel never changes the selection, so
it never recolours.

---

## 2. Core: extraction

### 2.1 Data model: new file `Core/Artwork/GameColors.cs`

```csharp
public enum GameColorSource { None, Logo, Cover, Hero }
public enum GameColorKind { Logo, Cover, Hero }

/// One candidate colour ("#RRGGBB", raw from the art, not yet mapped) and where it came from.
public readonly record struct GameSwatch(string Hex, GameColorSource Source);

/// Theme-independent colours of one game, read once per art state. Accent == null: no usable colour (gray art).
public sealed record GameColors(
    string? Accent,
    GameColorSource AccentSource,
    double Confidence,                     // of the accent source, 0..1, rounded to 2 decimals (kept when rejected, for the log)
    IReadOnlyList<GameSwatch> Atmospheres, // up to 3, best first
    IReadOnlyList<GameSwatch> Shines)      // up to 3, best first (Tritone only)
{
    public const double Accept = 0.5;
    public bool IsUsable => Accent is not null && Confidence >= Accept;
    public static GameColors None { get; } = new(null, GameColorSource.None, 0, [], []);

    // Value equality including the lists (records compare IReadOnlyList by reference otherwise).
    public bool Equals(GameColors? other) => other is not null && Accent == other.Accent && AccentSource == other.AccentSource
        && Confidence.Equals(other.Confidence) && Atmospheres.SequenceEqual(other.Atmospheres) && Shines.SequenceEqual(other.Shines);
    public override int GetHashCode() => HashCode.Combine(Accent, AccentSource, Confidence, Atmospheres.Count, Shines.Count);
}
```

The same file holds `IndexedGameColors` for storage, with `ToColors()` and `From(GameColors)`. The index's camelCase policy
gives these JSON fields:

| JSON field | Content |
|---|---|
| `v` | algorithm version |
| `acc` | hex or absent |
| `src` | plain **string** |
| `conf` | 2 decimals |
| `atm` | `[{ "c": "#RRGGBB", "s": "Cover" }]` |
| `sh` | same shape as `atm` |

- Sources are plain strings, not enums. The index uses `JsonStringEnumConverter`, and an unknown enum value would throw.
  `ArtworkIndex.Load` then discards the **whole** index, SteamGridDB state included.
- `ToColors()` parses leniently. An unknown `src` with a non-null accent counts as missing, so it is recomputed. A swatch with
  an unknown `s` is dropped.
- Size at most 260 bytes (3 + 3 candidates).

### 2.2 Helper: new file `Core/Artwork/ColorScience.cs` (pure, doubles, no WPF)

- sRGB 8-bit → linear through a 256-entry lookup table; linear → OKLab with `Math.Cbrt`; OKLab ↔ OKLCH
- WCAG Y = 0.2126 R + 0.7152 G + 0.0722 B (linear)
- in-gamut test; chroma clip by bisection (18 steps) at fixed L and h
- `HueDistance` (0–180°), `Smooth(e0, e1, x)` (smoothstep), `DeltaE` (Euclidean OKLab)
- `Mud(c1, c2)`: OKLCH chroma of the 8-bit sRGB per-channel midpoint, divided by the mean chroma of c1 and c2. WPF gradients
  interpolate in sRGB.
- `Lerp(c1, c2, t)` in OKLCH:
  - L and C are interpolated linearly
  - the hue takes the shorter arc; at exactly 180° it goes in the positive direction
  - if one end has C < 0.02, the other end's hue is used throughout
  - alpha is interpolated linearly
  - the result is gamut-clipped and rounded to 8 bit
- `Fit(color, FitRole role)`, below. `FitRole` = (YMin(h), YMax, CMin, CMax 0.24, LiftBoost).

**`Fit`:**
1. `(L0, C0, h)` = OKLCH of the colour. Target chroma `C* = clamp(C0, CMin, 0.24)`. If the colour's Y is below `YMin(h)`,
   also `C* = max(C*, LiftBoost)`.
2. Scan L from 0.30 to 0.995: first coarsely in steps of 0.01, then in steps of 0.001 within ±0.01 of the best coarse point.
   Each step is `k = GamutClip(L, C*, h)` with chroma `C_k`, rounded to 8 bit. A step is in the band if the **rounded**
   colour's Y lies in [YMin(h), YMax].
3. `floor = min(CMin, max C_k in band) − 0.003`. Among in-band steps with `C_k ≥ floor`, pick the one with the smallest
   `|L − L0| + 2·max(0, C* − C_k)`. On a tie, take the lower L.
4. If nothing is in the band (impossible for the bands below; the tests assert this), use the bisection of the old spec.

Results on generated examples (accent role):

| Input | Old | New |
|---|---|---|
| navy `#0B1F4D` | `#7191CF` (C 0.100) | `#618EED` (C 0.150) |
| maroon `#7A1020` | `#DE6F71` | `#E46C6F` |
| forest `#1E5631` | `#75B987` | `#35AA60` |
| dark teal `#0F5257` | `#4AB9C1` | `#00AFBA` |
| pastel pink `#F7C6D9` | C 0.076, fails the old band | `#FFA1C9` (C 0.121) |
| `#CCE0FF` | C 0.048 | `#85B5FF` |
| red `#D62828` | — | `#FF544B` |
| neon `#00F0FF` | — | unchanged |

Hue drift stays ≤ 2°.

### 2.3 Algorithm: new file `Core/Artwork/GameColorAnalysis.cs` (pure, static)

#### `Analyze(ReadOnlySpan<byte> bgra, int w, int h, GameColorKind kind) → SourceResult?`

`SourceResult` holds:
- families: mean OKLab weighted by w·C, share, edge share, skin flag, frame flag
- the best family and its confidence
- `ChromaShare` (new, step 12)

1. **Opaque logo** (no pixel with α < 250): it keeps the **logo** rules (m = 0.02, no skin rule), but ring weighting, frame
   detection, fill-over-outline and halo logic are switched off. A coloured plate can be the brand; a white or black plate has
   C≈0 and drops out on its own. A beige brand plate is no longer rejected as skin.
2. **Pixels:**
   - Body pixels have α ≥ 128 and weight `α/255 × (ring ? 0.5 : 1)`.
   - Ring applies to logos only: a body pixel on the image border, or with a 4-neighbour of α < 128.
   - Halo pixels (logos only, 24 ≤ α < 128) are collected separately with weight α/255.
3. **Sparse bins:**
   - Bin key = `l·1024 + a·32 + b`: L in 20 steps of 0.05; a and b in 32 steps of 0.025 starting at −0.4.
   - Per pixel, store the key and the weights (w, w·L, w·a, w·b, ring w) in arrays sized to the pixel count.
   - Sort by key, then sum runs, which gives the occupied bins in key order.
   - No array is longer than the pixel count (at most 9216 for 96×96), so nothing lands on the large-object heap.
   - Total weight 0 → `null`.
4. **Deterministic clustering** over the occupied bins in key order:
   - The first seed is the heaviest bin. Each further seed is the argmax of `sqrt(w/total)·d²` over bins with w ≥ 0.2 % of the
     total, skipping d < 0.05.
   - K = 6 for logos, 8 for cover and hero.
   - Weighted Lloyd, at most 12 iterations; ties go to the lower index.
   - Clusters closer than ΔE 0.045 merge; the heavier one absorbs the other.
5. **Frame** (logos with transparency): a cluster is a frame if (C < 0.05 or L < 0.30), its edge share is ≥ 0.5, and its
   share is < 0.5. Frames are excluded from everything. `share_c = w_c / (total − frame weight)`.
6. **Hue families:**
   - Take clusters with C ≥ 0.035 that are not frames, heaviest first.
   - Each joins the first family with dHue ≤ 20° and Cmin/Cmax ≥ 0.6, or starts a new one.
   - The family mean is weighted by w·C.
   - Skin flag: 40° ≤ h ≤ 80°, 0.04 ≤ C ≤ 0.125, 0.40 ≤ L ≤ 0.92.
7. **Score.** m = 0.02 for logos, 0.03 otherwise. Families qualify with C ≥ 0.035, 0.22 ≤ L ≤ 0.97 and share ≥ 0.25·m. On
   cover and hero, skin families are never the accent.
   ```
   score = sqrt(share) · Smooth(0.5m, 1.5m, share) · min(C, 0.22) · Smooth(0.22, 0.40, L) · (1 − 0.5·Smooth(0.90, 0.97, L))
         · (logo ? 1 : 0.5 + 0.5·clamp(ΔE(family, heaviest cluster) / 0.12, 0, 1))
   ```
   Best = argmax. Ties go to the heavier family, then to the lower id.
8. **Fill over outline** (logos with transparency):
   - Condition: the best family owns the edge (edge share ≥ 0.5), and another qualifying family has a share ≥ 0.35 × the
     best's and an edge share ≤ 0.2.
   - Then the interior family becomes the best, and the outline stays a secondary family.
   - Yellow fill with a thick red outline gives yellow.
   - White letters with an orange outline give orange, because white fails C ≥ 0.035.
9. **Halo** (logos with transparency):
   - Condition: chromatic body weight < 10 % of the body weight, halo weight ≥ 35 % of the body weight, and ≥ 50 % of the
     halo weight with C ≥ 0.035.
   - Then steps 3–8 run on the halo pixels without ring and frame logic, and that result is the logo's result.
   - White lettering with a cyan glow gives cyan.
10. **Refine:** the accent is the w·C-weighted mean of the bins of the best family's member clusters with dHue ≤ 20° and
    C ≥ 0.6·C_best.
11. **Confidence:** `conf = Smooth(0.03, 0.07, C_accent) · Smooth(0.5m, 2m, share_best)`.
12. **`ChromaShare`** = the weight of qualifying families divided by the non-frame body weight. In the halo case it is the
    chromatic halo weight divided by the body weight.

#### `Derive(SourceResult? logo, SourceResult? cover, SourceResult? hero) → GameColors`

1. **Accent source** (logo dominance rule):
   ```
   reference = first of (cover, hero) with conf ≥ 0.5
   logoWins  = logo is { conf ≥ 0.5 } && (logo.ChromaShare ≥ 0.10 || reference is null
                                            || HueDistance(logo.Accent, reference.Accent) ≤ 40°)
   source    = logoWins ? logo : reference
   ```
   - No source: `GameColors(null, None, round2(max conf), [], [])`.
   - A small logo emblem that disagrees with a confident cover therefore gives way to the cover. With gray cover and hero, it
     still wins (better than the scheme).
2. **Atmosphere candidates.** Taken from every family that is not the accent family:

   | Origin | Weight | Share condition |
   |---|---|---|
   | logo secondary (only when the logo is the source) | 1.2 | ≥ 15 % of the accent family's share |
   | cover | 1.0 | ≥ 1 % |
   | hero | 0.8 | ≥ 1 % |

   Each also needs C ≥ 0.07, 0.30 ≤ L ≤ 0.92 and dHue to the accent in [12°, 120°].
   `score = weight · sqrt(share) · Smooth(0.015, 0.045, share) · min(C, 0.2) · Sep(dHue) · (skin ? 0.5 : 1)`.
   Keep the top 3 with score > 0.012.
   `Sep(d)`: 0.5 → 1 linear for 12–25°, 1 for 25–90°, 1 → 0.5 linear for 90–120°.
3. **Shine candidates:**
   - Families with C ≥ 0.04, L ≥ 0.62, share ≥ 0.5 % (logo secondaries: ≥ 15 % of the accent share), dHue to the accent
     ≥ 25°, and not the first atmosphere candidate.
   - Same weights as above. `score = weight · sqrt(share) · Smooth(0.01, 0.03, share) · min(C, 0.2) · L · (skin ? 0.5 : 1)`.
   - Keep the top 3.
4. **Raw values only.** Hex = refined family means; `conf` is rounded to 2 decimals. Mapping happens in the App (section 4), so
   changing a mapping rule never invalidates stored data.

### 2.4 Constants (all `internal const` or `internal static` options, so tests can mutate them)

| Constant | Value |
|---|---|
| Logo decode / analysis size | decode ≤ 384 px wide; analysis long side 96 px, short side ≥ 8 |
| Cover analysis size | 32×48 / 40×40 (aspect 0.83–1.2) / 48×32 |
| Hero analysis size | 48 × `PixelAnalysis.AmbientHeight` |
| Alpha thresholds | body ≥ 128; halo 24–127; opaque < 250 |
| Ring weight | 0.5 |
| Bins | 20 × 32 × 32 key space, stored sparse |
| Seed minimum | 0.2 % |
| Seed minimum distance | 0.05 |
| K | logo 6, cover/hero 8 |
| Lloyd iterations | 12 |
| Merge ΔE | 0.045 |
| Frame | C < 0.05 or L < 0.30; edge ≥ 0.5; share < 0.5 |
| Family | dHue ≤ 20°, Cmin/Cmax ≥ 0.6, C ≥ 0.035 |
| m | logo 0.02, cover/hero 0.03 |
| Accept | 0.5 |
| Logo dominance | ChromaShare ≥ 0.10, or agreement ≤ 40° |
| Fill over outline | edge ≥ 0.5; share ≥ 0.35×; other edge ≤ 0.2 |
| Halo | < 10 % chromatic body; ≥ 35 % halo; ≥ 50 % chromatic halo |
| Secondary logo share | 15 % of the accent family |
| Atmosphere | C ≥ 0.07; L 0.30–0.92; share ≥ 1 %; dHue 12–120° |
| Shine | C ≥ 0.04; L ≥ 0.62; share ≥ 0.5 %; dHue ≥ 25° |
| Skin | h 40–80°; C 0.04–0.125; L 0.40–0.92 |
| Version | `GameColorAnalysis.Version = 1` |

---

## 3. Reading the colours: on demand, through the artwork service

`LoadArtwork` and `GameArtwork` are **not** changed. Colours are read only when someone asks.

### 3.1 Contract: new file `App/Services/Artwork/GameColorsContracts.cs`

```csharp
/// Colours of a game's art (E94). Implemented by ArtworkService; other IArtworkService implementations need not offer it.
public interface IGameColorsSource
{
    /// Known colours of the game's current art state, without any work: true only from the in-memory memo. No I/O, no locks.
    bool TryPeek(GameConfig game, out GameColors colors);

    /// Colours of the current art state: from the memo, the index, or computed once on the artwork worker. Never throws
    /// except for cancellation; on an unexpected error it returns GameColors.None, which is neither memoised nor stored.
    Task<GameColors> GetGameColorsAsync(GameConfig game, CancellationToken cancellationToken = default);
}
```

Callers reach it with `context.Artwork as IGameColorsSource`. **`IArtworkService` is not changed**; it belongs to the
SteamGridDB track.

### 3.2 Implementation: new partial file `App/Services/Artwork/ArtworkService.GameColors.cs`

`public sealed partial class ArtworkService : IGameColorsSource` (a new partial file). It has access to the private
`Resolve`, `GameState`, `ArtSpec`, `Generator`, `Enqueue`, `Entry`, `WithEntry` and `MarkDirty`.

- **Memo:** `ConcurrentDictionary<string, (string Signature, GameColors Colors)> _gameColors`. One entry per game key, about
  200 bytes each.
- **`TryPeek`:** true only if `_states` has the key with the game's current `ConfigStamp` and the memo's signature equals that
  state's signature. Lock-free.
- **`GetGameColorsAsync`:** if `TryPeek` succeeds, return a completed task. Otherwise
  `Enqueue(key + "|" + ConfigStamp(snapshot) + "|colors", work, cancellationToken)` at normal priority (the user is looking
  at this game).
- **The work:**
  ```
  try
      state  = Resolve(game, full: false)                       // normally cached in _states
      stored = Entry(key, e => e.Signature == state.Signature ? e.Colors : null)
      if stored is { V: GameColorAnalysis.Version } && stored.ToColors() is { } known → memo, return known
      (logo, cover, hero, transient) = Inputs(state)            // 3.3
      result = GameColorAnalysis.Derive(Analyze(logo), Analyze(cover), Analyze(hero))
      Interlocked.Increment(ref _gameColorsComputed)            // counter for tests
      if !transient:
          _gameColors[key] = (state.Signature, result)
          written = WithEntry(key, e => { if (e.Signature != state.Signature) return false; e.Colors = IndexedGameColors.From(result); return true; })
          if written → MarkDirty()                              // race-safe: an art change during the computation is never overwritten
      log Debug "Spielfarben: Akzent aus {Source}, Sicherheit {Conf:0.00}" (no paths, no names)
      return result
  catch (Exception ex) when (ex is not OperationCanceledException)
      log Debug; return GameColors.None                         // not memoised, not stored; the artwork itself is unaffected
  ```
  This uses its own try/catch rather than `Guarded`, which logs at Error and whose fallback would be wrong here.

**Who calls it:**
- `MainWindow`, only while the switch is on and high contrast is off
- `AppearancePageViewModel`, for the preview game while the page exists

Nobody else does: not `LoadArtwork`, not grid tiles, not `ArtworkContactSheet`, not the `--snapshot` scenes, not the picker's
hypothetical preview.

### 3.3 Inputs (read-only; never writes trim files or generated images)

Every source must have `Generate == Generator.None`, a `File`, and `Origin` ∈ {Launcher, Alternative, Custom, SteamGridDb}.
`CoverFromSquare` is the one exception (below).

- **Logo:**
  - If the index already has a trimmed logo file for this game and it exists, decode it at width 96.
  - Otherwise decode the original at width ≤ 384, take `PixelAnalysis.OpaqueBounds`, and crop that rectangle **in memory** with
    `PixelAnalysis.Resample(...)` to a long side of 96 (short side ≥ 8; premultiplied, so no dark fringes).
  - It never calls `TrimmedLogo`, which writes the trim file and could race with `LoadArtwork` on the other worker.
- **Cover:**
  - `ArtworkImaging.Decode(file, 96)`, then `ToBgra`, then `Resample` to 32×48, 40×40 or 48×32 by aspect.
  - For `Generate == CoverFromSquare`: decode the **square source file** (`state.Cover.File`) at 96 and resample to 40×40. The
    generated composite (colour field, stripes, name) is never read.
- **Hero:** measure, then `ArtworkImaging.DecodeStretched(file, 48, PixelAnalysis.AmbientHeight(w, h))`, the same input
  `ComputeAmbient` uses.
- **Failures:** a decode failure (`ArtworkImaging.IsDecodeFailure`) makes that source absent and sets `transient = true`.
  The result is then returned but neither memoised nor stored, so a file locked mid-write is not remembered.

Cost per game, once, only for switch-on users: three small decodes plus at most one ≤ 384 px logo decode (≤ 0.6 MB transient).
Analysis is estimated at ≤ 1 ms per source.

### 3.4 Index and versioning

- **`ArtworkIndexEntry`** (`Core/Artwork/ArtworkIndex.cs:145-193`) gets `public IndexedGameColors? Colors { get; set; }`.
  `ResetDerived` gets `Colors = null;`. These two additive lines are the only change in that file.
- **`ArtworkIndex.CurrentVersion` stays 1.** Changing it would throw away every user's index.
- **Old index files** have no `colors`; it is computed on the next request.
- **Older HDRpilot** ignores the unknown property. `WhenWritingNull` keeps entries without colours unchanged.
- **Algorithm change:** bump `GameColorAnalysis.Version`. Entries with another `v` are recomputed lazily.
- **Art change** (watcher, picker, SteamGridDB file): the signature changes, `ResetDerived` clears `Colors`, and the next
  request recomputes. No hook is needed in the picker or SteamGridDB code.
- **Removed games** are pruned as today.

---

## 4. App: mapping, engine overload, contrast

### 4.1 `PaletteEngine` overload (`App/Theming/PaletteEngine.cs`)

```csharp
public static ThemePalette Compute(PaletteColors palette, bool dark, bool autoTextOnAccent,
                                   Color surfaceAtmosphere, IReadOnlyList<Color>? extraSurfaces = null)
```

- Surfaces = `[panel, background, top, bottom]` derived from `surfaceAtmosphere` exactly as today, plus `extraSurfaces`.
- The existing `Compute(palette, dark, auto)` calls it with `palette.Atmosphere` and no extras, so its output is
  byte-identical: token files, `ThemeTokenFileTests` and the 64 preset combinations stay green.

### 4.2 `GamePalette` (new file `App/Theming/GamePalette.cs`, pure)

```csharp
public static PaletteColors? Map(GameColors? raw, PaletteMode mode, Color? glowHint = null); // null → scheme
public static ThemePalette Compute(PaletteColors accents, AppearanceState s, bool dark);     // game palettes only
public static ThemePalette Scheme(AppearanceState s, bool dark);                            // = PaletteEngine.Compute(s.Palette, dark, s.AutoTextOnAccent), exactly the app level
public static PaletteColors Calm(PaletteColors scheme);                                     // OKLCH chroma ≤ 0.03, same L and h
public static IReadOnlyList<Color> LibraryBackgroundBounds(bool dark);                      // computed once (4.6)
```

`dark` is always passed in by the caller: the window's `_dark` from `ReadTheme`, not `s.IsDark`. In the test host the light
theme comes from the token files while `StaticAppearanceService` stays dark.

**`Map`:**
1. If `raw is null` or `!raw.IsUsable`, return `null`.
2. `acc = Fit(raw.Accent, Accent)`.
3. **Atmosphere.**
   - **Glow hint:** if `glowHint` is set, stably reorder the candidates. Those whose fitted hue is within 45° of the hint come
     first, closest first.
   - Take the first candidate where `atm = Fit(c, Atmosphere)` has `d = HueDistance(acc, atm)` in [12°, 120°] and
     `Mud(acc, atm) ≥ 0.70`.
   - If d < 30°, rotate the hue away from the accent to exactly 32° on its own side, fit again and re-check the mud. If it
     fails, go to the next candidate.
   - **Derived atmosphere**, when no candidate passes:
     - hue = `acc.h + (Warm(acc.h) ? −40° : +40°)`, with `Warm(h)` = h ∈ [330°, 360°) ∪ [0°, 110°)
     - C = max(acc.C, 0.12), L = acc.L − 0.08
     - fit it; while `Mud < 0.70`, step the hue 10° back toward the accent, but not below 30°
4. **Shine.**
   - **Duotone:** `PalettePresets.DerivedShine(atm)`, as for the presets.
   - **Tritone:** the first candidate in `raw.Shines` that, after `Fit(Shine)`, meets all of:
     - dHue to the accent in [25°, 150°]
     - dHue to the atmosphere in [25°, 90°]
     - `Mud(atm, shine) ≥ 0.70`
     - `Mud(acc, shine) ≥ 0.70`, because `BrushAccentShine` runs accent → shine
   - Otherwise the arc: hue = `atm.h + 50° · sign(atm.h − acc.h)` (signed shortest arc), C 0.20, L 0.80, fit, same checks.
   - Otherwise `DerivedShine(atm)`, which has the same (unchecked) behaviour as the presets.
5. Return `new PaletteColors(acc, atm, shine)`, opaque.

**Bands** (theme-independent; hue in OKLCH degrees):

| Role | Ymin | Ymax | Chroma | Lift boost |
|---|---|---|---|---|
| Accent | h < 45: 0.28 · 45–55: 0.32 · 55–95 (gold): 0.45 · 95–125 (lemon): 0.60 · **125–230: 0.30** · 230–300: 0.28 · ≥ 300: 0.30 | 0.75 | **0.12**–0.24 | 0.15 |
| Atmosphere | 55–125: 0.30, otherwise 0.21 | 0.50 | **0.12**–0.24 | 0.15 |
| Shine (Tritone) | 55–95: 0.45 · 95–125: 0.60 · otherwise 0.24 | 0.70 | 0.08–0.24 | — |

Reasons:
- Every fill has Y ≥ 0.21, so Ink (`#0B0C12`) reaches ≥ 4.83:1 on it while white reaches ≤ 4.04:1. With automatic text,
  on-accent text is always Ink and `FillFor` leaves the fills unchanged.
- Accent Y ≥ 0.28 gives ≥ 5.7:1 as text on the dark panel without the fixer.
- Ymax 0.75 stops neon from becoming white-ish buttons.
- The hue floors stop gold and lemon from turning olive or brown.
- The 125–230° floor was lowered from 0.40 to 0.30, so forest and teal brands stay deep: forest gives `#35AA60` at 6.6:1 with
  Ink.
- The bands are not those of the presets: neongruen (Y 0.84), polarlicht (0.76) and royal (0.20) lie outside. The bands are
  their own rule. Presets are never fitted.

**Cost:** `Map` runs about 10 `Fit` calls of about 1.6k evaluations each. The window caches `Map` results (5.5). The cache is
keyed by value, using the equality of `GameColors`, the mode and the hint.

### 4.3 `Compute(accents, s, dark)` and the amber rule

```csharp
var p = PaletteEngine.Compute(accents, dark, s.AutoTextOnAccent,
                              surfaceAtmosphere: s.Palette.Atmosphere, extraSurfaces: LibraryBackgroundBounds(dark));
return dark ? p : Amber(p, accents, surfaces);
```

**Amber rule** (light theme, game palettes only; preset output is unchanged):
- **Which roles:** AccentText (from C1, target 4.6), Control1 (C1, 3.0), Control2 (C2, 3.0). FocusRing and SelectionRing equal
  Control1.
- **When:** the source hue lies in [80°, 115°) and the engine had to darken the colour.
- **What:** the value is recomputed by bisecting OKLCH L at **hue 75°**, with the source chroma gamut-clipped, to the same
  target against the same surface list. If the bisection cannot reach the target, the engine value stays.
- **Why the hue changes:** darkening in sRGB toward black keeps the OKLCH hue. A darkened gold is therefore olive by its hue,
  and a constant-hue OKLCH darkening gives the same result:

  | Input | Engine (sRGB toward black) | OKLCH, constant hue | Amber rule |
  |---|---|---|---|
  | `#E8B500` | `#8B6C00` | `#927000` | `#9F6A00` |

  The app's own light-theme Warning colour `#94600A` uses the same hue.

### 4.4 Scheme and calm scheme

- **Gray art, the switch-off path, and installing the layer** all use `GamePalette.Scheme(s, dark)`. That is exactly the
  app-level palette, without bounds, so installing the layer changes nothing visible and "gray = scheme" holds exactly for
  every scheme. (With the bounds, five schemes differed by up to ΔE 0.02.)
- **`Calm(scheme)`** caps each colour's OKLCH chroma at 0.03 and keeps L and h. It is only ever the `scheme` argument of
  `AmbientPalette.Resolve` in the "Farben an, Hintergrund aus" case (5.5).

### 4.5 Guarantees and their tests

The property test (9.2) must show the following for every mapped palette, in both themes, with automatic text on and off,
against the 8 scheme surface sets plus the bounds:

| Property | Requirement |
|---|---|
| `OnAccentContrast` | ≥ 4.5 |
| `AccentText` against every surface | ≥ 4.5 (engine target 4.6) |
| `Control1`, `Control2`, `FocusRing`, `SelectionRing` against every surface | ≥ 3.0 |
| With automatic text | `OnAccent == Ink`; `AccentFill1/2/3 == C1/C2/C3` |
| Bands | Y within the band (8-bit colour); C ≥ min(Cmin, highest in-gamut C inside the band at that hue) − 0.003; hue drift ≤ 2° |
| Atmosphere | hue distance to the accent in [30°, 120°]; `Mud(acc, atm) ≥ 0.70` |
| Tritone, shine not derived | `Mud(atm, shine) ≥ 0.70`, `Mud(acc, shine) ≥ 0.70`, dHue(acc, shine) ≤ 150° |
| Light theme, accent hue 80–115° | AccentText and Control1 have an OKLCH hue in [70°, 80°] |

**Known limits** (the same as for presets today):
- The raw-colour keys are decorative and are not contrast-checked: `AccentSoft` 22 %, `AccentStrong` 62 %, `SelectionFill`
  14 %, the glow stops.
- The white thumb of the library switches is below 3:1 on light accent tracks, and the track (`Hp.Brush.Accent`) is below 3:1
  against white in the light theme. The state is also shown by the thumb's position. See the follow-up in section 16.

### 4.6 `LibraryBackgroundBounds(dark)`

Returns the brightest (dark theme) or darkest (light theme) possible base tone (`AmbientPalette.Resolve(...).Background`).
It is computed once from every input form `Resolve` can receive:

- **Art:** a1 = HSL(36 hues, S ∈ {0.2, 0.5, 0.75}, L 0.15) and a2 = HSL(36 hues, S ∈ {0.45, 0.7, 0.95}, L ∈ {0.46, 0.53, 0.60}),
  the clamps of `PixelAnalysis` (lines 159–160).
- **Name:** a1 = HSL(36 hues, 0.55, 0.16) and a2 = HSL(36 hues, 0.78, 0.55), as `AmbientColors.FromName` makes them. Note the
  name a1 has L 0.16, above the art's 0.15.
- **Gray mixes:** each art and name a1/a2 mixed 50 % with the 8 presets' colours and with band atmospheres at Y 0.21 and 0.50
  (36 hues). Game accents are band colours.
- **Scheme backgrounds:**
  - the 8 presets
  - 36 fully saturated custom atmospheres, plus white, black and 50 % gray (custom schemes can be any colour)
  - the calm versions of all of these

A test checks that `FromName` over 360 hues and the documented `PixelAnalysis` clamp constants stay inside the sweep. If those
formulas ever change, the test fails.

---

## 5. Applying in the library window

### 5.1 `GameAccentLayer` (new file `App/Theming/GameAccentLayer.cs`)

It is a `ResourceDictionary` that `MainWindow` inserts at `Resources.MergedDictionaries[0]` (empty today). It is installed only
while the switch is on and high contrast is off. Window-level resources win over the app-level generated dictionary in the
library window only.

| Group | Keys |
|---|---|
| Brushes: persistent, unfrozen; 14 brushes, 19 animated colours | `BrushTextOnAccent`, `BrushAccent`, `BrushAccentShine`, `BrushAccentSolid`, `BrushAccentText`, `BrushAccentSoft`, `BrushAccentStrong`, `BrushAtmosphereSoft`, `BrushShine`, `BrushSelectionFill`, `BrushSelectionRing`, `BrushFocusRing`, `BrushHdrOn`, `BrushToggleOn` |
| Color part: 11 values in one child dictionary (`Dictionary.MergedDictionaries[0]`) | `ColorC1`, `ColorC2`, `ColorC3`, `ColorOnAccent`, `ColorAccentText`, `ColorAccent1–3`, `ColorGlow`, `ColorAmbient2`, `ColorAmbient3` |
| Excluded on purpose (background and chrome follow the scheme) | `ColorBackground`, `ColorAmbient1`, `ColorShadow`, `BrushWindowBackground`, `BrushBackgroundGlow`, `BrushBackgroundGlow2`, and every surface, text, state, source and font key |

**Values come from the engine's own builder.** In `ThemeResources.Build`, the lines for these 25 keys move unchanged into
`internal static void AddAccentEntries(ResourceDictionary d, ThemePalette p)`, and `Build` calls it. Token files stay unchanged.
`ThemeResources.ColorTo` and `BuildTransition` are not touched.

**API:**
```csharp
internal sealed class GameAccentLayer : IDisposable
{
    public static IReadOnlyList<object> BrushKeys { get; }    // 14
    public static IReadOnlyList<object> ColorKeys { get; }    // 11
    public static IReadOnlyList<object> ExcludedKeys { get; } // documented exclusions (completeness test)
    public GameAccentLayer(Dispatcher dispatcher, ThemePalette initial); // brushes = CloneCurrentValue of AddAccentEntries(initial)
    public ResourceDictionary Dictionary { get; }
    public ThemePalette Target { get; }
    public void TransitionTo(ThemePalette target, TimeSpan duration, Action? onSettled = null);
    public void Settle();
    public GameAccentStats Stats { get; }                     // Applied, SkippedEqual, ColorPartSwaps, Retargets, Replacements
    internal Action<TimeSpan, Action>? TimerHookForTest { get; set; }
    public void Dispose();
}
```

**`TransitionTo(target, d, onSettled)`:**
1. If `target == Target` (record equality) and nothing is pending: count `SkippedEqual`, call `onSettled` at once, return.
2. **Color part, at t = 0:** build the 11 values with `AddAccentEntries` into a temporary dictionary. If they differ, replace
   `Dictionary.MergedDictionaries[0]` with a new child dictionary. This is one tree invalidation, counted in `ColorPartSwaps`.
   - The swap happens **with the start of the fade**, the same way the app-level `BuildTransition` sets Color values
     immediately.
   - The new tile's halo, the Starten and HDR glows and the switch halo change together with the user's action, never in the
     middle of a smooth fade.
   - No swap is ever deferred, so none can be skipped.
3. **Brushes:** per solid brush and per gradient stop, retarget with the tracked target (5.3):
   - target unchanged → no-op
   - otherwise `BeginAnimation(prop, OklchFade.Create(current, target, d), HandoffBehavior.SnapshotAndReplace)`, where `current`
     is the value on screen (`brush.Color` and `stop.Color` return the animated value)
   - `d == 0` → set the base value, then `BeginAnimation(prop, null)`
4. **Settle:**
   - `d > 0`: one one-shot `DispatcherTimer(DispatcherPriority.Background)` at `d + 80 ms`, restarted by a newer transition.
     On settle: set each base value to its target, remove the animations, keep the brushes unfrozen, then call all `onSettled`
     callbacks registered so far.
   - `d == 0`: settles synchronously.
   - Nothing runs while idle.
5. **Never replaced:** brush instances live as long as the layer. Every accent brush has the same structure in dark and light.

### 5.2 OKLCH fades: new file `App/Theming/OklchFade.cs`

```csharp
internal static class OklchFade
{
    public static ColorAnimationUsingKeyFrames Create(Color from, Color to, TimeSpan duration);
}
```

- **Why:** WPF's `Color` arithmetic, and therefore `ColorAnimation`, interpolates in scRGB (linear light). A direct fade
  between distant hues passes through gray. Generated examples, chroma at the midpoint relative to the weaker end:
  `#FF5050` → `#2EC4B6` 40 %, `#FF71CE` → `#3DDC84` 9 %.
- **Keyframes:** four segments with keyframe colours at 25 / 50 / 75 / 100 % of the path, from `ColorScience.Lerp` (OKLCH,
  shortest arc).
- **Key times:** 0.397·d, 0.5·d, 0.603·d and d. This is where `CubicEase` InOut reaches those fractions.
  - First segment: `EasingColorKeyFrame` with CubicEase EaseIn (exact on that segment).
  - Two middle segments: `LinearColorKeyFrame`.
  - Last segment: `EasingColorKeyFrame` with CubicEase EaseOut (exact).
- **Measured on the generated pairs:** the worst chroma between keyframes stays ≥ 79 % of the weaker end, instead of 9–40 %.
- **No `From`:** the animation starts from the base value on screen and the first keyframe is computed from it, so an
  interrupted fade continues smoothly.
- `FillBehavior.HoldEnd`; `Timeline.SetDesiredFrameRate(…, EffectPolicy.OneShotFrameRate)`.
- If `from == to`, there is no animation.

### 5.3 Brushes built in code: `LiveBrush` / `LiveBrushSet` (new file `App/Theming/LiveBrush.cs`)

```csharp
internal static class LiveBrush
{
    public static T Unfrozen<T>(T template) where T : Brush;            // CloneCurrentValue, never frozen
    public static bool Compatible(Brush live, Brush target);            // false if live is frozen, or type, stop count or offsets differ
}
internal sealed class LiveBrushSet
{
    // Per solid brush or stop: no-op if the target equals the last tracked target (a running fade is left alone);
    // otherwise an OklchFade with SnapshotAndReplace, or a direct set when d == 0. Geometry and opacity are set directly.
    // An incompatible live brush is replaced (counted).
    public Brush Track(Brush? live, Brush target, TimeSpan d);
    public void Settle();
    public GameAccentStats Stats { get; }
}
```

Per-stop tracking is what keeps `ApplyAmbient`, which runs in the same dispatcher turn as the accent path, from cancelling an
accent fade. It retargets the same stops to the same targets, so it does nothing.

| Brush (owner) | Colours | Duration on the accent path | From `ApplyAmbient` / `ApplyWindowBrushes` |
|---|---|---|---|
| `Lib.Brush.RowSelection` (window resource, live instance) | switch on: dark = `_accents.Atmosphere` gradient 30/8/0 %; light = `_accents.Accent` 14 %. Switch off: as 1.5.0 (dark = ambient A2 gradient, light = scheme accent 14 %) | accent duration | switch on: no-op (same targets). Switch off: instant. |
| `EdgeGlow.BorderBrush` | stops 0, 1, 3 = A3, A2, A3; stop 2 = `_accents.Accent` | stop 2: accent duration | stops 0, 1, 3: instant (as 1.5.0); stop 2: no-op |
| `SeamCore.Fill`, `SeamGlow.Fill` | from `_accents` (C1 = Accent, C2 = Atmosphere) | accent duration | no-op |
| `StreakCore.Background`, `StreakGlow.Background` | from `_accents` (C1–C3). A direction change sets points and centre directly. | accent duration | no-op |
| `ToolbarToggle` capsule halo | frozen brush rebuilt from C2 (α 0x8C at 0), C1 (α 0x38 at 0.55), C1 (α 0 at 0.75) via new `ToolbarToggle.SetAccentColors(c1, c2)` | instant (only visible during the short capsule glow) | — |

**An instance is replaced only when the structure changes:**
- dark ↔ light: `RowSelection` switches between solid and gradient; the streak offsets differ
- entering or leaving high contrast: the frozen system brushes are incompatible

### 5.4 Debounce: `GameAccentScheduler` (new file `App/Theming/GameAccentScheduler.cs`, pure, no timers)

```csharp
internal sealed class GameAccentScheduler
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);
    public Decision Selected(TimeSpan now, bool repeat); // ApplyNow | Hold(dueAt)
    public Decision ColorsArrived();                     // ApplyNow | None (while holding)
    public Decision Elapsed(TimeSpan now);               // ApplyNow | Hold(dueAt) on an early tick | None
    public bool IsHolding { get; }
}
```

**Rules:**
- `Selected`: `fast = repeat || (last != null && now − last < Quiet)`, then `last = now`.
  - Not fast: stop holding and return ApplyNow. The window cancels its timer.
  - Fast: start holding, `dueAt = now + Quiet`, return Hold. The window (re)starts its one-shot timer.
- `ColorsArrived`: None while holding, otherwise ApplyNow.
- `Elapsed`: if holding and `now ≥ dueAt`, stop holding and return ApplyNow. If holding and early, return Hold(dueAt).
  Otherwise None.

**The `repeat` flag:** `MainWindow` notes `e.IsRepeat` in a `PreviewKeyDown` handler registered in the new partial. `repeat`
is true when the last key event was a repeat less than 150 ms ago.

**Held arrow key (Windows defaults: delay about 500 ms, repeat about 33 ms):**

| Time | Event | Effect |
|---|---|---|
| 0 ms | change | ApplyNow (one fade) |
| 500 ms | first repeat | Hold |
| 533, 566, … ms | repeats | Hold, timer restarted |
| release + 300 ms | timer | one fade to the final game |

That is one fade plus the final one. Typing in the search, where the selection follows the filter, is debounced by time in
the same way.

### 5.5 Window wiring: new partial `App/MainWindow.GameColors.cs`, plus small hooks

**Fields:**
- `PaletteColors _accents`: the accents shown; starts as the scheme
- `bool _showingGame`
- `GameAccentLayer? _accentLayer` and `int _accentLayerGeneration`
- `GameAccentScheduler _accentScheduler` and the one-shot `DispatcherTimer? _accentTimer`
- `internal Func<TimeSpan> AccentClock`: a Stopwatch; tests inject their own
- `LiveBrushSet _liveBrushes`
- `IGameColorsSource? _colorsSource`
- `string? _colorsKey` and `GameColors? _colors`: the known colours for that key
- `CancellationTokenSource? _colorsRequest`
- `TimeSpan? _lastKeyRepeat`
- the appearance snapshot seen last
- a `Map` cache of at most 64 entries, cleared when full
- `internal GameAccentStats AccentStats`, with `Held` and `Requests` counted here

`FollowColors => Appearance.Current.ColorsFollowGame && !_highContrast && _colorsSource is not null`.

```
InitializeGameColors(vm)            // in Attach, after _motion exists, before UpdateHero(instant: true)
    _colorsSource = vm.Context.Artwork as IGameColorsSource
    _accents = vm.Context.Appearance.Current.Palette
    subscribe vm.Context.Appearance.Changed (missing in 1.5.0) and PreviewKeyDown (repeat flag)

OnGameAccentSelection(instant)      // UpdateHero, before ApplyAmbient
    if !FollowColors: ApplySchemeAccents(instant); return
    if _hero is null: return                                     // keep
    RequestColors(_hero)
    decision = instant ? ApplyNow : _accentScheduler.Selected(AccentClock(), repeat)
    Hold → (re)start timer, Held++, return;  ApplyNow → stop timer, ApplySelectedAccents(instant)

RequestColors(hero)
    if _colorsKey == hero.Key && _colors is not null: return
    cancel _colorsRequest; _colorsKey = hero.Key; _colors = null
    if _colorsSource.TryPeek(hero.Item.Config, out c): _colors = c; return
    Requests++; start GetGameColorsAsync(hero.Item.Config, token)
    on completion (dispatcher): if not cancelled and _colorsKey == key → _colors = result; OnAccentInputsArrived()

OnAccentInputsArrived()             // colours arrived, or the Artwork branch of OnHeroPropertyChanged
    if FollowColors && _accentScheduler.ColorsArrived() == ApplyNow: ApplySelectedAccents(instant: false)

OnAccentTimer()                     // Elapsed → ApplyNow: ApplySelectedAccents(false); early: restart

ApplySelectedAccents(instant)
    if _hero is null || _colorsKey != _hero.Key || _colors is null || _hero.Artwork is null: return   // keep; arrival re-enters
    UpdateGameAccents(MapCached(_colors, s.Mode, GlowHint()), instant)                              // null → scheme

ApplySchemeAccents(instant) => UpdateGameAccents(null, instant)
RefreshShownAccents(instant) => FollowColors ? (_showingGame ? UpdateGameAccents(_accents, instant) : ApplySelectedAccents(instant))
                                             : ApplySchemeAccents(instant)

UpdateGameAccents(PaletteColors? game, instant)
    s = Appearance.Current; follow = FollowColors
    d = instant ? 0 : _motion?.Transition(AppearanceService.RecolorDuration) ?? 0
    useGame = follow && game is not null
    accents = useGame ? game.Value : s.Palette
    target  = useGame ? GamePalette.Compute(accents, s, _dark) : GamePalette.Scheme(s, _dark)
    if follow:
        if _accentLayer is null: create with GamePalette.Scheme(s, _dark) (invisible install), insert at [0], _accentLayerGeneration++
        _accentLayer.TransitionTo(target, d)
    else if _accentLayer is { } layer:
        gen = ++_accentLayerGeneration
        layer.TransitionTo(target, d, () => { if gen == _accentLayerGeneration && !FollowColors: remove, dispose, _accentLayer = null })
    _showingGame = useGame
    if accents == _accents: return                               // the layer already handled surface or theme changes
    _accents = accents
    Hero.SetPalette(_palette, _accents, _dark)                   // the sweep only shows during the HDR moment; built instantly as today
    ApplyAccentBrushes(d)                                        // 5.3 table, including ApplyToolbarBrushes(d)
    if follow && s.BackgroundFollowsGame && GrayArt(): ApplyAmbient(instant: d == TimeSpan.Zero)   // the gray mix reads _accents
    Applied++; Debug log "Spielfarben: Akzent aus {Source}, Sicherheit {Conf:0.00}, Übergang {ms} ms"
```

`GlowHint()` returns `LibraryArt.Parse(art.Ambient.Glow)` only when all of these hold:
- `s.BackgroundFollowsGame`
- `_hero.Artwork.Hero.Origin` is not Generated or None
- `Ambient.Dullness != Gray`

Otherwise it returns null. `GrayArt()` = `_hero?.Artwork?.Ambient.Dullness == Gray`.

**Hooks in `MainWindow.xaml.cs`** (small hunks; this file also changes on ui/hero, ui/details and ui/scroll, see 13):

| Location | Change |
|---|---|
| `Attach` (around line 218) | `InitializeGameColors(viewModel)` before `UpdateHero(instant: true)`. |
| `UpdateHero` (1409–1431), before `ApplyAmbient` | `OnGameAccentSelection(instant)`. |
| `OnHeroPropertyChanged`, `Artwork` branch (1433–1444) | After `ApplyAmbient`: `_colorsKey = null` (the art may have changed, so peek again), `RequestColors(_hero)`, `OnAccentInputsArrived()`. |
| `ApplyAmbient` (1457–1475) | `schemeArg = !FollowColors ? _scheme : (follow ? _accents : GamePalette.Calm(_scheme))`. Then `_palette = AmbientPalette.Resolve(follow ? ambient : null, schemeArg, follow, _dark)` and `Hero.SetPalette(_palette, _accents, _dark)`. |
| `ApplyWindowBrushes` (1478–1552) | `RowSelection` and `EdgeGlow` through `_liveBrushes` (5.3). `_scheme.Accent` → `_accents.Accent`. |
| Particles: 298, 1759; burst 1781 | `_palette.ParticleColors(_accents, follow)`; the burst uses `_accents.Accent` and `_accents.Shine`. |
| `FindResource` snapshots: `EdgeLine` off-state 1564, `CapsuleDot.Fill` 1594, `BrandGlyph.Stroke` 1600, `NitsScale.Fill` 1605, `StatusDot.Fill` 1610–1615, `EffectsGlyph.Foreground` 1702 | `SetResourceReference(property, key)` for the themed branch. Frozen literal brushes stay. They then follow the layer, its install and removal, and scheme changes, which also fixes the stale-after-scheme-change behaviour of 1.5.0. |
| `OnThemeChanged` (1898–1903) | After `ReadTheme` and `ApplyAmbient(instant: true)`: `RefreshShownAccents(instant: true)`. |
| `OnClosed` | Stop the timer, cancel the request, settle and dispose the layer and `_liveBrushes`, unsubscribe. |

**`OnAppearanceChanged`** (new subscription):
- Compare a snapshot of (Palette, Mode, AutoTextOnAccent, BackgroundFollowsGame, ColorsFollowGame). `IsDark` is **not** part
  of it. `AppearanceService` subscribes to `ThemeManager.ThemeChanged` before the window and raises `Changed` synchronously,
  so `_dark` is still stale at that moment. Dark and light are handled in `OnThemeChanged` after `ReadTheme`.
- If the snapshot changed:
  - `ApplyAmbient(instant: false)`. This also makes "Hintergrund folgt" and scheme changes apply live in an open window (a
    1.5.0 gap).
  - If `FollowColors`: `_colorsKey = null`, `RequestColors(_hero)`, `ApplySelectedAccents(false)`, without debounce.
  - Otherwise: `ApplySchemeAccents(false)`.

**`MainWindow.Toolbar.cs`:**
- `ApplyToolbarBrushes(TimeSpan d = default)` and `ApplyStreakBrushes(TimeSpan d = default)` read `_accents`
  (C1 = Accent, C2 = Atmosphere, C3 = Shine) and go through `_liveBrushes`. The `TryFindResource(ColorC1/C2/C3)` lookups and
  the literal fallback colours go away.
- They call `ToolbarToggle.SetAccentColors(c1, c2)`.
- `SetToolbarHighContrast(on)` also calls `RefreshShownAccents(instant: true)`. The layer is removed at once when HC turns on
  and reinstalled when it turns off.

**Hero crossfade sync:** a selection with cached art and peekable colours starts the accent fade, the hero crossfade and the
backdrop fade in the same dispatcher turn. Otherwise the colours wait for whichever of art or colours arrives last.
`HeroTransition` keys on `_motion.Level`, the accents on `_motion.Transition` (0 while hidden); that difference is intentional.

### 5.6 Calm background ("Farben an, Hintergrund aus")

- Comes from `ApplyAmbient` (5.5): `AmbientBackdrop`, `PaneGlow`, `HeroTitle`, `HeroSheen`, the ambient stops of `EdgeGlow`, and
  the hero's rays, light points, bloom halo and flash all use the calm scheme.
- The hero sweep, the particles and all accents use the game.
- It is constant across games.

### 5.7 Popups and style setters

- **Popups:** the game `ContextMenu` (`TryFindResource("GameContextMenu")`) and the toolbar tooltips must resolve the layer.
  Window test W6 checks this. If one resolves app-level resources instead, add `layer.Dictionary` to that popup's
  `Resources.MergedDictionaries` on `Opened`.
- **Style setters.** Four places put a brush whose colour is a `DynamicResource` inside a `Style` setter:
  - `Lib.Tag` native (`Library.xaml:660-670`)
  - `Lib.HdrBadge` native (579–588)
  - the HDR-native badge border on `Ambient3` (636–641)
  - `Lib.SectionLine` (1696–1707)

  WPF stores unfreezable setter values per instance, so they probably resolve the window layer. Window test W3 asserts all
  four.
- **Pre-authorised fallback**, only if W3 fails and only after the UI tracks have merged:
  - one small `Library.xaml` hunk per failing site, replacing the inline setter brush with a `DynamicResource` to a new
    window-private key (`Lib.Brush.TagNative`, `Lib.Brush.TagNativeLine`, `Lib.Brush.BadgeNativeLine`, `Lib.Brush.SectionLine`)
  - an app-level default with today's values
  - the layer provides the game version of that key
  - no new ThemeKeys

  List the hunk in the collision table when it is used.

### 5.8 What keeps the scheme, and why

| Surface | Reason |
|---|---|
| Settings window, except the live preview (6) | It shows and grades the user's scheme (contrast chips). Recolouring it per selected game would confuse the choice. |
| Tray menu, tray icon, window and taskbar icon | E89: icons follow the scheme. Recognition, and no icon regeneration or taskbar flicker per game. |
| Dialogs (`ConfirmDialog`, `AddGameDialog`, `ArtworkPickerDialog`, remove-settings dialog) | Separate windows on app-level resources. The picker belongs to the SteamGridDB track. |
| `HeroStage` internals | Unchanged; only the palette arguments change. |

---

## 6. Config, settings UI, strings

**Config** (`Core/Config/ShowcaseSettings.cs`, after `BackgroundFollowsGame`):
```csharp
/// <summary>Akzente der Bibliothek übernehmen die Farben von Logo und Cover des gewählten Spiels (E94).</summary>
public bool ColorsFollowGame { get; set; }
```
- JSON `colorsFollowGame`, default false. Old configs load it as false. "Standard" (`ResetToDefaults`) sets it to false.

**State:**
- `AppearanceState` gets a last positional parameter `bool ColorsFollowGame = false`.
- `AppearanceService.Compute` passes `settings.ColorsFollowGame`.
- `AffectsResources` is unchanged: toggling raises `Changed` and never rebuilds app-level resources.

**Settings page "Darstellung"** (`App/Views/Shell/SettingsWindow.xaml`, palette section): a new row directly after the
`BackgroundFollowsGameToggle` row (695–707), with the same structure. The new row becomes the last one and takes over
`Margin="0"`.
- `CheckBox` `Hp.ToggleSwitch` with:
  - AutomationId `ColorsFollowGameToggle`
  - `AutomationProperties.Name` = `ShellStrings.ColorsFollowGameLabel`
  - `AutomationProperties.HelpText` = `ShellStrings.ColorsFollowGameHint`
  - `IsChecked="{Binding ColorsFollowGame, Mode=TwoWay}"`
- A decorative swatch `Border` 46×36, radius 10, with `Background="{Binding ColorsFollowSample}"`.
- Label and caption from the strings below.
- `BackgroundFollowsGameToggle` also gets `AutomationProperties.HelpText="{x:Static res:ShellStrings.BackgroundFollowsGameHint}"`,
  for symmetry.

**`AppearancePageViewModel`** (leave lines 428–548 alone; that is the SteamGridDB part):
- `ColorsFollowGame`, following the `BackgroundFollowsGame` pattern (235–245).
- `_previewColors`: in `LoadPreviewAsync` (about line 1228), after the art, with the same version check, from
  `_context.Artwork as IGameColorsSource`, cancelled by `_cancel`. Requested while the page exists, also when the switch is
  off, so the sample shows what the switch would do. That is one computation per preview game, in the foreground.
- `PreviewAccents` (`PaletteColors?`): `ColorsFollowGame ? GamePalette.Map(_previewColors, State.Mode) : null`.
- `ColorsFollowSample`: a frozen `LinearGradientBrush` from `AccentFill1` to `AccentFill2` of
  `Map(...) is { } g ? GamePalette.Compute(g, State, dark: true) : GamePalette.Scheme(State, dark: true)`. For a gray preview
  game it shows the scheme, which is the truthful preview.
- Notify from `OnAppearanceChanged` (955–967) and wherever the preview game or its colours change.

**Live preview** (`SettingsWindow.xaml.cs`):
- While `PreviewAccents != null` and high contrast is off, a `GameAccentLayer` sits on `Stage.Resources.MergedDictionaries`.
  Every change is instant (d = 0), with no scheduler.
- Dark or light comes from the window's resources, by the same rule as `MainWindow.ReadTheme`.
- Subscribe to `SystemParameters.StaticPropertyChanged` for `HighContrast`, and unsubscribe on `Closed`.
- `StageDot.Fill` (line 298) uses `SetResourceReference` for the themed branch, so it resolves from inside `Stage`.
- The contrast chips keep showing the scheme.

**Strings** (`App/Resources/ShellStrings.resx`, after `BackgroundFollowsGameHint`, exact text, same dash style):
```xml
<data name="ColorsFollowGameLabel" xml:space="preserve">
  <value>Farben folgen dem Spiel</value>
</data>
<data name="ColorsFollowGameHint" xml:space="preserve">
  <value>Akzente der Bibliothek übernehmen die Farben von Logo und Cover des gewählten Spiels – einmal pro Spiel ausgelesen; bei grauen Bildern, in den Einstellungen, im Tray-Menü und in Dialogen gilt das Farbschema</value>
</data>
```
There are no new strings in `LibraryStrings.resx` or `ArtworkStrings.resx`.

---

## 7. Accessibility

- **No meaning by colour alone.** No new state is introduced. Game colours only change the hue of existing states, and those
  keep their other cues:
  - selection: ring, fill, halo, and position in the details
  - switches: thumb position
  - HDR: button text "HDR an/aus" and the nits values
  - filters: count number, label
  - focus: ring thickness

  W9 asserts that the visual trees are identical apart from brush colours.
- **Contrast:**
  - Section 4.5 holds in dark and light.
  - Text on accents never drops below 4.5:1, even with "Textfarbe automatisch" off: white text, and fills darkened by the
    engine.
- **Known limit:** the white thumb of the library switches is below 3:1 on light accent tracks, the same as with light
  presets today. It is documented in E94, with a follow-up in section 16.
- **High contrast:** game colours are off; the toolbar keeps its system colours; the settings preview removes its layer.
- **Motion:**
  - Aus is instant; Reduziert takes 0.3 s; "Windows-Animationseffekte beachten" caps the effective level.
  - The fade is a one-shot and is not registered with `MotionController`, so "Uhren" is unchanged.
- **Screen readers:** both switches have a Name and a HelpText. Colour changes are not announced, because they carry no
  information.

---

## 8. Performance budget and how to measure it (no load tests, no timing asserts)

| Item | Budget | How it is measured |
|---|---|---|
| Colour reading | Only when the switch is on (library window) or the "Darstellung" page shows a preview game. Once per game and art signature, on the artwork worker (BelowNormal). Small decodes (3.3). Never from `LoadArtwork`, grid tiles, the contact sheet or snapshots. Zero for default-off users. | `ArtworkServiceGameColorsTests`: computation counter = 1 per signature; 0 after `LoadImage` and `GetArtworkAsync`. `Analyze` sees ≤ 96×96 px; bin arrays ≤ pixel count; K ≤ 8; ≤ 12 iterations. |
| Index | ≤ 260 bytes per game | serialisation test |
| Per applied change, **accent path only** (`OnGameAccentSelection` / `ApplySelectedAccents` / `UpdateGameAccents`, the layer, the live brushes) | at most 1 Color-part swap (only if the values differ), 0 brush replacements, ≤ 19 layer and ≤ 22 live-brush stop animations, 1 settle timer | `GameAccentStats` counters for scripted sequences |
| Allocation per applied change (accent path only) | ≤ 256 KB; locked at the measured average + 25 %, number written into the G15.7 evidence | `GC.GetAllocatedBytesForCurrentThread()` around the accent-path calls, 10 applied changes after one warm-up, STA |
| Per held selection (accent path only) | 0 layer and live-brush operations, ≤ 1 KB | same counters and probe, 50 scripted fast selections |
| Switch off | 0 layer operations, no layer, 0 colour requests | W1 |
| Idle after a fade | 0 enabled accent timers; no animated properties on the layer and live brushes; "Uhren 0" unchanged | after `Settle()` |

**Not part of this budget:** `ApplyAmbient` keeps its 1.5.0 writes of window resources per selection (8 in dark, up to 17 in
light, each a tree walk). Moving them to live brushes is a follow-up (16). The old idea of checking for no buffer ≥ 85 KB is
dropped, because `GetAllocatedBytes` cannot see it. Sparse bins avoid such buffers by design.

---

## 9. Tests

All images are generated in code. Never use real game art or launcher caches. Each property test is written as a few `[Fact]`s
with one combined assertion, not as Theory rows. The MinTests values in section 12 follow from the counts below.

### 9.1 Core: `tests/HdrPilot.Tests/Artwork/` (namespace `HdrPilot.Tests.Artwork`)

**`GeneratedArt.cs`** (helper, deterministic LCG seed):
- **Logo canvas:** 384×96, transparent, six letters 44×64 at x = 20 + 58·i, y = 16, each with a 20×32 transparent counter.
- **Logo options:** outline n px; shadow (+3, +3); extrusion (+1..+4); halo (box blur r = 6, α ≤ 0.45); emblem (filled circle
  of a given share of the letter area); gradient fill; opaque plate.
- **Cover:** 300×450 with 2×2 supersampling and ±3 noise. Scenes: night portrait (navy sky, orange title band 4 %), jungle,
  teal sci-fi, blue (hue ≈ 255°), sepia, gray, big face (skin ellipse 38 %, orange title 4 %), red, red-orange (hue within 40°
  of `#D62828`).
- **Hero:** 960×320; sunset, blue sky, gray.
- **Downscaling** uses `PixelAnalysis.Resample`, as in production.

**`GameColorAnalysisTests.cs`** (40 tests). Each asserts the source, the accent hue within ±6° of the generator's colour
(OKLCH) and the confidence range. Mapped hex values are not asserted.

| # | Input | Expected |
|---|---|---|
| G1 | red `#D62828` logo, 2 px black outline; night-portrait cover | Logo, conf ≥ 0.9; the cover's orange in `Atmospheres` |
| G2 | white logo, black outline and shadow; night portrait | Cover; orange title (not navy, not skin) |
| G3 | black logo, gray cover, gray hero | Accent null; `Map` gives null |
| G4 | logo blue 50 % / yellow 33 % / red 17 %; jungle cover | Logo, blue; yellow is the first `Shines` candidate |
| G5 | white letters, orange 3 px outline; red cover | Logo, orange |
| G6 | yellow fill, thick red outline | Logo, yellow; red is the first secondary |
| G7 | neon cyan `#00F0FF` logo; gray cover | Logo, cyan; no atmosphere candidates |
| G8 | navy `#0B1F4D` logo | Logo; mapped accent Y ≥ 0.28, C ≥ 0.14, hue ±2° |
| G9 | pastel pink `#F7C6D9`, white outline | Logo, conf ≥ 0.5; mapped C ≥ 0.115 |
| G10 | beige brand `#E3A46F` logo; portrait cover | Logo (no skin rule on logos) |
| G11 | white logo with a 1.1 % red dot; sepia cover | Accent null |
| G12 | sunset hero only (no logo, generated cover) | Hero |
| G13 | white logo; teal sci-fi cover | Cover, teal |
| G14 | white lettering with a cyan halo (α < 128) | Logo through the halo rule, cyan |
| G15 | slate `#6B7790` logo | conf < 0.5, accent null |
| G16 | white logo with a 15 % red bar | Logo, red (ChromaShare ≥ 0.10) |
| G17 | big-face cover | Cover, orange; skin never the accent |
| G18 | white letters, heavy 3 px black outline, red emblem at 3.5 % of the letter area, no cover or hero | Logo, red, conf ≥ 0.7 (calibrate the generator so that the frame mutant drops below 0.5) |
| G18b | the G18 logo with the teal sci-fi cover | Cover, teal (logo dominance rule) |
| G18c | the G18 logo with the red-orange cover | Logo, red (agreement ≤ 40°) |
| G19 | opaque red plate with white text | Logo, red |
| G20 | opaque black box with yellow text | Logo, yellow |
| G21 | fully transparent; 1×1; all α < 24 | null / accent null, no exception |
| G22 | gold metallic gradient with dark extrusion | Logo, gold; extrusion is a frame |
| G23 | opaque beige plate `#E3A46F` with white text | Logo (opaque logos keep the logo rules) |

That is 25 tests. Then:
- **Mutations (6).** Each switches an internal option off and asserts that the result **changes**:
  - G17 without the skin rule picks skin
  - G18 without frame exclusion gives conf < 0.5
  - G6 without fill-over-outline gives red
  - G14 without the halo rule gives null
  - G22 scored on raw clusters instead of families fails stability
  - G18b without the dominance rule gives the logo
- **Properties (9):**
  - determinism, comparing with `GameColors` value equality (1)
  - mirror image gives an identical result (1)
  - stability, a Theory with 5 rows for G1, G4, G6, G13 and G22: noise ±8, a ½ px shift and 90 % scale give the same source
    and a mapped accent within ΔE_ok ≤ 0.03 (5)
  - confidence rises monotonically with chroma (1)
  - analysis bounds: pixels, bin array length, K, iterations (1)

**`ColorScienceTests.cs`** (8):
- round trip ±1/255
- Y of the reference colours
- the gamut clip keeps the hue
- `Fit` stays in the band for 36 hues × 7 L × 4 C, for all three roles
- the `Fit` chroma floor and lift boost
- `Fit` examples (navy, maroon, forest, dark teal, pastel pink): hue ±2°, C ≥ 0.115, dark teal stays below L 0.75
- `Mud` cases
- `Lerp`: shortest arc, gray end, alpha

**`GameColorsIndexTests.cs`** (8):
- JSON round trip
- an old index without `colors` gives null
- a `v` mismatch counts as missing
- `ResetDerived` clears `Colors`
- `CurrentVersion` is still 1
- an unknown `src` string does not discard the index
- an entry with 3 + 3 candidates is ≤ 260 bytes
- `GameColors` value equality

**`Config/ShowcaseConfigTests.cs`** (edit, +2): an old config gives `ColorsFollowGame == false`; save/load round trip and
Standard.

### 9.2 App unit: `tests/HdrPilot.App.Tests/Theming/` (namespace `HdrPilot.App.Tests.Theming`)

**`GamePaletteTests.cs`** (17):
- bands and hue drift through `Map`
- derived atmosphere: warm −40°; cool +40°
- spread to 32°
- mud ≥ 0.70 and its fallback step
- Tritone: candidate, arc, derived fallback, accent–shine mud and ≤ 150°
- Duotone = `DerivedShine`
- `Map(null)` and `Map(gray)` give null
- the glow hint reorders candidates
- `Calm` caps chroma at 0.03 and keeps the hue
- `Scheme` equals `PaletteEngine.Compute` exactly for 8 presets × 2 themes
- the light amber rule: gold AccentText hue 70–80°, contrast ≥ 4.6
- bounds ≥ / ≤ every sampled background
- bounds cover the formula inputs (`FromName` over 360 hues, the `PixelAnalysis` clamp constants)

**`GamePaletteContrastTests.cs`** (3 Facts):
- Raw accents: 36 hues × 7 L (0.25–0.95) × 4 C (0.03–0.25), with conf 1 and no candidates.
- 300 seeded random raw sets with candidates.
- The light-theme amber subset.

`Map` runs once per raw set and mode. The engine runs per combination: 8 scheme surface sets × dark/light × automatic text
on/off. Every row of 4.5 is asserted. Single-threaded, no timing assert.

**Edits:**
- `PaletteEngineTests.cs` (+1): the new overload with `surfaceAtmosphere = palette.Atmosphere` and no extras equals the old
  one for every preset × theme × mode × auto.
- `ThemeResourcesTests.cs` (+2):
  - accent key completeness: for two palettes with the same surface atmosphere and different C1–C3, every key whose `Build`
    value differs is in `BrushKeys`, `ColorKeys` or `ExcludedKeys`
  - `AddAccentEntries` writes exactly those 25 keys

**`OklchFadeTests.cs`** (5):
- keyframe count and key times
- starts from the current value (no `From`)
- chroma between keyframes ≥ 0.6·min(C_from, C_to) for the three generated pairs
- gray-end hue
- alpha is linear

**`LiveBrushTests.cs`** (6):
- `SnapshotAndReplace` and no `From`
- retargeting the same instance twice keeps the instance
- `Settle` leaves base values = targets and no animated properties
- incompatible structure and frozen live brushes are reported
- d = 0 leaves no clocks
- per-stop no-op when the target is unchanged (a running fade survives)

**`GameAccentLayerTests.cs`** (STA, `WpfTestHost`, 7):
- resolved values equal `Build(target)` for every layer key
- the same instances across transitions
- the Color part is swapped once, at the start, per changed apply, and never when unchanged
- the settle phase through the manual timer hook
- `onSettled` and generation behaviour
- after `Dispose` nothing is animated
- an interrupted fade continues from the colour on screen

**`GameAccentSchedulerTests.cs`** (8):
- a single change gives ApplyNow
- 33 ms bursts give Hold, then `Elapsed` at last + 300 ms gives ApplyNow
- colours arriving while holding give None
- the key-repeat timeline gives exactly 1 + 1
- `repeat` holds at once
- late tick
- early tick re-holds
- a not-fast change cancels holding

### 9.3 Window tests: `tests/HdrPilot.App.Tests/Library/` (namespace `HdrPilot.App.Tests.Library`)

**Setup:** `MainWindow` + `LibraryViewModel` + `FakeArtworkService`, which now implements `IGameColorsSource` (9.5).
- Colours come from `GameColorAnalysis` over `GeneratedArt`, or are set directly. Effect level Aus unless stated.
- In the test host, app-level accents come from the token files (Abendrot). A test that needs another scheme inserts
  `ThemeResources.Build(PaletteEngine.Compute(scheme, dark, auto))` behind the tokens, as `AppearanceService.Replace` does,
  and removes it afterwards.

**`GameAccentWindowTests.cs`** (17):

| # | Checks |
|---|---|
| W1 | Switch off: no layer in `window.Resources.MergedDictionaries`; `FindResource(BrushAccent)` is the app-level instance; 10 selections give 0 layer operations and 0 colour requests. |
| W2 | Switch on: the red game resolves `Hp.Brush.Accent` stops to `GamePalette.Compute(...).AccentFill1/2`; the instance is the same across selections. |
| W3 | The style-setter sites resolve the layer: `Lib.Tag` native, `Lib.HdrBadge` native, the HDR-native badge border (`Ambient3`), `Lib.SectionLine`. If not: the fallback of 5.7, then assert again. |
| W4 | Voll: a selection change starts animations (stats); reselecting mid-fade keeps the instances; after settle, no animated properties. |
| W5 | Debounce through the injected clock and timer hook: 20 fast selections give `Applied` 1 + 1 and `Held` 19; a key-repeat flag holds from the first repeat. |
| W6 | Popups: the game `ContextMenu` and a toolbar tooltip resolve the layer brush (otherwise the fallback of 5.7). |
| W7 | High contrast through `SetToolbarHighContrast(true)`: the layer is removed at once and `_accents == scheme`; back off: reinstalled. |
| W8 | `CapsuleDot`, `StatusDot`, `NitsScale`, `BrandGlyph`, `EffectsGlyph` and the `EdgeLine` off-state resolve layer instances when on, app-level instances when off. |
| W9 | The visual trees of the selected row, tile, switch and HDR button are identical between scheme and game, apart from brush colours. |
| W10 | With scheme `royal` installed app-level: a gray game after a red one gives exactly the scheme values; art still loading keeps red; an empty selection keeps red. |
| W11 | Particles and burst use `_accents`. Background off: they contain the game accent. Background on: art colours plus the game shine. |
| W12 | `Appearance.Changed`: a scheme change while on recomputes the layer. A theme change is instant and uses `_dark` after `ReadTheme`, with `AppearanceService` raising first. |
| W13 | Switch off, then on again before settle: the layer stays (generation token). |
| W14 | Colours before art: applied with the art. Art before colours: applied with the colours. Colours for an earlier selection are ignored. |
| W15 | Farben an, Hintergrund aus: backdrop and hero palette come from `Calm(scheme)` (chroma ≤ 0.03) and are the same for two different games. Both on with a gray hero: the mix uses `_accents`. |
| W16 | Toolbar: seam, streak and the `ToolbarToggle` halo come from `_accents`. `ApplyAmbient` right after the start of an accent fade does not cancel it: the stops keep their animation. |
| W17 | Light theme through `WpfTestHost.UseTheme(false)` with `StaticAppearanceService` dark: the layer has light-theme values. |

**`GameAccentCostTests.cs`** (5):
- counters per applied change
- allocation per applied change
- held selections: counters and allocation
- switch off: zero operations and no requests
- idle after settle

### 9.4 Settings tests (namespace `HdrPilot.App.Tests.Shell`)

- **`SettingsLayoutTests.cs`:** add `"ColorsFollowGameToggle"` to `NewIds`, after `BackgroundFollowsGameToggle`.
- **`SettingsAppearancePageTests.cs`** (+6):
  - toggling writes `colorsFollowGame`
  - Standard resets it
  - `ColorsFollowSample` shows the scheme gradient for a gray preview game and the game gradient for a red one
  - preview colours are requested only through `IGameColorsSource` and cancelled on close
  - the preview layer is attached only while on and not high contrast
  - `StageDot` resolves from inside `Stage`

### 9.5 Artwork service: `tests/HdrPilot.App.Tests/Artwork/ArtworkServiceGameColorsTests.cs` (new file, 12)

Uses `ArtworkFixture` images. `ArtworkServiceTests.cs` is not edited.
- computed once and read back from the index
- `TryPeek` succeeds after the first computation
- a signature change (custom logo chosen) recomputes
- generated cover and hero are skipped
- a name-rendered logo is not a source
- `CoverFromSquare` reads the square file
- a real cover that fails to decode is absent, and the result is not stored
- race: the signature changes during the computation, and nothing is written
- an unexpected exception gives `None` and leaves the artwork unaffected
- `LoadImage` and `GetArtworkAsync` never compute
- an old index computes lazily
- the hypothetical preview never computes or stores

`FakeArtworkService` (`Library/ShowcaseTestSupport.cs`, sealed) changes additively:
- it implements `IGameColorsSource`
- optional constructor parameters `Func<string, GameColors?>? colors = null` and `bool colorsAsync = false`
- with `colorsAsync`, `TryPeek` is false until the test calls `CompleteColors(key)`

---

## 10. Render scenarios with generated art, and the gate script

The existing `--snapshot` scenes (`App/AppServices.Library.cs:84-154`) read the real library and launcher caches, which is not
allowed here, and the product binary should not carry demo art. So these are **WPF render tests**, using
`HDRPILOT_LIBRARY_RENDER_DIR` like `MainWindowLayoutTests` and `AutoHdrLayoutTests`.

**`tests/HdrPilot.App.Tests/Library/GameColorRenderTests.cs`** (9 Facts, 18 PNGs):
- Window 1600×900, effect level Aus, drawer closed.
- Invented games "Testspiel Rot", "Testspiel Blau", "Testspiel Grau", "Testspiel Bunt", "Testspiel Navy", "Testspiel Gold".

| Fact | Art | Views × themes | Expectation |
|---|---|---|---|
| rot | G1 logo + night-portrait cover | Liste, Regal × dark, light (4) | red accents, orange atmosphere |
| blau | white logo + blue cover | Liste × dark, light (2) | blue from the cover |
| grau | G3, with scheme `royal` installed app-level | Liste × dark, light (2) | equals the switch-off render within ΔE_ok ≤ 0.01 |
| bunt-duo | G4 logo + jungle cover, Duotone | Raster × dark, light (2) | blue; green or derived atmosphere |
| bunt-tri | the same, Tritone | Raster × dark, light (2) | yellow shine; accent–shine without a gray middle |
| bg-aus | G1, background switch off | Liste × dark, light (2) | calm background (sampled glow pixel C ≤ 0.05) |
| hint | red logo + blue-sky hero, both switches on | Regal × dark (1) | the atmosphere follows the hero glow when a candidate is within 45° |
| navy | G8 logo | Liste × dark, light (2) | vivid medium blue, not a dusty pastel (C ≥ 0.14) |
| gold | gold `#E8B500` logo | Liste × light (1) | section-line and accent-text pixels amber (hue 70–80°) |

**Assertions:**
- **Starten button:** a pixel 8 px inside the left edge at mid-height (found with `TranslatePoint`). Compare it with the
  **gradient value at that position**: the `Hp.Brush.Accent` axis (0, 0.32) → (1, 0.68), sRGB interpolation between
  AccentFill1 and AccentFill2, over the window background. ΔE_ok ≤ 0.03.
- **Selection ring:** pixel ≥ 3:1 against the neighbouring background.
- **gray:** as in the table.

PNGs are written only when `HDRPILOT_LIBRARY_RENDER_DIR` is set, named `gamecolors-<fact>-<view>-<theme>.png`.

**New `tools/gates/phase15-gamecolors.ps1`:**
1. Set `HDRPILOT_LIBRARY_RENDER_DIR` to `%TEMP%\HDRpilot-Gates\phase15-gamecolors` and clear that folder.
2. Run `tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrPilot.App.Tests.Library.GameColorRender" -MinTests 9`.
3. Check that all 18 expected files exist, are exactly 1600×900, and are not uniform (pixel variance > 0).
4. Print `PHASE15_GAMECOLORS_OK` (exit 0) or `PHASE15_GAMECOLORS_FAILED`.

It starts no app, reads no config or caches, and uses no network.

**`tools/gates/phase10-settings-ui.ps1`** (edit):
- add `'ColorsFollowGameToggle'` to `$newIds`, after `'BackgroundFollowsGameToggle'`
- toggle on, then `Wait-Appearance $cfg { param($s) $s.appearance.colorsFollowGame -eq $true }`, then toggle off
- check that "Standard" leaves it false

`phase10-ui.ps1` stays unchanged, because the default is off.

---

## 11. Docs (paste-ready, German)

### 11.1 `docs/entscheidungen.md`, new entry after E92 (E93 if the parallel track does not claim it)

**E94 Farben folgen dem Spiel (Wunsch des Nutzers 2026-10-22, G15):** Neuer Schalter „Farben folgen dem Spiel“ (Standard aus,
`colorsFollowGame`) neben „Hintergrund folgt dem Spiel“; beide wirken unabhängig. Ist er an, nehmen alle Akzente der Bibliothek,
die sonst das Farbschema färbt, die Farben des gewählten Spiels an: Titelbereich, Starten- und HDR-Knopf samt Leuchten,
Detailbereich, Auswahl in Liste, Raster und Regal, Werkzeugleiste samt Lichtnaht, Streif und Schein der Lasche, Schalter,
Fokusrahmen, Kennzeichen, Zähler der Filter, Fensterkante und Partikel im HDR-Moment.

- **Herkunft:** Der Akzent kommt aus dem Logo (Markenfarbe), wenn es farbig genug ist: mindestens ein Zehntel seiner Fläche
  ist farbig, oder sein Farbton passt zu Cover bzw. Titelbild, oder diese sind grau. Sonst kommt er aus dem Cover, sonst aus
  dem Titelbild. Atmosphäre und Glanz kommen aus Zweitfarben von Logo, Cover und Titelbild, sonst abgeleitet wie bei den
  Vorlagen: ±40°, mindestens 30° Abstand, keine graue Mitte im Verlauf, im Tritonus auch nicht zwischen Akzent und Glanz.
  Duoton/Tritonus des Nutzers gilt weiter. Sind beide Schalter an, wird die Zweitfarbe bevorzugt, die zum Leuchten des
  Titelbilds passt.
- **Was nie zählt:** dunkle oder graue Konturen und Schatten, Hauttöne auf Cover und Titelbild, kleine Punkte, erzeugte Bilder
  und Farben aus dem Namen. Eine farbige Kontur um weiße Schrift kann dagegen die Markenfarbe sein.
- **Graue Bilder:** Fast graue Bilder ergeben keine Spielfarbe; dann gilt genau das Farbschema. Es wird nicht gemischt, weil
  Mischungen weit entfernter Farbtöne grau-braune Knöpfe ergeben.
- **Auslesen:** nur bei eingeschaltetem Schalter (und für das Vorschauspiel der Seite „Darstellung“), einmal je Spiel und
  Bildstand. Gelesen werden kleine Fassungen von Logo, Cover und Titelbild, auf dem Bilder-Thread mit niedriger Priorität;
  das Verfahren arbeitet in OKLab mit fester Rasterung und ist deterministisch. Gespeichert wird das Ergebnis als `colors` in
  `art\index.json` mit eigener Fassung; ändert sich ein Bild, wird neu gelesen. Bei ausgeschaltetem Schalter kostet die
  Funktion nichts.
- **Kontrast:** Die Farben werden in feste Wertebereiche gebracht: Helligkeit je Farbton und Mindest-Buntheit; dunkle
  Markenfarben werden heller und kräftiger, nicht blass. Danach laufen sie durch dieselbe Kontrastrechnung wie die Vorlagen:
  Text auf Akzent und Akzenttext mindestens 4,5:1, Bedienelemente mindestens 3:1, hell und dunkel, gegen die Flächen des
  Schemas und gegen den hellsten bzw. dunkelsten möglichen Grundton der Bibliothek. Bei automatischer Textfarbe steht auf
  Akzentflächen immer die dunkle Schrift. Gelb und Gold werden im hellen Thema für Text und Ränder zu Bernstein abgedunkelt
  statt zu Oliv.
- **Geltungsbereich:** Nur das Bibliotheksfenster folgt. Die Einstellungen (außer der Live-Vorschau), das Tray-Menü, die
  Dialoge sowie Tray- und Fenstersymbol behalten das Farbschema. Bei hohem Kontrast gibt es keine Spielfarben.
- **Umschalten:** Eine eigene Farbebene im Fenster mit bleibenden Pinseln wird einmalig überblendet, im OKLCH-Farbraum und vom
  gerade gezeigten Farbton aus, damit keine graue Mitte entsteht: bei „Voll“ 0,9 s wie der Titelbereich, bei „Reduziert“
  0,3 s, bei „Aus“ sofort. Ist das Fenster versteckt oder minimiert, läuft ein Spiel mit Pause oder wechselt das Thema, geht es
  sofort. Leuchten, Punkte und Kennzeichen mit Rohfarben wechseln zu Beginn der Überblendung. Die Farben kommen zusammen mit
  den Bildern des Spiels. Die Wörterbücher werden nicht je Spiel ausgetauscht.
- **Schnelle Auswahl:** Wechselt die Auswahl schneller als alle 300 ms oder ist die Pfeiltaste gehalten, bleiben die Farben
  stehen und folgen 300 ms nach dem letzten Wechsel.
- **Leerlauf:** Im Leerlauf läuft nichts.
- **Hintergrund:** Ist „Hintergrund folgt dem Spiel“ aus, bleibt der Hintergrund für alle Spiele gleich: ein ruhiger Grund im
  Farbton des Schemas mit stark gesenkter Buntheit, damit keine Schemafarben neben den Spielfarben leuchten. Sind beide an,
  mischen sich graue Titelbilder im Hintergrund mit den gezeigten Spielfarben.
- **Bekannte Grenze:** Der weiße Knopf der Schalter erreicht auf hellen Akzenten weniger als 3:1, wie bei hellen Vorlagen; der
  Zustand ist zusätzlich an der Lage des Knopfs zu sehen. Wer für ein Spiel andere Farben möchte, wählt ein anderes Logo oder
  Cover („Bild wählen…“).

### 11.2 `README.md`, settings table, new row directly after "Hintergrund folgt dem Spiel"

```
| Farben folgen dem Spiel | Akzente der Bibliothek (Starten- und HDR-Knopf, Auswahl, Schalter, Fokusrahmen, Kennzeichen, Werkzeugleiste, Partikel im HDR-Moment) nehmen die Farben von Logo und Cover des gewählten Spiels an, einmal pro Spiel ausgelesen und nur bei eingeschaltetem Schalter. Bei grauen Bildern und bei hohem Kontrast gilt das Farbschema; Einstellungen, Tray-Menü und Dialoge behalten es immer | aus |
```

The first cell must equal `ColorsFollowGameLabel`; `phase5-readme.ps1` checks this.

### 11.3 `docs/design/showcase-hero/umsetzung-theme.md`

New section "Spielfarben (E94)", covering:
- the layer, its key lists and the excluded keys
- `AddAccentEntries`
- OKLCH fades without `From`
- the Color-part swap at the start
- `LiveBrushSet` with per-stop targets
- the scheduler, including key repeat
- the calm background
- the scope table of 5.8
- the on-demand reading through `IGameColorsSource`

### 11.4 `docs/stand.md`

A status line for Phase 15.

---

## 12. `GATES.md` proposal (paste-ready, German)

The MinTests values follow from section 9. Existing counts on `main`: PaletteEngine 75, ThemeResources 5, ThemeKeys 4,
ThemeTokenFile 2, ShowcaseConfig 6, SettingsLayout 18, SettingsAppearancePage 11. Adjust them if the implementation base
differs.

```markdown
## Phase 15: Farben folgen dem Spiel (Wunsch des Nutzers 2026-10-22, E94)

Scope: Schalter „Farben folgen dem Spiel“ (Standard aus) neben „Hintergrund folgt dem Spiel“; Akzente der Bibliothek aus Logo,
Cover und Titelbild des gewählten Spiels. Tests und Bilder nur mit erzeugten Spielbildern; kein Gate dieser Phase liest echte
Spielbilder oder Launcher-Caches.

- [ ] G15.1: Spielfarben auslesen: Akzent aus dem Logo (Markenfarbe), wenn es farbig genug ist (mindestens ein Zehntel farbige Fläche oder passend zu Cover bzw. Titelbild oder diese grau), sonst Cover, sonst Titelbild; Zweitfarben für Atmosphäre und Glanz; dunkle oder graue Konturen und Schatten, Hauttöne auf Cover und Titelbild, kleine Punkte, erzeugte Bilder und Namensfarben ergeben nie den Akzent; fast graue Bilder ergeben keine Spielfarbe; deterministisch und stabil (Rauschen, halbes Pixel, 90 % Größe: ΔE_OK ≤ 0,03); gespeichert als `colors` in `art\index.json` mit eigener Fassung (unbekannte Werte verwerfen das Verzeichnis nicht, Fassung des Verzeichnisses bleibt 1); per Unit-Tests mit erzeugten Bildern und Gegenproben (Mutanten: ohne Ausschluss der Kontur, ohne Hautregel, ohne Füllung vor Kontur, ohne Leuchten, ohne Farbfamilien, ohne Vorrang-Regel des Logos) belegt
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrPilot.Tests.Artwork.GameColor|FullyQualifiedName~HdrPilot.Tests.Artwork.ColorScience" -MinTests 56
  EXPECT: TESTS_OK
  EVIDENCE: pending

- [ ] G15.2: Zuordnung und Kontrast: Spielfarben in festen Wertebereichen (Helligkeit je Farbton, Mindest-Buntheit 0,12, dunkle Markenfarben heller und kräftiger, Abstand Akzent–Atmosphäre 30–120°, keine graue Mitte, im Tritonus auch zwischen Akzent und Glanz); Text auf Akzent und Akzenttext mindestens 4,5:1, Bedienelemente mindestens 3:1 in Hell und Dunkel gegen die Flächen aller Schemata und den möglichen Grundton der Bibliothek (Eigenschaftstest über mehr als 1000 erzeugte Paletten); bei automatischer Textfarbe immer dunkle Schrift und unveränderte Akzentflächen; Gelb und Gold im hellen Thema Bernstein statt Oliv; graue Spiele genau das Farbschema; die bisherigen Vorlagen und Token-Dateien unverändert
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrPilot.App.Tests.Theming.GamePalette|FullyQualifiedName~HdrPilot.App.Tests.Theming.PaletteEngine|FullyQualifiedName~HdrPilot.App.Tests.Theming.ThemeResources|FullyQualifiedName~HdrPilot.App.Tests.Theming.ThemeKeys|FullyQualifiedName~HdrPilot.App.Tests.Theming.ThemeTokenFile" -MinTests 109
  EXPECT: TESTS_OK
  EVIDENCE: pending

- [ ] G15.3: Umschalten in der Bibliothek: eigene Farbebene nur im Bibliotheksfenster (Einstellungen außer der Vorschau, Tray-Menü, Dialoge und Symbole behalten das Schema), bleibende Pinsel ohne Austausch der Wörterbücher je Spiel, Überblendung im OKLCH-Farbraum vom gezeigten Wert aus ohne graue Mitte (Voll 0,9 s, Reduziert 0,3 s, Aus sofort), Rohfarben wechseln zu Beginn, Farben kommen zusammen mit den Bildern, Entprellung 300 ms bei schneller Auswahl und gehaltener Taste, graue Bilder → Farbschema, ladende Bilder und leere Auswahl → Farben bleiben, hoher Kontrast → keine Spielfarben, Kontextmenüs, Tooltips und Kennzeichen folgen, Werkzeugleiste samt Schein der Lasche folgt, „Hintergrund aus“ ergibt einen ruhigen, gleichbleibenden Grund; danach keine Uhren und keine laufenden Animationen
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrPilot.App.Tests.Theming.OklchFade|FullyQualifiedName~HdrPilot.App.Tests.Theming.LiveBrush|FullyQualifiedName~HdrPilot.App.Tests.Theming.GameAccent|FullyQualifiedName~HdrPilot.App.Tests.Library.GameAccentWindow" -MinTests 43
  EXPECT: TESTS_OK
  EVIDENCE: pending

- [ ] G15.4: Einstellung und Seite „Darstellung“: `colorsFollowGame` Standard aus, alte Konfigurationen unverändert, „Standard“ setzt zurück; Schalter mit Kennung `ColorsFollowGameToggle`, Name und Hilfetext aus ShellStrings.resx (auch „Hintergrund folgt dem Spiel“ mit Hilfetext), Farbprobe und Live-Vorschau in den Spielfarben des Vorschauspiels, bei hohem Kontrast ohne Spielfarben
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrPilot.Tests.Config.ShowcaseConfig|FullyQualifiedName~HdrPilot.App.Tests.Shell.SettingsLayout|FullyQualifiedName~HdrPilot.App.Tests.Shell.SettingsAppearancePage" -MinTests 43
  EXPECT: TESTS_OK
  EVIDENCE: pending

- [ ] G15.5: Einstellungen per UI-Automation mit der neuen Kennung (Schalter an und aus landet sofort in der Konfiguration)
  CHECK: ps tools\gates\phase10-settings-ui.ps1
  EXPECT: PHASE10_SETTINGS_UI_OK
  EVIDENCE: pending

- [ ] G15.6: Bilder mit erzeugten Spielbildern (rot/orange, weiß/blau, grau mit anderem Schema, bunt in Duoton und Tritonus, Hintergrund aus, Leuchten des Titelbilds, dunkle Markenfarbe, Gold im hellen Thema; Liste, Raster, Regal; hell und dunkel) in genauer Größe, Starten-Knopf im erwarteten Verlauf, graues Spiel gleich dem Farbschema
  CHECK: ps tools\gates\phase15-gamecolors.ps1
  EXPECT: PHASE15_GAMECOLORS_OK
  EVIDENCE: pending

- [ ] G15.7: Kosten: Auslesen nur bei eingeschaltetem Schalter bzw. für das Vorschauspiel, einmal je Spiel und Bildstand, nie für Kacheln, Kontaktbogen oder Bildschirmfotos; die Farbebene macht je angewendetem Wechsel höchstens einen Austausch der Farbwerte und keinen neuen Pinsel, bei gehaltener Pfeiltaste keine Arbeit, Speicher je Wechsel unter der gemessenen Grenze; Leerlauf mit geschlossenem Fenster wie bisher
  CHECK: ps tools\gates\test.ps1 -Filter "FullyQualifiedName~HdrPilot.App.Tests.Library.GameAccentCost|FullyQualifiedName~HdrPilot.App.Tests.Artwork.ArtworkServiceGameColors" -MinTests 17
  EXPECT: TESTS_OK
  CHECK: ps tools\gates\phase3-idle.ps1
  EXPECT: PHASE3_IDLE_OK
  EVIDENCE: pending (Grafikkartenlast mit eingeschaltetem Schalter wie G10.9 nur zusammen mit dem Nutzer: `phase10-gpu.ps1`, PHASE10_GPU_OK)

- [ ] G15.8: README und E94 passen zu den resx-Texten; keine lokalen Pfade
  CHECK: ps tools\gates\phase5-readme.ps1
  EXPECT: PHASE5_README_OK
  CHECK: ps tools\gates\publish-check.ps1
  EXPECT: PUBLISH_CHECK_OK
  EVIDENCE: pending

- [ ] G15.9: Gesamtstand: build.ps1 ohne Warnungen, alle Tests und die UI-Gates ohne echte HDR-Umschaltung grün, unabhängige Prüfung mit Gegenprüfung
  CHECK: ps tools\gates\build.ps1
  EXPECT: BUILD_OK_0_WARNINGS
  CHECK: ps tools\gates\test.ps1
  EXPECT: TESTS_OK
  EVIDENCE: pending

- [ ] G15.10: Abnahme durch den Nutzer am echten System (echte Spiele, Hell und Dunkel, Pfeiltaste gehalten, beide Schalter einzeln und zusammen; Blick auf den ruhigen Grund bei „Hintergrund aus“ und auf Gold/Gelb im hellen Thema)
  EVIDENCE: pending
```

---

## 13. Files touched

**New:**

| File | Content |
|---|---|
| `Core/Artwork/ColorScience.cs` | OKLab/OKLCH, Y, gamut clip, `Fit`, `Mud`, `Lerp`, smoothstep, hue distance |
| `Core/Artwork/GameColors.cs` | `GameColors` (value equality), `GameSwatch`, `GameColorSource`, `GameColorKind`, `IndexedGameColors` |
| `Core/Artwork/GameColorAnalysis.cs` | `Analyze`, `Derive`, `Version`, constants |
| `App/Services/Artwork/GameColorsContracts.cs` | `IGameColorsSource` |
| `App/Services/Artwork/ArtworkService.GameColors.cs` | partial: implementation, memo, inputs, counter |
| `App/Theming/GamePalette.cs` | `Map`, `Compute` (amber rule), `Scheme`, `Calm`, `LibraryBackgroundBounds` |
| `App/Theming/OklchFade.cs` | keyframe fades |
| `App/Theming/GameAccentLayer.cs` | key lists, layer, `GameAccentStats` |
| `App/Theming/LiveBrush.cs` | `LiveBrush`, `LiveBrushSet` |
| `App/Theming/GameAccentScheduler.cs` | debounce state machine |
| `App/MainWindow.GameColors.cs` | partial: the wiring of 5.5 |
| `tools/gates/phase15-gamecolors.ps1` | render gate |
| Core tests | `GeneratedArt.cs`, `GameColorAnalysisTests.cs`, `ColorScienceTests.cs`, `GameColorsIndexTests.cs` |
| App tests | `Theming/GamePaletteTests.cs`, `GamePaletteContrastTests.cs`, `OklchFadeTests.cs`, `LiveBrushTests.cs`, `GameAccentLayerTests.cs`, `GameAccentSchedulerTests.cs`; `Library/GameAccentWindowTests.cs`, `GameAccentCostTests.cs`, `GameColorRenderTests.cs`; `Artwork/ArtworkServiceGameColorsTests.cs` |

**Edited:**

| File | Change | Possible collision |
|---|---|---|
| `Core/Artwork/ArtworkIndex.cs` | + `Colors` on `ArtworkIndexEntry`, + one line in `ResetDerived` | SteamGridDB track (`IndexedSteamGridDb`, `Signature`): additive, different region |
| `Core/Config/ShowcaseSettings.cs` | + `ColorsFollowGame` | none |
| `App/Theming/AppearanceContracts.cs` | `AppearanceState` + `ColorsFollowGame = false` | none |
| `App/Theming/AppearanceService.cs` | `Compute` passes the flag | none |
| `App/Theming/PaletteEngine.cs` | overload | none |
| `App/Theming/ThemeResources.cs` | extract `AddAccentEntries` (no other change) | none |
| `App/MainWindow.xaml.cs` | hooks of 5.5, `SetResourceReference` | **ui/hero (WIP), ui/details, ui/scroll**: implement after they merge into `phase5/release`, or rebase; hunks are small and local |
| `App/MainWindow.Toolbar.cs` | seam and streak through `LiveBrushSet` from `_accents`; halo colours; HC refresh | **ui/hero, ui/scroll**: same rule |
| `App/Views/Library/ToolbarToggle.cs` | `SetAccentColors(c1, c2)` replaces the static Abendrot `HaloBrush()` | none known (no UI branch touches it) |
| `App/ViewModels/Shell/AppearancePageViewModel.cs` | property, preview colours, sample, notifications | SteamGridDB part (428–548) untouched |
| `App/Views/Shell/SettingsWindow.xaml` | one row after `BackgroundFollowsGameToggle`; HelpText on that toggle | SteamGridDB section untouched |
| `App/Views/Shell/SettingsWindow.xaml.cs` | preview layer, HC subscription, `StageDot` reference | low |
| `App/Resources/ShellStrings.resx` | 2 entries | none |
| `tests/HdrPilot.Tests/Config/ShowcaseConfigTests.cs` | +2 | |
| `tests/HdrPilot.App.Tests/Theming/PaletteEngineTests.cs`, `ThemeResourcesTests.cs` | +1, +2 | |
| `tests/HdrPilot.App.Tests/Shell/SettingsLayoutTests.cs`, `SettingsAppearancePageTests.cs` | id, +6 | |
| `tests/HdrPilot.App.Tests/Library/ShowcaseTestSupport.cs` | `FakeArtworkService` implements `IGameColorsSource` (additive, optional parameters) | other tracks may edit: additive |
| `tools/gates/phase10-settings-ui.ps1` | `$newIds` + toggle round trip | |
| `README.md`, `docs/entscheidungen.md`, `docs/design/showcase-hero/umsetzung-theme.md`, `docs/stand.md`, `GATES.md` | texts of 11 and 12 | other tracks append too: append only |

**Deliberately not touched** (changed against the first spec: `ArtworkContracts.cs` and `ArtworkService.Load.cs` are no longer
edited):
- `MainWindow.xaml`, `Themes/Library.xaml` (except the pre-authorised fallback of 5.7), `Themes/Controls.xaml`,
  `Themes/Shell.xaml`, `Tokens.*.xaml`, `ThemeKeys.cs`
- `HeroStage.cs`, `ShowcaseLayout.cs`, `VirtualizingTilePanel.cs`, `GapScrollBar.cs`, `LibraryViewModel*.cs`,
  `AmbientPalette.cs`, `AmbientBackdrop.cs`
- `App/Services/Artwork/ArtworkContracts.cs`, `ArtworkService.Load.cs`
- `Core/Config/AppConfig.cs`, `ConfigStore.cs`
- `PixelAnalysis.cs` (used, not changed)

---

## 14. Files owned by the parallel SteamGridDB-search track: do not edit

- `App/Services/Artwork/SteamGridDbService.cs`, `App/Services/Artwork/SteamGridDbContracts.cs`
- `Core/Artwork/SteamGridDbClient.cs`, `Core/Artwork/SteamGridDbKeyStore.cs`
- `App/Views/Library/ArtworkPickerDialog.xaml`, `ArtworkPickerDialog.xaml.cs`, `App/Views/Library/LibraryDialogs.cs`
- `App/ViewModels/Library/ArtworkPickerViewModel.cs`, `App/ViewModels/Library/ArtworkTileViewModel.cs`
- `App/Services/Artwork/ArtworkService.Custom.cs`, `ArtworkService.Resolve.cs` (including `Hypothetical`, `SteamGridDbFiles`,
  `ComputeAmbient`), `ArtworkService.cs`, `ArtworkService.Coverage.cs`, `ArtworkWatcher.cs`.
  The new partial **calls** the private `Resolve`, `Enqueue`, `Entry`, `WithEntry` and `MarkDirty` and changes none of them.
- `App/Resources/ArtworkStrings.resx`
- `App/ViewModels/Shell/AppearancePageViewModel.cs` lines about 428–548 (SteamGridDB settings), and the `SectionArtwork` block
  of `SettingsWindow.xaml`
- In `Core/Artwork/ArtworkIndex.cs`: the `IndexedSteamGridDb` class, `SteamGridDbLastRun` and `Signature`. Only the two
  additive lines of 3.4 are allowed.
- `ArtworkContracts.cs` entirely (`IArtworkService`, `GameArtwork`)
- Tests: `SteamGridDbServiceTests.cs`, `SteamGridDbClientTests.cs`, any `ArtworkPicker*Tests`, `Artwork/ArtworkServiceTests.cs`

**Interface between the tracks:** none needed. A picker or SteamGridDB choice changes the art signature, which clears `colors`,
and the next request recomputes.

---

## 15. Implementation order (each step green before the next)

1. **Core:** `ColorScience` (with `Fit` and `Lerp`), `GameColorAnalysis`, `GameColors`, with tests and mutation checks.
2. **Reading:** the index field, `IGameColorsSource`, `ArtworkService.GameColors.cs`, `FakeArtworkService`, with service tests.
3. **Mapping:** `PaletteEngine` overload, `GamePalette` (bands, amber rule, `Scheme`, `Calm`, bounds), with property tests.
4. **Config and settings:** config, `AppearanceState`, strings, settings row, preview, with settings tests.
5. **Brushes:** `ThemeResources.AddAccentEntries`, `OklchFade`, `LiveBrush`, `GameAccentLayer`, with tests.
6. **Window:** scheduler, `MainWindow.GameColors.cs`, hooks, toolbar, `ToolbarToggle.SetAccentColors`,
   `SetResourceReference`, with window and cost tests. Merge `ui/hero`, `ui/details` and `ui/scroll` first if they have landed.
7. **Renders and docs:** render tests, `phase15-gamecolors.ps1`, docs, `GATES.md`, then the full gate run. That run is local,
   throttled and sequential, with one workflow at a time.

---

## 16. Follow-ups, out of scope

Each of these changes the presets or the general look:

- **Switch thumb on light accents:** the library switches would use `TextOnAccent` for the thumb (dark on light tracks), and
  `Hp.Brush.ToggleOn` (≥ 3:1) instead of `Hp.Brush.Accent` for the track. This needs a `Library.xaml` change and also changes
  the presets.
- **Amber rule for presets:** extend it to presets with yellow or gold accents.
- **Light streak core** built from the control colours.
- **Live brushes for the remaining `ApplyAmbient` window resources:** about 8 fewer tree walks per selection in dark and 17 in
  light.
- **Per-game option "Farbschema verwenden"**, an escape hatch for single games. Until then: choose another logo or cover.

---

## 17. Review of the two critiques

### 17.1 Design critique

| # | Verdict | Reason or change |
|---|---|---|
| D1 midpoint swap pops | **Accepted**, option (c) for every trigger | Verified: `SelectedHalo` (`MainWindow.xaml:347-352`, opacity set at once by the trigger at 479), the Starten and HDR glows and the switch halo use `Hp.Color.C2`. The Color part now swaps at t = 0, as the app-level `BuildTransition` already does ("Farben gelten sofort"). This also removes the deferred swap (T12). No XAML edits. |
| D2 fades through gray | **Accepted** | WPF `Color` arithmetic is scRGB. Generated pairs: direct midpoints keep 9–40 % chroma; the 4-segment OKLCH keyframes keep ≥ 79 %. `OklchFade`. |
| D3 "Farben an / Hintergrund aus" clash | **Accepted**, option (a) | Verified: `AmbientBackdrop.Apply` uses A2 34 %, A3 22 % and A1 30 %, and the `HeroStage` rays, dots, bloom and flash use A3, which is the scheme's shine when the background switch is off. The calm scheme (chroma ≤ 0.03) is constant per game. User check in G15.10. |
| D4 olive in the light theme | **Problem accepted; proposed fix rejected and replaced** | Mixing toward black in sRGB keeps the OKLCH hue. Constant-hue OKLCH darkening gives the same olive (`#E8B500`: engine `#8B6C00`, OKLCH `#927000`). Replacement: the darkened roles for hues 80–115° use hue 75° (amber, `#9F6A00`), for game palettes only. |
| D5 dusty pastels | **Accepted** | `Fit` revised: Cmin 0.12, lift boost 0.15, floor 0.30 for 125–230°, L search. Navy `#618EED`, maroon `#E46C6F`, forest `#35AA60`, dark teal `#00AFBA` (not neon), pastel pink `#FFA1C9`. Navy render added. |
| D6 style-setter brushes | **Accepted** (same as T22) | W3 asserts four sites; fallback pre-authorised (5.7). |
| D7 toolbar gap | **Accepted** for the capsule halo; **rejected** for `NewBadge` | Halo verified as hard-coded Abendrot; now `SetAccentColors`, which also fixes non-Abendrot schemes in 1.5.0. `NewBadge` is a fixed signal colour like Ok and Amber; the header "Neu" dot (C3) follows like every C3 site. |
| D8 three sources disagree | **Accepted**, reduced | The glow hint reorders atmosphere candidates (≤ 45°), only with both switches on and real, non-gray hero art. Stored data unchanged. Render "hint". |
| D9 tiny logo element dominates | **Accepted** | Logo dominance rule (≥ 10 % chromatic share, ≤ 40° agreement, or no confident cover/hero). G18b, G18c, mutation. |
| D10 scheme flash on cold open | **Accepted**, simplified | Memo peek (lock-free, no I/O). Reading the index on the UI thread was rejected: the index's first load does disk I/O under its lock. Colours that arrive before the window shows apply instantly. |
| D11 cost for default-off users | **Accepted** | On-demand reading, no `LoadArtwork` hook, small decodes instead of 1–3 MB copies. Skipping the hero decode was rejected as unnecessary once reading is gated. |
| D12 Tritone accent–shine mud | **Accepted** | `Mud(acc, shine) ≥ 0.70` and ≤ 150° added to selection, arc and property test. |
| D13 switch thumb contrast | **Accepted as known limit and follow-up** | The fix needs a `Library.xaml` change (the thumb fill is the literal `#FFFFFFFF`) and would change the presets too. |
| D14 gray is not exactly the scheme | **Accepted** (same as T4) | `GamePalette.Scheme`. |
| D15 docs contradict tests | **Accepted** | "dunkle oder graue Konturen und Schatten"; "Grundton". |
| D16 settings page gaps | (a) **accepted**, folded into the hint text; (b) **accepted**, HelpText added; (c) **rejected**: for a gray preview game, the scheme is the truthful preview; (d) **accepted** as documentation and a follow-up | |
| D17 first key repeat | **Accepted** | `Selected(now, repeat)`; one fade plus the final one. |
| D18 gray mix vs held accents | **Accepted** | The gray mix uses `_accents`. |

### 17.2 Tech critique

| # | Verdict | Reason or change |
|---|---|---|
| T1 `Fit` cannot reach Cmin | **Accepted** | Merged into the revised `Fit` (L search). The band property is C ≥ min(Cmin, highest in band) − 0.003. |
| T2 an exception blanks the artwork | **Accepted**, adapted | Verified: `Guarded` falls back to `EmptyArtwork`. Reading is now a separate request with its own try/catch; `LoadArtwork` is no longer involved. |
| T3 race writes old colours | **Accepted** | Write only when the entry's signature matches. |
| T4 scheme path uses the bounds | **Accepted** | `GamePalette.Scheme` = `PaletteEngine.Compute` exactly. W10 and the gray render use `royal`. |
| T5 generated pixels read as art | **Accepted**, adapted | Verified: `LoadSpec(...) ?? LoadFallback(...)`, and `CoverFromSquare` is a composite. Sources now come from the specs (`Generator.None`); the square file is read directly. |
| T6 dark flag | **Accepted** | Verified: `UseTheme` swaps only the token files. `_dark` is used everywhere; W17. |
| T7 fade cancelled by `ApplyAmbient` | **Accepted** | Per-stop tracked targets, a duration table per brush, frozen means incompatible. |
| T8 cost claims | **Accepted** | Verified: `ApplyAmbient` writes 8 or more window resources per selection. Budgets now cover the accent path only; G15.7 reworded; the 85 KB check dropped. |
| T9 gate filters | **Accepted** | Namespace-qualified filters, `GameAccentCostTests`, MinTests derived from the lists, property tests as Facts. |
| T10 theme order | **Accepted** | Verified: `AppearanceService` subscribes first and raises `Changed` synchronously. `IsDark` is ignored in `OnAppearanceChanged`. |
| T11 off/on removes the layer | **Accepted** | Generation token; W13. |
| T12 midpoint swap skipped | **Resolved** by D1 | No deferred swap any more. |
| T13 index size | **Accepted** | conf to 2 decimals, ≤ 260 bytes, 3 + 3 candidates kept. |
| T14 equality of records with lists | **Accepted** | Custom value equality on `GameColors`. |
| T15 dense bins on the large-object heap | **Accepted** | Sparse sorted bins. |
| T16 first colours fade instead of being instant | **Inconsistency accepted; forced-instant fix rejected** | Peek gives instant. Otherwise the colours fade in step with the hero crossfade: an instant jump 0.1–0.3 s after opening, while the hero fades in, would be a visible flash. A hidden window gives 0 anyway. |
| T17 temporary decode failure stored | **Accepted** | Neither memoised nor stored. |
| T18 enum strings in the index | **Accepted** | Verified: `JsonStringEnumConverter`, and `Load` discards the file on `JsonException`. Plain strings, lenient parsing. |
| T19 preset wording | **Accepted** | Bands described as their own rule. |
| T20 computing for everyone and other callers | **Accepted** | Resolved by on-demand reading. |
| T21 opaque logo analysed as cover | **Accepted** | Logo rules kept; G23. |
| T22 style setters | **Accepted** | W3 plus the fallback. |
| T23 Starten pixel check | **Accepted** | Expected value at the gradient position. |
| T24 HC in the settings preview | **Accepted** | Verified that there is no subscription today; one is added. |

### 17.3 Additional findings from this verification

| # | Finding | Change |
|---|---|---|
| V1 | The bounds sweep missed real inputs: the name ambient a1 is HSL(·, 0.55, **0.16**), the art a1 saturation is clamped to 0.2–0.75, and custom schemes and the calm scheme were not swept. | Sweep extended (4.6), plus a formula test. |
| V2 | Darkening in sRGB keeps the OKLCH hue, so "OKLCH instead of sRGB" cannot fix olive. | Amber rule (4.3). |
| V3 | In tests, app-level accents come from the token files (Abendrot), not from `StaticAppearanceService`. | Tests with other schemes insert `ThemeResources.Build` for that scheme (9.3). |
| V4 | Calling `TrimmedLogo` from a second worker can race with `LoadArtwork` on the same trim file. | The colour path reads logos read-only (3.3). |
| V5 | A fine L scan with gamut bisection costs about 5k evaluations per `Fit`. | Coarse-to-fine scan and a window `Map` cache. The property test maps once per raw set. |
