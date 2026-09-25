using System.IO;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// A car's model files. One rule for which of the .kn5 files in a car's folder is the car itself, shared by whoever
    /// reads it (the encrypted-model check) and whoever budgets for holding it (the dealer lot, the main screen).
    /// </summary>
    public static class CarModelFiles
    {
        /// <summary>The car's own model: the biggest .kn5 in its folder that is not the collider or a LOD</summary>
        public static string? MainModel(string carFolder)
        {
            try
            {
                return new DirectoryInfo(carFolder)
                    .EnumerateFiles("*.kn5", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.Name.Equals("collider.kn5", StringComparison.OrdinalIgnoreCase)
                                && !f.Name.Contains("_lod_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Length)
                    .FirstOrDefault()?.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Roughly what a car will cost to hold in a viewer: the size of its own model. Good enough to tell a 15 MB
        /// coupe from a 400 MB one, which is all a budget needs to know. 0 when there is no model to measure.
        /// </summary>
        public static long EstimateBytes(string carFolder)
        {
            if (MainModel(carFolder) is not { } model) return 0L;

            try
            {
                return new FileInfo(model).Length;
            }
            catch (Exception)
            {
                return 0L;
            }
        }
    }
}
