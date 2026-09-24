using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>Selling the player's cars: to a dealer (or the scrapyard) at once, or through an ad in the paper</summary>
    public interface ICarSaleService
    {
        /// <summary>What the car is worth (<see cref="CarValuation"/>)</summary>
        decimal ValueOf(Car car);

        /// <summary>Who takes the car today and for how much: the trade-in dealer, or the scrapyard for a totaled car</summary>
        DealerQuote QuoteDealer(GameState gameState, Car car);

        /// <summary>Sells the car on the spot at <see cref="QuoteDealer"/>, spends the time and saves</summary>
        Task<SaleResult> SellToDealerAsync(GameState gameState, Car car);

        /// <summary>The car's ad in the paper, if it has one</summary>
        CarSaleAd? AdFor(GameState gameState, Car car);

        /// <summary>A price to start from when putting the car in the paper: its worth</summary>
        decimal SuggestedAskingPrice(Car car);

        /// <summary>What the car can be put in the paper for</summary>
        (decimal Min, decimal Max) AskingPriceRange(Car car);

        /// <summary>Pays for the ad, puts the car in the paper, spends the time and saves</summary>
        Task<SaleResult> PlaceAdAsync(GameState gameState, Car car, decimal askingPrice);

        /// <summary>Takes the ad out of the paper (the fee is gone) and saves</summary>
        SaleResult WithdrawAd(GameState gameState, CarSaleAd ad);

        /// <summary>Takes the buyer's money: the car leaves the game. Spends the time and saves</summary>
        Task<SaleResult> AcceptOfferAsync(GameState gameState, CarSaleAd ad);

        /// <summary>Sends the buyer away; the ad stays in the paper for the next one. Saves</summary>
        SaleResult DeclineOffer(GameState gameState, CarSaleAd ad);

        /// <summary>A day in the classifieds, once a game day: old ads go, buyers give up or call</summary>
        void ReviewAds(GameState gameState, DateTime currentDate, Random random);
    }
}
