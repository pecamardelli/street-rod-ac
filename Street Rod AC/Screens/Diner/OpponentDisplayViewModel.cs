using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;

namespace Street_Rod_AC.Screens.Diner
{
    /// <summary>
    /// View model for displaying an opponent in the list
    /// </summary>
    public class OpponentDisplayViewModel
    {
        public Opponent Opponent { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public string? PortraitPath { get; set; }

        /// <summary>How tough the opponent is for the player; the view picks the colours for it</summary>
        public OpponentDifficulty Difficulty { get; set; }

        public string Name => Opponent.Name;
        public string Nickname => Opponent.Nickname;
        public string CarDisplay => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public string CarBrand => CarDefinition.Brand;
        public string CarName => CarDefinition.Name;
        public int Reputation => Opponent.Stats.Reputation;
        public string ReputationTier => Opponent.Stats.GetReputationTier();
        public string RecordDisplay => $"{Opponent.Stats.Wins}W - {Opponent.Stats.Losses}L";

        // Drag race record display
        public string DragRecordDisplay => Opponent.Stats.DragRaces > 0
            ? $"{Opponent.Stats.DragWins}W - {Opponent.Stats.DragLosses}L"
            : "No races";

        // Road race record display
        public string RoadRecordDisplay => Opponent.Stats.RoadRaces > 0
            ? $"{Opponent.Stats.RoadWins}W - {Opponent.Stats.RoadLosses}L"
            : "No races";

        public bool HasPortrait => !string.IsNullOrEmpty(PortraitPath);

        // Additional record stats
        public string WinRateDisplay
        {
            get
            {
                var winRate = Opponent.Stats.Races > 0
                    ? (Opponent.Stats.Wins * 100.0 / Opponent.Stats.Races)
                    : 0.0;
                return $"{winRate:F0}%";
            }
        }

        public string PinkSlipsDisplay
        {
            get
            {
                var won = Opponent.Stats.PinkSlipsWon;
                var lost = Opponent.Stats.PinkSlipsLost;
                return $"{won}W - {lost}L";
            }
        }

        // Car stats
        public string CarYear => CarDefinition.Year.HasValue ? CarDefinition.Year.Value.ToString() : "";

        /// <summary>The car's stated power; read by <see cref="AcSpecs"/>, the one parser for ui_car.json numbers</summary>
        public string CarPower
        {
            get
            {
                var power = AcSpecs.ParsePower(CarDefinition.Specs?.Bhp) ?? 0;
                return power > 0 ? $"{power:N0} HP" : "";
            }
        }

        public string CarWeight
        {
            get
            {
                var weight = AcSpecs.ParseWeight(CarDefinition.Specs?.Weight) ?? 0;
                return weight > 0 ? $"{weight:N0} kg" : "";
            }
        }

        public string CarDrivetrain => CarDefinition.Specs?.Drivetrain ?? "";
    }

    public enum OpponentDifficulty
    {
        Easy,
        Matched,
        Hard
    }
}
