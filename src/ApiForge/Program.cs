namespace ApiForge;

/// <summary>
/// Entry point. Exit codes: 0 = success, 1 = test failures, 2 = usage / run error.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "init" => Init(args.Skip(1).ToArray()),
                "validate" => Validate(args.Skip(1).ToArray()),
                "run" => Runner.Run(args.Skip(1).ToArray()),
                "--help" or "-h" or "help" => Help(),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (CollectionLoadException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int Help()
    {
        PrintUsage();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            apiforge — CLI API testing tool

            Usage:
              apiforge init [name] [--output <file>]
                  Write an example collection to <file> (default: apiforge.json).

              apiforge validate <collection.json>
                  Check a collection file for schema and validation errors.

              apiforge run <collection.json> [options]
                  Execute every request in order and report assertion results.
                  Options:
                    --env <name>        Use a named environment from the collection.
                    --var <name>=<value>  Override or add a variable (repeatable).
                    --json <file>       Also write a machine-readable JSON report.
                    --timeout <seconds> Per-request timeout (default 30).

            Exit codes: 0 = all assertions passed, 1 = assertion failures, 2 = usage/run error.
            """);
    }

    private static int Init(string[] args)
    {
        var name = "Example API";
        var output = "apiforge.json";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
                output = args[++i];
            else if (!args[i].StartsWith('-'))
                name = args[i];
            else
            {
                Console.Error.WriteLine($"Unknown option '{args[i]}' for init.");
                return 2;
            }
        }

        if (File.Exists(output))
        {
            Console.Error.WriteLine($"Refusing to overwrite existing file '{output}'.");
            return 2;
        }

        CollectionLoader.Save(CollectionLoader.Example(name), output);
        Console.WriteLine($"Wrote example collection '{name}' to {output}");
        Console.WriteLine("Edit it, then run: apiforge validate " + output);
        return 0;
    }

    private static int Validate(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: apiforge validate <collection.json>");
            return 2;
        }

        var collection = CollectionLoader.Load(args[0]);
        Console.WriteLine($"Valid: '{collection.Name}' — {collection.Requests.Count} request(s), " +
                          $"{collection.Environments.Count} environment(s).");
        return 0;
    }
}
