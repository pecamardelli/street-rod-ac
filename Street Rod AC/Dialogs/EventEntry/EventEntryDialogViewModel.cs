using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Screens.Newspaper;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.EventEntry
{
    /// <summary>
    /// View model for the event entry dialog where players select a car and confirm entry
    /// </summary>
    public class EventEntryDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action<EventEntryResult?> _callback;

        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }

        // Event info
        public string EventName { get; }
        public string EventDescription { get; }
        public string Requirements { get; }
        public string Reward { get; }
        public bool IsPinkSlip { get; }
        public string RaceTypeDisplay { get; }

        // Opponent info
        public string OpponentName { get; }
        public string? OpponentNickname { get; }
        public string OpponentCarDisplay { get; }
        public bool IsPoolOpponent { get; }

        // Car selection
        public List<EligibleCarViewModel> EligibleCars { get; }

        private EligibleCarViewModel? _selectedCar;
        public EligibleCarViewModel? SelectedCar
        {
            get => _selectedCar;
            set
            {
                _selectedCar = value;
                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(CanConfirm));
                OnPropertyChanged(nameof(SelectedCarDisplay));
            }
        }

        public string SelectedCarDisplay => SelectedCar?.DisplayName ?? "Select a car...";
        public bool CanConfirm => SelectedCar != null;

        // For result
        private readonly EventInvitationViewModel _eventVm;
        private readonly EventOpponentResult _opponent;
        private readonly RaceEventDefinition _eventDef;

        public EventEntryDialogViewModel(
            DialogService dialogService,
            EventInvitationViewModel eventVm,
            RaceEventDefinition eventDef,
            EventOpponentResult opponent,
            string opponentCarDisplay,
            Action<EventEntryResult?> callback)
        {
            _dialogService = dialogService;
            _eventVm = eventVm;
            _eventDef = eventDef;
            _opponent = opponent;
            _callback = callback;

            // Event info
            EventName = eventVm.Name;
            EventDescription = eventVm.Description;
            Requirements = eventVm.RequirementsDescription;
            Reward = eventVm.RewardDescription;
            IsPinkSlip = eventVm.IsPinkSlip;
            RaceTypeDisplay = eventVm.RaceTypeDisplay;

            // Opponent info
            OpponentName = opponent.OpponentName;
            OpponentNickname = opponent.OpponentNickname;
            OpponentCarDisplay = opponentCarDisplay;
            IsPoolOpponent = opponent.IsPoolOpponent;

            // Car selection
            EligibleCars = eventVm.EligibleCars;
            if (EligibleCars.Count > 0)
            {
                SelectedCar = EligibleCars[0];
            }

            ConfirmCommand = new RelayCommand(OnConfirm, () => CanConfirm);
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void OnConfirm()
        {
            if (SelectedCar == null)
                return;

            var result = new EventEntryResult
            {
                EventId = _eventVm.EventId,
                EventInstanceId = _eventVm.InstanceId,
                PlayerCarInstanceId = SelectedCar.InstanceId,
                Opponent = _opponent,
                EventDefinition = _eventDef,
                IsPinkSlip = IsPinkSlip
            };

            _callback?.Invoke(result);
            _dialogService.CloseDialog();
        }

        private void OnCancel()
        {
            _callback?.Invoke(null);
            _dialogService.CloseDialog();
        }
    }

    /// <summary>
    /// Result of event entry dialog
    /// </summary>
    public class EventEntryResult
    {
        public string EventId { get; set; } = string.Empty;
        public Guid EventInstanceId { get; set; }
        public Guid PlayerCarInstanceId { get; set; }
        public EventOpponentResult Opponent { get; set; } = null!;
        public RaceEventDefinition EventDefinition { get; set; } = null!;
        public bool IsPinkSlip { get; set; }
    }
}
