using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Street_Rod_AC.Screens.Game
{
    public class GameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IAssettoCorsaLauncher _launcher;

        public RelayCommand BackCommand { get; }
        public RelayCommand ExitCommand { get; }
        public RelayCommand NewspaperCommand { get; }
        public RelayCommand GarageCommand { get; }
        public RelayCommand HitTheStreetsCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public GameScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepository,
            IAssettoCorsaLauncher launcher)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _launcher = launcher;

            BackCommand = new RelayCommand(OnBack);
            ExitCommand = new RelayCommand(OnExit);
            NewspaperCommand = new RelayCommand(OnNewspaper);
            GarageCommand = new RelayCommand(OnGarage);
            HitTheStreetsCommand = new RelayCommand(OnHitTheStreets, CanHitTheStreets);
        }

        private void OnNewspaper()
        {
            var newspaperViewModel = new Newspaper.NewspaperScreenViewModel(_navigationService, _dialogService, _gameState);
            _navigationService.NavigateTo(newspaperViewModel);
        }

        private void OnGarage()
        {
            var app = (App)System.Windows.Application.Current;
            var garageViewModel = new Garage.GarageScreenViewModel(_navigationService, _dialogService, _gameState, _catalogRepository, app.Launcher);
            _navigationService.NavigateTo(garageViewModel);
        }

        private void OnBack()
        {
            // Show confirmation dialog
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to go back to the Main Menu? Your current game will remain saved.",
                "Go Back?",
                confirmed =>
                {
                    if (confirmed)
                    {
                        var mainMenuViewModel = new MainMenu.MainMenuScreenViewModel(_navigationService, _dialogService);
                        _navigationService.NavigateTo(mainMenuViewModel);
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private void OnExit()
        {
            // Show confirmation dialog
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to exit? Your current game will remain saved.",
                "Exit Game?",
                confirmed =>
                {
                    if (confirmed)
                    {
                        System.Windows.Application.Current.Shutdown();
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private bool CanHitTheStreets()
        {
            // Player must have at least one car to race
            return _gameState.Player.Cars.Count > 0 && !_launcher.IsExecutionLocked;
        }

        private async void OnHitTheStreets()
        {
            try
            {
                // Check if player has a car
                if (_gameState.Player.Cars.Count == 0)
                {
                    var errorDialog = new InformationDialogViewModel(
                        _dialogService,
                        "You need to own a car before you can race!\n\nVisit the newspaper to find cars for sale.",
                        "No Car");
                    _dialogService.ShowDialog(errorDialog);
                    return;
                }

                // Get player's first car (we can add car selection later)
                var playerCar = _gameState.Player.Cars[0];

                // Select first available opponent from used car market
                var opponentCar = _gameState.UsedCarMarket.FirstOrDefault(c => !c.IsSold);

                if (opponentCar == null)
                {
                    var errorDialog = new InformationDialogViewModel(
                        _dialogService,
                        "No opponents available! The used car market needs to be populated.",
                        "No Opponents");
                    _dialogService.ShowDialog(errorDialog);
                    return;
                }

                // Hardcoded opponent name for now
                var opponentName = "Street Racer";

                // Get car definitions from catalog
                var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
                var opponentCarDef = _catalogRepository.GetCar(opponentCar.CarDefinitionId);

                if (playerCarDef == null || opponentCarDef == null)
                {
                    var errorDialog = new InformationDialogViewModel(
                        _dialogService,
                        "Failed to load car definitions from catalog.",
                        "Error");
                    _dialogService.ShowDialog(errorDialog);
                    return;
                }

                // Create drag race launch intent
                var dragRaceIntent = new DragRaceLaunchIntent
                {
                    PlayerCarId = playerCarDef.Id,
                    PlayerSkin = playerCar.SkinId,
                    PlayerName = _gameState.Player.Name,
                    OpponentCarId = opponentCarDef.Id,
                    OpponentSkin = opponentCar.SkinId,
                    OpponentName = opponentName
                };

                var confirmMessage = $"Ready to race!\n\n" +
                    $"You: {playerCarDef?.Brand} {playerCarDef?.Name}\n" +
                    $"Opponent: {opponentName}\n" +
                    $"    {opponentCarDef?.Brand} {opponentCarDef?.Name}\n\n" +
                    $"Track: Drag Strip (1/4 mile)\n\n" +
                    $"Launch Assetto Corsa?";

                var confirmDialog = new ConfirmationDialogViewModel(
                    _dialogService,
                    confirmMessage,
                    "Drag Race",
                    async confirmed =>
                    {
                        if (confirmed)
                        {
                            // Launch race through the proper pipeline
                            var result = await _launcher.LaunchRaceAsync(dragRaceIntent);

                            if (result.Success)
                            {
                                var logger = AppLoggerFactory.CreateLogger("GameScreenViewModel");
                                logger.Information("Drag race completed successfully");
                            }
                            else
                            {
                                var errorDialog = new InformationDialogViewModel(
                                    _dialogService,
                                    $"Failed to launch drag race:\n\n{result.ErrorMessage}",
                                    "Launch Error");
                                _dialogService.ShowDialog(errorDialog);
                            }
                        }
                    });

                _dialogService.ShowDialog(confirmDialog);
            }
            catch (System.Exception ex)
            {
                var errorDialog = new InformationDialogViewModel(
                    _dialogService,
                    $"Failed to create drag race:\n\n{ex.Message}",
                    "Error");
                _dialogService.ShowDialog(errorDialog);
            }
        }

        public override void Enter()
        {
            base.Enter();
        }

        public override void Exit()
        {
            base.Exit();
        }
    }
}
