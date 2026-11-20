using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiForge;

/// <summary>Human-readable summary line and machine-readable JSON report.</summary>
public static class Reporter
{
    public static string SummaryLine(RunReport report) =>
        report.AllPassed
            ? $"All {report.Results.Count} request(s) passed in {report.DurationMs}ms."
            : $"{report.FailedCount} of {report.Results.Count} request(s) failed in {report.DurationMs}ms.";

    public static void WriteJson(RunReport report, string path)
    {
        var payload = new
        {
            collection = report.CollectionName,
            environment = report.Environment,
            startedAt = report.StartedAt,
            durationMs = report.DurationMs,
            passed = report.PassedCount,
            failed = report.FailedCount,
            allPassed = report.AllPassed,
            results = report.Results.Select(r => new
            {
                request = r.RequestName,
                status = r.StatusCode,
                elapsedMs = r.ElapsedMs,
                error = r.Error,
                passed = r.Passed,
                captures = r.Captures,
                captureWarnings = r.CaptureWarnings,
                assertions = r.Assertions.Select(a => new
                {
                    description = a.Description,
                    passed = a.Passed,
                    actual = a.Actual,
                    message = a.Message,
                }),
            }),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(path, json + "\n");
    }
}
