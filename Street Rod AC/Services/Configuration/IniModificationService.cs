using Street_Rod_AC.Logging;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Configuration.Parsers;
using System.IO;

namespace Street_Rod_AC.Services.Configuration
{
    /// <summary>
    /// Central service for all Assetto Corsa INI file modifications.
    /// Implements the Read -> Intent -> Apply model.
    /// </summary>
    public class IniModificationService : IIniModificationService
    {
        private readonly string _cfgDirectory;
        private readonly IniParser _parser;
        private readonly IniWriter _writer;
        private readonly IAppLogger _logger;

        public IniModificationService()
        {
            // AC cfg files are in Documents/Assetto Corsa/cfg
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _cfgDirectory = Path.Combine(documentsPath, "Assetto Corsa", "cfg");

            _parser = new IniParser();
            _writer = new IniWriter();
            _logger = AppLoggerFactory.CreateLogger("IniModification");

            // Ensure cfg directory exists
            if (!Directory.Exists(_cfgDirectory))
            {
                _logger.Warning("AC cfg directory not found: {CfgDirectory}", _cfgDirectory);
            }
            else
            {
                _logger.Information("INI Modification Service initialized. CFG directory: {CfgDirectory}", _cfgDirectory);
            }
        }

        public bool ApplyIntent(ModificationIntent intent)
        {
            _logger.Information("Applying intent: {Description}", intent.Description);

            try
            {
                return intent switch
                {
                    ShowroomIntent showroomIntent => ApplyShowroomIntent(showroomIntent),
                    DisableAssistsIntent assistsIntent => ApplyDisableAssistsIntent(assistsIntent),
                    DragRaceIntent dragRaceIntent => ApplyDragRaceIntent(dragRaceIntent),
                    RaceConfigIntent raceIntent => ApplyRaceConfigIntent(raceIntent),
                    _ => throw new NotSupportedException($"Intent type not supported: {intent.GetType().Name}")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply intent: {Description}", intent.Description);
                return false;
            }
        }

        public IniFile? ReadIniFile(string fileName)
        {
            var filePath = GetIniFilePath(fileName);
            return _parser.TryParse(filePath);
        }

        public bool FileExists(string fileName)
        {
            var filePath = GetIniFilePath(fileName);
            return File.Exists(filePath);
        }

        public string GetIniFilePath(string fileName)
        {
            // If already absolute path, return as-is
            if (Path.IsPathRooted(fileName))
            {
                return fileName;
            }

            // Otherwise, resolve relative to cfg directory
            return Path.Combine(_cfgDirectory, fileName);
        }

        public bool RestoreFromBackup(string fileName, string? backupTimestamp = null)
        {
            try
            {
                var filePath = GetIniFilePath(fileName);
                var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
                var fileNameOnly = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);

                var backupPattern = $"{fileNameOnly}.backup_*{extension}";
                var backups = Directory.GetFiles(directory, backupPattern)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                if (backups.Count == 0)
                {
                    _logger.Warning("No backups found for {FileName}", fileName);
                    return false;
                }

                FileInfo backupToRestore;

                if (backupTimestamp != null)
                {
                    backupToRestore = backups.FirstOrDefault(b => b.Name.Contains(backupTimestamp))
                        ?? throw new FileNotFoundException($"Backup with timestamp {backupTimestamp} not found");
                }
                else
                {
                    backupToRestore = backups.First(); // Most recent
                }

                File.Copy(backupToRestore.FullName, filePath, true);
                _logger.Information("Restored {FileName} from backup: {BackupFile}",
                    fileName, backupToRestore.Name);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to restore {FileName} from backup", fileName);
                return false;
            }
        }

        public void DeleteBackups(string fileName)
        {
            try
            {
                var filePath = GetIniFilePath(fileName);
                var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
                var fileNameOnly = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);

                var backupPattern = $"{fileNameOnly}.backup_*{extension}";
                var backups = Directory.GetFiles(directory, backupPattern);

                foreach (var backupFile in backups)
                {
                    File.Delete(backupFile);
                    _logger.Debug("Deleted backup: {BackupFile}", Path.GetFileName(backupFile));
                }

                if (backups.Length > 0)
                {
                    _logger.Information("Deleted {Count} backup file(s) for {FileName}", backups.Length, fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete backups for {FileName}", fileName);
            }
        }

        // ===== INTENT APPLICATION METHODS =====

        private bool ApplyShowroomIntent(ShowroomIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);

            // Parse or create new file
            var iniFile = _parser.TryParse(filePath) ?? new IniFile(filePath);

            // Apply changes to [SHOWROOM] section
            iniFile.SetValue("SHOWROOM", "CAR", intent.CarId);
            iniFile.SetValue("SHOWROOM", "SKIN", intent.SkinId);
            iniFile.SetValue("SHOWROOM", "SELECTED_SKIN", intent.SkinId);
            iniFile.SetValue("SHOWROOM", "TRACK", intent.Track);

            // Write back
            _writer.Write(iniFile);

            _logger.Information("Applied showroom intent: Car={CarId}, Skin={SkinId}",
                intent.CarId, intent.SkinId);

            return true;
        }

        private bool ApplyDisableAssistsIntent(DisableAssistsIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);

            var iniFile = _parser.TryParse(filePath) ?? new IniFile(filePath);

            // TODO: Implement assist disabling logic
            // This would set ABS=0, TC=0, etc. in the assists.ini file

            _writer.Write(iniFile);

            _logger.Information("Applied disable assists intent");

            return true;
        }

        private bool ApplyRaceConfigIntent(RaceConfigIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);

            var iniFile = _parser.TryParse(filePath) ?? new IniFile(filePath);

            // Configure [RACE] section
            iniFile.SetValue("RACE", "MODEL", intent.CarId);
            iniFile.SetValue("RACE", "SKIN", intent.SkinId);
            iniFile.SetValue("RACE", "TRACK", intent.TrackId);
            iniFile.SetValue("RACE", "CONFIG_TRACK", intent.TrackConfig ?? string.Empty);

            _writer.Write(iniFile);

            _logger.Information("Applied race config intent: Car={CarId}, Skin={SkinId}, Track={TrackId}, Config={TrackConfig}",
                intent.CarId, intent.SkinId, intent.TrackId, intent.TrackConfig ?? "(none)");

            return true;
        }

        private bool ApplyDragRaceIntent(DragRaceIntent intent)
        {
            var filePath = GetIniFilePath(intent.TargetFile);

            _logger.Information("Applying drag race intent (generating INI from code)");

            // Create backup if file exists (before we modify it)
            if (File.Exists(filePath))
            {
                var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupPath = Path.Combine(directory, $"{fileName}.backup_{timestamp}{extension}");

                File.Copy(filePath, backupPath, false);
                _logger.Debug("Created backup: {BackupPath}", Path.GetFileName(backupPath));
            }

            // Build the race.ini content from scratch
            var content = BuildDragRaceIni(intent);

            // Write to race.ini
            File.WriteAllText(filePath, content);

            _logger.Information("Applied drag race intent: Player={PlayerName} ({PlayerCarId}), Opponent={OpponentName} ({OpponentCarId}), AI={AILevel}/{AIAggression}",
                intent.PlayerName, intent.PlayerCarId, intent.OpponentName, intent.OpponentCarId, intent.OpponentAILevel, intent.OpponentAIAggression);

            return true;
        }

        /// <summary>
        /// Builds the complete drag race INI file content with all required sections
        /// </summary>
        private string BuildDragRaceIni(DragRaceIntent intent)
        {
            var sb = new System.Text.StringBuilder();

            // [BENCHMARK]
            sb.AppendLine("[BENCHMARK]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [DYNAMIC_TRACK]
            sb.AppendLine("[DYNAMIC_TRACK]");
            sb.AppendLine("LAP_GAIN=1");
            sb.AppendLine("RANDOMNESS=0");
            sb.AppendLine("SESSION_START=100");
            sb.AppendLine("SESSION_TRANSFER=100");
            sb.AppendLine("PRESET=5");
            sb.AppendLine();

            // [GHOST_CAR]
            sb.AppendLine("[GHOST_CAR]");
            sb.AppendLine("ENABLED=0");
            sb.AppendLine("FILE=");
            sb.AppendLine("LOAD=0");
            sb.AppendLine("PLAYING=0");
            sb.AppendLine("RECORDING=0");
            sb.AppendLine("SECONDS_ADVANTAGE=0");
            sb.AppendLine();

            // [GROOVE]
            sb.AppendLine("[GROOVE]");
            sb.AppendLine("VIRTUAL_LAPS=10");
            sb.AppendLine("MAX_LAPS=30");
            sb.AppendLine("STARTING_LAPS=0");
            sb.AppendLine();

            // [HEADER]
            sb.AppendLine("[HEADER]");
            sb.AppendLine("VERSION=1");
            sb.AppendLine("__CM_FEATURE_SET=2");
            sb.AppendLine("__CM_NEW_MODE_USED=sr_race");  // CSP new-mode identifier
            sb.AppendLine();

            // [LAP_INVALIDATOR]
            sb.AppendLine("[LAP_INVALIDATOR]");
            sb.AppendLine("ALLOWED_TYRES_OUT=-1");
            sb.AppendLine();

            // [LIGHTING]
            sb.AppendLine("[LIGHTING]");
            sb.AppendLine("CLOUD_SPEED=0.200");
            sb.AppendLine("SUN_ANGLE=-16.00");
            sb.AppendLine("TIME_MULT=1.0");
            sb.AppendLine("__CM_WEATHER_TYPE=19");
            sb.AppendLine("__TRACK_GEOTAG_LONG=0.231944444444444");
            sb.AppendLine("__TRACK_TIMEZONE_BASE_OFFSET=3600");
            sb.AppendLine("__TRACK_TIMEZONE_DTS=0");
            sb.AppendLine("__TRACK_TIMEZONE_OFFSET=3600");
            sb.AppendLine("__TRACK_GEOTAG_LAT=47.9602777777778");
            sb.AppendLine("__CM_WEATHER_CONTROLLER=base");
            sb.AppendLine();

            // [OPTIONS]
            sb.AppendLine("[OPTIONS]");
            sb.AppendLine("USE_MPH=0");
            sb.AppendLine();

            // [RACE] - Player car info and track
            sb.AppendLine("[RACE]");
            sb.AppendLine("AI_LEVEL=100");
            sb.AppendLine("CARS=2");
            sb.AppendLine($"CONFIG_TRACK={intent.TrackConfig ?? string.Empty}");
            sb.AppendLine("DRIFT_MODE=0");
            sb.AppendLine("FIXED_SETUP=0");
            sb.AppendLine("JUMP_START_PENALTY=1");
            sb.AppendLine($"MODEL={intent.PlayerCarId}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine("PENALTIES=0");
            sb.AppendLine("RACE_LAPS=1");
            sb.AppendLine($"SKIN={intent.PlayerSkin}");
            sb.AppendLine($"TRACK={intent.TrackId}");
            sb.AppendLine("MODE=sr_race");  // CSP new-mode for auto-start and auto-quit
            sb.AppendLine();

            // [REMOTE]
            sb.AppendLine("[REMOTE]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine("GUID=");
            sb.AppendLine("NAME=");
            sb.AppendLine("PASSWORD=");
            sb.AppendLine("REQUESTED_CAR=");
            sb.AppendLine("SERVER_IP=");
            sb.AppendLine("SERVER_PORT=");
            sb.AppendLine("TEAM=");
            sb.AppendLine();

            // [REPLAY]
            sb.AppendLine("[REPLAY]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine("FILENAME=");
            sb.AppendLine();

            // [RESTART]
            sb.AppendLine("[RESTART]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [TEMPERATURE]
            sb.AppendLine("[TEMPERATURE]");
            sb.AppendLine("AMBIENT=12");
            sb.AppendLine("ROAD=13");
            sb.AppendLine();

            // [WEATHER]
            sb.AppendLine("[WEATHER]");
            sb.AppendLine("NAME=4_mid_clear");
            sb.AppendLine();

            // [WIND]
            sb.AppendLine("[WIND]");
            sb.AppendLine("DIRECTION_DEG=-1");
            sb.AppendLine("SPEED_KMH_MAX=40");
            sb.AppendLine("SPEED_KMH_MIN=2");
            sb.AppendLine();

            // [__PREVIEW_GENERATION]
            sb.AppendLine("[__PREVIEW_GENERATION]");
            sb.AppendLine("ACTIVE=0");
            sb.AppendLine();

            // [SESSION_0] - Drag race session
            sb.AppendLine("[SESSION_0]");
            sb.AppendLine("NAME=Drag Race");
            sb.AppendLine("TYPE=7");
            sb.AppendLine("SPAWN_SET=START");
            sb.AppendLine("MATCHES=10");
            sb.AppendLine();

            // [CAR_0] - Player
            sb.AppendLine("[CAR_0]");
            sb.AppendLine("MODEL=-");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"SKIN={intent.PlayerSkin}");
            sb.AppendLine($"DRIVER_NAME={intent.PlayerName}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");
            sb.AppendLine();

            // [CAR_1] - Opponent (AI)
            sb.AppendLine("[CAR_1]");
            sb.AppendLine($"MODEL={intent.OpponentCarId}");
            sb.AppendLine("MODEL_CONFIG=");
            sb.AppendLine($"AI_LEVEL={intent.OpponentAILevel}");
            sb.AppendLine($"AI_AGGRESSION={intent.OpponentAIAggression}");
            sb.AppendLine($"SKIN={intent.OpponentSkin}");
            sb.AppendLine($"DRIVER_NAME={intent.OpponentName}");
            sb.AppendLine("NATIONALITY=");
            sb.AppendLine("NATION_CODE=");

            return sb.ToString();
        }

        /// <summary>
        /// Replace a specific key value in an INI section
        /// </summary>
        private string ReplaceLine(string content, string section, string key, string newValue)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var inSection = false;
            var result = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Check if we're entering the target section
                if (trimmedLine.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    result.Add(line);
                    continue;
                }

                // Check if we're leaving the section
                if (inSection && trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    inSection = false;
                }

                // If we're in the target section and this line has the key
                if (inSection && trimmedLine.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    // Replace the value
                    result.Add($"{key}={newValue}");
                }
                else
                {
                    result.Add(line);
                }
            }

            return string.Join(Environment.NewLine, result);
        }
    }
}
