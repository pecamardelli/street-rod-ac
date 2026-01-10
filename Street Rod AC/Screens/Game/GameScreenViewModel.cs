using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Game
{
    public class GameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;

        public RelayCommand BackCommand { get; }
        public RelayCommand NewspaperCommand { get; }
        public RelayCommand GarageCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public GameScreenViewModel(NavigationService navigationService, DialogService dialogService, Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;

            BackCommand = new RelayCommand(OnBack);
            NewspaperCommand = new RelayCommand(OnNewspaper);
            GarageCommand = new RelayCommand(OnGarage);
        }

        private void OnNewspaper()
        {
            var newspaperViewModel = new Newspaper.NewspaperScreenViewModel(_navigationService, _dialogService, _gameState);
            _navigationService.NavigateTo(newspaperViewModel);
        }

        private void OnGarage()
        {
            var app = (App)System.Windows.Application.Current;
            var garageViewModel = new Garage.GarageScreenViewModel(_navigationService, _dialogService, _gameState, app.CatalogRepository, app.Launcher);
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
