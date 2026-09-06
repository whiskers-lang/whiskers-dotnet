namespace Whiskers;

public interface INode { }

public sealed record TextNode(string Content) : INode;
public sealed record CommentNode(string Content) : INode;

// Escape=true for {{name}}, false for {{{name}}} and {{& name}}
public sealed record VariableNode(string Key, bool Escape = true) : INode;
public sealed record PartialNode(string Name, string Indent = "") : INode;
public sealed record SetDelimiterNode(string Open, string Close) : INode;

public sealed record SectionNode(
    char Sigil,
    string Key,
    string? Alias,
    IReadOnlyList<Argument> Arguments,
    IReadOnlyList<INode> Children,
    int ContentStart,
    int ContentEnd,
    string ContentOpenDelim = "{{",
    string ContentCloseDelim = "}}") : INode;

public sealed record TemplateNode(IReadOnlyList<INode> Children, string Source = "");
