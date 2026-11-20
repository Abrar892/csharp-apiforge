using ApiForge;

namespace ApiForge.Tests;

public class AssertionEngineTests
{
    private static RequestResult Response(
        int status = 200,
        string body = """{"id": 1, "name": "Leanne"}""",
        Dictionary<string, string>? headers = null,
        long elapsedMs = 120,
        string? error = null) => new()
        {
            RequestName = "test",
            StatusCode = error is null ? status : null,
            ElapsedMs = elapsedMs,
            Error = error,
            ResponseBody = body,
            ResponseHeaders = headers ?? new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json; charset=utf-8",
            },
        };

    private static ApiRequest Request(params Assertion[] assertions) => new()
    {
        Name = "test",
        Method = "GET",
        Path = "/",
        Assertions = assertions.ToList(),
    };

    private static AssertionResult Only(ApiRequest request, RequestResult result)
    {
        var outcomes = AssertionEngine.Evaluate(request, result);
        Assert.Single(outcomes);
        return outcomes[0];
    }

    [Fact]
    public void StatusPassesOnMatch()
    {
        var outcome = Only(Request(new Assertion { Type = "status", Expected = "200" }), Response());
        Assert.True(outcome.Passed);
        Assert.Equal("200", outcome.Actual);
    }

    [Fact]
    public void StatusFailsOnMismatchWithMessage()
    {
        var outcome = Only(Request(new Assertion { Type = "status", Expected = "201" }), Response());
        Assert.False(outcome.Passed);
        Assert.Equal("200", outcome.Actual);
        Assert.Contains("201", outcome.Message);
    }

    [Fact]
    public void StatusFailsWithoutResponse()
    {
        var outcome = Only(Request(new Assertion { Type = "status", Expected = "200" }),
            Response(error: "Connection refused"));
        Assert.False(outcome.Passed);
        Assert.Contains("no response", outcome.Message);
    }

    [Fact]
    public void JsonPathComparesNumbersAsText()
    {
        var outcome = Only(
            Request(new Assertion { Type = "jsonpath", Path = "$.id", Expected = "1" }), Response());
        Assert.True(outcome.Passed);
    }

    [Fact]
    public void JsonPathFailsOnMismatchAndMissingPath()
    {
        var wrong = Only(
            Request(new Assertion { Type = "jsonpath", Path = "$.name", Expected = "Bob" }), Response());
        Assert.False(wrong.Passed);
        Assert.Equal("Leanne", wrong.Actual);

        var missing = Only(
            Request(new Assertion { Type = "jsonpath", Path = "$.nope", Expected = "x" }), Response());
        Assert.False(missing.Passed);
        Assert.Contains("matched nothing", missing.Message);
    }

    [Fact]
    public void JsonPathFailsOnNonJsonBody()
    {
        var outcome = Only(
            Request(new Assertion { Type = "jsonpath", Path = "$.id", Expected = "1" }),
            Response(body: "<html>not json</html>"));
        Assert.False(outcome.Passed);
        Assert.Contains("not JSON", outcome.Message);
    }

    [Fact]
    public void HeaderMatchesByContainmentCaseInsensitive()
    {
        var outcome = Only(
            Request(new Assertion { Type = "header", Path = "Content-Type", Expected = "application/json" }),
            Response());
        Assert.True(outcome.Passed);

        var missing = Only(
            Request(new Assertion { Type = "header", Path = "X-Trace", Expected = "1" }), Response());
        Assert.False(missing.Passed);
    }

    [Fact]
    public void BodyContainsAndRegex()
    {
        var contains = Only(
            Request(new Assertion { Type = "body-contains", Value = "Leanne" }), Response());
        Assert.True(contains.Passed);

        var notContains = Only(
            Request(new Assertion { Type = "body-contains", Value = "Bob" }), Response());
        Assert.False(notContains.Passed);

        var regex = Only(
            Request(new Assertion { Type = "body-regex", Value = @"""id"":\s*1" }), Response());
        Assert.True(regex.Passed);
    }

    [Fact]
    public void BodyRegexFailsGracefullyOnInvalidPattern()
    {
        var outcome = Only(
            Request(new Assertion { Type = "body-regex", Value = "([invalid" }), Response());
        Assert.False(outcome.Passed);
        Assert.Contains("invalid regex", outcome.Message);
    }

    [Fact]
    public void LatencyChecksElapsedTime()
    {
        var fast = Only(
            Request(new Assertion { Type = "latency-ms", MaxMs = 200 }), Response(elapsedMs: 120));
        Assert.True(fast.Passed);

        var slow = Only(
            Request(new Assertion { Type = "latency-ms", MaxMs = 100 }), Response(elapsedMs: 120));
        Assert.False(slow.Passed);
        Assert.Equal("120ms", slow.Actual);
    }

    [Fact]
    public void NonStatusAssertionsFailWithoutResponse()
    {
        var outcome = Only(
            Request(new Assertion { Type = "body-contains", Value = "x" }),
            Response(error: "DNS failure"));
        Assert.False(outcome.Passed);
        Assert.Contains("no response", outcome.Message);
    }
}
