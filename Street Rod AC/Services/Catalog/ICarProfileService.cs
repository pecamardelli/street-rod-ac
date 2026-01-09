using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Service for generating and managing car gameplay profiles
    /// </summary>
    public interface ICarProfileService
    {
        /// <summary>
        /// Generates a default profile for a car definition
        /// Uses power/weight ratio and other specs to calculate price/precedence
        /// </summary>
        CarProfile GenerateDefaultProfile(CarDefinition carDefinition);

        /// <summary>
        /// Calculates base price from car specifications
        /// </summary>
        decimal CalculateBasePrice(CarDefinition carDefinition);

        /// <summary>
        /// Calculates dealer precedence (spawn probability)
        /// </summary>
        float CalculatePrecedence(CarDefinition carDefinition);

        /// <summary>
        /// Ensures all car definitions have profiles, creating missing ones
        /// </summary>
        Task EnsureProfilesExistAsync();
    }
}
