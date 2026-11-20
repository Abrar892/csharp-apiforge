using System.Diagnostics;

namespace ApiForge;

/// <summary>
/// Implements `apiforge run`: loads the collection, resolves the environment
/// and --var overrides, executes requests in order and prints a summary.
/// </summary>
public static class Runner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: apiforge run <collection.json> [--env <name>] [--var k=v] [--json <file>] [--timeout <seconds>]");
            return 2;
        }

        var collectionPath = args[0];
        string? environment = null;
        string? jsonReportPath = null;
        var timeoutSeconds = 30;
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--env" when i + 1 < args.Length:
                    environment = args[++i];
                    break;
                case "--json" when i + 1 < args.Length:
                    jsonReportPath = args[++i];
                    break;
                case "--timeout" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        Console.Error.WriteLine("--timeout needs a positive number of seconds.");
                        return 2;
                    }
                    break;
                case "--var" when i + 1 < args.Length:
                {
                    var pair = args[++i];
                    var equals = pair.IndexOf('=');
                    if (equals <= 0)
                    {
                        Console.Error.WriteLine($"--var expects name=value, got '{pair}'.");
                        return 2;
                    }
                    overrides[pair[..equals]] = pair[(equals + 1)..];
                    break;
                }
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}' for run.");
                    return 2;
            }
        }

        var collection = CollectionLoader.Load(collectionPath);
        var variables = BuildVariables(collection, environment, overrides);

        return RunAsync(collection, environment, variables, TimeSpan.FromSeconds(timeoutSeconds), jsonReportPath)
            .GetAwaiter().GetResult();
    }

    private static VariableStore BuildVariables(
        ApiCollection collection,
        string? environment,
        Dictionary<string, string> overrides)
    {
        var variables = new VariableStore();
        if (environment is not null)
        {
            if (!collection.Environments.TryGetValue(environment, out var envValues))
                throw new RunException(
                    $"Environment '{environment}' is not defined in the collection. " +
                    $"Available: {string.Join(", ", collection.Environments.Keys)}.");
            foreach (var (name, value) in envValues)
                variables.Set(name, value);
        }
        foreach (var (name, value) in overrides)
            variables.Set(name, value);
        return variables;
    }

    private static async Task<int> RunAsync(
        ApiCollection collection,
        string? environment,
        VariableStore variables,
        TimeSpan timeout,
        string? jsonReportPath)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var runner = new HttpRunner();
        var results = new List<RequestResult>();

        Console.WriteLine($"Running '{collection.Name}' — {collection.Requests.Count} request(s)" +
                          (environment is null ? "" : $" (env: {environment})"));
        Console.WriteLine();

        foreach (var request in collection.Requests)
        {
            RequestResult result;
            try
            {
                result = await runner.RunAsync(request, collection.BaseUrl, variables, timeout);
            }
            catch (RunException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 2;
            }

            result.Assertions.AddRange(AssertionEngine.Evaluate(request, result));
            results.Add(result);
            PrintRequest(result);
        }

        var report = new RunReport
        {
            CollectionName = collection.Name,
            Environment = environment,
            StartedAt = startedAt,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Results = results,
        };

        Console.WriteLine();
        Console.WriteLine(Reporter.SummaryLine(report));
        if (jsonReportPath is not null)
        {
            Reporter.WriteJson(report, jsonReportPath);
            Console.WriteLine($"JSON report written to {jsonReportPath}");
        }

        return report.AllPassed ? 0 : 1;
    }

    private static void PrintRequest(RequestResult result)
    {
        var status = result.Error is null
            ? $"{result.StatusCode} in {result.ElapsedMs}ms"
            : $"ERROR: {result.Error}";
        var verdict = result.Passed ? "PASS" : "FAIL";
        Console.WriteLine($"[{verdict}] {result.RequestName} — {status}");
        foreach (var warning in result.CaptureWarnings)
            Console.WriteLine($"        warning: {warning}");
        foreach (var assertion in result.Assertions.Where(a => !a.Passed))
            Console.WriteLine($"        failed: {assertion.Description}" +
                              (assertion.Actual is null ? "" : $" (actual: {assertion.Actual})"));
    }
}
