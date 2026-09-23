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
    /// A sound is never worth a race: one whose bank is gone leaves the car on its own.
    /// </summary>
    public class CarDataOverlay
    {
        private const string ManifestFile = "manifest.json";
        private const string FilesFolder = "files";

        /// <summary>Where a car's own sound waits inside its sfx folder while a race runs on another</summary>
        public const string SfxKeepFolder = AcCarSound.KeepFolder;

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
            sound = Usable(carId, sound);
            if (files.Count == 0 && sound == null) return true;
            if (_appliedNow.Contains(carId))
            {
                _logger.Warning("{Car} already has changed data: leaving it as it is", carId);
                return false;
            }

            RestoreLeftover(carId);

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
                    AcdOf(carId, carDirectory);
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
                // A file already in the keep folder is the car's own too (see Keep): it goes back with the restore
                var keepFolder = Path.Combine(sfxDirectory, SfxKeepFolder);
                var bankName = AcCarSound.BankFileName(carId);
                manifest.Sfx = new Manifest.SfxEntry
                {
                    CreatedFolder = !Directory.Exists(sfxDirectory),
                    BankExisted = File.Exists(Path.Combine(sfxDirectory, bankName)) || File.Exists(Path.Combine(keepFolder, bankName)),
                    GuidsExisted = File.Exists(Path.Combine(sfxDirectory, AcCarSound.GuidsFileName)) || File.Exists(Path.Combine(keepFolder, AcCarSound.GuidsFileName)),
                    Source = sound.BankPath
                };
            }

            File.WriteAllText(Path.Combine(keep, ManifestFile), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            _appliedNow.Add(carId);

            if (manifest.CreatedDataFolder)
            {
                UnpackAcd(carId, carDirectory, dataDirectory);
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
            Keep(carId, bank, Path.Combine(keepFolder, AcCarSound.BankFileName(carId)));
            Keep(carId, guids, Path.Combine(keepFolder, AcCarSound.GuidsFileName));

            var linked = LinkOrCopy(sound.BankPath, bank);
            File.WriteAllText(guids, sound.GuidsFor(carId), Encoding.ASCII);

            _logger.Information("{Car}: races on the sound of {Donor} ({Bank}, {How})", carId, sound.DonorId, sound.BankPath, linked ? "linked" : "copied");
        }

        /// <summary>
        /// Moves a file of the car's sound into the keep folder. A file already there is the car's own, left by a race
        /// whose manifest was lost: the one in place is what that race put in, and it is the one that goes, never the
        /// only copy of the car's own.
        /// </summary>
        private void Keep(string carId, string file, string kept)
        {
            if (!File.Exists(file)) return;
            if (File.Exists(kept))
            {
                _logger.Warning("{Car}: {File} was already kept from an earlier race; the one in its place is not the car's and goes", carId, Path.GetFileName(file));
                File.Delete(file);
                return;
            }

            File.Move(file, kept);
        }

        /// <summary>
        /// The sound as it can go in right now, or null for none. A donor car that races on another sound itself has its
        /// own bank in its keep folder, and the file under its name is the other sound's (the GUID text is always the
        /// donor's own), so the bank is taken from where its bytes are. A bank that is gone (the car uninstalled, the
        /// library folder removed while the game ran) leaves the car on its own sound.
        /// </summary>
        private CarSound? Usable(string carId, CarSound? sound)
        {
            if (sound == null) return null;

            var bank = AcCarSound.OwnBank(sound.BankPath);
            if (bank == null)
            {
                _logger.Warning("{Car}: the bank of {Donor} is gone ({Bank}); it keeps its own sound", carId, sound.DonorId, sound.BankPath);
                return null;
            }

            return bank == sound.BankPath ? sound : sound with { BankPath = bank };
        }

        /// <summary>What an earlier race could not put back goes back before anything else goes in or is copied</summary>
        private void RestoreLeftover(string carId)
        {
            if (!IsApplied(carId) || _appliedNow.Contains(carId)) return;
            _logger.Warning("{Car} still had changed data from an earlier race: putting it back first", carId);
            Restore(carId);
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
        /// which is written to); the marker goes in first, so a copy cut short is still known to be ours. The copy is
        /// made from the car as it ships: what an earlier race left changed in it goes back first, and a car already
        /// changed for this race cannot be copied.
        /// </summary>
        public void CreateClone(string carId, string cloneId, IReadOnlyDictionary<string, string> files, CarSound? sound, string? masterGuidsPath = null)
        {
            var source = Path.Combine(_carsPath, carId);
            var target = Path.Combine(_carsPath, cloneId);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"{carId} is not installed");
            if (_appliedNow.Contains(carId)) throw new InvalidOperationException($"{carId} is already changed for this race: its copy is made before that");
            RestoreLeftover(carId);

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
                if (LinkOrCopy(file, destination)) linked++;
                else copied++;
            }

            // The data is written to, so it is a copy: the car's own folder, or what its data.acd holds
            var targetData = Path.Combine(target, AcCarData.DataFolder);
            if (Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(targetData);
                foreach (var file in Directory.GetFiles(dataFolder)) File.Copy(file, Path.Combine(targetData, Path.GetFileName(file)), true);
            }
            else
            {
                UnpackAcd(carId, source, targetData);
            }

            foreach (var file in Directory.GetFiles(targetData)) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            foreach (var (name, content) in files) File.WriteAllText(Path.Combine(targetData, name), content, Encoding.Latin1);

            // The bank goes in under the copy's name, with the GUID lines written for it; a chosen sound whose bank is
            // gone leaves the copy on the car's own
            sound = Usable(cloneId, sound) ?? Usable(cloneId, AcCarSound.FromCar(source, masterGuidsPath ?? AppSettings.Instance.SfxGuidsPath));
            if (sound != null)
            {
                var targetSfx = Path.Combine(target, AcCarSound.SfxFolder);
                Directory.CreateDirectory(targetSfx);
                LinkOrCopy(sound.BankPath, Path.Combine(targetSfx, AcCarSound.BankFileName(cloneId)));
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

            // Deleting a link leaves the car's own file where it is. The marker goes last, after every file and folder,
            // so a delete cut short leaves a folder still known to be ours, and the next start finishes it.
            var marker = Path.Combine(folder, AcCarFolder.CloneMarker);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!file.Equals(marker, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }

            foreach (var directory in Directory.GetDirectories(folder)) Directory.Delete(directory, true);
            File.Delete(marker);
            Directory.Delete(folder);
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

        /// <summary>A packed car's data.acd; a car with neither it nor a data folder cannot race on changed data</summary>
        private static string AcdOf(string carId, string carDirectory)
        {
            var acd = Path.Combine(carDirectory, AcdFile.FileName);
            if (!File.Exists(acd)) throw new FileNotFoundException($"{carId} has neither a data folder nor {AcdFile.FileName}", acd);
            return acd;
        }

        /// <summary>What a packed car's data.acd holds, written out as a data folder</summary>
        private static void UnpackAcd(string carId, string carDirectory, string dataDirectory)
        {
            var entries = AcdFile.Read(AcdOf(carId, carDirectory));
            Directory.CreateDirectory(dataDirectory);
            foreach (var (name, content) in entries) File.WriteAllBytes(Path.Combine(dataDirectory, name), content);
        }

        /// <summary>
        /// A hard link to <paramref name="source"/> when the two paths are on one volume and the source is not read-only
        /// (a link shares the flag: clearing it would clear it on the car's own file, and a read-only link could not be
        /// deleted or replaced after the race); otherwise a writable copy. True when linked.
        /// </summary>
        private static bool LinkOrCopy(string source, string destination)
        {
            if (File.Exists(destination)) File.Delete(destination);
            if (!File.GetAttributes(source).HasFlag(FileAttributes.ReadOnly) && CreateHardLink(destination, source, IntPtr.Zero)) return true;

            File.Copy(source, destination, true);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
            return false;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
