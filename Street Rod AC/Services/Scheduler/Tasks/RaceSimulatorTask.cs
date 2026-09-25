using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Simulation;

namespace Street_Rod_AC.Services.Scheduler.Tasks
{
    /// <summary>
    /// Simulates AI races daily when time advances
    /// </summary>
    public class RaceSimulatorTask : IScheduledTask
    {
        private readonly RaceSimulatorService _simulatorService;
        private readonly IContentCatalogRepository? _catalogRepo;
        private readonly IAppLogger _logger;

        public string TaskId => "race_simulator";
        public int IntervalDays => 1;

        /// <param name="catalogRepo">Names the cars in the street talk; null and they go by their folder names</param>
        public RaceSimulatorTask(RaceSimulatorService simulatorService, IContentCatalogRepository? catalogRepo = null)
        {
            _simulatorService = simulatorService;
            _catalogRepo = catalogRepo;
            _logger = AppLoggerFactory.CreateLogger("RaceSimulatorTask");
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Running daily race simulation for date {Date}", currentDate);

            var result = _simulatorService.SimulateDay(gameState, currentDate);

            _logger.Information(
                "Race simulation complete. Races simulated: {TotalRaces}",
                result.TotalRaces);

            // What the street talks about: cars changing hands and cars wrecked
            var talk = new List<string>();
            var carName = _catalogRepo == null ? (Func<string, string>)(id => id) : CarNames.Book(_catalogRepo);
            foreach (var race in result.Races)
            {
                var car = race.LoserCar == null ? "car" : carName(race.LoserCar.DefinitionId);
                if (race.IsPinkSlip)
                {
                    _logger.Information("Pink slip race! {Winner} won {Loser}'s car", race.WinnerName, race.LoserName);
                    talk.Add(race.LoserCrashed
                        ? $"{race.LoserName} wrecked the {car} racing {race.WinnerName} for pink slips; {race.WinnerName} towed away what's left of it."
                        : $"{race.WinnerName} took {race.LoserName}'s {car} in a pink slip race.");
                }
                else if (race.LoserCrashed)
                {
                    talk.Add($"{race.LoserName} wrecked the {car} in a {race.RaceType.ToLowerInvariant()} race against {race.WinnerName}.");
                }
            }

            OpponentLifeService.AddTalk(gameState, currentDate, talk);
            return Task.CompletedTask;
        }
    }
}
