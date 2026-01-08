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
        public int PartsInstalled { get; set; }

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
            PartsInstalled = 0;
        }

        public double WinRate => Races > 0 ? (double)Wins / Races : 0.0;
        public decimal NetEarnings => TotalEarnings - TotalLosses;
    }
}
