namespace Whiskers;

public sealed record RenderOptions(
    bool StrictMode = false,
    bool SkipHtmlEncoding = false,
    int MaxRecursionDepth = 256);
