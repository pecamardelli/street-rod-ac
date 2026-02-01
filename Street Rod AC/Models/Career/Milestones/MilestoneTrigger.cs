namespace Street_Rod_AC.Models.Career.Milestones
{
    /// <summary>
    /// Types of events that can trigger milestone progress
    /// </summary>
    public enum MilestoneTrigger
    {
        /// <summary>
        /// Total race wins (any type)
        /// </summary>
        TotalWins,

        /// <summary>
        /// Drag race wins specifically
        /// </summary>
        DragWins,

        /// <summary>
        /// Road/circuit race wins
        /// </summary>
        RoadWins,

        /// <summary>
        /// Pink slip race wins
        /// </summary>
        PinkSlipWins,

        /// <summary>
        /// Reputation level reached
        /// </summary>
        ReputationReached,

        /// <summary>
        /// Total money earned from races
        /// </summary>
        MoneyEarned,

        /// <summary>
        /// Number of cars currently owned
        /// </summary>
        CarsOwned,

        /// <summary>
        /// Game days played
        /// </summary>
        DaysPlayed,

        /// <summary>
        /// Unique opponents defeated at least once
        /// </summary>
        OpponentsDefeated
    }
}
