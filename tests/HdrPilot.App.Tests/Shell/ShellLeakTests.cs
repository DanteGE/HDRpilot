using System.Runtime.CompilerServices;
using System.Windows;
using HdrPilot.App.Services;
using HdrPilot.App.ViewModels.Shell;
using HdrPilot.App.Views.Shell;
using HdrPilot.Tests.TestSupport;

namespace HdrPilot.App.Tests.Shell;

/// <summary>
/// Nach dem Schließen darf nichts mehr auf Fenster oder ViewModel verweisen (keine statischen Ereignisse, keine
/// Abos an der App), sonst bliebe die Oberfläche im Tray-Betrieb im Speicher.
/// </summary>
[Collection(ShellSerialCollection.Name)]
public sealed class ShellLeakTests
{
    [Fact]
    public void Closed_settings_window_and_view_model_are_collected()
    {
        var config = ShellTestFactory.NewConfig(new InMemoryFileSystem(), c => c.Settings.SearchFolders = [@"D:\Spielordner"]);
        var context = new FakeShellContext(config, ShellTestFactory.TwoDisplays(), new ConfigOnlyAutostartService(config));

        var (window, model) = WpfTestHost.Run(() => OpenAndClose(context));
        WpfTestHost.DoEvents();
        Collect();

        Assert.False(window.IsAlive, "Einstellungen-Fenster bleibt im Speicher");
        Assert.False(model.IsAlive, "SettingsViewModel bleibt im Speicher");
        Assert.Equal(0, context.Subscribers);
    }

    [Fact]
    public void Ambient_animation_releases_the_window_when_it_closes()
    {
        var (window, started) = WpfTestHost.Run(CreateAmbient);
        WpfTestHost.DoEvents();
        Collect();

        Assert.False(started, "Nie angezeigtes, inaktives Fenster darf keine Dauer-Animation starten");
        Assert.False(window.IsAlive, "AmbientAnimation hält das Fenster über das statische Ereignis fest");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Window, WeakReference Model) OpenAndClose(FakeShellContext context)
    {
        var window = new SettingsWindow();
        var model = new SettingsViewModel(context, new FakeFolderPicker());
        window.Attach(model);
        WpfTestHost.Layout(window, new Size(1080, 900));
        model.GracePeriodSeconds = 30;
        window.Close();
        return (new WeakReference(window), new WeakReference(model));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Window, bool Started) CreateAmbient()
    {
        var window = new Window();
        var started = false;
        _ = new AmbientAnimation(window, () => started = true, () => { });

        // Einstellung „Animationseffekte“ wechselt: das Fenster ist weder sichtbar noch aktiv, also weiter nichts.
        ShellMotion.Override = true;
        ShellMotion.Override = null;
        window.Close();
        return (new WeakReference(window), started);
    }

    private static void Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
