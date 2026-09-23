namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// Where a car sits on a dealer's lot: metres from the lot's origin, and which way it points.
    /// Authored per showroom, because the floor each one gives you is a different shape.
    /// </summary>
    public class LotBay
    {
        /// <summary>Across the lot, in metres</summary>
        public float X { get; set; }

        /// <summary>Along the lot, in metres</summary>
        public float Z { get; set; }

        /// <summary>Which way the car faces, in degrees. 0 points down +Z</summary>
        public float Heading { get; set; }
    }

    /// <summary>
    /// Everything about a dealer that never changes: where it is on the map, what it looks like inside,
    /// and the kind of cars it keeps.
    ///
    /// Kept apart from <see cref="DealerLocation"/> on purpose. The save holds the dealer's Id and no more,
    /// so adding a field here does not have to reach into everybody's saved game: the definitions are the
    /// truth and are merged over the save on load (<c>DealerCatalog</c>).
    /// </summary>
    public class DealerDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        /// <summary>A line for the map card: what sort of place this is</summary>
        public string Blurb { get; set; } = string.Empty;

        /// <summary>Where the pin sits on the map image, 0..1 from the left</summary>
        public float MapX { get; set; }

        /// <summary>Where the pin sits on the map image, 0..1 from the top</summary>
        public float MapY { get; set; }

        /// <summary>Folder name under content/showroom used as the lot's 3D scene</summary>
        public string ShowroomId { get; set; } = "industrial";

        /// <summary>
        /// The share of the price range this dealer stocks, cheapest to dearest, 0..1.
        /// The cheap lot takes the bottom of the market, the smart showroom the top.
        /// </summary>
        public float PriceBandLow { get; set; }
        public float PriceBandHigh { get; set; } = 1f;

        /// <summary>How rough the cars are here: the middle of the condition range, 0..1</summary>
        public float ConditionCenter { get; set; } = 0.6f;

        /// <summary>Hours of game time it takes to get here from the garage and back</summary>
        public float TravelHours { get; set; } = 1f;

        /// <summary>
        /// How many cars the lot carries. Keep it at or under the capacity of its showroom, or the overflow
        /// can only be read about and never looked at.
        /// </summary>
        public int StockLow { get; set; } = 8;
        public int StockHigh { get; set; } = 12;

        /// <summary>The save only ever needs the name and the region</summary>
        public DealerLocation ToLocation() => new()
        {
            Id = Id,
            Name = Name,
            Region = Region
        };
    }
}
