namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// One physical part somebody owns, as it is saved: what it is, what shape it is in, how it is tuned and
    /// what is bolted onto it. A car's engine is one of these with the whole engine below it; a loose part
    /// on a shelf keeps whatever was on it when it came off.
    /// </summary>
    public class PartInstance
    {
        /// <summary>Slot of a car the engine block sits on, as the part scripts number it</summary>
        public const int CarEngineSlot = 1;

        public Guid InstanceId { get; set; } = Guid.NewGuid();

        /// <summary>Id of the part in the parts catalog, e.g. "engines/Mopar/block_340"</summary>
        public string DefinitionId { get; set; } = string.Empty;

        /// <summary>1 = new, 0 = worn out (mileage)</summary>
        public double Wear { get; set; } = 1.0;

        /// <summary>1 = straight, 0 = wrecked (damage)</summary>
        public double Tear { get; set; } = 1.0;

        /// <summary>Settings changed from the part's defaults, by script field ("rpm_idle", "ratio[2]")</summary>
        public Dictionary<string, double> Tuning { get; set; } = new();

        /// <summary>
        /// Slot of whatever this part is mounted on: a slot of the parent part, or of the car for the parts
        /// that sit on the car itself (<see cref="CarEngineSlot"/>). 0 while the part is loose.
        /// </summary>
        public int ParentSlot { get; set; }

        /// <summary>The part's own slot it is mounted by; 0 on the car itself or while loose</summary>
        public int OwnSlot { get; set; }

        public List<PartInstance> Children { get; set; } = [];

        public PartInstance() { }

        public PartInstance(string definitionId)
        {
            DefinitionId = definitionId;
        }

        public IEnumerable<PartInstance> SelfAndDescendants()
        {
            yield return this;
            foreach (var child in Children)
            {
                foreach (var part in child.SelfAndDescendants()) yield return part;
            }
        }
    }
}
