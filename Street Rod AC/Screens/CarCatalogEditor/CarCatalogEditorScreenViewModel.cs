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

        public bool HasDirtyItems => _catalogItems?.Any(x => x.IsDirty) ?? false;

        public CarCatalogEditorScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            ICarProfileService profileService)
        {
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

            LoadCatalog();
        }

        private void LoadCatalog()
        {
            _logger.Information("Loading car catalog for editing");

            var allCars = _catalogRepo.GetAllCars()
                .Where(c => c.Status == ContentStatus.Active)
                .OrderBy(c => c.Brand)
                .ThenBy(c => c.Name)
                .ToList();

            _logger.Information("Found {Count} active car definitions", allCars.Count);

            // Collect unique brands
            var brands = allCars
                .Select(c => c.Brand)
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            foreach (var brand in brands)
            {
                BrandOptions.Add(brand);
            }

            // Load each car with its profile
            foreach (var carDef in allCars)
            {
                var profile = _profileRepo.GetOrCreateProfile(
                    carDef.Id,
                    () => _profileService.GenerateDefaultProfile(carDef));

                var itemVm = new CatalogItemViewModel(carDef, profile);
                itemVm.PropertyChanged += OnItemPropertyChanged;
                _catalogItems.Add(itemVm);
            }

            _logger.Information("Loaded {Count} catalog items for editing", _catalogItems.Count);
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

            foreach (var item in dirtyItems)
            {
                item.ApplyChanges();
                _profileRepo.UpsertProfile(item.Profile);
                _logger.Debug("Saved profile for {CarId}", item.Definition.Id);
            }

            OnPropertyChanged(nameof(HasDirtyItems));

            var successDialog = new InformationDialogViewModel(
                _dialogService,
                $"Saved {dirtyItems.Count} car profile(s).",
                "Save Successful");
            _dialogService.ShowDialog(successDialog);

            _logger.Information("Save completed");
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

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered car catalog editor screen");
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
    }
}
