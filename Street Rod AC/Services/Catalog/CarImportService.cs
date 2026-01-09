using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Implements car import pipeline following the Import System Guidelines
    /// </summary>
    public class CarImportService : ICarImportService
    {
        private readonly IContentCatalogRepository _catalog;
        private readonly AppSettings _settings;
        private readonly IAppLogger _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // Known Kunos car prefixes
        private static readonly HashSet<string> KunosPrefix = ["ks_", "abarth_", "alfa_", "bmw_", "ferrari_",
            "ford_", "lamborghini_", "lotus_", "maserati_", "mazda_", "mclaren_", "mercedes_",
            "nissan_", "pagani_", "porsche_", "praga_", "ruf_", "scuderia_", "shelby_", "tatuusfa1_"];

        public CarImportService(IContentCatalogRepository catalog)
        {
            _catalog = catalog;
            _settings = AppSettings.Instance;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.Import);
        }

        public async Task<ImportResult> ImportCarsAsync(IProgress<ImportProgress>? progress = null)
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
            var carFolders = await DiscoverCarsAsync();
            result.TotalFound = carFolders.Count;
            _logger.Information("Discovery phase completed. Found {CarCount} cars", carFolders.Count);

            // MATERIALIZATION PHASE: Process each discovered car
            for (int i = 0; i < carFolders.Count; i++)
            {
                var carFolder = carFolders[i];
                var carId = Path.GetFileName(carFolder);

                try
                {
                    progress?.Report(new ImportProgress
                    {
                        Current = i + 1,
                        Total = carFolders.Count,
                        CurrentCarName = carId,
                        Operation = "Importing"
                    });

                    var carDef = await ImportCarAsync(carFolder, carId);

                    if (carDef != null)
                    {
                        // Check if car already exists in catalog
                        var existing = _catalog.GetCar(carId);

                        if (existing == null)
                        {
                            result.Imported++;
                        }
                        else if (existing.ContentHash != carDef.ContentHash)
                        {
                            result.Updated++;
                        }
                        else
                        {
                            // No changes, just mark as active
                            _catalog.UpdateCarStatus(carId, ContentStatus.Active);
                            result.Skipped++;
                            continue;
                        }

                        _catalog.UpsertCar(carDef);
                    }
                    else
                    {
                        result.Failed++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    var errorMsg = $"{carId}: {ex.Message}";
                    result.Errors.Add(errorMsg);
                    _logger.Warning(ex, "Failed to import car {CarId}", carId);
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            _logger.Information("Car import finished. Duration: {Duration}s, Imported: {Imported}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
                result.Duration.TotalSeconds, result.Imported, result.Updated, result.Skipped, result.Failed);

            return result;
        }

        public async Task<ImportResult> IncrementalUpdateAsync(IProgress<ImportProgress>? progress = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ImportResult();

            // Mark all existing cars as legacy first
            // Only cars found during this scan will be marked active
            _catalog.MarkAllCarsAsLegacy();

            // Run import - it will mark found cars as active
            var importResult = await ImportCarsAsync(progress);

            stopwatch.Stop();
            importResult.Duration = stopwatch.Elapsed;

            return importResult;
        }

        private async Task<List<string>> DiscoverCarsAsync()
        {
            var carFolders = new List<string>();

            // Find all ui_car.json files
            var uiCarFiles = Directory.GetFiles(_settings.CarsPath, "ui_car.json", SearchOption.AllDirectories);

            foreach (var uiJsonPath in uiCarFiles)
            {
                try
                {
                    // Validate structure: carFolder/ui/ui_car.json
                    var uiFolder = Path.GetDirectoryName(uiJsonPath);
                    var carFolder = Path.GetDirectoryName(uiFolder);

                    if (carFolder == null || uiFolder == null)
                        continue;

                    // Validate that ui folder is directly under car folder
                    if (Path.GetFileName(uiFolder) != "ui")
                        continue;

                    // Validate that car folder is directly under content/cars
                    if (Path.GetDirectoryName(carFolder) != _settings.CarsPath)
                        continue;

                    carFolders.Add(carFolder);
                }
                catch
                {
                    // Skip invalid paths
                }
            }

            return await Task.FromResult(carFolders);
        }

        private async Task<CarDefinition?> ImportCarAsync(string carFolder, string carId)
        {
            var uiJsonPath = Path.Combine(carFolder, "ui", "ui_car.json");

            if (!File.Exists(uiJsonPath))
                return null;

            try
            {
                // Read and parse ui_car.json
                var jsonContent = await File.ReadAllTextAsync(uiJsonPath);

                // Calculate content hash
                var contentHash = CalculateHash(jsonContent);

                // Parse JSON WITHOUT loading torque/power curves
                var jsonDoc = JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;

                var carDef = new CarDefinition
                {
                    Id = carId,
                    Name = GetJsonString(root, "name") ?? carId,
                    Brand = GetJsonString(root, "brand") ?? string.Empty,
                    Description = GetJsonString(root, "description") ?? string.Empty,
                    Class = GetJsonString(root, "class") ?? string.Empty,
                    Country = GetJsonString(root, "country"),
                    Author = GetJsonString(root, "author"),
                    Year = GetJsonInt(root, "year"),
                    Version = GetJsonString(root, "version"),
                    Url = GetJsonString(root, "url"),
                    ContentHash = contentHash,
                    Source = DetermineSource(carId, root),
                    Status = ContentStatus.Active,
                    ImportedDate = DateTime.Now,
                    LastUpdatedDate = DateTime.Now
                };

                // Parse tags
                if (root.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    carDef.Tags = tagsElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString() ?? string.Empty)
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();
                }

                // Parse specs
                if (root.TryGetProperty("specs", out var specsElement))
                {
                    carDef.Specs = new CarSpecsData
                    {
                        Bhp = GetJsonString(specsElement, "bhp"),
                        Torque = GetJsonString(specsElement, "torque"),
                        Weight = GetJsonString(specsElement, "weight"),
                        TopSpeed = GetJsonString(specsElement, "topspeed"),
                        Acceleration = GetJsonString(specsElement, "acceleration"),
                        PwRatio = GetJsonString(specsElement, "pwratio")
                    };
                }

                return carDef;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to import car '{carId}': {ex.Message}", ex);
            }
        }

        private ContentSource DetermineSource(string carId, JsonElement root)
        {
            // Check if it's Kunos official content by folder prefix
            var lowerCarId = carId.ToLowerInvariant();

            foreach (var prefix in KunosPrefix)
            {
                if (lowerCarId.StartsWith(prefix))
                {
                    // Further check: Kunos cars typically don't have author field or author is "Kunos Simulazioni"
                    var author = GetJsonString(root, "author");
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

            // Check for DLC indicators
            if (root.TryGetProperty("tags", out var tagsElement))
            {
                var tags = tagsElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()?.ToLowerInvariant())
                    .Where(x => !string.IsNullOrEmpty(x));

                if (tags.Any(t => t!.Contains("dlc")))
                {
                    return ContentSource.DLC;
                }
            }

            // Default to Mod
            return ContentSource.Mod;
        }

        private string CalculateHash(string content)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content);
            var hashBytes = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }

        private string? GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            return null;
        }

        private int? GetJsonInt(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            return null;
        }
    }
}
