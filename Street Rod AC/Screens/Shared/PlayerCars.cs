using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services.Catalog;

namespace Street_Rod_AC.Screens.Shared
{
    /// <summary>One of the player's cars as the screens list it: the car, its catalog entry and its picture</summary>
    public sealed record PlayerCarEntry(Car Car, CarDefinition Definition, string PreviewImagePath);

    /// <summary>
    /// The player's cars with what the garage and the car list show of them. A car whose model is no longer
    /// in the catalog is left out, with a warning in the log.
    /// </summary>
    public static class PlayerCars
    {
        public static List<PlayerCarEntry> Load(GameState gameState, IContentCatalogRepository catalog, IAppLogger logger)
        {
            var entries = new List<PlayerCarEntry>();
            if (gameState.Player.Cars == null || gameState.Player.Cars.Count == 0)
                return entries;

            foreach (var car in gameState.Player.Cars)
            {
                var carDef = catalog.GetCar(car.DefinitionId);
                if (carDef == null)
                {
                    logger.Warning("Car definition not found for car instance {InstanceId}, definition {DefinitionId}",
                        car.InstanceId, car.DefinitionId);
                    continue;
                }

                // The skin's picture, else the car's; the fallback chain lives with the other car folder rules
                var preview = AcCarFolder.PreviewImage(AppSettings.Instance.CarsPath, carDef.Id, car.SkinId);
                entries.Add(new PlayerCarEntry(car, carDef, preview ?? string.Empty));
            }

            return entries;
        }
    }
}
