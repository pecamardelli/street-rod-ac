using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// Interface for car entry filters used in events and race requirements.
    /// Filters can match against car definitions (static catalog data) and
    /// optionally car instances (player-owned cars with condition/mods).
    /// </summary>
    public interface ICarFilter
    {
        /// <summary>
        /// Unique type identifier for serialization
        /// </summary>
        string FilterType { get; }

        /// <summary>
        /// Human-readable description of what this filter requires
        /// </summary>
        string DisplayDescription { get; }

        /// <summary>
        /// Check if a car matches this filter's criteria
        /// </summary>
        /// <param name="car">The car definition from the catalog</param>
        /// <param name="instance">Optional car instance for condition/value checks</param>
        /// <returns>True if the car matches the filter criteria</returns>
        bool Matches(CarDefinition car, Car? instance = null);
    }
}
