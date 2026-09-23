using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Services
{
    /// <summary>
    /// Puts a car's physics data and its sound, as its parts make them, into the Assetto Corsa install for the
    /// length of a race, and takes them out again. The rule for every change to the install: the originals are
    /// kept aside first (with a manifest that says what to put back), the change is the smallest that does the
    /// job, and whatever happens the originals go back: after the race, or the next time the game starts.
    /// A car that ships packed (data.acd, no data folder) gets its data unpacked for the race and the folder
    /// removed after, since the game reads the folder when there is one.
    /// The sound is the bank and GUIDs under the car's <c>sfx</c> folder: the car's own two files move into a keep
    /// folder next to them (a rename, whatever their size), the new bank is hard-linked in under the car's name
    /// (no copy either; a copy only when the link cannot be made), and the GUIDs are written for the car's id.
    /// </summary>
    public class CarDataOverlay
    {
        private const string ManifestFile = "manifest.json";
        private const string FilesFolder = "files";

        /// <summary>Where a car's own sound waits inside its sfx folder while a race runs on another</summary>
        public const string SfxKeepFolder = "_streetrod_keep";

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

            /// <summary>The sound was changed; null when the car raced on its own</summary>
            public SfxEntry? Sfx { get; set; }

            public sealed class Entry
            {
                public string Name { get; set; } = string.Empty;

                /// <summary>Whether the car had the file: put back from the kept copy, or deleted</summary>
                public bool Existed { get; set; }
            }

            public sealed class SfxEntry
            {
                /// <summary>The car had no sfx folder at all; it goes away with the restore</summary>
                public bool CreatedFolder { get; set; }

                /// <summary>The car's own bank and GUIDs were moved into the keep folder; without them there is nothing to move back</summary>
                public bool BankExisted { get; set; }

                public bool GuidsExisted { get; set; }

                /// <summary>Where the new bank came from, for the log and for anyone reading the manifest</summary>
                public string Source { get; set; } = string.Empty;
            }
        }

        /// <summary>Cars whose data is changed right now</summary>
        public IReadOnlyList<string> Applied =>
            Directory.Exists(_keepPath)
                ? Directory.GetDirectories(_keepPath).Where(d => File.Exists(Path.Combine(d, ManifestFile))).Select(Path.GetFileName).ToList()!
                : Array.Empty<string>();

        public bool IsApplied(string carId) => File.Exists(Path.Combine(_keepPath, carId, ManifestFile));

        /// <summary>
        /// Writes the files into the car's data and the sound into its sfx folder, keeping the originals. False when
        /// the car is already changed for this race (two cars of the same model: the first set stays).
        /// </summary>
        /// <param name="files">Data file name to content; empty leaves the data alone</param>
        /// <param name="sound">The sound to race with; null leaves the car's own</param>
        public bool Apply(string carId, IReadOnlyDictionary<string, string> files, CarSound? sound = null)
        {
            if (files.Count == 0 && sound == null) return true;
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
            var sfxDirectory = Path.Combine(carDirectory, AcCarSound.SfxFolder);
            var keep = Path.Combine(_keepPath, carId);
            var keepFiles = Path.Combine(keep, FilesFolder);
            if (Directory.Exists(keep)) Directory.Delete(keep, true);
            Directory.CreateDirectory(keepFiles);

            var manifest = new Manifest { CarId = carId, Applied = DateTime.Now };

            // The originals first, then the manifest that names them, then the change: a crash at any
            // point leaves either nothing changed or everything needed to change it back
            if (files.Count > 0)
            {
                if (!Directory.Exists(dataDirectory))
                {
                    // A packed car runs on its data folder once there is one: the whole of it, with our files over it
                    var acd = Path.Combine(carDirectory, AcdFile.FileName);
                    if (!File.Exists(acd)) throw new FileNotFoundException($"{carId} has neither a data folder nor {AcdFile.FileName}", acd);
                    manifest.CreatedDataFolder = true;
                }
                else
                {
                    foreach (var name in files.Keys)
                    {
                        var original = Path.Combine(dataDirectory, name);
                        var existed = File.Exists(original);
                        if (existed) File.Copy(original, Path.Combine(keepFiles, name), true);
                        manifest.Files.Add(new Manifest.Entry { Name = name, Existed = existed });
                    }
                }
            }

            if (sound != null)
            {
                if (!File.Exists(sound.BankPath)) throw new FileNotFoundException($"The sound for {carId} has no bank", sound.BankPath);
                manifest.Sfx = new Manifest.SfxEntry
                {
                    CreatedFolder = !Directory.Exists(sfxDirectory),
                    BankExisted = File.Exists(Path.Combine(sfxDirectory, AcCarSound.BankFileName(carId))),
                    GuidsExisted = File.Exists(Path.Combine(sfxDirectory, AcCarSound.GuidsFileName)),
                    Source = sound.BankPath
                };
            }

            File.WriteAllText(Path.Combine(keep, ManifestFile), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            _appliedNow.Add(carId);

            if (manifest.CreatedDataFolder)
            {
                Directory.CreateDirectory(dataDirectory);
                foreach (var (name, content) in AcdFile.Read(Path.Combine(carDirectory, AcdFile.FileName))) File.WriteAllBytes(Path.Combine(dataDirectory, name), content);
                _logger.Information("{Car}: data unpacked from {Acd} for the race", carId, AcdFile.FileName);
            }

            foreach (var (name, content) in files) File.WriteAllText(Path.Combine(dataDirectory, name), content, Encoding.Latin1);
            if (files.Count > 0)
                _logger.Information("{Car}: {Count} data file(s) changed for the race: {Files}", carId, files.Count, string.Join(", ", files.Keys));

            if (sound != null) ApplySound(carId, sfxDirectory, sound);
            return true;
        }

        private void ApplySound(string carId, string sfxDirectory, CarSound sound)
        {
            var keepFolder = Path.Combine(sfxDirectory, SfxKeepFolder);
            Directory.CreateDirectory(keepFolder);

            var bank = Path.Combine(sfxDirectory, AcCarSound.BankFileName(carId));
            var guids = Path.Combine(sfxDirectory, AcCarSound.GuidsFileName);
            if (File.Exists(bank)) File.Move(bank, Path.Combine(keepFolder, AcCarSound.BankFileName(carId)), true);
            if (File.Exists(guids)) File.Move(guids, Path.Combine(keepFolder, AcCarSound.GuidsFileName), true);

            var linked = TryHardLink(sound.BankPath, bank);
            if (!linked) File.Copy(sound.BankPath, bank, true);
            File.WriteAllText(guids, sound.GuidsFor(carId), Encoding.ASCII);

            _logger.Information("{Car}: races on the sound of {Donor} ({Bank}, {How})", carId, sound.DonorId, sound.BankPath, linked ? "linked" : "copied");
        }

        /// <summary>Puts a car's data back as it was. True when there was something to put back.</summary>
        public bool Restore(string carId)
        {
            var keep = Path.Combine(_keepPath, carId);
            var manifestPath = Path.Combine(keep, ManifestFile);
            if (!File.Exists(manifestPath)) return false;

            var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath)) ?? new Manifest { CarId = carId };
            var carDirectory = Path.Combine(_carsPath, carId);
            var dataDirectory = Path.Combine(carDirectory, AcCarData.DataFolder);

            if (manifest.CreatedDataFolder)
            {
                if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
                _logger.Information("{Car}: the unpacked data folder is gone again", carId);
            }
            else if (manifest.Files.Count > 0)
            {
                foreach (var entry in manifest.Files)
                {
                    var target = Path.Combine(dataDirectory, entry.Name);
                    if (entry.Existed) File.Copy(Path.Combine(keep, FilesFolder, entry.Name), target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }

                _logger.Information("{Car}: {Count} data file(s) put back", carId, manifest.Files.Count);
            }

            if (manifest.Sfx != null) RestoreSound(carId, Path.Combine(carDirectory, AcCarSound.SfxFolder), manifest.Sfx);

            Directory.Delete(keep, true);
            _appliedNow.Remove(carId);
            return true;
        }

        /// <summary>
        /// The link goes, the originals come back out of the keep folder. Every step checks what is there, so a
        /// restore that was cut short finishes the next time.
        /// </summary>
        private void RestoreSound(string carId, string sfxDirectory, Manifest.SfxEntry sfx)
        {
            var bank = Path.Combine(sfxDirectory, AcCarSound.BankFileName(carId));
            var guids = Path.Combine(sfxDirectory, AcCarSound.GuidsFileName);
            var keepFolder = Path.Combine(sfxDirectory, SfxKeepFolder);
            var keptBank = Path.Combine(keepFolder, AcCarSound.BankFileName(carId));
            var keptGuids = Path.Combine(keepFolder, AcCarSound.GuidsFileName);

            if (File.Exists(keptBank)) File.Move(keptBank, bank, true);
            else if (!sfx.BankExisted && File.Exists(bank)) File.Delete(bank);

            if (File.Exists(keptGuids)) File.Move(keptGuids, guids, true);
            else if (!sfx.GuidsExisted && File.Exists(guids)) File.Delete(guids);

            if (Directory.Exists(keepFolder) && !Directory.EnumerateFileSystemEntries(keepFolder).Any()) Directory.Delete(keepFolder);
            if (sfx.CreatedFolder && Directory.Exists(sfxDirectory) && !Directory.EnumerateFileSystemEntries(sfxDirectory).Any()) Directory.Delete(sfxDirectory);

            _logger.Information("{Car}: its own sound is back", carId);
        }

        /// <summary>
        /// Puts every car back and takes away every copy made for a race; at start-up this undoes what a crash left
        /// behind. A car that cannot be put back keeps its manifest, and the next Apply to it tries again first.
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
            return restored + RemoveClones();
        }

        /// <summary>
        /// A copy of a car folder under <paramref name="cloneId"/>, for a second car of that model in the race: it races
        /// on <paramref name="files"/> over the car's own data and on <paramref name="sound"/> (the car's own when null),
        /// and the original is not touched. Models, textures and skins are hard links (nothing is copied but the data,
        /// which is written to); the marker goes in first, so a copy cut short is still known to be ours.
        /// </summary>
        public void CreateClone(string carId, string cloneId, IReadOnlyDictionary<string, string> files, CarSound? sound, string? masterGuidsPath = null)
        {
            var source = Path.Combine(_carsPath, carId);
            var target = Path.Combine(_carsPath, cloneId);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"{carId} is not installed");
            if (Directory.Exists(target))
            {
                if (!AcCarFolder.IsClone(target)) throw new IOException($"{cloneId} is a car of the install, not a copy: it is left alone");
                RemoveClone(cloneId);
            }

            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, AcCarFolder.CloneMarker),
                JsonConvert.SerializeObject(new { Source = carId, Created = DateTime.Now }, Formatting.Indented));

            var dataFolder = Path.Combine(source, AcCarData.DataFolder);
            var sfxFolder = Path.Combine(source, AcCarSound.SfxFolder);
            var linked = 0;
            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                // The data and the sound are made below; data.acd would not open under another name
                if (IsUnder(file, dataFolder) || IsUnder(file, sfxFolder)) continue;
                var relative = Path.GetRelativePath(source, file);
                if (relative.Equals(AcdFile.FileName, StringComparison.OrdinalIgnoreCase)) continue;

                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                // A read-only file is copied: clearing the flag on a link would clear it on the car's own file
                if (!File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly) && TryHardLink(file, destination))
                {
                    linked++;
                    continue;
                }

                File.Copy(file, destination, true);
                File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
                copied++;
            }

            // The data is written to, so it is a copy: the car's own folder, or what its data.acd holds
            var targetData = Path.Combine(target, AcCarData.DataFolder);
            Directory.CreateDirectory(targetData);
            if (Directory.Exists(dataFolder))
            {
                foreach (var file in Directory.GetFiles(dataFolder)) File.Copy(file, Path.Combine(targetData, Path.GetFileName(file)), true);
            }
            else
            {
                var acd = Path.Combine(source, AcdFile.FileName);
                if (!File.Exists(acd)) throw new FileNotFoundException($"{carId} has neither a data folder nor {AcdFile.FileName}", acd);
                foreach (var (name, content) in AcdFile.Read(acd)) File.WriteAllBytes(Path.Combine(targetData, name), content);
            }

            foreach (var file in Directory.GetFiles(targetData)) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            foreach (var (name, content) in files) File.WriteAllText(Path.Combine(targetData, name), content, Encoding.Latin1);

            // The bank goes in under the copy's name, with the GUID lines written for it
            sound ??= AcCarSound.FromCar(source, masterGuidsPath ?? AppSettings.Instance.SfxGuidsPath);
            if (sound != null)
            {
                var targetSfx = Path.Combine(target, AcCarSound.SfxFolder);
                Directory.CreateDirectory(targetSfx);
                var bank = Path.Combine(targetSfx, AcCarSound.BankFileName(cloneId));
                if (!TryHardLink(sound.BankPath, bank)) File.Copy(sound.BankPath, bank, true);
                File.WriteAllText(Path.Combine(targetSfx, AcCarSound.GuidsFileName), sound.GuidsFor(cloneId), Encoding.ASCII);
            }
            else
            {
                _logger.Warning("{Car} has no sound of its own to give its copy: {Clone} races without one", carId, cloneId);
            }

            _logger.Information("{Clone}: a copy of {Car} for the race ({Linked} file(s) linked, {Copied} copied, {Files} data file(s) changed, sound of {Sound})",
                cloneId, carId, linked, copied, files.Count, sound?.DonorId ?? "none");
        }

        /// <summary>Deletes a copy made for a race; a folder without the marker is never touched</summary>
        public bool RemoveClone(string cloneId)
        {
            var folder = Path.Combine(_carsPath, cloneId);
            if (!Directory.Exists(folder) || !AcCarFolder.IsClone(folder)) return false;

            // Deleting a link leaves the car's own file where it is; the marker goes last, so a delete cut short is retried
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals(AcCarFolder.CloneMarker, StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(file);
            }

            Directory.Delete(folder, true);
            _logger.Information("{Clone}: the copy is gone", cloneId);
            return true;
        }

        /// <summary>Every copy in the install, whichever race made it</summary>
        public int RemoveClones()
        {
            if (!Directory.Exists(_carsPath)) return 0;

            var removed = 0;
            foreach (var folder in Directory.GetDirectories(_carsPath).Where(AcCarFolder.IsClone))
            {
                try
                {
                    if (RemoveClone(Path.GetFileName(folder))) removed++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not delete the copy {Folder}; the next start tries again", folder);
                }
            }

            return removed;
        }

        private static bool IsUnder(string file, string folder) =>
            file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>A hard link when the two paths are on one volume; false when the file system will not have it</summary>
        private static bool TryHardLink(string source, string destination)
        {
            if (File.Exists(destination)) File.Delete(destination);
            return CreateHardLink(destination, source, IntPtr.Zero);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
