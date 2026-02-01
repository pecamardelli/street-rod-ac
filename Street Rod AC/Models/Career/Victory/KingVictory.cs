using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Victory achieved by defeating "The King" in a pink slip race.
    /// Unlocks after reaching 10 wins and 50 reputation.
    /// </summary>
    public class KingVictory : IVictoryCondition
    {
        public const int REQUIRED_WINS = 10;
        public const int REQUIRED_REPUTATION = 50;
        public const string KING_NAME = "The King";

        public string VictoryType => "King";
        public string Name => "Dethrone the King";
        public string Description => "Challenge and defeat The King in a pink slip race to claim the crown.";

        public bool IsUnlocked(CareerState career)
        {
            var wins = career.GetCounter(Milestones.MilestoneTrigger.TotalWins);
            var reputation = career.GetCounter(Milestones.MilestoneTrigger.ReputationReached);

            return wins >= REQUIRED_WINS && reputation >= REQUIRED_REPUTATION;
        }

        public VictoryProgress GetProgress(CareerState career)
        {
            var wins = career.GetCounter(Milestones.MilestoneTrigger.TotalWins);
            var reputation = career.GetCounter(Milestones.MilestoneTrigger.ReputationReached);
            var kingDefeated = career.DefeatedOpponentIds.Contains(KING_NAME);

            var progress = new VictoryProgress();

            // Calculate progress based on unlock requirements and king defeat
            var unlockProgress = 0f;
            if (wins >= REQUIRED_WINS)
            {
                unlockProgress += 33.3f;
                progress.CompletedSteps.Add($"Win {REQUIRED_WINS} races");
            }
            else
            {
                progress.RemainingSteps.Add($"Win {REQUIRED_WINS} races ({wins}/{REQUIRED_WINS})");
            }

            if (reputation >= REQUIRED_REPUTATION)
            {
                unlockProgress += 33.3f;
                progress.CompletedSteps.Add($"Reach {REQUIRED_REPUTATION} reputation");
            }
            else
            {
                progress.RemainingSteps.Add($"Reach {REQUIRED_REPUTATION} reputation ({reputation}/{REQUIRED_REPUTATION})");
            }

            if (kingDefeated)
            {
                unlockProgress += 33.4f;
                progress.CompletedSteps.Add("Defeat The King in a pink slip race");
            }
            else if (IsUnlocked(career))
            {
                progress.RemainingSteps.Add("Challenge and defeat The King in a pink slip race");
            }
            else
            {
                progress.RemainingSteps.Add("(Locked) Defeat The King in a pink slip race");
            }

            progress.Percentage = unlockProgress;
            progress.ProgressDescription = kingDefeated
                ? "The King has been dethroned!"
                : IsUnlocked(career)
                    ? "The King awaits your challenge"
                    : $"Prove yourself worthy ({wins}/{REQUIRED_WINS} wins, {reputation}/{REQUIRED_REPUTATION} rep)";

            return progress;
        }

        public bool IsAchieved(CareerState career)
        {
            // Must be unlocked AND have defeated The King
            return IsUnlocked(career) && career.DefeatedOpponentIds.Contains(KING_NAME);
        }
    }
}
