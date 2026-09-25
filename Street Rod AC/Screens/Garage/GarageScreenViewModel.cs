using Street_Rod_AC.Configuration;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Police;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Settings;
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
        private readonly ICarPartsService _partsService;
        private readonly IGameTimeService _timeService;
        private readonly GameSettingsService _settingsService;
        private readonly ICarSaleService _saleService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly RaceCarDataService _raceCarDataService;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand ExitCommand { get; }
        public AsyncRelayCommand LaunchShowroomCommand { get; }
        public AsyncRelayCommand FreeRunCommand { get; }
        public RelayCommand RepairsCommand { get; }
        public RelayCommand SellCommand { get; }

        /// <summary>Pays the impound and brings the selected car home, once its days there are up</summary>
        public RelayCommand CollectCommand { get; }

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
                _settingsService.Current.FreeRunTrack = value.Key;
                _settingsService.Save();
                FreeRunCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasFreeRunTracks => FreeRunTracks.Count > 0;
        public RelayCommand SelectCarCommand { get; }
        public RelayCommand ShowCalendarCommand { get; }

        /// <summary>Calls it a day: the rest of today goes by, and the game picks up tomorrow morning</summary>
        public RelayCommand EndDayCommand { get; }
        public RelayCommand NewspaperCommand { get; }
        public RelayCommand CarDealersCommand { get; }
        public RelayCommand HitTheStreetsCommand { get; }
        public RelayCommand CareerCommand { get; }

        /// <summary>The parts view of the selected car: its engine, the part clicked on, the shelf of loose parts</summary>
        public PartsWorkbenchViewModel Workbench { get; }

        /// <summary>
        /// Set by the view: plays the screen's fade-out and completes when it has finished.
        /// Awaited before navigating away so the garage doesn't just pop off screen.
        /// </summary>
        public Func<Task>? ExitTransition { get; set; }

        /// <summary>
        /// Set by the view: brings the screen back after the exit transition played but the navigation failed,
        /// so the player is not left looking at a faded-out garage
        /// </summary>
        public Action? ExitCancelled { get; set; }

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
                OnPropertyChanged(nameof(SellButtonText));
                RaiseImpoundChanged();
                if (carChanged)
                {
                    // A car in the impound isn't here to work on
                    Workbench.SetCar(value?.CarInstance is { IsImpounded: false } car ? car : null);
                    RefreshEngine();
                }
            }
        }

        private Audio.EngineSpec? _selectedEngine;
        private int _engineVersion;

        /// <summary>The selected car's engine, to start and rev where it stands; null without a car or an engine</summary>
        public Audio.EngineSpec? SelectedEngine
        {
            get => _selectedEngine;
            private set
            {
                _selectedEngine = value;
                OnPropertyChanged(nameof(SelectedEngine));
            }
        }

        /// <summary>Works the engine out again: the car changed, or what is in it</summary>
        private async void RefreshEngine()
        {
            // Nothing may escape an async void
            try
            {
                var version = ++_engineVersion;
                var car = SelectedCar;
                var spec = car == null ? null : await Services.Parts.EngineSpecs.ForAsync(_partsService, car.CarInstance, car.DisplayName);
                if (version == _engineVersion) SelectedEngine = spec;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not work out the selected car's engine");
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

        /// <summary>The calendar pictures once decoded, by number, for the whole session: the same twelve come round every year</summary>
        private static readonly Dictionary<int, BitmapImage> CalendarImages = new();

        /// <summary>
        /// Height the pictures are decoded at. They are 1536x1024 PNGs shown 120 px high; decoded at twice that
        /// they stay sharp on a high-DPI screen at a small fraction of the memory and the decode time.
        /// </summary>
        private const int CalendarDecodeHeight = 240;

        /// <summary>The month whose pictures the view was last told about, so they are raised only when it turns</summary>
        private int _shownCalendarMonth = -1;

        /// <summary>The day the calendar grid was last raised for</summary>
        private DateTime _shownCalendarDay = DateTime.MinValue;

        private BitmapImage GetRandomCalendarImage(int index)
        {
            // Seed random with year and month so images are consistent for each month but different between months
            var seed = _gameState.Date.Year * 100 + _gameState.Date.Month + index * 17;
            var random = new Random(seed);
            var imageNumber = random.Next(1, 13); // 1-12
            return CalendarImage(imageNumber);
        }

        /// <summary>One calendar picture, decoded small and frozen once, then shared</summary>
        public static BitmapImage CalendarImage(int imageNumber)
        {
            if (CalendarImages.TryGetValue(imageNumber, out var cached)) return cached;

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Assets/Images/Misc/Calendar/calendar{imageNumber:D2}.png", UriKind.Absolute);
            image.DecodePixelHeight = CalendarDecodeHeight;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            CalendarImages[imageNumber] = image;
            return image;
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
            IGameTimeService timeService,
            GameSettingsService settingsService,
            IAssettoCorsaContentService contentService,
            RaceCarDataService raceCarDataService,
            ICarSaleService saleService,
            bool skipAnimation = false)
        {
            _saleService = saleService;
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _launcher = launcher;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _timeService = timeService;
            _settingsService = settingsService;
            _contentService = contentService;
            _raceCarDataService = raceCarDataService;
            _logger = AppLoggerFactory.CreateLogger("Garage");
            SkipEnterAnimation = skipAnimation;

            BackCommand = new RelayCommand(OnBack);
            ExitCommand = new RelayCommand(OnExit);
            LaunchShowroomCommand = new AsyncRelayCommand(OnLaunchShowroom, CanLaunchShowroom);
            FreeRunCommand = new AsyncRelayCommand(OnFreeRun, () => SelectedCarHere && FreeRunTrack != null && !_launcher.IsExecutionLocked);
            LoadFreeRunTracks();
            RepairsCommand = new RelayCommand(OnRepairs, () => SelectedCarHere);
            SellCommand = new RelayCommand(OnSell, () => SelectedCarHere && !_launcher.IsExecutionLocked);
            CollectCommand = new RelayCommand(OnCollect, CanCollect);
            SelectCarCommand = new RelayCommand(OnSelectCar);
            ShowCalendarCommand = new RelayCommand(OnShowCalendar);
            EndDayCommand = new RelayCommand(OnEndDay, () => !_endingDay && !_launcher.IsExecutionLocked);
            NewspaperCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToNewspaper(_gameState)));
            CarDealersCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToDealerMap(_gameState)));
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
                spendMinutes: minutes => _timeService.SpendTimeAsync(_gameState, minutes));
            // A new engine tree is a new engine to start: parts went on or came off
            Workbench.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PartsWorkbenchViewModel.Engine)) RefreshEngine();
            };
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

            // The cars are loaded in Enter, once: the screen is always entered right after it is made
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

            foreach (var entry in PlayerCars.Load(_gameState, _catalogRepo, _logger))
            {
                Cars.Add(new CarDisplayViewModel
                {
                    CarInstance = entry.Car,
                    CarDefinition = entry.Definition,
                    PreviewImagePath = entry.PreviewImagePath
                });
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
                    await _timeService.SpendTimeAsync(_gameState, GameAction.ViewShowroom);
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
                foreach (var track in _contentService.GetTracks().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (track.Configurations.Count == 0) FreeRunTracks.Add(new FreeRunTrackViewModel(track.TrackId, null, track.Name));
                    foreach (var configuration in track.Configurations)
                        FreeRunTracks.Add(new FreeRunTrackViewModel(track.TrackId, configuration.FolderName, $"{track.Name} - {configuration.Name}"));
                }

                var remembered = _settingsService.Current.FreeRunTrack;
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
            // Read once: the selection can change while the parts are checked and AC runs
            if (SelectedCar is not { } selected || FreeRunTrack is not { } track) return;

            var car = selected.CarInstance;
            _logger.Information("Free run: {Car} on {Track}", car.DefinitionId, track.Key);

            try
            {
                // The car races on its parts; one that will not go stays in
                Services.Race.RaceCarData? data = null;
                if (_partsService.IsAvailable)
                {
                    if (await _partsService.EnsurePartsAsync(car) && !string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);

                    // Blown, wrecked or totaled: it goes nowhere until it is repaired
                    var damage = Parts.Cars.CarCondition.WhyCannotRace(car, Parts.Cars.CarCondition.Groups(_partsService.Catalog));
                    if (damage.Count > 0)
                    {
                        _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                            _dialogService,
                            $"The car is not going anywhere: {string.Join("; ", damage)}.\n\nThe Repairs button sorts it out.",
                            "Car Won't Run"));
                        return;
                    }

                    data = await Task.Run(() => _raceCarDataService.Prepare(car));
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
                    CarId = selected.CarDefinition.Id,
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
                    await _timeService.SpendTimeAsync(_gameState, GameAction.FreeRun);
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

        /// <summary>The repair bay for the selected car: each job paid for, timed and saved as it is done</summary>
        private void OnRepairs()
        {
            if (SelectedCar is not { } selected) return;

            var car = selected.CarInstance;
            _dialogService.ShowDialog(new Dialogs.RepairShop.RepairShopDialogViewModel(
                _dialogService, car, selected.DisplayName, Workbench.Catalog, _gameState.Rules.PartPriceMultiplier,
                () => _gameState.Player.Money,
                async job =>
                {
                    if (_gameState.Player.Money < job.Cost) return;

                    _gameState.Player.Money -= job.Cost;
                    job.Apply();
                    _logger.Information("Repaired {Car}: {Job} for ${Cost}", car.DefinitionId, job.Name, job.Cost);

                    try
                    {
                        // Before saving: the time the work took belongs in the save, and so does the new day it may end in
                        await _timeService.SpendTimeAsync(_gameState, job.Time);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Could not spend the time for the repair: {Error}", ex.Message);
                    }

                    try
                    {
                        if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Could not save the game after a repair");
                    }

                    RefreshCalendarDisplay();
                    OnPropertyChanged(nameof(BankrollDisplay));
                    OnPropertyChanged(nameof(SelectedCar));
                    // The parts view shows the parts' shape: it gets the repaired ones
                    Workbench.SetCar(car);
                }));
        }

        // ----- the police impound -----

        /// <summary>A car is selected and it is in the garage, not the police impound</summary>
        private bool SelectedCarHere => SelectedCar is { CarInstance.IsImpounded: false };

        public bool IsSelectedCarImpounded => SelectedCar?.CarInstance.IsImpounded == true;

        /// <summary>Where the selected car is and what getting it back takes</summary>
        public string ImpoundNote
        {
            get
            {
                if (SelectedCar?.CarInstance is not { ImpoundedUntil: { } until } car) return string.Empty;
                return PoliceRules.CanCollect(car, _gameState.Date)
                    ? $"The police have this car in the impound. It can be collected now for ${car.ImpoundFee:N0}."
                    : $"The police have this car in the impound until {until:dddd d MMMM}, {until:h:mm tt}. Collecting it will cost ${car.ImpoundFee:N0}.";
            }
        }

        public string CollectButtonText => SelectedCar?.CarInstance is { IsImpounded: true } car ? $"Collect (${car.ImpoundFee:N0})" : "Collect";

        private bool CanCollect() =>
            SelectedCar?.CarInstance is { } car && PoliceRules.CanCollect(car, _gameState.Date) && _gameState.Player.Money >= car.ImpoundFee;

        /// <summary>Pays the impound, brings the car home and saves; an hour goes by getting there and back</summary>
        private async void OnCollect()
        {
            try
            {
                if (SelectedCar is not { } selected || !CanCollect()) return;

                var car = selected.CarInstance;
                _gameState.Player.Money -= car.ImpoundFee;
                _gameState.Player.Stats.TotalLosses += car.ImpoundFee;
                _logger.Information("Collected {Car} from the impound for ${Fee}", car.DefinitionId, car.ImpoundFee);
                PoliceRules.Release(car);

                try
                {
                    await _timeService.SpendTimeAsync(_gameState, GameAction.CollectFromImpound);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not spend the time for the impound: {Error}", ex.Message);
                }

                try
                {
                    if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not save the game after collecting the car");
                }

                Workbench.SetCar(car);
                RefreshCalendarDisplay();
                OnPropertyChanged(nameof(BankrollDisplay));
                RaiseImpoundChanged();
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void
                _logger.Error(ex, "Could not collect the car from the impound");
            }
        }

        private void RaiseImpoundChanged()
        {
            OnPropertyChanged(nameof(IsSelectedCarImpounded));
            OnPropertyChanged(nameof(ImpoundNote));
            OnPropertyChanged(nameof(CollectButtonText));
            RelayCommand.RaiseCanExecuteChanged();
            FreeRunCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>"Sell", or where the selected car's sale stands when it is in the paper</summary>
        public string SellButtonText
        {
            get
            {
                if (SelectedCar is not { } selected || _saleService.AdFor(_gameState, selected.CarInstance) is not { } ad) return "Sell";
                return ad.Offer is { } offer && offer.Expires >= _gameState.Date ? "Sell (offer!)" : "Sell (in the paper)";
            }
        }

        /// <summary>Selling the selected car: to the dealer, or through the paper</summary>
        private void OnSell()
        {
            if (SelectedCar is not { } selected) return;

            _dialogService.ShowDialog(new Dialogs.SellCar.SellCarDialogViewModel(
                _dialogService, _gameState, selected.CarInstance, selected.DisplayName, _saleService,
                sold: () =>
                {
                    // The car is gone: the garage shows what is left, the selected car the sale moved on to
                    LoadPlayerCars();
                    if (Cars.Count == 0) SelectedCar = null;
                    OnPropertyChanged(nameof(HasCars));
                    AfterSale();
                },
                changed: AfterSale));
        }

        private void AfterSale()
        {
            RefreshCalendarDisplay();
            OnPropertyChanged(nameof(BankrollDisplay));
            OnPropertyChanged(nameof(SellButtonText));
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
        private async void LeaveTo(Func<bool> navigate)
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

            try
            {
                // The navigation service reports a screen that would not open; the garage then comes back
                if (!navigate()) ExitCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not leave the garage");
                ExitCancelled?.Invoke();
            }
            finally
            {
                _isLeaving = false;
            }
        }

        private bool _endingDay;

        /// <summary>Asks, then ends the day: the night's scheduled work runs, and the new day is saved</summary>
        private void OnEndDay()
        {
            var left = _timeService.GetRemainingMinutesToday(_gameState);
            var message = left > 0
                ? $"Call it a day? The rest of today ({left / 60}h {left % 60:D2}m) goes by, and you pick up tomorrow at {_timeService.DayStartHour}:00 AM."
                : $"Call it a day? You pick up tomorrow at {_timeService.DayStartHour}:00 AM.";

            _dialogService.ShowDialog(new Dialogs.Confirmation.ConfirmationDialogViewModel(
                _dialogService, message, "End the Day?",
                confirmed =>
                {
                    if (confirmed) EndDay();
                }));
        }

        private async void EndDay()
        {
            if (_endingDay) return;
            _endingDay = true;
            RelayCommand.RaiseCanExecuteChanged();

            try
            {
                var result = await _timeService.EndDayAsync(_gameState);
                _logger.Information("Day ended from the garage; now {Date}", _gameState.Date);
                if (result.FailedTaskIds.Count > 0)
                    _logger.Warning("The night's work did not all get done: {Tasks}", string.Join(", ", result.FailedTaskIds));

                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not end the day");
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    _dialogService, $"The day could not be ended:\n\n{ex.Message}", "End of Day"));
            }
            finally
            {
                _endingDay = false;
                // Nothing may escape an async void, the refresh included
                try
                {
                    RefreshCalendarDisplay();
                    OnPropertyChanged(nameof(BankrollDisplay));
                    // The night may have brought an offer for a car in the paper, or the impound's release date
                    OnPropertyChanged(nameof(SellButtonText));
                    RaiseImpoundChanged();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not refresh the garage after the end of the day");
                }
            }
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

            try
            {
                // Refresh calendar properties to reflect current game time
                RefreshCalendarDisplay();

                // Refresh bankroll display in case money changed
                OnPropertyChanged(nameof(BankrollDisplay));

                LoadPlayerCars();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not set up the garage");
            }
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

            // The grid changes with the day and the pictures with the month: not on every piece of work in the garage
            if (_gameState.Date.Date != _shownCalendarDay)
            {
                _shownCalendarDay = _gameState.Date.Date;
                OnPropertyChanged(nameof(CalendarDays));
            }

            var month = _gameState.Date.Year * 100 + _gameState.Date.Month;
            if (month == _shownCalendarMonth) return;

            _shownCalendarMonth = month;
            OnPropertyChanged(nameof(CalendarImage1));
            OnPropertyChanged(nameof(CalendarImage2));
            OnPropertyChanged(nameof(CalendarImage3));
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
        /// <summary>What is worst about the car, or its overall shape: the one condition the market prices it by</summary>
        public string ConditionDisplay => Shared.ConditionDisplay.Of(CarInstance).Percent;
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
