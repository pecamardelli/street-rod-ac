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
        public RelayCommand<EligibleCarViewModel> SelectCarCommand { get; }

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

        /// <summary>
        /// The cars that may enter: copies of the invitation's, so what is picked here is this dialog's alone
        /// and a cancelled pick does not show up highlighted the next time the invitation is opened
        /// </summary>
        public List<EligibleCarViewModel> EligibleCars { get; }

        private EligibleCarViewModel? _selectedCar;

        /// <summary>The car that enters. Setting it moves the highlight: exactly the selected car shows as picked</summary>
        public EligibleCarViewModel? SelectedCar
        {
            get => _selectedCar;
            set
            {
                if (ReferenceEquals(_selectedCar, value)) return;

                if (_selectedCar != null) _selectedCar.IsSelected = false;
                _selectedCar = value;
                if (_selectedCar != null) _selectedCar.IsSelected = true;

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
            EligibleCars = eventVm.EligibleCars.Select(c => c.Copy()).ToList();
            if (EligibleCars.Count > 0)
            {
                SelectedCar = EligibleCars[0];
            }

            ConfirmCommand = new RelayCommand(OnConfirm, () => CanConfirm);
            CancelCommand = new RelayCommand(OnCancel);
            SelectCarCommand = new RelayCommand<EligibleCarViewModel>(car =>
            {
                if (car != null) SelectedCar = car;
            });
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

            // Closed before the callback: it goes on to the race, and anything it shows must not close with this
            _dialogService.CloseDialog();
            _callback?.Invoke(result);
        }

        private void OnCancel()
        {
            _dialogService.CloseDialog();
            _callback?.Invoke(null);
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
