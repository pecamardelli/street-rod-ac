using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Market;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// Refreshes the used car market daily, unless the save's rules turned that off
    /// (<see cref="GameRules.MarketRefreshEnabled"/>): the lots then keep what they have until it is bought
    /// </summary>
    public class MarketRefreshTask : IScheduledTask
    {
        private readonly IUsedCarMarketService _marketService;
        private readonly IAppLogger _logger;

        public string TaskId => "market_refresh";
        public int IntervalDays => 1;

        public MarketRefreshTask(IUsedCarMarketService marketService)
        {
            _marketService = marketService;
            _logger = AppLoggerFactory.CreateLogger("MarketRefreshTask");
        }

        public async Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            if (!gameState.Rules.MarketRefreshEnabled)
            {
                _logger.Debug("Market refresh is off for this game");
                return;
            }

            _logger.Information("Running daily market refresh for date {Date}", currentDate);

            // A new career has no dealers until the player opens the map; the rivals buy off the lots from day one
            if (gameState.DealerLocations is not { Count: > 0 })
            {
                gameState.DealerLocations = _marketService.GetDefaultDealers();
            }

            var previousCount = gameState.UsedCarMarket.Count;
            var previousAvailable = gameState.UsedCarMarket.Count(l => !l.IsSold);

            // The refresh reads the listings before it awaits the new cars' engines; a car listed meanwhile (a rival
            // sold one, the player traded one in) is not in what it returns and would be lost with the old list
            var live = gameState.UsedCarMarket;
            var seen = live.Select(l => l.Id).ToHashSet();

            var refreshed = await _marketService.RefreshMarketAsync(
                live,
                gameState.DealerLocations,
                currentDate,
                gameState.Rules.CarPriceMultiplier);

            var returned = refreshed.Select(l => l.Id).ToHashSet();
            refreshed.AddRange(gameState.UsedCarMarket.Where(l => !seen.Contains(l.Id) && !returned.Contains(l.Id)).ToList());
            gameState.UsedCarMarket = refreshed;

            var newCount = gameState.UsedCarMarket.Count;
            var newAvailable = gameState.UsedCarMarket.Count(l => !l.IsSold);

            _logger.Information(
                "Market refresh complete. Listings: {Previous} -> {New}, Available: {PrevAvail} -> {NewAvail}",
                previousCount, newCount, previousAvailable, newAvailable);
        }
    }
}
