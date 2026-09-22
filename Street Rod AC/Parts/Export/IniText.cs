using System.Globalization;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Parts.Export;

/// <summary>
/// Edits the text of an Assetto Corsa ini file in place: only the values asked for change, comments, order and
/// everything else stay as the car's author left them. A section name may occur more than once (a second tyre
/// compound is a second [FRONT]); by default the first occurrence is meant.
/// </summary>
public sealed class IniText
{
    private static readonly Regex Header = new(@"^\s*\[(?<name>[^\]]+)\]", RegexOptions.Compiled);

    private readonly List<string> _lines;

    public IniText(string? text)
    {
        _lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();
        while (_lines.Count > 0 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    public override string ToString() => string.Join("\r\n", _lines) + "\r\n";

    /// <summary>Whether anything was set or removed since the text was read</summary>
    public bool Changed { get; private set; }

    /// <summary>Section names in order, repeated where they occur more than once</summary>
    public IEnumerable<string> Sections => _lines.Select(l => Header.Match(l)).Where(m => m.Success).Select(m => m.Groups["name"].Value);

    public string? Get(string section, string key, int occurrence = 0)
    {
        var (start, end) = Find(section, occurrence);
        for (var i = start + 1; i < end; i++)
        {
            if (KeyOf(_lines[i]) != key) continue;

            var value = _lines[i][(_lines[i].IndexOf('=') + 1)..];
            var comment = value.IndexOf(';');
            return (comment < 0 ? value : value[..comment]).Trim();
        }

        return null;
    }

    public double? GetNumber(string section, string key, int occurrence = 0) =>
        double.TryParse(Get(section, key, occurrence), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    public void Set(string section, string key, string value, int occurrence = 0)
    {
        Changed = true;
        var (start, end) = Find(section, occurrence);
        if (start < 0)
        {
            if (_lines.Count > 0) _lines.Add(string.Empty);
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (KeyOf(_lines[i]) != key) continue;

            // Keep the comment that explains the key
            var comment = _lines[i].IndexOf(';');
            _lines[i] = comment < 0 ? $"{key}={value}" : $"{key}={value}\t\t\t{_lines[i][comment..]}";
            return;
        }

        // New keys go right after the last key of the section, before the blank lines that end it
        var insertAt = end;
        while (insertAt > start + 1 && _lines[insertAt - 1].Trim().Length == 0) insertAt--;
        _lines.Insert(insertAt, $"{key}={value}");
    }

    public void Set(string section, string key, double value, string format, int occurrence = 0) =>
        Set(section, key, value.ToString(format, CultureInfo.InvariantCulture), occurrence);

    /// <summary>
    /// Multiplies a number where the key is there; a key the car does not have is left out, and a factor of
    /// one leaves the line as the author wrote it
    /// </summary>
    public bool Scale(string section, string key, double factor, string format, int occurrence = 0)
    {
        if (GetNumber(section, key, occurrence) is not { } value) return false;

        if (Math.Abs(factor - 1) > 1e-9) Set(section, key, value * factor, format, occurrence);
        return true;
    }

    public void RemoveKey(string section, string key)
    {
        var (start, end) = Find(section);
        for (var i = end - 1; i > start && start >= 0; i--)
        {
            if (KeyOf(_lines[i]) != key) continue;

            _lines.RemoveAt(i);
            Changed = true;
        }
    }

    public void RemoveSection(string section)
    {
        var (start, end) = Find(section);
        if (start < 0) return;

        Changed = true;
        // Comment banners right above the next section belong to that one
        while (end > start + 1 && (_lines[end - 1].Trim().Length == 0 || _lines[end - 1].TrimStart()[0] is ';' or '/')) end--;
        _lines.RemoveRange(start, end - start);
    }

    /// <summary>Line of the section header and the line after the section's last; (-1, -1) when there is none</summary>
    private (int Start, int End) Find(string section, int occurrence = 0)
    {
        var start = -1;
        for (var seen = 0; seen <= occurrence; seen++)
        {
            start = _lines.FindIndex(start + 1, l => Header.Match(l) is { Success: true } m && m.Groups["name"].Value == section);
            if (start < 0) return (-1, -1);
        }

        var end = _lines.FindIndex(start + 1, l => Header.IsMatch(l));
        return (start, end < 0 ? _lines.Count : end);
    }

    private static string? KeyOf(string line)
    {
        var text = line.TrimStart();
        if (text.Length == 0 || text[0] is ';' or '[' or '/') return null;

        var equals = text.IndexOf('=');
        return equals <= 0 ? null : text[..equals].Trim();
    }
}
