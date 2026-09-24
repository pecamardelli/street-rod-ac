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
    ///
    /// A cars folder that is missing or cannot be read (a misconfigured path, an unplugged drive) is not an install
    /// with no cars: taking it for one would hand every opponent of a new game "no car", for good in that save. So a
    /// failed listing falls back to the last listing that worked in this run, and before any has, <see cref="Only"/>
    /// keeps the catalog's cars as they are - their Active status is the last start-up scan's word on what is installed.
    /// </summary>
    public static class InstalledCars
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        private static readonly object LastKnownLock = new();
        private static HashSet<string>? _lastKnown;

        /// <summary>
        /// Folder names (car ids) of the installed cars, race copies left out. When the folder cannot be listed: the
        /// last listing that worked, or empty if none has yet.
        /// </summary>
        public static HashSet<string> Ids()
        {
            TryIds(out var ids);
            return ids ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The installed car ids, listed now. False when the cars folder is missing or cannot be read (logged), with
        /// <paramref name="ids"/> the last listing that worked in this run, or null when there has been none.
        /// </summary>
        public static bool TryIds(out HashSet<string>? ids)
        {
            var carsPath = AppSettings.Instance.CarsPath;
            if (AcCarFolder.TryListInstalledCars(carsPath, out var folders, out var error))
            {
                ids = new HashSet<string>(folders.Select(folder => Path.GetFileName(folder)), StringComparer.OrdinalIgnoreCase);
                lock (LastKnownLock) _lastKnown = ids;
                return true;
            }

            lock (LastKnownLock) ids = _lastKnown == null ? null : new HashSet<string>(_lastKnown, StringComparer.OrdinalIgnoreCase);
            Logger.Warning("Could not list the installed cars in {Path} ({Error}); {Fallback}", carsPath, error ?? "unknown error",
                ids == null ? "going by the catalog's active cars" : "going by the last listing that worked");
            return false;
        }

        /// <summary>
        /// The cars of <paramref name="cars"/> that are installed, in their order. When the cars folder cannot be listed
        /// and no listing has worked yet in this run, all of <paramref name="cars"/> (logged by <see cref="TryIds"/>).
        /// </summary>
        public static List<CarDefinition> Only(IEnumerable<CarDefinition> cars, out int notInstalled)
        {
            notInstalled = 0;
            if (!TryIds(out var installed) && installed == null)
                return [.. cars];

            var kept = new List<CarDefinition>();
            foreach (var car in cars)
            {
                if (installed!.Contains(car.Id)) kept.Add(car);
                else notInstalled++;
            }

            return kept;
        }
    }
}
