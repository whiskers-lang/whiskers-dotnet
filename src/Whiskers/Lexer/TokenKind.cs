namespace Whiskers;

public enum TokenKind
{
    Text,
    Comment,
    OpenTag,
    CloseTag,
    OpenRaw,
    CloseRaw,
    Sigil,
    Dot,
    At,
    Ident,
    Colon,
    StringLit,
    NumLit,
    Delim,
    Eof,
}
