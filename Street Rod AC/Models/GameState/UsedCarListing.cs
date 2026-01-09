namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// Represents a car available for purchase in the used car market.
    /// This is market state, stored in GameState, not Catalog.
    /// </summary>
    public class UsedCarListing
    {
        /// <summary>
        /// Unique identifier for this listing
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// References CarDefinition.Id from catalog
        /// </summary>
        public string CarDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// Price for this specific listing (varies from BasePrice based on condition)
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// Mileage in kilometers
        /// </summary>
        public int Mileage { get; set; }

        /// <summary>
        /// Condition (0.0 = broken, 1.0 = perfect)
        /// Affects price and performance
        /// </summary>
        public float Condition { get; set; }

        /// <summary>
        /// Which skin/livery this instance uses
        /// References AC skin folder name
        /// </summary>
        public string SkinId { get; set; } = string.Empty;

        /// <summary>
        /// When this listing was created
        /// </summary>
        public DateTime ListedDate { get; set; }

        /// <summary>
        /// Which dealer location has this car
        /// </summary>
        public string DealerLocation { get; set; } = string.Empty;

        /// <summary>
        /// Whether this listing has been sold
        /// </summary>
        public bool IsSold { get; set; }

        /// <summary>
        /// When this listing was sold (if sold)
        /// </summary>
        public DateTime? SoldDate { get; set; }
    }

    /// <summary>
    /// Represents a dealer location in the game
    /// </summary>
    public class DealerLocation
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string? Specialization { get; set; } // "Imports", "Classics", "Performance", etc.
    }
}
