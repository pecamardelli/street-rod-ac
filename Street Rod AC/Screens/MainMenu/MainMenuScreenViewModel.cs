using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.MainMenu
{
    public class MainMenuScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;

        public RelayCommand NewGameCommand { get; }
        public RelayCommand LoadGameCommand { get; }
        public RelayCommand ExitCommand { get; }

        public MainMenuScreenViewModel(NavigationService navigationService, DialogService dialogService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;

            NewGameCommand = new RelayCommand(OnNewGame);
            LoadGameCommand = new RelayCommand(OnLoadGame);
            ExitCommand = new RelayCommand(OnExit);
        }

        private void OnNewGame()
        {
            var newGameScreen = new NewGame.NewGameScreenViewModel(_navigationService, _dialogService);
            _navigationService.NavigateTo(newGameScreen);
        }

        private void OnLoadGame()
        {
            var infoDialog = new InformationDialogViewModel(
                _dialogService,
                "Load Game functionality is coming soon!",
                "Load Game");

            _dialogService.ShowDialog(infoDialog);
        }

        private void OnExit()
        {
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to exit?",
                "Exit Street Rod AC",
                confirmed =>
                {
                    if (confirmed)
                    {
                        System.Windows.Application.Current.Shutdown();
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
