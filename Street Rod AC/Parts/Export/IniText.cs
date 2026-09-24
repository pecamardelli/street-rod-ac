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

    // Where each section header is, in order: found once and again only after a line is added or removed, not on
    // every Get and Set (a tyres file is asked about two dozen keys per compound)
    private List<(string Name, int Line)>? _headers;

    public IniText(string? text)
    {
        _lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();
        while (_lines.Count > 0 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    public override string ToString() => string.Join("\r\n", _lines) + "\r\n";

    /// <summary>Whether anything was set or removed since the text was read</summary>
    public bool Changed { get; private set; }

    /// <summary>Section names in order, repeated where they occur more than once</summary>
    public IEnumerable<string> Sections => Headers.Select(h => h.Name).ToList();

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
            _headers = null;
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
        _headers = null;
    }

    /// <summary>
    /// A number, written invariant. NaN and infinities are no number to the game: the line is left as the author
    /// wrote it (callers work their figures out to be finite; this is the last line of defence).
    /// </summary>
    public void Set(string section, string key, double value, string format, int occurrence = 0)
    {
        if (!double.IsFinite(value)) return;

        Set(section, key, value.ToString(format, CultureInfo.InvariantCulture), occurrence);
    }

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
            _headers = null;
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
        _headers = null;
    }

    /// <summary>Line of the section header and the line after the section's last; (-1, -1) when there is none</summary>
    private (int Start, int End) Find(string section, int occurrence = 0)
    {
        var headers = Headers;
        var seen = 0;
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Name != section || seen++ < occurrence) continue;

            return (headers[i].Line, i + 1 < headers.Count ? headers[i + 1].Line : _lines.Count);
        }

        return (-1, -1);
    }

    private List<(string Name, int Line)> Headers
    {
        get
        {
            if (_headers != null) return _headers;

            var headers = new List<(string Name, int Line)>();
            for (var i = 0; i < _lines.Count; i++)
            {
                if (Header.Match(_lines[i]) is { Success: true } match) headers.Add((match.Groups["name"].Value, i));
            }

            return _headers = headers;
        }
    }

    private static string? KeyOf(string line)
    {
        var text = line.TrimStart();
        if (text.Length == 0 || text[0] is ';' or '[' or '/') return null;

        var equals = text.IndexOf('=');
        return equals <= 0 ? null : text[..equals].Trim();
    }
}
