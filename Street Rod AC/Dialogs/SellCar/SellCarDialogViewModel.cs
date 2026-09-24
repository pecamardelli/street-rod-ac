using System.Globalization;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.SellCar
{
    /// <summary>
    /// Selling one car from the garage: to the trade-in dealer (or the scrapyard, for a wreck) on the spot, or
    /// through an ad in the paper, where buyers' offers turn up over the days. Selling to the dealer takes a second
    /// click: a car sold is gone. The money, the clock and the save are the sale service's; the garage hears of a
    /// sale through <paramref name="sold"/>.
    /// </summary>
    public class SellCarDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly Car _car;
        private readonly ICarSaleService _saleService;
        private readonly Action _sold;
        private readonly Action _changed;
        private DealerQuote _quote;
        private bool _busy;

        /// <param name="sold">The car has left the garage</param>
        /// <param name="changed">The money or the clock moved (an ad was paid for)</param>
        public SellCarDialogViewModel(DialogService dialogService, GameState gameState, Car car, string carName,
            ICarSaleService saleService, Action sold, Action changed)
        {
            _dialogService = dialogService;
            _gameState = gameState;
            _car = car;
            _saleService = saleService;
            _sold = sold;
            _changed = changed;
            CarName = carName;

            _quote = saleService.QuoteDealer(gameState, car);
            _askingPriceText = saleService.SuggestedAskingPrice(car).ToString("0", CultureInfo.CurrentCulture);

            SellToDealerCommand = new AsyncRelayCommand(OnSellToDealer, () => !_busy);
            PlaceAdCommand = new AsyncRelayCommand(OnPlaceAd, () => !_busy && AskingPrice is not null && !HasAd && !_quote.IsScrap);
            AcceptOfferCommand = new AsyncRelayCommand(OnAcceptOffer, () => !_busy && HasOffer);
            DeclineOfferCommand = new RelayCommand(OnDeclineOffer, () => !_busy && HasOffer);
            WithdrawAdCommand = new RelayCommand(OnWithdrawAd, () => !_busy && HasAd);
            CloseCommand = new RelayCommand(() => _dialogService.CloseDialog(), () => !_busy);
        }

        public string CarName { get; }

        public string ValueDisplay => $"Worth about ${_quote.Value:N0}";

        public string MoneyDisplay => $"${_gameState.Player.Money:N0}";

        // ----- the dealer, or the scrapyard -----

        public string DealerTitle => _quote.IsScrap ? "The scrapyard" : $"Trade it in at {_quote.BuyerName}";

        public string DealerDetail => _quote.IsScrap
            ? "The car is totaled: only the scrapyard wants it, for what is left of it."
            : $"Cash on the spot, {(int)(CarSaleService.DealerShare * 100)}% of what it's worth. The car goes on their lot.";

        public string DealerAmountDisplay => $"${_quote.Amount:N0}";

        private bool _confirmingSale;

        /// <summary>The first click asks, the second sells</summary>
        public bool ConfirmingSale
        {
            get => _confirmingSale;
            private set
            {
                if (SetProperty(ref _confirmingSale, value)) OnPropertyChanged(nameof(SellButtonText));
            }
        }

        public string SellButtonText => ConfirmingSale ? "Sure? Sell it" : _quote.IsScrap ? "Scrap it" : "Sell";

        public AsyncRelayCommand SellToDealerCommand { get; }

        // ----- the paper -----

        public bool CanAdvertise => !_quote.IsScrap;

        public string AdFeeDisplay => $"The ad costs ${CarSaleService.AdFee:N0} and runs {CarSaleService.AdDays} days.";

        private CarSaleAd? Ad => _saleService.AdFor(_gameState, _car);

        public bool HasAd => Ad != null;

        public bool ShowAdForm => CanAdvertise && !HasAd;

        public string AdStatus => Ad is { } ad
            ? $"In the paper at ${ad.AskingPrice:N0} since {ad.PostedDate:MMM d}, until {ad.PostedDate.AddDays(CarSaleService.AdDays):MMM d}."
            : string.Empty;

        public bool HasOffer => Ad?.Offer is { } offer && offer.Expires >= _gameState.Date;

        public string OfferDisplay => Ad?.Offer is { } offer && HasOffer
            ? $"{CarSaleService.Capitalized(offer.BuyerName)} offers ${offer.Amount:N0}, until {offer.Expires:ddd h tt}."
            : "No buyer has called yet.";

        private string _askingPriceText;

        public string AskingPriceText
        {
            get => _askingPriceText;
            set
            {
                if (SetProperty(ref _askingPriceText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AskingPriceHint));
                    PlaceAdCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>The asking price typed, when it is a whole-dollar amount in the range; null otherwise</summary>
        private decimal? AskingPrice
        {
            get
            {
                var text = AskingPriceText.Trim().TrimStart('$');
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)) return null;
                var (min, max) = _saleService.AskingPriceRange(_car);
                return price >= min && price <= max ? Math.Round(price) : null;
            }
        }

        public string AskingPriceHint
        {
            get
            {
                var (min, max) = _saleService.AskingPriceRange(_car);
                return AskingPrice is null
                    ? $"Ask between ${min:N0} and ${max:N0}."
                    : "Ask more and fewer buyers call, and those who do haggle.";
            }
        }

        public AsyncRelayCommand PlaceAdCommand { get; }
        public AsyncRelayCommand AcceptOfferCommand { get; }
        public RelayCommand DeclineOfferCommand { get; }
        public RelayCommand WithdrawAdCommand { get; }
        public RelayCommand CloseCommand { get; }

        private string _status = string.Empty;

        /// <summary>What just happened, or why it didn't</summary>
        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        private async Task OnSellToDealer()
        {
            if (!ConfirmingSale)
            {
                ConfirmingSale = true;
                return;
            }

            await Run(() => _saleService.SellToDealerAsync(_gameState, _car));
        }

        private async Task OnPlaceAd()
        {
            if (AskingPrice is not { } price) return;
            await Run(() => _saleService.PlaceAdAsync(_gameState, _car, price));
        }

        private Task OnAcceptOffer() => Ad is { } ad ? Run(() => _saleService.AcceptOfferAsync(_gameState, ad)) : Task.CompletedTask;

        private void OnDeclineOffer()
        {
            if (Ad is { } ad) Show(_saleService.DeclineOffer(_gameState, ad));
        }

        private void OnWithdrawAd()
        {
            if (Ad is { } ad) Show(_saleService.WithdrawAd(_gameState, ad));
        }

        private async Task Run(Func<Task<SaleResult>> action)
        {
            if (_busy) return;

            _busy = true;
            RaiseAll();
            try
            {
                Show(await action());
            }
            finally
            {
                _busy = false;
                RaiseAll();
            }
        }

        private void Show(SaleResult result)
        {
            var sold = result.Succeeded && !_gameState.Player.Cars.Contains(_car);
            var message = result.SaveFailed ? result.Message + "\n\nThe game could not be saved: the details are in the log." : result.Message;

            if (sold)
            {
                _dialogService.CloseDialog();
                _sold();
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, "Car Sold"));
                return;
            }

            Status = message;
            ConfirmingSale = false;
            _quote = _saleService.QuoteDealer(_gameState, _car);
            _changed();
            OnPropertyChanged(string.Empty);
        }

        private void RaiseAll()
        {
            SellToDealerCommand.RaiseCanExecuteChanged();
            PlaceAdCommand.RaiseCanExecuteChanged();
            AcceptOfferCommand.RaiseCanExecuteChanged();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
