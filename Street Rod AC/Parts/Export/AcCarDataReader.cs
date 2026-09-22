using System.IO;
using System.Text;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// The physics data of an Assetto Corsa car as it is on disk: a data folder, or data.acd when there is none.
/// The game itself reads the folder when both exist, so that is the order here too.
/// </summary>
public sealed class AcCarData
{
    public const string DataFolder = "data";

    private readonly Dictionary<string, byte[]>? _packed;

    private AcCarData(string carDirectory, string? dataDirectory, Dictionary<string, byte[]>? packed)
    {
        CarDirectory = carDirectory;
        DataDirectory = dataDirectory;
        _packed = packed;
    }

    public string CarDirectory { get; }

    /// <summary>The unpacked data folder the car runs on; null when it runs on data.acd</summary>
    public string? DataDirectory { get; }

    public bool IsPacked => DataDirectory == null;

    public static AcCarData Open(string carDirectory)
    {
        var folder = Path.Combine(carDirectory, DataFolder);
        if (Directory.Exists(folder)) return new AcCarData(carDirectory, folder, null);

        var acd = Path.Combine(carDirectory, AcdFile.FileName);
        if (!File.Exists(acd)) throw new FileNotFoundException($"{carDirectory} has neither a data folder nor {AcdFile.FileName}", acd);

        var packed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in AcdFile.Read(acd)) packed[name] = content;
        return new AcCarData(carDirectory, null, packed);
    }

    public bool Contains(string name) => _packed?.ContainsKey(name) ?? File.Exists(Path.Combine(DataDirectory!, name));

    /// <returns>Text of a data file, null when the car has no such file</returns>
    public string? ReadText(string name) => ReadBytes(name) is { } bytes ? Encoding.Latin1.GetString(bytes) : null;

    public byte[]? ReadBytes(string name)
    {
        if (_packed != null) return _packed.GetValueOrDefault(name);

        var path = Path.Combine(DataDirectory!, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Every file of the data, packed or not</summary>
    public IEnumerable<string> Names => _packed?.Keys ?? Directory.EnumerateFiles(DataDirectory!).Select(Path.GetFileName)!;
}

/// <summary>Reads the physics data of an Assetto Corsa car, whether it ships as a data folder or packed as data.acd</summary>
public static class AcCarDataReader
{
    /// <returns>Text of a data file by name, null when the car has no such file; the shape the export reads cars by</returns>
    public static Func<string, string?> ForCar(string carDirectory)
    {
        var data = AcCarData.Open(carDirectory);
        return data.ReadText;
    }

    /// <summary>A data folder on disk (the bench's way in)</summary>
    public static Func<string, string?> ForFolder(string dataDirectory) =>
        name => File.Exists(Path.Combine(dataDirectory, name)) ? File.ReadAllText(Path.Combine(dataDirectory, name), Encoding.Latin1) : null;
}
