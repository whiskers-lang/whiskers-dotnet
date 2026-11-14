using Whiskers;

namespace Whiskers.Tests;

public class ValueLambdaSectionTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(false, "")]
    [InlineData("", "[]")]
    [InlineData(0, "[0]")]
    [InlineData("Report", "[Report]")]
    public void ReturnedValuesUseOrdinarySectionTruthiness(object? value, string expected)
    {
        var calls = 0;
        var data = new { value = (Func<object?>)(() => { calls++; return value; }) };

        Assert.Equal(expected, Renderer.Render("{{#value}}[{{.}}]{{/value}}", data));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("before{{#value}}{{/value}}after", false, "beforeafter")]
    [InlineData("before\n{{#value}}\n{{/value}}\nafter", false, "before\nafter")]
    [InlineData("before\r\n  {{#value}}\r\n  {{/value}}\r\nafter", false, "before\r\nafter")]
    [InlineData("before{{#value key}}{{/value}}after", true, "beforeafter")]
    [InlineData("before\n{{#value key}}\n{{/value}}\nafter", true, "before\nafter")]
    [InlineData("before\r\n  {{#value key}}\r\n  {{/value}}\r\nafter", true, "before\r\nafter")]
    public void EmptySectionsStillInvokeExactlyOnce(string template, bool takesArguments, string expected)
    {
        var calls = 0;
        object lambda = takesArguments
            ? (Func<object?[], object?>)(args =>
            {
                Assert.Equal("key", Assert.Single(args));
                calls++;
                return null;
            })
            : (Func<object?>)(() => { calls++; return null; });

        Assert.Equal(expected, Renderer.Render(template, new { value = lambda }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ReturnedArraysUseAliasesMetadataAndParentLookup()
    {
        var calls = 0;
        var items = new[] { new { name = "one" }, new { name = "two" } };
        var data = new
        {
            title = "Parent",
            value = (Func<object>)(() => { calls++; return items; }),
        };
        const string template = "{{#value :item}}{{item.name}}/{{@item.index}}/" +
            "{{@number}}/{{@length}}/{{title}};{{/value}}";

        Assert.Equal("one/0/1/2/Parent;two/1/2/2/Parent;", Renderer.Render(template, data));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EmptyCollectionsRetainArrayAndDictionaryDistinction()
    {
        var data = new
        {
            array = (Func<object>)(() => Array.Empty<object>()),
            dictionary = (Func<object>)(() => new Dictionary<string, object?>()),
        };
        Assert.Equal("dictionary", Renderer.Render(
            "{{#array}}array{{/array}}{{#dictionary}}dictionary{{/dictionary}}", data));
    }

    [Fact]
    public void ArgumentsRetainTypesAndResolveBeforeReturnedContextIsPushed()
    {
        var calls = 0;
        var data = new
        {
            title = "Parent",
            key = "root",
            item = new { key = "local" },
            value = (Func<object?[], object>)(args =>
            {
                calls++;
                Assert.Equal("key", Assert.IsType<string>(args[0]));
                Assert.Equal("two words", Assert.IsType<string>(args[1]));
                Assert.Equal(42, Assert.IsType<int>(args[2]));
                Assert.Equal(3.14, Assert.IsType<double>(args[3]));
                Assert.Equal("local", args[4]);
                Assert.Equal("root", args[5]);
                return new Dictionary<string, object?> { ["name"] = "Child" };
            }),
        };
        Assert.Equal("Child/Child/Parent", Renderer.Render(
            "{{#item}}{{#value :result key \"two words\" 42 3.14 *key *@root.key}}" +
            "{{result.name}}/{{name}}/{{title}}{{/value}}{{/item}}", data));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{{#value key}}{{.}}|{{{.}}}{{/value}}")]
    [InlineData("{{=<% %>=}}<%#value key%><% . %>|<%& .%><%/value%>")]
    public void ReturnedStringsAreDataNotTemplates(string template)
    {
        var data = new
        {
            name = "World",
            value = (Func<object?[], string>)(_ => "<b>{{name}}</b>"),
        };
        Assert.Equal("&lt;b&gt;{{name}}&lt;/b&gt;|<b>{{name}}</b>", Renderer.Render(template, data));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData("text")]
    public void NonFunctionsWithArgumentsThrow(object? value)
    {
        var exception = Assert.Throws<RenderException>(() =>
            Renderer.Render("{{#value key}}body{{/value}}", new { value }));
        Assert.Equal("Key 'value' does not accept arguments", exception.Message);
    }

    [Fact]
    public void NoArgumentFunctionRejectsArgumentsWithoutInvocation()
    {
        var data = new { value = (Func<object>)(() => throw new InvalidOperationException()) };
        Assert.Throws<RenderException>(() => Renderer.Render("{{#value key}}body{{/value}}", data));
    }

    [Fact]
    public void MissingKeysRetainStrictAndNonStrictBehavior()
    {
        Assert.Equal("beforeafter", Renderer.Render("before{{#missing key}}x{{/missing}}after", null));
        Assert.Throws<RenderException>(() =>
            Renderer.Render("{{#@strict}}{{#missing key}}x{{/missing}}{{/@strict}}", null));
    }

    [Fact]
    public void OtherSigilsNeverInvokeValueFunctions()
    {
        var data = new { value = (Func<object>)(() => throw new InvalidOperationException()) };
        Assert.Equal("presentnonnull", Renderer.Render(
            "{{^value}}inverted{{/value}}{{*value}}present{{/value}}{{%value}}absent{{/value}}" +
            "{{~value}}nonnull{{/value}}{{?value}}null{{/value}}", data));
    }

    [Fact]
    public void ReturnedDelegatesAreNotDispatchedAsLambdasAgain()
    {
        var calls = 0;
        var data = new
        {
            value = (Func<object>)(() =>
            {
                calls++;
                return (Func<string, string>)(_ => throw new InvalidOperationException());
            }),
        };
        Assert.Equal("body", Renderer.Render("{{#value}}body{{/value}}", data));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EachReachedOccurrenceInvokesOnceAndExceptionsPropagate()
    {
        var calls = 0;
        var data = new
        {
            value = (Func<object?>)(() => { calls++; return null; }),
            fail = (Func<object>)(() => throw new InvalidOperationException("lambda failed")),
            disabled = false,
        };
        Assert.Equal("", Renderer.Render(
            "{{#value}}{{/value}}{{#value}}{{/value}}" +
            "{{#disabled}}{{#value}}{{/value}}{{/disabled}}", data));
        Assert.Equal(2, calls);
        var error = Assert.Throws<InvalidOperationException>(() =>
            Renderer.Render("{{#fail}}{{/fail}}", data));
        Assert.Equal("lambda failed", error.Message);
    }

    [Fact]
    public void ReturnedEnumerableIsConsumedOnlyOnce()
    {
        var enumerations = 0;
        IEnumerable<string> Items()
        {
            enumerations++;
            Assert.Equal(1, enumerations);
            yield return "one";
            yield return "two";
        }

        var data = new { value = (Func<object>)(() => Items()) };
        Assert.Equal("onetwo", Renderer.Render("{{#value}}{{.}}{{/value}}", data));
        Assert.Equal(1, enumerations);
    }
}
