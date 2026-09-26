using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Screens.Shared
{
    /// <summary>A car's history (<see cref="CarHistory"/>) in words, the same on every screen</summary>
    public static class CarHistoryDisplay
    {
        /// <summary>
        /// "2 owners before you · won from Johnny on a pink slip · 5 wins in 8 races · best 13.52 @ 104 mph" for a car
        /// somebody has; "3 previous owners, the last Johnny · 5 wins in 8 races" for one on a lot
        /// </summary>
        public static string Summary(CarHistory? history, bool forSale)
        {
            if (history == null) return string.Empty;

            var parts = new List<string> { Owners(history, forSale) };
            if (!forSale && HowBought(history) is { } how) parts.Add(how);
            parts.Add(Record(history));
            parts.Add(BestQuarter(history));
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }

        /// <summary>"best 13.52 @ 104 mph", the car's best quarter mile; empty when it has never run one</summary>
        public static string BestQuarter(CarHistory history)
        {
            if (history.BestQuarterSeconds is not { } et) return string.Empty;
            var time = et.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return history.BestQuarterMph is { } mph ? $"best {time} @ {mph:0} mph" : $"best {time}";
        }

        /// <summary>"5 wins in 8 races, 2 cars won on pink slips"; "Never raced" when it hasn't</summary>
        public static string Record(CarHistory history)
        {
            if (history.Races <= 0) return "Never raced";

            var record = $"{Count(history.Wins, "win")} in {Count(history.Races, "race")}";
            return history.PinkSlipsWon > 0 ? $"{record}, {Count(history.PinkSlipsWon, "car")} won on pink slips" : record;
        }

        private static string Owners(CarHistory history, bool forSale)
        {
            // On a lot everybody who had it is a previous owner; in somebody's garage, all but them
            var before = forSale ? history.OwnerCount : Math.Max(0, history.OwnerCount - 1);
            var last = forSale ? history.Owners.LastOrDefault()?.Name : null;

            if (before == 0) return forSale ? "No previous owners" : "First owner";
            if (forSale && last != null) return $"{Count(before, "previous owner")}, the last {last}";
            return forSale ? Count(before, "previous owner") : $"{Count(before, "owner")} before you";
        }

        /// <summary>How the one who has the car now came by it, from whom: "won from Johnny on a pink slip"</summary>
        private static string? HowBought(CarHistory history)
        {
            if (history.Owners.Count == 0) return null;

            var now = history.Owners[^1];
            var from = history.Owners.Count >= 2 ? history.Owners[^2].Name : null;
            return now.How switch
            {
                CarAcquisition.PinkSlip => from != null ? $"won from {from} on a pink slip" : "won on a pink slip",
                CarAcquisition.PrivateSale => from != null ? $"bought from {from}" : "bought privately",
                CarAcquisition.Dealer => "bought off a lot",
                _ => null
            };
        }

        private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n:N0} {noun}s";
    }
}
