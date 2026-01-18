using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Simulation;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// Simulates AI races daily when time advances
    /// </summary>
    public class RaceSimulatorTask : IScheduledTask
    {
        private readonly RaceSimulatorService _simulatorService;
        private readonly IAppLogger _logger;

        public string TaskId => "race_simulator";
        public int IntervalDays => 1;

        public RaceSimulatorTask(RaceSimulatorService simulatorService)
        {
            _simulatorService = simulatorService;
            _logger = AppLoggerFactory.CreateLogger("RaceSimulatorTask");
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Running daily race simulation for date {Date}", currentDate);

            var result = _simulatorService.SimulateDay(gameState, currentDate);

            _logger.Information(
                "Race simulation complete. Races simulated: {TotalRaces}",
                result.TotalRaces);

            // Log summary of interesting events
            foreach (var race in result.Races.Where(r => r.IsPinkSlip))
            {
                _logger.Information(
                    "Pink slip race! {Winner} won {Loser}'s car",
                    race.WinnerName, race.LoserName);
            }

            return Task.CompletedTask;
        }
    }
}
