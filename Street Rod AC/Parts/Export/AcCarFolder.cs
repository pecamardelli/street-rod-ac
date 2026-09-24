using System.IO;
using Street_Rod_AC.Helpers;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// Car folders the game makes for a race and takes away after it: a second car of a model already in the race
/// races in a copy of the folder under its own id, so it can have its own data and sound. A marker file inside says
/// the folder is one of ours; whatever reads the install's cars leaves such a folder out, and only such a folder is
/// ever deleted.
/// </summary>
public static class AcCarFolder
{
    public const string CloneMarker = "streetrod_clone.json";
    public const string CloneSuffix = "__sr_opponent";

    /// <summary>The id the copy of <paramref name="carId"/> races under</summary>
    public static string CloneIdFor(string carId) => carId + CloneSuffix;

    public static bool IsClone(string carFolder) => File.Exists(Path.Combine(carFolder, CloneMarker));

    /// <summary>The car folders of the install, without the copies made for a race: what every reader of the cars goes through</summary>
    /// <param name="carsFolder">The install's content\cars</param>
    public static IEnumerable<string> InstalledCars(string carsFolder) =>
        Directory.Exists(carsFolder) ? Directory.EnumerateDirectories(carsFolder).Where(f => !IsClone(f)) : Enumerable.Empty<string>();

    public const string DefaultSkin = "default";
    private const string PreviewFile = "preview.jpg";

    /// <summary>
    /// The picture to show for a car in a skin: the skin's preview, else the car's own preview, else the one in its
    /// ui folder; null when there is none. No skin (null or empty) is the "default" skin.
    /// </summary>
    /// <param name="carsPath">The install's content\cars</param>
    /// <remarks>
    /// The ids come from the save and the catalog: a car id that is not one folder name has no picture, and a skin id
    /// that is not one folder name is no skin (the car's own pictures still count).
    /// </remarks>
    public static string? PreviewImage(string carsPath, string carId, string? skinId)
    {
        if (!PathNames.IsSafeSegment(carId)) return null;

        var carFolder = Path.Combine(carsPath, carId);
        var skin = string.IsNullOrEmpty(skinId) ? DefaultSkin : skinId;
        var candidates = new List<string>(3);
        if (PathNames.IsSafeSegment(skin)) candidates.Add(Path.Combine(carFolder, "skins", skin, PreviewFile));
        candidates.Add(Path.Combine(carFolder, PreviewFile));
        candidates.Add(Path.Combine(carFolder, "ui", PreviewFile));

        return candidates.FirstOrDefault(File.Exists);
    }
}
