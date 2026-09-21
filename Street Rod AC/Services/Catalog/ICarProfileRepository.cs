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
        /// Reads the stored profile, asks the caller what to store instead and stores that, all in one visit
        /// to the database, so that nothing saved from another thread in between gets lost. The caller returns
        /// the stored profile with its changes, or one of its own with whatever it wants to keep of the stored
        /// one, or null to leave things as they are. False when there is no such profile or it was left alone.
        /// </summary>
        bool UpdateProfile(string carDefinitionId, Func<CarProfile, CarProfile?> change);

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
