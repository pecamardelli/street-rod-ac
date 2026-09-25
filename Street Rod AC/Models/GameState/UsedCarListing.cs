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

        /// <summary>
        /// The parts this very car comes with, as on <see cref="Car.Parts"/>: what is for sale is what gets bought.
        /// Empty on listings from before cars had parts; those get a factory engine when bought.
        /// </summary>
        public List<PartInstance> Parts { get; set; } = [];

        /// <summary>What the seller says about the engine, e.g. "GM 327, 275 hp"; null when there is nothing to say</summary>
        public string? EngineSummary { get; set; }

        /// <summary>Horsepower of the engine on the dyno; null when nobody has put it on one (older listings)</summary>
        public double? PowerHp { get; set; }

        /// <summary>True when the engine is not as it left the factory</summary>
        public bool IsModified { get; set; }

        /// <summary>
        /// True when <see cref="Parts"/> already holds the car's wheels, brakes, springs and shocks (a car that was
        /// somebody's, relisted); the buyer's car must not be given a second factory set then
        /// </summary>
        public bool HasRunningGearAssigned { get; set; }

        /// <summary>
        /// The body's damage per zone, as on <see cref="Car.BodyDamageKmh"/>: a relisted car is sold with its dents.
        /// Null on a car a dealer had from new stock, and on listings from before it was kept: a straight body.
        /// </summary>
        public double[]? BodyDamageKmh { get; set; }

        /// <summary>
        /// The car's history, as on <see cref="Car.History"/>: a relisted car keeps it, and new stock comes with the
        /// owners it had before. Empty on listings from before it was kept.
        /// </summary>
        public CarHistory History { get; set; } = new();

        /// <summary>
        /// The car's own id (<see cref="Car.InstanceId"/>) when somebody had it and it was relisted: whoever buys it gets
        /// that very car back, and a rival who wanted it knows it (<see cref="Grudge.CarInstanceId"/>). Null on new stock
        /// and on listings from before it was kept.
        /// </summary>
        public Guid? CarInstanceId { get; set; }
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
