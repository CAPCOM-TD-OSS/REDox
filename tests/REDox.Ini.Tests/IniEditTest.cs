using System;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Ini.Tests;

public sealed class IniEditTest
{
    private const string IniSimple = """
                                     ; Application configuration
                                     application_name = SampleApp
                                     [   window   ]
                                     width = 1920
                                     height = 1080
                                     """;

    private const string IniSample = """
                                     ; Application configuration
                                     # '#' style comments may also be supported

                                     application_name = SampleApp
                                     version = 1.0.0

                                     ; Window settings
                                     [window]
                                     ; Window title
                                     title = Sample Application
                                     ; Window size
                                     width = 1920
                                     height = 1080

                                     ; Graphics settings
                                     [graphics]
                                     renderer = Vulkan
                                     quality = high

                                     ; end of ini
                                     """;

    private static string NormalizeNewLines(string s)
    {
        return s.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    [Fact]
    public void TriviaEdit()
    {
        var map = new DMap();
        map.Add(123, "ABC");
        map.Add(true, "DEF");

        map[0].Key.LeadingTrivia.AddLast("===");
        map[1].Key.LeadingTrivia.AddLast("===");

        var ini = map.AsValue().ToIni(new IniWriteOptions { WriteSpaces = true, PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(ini);

        Assert.Equal("""
                     ;===
                     123 = ABC
                     ;===
                     True = DEF
                     """, NormalizeNewLines(ini));
    }

    [Fact]
    public void DomEdit()
    {
        var value = DValue.From(new { Flag = true, Name = "MyName", Age = 24, Date = new DateTime(1972, 8, 22) });

        var ini = value.ToIni(new IniWriteOptions { WriteSpaces = true });

        TestContext.Current.TestOutputHelper?.WriteLine(ini);

        Assert.Equal("""
                     Flag = true
                     Name = MyName
                     Age = 24
                     Date = 1972-08-22T00:00:00
                     """, NormalizeNewLines(ini));

        value.AsObject().GetPropertyEntry("Flag").LeadingTrivia.AddFirst("this is flag");
        value.AsObject().GetPropertyEntry("Name").LeadingTrivia.AddFirst("this is name");
        value.AsObject().GetPropertyEntry("Age").LeadingTrivia.AddFirst("this is age");
        value.AsObject().GetPropertyEntry("Date").LeadingTrivia.AddFirst("this is date");

        var ini2 = value.ToIni(new IniWriteOptions { WriteSpaces = true, PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(ini2);

        Assert.Equal("""
                     ;this is flag
                     Flag = true
                     ;this is name
                     Name = MyName
                     ;this is age
                     Age = 24
                     ;this is date
                     Date = 1972-08-22T00:00:00
                     """, NormalizeNewLines(ini2));
    }

    [Fact]
    public void DomEdit2()
    {
        var obj = new DObject();
        obj["Ini"] = DValue.Create(new Utf8Symbol("Hello"));
        obj["DT"] = DValue.Create(new DateTime(1972, 8, 22));
        obj["Bin"] = DValue.Create(new byte[] { 1, 2, 3 }, ByteStringKind.Base16);
        obj["Section"] = new DObject
        {
            { "A", 123 },
            { "B", 1.23 }
        };

        obj.GetPropertyEntry("Ini").LeadingTrivia.AddLast("===This is ini settings===", TriviaKind.LineComment);
        obj["Ini"].LeadingTrivia.AddLast("===", TriviaKind.LineComment);
        obj.GetPropertyEntry("DT").LeadingTrivia.AddLast("Timestamp", TriviaKind.LineComment);
        obj["DT"].LeadingTrivia.AddLast("------", TriviaKind.LineComment);

        var ini = obj.AsValue().ToIni(new IniWriteOptions { WriteSpaces = true, PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(ini);

        TestContext.Current.TestOutputHelper?.WriteLine((string?)obj["Bin"] ?? "null");
    }

    [Fact]
    public void DomEdit_ByteStringKinds_ShouldWriteExpectedText()
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var data = new byte[] { 1, 2, 3, 64, 128, 255 };
        var raw = new[] { (byte)'r', (byte)'a', (byte)'w', (byte)'-', (byte)'t', (byte)'e', (byte)'x', (byte)'t' };

        var obj = new DObject
        {
            ["Base16"] = DValue.Create(data, ByteStringKind.Base16),
            ["Base64"] = DValue.Create(data, ByteStringKind.Base64),
            ["Base64Url"] = DValue.Create(data, ByteStringKind.Base64Url),
            ["Guid"] = DValue.Create(guid),
            ["Raw"] = DValue.Create(raw, ByteStringKind.Raw)
        };

        var ini = obj.AsValue().ToIni(new IniWriteOptions { WriteSpaces = true });

        Assert.Equal("""
                     Base16 = 0102034080FF
                     Base64 = AQIDQID/
                     Base64Url = AQIDQID_
                     Guid = 00112233-4455-6677-8899-aabbccddeeff
                     Raw = raw-text
                     """, NormalizeNewLines(ini));
    }

    [Fact]
    public void DomEdit_MixedMutations_ShouldEncodeExpectedIni()
    {
        var root = DValue.ParseIni(IniSample, options: new IniDocumentOptions { PreserveTrivia = true }).AsObject();

        root["application_name"].ReplaceWith("EditedApp");
        Assert.True(root.Remove("version"));

        root["window"].AsObject()["width"].ReplaceWith(1280);
        root["window"].AsObject()["height"].ReplaceWith(720);
        root["window"].AsObject().Add("fullscreen", true);

        root["graphics"].AsObject()["renderer"].ReplaceWith("DirectX12");
        Assert.True(root["graphics"].AsObject().Remove("quality"));
        root["graphics"].AsObject()
            .Add("buffer", DValue.Create(new byte[] { 0x01, 0x02, 0xff }, ByteStringKind.Base16));

        root.AddObject("network").Add("port", 443);

        root["temporary"] = new DArray { 1, 2, 3 };
        root["disabled_section"] = new DObject { { "ignored", 1 } };
        root["disabled_section"].ReplaceWith("off");

        var ini = IniDocument.EncodeToString(root, new IniWriteOptions { WriteSpaces = true });

        Assert.Equal("""
                     application_name = EditedApp
                     disabled_section = off
                     [window]
                     title = Sample Application
                     width = 1280
                     height = 720
                     fullscreen = true
                     [graphics]
                     renderer = DirectX12
                     buffer = 0102FF
                     [network]
                     port = 443
                     """, NormalizeNewLines(ini));
    }

    [Fact]
    public void Json5ToIni_PreserveTrivia_ShouldConvertLineComments()
    {
        var json5 = """
                    {
                    //app
                    app:'Demo',
                    //window
                    window:{
                    //width
                    width:800
                    }
                    }
                    """;

        using var doc = Json5Document.Parse(json5, options: new Json5DocumentOptions { PreserveTrivia = true });

        var ini = IniDocument.EncodeToString(doc.RootElement,
            new IniWriteOptions { PreserveTrivia = true, WriteSpaces = true });

        Assert.Equal("""
                     ;app
                     app = Demo
                     ;window
                     [window]
                     ;width
                     width = 800
                     """, NormalizeNewLines(ini));

        using var reparsed = IniDocument.Parse(ini, options: new IniDocumentOptions { PreserveTrivia = true });

        Assert.Equal("Demo", reparsed.RootElement.GetProperty("app").GetString());
        Assert.Equal(800, (int)reparsed.RootElement.GetProperty("window").GetProperty("width").AsValue());
    }

    [Fact]
    public void IniToJson5_PreserveTrivia_ShouldNotEmitIniSeparators()
    {
        var ini = """
                  ;app
                  app = Demo
                  [window]
                  width = 800
                  """;

        using var doc = IniDocument.Parse(ini, options: new IniDocumentOptions { PreserveTrivia = true });

        var json5 = Json5Document.EncodeToString(doc.RootElement,
            new Json5WriteOptions { PreserveTrivia = true, WriteIndented = true });

        using var reparsed = Json5Document.Parse(json5, options: new Json5DocumentOptions { PreserveTrivia = true });

        Assert.Equal("Demo", reparsed.RootElement.GetProperty("app").GetString());
        Assert.Equal(800, (int)reparsed.RootElement.GetProperty("window").GetProperty("width").AsValue());
    }


    [Fact]
    public void JsonToIni()
    {
        var value = DValue.ParseJson("""
                                     {
                                        "param":123,
                                        "flag":true,
                                        "human":{"age":44},
                                        "rate":1.23,
                                        "map":{"any":"mode"},
                                        "arr":[1,2,3]
                                     }
                                     """);

        var ini = value.ToIni(new IniWriteOptions { WriteSpaces = true });

        Assert.Equal("""
                     param = 123
                     flag = true
                     rate = 1.23
                     [human]
                     age = 44
                     [map]
                     any = mode
                     """, NormalizeNewLines(ini));

        //with comment
        foreach (var prop in value.AsElement().EnumerateObject())
        {
            prop.LeadingTrivia.AddLast(prop.Value.ValueKind.ToString(), TriviaKind.LineComment);
            prop.Value.AsValue().LeadingTrivia.AddLast(prop.Value.ValueKind.ToString(), TriviaKind.LineComment);
        }

        var ini2 = value.ToIni(new IniWriteOptions { WriteSpaces = true, PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(ini2);

        Assert.Equal("""
                     ;Number
                     param = 123
                     ;True
                     flag = true
                     ;Number
                     rate = 1.23
                     ;Object
                     [human]
                     age = 44
                     ;Object
                     [map]
                     any = mode
                     """, NormalizeNewLines(ini2));
    }


    [Fact]
    public void DomEdit3()
    {
        var dox = DoxSerializer.Serialize(new
        {
            DateOfs = new DateTimeOffset(new DateTime(1222, 2, 1), TimeSpan.Zero),
            Date = new DateTime(1442, 4, 1),
            Id = Guid.Empty,
            Deci = 1.000m,
            Sgl = 1.23f,
            Haf = (Half)1.222,
            Arr = new byte[] { 1, 2, 3 },
            BigNum = Int128.MaxValue
        });

        using var doc = DoxDocument.Parse(dox);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            prop.LeadingTrivia.AddLast(prop.Value.Token.Variant.ToString(), TriviaKind.LineComment);
            prop.Value.AsValue().LeadingTrivia.AddLast("------", TriviaKind.LineComment);
        }

        var ini = doc.RootElement.ToIni(new IniWriteOptions { WriteSpaces = true, PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(ini);

        Assert.Equal("""
                     ;TimestampOffsetDateTime
                     DateOfs = 1222-02-01T00:00:00+00:00
                     ;Timestamp
                     Date = 1442-04-01T00:00:00
                     ;ByteStringGuid
                     Id = 00000000-0000-0000-0000-000000000000
                     ;FloatDecimal
                     Deci = 1.000
                     ;FloatSingle
                     Sgl = 1.23
                     ;FloatHalf
                     Haf = 1.222
                     ;ByteString
                     Arr = AQID
                     ;BigNumberInt128
                     BigNum = 170141183460469231731687303715884105727
                     """, NormalizeNewLines(ini));
    }


    [Fact]
    public void SimpleEditTrivia()
    {
        var root = DValue.ParseIni(IniSimple, options: new IniDocumentOptions { PreserveTrivia = true }).AsObject();

        var result = IniDocument.EncodeToString(root, new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal("""
                     ; Application configuration
                     application_name = SampleApp
                     [   window   ]
                     width = 1920
                     height = 1080
                     """, result);

        var result2 = IniDocument.EncodeToString(root);

        Assert.Equal("""
                     application_name=SampleApp
                     [window]
                     width=1920
                     height=1080
                     """, NormalizeNewLines(result2));

        if (root.TryGetPropertyEntry("application_name", out var prop))
        {
            foreach (var trivia in prop.LeadingTrivia)
            {
                if (trivia.Kind == TriviaKind.LineComment)
                {
                    trivia.ReplaceWith(trivia.GetString() + " & " + "UserComment");
                }
            }
        }

        if (root.TryGetPropertyEntry("window", out var window))
        {
//            window.LeadingTrivia.AddFirst("MyComment",TriviaKind.LineComment);

            if (window.Value.AsObject().TryGetPropertyEntry("width", out var width))
            {
                width.LeadingTrivia.AddLast("this is width", TriviaKind.LineComment);
                width.Value.AsValue().LeadingTrivia.AddLast("this is width", TriviaKind.LineComment);
                width.Value.AsValue().ReplaceWith(1280);
            }

            if (window.Value.AsObject().TryGetPropertyEntry("height", out var height))
            {
                height.LeadingTrivia.AddLast("this is height", TriviaKind.LineComment);
                height.Value.AsValue().ReplaceWith(720.12);
                height.Value.AsValue().LeadingTrivia.AddLast("this is height", TriviaKind.LineComment);
            }
        }


        var result3 = IniDocument.EncodeToString(root, new IniWriteOptions { PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(result3);

        Assert.Equal("""
                     ; Application configuration & UserComment
                     application_name = SampleApp
                     [   window]
                     ;this is width
                     width = 1280
                     ;this is height
                     height = 720.12
                     """, NormalizeNewLines(result3));
    }

    [Fact]
    public void SimpleEdit()
    {
        var root = DValue.ParseIni(IniSample, options: new IniDocumentOptions { PreserveTrivia = true }).AsObject();

        root["version"] = "2.0.0";
        root["graphics"].AsObject()["occlusion"] = true;

        if (!root.TryGetPropertyValue("sound", out var sound))
        {
            sound = root.AddObject("sound");
        }

        sound["volume"] = 100;

        var result =
            IniDocument.EncodeToString(root, new IniWriteOptions { PreserveTrivia = true, WriteSpaces = true });

        TestContext.Current.TestOutputHelper?.WriteLine(result);

        var result2 = IniDocument.EncodeToString(root["window"], new IniWriteOptions { PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine("---");
        TestContext.Current.TestOutputHelper?.WriteLine(result2);
    }
}