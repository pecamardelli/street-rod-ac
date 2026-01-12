using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Catalog;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for initializing opponents when creating a new game
    /// </summary>
    public class OpponentInitializationService : IOpponentInitializationService
    {
        private readonly IOpponentRepository _opponentRepository;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IAppLogger _logger;
        private readonly Random _random;

        public OpponentInitializationService(
            IOpponentRepository opponentRepository,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo)
        {
            _opponentRepository = opponentRepository;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
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

                // Generate and assign a used car to this opponent
                var car = GenerateOpponentCar(opponent);
                if (car != null)
                {
                    opponent.Cars.Add(car);
                    _logger.Debug("Assigned car {CarId} to opponent {Name}",
                        car.DefinitionId, opponent.Name);
                }
                else
                {
                    _logger.Warning("Failed to assign car to opponent {Name}", opponent.Name);
                }

                // Initialize opponent's bankroll
                opponent.Money = GenerateOpponentMoney(opponent);
                _logger.Debug("Set bankroll for opponent {Name}: ${Money:N0}",
                    opponent.Name, opponent.Money);

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
            return [.. shuffled.Take(count)];
        }

        /// <summary>
        /// Generate starting money for an opponent based on their traits
        /// Higher skill, age, and reputation = more money
        /// </summary>
        private decimal GenerateOpponentMoney(Opponent opponent)
        {
            // Base money: $2,000 to $5,000
            var baseMoney = 2000m + (_random.Next(0, 3001));

            // Skill bonus: Higher skill = more money (up to +$3,000)
            var skillBonus = (opponent.Skill - 80) * 150m; // 0 to 3000

            // Age bonus: Older opponents have had more time to earn (up to +$4,000)
            decimal ageBonus;
            if (opponent.Age < 26)
            {
                ageBonus = _random.Next(0, 501); // Young: $0-500
            }
            else if (opponent.Age < 36)
            {
                ageBonus = _random.Next(500, 1501); // Middle-aged: $500-1500
            }
            else if (opponent.Age < 46)
            {
                ageBonus = _random.Next(1500, 2501); // Established: $1500-2500
            }
            else
            {
                ageBonus = _random.Next(2000, 4001); // Veterans: $2000-4000
            }

            // Reputation bonus: Higher rep = more money (up to +$2,000)
            var reputationBonus = Math.Max(0, (opponent.Stats.Reputation - 50) * 40m);

            // Calculate total
            var totalMoney = baseMoney + skillBonus + ageBonus + reputationBonus;

            // Add random variation (±15%)
            var variation = 1.0m + ((decimal)_random.NextDouble() * 0.3m - 0.15m);
            totalMoney *= variation;

            // Round to nearest 100 and ensure minimum
            totalMoney = Math.Round(totalMoney / 100) * 100;
            return Math.Max(1000m, totalMoney); // Minimum $1,000
        }

        /// <summary>
        /// Generate a used car for an opponent
        /// Creates a car instance with appropriate condition and mileage based on opponent traits
        /// </summary>
        private Car? GenerateOpponentCar(Opponent opponent)
        {
            // Get all active cars from catalog
            var activeCars = _catalogRepo.GetCarsByStatus(Models.Catalog.ContentStatus.Active);

            if (activeCars.Count == 0)
            {
                _logger.Warning("No active cars available to assign to opponent {Name}", opponent.Name);
                return null;
            }

            // Select a random car definition
            var carDefinition = activeCars[_random.Next(activeCars.Count)];

            // Get car profile for pricing info
            var profile = _profileRepo.GetProfile(carDefinition.Id);
            if (profile == null)
            {
                _logger.Warning("No profile found for car {CarId}, cannot assign to opponent", carDefinition.Id);
                return null;
            }

            // Select random skin
            var skinId = "default";
            if (carDefinition.AvailableSkins != null && carDefinition.AvailableSkins.Count > 0)
            {
                skinId = carDefinition.AvailableSkins[_random.Next(carDefinition.AvailableSkins.Count)];
            }

            // Generate condition based on opponent's skill and age
            // Higher skill opponents tend to have better maintained cars
            // Younger opponents might have more worn cars (less money for maintenance)
            var baseCondition = 0.5f + (opponent.Skill - 80) / 100f; // 0.5 to 0.7 based on skill
            var ageAdjustment = opponent.Age > 35 ? 0.1f : -0.05f; // Older opponents have better cars
            var condition = Math.Clamp(baseCondition + ageAdjustment + ((float)_random.NextDouble() * 0.2f - 0.1f), 0.4f, 0.9f);

            // Generate mileage (higher skill opponents might have lower mileage)
            var baseMileage = 50000 + _random.Next(0, 100000);
            var skillAdjustment = (100 - opponent.Skill) * 500; // Lower skill = more mileage
            var mileage = baseMileage + skillAdjustment;

            // Create the car instance
            var car = new Car(carDefinition.Id)
            {
                SkinId = skinId,
                OdometerKM = mileage,
                EngineHealth = condition,
                TransmissionHealth = condition + (float)_random.NextDouble() * 0.1f - 0.05f,
                BodyCondition = condition + (float)_random.NextDouble() * 0.1f - 0.05f,
                TireCondition = 0.6f + (float)_random.NextDouble() * 0.3f, // Tires vary more
                PurchasePrice = profile.BasePrice * (decimal)condition,
                PurchaseDate = DateTime.Now.AddDays(-_random.Next(30, 365)) // Owned for 1 month to 1 year
            };

            _logger.Debug("Generated car {CarId} for opponent {Name}: Condition={Condition:F2}, Mileage={Mileage}",
                carDefinition.Id, opponent.Name, condition, mileage);

            return car;
        }
    }
}
