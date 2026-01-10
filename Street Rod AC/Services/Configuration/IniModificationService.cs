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

            // Template path (in application directory)
            var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Project Guidelines", "examples", "drag_race.ini");

            _logger.Information("Applying drag race intent using template: {TemplatePath}", templatePath);

            if (!File.Exists(templatePath))
            {
                _logger.Error("Drag race template not found at: {TemplatePath}", templatePath);
                throw new FileNotFoundException($"Drag race template not found at: {templatePath}");
            }

            // Read template content
            var templateContent = File.ReadAllText(templatePath);

            // Replace values in template
            // RACE section: MODEL and SKIN (for player car)
            templateContent = ReplaceLine(templateContent, "[RACE]", "MODEL", intent.PlayerCarId);
            templateContent = ReplaceLine(templateContent, "[RACE]", "SKIN", intent.PlayerSkin);

            // CAR_0 section (player)
            templateContent = ReplaceLine(templateContent, "[CAR_0]", "SKIN", intent.PlayerSkin);
            templateContent = ReplaceLine(templateContent, "[CAR_0]", "DRIVER_NAME", intent.PlayerName);

            // CAR_1 section (opponent)
            templateContent = ReplaceLine(templateContent, "[CAR_1]", "MODEL", intent.OpponentCarId);
            templateContent = ReplaceLine(templateContent, "[CAR_1]", "SKIN", intent.OpponentSkin);
            templateContent = ReplaceLine(templateContent, "[CAR_1]", "DRIVER_NAME", intent.OpponentName);

            // Write directly to race.ini (IniWriter will create backup automatically)
            File.WriteAllText(filePath, templateContent);

            _logger.Information("Applied drag race intent: Player={PlayerName} ({PlayerCarId}), Opponent={OpponentName} ({OpponentCarId})",
                intent.PlayerName, intent.PlayerCarId, intent.OpponentName, intent.OpponentCarId);

            return true;
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
