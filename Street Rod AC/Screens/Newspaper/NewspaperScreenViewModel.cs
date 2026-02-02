using System.Collections.ObjectModel;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Newspaper
{
    public class NewspaperScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IRaceEventService _eventService;
        private readonly ICarFilterService _filterService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IEventOpponentService _eventOpponentService;

        public RelayCommand BackCommand { get; }
        public RelayCommand UsedCarsCommand { get; }
        public RelayCommand UsedPartsCommand { get; }
        public RelayCommand<EventInvitationViewModel> EnterEventCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";
        public bool SkipEnterAnimation { get; }

        // Race Invitations
        public ObservableCollection<EventInvitationViewModel> RaceInvitations { get; } = [];
        public bool HasRaceInvitations => RaceInvitations.Count > 0;

        public NewspaperScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IRaceEventService eventService,
            ICarFilterService filterService,
            IContentCatalogRepository catalogRepository,
            IEventOpponentService eventOpponentService,
            bool skipAnimation = false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _eventService = eventService;
            _filterService = filterService;
            _catalogRepository = catalogRepository;
            _eventOpponentService = eventOpponentService;
            SkipEnterAnimation = skipAnimation;

            BackCommand = new RelayCommand(OnBack);
            UsedCarsCommand = new RelayCommand(OnUsedCars);
            UsedPartsCommand = new RelayCommand(OnUsedParts);
            EnterEventCommand = new RelayCommand<EventInvitationViewModel>(OnEnterEvent);

            LoadRaceInvitations();
        }

        private void OnUsedCars()
        {
            _navigationService.NavigateToUsedCarMarket(_gameState);
        }

        private void OnUsedParts()
        {
            _navigationService.NavigateToUsedParts(_gameState);
        }

        private void OnBack()
        {
            _navigationService.NavigateToGame(_gameState);
        }

        private void LoadRaceInvitations()
        {
            RaceInvitations.Clear();

            var currentTime = _gameState.Date;
            var activeEvents = _eventService.GetActiveEvents(_gameState.Career, currentTime);

            foreach (var eventInstance in activeEvents)
            {
                var definition = _eventService.GetEventDefinition(eventInstance.EventDefinitionId);
                if (definition == null) continue;

                // Find eligible cars from player's garage
                var eligibleCars = FindEligibleCars(definition);

                var vm = EventInvitationViewModel.FromEvent(
                    eventInstance,
                    definition,
                    eligibleCars,
                    currentTime);

                RaceInvitations.Add(vm);
            }

            // Sort: events with eligible cars first, then by expiry
            var sorted = RaceInvitations
                .OrderByDescending(e => e.HasEligibleCars)
                .ThenBy(e => e.ExpiresAt ?? DateTime.MaxValue)
                .ToList();

            RaceInvitations.Clear();
            foreach (var e in sorted)
            {
                RaceInvitations.Add(e);
            }

            OnPropertyChanged(nameof(HasRaceInvitations));
        }

        private List<EligibleCarViewModel> FindEligibleCars(Models.Career.Events.RaceEventDefinition definition)
        {
            var eligible = new List<EligibleCarViewModel>();

            foreach (var car in _gameState.Player.Cars)
            {
                var carDef = _catalogRepository.GetCar(car.DefinitionId);
                if (carDef == null) continue;

                // Check filter (if any)
                if (definition.EntryRequirements != null)
                {
                    if (!_filterService.Matches(definition.EntryRequirements, carDef, car))
                        continue;
                }

                var displayName = $"{carDef.Year} {carDef.Brand} {carDef.Name}";
                eligible.Add(EligibleCarViewModel.FromCar(car, displayName));
            }

            return eligible;
        }

        private void OnEnterEvent(EventInvitationViewModel? eventVm)
        {
            if (eventVm == null || !eventVm.CanEnter)
                return;

            // Get event definition
            var eventDef = _eventService.GetEventDefinition(eventVm.EventId);
            if (eventDef == null)
                return;

            // Get opponent for the event
            var opponent = _eventOpponentService.GetOpponentForEvent(eventDef, _gameState);
            if (opponent == null)
            {
                var errorDialog = new Dialogs.Information.InformationDialogViewModel(
                    _dialogService,
                    "No opponent available for this event.",
                    "Cannot Enter Event");
                _dialogService.ShowDialog(errorDialog);
                return;
            }

            // Get opponent car display name
            var opponentCarDef = _catalogRepository.GetCar(opponent.CarDefinitionId);
            var opponentCarDisplay = opponentCarDef != null
                ? $"{opponentCarDef.Year} {opponentCarDef.Brand} {opponentCarDef.Name}"
                : "Unknown Car";

            // Show event entry dialog
            var dialog = new Dialogs.EventEntry.EventEntryDialogViewModel(
                _dialogService,
                eventVm,
                eventDef,
                opponent,
                opponentCarDisplay,
                OnEventEntryComplete);
            _dialogService.ShowDialog(dialog);
        }

        private void OnEventEntryComplete(Dialogs.EventEntry.EventEntryResult? result)
        {
            if (result == null)
                return;

            // Get player's selected car
            var playerCar = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == result.PlayerCarInstanceId);
            if (playerCar == null)
                return;

            // Create race launch intent
            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            var opponentCarDef = _catalogRepository.GetCar(result.Opponent.CarDefinitionId);

            if (playerCarDef == null || opponentCarDef == null)
                return;

            // Build the launch intent for the event race
            var launchIntent = new Services.Configuration.Models.DragRaceLaunchIntent
            {
                PlayerCarId = playerCar.DefinitionId,
                PlayerSkin = playerCar.SkinId ?? "default",
                PlayerName = _gameState.Player.Name,
                PlayerCarInstanceId = playerCar.InstanceId,
                OpponentCarId = result.Opponent.CarDefinitionId,
                OpponentSkin = result.Opponent.CarSkin,
                OpponentName = result.Opponent.OpponentName,
                OpponentCarInstanceId = result.Opponent.PoolOpponentCar?.InstanceId ?? Guid.Empty,
                OpponentAILevel = result.Opponent.Skill,
                OpponentAIAggression = result.Opponent.Aggression,
                IsPinkSlip = result.IsPinkSlip
            };

            // Create race context for result processing
            var raceContext = new Models.Race.RaceContext
            {
                PlayerName = _gameState.Player.Name,
                OpponentName = result.Opponent.OpponentName,
                PlayerCarInstanceId = playerCar.InstanceId,
                OpponentCarInstanceId = result.Opponent.PoolOpponentCar?.InstanceId ?? Guid.Empty,
                CashWager = 0, // Event rewards handled separately
                IsPinkSlip = result.IsPinkSlip,
                TrackId = result.EventDefinition.TrackId ?? "drag_strip",
                RaceType = result.EventDefinition.RaceType,
                EventId = result.EventId,
                IsEventOnlyOpponent = !result.Opponent.IsPoolOpponent
            };

            launchIntent.Metadata["RaceContext"] = raceContext;

            // Navigate to race loading
            _navigationService.NavigateToRaceLoading(_gameState, launchIntent);
        }

        public override void Enter()
        {
            base.Enter();

            // Refresh event list in case it changed
            LoadRaceInvitations();

            // Only spend time when actually visiting (not returning from sub-screens)
            if (!SkipEnterAnimation)
            {
                _ = ((App)System.Windows.Application.Current).SpendTimeAsync(GameAction.VisitNewspaper);
            }
        }

        public override void Exit()
        {
            base.Exit();
        }
    }
}
