using Street_Rod_AC.Models.Career.Events;
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
        private readonly Random _random = new();

        public EventOpponentService(
            ICarFilterService filterService,
            IContentCatalogRepository catalogRepository)
        {
            _filterService = filterService;
            _catalogRepository = catalogRepository;
        }

        public EventOpponentResult? GetOpponentForEvent(
            RaceEventDefinition eventDef,
            GameState gameState)
        {
            // Try pool opponents first (unless exclusive)
            if (!eventDef.ExclusiveOpponents)
            {
                var poolOpponent = FindEligiblePoolOpponent(eventDef, gameState);
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
            return GenerateFallbackOpponent(eventDef);
        }

        private EventOpponentResult? FindEligiblePoolOpponent(
            RaceEventDefinition eventDef,
            GameState gameState)
        {
            var candidates = new List<(Opponent, Car)>();

            // Check ReadyToRace pool for eligible opponents
            foreach (var racer in gameState.Racers.ReadyToRace.Values)
            {
                if (racer is not Opponent opponent)
                    continue;

                var car = opponent.Cars.FirstOrDefault();
                if (car == null)
                    continue;

                // Check if opponent's car meets event requirements
                if (eventDef.EntryRequirements != null)
                {
                    var carDef = _catalogRepository.GetCar(car.DefinitionId);
                    if (carDef == null)
                        continue;

                    if (!_filterService.Matches(eventDef.EntryRequirements, carDef, car))
                        continue;
                }

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

        private EventOpponentResult GenerateFallbackOpponent(RaceEventDefinition eventDef)
        {
            // Generate a basic opponent when none are available
            // This ensures events can always be entered
            var names = new[] { "Speed Demon", "Road Runner", "Night Rider", "Street King", "Drag Master" };
            var nicknames = new[] { "The Flash", "Quick Draw", "Burnout", "Smokey", "Rev Head" };

            return new EventOpponentResult
            {
                IsPoolOpponent = false,
                SpecialOpponent = new EventOpponent
                {
                    Name = names[_random.Next(names.Length)],
                    Nickname = nicknames[_random.Next(nicknames.Length)],
                    Skill = 85 + _random.Next(10),
                    Aggression = 40 + _random.Next(30),
                    CarDefinitionId = "default_car"
                },
                OpponentName = names[_random.Next(names.Length)],
                OpponentNickname = nicknames[_random.Next(nicknames.Length)],
                CarDefinitionId = "default_car",
                CarSkin = "default",
                Skill = 85 + _random.Next(10),
                Aggression = 40 + _random.Next(30)
            };
        }
    }
}
