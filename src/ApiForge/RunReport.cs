namespace ApiForge;

/// <summary>Outcome of a single assertion check.</summary>
public sealed class AssertionResult
{
    public required string Description { get; init; }
    public required bool Passed { get; init; }
    public string? Actual { get; init; }
    public string? Message { get; init; }
}

/// <summary>Everything observed while running one request.</summary>
public sealed class RequestResult
{
    public required string RequestName { get; init; }
    public int? StatusCode { get; init; }
    public long ElapsedMs { get; init; }

    /// <summary>Transport-level failure (DNS, connection refused, timeout). Null on HTTP responses.</summary>
    public string? Error { get; init; }

    public Dictionary<string, string> ResponseHeaders { get; init; } = new();
    public string ResponseBody { get; init; } = "";
    public Dictionary<string, string> Captures { get; init; } = new();
    public List<string> CaptureWarnings { get; init; } = new();
    public List<AssertionResult> Assertions { get; init; } = new();

    /// <summary>A request passes when it got an HTTP response and every assertion passed.</summary>
    public bool Passed => Error is null && Assertions.All(a => a.Passed);
}

/// <summary>Outcome of a whole collection run.</summary>
public sealed class RunReport
{
    public required string CollectionName { get; init; }
    public string? Environment { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public long DurationMs { get; init; }
    public required List<RequestResult> Results { get; init; }

    public int PassedCount => Results.Count(r => r.Passed);
    public int FailedCount => Results.Count - PassedCount;
    public bool AllPassed => FailedCount == 0;
}
