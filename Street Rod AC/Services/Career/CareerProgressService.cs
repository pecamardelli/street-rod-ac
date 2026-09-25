using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for checking career progress after races and putting what the player should hear about newly
    /// completed milestones, unlocked victories and game victories into words. It opens no dialogs: the texts
    /// go back as <see cref="PlayerMessage"/>s on the result, and the screen that ran the race shows them.
    /// </summary>
    public class CareerProgressService : ICareerProgressService
    {
        /// <summary>Prefix of a milestone unlock that opens a victory path, e.g. "victory:King"</summary>
        private const string VictoryUnlockPrefix = "victory:";

        private readonly IMilestoneService _milestoneService;
        private readonly IVictoryConditionService _victoryService;
        private readonly IAppLogger _logger;

        public CareerProgressService(
            IMilestoneService milestoneService,
            IVictoryConditionService victoryService)
        {
            _milestoneService = milestoneService;
            _victoryService = victoryService;
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
            var achievedVictory = _victoryService.ClaimVictory(gameState);
            if (achievedVictory != null)
            {
                result.AchievedVictory = achievedVictory.VictoryType;
                _logger.Information("Player achieved victory: {VictoryType} - {VictoryName}",
                    achievedVictory.VictoryType, achievedVictory.Name);
            }

            // What to tell the player (priority: victory > milestones > unlocks)
            if (ComposeMessage(gameState, newMilestones, newVictoryUnlocks, achievedVictory) is { } message)
            {
                result.PlayerMessages.Add(message);
            }

            return result;
        }

        /// <summary>
        /// The one message about career progress, with priority:
        /// Victory > Milestones > Victory Unlocks. Null when there is nothing to tell.
        /// </summary>
        private PlayerMessage? ComposeMessage(
            GameState gameState,
            List<MilestoneDefinition> newMilestones,
            List<IVictoryCondition> newVictoryUnlocks,
            IVictoryCondition? achievedVictory)
        {
            // Priority 1: Game Victory
            if (achievedVictory != null)
            {
                return VictoryMessage(gameState, achievedVictory, newMilestones, newVictoryUnlocks);
            }

            // Priority 2: Milestones (with optional unlock info)
            if (newMilestones.Count > 0)
            {
                return MilestoneMessage(newMilestones, newVictoryUnlocks);
            }

            // Priority 3: Victory Unlocks only
            return newVictoryUnlocks.Count > 0 ? UnlockMessage(newVictoryUnlocks) : null;
        }

        /// <summary>
        /// The message for a game victory: shown as the victory screen (<see cref="VictoryCard"/>), with what
        /// else the race brought as its footnote
        /// </summary>
        public static PlayerMessage VictoryMessage(
            GameState gameState,
            IVictoryCondition achievedVictory,
            List<MilestoneDefinition> newMilestones,
            List<IVictoryCondition> newVictoryUnlocks)
        {
            var footnote = new List<string>();
            if (newMilestones.Count > 0)
                footnote.Add("Milestones also completed: " + string.Join(", ", newMilestones.Select(m => m.Name)));

            var otherUnlocks = newVictoryUnlocks.Where(v => v.VictoryType != achievedVictory.VictoryType).ToList();
            if (otherUnlocks.Count > 0)
                footnote.Add("New victory paths unlocked: " + string.Join(", ", otherUnlocks.Select(v => v.Name)));

            var card = VictoryCard.For(achievedVictory.Name, achievedVictory.Description, gameState, string.Join("\n", footnote));
            var text = $"Congratulations!\n\nYou have achieved the {achievedVictory.Name} victory!\n\n{achievedVictory.Description}";
            if (footnote.Count > 0) text += "\n\n" + card.Footnote;

            return new PlayerMessage("VICTORY!", text) { Victory = card };
        }

        /// <summary>
        /// The message for completed milestones
        /// </summary>
        private PlayerMessage MilestoneMessage(
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

            return new PlayerMessage("Milestone Complete", message);
        }

        /// <summary>
        /// The message for unlocked victory conditions (when no milestones were completed)
        /// </summary>
        private static PlayerMessage UnlockMessage(List<IVictoryCondition> newVictoryUnlocks)
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

            return new PlayerMessage("Path Unlocked", message);
        }

        /// <summary>
        /// Get human-readable descriptions for milestone unlocks. Unlock ids carry their kind as a prefix
        /// ("victory:King"); one that names nothing the game knows is left out rather than shown raw.
        /// </summary>
        private List<string> GetUnlockDescriptions(
            List<string> unlockIds,
            List<IVictoryCondition> newVictoryUnlocks)
        {
            var descriptions = new List<string>();

            foreach (var rawId in unlockIds)
            {
                if (!rawId.StartsWith(VictoryUnlockPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning("Milestone unlock {Unlock} names nothing the game knows; not shown", rawId);
                    continue;
                }

                var unlockId = rawId[VictoryUnlockPrefix.Length..];

                // Check if it's a victory unlock
                var victory = newVictoryUnlocks.FirstOrDefault(v => string.Equals(v.VictoryType, unlockId, StringComparison.OrdinalIgnoreCase));
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

                _logger.Warning("Milestone unlock {Unlock} names no victory path; not shown", rawId);
            }

            return descriptions;
        }
    }
}
