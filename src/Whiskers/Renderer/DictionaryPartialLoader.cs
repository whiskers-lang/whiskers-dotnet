namespace Whiskers;

/// <summary>Loads partial template source from a supplied dictionary.</summary>
/// <remarks>
/// Retains the dictionary and its lookup semantics without copying or caching.
/// Changes between renders are visible. Callers must not mutate an ordinary
/// dictionary concurrently with rendering.
/// </remarks>
public sealed class DictionaryPartialLoader : IPartialLoader
{
    private readonly IDictionary<string, string> _templates;

    public DictionaryPartialLoader(IDictionary<string, string> templates)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    public string? Load(string name) =>
        _templates.TryGetValue(name, out var template) ? template : null;
}
