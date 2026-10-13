using System;
using BenchmarkDotNet.Running;
using REDox.Ini.Benchmarks;

if (args is ["--validate"])
{
    foreach (var sections in new[] { 1, 10, 100 })
    {
        var parseString = new IniParseString { SectionCount = sections };
        parseString.Setup();
        BenchmarkData.Check(BenchmarkData.LastValue, parseString.REDox(), "REDox string parse");
        BenchmarkData.Check(BenchmarkData.LastValue, parseString.IniParser(), "ini-parser-netstandard string parse");

        var parseStream = new IniParseStream { SectionCount = sections };
        parseStream.Setup();
        BenchmarkData.Check(BenchmarkData.LastValue, parseStream.REDox(), "REDox stream parse");
        BenchmarkData.Check(BenchmarkData.LastValue, parseStream.IniParser(), "ini-parser-netstandard stream parse");
        BenchmarkData.Check(BenchmarkData.LastValue, parseStream.Microsoft(), "Microsoft stream parse");

        var serialize = new IniSerialize { SectionCount = sections };
        try
        {
            serialize.Setup();
            BenchmarkData.ValidateOutput(serialize.REDox(), sections);
            BenchmarkData.ValidateOutput(serialize.IniParser(), sections);
        }
        finally
        {
            serialize.Cleanup();
        }

        var edit = new IniEdit { SectionCount = sections };
        edit.Setup();
        BenchmarkData.ValidateOutput(edit.REDox(), sections, BenchmarkData.FormattedDate);
        BenchmarkData.ValidateOutput(edit.IniParser(), sections, BenchmarkData.FormattedDate);
        Console.WriteLine($"Validated {sections} sections / {sections * BenchmarkData.KeysPerSection + 2} values / {parseString.Utf8.Length} UTF-8 bytes.");
    }

    foreach (var input in new[] { "Standard", "LargeThenSmall", "TrailingWhitespace" })
    foreach (var preserveTrivia in new[] { false, true })
    {
        var parseUtf8 = new IniParseUtf8 { Input = input, PreserveTrivia = preserveTrivia };
        parseUtf8.Setup();
        parseUtf8.REDox();
        Console.WriteLine($"Validated UTF-8 {input} / PreserveTrivia={preserveTrivia}.");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);