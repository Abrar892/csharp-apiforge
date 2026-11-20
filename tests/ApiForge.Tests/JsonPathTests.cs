using System.Text.Json;
using ApiForge;

namespace ApiForge.Tests;

public class JsonPathTests
{
    private static JsonElement Doc(string json)
    {
        var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void SimpleProperty()
    {
        var root = Doc("""{"name": "Leanne"}""");
        var match = JsonPath.EvaluateSingle(root, "$.name");
        Assert.NotNull(match);
        Assert.Equal("Leanne", JsonPath.ToDisplayString(match.Value));
    }

    [Fact]
    public void NestedPropertyAndIndex()
    {
        var root = Doc("""{"store": {"book": [{"title": "Dune"}, {"title": "Foundation"}]}}""");
        Assert.Equal("Foundation",
            JsonPath.ToDisplayString(JsonPath.EvaluateSingle(root, "$.store.book[1].title")!.Value));
        Assert.Equal("Dune",
            JsonPath.ToDisplayString(JsonPath.EvaluateSingle(root, "$.store.book[0].title")!.Value));
    }

    [Fact]
    public void NegativeIndexCountsFromEnd()
    {
        var root = Doc("""{"items": [10, 20, 30]}""");
        Assert.Equal("30",
            JsonPath.ToDisplayString(JsonPath.EvaluateSingle(root, "$.items[-1]")!.Value));
    }

    [Fact]
    public void WildcardReturnsEveryElement()
    {
        var root = Doc("""{"items": [{"id": 1}, {"id": 2}, {"id": 3}]}""");
        var matches = JsonPath.Evaluate(root, "$.items[*].id");
        Assert.Equal(new[] { "1", "2", "3" }, matches.Select(JsonPath.ToDisplayString));
    }

    [Fact]
    public void RecursiveDescentFindsNestedProperty()
    {
        var root = Doc("""{"a": {"price": 5}, "b": [{"price": 9}, {"other": 1}]}""");
        var matches = JsonPath.Evaluate(root, "$..price");
        Assert.Equal(new[] { "5", "9" }, matches.Select(JsonPath.ToDisplayString));
    }

    [Fact]
    public void BracketQuotedKeyWithSpaces()
    {
        var root = Doc("""{"weird key": "value"}""");
        Assert.Equal("value",
            JsonPath.ToDisplayString(JsonPath.EvaluateSingle(root, "$['weird key']")!.Value));
    }

    [Fact]
    public void MissingPathMatchesNothing()
    {
        var root = Doc("""{"a": 1}""");
        Assert.Empty(JsonPath.Evaluate(root, "$.nope"));
        Assert.Null(JsonPath.EvaluateSingle(root, "$.a.b.c"));
        Assert.False(JsonPath.Exists(root, "$.nope"));
        Assert.True(JsonPath.Exists(root, "$.a"));
    }

    [Fact]
    public void InvalidPathThrows()
    {
        var root = Doc("""{"a": 1}""");
        Assert.Throws<JsonPathException>(() => JsonPath.Evaluate(root, "a.b"));
        Assert.Throws<JsonPathException>(() => JsonPath.Evaluate(root, "$.a["));
        Assert.Throws<JsonPathException>(() => JsonPath.Evaluate(root, "$.a[foo]"));
    }

    [Fact]
    public void ToDisplayStringRendersKinds()
    {
        Assert.Equal("text", JsonPath.ToDisplayString(Doc(@"""text""")));
        Assert.Equal("42", JsonPath.ToDisplayString(Doc("42")));
        Assert.Equal("true", JsonPath.ToDisplayString(Doc("true")));
        Assert.Equal("null", JsonPath.ToDisplayString(Doc("null")));
        Assert.Equal("""{"a":1}""", JsonPath.ToDisplayString(Doc("""{"a":1}""")));
    }
}
