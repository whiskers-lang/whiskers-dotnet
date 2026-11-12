using Whiskers;

namespace Whiskers.Tests;

public class WhitespaceTests
{
    public static IEnumerable<object[]> StandaloneCases()
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        foreach (var indent in new[] { "", "  ", "\t" })
        foreach (var precedingTag in new[] { "comment", "delimiter", "partial", "section" })
        foreach (var count in new[] { 0, 1, 2 })
            yield return [newline, indent, precedingTag, count];
    }

    public static IEnumerable<object[]> LambdaCases()
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        foreach (var indent in new[] { "", "  ", "\t" })
        foreach (var signature in new[]
        {
            "string", "object", "context", "callback",
            "args-string", "args-object", "args-callback",
        })
            yield return [newline, indent, signature];
    }

    private static object IdentityLambda(string signature) => signature switch
    {
        "string" => (Func<string, string>)(raw => raw),
        "object" => (Func<string, object>)(raw => raw),
        "context" => (Func<object?, string, object>)((_, raw) => raw),
        "callback" => (Func<string, Func<string, string>, object>)((raw, render) => render(raw)),
        "args-string" => (Func<object?[], string, string>)((_, raw) => raw),
        "args-object" => (Func<object?[], string, object>)((_, raw) => raw),
        "args-callback" => (Func<object?[], string, Func<string, string>, object>)((_, raw, render) => render(raw)),
        _ => throw new ArgumentOutOfRangeException(nameof(signature)),
    };

    private static string LambdaTag(string signature) =>
        signature.StartsWith("args-") ? "{{#lambda arg}}" : "{{#lambda}}";

    [Theory, MemberData(nameof(StandaloneCases))]
    public void ConsecutiveStandaloneTags_PreserveSectionLineBoundaries(
        string newline, string indent, string precedingTag, int count)
    {
        var preceding = precedingTag switch
        {
            "comment" => "{{! comment}}",
            "delimiter" => "{{=<% %>=}}",
            "partial" => "{{> spacer}}",
            "section" => $"{{{{#enabled}}}}{newline}{indent}{{{{/enabled}}}}",
            _ => throw new ArgumentOutOfRangeException(nameof(precedingTag)),
        };
        var open = precedingTag == "delimiter" ? "<%" : "{{";
        var close = precedingTag == "delimiter" ? "%>" : "}}";
        var template = $"Before{newline}{indent}{preceding}{newline}"
            + $"{indent}{open}#items{close}{newline}"
            + $"{indent}{open}.{close}{newline}"
            + $"{indent}{open}/items{close}{newline}After";
        var items = new[] { "one", "two" }.Take(count).ToArray();
        var expected = $"Before{newline}"
            + string.Concat(items.Select(item => $"{indent}{item}{newline}"))
            + "After";
        var partials = new TestPartials($"{{{{! spacer}}}}{newline}");

        Assert.Equal(expected, Renderer.Render(template, new { items, enabled = true }, partials));
    }

    [Theory, MemberData(nameof(LambdaCases))]
    public void SectionLambda_StripsStandaloneTagsInRenderedFragments(
        string newline, string indent, string signature)
    {
        var template = $"Before{newline}{LambdaTag(signature)}{newline}"
            + $"{indent}{{{{#items}}}}{newline}{indent}{{{{.}}}}{newline}"
            + $"{indent}{{{{/items}}}}{newline}{{{{/lambda}}}}{newline}After";
        var data = new Dictionary<string, object?>
        {
            ["lambda"] = IdentityLambda(signature),
            ["items"] = new[] { "one", "two" },
        };

        Assert.Equal($"Before{newline}{indent}one{newline}{indent}two{newline}After",
            Renderer.Render(template, data));
    }

    [Theory, MemberData(nameof(LambdaCases))]
    public void SectionLambda_ExcludesStandaloneClosingTagIndentation(
        string newline, string indent, string signature)
    {
        var template = $"Before{newline}{indent}{LambdaTag(signature)}{newline}"
            + $"{indent}body{newline}{indent}{{{{/lambda}}}}{newline}After";
        var data = new Dictionary<string, object?> { ["lambda"] = IdentityLambda(signature) };

        Assert.Equal($"Before{newline}{indent}body{newline}After",
            Renderer.Render(template, data));
    }

    [Theory, MemberData(nameof(LambdaCases))]
    public void SectionLambda_PreservesWhitespaceWhenClosingTagIsInline(
        string newline, string indent, string signature)
    {
        var template = $"Before{LambdaTag(signature)}  body{newline}{newline}"
            + $"{indent}{{{{/lambda}}}}After";
        var data = new Dictionary<string, object?> { ["lambda"] = IdentityLambda(signature) };

        Assert.Equal($"Before  body{newline}{newline}{indent}After",
            Renderer.Render(template, data));
    }

    [Theory, MemberData(nameof(LambdaCases))]
    public void SectionLambda_PreservesWhitespaceOnlyInlineBody(
        string newline, string indent, string signature)
    {
        var template = $"Before{newline}  {LambdaTag(signature)}{indent}{{{{/lambda}}}}{newline}After";
        var data = new Dictionary<string, object?> { ["lambda"] = IdentityLambda(signature) };

        Assert.Equal($"Before{newline}  {indent}{newline}After",
            Renderer.Render(template, data));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SectionLambda_RenderCallbackUsesActiveDelimiters(string newline)
    {
        var template = $"Before{newline}{{{{=<% %>=}}}}{newline}<%#lambda%>{newline}"
            + $"  <%#items%>{newline}  <%.%>{newline}  <%/items%>{newline}"
            + $"<%/lambda%>{newline}After";
        var data = new Dictionary<string, object?>
        {
            ["lambda"] = IdentityLambda("callback"),
            ["items"] = new[] { "one", "two" },
        };

        Assert.Equal($"Before{newline}  one{newline}  two{newline}After",
            Renderer.Render(template, data));
    }

    [Fact]
    public void SectionLambda_GeneratedTemplateStripsStandaloneTags()
    {
        var data = new Dictionary<string, object?>
        {
            ["lambda"] = (Func<string, string>)(_ => "  {{#items}}\n  {{.}}\n  {{/items}}\n"),
            ["items"] = new[] { "one" },
        };

        Assert.Equal("Before\n  one\nAfter",
            Renderer.Render("Before\n{{#lambda}}ignored{{/lambda}}After", data));
    }

    [Fact]
    public void SectionLambda_RawSpanPreservesInternalTagsAndBlankLines()
    {
        string? captured = null;
        var data = new Dictionary<string, object?>
        {
            ["lambda"] = (Func<string, string>)(raw => { captured = raw; return ""; }),
        };
        const string template = "Before\n  {{#lambda}}\n\n  {{#items}}\n  {{.}}\n  {{/items}}\n  {{/lambda}}\nAfter";

        Assert.Equal("Before\nAfter", Renderer.Render(template, data));
        Assert.Equal("\n  {{#items}}\n  {{.}}\n  {{/items}}\n", captured);
    }

    [Fact]
    public void ConsecutiveStandaloneTags_WorkInsidePartialsAndNestedSections()
    {
        var partials = new TestPartials(
            "{{#enabled}}\n  {{! comment}}\n  {{#items}}\n  {{.}}\n  {{/items}}\n{{/enabled}}\n");

        Assert.Equal("Before\n    one\n    two\nAfter",
            Renderer.Render("Before\n  {{> content}}\nAfter",
                new { enabled = true, items = new[] { "one", "two" } }, partials));
    }

    [Fact]
    public void InlineCommentAndSection_PreserveSurroundingWhitespace()
    {
        Assert.Equal("Before  XXAfter",
            Renderer.Render("Before {{! comment}} {{#items}}X{{/items}}After",
                new { items = new[] { "one", "two" } }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void InlineSectionTagPair_PreservesItsLine(string body)
    {
        Assert.Equal($"Before\n  {body}\nAfter",
            Renderer.Render($"Before\n  {{{{#enabled}}}}{body}{{{{/enabled}}}}\nAfter",
                new { enabled = true }));
    }

    private sealed class TestPartials(string template) : IPartialLoader
    {
        public string? Load(string name) => template;
    }
}
