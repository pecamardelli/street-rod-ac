namespace Street_Rod_AC.Services.Configuration.Models
{
    /// <summary>
    /// Represents a single key-value pair in an INI file section.
    /// Preserves original formatting and value.
    /// </summary>
    public class IniKey
    {
        /// <summary>
        /// The key name (left side of =)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The value (right side of =)
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Original raw line from file (for preservation)
        /// </summary>
        public string OriginalLine { get; set; } = string.Empty;

        /// <summary>
        /// Whether this key was modified by Street Rod Manager
        /// </summary>
        public bool IsModified { get; set; } = false;

        /// <summary>
        /// Original value before modification (for reversal)
        /// </summary>
        public string? OriginalValue { get; set; }

        public IniKey()
        {
        }

        public IniKey(string name, string value, string originalLine)
        {
            Name = name;
            Value = value;
            OriginalLine = originalLine;
        }

        /// <summary>
        /// Creates a formatted line for writing
        /// </summary>
        public string ToLine()
        {
            return $"{Name}={Value}";
        }

        public override string ToString() => $"{Name}={Value}";
    }
}
