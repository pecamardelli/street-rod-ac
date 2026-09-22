using System.Collections.ObjectModel;
using Street_Rod_AC.Dialogs;
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
        /// The part of los_angeles_map.jpg that is shown, as fractions of the whole image: the basin from the
        /// valley down to the harbour. Dealer positions are stored against the whole image, so this can be
        /// moved without touching the data.
        /// </summary>
        private const double CropX = 0.28;
        private const double CropY = 0.12;
        private const double CropWidth = 0.72;
        private const double CropHeight = 0.60;

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

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

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

            if (_gameState.UsedCarMarket == null || _gameState.UsedCarMarket.Count == 0)
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

        private void BuildPins()
        {
            Pins.Clear();

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
                    TravelHours = dealer.TravelHours
                });
            }

            OnPropertyChanged(nameof(HasDealers));
            _logger.Information("Map showing {Count} dealers", Pins.Count);
        }

        private void OnVisitDealer(DealerPinViewModel? pin)
        {
            if (pin == null) return;

            _logger.Information("Driving out to {Dealer}", pin.Name);
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

        public string StockDisplay => StockCount == 1 ? "1 car" : $"{StockCount} cars";

        public string PriceDisplay => StockCount == 0
            ? "Nothing on the lot"
            : $"${CheapestPrice:N0} - ${DearestPrice:N0}";

        public string TravelDisplay => TravelHours <= 1f
            ? "About an hour away"
            : $"About {TravelHours:0.#} hours away";
    }
}
