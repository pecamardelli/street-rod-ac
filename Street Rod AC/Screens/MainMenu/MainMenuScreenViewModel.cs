using Street_Rod_AC.Configuration;
using Street_Rod_AC.Controls.Showcase;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Showcase;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.MainMenu
{
    /// <summary>What the main screen shows over its cars: the menu, or one of these</summary>
    public enum MainMenuCard
    {
        None,
        NewGame,
        LoadGame,
        Settings
    }

    /// <summary>
    /// The main screen. Installed cars stand in the showrooms behind it, one at a time; the menu is laid over them, and
    /// New Game, Load Game and Settings open as cards in its place rather than as screens of their own. Only starting
    /// or loading a game leaves.
    /// </summary>
    public class MainMenuScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly IContentCatalogRepository _catalog;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("MainMenu");

        private BaseScreenViewModel? _currentCard;
        private IReadOnlyList<ShowcaseCar> _showcaseCars = [];
        private IReadOnlyList<ShowcaseScene> _showcaseScenes = [];
        private bool _left;
        private bool _nothingToShow;

        /// <summary>A card asked for before the screen was entered</summary>
        private MainMenuCard _pendingCard;

        public RelayCommand NewGameCommand { get; }
        public RelayCommand LoadGameCommand { get; }
        public RelayCommand SettingsCommand { get; }
        public RelayCommand ExitCommand { get; }

        /// <summary>Esc: back to the menu from whichever card is open</summary>
        public RelayCommand CloseCardCommand { get; }

        /// <param name="openCard">A card to open straight away</param>
        public MainMenuScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IContentCatalogRepository catalog,
            MainMenuCard openCard = MainMenuCard.None)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _catalog = catalog;

            NewGameCommand = new RelayCommand(() => Open(MainMenuCard.NewGame));
            LoadGameCommand = new RelayCommand(() => Open(MainMenuCard.LoadGame));
            SettingsCommand = new RelayCommand(() => Open(MainMenuCard.Settings));
            ExitCommand = new RelayCommand(OnExit);
            CloseCardCommand = new RelayCommand(CloseCard);

            _pendingCard = openCard;
        }

        /// <summary>The card open over the cars, or null while the menu shows</summary>
        public BaseScreenViewModel? CurrentCard
        {
            get => _currentCard;
            private set
            {
                if (SetProperty(ref _currentCard, value)) OnPropertyChanged(nameof(IsMenuShown));
            }
        }

        public bool IsMenuShown => _currentCard == null;

        /// <summary>The installed cars of the catalog, for the showroom behind the menu</summary>
        public IReadOnlyList<ShowcaseCar> ShowcaseCars
        {
            get => _showcaseCars;
            private set => SetProperty(ref _showcaseCars, value);
        }

        /// <summary>
        /// The showroom has been looked for and there is nothing for it: no cars or no rooms (no Assetto Corsa folder,
        /// say). The screen shows its old picture then.
        /// </summary>
        public bool NothingToShow
        {
            get => _nothingToShow;
            private set => SetProperty(ref _nothingToShow, value);
        }

        /// <summary>The showrooms they stand in</summary>
        public IReadOnlyList<ShowcaseScene> ShowcaseScenes
        {
            get => _showcaseScenes;
            private set => SetProperty(ref _showcaseScenes, value);
        }

        public override void Enter()
        {
            base.Enter();

            if (_pendingCard != MainMenuCard.None)
            {
                Open(_pendingCard);
                _pendingCard = MainMenuCard.None;
            }

            _ = LoadShowcaseAsync();
        }

        public override void Exit()
        {
            _left = true;
            CloseCard();
            base.Exit();
        }

        /// <summary>
        /// Reads what the showroom behind the menu has to show. Off the UI thread: the catalog and the cars folder are
        /// read from disk. Nothing found leaves the lists empty, and the screen shows its picture.
        /// </summary>
        private async Task LoadShowcaseAsync()
        {
            try
            {
                var settings = AppSettings.Instance;
                var (cars, scenes) = await Task.Run(() =>
                {
                    var installed = InstalledCars.Ids();
                    var definitions = _catalog.GetCarsByStatus(ContentStatus.Active);
                    return (
                        ShowcaseContent.Cars(definitions, installed, settings.CarsPath),
                        ShowcaseContent.LoadScenes(ShowcaseContent.ScenesFile, settings.GaragesPath, settings.ShowroomsPath));
                });

                if (_left) return;

                _logger.Information("Main screen shows {Cars} cars in {Scenes} scenes", cars.Count, scenes.Count);
                ShowcaseScenes = scenes;
                ShowcaseCars = cars;
                NothingToShow = cars.Count == 0 || scenes.Count == 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not find the cars for the main screen; it shows its picture");
                NothingToShow = true;
            }
        }

        private void Open(MainMenuCard card)
        {
            CloseCard();

            BaseScreenViewModel? opened = card switch
            {
                MainMenuCard.NewGame => _navigationService.CreateNewGameCard(CloseCard),
                MainMenuCard.LoadGame => _navigationService.CreateLoadGameCard(CloseCard),
                MainMenuCard.Settings => _navigationService.CreateSettingsCard(CloseCard),
                _ => null
            };

            if (opened == null) return;

            try
            {
                opened.Enter();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the {Card} card", card);
                return;
            }

            CurrentCard = opened;
        }

        private void CloseCard()
        {
            var card = _currentCard;
            if (card == null) return;

            CurrentCard = null;
            try
            {
                card.Exit();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "A main menu card did not close cleanly");
            }
        }

        private void OnExit()
        {
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to exit?",
                "Exit Street Corsa",
                confirmed =>
                {
                    if (confirmed)
                    {
                        System.Windows.Application.Current.Shutdown();
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }
    }
}
