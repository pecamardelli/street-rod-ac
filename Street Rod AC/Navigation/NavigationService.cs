using System.ComponentModel;
using System.Runtime.CompilerServices;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Settings;
using Street_Rod_AC.Services.Storage;

namespace Street_Rod_AC.Navigation
{
    /// <summary>
    /// Central navigation service with typed factory methods for all screens.
    /// Screens should use these factory methods rather than directly instantiating other screens.
    /// </summary>
    public class NavigationService : INotifyPropertyChanged
    {
        private IScreen _currentScreen;
        private readonly DialogService _dialogService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly ICarProfileRepository _profileRepository;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IOpponentChallengeService _opponentChallengeService;
        private readonly IGameStateRepository _gameStateRepository;
        private readonly IUsedCarMarketService _marketService;
        private readonly GameSettingsService _gameSettingsService;
        private readonly ICarProfileService _profileService;

        public IScreen CurrentScreen
        {
            get => _currentScreen;
            private set
            {
                if (_currentScreen != value)
                {
                    _currentScreen = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public NavigationService(
            DialogService dialogService,
            IContentCatalogRepository catalogRepository,
            ICarProfileRepository profileRepository,
            IAssettoCorsaLauncher launcher,
            IOpponentChallengeService opponentChallengeService,
            IGameStateRepository gameStateRepository,
            IUsedCarMarketService marketService,
            GameSettingsService gameSettingsService,
            ICarProfileService profileService)
        {
            _dialogService = dialogService;
            _catalogRepository = catalogRepository;
            _profileRepository = profileRepository;
            _launcher = launcher;
            _opponentChallengeService = opponentChallengeService;
            _gameStateRepository = gameStateRepository;
            _marketService = marketService;
            _gameSettingsService = gameSettingsService;
            _profileService = profileService;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        // ============================================================
        // TYPED FACTORY METHODS
        // Each method encapsulates the creation of a specific screen
        // ============================================================

        public void NavigateToInit()
        {
            var screen = new Screens.Init.InitScreenViewModel(this, _dialogService);
            NavigateTo(screen);
        }

        public void NavigateToMainMenu()
        {
            var screen = new Screens.MainMenu.MainMenuScreenViewModel(this, _dialogService);
            NavigateTo(screen);
        }

        public void NavigateToSettings()
        {
            var screen = new Screens.Settings.SettingsScreenViewModel(this, _dialogService, _gameSettingsService);
            NavigateTo(screen);
        }

        public void NavigateToNewGame()
        {
            var screen = new Screens.NewGame.NewGameScreenViewModel(this, _dialogService);
            NavigateTo(screen);
        }

        public void NavigateToLoadGame()
        {
            var screen = new Screens.LoadGame.LoadGameScreenViewModel(this, _dialogService);
            NavigateTo(screen);
        }

        public void NavigateToGame(GameState gameState)
        {
            var screen = new Screens.Game.GameScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _launcher);
            NavigateTo(screen);
        }

        public void NavigateToGarage(GameState gameState, bool skipAnimation = false)
        {
            var screen = new Screens.Garage.GarageScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _launcher,
                skipAnimation);
            NavigateTo(screen);
        }

        public void NavigateToCarSelection(GameState gameState)
        {
            var screen = new Screens.CarSelection.CarSelectionScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _gameStateRepository);
            NavigateTo(screen);
        }

        public void NavigateToDiner(GameState gameState)
        {
            var screen = new Screens.Diner.DinerScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _opponentChallengeService,
                _launcher);
            NavigateTo(screen);
        }

        public void NavigateToNewspaper(GameState gameState, bool skipAnimation = false)
        {
            var screen = new Screens.Newspaper.NewspaperScreenViewModel(
                this,
                _dialogService,
                gameState,
                skipAnimation);
            NavigateTo(screen);
        }

        public void NavigateToUsedCarMarket(GameState gameState)
        {
            var screen = new Screens.UsedCarMarket.UsedCarMarketScreenViewModel(
                this,
                _dialogService,
                gameState,
                _marketService,
                _catalogRepository,
                _profileRepository,
                _gameStateRepository);
            NavigateTo(screen);
        }

        public void NavigateToUsedParts(GameState gameState)
        {
            var screen = new Screens.UsedParts.UsedPartsScreenViewModel(
                this,
                _dialogService,
                gameState);
            NavigateTo(screen);
        }

        public void NavigateToCarCatalogEditor()
        {
            var screen = new Screens.CarCatalogEditor.CarCatalogEditorScreenViewModel(
                this,
                _dialogService,
                _catalogRepository,
                _profileRepository,
                _profileService);
            NavigateTo(screen);
        }
    }
}
