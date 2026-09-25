namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// The police sent to a street race: extra AI cars in race.ini after the two racers, which the race mode keeps
    /// out of sight until the patrol shows up and then sets on the racers
    /// </summary>
    public sealed class RacePolice
    {
        /// <summary>The police car's folder</summary>
        public string CarId { get; init; } = string.Empty;

        /// <summary>One livery per police car: as many cars as skins</summary>
        public List<string> Skins { get; init; } = new();

        /// <summary>How far round the lap the patrol shows up, as a share of it</summary>
        public double SpotShare { get; init; }

        public int Count => Skins.Count;
    }
}
