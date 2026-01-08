namespace Street_Rod_AC.Models.GameState
{
    public class RacerCollection
    {
        public Dictionary<string, Racer> Inactive { get; set; }
        public Dictionary<string, Racer> Retired { get; set; }
        public Dictionary<string, Racer> ReadyToRace { get; set; }

        public RacerCollection()
        {
            Inactive = [];
            Retired = [];
            ReadyToRace = [];
        }

        public void AddRacer(Racer racer)
        {
            var collection = racer.Status switch
            {
                RacerStatus.Inactive => Inactive,
                RacerStatus.Retired => Retired,
                RacerStatus.ReadyToRace => ReadyToRace,
                _ => ReadyToRace
            };

            collection[racer.Name] = racer;
        }

        public void MoveRacer(string name, RacerStatus newStatus)
        {
            Racer? racer = null;

            // Find and remove from current collection
            if (Inactive.TryGetValue(name, out racer))
                Inactive.Remove(name);
            else if (Retired.TryGetValue(name, out racer))
                Retired.Remove(name);
            else if (ReadyToRace.TryGetValue(name, out racer))
                ReadyToRace.Remove(name);

            if (racer != null)
            {
                racer.Status = newStatus;
                AddRacer(racer);
            }
        }

        public int TotalCount => Inactive.Count + Retired.Count + ReadyToRace.Count;
    }
}
