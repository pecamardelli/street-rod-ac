using System.IO;

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
}
