using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.ViewModels;
using System.Windows;

namespace Street_Rod_AC.Screens.Newspaper
{
    public class NewspaperScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;

        public RelayCommand BackCommand { get; }
        public RelayCommand UsedCarsCommand { get; }
        public RelayCommand UsedPartsCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public NewspaperScreenViewModel(NavigationService navigationService, DialogService dialogService, Models.GameState.GameState gameState)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;

            BackCommand = new RelayCommand(OnBack);
            UsedCarsCommand = new RelayCommand(OnUsedCars);
            UsedPartsCommand = new RelayCommand(OnUsedParts);
        }

        private void OnUsedCars()
        {
            var app = (App)System.Windows.Application.Current;
            var usedCarMarketViewModel = new UsedCarMarket.UsedCarMarketScreenViewModel(
                _navigationService,
                _dialogService,
                _gameState,
                app.MarketService,
                app.CatalogRepository,
                app.ProfileRepository,
                app.GameStateRepository);
            _navigationService.NavigateTo(usedCarMarketViewModel);
        }

        private void OnUsedParts()
        {
            var usedPartsViewModel = new UsedParts.UsedPartsScreenViewModel(
                _navigationService,
                _dialogService,
                _gameState);
            _navigationService.NavigateTo(usedPartsViewModel);
        }

        private void OnBack()
        {
            var app = (App)System.Windows.Application.Current;
            var gameViewModel = new Game.GameScreenViewModel(
                _navigationService,
                _dialogService,
                _gameState,
                app.CatalogRepository,
                app.Launcher);
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
