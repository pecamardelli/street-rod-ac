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

            var previousCount = gameState.UsedCarMarket.Count;
            var previousAvailable = gameState.UsedCarMarket.Count(l => !l.IsSold);

            gameState.UsedCarMarket = await _marketService.RefreshMarketAsync(
                gameState.UsedCarMarket,
                gameState.DealerLocations,
                currentDate,
                gameState.Rules.CarPriceMultiplier);

            var newCount = gameState.UsedCarMarket.Count;
            var newAvailable = gameState.UsedCarMarket.Count(l => !l.IsSold);

            _logger.Information(
                "Market refresh complete. Listings: {Previous} -> {New}, Available: {PrevAvail} -> {NewAvail}",
                previousCount, newCount, previousAvailable, newAvailable);
        }
    }
}
