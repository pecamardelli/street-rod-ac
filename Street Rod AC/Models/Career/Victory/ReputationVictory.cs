using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Victory achieved by reaching maximum reputation.
    /// Always available from game start.
    /// </summary>
    public class ReputationVictory : IVictoryCondition
    {
        public const int TARGET_REPUTATION = 100;

        public string VictoryType => "Reputation";
        public string Name => "Street Legend";
        public string Description => $"Become a legend of the streets by reaching {TARGET_REPUTATION} reputation.";

        public bool IsUnlocked(CareerState career)
        {
            // Always available
            return true;
        }

        public VictoryProgress GetProgress(CareerState career)
        {
            var currentRep = career.GetCounter(Milestones.MilestoneTrigger.ReputationReached);
            var percentage = Math.Min(100f, (currentRep / (float)TARGET_REPUTATION) * 100f);

            var progress = new VictoryProgress
            {
                Percentage = percentage,
                ProgressDescription = $"Reputation: {currentRep}/{TARGET_REPUTATION}"
            };

            if (currentRep >= TARGET_REPUTATION)
            {
                progress.CompletedSteps.Add($"Reach {TARGET_REPUTATION} reputation");
            }
            else
            {
                progress.RemainingSteps.Add($"Reach {TARGET_REPUTATION} reputation ({TARGET_REPUTATION - currentRep} more needed)");
            }

            return progress;
        }

        public bool IsAchieved(CareerState career)
        {
            return career.GetCounter(Milestones.MilestoneTrigger.ReputationReached) >= TARGET_REPUTATION;
        }
    }
}
