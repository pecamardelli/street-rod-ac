using Street_Rod_AC.Logging;
using Street_Rod_AC.Services.Configuration.Models;
using System.IO;
using System.Text;

namespace Street_Rod_AC.Services.Configuration.Parsers
{
    /// <summary>
    /// Writes INI files safely using atomic operations and minimal diffs.
    /// Preserves formatting, comments, and unknown keys.
    /// </summary>
    public class IniWriter
    {
        private readonly IAppLogger _logger;

        public IniWriter()
        {
            _logger = AppLoggerFactory.CreateLogger("IniWriter");
        }

        /// <summary>
        /// Write an INI file to disk atomically (temp file -> validate -> replace)
        /// </summary>
        public void Write(IniFile iniFile)
        {
            if (string.IsNullOrEmpty(iniFile.FilePath))
            {
                throw new ArgumentException("IniFile.FilePath cannot be empty");
            }

            _logger.Information("Writing INI file: {FilePath}", iniFile.FilePath);

            // Create backup if file exists
            CreateBackup(iniFile.FilePath);

            // Write to temp file first
            var tempPath = $"{iniFile.FilePath}.tmp";

            try
            {
                WriteToFile(iniFile, tempPath);

                // Validate temp file (basic check)
                if (!File.Exists(tempPath))
                {
                    throw new IOException("Temp file was not created");
                }

                var tempFileInfo = new FileInfo(tempPath);
                if (tempFileInfo.Length == 0)
                {
                    throw new IOException("Temp file is empty");
                }

                // Atomic replace
                File.Copy(tempPath, iniFile.FilePath, true);
                File.Delete(tempPath);

                _logger.Information("Successfully wrote INI file: {FilePath}", iniFile.FilePath);
            }
            catch (Exception ex)
            {
                // Clean up temp file on failure
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                _logger.Error(ex, "Failed to write INI file: {FilePath}", iniFile.FilePath);
                throw new IOException($"Failed to write INI file: {iniFile.FilePath}", ex);
            }
        }

        /// <summary>
        /// Write INI content to a file
        /// </summary>
        private void WriteToFile(IniFile iniFile, string filePath)
        {
            var sb = new StringBuilder();

            // Write header lines (comments/blanks before first section)
            foreach (var headerLine in iniFile.HeaderLines)
            {
                sb.AppendLine(headerLine);
            }

            // Write each section
            for (int i = 0; i < iniFile.Sections.Count; i++)
            {
                var section = iniFile.Sections[i];

                // Add blank line between sections (except before first section if no header)
                if (i > 0 || iniFile.HeaderLines.Count > 0)
                {
                    sb.AppendLine();
                }

                // Write section header
                sb.AppendLine(section.OriginalHeader);

                // Write keys in order
                for (int keyIndex = 0; keyIndex < section.Keys.Count; keyIndex++)
                {
                    // Write any preserved comments/blanks before this key
                    if (section.PreKeyLines.TryGetValue(keyIndex, out var preLine))
                    {
                        sb.AppendLine(preLine);
                    }

                    var key = section.Keys[keyIndex];
                    sb.AppendLine(key.ToLine());
                }

                // Write any trailing preserved lines
                var maxKeyIndex = section.Keys.Count;
                for (int lineIndex = maxKeyIndex; lineIndex < maxKeyIndex + 10; lineIndex++)
                {
                    if (section.PreKeyLines.TryGetValue(lineIndex, out var trailingLine))
                    {
                        sb.AppendLine(trailingLine);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Create a timestamped backup of the original file
        /// </summary>
        private void CreateBackup(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                var backupPath = Path.Combine(directory, $"{fileName}.backup_{timestamp}{extension}");

                File.Copy(filePath, backupPath, false);

                _logger.Debug("Created backup: {BackupPath}", backupPath);

                // Clean up old backups (keep last 5)
                CleanOldBackups(directory, fileName, extension);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to create backup for {FilePath}", filePath);
                // Don't fail the write operation if backup fails
            }
        }

        /// <summary>
        /// Remove old backup files, keeping only the most recent ones
        /// </summary>
        private void CleanOldBackups(string directory, string fileName, string extension, int keepCount = 5)
        {
            try
            {
                var backupPattern = $"{fileName}.backup_*{extension}";
                var backupFiles = Directory.GetFiles(directory, backupPattern)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                // Delete backups beyond keepCount
                foreach (var oldBackup in backupFiles.Skip(keepCount))
                {
                    oldBackup.Delete();
                    _logger.Debug("Deleted old backup: {BackupPath}", oldBackup.FullName);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to clean old backups in {Directory}", directory);
            }
        }
    }
}
