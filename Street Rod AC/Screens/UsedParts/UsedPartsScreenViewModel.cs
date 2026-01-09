using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.UsedParts
{
    /// <summary>
    /// View model for the used parts market screen
    /// TODO: Implement parts market functionality
    /// </summary>
    public class UsedPartsScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public UsedPartsScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _logger = AppLoggerFactory.CreateLogger("UsedParts");

            BackCommand = new RelayCommand(OnBack);
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to newspaper");
            var newspaperViewModel = new Newspaper.NewspaperScreenViewModel(_navigationService, _dialogService, _gameState);
            _navigationService.NavigateTo(newspaperViewModel);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered used parts screen");
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited used parts screen");
        }
    }
}
