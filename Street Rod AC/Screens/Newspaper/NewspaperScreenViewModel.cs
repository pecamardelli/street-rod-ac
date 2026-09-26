using System.Collections.ObjectModel;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.News;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Storage;
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
        private readonly IAssettoCorsaContentService _contentService;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly ICarSaleService _saleService;
        private readonly IAppLogger _logger;

        // True from the moment the player confirms an entry until the race is set up (or not): the paper stays
        // clickable meanwhile, and a second entry would start a second race
        private bool _isEntering;

        public RelayCommand BackCommand { get; }
        public RelayCommand UsedCarsCommand { get; }
        public RelayCommand UsedPartsCommand { get; }
        public RelayCommand<EventInvitationViewModel> EnterEventCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";
        public bool SkipEnterAnimation { get; }

        // Race Invitations
        public ObservableCollection<EventInvitationViewModel> RaceInvitations { get; } = [];
        public bool HasRaceInvitations => RaceInvitations.Count > 0;

        /// <summary>The race pages: what the paper wrote about the last few days' races, the lead story first</summary>
        public ObservableCollection<NewsArticleViewModel> Articles { get; } = [];
        public bool HasArticles => Articles.Count > 0;

        /// <summary>The paper's date line</summary>
        public string EditionDisplay => _gameState.Date.ToString("dddd, MMMM d, yyyy");

        /// <summary>The player's own cars in the paper, and the buyers who called about them</summary>
        public ObservableCollection<PlayerCarAdViewModel> PlayerAds { get; } = [];
        public bool HasPlayerAds => PlayerAds.Count > 0;

        public NewspaperScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IRaceEventService eventService,
            ICarFilterService filterService,
            IContentCatalogRepository catalogRepository,
            IEventOpponentService eventOpponentService,
            IAssettoCorsaContentService contentService,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            RaceSetupBuilder raceSetup,
            ICarSaleService saleService,
            bool skipAnimation = false)
        {
            _saleService = saleService;
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _eventService = eventService;
            _filterService = filterService;
            _catalogRepository = catalogRepository;
            _eventOpponentService = eventOpponentService;
            _contentService = contentService;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _raceSetup = raceSetup;
            _logger = AppLoggerFactory.CreateLogger("Newspaper");
            SkipEnterAnimation = skipAnimation;

            BackCommand = new RelayCommand(OnBack);
            UsedCarsCommand = new RelayCommand(OnUsedCars);
            UsedPartsCommand = new RelayCommand(OnUsedParts);
            EnterEventCommand = new RelayCommand<EventInvitationViewModel>(OnEnterEvent, _ => !_isEntering);

            // The invitations are loaded in Enter, once: the screen is always entered right after it is made
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
            _navigationService.NavigateToGarage(_gameState);
        }

        private void LoadArticles()
        {
            Articles.Clear();
            try
            {
                var page = NewsWriter.FrontPage(_gameState.News, _gameState.Date);
                for (var i = 0; i < page.Count; i++) Articles.Add(new NewsArticleViewModel(page[i], _gameState.Date, isLead: i == 0));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not lay out the race pages");
            }

            OnPropertyChanged(nameof(HasArticles));
            OnPropertyChanged(nameof(EditionDisplay));
        }

        private void LoadPlayerAds()
        {
            PlayerAds.Clear();
            try
            {
                foreach (var ad in _gameState.NewspaperAds.PlayerCars)
                {
                    var car = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == ad.CarInstanceId);
                    if (car == null) continue;

                    var definition = _catalogRepository.GetCar(car.DefinitionId);
                    var name = definition != null ? $"{definition.Brand} {definition.Name}" : car.DefinitionId;
                    var hasOffer = ad.Offer is { } offer && offer.Expires >= _gameState.Date;
                    PlayerAds.Add(new PlayerCarAdViewModel(
                        name,
                        $"Asking ${ad.AskingPrice:N0}",
                        hasOffer ? $"{CarSaleService.Capitalized(ad.Offer!.BuyerName)} offers ${ad.Offer.Amount:N0}, until {ad.Offer.Expires:ddd h tt}" : "No buyer has called yet",
                        hasOffer,
                        new RelayCommand(() => OnAcceptOffer(ad, name)),
                        new RelayCommand(() => OnDeclineOffer(ad))));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the player's ads");
            }

            OnPropertyChanged(nameof(HasPlayerAds));
        }

        private void OnAcceptOffer(CarSaleAd ad, string carName)
        {
            if (ad.Offer is not { } offer) return;

            _dialogService.ShowDialog(new ConfirmationDialogViewModel(_dialogService,
                $"Sell the {carName} to {offer.BuyerName} for ${offer.Amount:N0}? The car is gone once it's sold.",
                "Sell the Car",
                async yes =>
                {
                    if (!yes) return;
                    try
                    {
                        var result = await _saleService.AcceptOfferAsync(_gameState, ad);
                        ShowSaleResult(result, result.Succeeded ? "Car Sold" : "No Sale");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Could not sell the car");
                        ShowSaleResult(new SaleResult(SaleOutcome.Refused, $"The car could not be sold:\n\n{ex.Message}"), "No Sale");
                    }
                }));
        }

        private void OnDeclineOffer(CarSaleAd ad)
        {
            _saleService.DeclineOffer(_gameState, ad);
            LoadPlayerAds();
        }

        private void ShowSaleResult(SaleResult result, string title)
        {
            LoadPlayerAds();
            OnPropertyChanged(nameof(BankrollDisplay));
            var message = result.SaveFailed ? result.Message + "\n\nThe game could not be saved: the details are in the log." : result.Message;
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
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
                    currentTime,
                    _gameState.Rules.RacePrizeMultiplier);

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
            if (eventVm == null || !eventVm.CanEnter || _isEntering)
                return;

            try
            {
                // Get event definition
                var eventDef = _eventService.GetEventDefinition(eventVm.EventId);
                if (eventDef == null)
                    return;

                // Get opponent for the event
                var opponent = _eventOpponentService.GetOpponentForEvent(eventDef, _gameState);
                if (opponent == null)
                {
                    var errorDialog = new InformationDialogViewModel(
                        _dialogService,
                        eventDef.IsPinkSlip
                            ? "Nobody is putting their pink slip up for this one today. Try again tomorrow."
                            : "No opponent available for this event.",
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
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the entry for {Event}", eventVm.EventId);
                ShowRaceError(ex);
            }
        }

        /// <summary>
        /// The player entered: the event is raced on its own track, with each car on what its parts make of it,
        /// through the same set-up as a challenge at the diner.
        /// </summary>
        private async void OnEventEntryComplete(Dialogs.EventEntry.EventEntryResult? result)
        {
            if (result == null || _isEntering)
                return;

            _isEntering = true;
            RelayCommand.RaiseCanExecuteChanged();
            try
            {
                await EnterEventAsync(result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not set up the race for {Event}", result.EventId);
                ShowRaceError(ex);
            }
            finally
            {
                _isEntering = false;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task EnterEventAsync(Dialogs.EventEntry.EventEntryResult result)
        {
            // Get player's selected car
            var playerCar = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == result.PlayerCarInstanceId);
            if (playerCar == null)
                return;

            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            var opponentCarDef = _catalogRepository.GetCar(result.Opponent.CarDefinitionId);
            if (playerCarDef == null || opponentCarDef == null)
            {
                _logger.Warning("Event {Event}: a car is missing from the catalog (player {Player}, opponent {Opponent})",
                    result.EventId, playerCar.DefinitionId, result.Opponent.CarDefinitionId);
                return;
            }

            // A bracket race is run to the quarter, on a strip that runs it: the mode only ends one there
            var definition = result.EventDefinition;
            var isBracket = definition.IsBracketRace;
            var track = isBracket
                ? RaceSetupBuilder.PickStrip(_contentService.GetTracks(), quarterOnly: true, definition.TrackId)
                : RaceSetupBuilder.PickTrack(_contentService.GetTracks(), definition.RaceType, definition.TrackId, result.EventInstanceId.GetHashCode());
            if (track == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    isBracket
                        ? "There is no drag strip installed that runs the quarter mile, and a bracket race is run to it."
                        : "There is no track installed this event can be raced on.",
                    "Cannot Enter Event"));
                return;
            }

            // The pool racer drives as they are; a one-off entrant is given the event's skill and aggression.
            // Either way the AC values come from the adapter, the one place they are made.
            var opponent = result.Opponent;
            var driver = opponent.PoolOpponent
                ?? new Opponent(opponent.OpponentName, 25, Gender.Other, opponent.Skill, opponent.Aggression);
            var ai = OpponentAIAdapter.ToAssettoCorsaAI(driver, _gameState.Rules);

            // A bracket race: the rival dials in from its car (a one-off entrant's has no history), the player picks theirs
            BracketSetup? bracket = null;
            if (isBracket)
            {
                var rivalDialIn = BracketRules.RivalDialInFor(opponent.PoolOpponentCar ?? new Car(opponent.CarDefinitionId), opponentCarDef);
                var playerDialIn = await Dialogs.DialIn.DialInDialogViewModel.AskAsync(_dialogService, playerCar, playerCarDef,
                    CarNames.Of(playerCarDef), opponent.OpponentName, rivalDialIn);
                if (playerDialIn == null)
                {
                    _logger.Information("The player backed out of {Event} at the dial-in", result.EventId);
                    return;
                }

                bracket = BracketRules.Setup(playerDialIn.Value, rivalDialIn, ai.AILevel);
            }

            var setup = await _raceSetup.BuildAsync(new RaceEntry
            {
                PlayerName = _gameState.Player.Name,
                PlayerCar = playerCar,
                OpponentName = opponent.OpponentName,
                OpponentCarId = opponent.CarDefinitionId,
                OpponentSkin = opponent.CarSkin,
                OpponentCar = opponent.PoolOpponentCar,
                OpponentAI = ai,
                TrackId = track.Value.TrackId,
                TrackConfig = track.Value.TrackConfig,
                RaceType = definition.RaceType,
                CashWager = 0, // Event rewards handled separately
                IsPinkSlip = result.IsPinkSlip,
                DamagePercent = _gameState.Rules.RaceDamagePercent,
                EventId = result.EventId,
                EventInstanceId = result.EventInstanceId,
                IsEventOnlyOpponent = !opponent.IsPoolOpponent,
                // An organised event: the police stay away, but it runs at the game's hour, after dark too
                RaceTime = _gameState.Date,
                Bracket = bracket
            });

            // Setting the cars up takes a moment: a player who put the paper down meanwhile has called it off
            if (!ReferenceEquals(_navigationService.CurrentScreen, this))
            {
                _logger.Information("The player left the newspaper while {Event} was set up; no race", result.EventId);
                return;
            }

            if (setup.Intent == null)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"Your car is not going anywhere: {setup.PlayerCarProblem}.\n\nSort it out in the garage first.",
                    "Car Won't Run"));
                return;
            }

            _logger.Information("Entering {Event} on {Track} {Config}", result.EventId, track.Value.TrackId, track.Value.TrackConfig ?? "");
            _navigationService.NavigateToRaceLoading(_gameState, setup.Intent);
        }

        private void ShowRaceError(Exception ex)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                $"The race could not be set up:\n\n{ex.Message}",
                "Race Not Started"));
        }

        public override async void Enter()
        {
            base.Enter();

            try
            {
                LoadRaceInvitations();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the race invitations");
            }

            LoadPlayerAds();
            LoadArticles();

            // Only spend time when actually visiting (not returning from sub-screens)
            if (SkipEnterAnimation)
                return;

            try
            {
                // Late in the evening that is the next morning, with another paper: the invitations are read
                // again, and the new day is saved
                var spent = await _timeService.SpendTimeAsync(_gameState, GameAction.VisitNewspaper);
                if (spent.NewDayStarted)
                {
                    LoadRaceInvitations();
                    LoadPlayerAds();
                    LoadArticles();
                }
                OnPropertyChanged(nameof(BankrollDisplay));

                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the newspaper");
            }
        }
    }
}
