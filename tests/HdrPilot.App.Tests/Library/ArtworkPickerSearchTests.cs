using System.ComponentModel;
using HdrPilot.App.Resources;
using HdrPilot.App.Services.Artwork;
using HdrPilot.App.ViewModels;
using HdrPilot.App.ViewModels.Library;
using HdrPilot.Core.Artwork;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using static HdrPilot.App.Tests.Library.TestLibraryContext;

namespace HdrPilot.App.Tests.Library;

/// <summary>
/// „Bild wählen…“ mit SteamGridDB (E93, G14.3) ohne Fenster: Zustände und Texte aus resx, je Reiter ein eigener Klick, Mehr
/// laden bis zur leeren Seite, Speicher verborgener Reiter, Spielwahl, eine Wahl je Reiter über beide Listen, Laden beim
/// Übernehmen noch im Dialog, Schließen gibt frei, Link zu den Einstellungen, Ansage nur bei Zustandswechseln. Nur
/// Attrappen und erfundene Namen, nie das Netz.
/// </summary>
public sealed class ArtworkPickerSearchTests
{
    private const string Manual = "Gog:1207658930";
    private const string SteamKey = "Steam:9990150";

    private readonly FakeSteamGridDbSearch _sgdb = new();
    private readonly FakeArtworkService _art = new();
    private readonly FakeDialogs _dialogs = new();

    [Fact]
    public void Without_a_key_the_section_shows_the_hint_and_the_link_and_opens_no_session() => Sta.Run(() =>
    {
        _sgdb.Key = SteamGridDbKeyState.Missing;
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;

        Assert.Equal(SteamGridDbSearchState.NoKey, picker.Online.State);
        Assert.Equal(LibraryStrings.Picker_SgdbNoKey, picker.Online.StatusText);
        Assert.True(picker.Online.ShowSettingsLink);
        Assert.False(picker.Online.ShowSearchButton);
        Assert.False(picker.Online.ShowHeader);
        Assert.Equal(StatusKind.Info, picker.Online.StatusTone);
        Assert.Empty(_sgdb.Sessions);

        // Ohne SteamGridDB im Kontext (Standard) ebenso.
        var context = Context();
        context.SteamGridDb = NullSteamGridDbService.Instance;
        using var plain = new LibraryViewModel(context, _dialogs);
        var other = new ArtworkPickerViewModel(plain, plain.Games.Single(g => g.Key == Manual), ArtworkKind.Cover);
        Assert.Equal(SteamGridDbSearchState.NoKey, other.Online.State);
        other.Dispose();
        return Task.CompletedTask;
    });

    [Fact]
    public void An_unreadable_key_shows_its_own_hint() => Sta.Run(() =>
    {
        _sgdb.Key = SteamGridDbKeyState.Unreadable;
        var (_, vm, picker) = Open(Manual, ArtworkKind.Logo);
        using var _vm = vm;

        Assert.Equal(SteamGridDbSearchState.KeyUnreadable, picker.Online.State);
        Assert.Equal(LibraryStrings.Picker_SgdbKeyUnreadable, picker.Online.StatusText);
        Assert.Equal(StatusKind.Warning, picker.Online.StatusTone);
        Assert.True(picker.Online.ShowSettingsLink);
        Assert.Empty(_sgdb.Sessions);
        return Task.CompletedTask;
    });

    [Fact]
    public void Opening_switching_tabs_expanding_and_typing_send_nothing() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.LoadAsync();

        picker.Kind = ArtworkKind.Logo;
        picker.Kind = ArtworkKind.Cover;
        picker.Online.IsMatchOpen = true;
        picker.Online.MatchSearchText = "Nebelfahrt";
        picker.Online.IsMatchOpen = false;
        await Settle();

        var session = Assert.Single(_sgdb.Sessions);
        Assert.Empty(session.ApiCalls);
        Assert.Empty(session.PreviewCalls);
        Assert.Equal(SteamGridDbSearchState.Ready, picker.Online.State);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMatch, "Kupferwald"), picker.Online.MatchText);
        Assert.Equal(LibraryStrings.Picker_SgdbMatchHowSteam, picker.Online.MatchToolTip);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbSteamEntry, "Kupferwald"), Assert.Single(picker.Online.MatchEntries).Text);
    });

    [Fact]
    public void Search_click_shows_24_tiles_with_author_size_and_automation_name() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(Manual, ArtworkKind.Cover);
        using var _vm = vm;

        await picker.Online.SearchAsync(ArtworkKind.Cover);
        await Settle();

        var online = picker.Online;
        Assert.Equal(SteamGridDbSearchState.Results, online.State);
        Assert.Equal(24, online.Results.Count);
        Assert.Equal(["resolve", "assets:Cover:game/9101:0"], _sgdb.Session!.ApiCalls);
        var first = online.Results[0];
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbAuthor, "Testautor " + first.Asset!.Id.ToString("00", System.Globalization.CultureInfo.InvariantCulture)), first.AuthorText);
        Assert.Equal(first.AuthorText, first.Label);
        Assert.False(first.ShowSize); // 600 × 900 ist die übliche Größe
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbItemSize, LibraryText.Format(LibraryStrings.Picker_SgdbItemName, LibraryStrings.Picker_TabCover, first.Author), 600, 900), first.AutomationName);
        Assert.Equal(LibraryStrings.Picker_SgdbItemHelp, first.AutomationHelp);
        Assert.Null(first.Token);
        Assert.True(first.IsOnline);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbResults, 24, LibraryStrings.Picker_KindPluralCover), online.CountText);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMatch, LibraryText.Format(LibraryStrings.Picker_SgdbMatchYear, "Kupferwald", 2019)), online.MatchText);
        Assert.False(online.ShowSearchButton);
        Assert.True(online.ShowMore);

        // Eine ungewöhnliche Größe steht da, ohne Autor heißt es „Autor unbekannt“.
        var odd = ArtworkChoiceViewModel.Online(FakeSearchSession.Asset(5, ArtworkKind.Cover, author: null, width: 342, height: 482), ArtworkKind.Cover);
        Assert.True(odd.ShowSize);
        Assert.Equal(LibraryStrings.Picker_SgdbAuthorUnknown, odd.AuthorText);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbItemSize, LibraryText.Format(LibraryStrings.Picker_SgdbItemNameNoAuthor, LibraryStrings.Picker_TabCover), 342, 482), odd.AutomationName);
        Assert.True(ArtworkChoiceViewModel.Online(FakeSearchSession.Asset(6, ArtworkKind.Logo), ArtworkKind.Logo).ShowSize); // Logos immer
    });

    [Fact]
    public void More_shows_the_next_24_from_memory_then_fetches_the_next_page() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        var online = picker.Online;

        await online.SearchAsync(ArtworkKind.Hero);
        Assert.Single(_sgdb.Session!.ApiCalls); // Steam: keine Namenssuche
        await online.MoreAsync(ArtworkKind.Hero);
        Assert.Equal(48, online.Results.Count);
        Assert.Single(_sgdb.Session.ApiCalls); // aus dem Speicher
        await online.MoreAsync(ArtworkKind.Hero);

        Assert.Equal(72, online.Results.Count);
        Assert.Equal(["assets:Hero:steam/9990150:0", "assets:Hero:steam/9990150:1"], _sgdb.Session.ApiCalls);
        Assert.Equal(72, online.Results.Select(r => r.Asset!.Id).Distinct().Count());
    });

    [Fact]
    public void The_list_ends_on_an_empty_page_and_more_ignores_a_second_click_while_a_page_loads() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        var online = picker.Online;
        await online.SearchAsync(ArtworkKind.Hero);
        await online.MoreAsync(ArtworkKind.Hero);
        await online.MoreAsync(ArtworkKind.Hero);
        Assert.True(online.ShowMore); // Seite 1 war nicht leer: es könnte mehr geben

        var session = _sgdb.Session!;
        session.HoldApi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var more = online.MoreAsync(ArtworkKind.Hero);
        Assert.True(online.IsMoreBusy);
        Assert.False(online.MoreCommand.CanExecute(null));
        await online.MoreAsync(ArtworkKind.Hero); // zweiter Klick: übergangen
        session.HoldApi.SetResult();
        await more;

        Assert.Equal(3, session.ApiCalls.Count);
        Assert.EndsWith(":2", session.ApiCalls[^1], StringComparison.Ordinal);
        Assert.Equal(72, online.Results.Count);
        Assert.False(online.ShowMore);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbEnd, LibraryStrings.Picker_KindPluralHero), online.StatusText);
    });

    [Fact]
    public void Each_tab_searches_only_after_its_own_click_and_keeps_its_results() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);

        picker.Kind = ArtworkKind.Hero;
        Assert.Equal(SteamGridDbSearchState.Ready, picker.Online.State);
        Assert.Empty(picker.Online.Results);
        Assert.Single(_sgdb.Session!.ApiCalls);

        picker.Kind = ArtworkKind.Cover;
        Assert.Equal(24, picker.Online.Results.Count);
        Assert.Single(_sgdb.Session.ApiCalls);
        await picker.Online.SearchAsync(ArtworkKind.Cover); // schon Ergebnisse: nichts
        Assert.Single(_sgdb.Session.ApiCalls);
    });

    [Fact]
    public void Switching_tabs_cancels_waiting_previews_and_resumes_them_on_return() => Sta.Run(async () =>
    {
        _sgdb.Setup = s => s.HoldPreviews = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var session = _sgdb.Session!;
        var firstQueue = session.QueueTokens.ToList();
        Assert.Equal(22, firstQueue.Count); // 24 Kacheln, zwei ohne Vorschau

        picker.Kind = ArtworkKind.Logo;
        await Settle();
        Assert.All(firstQueue, t => Assert.True(t.IsCancellationRequested));
        Assert.Empty(picker.Online.Results); // der Logo-Reiter zeigt keine Cover

        picker.Kind = ArtworkKind.Cover;
        Assert.Equal(44, session.QueueTokens.Count); // wieder angefragt
        session.HoldPreviews!.SetResult();
        await WaitFor(() => picker.Online.Results.Count(r => r.PreviewState == PreviewState.Loaded) == 22);
        Assert.Equal(22, session.PreviewCalls.Count);
    });

    [Fact]
    public void Hidden_tabs_release_their_previews_when_memory_runs_short_and_reload_them_on_return() => Sta.Run(async () =>
    {
        _sgdb.Setup = s => s.PreviewBytes = 3_500_000; // 22 Vorschauen ≈ 77 MB je Reiter
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        await WaitFor(() => picker.Online.HeldBytes == 22 * 3_500_000L);
        var covers = picker.Online.Results.ToList();

        picker.Kind = ArtworkKind.Hero;
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        await WaitFor(() => picker.Online.Results.All(r => r.PreviewState is PreviewState.Loaded or PreviewState.Missing));
        var heroes = picker.Online.Results.ToList();
        picker.Selected = heroes[1];

        // Zurück zum Cover: über 96 MB, der verborgene Reiter (Titelbild) gibt frei, nur seine gewählte Kachel nicht.
        picker.Kind = ArtworkKind.Cover;

        Assert.True(picker.Online.HeldBytes <= SteamGridDbSearchViewModel.MaxHeldBytes);
        Assert.NotNull(heroes[1].Preview);
        Assert.Null(heroes[2].Preview);
        Assert.Equal(PreviewState.None, heroes[2].PreviewState);
        Assert.NotNull(covers[2].Preview);
        var calls = _sgdb.Session!.PreviewCalls.Count;

        // Wieder beim Titelbild: die freigegebenen Vorschauen kommen neu.
        picker.Kind = ArtworkKind.Hero;
        await WaitFor(() => heroes.All(r => r.PreviewState is PreviewState.Loaded or PreviewState.Missing));
        Assert.Equal(calls + 21, _sgdb.Session.PreviewCalls.Count); // ohne die gewählte, die ihre behielt
    });

    [Fact]
    public void A_tab_stops_offering_more_at_the_tile_limit_or_the_memory_bound() => Sta.Run(async () =>
    {
        _sgdb.Setup = s =>
        {
            s.PageSizes.Clear();
            s.PageSizes[0] = 50;
            for (var p = 1; p < 8; p++)
            {
                s.PageSizes[p] = 50;
            }
        };
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Logo);
        using var _vm = vm;
        var online = picker.Online;
        await online.SearchAsync(ArtworkKind.Logo);
        for (var i = 0; i < 12 && online.ShowMore; i++)
        {
            await online.MoreAsync(ArtworkKind.Logo);
        }

        Assert.Equal(SteamGridDbSearchViewModel.MaxTiles, online.Results.Count);
        Assert.False(online.ShowMore);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbTabLimit, LibraryStrings.Picker_KindPluralLogo), online.StatusText);

        // Speichergrenze: Die nächste Seite passte nicht mehr in 96 MB.
        var big = new FakeSteamGridDbSearch { Setup = s => s.PreviewBytes = 4_500_000 };
        var context = Context(big);
        using var vm2 = new LibraryViewModel(context, _dialogs);
        var heavy = new ArtworkPickerViewModel(vm2, vm2.Games.Single(g => g.Key == SteamKey), ArtworkKind.Hero);
        await heavy.Online.SearchAsync(ArtworkKind.Hero);
        await WaitFor(() => heavy.Online.HeldBytes >= 22 * 4_500_000L);
        await heavy.Online.MoreAsync(ArtworkKind.Hero);
        Assert.False(heavy.Online.ShowMore);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbTabLimit, LibraryStrings.Picker_KindPluralHero), heavy.Online.StatusText);
        heavy.Dispose();
    });

    [Fact]
    public void One_selection_per_tab_across_local_and_online_lists() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.LoadAsync();
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var tile = picker.Online.Results[2];

        picker.Selected = tile;
        Assert.Same(tile, picker.Selected);
        Assert.True(tile.IsSelected);
        Assert.DoesNotContain(picker.Candidates, c => c.IsSelected);
        Assert.Single(picker.Online.Results, r => r.IsSelected);
        Assert.True(picker.HasChanges);

        picker.Selected = null; // die Liste ohne die Wahl meldet null: übergangen
        Assert.Same(tile, picker.Selected);

        picker.Selected = picker.Candidates[0];
        Assert.False(tile.IsSelected);
        Assert.True(picker.Candidates[0].IsSelected);
        Assert.DoesNotContain(picker.Online.Results, r => r.IsSelected);
    });

    [Fact]
    public void Selected_accepts_only_items_of_the_current_tab() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.LoadAsync();
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var cover = picker.Online.Results[0];

        picker.Kind = ArtworkKind.Hero;
        await picker.LoadAsync();
        var heroChoice = picker.Selected;
        picker.Selected = cover;

        Assert.Same(heroChoice, picker.Selected);
        Assert.False(picker.Selected!.IsOnline);
    });

    [Fact]
    public void Reset_selects_automatic_after_an_online_pick() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.LoadAsync();
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        var tile = picker.Online.Results[0];
        picker.Selected = tile;

        picker.ResetCommand.Execute(null);

        Assert.Null(picker.Selected!.Token);
        Assert.False(picker.Selected.IsOnline);
        Assert.False(tile.IsSelected);
    });

    [Fact]
    public void A_match_change_keeps_the_current_tabs_pick_and_resets_other_tabs_with_a_note() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.LoadAsync();
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        picker.Selected = picker.Online.Results[1];
        picker.Kind = ArtworkKind.Cover;
        await picker.LoadAsync();
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var kept = picker.Online.Results[3];
        picker.Selected = kept;

        await picker.Online.ChooseMatchAsync(picker.Online.MatchEntries.Single(e => e.Hit?.Id == 9102));

        Assert.Same(kept, picker.Online.Results[0]); // vorn in der neuen Liste
        Assert.Same(kept, picker.Selected);
        Assert.Single(picker.Online.Results, r => r.Asset!.Id == kept.Asset!.Id);
        Assert.Contains("assets:Cover:game/9102:0", _sgdb.Session!.ApiCalls);
        Assert.Equal(LibraryStrings.Picker_SgdbPicksReset, picker.Message);
        Assert.Equal(StatusKind.Info, picker.MessageKind);
        picker.Kind = ArtworkKind.Hero;
        Assert.False(picker.Selected!.IsOnline); // Titelbild fiel auf seine Standardwahl zurück
        Assert.Equal(SteamGridDbSearchState.Ready, picker.Online.State);
    });

    [Fact]
    public void No_match_opens_the_chooser_with_hits_and_a_choice_loads_its_images() => Sta.Run(async () =>
    {
        _sgdb.Setup = s => s.Resolve = () => new SteamGridDbResolveResult(null, [FakeSearchSession.Hit(9102, "Kupferwald: Neuauflage", 2023), FakeSearchSession.Hit(9103, "Kupferwald II", verified: true)], false);
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;
        var focus = new List<SteamGridDbFocusTarget>();
        picker.Online.FocusRequest += (_, e) => focus.Add(e.Target);

        await picker.Online.SearchAsync(ArtworkKind.Hero);

        Assert.Equal(SteamGridDbSearchState.NoMatch, picker.Online.State);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbNoMatch, "Kupferwald"), picker.Online.StatusText);
        Assert.True(picker.Online.IsMatchOpen);
        Assert.Equal(2, picker.Online.MatchEntries.Count);
        Assert.Equal("Kupferwald II, verifiziert", picker.Online.MatchEntries[1].AutomationName);
        Assert.Equal("Kupferwald: Neuauflage, 2023", picker.Online.MatchEntries[0].AutomationName);
        Assert.Contains(SteamGridDbFocusTarget.MatchList, focus);

        await picker.Online.ChooseMatchAsync(picker.Online.MatchEntries[1]);

        Assert.False(picker.Online.IsMatchOpen);
        Assert.Equal(SteamGridDbSearchState.Results, picker.Online.State);
        Assert.Contains("assets:Hero:game/9103:0", _sgdb.Session!.ApiCalls);
        Assert.Equal(LibraryStrings.Picker_SgdbMatchHowChosen, picker.Online.MatchToolTip);
        Assert.True(picker.Online.MatchEntries[1].IsCurrent);
        Assert.Equal(LibraryStrings.Picker_SgdbEntryCurrent, picker.Online.MatchEntries[1].ItemStatus);
    });

    [Fact]
    public void Several_exact_hits_take_the_first_and_flag_the_match() => Sta.Run(async () =>
    {
        _sgdb.Setup = s => s.Resolve = () => new SteamGridDbResolveResult(
            new SteamGridDbMatch(SteamGridDbMatchKind.ExactName, SteamGridDbTarget.Game(9104), "Kupferwald", 2005, false, Ambiguous: true),
            [FakeSearchSession.Hit(9104, "Kupferwald", 2005), FakeSearchSession.Hit(9101, "Kupferwald", 2019, true)],
            false);
        var (_, vm, picker) = Open(Manual, ArtworkKind.Logo);
        using var _vm = vm;

        await picker.Online.SearchAsync(ArtworkKind.Logo);

        Assert.True(picker.Online.IsMatchAmbiguous);
        Assert.Equal(LibraryStrings.Picker_SgdbMatchAmbiguous, picker.Online.MatchAmbiguousText);
        Assert.Contains("assets:Logo:game/9104:0", _sgdb.Session!.ApiCalls);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMatch, LibraryText.Format(LibraryStrings.Picker_SgdbMatchYear, "Kupferwald", 2005)), picker.Online.MatchText);
    });

    [Fact]
    public void A_remembered_match_shows_its_name_after_the_first_click_and_is_offered_first() => Sta.Run(async () =>
    {
        _sgdb.Setup = s => s.InitialMatch = new SteamGridDbMatch(SteamGridDbMatchKind.Remembered, SteamGridDbTarget.Game(9105), "Kupferwald", null, false);
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;
        var online = picker.Online;
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMatch, "Kupferwald"), online.MatchText); // vor dem Netz: Name der Bibliothek
        Assert.Equal(LibraryStrings.Picker_SgdbMatchHowRemembered, online.MatchToolTip);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbRememberedEntry, "Kupferwald"), online.MatchEntries[0].Text);

        await online.SearchAsync(ArtworkKind.Hero);
        picker.Kind = ArtworkKind.Cover;
        await online.SearchAsync(ArtworkKind.Cover);

        Assert.Equal(["assets:Hero:game/9105:0", "describe:9105", "assets:Cover:game/9105:0"], _sgdb.Session!.ApiCalls);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbMatch, LibraryText.Format(LibraryStrings.Picker_SgdbMatchYear, "Kupferwald (SteamGridDB)", 2018)), online.MatchText);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbRememberedEntry, "Kupferwald (SteamGridDB)"), online.MatchEntries[0].Text);
        Assert.True(online.MatchEntries[0].IsCurrent);
        Assert.False(online.MatchChangedByUser);
    });

    [Fact]
    public void Chooser_searches_only_on_enter_or_button_and_cancels_the_previous_search() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;
        var online = picker.Online;
        online.IsMatchOpen = true;
        online.MatchSearchText = "Nebel";
        Assert.Empty(_sgdb.Session!.ApiCalls);

        _sgdb.Session.HoldApi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = online.SearchGamesAsync();
        Assert.True(online.IsMatchSearching);
        Assert.Equal(LibraryStrings.Picker_SgdbMatchSearching, online.MatchHint);
        online.MatchSearchText = "Nebelfahrt";
        var second = online.SearchGamesAsync();
        _sgdb.Session.HoldApi.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["games:Nebel", "games:Nebelfahrt"], _sgdb.Session.ApiCalls);
        Assert.Equal(3, online.MatchEntries.Count);
        Assert.Equal("Nebelfahrt (2019)", online.MatchEntries[0].Text); // nur die zweite Suche zählt
        Assert.False(online.IsMatchSearching);
        Assert.Equal(LibraryStrings.Picker_SgdbMatchHint, online.MatchHint);
        Assert.True(online.ShowCommitHint);
    });

    [Fact]
    public void A_changed_match_resets_all_tabs_but_loads_only_the_current_one() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        picker.Kind = ArtworkKind.Cover;
        await picker.Online.SearchGamesAsync();

        await picker.Online.ChooseMatchAsync(picker.Online.MatchEntries.Single(e => e.Hit?.Id == 9103));

        Assert.Contains("assets:Cover:game/9103:0", _sgdb.Session!.ApiCalls);
        Assert.DoesNotContain(_sgdb.Session.ApiCalls, c => c.StartsWith("assets:Hero:game/9103", StringComparison.Ordinal));
        picker.Kind = ArtworkKind.Hero;
        Assert.Equal(SteamGridDbSearchState.Ready, picker.Online.State);
        Assert.Empty(picker.Online.Results);
        Assert.True(picker.Online.MatchChangedByUser);
    });

    [Fact]
    public void Steam_match_changes_are_never_remembered() => Sta.Run(async () =>
    {
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.Online.SearchGamesAsync();
        await picker.Online.ChooseMatchAsync(picker.Online.MatchEntries.Single(e => e.Hit?.Id == 9102));
        Assert.True(picker.Online.MatchChangedByUser);
        Assert.False(picker.Online.ShowCommitHint);

        picker.CommitMatch(await picker.ApplyWithOutcomeAsync());
        picker.CommitMatch(ApplyOutcome.Applied);

        Assert.Empty(_sgdb.Remembered);
        Assert.Null(context.Config.FindGame(SteamKey)!.Artwork);
    });

    [Fact]
    public void Other_games_remember_a_changed_match_after_apply_or_unchanged_apply_never_after_cancel_or_failure() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(Manual, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.Online.SearchGamesAsync();
        await picker.Online.ChooseMatchAsync(picker.Online.MatchEntries.Single(e => e.Hit?.Id == 9102));

        picker.CommitMatch(ApplyOutcome.Failed);
        Assert.Empty(_sgdb.Remembered);
        var outcome = await picker.ApplyWithOutcomeAsync();
        Assert.Equal(ApplyOutcome.Unchanged, outcome);
        picker.CommitMatch(outcome);
        Assert.Equal([(Manual, 9102L)], _sgdb.Remembered);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_MatchRemembered, "Kupferwald"), vm.StatusMessage);

        // Über den Dialog: Abbrechen merkt nichts, Übernehmen schon.
        _sgdb.Remembered.Clear();
        vm.SelectedGame = vm.Games.Single(g => g.Key == Manual);
        _dialogs.OnArtworkPicker = p =>
        {
            _ = p.Online.ChooseMatchAsync(new SteamGridDbMatchEntry("Kupferwald II", "Kupferwald II", false, FakeSearchSession.Hit(9103, "Kupferwald II"), SteamGridDbTarget.Game(9103)));
            return false;
        };
        await Run(vm.ChooseArtworkCommand, "Hero");
        Assert.Empty(_sgdb.Remembered);

        _dialogs.OnArtworkPicker = p =>
        {
            _ = p.Online.ChooseMatchAsync(new SteamGridDbMatchEntry("Kupferwald II", "Kupferwald II", false, FakeSearchSession.Hit(9103, "Kupferwald II"), SteamGridDbTarget.Game(9103)));
            return true;
        };
        await Run(vm.ChooseArtworkCommand, "Hero");
        await WaitFor(() => _sgdb.Remembered.Count == 1);
        Assert.Equal([(Manual, 9103L)], _sgdb.Remembered);
    });

    [Theory]
    [InlineData("noMatch")]
    [InlineData("steamUnknown")]
    [InlineData("noHits")]
    [InlineData("noImages")]
    [InlineData("unauthorized")]
    [InlineData("rateLimited")]
    [InlineData("offline")]
    [InlineData("timeout")]
    [InlineData("server")]
    [InlineData("invalid")]
    [InlineData("blocked")]
    [InlineData("budget")]
    public void Error_kinds_show_the_exact_texts(string kind) => Sta.Run(async () =>
    {
        var steam = kind == "steamUnknown";
        _sgdb.Setup = s =>
        {
            switch (kind)
            {
                case "noMatch":
                    s.Resolve = () => new SteamGridDbResolveResult(null, [FakeSearchSession.Hit(9102, "Anderes")], false);
                    break;
                case "steamUnknown":
                    // Unbekannte Steam-App: Seite 0 meldet „Spiel unbekannt“, die Namenssuche findet nichts Gleiches.
                    s.UnknownTargets.Add("steam/9990150");
                    s.Resolve = () => new SteamGridDbResolveResult(null, [FakeSearchSession.Hit(9102, "Anderes")], true);
                    break;
                case "noHits":
                    s.Resolve = () => new SteamGridDbResolveResult(null, [], false);
                    break;
                case "noImages":
                    s.PageSizes.Clear();
                    break;
                default:
                    s.Fail = call => call.StartsWith("assets", StringComparison.Ordinal) ? Error(kind) : null;
                    break;
            }
        };
        var (_, vm, picker) = Open(steam ? SteamKey : Manual, ArtworkKind.Cover);
        using var _vm = vm;

        await picker.Online.SearchAsync(ArtworkKind.Cover);

        var plural = LibraryStrings.Picker_KindPluralCover;
        var (state, text, tone) = kind switch
        {
            "noMatch" => (SteamGridDbSearchState.NoMatch, LibraryText.Format(LibraryStrings.Picker_SgdbNoMatch, "Kupferwald"), StatusKind.Info),
            "steamUnknown" => (SteamGridDbSearchState.NoMatch, LibraryStrings.Picker_SgdbSteamUnknown, StatusKind.Info),
            "noHits" => (SteamGridDbSearchState.NoHits, LibraryText.Format(LibraryStrings.Picker_SgdbNoHits, "Kupferwald"), StatusKind.Info),
            "noImages" => (SteamGridDbSearchState.NoImages, LibraryText.Format(LibraryStrings.Picker_SgdbNoImages, plural), StatusKind.Info),
            "unauthorized" => (SteamGridDbSearchState.Unauthorized, LibraryStrings.Picker_SgdbUnauthorized, StatusKind.Error),
            "rateLimited" => (SteamGridDbSearchState.RateLimited, LibraryStrings.Picker_SgdbRateLimited, StatusKind.Warning),
            "offline" => (SteamGridDbSearchState.Offline, LibraryStrings.Picker_SgdbOffline, StatusKind.Warning),
            "timeout" => (SteamGridDbSearchState.Offline, LibraryStrings.Picker_SgdbTimeout, StatusKind.Warning),
            "budget" => (SteamGridDbSearchState.BudgetExhausted, LibraryStrings.Picker_SgdbBudget, StatusKind.Info),
            _ => (SteamGridDbSearchState.ServerError, LibraryStrings.Picker_SgdbServerError, StatusKind.Warning),
        };
        Assert.Equal(state, picker.Online.State);
        Assert.Equal(text, picker.Online.StatusText);
        Assert.Equal(tone, picker.Online.StatusTone);
        Assert.Equal(state is SteamGridDbSearchState.RateLimited or SteamGridDbSearchState.Offline or SteamGridDbSearchState.ServerError, picker.Online.ShowRetry);
        Assert.Equal(state == SteamGridDbSearchState.Unauthorized, picker.Online.ShowSettingsLink);
    });

    [Fact]
    public void An_error_during_more_keeps_the_grid_and_retry_repeats_that_page() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        var online = picker.Online;
        await online.SearchAsync(ArtworkKind.Hero);
        await online.MoreAsync(ArtworkKind.Hero);
        var failing = true;
        _sgdb.Session!.Fail = call => failing && call.EndsWith(":1", StringComparison.Ordinal) ? new SteamGridDbException(SteamGridDbErrorKind.HttpError, "Test") : null;

        await online.MoreAsync(ArtworkKind.Hero);

        Assert.Equal(48, online.Results.Count); // die Kacheln bleiben
        Assert.True(online.ShowResults);
        Assert.Equal(SteamGridDbSearchState.ServerError, online.State);
        Assert.True(online.ShowRetry);
        Assert.False(online.ShowMore);

        failing = false;
        await online.RetryAsync(ArtworkKind.Hero);

        Assert.Equal(72, online.Results.Count);
        Assert.Equal(2, _sgdb.Session.ApiCalls.Count(c => c.EndsWith(":1", StringComparison.Ordinal)));
        Assert.Equal(SteamGridDbSearchState.Results, online.State);
    });

    [Fact]
    public void Rate_limit_disables_search_more_and_retry_until_the_pause_ends() => Sta.Run(async () =>
    {
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        var session = _sgdb.Session!;
        session.Fail = _ => new SteamGridDbException(SteamGridDbErrorKind.RateLimited, "Test");
        session.BlockedUntil = context.FakeClock.UtcNow + TimeSpan.FromSeconds(60);

        await picker.Online.SearchAsync(ArtworkKind.Cover);

        Assert.Equal(SteamGridDbSearchState.RateLimited, picker.Online.State);
        Assert.False(picker.Online.CanRetry);
        Assert.False(picker.Online.RetryCommand.CanExecute(null));
        Assert.Equal(LibraryStrings.Picker_SgdbRateLimited, picker.Online.RetryToolTip);
        Assert.Equal(1, context.Dispatcher.PendingDelays); // ein einmaliger Zeitgeber, kein Ticken

        session.BlockedUntil = null;
        session.Fail = _ => null;
        context.Dispatcher.RunDelayed();
        Assert.True(picker.Online.CanRetry);
        await picker.Online.RetryAsync(ArtworkKind.Cover);
        Assert.Equal(SteamGridDbSearchState.Results, picker.Online.State);
    });

    [Fact]
    public void A_slow_answer_shows_the_slow_text_once() => Sta.Run(async () =>
    {
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        _sgdb.Session!.HoldApi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = Track(picker.Online, nameof(SteamGridDbSearchViewModel.StatusText));

        var search = picker.Online.SearchAsync(ArtworkKind.Cover);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbLoading, LibraryStrings.Picker_KindPluralCover), picker.Online.StatusText);
        context.Dispatcher.RunDelayed();
        Assert.Equal(LibraryStrings.Picker_SgdbSlow, picker.Online.StatusText);
        context.Dispatcher.RunDelayed(); // nichts mehr fällig
        Assert.Equal(1, changes.Count(t => t == LibraryStrings.Picker_SgdbSlow));
        _sgdb.Session.HoldApi.SetResult();
        await search;
        Assert.Equal(SteamGridDbSearchState.Results, picker.Online.State);
    });

    [Fact]
    public void Budget_exhaustion_shows_its_text_and_keeps_the_tiles_selectable() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        await picker.Online.MoreAsync(ArtworkKind.Hero);
        _sgdb.Session!.Fail = _ => new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Test");

        await picker.Online.MoreAsync(ArtworkKind.Hero);
        picker.Selected = picker.Online.Results[40];

        Assert.Equal(SteamGridDbSearchState.BudgetExhausted, picker.Online.State);
        Assert.Equal(LibraryStrings.Picker_SgdbBudget, picker.Online.StatusText);
        Assert.False(picker.Online.ShowRetry);
        Assert.Same(picker.Online.Results[40], picker.Selected);
    });

    [Fact]
    public void Missing_or_failed_previews_show_a_placeholder_and_stay_selectable() => Sta.Run(async () =>
    {
        _sgdb.Setup = s => s.PreviewFail = a => a.Id % 1000 == 3 ? new IOException("kaputt") : null;
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        await WaitFor(() => picker.Online.Results.All(r => r.PreviewState is not (PreviewState.Loading or PreviewState.None)));

        var missing = picker.Online.Results.Single(r => r.Asset!.Id % 1000 == 10);
        var failed = picker.Online.Results.Single(r => r.Asset!.Id % 1000 == 3);
        Assert.Equal(PreviewState.Missing, missing.PreviewState);
        Assert.Equal(LibraryStrings.Picker_SgdbNoPreview, missing.PlaceholderText);
        Assert.Equal(PreviewState.Failed, failed.PreviewState);
        Assert.Equal(LibraryStrings.Picker_SgdbPreviewFailed, failed.PlaceholderText);
        Assert.DoesNotContain(_sgdb.Session!.PreviewCalls, a => a.Id == missing.Asset!.Id); // keine Anfrage ohne Vorschau
        Assert.DoesNotContain(_sgdb.Session.PreviewCalls, a => a.Url == a.ThumbUrl); // nie das volle Bild
        Assert.Equal(SteamGridDbSearchState.Results, picker.Online.State);

        picker.Selected = failed;
        Assert.Same(failed, picker.Selected);
        picker.Selected = missing;
        Assert.Same(missing, picker.Selected);
    });

    [Fact]
    public void Prepare_apply_downloads_every_online_pick_before_the_dialog_closes() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        var hero = picker.Online.Results[1];
        picker.Selected = hero;
        picker.Kind = ArtworkKind.Cover;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var cover = picker.Online.Results[2];
        picker.Selected = cover;
        _sgdb.Session!.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var prepare = picker.PrepareApplyAsync();
        Assert.True(picker.IsApplying);
        Assert.False(picker.CanEdit);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbDownloadingMany, 2), picker.BusyText);
        Assert.False(picker.ResetCommand.CanExecute(null));
        Assert.False(picker.Online.SearchCommand.CanExecute(null));
        _sgdb.Session.HoldDownloads.SetResult();

        Assert.True(await prepare);
        Assert.False(picker.IsApplying);
        Assert.Null(picker.BusyText);
        Assert.Equal([(hero.Asset!, ArtworkKind.Hero), (cover.Asset!, ArtworkKind.Cover)], _sgdb.Session.Downloads);
        Assert.NotNull(hero.PendingData);
        Assert.NotNull(cover.PendingData);
        Assert.True(await picker.PrepareApplyAsync()); // schon geladen: nichts mehr
        Assert.Equal(2, _sgdb.Session.Downloads.Count);
    });

    [Theory]
    [InlineData("network")]
    [InlineData("blocked")]
    [InlineData("notAnImage")]
    [InlineData("tooLarge")]
    [InlineData("rateLimited")]
    public void Prepare_apply_failure_switches_to_the_failing_tab_and_keeps_message_selection_and_data(string failure) => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var cover = picker.Online.Results[0];
        picker.Selected = cover;
        picker.Kind = ArtworkKind.Hero;
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        var hero = picker.Online.Results[0];
        picker.Selected = hero;
        _sgdb.Session!.DownloadFail = a => ReferenceEquals(a, cover.Asset) ? failure switch
        {
            "network" => new SteamGridDbException(SteamGridDbErrorKind.Network, "Test"),
            "blocked" => new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Test"),
            "notAnImage" => new InvalidDataException("Test"),
            "tooLarge" => new ImageTooLargeException("Test"),
            _ => new SteamGridDbException(SteamGridDbErrorKind.RateLimited, "Test"),
        } : null;
        var focus = new List<SteamGridDbFocusRequestEventArgs>();
        picker.Online.FocusRequest += (_, e) => focus.Add(e);

        Assert.False(await picker.PrepareApplyAsync());

        Assert.Equal(ArtworkKind.Cover, picker.Kind);
        Assert.Same(cover, picker.Selected);
        Assert.NotNull(hero.PendingData); // schon geladen, bleibt
        Assert.Null(cover.PendingData);
        Assert.False(picker.IsApplying);
        Assert.Equal(StatusKind.Error, picker.MessageKind);
        Assert.Equal(failure switch
        {
            "network" => LibraryStrings.Picker_SgdbDownloadFailed,
            "blocked" => LibraryStrings.Picker_SgdbDownloadBlocked,
            "notAnImage" => LibraryStrings.Picker_SgdbDownloadNotAnImage,
            "tooLarge" => LibraryStrings.Picker_TooLarge,
            _ => LibraryStrings.Picker_SgdbRateLimited,
        }, picker.Message);
        Assert.Contains(focus, f => f.Target == SteamGridDbFocusTarget.Tile && ReferenceEquals(f.Item, cover));
    });

    [Fact]
    public void Prepare_apply_returns_false_on_cancel_without_throwing() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Logo);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Logo);
        picker.Selected = picker.Online.Results[0];
        _sgdb.Session!.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var prepare = picker.PrepareApplyAsync();
        picker.CloseSession(); // Abbrechen, Esc oder X während des Ladens

        Assert.False(await prepare);
        Assert.True(_sgdb.Session.Disposed);
        Assert.False(await picker.PrepareApplyAsync());
    });

    [Fact]
    public void Apply_stores_an_online_pick_as_a_custom_image_and_deletes_the_replaced_one() => Sta.Run(async () =>
    {
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        context.ConfigService.Update(c => c.FindGame(SteamKey)!.Artwork = new GameArtworkChoice { Cover = "custom:alt.jpg" });
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        picker.Selected = picker.Online.Results[2];
        Assert.True(await picker.PrepareApplyAsync());
        picker.CloseSession();

        var outcome = await picker.ApplyWithOutcomeAsync();

        Assert.Equal(ApplyOutcome.Applied, outcome);
        Assert.Equal([(SteamKey, ArtworkKind.Cover, 4)], _art.SavedData);
        Assert.Equal("custom:Steam-9990150-cover.jpg", context.Config.FindGame(SteamKey)!.Artwork!.Cover);
        Assert.Equal(["custom:alt.jpg"], _art.Deleted);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_Applied, "Kupferwald"), vm.StatusMessage);
    });

    [Fact]
    public void An_online_pick_without_data_is_never_written_as_automatic() => Sta.Run(async () =>
    {
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        context.ConfigService.Update(c => c.FindGame(SteamKey)!.Artwork = new GameArtworkChoice { Hero = "alt:hero_2.jpg" });
        await picker.Online.SearchAsync(ArtworkKind.Hero);
        picker.Selected = picker.Online.Results[0];

        var outcome = await picker.ApplyWithOutcomeAsync(); // ohne PrepareApplyAsync

        Assert.Equal(ApplyOutcome.Unchanged, outcome);
        Assert.Equal("alt:hero_2.jpg", context.Config.FindGame(SteamKey)!.Artwork!.Hero);
        Assert.Empty(_art.SavedData);
    });

    [Fact]
    public void Cancel_during_the_download_writes_nothing() => Sta.Run(async () =>
    {
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        picker.Selected = picker.Online.Results[0];
        _sgdb.Session!.HoldDownloads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepare = picker.PrepareApplyAsync();

        picker.Dispose();
        Assert.False(await prepare);

        Assert.Empty(_art.SavedData);
        Assert.Null(context.Config.FindGame(SteamKey)!.Artwork);
        Assert.Null(picker.Online.Results.FirstOrDefault());
    });

    [Fact]
    public void Closing_cancels_the_session_and_releases_unchosen_previews_but_local_apply_still_works() => Sta.Run(async () =>
    {
        _art.Candidates[ArtworkKind.Cover] = [new ArtworkCandidate("alt:cover_1.jpg", ArtworkKind.Cover, ArtworkOrigin.Alternative, "Steam 1", null, null, 600, 900, IsCurrent: false)];
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.LoadAsync();
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        await WaitFor(() => picker.Online.Results[2].Preview is not null && picker.Online.Results[1].Preview is not null);
        var chosen = picker.Online.Results[1];
        var other = picker.Online.Results[2];
        picker.Selected = chosen;

        picker.CloseSession();

        Assert.True(_sgdb.Session!.Disposed);
        Assert.Empty(picker.Online.Results);
        Assert.NotNull(chosen.Preview);
        Assert.Null(other.Preview);
        Assert.Equal(0, _sgdb.Subscribers);

        picker.Dispose();
        picker.Selected = picker.Candidates.Single(c => c.Token == "alt:cover_1.jpg");
        Assert.True(await picker.ApplyAsync());
        Assert.Equal("alt:cover_1.jpg", context.Config.FindGame(SteamKey)!.Artwork!.Cover);
    });

    [Fact]
    public void The_settings_link_closes_without_changes_opens_the_artwork_section_and_names_lost_choices() => Sta.Run(async () =>
    {
        _sgdb.Key = SteamGridDbKeyState.Missing;
        _art.Candidates[ArtworkKind.Hero] = [new ArtworkCandidate("alt:hero_1.jpg", ArtworkKind.Hero, ArtworkOrigin.Alternative, "Steam 1", null, null, 1920, 620, IsCurrent: false)];
        var (context, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        await picker.LoadAsync();
        Assert.Equal(LibraryStrings.Picker_SgdbOpenSettings, picker.Online.SettingsLinkText);
        picker.Selected = picker.Candidates.Single(c => c.Token == "alt:hero_1.jpg");
        Assert.Equal(LibraryStrings.Picker_SgdbOpenSettingsDiscard, picker.Online.SettingsLinkText);

        var closed = 0;
        vm.SelectedGame = vm.Games.Single(g => g.Key == SteamKey);
        _dialogs.OnArtworkPicker = p =>
        {
            p.CloseRequested += (_, _) => closed++;
            p.LoadAsync().GetAwaiter().GetResult();
            p.Selected = p.Candidates.Single(c => c.Token == "alt:hero_1.jpg");
            p.Online.OpenSettingsCommand.Execute(null);
            return false; // der Dialog schließt mit „false“
        };
        await Run(vm.ChooseArtworkCommand, "Hero");

        Assert.Equal(1, closed);
        Assert.True(_dialogs.Pickers[^1].SettingsRequested);
        Assert.Equal([SettingsTarget.Artwork], context.SettingsTargets);
        Assert.Null(context.Config.FindGame(SteamKey)!.Artwork);
    });

    [Fact]
    public void Removing_the_key_while_open_switches_every_tab_to_the_hint() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        await picker.Online.SearchAsync(ArtworkKind.Cover);
        var session = _sgdb.Session!;

        _sgdb.Key = SteamGridDbKeyState.Missing;
        _sgdb.RaiseChanged();

        Assert.Equal(SteamGridDbSearchState.NoKey, picker.Online.State);
        Assert.Empty(picker.Online.Results);
        Assert.True(session.Disposed);
        foreach (var kind in new[] { ArtworkKind.Hero, ArtworkKind.Logo })
        {
            picker.Kind = kind;
            Assert.Equal(SteamGridDbSearchState.NoKey, picker.Online.State);
        }

        // Schlüssel wieder da: die Reiter sind wieder bereit.
        _sgdb.Key = SteamGridDbKeyState.Stored;
        _sgdb.RaiseChanged();
        Assert.Equal(SteamGridDbSearchState.Ready, picker.Online.State);
        Assert.Equal(2, _sgdb.Sessions.Count);
    });

    [Fact]
    public void Search_works_with_automatic_filling_off() => Sta.Run(async () =>
    {
        // Mit dem echten Dienst (Attrappe nur als Übertragung): Schalter aus, Schlüssel gespeichert.
        var rig = new HdrPilot.App.Tests.Artwork.SgdbRig();
        using var service = rig.Service();
        service.SaveKey(HdrPilot.App.Tests.Artwork.SgdbRig.Key);
        Assert.False(rig.Config.Settings.Artwork.SteamGridDb);
        var context = Context();
        context.SteamGridDb = service;
        using var vm = new LibraryViewModel(context, _dialogs);
        var picker = new ArtworkPickerViewModel(vm, vm.Games.Single(g => g.Key == SteamKey), ArtworkKind.Cover);
        Assert.Equal(0, service.HandlersCreated);

        await picker.Online.SearchAsync(ArtworkKind.Cover);
        await WaitFor(() => picker.Online.Results.All(r => r.PreviewState == PreviewState.Loaded));

        Assert.Equal(24, picker.Online.Results.Count);
        Assert.Equal(1, service.HandlersCreated);
        picker.Dispose();
        Assert.Equal(25, service.Status.LastSearchRequests); // 1 Seite, 24 Vorschauen
    });

    [Fact]
    public void Late_results_after_close_are_ignored() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Hero);
        using var _vm = vm;
        _sgdb.Session!.HoldApi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = picker.Online.SearchAsync(ArtworkKind.Hero);

        picker.CloseSession();
        _sgdb.Session.HoldApi.SetResult();
        await search;

        Assert.Empty(picker.Online.Results);
        Assert.NotEqual(SteamGridDbSearchState.Results, picker.Online.State);
        Assert.True(picker.Online.IsClosed);
    });

    [Fact]
    public void Only_state_changes_and_the_finished_batch_reach_the_announcement() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        var announced = Track(picker.Online, nameof(SteamGridDbSearchViewModel.Announcement));

        await picker.Online.SearchAsync(ArtworkKind.Cover);
        await WaitFor(() => picker.Online.ProgressText is null && picker.Online.Results.All(r => r.PreviewState is PreviewState.Loaded or PreviewState.Missing));

        Assert.Equal(
            [
                LibraryText.Format(LibraryStrings.Picker_SgdbLoading, LibraryStrings.Picker_KindPluralCover),
                LibraryText.Format(LibraryStrings.Picker_SgdbResults, 24, LibraryStrings.Picker_KindPluralCover),
                LibraryText.Format(LibraryStrings.Picker_SgdbPreviewProgress, 22, 22),
            ],
            announced);
    });

    [Fact]
    public void The_search_button_stays_focusable_and_busy_while_loading() => Sta.Run(async () =>
    {
        var (_, vm, picker) = Open(SteamKey, ArtworkKind.Cover);
        using var _vm = vm;
        var online = picker.Online;
        Assert.Equal(LibraryStrings.Picker_SgdbSearch, online.SearchButtonText);
        Assert.Equal(LibraryText.Format(LibraryStrings.Picker_SgdbSearchName, LibraryStrings.Picker_KindPluralCover), online.SearchAutomationName);
        _sgdb.Session!.HoldApi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        online.SearchCommand.Execute(null);
        Assert.True(online.ShowSearchButton);
        Assert.True(online.IsSearchBusy);
        Assert.True(online.ShowSkeleton);
        Assert.True(online.SearchCommand.CanExecute(null)); // bleibt bedienbar (und damit fokussierbar)
        Assert.Equal(LibraryStrings.Picker_SgdbSearching, online.SearchButtonText);
        Assert.Equal(LibraryStrings.Picker_SgdbSearching, online.SearchItemStatus);
        online.SearchCommand.Execute(null); // wird übergangen
        _sgdb.Session.HoldApi.SetResult();
        await WaitFor(() => online.State == SteamGridDbSearchState.Results);

        Assert.Single(_sgdb.Session.ApiCalls);
        Assert.False(online.ShowSearchButton);
        Assert.Null(online.SearchItemStatus);
    });

    private static Exception Error(string kind) => kind switch
    {
        "unauthorized" => new SteamGridDbException(SteamGridDbErrorKind.Unauthorized, "Test"),
        "rateLimited" => new SteamGridDbException(SteamGridDbErrorKind.RateLimited, "Test"),
        "offline" => new SteamGridDbException(SteamGridDbErrorKind.Network, "Test"),
        "timeout" => Timeout(),
        "invalid" => new SteamGridDbException(SteamGridDbErrorKind.InvalidResponse, "Test"),
        "blocked" => new SteamGridDbException(SteamGridDbErrorKind.Blocked, "Test"),
        "budget" => new SteamGridDbException(SteamGridDbErrorKind.RequestLimit, "Test"),
        _ => new SteamGridDbException(SteamGridDbErrorKind.HttpError, "Test"),
    };

    private static SteamGridDbException Timeout()
    {
        var error = new SteamGridDbException(SteamGridDbErrorKind.Network, "Test");
        error.Data["timeout"] = true;
        return error;
    }

    private static async Task Run(System.Windows.Input.ICommand command, object? parameter)
    {
        command.Execute(parameter);
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
        }
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static List<string?> Track(INotifyPropertyChanged source, string property)
    {
        var values = new List<string?>();
        source.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == property)
            {
                var value = sender!.GetType().GetProperty(property)!.GetValue(sender) as string;
                if (values.Count == 0 || values[^1] != value)
                {
                    values.Add(value);
                }
            }
        };
        return values;
    }

    private TestLibraryContext Context(ISteamGridDbService? sgdb = null)
    {
        var context = new TestLibraryContext(
            Game(GameSource.Gog, "1207658930", "Kupferwald", isNew: false),
            Game(GameSource.Steam, "9990150", "Kupferwald", isNew: false))
        {
            Artwork = _art,
        };
        context.SteamGridDb = sgdb ?? _sgdb;
        return context;
    }

    private (TestLibraryContext Context, LibraryViewModel Vm, ArtworkPickerViewModel Picker) Open(string key, ArtworkKind kind)
    {
        var context = Context();
        var vm = new LibraryViewModel(context, _dialogs);
        var picker = new ArtworkPickerViewModel(vm, vm.Games.Single(g => g.Key == key), kind);
        return (context, vm, picker);
    }
}
