using HdrPilot.App.Infrastructure;
using HdrPilot.App.Resources;

namespace HdrPilot.App.ViewModels.Shell;

/// <summary>Ein Bildschirm in der Auswahl „zu schaltende Bildschirme“.</summary>
public sealed class DisplayOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _hdrEnabled;
    private bool _hdrSupported;
    private bool _isConnected;
    private bool _isPrimary;
    private string _name;

    public DisplayOptionViewModel(string path, string name, bool isPrimary, bool hdrSupported, bool hdrEnabled, bool isConnected, bool isSelected)
    {
        Path = path;
        _name = name;
        _isPrimary = isPrimary;
        _hdrSupported = hdrSupported;
        _hdrEnabled = hdrEnabled;
        _isConnected = isConnected;
        _isSelected = isSelected;
    }

    /// <summary>monitorDevicePath (dauerhafter Schlüssel).</summary>
    public string Path { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool IsPrimary
    {
        get => _isPrimary;
        set => SetProperty(ref _isPrimary, value);
    }

    public bool HdrSupported
    {
        get => _hdrSupported;
        set => SetProperty(ref _hdrSupported, value);
    }

    public bool HdrEnabled
    {
        get => _hdrEnabled;
        set => SetProperty(ref _hdrEnabled, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Kurzname aus dem monitorDevicePath (z. B. „MSI3DD0“), wenn der Bildschirm nicht verbunden ist.</summary>
    public static string NameFromPath(string path)
    {
        var parts = path.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : ShellStrings.DisplayUnknownName;
    }
}
