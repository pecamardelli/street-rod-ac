using System.Text.Json.Serialization;

namespace Street_Rod_AC.Models.AC;

/// <summary>
/// Represents track metadata from Assetto Corsa's ui_track.json file
/// </summary>
public class TrackInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("length")]
    public string Length { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public string Width { get; set; } = string.Empty;

    [JsonPropertyName("pitboxes")]
    public string Pitboxes { get; set; } = string.Empty;

    [JsonPropertyName("run")]
    public string Run { get; set; } = string.Empty;

    /// <summary>
    /// The track's folder ID (e.g., "ks_brands_hatch")
    /// </summary>
    public string TrackId { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the track's content folder
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Available configurations/variants for this track
    /// </summary>
    public List<TrackConfiguration> Configurations { get; set; } = new();
}

/// <summary>
/// Represents a track configuration/layout variant
/// </summary>
public class TrackConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public string Pitboxes { get; set; } = string.Empty;
}
