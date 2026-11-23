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

### Starting scope

Pass `startingPath` to render from part of the original data:

```csharp
var data = new
{
    company = new
    {
        name = "Example",
        departments = new { engineering = new { location = "London" } }
    }
};
var output = Renderer.Render(
    "{{location}} - {{name}} - {{@root.company.name}}",
    data,
    startingPath: "company.departments.engineering");
// London - Example - Example
```

The instance `Render(TemplateNode, object?, string? startingPath = null)` API
accepts the same selection. Each property or indexed element becomes a scope
frame, so ordinary lookup searches the selected value and then its ancestors.
Local `.name` lookup stays within the selected value, and `@root` remains the
original data. Partials and lambda render callbacks inherit this scope stack.

Starting paths support dotted properties, zero-based array/list indices such as
`company.departments[0]`, and quoted keys such as `company["department.name"]`.
Both single and double quotes are supported; escape the enclosing quote or a
backslash with a backslash. Quoted keys can be empty or contain dots and brackets.
Root indices and quoted keys, such as `[0].name` and `["company.name"]`, work too.
Indexing requires an `IList` (including ordinary one-dimensional, zero-based
arrays); arbitrary enumerable sequences are not enumerated for selection.
This syntax is for starting paths only, not template expressions.

Navigation always follows the specified path from the root, without ancestor
fallback or invoking lambdas. Empty or malformed paths, missing properties,
out-of-range indices, and traversal through null throw `RenderException`,
regardless of strict mode. Selecting an existing null value is valid.
Omitting `startingPath` or passing null preserves normal rendering.

Selecting a collection does not iterate it or create iteration metadata.
Use `{{#.}}{{@index}}: {{.}}{{/.}}` to iterate the selected collection normally.
Selection does not mutate data or add wrapper text or whitespace.

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

### Value lambdas in normal sections

The interpolation delegate signatures `Func<object>` (no arguments) and
`Func<object?[], object>` (template arguments) also work in `#` sections:

```csharp
var data = new Dictionary<string, object?>
{
    ["heading"] = (Func<object>)(() => "Report"),
    ["choose"] = (Func<object?[], object?>)(args => args[0]),
};
var output = Renderer.Render(
    "{{#heading}}Title: {{.}}{{/heading}} | {{#choose \"World\"}}Hello {{.}}!{{/choose}}",
    data);
// Title: Report | Hello World!
```

A value lambda is invoked exactly once per section occurrence, even for an empty
body. The result follows ordinary section rules: null, false, and empty arrays
skip the body; non-empty arrays iterate with aliases and metadata; other values
become the body context with parent lookup still available. Empty strings, zero,
and empty dictionaries remain truthy.

The result is data, not template source: returned `{{tags}}` remain literal when
interpolated through `{{.}}`, with normal HTML escaping. Section evaluation does
not recursively invoke a returned delegate. Arguments use the same matching and
conversion as interpolation.

Raw-body section delegates retain their existing raw-text/render-callback
contracts and template re-rendering behavior. Only `#` invokes value lambdas;
inverted, presence, and nullness checks continue inspecting the function itself.

### Whitespace

Standalone section, comment, delimiter-change, and partial tags consume their
own lines, including indentation and the line ending. Consecutive standalone
tags follow the same rules. Other content keeps its whitespace and line endings.

Raw-body section lambda bodies exclude the leading line ending and closing-tag
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
