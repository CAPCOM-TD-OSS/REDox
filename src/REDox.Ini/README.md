# RE:Dox INI

## 1. What is RE:Dox INI?

**RE:Dox INI** is an INI parser and writer for .NET. It is part of [RE:Dox](https://github.com/CAPCOM-TD-OSS/redox), developed as a core component of CAPCOM's **'REX' Technology**.

Use `IniDocument` to read and edit global settings and sections, preserve comments, and save the result. It stores input through RE:Dox's shared 64-bit token model and decodes names and values only when needed.

## 2. Why should I care?

- **Fast reading**: in the 100-section stream parsing benchmark below, **11.06x** faster than ini-parser-netstandard and **2.84x** faster than Microsoft INI.
- **Low allocation**: the same benchmark allocated **208 B per operation** at all three sizes: 1 / 10 / 100 sections.
- **Editable documents**: add, update, and remove settings and sections through `DObject` views.
- **Comments and formatting**: preserve comments, whitespace, and separators with `PreserveTrivia` on both reading and writing.
- **Output options**: control line endings, spaces around `=`, UTF-8 BOM output, culture, and date formatting.

## 3. 30-second example

Requires **.NET 10 or later**. The INI package depends on the RE:Dox core package.

```bash
dotnet add package CAPCOM.REDox.Ini
```

```csharp
using System;
using REDox.Ini;

const string ini = """
    application_name = Demo
    [window]
    width = 1920
    height = 1080
    """;

using var document = IniDocument.Parse(ini);
var root = document.RootElement;

string? name = root.GetProperty("application_name").GetString();
int width = root.GetProperty("window").GetProperty("width").To<int>();

var window = root.GetProperty("window").AsObject();
window["width"] = 1280;
window.Add("fullscreen", true);

string output = IniDocument.EncodeToString(root, new IniWriteOptions
{
    WriteSpaces = true,
    NewLine = "\n"
});
Console.WriteLine(output);
```

Parsed values are text tokens. Use `To<T>()` for typed conversion, `TryGetProperty()` for optional settings, and `IniDocument.TryParse()` to handle syntax errors through a return value. `Parse()` reports invalid syntax with a `DocumentParseException`.

`Parse()` accepts strings, UTF-8 bytes, and streams. Write with `EncodeToString()`, `Encode()`, or `EncodeTo()` to produce a string, a UTF-8 byte array, or output to a `Stream` / `IBufferWriter<byte>`.

## 4. Performance

`IniParseStream` results measured on **2026-10-13** with BenchmarkDotNet **0.15.8 / DefaultJob**, AMD Ryzen Threadripper PRO 9975WX (32 physical / 64 logical cores), Windows 11 24H2 (10.0.26100.9168), .NET SDK 10.0.401 / runtime 10.0.12, and X64 RyuJIT x86-64-v4.

Comparison libraries: **ini-parser-netstandard 2.5.3** and **Microsoft.Extensions.Configuration.Ini 10.0.12**.

### UTF-8 stream parsing + value lookup: Mean / Allocated

| Sections | Values | RE:Dox INI | ini-parser-netstandard | Microsoft INI |
| --- | ---: | ---: | ---: | ---: |
| 1 | 18 | **0.655 µs / 208 B** | 4.527 µs / 17.36 KB | 1.172 µs / 10.43 KB |
| 10 | 162 | **3.778 µs / 208 B** | 37.299 µs / 118.91 KB | 9.624 µs / 58.01 KB |
| 100 | 1,602 | **36.036 µs / 208 B** | 398.590 µs / 1,137.16 KB | 102.520 µs / 559.27 KB |

1 KB = 1,024 B. Speedup is the comparison library's mean time divided by RE:Dox INI's mean time.

Inputs contain two global keys, 16 keys per section, and Japanese text values. The 100-section input is **32,992 UTF-8 bytes**. Duplicate-key validation is enabled; comment preservation is disabled.

Each operation creates a stream over UTF-8 data in memory, parses the full input, and reads one value from the last section. Stream and reader creation and document disposal are included; disk I/O is excluded. Internal representations and whether all values are decoded differ between libraries.

Allocated bytes are measured per operation after warmup; they do not represent memory retained by pools or total process memory. Results depend on input, environment, and options.

Source: the local BenchmarkDotNet report at `benchmarks/REDox.Ini.Benchmarks/bin/Release/net10.0/BenchmarkDotNet.Artifacts/results/REDox.Ini.Benchmarks.IniParseStream-report-github.md`. Benchmark implementation: [IniParseStream.cs](../../benchmarks/REDox.Ini.Benchmarks/IniParseStream.cs) and [BenchmarkData.cs](../../benchmarks/REDox.Ini.Benchmarks/BenchmarkData.cs).

Reproduce from the repository root:

```bash
dotnet run -c Release --project benchmarks/REDox.Ini.Benchmarks -- --validate
dotnet run -c Release --project benchmarks/REDox.Ini.Benchmarks -- --filter '*IniParseStream*'
```

## 5. Comments and options

Enable preservation on both reading and writing to keep comments and formatting:

```csharp
using REDox.Ini;

using var document = IniDocument.Parse("; Window settings\n[window]\nwidth = 1920\n",
    options: new IniDocumentOptions { PreserveTrivia = true });

document.RootElement.GetProperty("window").AsObject()["width"] = 1280;

string output = IniDocument.EncodeToString(document.RootElement,
    new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" });
```

| Read option | Behavior when enabled |
| --- | --- |
| `PreserveTrivia` | Retain comments, whitespace, and separators. |
| `CommentSyntax` | Accept `Both` (default), `Semicolon`, or `Hash` comment delimiters. |
| `AllowInlineComments` | Treat selected delimiters in values as comments when preceded by whitespace or at the start of the value. |
| `AllowDuplicateKeys` | Accept duplicate keys and section names. |

All read flags default to `false`. Names are case-sensitive unless `SerializerSettings.PropertyNameCaseInsensitive` is enabled, which also applies to duplicate detection (ASCII letters only). Duplicate keys within a section, duplicate section names, and collisions between global keys and section names are rejected by default. When duplicates are allowed, lookup by name returns the first match.

Lines starting with a selected delimiter are comments. By default, both `;` and `#` are selected. `CommentSyntax` also applies to comments after section headers and enabled inline comments. Unselected delimiters are ordinary characters: for example, `#name=value` is a key/value line under `Semicolon`. A non-comment line without `=` is still invalid. Leading and trailing spaces around names and values are trimmed; quotation marks are ordinary value characters. Output supports global scalar settings and one level of sections.

| Write option | Behavior |
| --- | --- |
| `PreserveTrivia` | Write retained comments and formatting. |
| `CommentStyle` | `PreserveOrSemicolon` (default) / `PreserveOrHash` keep retained INI source delimiters and use `;` / `#` for new, modified, or non-INI comments. `AlwaysSemicolon` / `AlwaysHash` use the selected delimiter for all output comments. |
| `WriteSpaces` | Generate separators as `key = value`. |
| `NewLine` | Use `"\n"` / `"\r\n"` for generated line breaks. Defaults to `Environment.NewLine`. |
| `WriteBom` | Emit a UTF-8 BOM. |

Choose the read and write delimiters independently:

```csharp
using var document = IniDocument.Parse("; Settings\nkey=value ; explanation\n",
    options: new IniDocumentOptions
    {
        PreserveTrivia = true,
        AllowInlineComments = true,
        CommentSyntax = IniCommentSyntax.Semicolon
    });

string output = IniDocument.EncodeToString(document.RootElement,
    new IniWriteOptions { PreserveTrivia = true, CommentStyle = IniCommentStyle.AlwaysHash });
```

This writes `# Settings` and `key=value # explanation`. `CommentStyle` does not enable comment output by itself; use `PreserveTrivia`. Comment bodies and delimiters inside values are not changed.

Added or modified comments and comments from other formats, including JSON5 block comments, are written as INI line comments. Each line receives the selected delimiter, including empty lines within a comment. Embedded CR, LF, and CRLF line breaks use `NewLine` on output; a trailing line break does not add an extra empty comment line. Retained INI trivia keeps its original formatting.

Use `IniDocument.TrailingTrivia` to enumerate, replace, clear, or add trivia at the end of the document. The collection includes retained trailing whitespace as well as comments. New comments can also be added when the document was parsed without `PreserveTrivia`:

```csharp
document.TrailingTrivia.AddLast(" End of settings", TriviaKind.LineComment);
string withTrailingComment = document.RootElement.ToIni(
    new IniWriteOptions { PreserveTrivia = true });
```

To build a document from scratch, parse an empty string and add keys and sections. Comments in the root container's `LeadingTrivia` appear at the start of the document. Comments in a section container's `LeadingTrivia` appear after its section header:

```csharp
using var created = IniDocument.Parse("");
var root = created.RootElement.AsObject();
root.LeadingTrivia.AddLast(" Settings");
root.Add("enabled", "true");
var section = root.AddObject("network");
section.LeadingTrivia.AddLast(" Network settings");
section.Add("port", "443");
created.TrailingTrivia.AddLast(" End of settings");
string generated = root.ToIni(new IniWriteOptions { PreserveTrivia = true });
```

The generated text is:

```text
; Settings
enabled=true
[network]
; Network settings
port=443
; End of settings
```

## 6. Document lifetime

Dispose `IniDocument` with `using`. Elements and views obtained from a document must not be used after it is disposed.

| Input | Buffer handling |
| --- | --- |
| `string` | Encode into a UTF-8 buffer owned by the document. |
| `ReadOnlySpan<byte>` | Copy into a buffer owned by the document. |
| `byte[]` / `ReadOnlyMemory<byte>` | Reference input without copying. Keep it unchanged while in use. |
| `Stream` | Read into a buffer owned by the document. The caller manages the stream. |

`Duplicate()` creates a snapshot with independent token storage. It copies document-owned input but shares caller-supplied byte arrays or memory, which must remain valid and unchanged while either document is in use.

With **C# 14**, use the convenience APIs `DValue.ParseIni()` and `ToIni()`:

```csharp
using REDox;
using REDox.Ini;

var value = DValue.ParseIni("application_name=Demo\n");
value.AsObject()["application_name"] = "EditedApp";
string output = value.ToIni(new IniWriteOptions { WriteSpaces = true });
```

This API parses and duplicates the document internally, returning a value with a lifetime independent of the original document.

## License

[Apache License 2.0](https://github.com/CAPCOM-TD-OSS/redox/blob/main/LICENSE). See the [RE:Dox README](https://github.com/CAPCOM-TD-OSS/redox/blob/main/README.md) for shared APIs and other formats.
