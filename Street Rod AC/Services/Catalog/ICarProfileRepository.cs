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
        /// Upserts many profiles in one visit to the database
        /// </summary>
        void UpsertProfiles(IEnumerable<CarProfile> profiles);

        /// <summary>
        /// Reads the stored profile, asks the caller what to store instead and stores that, all in one visit
        /// to the database, so that nothing saved from another thread in between gets lost. The caller returns
        /// the stored profile with its changes, or one of its own with whatever it wants to keep of the stored
        /// one, or null to leave things as they are. False when there is no such profile or it was left alone.
        /// </summary>
        bool UpdateProfile(string carDefinitionId, Func<CarProfile, CarProfile?> change);

        /// <summary>
        /// Many profiles in one visit to the database: each of <paramref name="created"/> is stored when there is
        /// no profile for its car yet (one stored in the meantime is kept), and each change in
        /// <paramref name="changes"/> is applied to the profile as stored now, the way <see cref="UpdateProfile"/>
        /// does. A batch that read its profiles earlier thus never writes back a stale copy over fields somebody
        /// else changed since. Returns how many profiles were stored.
        /// </summary>
        int MergeProfiles(IEnumerable<CarProfile> created, IEnumerable<KeyValuePair<string, Func<CarProfile, CarProfile?>>> changes);

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
