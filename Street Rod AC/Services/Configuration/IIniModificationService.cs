using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Services.Configuration
{
    /// <summary>
    /// Central service for all Assetto Corsa INI file modifications.
    /// This is the ONLY interface that should touch INI files.
    ///
    /// Follows the Read -> Intent -> Apply model:
    /// 1. Read: Parse INI preserving everything
    /// 2. Intent: Declare what to change (high-level)
    /// 3. Apply: Translate intent to minimal file changes
    /// </summary>
    public interface IIniModificationService
    {
        /// <summary>
        /// Apply a modification intent to the appropriate INI file.
        /// This is the primary method for all INI modifications.
        /// </summary>
        /// <param name="intent">High-level modification intent</param>
        /// <returns>True if successful, false otherwise</returns>
        bool ApplyIntent(ModificationIntent intent);

        /// <summary>
        /// Read an INI file (for inspection, not modification)
        /// </summary>
        /// <param name="fileName">File name (relative to cfg directory)</param>
        /// <returns>Parsed INI file or null if not found</returns>
        IniFile? ReadIniFile(string fileName);

        /// <summary>
        /// Check if a specific INI file exists
        /// </summary>
        /// <param name="fileName">File name (relative to cfg directory)</param>
        bool FileExists(string fileName);

        /// <summary>
        /// Get the full path to an INI file in the cfg directory
        /// </summary>
        /// <param name="fileName">File name</param>
        string GetIniFilePath(string fileName);

        /// <summary>
        /// Restore a file from backup
        /// </summary>
        /// <param name="fileName">File name to restore</param>
        /// <param name="backupTimestamp">Specific backup timestamp, or null for most recent</param>
        bool RestoreFromBackup(string fileName, string? backupTimestamp = null);

        /// <summary>
        /// Delete all backup files for a specific INI file
        /// </summary>
        /// <param name="fileName">File name whose backups should be deleted</param>
        void DeleteBackups(string fileName);
    }
}
