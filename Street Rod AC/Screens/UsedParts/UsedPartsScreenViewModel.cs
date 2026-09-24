using System.Collections.ObjectModel;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Navigation;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.UsedParts
{
    /// <summary>What the parts pages show</summary>
    public enum PartsShopTab
    {
        /// <summary>Used parts from the ads</summary>
        Used,

        /// <summary>New parts by mail order</summary>
        New,

        /// <summary>The player's own shelf, to sell from</summary>
        Sell
    }

    /// <summary>
    /// The parts pages of the newspaper: used parts from the ads, new ones by mail order, and selling what is
    /// on the shelf. Whatever is bought goes to the shelf in the garage.
    /// </summary>
    public class UsedPartsScreenViewModel : BaseScreenViewModel
    {
        public const string AllGroups = "All parts";

        // A full page of rows, not the catalog's two thousand at once
        private const int MaxRows = 250;

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly ICarPartsService _partsService;
        private readonly IPartsShopService _shopService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IGameTimeService _timeService;
        private readonly IAppLogger _logger;

        private HashSet<string> _fitting = new(StringComparer.OrdinalIgnoreCase);

        public RelayCommand BackCommand { get; }
        public RelayCommand<string> ShowTabCommand { get; }
        public RelayCommand<PartOfferViewModel> BuyCommand { get; }

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";

        public ObservableCollection<PartOfferViewModel> Offers { get; } = new();
        public ObservableCollection<string> Groups { get; } = new();

        public UsedPartsScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            ICarPartsService partsService,
            IPartsShopService shopService,
            IGameStateRepository gameStateRepo,
            IContentCatalogRepository catalogRepo,
            IGameTimeService timeService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _partsService = partsService;
            _shopService = shopService;
            _gameStateRepo = gameStateRepo;
            _catalogRepo = catalogRepo;
            _timeService = timeService;
            _logger = AppLoggerFactory.CreateLogger("UsedParts");

            BackCommand = new RelayCommand(OnBack);
            ShowTabCommand = new RelayCommand<string>(tab => Tab = Enum.Parse<PartsShopTab>(tab ?? nameof(PartsShopTab.Used)));
            BuyCommand = new RelayCommand<PartOfferViewModel>(OnOfferAction);
        }

        #region Filters

        private PartsShopTab _tab = PartsShopTab.Used;

        public PartsShopTab Tab
        {
            get => _tab;
            set
            {
                if (!SetProperty(ref _tab, value)) return;

                // Every page starts out unfiltered: a kind picked among the new parts would hide half the shelf
                _selectedGroup = AllGroups;
                _searchText = string.Empty;
                OnPropertyChanged(nameof(SelectedGroup));
                OnPropertyChanged(nameof(SearchText));
                OnPropertyChanged(nameof(IsUsedTab));
                OnPropertyChanged(nameof(IsNewTab));
                OnPropertyChanged(nameof(IsSellTab));
                OnPropertyChanged(nameof(CanFilterByFit));
                LoadOffers();
            }
        }

        public bool IsUsedTab => _tab == PartsShopTab.Used;
        public bool IsNewTab => _tab == PartsShopTab.New;
        public bool IsSellTab => _tab == PartsShopTab.Sell;

        private string _selectedGroup = AllGroups;

        public string SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value ?? AllGroups)) LoadOffers();
            }
        }

        private string _searchText = string.Empty;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) LoadOffers();
            }
        }

        private bool _onlyFitting = true;

        /// <summary>Show only what goes somewhere on the car that is selected in the garage</summary>
        public bool OnlyFitting
        {
            get => _onlyFitting;
            set
            {
                if (SetProperty(ref _onlyFitting, value)) LoadOffers();
            }
        }

        public bool CanFilterByFit => SelectedCar != null && !IsSellTab;

        public string FitFilterLabel => SelectedCar == null ? "Only what fits my car" : $"Only what fits my {CarName}";

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool IsAvailable => _shopService.IsAvailable;

        #endregion

        private Car? SelectedCar =>
            _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);

        private string CarName
        {
            get
            {
                var definition = SelectedCar == null ? null : _catalogRepo.GetCar(SelectedCar.DefinitionId);
                return definition?.Name ?? "car";
            }
        }

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered used parts screen");

            try
            {
                // Catalog, first ads and the fit check take a moment the first time: not on the UI thread.
                // The game state itself is only changed on it.
                var car = SelectedCar;
                if (await Task.Run(() => _shopService.IsAvailable))
                {
                    if (car != null) await _partsService.EnsurePartsAsync(car);
                    if (_gameState.NewspaperAds.Parts.Count == 0) await _shopService.RefreshAdsAsync(_gameState, _gameState.Date);
                    _fitting = await Task.Run(() => _shopService.FindFittingParts(car));
                }

                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(CanFilterByFit));
                OnPropertyChanged(nameof(FitFilterLabel));

                Groups.Clear();
                Groups.Add(AllGroups);
                foreach (var group in _shopService.Assortment.Select(PartKinds.GroupOf).Distinct().OrderBy(g => g)) Groups.Add(group);

                LoadOffers();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the parts pages");
                StatusText = "The parts pages could not be opened.";
            }

            try
            {
                // Spend time for visiting the used parts shop (30 min)
                var spent = await _timeService.SpendTimeAsync(_gameState, GameAction.VisitUsedParts);

                // Late in the evening that is the next morning, with another paper
                if (spent.NewDayStarted) LoadOffers();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for the parts pages");
            }
        }

        private void LoadOffers()
        {
            Offers.Clear();
            if (!_shopService.IsAvailable)
            {
                StatusText = "No parts catalog is installed.";
                return;
            }

            var catalog = _partsService.Catalog;
            IEnumerable<PartOfferViewModel> offers = _tab switch
            {
                PartsShopTab.Used => _gameState.NewspaperAds.Parts
                    .Where(ad => catalog.Get(ad.Part.DefinitionId) != null)
                    .Select(ad => PartOfferViewModel.ForAd(ad, catalog.Get(ad.Part.DefinitionId)!)),
                PartsShopTab.New => _shopService.Assortment.Select(p => PartOfferViewModel.ForNew(p, _shopService.NewPrice(p))),
                _ => _gameState.Player.Parts
                    .Where(p => catalog.Get(p.DefinitionId) != null)
                    .Select(p => PartOfferViewModel.ForSale(p, catalog.Get(p.DefinitionId)!, _shopService.TradeInPrice(p)))
            };

            if (_selectedGroup != AllGroups) offers = offers.Where(o => o.Group == _selectedGroup);
            if (_searchText.Length > 0) offers = offers.Where(o => o.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            if (_onlyFitting && CanFilterByFit) offers = offers.Where(o => _fitting.Contains(o.Definition.Id));

            var matching = offers.ToList();
            foreach (var offer in matching.Take(MaxRows))
            {
                offer.Fits = _fitting.Contains(offer.Definition.Id);
                offer.CanAfford = IsSellTab || offer.Price <= _gameState.Player.Money;
                Offers.Add(offer);
            }

            StatusText = matching.Count switch
            {
                0 when IsSellTab => "Your shelf is empty.",
                0 => "Nothing like that on offer.",
                > MaxRows => $"Showing {MaxRows} of {matching.Count}: narrow it down by kind or name.",
                _ => $"{matching.Count} part{(matching.Count == 1 ? "" : "s")}"
            };
        }

        private void OnOfferAction(PartOfferViewModel? offer)
        {
            if (offer == null) return;

            var done = offer switch
            {
                { Ad: { } ad } => _shopService.BuyUsed(_gameState, ad),
                { Owned: { } owned } => _shopService.Sell(_gameState, owned),
                _ => _shopService.BuyNew(_gameState, offer.Definition)
            };

            if (!done)
            {
                var (message, title) = offer switch
                {
                    { Owned: not null } => ("That part is no longer on your shelf.", "Not Sold"),
                    { Ad: { } ad } when !_gameState.NewspaperAds.Parts.Contains(ad) => ("Somebody was quicker: that ad is no longer in the paper.", "Already Sold"),
                    _ => ("You don't have enough money for this part.", "Insufficient Funds")
                };
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));

                // Whatever went wrong, the rows are out of date
                OnPropertyChanged(nameof(BankrollDisplay));
                LoadOffers();
                return;
            }

            try
            {
                _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save game state after a parts deal");
            }

            OnPropertyChanged(nameof(BankrollDisplay));
            LoadOffers();
        }

        private void OnBack()
        {
            _logger.Information("Navigating back to newspaper");
            _navigationService.NavigateToNewspaper(_gameState, skipAnimation: true);
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Exited used parts screen");
        }
    }

    /// <summary>One row of the parts pages: a used part from an ad, a new part, or one of the player's own</summary>
    public class PartOfferViewModel
    {
        private PartOfferViewModel(PartDefinition definition, decimal price, string action)
        {
            Definition = definition;
            Price = price;
            ActionLabel = action;
            Name = definition.DisplayName ?? definition.Name;
            Group = PartKinds.GroupOf(definition);
        }

        public PartDefinition Definition { get; }
        public PartAd? Ad { get; private init; }
        public PartInstance? Owned { get; private init; }

        public string Name { get; }
        public string Group { get; }
        public decimal Price { get; }
        public string PriceDisplay => $"${Price:N0}";
        public string ActionLabel { get; }

        /// <summary>Condition, seller, what comes with it</summary>
        public string Detail { get; private init; } = "New";

        public bool Fits { get; set; }
        public bool CanAfford { get; set; }

        public static PartOfferViewModel ForNew(PartDefinition definition, decimal price) => new(definition, price, "Order");

        public static PartOfferViewModel ForAd(PartAd ad, PartDefinition definition) =>
            new(definition, ad.AskingPrice, "Buy")
            {
                Ad = ad,
                Detail = $"{PartTrees.Describe(ad.Part)}  ·  {ad.SellerName}"
            };

        public static PartOfferViewModel ForSale(PartInstance part, PartDefinition definition, decimal price) =>
            new(definition, price, "Sell")
            {
                Owned = part,
                Detail = PartTrees.Describe(part)
            };
    }
}
