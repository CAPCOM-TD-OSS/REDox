// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Linq;
using BenchmarkDotNet.Attributes;
using REDox.Json;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomJoinElement
{
    private JsonDocument _destination = null!;
    private JsonDocument _source = null!;

    [Params("Integers", "Objects")] public string Shape { get; set; } = "Integers";

    [Params(false, true)] public bool Edited { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var values = Enumerable.Range(0, 1000).Select(i => Shape == "Integers"
            ? i.ToString(CultureInfo.InvariantCulture)
            : $"{{\"id\":{i},\"name\":\"item-{i}\",\"values\":[1,2,3]}}");
        _source = JsonDocument.Parse("[" + string.Join(',', values) + "]");
        _destination = JsonDocument.Parse("0");
        if (Edited)
        {
            var array = _source.RootElement.AsArray();
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

        _destination.RootElement.AsValue().ReplaceWith(_source.RootElement);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _source.Dispose();
        _destination.Dispose();
    }

    [Benchmark]
    public DArray AppendToNewArray()
    {
        var array = new DArray();
        array.Add(_source.RootElement);
        return array;
    }

    [Benchmark]
    public int ReplaceExistingArray()
    {
        _destination.RootElement.AsValue().ReplaceWith(_source.RootElement);
        return _destination.RootElement.GetArrayLength();
    }
}