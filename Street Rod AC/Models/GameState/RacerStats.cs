namespace Street_Rod_AC.Models.GameState
{
    public class RacerStats
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Races { get; set; }
        public decimal TotalEarnings { get; set; }
        public decimal TotalLosses { get; set; }
        public int PinkSlipsWon { get; set; }
        public int PinkSlipsLost { get; set; }
        public int CarsOwned { get; set; }
        public int CarsSold { get; set; }

        /// <summary>
        /// Reputation score (0-100) based on race performance
        /// Higher reputation indicates a more feared/respected racer
        /// </summary>
        public int Reputation { get; set; }

        /// <summary>
        /// Bonus reputation earned from event victories
        /// Added on top of calculated reputation
        /// </summary>
        public int EventReputationBonus { get; set; }

        public RacerStats()
        {
            Wins = 0;
            Losses = 0;
            Races = 0;
            TotalEarnings = 0m;
            TotalLosses = 0m;
            PinkSlipsWon = 0;
            PinkSlipsLost = 0;
            CarsOwned = 0;
            CarsSold = 0;
            Reputation = 50; // Start at neutral reputation
        }

        public double WinRate => Races > 0 ? (double)Wins / Races : 0.0;
        public decimal NetEarnings => TotalEarnings - TotalLosses;

        /// <summary>
        /// Calculate reputation based on current stats
        /// Formula: Base(50) + WinRate Bonus(0-30) + PinkSlip Bonus(0-15) - Loss Penalty(0-20)
        /// Result clamped to 0-100
        /// </summary>
        public int CalculateReputation()
        {
            // Start with base reputation
            int reputation = 50;

            if (Races > 0)
            {
                // Win rate bonus: 0-30 points (0% win = 0, 100% win = +30)
                var winRateBonus = (int)(WinRate * 30);
                reputation += winRateBonus;

                // Pink slip wins are prestigious: +5 per win (max +15)
                var pinkSlipBonus = Math.Min(PinkSlipsWon * 5, 15);
                reputation += pinkSlipBonus;

                // Pink slip losses hurt reputation: -5 per loss (max -15)
                var pinkSlipPenalty = Math.Min(PinkSlipsLost * 5, 15);
                reputation -= pinkSlipPenalty;

                // Consistent losing streak penalty
                if (Races >= 5 && WinRate < 0.3)
                {
                    // Poor performers lose reputation faster
                    reputation -= 10;
                }

                // Championship-level performance bonus
                if (Races >= 10 && WinRate > 0.8)
                {
                    // Elite racers get bonus reputation
                    reputation += 10;
                }
            }

            // Add event reputation bonus
            reputation += EventReputationBonus;

            // Clamp to valid range
            return Math.Max(0, Math.Min(100, reputation));
        }

        /// <summary>
        /// Get reputation tier description
        /// </summary>
        public string GetReputationTier()
        {
            return Reputation switch
            {
                >= 90 => "Legendary",
                >= 75 => "Elite",
                >= 60 => "Respected",
                >= 40 => "Average",
                >= 25 => "Struggling",
                _ => "Unknown"
            };
        }
    }
}
