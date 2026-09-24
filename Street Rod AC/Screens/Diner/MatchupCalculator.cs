using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts.Cars;

namespace Street_Rod_AC.Screens.Diner
{
    /// <summary>
    /// The numbers behind a diner matchup: how the two cars compare, how tough the opponent is for the
    /// player, and what can be wagered. No state and no UI, so the rules can be checked on their own.
    /// </summary>
    public static class MatchupCalculator
    {
        /// <summary>Reputation points either way within which an opponent counts as a fair match</summary>
        public const int MatchedReputationBand = 15;

        /// <summary>The smallest cash wager: a drag race is a small bet, a road race a bigger one</summary>
        public static decimal MinimumWager(RaceType? raceType) => raceType == RaceType.DragRace ? 10m : 25m;

        /// <summary>The house limit on a cash wager in the day, against a rival of ordinary standing, before either racer's money is counted</summary>
        public static decimal MaximumWager(RaceType? raceType) => raceType == RaceType.DragRace ? 100m : 250m;

        /// <summary>From this hour of the game's day the street is racing for real money</summary>
        public const int NightFromHour = 20;

        /// <summary>How much more the stakes go at night</summary>
        public const decimal NightStakes = 2m;

        /// <summary>Reputation above which a rival plays for more: the stakes grow by one for every <see cref="ReputationPerStake"/> points over it</summary>
        public const int StakesReputation = 50;

        public const int ReputationPerStake = 25;

        public static bool IsNight(DateTime gameTime) => gameTime.Hour >= NightFromHour;

        /// <summary>
        /// How many times the house limit this race may go for: up to three times against the best-known rivals
        /// (reputation 100), twice that at night
        /// </summary>
        public static decimal StakesFactor(int opponentReputation, DateTime? gameTime)
        {
            var standing = 1m + Math.Max(0, Math.Min(opponentReputation, 100) - StakesReputation) / (decimal)ReputationPerStake;
            return gameTime is { } time && IsNight(time) ? standing * NightStakes : standing;
        }

        /// <summary>
        /// The cash wager range for this race: the house limits, raised at night and against a rival with a name
        /// (<see cref="StakesFactor"/>), the top capped by what the poorer of the two racers has. When that is less
        /// than the minimum, the minimum comes down with it.
        /// </summary>
        public static (decimal Min, decimal Max) WagerLimits(RaceType? raceType, decimal playerMoney, decimal opponentMoney,
            int opponentReputation = StakesReputation, DateTime? gameTime = null)
        {
            var min = MinimumWager(raceType);
            var house = Math.Round(MaximumWager(raceType) * StakesFactor(opponentReputation, gameTime) / 5) * 5;
            var max = Math.Min(house, Math.Min(playerMoney, opponentMoney));
            if (min > max) min = max;
            return (min, max);
        }

        /// <summary>The wager brought into the range, the way the slider shows it</summary>
        public static decimal ClampWager(decimal wager, decimal min, decimal max) =>
            wager < min ? min : wager > max ? max : wager;

        /// <summary>How tough the opponent is for the player, by reputation</summary>
        public static OpponentDifficulty DifficultyOf(int opponentReputation, int playerReputation)
        {
            var difference = opponentReputation - playerReputation;
            if (difference < -MatchedReputationBand) return OpponentDifficulty.Easy;
            if (difference > MatchedReputationBand) return OpponentDifficulty.Hard;
            return OpponentDifficulty.Matched;
        }

        /// <summary>
        /// Horsepower, weight and power to weight side by side, each with who it favours. A stat neither car
        /// states is left out. Spec text is read by <see cref="AcSpecs"/>, the one parser for ui_car.json numbers.
        /// </summary>
        public static List<MatchupStatViewModel> Compare(CarDefinition playerCar, CarDefinition opponentCar)
        {
            var stats = new List<MatchupStatViewModel>();

            var playerPower = AcSpecs.ParsePower(playerCar.Specs?.Bhp) ?? 0;
            var opponentPower = AcSpecs.ParsePower(opponentCar.Specs?.Bhp) ?? 0;
            var playerWeight = AcSpecs.ParseWeight(playerCar.Specs?.Weight) ?? 0;
            var opponentWeight = AcSpecs.ParseWeight(opponentCar.Specs?.Weight) ?? 0;

            // Horsepower - higher is better
            if (playerPower > 0 || opponentPower > 0)
            {
                stats.Add(Stat("Horsepower", $"{playerPower:N0} HP", $"{opponentPower:N0} HP",
                    playerPower, opponentPower, higherIsBetter: true));
            }

            // Weight - lower is better
            if (playerWeight > 0 || opponentWeight > 0)
            {
                stats.Add(Stat("Weight", $"{playerWeight:N0} kg", $"{opponentWeight:N0} kg",
                    playerWeight, opponentWeight, higherIsBetter: false));
            }

            // Power-to-weight ratio - higher is better
            if (playerWeight > 0 && opponentWeight > 0 && (playerPower > 0 || opponentPower > 0))
            {
                var playerPwr = playerPower / (playerWeight / 1000.0);
                var opponentPwr = opponentPower / (opponentWeight / 1000.0);
                stats.Add(Stat("HP/Ton", $"{playerPwr:N1}", $"{opponentPwr:N1}",
                    playerPwr, opponentPwr, higherIsBetter: true));
            }

            return stats;
        }

        /// <summary>-1 opponent ahead, 0 level, 1 player ahead; flipped when lower is better</summary>
        public static int AdvantageOf(double playerValue, double opponentValue, bool higherIsBetter)
        {
            var diff = playerValue - opponentValue;
            var advantage = Math.Abs(diff) < 0.01 ? 0 : (diff > 0 ? 1 : -1);
            return higherIsBetter ? advantage : -advantage;
        }

        private static MatchupStatViewModel Stat(string label, string playerDisplay, string opponentDisplay,
            double playerValue, double opponentValue, bool higherIsBetter) => new()
        {
            Label = label,
            PlayerValue = playerDisplay,
            OpponentValue = opponentDisplay,
            Advantage = AdvantageOf(playerValue, opponentValue, higherIsBetter)
        };
    }
}
