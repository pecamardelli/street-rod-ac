using System.IO;
using Newtonsoft.Json;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Settings
{
    /// <summary>
    /// Service for loading and saving application settings
    /// </summary>
    public class GameSettingsService
    {
        private const string SettingsFileName = "settings.json";
        private readonly string _settingsPath;
        private readonly IAppLogger _logger;

        private GameSettings _currentSettings;

        public GameSettings Current => _currentSettings;

        public GameSettingsService()
        {
            _logger = AppLoggerFactory.CreateLogger("GameSettings");
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
            _currentSettings = Load();
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
        /// Save current settings to file
        /// </summary>
        public void Save()
        {
            Save(_currentSettings);
        }

        /// <summary>
        /// Save settings to file
        /// </summary>
        public void Save(GameSettings settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
                _currentSettings = settings;
                _logger.Information("Settings saved to {Path}", _settingsPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings to {Path}", _settingsPath);
            }
        }

        /// <summary>
        /// Reset settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            _currentSettings = new GameSettings();
            Save();
            _logger.Information("Settings reset to defaults");
        }
    }
}
