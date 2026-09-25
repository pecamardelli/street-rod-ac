using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;

namespace Street_Rod_AC.Screens.CarSelection
{
    public class CarSelectionScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IGameTimeService _timeService;
        private readonly IAppLogger _logger;

        // A pick under way: the switch is paid for once, and the screen is not left while the time is spent
        private bool _selecting;

        public RelayCommand BackCommand { get; }
        public RelayCommand<CarSelectionItemViewModel> SelectCarCommand { get; }

        private ObservableCollection<CarSelectionItemViewModel> _cars;
        public ObservableCollection<CarSelectionItemViewModel> Cars
        {
            get => _cars;
            set
            {
                _cars = value;
                OnPropertyChanged(nameof(Cars));
            }
        }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public CarSelectionScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepo,
            IGameStateRepository gameStateRepo,
            IGameTimeService timeService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _gameStateRepo = gameStateRepo;
            _timeService = timeService;
            _logger = AppLoggerFactory.CreateLogger("CarSelection");

            BackCommand = new RelayCommand(OnBack, () => !_selecting);
            SelectCarCommand = new RelayCommand<CarSelectionItemViewModel>(OnSelectCar, _ => !_selecting);

            // The cars are loaded in Enter
            _cars = new ObservableCollection<CarSelectionItemViewModel>();
        }

        private void LoadPlayerCars()
        {
            Cars.Clear();

            if (_gameState.Player.Cars == null || _gameState.Player.Cars.Count == 0)
            {
                _logger.Information("Player has no cars");
                return;
            }

            _logger.Information("Loading {Count} player cars for selection", _gameState.Player.Cars.Count);

            foreach (var entry in PlayerCars.Load(_gameState, _catalogRepo, _logger))
            {
                Cars.Add(new CarSelectionItemViewModel
                {
                    CarInstance = entry.Car,
                    CarDefinition = entry.Definition,
                    PreviewImagePath = entry.PreviewImagePath,
                    IsSelected = entry.Car.InstanceId == _gameState.Player.SelectedCarInstanceId
                });
            }

            _logger.Information("Loaded {Count} cars for selection", Cars.Count);
        }

        private async void OnSelectCar(CarSelectionItemViewModel? selectedCar)
        {
            if (selectedCar == null || _selecting)
                return;

            _selecting = true;
            RelayCommand.RaiseCanExecuteChanged();
            try
            {
                // Only spend time if actually switching to a different car
                var isActuallySwitching = _gameState.Player.SelectedCarInstanceId != selectedCar.CarInstance.InstanceId;

                _logger.Information("Selected car {CarId} (Instance: {InstanceId})",
                    selectedCar.CarDefinition.Id, selectedCar.CarInstance.InstanceId);

                // Update game state
                _gameState.Player.SelectedCarInstanceId = selectedCar.CarInstance.InstanceId;

                // Spend time for switching cars (15 min). Waited for before saving: late in the day that is the
                // next morning, and what the new day brings belongs in the save.
                if (isActuallySwitching)
                {
                    try
                    {
                        await _timeService.SpendTimeAsync(_gameState, GameAction.SwitchCar);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Could not spend the time for switching cars");
                    }
                }

                // Save game state to persist the selection
                try
                {
                    if (!string.IsNullOrEmpty(_gameState.SaveName))
                    {
                        _gameStateRepo.Save(_gameState, _gameState.SaveName);
                        _logger.Information("Game state saved after car selection");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save game state after car selection");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not switch cars");
            }
            finally
            {
                _selecting = false;
                RelayCommand.RaiseCanExecuteChanged();
            }

            // Navigate back to garage screen; the navigation service catches and reports a garage that will not open
            OnBack();
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to garage screen");
            _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered car selection screen");

            LoadPlayerCars();
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited car selection screen");
        }
    }

    /// <summary>
    /// View model for a car item in the selection list
    /// </summary>
    public class CarSelectionItemViewModel
    {
        public Car CarInstance { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public string PreviewImagePath { get; set; } = string.Empty;
        public bool IsSelected { get; set; }

        public string DisplayName => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public string YearDisplay => $"({CarDefinition.Year ?? 0})";
        public string ConditionDisplay => $"Condition: {Shared.ConditionDisplay.Of(CarInstance).Percent}";
        public string MileageDisplay => $"Mileage: {CarInstance.OdometerKM:N0} km";
        public bool HasPreviewImage => !string.IsNullOrEmpty(PreviewImagePath);
    }
}
