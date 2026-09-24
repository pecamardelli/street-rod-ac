using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;

namespace Street_Rod_AC.Services.Parts
{
    /// <summary>Buying and selling parts; everything bought goes to the player's shelf</summary>
    public interface IPartsShopService
    {
        /// <summary>False when there are no converted parts: nothing to sell</summary>
        bool IsAvailable { get; }

        /// <summary>Every part that can be ordered new, by name</summary>
        IReadOnlyList<PartDefinition> Assortment { get; }

        /// <summary>What the shop asks for the part new</summary>
        /// <param name="priceMultiplier">The save's <see cref="GameRules.PartPriceMultiplier"/></param>
        decimal NewPrice(PartDefinition part, double priceMultiplier);

        /// <summary>What the player gets for a part of theirs, with whatever is mounted on it</summary>
        decimal TradeInPrice(PartInstance part);

        /// <summary>
        /// Takes old ads out of the paper and puts new ones in; run once a game day. The new ads are made on a
        /// worker thread (whole engines among them), the game state is only touched on the thread that called.
        /// </summary>
        Task RefreshAdsAsync(GameState gameState, DateTime currentDate);

        /// <summary>Ids of the parts that go somewhere on a car as it stands, taken slots included</summary>
        HashSet<string> FindFittingParts(Car? car);

        /// <summary>False when the player cannot pay</summary>
        bool BuyNew(GameState gameState, PartDefinition part);

        /// <summary>False when the player cannot pay, or the ad has left the paper in the meantime</summary>
        bool BuyUsed(GameState gameState, PartAd ad);

        bool Sell(GameState gameState, PartInstance part);
    }
}
