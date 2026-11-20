using System.Text;
using ApiForge;

namespace ApiForge.Tests;

/// <summary>Returns canned responses and records the requests it receives.</summary>
internal sealed class MockHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public List<HttpRequestMessage> SeenRequests { get; } = new();

    /// <summary>Bodies of requests seen, read while the message was alive.</summary>
    public List<string?> SeenBodies { get; } = new();

    public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public MockHandler(string body, int status = 200, string contentType = "application/json")
        : this(_ => new HttpResponseMessage((System.Net.HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        })
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SeenRequests.Add(request);
        SeenBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));
        return _respond(request);
    }
}

public class HttpRunnerTests
{
    private static ApiRequest Get(string path = "/users/{{userId}}") => new()
    {
        Name = "Get user",
        Method = "GET",
        Path = path,
    };

    [Fact]
    public async Task CombinesBaseUrlWithRelativePathAndSubstitutesVariables()
    {
        var handler = new MockHandler("""{"id": 1}""");
        var runner = new HttpRunner(handler);
        var variables = new VariableStore(new Dictionary<string, string> { ["userId"] = "42" });

        var result = await runner.RunAsync(Get(), "https://api.example.com", variables, TimeSpan.FromSeconds(5));

        Assert.Equal(200, result.StatusCode);
        Assert.Single(handler.SeenRequests);
        Assert.Equal("https://api.example.com/users/42",
            handler.SeenRequests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task AbsolutePathIsUsedAsIs()
    {
        var handler = new MockHandler("""{"ok": true}""");
        var runner = new HttpRunner(handler);

        var result = await runner.RunAsync(
            Get("https://other.example.com/health"), "https://api.example.com",
            new VariableStore(), TimeSpan.FromSeconds(5));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("https://other.example.com/health",
            handler.SeenRequests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task QueryParametersAreSubstitutedAndEncoded()
    {
        var handler = new MockHandler("""[]""");
        var runner = new HttpRunner(handler);
        var request = Get("/search");
        request.Query["q"] = "hello world";
        request.Query["tag"] = "{{tag}}";
        var variables = new VariableStore(new Dictionary<string, string> { ["tag"] = "a&b" });

        await runner.RunAsync(request, "https://api.example.com", variables, TimeSpan.FromSeconds(5));

        Assert.Equal("https://api.example.com/search?q=hello%20world&tag=a%26b",
            handler.SeenRequests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task HeadersAreSubstitutedAndBodySentAsJson()
    {
        var handler = new MockHandler("""{"created": true}""", status: 201);
        var runner = new HttpRunner(handler);
        var request = new ApiRequest
        {
            Name = "Create",
            Method = "POST",
            Path = "/items",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer {{token}}",
                ["Content-Type"] = "application/json",
            },
            Body = """{"name": "{{name}}"}""",
        };
        var variables = new VariableStore(new Dictionary<string, string>
        {
            ["token"] = "secret",
            ["name"] = "Widget",
        });

        var result = await runner.RunAsync(request, "https://api.example.com", variables, TimeSpan.FromSeconds(5));

        Assert.Equal(201, result.StatusCode);
        var sent = handler.SeenRequests[0];
        Assert.Equal("Bearer secret", sent.Headers.GetValues("Authorization").Single());
        Assert.Equal("""{"name": "Widget"}""", handler.SeenBodies[0]);
        Assert.Equal("application/json", sent.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task JsonCaptureFeedsLaterVariables()
    {
        var handler = new MockHandler("""{"id": 99, "name": "Leanne"}""");
        var runner = new HttpRunner(handler);
        var request = Get();
        request.Captures.Add(new Capture { Name = "userName", From = "json", Path = "$.name" });
        request.Captures.Add(new Capture { Name = "userIdOut", From = "json", Path = "$.id" });
        var variables = new VariableStore(new Dictionary<string, string> { ["userId"] = "1" });

        var result = await runner.RunAsync(request, "https://api.example.com", variables, TimeSpan.FromSeconds(5));

        Assert.Equal("Leanne", result.Captures["userName"]);
        Assert.Equal("99", result.Captures["userIdOut"]);
        Assert.True(variables.TryGet("userName", out var stored));
        Assert.Equal("Leanne", stored);
        Assert.Empty(result.CaptureWarnings);
    }

    [Fact]
    public async Task HeaderCaptureAndMissingCaptureWarning()
    {
        var handler = new MockHandler(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"a": 1}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("X-Request-Id", "req-123");
            return response;
        });
        var runner = new HttpRunner(handler);
        var request = Get();
        request.Captures.Add(new Capture { Name = "trace", From = "header", Path = "X-Request-Id" });
        request.Captures.Add(new Capture { Name = "missing", From = "json", Path = "$.nope" });

        var result = await runner.RunAsync(request, "https://api.example.com",
            new VariableStore(new Dictionary<string, string> { ["userId"] = "1" }), TimeSpan.FromSeconds(5));

        Assert.Equal("req-123", result.Captures["trace"]);
        Assert.Single(result.CaptureWarnings);
        Assert.Contains("missing", result.CaptureWarnings[0]);
    }

    [Fact]
    public async Task MissingVariableThrowsBeforeSending()
    {
        var handler = new MockHandler("""{}""");
        var runner = new HttpRunner(handler);

        var ex = await Assert.ThrowsAsync<RunException>(() =>
            runner.RunAsync(Get(), "https://api.example.com", new VariableStore(), TimeSpan.FromSeconds(5)));

        Assert.Contains("userId", ex.Message);
        Assert.Empty(handler.SeenRequests);
    }

    [Fact]
    public async Task TransportErrorBecomesResultError()
    {
        var handler = new MockHandler(_ => throw new HttpRequestException("Connection refused"));
        var runner = new HttpRunner(handler);

        var result = await runner.RunAsync(Get("/ping"), "https://api.example.com",
            new VariableStore(), TimeSpan.FromSeconds(5));

        Assert.Null(result.StatusCode);
        Assert.Contains("Connection refused", result.Error);
        Assert.False(result.Passed);
    }

    [Fact]
    public void BuildUrlRejectsRootRelativePathAsAbsolute()
    {
        // Regression test: Uri.TryCreate("/x", Absolute) succeeds as file://,
        // so a root-relative path must still be combined with the BaseUrl.
        var runner = new HttpRunner();
        var request = Get("/users/1");
        var url = runner.BuildUrl(request, "https://api.example.com", new VariableStore());
        Assert.Equal("https://api.example.com/users/1", url);
    }
}
