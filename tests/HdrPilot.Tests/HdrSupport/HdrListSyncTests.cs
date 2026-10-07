using System.Net;
using HdrPilot.Core.Config;
using HdrPilot.Core.Games;
using HdrPilot.Core.HdrSupport;
using HdrPilot.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace HdrPilot.Tests.HdrSupport;

/// <summary>Abgleich der HDR-Liste gegen eine nachgebildete PCGamingWiki-Schnittstelle (nie das Internet).</summary>
public sealed class HdrListSyncTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly AppPaths _paths = AppPaths.InDirectory(@"C:\Daten\HdrPilot");
    private readonly FakeClock _clock = new();
    private readonly ListLogger<HdrListSyncTests> _log = new();
    private readonly FakeWiki _wiki = new();
    private readonly List<FakeHttpHandler> _handlers = [];
    private readonly HdrSupportCache _cache;
    private readonly HdrListSync _sync;

    public HdrListSyncTests()
    {
        _cache = new HdrSupportCache(_fs, _paths);
        _sync = new HdrListSync(_cache, _clock, _log);
    }

    private List<PcgwRequest> Requests => _handlers.SelectMany(h => h.Requests).ToList();

    private static HdrListGame Steam(string id, string name) => new(GameKey.Create(GameSource.Steam, id), GameSource.Steam, id, name);

    private static HdrListGame Gog(string id, string name) => new(GameKey.Create(GameSource.Gog, id), GameSource.Gog, id, name);

    private static HdrListGame Other(GameSource source, string id, string name, bool installed = true, DateTimeOffset? lastPlayed = null) =>
        new(GameKey.Create(source, id), source, id, name, installed, lastPlayed);

    private HdrListRunResult Run(IReadOnlyList<HdrListGame> games, PcgwAccount? account = null, bool force = false) =>
        _sync.Run(games, account, () =>
        {
            var handler = _wiki.CreateHandler();
            _handlers.Add(handler);
            return new PcgwClient(handler, _clock, "HdrPilot/1.0.0 (Windows desktop app; HDR list)", TimeSpan.FromSeconds(30));
        }, force, CancellationToken.None);

    private HdrSupportEntry Entry(string key) => _cache.Load().Games[key];

    [Fact]
    public void Steam_games_are_found_by_title_and_their_app_id_confirmed()
    {
        _wiki.Page("Dying Light: The Beast", "3008130", "true")
            .Page("Dead Island 2", "934700", "true")
            .Page("Cyberpunk 2077", "1091500", "limited")
            .Page("Palworld", "1623730", "false")
            .Page("Battlefield 6", "2807960", "true");
        HdrListGame[] games =
        [
            Steam("3008130", "Dying Light: The Beast"), Steam("934700", "Dead Island 2"), Steam("1091500", "Cyberpunk 2077"),
            Other(GameSource.Xbox, "Palworld_pfn", "Palworld"), Other(GameSource.EA, "Origin.OFR.50.0005", "Battlefield™ 6"),
        ];

        var result = Run(games);

        Assert.Equal(HdrListRunOutcome.Completed, result.Outcome);
        Assert.Equal((1, 5, 5), (result.Requests, result.Checked, result.Found));
        Assert.Equal(HdrSupportStatus.Native, Entry("Steam:3008130").Status);
        Assert.Equal("Dying Light: The Beast", Entry("Steam:3008130").PcgwTitle);
        Assert.Equal(HdrSupportStatus.Native, Entry("Steam:934700").Status);
        Assert.Equal(HdrSupportStatus.Limited, Entry("Steam:1091500").Status);
        Assert.Equal(HdrSupportStatus.None, Entry("Xbox:Palworld_pfn").Status);
        Assert.Equal(HdrSupportStatus.Native, Entry("EA:Origin.OFR.50.0005").Status); // ™ entfernt
        Assert.Equal(_clock.UtcNow, Entry("Steam:934700").CheckedAt);

        var data = _cache.Load();
        Assert.Equal(_clock.UtcNow, data.LastSuccessAt);
        Assert.Null(data.LastError);
        Assert.False(data.AccountMode);
        Assert.Equal(new Dictionary<string, HdrSupportStatus>
        {
            ["Steam:3008130"] = HdrSupportStatus.Native, ["Steam:934700"] = HdrSupportStatus.Native, ["Steam:1091500"] = HdrSupportStatus.Limited,
            ["Xbox:Palworld_pfn"] = HdrSupportStatus.None, ["EA:Origin.OFR.50.0005"] = HdrSupportStatus.Native,
        }, _cache.LoadStatuses().Where(s => s.Value != HdrSupportStatus.Unknown).ToDictionary());
        Assert.True(_log.Contains(LogLevel.Information, "HDR-Liste abgeglichen (ohne Konto): 1 Anfragen, 5 Spiele geprüft"));
    }

    [Fact]
    public void Steam_page_with_another_app_id_does_not_count_and_the_search_finds_the_right_one()
    {
        _wiki.Page("Doom", "2280", "false").Page("Doom (2016)", "379720", "true");
        _wiki.Search["Doom"] = "Doom (2016)";

        var result = Run([Steam("379720", "Doom")]);

        Assert.Equal(2, result.Requests);
        Assert.Equal(HdrSupportStatus.Native, Entry("Steam:379720").Status);
        Assert.Equal("Doom (2016)", Entry("Steam:379720").PcgwTitle);
        Assert.False(Entry("Steam:379720").SearchPending);
        Assert.Equal("search", Requests[1]["generator"]);
        Assert.Equal([TimeSpan.FromSeconds(2)], _clock.Delays); // Pause zwischen den Anfragen
    }

    [Fact]
    public void Avatar_without_colon_is_found_by_the_search_like_on_the_real_wiki()
    {
        _wiki.Page("Avatar: Frontiers of Pandora", "2840770", "true");
        _wiki.Search["Avatar Frontiers of Pandora"] = "Avatar: Frontiers of Pandora";

        var result = Run([Steam("2840770", "Avatar Frontiers of Pandora™")]);

        Assert.Equal("Avatar Frontiers of Pandora", Requests[0]["titles"]);
        Assert.Equal("Avatar Frontiers of Pandora", Requests[1]["gsrsearch"]);
        Assert.Equal((2, 1, 1), (result.Requests, result.Checked, result.Found)); // einmal gezählt, obwohl zweimal geprüft
        Assert.Equal(HdrSupportStatus.Native, Entry("Steam:2840770").Status);
        Assert.Equal("Avatar: Frontiers of Pandora", Entry("Steam:2840770").PcgwTitle);
    }

    [Fact]
    public void Search_hit_with_another_app_id_is_not_taken()
    {
        _wiki.Page("Some Other Game", "1", "true");
        _wiki.Search["Mein Spiel"] = "Some Other Game";

        Run([Steam("42", "Mein Spiel")]);

        var entry = Entry("Steam:42");
        Assert.Equal(HdrSupportStatus.Unknown, entry.Status);
        Assert.Null(entry.PcgwTitle);
        Assert.False(entry.SearchPending);
    }

    [Fact]
    public void Dash_names_also_try_the_colon_title_in_the_same_request()
    {
        _wiki.Page("Dying Light: The Beast", "3008130", "true");

        var result = Run([Other(GameSource.Epic, "beast", "Dying Light - The Beast")]);

        Assert.Equal(1, result.Requests);
        Assert.Equal("Dying Light - The Beast|Dying Light: The Beast", Requests[0]["titles"]);
        Assert.Equal(HdrSupportStatus.Native, Entry("Epic:beast").Status);
    }

    [Fact]
    public void Wiki_normalization_and_redirects_are_followed()
    {
        _wiki.Page("Doom Eternal", "782330", "true");
        _wiki.Redirects["DOOM Eternal"] = "Doom Eternal";

        Run([Other(GameSource.Xbox, "doom", "DOOM Eternal"), Other(GameSource.Ubisoft, "x", "doom Eternal")]);

        Assert.Equal("Doom Eternal", Entry("Xbox:doom").PcgwTitle);
        Assert.Equal(HdrSupportStatus.Native, Entry("Ubisoft:x").Status); // erster Buchstabe egal
    }

    [Fact]
    public void Gog_games_need_their_gog_id_when_the_page_lists_any()
    {
        _wiki.Page("Mit ID", null, "true", gog: "1207658924")
            .Page("Andere ID", null, "true", gog: "111")
            .Page("Ohne ID", null, "limited");

        Run([Gog("1207658924", "Mit ID"), Gog("222", "Andere ID"), Gog("333", "Ohne ID")]);

        Assert.Equal(HdrSupportStatus.Native, Entry("Gog:1207658924").Status);
        Assert.Equal(HdrSupportStatus.Unknown, Entry("Gog:222").Status);
        Assert.False(Entry("Gog:222").SearchPending); // gesucht, nichts Passendes
        Assert.Equal(HdrSupportStatus.Limited, Entry("Gog:333").Status);
        Assert.Equal(["Andere ID"], Requests.Where(r => r["generator"] == "search").Select(r => r["gsrsearch"]));
    }

    [Fact]
    public void Ubisoft_avatar_is_found_by_the_search_with_a_title_check_ignoring_punctuation()
    {
        // Wie in der echten Bibliothek: Avatar kommt aus Ubisoft Connect, ohne Laden-ID im Wiki abgleichbar.
        _wiki.Page("Avatar: Frontiers of Pandora", "2840770", "true");
        _wiki.Search["Avatar Frontiers of Pandora"] = "Avatar: Frontiers of Pandora";

        var result = Run([Other(GameSource.Ubisoft, "4740", "Avatar Frontiers of Pandora™")]);

        Assert.Equal((2, 1, 1), (result.Requests, result.Checked, result.Found));
        Assert.Equal(HdrSupportStatus.Native, Entry("Ubisoft:4740").Status);
        Assert.Equal("Avatar: Frontiers of Pandora", Entry("Ubisoft:4740").PcgwTitle);
    }

    [Fact]
    public void Search_hit_with_another_title_is_not_taken_for_games_without_store_ids()
    {
        _wiki.Page("Doom (2016)", "379720", "true").Page("Gog Spiel Remastered", null, "true", gog: "5");
        _wiki.Search["Doom"] = "Doom (2016)";
        _wiki.Search["Gog Spiel"] = "Gog Spiel Remastered";

        Run([Other(GameSource.Epic, "doom", "Doom"), Gog("5", "Gog Spiel"), Gog("6", "Gog Spiel Remastered")]);

        Assert.Equal(HdrSupportStatus.Unknown, Entry("Epic:doom").Status); // „Doom (2016)“ ist nicht „Doom“
        Assert.Equal(HdrSupportStatus.Native, Entry("Gog:5").Status); // GOG-ID passt
        Assert.Equal(HdrSupportStatus.Unknown, Entry("Gog:6").Status); // Titel gefunden, aber andere GOG-ID
    }

    [Fact]
    public void Installed_and_recently_played_games_are_checked_and_searched_first()
    {
        var day = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        HdrListGame[] games =
        [
            Other(GameSource.Epic, "a", "A alt", installed: false),
            Other(GameSource.Epic, "b", "B selten", lastPlayed: day),
            Other(GameSource.Epic, "c", "C gestern", lastPlayed: day.AddDays(5)),
            Other(GameSource.Epic, "d", "D nie"),
        ];

        Run(games);

        Assert.Equal("C gestern|B selten|D nie|A alt", Requests[0]["titles"]);
        Assert.Equal(["C gestern", "B selten"], Requests.Where(r => r["generator"] == "search").Select(r => r["gsrsearch"]));
    }

    [Fact]
    public void Account_mode_matches_names_ignoring_punctuation_only_when_unique()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "geheim";
        _wiki.CargoRows.Add(("Avatar: Frontiers of Pandora", "2840770", null, "true"));
        _wiki.CargoRows.Add(("Spider-Man", "1", null, "true"));
        _wiki.CargoRows.Add(("Spiderman", "2", null, "limited"));

        Run([Other(GameSource.Ubisoft, "4740", "Avatar Frontiers of Pandora™"), Other(GameSource.Epic, "s", "Spider Man")], new PcgwAccount("Dante_GE@HdrPilot", "geheim"));

        Assert.Equal(HdrSupportStatus.Native, Entry("Ubisoft:4740").Status);
        Assert.Equal(HdrSupportStatus.Unknown, Entry("Epic:s").Status); // zwei Einträge kämen in Frage
    }

    [Fact]
    public void At_most_two_requests_of_fifty_titles_never_checked_games_first_then_the_oldest()
    {
        var games = Enumerable.Range(0, 120).Select(i => Other(GameSource.Epic, "g" + i.ToString("D3"), "Spiel " + i.ToString("D3"))).ToList();
        games.ForEach(g => _wiki.Page(g.Name, null, "false")); // alle gefunden: keine Suchen

        var first = Run(games);

        Assert.Equal(2, first.Requests);
        Assert.All(Requests.Take(2), r => Assert.Equal(50, r["titles"]!.Split('|').Length));
        Assert.Equal(100, _cache.Load().Games.Count);
        Assert.Equal(100, first.Checked);

        // Nächster Start nach mehr als einer Stunde: die restlichen 20, sonst nichts (alles jünger als 14 Tage).
        _clock.Advance(TimeSpan.FromHours(2));
        var second = Run(games);
        Assert.Equal(1, second.Requests);
        Assert.Equal(20, Requests[2]["titles"]!.Split('|').Length);
        Assert.Equal(120, _cache.Load().Games.Count);

        // Ohne Fälliges: kein einziger Netzzugriff.
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, Run(games).Requests);

        // Nach 14 Tagen: die ältesten zuerst (die ersten 100), wieder höchstens 100 Titel.
        _clock.Advance(TimeSpan.FromDays(14) - TimeSpan.FromHours(3));
        var fourth = Run(games);
        Assert.Equal(2, fourth.Requests);
        var requested = Requests.Skip(3).SelectMany(r => r["titles"]!.Split('|')).ToList();
        Assert.Equal(100, requested.Count);
        Assert.Contains("Spiel 000", requested);
        Assert.DoesNotContain("Spiel 110", requested);
    }

    [Fact]
    public void A_second_run_within_an_hour_is_skipped_unless_forced()
    {
        _wiki.Page("A", null, "true").Page("B", null, "false");
        HdrListGame[] games = [Other(GameSource.Epic, "1", "A")];
        Run(games);
        _clock.Advance(TimeSpan.FromMinutes(59));
        games = [Other(GameSource.Epic, "1", "A"), Other(GameSource.Epic, "2", "B")];

        var skipped = Run(games);

        Assert.Equal(HdrListRunOutcome.Skipped, skipped.Outcome);
        Assert.Single(_handlers); // kein Client für den übersprungenen Lauf
        var forced = Run(games, force: true);
        Assert.Equal(HdrListRunOutcome.Completed, forced.Outcome);
        Assert.Equal("B", Requests[^1]["titles"]);

        _clock.Advance(TimeSpan.FromMinutes(61));
        Assert.NotEqual(HdrListRunOutcome.Skipped, Run(games).Outcome);
    }

    [Fact]
    public void Rate_limit_stops_the_run_keeps_the_cache_and_logs_one_line()
    {
        _wiki.Page("A", "1", "true");
        HdrListGame[] games = [Steam("1", "A")];
        Run(games);
        var checkedAt = Entry("Steam:1").CheckedAt;
        _clock.Advance(TimeSpan.FromDays(15));
        _wiki.Failures[_wiki.RequestNumber + 1] = () => FakeHttpHandler.Status(HttpStatusCode.TooManyRequests);
        var warningsBefore = _log.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = Run(games);

        Assert.Equal(HdrListRunOutcome.Failed, result.Outcome);
        Assert.Equal(PcgwErrorKind.RateLimited, result.Error);
        Assert.Equal(1, result.Requests);
        var data = _cache.Load();
        Assert.Equal(PcgwErrorKind.RateLimited, data.LastError);
        Assert.Equal(HdrSupportStatus.Native, data.Games["Steam:1"].Status); // Zwischenspeicher bleibt
        Assert.Equal(checkedAt, data.Games["Steam:1"].CheckedAt);
        Assert.Equal(_clock.UtcNow, data.LastRunAt);
        Assert.Equal(1, _log.Entries.Count(e => e.Level == LogLevel.Warning) - warningsBefore);
        Assert.True(_log.Contains(LogLevel.Warning, "RateLimited"));
    }

    [Fact]
    public void Network_error_in_the_second_batch_keeps_the_results_of_the_first()
    {
        var games = Enumerable.Range(0, 80).Select(i => Other(GameSource.Epic, "g" + i.ToString("D2"), "Spiel " + i.ToString("D2"))).ToList();
        _wiki.Page("Spiel 00", null, "true");
        _wiki.Failures[2] = () => throw new HttpRequestException("Verbindung getrennt");

        var result = Run(games);

        Assert.Equal(PcgwErrorKind.Network, result.Error);
        Assert.Equal(50, _cache.Load().Games.Count);
        Assert.Equal(HdrSupportStatus.Native, Entry("Epic:g00").Status);
        Assert.Equal(PcgwErrorKind.Network, _cache.Load().LastError);
        Assert.Null(_cache.Load().LastSuccessAt);
    }

    [Fact]
    public void Renamed_games_are_checked_again_and_removed_games_dropped()
    {
        _wiki.Page("Neuer Name", null, "true");
        Run([Other(GameSource.Epic, "x", "Alter Name"), Other(GameSource.Epic, "y", "Weg")]);
        Assert.Equal(HdrSupportStatus.Unknown, Entry("Epic:x").Status);
        _clock.Advance(TimeSpan.FromHours(2));

        Run([Other(GameSource.Epic, "x", "Neuer Name")]);

        Assert.Equal("Neuer Name", Requests[^1]["titles"]);
        Assert.Equal(HdrSupportStatus.Native, Entry("Epic:x").Status);
        Assert.False(_cache.Load().Games.ContainsKey("Epic:y"));
    }

    [Fact]
    public void Names_that_cannot_be_queried_are_marked_without_a_request()
    {
        var result = Run([Other(GameSource.Manual, "m", "Spiel | Sammlung")]);

        Assert.Equal(0, result.Requests);
        Assert.Equal(HdrSupportStatus.Unknown, Entry("Manual:m").Status);
    }

    [Fact]
    public void Account_mode_logs_in_loads_every_page_and_matches_locally_without_sending_names()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "geheim";
        for (var i = 0; i < 1100; i++)
        {
            _wiki.CargoRows.Add(("Spiel " + i, (100000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), null, "true"));
        }

        _wiki.CargoRows.Add(("Cyberpunk 2077", "1091500", "1423049311,2093619782", "limited"));
        _wiki.CargoRows.Add(("Dead Island 2", "934700", null, "true"));
        _wiki.CargoRows.Add(("Mod-Spiel", "777", null, "hackable"));
        HdrListGame[] games =
        [
            Steam("1091500", "Cyberpunk 2077: Ultimate Edition"), Gog("1423049311", "Cyberpunk 2077"), Other(GameSource.Epic, "Crow", "DEAD ISLAND 2"),
            Steam("777", "Mod-Spiel"), Steam("1", "Nicht in der Liste"), Steam("100005", "Spiel 5"),
        ];

        var result = Run(games, new PcgwAccount("Dante_GE@HdrPilot", "geheim"));

        Assert.Equal(HdrListRunOutcome.Completed, result.Outcome);
        Assert.True(result.AccountMode);
        Assert.Equal(["query", "login", "cargoquery", "cargoquery", "cargoquery"], Requests.Select(r => r.Action));
        Assert.Equal(["0", "500", "1000"], Requests.Skip(2).Select(r => r["offset"]));
        Assert.Equal(4, _clock.Delays.Count); // 2 s zwischen allen fünf Anfragen
        Assert.All(Requests, r =>
        {
            Assert.DoesNotContain("Cyberpunk", r.Raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Nicht in der Liste", r.Raw, StringComparison.Ordinal);
        });

        Assert.Equal(HdrSupportStatus.Limited, Entry("Steam:1091500").Status); // über die AppID, nicht den Namen
        Assert.Equal(HdrSupportStatus.Limited, Entry("Gog:1423049311").Status);
        Assert.Equal(HdrSupportStatus.Native, Entry("Epic:Crow").Status); // Name ohne Groß-/Kleinschreibung
        Assert.Equal(HdrSupportStatus.ModOnly, Entry("Steam:777").Status);
        Assert.Equal(HdrSupportStatus.Unknown, Entry("Steam:1").Status);
        Assert.Equal(HdrSupportStatus.Native, Entry("Steam:100005").Status);

        var catalog = _cache.LoadCatalog();
        Assert.NotNull(catalog);
        Assert.Equal(1103, catalog.Entries.Count);
        Assert.True(_cache.Load().AccountMode);
        Assert.True(_fs.FileExists(_paths.HdrCatalogFile));
    }

    [Fact]
    public void Wrong_bot_password_is_reported_and_no_list_is_requested()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "richtig";

        var result = Run([Steam("1", "A")], new PcgwAccount("Dante_GE@HdrPilot", "falsch"));

        Assert.Equal(PcgwErrorKind.LoginFailed, result.Error);
        Assert.DoesNotContain(Requests, r => r.Action == "cargoquery");
        Assert.Equal(PcgwErrorKind.LoginFailed, _cache.Load().LastError);
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("falsch", StringComparison.Ordinal) || e.Message.Contains("richtig", StringComparison.Ordinal));
    }

    [Fact]
    public void Bot_password_without_cargo_right_is_permission_denied()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "geheim";
        _wiki.CargoGranted = false;

        var result = Run([Steam("1", "A")], new PcgwAccount("Dante_GE@HdrPilot", "geheim"));

        Assert.Equal(PcgwErrorKind.PermissionDenied, result.Error);
        Assert.Equal(PcgwErrorKind.PermissionDenied, _cache.Load().LastError);
    }

    [Fact]
    public void Without_network_new_games_are_matched_against_the_stored_list()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "geheim";
        _wiki.CargoRows.Add(("Dead Island 2", "934700", null, "true"));
        var account = new PcgwAccount("Dante_GE@HdrPilot", "geheim");
        Run([Steam("934700", "Dead Island 2")], account);
        _clock.Advance(TimeSpan.FromHours(2));
        _wiki.Failures[_wiki.RequestNumber + 1] = () => throw new HttpRequestException("offline");

        var result = Run([Steam("934700", "Dead Island 2"), Other(GameSource.Epic, "Crow", "Dead Island 2")], account);

        Assert.Equal(PcgwErrorKind.Network, result.Error);
        Assert.Equal(HdrSupportStatus.Native, Entry("Epic:Crow").Status);
        Assert.Equal(1, result.Found);
    }

    [Fact]
    public void Account_test_needs_login_and_one_cargo_row()
    {
        _wiki.Accounts["Dante_GE@HdrPilot"] = "geheim";
        var handler = _wiki.CreateHandler();
        using var client = new PcgwClient(handler, _clock, "UA", TimeSpan.FromSeconds(5));

        HdrListSync.TestAccount(client, new PcgwAccount("Dante_GE@HdrPilot", "geheim"), CancellationToken.None);

        Assert.Equal(["query", "login", "cargoquery"], handler.Requests.Select(r => r.Action));
        Assert.Equal("1", handler.Requests[2]["limit"]);
    }

    [Fact]
    public void Broken_cache_files_count_as_empty()
    {
        _fs.AddFile(_paths.HdrSupportFile, "{ kaputt");
        _fs.AddFile(_paths.HdrCatalogFile, "[]");

        Assert.Empty(_cache.Load().Games);
        Assert.Null(_cache.LoadCatalog());
        Assert.Empty(_cache.LoadStatuses());
    }

    [Fact]
    public void At_most_two_searches_per_run_the_rest_waits_for_the_next_start()
    {
        _wiki.Page("A", "1", "true");
        HdrListGame[] games = [Steam("1", "A"), Steam("2", "B"), Steam("3", "C"), Steam("4", "D")];

        var first = Run(games);

        Assert.Equal(3, first.Requests); // eine Titelabfrage, zwei Suchen
        Assert.Equal(["B", "C"], Requests.Where(r => r["generator"] == "search").Select(r => r["gsrsearch"]));
        Assert.True(Entry("Steam:4").SearchPending);
        Assert.False(Entry("Steam:2").SearchPending);
        var text = _fs.ReadAllText(_paths.HdrSupportFile);
        Assert.Contains("\"Steam:1\"", text, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"Native\"", text, StringComparison.Ordinal);
        Assert.Contains("\"pcgwTitle\": \"A\"", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("searchPending").Length - 1); // nur gespeichert, wenn true

        _clock.Advance(TimeSpan.FromHours(2));
        var second = Run(games);
        Assert.Equal(1, second.Requests); // nur noch die Suche für D, keine Titelabfrage
        Assert.Equal("D", Requests[^1]["gsrsearch"]);
        Assert.False(Entry("Steam:4").SearchPending);
    }
}
