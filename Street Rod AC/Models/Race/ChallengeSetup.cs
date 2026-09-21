using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Setup data for a race challenge
    /// </summary>
    public class ChallengeSetup
    {
        public Opponent Opponent { get; set; } = null!;
        public Car PlayerCar { get; set; } = null!;
        public Car OpponentCar { get; set; } = null!;
        public bool IsPinkSlip { get; set; }
        public decimal CashWager { get; set; }
        public string TrackId { get; set; } = "ks_drag";
        public string? TrackConfig { get; set; } = "drag1000";
        public RaceType RaceType { get; set; } = RaceType.DragRace;
    }
}
