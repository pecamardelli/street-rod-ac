using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Market;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// Refreshes the used car market daily
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

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Running daily market refresh for date {Date}", currentDate);

            var previousCount = gameState.UsedCarMarket.Count;
            var previousAvailable = gameState.UsedCarMarket.Count(l => !l.IsSold);

            gameState.UsedCarMarket = _marketService.RefreshMarket(
                gameState.UsedCarMarket,
                gameState.DealerLocations,
                currentDate);

            var newCount = gameState.UsedCarMarket.Count;
            var newAvailable = gameState.UsedCarMarket.Count(l => !l.IsSold);

            _logger.Information(
                "Market refresh complete. Listings: {Previous} -> {New}, Available: {PrevAvail} -> {NewAvail}",
                previousCount, newCount, previousAvailable, newAvailable);

            return Task.CompletedTask;
        }
    }
}
