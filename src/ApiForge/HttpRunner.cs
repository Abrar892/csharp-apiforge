using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ApiForge;

/// <summary>
/// Executes one request: resolves {{variables}}, builds the HTTP message,
/// sends it and applies captures. The handler is injectable so the runner
/// is fully testable without touching the network.
/// </summary>
public sealed class HttpRunner
{
    private readonly HttpMessageHandler? _handler;

    public HttpRunner(HttpMessageHandler? handler = null)
    {
        _handler = handler;
    }

    public async Task<RequestResult> RunAsync(
        ApiRequest request,
        string baseUrl,
        VariableStore variables,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(request, baseUrl, variables);

        using var client = _handler is null
            ? new HttpClient()
            : new HttpClient(_handler, disposeHandler: false);
        client.Timeout = timeout;

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), url);
        foreach (var (name, value) in request.Headers)
            message.Headers.TryAddWithoutValidation(name, variables.Substitute(value));
        if (request.Body is not null)
        {
            var body = variables.Substitute(request.Body);
            var contentType = request.Headers.TryGetValue("Content-Type", out var ct)
                ? variables.Substitute(ct)
                : GuessContentType(body);
            message.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new RequestResult
            {
                RequestName = request.Name,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = $"Request timed out after {timeout.TotalSeconds:g0}s.",
            };
        }
        catch (HttpRequestException ex)
        {
            return new RequestResult
            {
                RequestName = request.Name,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
        catch (Exception ex)
        {
            return new RequestResult
            {
                RequestName = request.Name,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }

        using (response)
        {
            var elapsed = stopwatch.ElapsedMilliseconds;
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
                headers[header.Key] = string.Join(", ", header.Value);
            foreach (var header in response.Content.Headers)
                headers[header.Key] = string.Join(", ", header.Value);

            var captures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            ApplyCaptures(request, response, headers, bodyText, captures, warnings, variables);

            return new RequestResult
            {
                RequestName = request.Name,
                StatusCode = (int)response.StatusCode,
                ElapsedMs = elapsed,
                ResponseHeaders = headers,
                ResponseBody = bodyText,
                Captures = captures,
                CaptureWarnings = warnings,
            };
        }
    }

    /// <summary>Builds the request URL, substituting variables and failing fast on unknown ones.</summary>
    public string BuildUrl(ApiRequest request, string baseUrl, VariableStore variables)
    {
        var path = variables.Substitute(request.Path);
        var missing = variables.MissingVariables(request.Path);
        foreach (var value in request.Query.Values)
            missing.AddRange(variables.MissingVariables(value));
        if (missing.Count > 0)
            throw new RunException(
                $"Request '{request.Name}' uses unknown variable(s): {string.Join(", ", missing.Distinct())}. " +
                "Provide them with --var or an environment.");

        string url;
        // NOTE: Uri.TryCreate("/users/1", Absolute) succeeds as a file:// URI,
        // so a root-relative path must NOT be treated as an absolute URL.
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
            && absoluteUri.Scheme is "http" or "https")
        {
            url = path;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new RunException($"Request '{request.Name}' has a relative path but the collection has no BaseUrl.");
            url = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        if (request.Query.Count > 0)
        {
            var query = string.Join("&", request.Query.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(variables.Substitute(kv.Value))}"));
            url += (url.Contains('?') ? "&" : "?") + query;
        }
        return url;
    }

    private static void ApplyCaptures(
        ApiRequest request,
        HttpResponseMessage response,
        Dictionary<string, string> headers,
        string bodyText,
        Dictionary<string, string> captures,
        List<string> warnings,
        VariableStore variables)
    {
        if (request.Captures.Count == 0)
            return;

        JsonDocument? document = null;
        foreach (var capture in request.Captures)
        {
            if (capture.From.Equals("header", StringComparison.OrdinalIgnoreCase))
            {
                if (headers.TryGetValue(capture.Path, out var headerValue))
                {
                    captures[capture.Name] = headerValue;
                    variables.Set(capture.Name, headerValue);
                }
                else
                {
                    warnings.Add($"Capture '{capture.Name}': response has no header '{capture.Path}'.");
                }
                continue;
            }

            document ??= TryParseJson(bodyText, request.Name, warnings);
            if (document is null)
                continue;

            JsonElement? match;
            try
            {
                match = JsonPath.EvaluateSingle(document.RootElement, capture.Path);
            }
            catch (JsonPathException ex)
            {
                warnings.Add($"Capture '{capture.Name}': {ex.Message}");
                continue;
            }

            if (match is null)
            {
                warnings.Add($"Capture '{capture.Name}': path '{capture.Path}' matched nothing.");
                continue;
            }

            var value = JsonPath.ToDisplayString(match.Value);
            captures[capture.Name] = value;
            variables.Set(capture.Name, value);
        }
        document?.Dispose();
    }

    private static JsonDocument? TryParseJson(string bodyText, string requestName, List<string> warnings)
    {
        try
        {
            return JsonDocument.Parse(bodyText);
        }
        catch (JsonException)
        {
            warnings.Add($"Request '{requestName}': response body is not JSON, so JSON captures were skipped.");
            return null;
        }
    }

    private static string GuessContentType(string body)
    {
        var trimmed = body.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[')
            ? "application/json"
            : "text/plain";
    }
}

public sealed class RunException : Exception
{
    public RunException(string message) : base(message) { }
}
