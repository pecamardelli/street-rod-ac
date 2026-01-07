using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;
using Street_Rod_AC.Views;

namespace Street_Rod_AC.Screens.MainMenu
{
    public class MainMenuScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;

        public RelayCommand NewGameCommand { get; }
        public RelayCommand LoadGameCommand { get; }
        public RelayCommand ExitCommand { get; }

        public MainMenuScreenViewModel(NavigationService navigationService)
        {
            _navigationService = navigationService;

            NewGameCommand = new RelayCommand(OnNewGame);
            LoadGameCommand = new RelayCommand(OnLoadGame);
            ExitCommand = new RelayCommand(OnExit);
        }

        private void OnNewGame()
        {
            System.Windows.MessageBox.Show("New Game - Coming Soon!", "Street Rod AC", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void OnLoadGame()
        {
            System.Windows.MessageBox.Show("Load Game - Coming Soon!", "Street Rod AC", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void OnExit()
        {
            var dialog = new QuitConfirmationDialog();
            var result = dialog.ShowDialog();

            if (result == true)
            {
                System.Windows.Application.Current.Shutdown();
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
