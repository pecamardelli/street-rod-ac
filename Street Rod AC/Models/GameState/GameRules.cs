using LiteDB;

namespace Street_Rod_AC.Models.GameState
{
    /// <summary>How hard a career is: picked on the New Game screen as a preset, which the player may then adjust</summary>
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard,

        /// <summary>A preset with some of its figures changed by hand</summary>
        Custom
    }

    public enum PinkSlipFrequency
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// The rules of one career, kept in its save: what things cost, what races pay, how fast cars wear and how
    /// hard the rivals drive. Set when the game starts and fixed from then on, so a career can't be made easier
    /// half-way. Saves from before these rules existed load as <see cref="Difficulty.Normal"/>.
    /// </summary>
    public class GameRules
    {
        public Difficulty Difficulty { get; set; } = Difficulty.Normal;

        /// <summary>What dealers and private sellers ask for a car. What the player gets for one is its value</summary>
        public double CarPriceMultiplier { get; set; } = 1.0;

        /// <summary>What the parts shop, the parts ads and the garage's repairs cost. What a part is worth doesn't change</summary>
        public double PartPriceMultiplier { get; set; } = 1.0;

        /// <summary>The cash an event pays out. Wagers are between the racers and don't change</summary>
        public double RacePrizeMultiplier { get; set; } = 1.0;

        /// <summary>Added to the rivals' AI level in AC; see <see cref="Services.Opponents.OpponentAIAdapter"/></summary>
        public int OpponentSkillModifier { get; set; }

        /// <summary>Added to the rivals' AI aggression in AC</summary>
        public int OpponentAggressionModifier { get; set; }

        /// <summary>
        /// How fast cars wear: scales AC's damage (which tops out at 100%, reached by <see cref="Difficulty.Normal"/>)
        /// and the mileage wear of the parts
        /// </summary>
        public double CarWearMultiplier { get; set; } = 1.0;

        /// <summary>AC's damage in a race, in percent: <see cref="CarWearMultiplier"/> of the full rate, which is also AC's most</summary>
        [BsonIgnore]
        public int RaceDamagePercent => (int)Math.Round(Math.Clamp(100 * Sane(CarWearMultiplier), 0, 100));

        /// <summary>How often pink slips are raced for: by rivals among themselves, in events, and how readily a rival takes one on</summary>
        public PinkSlipFrequency PinkSlipFrequency { get; set; } = PinkSlipFrequency.Medium;

        /// <summary>The rivals race each other every day, winning and losing money and cars</summary>
        public bool RaceSimulationEnabled { get; set; } = true;

        /// <summary>The rivals race more in summer and less in winter</summary>
        public bool SeasonalRacingEnabled { get; set; } = true;

        /// <summary>The dealers' lots fill up again as cars are sold and old stock goes</summary>
        public bool MarketRefreshEnabled { get; set; } = true;

        public GameRules Copy() => (GameRules)MemberwiseClone();

        public static GameRules For(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => new GameRules
            {
                Difficulty = Difficulty.Easy,
                CarPriceMultiplier = 0.85,
                PartPriceMultiplier = 0.85,
                RacePrizeMultiplier = 1.25,
                OpponentSkillModifier = -3,
                OpponentAggressionModifier = -15,
                CarWearMultiplier = 0.6,
                PinkSlipFrequency = PinkSlipFrequency.Low
            },
            Difficulty.Hard => new GameRules
            {
                Difficulty = Difficulty.Hard,
                CarPriceMultiplier = 1.15,
                PartPriceMultiplier = 1.15,
                RacePrizeMultiplier = 0.85,
                OpponentSkillModifier = 3,
                OpponentAggressionModifier = 15,
                CarWearMultiplier = 1.3,
                PinkSlipFrequency = PinkSlipFrequency.High
            },
            _ => new GameRules()
        };

        /// <summary>
        /// How much more or less often pink slips come up than on <see cref="PinkSlipFrequency.Medium"/>, as a factor
        /// on the chance
        /// </summary>
        [BsonIgnore]
        public double PinkSlipFactor => PinkSlipFrequency switch
        {
            PinkSlipFrequency.Low => 0.5,
            PinkSlipFrequency.High => 1.75,
            _ => 1.0
        };

        /// <summary>A price scaled by a multiplier, never negative; a multiplier out of a hand-edited save counts as 1</summary>
        public static decimal Scale(decimal price, double multiplier) =>
            Math.Max(0m, price * (decimal)Sane(multiplier));

        /// <summary>A multiplier that is not a positive, finite number (a save edited by hand) counts as 1</summary>
        public static double Sane(double multiplier) =>
            double.IsFinite(multiplier) && multiplier > 0 ? Math.Min(multiplier, 100) : 1.0;
    }
}
