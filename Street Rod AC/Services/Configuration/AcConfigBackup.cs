using System.IO;
using Newtonsoft.Json;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Configuration
{
    /// <summary>
    /// Keeps the originals of AC's cfg INI files (race.ini, showroom_start.ini) before the game changes them, and puts
    /// them back after AC exits, the way <see cref="CarDataOverlay"/> does for car data: the original bytes (or the
    /// fact that the file did not exist) go under %AppData%\StreetRodAC\AcRestore first, with a manifest flushed to
    /// disk, and only then is the file written. The restore runs in the launcher's finally, at start-up, on exit and
    /// on a crash; it is idempotent, and one file that cannot go back does not stop the others.
    ///
    /// A file is kept once per race: the first write keeps the user's file, later writes (a second launch after a
    /// restore that failed) find it kept already and leave the kept copy alone, so what goes back is always the
    /// user's own file, never ours.
    /// </summary>
    public class AcConfigBackup
    {
        /// <summary>
        /// The folder under AcRestore. Not a car id (CarDataOverlay's folders are car ids with a manifest.json; this
        /// one has none, so the overlay never takes it for a car).
        /// </summary>
        public const string FolderName = "~cfg";

        private const string ManifestFile = "ini-manifest.json";

        private readonly string _cfgDirectory;
        private readonly string _keep;
        private readonly object _lock = new();
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("IniRestore");

        /// <param name="cfgDirectory">Documents\Assetto Corsa\cfg: only files under it are ever kept or put back</param>
        /// <param name="restoreRoot">%AppData%\StreetRodAC\AcRestore</param>
        public AcConfigBackup(string cfgDirectory, string restoreRoot)
        {
            _cfgDirectory = cfgDirectory;
            _keep = Path.Combine(restoreRoot, FolderName);
        }

        private sealed class Manifest
        {
            public List<Entry> Files { get; set; } = new();

            public sealed class Entry
            {
                /// <summary>The absolute path of the file that was changed</summary>
                public string Path { get; set; } = string.Empty;

                /// <summary>Whether the file was there: put back from the kept copy, or deleted</summary>
                public bool Existed { get; set; }

                /// <summary>The kept copy's name in the keep folder</summary>
                public string Kept { get; set; } = string.Empty;

                public DateTime KeptAt { get; set; }

                /// <summary>
                /// The user's file was read-only: writing it clears the flag, and the restore sets it again. False in
                /// manifests written before it was recorded.
                /// </summary>
                public bool ReadOnly { get; set; }
            }
        }

        private string ManifestPath => Path.Combine(_keep, ManifestFile);

        /// <summary>Whether a file is waiting to be put back</summary>
        public bool HasPending => File.Exists(ManifestPath);

        /// <summary>
        /// Keeps the file's original before it is written. Call it before the first byte of the file changes; a file
        /// already kept (and not yet put back) stays as it was kept.
        /// </summary>
        public void Keep(string path)
        {
            var full = Path.GetFullPath(path);
            if (!PathNames.IsUnder(_cfgDirectory, full))
                throw new InvalidOperationException($"{full} is not an AC cfg file: it is not kept or changed");

            // The install's one gate first (see AcInstallGate), then this file's own lock
            using var gate = AcInstallGate.Hold();
            lock (_lock)
            {
                var manifest = ReadManifestOrSetAside() ?? new Manifest();
                if (manifest.Files.Any(f => string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase))) return;

                Directory.CreateDirectory(_keep);
                var entry = new Manifest.Entry
                {
                    Path = full,
                    Existed = File.Exists(full),
                    ReadOnly = File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReadOnly),
                    // Unique, not the entry's index: after a partial restore the list is shorter, and an index could
                    // name the kept copy of a file still waiting to go back
                    Kept = $"{Guid.NewGuid():N}_{Path.GetFileName(full)}",
                    KeptAt = DateTime.Now
                };

                // The original first, then the manifest that names it; the caller changes the file after this returns
                if (entry.Existed) SafeFile.WriteAllBytes(Path.Combine(_keep, entry.Kept), File.ReadAllBytes(full));
                manifest.Files.Add(entry);
                WriteManifest(manifest);

                _logger.Information("{File}: the original is kept until AC exits ({State})", full, entry.Existed ? "kept" : "did not exist");
            }
        }

        /// <summary>
        /// Puts every kept file back; returns how many went back. A file that cannot go back stays in the manifest
        /// for the next time and is logged; the others go back anyway. A manifest that cannot be read puts nothing
        /// back and deletes nothing: its folder is set aside for the user (see <see cref="ReadManifestOrSetAside"/>).
        /// </summary>
        public int RestoreAll()
        {
            using var gate = AcInstallGate.Hold();
            lock (_lock)
            {
                if (ReadManifestOrSetAside() is not { } manifest) return 0;

                var restored = 0;
                var left = new List<Manifest.Entry>();
                foreach (var entry in manifest.Files)
                {
                    try
                    {
                        RestoreOne(entry);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Could not put {File} back; the original stays kept under {Keep}", entry.Path, _keep);
                        left.Add(entry);
                    }
                }

                if (left.Count == 0)
                {
                    // Everything is back. The manifest goes first and on its own: a kept copy that cannot be deleted
                    // (a virus scanner holding it) must not leave a manifest behind that would later write this old
                    // original over a race.ini the user has edited since
                    File.Delete(ManifestPath);
                    try
                    {
                        Directory.Delete(_keep, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.Warning(ex, "Every cfg file is back, but the keep folder {Keep} could not be deleted; it is left for the next restore", _keep);
                    }
                }
                else
                {
                    manifest.Files = left;
                    WriteManifest(manifest);
                }

                return restored;
            }
        }

        private void RestoreOne(Manifest.Entry entry)
        {
            // A manifest is ours, but it is a file on disk: it never makes us write outside the cfg folder
            if (!PathNames.IsUnder(_cfgDirectory, entry.Path))
                throw new InvalidDataException($"{entry.Path} is not under {_cfgDirectory}");

            if (entry.Existed)
            {
                var kept = Path.Combine(_keep, entry.Kept);
                if (!PathNames.IsSafeSegment(entry.Kept) || !File.Exists(kept))
                    throw new FileNotFoundException($"The kept copy of {entry.Path} is gone", kept);

                if (File.Exists(entry.Path)) File.SetAttributes(entry.Path, File.GetAttributes(entry.Path) & ~FileAttributes.ReadOnly);
                SafeFile.WriteAllBytes(entry.Path, File.ReadAllBytes(kept));
                if (entry.ReadOnly) File.SetAttributes(entry.Path, File.GetAttributes(entry.Path) | FileAttributes.ReadOnly);
                _logger.Information("{File}: the original is back", entry.Path);
            }
            else if (File.Exists(entry.Path))
            {
                File.Delete(entry.Path);
                _logger.Information("{File}: removed again, it was not there before", entry.Path);
            }
        }

        /// <summary>
        /// The manifest, or null when there is none. One that reads as nothing (0 bytes after a power loss, "null",
        /// not JSON) cannot say what goes where: its folder, with the kept originals in it, is renamed aside and
        /// logged, so a new race neither trusts it nor writes over the originals it may hold.
        /// </summary>
        private Manifest? ReadManifestOrSetAside()
        {
            if (!File.Exists(ManifestPath)) return null;

            Manifest? manifest = null;
            try
            {
                manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(ManifestPath));
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "The INI restore manifest {Path} is not readable", ManifestPath);
            }

            // A manifest without its file list ("Files": null) cannot say what goes where either
            if (manifest?.Files != null) return manifest;

            var aside = $"{_keep}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                Directory.Move(_keep, aside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A scanner or a sync client holding a kept file: a race must not wait on that. What can be copied
                // goes aside, and the manifest leaves the keep folder, so a new race starts a manifest of its own.
                _logger.Warning(ex, "The keep folder {Keep} could not be set aside whole; its files are copied to {Aside} instead", _keep, aside);
                CopyAsideAndDropManifest(aside);
            }

            _logger.Error("The INI restore manifest was empty or unreadable: nothing was put back. The kept files are under {Aside}", aside);
            return null;
        }

        /// <summary>
        /// The fallback when the keep folder cannot be moved: every file that can be read is copied to
        /// <paramref name="aside"/>, then the manifest is deleted. Throws only when the manifest cannot go either.
        /// </summary>
        private void CopyAsideAndDropManifest(string aside)
        {
            Directory.CreateDirectory(aside);
            foreach (var file in Directory.GetFiles(_keep))
            {
                try
                {
                    File.Copy(file, Path.Combine(aside, Path.GetFileName(file)), overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warning(ex, "{File} could not be copied aside; it stays in {Keep}", file, _keep);
                }
            }

            File.Delete(ManifestPath);
        }

        private void WriteManifest(Manifest manifest) =>
            SafeFile.WriteAllText(ManifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
    }
}
