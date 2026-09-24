using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Storage;

namespace StreetRodAC.Tests;

public sealed class RaceResultProcessorTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- Fakes: the processor's collaborators, recording what reached them ----

    private sealed class FakeRepository : IGameStateRepository
    {
        public int Saves { get; private set; }
        public Exception? Throw { get; set; }
        public List<ProcessedRaceSession> Recorded { get; } = new();

        public void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction)
        {
            if (Throw != null) throw Throw;
            Saves++;
            // The session repository writes through the database it is handed: a throwaway in-memory one
            using var db = new LiteDatabase(new MemoryStream());
            sameTransaction?.Invoke(db);
            Recorded.AddRange(db.GetCollection<ProcessedRaceSession>("ProcessedRaceSessions").FindAll());
        }

        public void Save(GameState state, string saveName) => Save(state, saveName, null);
        public GameState? Load(string saveName) => throw new NotSupportedException();
        public bool Exists(string saveName) => throw new NotSupportedException();
        public void Delete(string saveName) => throw new NotSupportedException();
        public List<string> ListSaves() => throw new NotSupportedException();
        public GameState CreateNew(string saveName, string playerName) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class FakeCareer : ICareerProgressService
    {
        public CareerProgressResult CheckProgressAfterRace(GameState gameState) => new();
    }

    private sealed class FakeEvents : IRaceEventService
    {
        public IEnumerable<RaceEventDefinition> GetAllEventDefinitions() => [];
        public RaceEventDefinition? GetEventDefinition(string eventId) => null;
        public IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career) => [];
        public IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime) => [];
        public List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime) => [];
        public bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career) => false;
        public EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt) => null;
        public int CleanupExpiredEvents(CareerState career, DateTime currentTime) => 0;
    }

    private readonly FakeRepository _repository = new();

    private RaceResultProcessor Processor() =>
        new(_repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new FakeCareer(), new FakeEvents());

    private const string PlayerName = "Player";
    private const string OpponentName = "Rival";

    private static (GameState State, RaceContext Context, Car PlayerCar, Opponent Rival) World(decimal wager = 100m, bool pinkSlip = false)
    {
        var state = GameState.CreateNew(PlayerName);
        state.SaveName = "test";
        var playerCar = new Car("car_a") { InstanceId = Guid.NewGuid(), EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 };
        state.Player.Cars.Add(playerCar);
        state.Player.SelectedCarInstanceId = playerCar.InstanceId;

        var rival = new Opponent(OpponentName, 30, Gender.Male, 95, 50) { Money = 5000m };
        var rivalCar = new Car("car_b") { InstanceId = Guid.NewGuid(), EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 };
        rival.Cars.Add(rivalCar);
        state.Racers.AddRacer(rival);

        var context = new RaceContext
        {
            PlayerName = PlayerName, OpponentName = OpponentName, PlayerCarInstanceId = playerCar.InstanceId,
            OpponentCarInstanceId = rivalCar.InstanceId, CashWager = wager, IsPinkSlip = pinkSlip, TrackId = "ks_drag", RaceType = RaceType.DragRace
        };
        state.PendingRace = context;
        return (state, context, playerCar, rival);
    }

    /// <summary>A parsed result: who finished where, with the 1.1 markers or without them (1.0)</summary>
    private static RaceResultJson Result(JObject json) => JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;

    private static JObject File(int playerPosition, bool v11 = true)
    {
        var json = RaceResultValidatorTests.Fixture(v11 ? "1.1" : "1.0", PlayerName, OpponentName);
        json["participants"]![0]!["performance"]!["final_position"] = playerPosition;
        json["participants"]![1]!["performance"]!["final_position"] = 3 - playerPosition;
        return json;
    }

    private static string Fingerprint(GameState state) => JsonConvert.SerializeObject(state, new JsonSerializerSettings
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore, TypeNameHandling = TypeNameHandling.Auto
    });

    [Fact]
    public void The_player_is_who_is_player_says_whatever_the_order()
    {
        var (state, context, _, rival) = World();
        var json = File(playerPosition: 1);
        // The opponent first in the file, as the old hash-ordered Lua app could write it
        var list = (JArray)json["participants"]!;
        var player = list[0];
        list.RemoveAt(0);
        list.Add(player);

        Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.Equal(1_000_000m + 100m, state.Player.Money);
        Assert.Equal(4900m, rival.Money);
        Assert.Null(state.PendingRace);
        Assert.Equal(1, _repository.Saves);
        Assert.Single(_repository.Recorded);
        Assert.Equal(context.ContextId, _repository.Recorded[0].RaceContextId);
        Assert.True(_repository.Recorded[0].PlayerWon);
    }

    [Fact]
    public void Without_markers_the_players_name_decides_and_the_other_entry_is_the_opponent()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 2, v11: false);
        json["participants"]![1]!["driver_name"] = "Someone Else"; // only the player's name matches the context

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.NotNull(messages);
        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Equal(1_000_000m - 100m, state.Player.Money);
    }

    [Fact]
    public void Without_markers_and_the_players_name_the_opponents_name_decides()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1, v11: false);
        json["participants"]![0]!["driver_name"] = "Renamed Player";
        // The opponent first in the file
        var list = (JArray)json["participants"]!;
        var first = list[0];
        list.RemoveAt(0);
        list.Add(first);

        Processor().ProcessRaceResult(Result(json), context, state);
        Assert.Equal(1, state.Player.Stats.Wins);
    }

    [Fact]
    public void Car_index_zero_marks_the_player_when_is_player_is_absent()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1);
        foreach (var p in json["participants"]!) ((JObject)p).Remove("is_player");
        json["participants"]![0]!["driver_name"] = "X";
        json["participants"]![1]!["driver_name"] = "Y";
        var list = (JArray)json["participants"]!;
        var first = list[0];
        list.RemoveAt(0);
        list.Add(first);

        Processor().ProcessRaceResult(Result(json), context, state);
        Assert.Equal(1, state.Player.Stats.Wins);
    }

    [Fact]
    public void A_bad_timestamp_leaves_the_game_state_as_it_was()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1);
        json["session"]!["start_timestamp"] = "24/09/2026";
        var before = Fingerprint(state);

        Assert.ThrowsAny<Exception>(() => Processor().ProcessRaceResult(Result(json), context, state));

        Assert.Equal(before, Fingerprint(state));
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public void A_null_crash_block_that_got_past_validation_fails_before_the_state_changes()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1);
        json["participants"]![1]!["crash"] = JValue.CreateNull();
        var before = Fingerprint(state);

        Assert.ThrowsAny<Exception>(() => Processor().ProcessRaceResult(Result(json), context, state));

        Assert.Equal(before, Fingerprint(state));
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public void A_state_without_a_save_name_is_refused_before_anything_changes()
    {
        var (state, context, _, _) = World();
        state.SaveName = "";
        var before = Fingerprint(state);

        Assert.Throws<InvalidOperationException>(() => Processor().ProcessRaceResult(Result(File(1)), context, state));
        Assert.Equal(before, Fingerprint(state));
    }

    [Fact]
    public void The_odometer_takes_at_most_500_km_from_one_race()
    {
        var (state, context, playerCar, _) = World();
        var json = File(playerPosition: 1);
        json["participants"]![0]!["performance"]!["distance_km"] = 1e9;

        Processor().ProcessRaceResult(Result(json), context, state);
        Assert.Equal(500.0, playerCar.OdometerKM);
    }

    [Fact]
    public void A_pink_slip_loss_moves_the_players_car_and_clears_the_selection()
    {
        var (state, context, playerCar, rival) = World(wager: 0, pinkSlip: true);

        Processor().ProcessRaceResult(Result(File(playerPosition: 2)), context, state);

        Assert.DoesNotContain(playerCar, state.Player.Cars);
        Assert.Contains(playerCar, rival.Cars);
        Assert.Null(state.Player.SelectedCarInstanceId);
        // Wear was applied before the car changed hands
        Assert.True(playerCar.EngineHealth < 1);
    }

    [Fact]
    public void Both_crashed_is_a_draw_that_moves_no_money()
    {
        var (state, context, _, rival) = World();
        var json = File(playerPosition: 1);
        json["participants"]![0]!["crash"]!["crashed"] = true;
        json["participants"]![1]!["crash"]!["crashed"] = true;

        Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1_000_000m, state.Player.Money);
        Assert.Equal(5000m, rival.Money);
        Assert.Equal(0, state.Player.Stats.Races);
        Assert.Equal("BothCrashed", _repository.Recorded.Single().WinCondition);
    }

    [Fact]
    public void A_player_breakdown_loses_the_race_and_says_what_gave_out()
    {
        var (state, context, _, rival) = World();
        var json = File(playerPosition: 1);
        json["session"]!["end_reason"] = EndReasons.BrokeDown;
        json["participants"]![0]!["broke_down"] = true;
        json["participants"]![0]!["breakdown"] = Breakdowns.Gearbox;

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Equal(1_000_000m - 100m, state.Player.Money);
        Assert.Equal(5100m, rival.Money);
        Assert.Equal("PlayerBrokeDown", _repository.Recorded.Single().WinCondition);
        Assert.Contains(messages, m => m.Title == "Broke Down" && m.Text.Contains("gearbox"));
    }

    [Fact]
    public void A_rival_breakdown_wins_the_race_for_the_player()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 2);
        json["participants"]![1]!["broke_down"] = true;
        json["participants"]![1]!["breakdown"] = Breakdowns.Engine;

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.Equal("OpponentBrokeDown", _repository.Recorded.Single().WinCondition);
        Assert.Contains(messages, m => m.Title == "Rival Broke Down" && m.Text.Contains("engine"));
    }

    [Fact]
    public void Nobody_reaching_the_line_is_a_draw_that_moves_nothing()
    {
        var (state, context, _, rival) = World(wager: 0, pinkSlip: true);
        var json = File(playerPosition: 1);
        json["session"]!["end_reason"] = EndReasons.BrokeDown;
        json["participants"]![0]!["broke_down"] = true;
        json["participants"]![1]!["crash"]!["crashed"] = true;

        Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Single(state.Player.Cars);
        Assert.Single(rival.Cars);
        Assert.Equal(0, state.Player.Stats.Races);
        Assert.Equal("BothOut", _repository.Recorded.Single().WinCondition);
    }

    [Fact]
    public void The_cars_condition_lands_on_both_cars_and_the_player_gets_a_damage_report()
    {
        var (state, context, playerCar, rival) = World();
        var json = File(playerPosition: 1);
        json["participants"]![0]!["condition"] = JObject.Parse("{ \"body_damage_kmh\": [0, 0, 25.8, 0], \"engine_life\": 1000, \"gearbox_damage\": 0 }");
        json["participants"]![1]!["condition"] = JObject.Parse("{ \"body_damage_kmh\": [40, 0, 0, 0], \"engine_life\": 700 }");

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(new double[] { 0, 0, 25.8, 0 }, playerCar.BodyDamageKmh);
        var report = Assert.Single(messages, m => m.Title == "Damage Report");
        Assert.Contains("left side", report.Text);
        // No parts catalog: the cars' own figures take the rest
        Assert.Equal(1.0, playerCar.EngineHealth);
        var rivalCar = rival.Cars.Single();
        Assert.Equal(new double[] { 40, 0, 0, 0 }, rivalCar.BodyDamageKmh);
        Assert.Equal(0.7, rivalCar.EngineHealth, 6);
    }

    [Fact]
    public void A_crashed_player_is_towed_to_the_garage_with_the_damage_report()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 2);
        json["session"]!["end_reason"] = EndReasons.Crash;
        json["participants"]![0]!["crash"]!["crashed"] = true;
        json["participants"]![0]!["condition"] = JObject.Parse("{ \"body_damage_kmh\": [60, 0, 0, 0] }");

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        var towed = Assert.Single(messages, m => m.TowedToGarage);
        Assert.Equal("Towed Home", towed.Title);
        Assert.Contains("front took a 60 km/h hit", towed.Text);
        Assert.DoesNotContain(messages, m => m.Title == "Damage Report");
    }

    [Fact]
    public void A_race_without_a_crash_sends_nobody_to_the_garage()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1);
        json["participants"]![0]!["condition"] = JObject.Parse("{ \"body_damage_kmh\": [10, 0, 0, 0] }");

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.DoesNotContain(messages, m => m.TowedToGarage);
        Assert.Single(messages, m => m.Title == "Damage Report");
    }

    [Fact]
    public void A_drag_race_hands_out_the_timeslips_first()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1);
        json["participants"]![0]!["timeslip"] = JObject.Parse("{ \"reaction_s\": 0.51, \"quarter_mile_s\": 14.2, \"quarter_mile_mph\": 98.7 }");

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        var slip = messages[0].Timeslip;
        Assert.NotNull(slip);
        Assert.Equal(PlayerName, slip!.PlayerName);
        Assert.Equal(14.2, slip.Player!.QuarterMileSeconds);
        Assert.Null(slip.Opponent);
    }

    [Fact]
    public void A_false_start_is_no_contest_that_costs_reputation()
    {
        var (state, context, playerCar, rival) = World(pinkSlip: true);
        var json = File(playerPosition: 1);
        json["session"]!["end_reason"] = EndReasons.FalseStart;
        json["participants"]![0]!["false_start"] = true;
        var reputation = state.Player.Stats.Reputation;

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1_000_000m, state.Player.Money);
        Assert.Equal(5000m, rival.Money);
        Assert.Contains(playerCar, state.Player.Cars);     // the pink slip stays with its owner
        Assert.Equal(0, state.Player.Stats.Races);
        Assert.Equal(1, state.Player.Stats.FalseStarts);
        Assert.Equal(reputation - RacerStats.FalseStartPenalty, state.Player.Stats.Reputation);
        Assert.Null(state.PendingRace);
        Assert.Equal("FalseStart", _repository.Recorded.Single().WinCondition);
        Assert.Contains(messages, m => m.Title == "False Start");
    }

    [Fact]
    public void The_false_start_flag_alone_calls_the_race_off()
    {
        var (state, context, _, _) = World();
        var json = File(playerPosition: 1);
        json["participants"]![0]!["false_start"] = true;

        Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(0, state.Player.Stats.Wins);
        Assert.Equal(1_000_000m, state.Player.Money);
    }

    [Fact]
    public void Hitting_the_rival_out_of_your_lane_is_a_loss()
    {
        var (state, context, _, rival) = World();
        var json = File(playerPosition: 1);
        json["session"]!["end_reason"] = EndReasons.Disqualified;
        json["participants"]![0]!["disqualified"] = true;

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Equal(1_000_000m - 100m, state.Player.Money);
        Assert.Equal(5100m, rival.Money);
        Assert.Equal("PlayerDisqualified", _repository.Recorded.Single().WinCondition);
        Assert.Contains(messages, m => m.Title == "Disqualified");
    }

    [Fact]
    public void A_rival_disqualified_for_hitting_you_loses_even_when_the_hit_crashed_you()
    {
        var (state, context, _, rival) = World();
        var json = File(playerPosition: 2);
        json["session"]!["end_reason"] = EndReasons.Crash;
        json["participants"]![0]!["crash"]!["crashed"] = true;
        json["participants"]![1]!["disqualified"] = true;

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.Equal(1_000_000m + 100m, state.Player.Money);
        Assert.Equal(4900m, rival.Money);
        Assert.Equal("OpponentDisqualified", _repository.Recorded.Single().WinCondition);
        Assert.Contains(messages, m => m.Title == "Rival Disqualified");
    }

    [Fact]
    public void Being_put_back_mid_race_is_a_loss()
    {
        var (state, context, _, rival) = World();
        var json = File(playerPosition: 1);
        json["session"]!["end_reason"] = EndReasons.Abandoned;

        var messages = Processor().ProcessRaceResult(Result(json), context, state);

        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Equal(1_000_000m - 100m, state.Player.Money);
        Assert.Equal(5100m, rival.Money);
        Assert.Equal("PlayerAbandoned", _repository.Recorded.Single().WinCondition);
        Assert.Contains(messages, m => m.Title == "Out of the Race");
    }

    [Fact]
    public void A_forfeit_is_a_loss_with_its_record_and_clears_the_pending_race()
    {
        var (state, context, _, rival) = World(wager: 250m);

        var messages = Processor().ApplyForfeit(context, state);

        Assert.Equal(1_000_000m - 250m, state.Player.Money);
        Assert.Equal(5250m, rival.Money);
        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Null(state.PendingRace);
        Assert.Equal($"forfeit-{context.ContextId:D}", _repository.Recorded.Single().SessionId);
        Assert.Contains(messages, m => m.Title == "Race Forfeited");
    }

    [Fact]
    public void A_failing_save_leaves_nothing_recorded_and_the_state_as_it_was()
    {
        var (state, context, playerCar, _) = World();
        _repository.Throw = new InvalidOperationException("disk full");

        Assert.Throws<RaceNotSavedException>(() => Processor().ProcessRaceResult(Result(File(1)), context, state));
        Assert.Empty(_repository.Recorded);

        // Put back from the snapshot: the money, the stats, the car's wear, the pending race
        Assert.Equal(1_000_000m, state.Player.Money);
        Assert.Equal(0, state.Player.Stats.Wins);
        Assert.Equal(5000m, state.Racers.Find(OpponentName)!.Money);
        Assert.Equal(1.0, state.Player.Cars.Single(c => c.InstanceId == playerCar.InstanceId).EngineHealth);
        Assert.Same(context, state.PendingRace);
    }

    private sealed class ThrowingCareer : ICareerProgressService
    {
        public CareerProgressResult CheckProgressAfterRace(GameState gameState) => throw new InvalidOperationException("career check broke");
    }

    [Fact]
    public void A_step_that_throws_half_way_puts_the_state_back()
    {
        var (state, context, _, _) = World(wager: 0, pinkSlip: true);
        var processor = new RaceResultProcessor(_repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new ThrowingCareer(), new FakeEvents());

        // The pink slip moved the player's car to the rival before the career check threw
        Assert.Throws<InvalidOperationException>(() => processor.ApplyForfeit(context, state));

        Assert.Single(state.Player.Cars);
        Assert.Single(state.Racers.Find(OpponentName)!.Cars);
        Assert.Equal(0, state.Player.Stats.Losses);
        Assert.Same(context, state.PendingRace);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public void A_forfeit_to_an_opponent_the_game_does_not_track_names_no_wager_that_never_moved()
    {
        var (state, context, _, _) = World(wager: 300m);
        context.OpponentName = "Event Stranger";

        var messages = Processor().ApplyForfeit(context, state);

        Assert.Equal(1_000_000m, state.Player.Money);
        var message = Assert.Single(messages, m => m.Title == "Race Forfeited");
        Assert.DoesNotContain("wager goes to", message.Text);
    }

    [Fact]
    public void ReleasePendingRace_clears_only_its_own_race()
    {
        var (state, context, _, _) = World(wager: 0);
        var other = new RaceContext();

        Processor().ReleasePendingRace(other, state);
        Assert.Same(context, state.PendingRace);
        Assert.Equal(0, _repository.Saves);

        Processor().ReleasePendingRace(context, state);
        Assert.Null(state.PendingRace);
        Assert.Equal(1, _repository.Saves);
    }
}
