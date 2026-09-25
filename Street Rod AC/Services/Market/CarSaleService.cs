using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>What came of trying to sell a car, or to put it in the paper</summary>
    public enum SaleOutcome
    {
        Done,

        /// <summary>The car isn't the player's (any more)</summary>
        NotOwned,

        /// <summary>The car is out racing: it can't change hands until the race is settled</summary>
        Racing,

        /// <summary>The ad has no buyer on the phone, or has left the paper</summary>
        NoOffer,

        NotEnoughMoney,

        /// <summary>Nobody reading the paper wants it (a wreck), or the price makes no sense</summary>
        Refused
    }

    public readonly record struct SaleResult(SaleOutcome Outcome, string Message, bool SaveFailed = false)
    {
        public bool Succeeded => Outcome == SaleOutcome.Done;
    }

    /// <summary>What a dealer pays for a car today, or the scrapyard for a wreck</summary>
    /// <param name="Value">What the car is worth (<see cref="CarValuation"/>)</param>
    /// <param name="BuyerName">The dealer, or the scrapyard</param>
    /// <param name="LocationId">The lot the car goes on; null when it goes for scrap</param>
    public sealed record DealerQuote(decimal Value, decimal Amount, bool IsScrap, string BuyerName, string? LocationId);

    /// <summary>
    /// Selling the player's cars. A dealer buys at once, for <see cref="DealerShare"/> of what the car is worth, and
    /// puts it on its lot at the full price. A totaled car only goes to the scrapyard, for
    /// <see cref="ScrapShare"/> of its worth give or take <see cref="ScrapSpread"/> (as in the GameMaker version).
    /// Or the player puts the car in the paper at a price of their own: buyers call over the days, fewer and
    /// haggling harder the more is asked over what the car is worth (<see cref="OfferChance"/>). An offer holds
    /// for <see cref="OfferDays"/> days. The car stays in the garage and can race until an offer is taken.
    /// </summary>
    public class CarSaleService(
        IUsedCarMarketService market,
        IGameTimeService gameTimeService,
        IGameStateRepository gameStateRepository) : ICarSaleService
    {
        /// <summary>Share of a car's worth a dealer pays for it</summary>
        public const double DealerShare = 0.6;

        /// <summary>Share of a wreck's worth the scrapyard pays, before <see cref="ScrapSpread"/></summary>
        public const double ScrapShare = 0.15;

        /// <summary>How far the scrapyard's price strays either way, as a share of its price</summary>
        public const double ScrapSpread = 0.2;

        /// <summary>What an ad in the classifieds costs; it runs for <see cref="AdDays"/> days</summary>
        public const decimal AdFee = 10m;

        public const int AdDays = 14;

        /// <summary>How long a buyer waits for an answer</summary>
        public const int OfferDays = 2;

        /// <summary>The least a car can be put in the paper for</summary>
        public const decimal MinAskingPrice = 100m;

        /// <summary>How far over its worth a car can be put in the paper: past this nobody would even call</summary>
        public const decimal MaxAskingShare = 3m;

        public const string ScrapyardName = "the scrapyard";

        /// <summary>The chance the buyer who calls is one of the rivals, when one of them wants the car and can pay</summary>
        public const double RivalBuyerChance = 0.4;

        private static readonly string[] Buyers =
        {
            "Gary", "Denise", "Tommy", "Earl", "Linda", "Vince", "Carl", "Donna", "Rick", "Sal", "Marge", "Butch",
            "Norm", "Wanda", "Lenny", "a kid from the high school", "a man from out of town"
        };

        private readonly IUsedCarMarketService _market = market;
        private readonly IGameTimeService _gameTimeService = gameTimeService;
        private readonly IGameStateRepository _gameStateRepository = gameStateRepository;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarSale");

        public decimal ValueOf(Car car) => _market.ValueOf(car);

        public DealerQuote QuoteDealer(GameState gameState, Car car)
        {
            var value = _market.ValueOf(car);
            if (CarCondition.IsTotaled(car))
                return new DealerQuote(value, RoundToTen((double)value * ScrapFactor(car.InstanceId)), true, ScrapyardName, null);

            var location = _market.TradeInLocation(gameState.DealerLocations);
            var name = (gameState.DealerLocations?.FirstOrDefault(d => d.Id == location)
                        ?? _market.GetDefaultDealers().FirstOrDefault(d => d.Id == location))?.Name ?? "the dealer";
            return new DealerQuote(value, RoundToTen((double)value * DealerShare), false, name, location);
        }

        public async Task<SaleResult> SellToDealerAsync(GameState gameState, Car car)
        {
            if (WhyNotSellable(gameState, car) is { } refusal) return refusal;

            var quote = QuoteDealer(gameState, car);
            RemoveFromGarage(gameState, car);
            gameState.Player.Money += quote.Amount;

            // The dealer sells it on at its worth, marked up or down like every car on the lot; the scrapyard breaks it up
            if (quote.LocationId != null)
            {
                gameState.UsedCarMarket.Add(_market.ListCar(car, GameRules.Scale(quote.Value, gameState.Rules.CarPriceMultiplier),
                    quote.LocationId, gameState.Date));
            }

            _logger.Information("Sold {Car} to {Buyer} for ${Amount} (worth ${Value})", car.DefinitionId, quote.BuyerName, quote.Amount, quote.Value);

            var saveFailed = await SpendTimeAndSave(gameState, GameAction.SellCar);
            var message = quote.IsScrap
                ? $"The scrapyard hauled the wreck away and paid ${quote.Amount:N0} for what is left of it."
                : $"{quote.BuyerName} bought the car for ${quote.Amount:N0}.";
            return new SaleResult(SaleOutcome.Done, message, saveFailed);
        }

        public CarSaleAd? AdFor(GameState gameState, Car car) =>
            gameState.NewspaperAds.PlayerCars.FirstOrDefault(ad => ad.CarInstanceId == car.InstanceId);

        public decimal SuggestedAskingPrice(Car car) => Math.Max(MinAskingPrice, RoundToTen((double)_market.ValueOf(car)));

        public (decimal Min, decimal Max) AskingPriceRange(Car car) =>
            (MinAskingPrice, Math.Max(MinAskingPrice, RoundToTen((double)(_market.ValueOf(car) * MaxAskingShare))));

        public async Task<SaleResult> PlaceAdAsync(GameState gameState, Car car, decimal askingPrice)
        {
            if (WhyNotSellable(gameState, car) is { } refusal) return refusal;

            if (CarCondition.IsTotaled(car))
                return new SaleResult(SaleOutcome.Refused, "Nobody buys a wreck out of the paper. The scrapyard will take it.");

            var (min, max) = AskingPriceRange(car);
            if (askingPrice < min || askingPrice > max)
                return new SaleResult(SaleOutcome.Refused, $"Ask between ${min:N0} and ${max:N0}: nobody calls about a car priced like that.");

            if (AdFor(gameState, car) != null)
                return new SaleResult(SaleOutcome.Refused, "The car is in the paper already.");

            if (gameState.Player.Money < AdFee)
                return new SaleResult(SaleOutcome.NotEnoughMoney, $"An ad costs ${AdFee:N0}.");

            gameState.Player.Money -= AdFee;
            gameState.NewspaperAds.PlayerCars.Add(new CarSaleAd
            {
                CarInstanceId = car.InstanceId,
                AskingPrice = Math.Round(askingPrice),
                PostedDate = gameState.Date
            });
            _logger.Information("Put {Car} in the paper for ${Price}", car.DefinitionId, askingPrice);

            var saveFailed = await SpendTimeAndSave(gameState, GameAction.PlaceAd);
            return new SaleResult(SaleOutcome.Done,
                $"The ad runs for {AdDays} days at ${askingPrice:N0}. Buyers who call are in the newspaper, under your ads.", saveFailed);
        }

        public SaleResult WithdrawAd(GameState gameState, CarSaleAd ad)
        {
            if (!gameState.NewspaperAds.PlayerCars.Remove(ad)) return new SaleResult(SaleOutcome.NoOffer, "The ad has run out already.");

            _logger.Information("Took the ad for {Car} out of the paper", ad.CarInstanceId);
            return new SaleResult(SaleOutcome.Done, "The ad is out of the paper.", !Save(gameState));
        }

        public async Task<SaleResult> AcceptOfferAsync(GameState gameState, CarSaleAd ad)
        {
            if (!gameState.NewspaperAds.PlayerCars.Contains(ad) || ad.Offer is not { } offer || offer.Expires < gameState.Date)
                return new SaleResult(SaleOutcome.NoOffer, "That buyer has found another car.");

            var car = gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == ad.CarInstanceId);
            if (car == null)
            {
                gameState.NewspaperAds.PlayerCars.Remove(ad);
                return new SaleResult(SaleOutcome.NotOwned, "The car is no longer yours to sell.", !Save(gameState));
            }

            if (WhyNotSellable(gameState, car) is { } refusal) return refusal;

            // A rival who called has to have the money still; they may have spent it since
            Opponent? rival = null;
            if (offer.RivalName != null)
            {
                rival = gameState.Racers.Find(offer.RivalName) as Opponent;
                if (rival == null || rival.Money < offer.Amount)
                {
                    ad.Offer = null;
                    _logger.Information("{Rival} can no longer pay ${Amount} for {Car}", offer.RivalName, offer.Amount, car.DefinitionId);
                    return new SaleResult(SaleOutcome.NoOffer, $"{offer.RivalName} couldn't raise the money after all.", !Save(gameState));
                }
            }

            RemoveFromGarage(gameState, car);
            gameState.Player.Money += offer.Amount;

            if (rival != null)
            {
                // It races under them now; tomorrow's review decides whether it is the car they drive
                rival.Money -= offer.Amount;
                rival.Cars.Add(car);
                rival.Stats.CarsOwned++;
                OpponentLifeService.AddTalk(gameState, gameState.Date, [$"{rival.Name} bought {gameState.Player.Name}'s car out of the paper."]);
            }

            // Anybody else drives it away: it leaves the game
            _logger.Information("Sold {Car} to {Buyer} out of the paper for ${Amount}", car.DefinitionId, offer.BuyerName, offer.Amount);

            var saveFailed = await SpendTimeAndSave(gameState, GameAction.SellCar);
            return new SaleResult(SaleOutcome.Done, $"{Capitalized(offer.BuyerName)} paid ${offer.Amount:N0} and drove the car away.", saveFailed);
        }

        public SaleResult DeclineOffer(GameState gameState, CarSaleAd ad)
        {
            if (ad.Offer == null) return new SaleResult(SaleOutcome.NoOffer, "Nobody is waiting on an answer.");

            _logger.Information("Turned down {Buyer}'s ${Amount} for {Car}", ad.Offer.BuyerName, ad.Offer.Amount, ad.CarInstanceId);
            ad.Offer = null;
            return new SaleResult(SaleOutcome.Done, "The buyer hung up.", !Save(gameState));
        }

        /// <summary>
        /// A day in the classifieds: ads of cars the player no longer has and ads that ran their time leave the
        /// paper, buyers who got no answer give up, and a new buyer may call about each ad without one.
        /// </summary>
        public void ReviewAds(GameState gameState, DateTime currentDate, Random random)
        {
            var ads = gameState.NewspaperAds.PlayerCars;
            var gone = ads.RemoveAll(ad =>
                gameState.Player.Cars.All(c => c.InstanceId != ad.CarInstanceId) || (currentDate - ad.PostedDate).TotalDays > AdDays);

            foreach (var ad in ads)
            {
                if (ad.Offer is { } old && old.Expires < currentDate) ad.Offer = null;
                if (ad.Offer != null) continue;

                var car = gameState.Player.Cars.First(c => c.InstanceId == ad.CarInstanceId);
                decimal value;
                try
                {
                    value = _market.ValueOf(car);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not value {Car} for its ad: {Error}", car.DefinitionId, ex.Message);
                    continue;
                }

                if (random.NextDouble() >= OfferChance(ad.AskingPrice, value)) continue;

                var amount = OfferAmount(ad.AskingPrice, value, random.NextDouble());
                var rival = RivalBuyer(gameState, car, amount, random);
                ad.Offer = new CarOffer
                {
                    BuyerName = rival?.Name ?? Buyers[random.Next(Buyers.Length)],
                    RivalName = rival?.Name,
                    Amount = amount,
                    Expires = currentDate.Date.AddDays(OfferDays).AddHours(GameState.DayEndHour)
                };
                _logger.Information("{Buyer} offers ${Amount} for {Car} (asking ${Asking}, worth ${Value})",
                    ad.Offer.BuyerName, ad.Offer.Amount, car.DefinitionId, ad.AskingPrice, value);
            }

            if (gone > 0) _logger.Information("{Count} of the player's ads left the paper", gone);
        }

        /// <summary>
        /// A rival who wants the car and can pay for it, now and then (<see cref="RivalBuyerChance"/>): one without a car
        /// that can race, or whose car makes less power than this one, with room in the garage. Null for a buyer
        /// from outside the racing crowd. Rolls the dice only when there is such a rival.
        /// </summary>
        public static Opponent? RivalBuyer(GameState gameState, Car car, decimal amount, Random random)
        {
            var power = Simulation.RaceSimulatorService.GetCarHP(car);
            var wanting = gameState.Racers.ReadyToRace.Values.Concat(gameState.Racers.Retired.Values)
                .OfType<Opponent>()
                .Where(o => !o.IsKing && o.Money >= amount && o.Cars.Count < OpponentRules.MaxCars
                            && (o.Cars.Count == 0 || Simulation.RaceSimulatorService.GetCarHP(o.Cars[0]) < power))
                .OrderBy(o => o.Name, StringComparer.Ordinal)
                .ToList();
            if (wanting.Count == 0 || random.NextDouble() >= RivalBuyerChance) return null;

            return wanting[random.Next(wanting.Count)];
        }

        /// <summary>
        /// The chance a buyer calls on a given day: 60% for a car asked at 90% of its worth or less, and down by 12
        /// points for every tenth more, to nobody at 140%
        /// </summary>
        public static double OfferChance(decimal askingPrice, decimal value)
        {
            if (value <= 0) return 0.1;
            var ratio = (double)(askingPrice / value);
            return ratio <= 0.9 ? 0.6 : Math.Max(0, 0.6 - (ratio - 0.9) * 1.2);
        }

        /// <summary>
        /// What a buyer offers: the asking price for a car asked at no more than its worth; over that they haggle
        /// it down by up to 15% (<paramref name="roll"/> 0 to 1), though never below what the car is worth
        /// </summary>
        public static decimal OfferAmount(decimal askingPrice, decimal value, double roll)
        {
            if (askingPrice <= value) return askingPrice;

            var haggled = RoundToTen((double)askingPrice * (0.85 + 0.15 * Math.Clamp(roll, 0, 1)));
            return Math.Clamp(haggled, Math.Max(0m, value), askingPrice);
        }

        /// <summary>The scrapyard's price as a share of the car's worth: <see cref="ScrapShare"/> ± <see cref="ScrapSpread"/>, the same for a car every time it is asked</summary>
        public static double ScrapFactor(Guid carInstanceId)
        {
            var roll = (BitConverter.ToUInt32(carInstanceId.ToByteArray(), 0) % 1000) / 999.0;
            return ScrapShare * (1 - ScrapSpread + 2 * ScrapSpread * roll);
        }

        private static decimal RoundToTen(double amount) =>
            double.IsFinite(amount) && amount > 0 ? Math.Round((decimal)Math.Min(amount, 1e12) / 10) * 10 : 0m;

        /// <summary>A buyer's name at the start of a sentence ("a kid from the high school" → "A kid ...")</summary>
        public static string Capitalized(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

        private SaleResult? WhyNotSellable(GameState gameState, Car car)
        {
            if (!gameState.Player.Cars.Contains(car))
                return new SaleResult(SaleOutcome.NotOwned, "The car is no longer yours to sell.");

            if (gameState.PendingRace is { } race && race.PlayerCarInstanceId == car.InstanceId)
                return new SaleResult(SaleOutcome.Racing, "The car is out racing: it can be sold once the race is settled.");

            return null;
        }

        /// <summary>The car leaves the garage, its ad with it; the selected car moves on to another one</summary>
        private static void RemoveFromGarage(GameState gameState, Car car)
        {
            var player = gameState.Player;
            player.Cars.Remove(car);
            gameState.NewspaperAds.PlayerCars.RemoveAll(ad => ad.CarInstanceId == car.InstanceId);
            player.Stats.CarsSold++;
            if (player.SelectedCarInstanceId == car.InstanceId) player.SelectedCarInstanceId = player.Cars.FirstOrDefault()?.InstanceId;
        }

        /// <summary>Spends the time, then saves (the time and the new day it may end in belong in the save). True when the save failed</summary>
        private async Task<bool> SpendTimeAndSave(GameState gameState, GameAction action)
        {
            try
            {
                await _gameTimeService.SpendTimeAsync(gameState, action);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for {Action}", action);
            }

            return !Save(gameState);
        }

        private bool Save(GameState gameState)
        {
            if (string.IsNullOrEmpty(gameState.SaveName)) return true;

            try
            {
                _gameStateRepository.Save(gameState, gameState.SaveName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the game after a sale");
                return false;
            }
        }
    }
}
