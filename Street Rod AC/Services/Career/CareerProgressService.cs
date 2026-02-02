using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for checking career progress after races and showing notifications
    /// for newly completed milestones, unlocked victories, and game victories.
    /// </summary>
    public class CareerProgressService : ICareerProgressService
    {
        private readonly IMilestoneService _milestoneService;
        private readonly IVictoryConditionService _victoryService;
        private readonly DialogService _dialogService;
        private readonly IAppLogger _logger;

        public CareerProgressService(
            IMilestoneService milestoneService,
            IVictoryConditionService victoryService,
            DialogService dialogService)
        {
            _milestoneService = milestoneService;
            _victoryService = victoryService;
            _dialogService = dialogService;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.App);
        }

        /// <inheritdoc />
        public CareerProgressResult CheckProgressAfterRace(GameState gameState)
        {
            var result = new CareerProgressResult();

            // Check for newly completed milestones
            var newMilestones = _milestoneService.CheckForCompletedMilestones(gameState.Career);
            result.CompletedMilestones = newMilestones.Select(m => m.Id).ToList();

            if (newMilestones.Count > 0)
            {
                _logger.Information("Player completed {Count} milestone(s): {Milestones}",
                    newMilestones.Count, string.Join(", ", newMilestones.Select(m => m.Name)));
            }

            // Check for newly unlocked victory conditions
            var newVictoryUnlocks = _victoryService.CheckForNewUnlocks(gameState.Career);
            result.UnlockedVictories = newVictoryUnlocks.Select(v => v.VictoryType).ToList();

            if (newVictoryUnlocks.Count > 0)
            {
                _logger.Information("Player unlocked {Count} victory condition(s): {Victories}",
                    newVictoryUnlocks.Count, string.Join(", ", newVictoryUnlocks.Select(v => v.Name)));
            }

            // Check for game victory (only if not already won)
            if (!gameState.Career.HasWonGame)
            {
                var achievedVictory = _victoryService.CheckForVictory(gameState);
                if (achievedVictory != null)
                {
                    result.AchievedVictory = achievedVictory.VictoryType;
                    gameState.Career.HasWonGame = true;
                    gameState.Career.WinningVictoryType = achievedVictory.VictoryType;

                    _logger.Information("Player achieved victory: {VictoryType} - {VictoryName}",
                        achievedVictory.VictoryType, achievedVictory.Name);
                }
            }

            // Show notifications (priority: victory > milestones > unlocks)
            ShowNotifications(newMilestones, newVictoryUnlocks, result.AchievedVictory != null ? _victoryService.GetVictoryCondition(result.AchievedVictory) : null);

            return result;
        }

        /// <summary>
        /// Show notifications for career progress, with priority:
        /// Victory > Milestones > Victory Unlocks
        /// </summary>
        private void ShowNotifications(
            List<MilestoneDefinition> newMilestones,
            List<IVictoryCondition> newVictoryUnlocks,
            IVictoryCondition? achievedVictory)
        {
            // Priority 1: Game Victory
            if (achievedVictory != null)
            {
                ShowVictoryDialog(achievedVictory, newMilestones, newVictoryUnlocks);
                return;
            }

            // Priority 2: Milestones (with optional unlock info)
            if (newMilestones.Count > 0)
            {
                ShowMilestoneDialog(newMilestones, newVictoryUnlocks);
                return;
            }

            // Priority 3: Victory Unlocks only
            if (newVictoryUnlocks.Count > 0)
            {
                ShowUnlockDialog(newVictoryUnlocks);
            }
        }

        /// <summary>
        /// Show dialog for game victory achievement
        /// </summary>
        private void ShowVictoryDialog(
            IVictoryCondition achievedVictory,
            List<MilestoneDefinition> newMilestones,
            List<IVictoryCondition> newVictoryUnlocks)
        {
            var message = $"Congratulations!\n\nYou have achieved the {achievedVictory.Name} victory!\n\n{achievedVictory.Description}";

            // Add milestone info if any were completed
            if (newMilestones.Count > 0)
            {
                message += "\n\nMilestones also completed:";
                foreach (var milestone in newMilestones)
                {
                    message += $"\n  {milestone.Name}";
                }
            }

            // Add unlock info if any new victories were unlocked
            if (newVictoryUnlocks.Count > 0)
            {
                message += "\n\nNew victory paths unlocked:";
                foreach (var victory in newVictoryUnlocks.Where(v => v.VictoryType != achievedVictory.VictoryType))
                {
                    message += $"\n  {victory.Name}";
                }
            }

            var dialog = new InformationDialogViewModel(
                _dialogService,
                message,
                "VICTORY!"
            );
            _dialogService.ShowDialog(dialog);
        }

        /// <summary>
        /// Show dialog for completed milestones
        /// </summary>
        private void ShowMilestoneDialog(
            List<MilestoneDefinition> milestones,
            List<IVictoryCondition> newVictoryUnlocks)
        {
            string message;

            if (milestones.Count == 1)
            {
                var milestone = milestones[0];
                message = $"{milestone.Name}\n\n{milestone.Description}";

                // Show unlocks for this milestone
                if (milestone.Unlocks.Count > 0)
                {
                    var unlockDescriptions = GetUnlockDescriptions(milestone.Unlocks, newVictoryUnlocks);
                    if (unlockDescriptions.Count > 0)
                    {
                        message += "\n\nUnlocked:";
                        foreach (var unlock in unlockDescriptions)
                        {
                            message += $"\n  {unlock}";
                        }
                    }
                }
            }
            else
            {
                message = $"You completed {milestones.Count} milestones!\n";
                foreach (var milestone in milestones)
                {
                    message += $"\n  {milestone.Name}";
                }

                // Show any new victory unlocks
                if (newVictoryUnlocks.Count > 0)
                {
                    message += "\n\nNew victory paths unlocked:";
                    foreach (var victory in newVictoryUnlocks)
                    {
                        message += $"\n  {victory.Name}";
                    }
                }
            }

            var dialog = new InformationDialogViewModel(
                _dialogService,
                message,
                "Milestone Complete"
            );
            _dialogService.ShowDialog(dialog);
        }

        /// <summary>
        /// Show dialog for unlocked victory conditions (when no milestones were completed)
        /// </summary>
        private void ShowUnlockDialog(List<IVictoryCondition> newVictoryUnlocks)
        {
            string message;

            if (newVictoryUnlocks.Count == 1)
            {
                var victory = newVictoryUnlocks[0];
                message = $"New Victory Path Unlocked!\n\n{victory.Name}\n{victory.Description}";
            }
            else
            {
                message = "New Victory Paths Unlocked!\n";
                foreach (var victory in newVictoryUnlocks)
                {
                    message += $"\n  {victory.Name}";
                }
            }

            var dialog = new InformationDialogViewModel(
                _dialogService,
                message,
                "Path Unlocked"
            );
            _dialogService.ShowDialog(dialog);
        }

        /// <summary>
        /// Get human-readable descriptions for milestone unlocks
        /// </summary>
        private List<string> GetUnlockDescriptions(
            List<string> unlockIds,
            List<IVictoryCondition> newVictoryUnlocks)
        {
            var descriptions = new List<string>();

            foreach (var unlockId in unlockIds)
            {
                // Check if it's a victory unlock
                var victory = newVictoryUnlocks.FirstOrDefault(v => v.VictoryType == unlockId);
                if (victory != null)
                {
                    descriptions.Add($"{victory.Name} victory path");
                    continue;
                }

                // Check if it's a victory type we know about
                var knownVictory = _victoryService.GetVictoryCondition(unlockId);
                if (knownVictory != null)
                {
                    descriptions.Add($"{knownVictory.Name} victory path");
                    continue;
                }

                // Unknown unlock type - just show the ID
                descriptions.Add(unlockId);
            }

            return descriptions;
        }
    }
}
