using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HdrPilot.App.Resources;
using HdrPilot.App.ViewModels;
using HdrPilot.App.Views.Shell;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Karte „SteamGridDB (online)“ der Einstellungen (E93, G14.5): Schlüsselfeld und „Prüfen“ auch bei ausgeschaltetem Schalter,
/// der Schalter in eigener Zeile mit sichtbarer Beschriftung, Sprung zum Schlüsselfeld aus „Bild wählen…“. Auf dem
/// WPF-Testhost angeordnet, nie angezeigt.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class SettingsSteamGridDbWindowTests
{
    [Fact]
    public void Key_field_and_check_are_enabled_with_the_switch_off()
    {
        WpfTestHost.Run(() =>
        {
            var window = SettingsLayoutTests.CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(1280, 800));

            Assert.False(((CheckBox)window.FindName("SteamGridDbToggle")).IsChecked == true);
            Assert.True(((FrameworkElement)window.FindName("KeyField")).IsEnabled);
            Assert.True(((PasswordBox)window.FindName("SteamGridDbKey")).IsEnabled);
            Assert.True(((Button)window.FindName("SteamGridDbCheckButton")).IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public void The_switch_has_a_visible_label_row_of_its_own()
    {
        WpfTestHost.Run(() =>
        {
            var window = SettingsLayoutTests.CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(1280, 800));
            var row = (DockPanel)window.FindName("SteamGridDbAutoRow");
            var toggle = (CheckBox)window.FindName("SteamGridDbToggle");
            var label = (TextBlock)window.FindName("SteamGridDbAutoLabel");
            var check = (Button)window.FindName("SteamGridDbCheckButton");

            Assert.Same(row, toggle.Parent);
            Assert.Same(row, label.Parent);
            Assert.Equal(ShellStrings.SteamGridDbAutoLabel, label.Text);
            Assert.Equal(Visibility.Visible, label.Visibility);
            Assert.Equal(ShellStrings.SteamGridDbToggleName, AutomationProperties.GetName(toggle));
            Assert.Equal("SteamGridDbToggle", AutomationProperties.GetAutomationId(toggle));

            // Die Zeile steht unter Schlüsselfeld, „Prüfen“ und Statuszeile; der Kopf hat keinen Schalter mehr.
            var card = (FrameworkElement)window.FindName("SteamGridDbCard");
            Assert.True(row.TranslatePoint(default, card).Y > check.TranslatePoint(default, card).Y);
            Assert.Single(WpfTestHost.Visuals(card).OfType<CheckBox>());
            window.Close();
        });
    }

    [Fact]
    public void Show_target_artwork_scrolls_to_the_section_and_focuses_the_key_field()
    {
        WpfTestHost.Run(() =>
        {
            var window = SettingsLayoutTests.CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(1280, 800));
            Assert.NotEqual("NavAppearanceArtwork", window.CurrentSection);

            window.ShowTarget(SettingsTarget.Artwork);
            WpfTestHost.Layout(window, new Size(1280, 800));
            WpfTestHost.DoEvents();

            Assert.Equal("NavAppearanceArtwork", window.CurrentSection);
            var key = (PasswordBox)window.FindName("SteamGridDbKey");
            Assert.True(key.IsFocused || System.Windows.Input.FocusManager.GetFocusedElement(window) == key);
            window.Close();
        });
    }

    [Fact]
    public void Show_target_writes_the_test_log_line_only_with_a_test_log()
    {
        WpfTestHost.Run(() =>
        {
            var lines = new List<string>();
            var window = SettingsLayoutTests.CreateWindow(out _);
            WpfTestHost.Layout(window, new Size(1280, 800));

            window.ShowTarget(SettingsTarget.Artwork); // ohne Testprotokoll: nichts
            window.TestLog = lines.Add;
            window.ShowTarget(SettingsTarget.Start); // anderes Ziel: nichts
            window.ShowTarget(SettingsTarget.Artwork);

            Assert.Equal(["Prüfung: Einstellungen bei SectionArtwork, Fokus SteamGridDbKey"], lines);
            window.Close();
        });
    }
}
