using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>Buying a car, wherever it was found</summary>
    public interface ICarPurchaseService
    {
        /// <summary>
        /// Checks the money and the listing, moves the car into the player's garage, spends the time it takes
        /// and saves. The caller shows the result.
        /// </summary>
        Task<PurchaseResult> PurchaseAsync(
            Models.GameState.GameState gameState, UsedCarListing listing, CarDefinition carDef);

        /// <summary>The same for a rival's car out of the paper (<see cref="RivalCarAd"/>), at the ad's price as it stands now</summary>
        Task<PurchaseResult> PurchaseFromRivalAsync(Models.GameState.GameState gameState, RivalCarAd ad, CarDefinition carDef);
    }
}
