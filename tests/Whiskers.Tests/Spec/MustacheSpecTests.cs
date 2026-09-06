using Whiskers;
using YamlDotNet.RepresentationModel;

namespace Whiskers.Tests;

public class MustacheSpecTests
{
    private static readonly string? SpecDir = FindSpecDir();

    private static string? FindSpecDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "mustache-spec");
        return Directory.Exists(path) ? path : null;
    }

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
            var expected = ScalarValue(node, "expected");
            var data     = ConvertNode(GetChild(node, "data"));

            Dictionary<string, string>? partials = null;
            if (TryGetChild(node, "partials", out var pNode))
                partials = ((YamlMappingNode)pNode!).Children.ToDictionary(
                    kv => ((YamlScalarNode)kv.Key).Value!,
                    kv => ((YamlScalarNode)kv.Value).Value!);

            yield return [name, template, data, expected, partials];
        }
    }

    // --- YAML helpers ---

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
            {
                value = child.Value;
                return true;
            }
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

    // --- Test runner ---

    private static void Run(string template, object? data, string expected,
                            Dictionary<string, string>? partials = null)
    {
        IPartialLoader? loader = partials != null ? new DictLoader(partials) : null;
        var tokens = new Lexer(template).Tokenize();
        var ast    = new Parser(tokens, template).Parse();
        var result = new Renderer(loader).Render(ast, data);
        Assert.Equal(expected, result);
    }

    private sealed class DictLoader(Dictionary<string, string> map) : IPartialLoader
    {
        public string? Load(string name) => map.TryGetValue(name, out var v) ? v : null;
    }

    // --- Spec categories (one Theory per file) ---

    public static IEnumerable<object?[]> Interpolation_Data => LoadSpec("interpolation.yml");
    public static IEnumerable<object?[]> Sections_Data      => LoadSpec("sections.yml");
    public static IEnumerable<object?[]> Inverted_Data      => LoadSpec("inverted.yml");
    public static IEnumerable<object?[]> Comments_Data      => LoadSpec("comments.yml");
    public static IEnumerable<object?[]> Partials_Data      => LoadSpec("partials.yml");
    public static IEnumerable<object?[]> Delimiters_Data    => LoadSpec("delimiters.yml");

    [Theory, MemberData(nameof(Interpolation_Data))]
    public void Interpolation(string name, string template, object? data, string expected, Dictionary<string, string>? partials)
        => Run(template, data, expected, partials);

    [Theory, MemberData(nameof(Sections_Data))]
    public void Sections(string name, string template, object? data, string expected, Dictionary<string, string>? partials)
        => Run(template, data, expected, partials);

    [Theory, MemberData(nameof(Inverted_Data))]
    public void Inverted(string name, string template, object? data, string expected, Dictionary<string, string>? partials)
        => Run(template, data, expected, partials);

    [Theory, MemberData(nameof(Comments_Data))]
    public void Comments(string name, string template, object? data, string expected, Dictionary<string, string>? partials)
        => Run(template, data, expected, partials);

    [Theory, MemberData(nameof(Partials_Data))]
    public void Partials(string name, string template, object? data, string expected, Dictionary<string, string>? partials)
        => Run(template, data, expected, partials);

    [Theory, MemberData(nameof(Delimiters_Data))]
    public void Delimiters(string name, string template, object? data, string expected, Dictionary<string, string>? partials)
        => Run(template, data, expected, partials);
}
