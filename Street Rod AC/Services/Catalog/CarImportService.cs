using Newtonsoft.Json.Linq;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services.Police;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Implements car import pipeline following the Import System Guidelines
    /// </summary>
    public class CarImportService(IContentCatalogRepository catalog) : ICarImportService
    {
        private readonly IContentCatalogRepository _catalog = catalog;
        private readonly AppSettings _settings = AppSettings.Instance;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Import);

        // Known Kunos car prefixes
        private static readonly HashSet<string> KunosPrefix = ["ks_", "abarth_", "alfa_", "bmw_", "ferrari_",
            "ford_", "lamborghini_", "lotus_", "maserati_", "mazda_", "mclaren_", "mercedes_",
            "nissan_", "pagani_", "porsche_", "praga_", "ruf_", "scuderia_", "shelby_", "tatuusfa1_"];

        /// <summary>
        /// Scans content\cars and brings the catalog in step with it: new and changed cars are written, unchanged
        /// ones are marked Active, and cars that are no longer installed are marked Legacy - but only after a
        /// scan that worked, so an install on an unplugged drive does not empty the catalog.
        ///
        /// Runs on a worker thread, with one read of the catalog and one write: reading a few hundred
        /// ui_car.json files and hashing them used to hold the window up at start-up.
        /// </summary>
        public Task<ImportResult> ImportCarsAsync(IProgress<ImportProgress>? progress = null) =>
            Task.Run(() => ImportCars(progress));

        /// <summary>The same as <see cref="ImportCarsAsync"/>, which now also retires the cars that are gone</summary>
        public Task<ImportResult> IncrementalUpdateAsync(IProgress<ImportProgress>? progress = null) =>
            ImportCarsAsync(progress);

        private ImportResult ImportCars(IProgress<ImportProgress>? progress)
        {
            _logger.Information("Starting car import from {CarsPath}", _settings.CarsPath);
            var stopwatch = Stopwatch.StartNew();
            var result = new ImportResult();

            if (!Directory.Exists(_settings.CarsPath))
            {
                var error = $"Cars directory not found: {_settings.CarsPath}";
                _logger.Error("Cars directory not found at {CarsPath}", _settings.CarsPath);
                result.Errors.Add(error);
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            // DISCOVERY PHASE: Find all car folders with valid ui_car.json
            _logger.Debug("Starting discovery phase");
            List<string> carFolders;
            try
            {
                carFolders = DiscoverCars();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not a scan that worked: nothing is marked Legacy on the strength of it
                _logger.Error(ex, "Could not list the cars in {CarsPath}", _settings.CarsPath);
                result.Errors.Add($"Could not list the cars: {ex.Message}");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            result.TotalFound = carFolders.Count;
            _logger.Information("Discovery phase completed. Found {CarCount} cars", carFolders.Count);

            // The catalog as it stands, read once
            var known = _catalog.GetAllCars().ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toWrite = new List<CarDefinition>();

            // MATERIALIZATION PHASE: Process each discovered car
            for (int i = 0; i < carFolders.Count; i++)
            {
                var carFolder = carFolders[i];
                var carId = Path.GetFileName(carFolder);

                // A car with nothing but police liveries is the police's: never on a lot or in a rival's garage. Left
                // out of the catalog (and marked Legacy if it was ever in it); the chase finds it in the install.
                if (PoliceCars.IsPoliceCar(carFolder))
                {
                    _logger.Information("{Car} is a police car: not in the catalog", carId);
                    continue;
                }

                // An encrypted model races in AC but draws as shattered glass everywhere in the game: the car stays in
                // AC's folder and out of the catalog, the same way (see EncryptedCars)
                if (EncryptedCars.IsEncrypted(carFolder))
                {
                    _logger.Information("{Car} has an encrypted model: not in the catalog", carId);
                    continue;
                }

                // A car that is there but cannot be read now is still there: it is not retired for that
                seen.Add(carId);

                try
                {
                    progress?.Report(new ImportProgress
                    {
                        Current = i + 1,
                        Total = carFolders.Count,
                        CurrentCarName = carId,
                        Operation = "Importing"
                    });

                    var carDef = ImportCar(carFolder, carId);

                    if (carDef == null)
                    {
                        result.Failed++;
                        continue;
                    }

                    if (!known.TryGetValue(carId, out var existing))
                    {
                        result.Imported++;
                    }
                    else if (existing.ContentHash != carDef.ContentHash)
                    {
                        carDef.ImportedDate = existing.ImportedDate;
                        result.Updated++;
                    }
                    else
                    {
                        // No changes, just mark as active
                        if (existing.Status != ContentStatus.Active)
                        {
                            existing.Status = ContentStatus.Active;
                            existing.LastUpdatedDate = DateTime.Now;
                            toWrite.Add(existing);
                        }

                        result.Skipped++;
                        continue;
                    }

                    toWrite.Add(carDef);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    var errorMsg = $"{carId}: {ex.Message}";
                    result.Errors.Add(errorMsg);
                    _logger.Warning(ex, "Failed to import car {CarId}", carId);
                }
            }

            // Cars the scan did not find are gone from the install: kept on the books (saves may hold them), but
            // not Active, so nothing hands them to AC any more
            var retired = 0;
            foreach (var car in known.Values)
            {
                if (car.Status != ContentStatus.Active || seen.Contains(car.Id)) continue;

                car.Status = ContentStatus.Legacy;
                car.LastUpdatedDate = DateTime.Now;
                toWrite.Add(car);
                retired++;
            }

            if (retired > 0)
            {
                _logger.Information("{Count} catalog cars are no longer installed; marked Legacy", retired);
            }

            if (toWrite.Count > 0) _catalog.UpsertCars(toWrite);
            EncryptedCars.SaveCache();

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            _logger.Information("Car import finished. Duration: {Duration}s, Imported: {Imported}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
                result.Duration.TotalSeconds, result.Imported, result.Updated, result.Skipped, result.Failed);

            return result;
        }

        private List<string> DiscoverCars()
        {
            // Every folder directly under content/cars with a ui/ui_car.json; the copies made for a race are not cars of the install
            return AcCarFolder.InstalledCars(_settings.CarsPath)
                .Where(folder => File.Exists(AcCarUi.PathIn(folder)))
                .ToList();
        }

        private CarDefinition? ImportCar(string carFolder, string carId)
        {
            var uiJsonPath = AcCarUi.PathIn(carFolder);

            if (!File.Exists(uiJsonPath))
                return null;

            try
            {
                // Read and parse ui_car.json, the lenient way every reader of it in the game does (comments,
                // trailing commas, a year as 1969.0 or "1969", tags as a single string)
                var jsonContent = File.ReadAllText(uiJsonPath);
                var root = AcCarUi.TryParse(jsonContent, out var error)
                    ?? throw new InvalidDataException(error ?? "ui_car.json could not be read");

                // Skins are part of what the car is: a skin added or deleted must reach the catalog even when
                // ui_car.json stayed the same, or listings and opponents keep a skin that is gone
                var skins = ScanAvailableSkins(carFolder);

                var carDef = new CarDefinition
                {
                    Id = carId,
                    Name = AcCarUi.GetString(root, "name") ?? carId,
                    Brand = AcCarUi.GetString(root, "brand") ?? string.Empty,
                    Description = AcCarUi.GetString(root, "description") ?? string.Empty,
                    Class = AcCarUi.GetString(root, "class") ?? string.Empty,
                    Country = AcCarUi.GetString(root, "country"),
                    Author = AcCarUi.GetString(root, "author"),
                    Year = AcCarUi.GetInt(root, "year"),
                    Version = AcCarUi.GetString(root, "version"),
                    Url = AcCarUi.GetString(root, "url"),
                    Tags = [.. AcCarUi.GetStrings(root, "tags")],
                    ContentHash = CalculateHash(jsonContent, skins),
                    Source = DetermineSource(carId, root),
                    Status = ContentStatus.Active,
                    ImportedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now,
                    AvailableSkins = skins
                };

                // Parse specs
                if (AcCarUi.GetObject(root, "specs") is { } specs)
                {
                    carDef.Specs = new CarSpecsData
                    {
                        Bhp = AcCarUi.GetString(specs, "bhp"),
                        Torque = AcCarUi.GetString(specs, "torque"),
                        Weight = AcCarUi.GetString(specs, "weight"),
                        TopSpeed = AcCarUi.GetString(specs, "topspeed"),
                        Acceleration = AcCarUi.GetString(specs, "acceleration"),
                        PwRatio = AcCarUi.GetString(specs, "pwratio")
                    };
                }

                return carDef;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to import car '{carId}': {ex.Message}", ex);
            }
        }

        private static ContentSource DetermineSource(string carId, JObject root)
        {
            // Check if it's Kunos official content by folder prefix
            var lowerCarId = carId.ToLowerInvariant();

            foreach (var prefix in KunosPrefix)
            {
                if (lowerCarId.StartsWith(prefix))
                {
                    // Further check: Kunos cars typically don't have author field or author is "Kunos Simulazioni"
                    var author = AcCarUi.GetString(root, "author");
                    if (string.IsNullOrEmpty(author) || author.Contains("Kunos", StringComparison.OrdinalIgnoreCase))
                    {
                        return ContentSource.Kunos;
                    }
                }
            }

            // Check if it's DLC (has ks_ prefix but might have specific tags or in dlc folders)
            if (lowerCarId.StartsWith("ks_"))
            {
                // Could be DLC if it's in a DLC folder or has DLC tags
                // For now, treat all ks_ as Kunos, can be refined later
                return ContentSource.Kunos;
            }

            // Check for DLC indicators (tags as a list or as one string)
            if (AcCarUi.GetStrings(root, "tags").Any(t => t.Contains("dlc", StringComparison.OrdinalIgnoreCase)))
            {
                return ContentSource.DLC;
            }

            // Default to Mod
            return ContentSource.Mod;
        }

        /// <summary>ui_car.json and the skin folder names (sorted, so the order the disk lists them in does not matter)</summary>
        private static string CalculateHash(string content, IEnumerable<string> skins)
        {
            var text = content + "\n--skins--\n" + string.Join("\n", skins.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>
        /// Scans the skins directory and returns a list of available skin folder names
        /// </summary>
        private List<string> ScanAvailableSkins(string carFolder)
        {
            var skins = new List<string>();
            var skinsPath = Path.Combine(carFolder, "skins");

            if (!Directory.Exists(skinsPath))
            {
                _logger.Debug("No skins directory found for car at {CarFolder}", carFolder);
                return skins;
            }

            try
            {
                // Get all subdirectories in the skins folder
                var skinFolders = Directory.GetDirectories(skinsPath);

                foreach (var skinFolder in skinFolders)
                {
                    var skinId = Path.GetFileName(skinFolder);

                    // Only include directories that aren't empty and have valid names. A police livery is the
                    // police's, not something a car on a lot or a rival's car comes in (PoliceCars).
                    if (!string.IsNullOrEmpty(skinId) && !skinId.StartsWith(".") && !PoliceCars.IsPoliceSkin(skinFolder))
                    {
                        skins.Add(skinId);
                    }
                }

                _logger.Debug("Found {SkinCount} skins for car at {CarFolder}", skins.Count, carFolder);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to scan skins for car at {CarFolder}", carFolder);
            }

            return skins;
        }
    }
}
