using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using REDox.Json;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomOperation
{
    private readonly DArray _doxArr;
    private readonly string[] _items;

    private readonly string _jsonArrayString;
    private readonly string _jsonObjectString;
    private readonly DElement _redoxObj;
    private readonly JsonObject _stjObj;
    private readonly JsonArray _stnArr;

    public DomOperation()
    {
        var numbers = Enumerable.Range(0, 100).ToArray();
        _jsonArrayString = System.Text.Json.JsonSerializer.Serialize(numbers);

        var person = new { Name = "MyName", Age = 30, Family = new[] { "Alice", "Bob" }, Body = true };
        _jsonObjectString = System.Text.Json.JsonSerializer.Serialize(person);

        var doc = System.Text.Json.JsonDocument.Parse(_jsonArrayString);

        _redoxObj = Json.JsonDocument.Parse(_jsonObjectString, SerializerSettings.Default).RootElement;
        _stjObj = JsonObject.Parse(_jsonObjectString)!.AsObject();

        Console.WriteLine(_redoxObj.ToJsonString());
        Console.WriteLine(_stjObj.ToJsonString());

        var list = new List<string>(10000);
        for (var i = 0; i < 10000; i++)
        {
            list.Add("Prop" + i);
        }

        _items = list.ToArray();

        _stnArr = new JsonArray { 1, 2, 3 };
        _doxArr = new DArray { 1, 2, 3 };
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxAddArray()
    {
        const int count = 100;

        var arr = new DArray();
        arr.Capacity = count * 3;
        for (var i = 0; i < count; i++)
        {
            arr.Add((i & 1) == 0);
            arr.Add(i);
            arr.Add("test");
        }

        for (var j = 0; j < 100; j++)
        {
            for (var i = 0; i < count; i++)
            {
                arr[i * 3 + 0] = (i & 1) == 0;
                arr[i * 3 + 1] = i;
                arr[i * 3 + 2] = "test";
            }
        }

        return arr.Count;
    }


    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJNodeAddArray()
    {
        const int count = 100;

        var arr = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            arr.Add((i & 1) == 0);
            arr.Add(i);
            arr.Add("test");
        }

        for (var j = 0; j < 100; j++)
        {
            for (var i = 0; i < count; i++)
            {
                arr[i * 3 + 0] = (i & 1) == 0;
                arr[i * 3 + 1] = i;
                arr[i * 3 + 2] = "test";
            }
        }

        return arr.Count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxObjectReplace()
    {
        var obj = new DObject { { "A", 1 }, { "B", true } };
        for (var i = 0; i < 10000; i++)
        {
            _doxArr[i % 3] = obj;
        }
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public void STJObjectReplace()
    {
        var obj = new JsonObject { { "A", 1 }, { "B", true } };
        for (var i = 0; i < 10000; i++)
        {
            _stnArr[i % 3] = obj.DeepClone();
        }
    }


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxObjectReplace2()
    {
        for (var i = 0; i < 10000; i++)
        {
            _doxArr[i % 3] = DValue.Create(new { A = 1, B = true });
        }
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public void STJObjectReplace2()
    {
        for (var i = 0; i < 10000; i++)
        {
            _stnArr[i % 3] = System.Text.Json.JsonSerializer.SerializeToNode(new { A = 1, B = true });
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxAddProperty()
    {
        var obj = new DObject();
        obj.Capacity = _items.Length;

        for (var i = 0; i < _items.Length; i++)
        {
            obj.Add(_items[i], i);
        }

        return obj.Count;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJAddProperty()
    {
        var obj = new JsonObject();

        for (var i = 0; i < _items.Length; i++)
        {
            obj.Add(_items[i], i);
        }

        return obj.Count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxLookupLastProperty()
    {
        var obj = new DObject();
        obj.Capacity = 100;

        for (var i = 0; i < 100; i++)
        {
            obj.Add("Prop" + i, i);
        }

        var count = 0;
        for (var i = 0; i < 10000; i++)
        {
            count += (int)obj["Prop99"];
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJLookupLastProperty()
    {
        var obj = new JsonObject();

        for (var i = 0; i < 100; i++)
        {
            obj.Add("Prop" + i, i);
        }

        var count = 0;
        for (var i = 0; i < 10000; i++)
        {
            count += (int)obj["Prop99"]!;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxBuildArrayJson()
    {
        var arr = new DArray();
        for (var i = 0; i < 100; i++)
        {
            arr.Add((i & 1) == 0);
            arr.Add(i);
            arr.Add("test");
        }

        var count = 0;
        foreach (var v in arr)
        {
            if (v.GetToken().Kind == DTokenKind.Boolean)
            {
                if ((bool)v)
                {
                    count++;
                }
            }

            if (v.GetToken().Kind == DTokenKind.Integer)
            {
                count += (int)v;
            }

            if (v.GetToken().Kind == DTokenKind.String)
            {
                count += ((string)v)!.Length;
            }
        }

        return count + arr.ToJsonString().Length;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJBuildArrayJson()
    {
        var arr = new JsonArray();

        for (var i = 0; i < 100; i++)
        {
            arr.Add((i & 1) == 0);
            arr.Add(i);
            arr.Add("test");
        }

        var count = 0;
        foreach (var v in arr)
        {
            if (v == null)
            {
                continue;
            }

            if (v.GetValueKind() == System.Text.Json.JsonValueKind.True)
            {
                count++;
            }

            if (v.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            {
                count += v.GetValue<int>();
            }

            if (v.GetValueKind() == System.Text.Json.JsonValueKind.String)
            {
                count += v.GetValue<string>().Length;
            }
        }

        return count + arr.ToJsonString().Length;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public string REDoxObjectToJsonString()
    {
        return _redoxObj.ToJsonString();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public string STJObjectToJsonString()
    {
        return _stjObj.ToJsonString();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxObjectToUtf8String()
    {
        return Json.JsonSerializer.SerializeToUtf8Bytes(_redoxObj, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJObjectUtf8ToString()
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_stjObj);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int[]? REDoxPatchJsonArray()
    {
        using var doc = Json.JsonDocument.Parse(_jsonArrayString, SerializerSettings.Default);

        var arr = doc.RootElement.AsArray();

        arr[10] = 100;

        return arr.To<int[]>();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int[]? STJPatchJsonArray()
    {
        var arr = JsonArray.Parse(_jsonArrayString)!;

        arr[10] = 100;

        return arr.Deserialize<int[]>();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public Person? REDoxPatchJsonObject()
    {
        using var doc = Json.JsonDocument.Parse(_jsonObjectString, SerializerSettings.Default);

        var obj = doc.RootElement.AsObject();

        obj["Age"] = 99;

        return obj.To<Person>();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public Person? STJPatchJsonObject()
    {
        var obj = JsonObject.Parse(_jsonObjectString)!;

        obj["Age"] = 99;

        return obj.Deserialize<Person>();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int[]? REDoxSerializeArray()
    {
        var arr = new DArray();

        for (var i = 0; i < 10; i++)
        {
            arr.Add(i);
        }

        return arr.To<int[]>();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int[]? STJSerializeArray()
    {
        var arr = new JsonArray();

        for (var i = 0; i < 10; i++)
        {
            arr.Add(i);
        }

        return arr.Deserialize<int[]>();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public Person? REDoxSerializeObject()
    {
        var obj = new DObject();

        obj.Add("Name", "MyName");
        obj.Add("Age", 30);
        obj.Add("Family", new DArray { "Alice", "Bob" });
        obj.Add("Body", true);

        return obj.To<Person>();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public Person? STJSerializeObject()
    {
        var obj = new JsonObject();

        obj.Add("Name", "MyName");
        obj.Add("Age", 30);
        obj.Add("Family", new JsonArray { "Alice", "Bob" });
        obj.Add("Body", true);

        return obj.Deserialize<Person>();
    }

    public record Person(string Name, int Age, string[] Family, bool Body);
}