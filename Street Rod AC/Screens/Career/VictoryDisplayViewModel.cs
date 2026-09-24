using Street_Rod_AC.ViewModels;
using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Screens.Career
{
    /// <summary>
    /// View model for displaying a victory condition in the career screen
    /// </summary>
    public class VictoryDisplayViewModel : ObservableObject
    {
        public string VictoryType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // State
        public bool IsUnlocked { get; set; }
        public bool IsActive { get; set; }
        public bool IsAchieved { get; set; }

        // Progress
        public float ProgressPercentage { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public List<string> CompletedSteps { get; set; } = [];
        public List<string> RemainingSteps { get; set; } = [];

        // Visual properties
        public string StatusIcon => IsAchieved ? "✓" : IsActive ? "★" : IsUnlocked ? "○" : "🔒";

        // The colours are the view's: it paints the card from IsUnlocked, IsActive and IsAchieved, in that order

        public double Opacity => IsUnlocked ? 1.0 : 0.6;

        public bool CanSetActive => IsUnlocked && !IsAchieved;

        /// <summary>
        /// Create a view model from a victory condition and career state
        /// </summary>
        public static VictoryDisplayViewModel FromCondition(
            IVictoryCondition condition,
            CareerState career)
        {
            var progress = condition.GetProgress(career);
            var isActive = career.ActiveVictoryType == condition.VictoryType;

            return new VictoryDisplayViewModel
            {
                VictoryType = condition.VictoryType,
                Name = condition.Name,
                Description = condition.Description,
                IsUnlocked = condition.IsUnlocked(career),
                IsActive = isActive,
                IsAchieved = condition.IsAchieved(career),
                ProgressPercentage = progress.Percentage,
                ProgressText = progress.ProgressDescription,
                CompletedSteps = progress.CompletedSteps,
                RemainingSteps = progress.RemainingSteps
            };
        }
    }
}
