using System.Collections;
using System.Globalization;
using System.Text;

namespace Whiskers;

public sealed class Renderer(IPartialLoader? partials = null, RenderOptions? options = null)
{
    private readonly RenderOptions _options = options ?? new RenderOptions();
    private bool _strictMode;
    private int _partialDepth;

    public static string Render(string template, object? data, IPartialLoader? partials = null)
    {
        var tokens = new Lexer(template).Tokenize();
        var ast = new Parser(tokens, template).Parse();
        return new Renderer(partials).Render(ast, data);
    }

    public string Render(TemplateNode template, object? data)
    {
        _strictMode = _options.StrictMode;
        var sb = new StringBuilder();
        var stack = new ContextStack(data);
        var stripped = StripStandalone(template.Children);
        RenderNodes(stripped, stack, sb, template.Source);
        return sb.ToString();
    }

    private void RenderNodes(IReadOnlyList<INode> nodes, ContextStack stack, StringBuilder sb, string source)
    {
        foreach (var node in nodes)
            RenderNode(node, stack, sb, source);
    }

    private void RenderNode(INode node, ContextStack stack, StringBuilder sb, string source)
    {
        switch (node)
        {
            case TextNode t:       sb.Append(t.Content); break;
            case CommentNode:      break;
            case SetDelimiterNode: break;
            case VariableNode v:   RenderVariable(v, stack, sb); break;
            case SectionNode s:    RenderSection(s, stack, sb, source); break;
            case PartialNode p:    RenderPartial(p, stack, sb); break;
        }
    }

    private void RenderVariable(VariableNode node, ContextStack stack, StringBuilder sb)
    {
        if (!stack.TryResolve(node.Key, out var value))
        {
            if (_strictMode) throw new RenderException($"Key '{node.Key}' not found");
            return;
        }
        if (value is Func<object> noArgLambda) value = noArgLambda();
        var str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var escape = node.Escape && !_options.SkipHtmlEncoding;
        sb.Append(escape ? HtmlEscape(str) : str);
    }

    private void RenderSection(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        // {{#@strict}}...{{/@strict}} — enable strict mode for the block
        if (node.Key == "@strict" && node.Sigil == '#')
        {
            bool prev = _strictMode;
            _strictMode = true;
            RenderNodes(node.Children, stack, sb, source);
            _strictMode = prev;
            return;
        }

        switch (node.Sigil)
        {
            case '#': RenderTruthy(node, stack, sb, source);   break;
            case '^': RenderFalsy(node, stack, sb, source);    break;
            case '*': RenderExists(node, stack, sb, source);   break;
            case '%': RenderAbsent(node, stack, sb, source);   break;
            case '~': RenderNotNull(node, stack, sb, source);  break;
            case '?': RenderNull(node, stack, sb, source);     break;
        }
    }

    private void RenderTruthy(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        if (!stack.TryResolve(node.Key, out var value))
        {
            if (_strictMode) throw new RenderException($"Key '{node.Key}' not found");
            return;
        }
        if (IsFalsy(value)) return;

        if (node.Arguments.Count > 0)
        {
            var args = ConvertArgs(node.Arguments, stack);
            var raw = node.ContentEnd > node.ContentStart
                ? source[node.ContentStart..node.ContentEnd] : "";
            string result;
            if (value is Func<object?[], string, Func<string, string>, object> rcLambda)
            {
                Func<string, string> renderFn = tmpl =>
                {
                    var rSb = new StringBuilder();
                    var rAst = new Parser(new Lexer(tmpl, node.ContentOpenDelim, node.ContentCloseDelim).Tokenize(), tmpl).Parse();
                    RenderNodes(rAst.Children, stack, rSb, tmpl);
                    return rSb.ToString();
                };
                result = Convert.ToString(rcLambda(args, raw, renderFn), CultureInfo.InvariantCulture) ?? "";
            }
            else if (value is Func<object?[], string, string> strLambda)
            {
                result = strLambda(args, raw) ?? "";
            }
            else if (value is Func<object?[], string, object> objLambda)
            {
                result = Convert.ToString(objLambda(args, raw), CultureInfo.InvariantCulture) ?? "";
            }
            else
            {
                throw new RenderException($"Key '{node.Key}' does not accept arguments");
            }
            var reTokens = new Lexer(result).Tokenize();
            var reAst = new Parser(reTokens, result).Parse();
            RenderNodes(reAst.Children, stack, sb, result);
            return;
        }

        if (value is Func<string, string>
                 or Func<string, object>
                 or Func<object?, string, object>
                 or Func<string, Func<string, string>, object>)
        {
            var raw = node.ContentEnd > node.ContentStart
                ? source[node.ContentStart..node.ContentEnd]
                : "";
            string result;
            switch (value)
            {
                case Func<string, Func<string, string>, object> rcLambda:
                    Func<string, string> renderFn = tmpl =>
                    {
                        var rSb = new StringBuilder();
                        var rAst = new Parser(new Lexer(tmpl, node.ContentOpenDelim, node.ContentCloseDelim).Tokenize(), tmpl).Parse();
                        RenderNodes(rAst.Children, stack, rSb, tmpl);
                        return rSb.ToString();
                    };
                    result = Convert.ToString(rcLambda(raw, renderFn), CultureInfo.InvariantCulture) ?? "";
                    break;
                case Func<object?, string, object> ctxLambda:
                    result = Convert.ToString(ctxLambda(stack.NearestObject, raw), CultureInfo.InvariantCulture) ?? "";
                    break;
                case Func<string, object> objLambda:
                    result = Convert.ToString(objLambda(raw), CultureInfo.InvariantCulture) ?? "";
                    break;
                default: // Func<string, string>
                    result = ((Func<string, string>)value)(raw);
                    break;
            }
            var reTokens = new Lexer(result).Tokenize();
            var reAst = new Parser(reTokens, result).Parse();
            RenderNodes(reAst.Children, stack, sb, result);
            return;
        }

        // Iterate arrays/lists; treat dicts and scalars as single-context push
        if (value is IEnumerable seq && value is not string && value is not IDictionary)
        {
            var items = seq.Cast<object?>().ToList();
            if (items.Count == 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                stack.Push(items[i], node.Alias, BuildMeta(i, items.Count));
                RenderNodes(node.Children, stack, sb, source);
                stack.Pop();
            }
            return;
        }

        stack.Push(value, node.Alias);
        RenderNodes(node.Children, stack, sb, source);
        stack.Pop();
    }

    private void RenderFalsy(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        if (!stack.TryResolve(node.Key, out var value))
        {
            if (_strictMode) throw new RenderException($"Key '{node.Key}' not found");
            RenderNodes(node.Children, stack, sb, source); // absent = falsy in non-strict
            return;
        }
        if (IsFalsy(value))
            RenderNodes(node.Children, stack, sb, source);
    }

    // * and % are exempt from strict mode — no throw on absent key
    private void RenderExists(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        if (stack.TryResolve(node.Key, out var value))
        {
            stack.Push(value, node.Alias);
            RenderNodes(node.Children, stack, sb, source);
            stack.Pop();
        }
    }

    private void RenderAbsent(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        if (!stack.TryResolve(node.Key, out _))
            RenderNodes(node.Children, stack, sb, source);
    }

    private void RenderNotNull(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        if (!stack.TryResolve(node.Key, out var value))
        {
            if (_strictMode) throw new RenderException($"Key '{node.Key}' not found");
            return;
        }
        if (value != null)
        {
            stack.Push(value, node.Alias);
            RenderNodes(node.Children, stack, sb, source);
            stack.Pop();
        }
    }

    private void RenderNull(SectionNode node, ContextStack stack, StringBuilder sb, string source)
    {
        if (!stack.TryResolve(node.Key, out var value))
        {
            if (_strictMode) throw new RenderException($"Key '{node.Key}' not found");
            return;
        }
        if (value == null)
            RenderNodes(node.Children, stack, sb, source);
    }

    private void RenderPartial(PartialNode node, ContextStack stack, StringBuilder sb)
    {
        if (partials == null) return;
        var src = partials.Load(node.Name);
        if (src == null) return;
        if (_partialDepth >= _options.MaxRecursionDepth)
            throw new RenderException($"Max partial recursion depth ({_options.MaxRecursionDepth}) exceeded");
        if (node.Indent.Length > 0)
            src = ApplyPartialIndent(src, node.Indent);
        var tokens = new Lexer(src).Tokenize();
        var tmpl = new Parser(tokens, src).Parse();
        var stripped = StripStandalone(tmpl.Children);
        _partialDepth++;
        RenderNodes(stripped, stack, sb, src);
        _partialDepth--;
    }

    // Prepend 'indent' to first line and after each non-terminal newline in a partial source.
    private static string ApplyPartialIndent(string src, string indent)
    {
        var sb = new StringBuilder(src.Length + indent.Length * 8);
        sb.Append(indent);
        for (int i = 0; i < src.Length; i++)
        {
            sb.Append(src[i]);
            if (src[i] == '\n' && i < src.Length - 1)
                sb.Append(indent);
        }
        return sb.ToString();
    }

    // ── Standalone tag stripping ──────────────────────────────────────────────

    // Pre-process a node list to strip whitespace/newlines around standalone tags.
    // Standalone-capable: CommentNode, SetDelimiterNode, PartialNode, SectionNode open/close.
    private static List<INode> StripStandalone(IReadOnlyList<INode> nodes, bool firstLineContinued = false)
    {
        var list = nodes.ToList();

        for (int i = 0; i < list.Count; i++)
        {
            switch (list[i])
            {
                case CommentNode or SetDelimiterNode:
                    if (CheckBefore(list, i, out _, out var trimTo, firstLineContinued)
                     && CheckAfter(list, i, out var trimFrom))
                    {
                        ApplyBeforeTrim(list, i, trimTo);
                        ApplyAfterTrim(list, i, trimFrom);
                    }
                    break;

                case PartialNode pn:
                    if (CheckBefore(list, i, out var indent, out var pTrimTo, firstLineContinued)
                     && CheckAfter(list, i, out var pTrimFrom))
                    {
                        ApplyBeforeTrim(list, i, pTrimTo);
                        ApplyAfterTrim(list, i, pTrimFrom);
                        if (indent.Length > 0)
                            list[i] = new PartialNode(pn.Name, indent);
                    }
                    break;

                case SectionNode sn:
                {
                    var sc = sn.Children.ToList();

                    // Section OPEN standalone: parent-before + first-child
                    bool openStandalone = false;
                    int contentStartAdjust = 0;
                    if (CheckBefore(list, i, out _, out var openBefore, firstLineContinued)
                     && CheckAfterFirst(sc, out var openAfter))
                    {
                        openStandalone = true;
                        ApplyBeforeTrim(list, i, openBefore);
                        ApplyAfterFirst(sc, openAfter);
                        contentStartAdjust = openAfter ?? 0;
                    }

                    // Section CLOSE standalone: last-child + parent-after
                    if (CheckBeforeLast(sc, out var closeBefore)
                     && CheckAfter(list, i, out var closeAfter))
                    {
                        ApplyBeforeLast(sc, closeBefore);
                        ApplyAfterTrim(list, i, closeAfter);
                    }

                    var stripped = sn with
                    {
                        ContentStart = sn.ContentStart + contentStartAdjust,
                        Children     = StripStandalone(sc, !openStandalone),
                    };
                    list[i] = stripped;
                    break;
                }
            }
        }

        return list;
    }

    // True if only spaces/tabs precede the tag on its line (or it's at the start of input).
    private static bool CheckBefore(List<INode> list, int i, out string indent, out int? trimTo, bool firstLineContinued = false)
    {
        indent = "";
        trimTo = null;
        if (i == 0) return !firstLineContinued;
        if (list[i - 1] is not TextNode prev) return false;
        var text = prev.Content;
        int nl = text.LastIndexOf('\n');
        string suffix = nl >= 0 ? text[(nl + 1)..] : text;
        if (!suffix.All(c => c == ' ' || c == '\t')) return false;
        // If there's no newline in the preceding text, we're on the same line as everything before it.
        // Any node before the text node, or a continued parent line, means not standalone.
        if (nl < 0 && (firstLineContinued || (i >= 2 && text.Length > 0))) return false;
        indent = suffix;
        trimTo = nl >= 0 ? nl + 1 : 0;
        return true;
    }

    // True if a newline (or end of input) follows the tag.
    private static bool CheckAfter(List<INode> list, int i, out int? trimFrom)
    {
        trimFrom = null;
        if (i >= list.Count - 1) return true;
        if (list[i + 1] is not TextNode next) return false;
        var text = next.Content;
        if (text.StartsWith("\r\n")) { trimFrom = 2; return true; }
        if (text.StartsWith("\n"))   { trimFrom = 1; return true; }
        return false;
    }

    // True if the first child of a section starts with a newline.
    private static bool CheckAfterFirst(List<INode> sc, out int? trimFrom)
    {
        trimFrom = null;
        if (sc.Count == 0) return true;
        if (sc[0] is not TextNode first) return false;
        if (first.Content.StartsWith("\r\n")) { trimFrom = 2; return true; }
        if (first.Content.StartsWith("\n"))   { trimFrom = 1; return true; }
        return false;
    }

    // True if the last child of a section has only spaces/tabs after its last newline.
    private static bool CheckBeforeLast(List<INode> sc, out int? trimTo)
    {
        trimTo = null;
        if (sc.Count == 0) return true;
        if (sc[^1] is not TextNode last) return false;
        var text = last.Content;
        int nl = text.LastIndexOf('\n');
        string suffix = nl >= 0 ? text[(nl + 1)..] : text;
        if (!suffix.All(c => c == ' ' || c == '\t')) return false;
        // No newline and preceding nodes on the same line → close tag is not standalone.
        if (nl < 0 && sc.Count > 1) return false;
        trimTo = nl >= 0 ? nl + 1 : 0;
        return true;
    }

    private static void ApplyBeforeTrim(List<INode> list, int i, int? trimTo)
    {
        if (trimTo.HasValue && i > 0 && list[i - 1] is TextNode prev)
            list[i - 1] = prev with { Content = prev.Content[..trimTo.Value] };
    }

    private static void ApplyAfterTrim(List<INode> list, int i, int? trimFrom)
    {
        if (trimFrom.HasValue && i < list.Count - 1 && list[i + 1] is TextNode next)
            list[i + 1] = next with { Content = next.Content[trimFrom.Value..] };
    }

    private static void ApplyAfterFirst(List<INode> sc, int? trimFrom)
    {
        if (trimFrom.HasValue && sc.Count > 0 && sc[0] is TextNode first)
            sc[0] = first with { Content = first.Content[trimFrom.Value..] };
    }

    private static void ApplyBeforeLast(List<INode> sc, int? trimTo)
    {
        if (trimTo.HasValue && sc.Count > 0 && sc[^1] is TextNode last)
            sc[^1] = last with { Content = last.Content[..trimTo.Value] };
    }

    private static Dictionary<string, object?> BuildMeta(int i, int total) => new()
    {
        ["index"]  = (object)i,
        ["number"] = i + 1,
        ["first"]  = i == 0,
        ["last"]   = i == total - 1,
        ["length"] = total,
    };

    private static object?[] ConvertArgs(IReadOnlyList<Argument> args, ContextStack stack) =>
        args.Select(a => a switch
        {
            StringArgument s   => (object?)s.Value,
            NumberArgument n   => int.TryParse(n.Value, out var i)
                                  ? (object?)i
                                  : (object?)double.Parse(n.Value, CultureInfo.InvariantCulture),
            VariableArgument v => stack.TryResolve(v.Key, out var val) ? val : null,
            _ => throw new RenderException($"Unknown argument type '{a.GetType().Name}'")
        }).ToArray();

    private static bool IsFalsy(object? v) =>
        v is null or false
        || (v is IEnumerable e && v is not string && v is not IDictionary
            && !e.Cast<object?>().Any());

    private static string HtmlEscape(string s) => s
        .Replace("&",  "&amp;")
        .Replace("<",  "&lt;")
        .Replace(">",  "&gt;")
        .Replace("\"", "&quot;");
}
