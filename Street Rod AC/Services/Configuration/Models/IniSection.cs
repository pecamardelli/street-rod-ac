namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Represents a section in an INI file (e.g., [SHOWROOM]).
    /// Preserves order and comments.
    /// </summary>
    public class IniSection
    {
        /// <summary>
        /// Section name (without brackets)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Keys in this section, preserving order
        /// </summary>
        public List<IniKey> Keys { get; set; } = [];

        /// <summary>
        /// Comments or blank lines that appear before keys in this section
        /// Key: line number relative to section start
        /// Value: the comment or blank line
        /// </summary>
        public Dictionary<int, string> PreKeyLines { get; set; } = [];

        /// <summary>
        /// Original section header line (for preservation)
        /// </summary>
        public string OriginalHeader { get; set; } = string.Empty;

        public IniSection()
        {
        }

        public IniSection(string name)
        {
            Name = name;
            OriginalHeader = $"[{name}]";
        }

        /// <summary>
        /// Get a key by name (case-insensitive)
        /// </summary>
        public IniKey? GetKey(string keyName)
        {
            return Keys.FirstOrDefault(k =>
                string.Equals(k.Name, keyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Set or add a key value
        /// </summary>
        public void SetKey(string keyName, string value, bool markAsModified = true)
        {
            var key = GetKey(keyName);
            if (key != null)
            {
                if (key.OriginalValue == null)
                {
                    key.OriginalValue = key.Value;
                }
                key.Value = value;
                key.IsModified = markAsModified;
            }
            else
            {
                // Add new key
                var newKey = new IniKey(keyName, value, $"{keyName}={value}")
                {
                    IsModified = markAsModified
                };
                Keys.Add(newKey);
            }
        }

        /// <summary>
        /// Check if section has a specific key
        /// </summary>
        public bool HasKey(string keyName)
        {
            return GetKey(keyName) != null;
        }

        public override string ToString() => $"[{Name}] ({Keys.Count} keys)";
    }
}
