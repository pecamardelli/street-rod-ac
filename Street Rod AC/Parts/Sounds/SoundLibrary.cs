using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Parts.Sounds;

/// <summary>A sound the library knows: where its bank and GUIDs are, and what engine it suits</summary>
public sealed record SoundEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string BankPath { get; init; }
    public required string GuidsText { get; init; }

    /// <summary>The car id the GUID lines name; a library sound may use a placeholder</summary>
    public required string DonorId { get; init; }

    /// <summary>Null when nothing says</summary>
    public int? Cylinders { get; init; }

    /// <summary>gm, ford, mopar as <see cref="MakeFamilies"/> has them; empty when nothing says</summary>
    public string Family { get; init; } = "";

    /// <summary>Where the bank's samples run out: the rev limiter of the car it came with</summary>
    public double? RpmMax { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Cars of the install that ship this very bank; empty for a sound nobody installed has</summary>
    public IReadOnlyList<string> Carriers { get; init; } = Array.Empty<string>();

    /// <summary>From the sound library rather than harvested off a car</summary>
    public bool Curated { get; init; }

    public long Size { get; init; }
    public string Checksum { get; init; } = "";

    public CarSound ToCarSound() => new(BankPath, GuidsText, DonorId);

    /// <summary>Whether the car already races on this bank, so there is nothing to swap</summary>
    public bool IsOwnSoundOf(string carId) => Carriers.Contains(carId, StringComparer.OrdinalIgnoreCase);
}

/// <summary>What a sound.json or a sounds.json car entry may say about a sound; every field optional</summary>
public sealed class SoundFacts
{
    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("donor_id")] public string? DonorId { get; set; }
    [JsonProperty("bank")] public string? Bank { get; set; }
    [JsonProperty("cylinders")] public int? Cylinders { get; set; }
    [JsonProperty("family")] public string? Family { get; set; }
    [JsonProperty("rpm_max")] public double? RpmMax { get; set; }
    [JsonProperty("tags")] public List<string>? Tags { get; set; }

    /// <summary>In sounds.json: this car's bank is no engine sound for anyone (a modern car's, a joke, a broken one)</summary>
    [JsonProperty("exclude")] public bool Exclude { get; set; }
}

/// <summary>The library's sounds.json: pins for blocks, and facts about installed cars the harvest cannot tell</summary>
public sealed class SoundOverrides
{
    /// <summary>Engine block part id to sound id: this block always gets this sound</summary>
    [JsonProperty("pins")] public Dictionary<string, string> Pins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Car id to facts, for the bank a car ships</summary>
    [JsonProperty("cars")] public Dictionary<string, SoundFacts> Cars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What the parts know about the engine a car came with: the harvest tags its bank with it</summary>
public delegate (int? Cylinders, string Family)? CarEngineFacts(string carId, string brand, string name, double? bhp);

/// <summary>
/// Every sound the game can put on an engine. Two sources, the same rules for both: a folder under the game's
/// <c>Assets\Sounds</c> is a sound (a bank, a <c>GUIDs.txt</c>, an optional <c>sound.json</c>; drop one in and the
/// game has it), and every installed car's own bank is a sound too, harvested with what the car says about it
/// (its rev limiter, and the engine the parts would give it). Banks that are the same bytes are one sound with
/// several carriers, so a car already on that bank is left alone.
/// </summary>
public sealed class SoundLibrary
{
    public const string SoundJson = "sound.json";
    public const string SoundsJson = "sounds.json";
    private const int ChecksumBytes = 64 * 1024;

    private readonly List<SoundEntry> _entries = new();
    private readonly Dictionary<string, SoundEntry> _byId = new(StringComparer.OrdinalIgnoreCase);

    private SoundLibrary(string root, SoundOverrides overrides)
    {
        Root = root;
        Overrides = overrides;
    }

    public string Root { get; }
    public SoundOverrides Overrides { get; }

    public IReadOnlyList<SoundEntry> All => _entries;
    public IReadOnlyList<SoundEntry> Curated => _entries.Where(e => e.Curated).ToList();
    public IReadOnlyList<SoundEntry> Harvested => _entries.Where(e => !e.Curated).ToList();

    public SoundEntry? Get(string? id) => id == null ? null : _byId.GetValueOrDefault(id);

    public static SoundLibrary Empty(string root) => new(root, new SoundOverrides());

    /// <summary>The library folder: one sound per subfolder. A folder that is not there is an empty library.</summary>
    public static SoundLibrary Load(string root)
    {
        var overridesPath = Path.Combine(root, SoundsJson);
        var overrides = File.Exists(overridesPath)
            ? JsonConvert.DeserializeObject<SoundOverrides>(File.ReadAllText(overridesPath)) ?? new SoundOverrides()
            : new SoundOverrides();
        var library = new SoundLibrary(root, overrides);
        if (!Directory.Exists(root)) return library;

        foreach (var folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var entry = ReadSoundFolder(folder);
            if (entry != null) library.Add(entry);
        }

        return library;
    }

    private static SoundEntry? ReadSoundFolder(string folder)
    {
        var id = Path.GetFileName(folder);
        var factsPath = Path.Combine(folder, SoundJson);
        var facts = File.Exists(factsPath) ? JsonConvert.DeserializeObject<SoundFacts>(File.ReadAllText(factsPath)) ?? new SoundFacts() : new SoundFacts();

        var bank = facts.Bank != null ? Path.Combine(folder, facts.Bank) : Directory.GetFiles(folder, "*.bank").OrderBy(f => f).FirstOrDefault();
        var guids = Path.Combine(folder, AcCarSound.GuidsFileName);
        if (bank == null || !File.Exists(bank) || !File.Exists(guids)) return null;

        // The GUID lines name whatever car the bank was built for: the bank's own name unless the json says
        var donor = facts.DonorId ?? Path.GetFileNameWithoutExtension(bank);
        return new SoundEntry
        {
            Id = id,
            Name = facts.Name ?? id,
            BankPath = bank,
            GuidsText = File.ReadAllText(guids),
            DonorId = donor,
            Cylinders = facts.Cylinders,
            Family = facts.Family ?? "",
            RpmMax = facts.RpmMax,
            Tags = facts.Tags ?? new List<string>(),
            Curated = true,
            Size = new FileInfo(bank).Length,
            Checksum = ChecksumOf(bank)
        };
    }

    private void Add(SoundEntry entry)
    {
        _entries.Add(entry);
        _byId[entry.Id] = entry;
    }

    /// <summary>
    /// Every installed car's own bank, as a sound. The same bytes under several cars are one sound with those
    /// carriers; a library sound that is those bytes gets the carriers instead. The rev ceiling is the highest
    /// limiter among the carriers (an author ran it that far), the cylinders and family what <paramref name="facts"/>
    /// says of the carriers, the library's sounds.json having the last word per car.
    /// </summary>
    /// <param name="carsFolder">The install's content\cars</param>
    /// <param name="masterGuidsPath">The install's content\sfx\GUIDs.txt, for cars without a file of their own</param>
    /// <param name="facts">What engine a car has, by what it says of itself; null tags nothing</param>
    /// <returns>How many cars were read</returns>
    public int Harvest(string carsFolder, string? masterGuidsPath, CarEngineFacts? facts)
    {
        if (!Directory.Exists(carsFolder)) return 0;

        var groups = new Dictionary<string, List<HarvestedCar>>(StringComparer.Ordinal);
        var read = 0;
        foreach (var folder in Directory.GetDirectories(carsFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var car = ReadCar(folder, masterGuidsPath, facts);
            if (car == null) continue;
            read++;
            if (!groups.TryGetValue(car.Checksum, out var list)) groups[car.Checksum] = list = new List<HarvestedCar>();
            list.Add(car);
        }

        foreach (var (checksum, cars) in groups)
        {
            var carriers = cars.Select(c => c.Id).ToList();
            var rpmMax = cars.Where(c => c.Limiter is > 0).Select(c => c.Limiter!.Value).DefaultIfEmpty().Max();
            // A bank under a Chevrolet, a Ford and a Plymouth says nothing about the family; one under three
            // Chevrolets does. What the carriers agree on is the tag, and nothing else is.
            var cylinders = Unanimous(cars.Select(c => c.Cylinders).Where(c => c != null).Select(c => c!.Value));
            var family = Unanimous(cars.Select(c => c.Family).Where(f => f.Length > 0), StringComparer.OrdinalIgnoreCase) ?? "";

            var curated = _entries.FirstOrDefault(e => e.Curated && e.Checksum == checksum);
            if (curated != null)
            {
                var updated = curated with
                {
                    Carriers = carriers,
                    RpmMax = curated.RpmMax ?? (rpmMax > 0 ? rpmMax : null),
                    Cylinders = curated.Cylinders ?? cylinders,
                    Family = curated.Family.Length > 0 ? curated.Family : family
                };
                _entries[_entries.IndexOf(curated)] = updated;
                _byId[updated.Id] = updated;
                continue;
            }

            var first = cars[0];
            Add(new SoundEntry
            {
                Id = "car:" + first.Id,
                Name = first.Name,
                BankPath = first.BankPath,
                GuidsText = first.GuidsText,
                DonorId = first.Id,
                Cylinders = cylinders,
                Family = family,
                RpmMax = rpmMax > 0 ? rpmMax : null,
                Carriers = carriers,
                Curated = false,
                Size = first.Size,
                Checksum = checksum
            });
        }

        return read;
    }

    private sealed record HarvestedCar(string Id, string Name, string BankPath, string GuidsText, long Size, string Checksum,
        double? Limiter, int? Cylinders, string Family);

    private HarvestedCar? ReadCar(string folder, string? masterGuidsPath, CarEngineFacts? facts)
    {
        var id = Path.GetFileName(folder);
        if (Overrides.Cars.TryGetValue(id, out var excluded) && excluded.Exclude) return null;
        var sound = AcCarSound.FromCar(folder, masterGuidsPath);
        if (sound == null) return null;

        string brand = "", name = id;
        double? bhp = null;
        var uiPath = Path.Combine(folder, "ui", "ui_car.json");
        if (File.Exists(uiPath))
        {
            try
            {
                var ui = JObject.Parse(File.ReadAllText(uiPath));
                brand = (string?)ui["brand"] ?? "";
                name = (string?)ui["name"] ?? id;
                bhp = StockEngineMatcher.ParsePower((string?)ui["specs"]?["bhp"]);
            }
            catch (Exception)
            {
                // A ui_car.json that does not parse says nothing; the car id will do for a name
            }
        }

        double? limiter = null;
        try
        {
            var readFile = AcCarDataReader.ForCar(folder);
            limiter = new IniText(readFile("engine.ini")).GetNumber("ENGINE_DATA", "LIMITER");
        }
        catch (Exception)
        {
            // No readable data: the bank's ceiling stays unknown
        }

        var engine = facts?.Invoke(id, brand, name, bhp);
        var cylinders = engine?.Cylinders;
        var family = engine?.Family ?? "";

        if (Overrides.Cars.TryGetValue(id, out var known))
        {
            if (known.Cylinders != null) cylinders = known.Cylinders;
            if (known.Family != null) family = known.Family;
            if (known.RpmMax != null) limiter = known.RpmMax;
            if (known.Name != null) name = known.Name;
        }

        return new HarvestedCar(id, name, sound.BankPath, sound.GuidsText, new FileInfo(sound.BankPath).Length, ChecksumOf(sound.BankPath),
            limiter, cylinders, family);
    }

    /// <summary>
    /// The engine the parts would give a car, for tagging its bank: the stock build's block says how many
    /// cylinders and which family.
    /// </summary>
    public static CarEngineFacts StockFacts(EngineBuildIndex index, PartsCatalog catalog) => (_, brand, name, bhp) =>
    {
        var build = StockEngineMatcher.Best(index, brand, name, bhp);
        if (build == null) return null;
        var block = catalog.Get(build.BlockId);
        var cylinders = block == null ? 0 : (int)block.Number("cylinders");
        return (cylinders > 0 ? cylinders : null, build.Family);
    };

    /// <summary>Size and the first 64 KB: enough to tell banks apart without reading 300 MB</summary>
    public static string ChecksumOf(string bankPath)
    {
        var buffer = new byte[ChecksumBytes];
        int length;
        using (var stream = File.OpenRead(bankPath)) length = stream.Read(buffer, 0, buffer.Length);
        return new FileInfo(bankPath).Length + ":" + Convert.ToHexString(MD5.HashData(buffer.AsSpan(0, length)));
    }

    private static int? Unanimous(IEnumerable<int> values)
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static string? Unanimous(IEnumerable<string> values, StringComparer comparer)
    {
        var distinct = values.Distinct(comparer).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }
}
