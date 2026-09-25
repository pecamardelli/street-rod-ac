using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Career;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Dealers;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Settings;
using Street_Rod_AC.Services.Talk;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Navigation
{
    /// <summary>
    /// Central navigation service with typed factory methods for all screens.
    /// Screens should use these factory methods rather than directly instantiating other screens.
    ///
    /// Every service a screen needs comes in through here, so a screen's dependencies are all in its
    /// constructor. Every factory goes through <see cref="SafeNavigate"/>: a screen that fails to build or to
    /// enter is logged and reported, and the player stays where they were.
    /// </summary>
    public class NavigationService : ObservableObject
    {
        private IScreen? _currentScreen;
        private readonly DialogService _dialogService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly ICarProfileRepository _profileRepository;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly IOpponentChallengeService _opponentChallengeService;
        private readonly IGameStateRepository _gameStateRepository;
        private readonly IUsedCarMarketService _marketService;
        private readonly GameSettingsService _gameSettingsService;
        private readonly ICarProfileService _profileService;
        private readonly ITalkService _talkService;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly ICarPurchaseService _purchaseService;
        private readonly ICarSaleService _saleService;
        private readonly IGameTimeService _timeService;
        private readonly ICarPartsService _carPartsService;
        private readonly IPartsShopService _partsShopService;
        private readonly RaceCarDataService _raceCarDataService;
        private readonly IRaceEventService _raceEventService;
        private readonly ICarFilterService _carFilterService;
        private readonly IEventOpponentService _eventOpponentService;
        private readonly IVictoryConditionService _victoryConditionService;
        private readonly IMilestoneService _milestoneService;
        private readonly Action<GameState?> _setCurrentGame;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Navigation);

        /// <summary>The screen on show; null until the first navigation</summary>
        public IScreen? CurrentScreen
        {
            get => _currentScreen;
            private set => SetProperty(ref _currentScreen, value);
        }

        /// <param name="setCurrentGame">Makes a loaded or new game the one the app saves on exit and races with</param>
        public NavigationService(
            DialogService dialogService,
            IContentCatalogRepository catalogRepository,
            ICarProfileRepository profileRepository,
            IAssettoCorsaLauncher launcher,
            IAssettoCorsaContentService contentService,
            IOpponentChallengeService opponentChallengeService,
            IGameStateRepository gameStateRepository,
            IUsedCarMarketService marketService,
            GameSettingsService gameSettingsService,
            ICarProfileService profileService,
            ITalkService talkService,
            IDealerCatalog dealerCatalog,
            ICarPurchaseService purchaseService,
            ICarSaleService saleService,
            IGameTimeService timeService,
            ICarPartsService carPartsService,
            IPartsShopService partsShopService,
            RaceCarDataService raceCarDataService,
            IRaceEventService raceEventService,
            ICarFilterService carFilterService,
            IEventOpponentService eventOpponentService,
            IVictoryConditionService victoryConditionService,
            IMilestoneService milestoneService,
            Action<GameState?> setCurrentGame)
        {
            _dialogService = dialogService;
            _catalogRepository = catalogRepository;
            _profileRepository = profileRepository;
            _launcher = launcher;
            _contentService = contentService;
            _opponentChallengeService = opponentChallengeService;
            _gameStateRepository = gameStateRepository;
            _marketService = marketService;
            _gameSettingsService = gameSettingsService;
            _profileService = profileService;
            _talkService = talkService;
            _dealerCatalog = dealerCatalog;
            _purchaseService = purchaseService;
            _saleService = saleService;
            _timeService = timeService;
            _carPartsService = carPartsService;
            _partsShopService = partsShopService;
            _raceCarDataService = raceCarDataService;
            _raceEventService = raceEventService;
            _carFilterService = carFilterService;
            _eventOpponentService = eventOpponentService;
            _victoryConditionService = victoryConditionService;
            _milestoneService = milestoneService;
            _setCurrentGame = setCurrentGame;
            _raceSetup = new RaceSetupBuilder(carPartsService, raceCarDataService);
        }

        /// <summary>
        /// Low-level navigation method. Prefer using typed factory methods instead.
        /// </summary>
        public void NavigateTo(IScreen screen)
        {
            CurrentScreen?.Exit();
            CurrentScreen = screen;
            CurrentScreen?.Enter();
        }

        /// <summary>
        /// Builds a screen and goes to it. A screen whose constructor throws is never shown; one whose Enter
        /// throws is left again for the screen the player came from. Either way the error is logged and the
        /// player told, instead of the exception ending the game. False when the player did not get there.
        /// </summary>
        private bool SafeNavigate(string screenName, Func<IScreen> create)
        {
            IScreen screen;
            try
            {
                screen = create();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the {Screen} screen", screenName);
                ShowNavigationError(screenName);
                return false;
            }

            var previous = CurrentScreen;
            try
            {
                NavigateTo(screen);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go to the {Screen} screen", screenName);
                BackTo(previous, screen);
                ShowNavigationError(screenName);
                return false;
            }
        }

        /// <summary>
        /// Puts the screen the player came from back after a failed navigation, as far as it will go. It is
        /// resumed, never entered again: an Enter can launch AC (the race screen) or spend time and save (the
        /// newspaper, the diner, the parts pages), and a failed navigation must do neither a second time.
        /// </summary>
        private void BackTo(IScreen? previous, IScreen failed)
        {
            if (previous == null || ReferenceEquals(CurrentScreen, previous)) return;

            try
            {
                if (ReferenceEquals(CurrentScreen, failed)) failed.Exit();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The screen that failed could not be left cleanly");
            }

            try
            {
                CurrentScreen = previous;
                previous.Resume();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go back to the screen before");
            }
        }

        private void ShowNavigationError(string screenName)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                $"The {screenName} could not be opened. The details are in the log.",
                "Something Went Wrong"));
        }

        // ============================================================
        // TYPED FACTORY METHODS
        // Each method encapsulates the creation of a specific screen
        // ============================================================

        public bool NavigateToInit() => SafeNavigate("start screen", () =>
            new Screens.Init.InitScreenViewModel(this, _dialogService));

        public bool NavigateToMainMenu() => SafeNavigate("main menu", () =>
            new Screens.MainMenu.MainMenuScreenViewModel(this, _dialogService));

        public bool NavigateToSettings() => SafeNavigate("settings", () =>
            new Screens.Settings.SettingsScreenViewModel(this, _dialogService, _gameSettingsService));

        public bool NavigateToNewGame() => SafeNavigate("new game screen", () =>
            new Screens.NewGame.NewGameScreenViewModel(
                this,
                _dialogService,
                _gameStateRepository,
                _raceEventService,
                _setCurrentGame));

        public bool NavigateToLoadGame() => SafeNavigate("saved games", () =>
            new Screens.LoadGame.LoadGameScreenViewModel(
                this,
                _dialogService,
                _gameStateRepository,
                _carPartsService,
                _setCurrentGame));

        public bool NavigateToGarage(GameState gameState, bool skipAnimation = false) => SafeNavigate("garage", () =>
            new Screens.Garage.GarageScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _launcher,
                _carPartsService,
                _gameStateRepository,
                _timeService,
                _gameSettingsService,
                _contentService,
                _raceCarDataService,
                _saleService,
                skipAnimation));

        public bool NavigateToCarSelection(GameState gameState) => SafeNavigate("car list", () =>
            new Screens.CarSelection.CarSelectionScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _gameStateRepository,
                _timeService));

        public bool NavigateToDiner(GameState gameState) => SafeNavigate("diner", () =>
            new Screens.Diner.DinerScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _opponentChallengeService,
                _contentService,
                _talkService,
                _timeService,
                _gameStateRepository,
                _raceSetup));

        public bool NavigateToNewspaper(GameState gameState, bool skipAnimation = false) => SafeNavigate("newspaper", () =>
            new Screens.Newspaper.NewspaperScreenViewModel(
                this,
                _dialogService,
                gameState,
                _raceEventService,
                _carFilterService,
                _catalogRepository,
                _eventOpponentService,
                _contentService,
                _timeService,
                _gameStateRepository,
                _raceSetup,
                _saleService,
                skipAnimation));

        public bool NavigateToUsedCarMarket(GameState gameState) => SafeNavigate("used car ads", () =>
            new Screens.UsedCarMarket.UsedCarMarketScreenViewModel(
                this,
                _dialogService,
                gameState,
                _marketService,
                _catalogRepository,
                _profileRepository,
                _gameStateRepository,
                _purchaseService));

        public bool NavigateToUsedParts(GameState gameState) => SafeNavigate("parts pages", () =>
            new Screens.UsedParts.UsedPartsScreenViewModel(
                this,
                _dialogService,
                gameState,
                _carPartsService,
                _partsShopService,
                _gameStateRepository,
                _catalogRepository,
                _timeService));

        public bool NavigateToCarCatalogEditor() => SafeNavigate("car catalog editor", () =>
            new Screens.CarCatalogEditor.CarCatalogEditorScreenViewModel(
                this,
                _dialogService,
                _catalogRepository,
                _profileRepository,
                _profileService,
                _carPartsService));

        public bool NavigateToRaceLoading(GameState gameState, DragRaceLaunchIntent launchIntent) => SafeNavigate("race", () =>
            new Screens.RaceLoading.RaceLoadingScreenViewModel(
                this,
                _dialogService,
                gameState,
                _launcher,
                launchIntent,
                _timeService,
                _gameStateRepository));

        public bool NavigateToDealerMap(GameState gameState) => SafeNavigate("dealer map", () =>
            new Screens.DealerMap.DealerMapScreenViewModel(
                this,
                _dialogService,
                gameState,
                _dealerCatalog,
                _marketService,
                _timeService));

        public bool NavigateToDealerLot(GameState gameState, string dealerId) => SafeNavigate("dealer's lot", () =>
            new Screens.DealerLot.DealerLotScreenViewModel(
                this,
                _dialogService,
                gameState,
                dealerId,
                _dealerCatalog,
                _marketService,
                _catalogRepository,
                _profileRepository,
                _gameStateRepository,
                _purchaseService,
                _carPartsService));

        public bool NavigateToCareer(GameState gameState) => SafeNavigate("career screen", () =>
            new Screens.Career.CareerScreenViewModel(
                this,
                _dialogService,
                _victoryConditionService,
                _milestoneService,
                gameState,
                _gameStateRepository));
    }
}
