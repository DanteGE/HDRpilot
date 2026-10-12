using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using HdrPilot.App.Resources;
using HdrPilot.App.Services;
using HdrPilot.App.Tests.Library;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Einstellungen und „Windows Auto HDR“ (E88, nach dem Livetest G9.6): Der globale Schalter um bekannte Starts ist der
/// Standard, keine Einstellung; HDRpilot schreibt nichts Dauerhaftes, also gibt es weder die frühere Notlösung noch
/// „Auto-HDR-Einträge zurücknehmen“. Geschaltet wird je Spiel in der Bibliothek.
/// </summary>
public sealed class AutoHdrSettingsTests
{
    [Fact]
    public void Settings_window_has_no_auto_hdr_section_switch_or_button()
    {
        WpfTestHost.Run(() =>
        {
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem());
            var ctx = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new SettingsWindow();
            window.Attach(new SettingsViewModel(ctx, new FakeFolderPicker()));
            WpfTestHost.Layout(window, new Size(1080, 2400));

            Assert.Null(window.FindName("AutoHdrSection"));
            var ids = Logical(window).OfType<FrameworkElement>().Select(AutomationProperties.GetAutomationId).Where(id => !string.IsNullOrEmpty(id)).ToList();
            Assert.DoesNotContain(ids, id => id.Contains("AutoHdr", StringComparison.Ordinal));
            Assert.Contains("SaveSettingsButton", ids); // Gegenprobe: die Suche findet AutomationIds
            window.Close();
        });
    }

    [Fact]
    public void Saving_the_settings_writes_no_fallback_setting_and_drops_one_from_a_preview()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile(ShellTestFactory.ConfigPath, """{ "version": 1, "settings": { "autoHdrGlobalFallback": true }, "games": [] }""");
        var config = ShellTestFactory.NewConfig(fs);
        var vm = new SettingsViewModel(new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config)), new FakeFolderPicker());
        Assert.False(vm.HasChanges);

        vm.GracePeriodSeconds = 12;
        Assert.True(vm.Save());

        var text = fs.ReadAllText(ShellTestFactory.ConfigPath);
        Assert.Contains("\"gracePeriodSeconds\": 12", text, StringComparison.Ordinal);
        Assert.DoesNotContain("autoHdrGlobalFallback", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_texts_hold_no_auto_hdr_fallback_or_rollback_entries()
    {
        var keys = ShellStrings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!
            .Cast<DictionaryEntry>().Select(e => (string)e.Key).ToList();

        Assert.DoesNotContain(keys, k => k.Contains("AutoHdr", StringComparison.Ordinal));
        Assert.Contains("SectionStartup", keys); // Gegenprobe: die Liste ist nicht leer
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in Logical(child))
            {
                yield return descendant;
            }
        }
    }
}
