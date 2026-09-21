using Newtonsoft.Json;

namespace Street_Rod_AC.Parts;

/// <summary>
/// A group of parts converted together (one SLRR rpk), stored as pack.json next to the part models
/// </summary>
public class PartPack
{
    public const string FileName = "pack.json";

    /// <summary>Pack id, also its folder under the parts root, e.g. "engines/Mopar"</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Where the pack was converted from, relative to the source game</summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;

    [JsonProperty("parts")]
    public List<PartDefinition> Parts { get; set; } = new();
}

/// <summary>
/// One physical part: its 3D model plus the slots that say where it mounts and what mounts on it.
/// Geometry data follows the SLRR part cfg: metres, left-handed, Y up.
/// </summary>
public class PartDefinition
{
    /// <summary>Unique id: pack id + config name, e.g. "engines/Mopar/block_340"</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human readable name taken from the part script, e.g. "Chrysler LA 340 engine Block"</summary>
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Script base class that defines how the part behaves,
    /// e.g. "java.game.parts.enginepart.block.block_vee.Block_Vee_OHV"
    /// </summary>
    [JsonProperty("base_class")]
    public string? BaseClass { get; set; }

    /// <summary>Catalog categories from the most specific up, e.g. ["blocks", "Main", "engine", "parts"]</summary>
    [JsonProperty("categories")]
    public List<string> Categories { get; set; } = new();

    /// <summary>Model file next to pack.json; null for parts without usable geometry</summary>
    [JsonProperty("model")]
    public string? Model { get; set; }

    [JsonProperty("mass")]
    public float Mass { get; set; }

    [JsonProperty("slots")]
    public List<PartSlot> Slots { get; set; } = new();

    /// <summary>Remaining cfg lines by keyword (category, damage, wing, wheel, ...), kept verbatim for the part logic</summary>
    [JsonProperty("config")]
    public Dictionary<string, List<string>> Config { get; set; } = new();

    /// <summary>Source ids, to trace a part back to the original data</summary>
    [JsonProperty("source_type_id")]
    public int SourceTypeId { get; set; }

    [JsonProperty("source_script")]
    public string? SourceScript { get; set; }
}

public class PartSlot
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>X, Y, Z in metres, in the part's own space</summary>
    [JsonProperty("position")]
    public float[] Position { get; set; } = new float[3];

    /// <summary>Yaw, pitch, roll in radians</summary>
    [JsonProperty("rotation")]
    public float[] Rotation { get; set; } = new float[3];

    [JsonProperty("damage_mode")]
    public int? DamageMode { get; set; }

    /// <summary>Parent slots this slot mounts on. Empty for slots that only receive other parts.</summary>
    [JsonProperty("attaches_to")]
    public List<PartSlotReference> AttachesTo { get; set; } = new();

    /// <summary>
    /// Slots of other parts this slot stands in for: the part fits wherever those fit.
    /// This is how parts that mount on a car (engine blocks, wheels, wings) declare what they fit.
    /// </summary>
    [JsonProperty("compatible_with")]
    public List<PartSlotReference> CompatibleWith { get; set; } = new();
}

public class PartSlotReference
{
    /// <summary>Id of the target part; null when the target is outside the converted parts (cars, base game)</summary>
    [JsonProperty("part")]
    public string? Part { get; set; }

    [JsonProperty("slot")]
    public int Slot { get; set; }

    /// <summary>Original reference, "rpk path#0xTYPEID", always set</summary>
    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;
}
