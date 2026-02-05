namespace Street_Rod_AC.Screens.Diner
{
    /// <summary>
    /// View model for displaying a single matchup stat comparison
    /// </summary>
    public class MatchupStatViewModel
    {
        /// <summary>
        /// Stat label (e.g., "Horsepower", "Weight")
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Player's value display string
        /// </summary>
        public string PlayerValue { get; set; } = "";

        /// <summary>
        /// Opponent's value display string
        /// </summary>
        public string OpponentValue { get; set; } = "";

        /// <summary>
        /// Advantage indicator: -1 = opponent advantage, 0 = equal, 1 = player advantage
        /// </summary>
        public int Advantage { get; set; }

        /// <summary>
        /// Display string for advantage indicator
        /// </summary>
        public string AdvantageIndicator => Advantage switch
        {
            1 => "(+)",
            -1 => "(-)",
            _ => "(=)"
        };

        /// <summary>
        /// Color for advantage indicator
        /// </summary>
        public string AdvantageColor => Advantage switch
        {
            1 => "#90EE90",   // Green - player advantage
            -1 => "#FF6B6B", // Red - opponent advantage
            _ => "#808080"   // Gray - equal
        };
    }
}
