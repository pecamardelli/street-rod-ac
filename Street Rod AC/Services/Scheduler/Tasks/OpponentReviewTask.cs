using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Opponents;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// The rivals' morning, daily and before they race each other: cars bought, repaired, sold and tuned, racers
    /// going broke and coming back, new faces on the street (<see cref="OpponentLifeService"/>)
    /// </summary>
    public class OpponentReviewTask(IOpponentLifeService lifeService) : IScheduledTask
    {
        private readonly IOpponentLifeService _lifeService = lifeService;

        public string TaskId => "opponent_review";
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate) => _lifeService.ReviewDayAsync(gameState, currentDate);
    }
}
