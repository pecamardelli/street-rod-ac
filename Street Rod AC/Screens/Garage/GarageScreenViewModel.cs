using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace Street_Rod_AC.Screens.Garage
{
    public class GarageScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand ExitCommand { get; }
        public AsyncRelayCommand LaunchShowroomCommand { get; }
        public AsyncRelayCommand FreeRunCommand { get; }

        /// <summary>Where a free run can go: every track of the install, one entry per layout</summary>
        public ObservableCollection<FreeRunTrackViewModel> FreeRunTracks { get; } = new();

        private FreeRunTrackViewModel? _freeRunTrack;

        public FreeRunTrackViewModel? FreeRunTrack
        {
            get => _freeRunTrack;
            set
            {
                if (!SetProperty(ref _freeRunTrack, value) || value == null) return;

                // Remembered for next time, across saves
                var settings = ((App)System.Windows.Application.Current).GameSettingsService;
                settings.Current.FreeRunTrack = value.Key;
                settings.Save();
                FreeRunCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasFreeRunTracks => FreeRunTracks.Count > 0;
        public RelayCommand SelectCarCommand { get; }
        public RelayCommand ShowCalendarCommand { get; }
        public RelayCommand NewspaperCommand { get; }
        public RelayCommand HitTheStreetsCommand { get; }
        public RelayCommand CareerCommand { get; }

        /// <summary>The parts view of the selected car: its engine, the part clicked on, the shelf of loose parts</summary>
        public PartsWorkbenchViewModel Workbench { get; }

        /// <summary>
        /// Set by the view: plays the screen's fade-out and completes when it has finished.
        /// Awaited before navigating away so the garage doesn't just pop off screen.
        /// </summary>
        public Func<Task>? ExitTransition { get; set; }

        private bool _isLeaving;

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
                var carChanged = !ReferenceEquals(_selectedCar?.CarInstance, value?.CarInstance);
                _selectedCar = value;
                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(SelectedCarDisplay));
                OnPropertyChanged(nameof(HasCars));
                if (carChanged) Workbench.SetCar(value?.CarInstance);
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

        /// <summary>Showroom model used as the environment of the 3D viewport</summary>
        public string GarageShowroomKn5 => AppSettings.Instance.GarageShowroomKn5;

        public bool SkipEnterAnimation { get; }

        /// <summary>The tiles that lead out of the garage make room while the car is being worked on</summary>
        public bool ShowNavigation => !Workbench.IsOpen;

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
        public BitmapImage CalendarImage1 => GetRandomCalendarImage(0);
        public BitmapImage CalendarImage2 => GetRandomCalendarImage(1);
        public BitmapImage CalendarImage3 => GetRandomCalendarImage(2);

        private BitmapImage GetRandomCalendarImage(int index)
        {
            // Seed random with year and month so images are consistent for each month but different between months
            var seed = _gameState.Date.Year * 100 + _gameState.Date.Month + index * 17;
            var random = new Random(seed);
            var imageNumber = random.Next(1, 13); // 1-12
            var uri = new Uri($"pack://application:,,,/Assets/Images/Misc/Calendar/calendar{imageNumber:D2}.png", UriKind.Absolute);
            return new BitmapImage(uri);
        }

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
            ICarPartsService partsService,
            IGameStateRepository gameStateRepo,
            bool skipAnimation = false)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _launcher = launcher;
            _gameStateRepo = gameStateRepo;
            _logger = AppLoggerFactory.CreateLogger("Garage");
            SkipEnterAnimation = skipAnimation;

            BackCommand = new RelayCommand(OnBack);
            ExitCommand = new RelayCommand(OnExit);
            LaunchShowroomCommand = new AsyncRelayCommand(OnLaunchShowroom, CanLaunchShowroom);
            FreeRunCommand = new AsyncRelayCommand(OnFreeRun, () => SelectedCar != null && FreeRunTrack != null && !_launcher.IsExecutionLocked);
            LoadFreeRunTracks();
            SelectCarCommand = new RelayCommand(OnSelectCar);
            ShowCalendarCommand = new RelayCommand(OnShowCalendar);
            NewspaperCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToNewspaper(_gameState)));
            HitTheStreetsCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToDiner(_gameState)));
            CareerCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToCareer(_gameState)));

            // The garage is the game's home screen: make sure a car is selected if the player owns any
            if (_gameState.Player.SelectedCarInstanceId == null && _gameState.Player.Cars?.Count > 0)
            {
                _gameState.Player.SelectedCarInstanceId = _gameState.Player.Cars[0].InstanceId;
            }

            _cars = new ObservableCollection<CarDisplayViewModel>();

            Workbench = new PartsWorkbenchViewModel(
                partsService,
                _gameState,
                save: () =>
                {
                    if (!string.IsNullOrEmpty(_gameState.SaveName)) gameStateRepo.Save(_gameState, _gameState.SaveName);
                },
                spendMinutes: minutes => ((App)System.Windows.Application.Current).SpendTimeAsync(minutes));
            Workbench.StateChanged += () =>
            {
                RefreshCalendarDisplay();
                OnPropertyChanged(nameof(BankrollDisplay));
            };
            Workbench.SaveFailed += _ => _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                _dialogService,
                "The work on the car is done, but the game could not be saved. It is saved again the next time something changes.",
                "Save Warning"));
            Workbench.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PartsWorkbenchViewModel.IsOpen)) OnPropertyChanged(nameof(ShowNavigation));
            };

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

        private void LoadFreeRunTracks()
        {
            try
            {
                var app = (App)System.Windows.Application.Current;
                foreach (var track in app.ContentService.GetTracks().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (track.Configurations.Count == 0) FreeRunTracks.Add(new FreeRunTrackViewModel(track.TrackId, null, track.Name));
                    foreach (var configuration in track.Configurations)
                        FreeRunTracks.Add(new FreeRunTrackViewModel(track.TrackId, configuration.FolderName, $"{track.Name} - {configuration.Name}"));
                }

                var remembered = app.GameSettingsService.Current.FreeRunTrack;
                _freeRunTrack = FreeRunTracks.FirstOrDefault(t => t.Key == remembered) ?? FreeRunTracks.FirstOrDefault(t => !t.Key.Contains("drag", StringComparison.OrdinalIgnoreCase)) ?? FreeRunTracks.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the tracks for a free run");
            }
        }

        /// <summary>
        /// The selected car out on its own on the chosen track, on what its parts make of it: a practice
        /// session the player ends when they have had enough. No opponent, no stake, no results; an hour goes by.
        /// </summary>
        private async Task OnFreeRun()
        {
            if (SelectedCar == null || FreeRunTrack is not { } track) return;

            var car = SelectedCar.CarInstance;
            var app = (App)System.Windows.Application.Current;
            _logger.Information("Free run: {Car} on {Track}", car.DefinitionId, track.Key);

            try
            {
                // The car races on its parts; one that will not go stays in
                Services.Race.RaceCarData? data = null;
                if (app.CarPartsService.IsAvailable)
                {
                    if (await app.CarPartsService.EnsurePartsAsync(car) && !string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                    data = await Task.Run(() => app.RaceCarDataService.Prepare(car));
                }

                if (data is { CanDrive: false })
                {
                    _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                        _dialogService,
                        $"The car is not going anywhere: {data.Problem}.",
                        "Car Won't Run"));
                    return;
                }

                var intent = new FreeRunLaunchIntent
                {
                    CarId = SelectedCar.CarDefinition.Id,
                    SkinId = car.SkinId,
                    PlayerName = _gameState.Player.Name,
                    TrackId = track.TrackId,
                    TrackConfig = track.Configuration
                };
                if (data != null) intent.CarData.Add(data);

                FreeRunCommand.RaiseCanExecuteChanged();
                var result = await _launcher.LaunchRaceAsync(intent);
                if (result.Success)
                {
                    await app.SpendTimeAsync(GameAction.FreeRun);
                }
                else
                {
                    _logger.Error("Free run failed: {Error}", result.ErrorMessage);
                    _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                        _dialogService, $"Could not start the free run:\n\n{result.ErrorMessage}", "Launch Error"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during the free run");
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    _dialogService, $"Could not start the free run:\n\n{ex.Message}", "Launch Error"));
            }
            finally
            {
                FreeRunCommand.RaiseCanExecuteChanged();
            }
        }

        private void OnSelectCar()
        {
            // No fade here: garage <-> car list is an instant swap in both directions
            _logger.Information("Navigating to car selection screen");
            _navigationService.NavigateToCarSelection(_gameState);
        }

        /// <summary>
        /// Plays the exit transition (if the view provided one), then navigates
        /// </summary>
        private async void LeaveTo(Action navigate)
        {
            if (_isLeaving) return;
            _isLeaving = true;

            try
            {
                if (ExitTransition != null)
                {
                    await ExitTransition();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Exit transition failed, navigating anyway: {Error}", ex.Message);
            }

            navigate();
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
            // Show confirmation dialog
            var confirmDialog = new Dialogs.Confirmation.ConfirmationDialogViewModel(
                _dialogService,
                "Are you sure you want to go back to the Main Menu? Your current game will remain saved.",
                "Go Back?",
                confirmed =>
                {
                    if (confirmed)
                    {
                        _logger.Information("Navigating back to main menu");
                        LeaveTo(() => _navigationService.NavigateToMainMenu());
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
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
            OnPropertyChanged(nameof(CalendarImage1));
            OnPropertyChanged(nameof(CalendarImage2));
            OnPropertyChanged(nameof(CalendarImage3));
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

        /// <summary>AC car folder, used by the 3D viewport</summary>
        public string CarDirectory => Path.Combine(AppSettings.Instance.CarsPath, CarDefinition.Id);
        public string SkinId => !string.IsNullOrEmpty(CarInstance.SkinId) ? CarInstance.SkinId : "default";

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

    /// <summary>A track, or one layout of it, a free run can go to</summary>
    public sealed class FreeRunTrackViewModel
    {
        public FreeRunTrackViewModel(string trackId, string? configuration, string name)
        {
            TrackId = trackId;
            Configuration = string.IsNullOrEmpty(configuration) ? null : configuration;
            Name = name;
        }

        public string TrackId { get; }
        public string? Configuration { get; }
        public string Name { get; }

        /// <summary>"track" or "track/configuration", as the settings remember it</summary>
        public string Key => Configuration == null ? TrackId : $"{TrackId}/{Configuration}";

        public override string ToString() => Name;
    }
}
