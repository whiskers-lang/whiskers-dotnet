using Whiskers;

namespace Whiskers.Tests;

public class DictionaryPartialLoaderTests
{
    [Fact]
    public void RendersNamedAndNestedPartials()
    {
        var loader = new DictionaryPartialLoader(new Dictionary<string, string>
        {
            ["greeting"] = "Hello {{name}}!",
            ["outer"] = "[{{> greeting}}]",
        });

        Assert.Equal("Hello World!", Renderer.Render("{{> greeting}}", new { name = "World" }, loader));
        Assert.Equal("[Hello World!]", Renderer.Render("{{> outer}}", new { name = "World" }, loader));
        Assert.Equal("Hello {{name}}!", loader.Load("greeting"));
    }

    [Fact]
    public void PreservesMissingAndEmptyPartials()
    {
        var loader = new DictionaryPartialLoader(new Dictionary<string, string> { ["empty"] = "" });

        Assert.Null(loader.Load("missing"));
        Assert.Equal("", loader.Load("empty"));
        Assert.Equal("ABC", Renderer.Render("A{{> missing}}B{{> empty}}C", null, loader));
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "found")]
    public void PreservesDictionaryComparer(bool ignoreCase, string expected)
    {
        var templates = new Dictionary<string, string>(
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            ["greeting"] = "found",
        };
        var loader = new DictionaryPartialLoader(templates);

        Assert.Equal(expected, Renderer.Render("{{> GREETING}}", null, loader));
    }

    [Fact]
    public void ObservesUpdatesAdditionsAndRemovalsBetweenRenders()
    {
        var templates = new Dictionary<string, string> { ["greeting"] = "before" };
        var loader = new DictionaryPartialLoader(templates);

        Assert.Equal("before", Renderer.Render("{{> greeting}}", null, loader));
        templates["greeting"] = "after";
        templates["new"] = "added";
        Assert.Equal("afteradded", Renderer.Render("{{> greeting}}{{> new}}", null, loader));
        templates.Remove("greeting");
        Assert.Equal("", Renderer.Render("{{> greeting}}", null, loader));
    }

    [Fact]
    public void RejectsNullDictionary()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new DictionaryPartialLoader(null!));
        Assert.Equal("templates", exception.ParamName);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void PreservesStandalonePartialIndentation(string newline)
    {
        var loader = new DictionaryPartialLoader(new Dictionary<string, string>
        {
            ["lines"] = $"first{newline}{{{{name}}}}{newline}",
        });

        Assert.Equal($"before{newline}  first{newline}  second{newline}after",
            Renderer.Render($"before{newline}  {{{{> lines}}}}{newline}after",
                new { name = "second" }, loader));
    }

    [Fact]
    public void PreservesRecursionLimit()
    {
        var loader = new DictionaryPartialLoader(new Dictionary<string, string> { ["self"] = "{{> self}}" });
        var renderer = new Renderer(loader, new RenderOptions(MaxRecursionDepth: 5));
        const string template = "{{> self}}";
        var ast = new Parser(new Lexer(template).Tokenize(), template).Parse();

        var exception = Assert.Throws<RenderException>(() => renderer.Render(ast, null));
        Assert.Contains("Max partial recursion depth (5) exceeded", exception.Message);
    }
}
