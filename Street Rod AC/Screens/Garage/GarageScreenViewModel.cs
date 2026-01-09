using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace Street_Rod_AC.Screens.Garage
{
    public class GarageScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }

        private ObservableCollection<CarDisplayViewModel> _cars;
        public ObservableCollection<CarDisplayViewModel> Cars
        {
            get => _cars;
            set
            {
                _cars = value;
                OnPropertyChanged(nameof(Cars));
            }
        }

        private CarDisplayViewModel? _selectedCar;
        public CarDisplayViewModel? SelectedCar
        {
            get => _selectedCar;
            set
            {
                _selectedCar = value;
                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(SelectedCarDisplay));
                OnPropertyChanged(nameof(HasCars));
            }
        }

        public string SelectedCarDisplay
        {
            get
            {
                if (SelectedCar == null)
                    return "No car selected";

                return $"{SelectedCar.CarDefinition.Brand} {SelectedCar.CarDefinition.Name} ({SelectedCar.CarDefinition.Year ?? 0})";
            }
        }

        public bool HasCars => Cars.Count > 0;

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public GarageScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepo)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _logger = AppLoggerFactory.CreateLogger("Garage");

            BackCommand = new RelayCommand(OnBack);

            _cars = new ObservableCollection<CarDisplayViewModel>();

            LoadPlayerCars();
        }

        private void LoadPlayerCars()
        {
            Cars.Clear();

            if (_gameState.Player.Cars == null || _gameState.Player.Cars.Count == 0)
            {
                _logger.Information("Player has no cars in garage");
                return;
            }

            _logger.Information("Loading {Count} player cars", _gameState.Player.Cars.Count);

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

                var displayVm = new CarDisplayViewModel
                {
                    CarInstance = car,
                    CarDefinition = carDef,
                    PreviewImagePath = File.Exists(previewPath) ? previewPath : string.Empty
                };

                Cars.Add(displayVm);
            }

            // Select first car by default
            if (Cars.Count > 0)
            {
                SelectedCar = Cars[0];
            }

            _logger.Information("Loaded {Count} cars into garage view", Cars.Count);
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to game screen");
            var gameViewModel = new Game.GameScreenViewModel(_navigationService, _dialogService, _gameState);
            _navigationService.NavigateTo(gameViewModel);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered garage screen");
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited garage screen");
        }
    }

    /// <summary>
    /// View model for displaying a car in the garage
    /// </summary>
    public class CarDisplayViewModel
    {
        public Car CarInstance { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public string PreviewImagePath { get; set; } = string.Empty;

        public string DisplayName => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public string YearDisplay => CarDefinition.Year?.ToString() ?? "Unknown";
        public string ConditionDisplay => $"{(int)(CarInstance.EngineHealth * 100)}%";
        public string MileageDisplay => $"{CarInstance.OdometerKM:N0} km";
        public bool HasPreviewImage => !string.IsNullOrEmpty(PreviewImagePath);
    }
}
