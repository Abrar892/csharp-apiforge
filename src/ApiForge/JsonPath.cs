using System.Text.Json;

namespace ApiForge;

/// <summary>
/// A small, dependency-free JSON path evaluator covering the subset API tests
/// actually need: property access, indexes, wildcards and recursive descent.
/// Supported syntax: $.store.book[0].title, $.items[*].id, $..price,
/// $['weird key'], $[0].
/// </summary>
public static class JsonPath
{
    /// <summary>Evaluates a path and returns every matching value (possibly none).</summary>
    public static List<JsonElement> Evaluate(JsonElement root, string path)
    {
        var segments = Parse(path);
        var current = new List<JsonElement> { root };
        foreach (var segment in segments)
            current = ApplySegment(current, segment);
        return current;
    }

    /// <summary>Evaluates a path and returns the first match, or null when there is none.</summary>
    public static JsonElement? EvaluateSingle(JsonElement root, string path)
    {
        var matches = Evaluate(root, path);
        return matches.Count == 0 ? null : matches[0];
    }

    /// <summary>True when the path matches at least one value.</summary>
    public static bool Exists(JsonElement root, string path) => Evaluate(root, path).Count > 0;

    /// <summary>
    /// Renders a JSON value the way assertions and captures see it: strings
    /// without quotes, numbers/booleans as written, objects/arrays as compact JSON.
    /// </summary>
    public static string ToDisplayString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Null => "null",
        _ => element.GetRawText(),
    };

    private abstract record Segment;
    private sealed record Property(string Name, bool Recursive) : Segment;
    private sealed record Index(int Position) : Segment;
    private sealed record Wildcard(bool Recursive) : Segment;

    private static List<Segment> Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('$'))
            throw new JsonPathException($"Invalid JSON path '{path}': paths must start with '$'.");

        var segments = new List<Segment>();
        var i = 1; // skip '$'
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var recursive = i + 1 < path.Length && path[i + 1] == '.';
                i += recursive ? 2 : 1;
                if (i < path.Length && path[i] == '*')
                {
                    segments.Add(new Wildcard(recursive));
                    i++;
                }
                else
                {
                    var start = i;
                    while (i < path.Length && (char.IsLetterOrDigit(path[i]) || path[i] is '_' or '-'))
                        i++;
                    if (i == start)
                        throw new JsonPathException($"Invalid JSON path '{path}': expected a property name after '.'.");
                    segments.Add(new Property(path[start..i], recursive));
                }
            }
            else if (path[i] == '[')
            {
                var close = path.IndexOf(']', i);
                if (close < 0)
                    throw new JsonPathException($"Invalid JSON path '{path}': missing ']'.");
                var inner = path[(i + 1)..close].Trim();
                if (inner == "*")
                    segments.Add(new Wildcard(false));
                else if (inner.StartsWith('\'') || inner.StartsWith('"'))
                    segments.Add(new Property(Unquote(inner), false));
                else if (int.TryParse(inner, out var index))
                    segments.Add(new Index(index));
                else
                    throw new JsonPathException($"Invalid JSON path '{path}': cannot parse '[{inner}]'.");
                i = close + 1;
            }
            else
            {
                throw new JsonPathException($"Invalid JSON path '{path}': unexpected character '{path[i]}' at position {i}.");
            }
        }
        return segments;
    }

    private static string Unquote(string quoted)
    {
        if (quoted.Length >= 2 && quoted[0] == quoted[^1] && quoted[0] is '\'' or '"')
            return quoted[1..^1].Replace("\\'", "'").Replace("\\\"", "\"");
        return quoted;
    }

    private static List<JsonElement> ApplySegment(List<JsonElement> inputs, Segment segment)
    {
        var results = new List<JsonElement>();
        foreach (var input in inputs)
        {
            var candidates = segment is Property { Recursive: true } or Wildcard { Recursive: true }
                ? DescendantsAndSelf(input)
                : new List<JsonElement> { input };

            foreach (var candidate in candidates)
            {
                switch (segment)
                {
                    case Property prop:
                        if (candidate.ValueKind == JsonValueKind.Object
                            && candidate.TryGetProperty(prop.Name, out var value))
                            results.Add(value);
                        break;
                    case Index index:
                        if (candidate.ValueKind == JsonValueKind.Array)
                        {
                            var position = index.Position < 0
                                ? candidate.GetArrayLength() + index.Position
                                : index.Position;
                            if (position >= 0 && position < candidate.GetArrayLength())
                                results.Add(candidate[position]);
                        }
                        break;
                    case Wildcard:
                        if (candidate.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in candidate.EnumerateArray())
                                results.Add(item);
                        }
                        else if (candidate.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var property in candidate.EnumerateObject())
                                results.Add(property.Value);
                        }
                        break;
                }
            }
        }
        return results;
    }

    private static List<JsonElement> DescendantsAndSelf(JsonElement element)
    {
        var found = new List<JsonElement> { element };
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                found.AddRange(DescendantsAndSelf(property.Value));
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                found.AddRange(DescendantsAndSelf(item));
        }
        return found;
    }
}

public sealed class JsonPathException : Exception
{
    public JsonPathException(string message) : base(message) { }
}
