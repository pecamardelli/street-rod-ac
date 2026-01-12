using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for initializing opponents when creating a new game
    /// </summary>
    public class OpponentInitializationService : IOpponentInitializationService
    {
        private readonly IOpponentRepository _opponentRepository;
        private readonly IAppLogger _logger;
        private readonly Random _random;

        public OpponentInitializationService(IOpponentRepository opponentRepository)
        {
            _opponentRepository = opponentRepository;
            _logger = AppLoggerFactory.CreateLogger("OpponentInitialization");
            _random = new Random();
        }

        /// <summary>
        /// Initialize opponents for a new game
        /// </summary>
        public void InitializeOpponents(GameState gameState, int? opponentCount = null)
        {
            _logger.Information("Initializing opponents for new game");

            // Load all available opponents
            var allOpponents = _opponentRepository.LoadAllOpponents();

            if (allOpponents.Count == 0)
            {
                _logger.Warning("No opponent definitions found. Game will have no AI racers.");
                return;
            }

            _logger.Information("Loaded {Count} opponent definitions", allOpponents.Count);

            // Determine how many opponents to use
            List<Opponent> selectedOpponents;
            if (opponentCount.HasValue && opponentCount.Value < allOpponents.Count)
            {
                // Select random subset
                selectedOpponents = GetRandomOpponents(opponentCount.Value);
                _logger.Information("Selected {Count} random opponents from pool of {Total}",
                    opponentCount.Value, allOpponents.Count);
            }
            else
            {
                // Use all opponents
                selectedOpponents = allOpponents;
                _logger.Information("Using all {Count} opponents", allOpponents.Count);
            }

            // Add opponents to game state
            foreach (var opponent in selectedOpponents)
            {
                // Set initial status (most start as ReadyToRace)
                opponent.Status = RacerStatus.ReadyToRace;

                // Add to racer collection
                gameState.Racers.AddRacer(opponent);

                _logger.Debug("Added opponent: {Name} (Skill: {Skill}, Aggression: {Aggression})",
                    opponent.Name, opponent.Skill, opponent.Aggression);
            }

            _logger.Information("Successfully initialized {Count} opponents", selectedOpponents.Count);
        }

        /// <summary>
        /// Get random selection of opponents
        /// </summary>
        public List<Opponent> GetRandomOpponents(int count)
        {
            var allOpponents = _opponentRepository.LoadAllOpponents();

            if (count >= allOpponents.Count)
            {
                return allOpponents;
            }

            // Shuffle and take first N
            var shuffled = allOpponents.OrderBy(x => _random.Next()).ToList();
            return shuffled.Take(count).ToList();
        }
    }
}
