using System;
using System.Globalization;
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using IniParser.Parser;
using Microsoft.Extensions.Configuration.Ini;

namespace REDox.Ini.Benchmarks;

static class BenchmarkData
{
    public const int KeysPerSection = 16;
    public const string LastKey = "key15";
    public const string LastValue = "設定値-日本語";
    public const string DateFormat = "yyyy年M月d日";
    public const string FormattedDate = "2026年10月5日";
    public static readonly DateTime Date = new(2026, 10, 5, 12, 34, 56);
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ja-JP");
    public static readonly IniDocumentOptions ReadOptions = new() { AllowDuplicateKeys = false };
    public static readonly IniWriteOptions WriteOptions = new() { WriteSpaces = true, NewLine = "\n" };

    public static string SectionName(int section)
    {
        return "section" + section.ToString(CultureInfo.InvariantCulture);
    }

    private static string KeyName(int key)
    {
        return "key" + key.ToString("D2", CultureInfo.InvariantCulture);
    }

    private static string Value(int section, int key)
    {
        return key == KeysPerSection - 1
            ? LastValue
            : $"value-{section.ToString(CultureInfo.InvariantCulture)}-{key.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string Create(int sections)
    {
        var builder = new StringBuilder("application_name = SampleApp\nlast_updated = pending\n");
        for (var section = 0; section < sections; section++)
        {
            builder.Append('\n').Append('[').Append(SectionName(section)).Append("]\n");
            for (var key = 0; key < KeysPerSection; key++)
            {
                builder.Append(KeyName(key)).Append(" = ").Append(Value(section, key)).Append('\n');
            }
        }

        return builder.ToString();
    }

    public static void Check(string? expected, string? actual, string context)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{context}: expected '{expected}', got '{actual}'.");
        }
    }

    public static void ValidateValues(Func<string?, string, string?> read, int sections, string date = "pending")
    {
        Check("SampleApp", read(null, "application_name"), "application_name");
        Check(date, read(null, "last_updated"), "last_updated");
        for (var section = 0; section < sections; section++)
        {
            for (var key = 0; key < KeysPerSection; key++)
            {
                Check(Value(section, key), read(SectionName(section), KeyName(key)), $"{SectionName(section)}:{KeyName(key)}");
            }
        }
    }

    public static void ValidateOutput(string output, int sections, string date = "pending")
    {
        // Verify every output value with the other implementation, outside measured operations.
        var data = new IniDataParser().Parse(output);
        if (data.Global.Count != 2 || data.Sections.Count != sections)
        {
            throw new InvalidOperationException("Unexpected number of global keys or sections.");
        }

        foreach (var section in data.Sections)
        {
            if (section.Keys.Count != KeysPerSection)
            {
                throw new InvalidOperationException("Unexpected number of section keys.");
            }
        }

        ValidateValues((section, key) => section == null
            ? data.Global[key]
            : data[section][key], sections, date);

        using var doc = IniDocument.Parse(output, options: ReadOptions);
        ValidateValues((section, key) => (section == null
                ? doc.RootElement
                : doc.RootElement.GetProperty(section))
            .GetProperty(key).GetString(), sections, date);
    }
}

public abstract class IniBenchmarkBase
{
    [Params(1, 10, 100)] public int SectionCount { get; set; }

    public byte[] Utf8 { get; private set; } = [];
    protected string Text { get; private set; } = string.Empty;
    protected string LastSection { get; private set; } = string.Empty;
    protected IniDataParser Parser { get; } = new();

    protected DoxSerializerSettings Settings { get; } = new()
    {
        Culture = BenchmarkData.Culture,
        DateFormatString = BenchmarkData.DateFormat
    };

    [GlobalSetup]
    public virtual void Setup()
    {
        Text = BenchmarkData.Create(SectionCount);
        Utf8 = Encoding.UTF8.GetBytes(Text);
        LastSection = BenchmarkData.SectionName(SectionCount - 1);
        Parser.Configuration.NewLineStr = "\n";
        Parser.Configuration.AssigmentSpacer = " ";

        // All parsers must agree on all values before a benchmark is allowed to run.
        BenchmarkData.ValidateOutput(Text, SectionCount);
        using var stream = new MemoryStream(Utf8, false);
        var provider = new IniStreamConfigurationProvider(new IniStreamConfigurationSource());
        provider.Load(stream);
        BenchmarkData.ValidateValues((section, key) => provider.TryGet(section == null ? key : section + ":" + key,
            out var value)
            ? value
            : null, SectionCount);
    }
}