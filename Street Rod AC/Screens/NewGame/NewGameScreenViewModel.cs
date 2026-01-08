using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.NewGame
{
    public class NewGameScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;

        public RelayCommand StartGameCommand { get; }
        public RelayCommand BackCommand { get; }

        public NewGameScreenViewModel(NavigationService navigationService, DialogService dialogService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;

            StartGameCommand = new RelayCommand(OnStartGame);
            BackCommand = new RelayCommand(OnBack);
        }

        private void OnStartGame()
        {
            var infoDialog = new InformationDialogViewModel(
                _dialogService,
                "Game start functionality is coming soon!",
                "Start Game");

            _dialogService.ShowDialog(infoDialog);
        }

        private void OnBack()
        {
            var mainMenuViewModel = new MainMenu.MainMenuScreenViewModel(_navigationService, _dialogService);
            _navigationService.NavigateTo(mainMenuViewModel);
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
