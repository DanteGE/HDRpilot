using HdrPilot.Core.AutoHdr;

namespace HdrPilot.Tests.AutoHdr;

/// <summary>
/// Format der Werte in UserGpuPreferences (E88): nur saubere REG_SZ werden zerlegt, nur das Token AutoHDREnable wird
/// gesetzt, alles andere bleibt byte-gleich; Zurücknehmen nach R1 (exakt), R2 (nur das Token) und R3 (stehen lassen).
/// </summary>
public sealed class AutoHdrRegistryTextTests
{
    private const string On = GpuPreferenceText.PerGameOn;

    [Theory]
    [InlineData("GpuPreference=2;")]
    [InlineData("AutoHDREnable=2097;")]
    [InlineData("GpuPreference=2")]
    [InlineData("SwapEffectUpgradeEnable=1;AutoHDREnable=2096;GpuPreference=0;")]
    [InlineData("AppStatus=4096;")]
    [InlineData("autohdrenable=0;")]
    [InlineData("Key_1=a.b-c;Other=ä;")]
    public void Clean_values_round_trip_byte_for_byte(string text)
    {
        Assert.True(GpuPreferenceText.TryParse(GpuPreferenceValue.FromString(text), out var tokens));
        Assert.Equal(text, tokens.ToString());
        Assert.Equal(text.EndsWith(';'), tokens.TrailingSeparator);
    }

    [Theory]
    [InlineData("0")] // ScopeX.exe
    [InlineData("")]
    [InlineData(";")]
    [InlineData(";;")]
    [InlineData("A=1;;B=2;")]
    [InlineData("A=1;a=2;")]
    [InlineData("GpuPreference=0;\0xy")]
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
        Assert.Equal(GpuPreferenceMergeKind.Unsupported, GpuPreferenceText.MergeOn(value, On).Kind);
    }

    [Theory]
    [InlineData(GpuPreferenceValueKind.DWord, "1")]
    [InlineData(GpuPreferenceValueKind.QWord, "1")]
    [InlineData(GpuPreferenceValueKind.ExpandString, "AutoHDREnable=2097;")]
    [InlineData(GpuPreferenceValueKind.MultiString, null)]
    [InlineData(GpuPreferenceValueKind.Binary, null)]
    [InlineData(GpuPreferenceValueKind.Other, null)]
    public void Other_kinds_are_never_touched_even_with_a_clean_looking_text(GpuPreferenceValueKind kind, string? text)
    {
        var value = new GpuPreferenceValue(kind, text);
        Assert.False(GpuPreferenceText.IsClean(value));
        Assert.Equal(GpuPreferenceMergeKind.Unsupported, GpuPreferenceText.MergeOn(value, On).Kind);
        Assert.Equal(GpuPreferenceRestoreRule.External, GpuPreferenceText.DecideRestore(value, GpuPreferenceValue.Missing, null, "AutoHDREnable=2097;", On).Rule);
    }

    [Theory]
    [InlineData(null, "AutoHDREnable=2097;", null)] // 1: fehlte
    [InlineData("GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;", null)] // 2: angehängt
    [InlineData("AutoHDREnable=2096;", "AutoHDREnable=2097;", "2096")] // 3: ersetzt
    [InlineData("SwapEffectUpgradeEnable=1;AutoHDREnable=2096;GpuPreference=0;", "SwapEffectUpgradeEnable=1;AutoHDREnable=2097;GpuPreference=0;", "2096")] // 5: mitten drin
    [InlineData("GpuPreference=2", "GpuPreference=2;AutoHDREnable=2097", null)] // 6: ohne Abschluss bleibt ohne
    [InlineData("autohdrenable=0;", "autohdrenable=2097;", "0")] // 7: Schreibweise bleibt
    public void Merge_sets_only_the_token_and_restore_gives_back_the_exact_original(string? before, string written, string? originalToken)
    {
        var original = before is null ? GpuPreferenceValue.Missing : GpuPreferenceValue.FromString(before);

        var merge = GpuPreferenceText.MergeOn(original, On);

        Assert.Equal(GpuPreferenceMergeKind.Write, merge.Kind);
        Assert.Equal(written, merge.Written);
        Assert.Equal(originalToken, merge.OriginalToken);

        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(written), original, merge.OriginalToken, written, On);
        Assert.Equal(GpuPreferenceRestoreRule.Exact, restore.Rule);
        Assert.Equal(before is null, restore.Delete);
        Assert.Equal(before, restore.Text);
    }

    [Theory]
    [InlineData("AutoHDREnable=2097;")] // 4
    [InlineData("autoHDRenable=2097")]
    [InlineData("GpuPreference=2;AutoHDREnable=2097;AppStatus=0;")]
    public void Token_already_on_needs_no_write(string text)
    {
        var merge = GpuPreferenceText.MergeOn(GpuPreferenceValue.FromString(text), On);
        Assert.Equal(GpuPreferenceMergeKind.AlreadyOn, merge.Kind);
        Assert.Null(merge.Written);

        // Gegenprobe: ein anderer Wert des Tokens ist nicht „schon an“.
        Assert.Equal(GpuPreferenceMergeKind.Write, GpuPreferenceText.MergeOn(GpuPreferenceValue.FromString(text.Replace("2097", "2096", StringComparison.Ordinal)), On).Kind);
    }

    [Theory]
    [InlineData(null, "AutoHDREnable=2097;", "AutoHDREnable=2097;AppStatus=4;", "AppStatus=4;")] // 10
    [InlineData("GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;", "GpuPreference=2;AutoHDREnable=2097;AppStatus=0;", "GpuPreference=2;AppStatus=0;")] // 11
    [InlineData("AutoHDREnable=2096;", "AutoHDREnable=2097;", "AppStatus=4096;AutoHDREnable=2097;", "AppStatus=4096;AutoHDREnable=2096;")]
    [InlineData("GpuPreference=2;", "GpuPreference=2;AutoHDREnable=2097;", "AutoHDREnable=2097;", "GpuPreference=2;")] // nichts mehr übrig: Original
    public void Windows_additions_stay_and_only_the_token_is_taken_back(string? before, string written, string now, string expected)
    {
        var original = before is null ? GpuPreferenceValue.Missing : GpuPreferenceValue.FromString(before);
        var token = GpuPreferenceText.MergeOn(original, On).OriginalToken;

        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(now), original, token, written, On);

        Assert.Equal(GpuPreferenceRestoreRule.TokenOnly, restore.Rule);
        Assert.False(restore.Delete);
        Assert.Equal(expected, restore.Text);
    }

    [Fact]
    public void Token_only_restore_deletes_when_nothing_is_left_and_the_value_was_missing()
    {
        // Windows hat den eigenen Text neu geschrieben (nur Abschluss weg): Token zurück, nichts übrig, Wert fehlte.
        var restore = GpuPreferenceText.DecideRestore(
            GpuPreferenceValue.FromString("AutoHDREnable=2097"), GpuPreferenceValue.Missing, null, "AutoHDREnable=2097;", On);
        Assert.Equal(GpuPreferenceRestoreRule.TokenOnly, restore.Rule);
        Assert.True(restore.Delete);
    }

    [Theory]
    [InlineData("AutoHDREnable=2096;")] // 12: Nutzer wählte „Auto HDR nicht verwenden“
    [InlineData("GpuPreference=2;")] // Token entfernt
    [InlineData("0")] // 14: nicht mehr sauber
    [InlineData("AutoHDREnable=2097;\0x")]
    public void Externally_changed_values_stay_untouched(string now)
    {
        var restore = GpuPreferenceText.DecideRestore(GpuPreferenceValue.FromString(now), GpuPreferenceValue.Missing, null, "AutoHDREnable=2097;", On);
        Assert.Equal(GpuPreferenceRestoreRule.External, restore.Rule);
        Assert.True(restore.LeaveAsIs);
    }

    [Fact]
    public void Deleted_or_retyped_values_stay_as_they_are()
    {
        // 13: gelöscht; 14: jetzt DWORD.
        foreach (var now in new[] { GpuPreferenceValue.Missing, new GpuPreferenceValue(GpuPreferenceValueKind.DWord, "1") })
        {
            var restore = GpuPreferenceText.DecideRestore(now, GpuPreferenceValue.FromString("GpuPreference=2;"), null, "GpuPreference=2;AutoHDREnable=2097;", On);
            Assert.True(restore.LeaveAsIs);
        }
    }

    [Fact]
    public void Keys_compare_without_case_and_keep_their_spelling()
    {
        Assert.True(GpuPreferenceText.TryParse("AutoHdrEnable=1;GpuPreference=2;", out var tokens));
        Assert.Equal("1", tokens.Get("AUTOHDRENABLE"));
        Assert.Equal("AutoHdrEnable=5;GpuPreference=2;", tokens.With("autohdrenable", "5").ToString());
        Assert.Equal("GpuPreference=2;", tokens.Without("AUTOHDRENABLE").ToString());
        Assert.Null(tokens.Get("AppStatus"));
        Assert.Equal("AutoHdrEnable=1;GpuPreference=2;AppStatus=4;", tokens.With("AppStatus", "4").ToString());
    }

    [Fact]
    public void Global_value_gets_only_its_token()
    {
        var merge = GpuPreferenceText.MergeOn(GpuPreferenceValue.FromString("AutoHDREnable=0;SwapEffectUpgradeEnable=1;"), GpuPreferenceText.GlobalOn);
        Assert.Equal("AutoHDREnable=1;SwapEffectUpgradeEnable=1;", merge.Written);
        Assert.Equal("0", merge.OriginalToken);
        Assert.Equal(GpuPreferenceMergeKind.AlreadyOn, GpuPreferenceText.MergeOn(GpuPreferenceValue.FromString(merge.Written!), GpuPreferenceText.GlobalOn).Kind);
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
