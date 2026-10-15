using System.Windows;
using HdrPilot.App.Infrastructure;

namespace HdrPilot.App.Tests;

/// <summary>Bildschirmfotos in festen Größen, Zuständen und Themen (E89, Sichtprüfung von G10.5 und G10.8).</summary>
public sealed class SnapshotPlanTests
{
    private static readonly Func<Window> NoWindow = () => throw new InvalidOperationException("nicht gebraucht");

    [Fact]
    public void Without_options_everything_is_rendered_dark_and_light_in_start_size()
    {
        var plan = SnapshotPlan.Parse(null, null, null);

        Assert.Empty(plan.Sizes);
        Assert.Empty(plan.Scenes);
        Assert.Equal([true, false], plan.Themes);
        Assert.True(plan.Includes(new SnapshotScene("hauptfenster", "liste", NoWindow)));
    }

    [Fact]
    public void Sizes_scenes_and_themes_are_parsed()
    {
        var plan = SnapshotPlan.Parse(" 1280x800, 1920X1080 ;3440×1440", "hauptfenster-raster, einstellungen", "hell");

        Assert.Equal([new SnapshotSize(1280, 800), new SnapshotSize(1920, 1080), new SnapshotSize(3440, 1440)], plan.Sizes);
        Assert.Equal([false], plan.Themes);
        Assert.True(plan.Includes(new SnapshotScene("hauptfenster", "raster", NoWindow)));
        Assert.True(plan.Includes(new SnapshotScene("einstellungen", "darstellung", NoWindow)));
        Assert.False(plan.Includes(new SnapshotScene("hauptfenster", "regal", NoWindow)));
    }

    [Fact]
    public void Review_means_the_three_sizes_of_phase_10()
    {
        Assert.Equal(SnapshotSize.Review, SnapshotPlan.Parse("review", null, null).Sizes);
        Assert.Equal([new SnapshotSize(1280, 800), new SnapshotSize(1920, 1080), new SnapshotSize(3440, 1440)], SnapshotSize.Review);
    }

    [Theory]
    [InlineData("1920", null)]
    [InlineData("100x100", null)]
    [InlineData("1920x1080x2", null)]
    [InlineData("abcxdef", null)]
    [InlineData(null, "grau")]
    public void Invalid_options_are_rejected(string? sizes, string? themes)
    {
        Assert.Throws<FormatException>(() => SnapshotPlan.Parse(sizes, null, themes));
    }

    [Fact]
    public void File_names_carry_scene_size_and_theme()
    {
        var scene = new SnapshotScene("hauptfenster", "raster", NoWindow);

        Assert.Equal("hauptfenster-raster-3440x1440-dunkel.png", SnapshotRenderer.FileName(scene, new SnapshotSize(3440, 1440), dark: true));
        Assert.Equal("hauptfenster-raster-hell.png", SnapshotRenderer.FileName(scene, null, dark: false));
    }
}
