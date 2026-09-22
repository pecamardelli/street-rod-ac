using Newtonsoft.Json;

namespace Street_Rod_AC.Parts;

/// <summary>
/// A complete engine as a list of parts: what a car left the factory with, or a build someone wrote down.
/// Stored as engine_builds.json next to the part packs.
/// </summary>
public class EngineBuild
{
    public const string FileName = "engine_builds.json";

    public const string OriginCar = "car";
    public const string OriginNotes = "notes";
    public const string OriginKit = "kit";

    /// <summary>Unique id, e.g. "cars/Charger69_RT/stock" or "notes/MOPAR/mopar-340-six-pack-290-hp"</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the list comes from: a car script of the source game, a text file of build notes, or an engine kit of a pack</summary>
    [JsonProperty("origin")]
    public string Origin { get; set; } = string.Empty;

    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Power the build is known to make in the source game, in hp; the engine model is tuned against these</summary>
    [JsonProperty("rated_power")]
    public double? RatedPower { get; set; }

    /// <summary>The engine block first, then everything that goes on it; parts outside the converted packs have no id</summary>
    [JsonProperty("parts")]
    public List<PartStockReference> Parts { get; set; } = new();
}
