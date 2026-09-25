using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Career;

namespace StreetRodAC.Tests;

/// <summary>The career's loose ends: the events in the paper, the counters they read, the victory and the prize camshaft</summary>
public class CareerGapsTests
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));

    private static RaceEventService Events() => new(new CarFilterService());

    [Fact]
    public void A_new_career_sees_the_events_its_starting_reputation_opens()
    {
        var game = GameState.CreateNew("Tester");
        var events = Events();

        // Before the counters followed the player, reputation read 0 and hid every event that asked for any
        Assert.DoesNotContain(events.GetEligibleEvents(game.Career), e => e.Id == "fifties_fever");

        game.Career.SyncStanding(game.Player);

        Assert.Equal(game.Player.Stats.Reputation, game.Career.GetCounter(MilestoneTrigger.ReputationReached));
        Assert.Contains(events.GetEligibleEvents(game.Career), e => e.Id == "fifties_fever");
        Assert.Contains(events.GetEligibleEvents(game.Career), e => e.Id == "classic_showdown");
    }

    [Fact]
    public void The_paper_never_has_more_than_five_invitations_open()
    {
        var game = GameState.CreateNew("Tester");
        game.Career.SyncStanding(game.Player);
        game.Career.CompletedMilestones.Add("street_cred");
        game.Career.CompletedMilestones.Add("pink_slip_hunter");
        var events = Events();

        var day = game.Date;
        for (var i = 0; i < 30; i++, day = day.AddDays(1))
        {
            events.GenerateEvents(game.Career, day);
            Assert.True(game.Career.ActiveEvents.Count(e => e.IsAvailable(day)) <= RaceEventService.MaxOpenEvents);
        }
    }

    [Fact]
    public void The_era_and_make_shows_are_road_races()
    {
        var events = Events().GetAllEventDefinitions().ToDictionary(e => e.Id);

        foreach (var id in new[] { "classic_showdown", "fifties_fever", "sixties_showdown", "european_invasion", "chevy_challenge" })
            Assert.Equal(RaceType.Circuit, events[id].RaceType);
        foreach (var id in new[] { "ford_fanatics", "budget_brawl", "muscle_madness", "high_stakes", "underdog_challenge" })
            Assert.Equal(RaceType.DragRace, events[id].RaceType);
    }

    /// <summary>Reputation victory reached (the counter at its target) and the King beaten, both at once</summary>
    private static GameState TwoPathsReached()
    {
        var game = GameState.CreateNew("Tester");
        game.Career.SetCounter(MilestoneTrigger.ReputationReached, 100);
        game.Career.SetCounter(MilestoneTrigger.TotalWins, 50);
        game.Career.RecordDefeatedOpponent("The King");
        return game;
    }

    [Fact]
    public void Only_the_chosen_path_wins()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();
        var king = victories.GetVictoryCondition("King")!;
        Assert.True(king.IsAchieved(game.Career));

        game.Career.ActiveVictoryType = "PinkSlipCollector";
        Assert.Null(victories.CheckForVictory(game));

        game.Career.ActiveVictoryType = "King";
        Assert.Equal("King", victories.CheckForVictory(game)!.VictoryType);
    }

    [Fact]
    public void With_no_path_chosen_the_first_reached_wins()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();

        Assert.NotNull(victories.CheckForVictory(game));
    }

    [Fact]
    public void A_victory_is_claimed_once_and_the_game_goes_on()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();
        game.Career.ActiveVictoryType = "King";

        var won = victories.ClaimVictory(game);

        Assert.Equal("King", won!.VictoryType);
        Assert.True(game.Career.HasWonGame);
        Assert.Equal("King", game.Career.WinningVictoryType);
        Assert.Null(victories.ClaimVictory(game));
    }

    [Fact]
    public void The_victory_message_is_shown_as_the_victory_screen()
    {
        var victories = new VictoryConditionService();
        var game = TwoPathsReached();
        var king = victories.GetVictoryCondition("King")!;

        var message = CareerProgressService.VictoryMessage(game, king, [], []);

        Assert.NotNull(message.Victory);
        Assert.Equal(king.Name, message.Victory!.VictoryName);
        Assert.Contains(message.Victory.Stats, s => s.Label == "Reputation");
    }

    [Fact]
    public void The_prize_camshaft_fits_the_engine_and_beats_its_own()
    {
        var catalog = Catalog.Value;
        var build = EngineBuildIndex.Create(catalog).Runnable
            .Single(b => b.Build.Id.EndsWith("chrysler-v8-pack/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var engine = EngineFactory.CreateStock(catalog, build, 0.8, new Random(1))!.Root;
        var current = engine.SelfAndDescendants()
            .Select(p => catalog.Get(p.DefinitionId))
            .First(d => d != null && PartKinds.GroupOf(d) == PrizeParts.CamshaftGroup)!;

        var prize = PrizeParts.BestCamshaft(catalog, engine);

        Assert.NotNull(prize);
        var (cam, count) = prize!.Value;
        Assert.Equal(PrizeParts.CamshaftGroup, PartKinds.GroupOf(cam));
        Assert.True(PartPricing.NewPrice(cam) > PartPricing.NewPrice(current));
        Assert.Equal(1, count);
    }

    [Fact]
    public void No_engine_no_prize_camshaft()
    {
        Assert.Null(PrizeParts.BestCamshaft(Catalog.Value, null));
    }
}
