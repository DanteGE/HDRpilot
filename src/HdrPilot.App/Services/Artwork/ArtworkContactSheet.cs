using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;

namespace HdrPilot.App.Services.Artwork;

/// <summary>
/// Entwicklung (G10.3, Bericht <c>--artwork-report</c>): Kontaktbogen der Ersatzbilder zur Sichtprüfung – je Beispielspiel
/// Titelbild, Cover, Kopfbild und Symbol, wie die Oberfläche sie bekommt. Nur in den Ordner des Berichts (eigene Ablage);
/// enthält Spielbilder und gehört daher nie ins Repository.
/// </summary>
public static class ArtworkContactSheet
{
    private const int RowHeight = 220;
    private const int Width = 1600;

    public static async Task WriteAsync(ArtworkService artwork, IReadOnlyList<GameConfig> games, IReadOnlyList<FallbackExample> examples, string path)
    {
        // Beispiele der Ersatzbilder, dazu weitere Spiele mit erzeugtem Titelbild (verschiedene Launcher) und zwei mit Launcher-Bildern.
        var chosen = examples.Select(e => games.FirstOrDefault(g => string.Equals(g.Key, e.GameKey, StringComparison.OrdinalIgnoreCase)))
            .OfType<GameConfig>()
            .ToList();
        foreach (var game in games.Where(g => g.Source != Core.Games.GameSource.Steam).OrderBy(g => g.Source).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (chosen.Count >= 12)
            {
                break;
            }

            if (!chosen.Contains(game))
            {
                chosen.Add(game);
            }
        }

        foreach (var game in games.Where(g => g.LastPlayed is not null).OrderByDescending(g => g.LastPlayed).Take(2))
        {
            if (!chosen.Contains(game))
            {
                chosen.Add(game);
            }
        }

        var rows = new List<(GameConfig Game, GameArtwork Art, ImageSource? Header)>();
        foreach (var game in chosen)
        {
            var art = await artwork.GetArtworkAsync(game, 1920);
            var header = await artwork.GetImageAsync(game, ArtworkKind.Header, 460);
            rows.Add((game, art, header));
        }

        var sheet = ArtworkService.Sta.Run(() => Draw(rows));
        ArtworkImaging.Save(sheet, path);
    }

    private static BitmapSource Draw(IReadOnlyList<(GameConfig Game, GameArtwork Art, ImageSource? Header)> rows)
    {
        var height = Math.Max(1, rows.Count) * RowHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 13, 18)), null, new Rect(0, 0, Width, height));
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            for (var i = 0; i < rows.Count; i++)
            {
                var (game, art, header) = rows[i];
                var y = (i * RowHeight) + 10;
                if (art.Hero?.Image is { } hero)
                {
                    dc.DrawImage(hero, new Rect(10, y, 620, 200));
                }

                if (art.Cover?.Image is { } cover)
                {
                    dc.DrawImage(cover, new Rect(640, y, 133, 200));
                }

                if (header is not null)
                {
                    dc.DrawImage(header, new Rect(783, y, 214, 100));
                }

                if (art.Icon?.Image is { } icon)
                {
                    dc.DrawImage(icon, new Rect(783, y + 110, 90, 90));
                }

                if (art.Logo?.Image is BitmapSource logo)
                {
                    var scale = Math.Min(200.0 / logo.PixelWidth, 90.0 / logo.PixelHeight);
                    dc.DrawImage(logo, new Rect(883, y + 110, logo.PixelWidth * scale, logo.PixelHeight * scale));
                }

                if (art.Field?.Image is { } field)
                {
                    dc.DrawImage(field, new Rect(1007, y, 120, 45));
                }

                var text = new FormattedText(
                    game.Name + "\n" + game.Source + " · " + art.Summary.Description + "\nTitelbild " + art.Hero?.Origin + " · Logo " + (art.Logo?.Origin.ToString() ?? "Name") + " · Cover " + art.Cover?.Origin + (art.Summary.CoverFromSmear ? " (verschmiert)" : string.Empty) + " · Symbol " + art.Icon?.Origin + " " + art.Icon?.PixelWidth + " px\nFarben " + art.Ambient.Deep + " " + art.Ambient.Glow + " " + art.Ambient.Shine + " · " + art.Ambient.Dullness + " · Licht " + art.Ambient.LightX.ToString("0.00", CultureInfo.InvariantCulture),
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    13,
                    Brushes.White,
                    1.0)
                { MaxTextWidth = 460 };
                dc.DrawText(text, new Point(1137, y + 55));
                foreach (var (hex, x) in new[] { (art.Ambient.Deep, 1137.0), (art.Ambient.Glow, 1167.0), (art.Ambient.Shine, 1197.0) })
                {
                    var (r, g, b) = PixelAnalysis.HexToRgb(hex);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)), null, new Rect(x, y, 26, 40));
                }
            }
        }

        var target = new RenderTargetBitmap(Width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
