using System;
using System.Text.Json.Nodes;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public class DomExtendTest
{
    private readonly DArray _doxArr = new(1, 2, 3);
    private readonly ITestOutputHelper _output;

    public DomExtendTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private void DumpTokens(Document doc)
    {
        _output.WriteLine(doc.RootElement.ToString()!);
        var index = 0;
        foreach (var token in doc.GetTokens())
        {
            _output.WriteLine(index + " : " + token);
            index++;
        }
    }

    [Fact]
    public void DomJsonParse()
    {
        var json = """{"A":1,"B":true}"""u8;

        var dox = DValue.ParseJson(json);
        var stj = JsonNode.Parse(json)!;

        Assert.Equal((int)stj.GetValueKind(), (int)dox.GetValueKind());
        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());
        Assert.Equal(stj.ToString(), dox.ToString());

        dox["A"] = 1.2000m;
        stj["A"] = 1.2000m;

        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());

        dox[1] = 1.2000m;
        stj[1] = 1.2000m;

        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());

        dox[0] = 123;
        stj[0] = 123;

        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());

        dox.AsObject()[0] = 456;
        stj.AsObject()[0] = 456;

        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());

        var doxArr = DValue.ParseJson("[1,2,3]");
        var stjArr = JsonNode.Parse("[1,2,3]")!;

        Assert.Equal(stjArr.ToJsonString(), doxArr.ToJsonString());

        doxArr[2] = 123;
        stjArr[2] = 123;

        Assert.Equal(stjArr.ToJsonString(), doxArr.ToJsonString());
    }

    [Fact]
    public void DomReplaceTest()
    {
        using var doc = JsonDocument.Parse("[[1,2],[3,4],[5,6]]");

        DumpTokens(doc);

        DArray? arr = null;
        var index = 0;
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            if (index == 1)
            {
                arr = v.AsArray();
            }

            index++;
        }

        DumpTokens(doc);

        arr?.Clear();

        DumpTokens(doc);

        arr?.AsValue().ReplaceWith(true);

        DumpTokens(doc);

        index = 0;
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            if (index == 2)
            {
                foreach (var a in v.EnumerateArray())
                {
                    a.AsValue().ReplaceWith(new DArray(1, 2, 3));
                    break;
                }
            }

            index++;
        }

        DumpTokens(doc);

        Assert.Equal("[[1,2],true,[[1,2,3],6]]", doc.RootElement.ToJsonString());

        var arr2 = doc.RootElement.AsArray();
        arr2.RemoveAt(0);

        DumpTokens(doc);

        Assert.Equal("[true,[[1,2,3],6]]", doc.RootElement.ToJsonString());

        arr2.Clear();

        DumpTokens(doc);

        Assert.Equal("[]", doc.RootElement.ToJsonString());
    }

    [Fact]
    public void DeserializeNode()
    {
        var obj = new DObject();
        obj["A"] = 123;
        obj["B"] = true;

        var json = JsonSerializer.Serialize(obj);

        Assert.Equal("""{"A":123,"B":true}""", json);

        var obj2 = JsonSerializer.Deserialize<DObject>(json);

        Assert.Equal(obj.ToJsonString(), obj2.ToJsonString());

        var map = new DMap();
        map.Add("A", 2);
        map.Add("B", false);

        var json2 = JsonSerializer.Serialize(map);

        var map2 = JsonSerializer.Deserialize<DMap>(json2);

        Assert.Equal(map.ToJsonString(), map2.ToJsonString());

        var arr = new DArray(1, "A", true);

        var json3 = JsonSerializer.Serialize(arr);

        var arr2 = JsonSerializer.Deserialize<DArray>(json3);

        Assert.Equal(arr.ToJsonString(), arr2.ToJsonString());

        var v = DValue.Create(123);

        var json4 = JsonSerializer.Serialize(v);

        Assert.Equal("123", json4);

        var v2 = JsonSerializer.Deserialize<DValue>(json4);

        Assert.Equal(v.ToJsonString(), v2.ToJsonString());
    }

    [Fact]
    public void MapTest()
    {
        var map = new DMap();
        map.Add(DValue.Create(new[] { 1, 2, 3 }), DValue.Create(new[] { 4, 5, 6 }));
        map.Add(DValue.Create(new { Name = "A", Age = 23 }), "OK");
        map.Add(123, 456);

        var obj = map[1].Key.AsMap();
        obj[0].Key.ReplaceWith(false);

        using var writer = new JsonWriter(SerializerSettings.Default);
        writer.WriteValue(map);

        var json = writer.EncodeToString();

        Assert.Equal(map.ToJsonString(), json);

        var arr = new DArray();
        arr.Add(map);
        var json2 = arr.ToJsonString();

        Assert.Equal("""[{"[1,2,3]":[4,5,6],"{\"false\":\"A\",\"Age\":23}":"OK","123":456}]""", json2);

        var arr2 = DValue.From(arr);

        var json3 = arr2.ToJsonString();

        Assert.Equal(json2, json3);
        var map2 = DValue.From(map);

        var json4 = map2.ToJsonString();

        Assert.Equal(json, json4);
    }

    [Fact]
    public void StaticReplaceTest()
    {
        var obj = new DObject { { "A", 1 }, { "B", true } };
        for (var i = 0; i < 1000000; i++)
        {
            _doxArr[i % 3] = obj;
        }

//        _output.WriteLine(_doxArr.Document.ExtendCount.ToString());

        _output.WriteLine("===" + _doxArr.Document.GetTokens().Length);

        foreach (var token in _doxArr.Document.GetTokens())
        {
            _output.WriteLine(token.ToString());
        }
    }

    [Fact]
    public void CloneArrayTest()
    {
        var arr = new DArray(1, 2, 3);
        Assert.Equal("[1,2,3]", arr.ToJsonString());
        var arr2 = arr.DeepClone();
        arr2.Add(true);
        Assert.Equal("[1,2,3]", arr.ToJsonString());
        Assert.Equal("[1,2,3,true]", arr2.ToJsonString());
    }

    [Fact]
    public void ReplaceTest2()
    {
        var arr = DValue.From(new[] { 1, 2, 3 });
        var reader = new DataReader(arr.AsElement());

        for (var j = 0; j < 1; j++)
        {
            var obj = new DObject { { "A", 1 }, { "B", true } };
            for (var i = 0; i < 10000; i++)
            {
                arr[i % 3] = obj;
            }


            _output.WriteLine($"{reader.Document.GetTokens().Length},{reader.Document.GetExtends().Length}");
        }

        DumpTokens(reader.Document);
    }

    [Fact]
    public void AddTest()
    {
        {
            var arr = new DArray();
            var arr1 = arr.AddArray();
            arr1.Add(1);
            var arr2 = arr.AddArray();
            arr2.Add(true);

            Assert.Equal("""[[1],[true]]""", arr.ToJsonString());

            var obj1 = arr.AddObject();
            obj1.Add("A", 1);
            var obj2 = arr.AddObject();
            obj2.Add("B", 2);
            obj2.Add("C", 3);

            Assert.Equal("""[[1],[true],{"A":1},{"B":2,"C":3}]""", arr.ToJsonString());

            var map1 = arr.AddMap();
            map1.Add(1, 1);

            var map2 = arr.AddMap();
            map2.Add(2, 2);

            Assert.Equal("""[[1],[true],{"A":1},{"B":2,"C":3},{"1":1},{"2":2}]""", arr.ToJsonString());
        }

        {
            var map = new DMap();
            var arr1 = map.AddArray("1");
            arr1.Add(1);
            var arr2 = map.AddArray("2");
            arr2.Add(true);

            Assert.Equal("""{"1":[1],"2":[true]}""", map.ToJsonString());

            var obj1 = map.AddObject("A");
            obj1.Add("A", 1);
            var obj2 = map.AddObject("B");
            obj2.Add("B", 2);
            obj2.Add("C", 3);

            Assert.Equal("""{"1":[1],"2":[true],"A":{"A":1},"B":{"B":2,"C":3}}""", map.ToJsonString());

            var map1 = map.AddMap("C");
            map1.Add(1, 1);

            var map2 = map.AddMap("D");
            map2.Add(2, 2);

            Assert.Equal("""{"1":[1],"2":[true],"A":{"A":1},"B":{"B":2,"C":3},"C":{"1":1},"D":{"2":2}}""",
                map.ToJsonString());
        }

        {
            var obj = new DObject();
            var arr1 = obj.AddArray("1");
            arr1.Add(1);
            var arr2 = obj.AddArray("2");
            arr2.Add(true);

            Assert.Equal("""{"1":[1],"2":[true]}""", obj.ToJsonString());

            var obj1 = obj.AddObject("A");
            obj1.Add("A", 1);
            var obj2 = obj.AddObject("B");
            obj2.Add("B", 2);
            obj2.Add("C", 3);

            Assert.Equal("""{"1":[1],"2":[true],"A":{"A":1},"B":{"B":2,"C":3}}""", obj.ToJsonString());

            var map1 = obj.AddMap("C");
            map1.Add(1, 1);

            var map2 = obj.AddMap("D");
            map2.Add(2, 2);

            Assert.Equal("""{"1":[1],"2":[true],"A":{"A":1},"B":{"B":2,"C":3},"C":{"1":1},"D":{"2":2}}""",
                obj.ToJsonString());
        }
    }

    [Fact]
    public void ObjectDomOperationTest()
    {
        var obj = new DObject
        {
            { "Name", "Taro" },
            { "Enabled", true },
            { "Count", 1 }
        };

        Assert.False(obj.TryAdd("Enabled", false));

        obj["Enabled"] = false;
        obj["Count"] = 2;
        Assert.True(obj.Remove("Name"));
        Assert.False(obj.Remove("Missing"));

        var tags = obj.AddArray("Tags");
        tags.Add("alpha");
        tags.Add("beta");
        tags.Insert(1, "middle");
        tags.RemoveAt(0);

        var nested = obj.AddObject("Nested");
        nested.Add("Value", 100);
        nested.Add("Active", true);
        nested["Value"] = 200;

        var map = obj.AddMap("Map");
        map.Add(1, "one");
        map.Add("two", 2);

        Assert.Equal(5, obj.Count);
        Assert.False(obj.ContainsKey("Name"));
        Assert.Equal("$.Tags[0]", tags[0].GetPath());
        Assert.Equal("$.Nested.Value", nested["Value"].GetPath());
        Assert.Equal(
            """{"Enabled":false,"Count":2,"Tags":["middle","beta"],"Nested":{"Value":200,"Active":true},"Map":{"1":"one","two":2}}""",
            obj.ToJsonString());
    }

    [Fact]
    public void ArrayDomOperationTest()
    {
        var arr = new DArray { 1, 2, 3 };

        arr.Insert(1, "inserted");
        arr[0] = true;
        var tail = arr[3];
        Assert.True(arr.Remove(tail));
        Assert.False(arr.Remove(99));

        var obj = arr.AddObject();
        obj.Add("Id", 10);
        obj.Add("Name", "item");

        var nested = arr.AddArray();
        nested.Add(false);
        nested.Add(1.000m);

        arr.RemoveRange(1, 1);

        Assert.Equal(4, arr.Count);
        Assert.Equal("$[2].Name", obj["Name"].GetPath());
        Assert.Equal("$[3][1]", nested[1].GetPath());
        Assert.Equal("""[true,2,{"Id":10,"Name":"item"},[false,1.000]]""", arr.ToJsonString());

        arr.Clear();

        Assert.Empty(arr);
        Assert.Equal("""[]""", arr.ToJsonString());
    }

    [Fact]
    public void ArrayDomReplaceContainerAndScalarWithInsertAndRemoveTest()
    {
        using var doc = JsonDocument.Parse("""{"Items":[{"Id":1},[2,3],"tail"]}""");
        var items = doc.RootElement.GetProperty("Items").AsArray();

        DumpTokens(doc);

        items[0].ReplaceWith("object-was-here");
        items[1].ReplaceWith(42);

        Assert.Equal("""{"Items":["object-was-here",42,"tail"]}""", doc.RootElement.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => items[0].AsObject());
        Assert.ThrowsAny<Exception>(() => items[1].AsArray());

        DumpTokens(doc);

        var insertedObject = new DObject { { "Created", true } };
        items.Insert(1, (DValue)insertedObject);

        DumpTokens(doc);

        items[2] = new DArray { "a", "b" };
        items.RemoveAt(0);

        Assert.Equal(3, items.Count);
        Assert.Equal("$.Items[0].Created", items[0].AsObject()["Created"].GetPath());
        Assert.Equal("$.Items[1][1]", items[1].AsArray()[1].GetPath());
        Assert.Equal("""{"Items":[{"Created":true},["a","b"],"tail"]}""", doc.RootElement.ToJsonString());
    }

    [Fact]
    public void ObjectDomReplaceScalarAndContainerWithAddAndRemoveTest()
    {
        var obj = new DObject
        {
            { "Node", 1 },
            { "Tail", "end" }
        };

        obj["Node"] = new DObject { { "A", 1 }, { "B", true } };
        Assert.Equal("""{"Node":{"A":1,"B":true},"Tail":"end"}""", obj.ToJsonString());

        obj["Node"].ReplaceWith("scalar");
        Assert.Throws<InvalidOperationException>(() => obj["Node"].AsObject());

        obj["Node"] = new DArray { 10, 20 };
        var nodeArray = obj["Node"].AsArray();
        nodeArray.Insert(1, "inserted");
        nodeArray.RemoveAt(0);

        obj["Node"].ReplaceWith(new { Values = new object[] { 1, "two" } });
        Assert.True(obj.Remove("Tail"));

        var tail = obj.AddArray("Tail");
        tail.Add(false);
        tail.AddObject().Add("Nested", "value");

        Assert.Equal(2, obj.Count);
        Assert.Equal("$.Tail[1].Nested", tail[1].AsObject()["Nested"].GetPath());
        Assert.Equal("""{"Node":{"Values":[1,"two"]},"Tail":[false,{"Nested":"value"}]}""", obj.ToJsonString());
    }

    [Fact]
    public void ReplaceObject()
    {
        var arr = new DArray();
        arr.Add(default);

        DumpTokens(arr.Document);

        Assert.Equal("[null]", arr.ToJsonString());

        var obj = new DObject { { "A", 1 }, { "B", true }, { "C", "ABC" } };

        for (var i = 0; i < 3; i++)
        {
            arr[0] = obj;
        }

        DumpTokens(arr.Document);

        Assert.Equal("""[{"A":1,"B":true,"C":"ABC"}]""", arr.ToJsonString());

        arr[0] = true;

        DumpTokens(arr.Document);

        Assert.Equal("""[true]""", arr.ToJsonString());

        arr.Add("A");
        arr.Add(123);
        arr.Add(123.45f);
        arr.Add(666.666);
        arr.Add("A");
        arr.Add(DValue.Create("{\"Nested\":true}"));

        DumpTokens(arr.Document);
    }

    [Fact]
    public void ReplaceTest()
    {
        using var doc = JsonDocument.Parse("[[1,2,3],[4,5,6],[7,8,9],[10,11,12]]");

        _output.WriteLine(doc.RootElement.ToString()!);

        DumpTokens(doc);

        var index = 0;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (index == 1)
            {
                element.AsValue().ReplaceWith(1.000m);
            }

            if (index == 3)
            {
                element.AsValue().ReplaceWith(2.000m);
            }

            index++;
        }

        DumpTokens(doc);

        Assert.Equal("[[1,2,3],1.000,[7,8,9],2.000]", doc.RootElement.ToString());
        _output.WriteLine(doc.RootElement.ToString()!);

        index = 0;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (index == 1)
            {
                element.AsValue().ReplaceWith(new[] { true, false });
            }

            index++;
        }

        Assert.Equal("[[1,2,3],[true,false],[7,8,9],2.000]", doc.RootElement.ToString());
        _output.WriteLine(doc.RootElement.ToString()!);

        index = 0;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (index == 0)
            {
                element.AsValue().ReplaceWith("ABC");
            }

            index++;
        }

        Assert.Equal("""["ABC",[true,false],[7,8,9],2.000]""", doc.RootElement.ToString());
        _output.WriteLine(doc.RootElement.ToString()!);

        Assert.Throws<InvalidOperationException>(() => doc.RootElement.AsObject());

        doc.RootElement.AsArray()[0] = 1.000m;
        doc.RootElement.AsArray()[1] = DValue.Create(new { Age = 32 });
        doc.RootElement.AsArray()[2] = DValue.Create(new byte[] { 1, 2, 3, 4 });

        Assert.Equal("""[1.000,{"Age":32},"AQIDBA==",2.000]""", doc.RootElement.ToString());
        _output.WriteLine(doc.RootElement.ToString()!);
    }

    [Fact]
    public void RemoveTest()
    {
        DArray arr;

        {
            using var doc = JsonDocument.Parse("[1,2,3]");

            arr = doc.RootElement.AsArray();

            var element0 = arr[0];
            var element1 = arr[1];
            var element2 = arr[2];

            _output.WriteLine(element1.ToString()!);
            _output.WriteLine(doc.RootElement.ToString()!);

            Assert.True(element1.IsValid);

            arr.RemoveAt(1);

            Assert.True(element0.IsValid);
            Assert.False(element1.IsValid);
            Assert.False(element2.IsValid);

            _output.WriteLine(doc.RootElement.ToString()!);

            element0 = arr[0];

            Assert.True(element0.IsValid);

            Assert.Throws<ObjectDisposedException>(() => element1.ReplaceWith(true));

            arr[1] = "Test";

            _output.WriteLine(doc.RootElement.ToString()!);

            arr.Add(1.000m);

            Assert.Equal("""[1,"Test",1.000]""", doc.RootElement.ToString());

            _output.WriteLine(doc.RootElement.ToString()!);

            Assert.True(element0.IsValid);

            arr.Insert(0, true);

            Assert.Equal("""[true,1,"Test",1.000]""", doc.RootElement.ToString());
            Assert.False(element0.IsValid);

            _output.WriteLine(doc.RootElement.ToString()!);

            arr[1].ReplaceWith("ABC");

            Assert.Equal("""[true,"ABC","Test",1.000]""", doc.RootElement.ToString());
            _output.WriteLine(doc.RootElement.ToString()!);
        }

        Assert.Throws<ObjectDisposedException>(() => arr.Add(true));
    }

    [Fact]
    public void ArrayIndexBoundaryTest()
    {
        var arr = new DArray { 1, 2, 3 };

        Assert.ThrowsAny<Exception>(() => arr[-1]);
        Assert.ThrowsAny<Exception>(() => arr[3]);
        Assert.ThrowsAny<Exception>(() => arr[-1] = true);
        Assert.ThrowsAny<Exception>(() => arr[3] = true);

        Assert.ThrowsAny<Exception>(() => arr.Insert(-1, true));
        Assert.ThrowsAny<Exception>(() => arr.Insert(4, true));

        arr.Insert(3, "tail");
        Assert.Equal("""[1,2,3,"tail"]""", arr.ToJsonString());

        Assert.ThrowsAny<Exception>(() => arr.RemoveAt(-1));
        Assert.ThrowsAny<Exception>(() => arr.RemoveAt(4));
    }

    [Fact]
    public void ArrayRemoveRangeBoundaryTest()
    {
        var arr = new DArray { 1, 2, 3, 4 };

        arr.RemoveRange(1, 0);
        Assert.Equal("""[1,2,3,4]""", arr.ToJsonString());

        arr.RemoveRange(2, 2);
        Assert.Equal("""[1,2]""", arr.ToJsonString());

        Assert.ThrowsAny<Exception>(() => arr.RemoveRange(-1, 1));
        Assert.ThrowsAny<Exception>(() => arr.RemoveRange(0, -1));
        Assert.ThrowsAny<Exception>(() => arr.RemoveRange(1, 10));
    }

    [Fact]
    public void ArrayClearInvalidatesExistingElementsTest()
    {
        var arr = new DArray { 1, 2, 3 };

        var e0 = arr[0];
        var e1 = arr[1];

        Assert.True(e0.IsValid);
        Assert.True(e1.IsValid);

        arr.Clear();

        Assert.False(e0.IsValid);
        Assert.False(e1.IsValid);
        Assert.Throws<ObjectDisposedException>(() => e0.ReplaceWith(true));
    }

    [Fact]
    public void ObjectDuplicateKeyTest()
    {
        var obj = new DObject
        {
            { "A", 1 }
        };

        Assert.ThrowsAny<Exception>(() => obj.Add("A", 2));

        obj["A"] = 3;
        Assert.Equal("""{"A":3}""", obj.ToJsonString());
    }


    [Fact]
    public void NullValueTest()
    {
        var obj = new DObject
        {
            { "A", DValue.Null },
            { "B", 1 }
        };

        Assert.Equal("""{"A":null,"B":1}""", obj.ToJsonString());

        obj["B"].ReplaceWith(DValue.Null);
        Assert.Equal("""{"A":null,"B":null}""", obj.ToJsonString());

        var arr = new DArray { DValue.Null, 1 };
        arr[1] = DValue.Null;

        Assert.Equal("""[null,null]""", arr.ToJsonString());
    }

    [Fact]
    public void ReplaceRootElementTest()
    {
        using var doc = JsonDocument.Parse("""{"A":1}""");

        doc.RootElement.AsValue().ReplaceWith(new[] { 1, 2, 3 });

        Assert.Equal("""[1,2,3]""", doc.RootElement.ToJsonString());
    }

    [Fact]
    public void SameObjectInsertedMultipleTimesShouldBeClonedOrMovedBySpecTest()
    {
        var obj = new DObject { { "A", 1 } };
        var arr = new DArray();

        arr.Add(obj);
        arr.Add(obj);

        // 仕様が「clone」なら両方残る
        Assert.Equal("""[{"A":1},{"A":1}]""", arr.ToJsonString());

        arr[0].AsObject()["A"] = 2;

        // clone なら片方だけ変わる
        Assert.Equal("""[{"A":2},{"A":1}]""", arr.ToJsonString());
    }


    /*
    [Fact]
    public void CannotCreateParentChildCycleTest()
    {
        var parent = new DoxObject();
        var child = parent.AddObject("Child");

        Assert.ThrowsAny<Exception>(() => child.Add("Parent", parent));
    }
    */

    /*
    [Fact]
    public void ModifyArrayDuringEnumerationTest()
    {
        var arr = new DoxArray { 1, 2, 3 };

        Assert.ThrowsAny<Exception>(() =>
        {
            foreach (var item in arr)
            {
                arr.Add(4);
            }
        });
    }
    */

    [Fact]
    public void ArrayPathAfterInsertAndRemoveTest()
    {
        var arr = new DArray();
        var a = arr.AddObject();
        a.Add("Name", "A");

        var b = arr.AddObject();
        b.Add("Name", "B");

        Assert.Equal("$[0].Name", a["Name"].GetPath());
        Assert.Equal("$[1].Name", b["Name"].GetPath());

        arr.Insert(0, true);

        Assert.Equal("$[1].Name", a["Name"].GetPath());
        Assert.Equal("$[2].Name", b["Name"].GetPath());

        arr.RemoveAt(1);

        Assert.False(a.IsValid);
        Assert.Equal("$[1].Name", b["Name"].GetPath());
    }

    [Fact]
    public void DeepCloneNestedContainerTest()
    {
        var obj = new DObject();
        var arr = obj.AddArray("Items");

        var child = arr.AddObject();
        child.Add("Name", "A");

        var clone = obj.DeepClone();

        clone["Items"].AsArray()[0].AsObject()["Name"] = "B";

        Assert.Equal("""{"Items":[{"Name":"A"}]}""", obj.ToJsonString());
        Assert.Equal("""{"Items":[{"Name":"B"}]}""", clone.ToJsonString());
    }

    [Fact]
    public void StringEscapeTest()
    {
        var obj = new DObject
        {
            { "Quote", "\"Hello\"" },
            { "Backslash", "\\" },
            { "NewLine", "A\nB" },
            { "Japanese", "こんにちは" },
            { "Emoji", "😀" }
        };

        Assert.Equal(
            """{"Quote":"\"Hello\"","Backslash":"\\","NewLine":"A\nB","Japanese":"こんにちは","Emoji":"😀"}""",
            obj.ToJsonString());
    }

    [Fact]
    public void NumberFormattingTest()
    {
        var arr = new DArray
        {
            0,
            -1,
            long.MaxValue,
            1234567890.123456789m,
            1.2300m
        };

        Assert.Equal("""[0,-1,9223372036854775807,1234567890.123456789,1.2300]""", arr.ToJsonString());
    }


    [Fact]
    public void ChildElementAfterDocumentDisposedTest()
    {
        DValue value;
        DObject obj;

        using (var doc = JsonDocument.Parse("""{"A":{"B":1}}"""))
        {
            obj = doc.RootElement.AsObject()["A"].AsObject();
            value = obj["B"];
        }

        Assert.Throws<ObjectDisposedException>(() => value.ReplaceWith(2));
        Assert.Throws<ObjectDisposedException>(() => obj.Add("C", 3));
    }

    [Fact]
    public void ObjectRemoveInvalidatesRemovedValueTest()
    {
        var obj = new DObject
        {
            { "A", 1 },
            { "B", 2 }
        };

        var a = obj["A"];

        Assert.True(a.IsValid);
        Assert.True(obj.Remove("A"));
        Assert.False(a.IsValid);

        Assert.Throws<ObjectDisposedException>(() => a.ReplaceWith(3));
        Assert.Equal("""{"B":2}""", obj.ToJsonString());
    }
}