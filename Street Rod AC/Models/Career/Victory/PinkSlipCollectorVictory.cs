using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Victory achieved by collecting a certain number of cars won via pink slip races.
    /// Unlocks after winning 5 pink slip races.
    /// </summary>
    public class PinkSlipCollectorVictory : IVictoryCondition
    {
        public const int REQUIRED_PINK_SLIP_WINS_TO_UNLOCK = 5;
        public const int TARGET_CARS_WON = 10;

        public string VictoryType => "PinkSlipCollector";
        public string Name => "Pink Slip Collector";
        public string Description => $"Win {TARGET_CARS_WON} cars through pink slip races to prove you're the ultimate high-stakes racer.";

        public bool IsUnlocked(CareerState career)
        {
            return career.GetCounter(Milestones.MilestoneTrigger.PinkSlipWins) >= REQUIRED_PINK_SLIP_WINS_TO_UNLOCK;
        }

        public VictoryProgress GetProgress(CareerState career)
        {
            var pinkSlipWins = career.GetCounter(Milestones.MilestoneTrigger.PinkSlipWins);
            var percentage = Math.Min(100f, (pinkSlipWins / (float)TARGET_CARS_WON) * 100f);

            var progress = new VictoryProgress
            {
                Percentage = percentage,
                ProgressDescription = $"Cars won: {pinkSlipWins}/{TARGET_CARS_WON}"
            };

            // Unlock step
            if (pinkSlipWins >= REQUIRED_PINK_SLIP_WINS_TO_UNLOCK)
            {
                progress.CompletedSteps.Add($"Win {REQUIRED_PINK_SLIP_WINS_TO_UNLOCK} pink slip races (unlocked!)");
            }
            else
            {
                progress.RemainingSteps.Add($"Win {REQUIRED_PINK_SLIP_WINS_TO_UNLOCK} pink slip races to unlock ({pinkSlipWins}/{REQUIRED_PINK_SLIP_WINS_TO_UNLOCK})");
            }

            // Victory step
            if (pinkSlipWins >= TARGET_CARS_WON)
            {
                progress.CompletedSteps.Add($"Win {TARGET_CARS_WON} cars via pink slip");
            }
            else if (IsUnlocked(career))
            {
                progress.RemainingSteps.Add($"Win {TARGET_CARS_WON - pinkSlipWins} more cars via pink slip");
            }

            return progress;
        }

        public bool IsAchieved(CareerState career)
        {
            return career.GetCounter(Milestones.MilestoneTrigger.PinkSlipWins) >= TARGET_CARS_WON;
        }
    }
}
