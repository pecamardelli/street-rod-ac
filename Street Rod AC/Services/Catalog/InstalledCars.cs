using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// The cars that are in content\cars right now. The catalog outlives the install: a car deleted from the
    /// install stays on its books until the next start-up scan marks it Legacy, and a car that is not there
    /// cannot be sold, raced or handed to an opponent - AC would fail to load it. Whoever picks a catalog car for
    /// something that ends up in AC asks here first: the market's pool, the opponents' cars, the event fallback.
    ///
    /// One listing of the folder per call rather than a Directory.Exists per car.
    /// </summary>
    public static class InstalledCars
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        /// <summary>Folder names (car ids) of the installed cars, race copies left out; empty when the folder cannot be read</summary>
        public static HashSet<string> Ids()
        {
            try
            {
                return new HashSet<string>(
                    AcCarFolder.InstalledCars(AppSettings.Instance.CarsPath).Select(folder => Path.GetFileName(folder)),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warning("Could not list the installed cars in {Path}: {Error}", AppSettings.Instance.CarsPath, ex.Message);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>The cars of <paramref name="cars"/> that are installed, in their order</summary>
        public static List<CarDefinition> Only(IEnumerable<CarDefinition> cars, out int notInstalled)
        {
            var installed = Ids();
            var kept = new List<CarDefinition>();
            notInstalled = 0;

            foreach (var car in cars)
            {
                if (installed.Contains(car.Id)) kept.Add(car);
                else notInstalled++;
            }

            return kept;
        }
    }
}
