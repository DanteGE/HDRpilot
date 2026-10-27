using System.Reflection;

namespace HdrPilot.App.Theming;

/// <summary>
/// Schlüssel der Farb- und Schrift-Ressourcen (E89, G10.1). Alle Ansichten benutzen nur diese Schlüssel (DynamicResource);
/// die Darstellung erzeugt sie aus Palette und Thema, Tokens.Dark.xaml und Tokens.Light.xaml enthalten dieselben Schlüssel
/// mit den Werten der Vorlage „Abendrot“ (Testhost, Start vor dem ersten Anwenden). In Klammern das Gegenstück im
/// Prototyp (app.css). Nur hinzufügen, nie umbenennen oder entfernen: Bibliothek und Einstellungen bauen darauf.
/// </summary>
public static class ThemeKeys
{
    // ---- Farben (Color) ----

    /// <summary>Akzent (--c1): Schaltflächen, Auswahl, Schalter.</summary>
    public const string ColorC1 = "Hp.Color.C1";

    /// <summary>Atmosphäre (--c2): Hintergrundtönung, Leuchten.</summary>
    public const string ColorC2 = "Hp.Color.C2";

    /// <summary>Glanz (--c3): HDR-Moment, Lichtstreifen, Pegel. Im Duoton abgeleitet.</summary>
    public const string ColorC3 = "Hp.Color.C3";

    /// <summary>Text auf Akzentflächen (--on), automatisch mit mindestens 4,5:1 gegen Akzent und Atmosphäre.</summary>
    public const string ColorOnAccent = "Hp.Color.OnAccent";

    /// <summary>Akzent als Textfarbe (--at), lesbar auf Fläche und Hintergrund (mindestens 4,5:1).</summary>
    public const string ColorAccentText = "Hp.Color.AccentText";

    /// <summary>Grundton des Fensters ohne Spielfarben (--bg mit Schema-Umgebung).</summary>
    public const string ColorBackground = "Hp.Color.Background";

    /// <summary>Umgebung aus dem Schema (--a1), wenn „Hintergrund folgt dem Spiel“ aus ist oder noch kein Bild geladen ist.</summary>
    public const string ColorAmbient1 = "Hp.Color.Ambient1";

    /// <summary>Umgebung aus dem Schema (--a2).</summary>
    public const string ColorAmbient2 = "Hp.Color.Ambient2";

    /// <summary>Umgebung aus dem Schema (--a3).</summary>
    public const string ColorAmbient3 = "Hp.Color.Ambient3";

    /// <summary>Bisheriger Schlüssel: erste Akzentfarbe (= <see cref="ColorC1"/>).</summary>
    public const string ColorAccent1 = "Hp.Color.Accent1";

    /// <summary>Bisheriger Schlüssel: zweite Akzentfarbe (= <see cref="ColorC2"/>).</summary>
    public const string ColorAccent2 = "Hp.Color.Accent2";

    /// <summary>Bisheriger Schlüssel: dritte Akzentfarbe (= <see cref="ColorC3"/>).</summary>
    public const string ColorAccent3 = "Hp.Color.Accent3";

    /// <summary>Bisheriger Schlüssel: Leuchtfarbe.</summary>
    public const string ColorGlow = "Hp.Color.Glow";

    /// <summary>Bisheriger Schlüssel: Schattenfarbe (nur für Verläufe, nie für DropShadowEffect).</summary>
    public const string ColorShadow = "Hp.Color.Shadow";

    // ---- Pinsel (Brush) ----

    /// <summary>Fensterhintergrund (--bg).</summary>
    public const string BrushWindowBackground = "Hp.Brush.WindowBackground";

    /// <summary>Leuchten im Hintergrund (Verlauf aus der Atmosphäre).</summary>
    public const string BrushBackgroundGlow = "Hp.Brush.BackgroundGlow";

    /// <summary>Zweites Leuchten im Hintergrund, links unten aus dem Akzent (Prototyp .bgl).</summary>
    public const string BrushBackgroundGlow2 = "Hp.Brush.BackgroundGlow2";

    public const string BrushTitleBar = "Hp.Brush.TitleBar";

    public const string BrushSurface = "Hp.Brush.Surface";

    public const string BrushSurfaceHover = "Hp.Brush.SurfaceHover";

    public const string BrushSurfaceSelected = "Hp.Brush.SurfaceSelected";

    public const string BrushSurfaceInput = "Hp.Brush.SurfaceInput";

    /// <summary>Leichte Fläche (--s1).</summary>
    public const string BrushSurface1 = "Hp.Brush.Surface1";

    /// <summary>Mittlere Fläche (--s2).</summary>
    public const string BrushSurface2 = "Hp.Brush.Surface2";

    /// <summary>Kräftige Fläche (--s3).</summary>
    public const string BrushSurface3 = "Hp.Brush.Surface3";

    public const string BrushBorder = "Hp.Brush.Border";

    public const string BrushBorderStrong = "Hp.Brush.BorderStrong";

    /// <summary>Feine Linie (--ln).</summary>
    public const string BrushLine = "Hp.Brush.Line";

    /// <summary>Kräftigere Linie (--ln2).</summary>
    public const string BrushLineStrong = "Hp.Brush.LineStrong";

    /// <summary>Haupttext (--t1).</summary>
    public const string BrushText = "Hp.Brush.Text";

    /// <summary>Zweittext (--t2).</summary>
    public const string BrushTextSecondary = "Hp.Brush.TextSecondary";

    /// <summary>Gedämpfter Text (--t3), mindestens 4,5:1 auf dem Hintergrund (auch hell).</summary>
    public const string BrushTextMuted = "Hp.Brush.TextMuted";

    /// <summary>Text auf Akzentflächen (= <see cref="ColorOnAccent"/>).</summary>
    public const string BrushTextOnAccent = "Hp.Brush.TextOnAccent";

    /// <summary>Text über Spielbildern (immer hell, in beiden Themen; Prototyp .hc).</summary>
    public const string BrushTextOnArt = "Hp.Brush.TextOnArt";

    /// <summary>Zweittext über Spielbildern.</summary>
    public const string BrushTextOnArtSecondary = "Hp.Brush.TextOnArtSecondary";

    /// <summary>Akzentverlauf Akzent → Atmosphäre (--gr): Starten, HDR einschalten.</summary>
    public const string BrushAccent = "Hp.Brush.Accent";

    /// <summary>Verlauf Akzent → Glanz (--gs).</summary>
    public const string BrushAccentShine = "Hp.Brush.AccentShine";

    /// <summary>Einfarbiger Akzent (--c1).</summary>
    public const string BrushAccentSolid = "Hp.Brush.AccentSolid";

    /// <summary>Akzent als Text (--at).</summary>
    public const string BrushAccentText = "Hp.Brush.AccentText";

    /// <summary>Akzent 22 % (--ac2): Chips, Hover.</summary>
    public const string BrushAccentSoft = "Hp.Brush.AccentSoft";

    /// <summary>Akzent 62 % (--ac6): Ränder aktiver Elemente.</summary>
    public const string BrushAccentStrong = "Hp.Brush.AccentStrong";

    /// <summary>Atmosphäre 55 % (--m5): weiche Leuchtflächen.</summary>
    public const string BrushAtmosphereSoft = "Hp.Brush.AtmosphereSoft";

    /// <summary>Glanz aufgehellt (--gl).</summary>
    public const string BrushShine = "Hp.Brush.Shine";

    /// <summary>Gewählte Zeile/Kachel: Akzent 14 % (hell und dunkel deutlich sichtbar).</summary>
    public const string BrushSelectionFill = "Hp.Brush.SelectionFill";

    /// <summary>Ring der gewählten Zeile/Kachel: Akzent 45 %.</summary>
    public const string BrushSelectionRing = "Hp.Brush.SelectionRing";

    /// <summary>Tastaturfokus (Akzent, mindestens 3:1).</summary>
    public const string BrushFocusRing = "Hp.Brush.FocusRing";

    /// <summary>Hintergrund von Detailbereich und Karten (--pnl, Verlauf).</summary>
    public const string BrushPanel = "Hp.Brush.Panel";

    /// <summary>Innenlicht oben an Karten (--pin).</summary>
    public const string BrushPanelHighlight = "Hp.Brush.PanelHighlight";

    /// <summary>Menüs und Popups (--mn).</summary>
    public const string BrushMenu = "Hp.Brush.Menu";

    /// <summary>Glasfläche über Bildern (--glass), ohne Weichzeichner.</summary>
    public const string BrushGlass = "Hp.Brush.Glass";

    /// <summary>
    /// Bildplatte unter Vorschauen, Platzhaltern und dem Ladegerüst in „Bild wählen…“ (E93): dunkel fast schwarz, hell ein
    /// kühles Grau (das weiße Surface2 des hellen Themas ließe das Gerüst verschwinden).
    /// </summary>
    public const string BrushPlate = "Hp.Brush.Plate";

    /// <summary>Daumen eines ausgeschalteten Schalters (--off).</summary>
    public const string BrushToggleOff = "Hp.Brush.ToggleOff";

    /// <summary>Spur eines eingeschalteten Schalters: Akzent → Atmosphäre, mindestens 3:1 gegen Fläche und Hintergrund.</summary>
    public const string BrushToggleOn = "Hp.Brush.ToggleOn";

    public const string BrushToggleThumb = "Hp.Brush.ToggleThumb";

    /// <summary>HDR an: Verlauf Akzent → Atmosphäre → Glanz.</summary>
    public const string BrushHdrOn = "Hp.Brush.HdrOn";

    public const string BrushHdrOff = "Hp.Brush.HdrOff";

    public const string BrushSuccess = "Hp.Brush.Success";

    public const string BrushWarning = "Hp.Brush.Warning";

    public const string BrushDanger = "Hp.Brush.Danger";

    /// <summary>Hinterlegung von Erfolgs-Kennzeichen (Prototyp .okc), Text darauf in <see cref="BrushSuccess"/>.</summary>
    public const string BrushSuccessSoft = "Hp.Brush.SuccessSoft";

    /// <summary>Hinterlegung von Warn-Kennzeichen und Hinweisen (Prototyp .wrn, .perf).</summary>
    public const string BrushWarningSoft = "Hp.Brush.WarningSoft";

    /// <summary>Hinterlegung von Fehler-Kennzeichen (Prototyp .bad).</summary>
    public const string BrushDangerSoft = "Hp.Brush.DangerSoft";

    public const string BrushSourceSteam = "Hp.Brush.Source.Steam";

    /// <summary>Epic: im hellen Thema dunkler (sonst auf Weiß unsichtbar).</summary>
    public const string BrushSourceEpic = "Hp.Brush.Source.Epic";

    public const string BrushSourceXbox = "Hp.Brush.Source.Xbox";

    public const string BrushSourceGog = "Hp.Brush.Source.Gog";

    public const string BrushSourceUbisoft = "Hp.Brush.Source.Ubisoft";

    public const string BrushSourceEA = "Hp.Brush.Source.EA";

    public const string BrushSourceBattleNet = "Hp.Brush.Source.BattleNet";

    public const string BrushSourceManual = "Hp.Brush.Source.Manual";

    /// <summary>Quiver Launcher (E91): Rosa, im hellen Thema dunkler (sonst auf Weiß zu blass).</summary>
    public const string BrushSourceQuiver = "Hp.Brush.Source.Quiver";

    // ---- Schriften (FontFamily) ----

    /// <summary>Titelschrift (Überschriften, Name statt Logo). Mitgelieferte Windows-Schrift, solange keine OFL-Schrift eingebettet ist.</summary>
    public const string FontDisplay = "Hp.Font.Display";

    /// <summary>Fließtext.</summary>
    public const string FontText = "Hp.Font.Text";

    /// <summary>Zahlen und Pfade (nits, EXE-Pfade).</summary>
    public const string FontMono = "Hp.Font.Mono";

    /// <summary>Alle Schlüssel (für Tests: jede Token-Datei und jede erzeugte Ressource enthält sie).</summary>
    public static IReadOnlyList<string> All { get; } = typeof(ThemeKeys)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    /// <summary>Schlüssel, deren Wert eine <see cref="System.Windows.Media.Color"/> ist (alle anderen: Pinsel oder Schrift).</summary>
    public static bool IsColor(string key) => key.StartsWith("Hp.Color.", StringComparison.Ordinal);

    /// <summary>Schlüssel, deren Wert eine <see cref="System.Windows.Media.FontFamily"/> ist.</summary>
    public static bool IsFont(string key) => key.StartsWith("Hp.Font.", StringComparison.Ordinal);
}
