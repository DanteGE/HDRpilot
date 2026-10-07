using System.Globalization;
using HdrPilot.App.Resources;
using HdrPilot.Core.Games;

namespace HdrPilot.App.ViewModels.Library;

/// <summary>Formatierte Texte der Spielebibliothek (alle Vorlagen aus LibraryStrings.resx).</summary>
public static class LibraryText
{
    public static string Format(string template, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, template, args);

    public static string Games(int count) =>
        count == 1 ? LibraryStrings.Count_GamesOne : Format(LibraryStrings.Count_GamesMany, count);

    public static string SourceName(GameSource source) => source switch
    {
        GameSource.Steam => LibraryStrings.Source_Steam,
        GameSource.Epic => LibraryStrings.Source_Epic,
        GameSource.Xbox => LibraryStrings.Source_Xbox,
        GameSource.Gog => LibraryStrings.Source_Gog,
        GameSource.Ubisoft => LibraryStrings.Source_Ubisoft,
        GameSource.EA => LibraryStrings.Source_EA,
        GameSource.BattleNet => LibraryStrings.Source_BattleNet,
        GameSource.Manual => LibraryStrings.Source_Manual,
        _ => source.ToString(),
    };
}
