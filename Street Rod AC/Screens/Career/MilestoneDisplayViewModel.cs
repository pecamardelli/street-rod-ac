using System.ComponentModel;
using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Screens.Career
{
    /// <summary>
    /// View model for displaying a milestone in the career screen
    /// </summary>
    public class MilestoneDisplayViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Category { get; set; }

        // Progress
        public bool IsCompleted { get; set; }
        public int CurrentValue { get; set; }
        public int TargetValue { get; set; }
        public float ProgressPercentage { get; set; }

        // Display text
        public string ProgressText => IsCompleted ? "Completed!" : $"{CurrentValue}/{TargetValue}";

        public string TooltipText => IsCompleted
            ? $"{Name}\n{Description}\n\nCompleted!"
            : $"{Name}\n{Description}\n\nProgress: {CurrentValue}/{TargetValue}";

        // Visual properties
        public string StatusIcon => IsCompleted ? "✓" : "○";

        public string StatusColor => IsCompleted ? "#90EE90" : "#FFFFFF";

        public string BackgroundColor => IsCompleted ? "#2A3A2A" : "#2A2A2A";

        public string BorderColor => IsCompleted ? "#00AA00" : "#404040";

        public double Opacity => IsCompleted ? 1.0 : 0.8;

        /// <summary>
        /// Create a view model from a milestone definition and career state
        /// </summary>
        public static MilestoneDisplayViewModel FromMilestone(
            MilestoneDefinition milestone,
            CareerState career)
        {
            var currentValue = career.GetCounter(milestone.Trigger);
            var isCompleted = career.CompletedMilestones.Contains(milestone.Id);

            return new MilestoneDisplayViewModel
            {
                Id = milestone.Id,
                Name = milestone.Name,
                Description = milestone.Description,
                Category = milestone.Category,
                IsCompleted = isCompleted,
                CurrentValue = Math.Min(currentValue, milestone.TargetValue),
                TargetValue = milestone.TargetValue,
                ProgressPercentage = milestone.TargetValue > 0
                    ? Math.Min(100f, (currentValue / (float)milestone.TargetValue) * 100f)
                    : 0f
            };
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
