using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Models.GameState
{
    public class RacerCollection
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Racers");

        public Dictionary<string, Racer> Inactive { get; set; }
        public Dictionary<string, Racer> Retired { get; set; }
        public Dictionary<string, Racer> ReadyToRace { get; set; }

        /// <summary>
        /// Racers who left the scene for good. Kept so their name stays theirs and one who comes back years later is
        /// the same racer, but they are in none of the lookups the game races from: not in <see cref="All"/>, not
        /// found by <see cref="Find"/>.
        /// </summary>
        public Dictionary<string, Racer> Departed { get; set; }

        public RacerCollection()
        {
            Inactive = [];
            Retired = [];
            ReadyToRace = [];
            Departed = [];
        }

        public void AddRacer(Racer racer)
        {
            var collection = racer.Status switch
            {
                RacerStatus.Inactive => Inactive,
                RacerStatus.Retired => Retired,
                RacerStatus.ReadyToRace => ReadyToRace,
                RacerStatus.Departed => Departed,
                _ => ReadyToRace
            };

            // Racers are known by name all through the game (the diner, the results, the simulator), so two of
            // them cannot share one: the second would silently take the first one's place and that racer would
            // be gone. The newcomer gets a number after the name instead.
            // Case does not make two names: "ace" and "Ace" read as one racer to the player and to anything that
            // compares names loosely.
            if (FindIgnoringCase(racer.Name) is { } other && !ReferenceEquals(other, racer))
            {
                var original = racer.Name;
                var number = 2;
                while (FindIgnoringCase($"{original} ({number})") != null) number++;
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

        /// <summary>The racer whose name matches whatever its case, the exact match first</summary>
        private Racer? FindIgnoringCase(string name) =>
            Find(name)
            ?? (Departed.TryGetValue(name, out var departed) ? departed : null)
            ?? ReadyToRace.Concat(Inactive).Concat(Retired).Concat(Departed)
                .FirstOrDefault(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

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
            else if (Departed.TryGetValue(name, out racer))
                Departed.Remove(name);

            if (racer != null)
            {
                racer.Status = newStatus;
                AddRacer(racer);
            }
        }

        /// <summary>Every racer on the scene, whatever their status: ready to race, sitting out, not on the street yet. Not those who left.</summary>
        [LiteDB.BsonIgnore]
        public IEnumerable<Racer> All => ReadyToRace.Values.Concat(Retired.Values).Concat(Inactive.Values);

        [LiteDB.BsonIgnore]
        public int TotalCount => Inactive.Count + Retired.Count + ReadyToRace.Count;
    }
}
