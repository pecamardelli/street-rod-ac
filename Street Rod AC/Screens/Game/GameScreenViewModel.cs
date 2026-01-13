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

            // Auto-select first car if none selected
            if (_gameState.Player.SelectedCarInstanceId == null && _gameState.Player.Cars.Count > 0)
            {
                _gameState.Player.SelectedCarInstanceId = _gameState.Player.Cars[0].InstanceId;
            }
        }

        private void OnNewspaper()
        {
            _navigationService.NavigateToNewspaper(_gameState);
        }

        private void OnGarage()
        {
            _navigationService.NavigateToGarage(_gameState);
        }

        private Models.GameState.Car? GetSelectedCar()
        {
            if (_gameState.Player.SelectedCarInstanceId == null)
                return null;

            return _gameState.Player.Cars.FirstOrDefault(c =>
                c.InstanceId == _gameState.Player.SelectedCarInstanceId);
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
                        _navigationService.NavigateToMainMenu();
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
            return true;
        }

        private void OnHitTheStreets()
        {
            _navigationService.NavigateToDiner(_gameState);
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
