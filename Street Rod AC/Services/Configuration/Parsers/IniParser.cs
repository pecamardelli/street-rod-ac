using Street_Rod_AC.Logging;
using Street_Rod_AC.Services.Configuration.Models;
using System.IO;

namespace Street_Rod_AC.Services.Configuration.Parsers
{
    /// <summary>
    /// Parses INI files while preserving all formatting, comments, and unknown keys.
    /// Follows the "read everything, understand what we need" philosophy.
    /// </summary>
    public class IniParser
    {
        private readonly IAppLogger _logger;

        public IniParser()
        {
            _logger = AppLoggerFactory.CreateLogger("IniParser");
        }

        /// <summary>
        /// Parse an INI file from disk
        /// </summary>
        public IniFile Parse(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning("INI file not found: {FilePath}", filePath);
                throw new FileNotFoundException($"INI file not found: {filePath}");
            }

            _logger.Debug("Parsing INI file: {FilePath}", filePath);

            var iniFile = new IniFile(filePath);
            var lines = File.ReadAllLines(filePath);

            IniSection? currentSection = null;
            int lineNumberInSection = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();

                // Blank line or comment
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(';') || trimmedLine.StartsWith('#'))
                {
                    if (currentSection == null)
                    {
                        // Header comment/blank before any section
                        iniFile.HeaderLines.Add(line);
                    }
                    else
                    {
                        // Comment/blank within a section
                        currentSection.PreKeyLines[lineNumberInSection] = line;
                        lineNumberInSection++;
                    }
                    continue;
                }

                // Section header [SECTION_NAME]
                if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
                {
                    var sectionName = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
                    currentSection = new IniSection(sectionName)
                    {
                        OriginalHeader = line
                    };
                    iniFile.Sections.Add(currentSection);
                    lineNumberInSection = 0;

                    _logger.Debug("Found section: [{SectionName}]", sectionName);
                    continue;
                }

                // Key-value pair KEY=VALUE
                if (trimmedLine.Contains('='))
                {
                    var parts = trimmedLine.Split('=', 2);
                    var keyName = parts[0].Trim();
                    var value = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                    if (currentSection != null)
                    {
                        var key = new IniKey(keyName, value, line);
                        currentSection.Keys.Add(key);
                        lineNumberInSection++;

                        _logger.Debug("  {Key}={Value}", keyName, value);
                    }
                    else
                    {
                        // Key without section - treat as header line (unusual but possible)
                        iniFile.HeaderLines.Add(line);
                        _logger.Warning("Found key-value outside of section: {Line}", line);
                    }
                    continue;
                }

                // Unknown line format - preserve it
                if (currentSection == null)
                {
                    iniFile.HeaderLines.Add(line);
                }
                else
                {
                    currentSection.PreKeyLines[lineNumberInSection] = line;
                    lineNumberInSection++;
                }

                _logger.Debug("Preserved unknown line: {Line}", line);
            }

            _logger.Information("Parsed INI file: {FilePath} - {SectionCount} sections, {KeyCount} total keys",
                filePath, iniFile.Sections.Count, iniFile.Sections.Sum(s => s.Keys.Count));

            return iniFile;
        }

        /// <summary>
        /// Try to parse, return null if file doesn't exist (non-throwing version)
        /// </summary>
        public IniFile? TryParse(string filePath)
        {
            try
            {
                return Parse(filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse INI file: {FilePath}", filePath);
                return null;
            }
        }
    }
}
