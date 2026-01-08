namespace Street_Rod_AC.Models.GameState
{
    public enum PartType
    {
        Engine,
        Turbo,
        Supercharger,
        ECU,
        Exhaust,
        Intake,
        Intercooler,
        Transmission,
        Differential,
        Suspension,
        Brakes,
        Tires,
        NOS,
        Other
    }

    public class Part
    {
        public Guid InstanceId { get; set; }
        public string DefinitionId { get; set; } // Reference to catalog
        public PartType Type { get; set; }

        // Condition
        public double WearLevel { get; set; } // 0.0 = broken, 1.0 = new
        public double Reliability { get; set; } // 0.0 to 1.0

        // Purchase Info
        public decimal PurchasePrice { get; set; }
        public DateTime PurchaseDate { get; set; }

        // Installation tracking
        public bool IsInstalled { get; set; }
        public Guid? InstalledOnCarId { get; set; }

        public Part()
        {
            InstanceId = Guid.NewGuid();
            DefinitionId = string.Empty;
            Type = PartType.Other;
            WearLevel = 1.0;
            Reliability = 1.0;
            PurchasePrice = 0m;
            PurchaseDate = DateTime.Now;
            IsInstalled = false;
            InstalledOnCarId = null;
        }

        public Part(string definitionId, PartType type) : this()
        {
            DefinitionId = definitionId;
            Type = type;
        }
    }
}
