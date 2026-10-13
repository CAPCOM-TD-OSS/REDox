using BenchmarkDotNet.Attributes;

namespace REDox.Ini.Benchmarks;

[MemoryDiagnoser]
public class IniEdit : IniBenchmarkBase
{
    public override void Setup()
    {
        base.Setup();
        BenchmarkData.ValidateOutput(REDox(), SectionCount, BenchmarkData.FormattedDate);
        BenchmarkData.ValidateOutput(IniParser(), SectionCount, BenchmarkData.FormattedDate);
    }

    // Parse, update a Japanese Gregorian date, and serialize the complete document.
    [Benchmark(Baseline = true)]
    public string REDox()
    {
        using var doc = IniDocument.Parse(Text, Settings, BenchmarkData.ReadOptions);
        doc.RootElement.AsObject()["last_updated"] = BenchmarkData.Date;
        return IniDocument.EncodeToString(doc.RootElement, BenchmarkData.WriteOptions);
    }

    [Benchmark]
    public string IniParser()
    {
        var data = Parser.Parse(Text);
        data.Global["last_updated"] = BenchmarkData.Date.ToString(BenchmarkData.DateFormat, BenchmarkData.Culture);
        return data.ToString();
    }
}