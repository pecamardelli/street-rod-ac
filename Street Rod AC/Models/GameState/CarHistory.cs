namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// What a car has been through: who has owned it, how each got it, and how it has raced. It goes with the car
    /// wherever it goes, a dealer's lot too (<see cref="UsedCarListing.History"/>), and a little of it shows in
    /// the price (<see cref="Services.Market.CarValuation.HistoryFactor"/>). The odometer is the car's own
    /// (<see cref="Car.OdometerKM"/>).
    /// </summary>
    public class CarHistory
    {
        /// <summary>Owners from before anybody in the game had it: people nobody on the street knows</summary>
        public int EarlierOwners { get; set; }

        /// <summary>The owners the game has seen, first to last; on a car, the last one has it now</summary>
        public List<CarOwner> Owners { get; set; } = [];

        /// <summary>Races the car has run, by whoever drove it; a race that settled nothing does not count</summary>
        public int Races { get; set; }

        public int Wins { get; set; }

        /// <summary>Cars won with it in pink-slip races</summary>
        public int PinkSlipsWon { get; set; }

        /// <summary>The car's best quarter mile, in seconds, whoever drove it; null until it has run one</summary>
        public double? BestQuarterSeconds { get; set; }

        /// <summary>The trap speed of that run, in mph</summary>
        public double? BestQuarterMph { get; set; }

        /// <summary>When it was run, in game time</summary>
        public DateTime? BestQuarterDate { get; set; }

        /// <summary>
        /// Everybody who has had the car, the one who has it now included. Somebody who had it twice (sold it and
        /// bought it back, lost it and won it back) is one owner.
        /// </summary>
        [LiteDB.BsonIgnore]
        public int OwnerCount => Math.Max(0, EarlierOwners) + Owners.Select(o => o.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        /// <summary>Nothing is known about the car: a history from a save from before it was kept</summary>
        [LiteDB.BsonIgnore]
        public bool IsBlank => EarlierOwners <= 0 && Owners.Count == 0 && Races <= 0;

        /// <summary>
        /// The car changes hands: <paramref name="name"/> has it from <paramref name="date"/> (game time). A car won
        /// back is one more change of hands, though not one more owner (<see cref="OwnerCount"/>).
        /// </summary>
        public void ChangeHands(string name, DateTime date, CarAcquisition how) =>
            Owners.Add(new CarOwner { Name = name, Since = date, How = how });

        /// <summary>A settled race run in the car</summary>
        public void RecordRace(bool won, bool pinkSlip)
        {
            Races++;
            if (!won) return;
            Wins++;
            if (pinkSlip) PinkSlipsWon++;
        }

        /// <summary>
        /// A quarter mile the car ran (<paramref name="seconds"/>, at <paramref name="mph"/>): kept when it beats the
        /// best. A time no car could run is not a run. True when it is the car's new best.
        /// </summary>
        public bool RecordQuarter(double? seconds, double? mph, DateTime date)
        {
            if (seconds is not { } et || !double.IsFinite(et) || et < MinQuarterSeconds || et > MaxQuarterSeconds) return false;
            if (BestQuarterSeconds is { } best && best <= et) return false;
            BestQuarterSeconds = et;
            BestQuarterMph = mph is { } speed && double.IsFinite(speed) && speed > 0 ? speed : null;
            BestQuarterDate = date;
            return true;
        }

        /// <summary>The quickest and slowest quarter a car of the game could run: anything else is a bad file</summary>
        public const double MinQuarterSeconds = 5, MaxQuarterSeconds = 60;

        /// <summary>A copy that shares nothing with this one: a listing and the car bought off it each keep their own</summary>
        public CarHistory Copy() => new()
        {
            EarlierOwners = EarlierOwners,
            Owners = Owners.Select(o => new CarOwner { Name = o.Name, Since = o.Since, How = o.How }).ToList(),
            Races = Races,
            Wins = Wins,
            PinkSlipsWon = PinkSlipsWon,
            BestQuarterSeconds = BestQuarterSeconds,
            BestQuarterMph = BestQuarterMph,
            BestQuarterDate = BestQuarterDate
        };
    }

    /// <summary>One owner of a car in the game</summary>
    public class CarOwner
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>When they got it, in game time</summary>
        public DateTime Since { get; set; }

        public CarAcquisition How { get; set; }
    }

    /// <summary>How an owner came by a car. Stored by name: never rename a value.</summary>
    public enum CarAcquisition
    {
        /// <summary>Theirs when the game began, or nobody knows</summary>
        Unknown,

        /// <summary>Bought off a dealer's lot</summary>
        Dealer,

        /// <summary>Won in a pink-slip race</summary>
        PinkSlip,

        /// <summary>Bought from its owner, out of the paper</summary>
        PrivateSale
    }
}
