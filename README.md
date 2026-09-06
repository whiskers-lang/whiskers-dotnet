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
