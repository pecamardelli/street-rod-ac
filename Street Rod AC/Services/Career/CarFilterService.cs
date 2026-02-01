using Street_Rod_AC.Models.Career.Filters;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Implementation of car filter evaluation service
    /// </summary>
    public class CarFilterService : ICarFilterService
    {
        public bool Matches(ICarFilter filter, CarDefinition car, Car? instance = null)
        {
            return filter.Matches(car, instance);
        }

        public IEnumerable<Car> FindMatchingCars(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup)
        {
            foreach (var car in cars)
            {
                var definition = definitionLookup(car.DefinitionId);
                if (definition != null && filter.Matches(definition, car))
                {
                    yield return car;
                }
            }
        }

        public IEnumerable<CarDefinition> FindMatchingDefinitions(ICarFilter filter,
            IEnumerable<CarDefinition> definitions)
        {
            return definitions.Where(d => filter.Matches(d));
        }

        public bool HasMatchingCar(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup)
        {
            return FindMatchingCars(filter, cars, definitionLookup).Any();
        }

        public ICarFilter? CreateFilter(string filterType, Dictionary<string, object>? parameters = null)
        {
            return filterType switch
            {
                "Brand" => CreateBrandFilter(parameters),
                "Decade" => CreateDecadeFilter(parameters),
                "Power" => CreatePowerFilter(parameters),
                "Origin" => CreateOriginFilter(parameters),
                "Value" => CreateValueFilter(parameters),
                "Composite" => CreateCompositeFilter(parameters),
                _ => null
            };
        }

        private static BrandFilter CreateBrandFilter(Dictionary<string, object>? parameters)
        {
            var filter = new BrandFilter();
            if (parameters?.TryGetValue("Brand", out var brand) == true)
                filter.Brand = brand?.ToString() ?? string.Empty;
            return filter;
        }

        private static DecadeFilter CreateDecadeFilter(Dictionary<string, object>? parameters)
        {
            var filter = new DecadeFilter();
            if (parameters?.TryGetValue("StartYear", out var startYear) == true)
                filter.StartYear = Convert.ToInt32(startYear);
            if (parameters?.TryGetValue("EndYear", out var endYear) == true)
                filter.EndYear = Convert.ToInt32(endYear);
            return filter;
        }

        private static PowerFilter CreatePowerFilter(Dictionary<string, object>? parameters)
        {
            var filter = new PowerFilter();
            if (parameters?.TryGetValue("MinHP", out var minHp) == true)
                filter.MinHP = Convert.ToInt32(minHp);
            if (parameters?.TryGetValue("MaxHP", out var maxHp) == true)
                filter.MaxHP = Convert.ToInt32(maxHp);
            return filter;
        }

        private static OriginFilter CreateOriginFilter(Dictionary<string, object>? parameters)
        {
            var filter = new OriginFilter();
            if (parameters?.TryGetValue("Origin", out var origin) == true)
                filter.Origin = origin?.ToString() ?? string.Empty;
            return filter;
        }

        private static ValueFilter CreateValueFilter(Dictionary<string, object>? parameters)
        {
            var filter = new ValueFilter();
            if (parameters?.TryGetValue("MinValue", out var minValue) == true)
                filter.MinValue = Convert.ToDecimal(minValue);
            if (parameters?.TryGetValue("MaxValue", out var maxValue) == true)
                filter.MaxValue = Convert.ToDecimal(maxValue);
            return filter;
        }

        private CompositeCarFilter CreateCompositeFilter(Dictionary<string, object>? parameters)
        {
            var filter = new CompositeCarFilter();

            if (parameters?.TryGetValue("RequireAll", out var requireAll) == true)
                filter.RequireAll = Convert.ToBoolean(requireAll);

            // Note: For full composite filter deserialization, you'd need to recursively
            // create child filters from a "Filters" parameter. This is a simplified version.

            return filter;
        }
    }
}
