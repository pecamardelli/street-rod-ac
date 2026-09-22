using System.IO;
using Newtonsoft.Json;

namespace Street_Rod_AC.Parts;

/// <summary>
/// Slots moved in their parts' space after conversion, by part id and slot id: what the garage's placement mode
/// writes when a part is nudged into place. The game applies the file over the packs when the catalog loads;
/// the converter folds it into its own shift rules on the next conversion and takes it away, so the packs stay
/// the truth. Metres, the part's own axes; a slot's entry is the sum of every nudge it got.
/// </summary>
public sealed class SlotShifts
{
    public const string FileName = "slot_shifts.json";

    private readonly SortedDictionary<string, SortedDictionary<int, float[]>> _shifts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the file lives (next to the packs); null for shifts that are not written anywhere</summary>
    public string? Path { get; private init; }

    public bool IsEmpty => _shifts.Count == 0;

    public IEnumerable<(string PartId, int SlotId, float[] Offset)> All =>
        _shifts.SelectMany(p => p.Value.Select(s => (p.Key, s.Key, s.Value)));

    public static SlotShifts Load(string folder)
    {
        var path = System.IO.Path.Combine(folder, FileName);
        var shifts = new SlotShifts { Path = path };
        if (!File.Exists(path)) return shifts;

        var read = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, float[]>>>(File.ReadAllText(path)) ?? new();
        foreach (var (partId, slots) in read)
        {
            foreach (var (slotId, offset) in slots)
            {
                if (offset.Length == 3) shifts.Add(partId, slotId, offset, save: false);
            }
        }

        return shifts;
    }

    /// <summary>The offset a slot has been given so far, zero when none</summary>
    public float[] Of(string partId, int slotId) =>
        _shifts.TryGetValue(partId, out var slots) && slots.TryGetValue(slotId, out var offset) ? (float[])offset.Clone() : new float[3];

    /// <summary>Adds to a slot's offset and writes the file</summary>
    public void Add(string partId, int slotId, float[] delta, bool save = true)
    {
        if (!_shifts.TryGetValue(partId, out var slots)) _shifts[partId] = slots = new SortedDictionary<int, float[]>();
        var offset = slots.TryGetValue(slotId, out var existing) ? existing : new float[3];
        for (var axis = 0; axis < 3; axis++) offset[axis] += delta[axis];

        if (offset.All(v => Math.Abs(v) < 1e-6f))
        {
            slots.Remove(slotId);
            if (slots.Count == 0) _shifts.Remove(partId);
        }
        else slots[slotId] = offset;

        if (save) Save();
    }

    /// <summary>Takes a slot back to where the packs put it, and writes the file</summary>
    public void Clear(string partId, int slotId)
    {
        var offset = Of(partId, slotId);
        Add(partId, slotId, new[] { -offset[0], -offset[1], -offset[2] });
    }

    /// <summary>Moves the slots of the parts given by the offsets; a slot or part that is not there is skipped</summary>
    public int ApplyTo(IReadOnlyDictionary<string, PartDefinition> parts)
    {
        var applied = 0;
        foreach (var (partId, slotId, offset) in All)
        {
            if (!parts.TryGetValue(partId, out var part)) continue;
            foreach (var slot in part.Slots.Where(s => s.Id == slotId))
            {
                for (var axis = 0; axis < 3; axis++) slot.Position[axis] += offset[axis];
                applied++;
            }
        }

        return applied;
    }

    public void Save()
    {
        if (Path == null) return;

        if (_shifts.Count == 0)
        {
            if (File.Exists(Path)) File.Delete(Path);
            return;
        }

        File.WriteAllText(Path, JsonConvert.SerializeObject(_shifts, Formatting.Indented));
    }
}
