using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomCreate
{
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public DArray REDoxCreateArrayEmpty()
    {
        var arr = new DArray();
        return arr;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public DArray REDoxCreateArrayIndexer()
    {
        var arr = new DArray(capacity: 4);
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);

        arr[0] = 123;
        arr[1] = "My name";
        arr[2] = 46;
        arr[3] = true;
        return arr;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public DArray REDoxCreateArrayAdd()
    {
        var arr = new DArray(capacity: 4);
        arr.Add(123);
        arr.Add("My name");
        arr.Add(46);
        arr.Add(true);
        return arr;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public JsonArray STJCreateArrayEmpty()
    {
        var arr = new JsonArray();
        return arr;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public JsonArray STJCreateArrayIndexer()
    {
        var arr = new JsonArray();
        arr.Add(null);
        arr.Add(null);
        arr.Add(null);
        arr.Add(null);

        arr[0] = 123;
        arr[1] = "My name";
        arr[2] = 46;
        arr[3] = true;
        return arr;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public JsonArray STJCreateArrayAdd()
    {
        var arr = new JsonArray();
        arr.Add(123);
        arr.Add("My name");
        arr.Add(46);
        arr.Add(true);
        return arr;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public DObject REDoxCreateObjectEmpty()
    {
        var obj = new DObject();
        return obj;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public DObject REDoxCreateObjectIndexer()
    {
        var obj = new DObject(capacity: 4);
        obj["id"] = 123;
        obj["name"] = "My name";
        obj["age"] = 46;
        obj["mail"] = true;
        return obj;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public DObject REDoxCreateObjectAdd()
    {
        var obj = new DObject(capacity: 4);
        obj.Add("id", 123);
        obj.Add("name", "My name");
        obj.Add("age", 46);
        obj.Add("mail", true);
        return obj;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public JsonObject STJCreateObjectEmpty()
    {
        var obj = new JsonObject();
        return obj;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public JsonObject STJCreateObjectIndexer()
    {
        var obj = new JsonObject();
        obj["id"] = 123;
        obj["name"] = "My name";
        obj["age"] = 46;
        obj["mail"] = true;
        return obj;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public JsonObject STJCreateObjectAdd()
    {
        var obj = new JsonObject();
        obj.Add("id", 123);
        obj.Add("name", "My name");
        obj.Add("age", 46);
        obj.Add("mail", true);
        return obj;
    }
}