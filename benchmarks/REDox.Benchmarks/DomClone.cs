// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Linq;
using BenchmarkDotNet.Attributes;
using REDox.Json;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomClone
{
    private JsonDocument _document = null!;
    private DElement _element;

    [Params("Integers", "Objects")] public string Shape { get; set; } = "Integers";

    [Params(false, true)] public bool Edited { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var values = Enumerable.Range(0, 1000).Select(i => Shape == "Integers"
            ? i.ToString(CultureInfo.InvariantCulture)
            : $"{{\"id\":{i},\"name\":\"item-{i}\",\"values\":[1,2,3]}}");
        _document = JsonDocument.Parse("[" + string.Join(',', values) + "]");
        _element = _document.RootElement;
        if (Edited)
        {
            var array = _element.AsArray();
            foreach (var value in array)
            {
                if (Shape == "Integers")
                {
                    value.ReplaceWith((int)value + 1);
                }
                else
                {
                    value.AsObject()["id"] = (int)value["id"] + 1;
                    value.AsObject()["values"].AsArray().Add(4);
                }
            }
        }

        _element = _document.RootElement;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document.Dispose();
    }

    // This is the TokenWriter path previously used by DElement.Clone for containers.
    [Benchmark(Baseline = true)]
    public DElement TokenWriterClone()
    {
        return DValue.From(_element).AsElement();
    }

    [Benchmark]
    public DElement DirectClone()
    {
        return _element.Clone();
    }
}