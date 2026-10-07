using System.Diagnostics;
using System.Windows;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Bindungsfehler (falscher Pfad, falscher Typ) melden sich in WPF nur in der Ablaufverfolgung. Diese Tests hören
/// dort mit, bauen Einstellungen und Tray-Menü vollständig auf (Layout, Vorlagen, Listenzeilen, ohne Anzeige) und
/// verlangen eine leere Fehlerliste.
/// </summary>
public sealed class ShellBindingTests
{
    [Fact]
    public void Settings_window_has_no_binding_errors()
    {
        var (errors, displayRows, folderRows) = WpfTestHost.Run(() =>
        {
            using var recorder = new BindingErrorRecorder();
            var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c =>
            {
                c.Settings.SearchFolders = [@"D:\Spielordner", @"E:\DRM-frei"];
                c.Settings.TargetDisplays = [@"\\?\DISPLAY#DEL41A8#abc"];
            });
            var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));
            var window = new SettingsWindow();
            window.Attach(new SettingsViewModel(context, new FakeFolderPicker()));
            WpfTestHost.Layout(window, new Size(1080, 1400));
            WpfTestHost.DoEvents();

            // Nur aussagekräftig, wenn die Listenzeilen wirklich entstanden sind.
            var visuals = WpfTestHost.Visuals(window).ToList();
            var rows = (visuals.OfType<System.Windows.Controls.CheckBox>().Count(c => c.DataContext is DisplayOptionViewModel),
                visuals.OfType<System.Windows.Controls.ListBoxItem>().Count(i => i.DataContext is string));
            window.Close();
            return (recorder.Messages, rows.Item1, rows.Item2);
        });

        Assert.Equal(3, displayRows);
        Assert.Equal(2, folderRows);
        Assert.Empty(errors);
    }

    [Fact]
    public void Recorder_detects_a_broken_binding()
    {
        var errors = WpfTestHost.Run(() =>
        {
            using var recorder = new BindingErrorRecorder();
            var text = new System.Windows.Controls.TextBlock { DataContext = new DisplayOptionViewModel("p", "n", false, false, false, true, false) };
            text.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("GibtEsNicht"));
            WpfTestHost.Layout(text, new Size(100, 100));
            WpfTestHost.DoEvents();
            return recorder.Messages;
        });

        Assert.Contains(errors, e => e.Contains("GibtEsNicht", StringComparison.Ordinal));
    }

    [Fact]
    public void Tray_menu_has_no_binding_errors()
    {
        var errors = WpfTestHost.Run(() =>
        {
            using var recorder = new BindingErrorRecorder();
            var model = new TrayMenuViewModel(new FakeTrayHost(), new TrayState(true, true, true, ["Hades II"]), ex => throw ex);
            var menu = new TrayMenu { DataContext = model };
            WpfTestHost.Layout(menu, new Size(double.PositiveInfinity, double.PositiveInfinity));
            WpfTestHost.DoEvents();
            menu.DataContext = null;
            return recorder.Messages;
        });

        Assert.Empty(errors);
    }

    /// <summary>Sammelt Meldungen der WPF-Bindungs-Ablaufverfolgung (Warnung und Fehler).</summary>
    private sealed class BindingErrorRecorder : TraceListener
    {
        private readonly SourceLevels _previousLevel;
        private readonly List<string> _messages = [];

        public BindingErrorRecorder()
        {
            // Ohne Debugger schreibt WPF erst nach Refresh in die Ablaufverfolgung.
            PresentationTraceSources.Refresh();
            var source = PresentationTraceSources.DataBindingSource;
            _previousLevel = source.Switch.Level;
            source.Switch.Level = SourceLevels.Warning;
            source.Listeners.Add(this);
        }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                lock (_messages)
                {
                    _messages.Add(message);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            var source = PresentationTraceSources.DataBindingSource;
            source.Listeners.Remove(this);
            source.Switch.Level = _previousLevel;
            base.Dispose(disposing);
        }
    }
}
