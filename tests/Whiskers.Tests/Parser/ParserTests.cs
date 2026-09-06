using Whiskers;

namespace Whiskers.Tests;

public class ParserTests
{
    private static TemplateNode Parse(string input)
    {
        var tokens = new Lexer(input).Tokenize();
        return new Parser(tokens).Parse();
    }

    [Fact]
    public void EmptyTemplate_ProducesEmptyChildList()
    {
        Assert.Empty(Parse("").Children);
    }

    [Fact]
    public void PlainText_ProducesTextNode()
    {
        var node = Assert.IsType<TextNode>(Assert.Single(Parse("hello").Children));
        Assert.Equal("hello", node.Content);
    }

    [Fact]
    public void Variable_ProducesEscapedVariableNode()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{name}}").Children));
        Assert.Equal("name", node.Key);
        Assert.True(node.Escape);
    }

    [Fact]
    public void DottedKeypath_PreservesFullPath()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{person.name}}").Children));
        Assert.Equal("person.name", node.Key);
    }

    [Fact]
    public void LocalKeypath_PreservesLeadingDot()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{.name}}").Children));
        Assert.Equal(".name", node.Key);
    }

    [Fact]
    public void MetadataKeypath_PreservesAtSign()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{@index}}").Children));
        Assert.Equal("@index", node.Key);
    }

    [Fact]
    public void CurrentContext_ProducesVariableNodeWithDot()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{.}}").Children));
        Assert.Equal(".", node.Key);
    }

    [Fact]
    public void TripleMustache_ProducesUnescapedVariable()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{{html}}}").Children));
        Assert.Equal("html", node.Key);
        Assert.False(node.Escape);
    }

    [Fact]
    public void AmpersandSigil_ProducesUnescapedVariable()
    {
        var node = Assert.IsType<VariableNode>(Assert.Single(Parse("{{& html}}").Children));
        Assert.Equal("html", node.Key);
        Assert.False(node.Escape);
    }

    [Fact]
    public void Comment_ProducesCommentNode()
    {
        var node = Assert.IsType<CommentNode>(Assert.Single(Parse("{{! a comment }}").Children));
        Assert.Equal(" a comment ", node.Content);
    }

    [Theory]
    [InlineData('#')]
    [InlineData('^')]
    [InlineData('*')]
    [InlineData('%')]
    [InlineData('~')]
    [InlineData('?')]
    public void SectionSigil_ProducesSectionNodeWithCorrectSigil(char sigil)
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse($"{{{{{sigil}key}}}}body{{{{/key}}}}").Children));
        Assert.Equal(sigil, node.Sigil);
        Assert.Equal("key", node.Key);
        Assert.Equal("body", Assert.IsType<TextNode>(Assert.Single(node.Children)).Content);
    }

    [Fact]
    public void NestedSections_ParseCorrectly()
    {
        var outer = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#outer}}{{#inner}}x{{/inner}}{{/outer}}").Children));
        var inner = Assert.IsType<SectionNode>(Assert.Single(outer.Children));
        Assert.Equal("inner", inner.Key);
        Assert.IsType<TextNode>(Assert.Single(inner.Children));
    }

    [Fact]
    public void NamedScope_ParsesAlias()
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#items :item}}{{/items}}").Children));
        Assert.Equal("item", node.Alias);
    }

    [Fact]
    public void LambdaStringArg_ParsesCorrectly()
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#fn \"hello\"}}{{/fn}}").Children));
        Assert.Equal("hello", Assert.IsType<StringArgument>(Assert.Single(node.Arguments)).Value);
    }

    [Fact]
    public void LambdaUnquotedStringArg_ParsesCorrectly()
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#fn hello}}{{/fn}}").Children));
        Assert.Equal("hello", Assert.IsType<StringArgument>(Assert.Single(node.Arguments)).Value);
    }

    [Fact]
    public void LambdaDynamicArg_RequiresStarPrefix()
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#fn *user.name}}{{/fn}}").Children));
        Assert.Equal("user.name", Assert.IsType<VariableArgument>(Assert.Single(node.Arguments)).Key);
    }

    [Fact]
    public void LambdaNumericArg_ParsesCorrectly()
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#fn 42}}{{/fn}}").Children));
        Assert.Equal("42", Assert.IsType<NumberArgument>(Assert.Single(node.Arguments)).Value);
    }

    [Fact]
    public void LambdaMultipleArgs_ParsesAllArguments()
    {
        var node = Assert.IsType<SectionNode>(Assert.Single(Parse("{{#fn \"a\" 1 b *value}}{{/fn}}").Children));
        Assert.Equal(4, node.Arguments.Count);
        Assert.IsType<StringArgument>(node.Arguments[0]);
        Assert.IsType<NumberArgument>(node.Arguments[1]);
        Assert.IsType<StringArgument>(node.Arguments[2]);
        Assert.IsType<VariableArgument>(node.Arguments[3]);
    }

    [Fact]
    public void Partial_ProducesPartialNode()
    {
        var node = Assert.IsType<PartialNode>(Assert.Single(Parse("{{> header}}").Children));
        Assert.Equal("header", node.Name);
    }

    [Fact]
    public void SetDelimiter_ProducesSetDelimiterNode()
    {
        var node = Assert.IsType<SetDelimiterNode>(Assert.Single(Parse("{{= [[ ]] =}}").Children));
        Assert.Equal("[[", node.Open);
        Assert.Equal("]]", node.Close);
    }

    [Fact]
    public void TextAndTags_ProduceCorrectNodeSequence()
    {
        var children = Parse("Hello, {{name}}!").Children;
        Assert.Equal(3, children.Count);
        Assert.IsType<TextNode>(children[0]);
        Assert.IsType<VariableNode>(children[1]);
        Assert.IsType<TextNode>(children[2]);
    }

    [Fact]
    public void MismatchedCloseTag_ThrowsParseException()
    {
        Assert.Throws<ParseException>(() => Parse("{{#foo}}{{/bar}}"));
    }

    [Fact]
    public void UnclosedSection_ThrowsParseException()
    {
        Assert.Throws<ParseException>(() => Parse("{{#foo}}body"));
    }

    [Fact]
    public void StandaloneCloseTag_ThrowsParseException()
    {
        Assert.Throws<ParseException>(() => Parse("{{/foo}}"));
    }
}
