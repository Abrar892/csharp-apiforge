using ApiForge;

namespace ApiForge.Tests;

public class VariableSubstitutionTests
{
    [Fact]
    public void SubstitutesSingleVariable()
    {
        var store = new VariableStore(new Dictionary<string, string> { ["userId"] = "42" });
        Assert.Equal("/users/42", store.Substitute("/users/{{userId}}"));
    }

    [Fact]
    public void SubstitutesRepeatedAndMultipleVariables()
    {
        var store = new VariableStore(new Dictionary<string, string>
        {
            ["host"] = "api.example.com",
            ["id"] = "7",
        });
        Assert.Equal("https://api.example.com/items/7?again=7",
            store.Substitute("https://{{host}}/items/{{id}}?again={{id}}"));
    }

    [Fact]
    public void UnknownPlaceholderIsLeftInPlaceAndReported()
    {
        var store = new VariableStore();
        Assert.Equal("/users/{{userId}}", store.Substitute("/users/{{userId}}"));
        Assert.Equal(new[] { "userId" }, store.MissingVariables("/users/{{userId}}"));
        Assert.False(store.IsFullyResolved("/users/{{userId}}"));
    }

    [Fact]
    public void MissingVariablesDeduplicates()
    {
        var store = new VariableStore();
        Assert.Equal(new[] { "token" }, store.MissingVariables("{{token}} and {{token}}"));
    }

    [Fact]
    public void VariableNamesAreCaseInsensitive()
    {
        var store = new VariableStore(new Dictionary<string, string> { ["Token"] = "abc" });
        Assert.Equal("abc", store.Substitute("{{token}}"));
        Assert.Equal("abc", store.Substitute("{{TOKEN}}"));
        Assert.True(store.TryGet("tOkEn", out var value));
        Assert.Equal("abc", value);
    }

    [Fact]
    public void SetOverridesPreviousValue()
    {
        var store = new VariableStore(new Dictionary<string, string> { ["env"] = "dev" });
        store.Set("env", "prod");
        Assert.Equal("prod", store.Substitute("{{env}}"));
    }

    [Fact]
    public void WhitespaceInsideBracesIsIgnored()
    {
        var store = new VariableStore(new Dictionary<string, string> { ["id"] = "9" });
        Assert.Equal("9", store.Substitute("{{ id }}"));
    }
}
