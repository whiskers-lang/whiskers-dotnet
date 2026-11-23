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

    [Fact]
    public void StartingScope_PreservesAncestorsRootAndLocalLookup()
    {
        var data = new
        {
            name = "root",
            company = new
            {
                name = "Example",
                departments = new { engineering = new { location = "London" } }
            }
        };
        var result = Renderer.Render(
            "  {{location}}|{{name}}|{{.name}}|{{@root.name}}|{{@root.company.name}}\n",
            data, startingPath: "company.departments.engineering");
        Assert.Equal("  London|Example||root|Example\n", result);
    }

    [Fact]
    public void StartingScope_PartialsAndLambdaCallbacksInheritStack()
    {
        var scope = D(("name", "local"));
        var data = D(
            ("name", "root"),
            ("scope", scope),
            ("wrap", (Func<string, Func<string, string>, object>)((raw, render) => render(raw))));
        var partials = new MockPartials(("item", "{{name}}/{{@root.name}}"));
        Assert.Equal("local/root|local/root", Renderer.Render(
            "{{>item}}|{{#wrap}}{{name}}/{{@root.name}}{{/wrap}}",
            data, partials, startingPath: "scope"));
        Assert.Single(scope);
    }

    [Fact]
    public void StartingScope_NullSelectionRetainsAncestorLookup()
    {
        var ast = new Parser(new Lexer("{{.}}|{{name}}|{{@root.name}}").Tokenize(),
            "{{.}}|{{name}}|{{@root.name}}").Parse();
        var renderer = new Renderer(options: new RenderOptions(StrictMode: true));
        Assert.Equal("|parent|parent",
            renderer.Render(ast, D(("name", "parent"), ("child", null)), "child"));
    }

    [Fact]
    public void StartingScope_CollectionOnlyIteratesExplicitly()
    {
        var data = D(("name", "root"), ("items", new[] { "a", "b" }));
        Assert.Equal("once||0:a:True:False:root;1:b:False:True:root;",
            Renderer.Render(
                "once|{{@index}}|{{#.}}{{@index}}:{{.}}:{{@first}}:{{@last}}:{{name}};{{/.}}",
                data, startingPath: "items"));
    }

    [Theory]
    [InlineData("items[0].name", "first")]
    [InlineData("items[1].name", "second")]
    [InlineData("list[1].name", "second")]
    [InlineData("[\"items\"][0][\"name\"]", "first")]
    public void StartingScope_SelectsIndexedItems(string path, string expected)
    {
        var items = new[] { new { name = "first" }, new { name = "second" } };
        Assert.Equal(expected, Renderer.Render("{{.}}",
            D(("items", items), ("list", items.ToList())), startingPath: path));
    }

    [Fact]
    public void StartingScope_IndexedItemRetainsCollectionAndObjectAncestors()
    {
        var data = D(("name", "root"), ("items", new[] { D(("value", "selected")) }));
        Assert.Equal("selected|1|root|", Renderer.Render(
            "{{value}}|{{Length}}|{{name}}|{{@index}}", data, startingPath: "items[0]"));
        Assert.Equal("selected", Renderer.Render("{{value}}",
            new[] { D(("value", "selected")) }, startingPath: "[0]"));
    }

    [Theory]
    [InlineData("[\"a.b\"]", "a.b")]
    [InlineData("['a.b']", "a.b")]
    [InlineData("[\"\"]", "")]
    [InlineData("[\"a[b]\"]", "a[b]")]
    [InlineData("[\"a\\\"b\"]", "a\"b")]
    [InlineData("['a\\'b']", "a'b")]
    [InlineData("[\"a\\\\b\"]", "a\\b")]
    [InlineData("[\"0\"]", "0")]
    public void StartingScope_QuotedKeysAreLiteral(string path, string key)
    {
        Assert.Equal("selected", Renderer.Render("{{.}}",
            D((key, "selected")), startingPath: path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".company")]
    [InlineData("company.")]
    [InlineData("company..name")]
    [InlineData("company.[0]")]
    [InlineData("company[]")]
    [InlineData("company[-1]")]
    [InlineData("company[2147483648]")]
    [InlineData("company[0")]
    [InlineData("company[0]name")]
    [InlineData("company]")]
    [InlineData("company[\"name\"")]
    [InlineData("company['name]")]
    [InlineData("company[\"na\\qme\"]")]
    public void StartingScope_RejectsMalformedPaths(string path)
    {
        Assert.Throws<RenderException>(() => Renderer.Render("unused",
            D(("company", D(("name", "Example")))), startingPath: path));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("company.name")]
    [InlineData("child.name")]
    [InlineData("items[1]")]
    [InlineData("company[0]")]
    [InlineData("text[0]")]
    public void StartingScope_MissingSelectionThrowsWithoutAncestorFallback(string path)
    {
        var data = D(("name", "root"), ("company", D()), ("child", null),
            ("items", new[] { "only" }), ("text", "abc"));
        var error = Assert.Throws<RenderException>(() =>
            Renderer.Render("unused", data, startingPath: path));
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void StartingScope_DoesNotInvokeLambdasDuringNavigation()
    {
        int calls = 0;
        var data = D(("fn", (Func<object>)(() => { calls++; return D(("name", "value")); })));
        Assert.Equal("constant", Renderer.Render("constant", data, startingPath: "fn"));
        Assert.Throws<RenderException>(() => Renderer.Render("unused", data, startingPath: "fn.name"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void StartingScope_ExplicitNullPathPreservesDefaultRendering()
    {
        Assert.Equal("root", Renderer.Render("{{name}}", D(("name", "root")), startingPath: null));
        Assert.Equal("", Renderer.Render("{{.}}", null, startingPath: null));
        Assert.Throws<RenderException>(() => Renderer.Render("unused", null, startingPath: "missing"));
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

    [Theory]
    [InlineData("\",\"", false)]
    [InlineData(",", true)]
    public void Lambda_PunctuationArgument_IsPassedOrRejectedBeforeInvocation(string argument, bool invalid)
    {
        var calls = 0;
        var inspect = (Func<object?[], string, string>)((args, _) =>
        {
            calls++;
            return Assert.IsType<string>(Assert.Single(args));
        });
        var data = D(("lambdas", D(("stripTrailing", inspect))));
        var template = "{{#lambdas.stripTrailing " + argument + "  }}body{{/lambdas.stripTrailing}}";

        if (invalid)
            Assert.Throws<ParseException>(() => Render(template, data));
        else
            Assert.Equal(",", Render(template, data));

        Assert.Equal(invalid ? 0 : 1, calls);
    }

    [Fact] public void Lambda_InterpolationArguments_PreserveTypesAndResolveContext()
    {
        var inspect = (Func<object?[], object>)(args =>
        {
            Assert.Equal("value", Assert.IsType<string>(args[0]));
            Assert.Equal("value", Assert.IsType<string>(args[1]));
            Assert.Equal("resolved", Assert.IsType<string>(args[2]));
            Assert.Equal(42, Assert.IsType<int>(args[3]));
            Assert.Equal(3.14, Assert.IsType<double>(args[4]));
            return "ok";
        });
        var data = D(("inspect", inspect), ("item", D(("value", "resolved"))));
        Assert.Equal("ok", Render(
            "{{#item}}{{inspect value \"value\" *value 42 3.14}}{{/item}}", data));
    }

    [Theory]
    [InlineData("{{recall key}}", "&lt;b&gt;{{name}}&lt;/b&gt;")]
    [InlineData("{{{recall key}}}", "<b>{{name}}</b>")]
    [InlineData("{{& recall key}}", "<b>{{name}}</b>")]
    [InlineData("{{=<% %>=}}<%recall key%>", "&lt;b&gt;{{name}}&lt;/b&gt;")]
    public void Lambda_InterpolationArguments_UseNormalInterpolationSemantics(string template, string expected)
    {
        var data = D(("name", "World"),
            ("recall", (Func<object?[], string>)(_ => "<b>{{name}}</b>")));
        Assert.Equal(expected, Render(template, data));
        Assert.Equal("<b>{{name}}</b>", RenderOpts("{{recall key}}", data,
            new RenderOptions(SkipHtmlEncoding: true)));
    }

    [Fact] public void Lambda_CacheSection_CanBeRecalledWithoutSection()
    {
        var cache = new Dictionary<string, string>();
        var data = D(("name", "World"), ("key", "greeting"),
            ("cache", (Func<object?[], string, Func<string, string>, object>)((args, raw, render) =>
            {
                cache[(string)args[0]!] = render(raw);
                return "";
            })),
            ("recall", (Func<object?[], object>)(args => cache[(string)args[0]!])));
        Assert.Equal("Hello, World!|Hello, World!", Render(
            "{{#cache greeting}}Hello, {{name}}!{{/cache}}{{recall greeting}}|{{recall *key}}", data));
    }

    [Theory]
    [InlineData("text")]
    [InlineData(false)]
    [InlineData(null)]
    public void Lambda_InterpolationArguments_RejectNonFunctions(object? value)
    {
        var error = Assert.Throws<RenderException>(() => Render("{{value key}}", D(("value", value))));
        Assert.Equal("Key 'value' does not accept arguments", error.Message);
    }

    [Fact] public void Lambda_InterpolationArguments_RejectNoArgFunction()
    {
        var data = D(("fn", (Func<object>)(() => throw new InvalidOperationException())));
        Assert.Throws<RenderException>(() => Render("{{fn key}}", data));
    }

    [Fact] public void Lambda_InterpolationArguments_PreserveMissingKeyBehavior()
    {
        Assert.Equal("", Render("{{missing key}}", D()));
        Assert.Throws<RenderException>(() => Render("{{missing key}}", D(), strict: true));
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
