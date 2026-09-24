using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Models.GameState
{
    public class RacerCollection
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Racers");

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

            // Racers are known by name all through the game (the diner, the results, the simulator), so two of
            // them cannot share one: the second would silently take the first one's place and that racer would
            // be gone. The newcomer gets a number after the name instead.
            if (Find(racer.Name) is { } other && !ReferenceEquals(other, racer))
            {
                var original = racer.Name;
                var number = 2;
                while (Find($"{original} ({number})") != null) number++;
                racer.Name = $"{original} ({number})";

                Logger.Warning("Two racers are called {Name}; the second one races as {NewName}", original, racer.Name);
            }

            collection[racer.Name] = racer;
        }

        /// <summary>The racer of that name, whatever their status; null when there is none</summary>
        public Racer? Find(string name) =>
            ReadyToRace.TryGetValue(name, out var ready) ? ready
            : Inactive.TryGetValue(name, out var inactive) ? inactive
            : Retired.TryGetValue(name, out var retired) ? retired
            : null;

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
