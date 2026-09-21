using AcTools.DataFile;

namespace Street_Rod_AC.Parts.Export;

/// <summary>Reads the physics data of an Assetto Corsa car, whether it ships as a data folder or packed as data.acd</summary>
public static class AcCarDataReader
{
    /// <returns>Text of a data file by name, null when the car has no such file; the shape <see cref="AcEngineData.Generate"/> reads cars by</returns>
    public static Func<string, string?> ForCar(string carDirectory)
    {
        var data = DataWrapper.FromCarDirectory(carDirectory);
        return name => data.Contains(name) ? data.GetData(name) : null;
    }
}
