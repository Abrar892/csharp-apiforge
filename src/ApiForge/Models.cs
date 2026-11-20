namespace ApiForge;

/// <summary>
/// A single API request inside a collection.
/// </summary>
public sealed class ApiRequest
{
    /// <summary>Human-readable name shown in reports.</summary>
    public string Name { get; set; } = "";

    /// <summary>HTTP method: GET, POST, PUT, PATCH, DELETE, HEAD.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Path relative to the collection BaseUrl, or a full URL.
    /// Supports {{variable}} substitution in path, query, headers and body.
    /// </summary>
    public string Path { get; set; } = "/";

    public Dictionary<string, string> Headers { get; set; } = new();

    public Dictionary<string, string> Query { get; set; } = new();

    /// <summary>Raw request body (usually JSON). Null means no body.</summary>
    public string? Body { get; set; }

    /// <summary>Values to pull out of the response and store as variables for later requests.</summary>
    public List<Capture> Captures { get; set; } = new();

    /// <summary>Checks that must hold for this request to pass.</summary>
    public List<Assertion> Assertions { get; set; } = new();
}

/// <summary>
/// Pulls one value out of a response into a named variable.
/// </summary>
public sealed class Capture
{
    /// <summary>Variable name the captured value is stored under.</summary>
    public string Name { get; set; } = "";

    /// <summary>Where to read from: "json" (a JSON path in the body) or "header".</summary>
    public string From { get; set; } = "json";

    /// <summary>JSON path such as "$.data.id" when From is "json", header name when "header".</summary>
    public string Path { get; set; } = "";
}

/// <summary>
/// One check on a response. Only the fields relevant to <see cref="Type"/> are used.
/// </summary>
public sealed class Assertion
{
    /// <summary>
    /// Assertion kind: "status", "jsonpath", "header", "body-contains",
    /// "body-regex" or "latency-ms".
    /// </summary>
    public string Type { get; set; } = "status";

    /// <summary>JSON path for "jsonpath", header name for "header".</summary>
    public string? Path { get; set; }

    /// <summary>Expected value: status code, JSON value, or header value.</summary>
    public string? Expected { get; set; }

    /// <summary>Substring (body-contains) or regex pattern (body-regex) the body must match.</summary>
    public string? Value { get; set; }

    /// <summary>Maximum allowed latency in milliseconds (latency-ms).</summary>
    public int? MaxMs { get; set; }

    public override string ToString() => Type switch
    {
        "status" => $"status == {Expected}",
        "jsonpath" => $"{Path} == {Expected}",
        "header" => $"header {Path} == {Expected}",
        "body-contains" => $"body contains \"{Value}\"",
        "body-regex" => $"body matches /{Value}/",
        "latency-ms" => $"latency <= {MaxMs}ms",
        _ => Type,
    };
}

/// <summary>
/// A whole test collection: requests plus the environments they can run against.
/// </summary>
public sealed class ApiCollection
{
    /// <summary>Schema version. Currently only 1 is supported.</summary>
    public int ApiForge { get; set; } = 1;

    public string Name { get; set; } = "Untitled";

    /// <summary>Base URL prepended to relative request paths.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Named environments; each maps variable names to values.</summary>
    public Dictionary<string, Dictionary<string, string>> Environments { get; set; } = new();

    public List<ApiRequest> Requests { get; set; } = new();

    /// <summary>Validates the collection and returns every problem found (empty = valid).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (ApiForge != 1)
            errors.Add($"Unsupported schema version {ApiForge}; this tool reads version 1.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Collection name must not be empty.");
        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl must not be empty.");
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            errors.Add($"BaseUrl '{BaseUrl}' is not an absolute URL.");
        if (Requests.Count == 0)
            errors.Add("Collection has no requests.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in Requests)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                errors.Add("Every request needs a name.");
            else if (!names.Add(req.Name))
                errors.Add($"Duplicate request name '{req.Name}'.");
            if (!HttpMethodNames.IsValid(req.Method))
                errors.Add($"Request '{req.Name}': unsupported method '{req.Method}'.");
            if (string.IsNullOrWhiteSpace(req.Path))
                errors.Add($"Request '{req.Name}': path must not be empty.");
            foreach (var assertion in req.Assertions)
            {
                if (!AssertionTypes.IsValid(assertion.Type))
                    errors.Add($"Request '{req.Name}': unknown assertion type '{assertion.Type}'.");
                else if (assertion.Type == "status" && !int.TryParse(assertion.Expected, out _))
                    errors.Add($"Request '{req.Name}': status assertion needs a numeric expected value.");
                else if (assertion.Type == "latency-ms" && assertion.MaxMs is null)
                    errors.Add($"Request '{req.Name}': latency-ms assertion needs maxMs.");
                else if ((assertion.Type == "jsonpath" || assertion.Type == "header")
                         && string.IsNullOrWhiteSpace(assertion.Path))
                    errors.Add($"Request '{req.Name}': {assertion.Type} assertion needs a path.");
            }
            foreach (var capture in req.Captures)
            {
                if (string.IsNullOrWhiteSpace(capture.Name))
                    errors.Add($"Request '{req.Name}': capture needs a name.");
                else if (capture.From is not ("json" or "header"))
                    errors.Add($"Request '{req.Name}': capture '{capture.Name}' has unknown source '{capture.From}' (use json or header).");
                else if (string.IsNullOrWhiteSpace(capture.Path))
                    errors.Add($"Request '{req.Name}': capture '{capture.Name}' needs a path.");
            }
        }
        return errors;
    }
}

internal static class HttpMethodNames
{
    private static readonly HashSet<string> Valid = new(StringComparer.OrdinalIgnoreCase)
        { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

    public static bool IsValid(string method) => Valid.Contains(method);
}

internal static class AssertionTypes
{
    private static readonly HashSet<string> Valid = new(StringComparer.OrdinalIgnoreCase)
        { "status", "jsonpath", "header", "body-contains", "body-regex", "latency-ms" };

    public static bool IsValid(string type) => Valid.Contains(type);
}
