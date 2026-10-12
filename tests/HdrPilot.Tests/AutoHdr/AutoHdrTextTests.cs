using HdrPilot.Core.AutoHdr;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Format des globalen Werts DirectXUserGlobalSettings in UserGpuPreferences (E88): nur saubere REG_SZ werden zerlegt,
/// nur das Token AutoHDREnable wird gesetzt, alles andere bleibt byte-gleich; Zurückstellen nach R1 (exakt), R2 (nur das
/// Token) und R3 (stehen lassen).
/// </summary>
public sealed class AutoHdrTextTests
{
    private const string On = GpuPreferenceText.GlobalOn;

    [Theory]
    [InlineData("AutoHDREnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("AutoHDREnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=1")]
    [InlineData("SwapEffectUpgradeEnable=1;AutoHDREnable=0;VRROptimizeEnable=0;")]
    [InlineData("autohdrenable=0;")]
    [InlineData("Key_1=a.b-c;Other=ä;")]
    public void Clean_values_round_trip_byte_for_byte(string text)
    {
        Assert.True(GpuPreferenceText.TryParse(GpuPreferenceValue.FromString(text), out var tokens));
        Assert.Equal(text, tokens.ToString());
        Assert.Equal(text.EndsWith(';'), tokens.TrailingSeparator);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(";")]
    [InlineData(";;")]
    [InlineData("A=1;;B=2;")]
    [InlineData("A=1;a=2;")]
    [InlineData("AutoHDREnable=0;\0xy")]
    [InlineData(" A=1;")]
    [InlineData("A=1 ;")]
    [InlineData("A=1;\n")]
    [InlineData("=1;")]
    [InlineData("A=;")]
    [InlineData("A=1=2;")]
    [InlineData("A-B=1;")]
    [InlineData("A=1;B")]
    public void Unclean_strings_are_never_parsed_or_merged(string text)
    {
        var value = GpuPreferenceValue.FromString(text);
        Assert.False(GpuPreferenceText.TryParse(value, out _));
        Assert.Equal(GpuPreferenceMergeKind.Unsupported, GpuPreferenceText.SetToken(value, On).Kind);
    }

    [Theory]
    [InlineData(GpuPreferenceValueKind.DWord, "1")]
    [InlineData(GpuPreferenceValueKind.QWord, "1")]
    [InlineData(GpuPreferenceValueKind.ExpandString, "AutoHDREnable=1;")]
    [InlineData(GpuPreferenceValueKind.MultiString, null)]
    [InlineData(GpuPreferenceValueKind.Binary, null)]
    [InlineData(GpuPreferenceValueKind.Other, null)]
    public void Other_kinds_are_never_touched_even_with_a_clean_looking_text(GpuPreferenceValueKind kind, string? text)
    {
        var value = new GpuPreferenceValue(kind, text);
        Assert.False(GpuPreferenceText.IsClean(value));
        Assert.Equal(GpuPreferenceMergeKind.Unsupported, GpuPreferenceText.SetToken(value, On).Kind);
        Assert.Equal(GpuPreferenceRestoreRule.External, GpuPreferenceText.DecideRestore(value, GpuPreferenceValue.Missing, null, "AutoHDREnable=1;", On).Rule);
    }

    [Theory]
    [InlineData(null, "AutoHDREnable=1;", null)] // fehlte
    [InlineData("SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable=1;AutoHDREnable=1;", null)] // angehängt
    [InlineData("AutoHDREnable=0;SwapEffectUpgradeEnable=1;", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;", "0")] // wie beim Nutzer
    [InlineData("SwapEffectUpgradeEnable=1;AutoHDREnable=0;VRROptimizeEnable=0;", "SwapEffectUpgradeEnable=1;AutoHDREnable=1;VRROptimizeEnable=0;", "0")] // mitten drin
    [InlineData("SwapEffectUpgradeEnable=1", "SwapEffectUpgradeEnable=1;AutoHDREnable=1", null)] // ohne Abschluss bleibt ohne
    [InlineData("autohdrenable=0;", "autohdrenable=1;", "0")] // Schreibweise bleibt
    public void Setting_the_token_changes_only_it_and_restore_gives_back_the_exact_original(string? before, string written, string? originalToken)
    {
        var original = before is null ? GpuPreferenceValue.Missing : GpuPreferenceValue.FromString(before);

        var merge = GpuPreferenceText.SetToken(original, On);

        Assert.Equal(GpuPreferenceMergeKind.Write, merge.Kind);
        Assert.Equal(written, merge.Written);
        Assert.Equal(originalToken, merge.OriginalToken);

        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(written), original, merge.OriginalToken, written, On);
        Assert.Equal(GpuPreferenceRestoreRule.Exact, restore.Rule);
        Assert.Equal(before is null, restore.Delete);
        Assert.Equal(before, restore.Text);
    }

    [Theory]
    [InlineData("AutoHDREnable=1;", "AutoHDREnable=0;")]
    [InlineData("autoHDRenable=1", "autoHDRenable=0")]
    [InlineData("AutoHDREnable=1;SwapEffectUpgradeEnable=1;", "AutoHDREnable=0;SwapEffectUpgradeEnable=1;")]
    public void Token_already_set_needs_no_write(string text, string switchedOff)
    {
        var merge = GpuPreferenceText.SetToken(GpuPreferenceValue.FromString(text), On);
        Assert.Equal(GpuPreferenceMergeKind.Unchanged, merge.Kind);
        Assert.Null(merge.Written);

        // Gegenprobe: ausschalten schreibt 0 an den Platz des Tokens, alles andere bleibt.
        var off = GpuPreferenceText.SetToken(GpuPreferenceValue.FromString(text), GpuPreferenceText.GlobalOff);
        Assert.Equal(GpuPreferenceMergeKind.Write, off.Kind);
        Assert.Equal(switchedOff, off.Written);
        Assert.Equal("1", off.OriginalToken);
    }

    [Theory]
    [InlineData(null, "AutoHDREnable=1;", "AutoHDREnable=1;VRROptimizeEnable=1;", "VRROptimizeEnable=1;")]
    [InlineData("AutoHDREnable=0;SwapEffectUpgradeEnable=1;", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;", "AutoHDREnable=1;SwapEffectUpgradeEnable=0;", "AutoHDREnable=0;SwapEffectUpgradeEnable=0;")]
    public void Other_changes_stay_and_only_the_token_is_taken_back(string? before, string written, string now, string expected)
    {
        var original = before is null ? GpuPreferenceValue.Missing : GpuPreferenceValue.FromString(before);
        var token = GpuPreferenceText.SetToken(original, On).OriginalToken;

        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(now), original, token, written, On);

        Assert.Equal(GpuPreferenceRestoreRule.TokenOnly, restore.Rule);
        Assert.False(restore.Delete);
        Assert.Equal(expected, restore.Text);
    }

    [Theory]
    [InlineData(null, "AutoHDREnable=1;", "AutoHDREnable=1")] // Windows schrieb den eigenen Text neu (nur Abschluss weg)
    [InlineData("SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable=1;AutoHDREnable=1;", "AutoHDREnable=1;")] // anderer Eintrag extern entfernt
    public void Token_only_restore_deletes_when_nothing_is_left(string? before, string written, string now)
    {
        // R2: nur das Token zurück, alles andere wie jetzt. Bleibt nichts, wird gelöscht, auch wenn der Wert vorher da war:
        // ein extern entfernter Eintrag kommt nicht über das Original zurück.
        var original = before is null ? GpuPreferenceValue.Missing : GpuPreferenceValue.FromString(before);
        var token = GpuPreferenceText.SetToken(original, On).OriginalToken;

        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(now), original, token, written, On);

        Assert.Equal(GpuPreferenceRestoreRule.TokenOnly, restore.Rule);
        Assert.True(restore.Delete);
        Assert.Null(restore.Text);
    }

    [Theory]
    [InlineData("AutoHDREnable=0;SwapEffectUpgradeEnable=1;")] // Nutzer schaltete Auto HDR in Windows aus
    [InlineData("SwapEffectUpgradeEnable=1;")] // Token entfernt
    [InlineData("0")] // nicht mehr sauber
    [InlineData("AutoHDREnable=1;\0x")]
    public void Externally_changed_values_stay_untouched(string now)
    {
        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(now), GpuPreferenceValue.Missing, null, "AutoHDREnable=1;", On);
        Assert.Equal(GpuPreferenceRestoreRule.External, restore.Rule);
        Assert.True(restore.LeaveAsIs);
    }

    [Fact]
    public void Deleted_or_retyped_values_stay_as_they_are()
    {
        foreach (var now in new[] { GpuPreferenceValue.Missing, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "1") })
        {
            var restore = GpuPreferenceText.DecideRestore(
                now, GpuPreferenceValue.FromString("AutoHDREnable=0;"), "0", "AutoHDREnable=1;", On);
            Assert.True(restore.LeaveAsIs);
        }
    }

    [Fact]
    public void Keys_compare_without_case_and_keep_their_spelling()
    {
        Assert.True(GpuPreferenceText.TryParse("AutoHdrEnable=1;SwapEffectUpgradeEnable=1;", out var tokens));
        Assert.Equal("1", tokens.Get("AUTOHDRENABLE"));
        Assert.Equal("AutoHdrEnable=5;SwapEffectUpgradeEnable=1;", tokens.With("autohdrenable", "5").ToString());
        Assert.Equal("SwapEffectUpgradeEnable=1;", tokens.Without("AUTOHDRENABLE").ToString());
        Assert.Null(tokens.Get("VRROptimizeEnable"));
        Assert.Equal("AutoHdrEnable=1;SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;", tokens.With("VRROptimizeEnable", "0").ToString());
    }

    [Fact]
    public void Same_value_compares_kind_and_text_exactly()
    {
        Assert.True(GpuPreferenceText.SameValue(GpuPreferenceValue.Missing, GpuPreferenceValue.Missing));
        Assert.True(GpuPreferenceText.SameValue(GpuPreferenceValue.FromString("A=1;"), GpuPreferenceValue.FromString("A=1;")));
        Assert.False(GpuPreferenceText.SameValue(GpuPreferenceValue.FromString("A=1;"), GpuPreferenceValue.FromString("a=1;")));
        Assert.False(GpuPreferenceText.SameValue(GpuPreferenceValue.FromString("A=1;"), GpuPreferenceValue.Missing));
        Assert.False(GpuPreferenceText.SameValue(new GpuPreferenceValue(GpuPreferenceValueKind.ExpandString, "A=1;"), GpuPreferenceValue.FromString("A=1;")));
    }
}
