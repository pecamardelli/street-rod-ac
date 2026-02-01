using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Victory achieved by defeating every opponent at least once.
    /// Unlocks at 75 reputation.
    /// </summary>
    public class DominationVictory : IVictoryCondition
    {
        public const int REQUIRED_REPUTATION = 75;

        public string VictoryType => "Domination";
        public string Name => "Total Domination";
        public string Description => "Defeat every opponent at least once to prove your complete dominance of the streets.";

        /// <summary>
        /// Total number of opponents in the game - set externally
        /// </summary>
        public int TotalOpponents { get; set; }

        public bool IsUnlocked(CareerState career)
        {
            return career.GetCounter(Milestones.MilestoneTrigger.ReputationReached) >= REQUIRED_REPUTATION;
        }

        public VictoryProgress GetProgress(CareerState career)
        {
            var reputation = career.GetCounter(Milestones.MilestoneTrigger.ReputationReached);
            var defeated = career.DefeatedOpponentIds.Count;

            var progress = new VictoryProgress();

            // Unlock progress
            if (reputation >= REQUIRED_REPUTATION)
            {
                progress.CompletedSteps.Add($"Reach {REQUIRED_REPUTATION} reputation");
            }
            else
            {
                progress.RemainingSteps.Add($"Reach {REQUIRED_REPUTATION} reputation ({reputation}/{REQUIRED_REPUTATION})");
            }

            // Domination progress
            if (TotalOpponents > 0)
            {
                var defeatPercentage = (defeated / (float)TotalOpponents) * 100f;

                if (defeated >= TotalOpponents)
                {
                    progress.CompletedSteps.Add($"Defeat all {TotalOpponents} opponents");
                    progress.Percentage = 100f;
                    progress.ProgressDescription = "Total Domination achieved!";
                }
                else if (IsUnlocked(career))
                {
                    progress.RemainingSteps.Add($"Defeat {TotalOpponents - defeated} more opponents");
                    progress.Percentage = defeatPercentage;
                    progress.ProgressDescription = $"Opponents defeated: {defeated}/{TotalOpponents}";
                }
                else
                {
                    progress.RemainingSteps.Add($"(Locked) Defeat all {TotalOpponents} opponents");
                    progress.Percentage = (reputation / (float)REQUIRED_REPUTATION) * 50f; // First 50% is unlock
                    progress.ProgressDescription = $"Unlock at {REQUIRED_REPUTATION} reputation";
                }
            }
            else
            {
                progress.ProgressDescription = "No opponents available";
            }

            return progress;
        }

        public bool IsAchieved(CareerState career)
        {
            if (!IsUnlocked(career))
                return false;

            // Must have defeated all opponents
            return TotalOpponents > 0 && career.DefeatedOpponentIds.Count >= TotalOpponents;
        }
    }
}
