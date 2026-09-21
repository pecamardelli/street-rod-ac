using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Parts;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// Turns over the used parts ads daily
    /// </summary>
    public class PartsAdsRefreshTask : IScheduledTask
    {
        private readonly IPartsShopService _shopService;

        public string TaskId => "parts_ads_refresh";
        public int IntervalDays => 1;

        public PartsAdsRefreshTask(IPartsShopService shopService)
        {
            _shopService = shopService;
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            // Building whole engines for the ads takes a moment: not on the UI thread
            return Task.Run(() => _shopService.RefreshAds(gameState, currentDate));
        }
    }
}
