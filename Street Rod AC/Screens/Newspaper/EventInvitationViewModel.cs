using System.ComponentModel;
using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Screens.Newspaper
{
    /// <summary>
    /// View model for displaying a race event invitation in the newspaper
    /// </summary>
    public class EventInvitationViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // Event definition info
        public string EventId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RequirementsDescription { get; set; } = string.Empty;
        public string RewardDescription { get; set; } = string.Empty;
        public RaceType RaceType { get; set; }
        public bool IsPinkSlip { get; set; }

        // Instance state
        public Guid InstanceId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string ExpiresInText { get; set; } = string.Empty;
        public bool IsExpiringSoon { get; set; }

        // Eligibility
        public bool HasEligibleCars { get; set; }
        public List<EligibleCarViewModel> EligibleCars { get; set; } = [];
        public string EligibilityText { get; set; } = string.Empty;

        // Visual
        public double Opacity => HasEligibleCars ? 1.0 : 0.5;
        public bool CanEnter => HasEligibleCars;

        public string RaceTypeDisplay => RaceType switch
        {
            RaceType.DragRace => "Drag Race",
            RaceType.Circuit => "Circuit Race",
            RaceType.Sprint => "Sprint Race",
            _ => "Race"
        };

        public string ExpiryColor => IsExpiringSoon ? "#FF6B6B" : "#FFA500";

        public string EligibilityColor => HasEligibleCars ? "#90EE90" : "#FF6B6B";

        public string PinkSlipWarning => IsPinkSlip ? "PINK SLIP - Risk your car!" : string.Empty;
        public bool ShowPinkSlipWarning => IsPinkSlip;

        /// <summary>
        /// Create a view model from an event instance and definition
        /// </summary>
        public static EventInvitationViewModel FromEvent(
            RaceEventInstance instance,
            RaceEventDefinition definition,
            List<EligibleCarViewModel> eligibleCars,
            DateTime currentTime)
        {
            var expiresIn = CalculateExpiresIn(instance.ExpiresAt, currentTime);
            var isExpiringSoon = instance.ExpiresAt.HasValue &&
                (instance.ExpiresAt.Value - currentTime).TotalHours < 24;

            return new EventInvitationViewModel
            {
                EventId = definition.Id,
                InstanceId = instance.InstanceId,
                Name = definition.Name,
                Description = definition.Description,
                RequirementsDescription = definition.GetEntryRequirementsDescription(),
                RewardDescription = definition.Reward.GetDescription(),
                RaceType = definition.RaceType,
                IsPinkSlip = definition.IsPinkSlip,
                ExpiresAt = instance.ExpiresAt,
                ExpiresInText = expiresIn,
                IsExpiringSoon = isExpiringSoon,
                HasEligibleCars = eligibleCars.Count > 0,
                EligibleCars = eligibleCars,
                EligibilityText = eligibleCars.Count > 0
                    ? $"{eligibleCars.Count} car{(eligibleCars.Count > 1 ? "s" : "")} eligible"
                    : "No eligible cars"
            };
        }

        private static string CalculateExpiresIn(DateTime? expiresAt, DateTime currentTime)
        {
            if (!expiresAt.HasValue)
                return "No expiration";

            var timeLeft = expiresAt.Value - currentTime;

            if (timeLeft.TotalDays >= 1)
                return $"Expires in {(int)timeLeft.TotalDays} day{((int)timeLeft.TotalDays > 1 ? "s" : "")}";

            if (timeLeft.TotalHours >= 1)
                return $"Expires in {(int)timeLeft.TotalHours} hour{((int)timeLeft.TotalHours > 1 ? "s" : "")}";

            if (timeLeft.TotalMinutes > 0)
                return $"Expires soon!";

            return "Expired";
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents a car that is eligible for an event
    /// </summary>
    public class EligibleCarViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid InstanceId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string ConditionText { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public static EligibleCarViewModel FromCar(Car car, string displayName)
        {
            return new EligibleCarViewModel
            {
                InstanceId = car.InstanceId,
                DisplayName = displayName,
                ConditionText = GetConditionText(car.BodyCondition)
            };
        }

        private static string GetConditionText(double condition)
        {
            return condition switch
            {
                >= 0.9 => "Excellent condition",
                >= 0.7 => "Good condition",
                >= 0.5 => "Fair condition",
                >= 0.3 => "Poor condition",
                _ => "Needs repair"
            };
        }
    }
}
