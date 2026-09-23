namespace Street_Rod_AC.Models.GameState
{
    public class Racer(RacerType type, string name)
    {
        public RacerType Type { get; set; } = type;
        public string Name { get; set; } = name;
        public RacerStats Stats { get; set; } = new RacerStats();
        public List<Car> Cars { get; set; } = [];
        /// <summary>Loose parts on the shelf; a part keeps whatever was mounted on it when it came off</summary>
        public List<PartInstance> Parts { get; set; } = [];
        public decimal Money { get; set; } = 0m;
        public RacerStatus Status { get; set; } = RacerStatus.ReadyToRace;

        // Parameterless constructor for LiteDB
        public Racer() : this(RacerType.AI, "Unknown")
        {
        }
    }

    public class Player : Racer
    {
        public Guid? SelectedCarInstanceId { get; set; }

        /// <summary>What a new player starts with; enough for several cars while the game is being built</summary>
        public const decimal StartingMoney = 1_000_000m;

        public Player(string name) : base(RacerType.Player, name)
        {
            Money = StartingMoney;
        }

        // Parameterless constructor for LiteDB
        public Player() : base(RacerType.Player, "Player")
        {
            Money = StartingMoney;
        }
    }
}
