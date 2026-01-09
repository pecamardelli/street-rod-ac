namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Represents a complete INI file with all sections, keys, and preserved formatting.
    /// Follows the preservation philosophy: read everything, understand what we need.
    /// </summary>
    public class IniFile
    {
        /// <summary>
        /// File path this INI was loaded from
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// All sections in this file, preserving order
        /// </summary>
        public List<IniSection> Sections { get; set; } = [];

        /// <summary>
        /// Lines that appear before any section (header comments, blank lines)
        /// </summary>
        public List<string> HeaderLines { get; set; } = [];

        /// <summary>
        /// Whether this file has been modified
        /// </summary>
        public bool IsModified { get; set; } = false;

        /// <summary>
        /// Timestamp when file was loaded
        /// </summary>
        public DateTime LoadedAt { get; set; } = DateTime.Now;

        public IniFile()
        {
        }

        public IniFile(string filePath)
        {
            FilePath = filePath;
        }

        /// <summary>
        /// Get a section by name (case-insensitive)
        /// </summary>
        public IniSection? GetSection(string sectionName)
        {
            return Sections.FirstOrDefault(s =>
                string.Equals(s.Name, sectionName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get or create a section
        /// </summary>
        public IniSection GetOrCreateSection(string sectionName)
        {
            var section = GetSection(sectionName);
            if (section == null)
            {
                section = new IniSection(sectionName);
                Sections.Add(section);
            }
            return section;
        }

        /// <summary>
        /// Get a key value from a specific section
        /// </summary>
        public string? GetValue(string sectionName, string keyName)
        {
            var section = GetSection(sectionName);
            var key = section?.GetKey(keyName);
            return key?.Value;
        }

        /// <summary>
        /// Set a key value in a specific section (creates section if needed)
        /// </summary>
        public void SetValue(string sectionName, string keyName, string value, bool markAsModified = true)
        {
            var section = GetOrCreateSection(sectionName);
            section.SetKey(keyName, value, markAsModified);
            IsModified = true;
        }

        /// <summary>
        /// Check if file has a specific section
        /// </summary>
        public bool HasSection(string sectionName)
        {
            return GetSection(sectionName) != null;
        }

        public override string ToString() => $"IniFile: {FilePath} ({Sections.Count} sections)";
    }
}
