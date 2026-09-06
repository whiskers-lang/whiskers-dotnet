using Whiskers;

namespace Whiskers.Tests;

public class RendererTests
{
    private static string Render(string template, object? data = null, IPartialLoader? partials = null, bool strict = false)
    {
        var tokens = new Lexer(template).Tokenize();
        var ast = new Parser(tokens, template).Parse();
        return new Renderer(partials, new RenderOptions(strict)).Render(ast, data);
    }

    private static Dictionary<string, object?> D(params (string k, object? v)[] pairs) =>
        pairs.ToDictionary(p => p.k, p => p.v);

    private sealed class MockPartials : IPartialLoader
    {
        private readonly Dictionary<string, string> _map;
        public MockPartials(params (string name, string content)[] templates) =>
            _map = templates.ToDictionary(t => t.name, t => t.content);
        public string? Load(string name) => _map.TryGetValue(name, out var t) ? t : null;
    }

    // --- Text and variables ---

    [Fact] public void PlainText_RendersAsIs() =>
        Assert.Equal("hello", Render("hello"));

    [Fact] public void EmptyTemplate_ProducesEmptyString() =>
        Assert.Equal("", Render(""));

    [Fact] public void Variable_SubstitutesValue() =>
        Assert.Equal("Alice", Render("{{name}}", D(("name", "Alice"))));

    [Fact] public void MissingVariable_ProducesEmptyString() =>
        Assert.Equal("", Render("{{missing}}", D()));

    [Fact] public void NullVariable_ProducesEmptyString() =>
        Assert.Equal("", Render("{{x}}", D(("x", null))));

    [Fact] public void Variable_HtmlEscapesSpecialChars() =>
        Assert.Equal("&lt;b&gt;&amp;&quot;", Render("{{v}}", D(("v", "<b>&\""))));

    [Fact] public void UnescapedAmpersand_SkipsHtmlEscape() =>
        Assert.Equal("<b>", Render("{{& v}}", D(("v", "<b>"))));

    [Fact] public void TripleMustache_SkipsHtmlEscape() =>
        Assert.Equal("<b>", Render("{{{v}}}", D(("v", "<b>"))));

    [Fact] public void DottedKeypath_ResolvesNestedKey() =>
        Assert.Equal("Alice", Render("{{person.name}}", D(("person", D(("name", "Alice"))))));

    [Fact] public void LocalKeypath_OnlyLooksInCurrentFrame()
    {
        // .name should NOT find the parent's "name"
        var data = D(("name", "parent"), ("inner", D(("age", 1))));
        Assert.Equal("", Render("{{#inner}}{{.name}}{{/inner}}", data));
    }

    [Fact] public void CurrentContext_RendersCurrentValue() =>
        Assert.Equal("x", Render("{{#items}}{{.}}{{/items}}", D(("items", new object?[] { "x" }))));

    [Fact] public void Poco_ResolvesByPropertyNameIgnoreCase() =>
        Assert.Equal("Alice", Render("{{name}}", new { Name = "Alice" }));

    // --- Sections ---

    [Fact] public void TruthySection_RendersWithContextPush() =>
        Assert.Equal("Alice", Render("{{#person}}{{name}}{{/person}}", D(("person", D(("name", "Alice"))))));

    [Fact] public void FalseBool_SkipsSection() =>
        Assert.Equal("", Render("{{#show}}yes{{/show}}", D(("show", false))));

    [Fact] public void NullValue_SkipsSection() =>
        Assert.Equal("", Render("{{#show}}yes{{/show}}", D(("show", null))));

    [Fact] public void InvertedSection_RendersWhenFalsy() =>
        Assert.Equal("no", Render("{{^show}}no{{/show}}", D(("show", false))));

    [Fact] public void InvertedSection_SkipsWhenTruthy() =>
        Assert.Equal("", Render("{{^show}}no{{/show}}", D(("show", true))));

    [Fact] public void Comment_ProducesNoOutput() =>
        Assert.Equal("AB", Render("A{{! ignored }}B"));

    // --- Array iteration ---

    [Fact] public void ArraySection_IteratesItems() =>
        Assert.Equal("abc", Render("{{#items}}{{.}}{{/items}}", D(("items", new object?[] { "a", "b", "c" }))));

    [Fact] public void EmptyArray_ProducesNoOutput() =>
        Assert.Equal("", Render("{{#items}}x{{/items}}", D(("items", new object?[0]))));

    [Fact] public void Iteration_ProvidesZeroBasedIndex() =>
        Assert.Equal("012", Render("{{#items}}{{@index}}{{/items}}", D(("items", new object?[] { "a", "b", "c" }))));

    [Fact] public void Iteration_ProvidesOneBasedNumber() =>
        Assert.Equal("123", Render("{{#items}}{{@number}}{{/items}}", D(("items", new object?[] { "a", "b", "c" }))));

    [Fact] public void Iteration_FirstAndLastFlags()
    {
        var result = Render("{{#items}}{{@first}}{{@last}}{{/items}}", D(("items", new object?[] { "a", "b", "c" })));
        Assert.Equal("TrueFalseFalseFalseFalseTrue", result);
    }

    [Fact] public void Iteration_ProvidesLength() =>
        Assert.Equal("333", Render("{{#items}}{{@length}}{{/items}}", D(("items", new object?[] { "a", "b", "c" }))));

    [Fact] public void Iteration_ItemFieldsAccessible()
    {
        var items = new object?[] { D(("n", "x")), D(("n", "y")) };
        Assert.Equal("xy", Render("{{#items}}{{n}}{{/items}}", D(("items", items))));
    }

    // --- Whiskers sigils ---

    [Fact] public void ExistsSigil_RendersWhenKeyPresent() =>
        Assert.Equal("yes", Render("{{*name}}yes{{/name}}", D(("name", null)))); // null exists

    [Fact] public void ExistsSigil_PushesContext() =>
        Assert.Equal("Alice", Render("{{*person}}{{name}}{{/person}}", D(("person", D(("name", "Alice"))))));

    [Fact] public void ExistsSigil_PushesNullContext() =>
        Assert.Equal("", Render("{{*x}}{{.}}{{/x}}", D(("x", null))));

    [Fact] public void ExistsSigil_SkipsWhenKeyAbsent() =>
        Assert.Equal("", Render("{{*name}}yes{{/name}}", D()));

    [Fact] public void AbsentSigil_RendersWhenKeyMissing() =>
        Assert.Equal("nope", Render("{{%name}}nope{{/name}}", D()));

    [Fact] public void AbsentSigil_SkipsWhenKeyPresent() =>
        Assert.Equal("", Render("{{%name}}nope{{/name}}", D(("name", "x"))));

    [Fact] public void NotNullSigil_RendersWhenValueNotNull() =>
        Assert.Equal("yes", Render("{{~name}}yes{{/name}}", D(("name", "Alice"))));

    [Fact] public void NotNullSigil_SkipsWhenValueNull() =>
        Assert.Equal("", Render("{{~name}}yes{{/name}}", D(("name", null))));

    [Fact] public void NullSigil_RendersWhenValueNull() =>
        Assert.Equal("is null", Render("{{?name}}is null{{/name}}", D(("name", null))));

    [Fact] public void NullSigil_SkipsWhenValueNotNull() =>
        Assert.Equal("", Render("{{?name}}is null{{/name}}", D(("name", "x"))));

    // --- Named scope ---

    [Fact] public void NamedScope_AccessibleViaAlias() =>
        Assert.Equal("Alice", Render("{{#person :p}}{{p.name}}{{/person}}", D(("person", D(("name", "Alice"))))));

    // --- Strict mode ---

    [Fact] public void StrictMode_ThrowsForMissingKey() =>
        Assert.Throws<RenderException>(() => Render("{{missing}}", D(), strict: true));

    [Fact] public void StrictBlock_EnablesStrictModeLocally() =>
        Assert.Throws<RenderException>(() => Render("{{#@strict}}{{missing}}{{/@strict}}", D()));

    [Fact] public void ExistsSigil_ExemptFromStrictMode() =>
        Assert.Equal("", Render("{{*missing}}yes{{/missing}}", D(), strict: true));

    [Fact] public void AbsentSigil_ExemptFromStrictMode() =>
        Assert.Equal("yes", Render("{{%missing}}yes{{/missing}}", D(), strict: true));

    // --- Partial ---

    [Fact] public void Partial_InheritesCurrentContext() =>
        Assert.Equal("Hello, Alice!", Render("{{> greeting}}", D(("name", "Alice")),
            new MockPartials(("greeting", "Hello, {{name}}!"))));

    // --- Lambda ---

    [Fact] public void Lambda_ReceivesRawBlockAndRerendersResult()
    {
        var data = D(("wrap", (Func<string, string>)(s => $"<b>{s}</b>")));
        Assert.Equal("<b>hello</b>", Render("{{#wrap}}hello{{/wrap}}", data));
    }

    [Fact] public void Lambda_FuncStringObject_ReceivesRawAndRerendersResult()
    {
        // raw content "{{n}}" passed to lambda → "[{{n}}]" → re-rendered → "[42]"
        var data = D(("n", 42), ("wrap", (Func<string, object>)(s => $"[{s}]")));
        Assert.Equal("[42]", Render("{{#wrap}}{{n}}{{/wrap}}", data));
    }

    [Fact] public void Lambda_FuncStringObject_ResultIsRerendered()
    {
        var data = D(("n", 42), ("wrap", (Func<string, object>)(_ => "{{n}}")));
        Assert.Equal("42", Render("{{#wrap}}ignored{{/wrap}}", data));
    }

    [Fact] public void Lambda_ContextAware_ReceivesCurrentView()
    {
        var data = D(("name", "Alice"), ("greet", (Func<object, string, object>)((ctx, s) =>
        {
            var d = (Dictionary<string, object?>)ctx;
            return $"Hello {d["name"]}: {s}";
        })));
        Assert.Equal("Hello Alice: world", Render("{{#greet}}world{{/greet}}", data));
    }

    [Fact] public void Lambda_RenderCallback_RendersContentInContext()
    {
        var data = D(("name", "Bob"), ("bold", (Func<string, Func<string, string>, object>)((s, render) => $"<b>{render(s)}</b>")));
        Assert.Equal("<b>Bob</b>", Render("{{#bold}}{{name}}{{/bold}}", data));
    }

    [Fact] public void Lambda_Arguments_DistinguishLiteralAndDynamicStrings()
    {
        var inspect = (Func<object[], string, string>)((args, _) => string.Join("|", args));
        var data = D(("value", "resolved"), ("inspect", inspect));
        Assert.Equal("value|value|resolved", Render(
            "{{#inspect value \"value\" *value}}{{/inspect}}",
            data));
    }

    [Fact] public void Lambda_NoArgInterpolation_InvokesFunc()
    {
        var data = D(("ts", (Func<object>)(() => "2026-01-01")));
        Assert.Equal("2026-01-01", Render("{{ts}}", data));
    }

    // --- InvariantCulture ---

    [Fact] public void Variable_Double_UsesInvariantCulture()
    {
        var data = D(("pi", 3.14));
        Assert.Equal("3.14", Render("{{pi}}", data));
    }

    // --- SkipHtmlEncoding ---

    private static string RenderOpts(string template, object? data, RenderOptions opts) =>
        new Renderer(null, opts).Render(new Parser(new Lexer(template).Tokenize(), template).Parse(), data);

    [Fact] public void SkipHtmlEncoding_DoesNotEscapeVariables()
    {
        var data = D(("v", "<b>"));
        Assert.Equal("<b>", RenderOpts("{{v}}", data, new RenderOptions(SkipHtmlEncoding: true)));
    }

    [Fact] public void SkipHtmlEncoding_False_StillEscapes()
    {
        var data = D(("v", "<b>"));
        Assert.Equal("&lt;b&gt;", RenderOpts("{{v}}", data, new RenderOptions(SkipHtmlEncoding: false)));
    }

    // --- MaxRecursionDepth ---

    private sealed class RecursivePartials : IPartialLoader
    {
        public string? Load(string name) => "{{> self}}";
    }

    [Fact] public void MaxRecursionDepth_ThrowsWhenExceeded()
    {
        var renderer = new Renderer(new RecursivePartials(), new RenderOptions(MaxRecursionDepth: 5));
        var ast = new Parser(new Lexer("{{> self}}").Tokenize(), "{{> self}}").Parse();
        Assert.Throws<RenderException>(() => renderer.Render(ast, null));
    }

    [Fact] public void MaxRecursionDepth_NonRecursive_DoesNotThrow()
    {
        var partials = new MockPartials(("a", "A"), ("b", "B"));
        var renderer = new Renderer(partials, new RenderOptions(MaxRecursionDepth: 2));
        var ast = new Parser(new Lexer("{{> a}}{{> b}}").Tokenize(), "{{> a}}{{> b}}").Parse();
        Assert.Equal("AB", renderer.Render(ast, null));
    }
}
