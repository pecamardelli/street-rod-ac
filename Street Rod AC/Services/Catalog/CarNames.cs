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

        /// <summary>
        /// <see cref="Of(IContentCatalogRepository, string)"/> with each car looked up once: for a review that names
        /// car after car. For that review only, on one thread.
        /// </summary>
        public static Func<string, string> Book(IContentCatalogRepository catalog)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return id => names.TryGetValue(id, out var name) ? name : names[id] = Of(catalog, id);
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
