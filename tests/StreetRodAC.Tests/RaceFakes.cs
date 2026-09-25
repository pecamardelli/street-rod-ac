using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Career;

namespace StreetRodAC.Tests;

/// <summary>
/// What the race result tests share: the processor's career and event collaborators that do nothing, and a game
/// with the player, one rival, a car each and the race between them pending
/// </summary>
internal static class RaceFakes
{
    public sealed class FakeCareer : ICareerProgressService
    {
        public CareerProgressResult CheckProgressAfterRace(GameState gameState) => new();
    }

    public sealed class FakeEvents : IRaceEventService
    {
        public IEnumerable<RaceEventDefinition> GetAllEventDefinitions() => [];
        public RaceEventDefinition? GetEventDefinition(string eventId) => null;
        public IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career) => [];
        public IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime) => [];
        public List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime, double pinkSlipFactor = 1.0) => [];
        public bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career) => false;
        public EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt) => null;
        public int CleanupExpiredEvents(CareerState career, DateTime currentTime) => 0;
    }

    public const string PlayerName = "Player";
    public const string OpponentName = "Rival";

    /// <summary>The game, the race pending in it, and who races in what</summary>
    public sealed record RaceWorld(GameState State, RaceContext Context, Car PlayerCar, Opponent Rival, Car RivalCar);

    /// <summary>
    /// The player (a million, one healthy car, selected) against a rival (5000, one healthy car), the race between
    /// them pending: a drag race on ks_drag, or a road race on ks_highlands
    /// </summary>
    public static RaceWorld World(decimal wager = 100m, bool pinkSlip = false, RaceType raceType = RaceType.DragRace, string saveName = "test")
    {
        var state = GameState.CreateNew(PlayerName);
        state.SaveName = saveName;
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
            OpponentCarInstanceId = rivalCar.InstanceId, CashWager = wager, IsPinkSlip = pinkSlip,
            TrackId = raceType == RaceType.DragRace ? "ks_drag" : "ks_highlands", RaceType = raceType
        };
        state.PendingRace = context;
        return new RaceWorld(state, context, playerCar, rival, rivalCar);
    }
}
