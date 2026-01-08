using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Newspaper
{
    public class NewspaperScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;

        public RelayCommand BackCommand { get; }

        public NewspaperScreenViewModel(NavigationService navigationService, DialogService dialogService, Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;

            BackCommand = new RelayCommand(OnBack);
        }

        private void OnBack()
        {
            var gameViewModel = new Game.GameScreenViewModel(_navigationService, _dialogService, _gameState);
            _navigationService.NavigateTo(gameViewModel);
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
