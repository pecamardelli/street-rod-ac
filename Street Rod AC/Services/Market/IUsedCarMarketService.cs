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
        Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate);

        /// <summary>
        /// Refreshes the market: removes old listings, spawns new ones. The listings that are there are only
        /// looked at on the calling thread; the engines of the new ones are put together on a worker thread.
        /// </summary>
        Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate);

        /// <summary>
        /// Gets available (not sold) listings
        /// </summary>
        List<UsedCarListing> GetAvailableListings(List<UsedCarListing> allListings);

        /// <summary>
        /// Gets available listings for a specific dealer
        /// </summary>
        List<UsedCarListing> GetListingsByDealer(List<UsedCarListing> allListings, string dealerLocationId);

        /// <summary>
        /// Gets default dealer locations
        /// </summary>
        List<DealerLocation> GetDefaultDealers();
    }
}
