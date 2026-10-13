using BenchmarkDotNet.Attributes;

namespace REDox.Ini.Benchmarks;

[MemoryDiagnoser]
public class IniParseString : IniBenchmarkBase
{
    [Benchmark(Baseline = true)]
    public string? REDox()
    {
        using var doc = IniDocument.Parse(Text, options: BenchmarkData.ReadOptions);
        return doc.RootElement.GetProperty(LastSection).GetProperty(BenchmarkData.LastKey).GetString();
    }

    [Benchmark]
    public string IniParser()
    {
        return Parser.Parse(Text)[LastSection][BenchmarkData.LastKey];
    }
}