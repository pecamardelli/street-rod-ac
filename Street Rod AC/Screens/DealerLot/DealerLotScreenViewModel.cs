using System.Collections.ObjectModel;
using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Controls;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Screens.UsedCarMarket;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Dealers;
using Street_Rod_AC.Services.Market;
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
        private readonly ICarPurchaseService _purchaseService;
        private readonly IAppLogger _logger;
        private readonly DealerDefinition? _dealer;

        public RelayCommand BackCommand { get; }
        public RelayCommand BackToLotCommand { get; }
        public RelayCommand<UsedCarListingViewModel> SelectCarCommand { get; }
        public RelayCommand<UsedCarListingViewModel> PurchaseCarCommand { get; }

        /// <summary>Everything this dealer has, whether or not it fitted on the lot</summary>
        public ObservableCollection<UsedCarListingViewModel> Stock { get; } = [];

        /// <summary>The cars the 3D lot should stand up, in the same order as the first of <see cref="Stock"/></summary>
        public IReadOnlyList<LotCarPlacement> LotCars { get; private set; } = [];

        public string DealerName => _dealer?.Name ?? "Dealer";
        public string DealerRegion => _dealer?.Region ?? string.Empty;
        public string DealerBlurb => _dealer?.Blurb ?? string.Empty;
        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

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
            ICarPurchaseService purchaseService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _dealerCatalog = dealerCatalog;
            _marketService = marketService;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _gameStateRepo = gameStateRepo;
            _purchaseService = purchaseService;
            _logger = AppLoggerFactory.CreateLogger("DealerLot");
            _dealer = dealerCatalog.Get(dealerId);

            BackCommand = new RelayCommand(OnBack);
            BackToLotCommand = new RelayCommand(() => SelectedIndex = -1);
            SelectCarCommand = new RelayCommand<UsedCarListingViewModel>(OnSelectCar);
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

        private int _parkedCount;

        /// <summary>
        /// How many cars the viewport actually stood up, which it reports back. Not the same as the number of
        /// bays: a lot of very heavy models runs out of room before it runs out of bays.
        /// </summary>
        public int ParkedCount
        {
            get => _parkedCount;
            set
            {
                if (_parkedCount == value) return;
                _parkedCount = value;
                OnPropertyChanged(nameof(ParkedCount));
                OnPropertyChanged(nameof(StockSummary));
            }
        }

        /// <summary>What is on the lot, and what is not out front</summary>
        public string StockSummary
        {
            get
            {
                if (Stock.Count == 0) return "Nothing on the lot today";
                if (ParkedCount <= 0) return $"{Stock.Count} in stock";
                return Stock.Count > ParkedCount
                    ? $"{ParkedCount} out front, {Stock.Count - ParkedCount} more round the back"
                    : $"{Stock.Count} on the lot";
            }
        }

        private void OnSelectCar(UsedCarListingViewModel? car)
        {
            if (car == null) return;

            var index = Stock.IndexOf(car);
            if (index < 0) return;

            // Any car in stock can be read about. Only the ones out front have somewhere for the camera to
            // stand: past that the viewport sees an index it has no car for and stays on the whole lot.
            SelectedIndex = index;
        }

        private bool CanPurchaseCar(UsedCarListingViewModel? car) =>
            car != null && _gameState.Player.Money >= car.Listing.Price;

        private void OnPurchaseCar(UsedCarListingViewModel? car)
        {
            if (car == null) return;

            var def = car.CarDefinition;
            var message = $"Purchase this {def.Brand} {def.Name}?\n\n" +
                          $"Year: {def.Year ?? 0}\n" +
                          $"Price: ${car.Listing.Price:N0}\n" +
                          $"Condition: {ConditionLabel(car.Listing.Condition)}\n" +
                          $"Mileage: {car.Listing.Mileage:N0} km\n" +
                          $"Dealer: {DealerName}\n\n" +
                          $"Your bankroll: ${_gameState.Player.Money:N0}";

            var confirm = new ConfirmationDialogViewModel(
                _dialogService,
                message,
                "Purchase Car?",
                confirmed =>
                {
                    if (confirmed) CompletePurchase(car);
                });

            _dialogService.ShowDialog(confirm);
        }

        private async void CompletePurchase(UsedCarListingViewModel car)
        {
            var result = await _purchaseService.PurchaseAsync(_gameState, car.Listing, car.CarDefinition);

            if (!result.Succeeded)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    result.Message,
                    result.Outcome == PurchaseOutcome.NotEnoughMoney ? "Insufficient Funds" : "Car Unavailable"));

                if (result.Outcome == PurchaseOutcome.NoLongerAvailable) Reload();
                return;
            }

            if (result.SaveFailed)
            {
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    "The purchase was successful but failed to save the game. Please save manually.",
                    "Save Warning"));
            }

            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService, result.Message, "Purchase Successful"));

            Reload();
        }

        /// <summary>
        /// The car that just sold has to come off the lot, which means standing the scene back up. Back to the
        /// whole-lot view first: the car the camera was on is gone.
        /// </summary>
        private void Reload()
        {
            SelectedIndex = -1;
            LoadStock();
            OnPropertyChanged(nameof(BankrollDisplay));
        }

        private static string ConditionLabel(float condition)
        {
            if (condition >= 0.9f) return "Excellent";
            if (condition >= 0.75f) return "Good";
            if (condition >= 0.6f) return "Fair";
            if (condition >= 0.4f) return "Poor";
            return "Very Poor";
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
