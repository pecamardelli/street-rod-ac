using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Market;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// The player's cars in the paper, daily: buyers call about them, give up on them, and old ads run out
    /// </summary>
    public class CarAdsReviewTask(ICarSaleService saleService) : IScheduledTask
    {
        private readonly ICarSaleService _saleService = saleService;

        public string TaskId => "car_ads_review";
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _saleService.ReviewAds(gameState, currentDate, Random.Shared);
            return Task.CompletedTask;
        }
    }
}
