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
        /// Parts mounted on the car itself, each with everything that is mounted on it in turn.
        /// <see cref="PartInstance.ParentSlot"/> says where on the car: the engine goes on <see cref="PartInstance.CarEngineSlot"/>.
        /// </summary>
        public List<PartInstance> Parts { get; set; }

        /// <summary>
        /// False until the car has been given the parts it left the factory with. Tells a car that never had
        /// any (an older save) from one whose engine has been pulled.
        /// </summary>
        public bool HasPartsAssigned { get; set; }

        [LiteDB.BsonIgnore]
        public PartInstance? Engine => Parts.FirstOrDefault(p => p.ParentSlot == PartInstance.CarEngineSlot);

        // Purchase/Sale Info
        public decimal PurchasePrice { get; set; }
        public DateTime PurchaseDate { get; set; }

        // Calculated Stats (not persisted, computed on load)
        public double TotalHP { get; set; }
        public double TotalWeight { get; set; }
        public double TotalReliability { get; set; }

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
            PurchaseDate = DateTime.Now;
        }

        public Car(string definitionId) : this()
        {
            DefinitionId = definitionId;
        }
    }
}
