using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Victory achieved by having the most wins by the end of the season (day 30).
    /// Unlocks once day 30 is reached.
    /// </summary>
    public class SeasonChampionVictory : IVictoryCondition
    {
        public const int SEASON_END_DAY = 30;

        public string VictoryType => "SeasonChampion";
        public string Name => "Season Champion";
        public string Description => $"Have the most wins of any racer by day {SEASON_END_DAY} to be crowned Season Champion.";

        /// <summary>
        /// Property to check against - set externally based on game state comparison
        /// </summary>
        public bool IsSeasonComplete { get; set; }

        /// <summary>
        /// Whether the player has the most wins - set externally
        /// </summary>
        public bool PlayerHasMostWins { get; set; }

        public bool IsUnlocked(CareerState career)
        {
            // Unlocks when the season is reachable (day 30+)
            var daysPlayed = career.GetCounter(Milestones.MilestoneTrigger.DaysPlayed);
            return daysPlayed >= SEASON_END_DAY;
        }

        public VictoryProgress GetProgress(CareerState career)
        {
            var daysPlayed = career.GetCounter(Milestones.MilestoneTrigger.DaysPlayed);
            var wins = career.GetCounter(Milestones.MilestoneTrigger.TotalWins);

            var progress = new VictoryProgress();

            // Progress is based on days played toward season end
            var dayProgress = Math.Min(100f, (daysPlayed / (float)SEASON_END_DAY) * 100f);
            progress.Percentage = dayProgress;

            if (daysPlayed >= SEASON_END_DAY)
            {
                progress.CompletedSteps.Add($"Reach day {SEASON_END_DAY}");
                progress.ProgressDescription = PlayerHasMostWins
                    ? "You are the Season Champion!"
                    : "Season complete - another racer has more wins";
            }
            else
            {
                progress.RemainingSteps.Add($"Reach day {SEASON_END_DAY} ({SEASON_END_DAY - daysPlayed} days remaining)");
                progress.ProgressDescription = $"Day {daysPlayed}/{SEASON_END_DAY} - Current wins: {wins}";
            }

            return progress;
        }

        public bool IsAchieved(CareerState career)
        {
            // Season must be complete AND player must have most wins
            // Note: The actual comparison with other racers must be done by the service
            return IsUnlocked(career) && PlayerHasMostWins;
        }
    }
}
