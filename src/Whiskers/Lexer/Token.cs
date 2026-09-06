namespace Whiskers;

public readonly record struct Token(TokenKind Kind, string Value, int Position);
