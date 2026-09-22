using System.IO;
using System.Text;
using Newtonsoft.Json;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Services
{
    /// <summary>
    /// Puts a car's physics data, as its parts make it, into the Assetto Corsa install for the length of a race,
    /// and takes it out again. The rule for every change to the install: the originals are kept aside first
    /// (with a manifest that says what to put back), the change is the smallest that does the job, and
    /// whatever happens the originals go back: after the race, or the next time the game starts.
    /// A car that ships packed (data.acd, no data folder) gets its data unpacked for the race and the folder
    /// removed after, since the game reads the folder when there is one.
    /// </summary>
    public class CarDataOverlay
    {
        private const string ManifestFile = "manifest.json";
        private const string FilesFolder = "files";

        private readonly string _carsPath;
        private readonly string _keepPath;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarData");

        // Cars changed since the last restore: a manifest on disk without an entry here is what an earlier
        // race left behind, not this one's
        private readonly HashSet<string> _appliedNow = new(StringComparer.OrdinalIgnoreCase);

        public CarDataOverlay() : this(AppSettings.Instance.CarsPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetRodAC", "AcRestore"))
        {
        }

        /// <param name="carsPath">The install's content\cars</param>
        /// <param name="keepPath">Where the originals wait while a race runs</param>
        public CarDataOverlay(string carsPath, string keepPath)
        {
            _carsPath = carsPath;
            _keepPath = keepPath;
        }

        /// <summary>What was changed for one car, and how to put it back</summary>
        private sealed class Manifest
        {
            public string CarId { get; set; } = string.Empty;
            public DateTime Applied { get; set; }

            /// <summary>The data folder was made from data.acd for the race and goes away with the restore</summary>
            public bool CreatedDataFolder { get; set; }

            public List<Entry> Files { get; set; } = new();

            public sealed class Entry
            {
                public string Name { get; set; } = string.Empty;

                /// <summary>Whether the car had the file: put back from the kept copy, or deleted</summary>
                public bool Existed { get; set; }
            }
        }

        /// <summary>Cars whose data is changed right now</summary>
        public IReadOnlyList<string> Applied =>
            Directory.Exists(_keepPath)
                ? Directory.GetDirectories(_keepPath).Where(d => File.Exists(Path.Combine(d, ManifestFile))).Select(Path.GetFileName).ToList()!
                : Array.Empty<string>();

        public bool IsApplied(string carId) => File.Exists(Path.Combine(_keepPath, carId, ManifestFile));

        /// <summary>
        /// Writes the files into the car's data, keeping the originals. False when the car's data is already
        /// changed for this race (two cars of the same model: the first set stays).
        /// </summary>
        public bool Apply(string carId, IReadOnlyDictionary<string, string> files)
        {
            if (files.Count == 0) return true;
            if (_appliedNow.Contains(carId))
            {
                _logger.Warning("{Car} already has changed data: leaving it as it is", carId);
                return false;
            }

            // What an earlier race could not put back goes back before anything else goes in
            if (IsApplied(carId))
            {
                _logger.Warning("{Car} still had changed data from an earlier race: putting it back first", carId);
                Restore(carId);
            }

            var carDirectory = Path.Combine(_carsPath, carId);
            var dataDirectory = Path.Combine(carDirectory, AcCarData.DataFolder);
            var keep = Path.Combine(_keepPath, carId);
            var keepFiles = Path.Combine(keep, FilesFolder);
            if (Directory.Exists(keep)) Directory.Delete(keep, true);
            Directory.CreateDirectory(keepFiles);

            var manifest = new Manifest { CarId = carId, Applied = DateTime.Now };

            // A packed car runs on its data folder once there is one: the whole of it, with our files over it
            if (!Directory.Exists(dataDirectory))
            {
                var acd = Path.Combine(carDirectory, AcdFile.FileName);
                if (!File.Exists(acd)) throw new FileNotFoundException($"{carId} has neither a data folder nor {AcdFile.FileName}", acd);

                manifest.CreatedDataFolder = true;
                File.WriteAllText(Path.Combine(keep, ManifestFile), JsonConvert.SerializeObject(manifest, Formatting.Indented));

                Directory.CreateDirectory(dataDirectory);
                foreach (var (name, content) in AcdFile.Read(acd)) File.WriteAllBytes(Path.Combine(dataDirectory, name), content);
                _logger.Information("{Car}: data unpacked from {Acd} for the race", carId, AcdFile.FileName);
            }
            else
            {
                // The originals first, then the manifest that names them, then the change: a crash at any
                // point leaves either nothing changed or everything needed to change it back
                foreach (var name in files.Keys)
                {
                    var original = Path.Combine(dataDirectory, name);
                    var existed = File.Exists(original);
                    if (existed) File.Copy(original, Path.Combine(keepFiles, name), true);
                    manifest.Files.Add(new Manifest.Entry { Name = name, Existed = existed });
                }

                File.WriteAllText(Path.Combine(keep, ManifestFile), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            }

            _appliedNow.Add(carId);
            foreach (var (name, content) in files) File.WriteAllText(Path.Combine(dataDirectory, name), content, Encoding.Latin1);

            _logger.Information("{Car}: {Count} data file(s) changed for the race: {Files}", carId, files.Count, string.Join(", ", files.Keys));
            return true;
        }

        /// <summary>Puts a car's data back as it was. True when there was something to put back.</summary>
        public bool Restore(string carId)
        {
            var keep = Path.Combine(_keepPath, carId);
            var manifestPath = Path.Combine(keep, ManifestFile);
            if (!File.Exists(manifestPath)) return false;

            var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath)) ?? new Manifest { CarId = carId };
            var dataDirectory = Path.Combine(_carsPath, carId, AcCarData.DataFolder);

            if (manifest.CreatedDataFolder)
            {
                if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
                _logger.Information("{Car}: the unpacked data folder is gone again", carId);
            }
            else
            {
                foreach (var entry in manifest.Files)
                {
                    var target = Path.Combine(dataDirectory, entry.Name);
                    if (entry.Existed) File.Copy(Path.Combine(keep, FilesFolder, entry.Name), target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }

                _logger.Information("{Car}: {Count} data file(s) put back", carId, manifest.Files.Count);
            }

            Directory.Delete(keep, true);
            _appliedNow.Remove(carId);
            return true;
        }

        /// <summary>
        /// Puts every car back; at start-up this undoes what a crash left behind. A car that cannot be put back
        /// keeps its manifest, and the next Apply to it tries again first.
        /// </summary>
        public int RestoreAll()
        {
            var restored = 0;
            foreach (var carId in Applied)
            {
                try
                {
                    if (Restore(carId)) restored++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not put the data of {Car} back; the originals are kept under {Path}", carId, Path.Combine(_keepPath, carId));
                }
            }

            _appliedNow.Clear();
            return restored;
        }
    }
}
