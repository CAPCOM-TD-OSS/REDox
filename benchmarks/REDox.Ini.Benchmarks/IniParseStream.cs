using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using IniParser;
using Microsoft.Extensions.Configuration.Ini;

namespace REDox.Ini.Benchmarks;

[MemoryDiagnoser]
public class IniParseStream : IniBenchmarkBase
{
    private FileIniDataParser _fileParser = null!;

    public override void Setup()
    {
        base.Setup();
        _fileParser = new FileIniDataParser(Parser);
        BenchmarkData.Check(BenchmarkData.LastValue, REDox(), "REDox stream parse");
        BenchmarkData.Check(BenchmarkData.LastValue, IniParser(), "ini-parser-netstandard stream parse");
        BenchmarkData.Check(BenchmarkData.LastValue, Microsoft(), "Microsoft stream parse");
    }

    [Benchmark(Baseline = true)]
    public string? REDox()
    {
        using var stream = new MemoryStream(Utf8, false);
        using var doc = IniDocument.Parse(stream, options: BenchmarkData.ReadOptions);
        return doc.RootElement.GetProperty(LastSection).GetProperty(BenchmarkData.LastKey).GetString();
    }

    [Benchmark]
    public string IniParser()
    {
        using var stream = new MemoryStream(Utf8, false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return _fileParser.ReadData(reader)[LastSection][BenchmarkData.LastKey];
    }

    [Benchmark]
    public string? Microsoft()
    {
        using var stream = new MemoryStream(Utf8, false);
        var provider = new IniStreamConfigurationProvider(new IniStreamConfigurationSource());
        provider.Load(stream);
        return provider.TryGet(LastSection + ":" + BenchmarkData.LastKey, out var value) ? value : null;
    }
}