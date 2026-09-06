using Whiskers;

namespace Whiskers.Tests;

public class LexerTests
{
    private static List<Token> Lex(string input) => new Lexer(input).Tokenize();

    [Fact]
    public void EmptyInput_ProducesOnlyEof()
    {
        var tokens = Lex("");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void PlainText_ProducesTextToken()
    {
        var tokens = Lex("hello world");
        Assert.Collection(tokens,
            t => { Assert.Equal(TokenKind.Text, t.Kind); Assert.Equal("hello world", t.Value); },
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void Variable_ProducesOpenIdentClose()
    {
        var tokens = Lex("{{name}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("name", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void Variable_WithSpaces_StripsWhitespace()
    {
        var tokens = Lex("{{ name }}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("name", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void TripleMustache_ProducesOpenRawIdentCloseRaw()
    {
        var tokens = Lex("{{{html}}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenRaw, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("html", t.Value); },
            t => Assert.Equal(TokenKind.CloseRaw, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void Comment_ProducesSingleCommentToken()
    {
        var tokens = Lex("{{! this is a comment }}");
        Assert.Collection(tokens,
            t => { Assert.Equal(TokenKind.Comment, t.Kind); Assert.Equal(" this is a comment ", t.Value); },
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void SectionOpen_ProducesSigilAndIdent()
    {
        var tokens = Lex("{{#items}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Sigil, t.Kind); Assert.Equal("#", t.Value); },
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("items", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void SectionClose_ProducesSlashSigilAndIdent()
    {
        var tokens = Lex("{{/items}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Sigil, t.Kind); Assert.Equal("/", t.Value); },
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("items", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Theory]
    [InlineData("#")]
    [InlineData("^")]
    [InlineData("*")]
    [InlineData("%")]
    [InlineData("~")]
    [InlineData("?")]
    [InlineData("/")]
    [InlineData("&")]
    [InlineData(">")]
    [InlineData("<")]
    [InlineData("$")]
    public void AllSigils_AreRecognized(string sigil)
    {
        var tokens = Lex($"{{{{{sigil}key}}}}");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Sigil && t.Value == sigil);
    }

    [Fact]
    public void MetadataVariable_ProducesAtAndIdent()
    {
        var tokens = Lex("{{@index}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => Assert.Equal(TokenKind.At, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("index", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void LocalLookup_ProducesDotAndIdent()
    {
        var tokens = Lex("{{.name}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => Assert.Equal(TokenKind.Dot, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("name", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void CurrentContext_ProducesSingleDot()
    {
        var tokens = Lex("{{.}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => Assert.Equal(TokenKind.Dot, t.Kind),
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void DottedKeypath_EmitsIdentDotIdent()
    {
        var tokens = Lex("{{person.name}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("person", t.Value); },
            t => Assert.Equal(TokenKind.Dot, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("name", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void NamedScope_EmitsSigilIdentColonIdent()
    {
        var tokens = Lex("{{#section :alias}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Sigil, t.Kind); Assert.Equal("#", t.Value); },
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("section", t.Value); },
            t => Assert.Equal(TokenKind.Colon, t.Kind),
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("alias", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void LambdaArgs_TokenizesStringAndNumber()
    {
        var tokens = Lex("{{#fn \"hello\" 42}}");
        Assert.Contains(tokens, t => t.Kind == TokenKind.StringLit && t.Value == "hello");
        Assert.Contains(tokens, t => t.Kind == TokenKind.NumLit && t.Value == "42");
    }

    [Fact]
    public void LambdaArgs_TokenizesDecimalNumber()
    {
        var tokens = Lex("{{#fn 3.14}}");
        Assert.Contains(tokens, t => t.Kind == TokenKind.NumLit && t.Value == "3.14");
    }

    [Fact]
    public void DynamicPartial_EmitsGreaterThanThenStarThenIdent()
    {
        var tokens = Lex("{{>*partialName}}");
        Assert.Collection(tokens,
            t => Assert.Equal(TokenKind.OpenTag, t.Kind),
            t => { Assert.Equal(TokenKind.Sigil, t.Kind); Assert.Equal(">", t.Value); },
            t => { Assert.Equal(TokenKind.Sigil, t.Kind); Assert.Equal("*", t.Value); },
            t => { Assert.Equal(TokenKind.Ident, t.Kind); Assert.Equal("partialName", t.Value); },
            t => Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void SetDelimiter_UpdatesDelimitersForSubsequentTags()
    {
        var tokens = Lex("{{= [[ ]] =}}[[ name ]]");
        Assert.Collection(tokens,
            t => { Assert.Equal(TokenKind.OpenTag, t.Kind);  Assert.Equal("{{",  t.Value); },
            t => { Assert.Equal(TokenKind.Sigil,   t.Kind);  Assert.Equal("=",   t.Value); },
            t => { Assert.Equal(TokenKind.Delim,   t.Kind);  Assert.Equal("[[",  t.Value); },
            t => { Assert.Equal(TokenKind.Delim,   t.Kind);  Assert.Equal("]]",  t.Value); },
            t => { Assert.Equal(TokenKind.Sigil,   t.Kind);  Assert.Equal("=",   t.Value); },
            t => { Assert.Equal(TokenKind.CloseTag, t.Kind); Assert.Equal("}}",  t.Value); },
            t => { Assert.Equal(TokenKind.OpenTag,  t.Kind); Assert.Equal("[[",  t.Value); },
            t => { Assert.Equal(TokenKind.Ident,    t.Kind); Assert.Equal("name", t.Value); },
            t => { Assert.Equal(TokenKind.CloseTag, t.Kind); Assert.Equal("]]",  t.Value); },
            t => Assert.Equal(TokenKind.Eof, t.Kind));
    }

    [Fact]
    public void TextAndTags_InterleavedCorrectly()
    {
        var tokens = Lex("Hello, {{name}}!");
        Assert.Collection(tokens,
            t => { Assert.Equal(TokenKind.Text,     t.Kind); Assert.Equal("Hello, ", t.Value); },
            t =>   Assert.Equal(TokenKind.OpenTag,  t.Kind),
            t => { Assert.Equal(TokenKind.Ident,    t.Kind); Assert.Equal("name", t.Value); },
            t =>   Assert.Equal(TokenKind.CloseTag, t.Kind),
            t => { Assert.Equal(TokenKind.Text,     t.Kind); Assert.Equal("!", t.Value); },
            t =>   Assert.Equal(TokenKind.Eof,      t.Kind));
    }

    [Fact]
    public void Token_Position_ReflectsSourceOffset()
    {
        var tokens = Lex("ab{{x}}");
        var openTag = tokens.First(t => t.Kind == TokenKind.OpenTag);
        Assert.Equal(2, openTag.Position);
    }
}
