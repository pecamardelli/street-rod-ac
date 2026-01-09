using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Repository for managing car gameplay profiles
    /// </summary>
    public interface ICarProfileRepository
    {
        /// <summary>
        /// Gets a profile by car definition ID
        /// </summary>
        CarProfile? GetProfile(string carDefinitionId);

        /// <summary>
        /// Gets or creates a profile with defaults if it doesn't exist
        /// </summary>
        CarProfile GetOrCreateProfile(string carDefinitionId, Func<CarProfile> createDefault);

        /// <summary>
        /// Upserts (insert or update) a profile
        /// </summary>
        void UpsertProfile(CarProfile profile);

        /// <summary>
        /// Gets all profiles
        /// </summary>
        List<CarProfile> GetAllProfiles();

        /// <summary>
        /// Checks if a profile exists
        /// </summary>
        bool ProfileExists(string carDefinitionId);

        /// <summary>
        /// Gets total profile count
        /// </summary>
        int GetProfileCount();
    }
}
