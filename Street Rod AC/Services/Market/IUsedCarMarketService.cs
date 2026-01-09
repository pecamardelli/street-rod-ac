using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Market
{
    /// <summary>
    /// Service for managing the used car market
    /// </summary>
    public interface IUsedCarMarketService
    {
        /// <summary>
        /// Spawns new used car listings based on precedence and dealer locations
        /// </summary>
        List<UsedCarListing> SpawnListings(List<DealerLocation> dealers, DateTime currentDate);

        /// <summary>
        /// Refreshes the market: removes old listings, spawns new ones
        /// </summary>
        List<UsedCarListing> RefreshMarket(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate);

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
