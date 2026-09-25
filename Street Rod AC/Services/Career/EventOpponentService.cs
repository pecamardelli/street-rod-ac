using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Catalog;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for selecting opponents for race events.
    /// Prefers pool opponents when available, falls back to special event opponents.
    /// </summary>
    public class EventOpponentService : IEventOpponentService
    {
        private readonly ICarFilterService _filterService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly Func<Car, bool> _canRace;
        private readonly Random _random = new();

        /// <param name="canRace">Whether a rival's car can race today; a racer whose car is laid up sits the event out</param>
        public EventOpponentService(
            ICarFilterService filterService,
            IContentCatalogRepository catalogRepository,
            Func<Car, bool>? canRace = null)
        {
            _filterService = filterService;
            _catalogRepository = catalogRepository;
            _canRace = canRace ?? (_ => true);
        }

        public EventOpponentResult? GetOpponentForEvent(
            RaceEventDefinition eventDef,
            GameState gameState)
        {
            // The catalog's cars that are in the install, read once for the whole choice rather than once per
            // opponent: only these can go to AC
            var installed = InstalledCars.Only(_catalogRepository.GetCarsByStatus(ContentStatus.Active), out _)
                .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

            // Try pool opponents first (unless exclusive)
            if (!eventDef.ExclusiveOpponents)
            {
                var poolOpponent = FindEligiblePoolOpponent(eventDef, gameState, installed);
                if (poolOpponent != null)
                    return poolOpponent;
            }

            // Fall back to special opponents
            if (eventDef.SpecialOpponents?.Count > 0)
            {
                var special = eventDef.SpecialOpponents[
                    _random.Next(eventDef.SpecialOpponents.Count)
                ];

                return new EventOpponentResult
                {
                    IsPoolOpponent = false,
                    SpecialOpponent = special,
                    OpponentName = special.Name,
                    OpponentNickname = special.Nickname,
                    CarDefinitionId = special.CarDefinitionId,
                    CarSkin = special.CarSkin ?? "default",
                    Skill = special.Skill,
                    Aggression = special.Aggression
                };
            }

            // No opponent available - generate a fallback
            return GenerateFallbackOpponent(eventDef, installed);
        }

        private EventOpponentResult? FindEligiblePoolOpponent(
            RaceEventDefinition eventDef,
            GameState gameState,
            Dictionary<string, CarDefinition> installed)
        {
            var candidates = new List<(Opponent, Car)>();

            // Check ReadyToRace pool for eligible opponents
            foreach (var racer in gameState.Racers.ReadyToRace.Values)
            {
                // The King races for pink slips at his own table, not in anybody's event
                if (racer is not Opponent opponent || opponent.IsKing)
                    continue;

                var car = opponent.Cars.FirstOrDefault();
                if (car == null || !_canRace(car))
                    continue;

                // A car that is no longer installed cannot race
                if (!installed.TryGetValue(car.DefinitionId, out var carDef))
                    continue;

                // Check if opponent's car meets event requirements
                if (eventDef.EntryRequirements != null && !_filterService.Matches(eventDef.EntryRequirements, carDef, car))
                    continue;

                candidates.Add((opponent, car));
            }

            if (candidates.Count == 0)
                return null;

            // Pick random eligible opponent
            var (selectedOpponent, opponentCar) = candidates[_random.Next(candidates.Count)];

            return new EventOpponentResult
            {
                IsPoolOpponent = true,
                PoolOpponent = selectedOpponent,
                PoolOpponentCar = opponentCar,
                OpponentName = selectedOpponent.Name,
                OpponentNickname = selectedOpponent.Nickname,
                CarDefinitionId = opponentCar.DefinitionId,
                CarSkin = opponentCar.SkinId ?? "default",
                Skill = selectedOpponent.Skill,
                Aggression = selectedOpponent.Aggression
            };
        }

        /// <summary>
        /// A basic opponent when none are available, so events can always be entered. The car is an installed
        /// one from the catalog, one that meets the event's requirements when there is such a car. Null only
        /// when nothing at all is installed.
        /// </summary>
        private EventOpponentResult? GenerateFallbackOpponent(RaceEventDefinition eventDef, Dictionary<string, CarDefinition> installed)
        {
            var cars = installed.Values.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList();
            if (cars.Count == 0)
                return null;

            var fitting = eventDef.EntryRequirements == null
                ? cars
                : cars.Where(c => _filterService.Matches(eventDef.EntryRequirements, c)).ToList();
            var car = (fitting.Count > 0 ? fitting : cars)[_random.Next(fitting.Count > 0 ? fitting.Count : cars.Count)];
            var skin = car.AvailableSkins is { Count: > 0 } skins ? skins[_random.Next(skins.Count)] : "default";

            var names = new[] { "Speed Demon", "Road Runner", "Night Rider", "Street King", "Drag Master" };
            var nicknames = new[] { "The Flash", "Quick Draw", "Burnout", "Smokey", "Rev Head" };

            // Rolled once: the opponent that races and the one the result is about are the same one
            var opponent = new EventOpponent
            {
                Name = names[_random.Next(names.Length)],
                Nickname = nicknames[_random.Next(nicknames.Length)],
                Skill = _random.Next(Opponent.MinSkill, Opponent.MaxSkill + 1),
                Aggression = 40 + _random.Next(30),
                CarDefinitionId = car.Id,
                CarSkin = skin
            };

            return new EventOpponentResult
            {
                IsPoolOpponent = false,
                SpecialOpponent = opponent,
                OpponentName = opponent.Name,
                OpponentNickname = opponent.Nickname,
                CarDefinitionId = opponent.CarDefinitionId,
                CarSkin = skin,
                Skill = opponent.Skill,
                Aggression = opponent.Aggression
            };
        }
    }
}
