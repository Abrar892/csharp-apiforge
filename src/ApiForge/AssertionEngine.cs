using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiForge;

/// <summary>
/// Checks a request's assertions against the response it got. Every check
/// produces an <see cref="AssertionResult"/> so reports can show exactly
/// what failed and what the response actually contained.
/// </summary>
public static class AssertionEngine
{
    public static List<AssertionResult> Evaluate(ApiRequest request, RequestResult result)
    {
        var outcomes = new List<AssertionResult>();
        foreach (var assertion in request.Assertions)
        {
            if (result.Error is not null && assertion.Type != "status")
            {
                outcomes.Add(Fail(assertion, actual: null,
                    $"no response received ({result.Error})"));
                continue;
            }
            outcomes.Add(assertion.Type switch
            {
                "status" => CheckStatus(assertion, result),
                "jsonpath" => CheckJsonPath(assertion, result),
                "header" => CheckHeader(assertion, result),
                "body-contains" => CheckBodyContains(assertion, result),
                "body-regex" => CheckBodyRegex(assertion, result),
                "latency-ms" => CheckLatency(assertion, result),
                _ => Fail(assertion, actual: null, $"unknown assertion type '{assertion.Type}'"),
            });
        }
        return outcomes;
    }

    private static AssertionResult CheckStatus(Assertion assertion, RequestResult result)
    {
        if (result.StatusCode is null)
            return Fail(assertion, actual: null, $"no response received ({result.Error})");
        var expected = int.Parse(assertion.Expected!);
        return result.StatusCode == expected
            ? Pass(assertion, actual: result.StatusCode.ToString())
            : Fail(assertion, actual: result.StatusCode.ToString(),
                $"expected status {expected}, got {result.StatusCode}");
    }

    private static AssertionResult CheckJsonPath(Assertion assertion, RequestResult result)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.ResponseBody);
        }
        catch (JsonException)
        {
            return Fail(assertion, actual: null, "response body is not JSON");
        }

        using (document)
        {
            JsonElement? match;
            try
            {
                match = JsonPath.EvaluateSingle(document.RootElement, assertion.Path!);
            }
            catch (JsonPathException ex)
            {
                return Fail(assertion, actual: null, ex.Message);
            }

            if (match is null)
                return Fail(assertion, actual: null, $"path '{assertion.Path}' matched nothing");

            var actual = JsonPath.ToDisplayString(match.Value);
            return string.Equals(actual, assertion.Expected, StringComparison.Ordinal)
                ? Pass(assertion, actual)
                : Fail(assertion, actual,
                    $"expected '{assertion.Expected}' at '{assertion.Path}', got '{actual}'");
        }
    }

    private static AssertionResult CheckHeader(Assertion assertion, RequestResult result)
    {
        if (!result.ResponseHeaders.TryGetValue(assertion.Path!, out var actual))
            return Fail(assertion, actual: null, $"response has no header '{assertion.Path}'");
        // A header assertion passes when the expected value appears in the header
        // (Content-Type: application/json; charset=utf-8 should match "application/json").
        return actual.Contains(assertion.Expected!, StringComparison.OrdinalIgnoreCase)
            ? Pass(assertion, actual)
            : Fail(assertion, actual,
                $"expected header '{assertion.Path}' to contain '{assertion.Expected}', got '{actual}'");
    }

    private static AssertionResult CheckBodyContains(Assertion assertion, RequestResult result)
    {
        var needle = assertion.Value ?? "";
        return result.ResponseBody.Contains(needle, StringComparison.Ordinal)
            ? Pass(assertion, actual: null)
            : Fail(assertion, actual: null, $"body does not contain \"{needle}\"");
    }

    private static AssertionResult CheckBodyRegex(Assertion assertion, RequestResult result)
    {
        bool matched;
        try
        {
            matched = Regex.IsMatch(result.ResponseBody, assertion.Value ?? "");
        }
        catch (RegexParseException ex)
        {
            return Fail(assertion, actual: null, $"invalid regex '{assertion.Value}': {ex.Message}");
        }
        return matched
            ? Pass(assertion, actual: null)
            : Fail(assertion, actual: null, $"body does not match /{assertion.Value}/");
    }

    private static AssertionResult CheckLatency(Assertion assertion, RequestResult result)
    {
        var max = assertion.MaxMs!.Value;
        return result.ElapsedMs <= max
            ? Pass(assertion, actual: $"{result.ElapsedMs}ms")
            : Fail(assertion, actual: $"{result.ElapsedMs}ms",
                $"latency {result.ElapsedMs}ms exceeded {max}ms");
    }

    private static AssertionResult Pass(Assertion assertion, string? actual) =>
        new() { Description = assertion.ToString(), Passed = true, Actual = actual };

    private static AssertionResult Fail(Assertion assertion, string? actual, string message) =>
        new() { Description = assertion.ToString(), Passed = false, Actual = actual, Message = message };
}
