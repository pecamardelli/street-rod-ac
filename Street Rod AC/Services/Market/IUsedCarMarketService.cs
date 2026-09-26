using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>
    /// Service for managing the used car market
    /// </summary>
    public interface IUsedCarMarketService
    {
        /// <summary>
        /// Spawns new used car listings based on precedence and dealer locations.
        /// The engines of the cars are put together on a worker thread: that takes a moment.
        /// </summary>
        /// <param name="priceMultiplier">The save's <see cref="GameRules.CarPriceMultiplier"/></param>
        Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier);

        /// <summary>
        /// Refreshes the market: removes old listings, spawns new ones. The listings that are there are only
        /// looked at on the calling thread; the engines of the new ones are put together on a worker thread.
        /// </summary>
        /// <param name="priceMultiplier">The save's <see cref="GameRules.CarPriceMultiplier"/>, for the new listings</param>
        Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier);

        /// <summary>
        /// Gets available (not sold) listings
        /// </summary>
        List<UsedCarListing> GetAvailableListings(List<UsedCarListing> allListings);

        /// <summary>
        /// Gets available listings for a specific dealer
        /// </summary>
        List<UsedCarListing> GetListingsByDealer(List<UsedCarListing> allListings, string dealerLocationId);

        /// <summary>
        /// The dealers of the dealer file, or built-in ones when there is none
        /// </summary>
        List<DealerLocation> GetDefaultDealers();

        /// <summary>
        /// Puts a car that somebody owned on a lot, the one way a listing is made from a car: it goes with its
        /// parts (engine, running gear), and the seller says what engine it has and whether it has been worked
        /// on. The car's part list moves to the listing; the caller no longer owns the car. The listing is
        /// returned, not added to the market.
        /// </summary>
        /// <param name="describeEngine">False leaves out what the seller says about the engine, and the dyno run it takes</param>
        UsedCarListing ListCar(Car car, decimal price, string location, DateTime listedDate, bool describeEngine = true);

        /// <summary>What a seller says about the car's engine, off the dyno; null without an engine or parts to tell</summary>
        EngineDescription? DescribeEngine(Car car);

        /// <summary>What the car is worth (<see cref="CarValuation"/>): its model's base price, its condition, its engine</summary>
        decimal ValueOf(Car car);

        /// <summary>
        /// <see cref="ValueOf"/> for a review that values car after car (a day of the rivals', the classifieds): each
        /// model is looked up in the catalog once. For one review only, on one thread; a later edit of a profile is
        /// not seen by it.
        /// </summary>
        Func<Car, decimal> Valuer() => ValueOf;

        /// <summary>The id of the dealer that takes in the cars nobody asked about: the roughest lot of <paramref name="dealers"/></summary>
        string TradeInLocation(IReadOnlyList<DealerLocation>? dealers);
    }

    /// <summary>An engine as a seller describes it: what it is, its dyno power, and whether somebody has been at it</summary>
    public sealed record EngineDescription(string? Summary, double PowerHp, bool IsModified);
}
