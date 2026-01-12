using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Diner
{
    public class DinerScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IAppLogger _logger;

        public RelayCommand GarageCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public DinerScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _logger = AppLoggerFactory.CreateLogger("Diner");

            GarageCommand = new RelayCommand(OnGarage);
        }

        private void OnGarage()
        {
            _logger.Information("Navigating to garage");
            var app = (App)System.Windows.Application.Current;
            var garageViewModel = new Garage.GarageScreenViewModel(
                _navigationService,
                _dialogService,
                _gameState,
                app.CatalogRepository,
                app.Launcher);
            _navigationService.NavigateTo(garageViewModel);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered diner screen");
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited diner screen");
        }
    }
}
