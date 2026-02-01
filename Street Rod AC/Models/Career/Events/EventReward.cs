namespace Street_Rod_AC.Models.Career.Events
{
    /// <summary>
    /// Defines rewards given for completing a race event
    /// </summary>
    public class EventReward
    {
        /// <summary>
        /// Cash reward amount
        /// </summary>
        public decimal Cash { get; set; }

        /// <summary>
        /// Reputation points gained
        /// </summary>
        public int Reputation { get; set; }

        /// <summary>
        /// Optional special item reward (e.g., rare part ID)
        /// </summary>
        public string? SpecialItem { get; set; }

        /// <summary>
        /// Description of the special item if any
        /// </summary>
        public string? SpecialItemDescription { get; set; }

        /// <summary>
        /// Create an empty reward
        /// </summary>
        public EventReward() { }

        /// <summary>
        /// Create a cash and reputation reward
        /// </summary>
        public EventReward(decimal cash, int reputation = 0)
        {
            Cash = cash;
            Reputation = reputation;
        }

        /// <summary>
        /// Create a reward with all parameters
        /// </summary>
        public EventReward(decimal cash, int reputation, string specialItem, string itemDescription)
        {
            Cash = cash;
            Reputation = reputation;
            SpecialItem = specialItem;
            SpecialItemDescription = itemDescription;
        }

        /// <summary>
        /// Get a human-readable description of the reward
        /// </summary>
        public string GetDescription()
        {
            var parts = new List<string>();

            if (Cash > 0)
                parts.Add($"${Cash:N0}");

            if (Reputation > 0)
                parts.Add($"+{Reputation} reputation");

            if (!string.IsNullOrEmpty(SpecialItemDescription))
                parts.Add(SpecialItemDescription);
            else if (!string.IsNullOrEmpty(SpecialItem))
                parts.Add($"Special: {SpecialItem}");

            return parts.Count > 0 ? string.Join(" + ", parts) : "No reward";
        }
    }
}
