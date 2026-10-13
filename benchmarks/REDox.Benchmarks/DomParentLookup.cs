// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Linq;
using BenchmarkDotNet.Attributes;
using REDox.Json;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomParentLookup
{
    private const int BatchSize = 64;
    private readonly JsonDocument[] _documents = new JsonDocument[BatchSize];
    private readonly DValue[] _values = new DValue[BatchSize];

    [Params(16, 4096)] public int Count { get; set; }

    [Params("Array", "Object", "NestedArrays")]
    public string Shape { get; set; } = "Array";

    [Params(false, true)] public bool Materialize { get; set; }

    [IterationSetup]
    public void Setup()
    {
        var json = Shape switch
        {
            "Object" => "{" + string.Join(",", Enumerable.Range(0, Count).Select(i => $"\"k{i}\":{i}")) + "}",
            "NestedArrays" => "[" + string.Join(",", Enumerable.Repeat("[0]", Count)) + "]",
            _ => "[" + string.Join(",", Enumerable.Range(0, Count)) + "]"
        };

        for (var i = 0; i < BatchSize; i++)
        {
            var document = JsonDocument.Parse(json);
            _documents[i] = document;
            if (Materialize)
            {
                document.RootElement.AsContainer();
            }

            _values[i] = Shape == "Object"
                ? document.RootElement.EnumerateMap().First().Value.AsValue()
                : document.RootElement.EnumerateArray().First().AsValue();
        }
    }

    [IterationCleanup]
    public void Cleanup()
    {
        foreach (var document in _documents)
        {
            document.Dispose();
        }
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int FirstParentLookup()
    {
        var count = 0;
        foreach (var value in _values)
        {
            if (value.Parent.HasValue)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int RootParentLookup()
    {
        var count = 0;
        foreach (var document in _documents)
        {
            if (document.RootElement.AsValue().Parent.HasValue)
            {
                count++;
            }
        }

        return count;
    }
}