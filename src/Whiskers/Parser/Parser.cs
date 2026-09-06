namespace Whiskers;

public sealed class Parser(IReadOnlyList<Token> tokens, string source = "")
{
    private int _pos;
    private string _openDelim = "{{";
    private string _closeDelim = "}}";

    public TemplateNode Parse()
    {
        var children = ParseNodes();
        if (Current().Kind != TokenKind.Eof)
            throw new ParseException($"Unexpected token {Current().Kind} at position {Current().Position}");
        return new TemplateNode(children, source);
    }

    private List<INode> ParseNodes()
    {
        var nodes = new List<INode>();
        while (!IsAtEof() && !IsAtCloseTag())
            nodes.Add(ParseNode());
        return nodes;
    }

    private INode ParseNode()
    {
        var t = Current();

        if (t.Kind == TokenKind.Text)    { Advance(); return new TextNode(t.Value); }
        if (t.Kind == TokenKind.Comment) { Advance(); return new CommentNode(t.Value); }
        if (t.Kind == TokenKind.OpenRaw) return ParseRawVariable();
        if (t.Kind == TokenKind.OpenTag) return ParseTag();

        throw new ParseException($"Unexpected token {t.Kind} at position {t.Position}");
    }

    private VariableNode ParseRawVariable()
    {
        Advance(); // consume OpenRaw
        var key = ParseKeypath();
        Consume(TokenKind.CloseRaw);
        return new VariableNode(key, Escape: false);
    }

    private INode ParseTag()
    {
        Consume(TokenKind.OpenTag);
        var t = Current();

        if (t.Kind == TokenKind.Sigil)
        {
            return t.Value switch
            {
                "/" => throw new ParseException($"Unexpected section close at position {t.Position}"),
                "&" => ParseAmpersandVariable(),
                ">" => ParsePartial(),
                "=" => ParseSetDelimiter(),
                "<" or "$" => throw new ParseException($"Template inheritance ('{t.Value}') is not yet supported"),
                _ when "# ^ * % ~ ?".Contains(t.Value) => ParseSection(t.Value[0]),
                _ => throw new ParseException($"Unknown sigil '{t.Value}' at position {t.Position}"),
            };
        }

        // No sigil — variable
        var key = ParseKeypath();
        Consume(TokenKind.CloseTag);
        return new VariableNode(key);
    }

    private VariableNode ParseAmpersandVariable()
    {
        Advance(); // consume '&'
        var key = ParseKeypath();
        Consume(TokenKind.CloseTag);
        return new VariableNode(key, Escape: false);
    }

    private PartialNode ParsePartial()
    {
        Advance(); // consume '>'
        if (Current().Kind == TokenKind.Sigil && Current().Value == "*")
            throw new ParseException("Dynamic partials are not yet supported");
        var name = ParseKeypath();
        Consume(TokenKind.CloseTag);
        return new PartialNode(name);
    }

    private SetDelimiterNode ParseSetDelimiter()
    {
        Advance(); // consume '='
        var open = Consume(TokenKind.Delim).Value;
        var close = Consume(TokenKind.Delim).Value;
        Consume(TokenKind.Sigil, "=");
        Consume(TokenKind.CloseTag);
        _openDelim = open;
        _closeDelim = close;
        return new SetDelimiterNode(open, close);
    }

    private SectionNode ParseSection(char sigil)
    {
        Advance(); // consume sigil
        var key = ParseKeypath();

        string? alias = null;
        if (Current().Kind == TokenKind.Colon)
        {
            Advance();
            alias = Consume(TokenKind.Ident).Value;
        }

        var args = new List<Argument>();
        while (Current().Kind is TokenKind.StringLit or TokenKind.NumLit
               or TokenKind.Ident || Current().Kind == TokenKind.Sigil && Current().Value == "*")
        {
            if (Current().Kind == TokenKind.StringLit)
            { args.Add(new StringArgument(Current().Value)); Advance(); }
            else if (Current().Kind == TokenKind.NumLit)
            { args.Add(new NumberArgument(Current().Value)); Advance(); }
            else if (Current().Kind == TokenKind.Ident)
            { args.Add(new StringArgument(Current().Value)); Advance(); }
            else
            {
                Advance();
                args.Add(new VariableArgument(ParseKeypath()));
            }
        }

        Consume(TokenKind.CloseTag);
        int contentStart = Current().Position;
        string openDelim  = _openDelim;
        string closeDelim = _closeDelim;
        var children = ParseNodes();

        if (IsAtEof())
            throw new ParseException($"Unclosed section '{key}'");

        int contentEnd = Current().Position;
        var closeKey = ConsumeCloseTag();
        if (closeKey != key)
            throw new ParseException($"Close tag '{{/{closeKey}}}' does not match opener '{{{sigil}{key}}}'");

        return new SectionNode(sigil, key, alias, args, children, contentStart, contentEnd, openDelim, closeDelim);
    }

    private string ConsumeCloseTag()
    {
        Consume(TokenKind.OpenTag);
        Consume(TokenKind.Sigil, "/");
        var key = ParseKeypath();
        Consume(TokenKind.CloseTag);
        return key;
    }

    private string ParseKeypath()
    {
        var t = Current();

        if (t.Kind == TokenKind.Dot)
        {
            Advance();
            return Current().Kind == TokenKind.Ident ? "." + ParseIdentChain() : ".";
        }

        if (t.Kind == TokenKind.At)
        {
            Advance();
            return "@" + ParseIdentChain();
        }

        if (t.Kind == TokenKind.Ident)
            return ParseIdentChain();

        throw new ParseException($"Expected keypath, got {t.Kind} at position {t.Position}");
    }

    private string ParseIdentChain()
    {
        var parts = new List<string> { Consume(TokenKind.Ident).Value };
        while (Current().Kind == TokenKind.Dot && Peek(1).Kind == TokenKind.Ident)
        {
            Advance(); // consume '.'
            parts.Add(Consume(TokenKind.Ident).Value);
        }
        return string.Join(".", parts);
    }

    private bool IsAtEof() => Current().Kind == TokenKind.Eof;

    private bool IsAtCloseTag() =>
        Current().Kind == TokenKind.OpenTag
        && Peek(1).Kind == TokenKind.Sigil
        && Peek(1).Value == "/";

    private Token Current() => tokens[_pos];

    private Token Peek(int offset) =>
        _pos + offset < tokens.Count ? tokens[_pos + offset] : tokens[^1];

    private void Advance() => _pos++;

    private Token Consume(TokenKind kind)
    {
        var t = Current();
        if (t.Kind != kind)
            throw new ParseException($"Expected {kind}, got {t.Kind} at position {t.Position}");
        Advance();
        return t;
    }

    private Token Consume(TokenKind kind, string value)
    {
        var t = Consume(kind);
        if (t.Value != value)
            throw new ParseException($"Expected {kind}('{value}'), got {kind}('{t.Value}') at position {t.Position}");
        return t;
    }
}
