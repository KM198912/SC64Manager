using System.Text;

namespace NetGui.Services;

public class IniService
{
    private readonly Dictionary<string, Dictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);

    public void Load(string content)
    {
        _data.Clear();
        string currentSection = "default";
        
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                continue;

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (!_data.ContainsKey(currentSection))
                    _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var parts = trimmed.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    if (!_data.ContainsKey(currentSection))
                        _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    
                    _data[currentSection][parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
    }

    public string Save()
    {
        var sb = new StringBuilder();
        foreach (var section in _data)
        {
            sb.AppendLine($"[{section.Key}]");
            foreach (var kvp in section.Value)
            {
                sb.AppendLine($"{kvp.Key}={kvp.Value}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string GetString(string section, string key, string defaultValue = "")
    {
        if (_data.TryGetValue(section, out var s) && s.TryGetValue(key, out var v))
            return v;
        return defaultValue;
    }

    public bool GetBool(string section, string key, bool defaultValue = false)
    {
        var val = GetString(section, key, defaultValue.ToString().ToLower());
        if (bool.TryParse(val, out bool result)) return result;
        if (val == "1") return true;
        if (val == "0") return false;
        return defaultValue;
    }

    public void SetString(string section, string key, string value)
    {
        if (!_data.ContainsKey(section))
            _data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _data[section][key] = value;
    }

    public void SetInt(string section, string key, int value)
    {
        SetString(section, key, value.ToString());
    }

    public void SetBool(string section, string key, bool value)
    {
        SetString(section, key, value ? "1" : "0");
    }
}
