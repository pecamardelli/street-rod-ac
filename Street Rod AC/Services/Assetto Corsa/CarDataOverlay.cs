using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Helpers;
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

        /// <summary>
        /// Every race copy made, by absolute folder, until it is removed: a copy left in an install the settings no
        /// longer name (the AC folder changed after a crash) is still found and taken away
        /// </summary>
        private const string ClonesFile = "clones.json";

        /// <summary>Where a car's own sound waits inside its sfx folder while a race runs on another</summary>
        public const string SfxKeepFolder = AcCarSound.KeepFolder;

        private readonly string _carsPath;
        private readonly string _keepPath;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarData");

        // Cars changed since the last restore: a manifest on disk without an entry here is what an earlier
        // race left behind, not this one's
        private readonly HashSet<string> _appliedNow = new(StringComparer.OrdinalIgnoreCase);

        public CarDataOverlay() : this(AppSettings.Instance.CarsPath, AppSettings.AcRestorePath)
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

            /// <summary>
            /// The absolute car folder that was changed: the one that goes back, even when the AC folder in the
            /// settings has changed since. Empty in manifests written before it was recorded.
            /// </summary>
            public string CarFolder { get; set; } = string.Empty;

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

                /// <summary>The car's file was read-only: the flag is cleared for the race and set again after it</summary>
                public bool ReadOnly { get; set; }
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
                ? Directory.GetDirectories(_keepPath)
                    .Where(d => File.Exists(Path.Combine(d, ManifestFile)))
                    .Select(d => Path.GetFileName(d)!)
                    .Where(PathNames.IsSafeSegment)
                    .ToList()
                : Array.Empty<string>();

        public bool IsApplied(string carId) =>
            PathNames.IsSafeSegment(carId) && File.Exists(Path.Combine(_keepPath, carId, ManifestFile));

        /// <summary>
        /// A new race's preparation starts: whatever is still listed as changed "for this race" is from an earlier one
        /// whose restore was skipped or failed, and is treated as a leftover from here on (put back before a car is
        /// read, applied or copied) instead of blocking this race's changes.
        /// </summary>
        public void BeginRace()
        {
            using var gate = AcInstallGate.Hold();
            if (_appliedNow.Count > 0)
                _logger.Warning("{Count} car(s) still listed as changed from an earlier race: they are put back before they are used again", _appliedNow.Count);
            _appliedNow.Clear();
        }

        /// <summary>
        /// A car whose data an earlier race left changed (its restore failed, or the app died during the race) is put
        /// back now, so what is read from its folder is the car's own. Nothing happens while AC runs (the game may be
        /// on that data) or when the car is changed for the race being prepared right now. True when it was put
        /// back; throws when it could not be.
        /// </summary>
        public bool RestoreIfLeftover(string carId)
        {
            using var gate = AcInstallGate.Hold();
            if (!IsApplied(carId) || _appliedNow.Contains(carId)) return false;
            if (AcProcesses.AnyRunning())
            {
                _logger.Warning("{Car} still has data from an earlier race, but Assetto Corsa is running: it goes back once the game has closed", carId);
                return false;
            }

            _logger.Warning("{Car} still had changed data from an earlier race: putting it back before it is read", carId);
            return Restore(carId);
        }

        /// <summary>
        /// The car's folder under content\cars, for an id that is one folder name; anything else (empty, ".", "..",
        /// a path) is refused before it can reach a recursive delete
        /// </summary>
        private string CarDirectory(string carId)
        {
            if (!PathNames.IsSafeSegment(carId)) throw new ArgumentException($"'{carId}' is not a car folder name", nameof(carId));
            return PathNames.CombineUnder(_carsPath, carId);
        }

        /// <summary>Where a car's originals wait, checked the same way: never the AcRestore root itself</summary>
        private string KeepDirectory(string carId)
        {
            if (!PathNames.IsSafeSegment(carId)) throw new ArgumentException($"'{carId}' is not a car folder name", nameof(carId));
            return PathNames.CombineUnder(_keepPath, carId);
        }

        /// <summary>
        /// The folder a manifest says it changed, when it is plausibly that car's folder in an AC install (named after
        /// the car, inside a "cars" folder); else the car's folder in the install the game runs on now
        /// </summary>
        private string ChangedCarDirectory(string carId, Manifest manifest)
        {
            var recorded = manifest.CarFolder;
            if (!string.IsNullOrWhiteSpace(recorded) && Path.IsPathFullyQualified(recorded))
            {
                var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(recorded));
                var parent = Path.GetDirectoryName(folder);
                if (string.Equals(Path.GetFileName(folder), carId, StringComparison.OrdinalIgnoreCase)
                    && parent != null && string.Equals(Path.GetFileName(parent), "cars", StringComparison.OrdinalIgnoreCase))
                    return folder;

                _logger.Warning("{Car}: the manifest names {Folder}, which is not that car's folder; putting back {Current} instead", carId, recorded, CarDirectory(carId));
            }

            return CarDirectory(carId);
        }

        /// <summary>
        /// Writes the files into the car's data and the sound into its sfx folder, keeping the originals. False when
        /// the car is already changed for this race (two cars of the same model: the first set stays).
        /// </summary>
        /// <param name="files">Data file name to content; empty leaves the data alone</param>
        /// <param name="sound">The sound to race with; null leaves the car's own</param>
        public bool Apply(string carId, IReadOnlyDictionary<string, string> files, CarSound? sound = null)
        {
            using var gate = AcInstallGate.Hold();
            sound = Usable(carId, sound);
            if (files.Count == 0 && sound == null) return true;
            if (_appliedNow.Contains(carId))
            {
                _logger.Warning("{Car} already has changed data: leaving it as it is", carId);
                return false;
            }

            var carDirectory = CarDirectory(carId);
            var keep = KeepDirectory(carId);
            RestoreLeftover(carId);

            var dataDirectory = Path.Combine(carDirectory, AcCarData.DataFolder);
            var sfxDirectory = Path.Combine(carDirectory, AcCarSound.SfxFolder);
            var keepFiles = Path.Combine(keep, FilesFolder);
            if (Directory.Exists(keep)) Directory.Delete(keep, true);
            Directory.CreateDirectory(keepFiles);

            var manifest = new Manifest { CarId = carId, Applied = DateTime.Now, CarFolder = Path.GetFullPath(carDirectory) };

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
                        var original = DataFile(dataDirectory, name);
                        var existed = File.Exists(original);
                        var readOnly = existed && File.GetAttributes(original).HasFlag(FileAttributes.ReadOnly);
                        if (existed)
                        {
                            // The kept copy is ours to delete after the restore: it must not carry the flag along
                            var kept = Path.Combine(keepFiles, name);
                            File.Copy(original, kept, true);
                            ClearReadOnly(kept);
                        }

                        manifest.Files.Add(new Manifest.Entry { Name = name, Existed = existed, ReadOnly = readOnly });
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

            // Flushed to the disk and renamed into place: a power cut right after leaves a whole manifest, never an
            // empty one next to changed data
            WriteManifest(keep, manifest);
            _appliedNow.Add(carId);

            if (manifest.CreatedDataFolder)
            {
                UnpackAcd(carId, carDirectory, dataDirectory);
                _logger.Information("{Car}: data unpacked from {Acd} for the race", carId, AcdFile.FileName);
            }

            foreach (var (name, content) in files)
            {
                var target = DataFile(dataDirectory, name);
                if (File.Exists(target)) ClearReadOnly(target);
                File.WriteAllText(target, content, Encoding.Latin1);
            }
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

        /// <summary>
        /// Puts a car's data back as it was. True when there was something to put back. Every step is tried, whatever
        /// the others do: the data files one by one, then the sound. The kept originals are deleted only when all of
        /// them went back; what failed stays in the manifest for the next try, and this throws to say so. A manifest
        /// that reads as nothing (0 bytes after a power cut, "null") throws too and deletes nothing.
        /// </summary>
        public bool Restore(string carId)
        {
            using var gate = AcInstallGate.Hold();
            var keep = KeepDirectory(carId);
            var manifestPath = Path.Combine(keep, ManifestFile);
            if (!File.Exists(manifestPath)) return false;

            Manifest? manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The manifest of {carId} cannot be read; its originals are left under {keep}", ex);
            }

            if (manifest == null)
                throw new InvalidDataException($"The manifest of {carId} is empty; its originals are left under {keep}");

            // "Files": null deserializes over the initializer: without the list the manifest cannot say what goes where
            if (manifest.Files == null)
                throw new InvalidDataException($"The manifest of {carId} has no file list; its originals are left under {keep}");

            var carDirectory = ChangedCarDirectory(carId, manifest);
            var dataDirectory = Path.Combine(carDirectory, AcCarData.DataFolder);
            var failures = new List<Exception>();

            if (manifest.CreatedDataFolder)
            {
                try
                {
                    if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
                    _logger.Information("{Car}: the unpacked data folder is gone again", carId);
                    manifest.CreatedDataFolder = false;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _logger.Error(ex, "{Car}: the unpacked data folder {Folder} could not be deleted", carId, dataDirectory);
                }
            }
            else if (manifest.Files.Count > 0)
            {
                var left = new List<Manifest.Entry>();
                foreach (var entry in manifest.Files)
                {
                    try
                    {
                        RestoreFile(carId, keep, dataDirectory, entry);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(ex);
                        left.Add(entry);
                        _logger.Error(ex, "{Car}: {File} could not be put back; its original stays under {Keep}", carId, entry.Name, keep);
                    }
                }

                _logger.Information("{Car}: {Count} of {Total} data file(s) put back", carId, manifest.Files.Count - left.Count, manifest.Files.Count);
                manifest.Files = left;
            }

            if (manifest.Sfx != null)
            {
                try
                {
                    RestoreSound(carId, Path.Combine(carDirectory, AcCarSound.SfxFolder), manifest.Sfx);
                    manifest.Sfx = null;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    _logger.Error(ex, "{Car}: its own sound could not be put back", carId);
                }
            }

            if (failures.Count > 0)
            {
                // What went back is off the list; what did not is tried again next time, from the same originals
                WriteManifest(keep, manifest);
                throw new AggregateException($"{carId}: {failures.Count} step(s) of the restore failed; the next start tries again", failures);
            }

            Directory.Delete(keep, true);
            _appliedNow.Remove(carId);
            return true;
        }

        /// <summary>
        /// One data file back from its kept copy (or deleted, when the car did not have it), with its read-only flag as
        /// it was. A kept copy that is gone cannot ever go back: that is logged as lost and not retried forever.
        /// </summary>
        private void RestoreFile(string carId, string keep, string dataDirectory, Manifest.Entry entry)
        {
            var target = DataFile(dataDirectory, entry.Name);
            if (!entry.Existed)
            {
                if (File.Exists(target))
                {
                    ClearReadOnly(target);
                    File.Delete(target);
                }

                return;
            }

            var kept = Path.Combine(keep, FilesFolder, entry.Name);
            if (!File.Exists(kept))
            {
                _logger.Error("{Car}: the kept original of {File} is gone (deleted outside the game?); the race version stays in {Target}", carId, entry.Name, target);
                return;
            }

            // Written whole or not at all: a restore cut short never leaves the car a truncated file
            if (File.Exists(target)) ClearReadOnly(target);
            SafeFile.WriteAllBytes(target, File.ReadAllBytes(kept));
            if (entry.ReadOnly) File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        }

        /// <summary>A data file's path, for a name that is one file name inside the data folder</summary>
        private static string DataFile(string dataDirectory, string name)
        {
            if (!PathNames.IsSafeSegment(name)) throw new InvalidDataException($"'{name}' is not a data file name");
            return Path.Combine(dataDirectory, name);
        }

        private static void ClearReadOnly(string file)
        {
            var attributes = File.GetAttributes(file);
            if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }

        private static void WriteManifest(string keep, Manifest manifest) =>
            SafeFile.WriteAllText(Path.Combine(keep, ManifestFile), JsonConvert.SerializeObject(manifest, Formatting.Indented));

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
            using var gate = AcInstallGate.Hold();
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
            using var gate = AcInstallGate.Hold();
            var source = CarDirectory(carId);
            if (!IsCloneId(cloneId)) throw new ArgumentException($"'{cloneId}' is not a name for a copy (it must end with {AcCarFolder.CloneSuffix})", nameof(cloneId));
            var target = CarDirectory(cloneId);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"{carId} is not installed");
            if (_appliedNow.Contains(carId)) throw new InvalidOperationException($"{carId} is already changed for this race: its copy is made before that");
            var targetData = Path.Combine(target, AcCarData.DataFolder);
            foreach (var name in files.Keys) DataFile(targetData, name);
            RestoreLeftover(carId);

            if (Directory.Exists(target))
            {
                if (!IsOurClone(target)) throw new IOException($"{cloneId} is a car of the install, not a copy: it is left alone");
                RemoveClone(cloneId);
            }

            // Recorded before the folder exists: whatever becomes of the settings, the copy is found again
            RecordClone(target);
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
            foreach (var (name, content) in files) File.WriteAllText(DataFile(targetData, name), content, Encoding.Latin1);

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

        /// <summary>
        /// Deletes a copy made for a race. Only a folder that is ours on every count is touched: its name ends with
        /// <see cref="AcCarFolder.CloneSuffix"/>, it sits right in the install's content\cars, and it has the marker.
        /// A user's own car that happens to contain a marker file (a copy kept as a car, a mod shipping the file) is
        /// left alone.
        /// </summary>
        public bool RemoveClone(string cloneId)
        {
            using var gate = AcInstallGate.Hold();
            if (!IsCloneId(cloneId)) return false;
            var folder = CarDirectory(cloneId);
            if (!Directory.Exists(folder) || !IsOurClone(folder)) return false;

            DeleteClone(folder);
            _logger.Information("{Clone}: the copy is gone", cloneId);
            return true;
        }

        /// <summary>
        /// Deletes a copy's folder, checked to be ours by the caller. Deleting a link leaves the car's own file where it
        /// is. The marker goes last, after every file and folder, so a delete cut short leaves a folder still known to
        /// be ours, and the next start finishes it.
        /// </summary>
        private static void DeleteClone(string folder)
        {
            var marker = Path.Combine(folder, AcCarFolder.CloneMarker);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!file.Equals(marker, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }

            foreach (var directory in Directory.GetDirectories(folder)) Directory.Delete(directory, true);
            File.Delete(marker);
            Directory.Delete(folder);
        }

        /// <summary>
        /// Every copy in the install, whichever race made it, and every recorded copy in an install the game ran on
        /// before (the AC folder in the settings changed since)
        /// </summary>
        public int RemoveClones()
        {
            using var gate = AcInstallGate.Hold();
            var removed = 0;
            if (Directory.Exists(_carsPath))
            {
                foreach (var folder in Directory.GetDirectories(_carsPath).Where(IsOurClone))
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
            }

            return removed + RemoveRecordedClones();
        }

        /// <summary>
        /// The copies on record that are still there, wherever they are: each one only when it still looks like ours
        /// (named as a copy, in a "cars" folder, with the marker or empty). What is gone or not ours drops off the
        /// record; what could not be deleted stays on it for the next try.
        /// </summary>
        private int RemoveRecordedClones()
        {
            var recorded = ReadClones();
            if (recorded.Count == 0) return 0;

            var removed = 0;
            var left = new List<string>();
            foreach (var folder in recorded)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    if (!LooksLikeOurClone(folder))
                    {
                        _logger.Warning("{Folder} is on record as a race copy but is not one any more: it is left alone", folder);
                        continue;
                    }

                    DeleteClone(folder);
                    removed++;
                    _logger.Information("{Folder}: a copy left from an earlier race is gone", folder);
                }
                catch (Exception ex)
                {
                    left.Add(folder);
                    _logger.Error(ex, "Could not delete the copy {Folder}; the next start tries again", folder);
                }
            }

            try
            {
                WriteClones(left);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "The record of race copies {Path} could not be updated; what is gone drops off it next time", ClonesPath);
            }

            return removed;
        }

        private string ClonesPath => Path.Combine(_keepPath, ClonesFile);

        /// <summary>The copies on record; an unreadable record is logged and read as none (the install's own scan still runs)</summary>
        private List<string> ReadClones()
        {
            if (!File.Exists(ClonesPath)) return [];
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(ClonesPath))?
                    .Where(f => !string.IsNullOrWhiteSpace(f) && Path.IsPathFullyQualified(f))
                    .ToList() ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "The record of race copies {Path} is not readable; only the install's own copies are looked for", ClonesPath);
                return [];
            }
        }

        private void WriteClones(List<string> folders)
        {
            if (folders.Count == 0)
            {
                if (File.Exists(ClonesPath)) File.Delete(ClonesPath);
                return;
            }

            SafeFile.WriteAllText(ClonesPath, JsonConvert.SerializeObject(folders, Formatting.Indented));
        }

        private void RecordClone(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var recorded = ReadClones();
            if (recorded.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
            recorded.Add(full);
            WriteClones(recorded);
        }

        private static bool IsCloneId(string? id) =>
            PathNames.IsSafeSegment(id) && id!.EndsWith(AcCarFolder.CloneSuffix, StringComparison.OrdinalIgnoreCase)
            && id.Length > AcCarFolder.CloneSuffix.Length;

        /// <summary>
        /// A copy of ours: named as one, directly in content\cars, with the marker. Or with nothing in it at all: a copy
        /// cut short between its folder and its marker (nobody else makes an empty folder with that name, and deleting
        /// it loses nothing).
        /// </summary>
        private bool IsOurClone(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var parent = Path.GetDirectoryName(full);
            return parent != null
                && string.Equals(parent, Path.TrimEndingDirectorySeparator(Path.GetFullPath(_carsPath)), StringComparison.OrdinalIgnoreCase)
                && LooksLikeOurClone(full);
        }

        /// <summary>A copy of ours in any install: named as one, in a "cars" folder, with the marker or empty</summary>
        private static bool LooksLikeOurClone(string folder)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var parent = Path.GetDirectoryName(full);
            return IsCloneId(Path.GetFileName(full))
                && parent != null
                && string.Equals(Path.GetFileName(parent), "cars", StringComparison.OrdinalIgnoreCase)
                && (AcCarFolder.IsClone(full) || !Directory.EnumerateFileSystemEntries(full).Any());
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

        /// <summary>
        /// What a packed car's data.acd holds, written out as a data folder. The archive comes with a mod and is not
        /// trusted: every entry must be one file name, and one that is not (a path, "..") refuses the whole archive
        /// before anything is written.
        /// </summary>
        private static void UnpackAcd(string carId, string carDirectory, string dataDirectory)
        {
            var entries = AcdFile.Read(AcdOf(carId, carDirectory));
            var files = new List<(string Path, byte[] Content)>();
            foreach (var (name, content) in entries) files.Add((DataFile(dataDirectory, name), content));

            Directory.CreateDirectory(dataDirectory);
            foreach (var (path, content) in files) File.WriteAllBytes(path, content);
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
