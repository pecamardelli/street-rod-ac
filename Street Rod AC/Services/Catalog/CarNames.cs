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

        /// <summary>
        /// The model as people say it in passing: no make, and nothing from the year on, which in the catalog's names
        /// is the engine and gearbox ("Charger R/T 1969 440 Magnum 4-speed" is a Charger R/T). The whole name when
        /// that would leave nothing.
        /// </summary>
        public static string Short(CarDefinition definition)
        {
            var name = definition.Name.Trim();
            if (definition.Brand.Length > 0 && name.StartsWith(definition.Brand, StringComparison.OrdinalIgnoreCase))
                name = name[definition.Brand.Length..].Trim();

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var year = Array.FindIndex(words, w => w.Length == 4 && (w.StartsWith("19") || w.StartsWith("20")) && w.All(char.IsDigit));
            var kept = year > 0 ? string.Join(' ', words.Take(year)) : name;
            return kept.Length > 0 ? kept : definition.Name;
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
