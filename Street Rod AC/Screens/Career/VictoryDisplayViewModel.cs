using System.ComponentModel;
using Street_Rod_AC.Models.Career.Victory;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Screens.Career
{
    /// <summary>
    /// View model for displaying a victory condition in the career screen
    /// </summary>
    public class VictoryDisplayViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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

        public string StatusColor => IsAchieved ? "#90EE90" : IsActive ? "#FFD700" : IsUnlocked ? "#FFFFFF" : "#808080";

        public string BorderColor => IsAchieved ? "#00AA00" : IsActive ? "#FFA500" : IsUnlocked ? "#505050" : "#303030";

        public string BackgroundColor => IsActive ? "#3A3A2A" : "#2A2A2A";

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

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
