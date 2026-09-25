using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Storage;
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
        private readonly IGameStateRepository _gameStateRepo;
        private readonly PurchaseFlow _purchaseFlow;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand<UsedCarListingViewModel> PurchaseCarCommand { get; }

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
            ICarProfileRepository profileRepo,
            IGameStateRepository gameStateRepo,
            ICarPurchaseService purchaseService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _marketService = marketService;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _gameStateRepo = gameStateRepo;
            _logger = AppLoggerFactory.CreateLogger("UsedCarMarket");
            _purchaseFlow = new PurchaseFlow(dialogService, purchaseService, _logger);

            BackCommand = new RelayCommand(OnBack);
            PurchaseCarCommand = new RelayCommand<UsedCarListingViewModel>(OnPurchaseCar, CanPurchaseCar);

            _listings = new ObservableCollection<UsedCarListingViewModel>();
            _listingsView = CollectionViewSource.GetDefaultView(_listings);

            DealerOptions = ["All Dealers"];

            // The market is loaded in Enter, after the screen before it has been left
        }

        private bool _isLoading;

        /// <summary>The first market of a game is being put together: every car gets its engine, which takes a few seconds</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        private async Task InitializeMarket()
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
                IsLoading = true;
                try
                {
                    var listings = await _marketService.SpawnListingsAsync(_gameState.DealerLocations, _gameState.Date, _gameState.Rules.CarPriceMultiplier);
                    _gameState.UsedCarMarket = listings;
                    _logger.Information("Spawned {ListingCount} initial listings", listings.Count);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not spawn the initial market");
                }
                finally
                {
                    IsLoading = false;
                }
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

            // A market that failed to spawn is empty, not missing
            var availableListings = _marketService.GetAvailableListings(_gameState.UsedCarMarket ?? []);
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

            _purchaseFlow.Offer(_gameState, viewModel, result =>
            {
                // Bought, or sold to somebody else meanwhile: either way the ads are not what they were
                if (result.Succeeded || result.Outcome == PurchaseOutcome.NoLongerAvailable) LoadListings();
                OnPropertyChanged(nameof(BankrollDisplay));
            });
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to newspaper");
            _navigationService.NavigateToNewspaper(_gameState, skipAnimation: true);
        }

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered used car market screen");

            // The whole load in one guard: a market that cannot be put together leaves an empty page
            try
            {
                await InitializeMarket();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the used car ads");
                IsLoading = false;
            }
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
        public string ConditionDisplay => Shared.ConditionDisplay.Of(Listing.Condition).Percent;
        public string MileageDisplay => $"{Listing.Mileage:N0} km";

        /// <summary>The condition in a word, for the purchase question</summary>
        public string ConditionLabel => Shared.ConditionDisplay.Of(Listing.Condition).Label;

        /// <summary>What is under the hood, with a word on it when somebody has been at it</summary>
        public string EngineDisplay => Listing.EngineSummary == null
            ? string.Empty
            : Listing.IsModified ? Listing.EngineSummary + " (modified)" : Listing.EngineSummary;
    }
}
