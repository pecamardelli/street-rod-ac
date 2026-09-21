using LiteDB;

namespace Street_Rod_AC.Models.Catalog
{
    /// <summary>
    /// Represents gameplay properties for a car.
    /// This is the gameplay overlay on top of immutable CarDefinition.
    /// Stored in catalog.db, NOT in AC installation directories.
    /// </summary>
    public class CarProfile
    {
        /// <summary>
        /// References CarDefinition.Id (stable AC folder name)
        /// </summary>
        [BsonId]
        public string CarDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// Base price for this car in perfect condition
        /// Market listings will vary from this based on condition
        /// </summary>
        public decimal BasePrice { get; set; }

        /// <summary>
        /// Spawn probability (0.0 = rare, 1.0 = common)
        /// Controls how often this car appears in used car market
        /// </summary>
        public float DealerPrecedence { get; set; }

        /// <summary>
        /// How this profile was created
        /// </summary>
        public ProfileDataSource Source { get; set; } = ProfileDataSource.Generated;

        /// <summary>
        /// When this profile was created
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// When this profile was last modified
        /// </summary>
        public DateTime LastUpdatedDate { get; set; }

        /// <summary>
        /// The engine the car leaves the factory with: id of an engine build of the parts catalog
        /// (engine_builds.json). Suggested from the car's make, name and power; null until that has happened
        /// or when no build comes close.
        /// </summary>
        public string? StockEngineBuildId { get; set; }

        /// <summary>True when somebody picked the engine by hand; suggestions leave it alone then</summary>
        public bool StockEngineIsManual { get; set; }

        // Future enrichment properties (optional, not used initially)
        public string? StreetRodEra { get; set; }
        public string? PerformanceTier { get; set; }
        public bool IsStreetLegal { get; set; } = true;
    }

    /// <summary>
    /// Indicates how the profile data was created
    /// </summary>
    public enum ProfileDataSource
    {
        Generated,  // Algorithm-generated defaults
        Manual,     // User/curator edited
        Imported    // From external data pack (future)
    }
}
