using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Time;
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
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand ExitCommand { get; }
        public AsyncRelayCommand LaunchShowroomCommand { get; }
        public RelayCommand SelectCarCommand { get; }
        public RelayCommand ShowCalendarCommand { get; }

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

        public bool SkipEnterAnimation { get; }

        // Panel System
        private GaragePanel _activePanel = GaragePanel.CarPreview;
        public GaragePanel ActivePanel
        {
            get => _activePanel;
            set
            {
                _activePanel = value;
                OnPropertyChanged(nameof(ActivePanel));
            }
        }

        // Calendar Properties
        public DateTime CurrentGameDate => _gameState.Date;
        public string CurrentMonthYear => _gameState.Date.ToString("MMMM yyyy");
        public int CurrentDay => _gameState.Date.Day;
        public string CurrentDayOfWeek => _gameState.Date.ToString("dddd");
        public string CurrentTimeDisplay => _gameState.Date.ToString("h:mm tt");

        public List<CalendarDayViewModel> CalendarDays
        {
            get
            {
                var days = new List<CalendarDayViewModel>();
                var date = _gameState.Date;
                var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
                var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

                // Add empty slots for days before the first day of the month
                var startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
                for (int i = 0; i < startDayOfWeek; i++)
                {
                    days.Add(new CalendarDayViewModel { Day = 0, IsCurrentDay = false, IsEmpty = true });
                }

                // Add days of the month
                for (int day = 1; day <= daysInMonth; day++)
                {
                    days.Add(new CalendarDayViewModel
                    {
                        Day = day,
                        IsCurrentDay = day == date.Day,
                        IsEmpty = false
                    });
                }

                return days;
            }
        }

        public GarageScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepo,
            IAssettoCorsaLauncher launcher,
            bool skipAnimation = false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _launcher = launcher;
            _logger = AppLoggerFactory.CreateLogger("Garage");
            SkipEnterAnimation = skipAnimation;

            BackCommand = new RelayCommand(OnBack);
            ExitCommand = new RelayCommand(OnExit);
            LaunchShowroomCommand = new AsyncRelayCommand(OnLaunchShowroom, CanLaunchShowroom);
            SelectCarCommand = new RelayCommand(OnSelectCar);
            ShowCalendarCommand = new RelayCommand(OnShowCalendar);

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

            // Select the car that matches the game state's selected car, or first car by default
            if (Cars.Count > 0)
            {
                if (_gameState.Player.SelectedCarInstanceId != null)
                {
                    var selectedCarVm = Cars.FirstOrDefault(c =>
                        c.CarInstance.InstanceId == _gameState.Player.SelectedCarInstanceId);
                    SelectedCar = selectedCarVm ?? Cars[0];
                }
                else
                {
                    SelectedCar = Cars[0];
                }
            }

            _logger.Information("Loaded {Count} cars into garage view", Cars.Count);
        }

        private bool CanLaunchShowroom()
        {
            return SelectedCar != null && !_launcher.IsExecutionLocked;
        }

        private async Task OnLaunchShowroom()
        {
            if (SelectedCar == null)
                return;

            _logger.Information("Launching showroom for car {CarId} with skin {SkinId}",
                SelectedCar.CarDefinition.Id, SelectedCar.CarInstance.SkinId);

            try
            {
                var intent = new ShowroomLaunchIntent
                {
                    CarId = SelectedCar.CarDefinition.Id,
                    SkinId = SelectedCar.CarInstance.SkinId
                };

                var result = await _launcher.LaunchShowroomAsync(intent);

                if (result.Success)
                {
                    _logger.Information("Showroom launched successfully");

                    // Spend time for viewing the showroom (15 min)
                    await ((App)System.Windows.Application.Current).SpendTimeAsync(GameAction.ViewShowroom);
                }
                else
                {
                    _logger.Error("Showroom launch failed: {Error}", result.ErrorMessage);
                    var errorDialog = new Dialogs.Information.InformationDialogViewModel(
                        _dialogService,
                        $"Failed to launch showroom:\n\n{result.ErrorMessage}",
                        "Launch Error");
                    _dialogService.ShowDialog(errorDialog);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during showroom launch");
                var errorDialog = new Dialogs.Information.InformationDialogViewModel(
                    _dialogService,
                    $"Failed to launch showroom:\n\n{ex.Message}",
                    "Launch Error");
                _dialogService.ShowDialog(errorDialog);
            }
        }

        private void OnSelectCar()
        {
            _logger.Information("Navigating to car selection screen");
            _navigationService.NavigateToCarSelection(_gameState);
        }

        private void OnShowCalendar()
        {
            // Toggle between calendar and car preview
            ActivePanel = ActivePanel == GaragePanel.Calendar
                ? GaragePanel.CarPreview
                : GaragePanel.Calendar;

            _logger.Debug("Active panel changed to {Panel}", ActivePanel);
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to game screen");
            _navigationService.NavigateToGame(_gameState);
        }

        private void OnExit()
        {
            // Show confirmation dialog
            var confirmDialog = new Dialogs.Confirmation.ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to exit? Your current game will remain saved.",
                "Exit Game?",
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
            _logger.Information("Entered garage screen");

            // Refresh calendar properties to reflect current game time
            RefreshCalendarDisplay();

            // Refresh bankroll display in case money changed
            OnPropertyChanged(nameof(BankrollDisplay));

            // Reload player cars in case the collection changed
            LoadPlayerCars();
        }

        /// <summary>
        /// Notifies the UI that all calendar-related properties should be refreshed
        /// </summary>
        private void RefreshCalendarDisplay()
        {
            OnPropertyChanged(nameof(CurrentGameDate));
            OnPropertyChanged(nameof(CurrentMonthYear));
            OnPropertyChanged(nameof(CurrentDay));
            OnPropertyChanged(nameof(CurrentDayOfWeek));
            OnPropertyChanged(nameof(CurrentTimeDisplay));
            OnPropertyChanged(nameof(CalendarDays));
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

    /// <summary>
    /// View model for a single day in the calendar grid
    /// </summary>
    public class CalendarDayViewModel
    {
        public int Day { get; set; }
        public bool IsCurrentDay { get; set; }
        public bool IsEmpty { get; set; }
    }
}
