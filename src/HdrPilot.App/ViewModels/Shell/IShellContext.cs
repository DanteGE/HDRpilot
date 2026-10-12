using HdrPilot.App.Services;
using HdrPilot.Core.AutoHdr;
using HdrPilot.Core.Config;
using HdrPilot.Core.Hdr;
using HdrPilot.Core.HdrSupport;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>
/// Schmale Sicht auf die App für die Einstellungen (Adapter auf AppServices in AppServices.Shell.cs). In Tests durch
/// eine Attrappe ersetzt: ConfigService über InMemoryFileSystem, Bildschirme aus FakeHdrController, Autostart über
/// FakeRegistry.
/// </summary>
public interface IShellContext
{
    ConfigService Config { get; }

    IAutostartService Autostart { get; }

    /// <summary>HDR-Liste von PCGamingWiki: Stand, „Jetzt abgleichen“, Konto.</summary>
    IHdrListService HdrList { get; }

    /// <summary>Windows Auto HDR (E88): „Auto-HDR-Einträge zurücknehmen“.</summary>
    IAutoHdrService AutoHdr { get; }

    /// <summary>Öffnet eine https-Adresse im Standardbrowser (Links zu PCGamingWiki).</summary>
    void OpenWebPage(string url);

    /// <summary>HDR-Zustand oder Bildschirme könnten sich geändert haben (auf dem UI-Thread). Beim Schließen abmelden.</summary>
    event EventHandler? HdrStateChanged;

    /// <summary>Alle aktiven Bildschirme (nur lesen, schaltet nichts).</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    void OpenLogFolder();

    /// <summary>Nach dem Speichern: Tray auffrischen; bei geänderten Suchordnern im Hintergrund neu einlesen.</summary>
    void OnSettingsSaved(bool searchFoldersChanged);
}

/// <summary>Ordnerauswahl (Dialog). In Tests eine Attrappe.</summary>
public interface IFolderPicker
{
    /// <summary>Gewählter Ordner oder null bei Abbruch.</summary>
    string? PickFolder(string? initialDirectory);
}
