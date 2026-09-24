using Newtonsoft.Json.Linq;
using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Race.Validation;
using Street_Rod_AC.Services.Storage;

namespace StreetRodAC.Tests;

/// <summary>
/// The ingestion service on temp folders, with a real save: which file goes with which race (K1/K2), the orphan
/// pass, and the rule that a race is forfeited only when AC ran, has exited, and no file of it exists at all.
/// </summary>
public sealed class RaceResultIngestionServiceTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly SaveDatabase _database;
    private readonly GameStateRepository _repository;
    private readonly RaceSessionRepository _sessions;

    public RaceResultIngestionServiceTests()
    {
        _database = new SaveDatabase(_temp.Combine("Saves"));
        _repository = new GameStateRepository(null, _database);
        _sessions = new RaceSessionRepository(_database);
    }

    public void Dispose()
    {
        _repository.Dispose();
        _temp.Dispose();
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

    private string Inbox => _temp.Combine("inbox");
    private string Quarantine => _temp.Combine("quarantine");

    private RaceResultIngestionService Service(GameState state, bool? acRunning = false) => new(
        new RaceResultValidator(),
        new SessionDeduplicator(_sessions),
        new RaceResultProcessor(_repository, _sessions, new FakeCareer(), new FakeEvents()),
        _sessions,
        () => state,
        Inbox,
        Quarantine,
        _temp.Combine("archive"),
        () => acRunning);

    private static (GameState State, RaceContext Context, Opponent Rival) World(decimal wager = 100m, bool launched = true)
    {
        var state = GameState.CreateNew("Player");
        state.SaveName = "t";
        var playerCar = new Car("car_a") { InstanceId = Guid.NewGuid(), EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 };
        state.Player.Cars.Add(playerCar);
        var rival = new Opponent("Rival", 30, Gender.Male, 95, 50) { Money = 5000m };
        var rivalCar = new Car("car_b") { InstanceId = Guid.NewGuid(), EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 };
        rival.Cars.Add(rivalCar);
        state.Racers.AddRacer(rival);

        var context = new RaceContext
        {
            PlayerName = "Player", OpponentName = "Rival", PlayerCarInstanceId = playerCar.InstanceId,
            OpponentCarInstanceId = rivalCar.InstanceId, CashWager = wager, TrackId = "ks_drag", RaceType = RaceType.DragRace,
            CreatedAt = DateTime.Now.AddMinutes(-5),
            LaunchedAt = launched ? DateTime.Now.AddMinutes(-4) : null
        };
        state.PendingRace = context;
        return (state, context, rival);
    }

    /// <summary>A result file in the inbox: the player wins, for <paramref name="contextId"/> (none: no context_id)</summary>
    private string ResultFile(Guid? contextId, int playerPosition = 1)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", "Player", "Rival");
        var session = (JObject)json["session"]!;
        session["start_timestamp"] = DateTime.UtcNow.AddMinutes(-3).ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (contextId is { } id) session["context_id"] = id.ToString("D");
        else session.Remove("context_id");
        json["participants"]![0]!["performance"]!["final_position"] = playerPosition;
        json["participants"]![1]!["performance"]!["final_position"] = 3 - playerPosition;
        return _temp.File(Path.Combine("inbox", Guid.NewGuid() + ".json"), json.ToString());
    }

    [Fact]
    public async Task The_pending_races_file_is_applied_by_the_orphan_pass_and_the_race_is_no_longer_pending()
    {
        var (state, context, rival) = World();
        ResultFile(context.ContextId);

        var result = await Service(state).ProcessOrphanedResultsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Null(state.PendingRace);
        Assert.Equal(5000m - 100m, rival.Money);
        Assert.Empty(Directory.GetFiles(Inbox));
        Assert.True(await _sessions.IsContextSettledAsync("t", context.ContextId));
        Assert.NotEmpty(result.PlayerMessages);
    }

    [Fact]
    public async Task Another_races_file_is_quarantined_and_a_launched_race_without_a_file_is_forfeited()
    {
        var (state, context, rival) = World(wager: 250m);
        ResultFile(Guid.NewGuid());

        var result = await Service(state).ProcessOrphanedResultsAsync();

        Assert.Equal(0, result.FilesProcessed);
        Assert.Equal(1, result.FilesQuarantined);
        Assert.Equal(0, result.FilesQuarantinedForContext);
        Assert.True(result.ForfeitApplied);
        Assert.Null(state.PendingRace);
        Assert.Equal(5000m + 250m, rival.Money);
        Assert.Contains(result.PlayerMessages, m => m.Title == "Race Forfeited");
    }

    [Fact]
    public async Task A_locked_file_is_left_for_later_and_the_race_is_not_forfeited()
    {
        var (state, context, rival) = World();
        var file = ResultFile(context.ContextId);

        IngestionResult result;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await Service(state).ProcessOrphanedResultsAsync();
        }

        Assert.True(result.RetryLater);
        Assert.False(result.ForfeitApplied);
        Assert.Same(context, state.PendingRace);
        Assert.Equal(5000m, rival.Money);
        Assert.True(File.Exists(file));

        // Once the lock is gone, the next pass applies it
        var retry = await Service(state).ProcessOrphanedResultsAsync();
        Assert.Equal(1, retry.FilesProcessed);
        Assert.Null(state.PendingRace);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task While_AC_runs_or_may_run_a_race_without_a_file_is_not_forfeited(bool? acRunning)
    {
        var (state, context, rival) = World();

        var result = await Service(state, acRunning).ProcessOrphanedResultsAsync();

        Assert.True(result.WaitingForAssettoCorsa);
        Assert.False(result.ForfeitApplied);
        Assert.Same(context, state.PendingRace);
        Assert.Equal(5000m, rival.Money);
    }

    [Fact]
    public async Task A_pending_race_AC_never_started_for_is_released_without_a_forfeit()
    {
        var (state, _, rival) = World(launched: false);

        var result = await Service(state).ProcessOrphanedResultsAsync();

        Assert.False(result.ForfeitApplied);
        Assert.Null(state.PendingRace);
        Assert.Equal(5000m, rival.Money);
        Assert.Equal(0, state.Player.Stats.Losses);
    }

    [Fact]
    public async Task A_file_without_a_context_id_goes_to_the_pending_race()
    {
        var (state, _, rival) = World();
        ResultFile(contextId: null);

        var result = await Service(state).ProcessOrphanedResultsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Equal(5000m - 100m, rival.Money);
    }

    [Fact]
    public async Task A_new_race_settles_the_earlier_pending_race_first_and_then_becomes_the_pending_one()
    {
        var (state, earlier, rival) = World();
        ResultFile(earlier.ContextId);
        var next = new RaceContext { PlayerName = "Player", OpponentName = "Rival", CashWager = 50m };
        var messages = new List<PlayerMessage>();

        var began = await Service(state).BeginRaceAsync(next, messages);

        Assert.True(began);
        Assert.Same(next, state.PendingRace);
        Assert.True(await _sessions.IsContextSettledAsync("t", earlier.ContextId));
        Assert.Equal(5000m - 100m, rival.Money);
        Assert.Empty(Directory.GetFiles(Inbox));
    }

    [Fact]
    public async Task A_new_race_does_not_start_while_the_earlier_one_cannot_be_settled()
    {
        var (state, earlier, _) = World();
        var next = new RaceContext { PlayerName = "Player", OpponentName = "Rival" };
        var messages = new List<PlayerMessage>();

        var began = await Service(state, acRunning: true).BeginRaceAsync(next, messages);

        Assert.False(began);
        Assert.Same(earlier, state.PendingRace);
        Assert.Contains(messages, m => m.Title == "Last Race Not Settled");
    }

    [Fact]
    public async Task While_a_race_is_in_flight_the_orphan_pass_leaves_the_inbox_alone()
    {
        var (state, _, _) = World();
        state.PendingRace = null;
        var service = Service(state);
        var race = new RaceContext { PlayerName = "Player", OpponentName = "Rival", CashWager = 10m };
        Assert.True(await service.BeginRaceAsync(race, new List<PlayerMessage>()));
        var file = ResultFile(race.ContextId);

        var during = await service.ProcessOrphanedResultsAsync();
        Assert.Equal(0, during.FilesScanned);
        Assert.True(File.Exists(file));
        Assert.Same(race, state.PendingRace);

        service.EndRace(race);
        var after = await service.ProcessOrphanedResultsAsync();
        Assert.Equal(1, after.FilesProcessed);
    }

    [Fact]
    public async Task The_launchers_pass_counts_only_its_own_races_quarantined_files()
    {
        var (state, context, _) = World();
        ResultFile(Guid.NewGuid());
        _temp.File(Path.Combine("inbox", Guid.NewGuid() + ".json"), "{ not json");

        var result = await Service(state).IngestResultsAsync(context);

        Assert.Equal(2, result.FilesQuarantined);
        // The broken file was written after the race was set up, so it may be this race's; the other race's is not
        Assert.Equal(1, result.FilesQuarantinedForContext);
        Assert.Equal(0, result.FilesProcessed);
    }

    /// <summary>A repository whose saves fail (the disk is full); everything else is not needed here</summary>
    private sealed class FailingSaves : IGameStateRepository
    {
        public void Save(GameState state, string saveName, Action<LiteDB.LiteDatabase>? sameTransaction) =>
            throw new InvalidOperationException("disk full");
        public void Save(GameState state, string saveName) => Save(state, saveName, null);
        public GameState? Load(string saveName) => throw new NotSupportedException();
        public bool Exists(string saveName) => throw new NotSupportedException();
        public void Delete(string saveName) => throw new NotSupportedException();
        public List<string> ListSaves() => throw new NotSupportedException();
        public GameState CreateNew(string saveName, string playerName) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public async Task A_race_whose_save_fails_goes_back_to_the_inbox_and_the_state_is_as_before()
    {
        var (state, context, rival) = World();
        var file = ResultFile(context.ContextId);
        var moneyBefore = state.Player.Money;
        var service = new RaceResultIngestionService(
            new RaceResultValidator(), new SessionDeduplicator(_sessions),
            new RaceResultProcessor(new FailingSaves(), _sessions, new FakeCareer(), new FakeEvents()),
            _sessions, () => state, Inbox, Quarantine, _temp.Combine("archive"), () => false);

        var result = await service.IngestResultsAsync(context);

        Assert.Equal(0, result.FilesProcessed);
        Assert.Equal(1, result.FilesDeferred);
        Assert.True(result.RetryLater);
        Assert.True(File.Exists(file));
        Assert.Equal(moneyBefore, state.Player.Money);
        Assert.Equal(0, state.Player.Stats.Wins);
        Assert.Same(context, state.PendingRace);
        // The racers were put back from the snapshot: the rival in the state has its money as before the race
        Assert.Equal(5000m, state.Racers.Find("Rival")!.Money);
        Assert.False(await _sessions.IsContextSettledAsync("t", context.ContextId));
        _ = rival;
    }
}
