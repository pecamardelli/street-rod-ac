using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace Street_Rod_AC.Screens.CarSelection
{
    public class CarSelectionScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IAppLogger _logger;

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
            IGameStateRepository gameStateRepo)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _gameStateRepo = gameStateRepo;
            _logger = AppLoggerFactory.CreateLogger("CarSelection");

            BackCommand = new RelayCommand(OnBack);
            SelectCarCommand = new RelayCommand<CarSelectionItemViewModel>(OnSelectCar);

            _cars = new ObservableCollection<CarSelectionItemViewModel>();

            LoadPlayerCars();
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

            foreach (var car in _gameState.Player.Cars)
            {
                var carDef = _catalogRepo.GetCar(car.DefinitionId);
                if (carDef == null)
                {
                    _logger.Warning("Car definition not found for car instance {InstanceId}, definition {DefinitionId}",
                        car.InstanceId, car.DefinitionId);
                    continue;
                }

                // Get preview image path for the specific skin
                var skinId = !string.IsNullOrEmpty(car.SkinId) ? car.SkinId : "default";
                var previewPath = Path.Combine(AppSettings.Instance.CarsPath, carDef.Id, "skins", skinId, "preview.jpg");

                if (!File.Exists(previewPath))
                {
                    // Fallback to generic car preview if skin preview doesn't exist
                    previewPath = Path.Combine(AppSettings.Instance.CarsPath, carDef.Id, "preview.jpg");
                    if (!File.Exists(previewPath))
                    {
                        // Final fallback to ui folder preview
                        previewPath = Path.Combine(AppSettings.Instance.CarsPath, carDef.Id, "ui", "preview.jpg");
                    }
                }

                var itemVm = new CarSelectionItemViewModel
                {
                    CarInstance = car,
                    CarDefinition = carDef,
                    PreviewImagePath = File.Exists(previewPath) ? previewPath : string.Empty,
                    IsSelected = car.InstanceId == _gameState.Player.SelectedCarInstanceId
                };

                Cars.Add(itemVm);
            }

            _logger.Information("Loaded {Count} cars for selection", Cars.Count);
        }

        private void OnSelectCar(CarSelectionItemViewModel? selectedCar)
        {
            if (selectedCar == null)
                return;

            _logger.Information("Selected car {CarId} (Instance: {InstanceId})",
                selectedCar.CarDefinition.Id, selectedCar.CarInstance.InstanceId);

            // Update game state
            _gameState.Player.SelectedCarInstanceId = selectedCar.CarInstance.InstanceId;

            // Save game state to persist the selection
            try
            {
                _gameStateRepo.Save(_gameState, _gameState.SaveName);
                _logger.Information("Game state saved after car selection");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save game state after car selection");
            }

            // Navigate back to garage screen
            OnBack();
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to garage screen");
            var app = (App)System.Windows.Application.Current;
            var garageViewModel = new Garage.GarageScreenViewModel(
                _navigationService,
                _dialogService,
                _gameState,
                _catalogRepo,
                app.Launcher);
            _navigationService.NavigateTo(garageViewModel);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered car selection screen");
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
        public string ConditionDisplay => $"Condition: {(int)(CarInstance.EngineHealth * 100)}%";
        public string MileageDisplay => $"Mileage: {CarInstance.OdometerKM:N0} km";
        public bool HasPreviewImage => !string.IsNullOrEmpty(PreviewImagePath);
    }
}
