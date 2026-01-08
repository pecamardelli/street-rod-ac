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

        public GameScreenViewModel(NavigationService navigationService, DialogService dialogService, Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;

            BackCommand = new RelayCommand(OnBack);
        }

        private void OnBack()
        {
            // Show confirmation dialog
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to go back to the New Game screen? Your current game will remain saved.",
                "Go Back?",
                confirmed =>
                {
                    if (confirmed)
                    {
                        var newGameViewModel = new NewGame.NewGameScreenViewModel(_navigationService, _dialogService);
                        _navigationService.NavigateTo(newGameViewModel);
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
