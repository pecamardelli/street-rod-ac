using Street_Rod_AC.Models.Career.Filters;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Service for evaluating car filters and finding matching cars
    /// </summary>
    public interface ICarFilterService
    {
        /// <summary>
        /// Check if a specific car matches a filter
        /// </summary>
        bool Matches(ICarFilter filter, CarDefinition car, Car? instance = null);

        /// <summary>
        /// Find all cars in a collection that match a filter
        /// </summary>
        IEnumerable<Car> FindMatchingCars(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup);

        /// <summary>
        /// Find all car definitions that match a filter
        /// </summary>
        IEnumerable<CarDefinition> FindMatchingDefinitions(ICarFilter filter,
            IEnumerable<CarDefinition> definitions);

        /// <summary>
        /// Check if any car in a collection matches the filter
        /// </summary>
        bool HasMatchingCar(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup);

        /// <summary>
        /// Create a filter from a type name and parameters (for deserialization)
        /// </summary>
        ICarFilter? CreateFilter(string filterType, Dictionary<string, object>? parameters = null);
    }
}
