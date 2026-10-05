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