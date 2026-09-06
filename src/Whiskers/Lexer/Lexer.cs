namespace Whiskers;

public sealed class Lexer(string source, string open = "{{", string close = "}}") 
{
    private int _pos;
    private string _open = open;
    private string _close = close;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < source.Length)
        {
            if (_open == "{{" && Peek("{{{"))
                ReadRaw(tokens);
            else if (Peek(_open))
                ReadTag(tokens);
            else
                ReadText(tokens);
        }

        tokens.Add(new Token(TokenKind.Eof, "", _pos));
        return tokens;
    }

    private void ReadText(List<Token> tokens)
    {
        int start = _pos;
        while (_pos < source.Length
               && !(_open == "{{" && Peek("{{{"))
               && !Peek(_open))
        {
            _pos++;
        }
        if (_pos > start)
            tokens.Add(new Token(TokenKind.Text, source[start.._pos], start));
    }

    private void ReadRaw(List<Token> tokens)
    {
        tokens.Add(new Token(TokenKind.OpenRaw, "{{{", _pos));
        _pos += 3;
        ReadTagContent(tokens, "}}}");
        if (Peek("}}}"))
        {
            tokens.Add(new Token(TokenKind.CloseRaw, "}}}", _pos));
            _pos += 3;
        }
    }

    private void ReadTag(List<Token> tokens)
    {
        int start = _pos;
        _pos += _open.Length;

        // Comment: emit content only, no OpenTag/CloseTag
        if (_pos < source.Length && source[_pos] == '!')
        {
            _pos++;
            int cs = _pos;
            while (_pos < source.Length && !Peek(_close))
                _pos++;
            tokens.Add(new Token(TokenKind.Comment, source[cs.._pos], cs));
            if (Peek(_close)) _pos += _close.Length;
            return;
        }

        tokens.Add(new Token(TokenKind.OpenTag, _open, start));
        SkipWhitespace();

        // Set delimiter: = DELIM DELIM =
        if (_pos < source.Length && source[_pos] == '=')
        {
            tokens.Add(new Token(TokenKind.Sigil, "=", _pos++));
            SkipWhitespace();
            int ds = _pos;
            string newOpen = ReadDelimStr();
            tokens.Add(new Token(TokenKind.Delim, newOpen, ds));
            SkipWhitespace();
            ds = _pos;
            string newClose = ReadDelimStr();
            tokens.Add(new Token(TokenKind.Delim, newClose, ds));
            SkipWhitespace();
            if (_pos < source.Length && source[_pos] == '=')
                tokens.Add(new Token(TokenKind.Sigil, "=", _pos++));
            SkipWhitespace();
            if (Peek(_close))
            {
                tokens.Add(new Token(TokenKind.CloseTag, _close, _pos));
                _pos += _close.Length;
            }
            _open = newOpen;
            _close = newClose;
            return;
        }

        ReadTagContent(tokens, _close);

        if (Peek(_close))
        {
            tokens.Add(new Token(TokenKind.CloseTag, _close, _pos));
            _pos += _close.Length;
        }
    }

    private void ReadTagContent(List<Token> tokens, string closeDelim)
    {
        while (_pos < source.Length && !Peek(closeDelim))
        {
            char c = source[_pos];

            if (char.IsWhiteSpace(c))              { _pos++; continue; }
            if (IsSigilChar(c))                    { tokens.Add(new Token(TokenKind.Sigil,     c.ToString(), _pos++)); continue; }
            if (c == '@')                          { tokens.Add(new Token(TokenKind.At,         "@",          _pos++)); continue; }
            if (c == ':')                          { tokens.Add(new Token(TokenKind.Colon,      ":",          _pos++)); continue; }
            if (c == '.')                          { tokens.Add(new Token(TokenKind.Dot,        ".",          _pos++)); continue; }
            if (c == '"')                          { ReadStringLit(tokens); continue; }
            if (Ascii.IsDigit(c))                   { ReadNumLit(tokens); continue; }
            if (Ascii.IsLetter(c) || c == '_')      { ReadIdent(tokens); continue; }

            _pos++;
        }
    }

    private void ReadIdent(List<Token> tokens)
    {
        int start = _pos;
        while (_pos < source.Length
               && (Ascii.IsLetterOrDigit(source[_pos]) || source[_pos] is '_' or '-'))
        {
            _pos++;
        }
        tokens.Add(new Token(TokenKind.Ident, source[start.._pos], start));
    }

    private void ReadStringLit(List<Token> tokens)
    {
        int start = _pos++;
        while (_pos < source.Length && source[_pos] != '"')
        {
            if (source[_pos] == '\\') _pos++;
            if (_pos < source.Length) _pos++;
        }
        int end = _pos;
        if (_pos < source.Length) _pos++;
        tokens.Add(new Token(TokenKind.StringLit, source[(start + 1)..end], start));
    }

    private void ReadNumLit(List<Token> tokens)
    {
        int start = _pos;
        while (_pos < source.Length && Ascii.IsDigit(source[_pos])) _pos++;
        if (_pos < source.Length && source[_pos] == '.'
            && _pos + 1 < source.Length && Ascii.IsDigit(source[_pos + 1]))
        {
            _pos++;
            while (_pos < source.Length && Ascii.IsDigit(source[_pos])) _pos++;
        }
        tokens.Add(new Token(TokenKind.NumLit, source[start.._pos], start));
    }

    private string ReadDelimStr()
    {
        int start = _pos;
        while (_pos < source.Length && source[_pos] != '=' && !char.IsWhiteSpace(source[_pos]))
            _pos++;
        return source[start.._pos];
    }

    private void SkipWhitespace()
    {
        while (_pos < source.Length && char.IsWhiteSpace(source[_pos]))
            _pos++;
    }

    private bool Peek(string s) =>
        _pos + s.Length <= source.Length
        && string.CompareOrdinal(source, _pos, s, 0, s.Length) == 0;

    private static bool IsSigilChar(char c) =>
        c is '#' or '^' or '*' or '%' or '~' or '?' or '/' or '&' or '>' or '<' or '$';
}
