namespace Street_Rod_AC.Services.Settings
{
    /// <summary>
    /// Game-wide settings stored in settings.json
    /// These settings persist across all save games
    /// </summary>
    public class GameSettings
    {
        // Economy Settings
        public double CarPriceMultiplier { get; set; } = 1.0;
        public double PartPriceMultiplier { get; set; } = 1.0;
        public decimal StartingMoney { get; set; } = Models.GameState.Player.StartingMoney;
        public double RacePrizeMultiplier { get; set; } = 1.0;

        // Difficulty Settings
        public int OpponentSkillModifier { get; set; } = 0;
        public int OpponentAggressionModifier { get; set; } = 0;
        public double CarWearMultiplier { get; set; } = 1.0;
        public PinkSlipFrequency PinkSlipFrequency { get; set; } = PinkSlipFrequency.Medium;

        // Where the last free run went: "track" or "track/configuration"
        public string FreeRunTrack { get; set; } = string.Empty;

        /// <summary>
        /// The Assetto Corsa folder; null for the default (<see cref="Street_Rod_AC.Configuration.AppSettings.DefaultAssettoCorsaPath"/>).
        /// Read once, when the settings load at start-up: a change takes effect at the next start.
        /// </summary>
        public string? AssettoCorsaPath { get; set; }

        // Simulation Settings
        public bool RaceSimulationEnabled { get; set; } = true;
        public bool SeasonalRacingEnabled { get; set; } = true;
        public bool MarketRefreshEnabled { get; set; } = true;
    }

    public enum PinkSlipFrequency
    {
        Low,
        Medium,
        High
    }
}
