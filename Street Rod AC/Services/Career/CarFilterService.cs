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
        private readonly Func<Car, decimal>? _valueOf;

        /// <param name="valueOf">
        /// What a car is worth (the market's <c>CarValuation</c>), for value requirements; null values a car at
        /// what was paid for it
        /// </param>
        public CarFilterService(Func<Car, decimal>? valueOf = null)
        {
            _valueOf = valueOf;
        }

        public bool Matches(ICarFilter filter, CarDefinition car, Car? instance = null)
        {
            return filter.Matches(car, instance, _valueOf);
        }

        public IEnumerable<Car> FindMatchingCars(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup)
        {
            foreach (var car in cars)
            {
                var definition = definitionLookup(car.DefinitionId);
                if (definition != null && filter.Matches(definition, car, _valueOf))
                {
                    yield return car;
                }
            }
        }

        public bool HasMatchingCar(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup)
        {
            return FindMatchingCars(filter, cars, definitionLookup).Any();
        }
    }
}
