using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace HdrPilot.App.Views.Shell;

/// <summary>
/// Gruppen mit eigener Kennung für die UI-Automation (Prüfskripte, Bildschirmleser): WPF gibt Rahmen und Panels sonst keinen
/// Eintrag im Automationsbaum, ihre Kennung wäre unsichtbar. Darstellung und Verhalten wie Border, Grid, StackPanel bzw.
/// HeaderedContentControl.
/// </summary>
public sealed class GroupPeer(FrameworkElement owner, string className) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => className;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;
}

/// <summary>Rahmen als Gruppe im Automationsbaum.</summary>
public sealed class GroupBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this, nameof(GroupBorder));
}

/// <summary>Raster als Gruppe im Automationsbaum.</summary>
public sealed class GroupGrid : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this, nameof(GroupGrid));
}

/// <summary>Stapel als Gruppe im Automationsbaum (Navigation).</summary>
public sealed class GroupStackPanel : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this, nameof(GroupStackPanel));
}

/// <summary>Bereichskarte der Einstellungen (Stil Hp.Shell.Section) als Gruppe mit Kennung und Namen.</summary>
public sealed class SettingsCard : HeaderedContentControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this, nameof(SettingsCard));
}
