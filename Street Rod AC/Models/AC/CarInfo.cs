using System.Text.Json.Serialization;

namespace Street_Rod_AC.Models.AC;

/// <summary>
/// Represents car metadata from Assetto Corsa's ui_car.json file
/// </summary>
public class CarInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("class")]
    public string Class { get; set; } = string.Empty;

    [JsonPropertyName("specs")]
    public CarSpecs? Specs { get; set; }

    [JsonPropertyName("torqueCurve")]
    public List<List<double>>? TorqueCurve { get; set; }

    [JsonPropertyName("powerCurve")]
    public List<List<double>>? PowerCurve { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// The car's folder ID (e.g., "ks_porsche_911_gt3_rs")
    /// </summary>
    public string CarId { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the car's content folder
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;
}

public class CarSpecs
{
    [JsonPropertyName("bhp")]
    public string? Bhp { get; set; }

    [JsonPropertyName("torque")]
    public string? Torque { get; set; }

    [JsonPropertyName("weight")]
    public string? Weight { get; set; }

    [JsonPropertyName("topspeed")]
    public string? TopSpeed { get; set; }

    [JsonPropertyName("acceleration")]
    public string? Acceleration { get; set; }

    [JsonPropertyName("pwratio")]
    public string? PwRatio { get; set; }
}
