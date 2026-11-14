# Whiskers for .NET

The C# implementation of [Whiskers][whiskers-lang] — a strict superset of
[Mustache][mustache] with named scopes, presence and null checks,
iteration metadata, lambda arguments, and strict mode.

Targets `netstandard2.0`, ships as a single dependency-free DLL, and runs
on .NET Framework 4.6.1+, all versions of .NET Core, .NET 5+, Mono,
Xamarin, and Unity.

## Install

```
dotnet add package Whiskers
```

## Usage

```csharp
using Whiskers;

var output = Renderer.Render("Hello, {{name}}!", new { name = "world" });
// → "Hello, world!"
```

For sections, partials, aliases, lambdas, and every other language
feature, see the [specification][spec].

### Dictionary partials

Use `DictionaryPartialLoader` for partial templates already stored in memory:

```csharp
var templates = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["greeting"] = "Hello {{name}}!",
};
var loader = new DictionaryPartialLoader(templates);
var output = Renderer.Render(
    "{{> greeting}}",
    new Dictionary<string, object> { ["name"] = "World" },
    loader);
// Hello World!
```

In PowerShell, after loading `Whiskers.dll` with `Add-Type -Path`:

```powershell
$templates = [System.Collections.Generic.Dictionary[string,string]]::new()
$templates.Add('greeting', 'Hello {{name}}!')
$loader = [Whiskers.DictionaryPartialLoader]::new($templates)
$output = [Whiskers.Renderer]::Render(
    '{{> greeting}}',
    @{ name = 'World' },
    $loader
)
# Hello World!
```

The loader preserves the dictionary's comparer and returns the stored source
unchanged. Missing partials return `null` and render nothing; empty partials
remain empty strings. Partials can include other dictionary partials, with the
renderer retaining responsibility for parsing, standalone indentation, and
recursion limits.

The dictionary is retained, not copied or cached, so changes between renders
are visible. This is not a thread-safety guarantee: do not mutate an ordinary
dictionary concurrently with rendering. A null dictionary throws
`ArgumentNullException`. Continue implementing `IPartialLoader` for custom
storage or resolution strategies.

### Whitespace

Standalone section, comment, delimiter-change, and partial tags consume their
own lines, including indentation and the line ending. Consecutive standalone
tags follow the same rules. Other content keeps its whitespace and line endings.

Section lambda bodies exclude the leading line ending and closing-tag
indentation consumed by standalone processing. Their results and templates
passed to render callbacks follow the same standalone rules as ordinary
templates; internal template syntax and intentional blank lines are preserved.

## Repository layout

```
whiskers-dotnet/
├── src/Whiskers/          library source
├── tests/Whiskers.Tests/  xUnit tests
├── spec/                  submodule → whiskers-lang/spec (conformance fixtures)
└── mustache-spec/         submodule → mustache/spec (Mustache compatibility)
```

## Building

```
git clone --recurse-submodules https://github.com/whiskers-lang/whiskers-dotnet
cd whiskers-dotnet
dotnet build
dotnet test
```

If you cloned without `--recurse-submodules`:

```
git submodule update --init --recursive
```

## Contributing

See [CONTRIBUTING][contributing] at the org level. New language features
belong in [`whiskers-lang/spec`][spec] first — the implementation follows
the spec.

## License

MIT. See [`LICENSE`](LICENSE).

[whiskers-lang]: https://github.com/whiskers-lang
[mustache]: https://mustache.github.io/
[spec]: https://github.com/whiskers-lang/spec
[contributing]: https://github.com/whiskers-lang/.github/blob/main/CONTRIBUTING.md
