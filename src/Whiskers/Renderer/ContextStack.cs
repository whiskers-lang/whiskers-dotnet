using System.Collections;
using System.Reflection;

namespace Whiskers;

internal sealed class ContextStack
{
    private readonly List<Frame> _frames;

    internal ContextStack(object? root)
    {
        _frames = [new Frame(root, null, null)];
    }

    internal object? Root    => _frames[0].Value;
    internal object? Current => _frames[^1].Value;

    // Walks past primitive frames to the nearest dictionary/object for context-aware lambdas.
    internal object? NearestObject
    {
        get
        {
            for (int i = _frames.Count - 1; i >= 0; i--)
            {
                var v = _frames[i].Value;
                if (v is not (ValueType or string))
                    return v;
            }
            return _frames[^1].Value;
        }
    }

    internal void Push(object? value, string? alias = null, Dictionary<string, object?>? metadata = null)
        => _frames.Add(new Frame(value, alias, metadata));

    internal void Pop() => _frames.RemoveAt(_frames.Count - 1);

    internal bool TryResolve(string key, out object? value)
    {
        value = null;

        if (key == ".")           { value = _frames[^1].Value; return true; }
        if (key == "@root")       { value = Root; return true; }
        if (key.StartsWith("@root."))
            return TryResolvePath(Root, key["@root.".Length..], out value);

        // @metadata variables (index, number, first, etc.)
        if (key.StartsWith("@"))
        {
            var metaKey = key[1..];
            var metadataDot = metaKey.IndexOf('.');
            if (metadataDot >= 0)
            {
                var alias = metaKey[..metadataDot];
                metaKey = metaKey[(metadataDot + 1)..];
                for (int i = _frames.Count - 1; i >= 0; i--)
                {
                    if (_frames[i].Alias != alias) continue;
                    return _frames[i].Metadata?.TryGetValue(metaKey, out value) == true;
                }
                return false;
            }
            for (int i = _frames.Count - 1; i >= 0; i--)
                if (_frames[i].Metadata?.TryGetValue(metaKey, out value) == true)
                    return true;
            return false;
        }

        // .name — local lookup in current frame only
        if (key.StartsWith("."))
            return TryResolvePath(_frames[^1].Value, key[1..], out value);

        // alias.rest — named scope lookup takes priority over data keys
        var dot = key.IndexOf('.');
        var firstSeg = dot < 0 ? key : key[..dot];
        var rest = dot < 0 ? null : key[(dot + 1)..];
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (_frames[i].Alias != firstSeg) continue;
            if (rest == null) { value = _frames[i].Value; return true; }
            return TryResolvePath(_frames[i].Value, rest, out value);
        }

        // Normal walk: for dotted paths, find the first frame containing the first segment,
        // then resolve the remainder strictly within that frame's result (no cross-frame fallback).
        if (rest != null)
        {
            for (int i = _frames.Count - 1; i >= 0; i--)
                if (TryGetProperty(_frames[i].Value, firstSeg, out var headVal))
                    return TryResolvePath(headVal, rest, out value);
            return false;
        }

        for (int i = _frames.Count - 1; i >= 0; i--)
            if (TryGetProperty(_frames[i].Value, key, out value))
                return true;

        return false;
    }

    private static bool TryResolvePath(object? obj, string path, out object? value)
    {
        value = obj;
        foreach (var seg in path.Split('.'))
            if (!TryGetProperty(value, seg, out value))
                return false;
        return true;
    }

    private static bool TryGetProperty(object? obj, string key, out object? value)
    {
        value = null;
        if (obj == null) return false;

        if (obj is IDictionary<string, object?> dict)
            return dict.TryGetValue(key, out value);

        // Covers Dictionary<string, string>, Dictionary<string, int>, etc.
        if (obj is IDictionary nonGenericDict)
        {
            if (!nonGenericDict.Contains(key)) return false;
            value = nonGenericDict[key];
            return true;
        }

        var prop = obj.GetType().GetProperty(key,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) return false;
        value = prop.GetValue(obj);
        return true;
    }

    private readonly record struct Frame(object? Value, string? Alias, Dictionary<string, object?>? Metadata);
}
