using System.Globalization;

namespace SentinelX.Infrastructure.Obs;

/// <summary>Minimal read-only INI parser (OBS config files). Case-insensitive, BOM-tolerant.</summary>
public sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniFile? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return new IniFile(File.ReadAllLines(path));
        }
        catch
        {
            return null;
        }
    }

    public IniFile(IEnumerable<string> lines)
    {
        var current = "";
        _sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                if (!_sections.ContainsKey(current))
                    _sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            _sections[current][line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
    }

    public string? Get(string section, string key)
        => _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : null;

    public int? GetInt(string section, string key)
        => int.TryParse(Get(section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    public double? GetDouble(string section, string key)
        => double.TryParse(Get(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
