using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>A car as people say it: "1969 Chevrolet Camaro"</summary>
    public static class CarNames
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        /// <summary>Year, make and model; the folder name when the catalog doesn't know the car or can't be read</summary>
        public static string Of(IContentCatalogRepository catalog, string carDefinitionId)
        {
            try
            {
                if (catalog.GetCar(carDefinitionId) is { } definition) return Of(definition);
            }
            catch (Exception ex)
            {
                Logger.Warning("Could not look the {Car} up: {Error}", carDefinitionId, ex.Message);
            }

            return carDefinitionId;
        }

        public static string Of(CarDefinition definition)
        {
            var name = definition.Brand.Length == 0 || definition.Name.StartsWith(definition.Brand, StringComparison.OrdinalIgnoreCase)
                ? definition.Name
                : $"{definition.Brand} {definition.Name}";
            return definition.Year is { } year && !name.Contains(year.ToString()) ? $"{year} {name}" : name;
        }
    }
}
