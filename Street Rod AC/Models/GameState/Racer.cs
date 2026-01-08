namespace Street_Rod_AC.Models.GameState
{
    public class Racer(RacerType type, string name)
    {
        public RacerType Type { get; set; } = type;
        public string Name { get; set; } = name;
        public RacerStats Stats { get; set; } = new RacerStats();
        public List<Car> Cars { get; set; } = new List<Car>();
        public List<Part> Parts { get; set; } = new List<Part>();
        public decimal Money { get; set; } = 0m;
        public RacerStatus Status { get; set; } = RacerStatus.ReadyToRace;

        // Parameterless constructor for LiteDB
        public Racer() : this(RacerType.AI, "Unknown")
        {
        }
    }

    public class Player : Racer
    {
        public Player(string name) : base(RacerType.Player, name)
        {
            // Player starts with money
            Money = 10000m;
        }

        // Parameterless constructor for LiteDB
        public Player() : base(RacerType.Player, "Player")
        {
            Money = 10000m;
        }
    }
}
