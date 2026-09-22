using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Dealers
{
    /// <summary>
    /// The dealers as authored: where they are, what they look like inside, and what they stock.
    /// </summary>
    public interface IDealerCatalog
    {
        /// <summary>Every dealer, in the order the file lists them</summary>
        IReadOnlyList<DealerDefinition> All { get; }

        /// <summary>One dealer by its id, or null if the file does not have it</summary>
        DealerDefinition? Get(string id);

        /// <summary>
        /// The dealers a saved game should carry. Call on load: a save made before a dealer existed gets it,
        /// and a dealer dropped from the file goes away.
        /// </summary>
        List<DealerLocation> ToLocations();
    }
}
