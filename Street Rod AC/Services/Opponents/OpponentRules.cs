using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// The numbers of a rival's life, from the GameMaker version's daily review (scr_review_racers): how many racers
    /// are out on the street, which car a racer drives, which one he buys, when he is broke. No state, so the rules
    /// can be checked on their own; <see cref="OpponentLifeService"/> applies them.
    /// </summary>
    public static class OpponentRules
    {
        /// <summary>Racers on the street when a career starts</summary>
        public const int BaseActiveRacers = 6;

        /// <summary>More racers turn up every week</summary>
        public const int ActiveRacersPerWeek = 2;

        /// <summary>What counts as broke when there is no car for sale to measure against</summary>
        public const decimal BankruptFloor = 200m;

        /// <summary>
        /// What a broke racer scrapes together (a loan, a pawned watch, a second job), as a share of the cheapest car
        /// for sale: a few of those and they can buy one
        /// </summary>
        public const double MinCashShare = 0.1;
        public const double MaxCashShare = 0.6;

        /// <summary>The same when nothing is for sale (the GameMaker version's $50–500)</summary>
        public const int MinCashInjection = 50;
        public const int MaxCashInjection = 500;

        /// <summary>A racer keeps a car to race and one spare; the rest go to a dealer</summary>
        public const int MaxCars = 2;

        /// <summary>
        /// Money a racer keeps back from tuning, for the bets and the repairs: this share of what their car is worth,
        /// so it follows the prices
        /// </summary>
        public const decimal TuningReserveShare = 0.25m;

        /// <summary>Share of the money over the reserve a racer spends on parts in a day</summary>
        public const decimal TuningShare = 0.3m;

        public static int WeeksElapsed(DateTime date) =>
            Math.Max(0, (date.Date - GameState.GetStartingDateTime().Date).Days / 7);

        /// <summary>How many racers should be out on the street by <paramref name="date"/>, of <paramref name="total"/> there are</summary>
        public static int MinActive(DateTime date, int total) =>
            Math.Min(total, BaseActiveRacers + ActiveRacersPerWeek * WeeksElapsed(date));

        /// <summary>
        /// How much a racer wants to drive this car of theirs: one that can race before any that can't, then power,
        /// its shape, and its worth as the tie-breaker. (The GameMaker version gave a running car 1000 points, less
        /// than 100 hp is worth: a racer would pick a stronger wreck over the spare that runs.)
        /// </summary>
        /// <param name="condition">0 to 1</param>
        public static double CarScore(double hp, double condition, bool canRace, decimal value) =>
            Sane(hp) * 10 + Math.Clamp(Sane(condition), 0, 1) * 100 * 5 + (canRace ? RacingCarBonus : 0) + (double)value * 0.1;

        /// <summary>More than any power, shape and worth add up to</summary>
        private const double RacingCarBonus = 1_000_000;

        /// <summary>
        /// How much a racer with <paramref name="budget"/> wants to buy this car: power, shape, a car that runs, power
        /// for the money, and a car that leaves money over for repairs and parts (under 70% of the budget)
        /// </summary>
        public static double PurchaseScore(double hp, double condition, bool runs, decimal price, decimal budget)
        {
            var score = Sane(hp) * 2 + Math.Clamp(Sane(condition), 0, 1) * 100 * 3 + (runs ? 500 : 0);
            if (price > 0) score += Sane(hp) / (double)price * 1000;

            if (budget > 0)
            {
                var left = 1 - (double)(price / budget);
                if (left > 0.3) score += left * 200;
            }

            return score;
        }

        /// <summary>Broke: no car that can race, and less money than the cheapest car for sale</summary>
        public static bool IsBankrupt(bool hasRacingCar, decimal money, decimal? cheapestCar) =>
            !hasRacingCar && money < (cheapestCar ?? BankruptFloor);

        /// <summary>What a racer with <paramref name="money"/> and a car worth <paramref name="carValue"/> spends on parts today at most</summary>
        public static decimal TuningBudget(decimal money, decimal carValue) =>
            Math.Max(0m, (money - Math.Max(0m, carValue) * TuningReserveShare) * TuningShare);

        /// <summary>What a broke racer scrapes together: a share of the cheapest car for sale, <paramref name="roll"/> 0 to 1 picking where in the range</summary>
        public static decimal CashInjection(decimal? cheapestCar, double roll)
        {
            roll = Math.Clamp(double.IsFinite(roll) ? roll : 0, 0, 1);
            if (cheapestCar is not > 0) return Math.Round(MinCashInjection + (decimal)roll * (MaxCashInjection - MinCashInjection));

            var share = MinCashShare + roll * (MaxCashShare - MinCashShare);
            return Math.Max(1m, Math.Round(cheapestCar.Value * (decimal)share));
        }

        /// <summary>"his", "her", "their"</summary>
        public static string Possessive(Racer racer) => racer is Opponent { Gender: var gender }
            ? gender switch { Gender.Male => "his", Gender.Female => "her", _ => "their" }
            : "their";

        private static double Sane(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    }
}
