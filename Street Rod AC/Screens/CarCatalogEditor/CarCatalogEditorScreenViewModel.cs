using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Street_Rod_AC.Screens.CarCatalogEditor
{
    public class CarCatalogEditorScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly ICarProfileService _profileService;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ResetAllCommand { get; }
        public RelayCommand<CatalogItemViewModel> ResetItemCommand { get; }

        private ObservableCollection<CatalogItemViewModel> _catalogItems;
        public ObservableCollection<CatalogItemViewModel> CatalogItems
        {
            get => _catalogItems;
            set
            {
                _catalogItems = value;
                OnPropertyChanged(nameof(CatalogItems));
            }
        }

        private ICollectionView _catalogView;
        public ICollectionView CatalogView
        {
            get => _catalogView;
            set
            {
                _catalogView = value;
                OnPropertyChanged(nameof(CatalogView));
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                FilterCatalog();
            }
        }

        private string _selectedBrand = "All Brands";
        public string SelectedBrand
        {
            get => _selectedBrand;
            set
            {
                _selectedBrand = value;
                OnPropertyChanged(nameof(SelectedBrand));
                FilterCatalog();
            }
        }

        public ObservableCollection<string> BrandOptions { get; set; }

        private readonly Services.Parts.ICarPartsService? _partsService;

        public bool HasDirtyItems => _catalogItems?.Any(x => x.IsDirty) ?? false;

        private bool _isLoading;

        /// <summary>The catalog is being read; the list fills when it is done</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public CarCatalogEditorScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            ICarProfileService profileService,
            Services.Parts.ICarPartsService? partsService = null)
        {
            _partsService = partsService;
            _navigationService = navigationService;
            _dialogService = dialogService;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _profileService = profileService;
            _logger = AppLoggerFactory.CreateLogger("CarCatalogEditor");

            BackCommand = new RelayCommand(OnBack);
            SaveCommand = new RelayCommand(OnSave);
            ResetAllCommand = new RelayCommand(OnResetAll);
            ResetItemCommand = new RelayCommand<CatalogItemViewModel>(OnResetItem);

            _catalogItems = new ObservableCollection<CatalogItemViewModel>();
            _catalogView = CollectionViewSource.GetDefaultView(_catalogItems);

            BrandOptions = new ObservableCollection<string> { "All Brands" };

            // The catalog is read in Enter, off the UI thread
        }

        /// <summary>Every engine build that runs, the likeliest for this car first. Only called once the parts catalog has loaded</summary>
        private IReadOnlyList<EngineOptionViewModel> EngineOptionsFor(CarDefinition car)
        {
            if (_partsService is not { IsAvailable: true }) return Array.Empty<EngineOptionViewModel>();

            return Parts.Cars.StockEngineMatcher
                .Rank(_partsService.Builds, car.Brand, car.Name, Parts.Cars.StockEngineMatcher.ParsePower(car.Specs?.Bhp))
                .Select(m => new EngineOptionViewModel(m.Build.Build.Id, $"{m.Build.Build.Name}  \u00b7  {m.Build.PowerHp:0} hp, {m.Build.Litres:0.0} l"))
                .ToList();
        }

        /// <summary>What the list is made from, read off the UI thread</summary>
        private sealed record CatalogSnapshot(List<CarDefinition> Cars, Dictionary<string, CarProfile> Profiles, bool HasEngines);

        /// <summary>
        /// Reads every active car and every profile in one go on a worker thread, and loads the parts catalog
        /// there too (its first use is a full load). The rows are made back on the UI thread; their engine lists
        /// are ranked only when a row is shown.
        /// </summary>
        private async Task LoadCatalogAsync()
        {
            _logger.Information("Loading car catalog for editing");
            IsLoading = true;

            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var cars = _catalogRepo.GetAllCars()
                        .Where(c => c.Status == ContentStatus.Active)
                        .OrderBy(c => c.Brand)
                        .ThenBy(c => c.Name)
                        .ToList();

                    var profiles = new Dictionary<string, CarProfile>(StringComparer.OrdinalIgnoreCase);
                    foreach (var profile in _profileRepo.GetAllProfiles())
                    {
                        profiles[profile.CarDefinitionId] = profile;
                    }

                    var hasEngines = _partsService is { IsAvailable: true };
                    return new CatalogSnapshot(cars, profiles, hasEngines);
                });

                _logger.Information("Found {Count} active car definitions", snapshot.Cars.Count);

                // A second load (the screen entered again) starts from an empty list, not on top of the first
                foreach (var item in _catalogItems) item.PropertyChanged -= OnItemPropertyChanged;
                _catalogItems.Clear();
                BrandOptions.Clear();
                BrandOptions.Add("All Brands");
                SelectedBrand = "All Brands";

                foreach (var brand in snapshot.Cars.Select(c => c.Brand).Distinct().OrderBy(b => b))
                {
                    BrandOptions.Add(brand);
                }

                foreach (var carDef in snapshot.Cars)
                {
                    // A car imported since the profiles were made gets one now, as it did before
                    if (!snapshot.Profiles.TryGetValue(carDef.Id, out var profile))
                    {
                        profile = _profileRepo.GetOrCreateProfile(carDef.Id, () => _profileService.GenerateDefaultProfile(carDef));
                    }

                    var definition = carDef;
                    var itemVm = new CatalogItemViewModel(definition, profile, snapshot.HasEngines, () => EngineOptionsFor(definition));
                    itemVm.PropertyChanged += OnItemPropertyChanged;
                    _catalogItems.Add(itemVm);
                }

                _logger.Information("Loaded {Count} catalog items for editing", _catalogItems.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not load the car catalog for editing");
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The car catalog could not be read:\n\n{ex.Message}",
                    "Catalog Not Loaded"));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CatalogItemViewModel.IsDirty))
            {
                OnPropertyChanged(nameof(HasDirtyItems));
            }
        }

        private void FilterCatalog()
        {
            if (_catalogView == null) return;

            _catalogView.Filter = obj =>
            {
                if (obj is not CatalogItemViewModel item) return false;

                // Brand filter
                if (_selectedBrand != "All Brands" && item.Definition.Brand != _selectedBrand)
                {
                    return false;
                }

                // Search filter
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    var search = _searchText.ToLowerInvariant();
                    var matchesName = item.Definition.Name.ToLowerInvariant().Contains(search);
                    var matchesBrand = item.Definition.Brand.ToLowerInvariant().Contains(search);
                    if (!matchesName && !matchesBrand)
                    {
                        return false;
                    }
                }

                return true;
            };

            _catalogView.Refresh();
        }

        private void OnBack()
        {
            if (HasDirtyItems)
            {
                var confirmDialog = new ConfirmationDialogViewModel(
                    _dialogService,
                    "You have unsaved changes. Are you sure you want to leave?",
                    "Unsaved Changes",
                    confirmed =>
                    {
                        if (confirmed)
                        {
                            _navigationService.NavigateToSettings();
                        }
                    });
                _dialogService.ShowDialog(confirmDialog);
            }
            else
            {
                _navigationService.NavigateToSettings();
            }
        }

        private void OnSave()
        {
            var dirtyItems = _catalogItems.Where(x => x.IsDirty).ToList();

            if (dirtyItems.Count == 0)
            {
                var infoDialog = new InformationDialogViewModel(
                    _dialogService,
                    "No changes to save.",
                    "Save");
                _dialogService.ShowDialog(infoDialog);
                return;
            }

            _logger.Information("Saving {Count} modified car profiles", dirtyItems.Count);

            try
            {
                SaveProfiles(dirtyItems);
            }
            catch (Exception ex)
            {
                // catalog.db locked by a second copy of the game or a virus scanner, a read-only folder...
                _logger.Error(ex, "Could not save the car profiles");
                OnPropertyChanged(nameof(HasDirtyItems));
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The car profiles could not be saved:\n\n{ex.Message}",
                    "Save Failed"));
                return;
            }

            OnPropertyChanged(nameof(HasDirtyItems));

            var successDialog = new InformationDialogViewModel(
                _dialogService,
                $"Saved {dirtyItems.Count} car profile(s).",
                "Save Successful");
            _dialogService.ShowDialog(successDialog);

            _logger.Information("Save completed");
        }

        /// <summary>Writes the edited profiles, one by one; what was written before a failure stays written</summary>
        private void SaveProfiles(List<CatalogItemViewModel> dirtyItems)
        {
            foreach (var item in dirtyItems)
            {
                item.ApplyChanges();

                // The factory engines are suggested in the background after the game starts: one that came in
                // since this profile was read stays, unless an engine was picked here
                var edited = item.Profile;
                var updated = _profileRepo.UpdateProfile(edited.CarDefinitionId, stored =>
                {
                    if (!edited.StockEngineIsManual)
                    {
                        edited.StockEngineBuildId = stored.StockEngineBuildId;
                        edited.StockEngineIsManual = stored.StockEngineIsManual;
                    }

                    return edited;
                });
                if (!updated) _profileRepo.UpsertProfile(edited);
                _logger.Debug("Saved profile for {CarId}", item.Definition.Id);
            }
        }

        private void OnResetAll()
        {
            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                "This will recalculate all car prices and spawn rates from their specifications. Any manual edits will be lost. Continue?",
                "Reset All Profiles",
                confirmed =>
                {
                    if (confirmed)
                    {
                        ResetAllProfiles();
                    }
                });
            _dialogService.ShowDialog(confirmDialog);
        }

        private void ResetAllProfiles()
        {
            _logger.Information("Resetting all car profiles to defaults");

            foreach (var item in _catalogItems)
            {
                var generatedProfile = _profileService.GenerateDefaultProfile(item.Definition);
                item.SetFromGeneratedProfile(generatedProfile);
            }

            OnPropertyChanged(nameof(HasDirtyItems));

            var successDialog = new InformationDialogViewModel(
                _dialogService,
                $"Reset {_catalogItems.Count} car profiles. Click Save to apply changes.",
                "Reset Complete");
            _dialogService.ShowDialog(successDialog);

            _logger.Information("Reset all profiles complete");
        }

        private void OnResetItem(CatalogItemViewModel? item)
        {
            if (item == null) return;

            _logger.Debug("Resetting profile for {CarId}", item.Definition.Id);

            var generatedProfile = _profileService.GenerateDefaultProfile(item.Definition);
            item.SetFromGeneratedProfile(generatedProfile);

            OnPropertyChanged(nameof(HasDirtyItems));
        }

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered car catalog editor screen");

            // Guarded inside; this is only here so nothing can escape a fire-and-forget call
            try
            {
                await LoadCatalogAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the car catalog editor");
            }
        }

        public override void Exit()
        {
            base.Exit();

            // Clean up event handlers
            foreach (var item in _catalogItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            _logger.Information("Exited car catalog editor screen");
        }

        /// <summary>Back after a navigation away failed: the rows are still here, only their change tracking was let go</summary>
        public override void Resume()
        {
            base.Resume();

            foreach (var item in _catalogItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                item.PropertyChanged += OnItemPropertyChanged;
            }

            OnPropertyChanged(nameof(HasDirtyItems));
        }
    }
}
