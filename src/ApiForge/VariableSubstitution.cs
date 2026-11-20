using System.Text.RegularExpressions;

namespace ApiForge;

/// <summary>
/// Holds the variables available while a collection runs: environment values,
/// --var overrides, and values captured from earlier responses. Names are
/// case-insensitive; later sources override earlier ones.
/// </summary>
public sealed class VariableStore
{
    private static readonly Regex Placeholder = new(
        @"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}",
        RegexOptions.Compiled);

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public VariableStore() { }

    public VariableStore(IEnumerable<KeyValuePair<string, string>> initial)
    {
        foreach (var kv in initial)
            _values[kv.Key] = kv.Value;
    }

    public void Set(string name, string value) => _values[name] = value;

    public bool TryGet(string name, out string value) => _values.TryGetValue(name, out value!);

    public IReadOnlyDictionary<string, string> Snapshot() => _values;

    /// <summary>
    /// Replaces every {{name}} placeholder with its value. Placeholders with
    /// no known variable are left in place so the caller can report them.
    /// </summary>
    public string Substitute(string template)
    {
        if (string.IsNullOrEmpty(template))
            return template;
        return Placeholder.Replace(template, match =>
            _values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    /// <summary>Returns the names of placeholders in <paramref name="template"/> with no value.</summary>
    public List<string> MissingVariables(string template)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(template))
            return missing;
        foreach (Match match in Placeholder.Matches(template))
        {
            var name = match.Groups[1].Value;
            if (!_values.ContainsKey(name) && !missing.Contains(name))
                missing.Add(name);
        }
        return missing;
    }

    /// <summary>True when every placeholder in the template has a value.</summary>
    public bool IsFullyResolved(string template) => MissingVariables(template).Count == 0;
}
