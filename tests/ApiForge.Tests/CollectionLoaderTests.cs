using ApiForge;

namespace ApiForge.Tests;

public class CollectionLoaderTests
{
    private static string TempFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"apiforge-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void ExampleCollectionRoundTrips()
    {
        var path = TempFile("");
        try
        {
            var original = CollectionLoader.Example("Roundtrip API");
            CollectionLoader.Save(original, path);
            var loaded = CollectionLoader.Load(path);
            Assert.Equal("Roundtrip API", loaded.Name);
            Assert.Equal(2, loaded.Requests.Count);
            Assert.Equal("Get user", loaded.Requests[0].Name);
            Assert.True(loaded.Environments.ContainsKey("staging"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidJsonThrowsDescriptiveError()
    {
        var path = TempFile("{ not json");
        try
        {
            var ex = Assert.Throws<CollectionLoadException>(() => CollectionLoader.Load(path));
            Assert.Contains("not valid JSON", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileThrowsDescriptiveError()
    {
        var ex = Assert.Throws<CollectionLoadException>(() =>
            CollectionLoader.Load(Path.Combine(Path.GetTempPath(), "apiforge-does-not-exist.json")));
        Assert.Contains("Cannot read", ex.Message);
    }

    [Fact]
    public void ValidationErrorsAreReportedTogether()
    {
        var path = TempFile("""
            {
              "apiforge": 2,
              "name": "Bad",
              "baseUrl": "not-a-url",
              "requests": [
                { "name": "Dup", "method": "FETCH", "path": "/" },
                { "name": "Dup", "method": "GET", "path": "/",
                  "assertions": [{ "type": "wat", "expected": "1" }] }
              ]
            }
            """);
        try
        {
            var ex = Assert.Throws<CollectionLoadException>(() => CollectionLoader.Load(path));
            Assert.Contains("version 2", ex.Message);
            Assert.Contains("not an absolute URL", ex.Message);
            Assert.Contains("unsupported method", ex.Message);
            Assert.Contains("Duplicate request name", ex.Message);
            Assert.Contains("unknown assertion type", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptyCollectionIsRejected()
    {
        var path = TempFile("""
            { "apiforge": 1, "name": "Empty", "baseUrl": "https://api.example.com", "requests": [] }
            """);
        try
        {
            var ex = Assert.Throws<CollectionLoadException>(() => CollectionLoader.Load(path));
            Assert.Contains("no requests", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
