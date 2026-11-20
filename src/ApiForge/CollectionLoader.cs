using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiForge;

/// <summary>
/// Reads and writes collection files. The on-disk format is plain JSON so
/// collections are easy to hand-edit and to keep in version control.
/// </summary>
public static class CollectionLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Loads a collection, throwing a descriptive exception on bad input.</summary>
    public static ApiCollection Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            throw new CollectionLoadException($"Cannot read collection file '{path}': {ex.Message}", ex);
        }

        ApiCollection? collection;
        try
        {
            collection = JsonSerializer.Deserialize<ApiCollection>(text, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new CollectionLoadException($"Collection file '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (collection is null)
            throw new CollectionLoadException($"Collection file '{path}' is empty or contains JSON null.");

        var errors = collection.Validate();
        if (errors.Count > 0)
            throw new CollectionLoadException(
                $"Collection file '{path}' failed validation:\n  - {string.Join("\n  - ", errors)}");

        return collection;
    }

    public static void Save(ApiCollection collection, string path)
    {
        var json = JsonSerializer.Serialize(collection, WriteOptions);
        File.WriteAllText(path, json + "\n");
    }

    /// <summary>Builds a small example collection that doubles as documentation of the format.</summary>
    public static ApiCollection Example(string name = "Example API")
    {
        return new ApiCollection
        {
            ApiForge = 1,
            Name = name,
            BaseUrl = "https://jsonplaceholder.typicode.com",
            Environments = new Dictionary<string, Dictionary<string, string>>
            {
                ["staging"] = new() { ["userId"] = "1" },
            },
            Requests = new List<ApiRequest>
            {
                new()
                {
                    Name = "Get user",
                    Method = "GET",
                    Path = "/users/{{userId}}",
                    Headers = new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json",
                    },
                    Captures = new List<Capture>
                    {
                        new() { Name = "userName", From = "json", Path = "$.name" },
                    },
                    Assertions = new List<Assertion>
                    {
                        new() { Type = "status", Expected = "200" },
                        new() { Type = "jsonpath", Path = "$.id", Expected = "1" },
                        new() { Type = "header", Path = "Content-Type", Expected = "application/json" },
                        new() { Type = "latency-ms", MaxMs = 5000 },
                    },
                },
                new()
                {
                    Name = "List user posts",
                    Method = "GET",
                    Path = "/posts",
                    Query = new Dictionary<string, string> { ["userId"] = "{{userId}}" },
                    Assertions = new List<Assertion>
                    {
                        new() { Type = "status", Expected = "200" },
                        new() { Type = "body-contains", Value = "{{userName}}" },
                    },
                },
            },
        };
    }
}

public sealed class CollectionLoadException : Exception
{
    public CollectionLoadException(string message) : base(message) { }
    public CollectionLoadException(string message, Exception inner) : base(message, inner) { }
}
