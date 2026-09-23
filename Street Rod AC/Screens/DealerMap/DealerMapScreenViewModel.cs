using System.Collections.ObjectModel;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Services.Dealers;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.DealerMap
{
    /// <summary>
    /// The city with the dealers on it. Picking one goes to its lot.
    /// </summary>
    public class DealerMapScreenViewModel : BaseScreenViewModel
    {
        /// <summary>
        /// The part of los_angeles_map.jpg that is shown, as fractions of the whole image.
        ///
        /// The map is the Auto Club's "Los Angeles and Vicinity", which reaches out to Victorville and
        /// Joshua Tree - most of it desert with nothing to sell a car in. This is the inhabited corner: the
        /// valley and the coast across to Riverside. It stops just above the map's own title cartouche,
        /// because a sliver of it in the corner reads as a mistake rather than a flourish. Dealer positions
        /// are stored against the whole image, so this can be moved without touching the data.
        /// </summary>
        private const double CropX = 0.05;
        private const double CropY = 0.28;
        private const double CropWidth = 0.62;
        private const double CropHeight = 0.55;

        /// <summary>The map image as shipped, in pixels, so the crop can keep its shape on screen</summary>
        private const double MapPixelWidth = 2444;
        private const double MapPixelHeight = 1560;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly IUsedCarMarketService _marketService;
        private readonly IAppLogger _logger;

        public RelayCommand BackCommand { get; }
        public RelayCommand<DealerPinViewModel> VisitDealerCommand { get; }

        public ObservableCollection<DealerPinViewModel> Pins { get; } = [];

        /// <summary>The window onto the map image, for the view to crop with</summary>
        public System.Windows.Rect MapViewbox { get; } = new(CropX, CropY, CropWidth, CropHeight);

        /// <summary>
        /// The shape of that window, width over height. The map is shown at this shape whatever the window
        /// does: stretching it to fill would crop it by an unknown amount and take the pins off their places.
        /// </summary>
        public double MapAspect { get; } =
            (CropWidth * MapPixelWidth) / (CropHeight * MapPixelHeight);

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        /// <summary>The day, so a drive that costs half of it reads as a cost</summary>
        public string DateDisplay => _gameState.Date.ToString("dddd, MMMM d, yyyy");
        public string TimeDisplay => _gameState.Date.ToString("h:mm tt");

        private bool _isLoading;

        /// <summary>The market has never been filled and is being put together</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        /// <summary>No dealer definitions: the map has nothing to put on itself</summary>
        public bool HasDealers => Pins.Count > 0;

        public DealerMapScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IDealerCatalog dealerCatalog,
            IUsedCarMarketService marketService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _dealerCatalog = dealerCatalog;
            _marketService = marketService;
            _logger = AppLoggerFactory.CreateLogger("DealerMap");

            BackCommand = new RelayCommand(OnBack);
            VisitDealerCommand = new RelayCommand<DealerPinViewModel>(OnVisitDealer);

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            // The dealers the save carries are whatever the file says today: a save made before a dealer
            // existed still gets it, and one dropped from the file goes away
            var known = _dealerCatalog.ToLocations();
            if (known.Count > 0)
            {
                _gameState.DealerLocations = known;
            }
            else if (_gameState.DealerLocations == null || _gameState.DealerLocations.Count == 0)
            {
                _gameState.DealerLocations = _marketService.GetDefaultDealers();
            }

            if (_gameState.UsedCarMarket == null || _gameState.UsedCarMarket.Count == 0 || IsMarketStale())
            {
                IsLoading = true;
                try
                {
                    _gameState.UsedCarMarket = await _marketService.SpawnListingsAsync(
                        _gameState.DealerLocations, _gameState.Date);
                    _logger.Information("Spawned {Count} listings for the map", _gameState.UsedCarMarket.Count);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not spawn the market");
                }
                finally
                {
                    IsLoading = false;
                }
            }

            BuildPins();
        }

        /// <summary>
        /// Whether the market on the save belongs to a different set of dealers than the one in front of us.
        /// A save made before a dealer existed has nothing on its lot, and one made when lots were stocked
        /// differently can be carrying far more cars than a lot has room to stand. Either way the cars for
        /// sale are worth nothing to anybody, so they are thrown out and drawn again.
        /// </summary>
        private bool IsMarketStale()
        {
            var listings = _gameState.UsedCarMarket;
            if (listings == null || listings.Count == 0) return false;

            foreach (var dealer in _dealerCatalog.All)
            {
                var onTheLot = listings.Count(l => !l.IsSold && l.DealerLocation == dealer.Id);

                if (onTheLot == 0)
                {
                    _logger.Information("{Dealer} has nothing for sale; restocking the whole market", dealer.Name);
                    return true;
                }

                if (onTheLot > dealer.StockHigh * 2)
                {
                    _logger.Information("{Dealer} is carrying {Count} cars against a lot that holds {Room}; " +
                        "restocking the whole market", dealer.Name, onTheLot, dealer.StockHigh);
                    return true;
                }
            }

            return false;
        }

        private void BuildPins()
        {
            Pins.Clear();

            var remainingToday = ((App)System.Windows.Application.Current)
                .GameTimeService.GetRemainingMinutesToday(_gameState);

            foreach (var dealer in _dealerCatalog.All)
            {
                var stock = _marketService.GetListingsByDealer(_gameState.UsedCarMarket ?? [], dealer.Id);

                Pins.Add(new DealerPinViewModel
                {
                    Id = dealer.Id,
                    Name = dealer.Name,
                    Region = dealer.Region,
                    Blurb = dealer.Blurb,

                    // From a place on the whole image to a place on the part of it being shown
                    X = (dealer.MapX - CropX) / CropWidth,
                    Y = (dealer.MapY - CropY) / CropHeight,

                    StockCount = stock.Count,
                    CheapestPrice = stock.Count > 0 ? stock.Min(l => l.Price) : 0m,
                    DearestPrice = stock.Count > 0 ? stock.Max(l => l.Price) : 0m,
                    TravelHours = dealer.TravelHours,
                    MinutesLeftToday = remainingToday
                });
            }

            OnPropertyChanged(nameof(HasDealers));
            _logger.Information("Map showing {Count} dealers", Pins.Count);
        }

        private void OnVisitDealer(DealerPinViewModel? pin)
        {
            if (pin == null) return;

            // A drive that does not fit in what is left of the day costs the rest of it: the player gets
            // there, looks round, and comes back tomorrow morning. That is a fair trade for a long trip to
            // a lot with something good on it, but it is not one to make by accident.
            if (!pin.FitsToday)
            {
                var message = $"{pin.Name} is {pin.TravelDisplay.ToLowerInvariant()}, and it is " +
                              $"{_gameState.Date:h:mm tt}." + Environment.NewLine + Environment.NewLine +
                              "You would not be back before the day is out. Drive over anyway?";

                _dialogService.ShowDialog(new ConfirmationDialogViewModel(
                    _dialogService,
                    message,
                    "A long way for a look",
                    confirmed =>
                    {
                        if (confirmed) DriveTo(pin);
                    }));
                return;
            }

            DriveTo(pin);
        }

        private async void DriveTo(DealerPinViewModel pin)
        {
            _logger.Information("Driving out to {Dealer}: {Minutes} minutes there and back",
                pin.Name, pin.TravelMinutes);

            try
            {
                // The hours go before the lot is shown, so the clock on the way in is the time you arrived
                await ((App)System.Windows.Application.Current).SpendTimeAsync(pin.TravelMinutes);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for the drive to {Dealer}", pin.Name);
            }

            _navigationService.NavigateToDealerLot(_gameState, pin.Id);
        }

        private void OnBack()
        {
            _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered the dealer map");
        }
    }

    /// <summary>One dealer's pin on the map</summary>
    public class DealerPinViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Blurb { get; set; } = string.Empty;

        /// <summary>Where the pin sits on what the screen shows, 0..1</summary>
        public double X { get; set; }
        public double Y { get; set; }

        public int StockCount { get; set; }
        public decimal CheapestPrice { get; set; }
        public decimal DearestPrice { get; set; }
        public float TravelHours { get; set; }

        /// <summary>What is left of the working day when the map was drawn</summary>
        public int MinutesLeftToday { get; set; }

        /// <summary>The drive out and back, in minutes of game time</summary>
        public int TravelMinutes => Math.Max(15, (int)Math.Round(TravelHours * 60f));

        /// <summary>Whether there is enough of the day left to go and come back</summary>
        public bool FitsToday => TravelMinutes <= MinutesLeftToday;

        /// <summary>What the trip does to the day, for the card</summary>
        public string DayCostDisplay => FitsToday
            ? "You would be back before the day is out"
            : "You would not be back today";

        public string StockDisplay => StockCount == 1 ? "1 car" : $"{StockCount} cars";

        public string PriceDisplay => StockCount == 0
            ? "Nothing on the lot"
            : $"${CheapestPrice:N0} - ${DearestPrice:N0}";

        public string TravelDisplay => TravelHours <= 1f
            ? "About an hour away"
            : $"About {TravelHours:0.#} hours away";
    }
}
