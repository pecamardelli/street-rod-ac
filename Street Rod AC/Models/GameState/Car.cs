namespace Street_Rod_AC.Models.GameState
{
    public class Car
    {
        public Guid InstanceId { get; set; }
        public string DefinitionId { get; set; } // Reference to catalog (AC car folder)
        public string SkinId { get; set; } = "default"; // Selected skin/livery

        // Condition & Mileage (Street Rod style)
        public double OdometerKM { get; set; }
        public double EngineHealth { get; set; } // 0.0 to 1.0
        public double TransmissionHealth { get; set; } // 0.0 to 1.0
        public double BodyCondition { get; set; } // 0.0 to 1.0
        public double TireCondition { get; set; } // 0.0 to 1.0

        /// <summary>
        /// Body damage by zone as Assetto Corsa keeps it: front, rear, left, right, each the collision speed in km/h
        /// the zone has taken. It goes back into AC at the start of every race, scratches and dents with it, and
        /// only a body shop takes it off (<see cref="Parts.Cars.CarCondition"/>).
        /// </summary>
        public double[] BodyDamageKmh { get; set; } = new double[4];

        /// <summary>
        /// Parts mounted on the car itself, each with everything that is mounted on it in turn.
        /// <see cref="PartInstance.ParentSlot"/> says where on the car: the engine goes on <see cref="PartInstance.CarEngineSlot"/>.
        /// </summary>
        public List<PartInstance> Parts { get; set; }

        /// <summary>
        /// False until the car has been given the parts it left the factory with. Tells a car that never had
        /// any (an older save) from one whose engine has been pulled.
        /// </summary>
        public bool HasPartsAssigned { get; set; }

        /// <summary>False until the car has been given the wheels, brakes, springs and shocks it left the factory with</summary>
        public bool HasRunningGearAssigned { get; set; }

        [LiteDB.BsonIgnore]
        public PartInstance? Engine => Parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot);

        /// <summary>
        /// Horsepower on the dyno, as the car was last looked at: what its engine makes, tuning and wear included. Null
        /// until somebody has put it on the dyno; 0 when it does not run. The rivals choose, tune and race by it.
        /// </summary>
        public double? PowerHp { get; set; }

        // Purchase/Sale Info
        public decimal PurchasePrice { get; set; }

        /// <summary>
        /// The car is in the police impound (its owner was busted after a street race), and can be collected from
        /// this date on, in game time, for <see cref="ImpoundFee"/>. Null when it is not impounded. An impounded car
        /// goes nowhere: it does not race, run or sell until it is collected.
        /// </summary>
        public DateTime? ImpoundedUntil { get; set; }

        /// <summary>What collecting the car from the impound costs; 0 when it is not impounded</summary>
        public decimal ImpoundFee { get; set; }

        [LiteDB.BsonIgnore]
        public bool IsImpounded => ImpoundedUntil != null;

        /// <summary>
        /// When the car changed hands, in game time (1970s). Whoever creates the car sets it from the game's
        /// date; the real clock has no place in the game world.
        /// </summary>
        public DateTime PurchaseDate { get; set; }

        /// <summary>Who has owned the car and how it has raced; see <see cref="CarHistory"/></summary>
        public CarHistory History { get; set; } = new();

        public Car()
        {
            InstanceId = Guid.NewGuid();
            DefinitionId = string.Empty;
            OdometerKM = 0;
            EngineHealth = 1.0;
            TransmissionHealth = 1.0;
            BodyCondition = 1.0;
            TireCondition = 1.0;
            Parts = [];
            PurchasePrice = 0m;
        }

        public Car(string definitionId) : this()
        {
            DefinitionId = definitionId;
        }
    }
}
