namespace Whiskers;

public abstract record Argument;
public sealed record StringArgument(string Value) : Argument;
public sealed record NumberArgument(string Value) : Argument;
public sealed record VariableArgument(string Key) : Argument;
