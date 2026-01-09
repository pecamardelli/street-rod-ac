using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Street_Rod_AC.Screens.UsedCarMarket
{
    public class UsedCarMarketScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IUsedCarMarketService _marketService;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand<UsedCarListingViewModel> PurchaseCarCommand { get; }
        public RelayCommand RefreshMarketCommand { get; }

        private ObservableCollection<UsedCarListingViewModel> _listings;
        public ObservableCollection<UsedCarListingViewModel> Listings
        {
            get => _listings;
            set
            {
                _listings = value;
                OnPropertyChanged(nameof(Listings));
            }
        }

        private ICollectionView _listingsView;
        public ICollectionView ListingsView
        {
            get => _listingsView;
            set
            {
                _listingsView = value;
                OnPropertyChanged(nameof(ListingsView));
            }
        }

        private string _selectedDealer = "All Dealers";
        public string SelectedDealer
        {
            get => _selectedDealer;
            set
            {
                _selectedDealer = value;
                OnPropertyChanged(nameof(SelectedDealer));
                FilterListings();
            }
        }

        public ObservableCollection<string> DealerOptions { get; set; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public UsedCarMarketScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IUsedCarMarketService marketService,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _marketService = marketService;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _logger = AppLoggerFactory.CreateLogger("UsedCarMarket");

            BackCommand = new RelayCommand(OnBack);
            PurchaseCarCommand = new RelayCommand<UsedCarListingViewModel>(OnPurchaseCar, CanPurchaseCar);
            RefreshMarketCommand = new RelayCommand(OnRefreshMarket);

            _listings = new ObservableCollection<UsedCarListingViewModel>();
            _listingsView = CollectionViewSource.GetDefaultView(_listings);

            DealerOptions = new ObservableCollection<string> { "All Dealers" };

            InitializeMarket();
        }

        private void InitializeMarket()
        {
            _logger.Information("Initializing used car market screen");

            // Ensure market is initialized
            if (_gameState.UsedCarMarket == null || _gameState.UsedCarMarket.Count == 0)
            {
                _logger.Information("Market is empty, spawning initial listings");

                // Get or create dealers
                if (_gameState.DealerLocations == null || _gameState.DealerLocations.Count == 0)
                {
                    _gameState.DealerLocations = _marketService.GetDefaultDealers();
                    _logger.Information("Created {DealerCount} default dealers", _gameState.DealerLocations.Count);
                }

                // Spawn initial market
                var listings = _marketService.SpawnListings(_gameState.DealerLocations, _gameState.Date);
                _gameState.UsedCarMarket = listings;
                _logger.Information("Spawned {ListingCount} initial listings", listings.Count);
            }

            // Load dealer options
            if (_gameState.DealerLocations != null)
            {
                foreach (var dealer in _gameState.DealerLocations)
                {
                    DealerOptions.Add(dealer.Name);
                }
            }

            // Load listings into view models
            LoadListings();
        }

        private void LoadListings()
        {
            Listings.Clear();

            var availableListings = _marketService.GetAvailableListings(_gameState.UsedCarMarket);
            _logger.Information("Loading {Count} available listings", availableListings.Count);

            foreach (var listing in availableListings)
            {
                var carDef = _catalogRepo.GetCar(listing.CarDefinitionId);
                if (carDef == null)
                {
                    _logger.Warning("Car definition not found for listing {ListingId}, car {CarId}",
                        listing.Id, listing.CarDefinitionId);
                    continue;
                }

                var profile = _profileRepo.GetProfile(listing.CarDefinitionId);
                if (profile == null)
                {
                    _logger.Warning("Car profile not found for listing {ListingId}, car {CarId}",
                        listing.Id, listing.CarDefinitionId);
                    continue;
                }

                var dealerLocation = _gameState.DealerLocations?
                    .FirstOrDefault(d => d.Id == listing.DealerLocation);

                var viewModel = new UsedCarListingViewModel
                {
                    Listing = listing,
                    CarDefinition = carDef,
                    CarProfile = profile,
                    DealerName = dealerLocation?.Name ?? "Unknown",
                    CanAfford = _gameState.Player.Money >= listing.Price
                };

                Listings.Add(viewModel);
            }

            _logger.Information("Loaded {Count} listing view models", Listings.Count);
        }

        private void FilterListings()
        {
            if (ListingsView == null) return;

            if (_selectedDealer == "All Dealers")
            {
                ListingsView.Filter = null;
            }
            else
            {
                ListingsView.Filter = obj =>
                {
                    if (obj is UsedCarListingViewModel vm)
                    {
                        return vm.DealerName == _selectedDealer;
                    }
                    return false;
                };
            }

            ListingsView.Refresh();
        }

        private bool CanPurchaseCar(UsedCarListingViewModel? viewModel)
        {
            if (viewModel == null) return false;
            return _gameState.Player.Money >= viewModel.Listing.Price;
        }

        private void OnPurchaseCar(UsedCarListingViewModel? viewModel)
        {
            if (viewModel == null) return;

            var listing = viewModel.Listing;
            var carDef = viewModel.CarDefinition;

            _logger.Information("Player attempting to purchase car {CarId} for ${Price}",
                carDef.Id, listing.Price);

            // Show confirmation dialog
            var message = $"Purchase this {carDef.Brand} {carDef.Name}?\n\n" +
                         $"Year: {carDef.Year ?? 0}\n" +
                         $"Price: ${listing.Price:N0}\n" +
                         $"Condition: {GetConditionLabel(listing.Condition)}\n" +
                         $"Mileage: {listing.Mileage:N0} km\n" +
                         $"Dealer: {viewModel.DealerName}\n\n" +
                         $"Your bankroll: ${_gameState.Player.Money:N0}";

            var confirmDialog = new ConfirmationDialogViewModel(
                _dialogService,
                message,
                "Purchase Car?",
                confirmed =>
                {
                    if (confirmed)
                    {
                        CompletePurchase(listing, carDef, viewModel.DealerName);
                    }
                });

            _dialogService.ShowDialog(confirmDialog);
        }

        private void CompletePurchase(UsedCarListing listing, CarDefinition carDef, string dealerName)
        {
            // Validate funds
            if (_gameState.Player.Money < listing.Price)
            {
                _logger.Warning("Purchase failed: insufficient funds");
                var errorDialog = new InformationDialogViewModel(
                    _dialogService,
                    "You don't have enough money to purchase this car.",
                    "Insufficient Funds");
                _dialogService.ShowDialog(errorDialog);
                return;
            }

            // Validate listing still available
            var currentListing = _gameState.UsedCarMarket.FirstOrDefault(l => l.Id == listing.Id);
            if (currentListing == null || currentListing.IsSold)
            {
                _logger.Warning("Purchase failed: listing no longer available");
                var errorDialog = new InformationDialogViewModel(
                    _dialogService,
                    "This car is no longer available.",
                    "Car Unavailable");
                _dialogService.ShowDialog(errorDialog);
                LoadListings(); // Refresh display
                return;
            }

            // Create car instance from listing
            var carInstance = new Car
            {
                InstanceId = Guid.NewGuid(),
                DefinitionId = listing.CarDefinitionId,
                PurchasePrice = listing.Price,
                PurchaseDate = _gameState.Date,
                OdometerKM = listing.Mileage,
                // Map single condition to health metrics
                EngineHealth = listing.Condition,
                TransmissionHealth = listing.Condition,
                BodyCondition = listing.Condition,
                TireCondition = listing.Condition,
                InstalledParts = new List<Part>()
            };

            // Add to player's garage
            if (_gameState.Player.Cars == null)
            {
                _gameState.Player.Cars = new List<Car>();
            }
            _gameState.Player.Cars.Add(carInstance);

            // Deduct money
            _gameState.Player.Money -= listing.Price;

            // Mark listing as sold
            currentListing.IsSold = true;
            currentListing.SoldDate = _gameState.Date;

            _logger.Information("Purchase completed: {CarName} for ${Price}, new bankroll: ${Bankroll}",
                carDef.Name, listing.Price, _gameState.Player.Money);

            // Refresh display
            LoadListings();
            OnPropertyChanged(nameof(BankrollDisplay));

            // Show success message
            var successDialog = new InformationDialogViewModel(
                _dialogService,
                $"Congratulations! You've purchased a {carDef.Brand} {carDef.Name} for ${listing.Price:N0}.\n\nYou can now find it in your garage.",
                "Purchase Successful");
            _dialogService.ShowDialog(successDialog);
        }

        private void OnRefreshMarket()
        {
            _logger.Information("Manual market refresh requested");

            if (_gameState.DealerLocations == null || _gameState.DealerLocations.Count == 0)
            {
                _gameState.DealerLocations = _marketService.GetDefaultDealers();
            }

            var refreshedListings = _marketService.RefreshMarket(
                _gameState.UsedCarMarket,
                _gameState.DealerLocations,
                _gameState.Date);

            _gameState.UsedCarMarket = refreshedListings;
            LoadListings();

            _logger.Information("Market refresh completed");
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to newspaper");
            var newspaperViewModel = new Newspaper.NewspaperScreenViewModel(_navigationService, _dialogService, _gameState);
            _navigationService.NavigateTo(newspaperViewModel);
        }

        private string GetConditionLabel(float condition)
        {
            if (condition >= 0.9f) return "Excellent";
            if (condition >= 0.75f) return "Good";
            if (condition >= 0.6f) return "Fair";
            if (condition >= 0.4f) return "Poor";
            return "Very Poor";
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered used car market screen");
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited used car market screen");
        }
    }

    /// <summary>
    /// View model for a single used car listing
    /// </summary>
    public class UsedCarListingViewModel
    {
        public UsedCarListing Listing { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public CarProfile CarProfile { get; set; } = new();
        public string DealerName { get; set; } = string.Empty;
        public bool CanAfford { get; set; }

        public string DisplayName => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public string YearDisplay => CarDefinition.Year?.ToString() ?? "Unknown";
        public string PriceDisplay => $"${Listing.Price:N0}";
        public string ConditionDisplay => $"{(int)(Listing.Condition * 100)}%";
        public string MileageDisplay => $"{Listing.Mileage:N0} km";
    }
}
