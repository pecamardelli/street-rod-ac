using System.IO;
using Newtonsoft.Json;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Settings
{
    /// <summary>
    /// Service for loading and saving application settings. They live in %AppData%\StreetRodAC\settings.json:
    /// the program folder may be read-only (Program Files), and a setting that silently fails to save is lost.
    /// </summary>
    public class GameSettingsService
    {
        private const string SettingsFileName = "settings.json";
        private readonly string _settingsPath;
        private readonly IAppLogger _logger;

        private GameSettings _currentSettings;

        public GameSettings Current => _currentSettings;

        /// <summary>The settings file: where the AC folder can be set by hand when the game cannot start to show its Settings screen</summary>
        public string SettingsPath => _settingsPath;

        public GameSettingsService() : this(Path.Combine(AppSettings.AppDataPath, SettingsFileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName))
        {
        }

        /// <param name="settingsPath">Where the settings are kept</param>
        /// <param name="legacyPath">Where earlier versions kept them (next to the program); moved over once</param>
        public GameSettingsService(string settingsPath, string? legacyPath)
        {
            _logger = AppLoggerFactory.CreateLogger("GameSettings");
            _settingsPath = settingsPath;
            MigrateLegacy(legacyPath);
            _currentSettings = Load();
            ApplyInstallPath(_currentSettings);
        }

        /// <summary>
        /// An earlier version's settings.json next to the program is copied to the new place once, when there are no
        /// settings there yet. The old file is removed when the program folder lets us; left alone otherwise.
        /// </summary>
        private void MigrateLegacy(string? legacyPath)
        {
            if (legacyPath == null || File.Exists(_settingsPath) || !File.Exists(legacyPath)) return;

            try
            {
                SafeFile.WriteAllBytes(_settingsPath, File.ReadAllBytes(legacyPath));
                _logger.Information("Settings moved from {Old} to {Path}", legacyPath, _settingsPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not move the settings from {Old} to {Path}", legacyPath, _settingsPath);
                return;
            }

            try
            {
                File.Delete(legacyPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Debug("The old settings file stays at {Old}: {Reason}", legacyPath, ex.Message);
            }
        }

        /// <summary>
        /// The AC folder the settings name becomes the one the whole game uses. Only at load: everything that
        /// changes the install (the overlay, its restore) is built on the folder the game started with.
        /// </summary>
        private void ApplyInstallPath(GameSettings settings)
        {
            var path = string.IsNullOrWhiteSpace(settings.AssettoCorsaPath)
                ? AppSettings.DefaultAssettoCorsaPath
                : settings.AssettoCorsaPath.Trim();
            AppSettings.Instance.AssettoCorsaPath = path;
            _logger.Information("Assetto Corsa folder: {ACPath}", path);
        }

        /// <summary>
        /// Load settings from file, or create default if not exists
        /// </summary>
        public GameSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<GameSettings>(json);
                    if (settings != null)
                    {
                        _logger.Information("Settings loaded from {Path}", _settingsPath);
                        _currentSettings = settings;
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load settings from {Path}", _settingsPath);
            }

            _logger.Information("Using default settings");
            _currentSettings = new GameSettings();
            return _currentSettings;
        }

        /// <summary>
        /// Save current settings to file. False when they could not be written (logged here): the settings
        /// screen tells the player, a remembered preference just tries again next time.
        /// </summary>
        public bool Save()
        {
            return Save(_currentSettings);
        }

        /// <summary>
        /// Save settings to file: a temp file flushed to disk and renamed over the old one, so a crash mid-save
        /// leaves the old settings, never a truncated file that loads as defaults. False when the file could not
        /// be written; the settings in use only change when it was.
        /// </summary>
        public bool Save(GameSettings settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                SafeFile.WriteAllText(_settingsPath, json);
                _currentSettings = settings;
                _logger.Information("Settings saved to {Path}", _settingsPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings to {Path}", _settingsPath);
                return false;
            }
        }

        /// <summary>
        /// Reset settings to defaults. False when the defaults could not be written; the settings in use stay
        /// as they were then, so what the screen shows is what is on disk.
        /// </summary>
        public bool ResetToDefaults()
        {
            if (!Save(new GameSettings())) return false;

            _logger.Information("Settings reset to defaults");
            return true;
        }
    }
}
