using BenchmarkDotNet.Attributes;
using IniParser.Model;

namespace REDox.Ini.Benchmarks;

[MemoryDiagnoser]
public class IniSerialize : IniBenchmarkBase
{
    private IniData _data = null!;
    private IniDocument? _document;

    public override void Setup()
    {
        base.Setup();
        _document = IniDocument.Parse(Text, options: BenchmarkData.ReadOptions);
        _data = Parser.Parse(Text);
        BenchmarkData.ValidateOutput(REDox(), SectionCount);
        BenchmarkData.ValidateOutput(IniParser(), SectionCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public string REDox()
    {
        return IniDocument.EncodeToString(_document!.RootElement, BenchmarkData.WriteOptions);
    }

    [Benchmark]
    public string IniParser()
    {
        return _data.ToString();
    }
}