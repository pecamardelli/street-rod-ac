using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
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
    public class CarPurchaseService(IGameStateRepository gameStateRepository) : ICarPurchaseService
    {
        private readonly IGameStateRepository _gameStateRepository = gameStateRepository;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarPurchase");

        public async Task<PurchaseResult> PurchaseAsync(
            Models.GameState.GameState gameState, UsedCarListing listing, CarDefinition carDef)
        {
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

            var carInstance = new Car
            {
                InstanceId = Guid.NewGuid(),
                DefinitionId = listing.CarDefinitionId,
                SkinId = listing.SkinId,
                PurchasePrice = listing.Price,
                PurchaseDate = gameState.Date,
                OdometerKM = listing.Mileage,
                // Map single condition to health metrics
                EngineHealth = listing.Condition,
                TransmissionHealth = listing.Condition,
                BodyCondition = listing.Condition,
                TireCondition = listing.Condition,
                // What was for sale is what gets bought; older listings carry no parts and get the factory engine
                Parts = listing.Parts,
                HasPartsAssigned = listing.Parts.Count > 0
            };
            await ((App)System.Windows.Application.Current).CarPartsService.EnsurePartsAsync(carInstance);

            gameState.Player.Cars ??= [];
            gameState.Player.Cars.Add(carInstance);
            gameState.Player.Money -= listing.Price;

            // Somebody with no car of their own has just bought one: it is the one they mean. Without this
            // the garage falls back to whatever happens to be first in the list.
            gameState.Player.SelectedCarInstanceId ??= carInstance.InstanceId;

            currentListing.IsSold = true;
            currentListing.SoldDate = gameState.Date;

            // The parts went with the car; the sold listing stays around for a week and need not keep a copy
            currentListing.Parts = [];

            _logger.Information("Purchase completed: {CarName} for ${Price}, new bankroll: ${Bankroll}",
                carDef.Name, listing.Price, gameState.Player.Money);

            // Spend time for buying a car. Waited for before saving: late in the day that is the next morning,
            // with the market and the ads turned over, and all of it belongs in the save.
            try
            {
                await ((App)System.Windows.Application.Current).SpendTimeAsync(GameAction.BuyCar);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for buying the car");
            }

            var saveFailed = false;
            try
            {
                _gameStateRepository.Save(gameState, gameState.SaveName);
                _logger.Information("Game state saved after purchase");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save game state after purchase");
                saveFailed = true;
            }

            return new PurchaseResult(PurchaseOutcome.Bought,
                $"Congratulations! You've purchased a {carDef.Brand} {carDef.Name} for ${listing.Price:N0}.\n\n" +
                "You can now find it in your garage.",
                saveFailed);
        }
    }
}
