namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Something the player should be told after a race (an event reward, a forfeit, a milestone). Services
    /// return these instead of opening dialogs; the screen that ran the race shows them, one at a time.
    /// </summary>
    public sealed record PlayerMessage(string Title, string Text)
    {
        /// <summary>A drag race's timeslips, shown as a timeslip rather than as text; null for any other message</summary>
        public TimeslipCard? Timeslip { get; init; }

        /// <summary>
        /// The player's car was towed home after a crash: the race hands the player back in the garage, where the car
        /// is, rather than on the streets
        /// </summary>
        public bool TowedToGarage { get; init; }

        /// <summary>The game is won: shown as the victory screen rather than as text; null for any other message</summary>
        public VictoryCard? Victory { get; init; }
    }

    /// <summary>The game won: the path that won it, and the career that got there</summary>
    public sealed record VictoryCard(string VictoryName, string Description, IReadOnlyList<VictoryStat> Stats, string Footnote)
    {
        public static VictoryCard For(string victoryName, string description, GameState.GameState gameState, string footnote = "")
        {
            var stats = gameState.Player.Stats;
            var days = gameState.Career.GetCounter(Career.Milestones.MilestoneTrigger.DaysPlayed);
            return new VictoryCard(victoryName, description,
            [
                new("Won on", gameState.Date.ToString("MMMM d, yyyy")),
                new("Days on the streets", days.ToString("N0")),
                new("Races", $"{stats.Races:N0} ({stats.Wins:N0} won, {stats.Losses:N0} lost)"),
                new("Reputation", $"{stats.Reputation} ({stats.GetReputationTier()})"),
                new("Pink slips won", stats.PinkSlipsWon.ToString("N0")),
                new("Cars in the garage", gameState.Player.Cars.Count.ToString("N0")),
                new("Net earnings", $"${stats.NetEarnings:N0}")
            ], footnote);
        }
    }

    /// <summary>One line of the victory screen's career sheet</summary>
    public sealed record VictoryStat(string Label, string Value);

    /// <summary>Both lanes' timeslips of a drag race, as the strip hands them out</summary>
    public sealed record TimeslipCard(string PlayerName, Timeslip? Player, string OpponentName, Timeslip? Opponent);
}
