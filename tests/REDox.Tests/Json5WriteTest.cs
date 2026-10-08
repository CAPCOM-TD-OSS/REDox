using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public class Json5WriteTest
{
    private static readonly SerializerSettings s_settings = new DoxSerializerSettings
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = NamingPolicy.KebabCaseLower
    };

    private readonly ITestOutputHelper _output;

    public Json5WriteTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PreserveTriviaInObject()
    {
        var json5 = """
                    {
                        //Any string                            
                        value : "test",
                        //Any number
                        hex : 0xffff,
                        //bin
                    }
                    """;

        using var doc = Json5Document.Parse(json5, options: new Json5DocumentOptions { PreserveTrivia = true });

        doc.RootElement.AsObject().Remove("value");

        var result = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true });

        using var doc2 = Json5Document.Parse(result, options: new Json5DocumentOptions { PreserveTrivia = true });
    }

    [Theory]
    [InlineData("{a:1,b:2,c:3}", "a", "{\"b\":2,\"c\":3}")]
    [InlineData("{a:1,b:2,c:3}", "b", "{\"a\":1,\"c\":3}")]
    [InlineData("{a:1,b:2,c:3}", "c", "{\"a\":1,\"b\":2}")]
    [InlineData("{a:1,b:2,c:3,}", "a", "{\"b\":2,\"c\":3}")]
    [InlineData("{a:1,b:2,c:3,}", "c", "{\"a\":1,\"b\":2}")]
    [InlineData("{\n  //a\n  a:1, // after a\n  /* b */ b:2,\n}", "a", "{\"b\":2}")]
    [InlineData("{\n  //a\n  a:1, // after a\n  /* b */ b:2,\n}", "b", "{\"a\":1}")]
    [InlineData("{x:{a:1,b:2},y:3}", "x", "{\"y\":3}")]
    public void PreserveTriviaRemoveObjectMember(string json5, string key, string expected)
    {
        var options = new Json5DocumentOptions { PreserveTrivia = true };

        using var doc = Json5Document.Parse(json5, options: options);

        doc.RootElement.AsObject().Remove(key);

        var result = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true });

        _output.WriteLine(result);

        using var doc2 = Json5Document.Parse(result, options: options);

        Assert.Equal(expected, doc2.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("[1,2,3]", 0, "[2,3]")]
    [InlineData("[1,2,3]", 1, "[1,3]")]
    [InlineData("[1,2,3]", 2, "[1,2]")]
    [InlineData("[1,2,3,]", 0, "[2,3]")]
    [InlineData("[1,2,3,]", 2, "[1,2]")]
    [InlineData("[\n  //1\n  1, // after 1\n  /* 2 */ 2,\n]", 0, "[2]")]
    [InlineData("[[1,2],{a:1},3]", 0, "[{\"a\":1},3]")]
    [InlineData("[[1,2],{a:1},3]", 1, "[[1,2],3]")]
    public void PreserveTriviaRemoveArrayElement(string json5, int index, string expected)
    {
        var options = new Json5DocumentOptions { PreserveTrivia = true };

        using var doc = Json5Document.Parse(json5, options: options);

        doc.RootElement.AsArray().RemoveAt(index);

        var result = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true });

        _output.WriteLine(result);

        using var doc2 = Json5Document.Parse(result, options: options);

        Assert.Equal(expected, doc2.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("{a:{b:1,c:2}}", "c", "{\"a\":{\"b\":1}}")]
    [InlineData("{a:{b:1,c:2}}", "b", "{\"a\":{\"c\":2}}")]
    [InlineData("{a:{b:1,c:2,},}", "b", "{\"a\":{\"c\":2}}")]
    public void PreserveTriviaRemoveNestedObjectMember(string json5, string key, string expected)
    {
        var options = new Json5DocumentOptions { PreserveTrivia = true };

        using var doc = Json5Document.Parse(json5, options: options);

        doc.RootElement.AsObject()["a"].AsObject().Remove(key);

        var result = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true });

        _output.WriteLine(result);

        using var doc2 = Json5Document.Parse(result, options: options);

        Assert.Equal(expected, doc2.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("{a:1}", "a")]
    [InlineData("{a:1,}", "a")]
    [InlineData("{\n  // only\n  a:1,\n}", "a")]
    public void PreserveTriviaRemoveAllObjectMembers(string json5, string key)
    {
        var options = new Json5DocumentOptions { PreserveTrivia = true };

        using var doc = Json5Document.Parse(json5, options: options);

        doc.RootElement.AsObject().Remove(key);

        var result = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true });

        _output.WriteLine(result);

        using var doc2 = Json5Document.Parse(result, options: options);

        Assert.Equal("{}", doc2.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("[1]", 0)]
    [InlineData("[1,]", 0)]
    public void PreserveTriviaRemoveAllArrayElements(string json5, int index)
    {
        var options = new Json5DocumentOptions { PreserveTrivia = true };

        using var doc = Json5Document.Parse(json5, options: options);

        doc.RootElement.AsArray().RemoveAt(index);

        var result = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true });

        _output.WriteLine(result);

        using var doc2 = Json5Document.Parse(result, options: options);

        Assert.Equal("[]", doc2.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(70)]
    [InlineData(130)]
    public void PreserveTriviaRemoveDeepNestedElement(int depth)
    {
        var json5 = "[" + new string('[', depth) + "1" + new string(']', depth) + ",2]";

        var options = new Json5DocumentOptions { PreserveTrivia = true, MaxDepth = 256 };

        using var doc = Json5Document.Parse(json5, options: options);

        doc.RootElement.AsArray().RemoveAt(0);

        var result = Json5Document.EncodeToString(doc.RootElement,
            new Json5WriteOptions { PreserveTrivia = true, MaxDepth = 256 });

        _output.WriteLine(result);

        using var doc2 = Json5Document.Parse(result, options: options);

        Assert.Equal("[2]", doc2.RootElement.ToJsonString());
    }

    [Fact]
    public void PreserveTriviaScriptedAddRemoveCycle()
    {
        var json5 = """
                    // header
                    {
                      // a comment
                      a: 1, // trailing a
                      /* b */ b: [ /* first */ 1, 2, /* third */ 3, ], // after b
                      c: { x: 1, /* y */ y: 2, }, // after c
                      d: 'text', // last
                    }
                    """;

        var options = new Json5DocumentOptions { PreserveTrivia = true };
        var writeOptions = new Json5WriteOptions { PreserveTrivia = true };

        var doc = Json5Document.Parse(json5, options: options);

        try
        {
            void Cycle(string expected)
            {
                var result = Json5Document.EncodeToString(doc.RootElement, writeOptions);
                _output.WriteLine(result);

                var next = Json5Document.Parse(result, options: options);
                doc.Dispose();
                doc = next;

                AssertJsonEqual(expected, doc.RootElement.ToJsonString());

                Assert.Equal(result, Json5Document.EncodeToString(doc.RootElement, writeOptions));
            }

            var root = doc.RootElement.AsObject();
            root.Remove("a");
            root["b"].AsArray().RemoveAt(1);
            root["c"].AsObject().Remove("x");
            Cycle("""{"b":[1,3],"c":{"y":2},"d":"text"}""");

            root = doc.RootElement.AsObject();
            root["e"] = 5;
            root["b"].AsArray().Insert(0, 0);
            root["b"].AsArray().Add(4);
            root["c"].AsObject()["z"] = new DArray(1, 2);
            Cycle("""{"b":[0,1,3,4],"c":{"y":2,"z":[1,2]},"d":"text","e":5}""");

            root = doc.RootElement.AsObject();
            root.Remove("d");
            root.Remove("e");
            root["b"].AsArray().RemoveAt(3);
            root["b"].AsArray().RemoveAt(0);
            root["c"].AsObject()["z"].AsArray().RemoveAt(1);
            Cycle("""{"b":[1,3],"c":{"y":2,"z":[1]}}""");

            root = doc.RootElement.AsObject();
            root["b"].AsArray().Clear();
            root["c"].AsObject().Clear();
            Cycle("""{"b":[],"c":{}}""");

            root = doc.RootElement.AsObject();
            root["b"].AsArray().Add(new DObject { ["k"] = 1 });
            root["b"].AsArray().Add(2);
            root["c"].AsObject()["n"] = new DObject { ["m"] = new DArray(1) };
            Cycle("""{"b":[{"k":1},2],"c":{"n":{"m":[1]}}}""");

            root = doc.RootElement.AsObject();
            root["b"].AsArray()[0].ReplaceWith(new DArray(9));
            root["c"].AsObject()["n"].ReplaceWith(7);
            root.Remove("b");
            Cycle("""{"c":{"n":7}}""");

            root = doc.RootElement.AsObject();
            root.Remove("c");
            Cycle("{}");

            root = doc.RootElement.AsObject();
            root["a"] = 1;
            root["b"] = new DArray(1, 2);
            Cycle("""{"a":1,"b":[1,2]}""");
        }
        finally
        {
            doc.Dispose();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(1234)]
    public void PreserveTriviaRandomAddRemove(int seed)
    {
        var json5 = """
                    /* top */
                    {
                      // obj
                      obj: { a: 1, /* b */ b: 2, c: 3, }, // after obj
                      /* arr */ arr: [ 1, /* two */ 2, 3, // three
                        4, ],
                      last: null, // end
                    }
                    """;

        var options = new Json5DocumentOptions { PreserveTrivia = true, MaxDepth = 256 };
        var writeOptions = new Json5WriteOptions { PreserveTrivia = true, MaxDepth = 256 };

        var random = new System.Random(seed);
        var doc = Json5Document.Parse(json5, options: options);
        var model = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.ToJsonString())!.AsObject();
        var nextKey = 0;

        try
        {
            for (var step = 0; step < 300; step++)
            {
                var root = doc.RootElement.AsObject();
                var obj = root["obj"].AsObject();
                var arr = root["arr"].AsArray();
                var mObj = model["obj"]!.AsObject();
                var mArr = model["arr"]!.AsArray();

                switch (random.Next(9))
                {
                    case 0:
                    {
                        var key = "k" + nextKey++;
                        var value = random.Next(100);
                        obj[key] = value;
                        mObj[key] = value;
                        break;
                    }
                    case 1 when obj.Count > 0:
                    {
                        var key = System.Linq.Enumerable.ElementAt(mObj, random.Next(mObj.Count)).Key;
                        Assert.True(obj.Remove(key));
                        mObj.Remove(key);
                        break;
                    }
                    case 2:
                    {
                        var index = random.Next(arr.Count + 1);
                        var value = random.Next(100);
                        arr.Insert(index, value);
                        mArr.Insert(index, value);
                        break;
                    }
                    case 3 when arr.Count > 0:
                    {
                        var index = random.Next(arr.Count);
                        arr.RemoveAt(index);
                        mArr.RemoveAt(index);
                        break;
                    }
                    case 4:
                    {
                        var value = random.Next(100);
                        arr.Add(new DObject { ["v"] = value, ["w"] = new DArray(value) });
                        mArr.Add(new System.Text.Json.Nodes.JsonObject
                        {
                            ["v"] = value,
                            ["w"] = new System.Text.Json.Nodes.JsonArray(value)
                        });
                        break;
                    }
                    case 5 when arr.Count > 0:
                    {
                        var index = random.Next(arr.Count);
                        var value = random.Next(100);
                        arr[index].ReplaceWith(new DArray(value, value + 1));
                        mArr[index] = new System.Text.Json.Nodes.JsonArray(value, value + 1);
                        break;
                    }
                    case 6 when random.Next(8) == 0:
                        arr.Clear();
                        mArr.Clear();
                        break;
                    case 7 when random.Next(8) == 0:
                        obj.Clear();
                        mObj.Clear();
                        break;
                    case 8 when obj.Count > 0:
                    {
                        var key = System.Linq.Enumerable.ElementAt(mObj, random.Next(mObj.Count)).Key;
                        var value = random.Next(100);
                        obj[key].ReplaceWith(new DObject { ["r"] = value });
                        mObj[key] = new System.Text.Json.Nodes.JsonObject { ["r"] = value };
                        break;
                    }
                }

                if (step % 5 != 4)
                {
                    continue;
                }

                var result = Json5Document.EncodeToString(doc.RootElement, writeOptions);

                Json5Document next;
                try
                {
                    next = Json5Document.Parse(result, options: options);
                }
                catch (System.Exception e)
                {
                    _output.WriteLine($"seed={seed} step={step}");
                    _output.WriteLine(result);
                    throw new Xunit.Sdk.XunitException($"Invalid JSON5 at step {step}: {e.Message}\n{result}");
                }

                doc.Dispose();
                doc = next;

                AssertJsonEqual(model.ToJsonString(), doc.RootElement.ToJsonString());
            }

            _output.WriteLine(Json5Document.EncodeToString(doc.RootElement, writeOptions));
        }
        finally
        {
            doc.Dispose();
        }
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        var e = System.Text.Json.Nodes.JsonNode.Parse(expected);
        var a = System.Text.Json.Nodes.JsonNode.Parse(actual);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(e, a), $"expected: {expected}\nactual:   {actual}");
    }

    [Fact]
    public void PreserveTrivia()
    {
        var json5 = """
                    //Json5 
                    [
                    //a
                    {
                    b:{
                    c:{
                    value:"test"
                    }}},123
                    ]
                    """;

        var writeOptions = new Json5WriteOptions
        {
            PreserveTrivia = true
        };

        using var doc = Json5Document.Parse(json5, options: new Json5DocumentOptions { PreserveTrivia = true });

        var exceptions = doc.GetValueValidationErrors();

        foreach (var ex in exceptions)
        {
            _output.WriteLine(ex.ToString());
        }

        DomTestHelper.DumpTokens(_output, doc);

        var result = Json5Document.EncodeToString(doc.RootElement, writeOptions);

        Assert.Equal(json5, result);
        TestContext.Current.TestOutputHelper?.WriteLine(result);

        var arr = doc.RootElement.AsArray();
//        arr[1] = false;
        arr[1].LeadingTrivia.Clear();

        result = Json5Document.EncodeToString(doc.RootElement, writeOptions);

        TestContext.Current.TestOutputHelper?.WriteLine(result);

        TestContext.Current.TestOutputHelper?.WriteLine("===");

        foreach (var trivia in arr[1].LeadingTrivia)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(trivia.GetString());
        }

        TestContext.Current.TestOutputHelper?.WriteLine("===");
    }

    [Fact]
    public void ParseTrivia()
    {
        var json5 = """
                    //Json5 
                    {
                        //Any string                            
                        value : "test",
                        //Any number
                        /*b*/ number /*aaa*/ : /*magic*/ 123 /*c*/,
                        //hex
                        hex : 0xffff,
                        //bin
                        bin : 123,
                        //Single quat
                        'function' : 'foo'
                    }
                    """;
        var writeOptions = new Json5WriteOptions
        {
            PreserveTrivia = true
        };

        using var doc = Json5Document.Parse(json5, options: new Json5DocumentOptions { PreserveTrivia = true });

        var exceptions = doc.GetValueValidationErrors();

        foreach (var ex in exceptions)
        {
            _output.WriteLine(ex.ToString());
        }

        DomTestHelper.DumpTokens(_output, doc);

        var result = Json5Document.EncodeToString(doc.RootElement, writeOptions);

        Assert.Equal(json5, result);

        TestContext.Current.TestOutputHelper?.WriteLine(result);

        if (doc.RootElement.TryGetProperty("value", out var val))
        {
            val.AsValue().ReplaceWith(DValue.Create("my name", StringKind.SingleQuote));
            Assert.Equal("'my name'", Json5Document.EncodeToString(val));
        }

        if (doc.RootElement.TryGetProperty("hex", out var hex))
        {
            hex.AsValue().ReplaceWith(123);
            Assert.Equal("0x7b", Json5Document.EncodeToString(hex));
        }

        if (doc.RootElement.TryGetProperty("function", out var func))
        {
            func.AsValue().ReplaceWith("Abc");
            Assert.Equal("'Abc'", Json5Document.EncodeToString(func));
        }

        if (doc.RootElement.TryGetProperty("number", out var number))
        {
            var leadingTrivia = number.AsValue().LeadingTrivia;

            foreach (var trivia in leadingTrivia)
            {
                _output.WriteLine($"{trivia.Kind} {trivia}");
            }

            number.AsValue().ReplaceWith(567);

            foreach (var trivia in leadingTrivia)
            {
                _output.WriteLine($"{trivia.Kind} {trivia}");
            }
        }

        doc.RootElement.AsObject()["bin"] = DValue.Create(new byte[] { 1, 2, 3, 4, 5 }, ByteStringKind.Base16);

        Assert.Equal(@"""0102030405""", Json5Document.EncodeToString(doc.RootElement.AsObject()["bin"]));

        doc.RootElement.AsObject()["bin"].ReplaceWith(new byte[] { 5, 4, 3 });

        Assert.Equal(@"""050403""", Json5Document.EncodeToString(doc.RootElement.AsObject()["bin"]));

        result = Json5Document.EncodeToString(doc.RootElement, writeOptions);

        TestContext.Current.TestOutputHelper?.WriteLine(result);

//        leadingTrivia.AddLineComment();
    }

    [Fact]
    public void EncodeTest()
    {
        var options = new Json5WriteOptions
        {
            WriteIndented = true
        };

        var obj = new DObject();
        obj["Param"] = 123;
        obj["Name"] = "MyName";
        obj["Detail"] = DValue.Create(new { Age = 32, Mail = false });

        var json5 = Json5Document.EncodeToString(obj, options with
        {
            WriteIndented = true,
            PropertyNameStyle = Json5QuoteStyle.AutoOrSingle,
            StringStyle = Json5QuoteStyle.AlwaysDouble
        });

        TestContext.Current.TestOutputHelper?.WriteLine(json5);

        using var doc = Json5Document.Parse(json5, SerializerSettings.Default);

        var json = Json5Document.EncodeToString(doc.RootElement, options);

        TestContext.Current.TestOutputHelper?.WriteLine(json);

        var json2 = Json5Document.EncodeToString(doc.RootElement, options with
        {
            PropertyNameStyle = Json5QuoteStyle.AlwaysDouble,
            StringStyle = Json5QuoteStyle.AlwaysDouble
        });

        TestContext.Current.TestOutputHelper?.WriteLine(json2);

        var json3 = JsonDocument.EncodeToString(doc.RootElement,
            new JsonWriteOptions { WriteIndented = true });

        TestContext.Current.TestOutputHelper?.WriteLine(json3);

        Assert.Equal(json2, json3);

        var value1 = JsonDocument.EncodeToString(obj["Name"]);
        var value2 = Json5Document.EncodeToString(obj["Name"], options with
        {
            PropertyNameStyle = Json5QuoteStyle.AlwaysSingle,
            StringStyle = Json5QuoteStyle.AlwaysSingle
        });

        Assert.NotEqual(value1, value2);

        var value3 = Json5Document.EncodeToString(obj["Detail"], options with
        {
            PropertyNameStyle = Json5QuoteStyle.AlwaysSingle,
            StringStyle = Json5QuoteStyle.AlwaysSingle
        });

        TestContext.Current.TestOutputHelper?.WriteLine(value3);

        var result = doc.RootElement.To(new { Name = "", Detail = new { Age = 0 } });

        TestContext.Current.TestOutputHelper?.WriteLine(result.ToString()!);

        Assert.Equal("MyName", result.Name);
        Assert.Equal(32, result.Detail.Age);

        var obj2 = DValue.From(result, s_settings);

        TestContext.Current.TestOutputHelper?.WriteLine(obj2.ToJsonString());

        obj2["Age"] = 99;

        TestContext.Current.TestOutputHelper?.WriteLine(obj2.ToJsonString());

        var arr = new DArray();
        arr.Add(obj);
        arr.Add(obj2);
        arr.Add(doc.RootElement);
        arr.Add(true);

        TestContext.Current.TestOutputHelper?.WriteLine(arr.ToJsonString(new JsonWriteOptions
            { WriteIndented = true }));
    }
}