using System.IO;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Opponents;

namespace Street_Rod_AC.Screens.Shared
{
    /// <summary>A racer out today, the car they race in, and what that car is</summary>
    public sealed record RacerOut(Opponent Opponent, Car Car, CarDefinition Definition);

    /// <summary>
    /// Who is out to race today, the same wherever the player meets them (a diner table, the curb): racers on the
    /// scene whose car can race and is one the game knows. A racer whose car came back from a race today unable to
    /// go is at the garage, not out.
    /// </summary>
    public static class RacersOut
    {
        public static List<RacerOut> Today(GameState gameState, IOpponentChallengeService challengeService,
            IContentCatalogRepository catalogRepository, IAppLogger? logger = null)
        {
            var racers = new List<RacerOut>();
            foreach (var opponent in gameState.Racers.ReadyToRace.Values.OfType<Opponent>())
            {
                var car = opponent.Cars.FirstOrDefault();
                if (car == null)
                {
                    logger?.Warning("Opponent {Name} has no cars", opponent.Name);
                    continue;
                }

                if (!challengeService.CanRace(car))
                {
                    logger?.Information("{Name}'s car can't race today: not out", opponent.Name);
                    continue;
                }

                var definition = catalogRepository.GetCar(car.DefinitionId);
                if (definition == null)
                {
                    logger?.Warning("Car definition not found for opponent {Name}", opponent.Name);
                    continue;
                }

                racers.Add(new RacerOut(opponent, car, definition));
            }

            return racers;
        }

        /// <summary>The racer's portrait as a file on disk, or null when they have none or it is missing</summary>
        public static string? PortraitOf(Opponent opponent, IAppLogger? logger = null)
        {
            if (string.IsNullOrEmpty(opponent.PortraitPath)) return null;

            // Kept relative to the app's folder, sometimes with a leading slash
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, opponent.PortraitPath.TrimStart('/', '\\'));
            if (File.Exists(path)) return path;

            logger?.Warning("Portrait not found for opponent {Name}: {Path}", opponent.Name, path);
            return null;
        }
    }
}
