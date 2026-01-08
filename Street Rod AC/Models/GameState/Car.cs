namespace Street_Rod_AC.Models.GameState
{
    public class Car
    {
        public Guid InstanceId { get; set; }
        public string DefinitionId { get; set; } // Reference to catalog (AC car folder)

        // Condition & Mileage (Street Rod style)
        public double OdometerKM { get; set; }
        public double EngineHealth { get; set; } // 0.0 to 1.0
        public double TransmissionHealth { get; set; } // 0.0 to 1.0
        public double BodyCondition { get; set; } // 0.0 to 1.0
        public double TireCondition { get; set; } // 0.0 to 1.0

        // Installed Parts
        public List<Part> InstalledParts { get; set; }

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
            InstalledParts = new List<Part>();
            PurchasePrice = 0m;
            PurchaseDate = DateTime.Now;
        }

        public Car(string definitionId) : this()
        {
            DefinitionId = definitionId;
        }
    }
}
