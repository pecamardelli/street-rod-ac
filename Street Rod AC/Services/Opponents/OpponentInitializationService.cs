using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Parts;

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
        private readonly ICarPartsService? _parts;
        private readonly IAppLogger _logger;
        private readonly Random _random;

        /// <summary>The opponent definitions were read and hold no King: not read again every day of the session</summary>
        private bool _noKingDefined;

        /// <summary>What the King has in the bank</summary>
        public const decimal KingMoney = 20000m;

        /// <summary>The King's car is one of the this many strongest from the factory</summary>
        private const int KingCarChoices = 5;

        /// <param name="parts">
        /// Gives the rivals' cars their engines and running gear (a used engine, now and then worked on); null, or no
        /// parts, and the cars get them the first time they are looked at
        /// </param>
        public OpponentInitializationService(
            IOpponentRepository opponentRepository,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            ICarPartsService? parts = null)
        {
            _opponentRepository = opponentRepository;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _parts = parts;
            _logger = AppLoggerFactory.CreateLogger("OpponentInitialization");
            _random = new Random();
        }

        /// <summary>
        /// Initialize opponents for a new game
        /// </summary>
        public void InitializeOpponents(GameState gameState, int? opponentCount = null)
        {
            _logger.Information("Initializing opponents for new game");

            // Load all available opponents; the King is set up on his own
            var allOpponents = _opponentRepository.LoadAllOpponents().Where(o => !o.IsKing).ToList();

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
                selectedOpponents = allOpponents.OrderBy(_ => _random.Next()).Take(opponentCount.Value).ToList();
                _logger.Information("Selected {Count} random opponents from pool of {Total}",
                    opponentCount.Value, allOpponents.Count);
            }
            else
            {
                // Use all opponents
                selectedOpponents = allOpponents;
                _logger.Information("Using all {Count} opponents", allOpponents.Count);
            }

            // The cars they can have, looked up once for all of them: installed ones only (AC cannot race a car
            // that was deleted from content\cars, however long the catalog remembers it), with a price
            var carPool = BuildCarPool();

            // A few racers are out on the street when the career starts; the rest turn up as the weeks go by
            // (OpponentLifeService). Who comes first is the luck of the draw.
            var onTheStreet = OpponentRules.MinActive(gameState.Date, selectedOpponents.Count);
            var order = selectedOpponents.OrderBy(_ => _random.Next()).ToList();

            // Add opponents to game state
            for (var i = 0; i < order.Count; i++)
            {
                var opponent = order[i];
                opponent.Status = i < onTheStreet ? RacerStatus.ReadyToRace : RacerStatus.Inactive;

                // Generate and assign a used car to this opponent
                var car = GenerateOpponentCar(opponent, carPool, gameState.Date);
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

            _logger.Information("Successfully initialized {Count} opponents, {Street} of them on the street", selectedOpponents.Count, onTheStreet);

            EnsureKing(gameState, carPool);
        }

        public void EnsureKing(GameState gameState) => EnsureKing(gameState, null);

        private void EnsureKing(GameState gameState, List<(CarDefinition Car, CarProfile Profile)>? carPool)
        {
            var racers = gameState.Racers;
            if (racers.All.Any(r => r is Opponent { IsKing: true })) return;
            if (_noKingDefined) return;

            var king = _opponentRepository.LoadAllOpponents().FirstOrDefault(o => o.IsKing);
            if (king == null)
            {
                _noKingDefined = true;
                _logger.Warning("No King among the opponent definitions: the King victory can't be won");
                return;
            }

            // Out of sight until the player has earned a shot at him (OpponentLifeService brings him to the diner)
            king.Status = RacerStatus.Inactive;
            king.Money = KingMoney;

            // A name on the street: years of wins, pink slips taken
            king.Stats.Races = 52;
            king.Stats.Wins = 48;
            king.Stats.Losses = 4;
            king.Stats.DragRaces = king.Stats.RoadRaces = 26;
            king.Stats.DragWins = king.Stats.RoadWins = 24;
            king.Stats.DragLosses = king.Stats.RoadLosses = 2;
            king.Stats.PinkSlipsWon = 12;
            king.Stats.Reputation = king.Stats.CalculateReputation();

            var car = CreateKingCar(carPool ?? BuildCarPool(), gameState.Date, king);
            if (car != null) king.Cars.Add(car);

            racers.AddRacer(king);
            _logger.Information("{King} is in town, in a {Car} ({Power:0} hp)", king.Name, car?.DefinitionId ?? "(no car)", car?.PowerHp ?? 0);
        }

        /// <summary>
        /// One of the strongest cars there is from the factory (the dearest, without parts to tell), in top shape, its
        /// engine worked on as much as it gets
        /// </summary>
        private Car? CreateKingCar(List<(CarDefinition Car, CarProfile Profile)> pool, DateTime today, Opponent king)
        {
            var choices = (_parts is { IsAvailable: true } catalogParts
                    ? pool.Select(p => (p.Car, p.Profile, Power: catalogParts.GetStockBuild(p.Car)?.PowerHp ?? 0)).Where(p => p.Power > 0)
                    : pool.Select(p => (p.Car, p.Profile, Power: (double)p.Profile.BasePrice)))
                .OrderByDescending(p => p.Power)
                .Take(KingCarChoices)
                .Select(p => (p.Car, p.Profile))
                .ToList();
            if (choices.Count == 0) return null;

            var (definition, profile) = choices[_random.Next(choices.Count)];
            var car = new Car(definition.Id)
            {
                SkinId = definition.AvailableSkins is { Count: > 0 } skins ? skins[_random.Next(skins.Count)] : "default",
                OdometerKM = 20000 + _random.Next(0, 20000),
                EngineHealth = 0.95,
                TransmissionHealth = 0.95,
                BodyCondition = 1.0,
                TireCondition = 0.95,
                PurchaseDate = today.AddDays(-_random.Next(60, 365))
            };

            if (_parts is { IsAvailable: true } parts && parts.GetStockBuild(definition) is { } build)
            {
                try
                {
                    AddEngine(car, EngineFactory.CreateTuned(parts.Catalog, parts.Builds, build, 0.95, 1.0, _random));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not build the King's engine: {Error}", ex.Message);
                }
            }

            // The car everybody on the street has lost to: his since he had it, and it shows in its worth (before
            // FinishCar prices it)
            car.History.ChangeHands(king.Name, car.PurchaseDate, CarAcquisition.Unknown);
            car.History.Races = king.Stats.Races - _random.Next(0, 10);
            car.History.Wins = car.History.Races - _random.Next(1, 4);
            car.History.PinkSlipsWon = king.Stats.PinkSlipsWon;

            FinishCar(car, definition, profile);
            return car;
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
            var skillBonus = (opponent.Skill - 80) * 150m; // 1500 to 3000 (skills run 90-100)

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

        /// <summary>The installed, active catalog cars that have a profile to price them, with that profile</summary>
        private List<(CarDefinition Car, CarProfile Profile)> BuildCarPool()
        {
            var installed = InstalledCars.Only(_catalogRepo.GetCarsByStatus(ContentStatus.Active), out var notInstalled);
            if (notInstalled > 0)
            {
                _logger.Warning("{Count} catalog cars are not installed; no opponent gets one of them", notInstalled);
            }

            var profiles = _profileRepo.GetAllProfiles().ToDictionary(p => p.CarDefinitionId, StringComparer.OrdinalIgnoreCase);
            return [.. installed.Where(c => profiles.ContainsKey(c.Id)).Select(c => (c, profiles[c.Id]))];
        }

        /// <summary>
        /// Generate a used car for an opponent
        /// Creates a car instance with appropriate condition and mileage based on opponent traits
        /// </summary>
        private Car? GenerateOpponentCar(Opponent opponent, List<(CarDefinition Car, CarProfile Profile)> pool, DateTime today)
        {
            if (pool.Count == 0)
            {
                _logger.Warning("No installed car with a profile to assign to opponent {Name}", opponent.Name);
                return null;
            }

            // Select a random car definition
            var (carDefinition, profile) = pool[_random.Next(pool.Count)];

            // Select random skin
            var skinId = "default";
            if (carDefinition.AvailableSkins != null && carDefinition.AvailableSkins.Count > 0)
            {
                skinId = carDefinition.AvailableSkins[_random.Next(carDefinition.AvailableSkins.Count)];
            }

            // Generate condition based on opponent's skill and age
            // Higher skill opponents tend to have better maintained cars
            // Younger opponents might have more worn cars (less money for maintenance)
            var baseCondition = 0.5f + (opponent.Skill - 80) / 100f; // 0.6 to 0.7 (skills run 90-100)
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
                // The gearbox and the body are the parts' and the damage's once FinishCar has worked the figures out
                EngineHealth = condition,
                TransmissionHealth = condition,
                BodyCondition = 1.0,
                TireCondition = 0.6f + (float)_random.NextDouble() * 0.3f, // Tires vary more
                PurchaseDate = today.AddDays(-_random.Next(30, 365)) // Owned for 1 month to 1 year, in game time
            };

            // A car that has done that many miles has been through a few hands before theirs
            car.History.EarlierOwners = 1 + _random.Next(0, 1 + mileage / 50_000);
            car.History.ChangeHands(opponent.Name, car.PurchaseDate, CarAcquisition.Unknown);

            // A used engine, now and then worked on, like the ones on the lots
            if (_parts is { IsAvailable: true } parts)
            {
                try
                {
                    AddEngine(car, parts.CreateUsedEngine(carDefinition, condition));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not build the engine of {Name}'s {Car}: {Error}", opponent.Name, carDefinition.Id, ex.Message);
                }
            }

            FinishCar(car, carDefinition, profile);

            _logger.Debug("Generated car {CarId} for opponent {Name}: Condition={Condition:F2}, Mileage={Mileage}",
                carDefinition.Id, opponent.Name, condition, mileage);

            return car;
        }

        private static void AddEngine(Car car, BuiltEngine? engine)
        {
            if (engine == null) return;

            car.Parts.Add(engine.Root);
            car.HasPartsAssigned = true;
            car.PowerHp = UsedCarMarketService.PowerOf(engine.Report);
        }

        /// <summary>The running gear, the car's figures from its parts, and what it is worth</summary>
        private void FinishCar(Car car, CarDefinition definition, CarProfile profile)
        {
            if (_parts is { IsAvailable: true } parts)
            {
                try
                {
                    parts.EnsureParts(car);
                    CarCondition.RefreshFigures(car, CarCondition.Groups(parts.Catalog));
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not give the {Car} its running gear: {Error}", car.DefinitionId, ex.Message);
                }
            }

            // Paid what a car like that is worth: the same sum the market prices its cars with
            car.PurchasePrice = CarValuation.ValueOf(car, profile.BasePrice, _parts, definition);
        }
    }
}
