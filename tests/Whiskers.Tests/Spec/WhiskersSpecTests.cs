using Whiskers;
using YamlDotNet.RepresentationModel;

namespace Whiskers.Tests;

public class WhiskersSpecTests
{
    private static readonly string? SpecDir = FindSpecDir();

    private static string? FindSpecDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "whiskers-spec");
        return Directory.Exists(path) ? path : null;
    }

    // Named lambda fixtures referenced by name in YAML test cases.
    private static readonly Dictionary<string, object> LambdaFixtures = new()
    {
        ["identity"]     = (Func<string, string>)(s => s),
        ["bold"]         = (Func<string, string>)(s => $"<b>{s}</b>"),
        ["upcase"]       = (Func<string, object>)(s => (object)s.ToUpper()),
        ["const-hello"]  = (Func<object>)(() => (object)"hello"),
        ["bold-tag"]     = (Func<object>)(() => (object)"<b>"),
        ["context-name"] = (Func<object, string, object>)((ctx, s) =>
        {
            var name = ctx is Dictionary<string, object?> d
                && d.TryGetValue("name", out var v) ? v?.ToString() ?? "" : "";
            return (object)$"{name}: {s}";
        }),
        ["render-bold"]  = (Func<string, Func<string, string>, object>)((s, render) =>
            (object)$"<b>{render(s)}</b>"),
        // returns a Mustache snippet; used to verify re-rendering picks up context
        ["to-name"]      = (Func<string, object>)(_ => (object)"{{name}}"),
        ["truncate"]     = (Func<object[], string, Func<string, string>, object>)((args, raw, render) =>
        {
            int limit = (int)args[0];
            var rendered = render(raw);
            return rendered.Length > limit ? (object)(rendered[..limit] + "\u2026") : (object)rendered;
        }),
        ["wrap-tag"]     = (Func<object[], string, string>)((args, raw) =>
            $"<{args[0]}>{raw}</{args[0]}>"),
    };

    private static IEnumerable<object?[]> LoadSpec(string file)
    {
        if (SpecDir == null) yield break;
        var path = Path.Combine(SpecDir, file);
        if (!File.Exists(path)) yield break;

        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(path)));
        var root  = (YamlMappingNode)yaml.Documents[0].RootNode;
        var tests = (YamlSequenceNode)GetChild(root, "tests");

        foreach (var node in tests.Children.Cast<YamlMappingNode>())
        {
            var name     = ScalarValue(node, "name");
            var template = ScalarValue(node, "template");
            var expected = TryGetChild(node, "expected", out var expNode)
                           ? ((YamlScalarNode)expNode!).Value ?? "" : "";
            var throws   = TryGetChild(node, "throws", out var throwNode)
                           && throwNode is YamlScalarNode { Value: "true" };

            // Build data dict from optional 'data' key.
            Dictionary<string, object?> data;
            if (TryGetChild(node, "data", out var dataNode)
                && dataNode is YamlMappingNode dm
                && ConvertNode(dm) is Dictionary<string, object?> converted)
                data = converted;
            else
                data = [];

            // Merge named lambda fixtures from optional 'lambdas' key.
            if (TryGetChild(node, "lambdas", out var lambdaNode) && lambdaNode is YamlMappingNode lm)
                foreach (var kv in lm.Children)
                {
                    var key     = ((YamlScalarNode)kv.Key).Value!;
                    var fixture = ((YamlScalarNode)kv.Value).Value!;
                    data[key]   = LambdaFixtures[fixture];
                }

            yield return [name, template, data, expected, throws];
        }
    }

    // ── YAML helpers ──────────────────────────────────────────────────────────

    private static YamlNode GetChild(YamlMappingNode node, string key)
    {
        foreach (var child in node.Children)
            if (child.Key is YamlScalarNode s && s.Value == key)
                return child.Value;
        throw new KeyNotFoundException($"Key '{key}' not found");
    }

    private static bool TryGetChild(YamlMappingNode node, string key, out YamlNode? value)
    {
        foreach (var child in node.Children)
            if (child.Key is YamlScalarNode s && s.Value == key)
            { value = child.Value; return true; }
        value = null;
        return false;
    }

    private static string ScalarValue(YamlMappingNode node, string key) =>
        ((YamlScalarNode)GetChild(node, key)).Value ?? "";

    private static object? ConvertNode(YamlNode node) => node switch
    {
        YamlScalarNode  s => ParseScalar(s.Value),
        YamlMappingNode m => m.Children.ToDictionary(
                                 kv => ((YamlScalarNode)kv.Key).Value!,
                                 kv => ConvertNode(kv.Value)),
        YamlSequenceNode q => q.Children.Select(ConvertNode).ToArray(),
        _ => null,
    };

    private static object? ParseScalar(string? v)
    {
        if (v is null or "~" or "null") return null;
        if (v == "true")  return (object)true;
        if (v == "false") return (object)false;
        if (int.TryParse(v, out var i)) return (object)i;
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return (object)d;
        return v;
    }

    // ── Test runner ───────────────────────────────────────────────────────────

    private static void Run(string template, object? data, string expected, bool expectThrow = false)
    {
        var tokens = new Lexer(template).Tokenize();
        var ast    = new Parser(tokens, template).Parse();
        if (expectThrow)
        {
            Assert.Throws<RenderException>(() => new Renderer().Render(ast, data));
            return;
        }
        var result = new Renderer().Render(ast, data);
        Assert.Equal(expected, result);
    }

    // ── Spec categories ───────────────────────────────────────────────────────

    public static IEnumerable<object?[]> Sigils_Data      => LoadSpec("sigils.yml");
    public static IEnumerable<object?[]> Aliases_Data     => LoadSpec("aliases.yml");
    public static IEnumerable<object?[]> LocalLookup_Data => LoadSpec("~local-lookup.yml");
    public static IEnumerable<object?[]> Metadata_Data    => LoadSpec("~metadata.yml");
    // Optional modules (~prefix) — implementations may omit these.
    public static IEnumerable<object?[]> Lambdas_Data     => LoadSpec("~lambdas.yml");
    public static IEnumerable<object?[]> Strict_Data      => LoadSpec("~strict.yml");

    [Theory, MemberData(nameof(Sigils_Data))]
    public void Sigils(string name, string template, object? data, string expected, bool throws)
        => Run(template, data, expected, throws);

    [Theory, MemberData(nameof(Aliases_Data))]
    public void Aliases(string name, string template, object? data, string expected, bool throws)
        => Run(template, data, expected, throws);

    [Theory, MemberData(nameof(LocalLookup_Data))]
    public void LocalLookup(string name, string template, object? data, string expected, bool throws)
        => Run(template, data, expected, throws);

    [Theory, MemberData(nameof(Metadata_Data))]
    public void Metadata(string name, string template, object? data, string expected, bool throws)
        => Run(template, data, expected, throws);

    [Theory, MemberData(nameof(Lambdas_Data))]
    public void Lambdas(string name, string template, object? data, string expected, bool throws)
        => Run(template, data, expected, throws);

    [Theory, MemberData(nameof(Strict_Data))]
    public void Strict(string name, string template, object? data, string expected, bool throws)
        => Run(template, data, expected, throws);
}
