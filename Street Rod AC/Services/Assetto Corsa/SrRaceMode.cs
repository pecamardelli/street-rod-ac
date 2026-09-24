using System.IO;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Services.Configuration;

namespace Street_Rod_AC.Services
{
    /// <summary>
    /// The CSP mode every race runs in (<c>apps\new-modes\sr_race</c> in the repo, shipped next to the game under
    /// <c>AcModes\sr_race</c>): it starts the race, judges crashes and false starts, writes the result and quits.
    ///
    /// Installed into the AC install's <c>extension\lua\new-modes\sr_race</c> before every race, a file written
    /// only when it differs, so the mode in the game is always the one this build was made with. It stays there
    /// after the race: it is the game's own folder, which nothing else uses, and a race without it could not start
    /// or report. The Lua app it replaced (<c>apps\lua\sr_race_manager</c>) is removed on the way, since it would
    /// run beside the mode and write a second, older result of the same race.
    /// </summary>
    public sealed class SrRaceMode(string sourceFolder)
    {
        /// <summary>Where the build puts the mode, next to the game</summary>
        public static string DefaultSourceFolder => Path.Combine(AppContext.BaseDirectory, "AcModes", IniModificationService.RaceModeId);

        /// <summary>The Lua app the mode replaced, relative to the AC install</summary>
        private static readonly string RetiredApp = Path.Combine("apps", "lua", "sr_race_manager");

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceMode");

        public SrRaceMode() : this(DefaultSourceFolder)
        {
        }

        /// <summary>The mode's folder in an AC install</summary>
        public static string InstalledFolder(string acRoot) =>
            Path.Combine(acRoot, "extension", "lua", "new-modes", IniModificationService.RaceModeId);

        /// <summary>
        /// Puts the mode into <paramref name="acRoot"/> as this build has it. Null when it is there; otherwise why
        /// the race cannot run, for the player.
        /// </summary>
        public string? Install(string acRoot)
        {
            if (!Directory.Exists(Path.Combine(acRoot, "extension")))
                return "Custom Shaders Patch is not installed in Assetto Corsa: races need it.";

            if (!Directory.Exists(sourceFolder) || !File.Exists(Path.Combine(sourceFolder, "mode.lua")))
                return $"The race mode is missing from the game's folder ({sourceFolder}).";

            try
            {
                var target = InstalledFolder(acRoot);
                Directory.CreateDirectory(target);

                var written = 0;
                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in Directory.GetFiles(sourceFolder))
                {
                    var name = Path.GetFileName(source);
                    wanted.Add(name);
                    var bytes = File.ReadAllBytes(source);
                    var installed = Path.Combine(target, name);
                    if (File.Exists(installed) && File.ReadAllBytes(installed).AsSpan().SequenceEqual(bytes)) continue;

                    SafeFile.WriteAllBytes(installed, bytes);
                    written++;
                }

                // Files a newer build no longer has would still be loaded (or read) by the mode
                foreach (var stale in Directory.GetFiles(target).Where(f => !wanted.Contains(Path.GetFileName(f))))
                {
                    File.Delete(stale);
                    written++;
                }

                if (written > 0)
                    _logger.Information("Race mode installed in {Target} ({Count} file(s) changed)", target, written);

                RemoveRetiredApp(acRoot);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "Could not install the race mode into {AcRoot}", acRoot);
                return $"The race mode could not be installed into Assetto Corsa: {ex.Message}";
            }
        }

        private void RemoveRetiredApp(string acRoot)
        {
            var app = Path.Combine(acRoot, RetiredApp);
            if (!Directory.Exists(app)) return;

            Directory.Delete(app, recursive: true);
            _logger.Information("Removed the old race manager app from {App}: the race mode does its work now", app);
        }
    }
}
