namespace Street_Rod_AC.Services.Dealers
{
    /// <summary>
    /// The room a lot stands in, measured off its .kn5 rather than guessed. Everything about how a lot is
    /// laid out and how far back the camera stands comes from these numbers.
    /// </summary>
    public class ShowroomSpec
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Extent of the floor in metres</summary>
        public float FloorWidth { get; set; } = 28f;
        public float FloorDepth { get; set; } = 28f;

        /// <summary>How far out the walls stand from the middle. The camera stays inside this</summary>
        public float WallRadius { get; set; } = 14f;

        /// <summary>How many cars will fit and still be worth looking at from inside the room</summary>
        public int Capacity { get; set; } = 12;

        /// <summary>A room with a roof on it, as opposed to a yard</summary>
        public bool Indoor { get; set; } = true;

        /// <summary>What to fall back on for a showroom nobody has measured</summary>
        public static ShowroomSpec Unknown(string id) => new()
        {
            Id = id,
            FloorWidth = 28f,
            FloorDepth = 28f,
            WallRadius = 14f,
            Capacity = 10,
            Indoor = true
        };
    }
}
