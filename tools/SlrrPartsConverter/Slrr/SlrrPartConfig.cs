using System.Globalization;
using System.IO;
using System.Numerics;

namespace Street_Rod_AC.Slrr;

/// <summary>Attachment point of a part. Position is in metres, rotation is yaw/pitch/roll in radians.</summary>
public sealed class SlrrSlot
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Vector3 Position { get; init; }
    public required Vector3 YawPitchRoll { get; init; }

    public int? DamageMode { get; set; }

    /// <summary>(parent part id, parent slot id) pairs this slot can be mounted on</summary>
    public List<(int PartId, int SlotId)> AttachesTo { get; } = new();

    /// <summary>(car part id, car slot id) pairs this slot fits, for parts that mount on a car</summary>
    public List<(int PartId, int SlotId)> CompatibleWith { get; } = new();
}

/// <summary>
/// A "render" line: what to draw for the part. Renders followed by a "lod" line are alternatives
/// (only the most detailed one matters here); the others are all drawn, each with its own offset.
/// </summary>
public sealed class SlrrRender
{
    public required int Id { get; init; }
    public required Vector3 Position { get; init; }
    public required Vector3 YawPitchRoll { get; init; }

    /// <summary>Detail level, higher is better; null for renders that are not part of a LOD set</summary>
    public int? Lod { get; set; }

    public Matrix4x4 Matrix =>
        Matrix4x4.CreateFromYawPitchRoll(YawPitchRoll.X, YawPitchRoll.Y, YawPitchRoll.Z) *
        Matrix4x4.CreateTranslation(Position);
}

/// <summary>SLRR part .cfg: what to draw plus the slots other parts mount on</summary>
public sealed class SlrrPartConfig
{
    // Handled explicitly or meaningless outside SLRR's own renderer
    private static readonly HashSet<string> SkippedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "render", "lod", "lods", "mesh", "click", "texture", "slot", "attach", "compatible", "slotdmgmode", "body", "eof"
    };

    public List<SlrrRender> Renders { get; } = new();

    public List<SlrrSlot> Slots { get; } = new();

    public float Mass { get; private set; }

    /// <summary>Every other line by keyword, arguments kept verbatim (category, damage, wing, wheel, ...)</summary>
    public Dictionary<string, List<string>> Other { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static SlrrPartConfig Load(string filename)
    {
        var config = new SlrrPartConfig();
        SlrrSlot? slot = null;

        foreach (var raw in File.ReadLines(filename))
        {
            var commentStart = raw.IndexOf(';');
            var comment = commentStart < 0 ? string.Empty : raw[(commentStart + 1)..].Trim();
            var line = commentStart < 0 ? raw : raw[..commentStart];

            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line[..slashes];

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens[0].StartsWith('#') || tokens[0].StartsWith('\uFEFF')) continue;

            var keyword = tokens[0].ToLowerInvariant();
            switch (keyword)
            {
                case "render" when tokens.Length >= 2:
                    var renderId = SlrrRpk.ParseHex(tokens[1]);
                    if (renderId < 0) break;

                    // Optional inline offset: position, then yaw/pitch/roll
                    config.Renders.Add(new SlrrRender
                    {
                        Id = renderId,
                        Position = new Vector3(Float(tokens, 2), Float(tokens, 3), Float(tokens, 4)),
                        YawPitchRoll = new Vector3(Float(tokens, 5), Float(tokens, 6), Float(tokens, 7))
                    });
                    break;

                case "lod" when tokens.Length >= 2 && config.Renders.Count > 0:
                    config.Renders[^1].Lod = (int)Float(tokens[1]);
                    break;

                case "slot" when tokens.Length >= 8:
                    slot = new SlrrSlot
                    {
                        Position = new Vector3(Float(tokens[1]), Float(tokens[2]), Float(tokens[3])),
                        YawPitchRoll = new Vector3(Float(tokens[4]), Float(tokens[5]), Float(tokens[6])),
                        Id = (int)Float(tokens[7]),
                        Name = comment
                    };
                    config.Slots.Add(slot);
                    break;

                case "attach" when slot != null && tokens.Length >= 3:
                    slot.AttachesTo.Add((SlrrRpk.ParseHex(tokens[1]), (int)Float(tokens[2])));
                    break;

                case "compatible" when slot != null && tokens.Length >= 3:
                    slot.CompatibleWith.Add((SlrrRpk.ParseHex(tokens[1]), (int)Float(tokens[2])));
                    break;

                case "slotdmgmode" when slot != null && tokens.Length >= 2:
                    slot.DamageMode = SlrrRpk.ParseHex(tokens[1]);
                    break;

                case "body" when tokens.Length >= 8:
                    config.Mass = Float(tokens[7]);
                    break;

                default:
                    if (SkippedKeywords.Contains(keyword) || !char.IsLetter(keyword[0])) break;

                    if (!config.Other.TryGetValue(keyword, out var values)) config.Other[keyword] = values = new();
                    values.Add(string.Join(' ', tokens.Skip(1)));
                    break;
            }
        }

        return config;
    }

    private static float Float(string[] tokens, int index) => index < tokens.Length ? Float(tokens[index]) : 0f;

    private static float Float(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0f;
}
