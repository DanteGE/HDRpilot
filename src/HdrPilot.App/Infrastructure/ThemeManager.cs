using System.Windows;
using HdrPilot.Core.Abstractions;
using Microsoft.Win32;

namespace HdrPilot.App.Infrastructure;

/// <summary>
/// Hell oder dunkel nach der Windows-Einstellung „App-Modus“ (AppsUseLightTheme). Tauscht die Token-Ressourcen
/// zur Laufzeit; die Standard-Steuerelemente folgen über Application.ThemeMode="System" (Fluent).
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Uri DarkUri = new("pack://application:,,,/HDRpilot;component/Themes/Tokens.Dark.xaml", UriKind.Absolute);
    private static readonly Uri LightUri = new("pack://application:,,,/HDRpilot;component/Themes/Tokens.Light.xaml", UriKind.Absolute);
    private static ResourceDictionary? _current;
    private static IRegistry _registry = WindowsRegistry.Instance;

    public static event EventHandler? ThemeChanged;

    public static bool IsDark { get; private set; } = true;

    /// <summary>Erzwingt hell oder dunkel (Tests, Bildschirmfotos). Null: <see cref="Preference"/>. Hat immer Vorrang.</summary>
    public static bool? Override { get; set; }

    /// <summary>
    /// Wahl des Nutzers in „Darstellung → Thema“ (E89): true dunkel, false hell, null automatisch (Windows-Einstellung).
    /// Gesetzt von der Darstellung; <see cref="Override"/> geht vor.
    /// </summary>
    public static bool? Preference { get; set; }

    public static void Initialize(Application app, IRegistry? registry = null)
    {
        _registry = registry ?? WindowsRegistry.Instance;
        Apply(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
            {
                app.Dispatcher.BeginInvoke(() => Apply(app));
            }
        };
    }

    public static bool ReadSystemPrefersDark()
    {
        using var key = _registry.OpenKey(RegistryHiveKind.CurrentUser, PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    public static void Apply(Application app)
    {
        var dark = Override ?? Preference ?? ReadSystemPrefersDark();
        if (_current is not null && dark == IsDark)
        {
            return;
        }

        var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        var merged = app.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        merged.Insert(0, next);
        _current = next;
        IsDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
