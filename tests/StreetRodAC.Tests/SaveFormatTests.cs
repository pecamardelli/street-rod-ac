using LiteDB;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Storage;

namespace StreetRodAC.Tests;

/// <summary>
/// The save format: what a save stores and what an older save still loads as, through the real save path
/// (GameStateRepository on a SaveDatabase in a temp folder)
/// </summary>
public sealed class SaveFormatTests : IDisposable
{
    private const string Collection = "gamestate";

    private readonly TempDir _temp = new();
    private readonly SaveDatabase _database;
    private readonly GameStateRepository _repository;

    public SaveFormatTests()
    {
        _database = new SaveDatabase(_temp.Combine("Saves"));
        _repository = new GameStateRepository(null, _database);
    }

    public void Dispose()
    {
        _repository.Dispose();
        _temp.Dispose();
    }

    private static Opponent Rival(string name) => new()
    {
        Name = name,
        Nickname = string.Empty,
        DefinitionId = string.Empty,
        IsKing = false,
        Money = 321m,
        Status = RacerStatus.ReadyToRace
    };

    /// <summary>A game with something of every shape a save holds</summary>
    private static GameState FullShape()
    {
        var state = GameState.CreateNew("  Spaced Player  ");

        var rival = Rival("Ace");
        rival.Stats.Wins = 3;
        rival.Stats.Races = 4;
        var engine = new PartInstance("engine_v8") { ParentSlot = PartInstance.CarEngineSlot, Wear = 0.8, Tear = 0.9 };
        engine.Tuning["ignition"] = 0.25;
        var carb = new PartInstance("carb_4bbl") { ParentSlot = 12, OwnSlot = 3 };
        carb.Tuning["mixture"] = -0.5;
        engine.Children.Add(carb);
        var car = new Car("sr_car") { SkinId = string.Empty, Parts = [engine], PowerHp = 250 };
        rival.Cars.Add(car);
        state.Racers.AddRacer(rival);

        var retired = Rival("Old Timer");
        retired.Status = RacerStatus.Retired;
        state.Racers.AddRacer(retired);

        state.Player.Parts.Add(new PartInstance("cam_hot") { Tuning = { ["lift"] = 1.5 } });
        state.Career.MilestoneCounters[MilestoneTrigger.DragWins] = 7;
        state.Career.CompletedMilestones.Add("first_win");
        state.Career.DefeatedOpponentIds.Add("Ace");
        state.Career.CompletedEventIds.Add("event_1");
        state.Rules.Difficulty = Difficulty.Hard;
        state.PendingRace = new RaceContext { OpponentName = "Ace", CashWager = 50m, RaceType = RaceType.Sprint };
        return state;
    }

    private BsonDocument RawGame(string save) =>
        _database.Use(save, db => db.GetCollection(Collection).FindById(1));

    private void WriteRawGame(string save, BsonDocument document) =>
        _database.Use(save, db => db.GetCollection(Collection).Update(document));

    [Fact]
    public void A_full_game_round_trips_through_the_save()
    {
        _repository.Save(FullShape(), "Full");

        var loaded = _repository.Load("Full")!;

        var rival = Assert.IsType<Opponent>(loaded.Racers.ReadyToRace["Ace"]);
        Assert.Equal(321m, rival.Money);
        Assert.Equal(3, rival.Stats.Wins);
        Assert.IsType<Opponent>(loaded.Racers.Retired["Old Timer"]);

        // Empty strings stay empty, whitespace is not trimmed
        Assert.Equal(string.Empty, rival.Nickname);
        Assert.Equal(string.Empty, rival.DefinitionId);
        Assert.Equal(string.Empty, rival.Cars[0].SkinId);
        Assert.Equal("  Spaced Player  ", loaded.Player.Name);

        var engine = rival.Cars[0].Engine!;
        Assert.Equal("engine_v8", engine.DefinitionId);
        Assert.Equal(0.8, engine.Wear);
        Assert.Equal(0.25, engine.Tuning["ignition"]);
        var carb = Assert.Single(engine.Children);
        Assert.Equal(-0.5, carb.Tuning["mixture"]);
        Assert.Equal(3, carb.OwnSlot);
        Assert.Equal(250, rival.Cars[0].PowerHp);
        Assert.Equal(1.5, Assert.Single(loaded.Player.Parts).Tuning["lift"]);

        Assert.Equal(7, loaded.Career.MilestoneCounters[MilestoneTrigger.DragWins]);
        Assert.Contains("first_win", loaded.Career.CompletedMilestones);
        Assert.Contains("Ace", loaded.Career.DefeatedOpponentIds);
        Assert.Contains("event_1", loaded.Career.CompletedEventIds);
        Assert.Equal(Difficulty.Hard, loaded.Rules.Difficulty);
        Assert.Equal(RaceType.Sprint, loaded.PendingRace!.RaceType);
        Assert.Equal(string.Empty, loaded.PendingRace.TrackId);
        Assert.Equal(GameState.GetStartingDateTime(), loaded.Date);
    }

    [Fact]
    public void A_racer_is_tagged_with_a_short_stable_type_name()
    {
        _repository.Save(FullShape(), "Tags");

        var racers = RawGame("Tags")["Racers"].AsDocument;

        Assert.Equal("Opponent", racers["ReadyToRace"]["Ace"]["_type"].AsString);
        Assert.Equal("Opponent", racers["Retired"]["Old Timer"]["_type"].AsString);
    }

    [Theory]
    // As every save was written before the short names
    [InlineData("Street_Rod_AC.Models.GameState.Opponent, Street Rod AC")]
    // After a rename of the assembly and namespace that no longer exist
    [InlineData("StreetCorsa.Models.GameState.Opponent, Street Corsa")]
    public void A_save_with_an_assembly_qualified_type_name_still_loads(string oldTypeName)
    {
        _repository.Save(FullShape(), "Old");
        var game = RawGame("Old");
        game["Racers"]["ReadyToRace"]["Ace"]["_type"] = oldTypeName;
        WriteRawGame("Old", game);

        var loaded = _repository.Load("Old")!;

        Assert.IsType<Opponent>(loaded.Racers.ReadyToRace["Ace"]);
    }

    [Fact]
    public void Strings_an_older_save_stored_as_null_still_read_as_null()
    {
        _repository.Save(FullShape(), "Nulls");
        var game = RawGame("Nulls");
        game["Racers"]["ReadyToRace"]["Ace"]["Nickname"] = BsonValue.Null;
        WriteRawGame("Nulls", game);

        var rival = (Opponent)_repository.Load("Nulls")!.Racers.ReadyToRace["Ace"];

        Assert.Null(rival.Nickname);
    }

    [Fact]
    public void Computed_properties_are_not_stored()
    {
        var state = FullShape();
        state.Player.Cars.Add(new Car("sr_car") { ImpoundedUntil = state.Date });
        _repository.Save(state, "Computed");

        var game = RawGame("Computed");

        Assert.False(game["Racers"].AsDocument.ContainsKey("TotalCount"));
        Assert.False(game["Player"]["Stats"].AsDocument.ContainsKey("WinRate"));
        Assert.False(game["Player"]["Stats"].AsDocument.ContainsKey("NetEarnings"));
        Assert.False(game["Player"]["Cars"][0].AsDocument.ContainsKey("IsImpounded"));
        Assert.True(_repository.Load("Computed")!.Player.Cars[0].IsImpounded);
    }

    [Fact]
    public void A_new_racer_starts_at_neutral_reputation() => Assert.Equal(50, new RacerStats().Reputation);

    /// <summary>
    /// Enums are stored by member name. Renaming or removing a member makes every save that holds it fail to load:
    /// this list changes only together with a save migration.
    /// </summary>
    [Fact]
    public void Persisted_enum_names_do_not_change()
    {
        Assert.Equal(["Easy", "Normal", "Hard", "Custom"], Enum.GetNames<Difficulty>());
        Assert.Equal(["Low", "Medium", "High"], Enum.GetNames<PinkSlipFrequency>());
        Assert.Equal(["Player", "AI"], Enum.GetNames<RacerType>());
        Assert.Equal(["Inactive", "Retired", "ReadyToRace"], Enum.GetNames<RacerStatus>());
        Assert.Equal(["Male", "Female", "Other"], Enum.GetNames<Gender>());
        Assert.Equal(["DragRace", "Circuit", "Sprint"], Enum.GetNames<RaceType>());
        Assert.Equal(
            ["TotalWins", "DragWins", "RoadWins", "PinkSlipWins", "ReputationReached", "MoneyEarned", "CarsOwned", "DaysPlayed", "OpponentsDefeated"],
            Enum.GetNames<MilestoneTrigger>());
    }

    [Fact]
    public void Backups_rotate_once_per_session_not_on_every_save()
    {
        var state = _repository.CreateNew("Rot", "Rot");
        var path = _database.PathOf("Rot");

        state.Player.Money = 1m;
        _repository.Save(state, "Rot"); // the session's first save of an existing file: backup1
        state.Player.Money = 2m;
        _repository.Save(state, "Rot");
        _repository.Save(state, "Rot");

        Assert.True(File.Exists(path + ".backup1"));
        Assert.False(File.Exists(path + ".backup2"));

        // Another session (the app started again) rotates once more
        _repository.Dispose();
        using var next = new GameStateRepository(null, new SaveDatabase(_database.SavesDirectory));
        var loaded = next.Load("Rot")!;
        next.Save(loaded, "Rot");
        next.Save(loaded, "Rot");

        Assert.True(File.Exists(path + ".backup2"));
        Assert.False(File.Exists(path + ".backup3"));
    }
}
