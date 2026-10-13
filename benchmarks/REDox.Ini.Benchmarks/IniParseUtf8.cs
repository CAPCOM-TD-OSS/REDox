// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using BenchmarkDotNet.Attributes;

namespace REDox.Ini.Benchmarks;

[MemoryDiagnoser]
public class IniParseUtf8
{
    private IniDocumentOptions _options;
    private byte[] _utf8 = [];

    [Params("Standard", "LargeThenSmall", "TrailingWhitespace")]
    public string Input { get; set; } = "Standard";

    [Params(false, true)] public bool PreserveTrivia { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder();
        if (Input == "LargeThenSmall")
        {
            builder.Append("[large]\n");
            for (var key = 0; key < 4096; key++)
            {
                builder.Append("key").Append(key).Append("=value\n");
            }

            for (var section = 0; section < 256; section++)
            {
                builder.Append("[small").Append(section).Append("]\nkey0=value\n");
            }
        }
        else
        {
            builder.Append(BenchmarkData.Create(100));
            if (Input == "TrailingWhitespace")
            {
                builder.Replace("\n", new string(' ', 128) + "\n");
            }
        }

        _utf8 = Encoding.UTF8.GetBytes(builder.ToString());
        _options = new IniDocumentOptions { PreserveTrivia = PreserveTrivia };

        using var document = IniDocument.Parse(_utf8, options: _options);
        if (Input == "LargeThenSmall")
        {
            BenchmarkData.Check("value", document.RootElement.GetProperty("large").GetProperty("key4095").GetString(),
                "large section");
            for (var section = 0; section < 256; section++)
            {
                BenchmarkData.Check("value", document.RootElement.GetProperty("small" + section).GetProperty("key0").GetString(),
                    "small section");
            }
        }
        else
        {
            BenchmarkData.ValidateValues((section, key) => (section == null
                    ? document.RootElement
                    : document.RootElement.GetProperty(section))
                .GetProperty(key).GetString(), 100);
        }

        if (PreserveTrivia)
        {
            BenchmarkData.Check(builder.ToString(), IniDocument.EncodeToString(document.RootElement,
                new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" }), "preserved trivia");
        }
    }

    [Benchmark]
    public int REDox()
    {
        using var document = IniDocument.Parse(_utf8, options: _options);
        return document.GetTokens().Length;
    }
}