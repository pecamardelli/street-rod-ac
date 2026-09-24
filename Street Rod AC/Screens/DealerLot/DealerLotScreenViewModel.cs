using System.Collections.ObjectModel;
using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Controls;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.Shared;
using Street_Rod_AC.Screens.UsedCarMarket;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Dealers;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.DealerLot
{
    /// <summary>
    /// One dealer's lot, with its cars standing in it.
    ///
    /// Two views of the same scene: the whole lot, or one car. Which one is showing is
    /// <see cref="SelectedIndex"/>, and everything else on screen follows from it.
    /// </summary>
    public class DealerLotScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly IUsedCarMarketService _marketService;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly PurchaseFlow _purchaseFlow;
        private readonly ICarPartsService _partsService;
        private readonly IAppLogger _logger;
        private readonly DealerDefinition? _dealer;

        public RelayCommand BackCommand { get; }
        public RelayCommand BackToLotCommand { get; }
        public RelayCommand<UsedCarListingViewModel> PurchaseCarCommand { get; }

        /// <summary>Everything this dealer has, whether or not it fitted on the lot</summary>
        public ObservableCollection<UsedCarListingViewModel> Stock { get; } = [];

        /// <summary>The cars the 3D lot should stand up, in the same order as the first of <see cref="Stock"/></summary>
        public IReadOnlyList<LotCarPlacement> LotCars { get; private set; } = [];

        public string DealerName => _dealer?.Name ?? "Dealer";
        public string DealerRegion => _dealer?.Region ?? string.Empty;
        public string DealerBlurb => _dealer?.Blurb ?? string.Empty;
        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        /// <summary>The clock, so the hours the drive cost are in front of the player when they arrive</summary>
        public string DateDisplay => _gameState.Date.ToString("dddd, MMMM d, yyyy");
        public string TimeDisplay => _gameState.Date.ToString("h:mm tt");

        /// <summary>The lot's 3D scene, or null when the showroom is not installed</summary>
        public string? ShowroomKn5 { get; private set; }

        /// <summary>How far back the camera stands to take the lot in, worked out from the room it is in</summary>
        public float LotRadius { get; private set; } = 19f;

        public DealerLotScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            string dealerId,
            IDealerCatalog dealerCatalog,
            IUsedCarMarketService marketService,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            IGameStateRepository gameStateRepo,
            ICarPurchaseService purchaseService,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _dealerCatalog = dealerCatalog;
            _marketService = marketService;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _logger = AppLoggerFactory.CreateLogger("DealerLot");
            _purchaseFlow = new PurchaseFlow(dialogService, purchaseService, _logger);
            _dealer = dealerCatalog.Get(dealerId);

            BackCommand = new RelayCommand(OnBack);
            BackToLotCommand = new RelayCommand(() => SelectedIndex = -1);
            PurchaseCarCommand = new RelayCommand<UsedCarListingViewModel>(OnPurchaseCar, CanPurchaseCar);

            ResolveShowroom();
            LoadStock();
        }

        private int _selectedIndex = -1;

        /// <summary>Which car of <see cref="LotCars"/> the camera is on, or -1 for the whole lot</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                OnPropertyChanged(nameof(SelectedIndex));
                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(HasSelection));
                RefreshEngine();
            }
        }

        private Audio.EngineSpec? _selectedEngine;
        private int _engineVersion;

        /// <summary>The engine of the car being looked at, as it is sold, to start and rev on the lot</summary>
        public Audio.EngineSpec? SelectedEngine
        {
            get => _selectedEngine;
            private set
            {
                _selectedEngine = value;
                OnPropertyChanged(nameof(SelectedEngine));
            }
        }

        private async void RefreshEngine()
        {
            var version = ++_engineVersion;
            var car = SelectedCar;

            // Off at once: the car the camera leaves must not keep running
            SelectedEngine = null;
            if (car == null) return;

            try
            {
                var spec = await EngineSpecs.ForSaleAsync(_partsService, car.Listing, car.DisplayName);
                if (version == _engineVersion) SelectedEngine = spec;
            }
            catch (Exception ex)
            {
                // No engine to start is all that comes of it: the lot and the car are still there to look at
                _logger.Error(ex, "Could not work out the engine of {Car}", car.DisplayName);
            }
        }

        /// <summary>The car being looked at, or null on the lot view</summary>
        public UsedCarListingViewModel? SelectedCar =>
            _selectedIndex >= 0 && _selectedIndex < Stock.Count ? Stock[_selectedIndex] : null;

        public bool HasSelection => SelectedCar != null;

        private void ResolveShowroom()
        {
            var id = _dealer?.ShowroomId;
            if (string.IsNullOrWhiteSpace(id)) return;

            var path = Path.Combine(AppSettings.Instance.ShowroomsPath, id, id + ".kn5");
            if (File.Exists(path))
            {
                ShowroomKn5 = path;
                return;
            }

            // Folder and model are not always spelled the same way; take whatever .kn5 the folder holds
            var folder = Path.Combine(AppSettings.Instance.ShowroomsPath, id);
            if (Directory.Exists(folder))
            {
                var found = Directory.EnumerateFiles(folder, "*.kn5", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (found != null)
                {
                    ShowroomKn5 = found;
                    return;
                }
            }

            _logger.Warning("Showroom {Showroom} is not installed; the lot will have no scene", id);
        }

        private void LoadStock()
        {
            Stock.Clear();

            var listings = _marketService.GetListingsByDealer(_gameState.UsedCarMarket ?? [], _dealer?.Id ?? string.Empty);

            // The nearest cars go in the bays, in price order so the lot reads from cheap to dear
            foreach (var listing in listings.OrderBy(l => l.Price))
            {
                var carDef = _catalogRepo.GetCar(listing.CarDefinitionId);
                if (carDef == null)
                {
                    _logger.Warning("No car definition for listing {ListingId}", listing.Id);
                    continue;
                }

                var profile = _profileRepo.GetProfile(listing.CarDefinitionId);
                if (profile == null)
                {
                    _logger.Warning("No car profile for listing {ListingId}", listing.Id);
                    continue;
                }

                Stock.Add(new UsedCarListingViewModel
                {
                    Listing = listing,
                    CarDefinition = carDef,
                    CarProfile = profile,
                    DealerName = DealerName,
                    CanAfford = _gameState.Player.Money >= listing.Price
                });
            }

            // Bays are worked out from the room and the number of cars, so a lot always fits where it stands
            var showroom = _dealerCatalog.GetShowroom(_dealer?.ShowroomId ?? string.Empty);
            var bays = LotLayout.Build(Stock.Count, showroom);
            LotRadius = LotLayout.CameraRadiusFor(bays, showroom);

            var placements = new List<LotCarPlacement>();
            for (var i = 0; i < Stock.Count && i < bays.Count; i++)
            {
                var car = Stock[i];
                placements.Add(new LotCarPlacement(
                    Path.Combine(AppSettings.Instance.CarsPath, car.CarDefinition.Id),
                    string.IsNullOrWhiteSpace(car.Listing.SkinId) ? null : car.Listing.SkinId,
                    bays[i].X,
                    bays[i].Z,
                    bays[i].Heading,
                    car.DisplayName));
            }

            LotCars = placements;
            OnPropertyChanged(nameof(LotRadius));
            OnPropertyChanged(nameof(LotCars));
            OnPropertyChanged(nameof(Stock));
            OnPropertyChanged(nameof(StockSummary));

            _logger.Information("{Dealer}: {Stock} cars in stock, {Parked} of them parked",
                DealerName, Stock.Count, placements.Count);
        }

        /// <summary>What is on the lot, and what is not out front</summary>
        public string StockSummary
        {
            get
            {
                if (Stock.Count == 0) return "Nothing on the lot today";
                return LotCars.Count < Stock.Count
                    ? $"{LotCars.Count} out front, {Stock.Count - LotCars.Count} more round the back"
                    : $"{Stock.Count} on the lot";
            }
        }

        private bool CanPurchaseCar(UsedCarListingViewModel? car) =>
            car != null && _gameState.Player.Money >= car.Listing.Price;

        private void OnPurchaseCar(UsedCarListingViewModel? car)
        {
            if (car == null) return;

            _purchaseFlow.Offer(_gameState, car, result =>
            {
                if (!result.Succeeded)
                {
                    if (result.Outcome == PurchaseOutcome.NoLongerAvailable) Reload();
                    return;
                }

                // The car is bought and it is in the garage; that is where the player wants to be, not stood on
                // a lot looking at the gap where it was.
                OnPropertyChanged(nameof(BankrollDisplay));
                _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
            });
        }

        /// <summary>
        /// Puts the lot back the way the market says it is. Used when a car turned out to be gone: back to
        /// the whole-lot view first, because the car the camera was on is no longer there.
        /// </summary>
        private void Reload()
        {
            SelectedIndex = -1;
            LoadStock();
            OnPropertyChanged(nameof(BankrollDisplay));
        }

        private void OnBack()
        {
            _navigationService.NavigateToDealerMap(_gameState);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered {Dealer}", DealerName);
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Left {Dealer}", DealerName);
        }
    }
}
