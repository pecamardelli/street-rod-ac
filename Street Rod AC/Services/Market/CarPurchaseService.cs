using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.Services.Storage;
using Street_Rod_AC.Services.Time;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>What came of trying to buy a car</summary>
    public enum PurchaseOutcome
    {
        Bought,
        NotEnoughMoney,
        NoLongerAvailable
    }

    /// <summary>The result of a purchase, for the screen to put in front of the player</summary>
    public readonly record struct PurchaseResult(PurchaseOutcome Outcome, string Message, bool SaveFailed = false)
    {
        public bool Succeeded => Outcome == PurchaseOutcome.Bought;
    }

    /// <summary>
    /// Buying a car: the money, the paperwork and the clock. Shared by every screen that sells one, so a car
    /// bought off the lot and a car bought out of the listings end up in exactly the same state.
    /// </summary>
    public class CarPurchaseService(
        IGameStateRepository gameStateRepository,
        ICarPartsService partsService,
        IGameTimeService gameTimeService) : ICarPurchaseService
    {
        private readonly IGameStateRepository _gameStateRepository = gameStateRepository;
        private readonly ICarPartsService _partsService = partsService;
        private readonly IGameTimeService _gameTimeService = gameTimeService;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarPurchase");

        public async Task<PurchaseResult> PurchaseAsync(
            Models.GameState.GameState gameState, UsedCarListing listing, CarDefinition carDef)
        {
            if (listing.PrivateSeller != null) return await PurchaseFromRivalAsync(gameState, listing, carDef);

            if (gameState.Player.Money < listing.Price)
            {
                _logger.Warning("Purchase failed: insufficient funds");
                return new PurchaseResult(PurchaseOutcome.NotEnoughMoney,
                    "You don't have enough money to purchase this car.");
            }

            var currentListing = gameState.UsedCarMarket.FirstOrDefault(l => l.Id == listing.Id);
            if (currentListing == null || currentListing.IsSold)
            {
                _logger.Warning("Purchase failed: listing no longer available");
                return new PurchaseResult(PurchaseOutcome.NoLongerAvailable,
                    "This car is no longer available.");
            }

            // Claimed and paid for before anything is awaited: a second confirm that comes in while the engine is
            // being put together finds it sold, and a second purchase of a different car checks the money that is
            // actually left instead of the same bankroll, so two overlapping purchases can never overdraw
            currentListing.IsSold = true;
            currentListing.SoldDate = gameState.Date;
            gameState.Player.Money -= listing.Price;

            Car carInstance;
            try
            {
                carInstance = await CreateCarAsync(gameState, listing);
            }
            catch
            {
                currentListing.IsSold = false;
                currentListing.SoldDate = null;
                gameState.Player.Money += listing.Price;
                throw;
            }

            gameState.Player.Cars ??= [];
            gameState.Player.Cars.Add(carInstance);
            gameState.Player.Stats.CarsOwned++;
            gameState.Career.SyncStanding(gameState.Player);

            // Somebody with no car of their own has just bought one: it is the one they mean. Without this
            // the garage falls back to whatever happens to be first in the list.
            gameState.Player.SelectedCarInstanceId ??= carInstance.InstanceId;

            // The parts went with the car; the sold listing stays around for a week and need not keep a copy
            currentListing.Parts = [];

            _logger.Information("Purchase completed: {CarName} for ${Price}, new bankroll: ${Bankroll}",
                carDef.Name, listing.Price, gameState.Player.Money);

            // Spend time for buying a car. Waited for before saving: late in the day that is the next morning,
            // with the market and the ads turned over, and all of it belongs in the save.
            try
            {
                await _gameTimeService.SpendTimeAsync(gameState, GameAction.BuyCar);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for buying the car");
            }

            var saveFailed = !GameSaves.TrySave(_gameStateRepository, gameState, _logger, "a purchase");

            return new PurchaseResult(PurchaseOutcome.Bought,
                $"Congratulations! You've purchased a {carDef.Brand} {carDef.Name} for ${listing.Price:N0}.\n\n" +
                "You can now find it in your garage.",
                saveFailed);
        }

        /// <summary>
        /// A rival's car out of the paper (<see cref="RivalCarAds"/>): that very car, from the rival's garage, the money to
        /// the rival. Nothing to put together, so nothing is awaited before the car changes hands.
        /// </summary>
        private async Task<PurchaseResult> PurchaseFromRivalAsync(Models.GameState.GameState gameState, UsedCarListing listing, CarDefinition carDef)
        {
            // The ad as it stands now: its price may have come down since the page was drawn
            var ad = RivalCarAds.AdOf(gameState, listing);
            if (ad == null)
            {
                _logger.Warning("Purchase failed: the ad is gone");
                return new PurchaseResult(PurchaseOutcome.NoLongerAvailable, "This car is no longer available.");
            }

            var price = ad.AskingPrice;
            if (gameState.Player.Money < price)
            {
                _logger.Warning("Purchase failed: insufficient funds");
                return new PurchaseResult(PurchaseOutcome.NotEnoughMoney, "You don't have enough money to purchase this car.");
            }

            var car = RivalCarAds.HandOver(gameState, ad, gameState.Player.Name, gameState.Date);
            if (car == null)
            {
                _logger.Warning("Purchase failed: the rival no longer has the car");
                return new PurchaseResult(PurchaseOutcome.NoLongerAvailable, "This car is no longer available.");
            }

            gameState.Player.Money -= price;
            gameState.Player.Cars ??= [];
            gameState.Player.Cars.Add(car);
            gameState.Player.Stats.CarsOwned++;
            gameState.Career.SyncStanding(gameState.Player);
            gameState.Player.SelectedCarInstanceId ??= car.InstanceId;

            _logger.Information("Purchase completed: {CarName} from {Seller} for ${Price}, new bankroll: ${Bankroll}",
                carDef.Name, listing.PrivateSeller, price, gameState.Player.Money);

            try
            {
                await _gameTimeService.SpendTimeAsync(gameState, GameAction.BuyCar);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for buying the car");
            }

            var saveFailed = !GameSaves.TrySave(_gameStateRepository, gameState, _logger, "a purchase");

            return new PurchaseResult(PurchaseOutcome.Bought,
                $"You bought {listing.PrivateSeller}'s {carDef.Brand} {carDef.Name} for ${price:N0}.\n\n" +
                "You can now find it in your garage.",
                saveFailed);
        }

        /// <summary>
        /// The car that was for sale, as whoever buys it gets it: the parts it came with (the listing's list moves to the
        /// car), paid the listing's price on <paramref name="date"/>. Older listings carry no parts; the car is then
        /// given its factory ones (<see cref="ICarPartsService.EnsureParts"/>), which is the buyer's to do. The car's
        /// history comes with it, <paramref name="buyer"/> its newest owner.
        /// </summary>
        public static Car CarFrom(UsedCarListing listing, DateTime date, string buyer)
        {
            var car = new Car
            {
                // A relisted car is the same car it was
                InstanceId = listing.CarInstanceId ?? Guid.NewGuid(),
                DefinitionId = listing.CarDefinitionId,
                SkinId = listing.SkinId,
                PurchasePrice = listing.Price,
                PurchaseDate = date,
                OdometerKM = listing.Mileage,
                // Map single condition to health metrics
                EngineHealth = listing.Condition,
                TransmissionHealth = listing.Condition,
                BodyCondition = listing.Condition,
                TireCondition = listing.Condition,
                // What was for sale is what gets bought; older listings carry no parts and get the factory engine
                Parts = listing.Parts,
                HasPartsAssigned = listing.Parts.Count > 0,
                HasRunningGearAssigned = listing.HasRunningGearAssigned,
                PowerHp = listing.PowerHp,
                History = listing.History?.Copy() ?? new CarHistory()
            };
            car.History.ChangeHands(buyer, date, CarAcquisition.Dealer);

            // A relisted car comes with its dents; the body's figure is then the dents', not the listing's average
            if (listing.BodyDamageKmh is { Length: > 0 } body)
            {
                car.BodyDamageKmh = body;
                car.BodyDamageKmh = CarCondition.Body(car); // four sane zones, a copy of the car's own
                car.BodyCondition = CarCondition.BodyCondition(car);
            }

            return car;
        }

        /// <summary>The car that was for sale, with the parts it came with, or its factory engine if it came without</summary>
        private async Task<Car> CreateCarAsync(Models.GameState.GameState gameState, UsedCarListing listing)
        {
            var carInstance = CarFrom(listing, gameState.Date, gameState.Player.Name);

            try
            {
                await _partsService.EnsurePartsAsync(carInstance);
            }
            catch (Exception ex)
            {
                // The car sells without the parts it was missing, the way a listing whose engine could not be
                // built does; the garage gives it its factory ones the next time it looks
                _logger.Warning("Could not give the {CarId} its factory parts: {Error}", listing.CarDefinitionId, ex.Message);
            }

            return carInstance;
        }
    }
}
