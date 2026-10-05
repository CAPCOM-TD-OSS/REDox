using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class JsonElementCompatibility
{
    public enum Mode
    {
        Strict,
        Relaxed,
        RelaxedFormat,
        Nullable,
        Failed,
        FailedFormat
    }

    [Fact]
    public void DisposeTest()
    {
        JsonElement rootElement1 = default;
        {
            using var doc = System.Text.Json.JsonDocument.Parse("[1,2,3]");

            rootElement1 = doc.RootElement;
        }

        DElement rootElement2 = default;
        {
            using var doc = Json.JsonDocument.Parse("[1,2,3]", SerializerSettings.Default);

            rootElement2 = doc.RootElement;
        }

        Assert.Throws<ObjectDisposedException>(() => { rootElement1.ToString(); });
        Assert.Throws<ObjectDisposedException>(() => { rootElement1.GetArrayLength(); });

        Assert.Throws<ObjectDisposedException>(() => { rootElement2.ToString(); });

        Assert.Throws<ObjectDisposedException>(() => { rootElement2.GetArrayLength(); });
    }

    [Fact]
    public void CreateObject()
    {
        var obj = new DObject();
        obj.Capacity = 4;
        obj["id"] = 123;
        obj["name"] = "My name";
        obj["age"] = 46;
        obj["mail"] = true;
    }

    [Fact]
    public void CreateArray()
    {
        var arr = new DArray();
        arr.Capacity = 4;
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);

        arr[0] = 123;
        arr[1] = "My name";
        arr[2] = 46;
        arr[3] = true;
    }

    private static void AssertRelaxedScalarConversion<T>(
        string json,
        Type strictExceptionType,
        Func<DValue, T> convert,
        T expected)
    {
        var strict = DValue.ParseJson(json, new DoxSerializerSettings
        {
            AllowRelaxedScalarConversion = false
        });
        var relaxed = DValue.ParseJson(json, new DoxSerializerSettings
        {
            AllowRelaxedScalarConversion = true
        });

        var strictException = Record.Exception(() => convert(strict));
        Assert.NotNull(strictException);
        Assert.Equal(strictExceptionType, strictException.GetType());
        Assert.Equal(expected, convert(relaxed));
    }

    [Theory]
    [InlineData(@"""\x""", Mode.Failed)]
    [InlineData(@"""\u12G4""", Mode.Failed)]
    [InlineData(@"""\u123""", Mode.Failed)]
    [InlineData(@"""\u12""", Mode.Failed)]
    [InlineData(@"""\u1""", Mode.Failed)]
    [InlineData(@"""\u""", Mode.Failed)]
    //        [InlineData(@"""\uD800""", Mode.FailedFormat)]      // 上位単独
    //        [InlineData(@"""\uDC00""", Mode.FailedFormat)]      // 下位単独
    //[InlineData(@"""\uD83D\u0000""", Mode.FailedFormat)]
    public void InvalidStringTest(string json, Mode mode)
    {
        if (mode == Mode.Failed)
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(json);
                Assert.Fail("JsonException (またはその派生) が投げられる想定でした。");
            }
            catch (System.Text.Json.JsonException)
            {
            }

            try
            {
                JsonNode.Parse(json);
                Assert.Fail("JsonException (またはその派生) が投げられる想定でした。");
            }
            catch (System.Text.Json.JsonException)
            {
            }

            Assert.Throws<JsonReaderException>(() => JToken.Parse(json));

            try
            {
                Json.JsonDocument.Parse(json, SerializerSettings.Default,
                    new Json.JsonDocumentOptions { EnableValueValidation = true });
                Assert.Fail("DocumentParseException (またはその派生) が投げられる想定でした。");
            }
            catch (DocumentParseException)
            {
            }

            var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

            try
            {
                dox.GetString();
                var str = (string?)dox.AsValue();

                Assert.Fail("DocumentParseException (またはその派生) が投げられる想定でした。");
            }
            catch (DocumentParseException)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<InvalidOperationException>(() =>
                System.Text.Json.JsonDocument.Parse(json).RootElement.GetString());
            Assert.Throws<InvalidOperationException>(() => (string?)JsonNode.Parse(json));
            var text = (string?)JToken.Parse(json);

            var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

            var text1 = dox.GetString();
            var text2 = (string?)dox.AsValue();

            Assert.Equal(text, text1);
            Assert.Equal(text, text2);
        }
    }

    [Theory]
    [InlineData(@"""ABC""", Mode.Strict)]
    [InlineData(@"""あいう""", Mode.Strict)]
    [InlineData(@"""\r\n\t  aa\n""", Mode.Strict)]
    [InlineData(@"""\u0050\u0050\u0050""", Mode.Strict)]
    [InlineData(@"""""", Mode.Strict)]
    [InlineData(@"""a""", Mode.Strict)]
    [InlineData(@"""\u0022""", Mode.Strict)] // ＝ "\""
    [InlineData(@"""\\""", Mode.Strict)] // ＝ "\"
    [InlineData(@"""   """, Mode.Strict)]
    [InlineData(@"""\t""", Mode.Strict)]
    [InlineData(@"""\n""", Mode.Strict)]
    [InlineData(@"""\b""", Mode.Strict)]
    [InlineData(@"""\f""", Mode.Strict)]
    [InlineData(@"""\/""", Mode.Strict)]
    [InlineData(@"""a\/b""", Mode.Strict)]
    [InlineData(@"""\""quoted\""""", Mode.Strict)] // "quoted"
    [InlineData(@"""\u3042\u3044\u3046""", Mode.Strict)] // あいう
    [InlineData(@"""\uD83D\uDE00""", Mode.Strict)] // 😀（正しいサロゲート）
    [InlineData(@"""😀""", Mode.Strict)]
    [InlineData(@"""e\u0301""", Mode.Strict)] // e + 結合アキュート（NFD想定）
    [InlineData(@"""\u00E9""",
        Mode.Strict)] // é（NFC）                                                           // バックスラッシュ自身のテスト ("\\")
    [InlineData(@"""\""""", Mode.Strict)]
    [InlineData(@"""🍎""", Mode.Strict)]
    [InlineData(@"""\u0000""", Mode.Strict)]
    [InlineData(@"  "" trim check ""  ", Mode.Strict)]
    [InlineData(@"""C:\\temp\\file.txt""", Mode.Strict)]
    [InlineData(@"""C:\temp\file.txt""", Mode.Strict)] // C# ソース上は \t がタブ。@ verbatim でなく通常だと注意。
    [InlineData(@"""https:\/\/example.com\/a\/b""", Mode.Strict)]
    [InlineData(@"""https://example.com/a/b""", Mode.Strict)]
    [InlineData("  \n\r\t\"abc\"  \t", Mode.Strict)]
    [InlineData(@"true", Mode.Relaxed)]
    [InlineData(@"false", Mode.Relaxed)]
    [InlineData(@"123", Mode.Relaxed)]
    [InlineData(@"123.45", Mode.Relaxed)]
    [InlineData(@"-123", Mode.Relaxed)]
    [InlineData(@"-123.45", Mode.Relaxed)]
    [InlineData(@"1.2e3", Mode.Relaxed)]
    [InlineData(@"1.2E+3", Mode.Relaxed)]
    [InlineData(@"1e-2", Mode.Relaxed)]
    [InlineData(@"0", Mode.Relaxed)]
    [InlineData(@"123456789876543212345678987654321", Mode.Relaxed)]
    [InlineData(@"null", Mode.Strict)] // (string?)ntj は null, stj.GetString() は InvalidOperation の想定
    [InlineData(@"""null""", Mode.Strict)] // 文字列 "null"
    //[InlineData(@"0123", Mode.FailedFormat)]       // 標準JSONでは先頭ゼロ整数は不可（多くは Parse 例外）
    [InlineData(@"1e6", Mode.Relaxed)]
    [InlineData(@"-1E-3", Mode.Relaxed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"{}", Mode.Failed)]
    public void StringTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetString(), dox.GetString());
            Assert.Equal((string?)stn, (string?)dox.AsValue());
            Assert.Equal((string?)ntj, (string?)dox.AsValue());
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetString());
            Assert.Throws<InvalidOperationException>(() => (string?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetString());

            Assert.Equal((string?)ntj, (string?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (string?)value,
                (string?)ntj);
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetString());
            Assert.Throws<InvalidOperationException>(() => dox.GetString());
            Assert.Throws<InvalidOperationException>(() => (string?)dox.AsValue());

            try
            {
                var _ = (string?)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetString());
            Assert.Throws<FormatException>(() => dox.GetString());
            Assert.Throws<InvalidOperationException>(() => (string?)dox.AsValue());

            try
            {
                var _ = (string?)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Fact]
    public void GuidTest2()
    {
        var guid1 = new Guid("01234567-89AB-CDEF-0123-456789ABCDEF");
        var guid2 = new Guid("089ABDEF-0123-4567-0123-456789ABCDEF");

        var doc = Json.JsonDocument.Parse($"\"{guid1}\"", SerializerSettings.Default);

        var value = doc.RootElement;

        Assert.Equal(guid1, value.GetGuid());

        value.AsValue().ReplaceWith(guid2);

        Assert.Equal(guid2, value.GetGuid());

        Console.WriteLine(value.ToString());

        Assert.Equal(guid2.ToString(), value.ToString());

        value.AsValue().ReplaceWith("0123456789ABCDEF0123456789ABCDEF");

        //exact
        Assert.Throws<FormatException>(() => value.GetGuid());
        Assert.False(value.TryGetGuid(out var result));

        //relax
        Assert.Equal((Guid)value.AsValue(), guid1);

        //invalid binary
        value.AsValue().ReplaceWith(new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF });
        Assert.Throws<FormatException>(() => value.GetGuid());
        Console.WriteLine(value.ToString());

        //guid binary
        value.AsValue().ReplaceWith(new byte[]
            { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF });
        Assert.Equal(guid1, value.GetGuid());
        Assert.Equal((Guid)value.AsValue(), guid1);
        Console.WriteLine(value.ToString());

        value.AsValue()
            .ReplaceWith(DValue.Create(
                new byte[]
                {
                    0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF
                }, ByteStringKind.Guid));

        Assert.Equal(guid1, value.GetGuid());
        Assert.Equal((Guid)value.AsValue(), guid1);
        Console.WriteLine(value.ToString());

        var json = Json.JsonSerializer.Serialize(guid1, SerializerSettings.Default);

        Assert.Equal(json, "\"" + guid1 + "\"");

        var json2 = value.ToJsonString();

        Console.WriteLine(json2);

        var node1 = JsonNode.Parse(json2)!;
        var node2 = Json.JsonDocument.Parse(json2, SerializerSettings.Default).RootElement;

        Assert.Equal(node1.ToString(), node2.ToString());
        Assert.Equal((Guid)node1, (Guid)node2.AsValue());
        Assert.Equal(node1.ToJsonString(), node2.ToJsonString());

        var node3 = DValue.Create(guid1);

        Assert.Equal((Guid)node3, guid1);
        Assert.Equal(node3.ToString(), guid1.ToString());
    }

    [Fact]
    public void ValueKindTest()
    {
        var json = """
                   [123,true,false,15.0,"ABC","DEF",null,[1,2,3],{"name":"myname","age":55},[true,false],555,"test"]
                   """;

        var stn = JsonNode.Parse(json)?.AsArray();
        var redox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();

        for (var i = 0; i < redox.Count; i++)
        {
            var node = stn?[i];

            var stjKind = node != null ? node.GetValueKind() : System.Text.Json.JsonValueKind.Null;
            var doxKind = redox[i].GetValueKind();

            if ((int)stjKind != (int)doxKind)
            {
                redox[i].GetValueKind();
                Console.WriteLine($"Index {i}: STJ={stjKind} DOX={doxKind}");
            }

            Assert.Equal((int)stjKind, (int)doxKind);
        }
    }

    [Theory]
    [InlineData("true", Mode.Strict)]
    [InlineData("false", Mode.Strict)]
    [InlineData(@"""true""", Mode.Relaxed)]
    [InlineData(@"""false""", Mode.Relaxed)]
    [InlineData(@"""True""", Mode.Relaxed)]
    [InlineData(@"""trUe""", Mode.Relaxed)]
    [InlineData(@"""fAlsE""", Mode.Relaxed)]
    [InlineData(@"""False""", Mode.Relaxed)]
    [InlineData(@"""TRUE""", Mode.Relaxed)]
    [InlineData(@"""FALSE""", Mode.Relaxed)]
    [InlineData(@"123", Mode.Relaxed)]
    [InlineData(@"0", Mode.Relaxed)]
    [InlineData(@"0.0", Mode.Relaxed)]
    [InlineData(@"-123", Mode.Relaxed)]
    [InlineData(@"123.45", Mode.Relaxed)]
    [InlineData(@""" true """, Mode.Relaxed)]
    [InlineData(@""" trUe \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" \t\r\n  false """, Mode.Relaxed)]
    [InlineData(@""" FaLse \r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.Relaxed)]
    [InlineData(@"null", Mode.Nullable)]
    [InlineData(@"""123.45""", Mode.Failed)]
    [InlineData(@"""0""", Mode.Failed)]
    [InlineData(@"""0.0""", Mode.Failed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    public void BooleanTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetBoolean(), dox.GetBoolean());
            Assert.Equal((bool)ntj, (bool)dox.AsValue());
            Assert.Equal((bool?)stn, (bool)dox.AsValue());
            Assert.Equal((bool?)ntj, (bool?)dox.AsValue());
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetBoolean());
            Assert.Throws<InvalidOperationException>(() => (bool?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetBoolean());

            Assert.Equal((bool)ntj, (bool)dox.AsValue());
            Assert.Equal((bool?)ntj, (bool?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (bool)value, (bool)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((bool?)ntj, (bool?)dox.AsValue());
            Assert.Equal((bool?)stn, (bool?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (bool)ntj);
            Assert.Throws<NullReferenceException>(() => (bool)stn!);
            Assert.Throws<InvalidOperationException>(() => (bool)dox.AsValue());
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetBoolean());
            Assert.Throws<InvalidOperationException>(() => dox.GetBoolean());
            Assert.Throws<InvalidOperationException>(() => (bool)dox.AsValue());

            try
            {
                var _ = (bool)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }

    [Theory]
    [InlineData("123", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("255", Mode.Strict)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("255.1", Mode.RelaxedFormat)]
    [InlineData("255.5", Mode.FailedFormat)]
    [InlineData("1234", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData("-123.45", Mode.FailedFormat)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""255""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" 1234 """, Mode.Failed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Failed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"2.55e2", Mode.RelaxedFormat)] // 255
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void ByteTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetByte(), dox.GetByte());
            Assert.Equal((byte)ntj, (byte)dox.AsValue());
            Assert.Equal((byte?)stn, (byte)dox.AsValue());
            Assert.Equal((byte?)ntj, (byte?)dox.AsValue());
            Assert.Equal(stj.TryGetByte(out var stjValue), dox.TryGetByte(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetByte());
            Assert.Throws<InvalidOperationException>(() => (byte?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetByte());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));

            Assert.Equal((byte)ntj, (byte)dox.AsValue());
            Assert.Equal((byte?)ntj, (byte?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (byte)value, (byte)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetByte());
            Assert.Throws<InvalidOperationException>(() => (byte?)stn);
            Assert.Throws<FormatException>(() => dox.GetByte());
            Assert.Equal(stj.TryGetByte(out var stjValue), dox.TryGetByte(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((byte)ntj, (byte)dox.AsValue());
            Assert.Equal((byte?)ntj, (byte?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (byte)value, (byte)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((byte?)ntj, (byte?)dox.AsValue());
            Assert.Equal((byte?)stn, (byte?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (byte)ntj);
            Assert.Throws<NullReferenceException>(() => (byte)stn!);
            Assert.Throws<InvalidOperationException>(() => (byte)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetByte());
            Assert.Throws<InvalidOperationException>(() => dox.GetByte());
            Assert.Throws<InvalidOperationException>(() => (byte)dox.AsValue());

            try
            {
                var _ = (byte)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetByte());
            Assert.Throws<FormatException>(() => dox.GetByte());
            Assert.Throws<InvalidOperationException>(() => (byte)dox.AsValue());

            try
            {
                var _ = (byte)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("-128", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.RelaxedFormat)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("-0.123", Mode.RelaxedFormat)]
    [InlineData("127.1", Mode.RelaxedFormat)]
    [InlineData("127.5", Mode.FailedFormat)]
    [InlineData("-128.1", Mode.RelaxedFormat)]
    [InlineData("-128.5", Mode.RelaxedFormat)]
    [InlineData("-128.51", Mode.FailedFormat)]
    [InlineData("1234", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" 1234 """, Mode.Failed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void SByteTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetSByte(), dox.GetSByte());
            Assert.Equal((sbyte)ntj, (sbyte)dox.AsValue());
            Assert.Equal((sbyte?)stn, (sbyte)dox.AsValue());
            Assert.Equal((sbyte?)ntj, (sbyte?)dox.AsValue());
            Assert.Equal(stj.TryGetSByte(out var stjValue), dox.TryGetSByte(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetSByte());
            Assert.Throws<InvalidOperationException>(() => (sbyte?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetSByte());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetSByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetSByte(out var doxValue));

            Assert.Equal((sbyte)ntj, (sbyte)dox.AsValue());
            Assert.Equal((sbyte?)ntj, (sbyte?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (sbyte)value,
                (sbyte)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetSByte());
            Assert.Throws<InvalidOperationException>(() => (sbyte?)stn);
            Assert.Throws<FormatException>(() => dox.GetSByte());
            Assert.Equal(stj.TryGetSByte(out var stjValue), dox.TryGetSByte(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((sbyte)ntj, (sbyte)dox.AsValue());
            Assert.Equal((sbyte?)ntj, (sbyte?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (sbyte)value, (sbyte)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((sbyte?)ntj, (sbyte?)dox.AsValue());
            Assert.Equal((sbyte?)stn, (sbyte?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (sbyte)ntj);
            Assert.Throws<NullReferenceException>(() => (sbyte)stn!);
            Assert.Throws<InvalidOperationException>(() => (sbyte)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetSByte());
            Assert.Throws<InvalidOperationException>(() => dox.GetSByte());
            Assert.Throws<InvalidOperationException>(() => (sbyte)dox.AsValue());

            try
            {
                var _ = (sbyte)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetSByte());
            Assert.Throws<FormatException>(() => dox.GetSByte());
            Assert.Throws<InvalidOperationException>(() => (sbyte)dox.AsValue());

            try
            {
                var _ = (sbyte)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("0", Mode.Strict)]
    [InlineData("65535", Mode.Strict)]
    [InlineData("-0.5", Mode.RelaxedFormat)]
    [InlineData("-0.51", Mode.FailedFormat)]
    [InlineData("65535.4", Mode.RelaxedFormat)]
    [InlineData("65535.5", Mode.FailedFormat)]
    [InlineData(@"""65535""", Mode.Relaxed)]
    [InlineData("123", Mode.Strict)]
    [InlineData("255", Mode.Strict)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("255.1", Mode.RelaxedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData("-123.45", Mode.FailedFormat)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""255""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Failed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"2.55e2", Mode.RelaxedFormat)] // 255
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void UInt16Test(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetUInt16(), dox.GetUInt16());
            Assert.Equal((ushort)ntj, (ushort)dox.AsValue());
            Assert.Equal((ushort?)stn, (ushort)dox.AsValue());
            Assert.Equal((ushort?)ntj, (ushort?)dox.AsValue());
            Assert.Equal(stj.TryGetUInt16(out var stjValue), dox.TryGetUInt16(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetUInt16());
            Assert.Throws<InvalidOperationException>(() => (ushort?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetUInt16());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetUInt16(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetUInt16(out var doxValue));

            Assert.Equal((ushort)ntj, (ushort)dox.AsValue());
            Assert.Equal((ushort?)ntj, (ushort?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (ushort)value,
                (ushort)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetUInt16());
            Assert.Throws<InvalidOperationException>(() => (ushort?)stn);
            Assert.Throws<FormatException>(() => dox.GetUInt16());
            Assert.Equal(stj.TryGetUInt16(out var stjValue), dox.TryGetUInt16(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((ushort)ntj, (ushort)dox.AsValue());
            Assert.Equal((ushort?)ntj, (ushort?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (ushort)value, (ushort)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((ushort?)ntj, (ushort?)dox.AsValue());
            Assert.Equal((ushort?)stn, (ushort?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (ushort)ntj);
            Assert.Throws<NullReferenceException>(() => (ushort)stn!);
            Assert.Throws<InvalidOperationException>(() => (ushort)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetUInt16());
            Assert.Throws<InvalidOperationException>(() => dox.GetUInt16());
            Assert.Throws<InvalidOperationException>(() => (ushort)dox.AsValue());

            try
            {
                var _ = (ushort)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetUInt16());
            Assert.Throws<FormatException>(() => dox.GetUInt16());
            Assert.Throws<InvalidOperationException>(() => (ushort)dox.AsValue());

            try
            {
                var _ = (ushort)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("-32768", Mode.Strict)]
    [InlineData("32767", Mode.Strict)]
    [InlineData("-32768.5", Mode.RelaxedFormat)]
    [InlineData("-32768.51", Mode.FailedFormat)]
    [InlineData("32767.4", Mode.RelaxedFormat)]
    [InlineData("32767.5", Mode.FailedFormat)]
    [InlineData(@""" 32767 """, Mode.Relaxed)]
    [InlineData(@"""-32768""", Mode.Relaxed)]
    [InlineData("-128", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.RelaxedFormat)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("-0.123", Mode.RelaxedFormat)]
    [InlineData("127.1", Mode.RelaxedFormat)]
    [InlineData("-128.1", Mode.RelaxedFormat)]
    [InlineData("-128.5", Mode.RelaxedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void Int16Test(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetInt16(), dox.GetInt16());
            Assert.Equal((short)ntj, (short)dox.AsValue());
            Assert.Equal((short?)stn, (short)dox.AsValue());
            Assert.Equal((short?)ntj, (short?)dox.AsValue());
            Assert.Equal(stj.TryGetInt16(out var stjValue), dox.TryGetInt16(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetInt16());
            Assert.Throws<InvalidOperationException>(() => (short?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetInt16());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetInt16(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetInt16(out var doxValue));

            Assert.Equal((short)ntj, (short)dox.AsValue());
            Assert.Equal((short?)ntj, (short?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (short)value,
                (short)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetInt16());
            Assert.Throws<InvalidOperationException>(() => (short?)stn);
            Assert.Throws<FormatException>(() => dox.GetInt16());
            Assert.Equal(stj.TryGetInt16(out var stjValue), dox.TryGetInt16(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((short)ntj, (short)dox.AsValue());
            Assert.Equal((short?)ntj, (short?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (short)value, (short)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((short?)ntj, (short?)dox.AsValue());
            Assert.Equal((short?)stn, (short?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (short)ntj);
            Assert.Throws<NullReferenceException>(() => (short)stn!);
            Assert.Throws<InvalidOperationException>(() => (short)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetInt16());
            Assert.Throws<InvalidOperationException>(() => dox.GetInt16());
            Assert.Throws<InvalidOperationException>(() => (short)dox.AsValue());

            try
            {
                var _ = (short)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetInt16());
            Assert.Throws<FormatException>(() => dox.GetInt16());
            Assert.Throws<InvalidOperationException>(() => (short)dox.AsValue());

            try
            {
                var _ = (short)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("0", Mode.Strict)]
    [InlineData("4294967295", Mode.Strict)]
    [InlineData("-0.5", Mode.RelaxedFormat)]
    [InlineData("4294967295.4", Mode.RelaxedFormat)]
    [InlineData("4294967295.5", Mode.FailedFormat)]
    [InlineData(@"""4294967295""", Mode.Relaxed)]
    [InlineData("123", Mode.Strict)]
    [InlineData("255", Mode.Strict)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("255.1", Mode.RelaxedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData("-123.45", Mode.FailedFormat)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""255""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Failed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"2.55e2", Mode.RelaxedFormat)] // 255
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void UInt32Test(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetUInt32(), dox.GetUInt32());
            Assert.Equal((uint)ntj, (uint)dox.AsValue());
            Assert.Equal((uint?)stn, (uint)dox.AsValue());
            Assert.Equal((uint?)ntj, (uint?)dox.AsValue());
            Assert.Equal(stj.TryGetUInt32(out var stjValue), dox.TryGetUInt32(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetUInt32());
            Assert.Throws<InvalidOperationException>(() => (uint?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetUInt32());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetUInt32(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetUInt32(out var doxValue));

            Assert.Equal((uint)ntj, (uint)dox.AsValue());
            Assert.Equal((uint?)ntj, (uint?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (uint)value, (uint)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetUInt32());
            Assert.Throws<InvalidOperationException>(() => (uint?)stn);
            Assert.Throws<FormatException>(() => dox.GetUInt32());
            Assert.Equal(stj.TryGetUInt32(out var stjValue), dox.TryGetUInt32(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((uint)ntj, (uint)dox.AsValue());
            Assert.Equal((uint?)ntj, (uint?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (uint)value, (uint)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((uint?)ntj, (uint?)dox.AsValue());
            Assert.Equal((uint?)stn, (uint?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (uint)ntj);
            Assert.Throws<NullReferenceException>(() => (uint)stn!);
            Assert.Throws<InvalidOperationException>(() => (uint)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetUInt32());
            Assert.Throws<InvalidOperationException>(() => dox.GetUInt32());
            Assert.Throws<InvalidOperationException>(() => (uint)dox.AsValue());

            try
            {
                var _ = (uint)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetUInt32());
            Assert.Throws<FormatException>(() => dox.GetUInt32());
            Assert.Throws<InvalidOperationException>(() => (uint)dox.AsValue());

            try
            {
                var _ = (uint)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("-2147483648", Mode.Strict)]
    [InlineData("2147483647", Mode.Strict)]
    [InlineData("-2147483648.5", Mode.RelaxedFormat)]
    [InlineData("2147483647.4", Mode.RelaxedFormat)]
    [InlineData("2147483647.5", Mode.FailedFormat)]
    [InlineData(@"""2147483647""", Mode.Relaxed)]
    [InlineData(@"""-32768""", Mode.Relaxed)]
    [InlineData("-128", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.RelaxedFormat)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("-0.123", Mode.RelaxedFormat)]
    [InlineData("127.1", Mode.RelaxedFormat)]
    [InlineData("-128.1", Mode.RelaxedFormat)]
    [InlineData("-128.5", Mode.RelaxedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void Int32Test(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetInt32(), dox.GetInt32());
            Assert.Equal((int)ntj, (int)dox.AsValue());
            Assert.Equal((int?)stn, (int)dox.AsValue());
            Assert.Equal((int?)ntj, (int?)dox.AsValue());
            Assert.Equal(stj.TryGetInt32(out var stjValue), dox.TryGetInt32(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetInt32());
            Assert.Throws<InvalidOperationException>(() => dox.GetInt32());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetInt32(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetInt32(out var doxValue));
            Assert.Throws<InvalidOperationException>(() => (int?)stn);

            Assert.Equal((int)ntj, (int)dox.AsValue());
            Assert.Equal((int?)ntj, (int?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (int)value, (int)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetInt32());
            Assert.Throws<InvalidOperationException>(() => (int?)stn);
            Assert.Throws<FormatException>(() => dox.GetInt32());
            Assert.Equal(stj.TryGetInt32(out var stjValue), dox.TryGetInt32(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((int)ntj, (int)dox.AsValue());
            Assert.Equal((int?)ntj, (int?)dox.AsValue());
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((int?)ntj, (int?)dox.AsValue());
            Assert.Equal((int?)stn, (int?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (int)ntj);
            Assert.Throws<NullReferenceException>(() => (int)stn!);
            Assert.Throws<InvalidOperationException>(() => (int)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetInt32());
            Assert.Throws<InvalidOperationException>(() => dox.GetInt32());
            Assert.Throws<InvalidOperationException>(() => (int)dox.AsValue());

            try
            {
                var _ = (int)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetInt32());
            Assert.Throws<FormatException>(() => dox.GetInt32());
            Assert.Throws<InvalidOperationException>(() => (int)dox.AsValue());

            try
            {
                var _ = (int)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("0", Mode.Strict)]
    [InlineData("18446744073709551615", Mode.Strict)]
    [InlineData("-0.5", Mode.RelaxedFormat)] // 0に丸まる (OK)
    [InlineData("-0.51", Mode.FailedFormat)] // -1に丸まる (NG)
    [InlineData("18446744073709551615.0", Mode.FailedFormat)]
    [InlineData(@"""18446744073709551615""", Mode.Relaxed)]
    [InlineData("-1", Mode.FailedFormat)] // 負数はNG
    [InlineData("123", Mode.Strict)]
    [InlineData("255", Mode.Strict)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("255.1", Mode.RelaxedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData("-123.45", Mode.FailedFormat)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""255""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Failed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"2.55e2", Mode.RelaxedFormat)] // 255
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void UInt64Test(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetUInt64(), dox.GetUInt64());
            Assert.Equal((ulong)ntj, (ulong)dox.AsValue());
            Assert.Equal((ulong?)stn, (ulong)dox.AsValue());
            Assert.Equal((ulong?)ntj, (ulong?)dox.AsValue());
            Assert.Equal(stj.TryGetUInt64(out var stjValue), dox.TryGetUInt64(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetUInt64());
            Assert.Throws<InvalidOperationException>(() => (ulong?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetUInt64());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetUInt64(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetUInt64(out var doxValue));

            Assert.Equal((ulong)ntj, (ulong)dox.AsValue());
            Assert.Equal((ulong?)ntj, (ulong?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (ulong)value,
                (ulong)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetUInt64());
            Assert.Throws<InvalidOperationException>(() => (ulong?)stn);
            Assert.Throws<FormatException>(() => dox.GetUInt64());
            Assert.Equal(stj.TryGetUInt64(out var stjValue), dox.TryGetUInt64(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((ulong)ntj, (ulong)dox.AsValue());
            Assert.Equal((ulong?)ntj, (ulong?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (ulong)value, (ulong)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((ulong?)ntj, (ulong?)dox.AsValue());
            Assert.Equal((ulong?)stn, (ulong?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (ulong)ntj);
            Assert.Throws<NullReferenceException>(() => (ulong)stn!);
            Assert.Throws<InvalidOperationException>(() => (ulong)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetUInt64());
            Assert.Throws<InvalidOperationException>(() => dox.GetUInt64());
            Assert.Throws<InvalidOperationException>(() => (ulong)dox.AsValue());

            try
            {
                var _ = (ulong)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetUInt64());
            Assert.Throws<FormatException>(() => dox.GetUInt64());
            Assert.Throws<InvalidOperationException>(() => (ulong)dox.AsValue());

            try
            {
                var _ = (ulong)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("-9223372036854775808", Mode.Strict)]
    [InlineData("9223372036854775807", Mode.Strict)]
    [InlineData("-9223372036854775808.0", Mode.RelaxedFormat)]
    [InlineData("9223372036854775807.0", Mode.FailedFormat)]
    [InlineData(@"""-9223372036854775808""", Mode.Relaxed)]
    [InlineData(@""" 9223372036854775807 """, Mode.Relaxed)]
    [InlineData("9223372036854775808", Mode.FailedFormat)]
    [InlineData("9999999999999999999", Mode.FailedFormat)]
    [InlineData("-128", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.RelaxedFormat)]
    [InlineData("123.45", Mode.RelaxedFormat)]
    [InlineData("0.123", Mode.RelaxedFormat)]
    [InlineData("-0.123", Mode.RelaxedFormat)]
    [InlineData("127.1", Mode.RelaxedFormat)]
    [InlineData("-128.1", Mode.RelaxedFormat)]
    [InlineData("-128.5", Mode.RelaxedFormat)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("true", Mode.Relaxed)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.RelaxedFormat)]
    [InlineData(@"1e2", Mode.RelaxedFormat)] // 100
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData(@"null", Mode.Nullable)]
    public void Int64Test(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetInt64(), dox.GetInt64());
            Assert.Equal((long)ntj, (long)dox.AsValue());
            Assert.Equal((long?)stn, (long)dox.AsValue());
            Assert.Equal((long?)ntj, (long?)dox.AsValue());
            Assert.Equal(stj.TryGetInt64(out var stjValue), dox.TryGetInt64(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetInt64());
            Assert.Throws<InvalidOperationException>(() => (long?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetInt64());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetInt64(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetInt64(out var doxValue));

            Assert.Equal((long)ntj, (long)dox.AsValue());
            Assert.Equal((long?)ntj, (long?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (long)value, (long)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetInt64());
            Assert.Throws<InvalidOperationException>(() => (long?)stn);
            Assert.Throws<FormatException>(() => dox.GetInt64());
            Assert.Equal(stj.TryGetInt64(out var stjValue), dox.TryGetInt64(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((long)ntj, (long)dox.AsValue());
            Assert.Equal((long?)ntj, (long?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (long)value, (long)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((long?)ntj, (long?)dox.AsValue());
            Assert.Equal((long?)stn, (long?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (long)ntj);
            Assert.Throws<NullReferenceException>(() => (long)stn!);
            Assert.Throws<InvalidOperationException>(() => (long)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetByte(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetByte(out var doxValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetInt64());
            Assert.Throws<InvalidOperationException>(() => dox.GetInt64());
            Assert.Throws<InvalidOperationException>(() => (long)dox.AsValue());

            try
            {
                var _ = (long)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetInt64());
            Assert.Throws<FormatException>(() => dox.GetInt64());
            Assert.Throws<InvalidOperationException>(() => (long)dox.AsValue());

            try
            {
                var _ = (long)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("1.23", Mode.Strict)]
    [InlineData("-1.23", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("3.402823e38", Mode.Strict)]
    [InlineData("-3.402823e38", Mode.Strict)]
    [InlineData("3.402824e38", Mode.Strict)]
    [InlineData(@"""1.23""", Mode.Relaxed)]
    [InlineData(@"1e2", Mode.Strict)]
    [InlineData(@"null", Mode.Nullable)]
    [InlineData(@"""NaN""", Mode.Relaxed)]
    [InlineData(@"""Infinity""", Mode.Relaxed)]
    [InlineData("-128", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.Strict)]
    [InlineData("123.45", Mode.Strict)]
    [InlineData("0.123", Mode.Strict)]
    [InlineData("-0.123", Mode.Strict)]
    [InlineData("127.1", Mode.Strict)]
    [InlineData("-128.1", Mode.Strict)]
    [InlineData("-128.5", Mode.Strict)]
    [InlineData("1234567899999999999999999999999999999", Mode.Strict)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.Strict)]
    [InlineData("true", Mode.Relaxed)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.Strict)]
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData("9223372036854775807", Mode.Strict)]
    [InlineData(@"""-Infinity""", Mode.Relaxed)]
    [InlineData(@"""nan""", Mode.Relaxed)] // Newtonsoftの許容範囲確認
    [InlineData("1.401298e-45", Mode.Strict)]
    [InlineData("1e-50", Mode.Strict)] // 最終的に 0.0f になるはず
    [InlineData("0.3333333333333333333333", Mode.Strict)]
    [InlineData("3.4028234663852886e38", Mode.Strict)] // float.MaxValue（double精度での境界表現）
    [InlineData("3.4028236e38", Mode.Strict)] // overflow: TryGetSingle は false / 例外かを確認
    [InlineData("1.1754943508222875e-38", Mode.Strict)] // float.MinPositiveNormal
    [InlineData("1.1754942e-38", Mode.Strict)] // 非正規化だが非ゼロ
    [InlineData("1.401298464e-45", Mode.Strict)] // float.MinPositiveSubnormal（より厳密）
    [InlineData("1e-46", Mode.Strict)] // 丸めで 0.0f になるか／ならないかの境目に近い
    [InlineData("1e-300", Mode.Strict)]
    [InlineData("-0", Mode.Strict)]
    [InlineData("-0.0", Mode.Strict)]
    [InlineData("-0e0", Mode.Strict)]
    [InlineData("1E2", Mode.Strict)] // E の大文字
    [InlineData("1e+2", Mode.Strict)] // 正の符号付き指数
    [InlineData("1e-02", Mode.Strict)] // 先頭ゼロ付き指数
    [InlineData("0e0", Mode.Strict)]
    [InlineData(" \t\r\n123.45 ", Mode.Strict)] // 文字列全体の前後空白（パーサ許容確認）
//        [InlineData("\uFEFF123.45", Mode.Strict)]           // BOM（UTF-8/UTF-16 変換の扱い差）
    [InlineData("16777217", Mode.Strict)] // 2^24+1: float で丸め（隣接偶数丸めの確認）
    [InlineData("33554431", Mode.Strict)] // 2^25-1: 境界の丸め挙動
    [InlineData(@"""1,23""", Mode.Failed)] // 文字列だがカンマ小数（JSON 仕様外 / 文化依存）
    public void SingleTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetSingle(), dox.GetSingle());
            Assert.Equal((float)ntj, (float)dox.AsValue());
            Assert.Equal((float?)stn, (float)dox.AsValue());
            Assert.Equal((float?)ntj, (float?)dox.AsValue());
            Assert.Equal(stj.TryGetSingle(out var stjValue), dox.TryGetSingle(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetSingle());
            Assert.Throws<InvalidOperationException>(() => (float?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetSingle());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetSingle(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetSingle(out var doxValue));

            Assert.Equal((float)ntj, (float)dox.AsValue());
            Assert.Equal((float?)ntj, (float?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (float)value,
                (float)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetSingle());
            Assert.Throws<InvalidOperationException>(() => (float?)stn);
            Assert.Throws<FormatException>(() => dox.GetSingle());

            Assert.Equal((float)ntj, (float)dox.AsValue());
            Assert.Equal((float?)ntj, (float?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (float)value, (float)ntj);
            Assert.Equal(stj.TryGetSingle(out var stjValue), dox.TryGetSingle(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((float?)ntj, (float?)dox.AsValue());
            Assert.Equal((float?)stn, (float?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (float)ntj);
            Assert.Throws<NullReferenceException>(() => (float)stn!);
            Assert.Throws<InvalidOperationException>(() => (float)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetSingle(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetSingle(out var stjValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetSingle());
            Assert.Throws<InvalidOperationException>(() => dox.GetSingle());
            Assert.Throws<InvalidOperationException>(() => (float)dox.AsValue());

            try
            {
                var _ = (float)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetSingle());
            Assert.Throws<FormatException>(() => dox.GetSingle());
            Assert.Throws<InvalidOperationException>(() => (float)dox.AsValue());

            try
            {
                var _ = (float)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("1.7976931348623157E+308", Mode.Strict)] // MaxValue
    [InlineData("-1.7976931348623157E+308", Mode.Strict)] // MinValue
    [InlineData("0.0", Mode.Strict)]
    [InlineData("4.94065645841247E-324", Mode.Strict)] // 最小の正の値
    [InlineData("1.7976931348623159E+308", Mode.Strict)] // オーバーフロー
    [InlineData("1.7976931348623159E+1308", Mode.Strict)] // オーバーフロー
    [InlineData(@"7.9228162514264337593543950335", Mode.Strict)] // decimal範囲の大きな値
    [InlineData("1.23", Mode.Strict)]
    [InlineData("-1.23", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("3.402823e38", Mode.Strict)]
    [InlineData("-3.402823e38", Mode.Strict)]
    [InlineData("3.402824e38", Mode.Strict)]
    [InlineData(@"""1.23""", Mode.Relaxed)]
    [InlineData(@"1e2", Mode.Strict)]
    [InlineData(@"null", Mode.Nullable)]
    [InlineData(@"""NaN""", Mode.Relaxed)]
    [InlineData(@"""Infinity""", Mode.Relaxed)]
    [InlineData("-128", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.Strict)]
    [InlineData("123.45", Mode.Strict)]
    [InlineData("0.123", Mode.Strict)]
    [InlineData("-0.123", Mode.Strict)]
    [InlineData("127.1", Mode.Strict)]
    [InlineData("-128.1", Mode.Strict)]
    [InlineData("-128.5", Mode.Strict)]
//        [InlineData("1234567899999999999999999999999999999", Mode.Strict)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.Strict)]
    [InlineData("true", Mode.Relaxed)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.Strict)]
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData("9223372036854775807", Mode.Strict)]
    [InlineData(@"""-Infinity""", Mode.Relaxed)]
    [InlineData(@"""nan""", Mode.Relaxed)] // Newtonsoftの許容範囲確認
    [InlineData("1.401298e-45", Mode.Strict)]
    [InlineData("1e-50", Mode.Strict)] // 最終的に 0.0f になるはず
    [InlineData("0.3333333333333333333333", Mode.Strict)]
    public void DoubleTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetDouble(), dox.GetDouble());
            Assert.Equal((double)ntj, (double)dox.AsValue());
            Assert.Equal((double?)stn, (double)dox.AsValue());
            Assert.Equal((double?)ntj, (double?)dox.AsValue());
            Assert.Equal(stj.TryGetDouble(out var stjValue), dox.TryGetDouble(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetDouble());
            Assert.Throws<InvalidOperationException>(() => (double?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetDouble());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetDouble(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetDouble(out var doxValue));

            Assert.Equal((double)ntj, (double)dox.AsValue());
            Assert.Equal((double?)ntj, (double?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (double)value,
                (double)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetDouble());
            Assert.Throws<InvalidOperationException>(() => (double?)stn);
            Assert.Throws<FormatException>(() => dox.GetDouble());
            Assert.Equal(stj.TryGetDouble(out var stjValue), dox.TryGetDouble(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((double)ntj, (double)dox.AsValue());
            Assert.Equal((double?)ntj, (double?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (double)value, (double)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((double?)ntj, (double?)dox.AsValue());
            Assert.Equal((double?)stn, (double?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (double)ntj);
            Assert.Throws<NullReferenceException>(() => (double)stn!);
            Assert.Throws<InvalidOperationException>(() => (double)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetDouble(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetDouble(out var stjValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetDouble());
            Assert.Throws<InvalidOperationException>(() => dox.GetDouble());
            Assert.Throws<InvalidOperationException>(() => (double)dox.AsValue());

            try
            {
                var _ = (double)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetDouble());
            Assert.Throws<FormatException>(() => dox.GetDouble());
            Assert.Throws<InvalidOperationException>(() => (double)dox.AsValue());

            try
            {
                var _ = (double)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData("123.456", Mode.Strict)]
    [InlineData("-123.456", Mode.Strict)]
    [InlineData("0", Mode.Strict)]
    [InlineData("79228162514264337593543950335", Mode.Strict)] // MaxValue
    [InlineData("-79228162514264337593543950335", Mode.Strict)] // MinValue
    [InlineData("79228162514264337593543950336", Mode.FailedFormat)] // MaxValue + 1 (Overflow)
    [InlineData("0.0000000000000000000000000001", Mode.Strict)] // 小数点第28位 (最小正数値)
    [InlineData("0.00000000000000000000000000001", Mode.Strict)] // 精度限界を超えて丸められる可能性
    [InlineData(@"1e-10", Mode.Strict)] // 指数表記
    [InlineData(@"""123.456""", Mode.Relaxed)]
    [InlineData("1.7976931348623157E+308", Mode.FailedFormat)] // MaxValue
    [InlineData("-1.7976931348623157E+308", Mode.FailedFormat)] // MinValue
    [InlineData("0.0", Mode.Strict)]
    [InlineData("4.94065645841247E-324", Mode.Strict)] // 最小の正の値
    [InlineData("1.7976931348623159E+308", Mode.FailedFormat)] // オーバーフロー
    [InlineData("1.7976931348623159E+1308", Mode.FailedFormat)] // オーバーフロー
    [InlineData(@"7.9228162514264337593543950335", Mode.Strict)] // decimal範囲の大きな値
    [InlineData("1.23", Mode.Strict)]
    [InlineData("-1.23", Mode.Strict)]
    [InlineData("3.402823e38", Mode.FailedFormat)]
    [InlineData("-3.402823e38", Mode.FailedFormat)]
    [InlineData("3.402824e38", Mode.FailedFormat)]
    [InlineData(@"""1.23""", Mode.Relaxed)]
    [InlineData(@"1e2", Mode.Strict)]
    [InlineData(@"null", Mode.Nullable)]
    [InlineData(@"""NaN""", Mode.Failed)]
    [InlineData(@"""Infinity""", Mode.Failed)]
    [InlineData("-128", Mode.Strict)]
    [InlineData("127", Mode.Strict)]
    [InlineData("-123.45", Mode.Strict)]
    [InlineData("123.45", Mode.Strict)]
    [InlineData("0.123", Mode.Strict)]
    [InlineData("-0.123", Mode.Strict)]
    [InlineData("127.1", Mode.Strict)]
    [InlineData("-128.1", Mode.Strict)]
    [InlineData("-128.5", Mode.Strict)]
    [InlineData("1234567899999999999999999999999999999", Mode.FailedFormat)]
    [InlineData("1234567899999999999999999999999999999.1234", Mode.FailedFormat)]
    [InlineData("true", Mode.Relaxed)]
    [InlineData("false", Mode.Relaxed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""true""", Mode.Failed)]
    [InlineData(@"""false""", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)]
    [InlineData(@""" """, Mode.Failed)]
    [InlineData(@"""127""", Mode.Relaxed)]
    [InlineData(@""" 123 """, Mode.Relaxed)]
    [InlineData(@""" 123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@""" -123 \t\r\n""", Mode.Relaxed)]
    [InlineData(@"0.0000000000000000000001", Mode.Strict)]
    [InlineData(@"""0xFF""", Mode.Failed)]
    [InlineData(@"""-0xFF""", Mode.Failed)]
    [InlineData("9223372036854775807", Mode.Strict)]
    [InlineData(@"""-Infinity""", Mode.Failed)]
    [InlineData(@"""nan""", Mode.Failed)] // Newtonsoftの許容範囲確認
    [InlineData("1.401298e-45", Mode.Strict)]
    [InlineData("1e-50", Mode.Strict)] // 最終的に 0.0f になるはず
    [InlineData("0.3333333333333333333333", Mode.Strict)]
    [InlineData("1e-28", Mode.Strict)]
    [InlineData("1e28", Mode.Strict)]
    [InlineData("0.0000000000000000000000000000", Mode.Strict)] // スケール28のゼロ
    [InlineData("-0.0", Mode.Strict)]
    [InlineData("-0", Mode.Strict)]
    [InlineData("1.00000000000000000000000000005", Mode.Strict)] // 丸めが発生する可能性        
    public void DecimalTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetDecimal(), dox.GetDecimal());
//                Assert.Equal((decimal)ntj, (decimal)dox);
            Assert.Equal((decimal?)stn, (decimal)dox.AsValue());
//                Assert.Equal((decimal?)ntj, (decimal?)dox);
            Assert.Equal(stj.TryGetDecimal(out var stjValue), dox.TryGetDecimal(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetDecimal());
            Assert.Throws<InvalidOperationException>(() => (decimal?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetDecimal());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetDecimal(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetDecimal(out var doxValue));

            Assert.Equal((decimal)ntj, (decimal)dox.AsValue());
            Assert.Equal((decimal?)ntj, (decimal?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (decimal)value,
                (decimal)ntj);
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetDecimal());
            Assert.Throws<InvalidOperationException>(() => (decimal?)stn);
            Assert.Throws<FormatException>(() => dox.GetDecimal());
            Assert.Equal(stj.TryGetDecimal(out var stjValue), dox.TryGetDecimal(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            Assert.Equal((decimal)ntj, (decimal)dox.AsValue());
            Assert.Equal((decimal?)ntj, (decimal?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (decimal)value, (decimal)ntj);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((decimal?)ntj, (decimal?)dox.AsValue());
            Assert.Equal((decimal?)stn, (decimal?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (decimal)ntj);
            Assert.Throws<NullReferenceException>(() => (decimal)stn!);
            Assert.Throws<InvalidOperationException>(() => (decimal)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetDecimal(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetDecimal(out var stjValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetDecimal());
            Assert.Throws<InvalidOperationException>(() => dox.GetDecimal());
            Assert.Throws<InvalidOperationException>(() => (decimal)dox.AsValue());

            try
            {
                var _ = (decimal)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetDecimal());
            Assert.Throws<FormatException>(() => dox.GetDecimal());
            Assert.Throws<InvalidOperationException>(() => (decimal)dox.AsValue());

            try
            {
                var _ = (decimal)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData(@"""2024-01-15T10:30:00.0000001Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10Z""", Mode.Strict)] // 基本的なUTC
    [InlineData(@"""2024-01-15T10:30:10.1234567Z""", Mode.Strict)] // 最大精度(7桁)
    [InlineData(@"""2024-01-15T10:30:10.1234567+02:00""", Mode.Strict)] // 最大精度(7桁)
    [InlineData(@"""2024-01-15T10:30:10+05:00""", Mode.Strict)] // オフセット付き
    [InlineData(@"""2024-01-15T10:30:10""", Mode.Strict)] // Local/Unspecified (末尾なし)
    [InlineData(@"""2024-01-15T10:30:10.123""", Mode.Strict)] // ミリ秒あり
    [InlineData(@"""2024-01-15T10:30:10.123+03:00""", Mode.Strict)] // ミリ秒あり
    [InlineData(@"""2024-01-15T10:00:00-14:00""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:00.Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:00.+09:00""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:00:00+14:00""", Mode.Strict)] // 最大正オフセット（有効）
    [InlineData(@"""2024-01-15T10:00:00-12:00""", Mode.Strict)] // 最小負オフセット（有効）
    [InlineData(@"""2024-01-15T10:00:00-00:00""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.0Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.12Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.123456Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.0000000Z""", Mode.Strict)]
    [InlineData(@"""2024-02-29T10:00:00Z""", Mode.Strict)] // 2024年は閏年（有効）
    [InlineData(@"""2024-01-15""", Mode.Strict)] // 日付のみ（受け付けるか要仕様）
    [InlineData(@"""2024-01-15T10:30Z""", Mode.Strict)] // RFC 3339は秒省略可
    [InlineData(@"""2024-01-15T10:30+09:00""", Mode.Strict)] // RFC 3339は秒省略可
    [InlineData(@"""2024-01-15T10:30""", Mode.Strict)] // オフセットなし（すでに追加済みと同系）
    [InlineData(@"""2024-01-15T10:30:10.12345678Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.123456789Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.1234567899Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.12345678999Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.123456789999Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.1234567899999Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.12345678999999Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.123456789999999Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.1234567899999999Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.123Z""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:30:10.0000000+09:00""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:00:00-12:01""", Mode.Strict)]
    [InlineData(@"""2024-01-15T10:00:00-13:00""", Mode.Strict)]
    [InlineData(@"""2024-01-15 10:30:10Z""", Mode.RelaxedFormat)] // スペース区切り
    [InlineData(@"""2024-01-15T10:30:10z""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:30:10.12345678999999999Z""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:30:10.123456789999999999Z""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:30:10.1234567899999999999Z""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:30:10.1234567899999999999999999999999Z""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15t10:30:10z""", Mode.RelaxedFormat)] // t/zの小文字はNG（Strictなら）
    [InlineData(@"""   2024-01-15T10:30:10Z   """, Mode.RelaxedFormat)] // トリムの有無に応じて期待値を決める
    [InlineData(@"""\t2024-01-15T10:30:10Z\n""", Mode.RelaxedFormat)] // 制御文字を許すかどうかの方針次第
    [InlineData(@"""00:00:00""", Mode.RelaxedFormat)]
    [InlineData(@"""10:30:00""", Mode.RelaxedFormat)] // 時間のみ
    [InlineData(@"""2024/01/15 10:30:00""", Mode.RelaxedFormat)] // スラッシュ区切り
    [InlineData(@"""2024-01-15 10:30:00""", Mode.RelaxedFormat)] // T の代わりにスペース
    [InlineData(@"""\/Date(1705314600000)\/""",
        Mode.RelaxedFormat)] // Microsoft JSON Date format (※FormatかRelaxedかは実装依存)
    [InlineData(@"""2024/01/15 10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""2024/01/15""", Mode.RelaxedFormat)]
    [InlineData(@"""Mon, 15 Jan 2024 10:30:10 GMT""", Mode.RelaxedFormat)]
    [InlineData(@"""01/15/2024 10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""January 15, 2024""", Mode.RelaxedFormat)]
    [InlineData(@"""\/Date(1705314610000)\/""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15 10:30:10 +09:00""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15 10:30 +09:00""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15 10:30:10.123""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15 10:30:10.1234567Z""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15 10:30:10.1234567 +09:00""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15 10:30""", Mode.RelaxedFormat)]
    [InlineData(@"""01-15-2024""", Mode.RelaxedFormat)] // 例：en-US などで読めるケース
    [InlineData(@"""01/15/2024 10:30""", Mode.RelaxedFormat)] // en-US 想定
    [InlineData(@"""Jan 15 2024 10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""15 Jan 2024 10:30""", Mode.RelaxedFormat)]
    [InlineData(@"""\/Date(1705285810000)\/""", Mode.RelaxedFormat)]
    [InlineData(@"""\/Date(1705285810000+0900)\/""", Mode.RelaxedFormat)]
    [InlineData(@"""2024.01.15 10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""15 Jan 2024 10:30:10 +0900""", Mode.RelaxedFormat)]
    [InlineData(@"""2024年1月15日 10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""15-Jan-2024 10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""2024/01/15 10:30:10 AM""", Mode.RelaxedFormat)]
    [InlineData(@"""\/Date(1705314610000+0900)\/""", Mode.RelaxedFormat)]
    [InlineData(@"""10:30:10""", Mode.RelaxedFormat)]
    [InlineData(@"""10:30""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:00:00-15:00""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+14:30""", Mode.RelaxedFormat)] // 14:30 の扱い（STJ準拠）
    [InlineData(@"""2024-01-15T10:00:00+14:01""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:00:00-14:01""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+0900""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+15:00""", Mode.RelaxedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+09:99""", Mode.RelaxedFormat)]
    [InlineData("null", Mode.Nullable)]
    [InlineData(@"""2024-01-15T10:00:00 15 00""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+15 00""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+15/00""", Mode.FailedFormat)]
    [InlineData(@"""2023-02-29T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-04-31T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-00-01T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T25:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T24:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:60:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:00:60Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:00:00Z+09:00""", Mode.FailedFormat)]
    [InlineData(@"""0000-01-01T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""-0001-01-01T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""10000-01-01T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-00-15T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-13-15T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-99-15T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-00T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-32T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-99T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-06-31T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-02-30T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""1900-02-29T10:00:00Z""", Mode.FailedFormat)]
    [InlineData(@"""20240115T103010Z""", Mode.FailedFormat)]
    [InlineData(@"""20240115T103010+09:00""", Mode.FailedFormat)]
    [InlineData(@"""2024-W03-1T10:30:10Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-015T10:30:10Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:30:10 JST""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+09:00:30""", Mode.FailedFormat)]
    [InlineData(@"""10:30:10Z""", Mode.RelaxedFormat)] // 時刻のみ（RFC 3339では基本不可）
    [InlineData(@"""2016-12-31T23:59:60Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-1-5T9:3:7Z""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:00:00+09:00+02:00""", Mode.FailedFormat)]
    [InlineData(@"""2024-02-31T10:00:00Z""", Mode.FailedFormat)] // 2月31日は存在しない
    [InlineData(@"""2024-01-15T10:30:00.""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:30:00.123a""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:30:00.123.456""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:30:00.123,456""", Mode.FailedFormat)]
    [InlineData(@"""2024-01-15T10:30:10.""", Mode.FailedFormat)] // 小数点のみ
    [InlineData(@"""2024-01-15T10:30:10.123a""", Mode.FailedFormat)] // 非数字
    [InlineData(@"""0000-01-01T00:00:00Z""", Mode.FailedFormat)] // 年下限未満
    [InlineData(@"""10000-01-01T00:00:00Z""", Mode.FailedFormat)] // 年上限超過
    [InlineData(@"""This is not a date""", Mode.FailedFormat)]
    [InlineData(@"""2024-13-99""", Mode.FailedFormat)] // 存在しない日付
    [InlineData("true", Mode.Failed)] // ブール値
    [InlineData("123", Mode.Failed)] // 単純な数値 (UnixTimeとして扱わない場合)
    [InlineData("{}", Mode.Failed)] // オブジェクト        
    [InlineData("[]", Mode.Failed)] // 配列 
    public void DateTimeTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            var stjDt = stj.GetDateTime();
            var doxDt = dox.GetDateTime();
            var stjDto = stj.GetDateTimeOffset();
            var doxDto = dox.GetDateTimeOffset();

            Assert.Equal(stj.GetDateTime(), dox.GetDateTime());
            Assert.Equal(stj.GetDateTime().Ticks, dox.GetDateTime().Ticks);
            Assert.Equal(stj.GetDateTime().Kind, dox.GetDateTime().Kind);
            Assert.Equal(stj.TryGetDateTime(out var stjValue), dox.TryGetDateTime(out var doxValue));
            Assert.Equal(stjValue, doxValue);
            Assert.Equal(stj.GetDateTimeOffset(), dox.GetDateTimeOffset());
            Assert.Equal(stj.GetDateTimeOffset().Ticks, dox.GetDateTimeOffset().Ticks);
            Assert.Equal(stj.GetDateTimeOffset().Offset, dox.GetDateTimeOffset().Offset);
            Assert.Equal(stj.TryGetDateTimeOffset(out var stjDtoValue), dox.TryGetDateTimeOffset(out var doxDtoValue));
            Assert.Equal(stjDtoValue, doxDtoValue);

            Assert.Equal((DateTime?)stn, (DateTime?)dox.AsValue());
            Assert.Equal((DateTimeOffset?)stn, (DateTimeOffset?)dox.AsValue());

            dox.AsValue().ReplaceWith(stjDt);

            doxDt = (DateTime)Json.JsonDocument.Parse(dox.ToJsonString(), SerializerSettings.Default).RootElement
                .AsValue();
            Assert.Equal(doxDt, stjDt);
            Assert.Equal(doxDt.Kind, stjDt.Kind);

            Assert.Equal((DateTime)dox.AsValue(), stjDt);
            Assert.Equal(((DateTime)dox.AsValue()).Kind, stjDt.Kind);

            dox.AsValue().ReplaceWith(stjDto);

            doxDto = (DateTimeOffset)Json.JsonDocument.Parse(dox.ToJsonString(), SerializerSettings.Default).RootElement
                .AsValue();
            Assert.Equal(doxDto, stjDto);
            Assert.Equal((DateTimeOffset)dox.AsValue(), stjDto);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetDateTime());
            Assert.Throws<InvalidOperationException>(() => (DateTime?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetDateTime());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetDateTime(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetDateTime(out var doxValue));

            Assert.Equal((DateTime)ntj, (DateTime)dox.AsValue());
            Assert.Equal((DateTime?)ntj, (DateTime?)dox.AsValue());
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetDateTimeOffset());
            Assert.Throws<FormatException>(() => stj.GetDateTime());
            Assert.Throws<InvalidOperationException>(() => (DateTime?)stn);
            Assert.Throws<FormatException>(() => dox.GetDateTime());
            Assert.Equal(stj.TryGetDateTime(out var stjValue), dox.TryGetDateTime(out var doxValue));
            Assert.Equal(stjValue, doxValue);
            Assert.Equal(stj.TryGetDateTimeOffset(out var stjDtoValue), dox.TryGetDateTimeOffset(out var doxDtoValue));
            Assert.Equal(stjDtoValue, doxDtoValue);

            var ntjDt = (DateTime)ntj;
            var ntjDto = (DateTimeOffset)ntj;
            var doxDt = (DateTime)dox.AsValue();
            var doxDto = (DateTimeOffset)dox.AsValue();

            Assert.Equal(ntjDt, doxDt);
            Assert.Equal(ntjDt.Ticks, doxDt.Ticks);
            Assert.Equal(ntjDt.Kind, doxDt.Kind);
            Assert.Equal(ntjDto, doxDto);
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (DateTime)value, ntjDt);
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (DateTimeOffset)value, ntjDto);

            dox.AsValue().ReplaceWith(ntjDt);

            Assert.Equal((DateTime)dox.AsValue(), ntjDt);
            Assert.Equal(((DateTime)dox.AsValue()).Kind, ntjDt.Kind);

            dox.AsValue().ReplaceWith(ntjDto);

            Assert.Equal((DateTimeOffset)dox.AsValue(), ntjDto);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Equal((DateTime?)ntj, (DateTime?)dox.AsValue());
            Assert.Equal((DateTime?)stn, (DateTime?)dox.AsValue());

            Assert.Throws<ArgumentException>(() => (DateTime)ntj);
            Assert.Throws<NullReferenceException>(() => (DateTime)stn!);
            Assert.Throws<InvalidOperationException>(() => (DateTime)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetDateTime(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetDateTime(out var stjValue));
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetDateTimeOffset());
            Assert.Throws<InvalidOperationException>(() => dox.GetDateTimeOffset());
            Assert.Throws<InvalidOperationException>(() => stj.GetDateTime());
            Assert.Throws<InvalidOperationException>(() => dox.GetDateTime());
            Assert.Throws<InvalidOperationException>(() => (DateTime)dox.AsValue());

            try
            {
                var _ = (DateTime)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetDateTimeOffset());
            Assert.Throws<FormatException>(() => dox.GetDateTimeOffset());
            Assert.Throws<FormatException>(() => stj.GetDateTime());
            Assert.Throws<FormatException>(() => dox.GetDateTime());
            Assert.Throws<InvalidOperationException>(() => (DateTime)dox.AsValue());

            try
            {
                var _ = (DateTime)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }


    [Theory]
    [InlineData(@"""10:30:10Z""")]
    [InlineData(@"""10:30:10+09:00""")]
    [InlineData(@"""10:30:10-05:30""")]
    public void TimeOnlyDateTimeOffsetMatchesNewtonsoftJson(string json)
    {
        using var document = Json.JsonDocument.Parse(json, SerializerSettings.Default);
        var expected = (DateTimeOffset)JToken.Parse(json);
        var actual = (DateTimeOffset)document.RootElement.AsValue();

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Offset, actual.Offset);
    }

    [Theory]
    [InlineData(@"""550e8400-e29b-41d4-a716-446655440000""", Mode.Strict)]
    [InlineData(@"""d3ea415b-5136-407a-9a00-5339d1b6e4d2""", Mode.Strict)]
    [InlineData(@"""550E8400-E29B-41D4-A716-446655440000""", Mode.Strict)] // 全大文字
    [InlineData(@"""550e8400-E29B-41d4-a716-446655440000""", Mode.Strict)] // 混在
    [InlineData(@"""00000000-0000-0000-0000-000000000000""", Mode.Strict)]
    [InlineData(@"""00000000000000000000000000000000""", Mode.RelaxedFormat)]
    [InlineData(@"""{00000000-0000-0000-0000-000000000000}""", Mode.RelaxedFormat)]
    [InlineData(@"""(00000000-0000-0000-0000-000000000000)""", Mode.RelaxedFormat)]
    [InlineData(@"""\t550e8400-e29b-41d4-a716-446655440000""", Mode.RelaxedFormat)] // 先頭タブ
    [InlineData(@"""550e8400-e29b-41d4-a716-446655440000\n""", Mode.RelaxedFormat)] // 末尾改行
    [InlineData(@"""550e8400-e29b-41d4-a716-446655440000\u200B""", Mode.FailedFormat)] // ゼロ幅スペース
    [InlineData(@"""\u00A0550e8400-e29b-41d4-a716-446655440000""", Mode.RelaxedFormat)] // NBSP
    [InlineData(@"""５５０ｅ８４００-ｅ２９ｂ-４１ｄ４-ａ７１６-４４６６５５４４００００""", Mode.FailedFormat)] // 全角英数字と記号
    [InlineData(@"""550e8400-e29b-41d4-a716-44665544\u0661000""", Mode.FailedFormat)] // アラビア・インド数字 ١ 混入
    [InlineData(@"""{0X550E8400,0Xe29B,0X41D4,{0Xa7,0X16,0X44,0X66,0X55,0X44,0X00,0X00}}""",
        Mode.RelaxedFormat)] // 0X 大文字
    [InlineData(@"""{0x550e8400, 0xe29b, 0x41d4, { 0xa7, 0x16, 0x44, 0x66, 0x55, 0x44, 0x00, 0x00 } }""",
        Mode.RelaxedFormat)] // スペース多数
    [InlineData(@"""{0x550e8400,0xe29b,0x41d4,{0xa7,0x16,0x44,0x66,0x55,0x44,0x00}}""", Mode.FailedFormat)] // バイト数不足
    [InlineData(@"""{0x550e8400,0xe29b,0x41d4,{0xa7,0x16,0x44,0x66,0x55,0x44,0x00,0x00,0x01}}""",
        Mode.FailedFormat)] // バイト数過剰
    [InlineData(@"""550e8400e-29b-41d4-a716-446655440000""", Mode.FailedFormat)] // 最初の区切り位置不正
    [InlineData(@"""550e8400-e29b41d4-a716-4466-55440000""", Mode.FailedFormat)] // 区切り位置がバラバラ
    [InlineData(@"""6ba7b810-9dad-11d1-80b4-00c04fd430c8""", Mode.Strict)] // v1 (名前空間用の例でなくても v1)
    [InlineData(@"""6ba7b810-9dad-21d1-80b4-00c04fd430c8""", Mode.Strict)] // version bit を 2 にしたランダム例 (形式は妥当)
    [InlineData(@"""AQIDBAUGBwgJCgsMDQ4PEA==""", Mode.FailedFormat)] // 長さ 24 の Base64（16 バイト）
    [InlineData(@"""\n\t   {550e8400-e29b-41d4-a716-446655440000}   \t\n""", Mode.RelaxedFormat)]
    [InlineData(@"""550e8400\u2010e29b\u201041d4\u2010a716\u2010446655440000""", Mode.FailedFormat)] // U+2010 Hyphen
    [InlineData(@"""\u007B550e8400-e29b-41d4-a716-446655440000\u007D""", Mode.RelaxedFormat)] // { ... }
    [InlineData(@"""\u0028550e8400-e29b-41d4-a716-446655440000\u0029""", Mode.RelaxedFormat)] // ( ... )
    [InlineData(@"""550e8400-e29b-41d4-a716-446655440000 // comment""", Mode.FailedFormat)]
    [InlineData(@"""/* 550e8400-e29b-41d4-a716-446655440000 */""", Mode.FailedFormat)]
    [InlineData(@"""d3ea415b5136407a9a005339d1b6e4d2""", Mode.RelaxedFormat)]
    [InlineData(@"""{550e8400-e29b-41d4-a716-446655440000}""", Mode.RelaxedFormat)]
    [InlineData(@"""(550e8400-e29b-41d4-a716-446655440000)""", Mode.RelaxedFormat)]
    [InlineData(@"""{0x550e8400,0xe29b,0x41d4,{0xa7,0x16,0x44,0x66,0x55,0x44,0x00,0x00}}""", Mode.RelaxedFormat)]
    [InlineData(@"""   d3ea415b5136407a9a005339d1b6e4d2   """, Mode.RelaxedFormat)]
    [InlineData(@"""       {550e8400-e29b-41d4-a716-446655440000}  """, Mode.RelaxedFormat)]
    [InlineData(@"""   (550e8400-e29b-41d4-a716-446655440000) """, Mode.RelaxedFormat)]
    [InlineData(@"""       {0x550e8400,0xe29b,0x41d4,{0xa7,0x16,0x44,0x66,0x55,0x44,0x00,0x00}}    """,
        Mode.RelaxedFormat)]
    [InlineData("null", Mode.Nullable)]
    [InlineData(@"""AAAAAAAAAAAAAAAAAAAAAA==""", Mode.FailedFormat)] // 多くの場合文字列として扱われGuid変換で失敗するが、Newtonsoftの挙動確認用
    [InlineData(@"""""", Mode.FailedFormat)]
    [InlineData(@"""550e8400-e29b-41d4-a716-44665544000""", Mode.FailedFormat)] // 短い
    [InlineData(@"""550e8400-e29b-41d4-a716-4466554400000""", Mode.FailedFormat)] // 長い
    [InlineData(@"""550e8400\u0000e29b-41d4-a716-446655440000""", Mode.FailedFormat)] // 途中にNull文字
    [InlineData(@"""550e8400-e29b-41d4-a716-44665544k000""", Mode.FailedFormat)]
    [InlineData(@"""5""", Mode.FailedFormat)]
    [InlineData("true", Mode.Failed)] // ブール値
    [InlineData("123", Mode.Failed)] // 単純な数値 (UnixTimeとして扱わない場合)
    [InlineData("{}", Mode.Failed)] // オブジェクト        
    [InlineData("[]", Mode.Failed)] // 配列 
    public void GuidTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetGuid(), dox.GetGuid());
            Assert.Equal((Guid)ntj, (Guid)dox.AsValue());
            Assert.Equal((Guid?)stn, (Guid)dox.AsValue());
            Assert.Equal((Guid?)ntj, (Guid?)dox.AsValue());
            Assert.Equal(stj.TryGetGuid(out var stjValue), dox.TryGetGuid(out var doxValue));
            Assert.Equal(stjValue, doxValue);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetGuid());
            Assert.Throws<InvalidOperationException>(() => (Guid?)stn);
            Assert.Throws<InvalidOperationException>(() => dox.GetGuid());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetGuid(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetGuid(out var doxValue));

            Assert.Equal((Guid)ntj, (Guid)dox.AsValue());
            Assert.Equal((Guid?)ntj, (Guid?)dox.AsValue());
        }

        if (mode == Mode.RelaxedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetGuid());
            Assert.Throws<InvalidOperationException>(() => (Guid?)stn);
            Assert.Throws<FormatException>(() => dox.GetGuid());
            Assert.Equal(stj.TryGetGuid(out var stjValue), dox.TryGetGuid(out var doxValue));
            Assert.Equal(stjValue, doxValue);

            var ntjGuid = (Guid)ntj;
            var doxGuid = (Guid)dox.AsValue();

            Assert.Equal((Guid)ntj, (Guid)dox.AsValue());
            Assert.Equal((Guid?)ntj, (Guid?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(FormatException), value => (Guid)value, ntjGuid);
        }

        if (mode == Mode.Nullable)
        {
            Assert.Throws<ArgumentException>(() => (Guid)ntj);
            Assert.Throws<NullReferenceException>(() => (Guid)stn!);
            Assert.Throws<InvalidOperationException>(() => (Guid)dox.AsValue());
            Assert.Throws<InvalidOperationException>(() => stj.TryGetGuid(out var stjValue));
            Assert.Throws<InvalidOperationException>(() => dox.TryGetGuid(out var stjValue));

            Assert.Equal((Guid?)ntj, (Guid?)dox.AsValue());
            Assert.Equal((Guid?)stn, (Guid?)dox.AsValue());
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => stj.GetGuid());
            Assert.Throws<InvalidOperationException>(() => dox.GetGuid());
            Assert.Throws<InvalidOperationException>(() => (Guid)dox.AsValue());

            try
            {
                var _ = (Guid)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }

        if (mode == Mode.FailedFormat)
        {
            Assert.Throws<FormatException>(() => stj.GetGuid());
            Assert.Throws<FormatException>(() => dox.GetGuid());
            Assert.Throws<InvalidOperationException>(() => (Guid)dox.AsValue());

            try
            {
                var _ = (Guid)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 254, 255 })]
    public void BinaryTest(byte[] src)
    {
        var json1 = System.Text.Json.JsonSerializer.Serialize(src);
        var json2 = Json.JsonSerializer.Serialize(src, SerializerSettings.Default);

        Assert.Equal(json1, json2);

        var bytes1 = System.Text.Json.JsonSerializer.Deserialize<byte[]>(json1);
        var bytes2 = Json.JsonSerializer.Deserialize<byte[]>(json2, SerializerSettings.Default);

        Assert.Equal(bytes1, bytes2);

        var element1 = JsonElement.Parse(json1);
        var element2 = Json.JsonDocument.Parse(json1, SerializerSettings.Default).RootElement;

        Assert.Equal(element1.ToString(), element2.ToString());

        Assert.True(element1.TryGetBytesFromBase64(out var bytes3));
        Assert.True(element2.TryGetByteString(out var bytes4));

        Assert.True(bytes3.AsSpan().SequenceEqual(bytes4));
    }

    [Fact]
    public void CharTest2()
    {
        var c1 = 'A';
        var c2 = 'あ';

        var json1 = System.Text.Json.JsonSerializer.Serialize(c1);
        var json2 = Json.JsonSerializer.Serialize(c1, SerializerSettings.Default);
        var json3 = System.Text.Json.JsonSerializer.Serialize(c2, new JsonSerializerOptions
        {
            Encoder
                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var json4 = Json.JsonSerializer.Serialize(c2, SerializerSettings.Default);

        Assert.Equal(json1, json2);
        Assert.Equal(json3, json4);
    }

    [Theory]
    [InlineData(@"0", Mode.Relaxed)]
    [InlineData(@"60", Mode.Relaxed)]
    [InlineData(@"65535", Mode.Relaxed)]
    [InlineData(@"60.0", Mode.Failed)]
    [InlineData(@"60.123", Mode.Failed)]
    [InlineData(@"""A""", Mode.Strict)]
    [InlineData(@"""\u0050""", Mode.Strict)]
    [InlineData(@"""ABC""", Mode.Failed)]
    [InlineData(@"true", Mode.Failed)]
    [InlineData(@"false", Mode.Failed)]
    [InlineData(@"{}", Mode.Failed)]
    [InlineData(@"[]", Mode.Failed)]
    [InlineData(@"""""", Mode.Failed)] // 空文字
//        [InlineData(@"""\uD800""", Mode.Failed)]           // サロゲート単体
    [InlineData(@"""€""", Mode.Strict)] // BMP 非 ASCII
    [InlineData(@"""𐀀""", Mode.Failed)] // 非 BMP（サロゲートペア必要）
    [InlineData(@"""a\u0301""", Mode.Failed)] // 結合文字（2コードユニット）
    [InlineData(@"""\u0000""", Mode.Strict)] // ヌル文字
    [InlineData(@"""\""""", Mode.Strict)] // ダブルクォート1文字
    [InlineData(@"65536", Mode.Failed)] // 範囲外（上限超）
    [InlineData(@"-1", Mode.Failed)] // 負数
    [InlineData(@"55296", Mode.Relaxed)] // サロゲート領域（D800）
    [InlineData(@"2147483647", Mode.Failed)] // 32bit max
    [InlineData(@"65535.0", Mode.Failed)] // 小数（整数表現でも不可）
    [InlineData(@"null", Mode.Nullable)] // Nullable キャストで null 許容の確認
    public void CharTest(string json, Mode mode)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        if (mode == Mode.Strict)
        {
            Assert.Equal(stj.GetString(), (string)ntj!);
            Assert.Equal(stj.GetString(), dox.GetString());

            Assert.Equal((char)ntj, (char)stn!);
            Assert.Equal((char)dox.AsValue(), (char)stn!);
        }

        if (mode == Mode.Relaxed)
        {
            Assert.Throws<InvalidOperationException>(() => (char)stn!);

            Assert.Equal((char)ntj, (char)dox.AsValue());
            Assert.Equal((char?)ntj, (char?)dox.AsValue());
            AssertRelaxedScalarConversion(json, typeof(InvalidOperationException), value => (char)value, (char)ntj);
        }

        if (mode == Mode.Failed)
        {
            Assert.Throws<InvalidOperationException>(() => (char)stn!);
            Assert.Throws<InvalidOperationException>(() => (char)dox.AsValue());

            try
            {
                var _ = (char)ntj;

                Assert.Fail("Expected exception not thrown.");
            }
            catch (Exception)
            {
            }
        }
    }

    [Theory]
    [InlineData(@"123")]
    [InlineData(@"true")]
    [InlineData(@"false")]
    [InlineData(@"null")]
    [InlineData(@"""abcdefg""")]
    [InlineData(@"[1,2,3,4]")]
    [InlineData(@"{""name"":""value"",""number"":12345}")]
    public void ToStringTest(string json)
    {
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        Assert.Equal(stj.ToString(), dox.ToString());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(stj), dox.ToJsonString());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(stj, new JsonSerializerOptions { WriteIndented = true }),
            dox.ToJsonString(new JsonWriteOptions { WriteIndented = true }));
    }

    [Fact]
    public void JsonPropertyToStringTest()
    {
        var json = """
                   {
                     "String":"Intel",
                     "Number":123,
                     "Boolean":true,
                     "Null":null,
                     "Array":["DVD read/writer",500],
                     "Object":{"Nested":"Value"},
                     "Escaped\"Name":"Line\nBreak"
                   }
                   """;

        using var stjDoc = System.Text.Json.JsonDocument.Parse(json);
        using var doxDoc = Json.JsonDocument.Parse(json, SerializerSettings.Default);

        var stjProperties = stjDoc.RootElement.EnumerateObject().ToArray();
        var doxProperties = doxDoc.RootElement.EnumerateObject().ToArray();

        Assert.Equal(stjProperties.Length, doxProperties.Length);

        for (var i = 0; i < stjProperties.Length; i++)
        {
            Assert.Equal(stjProperties[i].Name, doxProperties[i].Name);
            Assert.Equal(stjProperties[i].Value.ToString(), doxProperties[i].Value.ToString());
            Assert.Equal(stjProperties[i].ToString(), doxProperties[i].ToString());
        }
    }

    [Fact]
    public void JsonPropertyToStringAfterDisposeTest()
    {
        JsonProperty stjProperty;
        DProperty doxProperty;

        {
            using var stjDoc = System.Text.Json.JsonDocument.Parse("""{"Value":123}""");
            stjProperty = stjDoc.RootElement.EnumerateObject().First();
        }

        {
            using var doxDoc = Json.JsonDocument.Parse("""{"Value":123}""", SerializerSettings.Default);
            doxProperty = doxDoc.RootElement.EnumerateObject().First();
        }

        Assert.Throws<ObjectDisposedException>(() => stjProperty.ToString());
        Assert.Throws<ObjectDisposedException>(() => doxProperty.ToString());
    }

    [Fact]
    public void PropertyTest()
    {
        var json = """
                                   {
                     "CPU": "Intel",
                     "Drives": ["DVD read/writer","500 gigabyte hard drive"],
                     "Empty":null
                     }                
                   """;

        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var ntj = JToken.Parse(json);

        var stjArr = stj.GetProperty("Drives");
        var stnArr = stn?["Drives"]?.AsArray();
        var doxArr = dox.GetProperty("Drives");

        Assert.Throws<InvalidOperationException>(() => stj.GetArrayLength());
        Assert.Throws<InvalidOperationException>(() => dox.GetArrayLength());
        Assert.Throws<InvalidOperationException>(() => stjArr.GetPropertyCount());
        Assert.Throws<InvalidOperationException>(() => doxArr.GetPropertyCount());
        Assert.Throws<KeyNotFoundException>(() => stj.GetProperty("NonExisting"));
        Assert.Throws<KeyNotFoundException>(() => dox.GetProperty("NonExisting"));
        Assert.Throws<KeyNotFoundException>(() => stj.GetProperty("NonExisting"u8));
        Assert.Throws<KeyNotFoundException>(() => dox.GetProperty("NonExisting"u8));
        Assert.Throws<IndexOutOfRangeException>(() => stjArr[5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => stnArr?[5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => doxArr.AsArray()[5]);

        var stjEnumObj = stj.EnumerateObject();
        var doxEnumObj = dox.EnumerateObject();

        while (stjEnumObj.MoveNext() & doxEnumObj.MoveNext())
        {
            Assert.Equal(stjEnumObj.Current.Name, doxEnumObj.Current.Name);
            Assert.Equal(stjEnumObj.Current.Value.ToString(), doxEnumObj.Current.Value.ToString());
        }

        var stjEnumArr = stjArr.EnumerateArray();
        var doxEnumArr = doxArr.EnumerateArray();

        while (stjEnumArr.MoveNext() & doxEnumArr.MoveNext())
        {
            Assert.Equal(stjEnumArr.Current.ToString(), doxEnumArr.Current.ToString());
        }

        Assert.Equal(stj.TryGetProperty("CPU", out var stjProp), dox.TryGetProperty("CPU", out var doxProp));
        Assert.Equal(stjProp.ToString(), doxProp.ToString());
        Assert.Equal(stj.TryGetProperty("CPU"u8, out stjProp), dox.TryGetProperty("CPU"u8, out doxProp));
        Assert.Equal(stjProp.ToString(), doxProp.ToString());

        Assert.Equal(stj.TryGetProperty("CPU2", out stjProp), dox.TryGetProperty("CPU2", out doxProp));
        Assert.Equal(stj.TryGetProperty("CPU2"u8, out stjProp), dox.TryGetProperty("CPU2"u8, out doxProp));


        Assert.Equal(dox.GetPropertyCount(), stj.GetPropertyCount());
        Assert.Equal(dox.AsObject().Count, stn?.AsObject().Count);
        Assert.Equal(doxArr.GetArrayLength(), stjArr.GetArrayLength());
        Assert.Equal(doxArr.AsArray().Count, stnArr?.Count);

        Assert.Equal(dox.GetProperty("CPU").ToString(), stj.GetProperty("CPU").ToString());
        Assert.Equal(dox.GetProperty("Drives").AsValue()[0].ToString(), stj.GetProperty("Drives")[0].ToString());
        Assert.Equal(dox.GetProperty("Drives").AsValue()[1].ToString(), stj.GetProperty("Drives")[1].ToString());
        Assert.Equal(dox.GetProperty("CPU"u8).ToString(), stj.GetProperty("CPU"u8).ToString());
        Assert.Equal(dox.GetProperty("Drives"u8).AsValue()[0].ToString(), stj.GetProperty("Drives"u8)[0].ToString());
        Assert.Equal(dox.GetProperty("Drives"u8).AsValue()[1].ToString(), stj.GetProperty("Drives"u8)[1].ToString());

        Assert.Equal(dox.AsValue()["CPU"].ToString(), stn?["CPU"]?.ToString());

        var v1 = stn?["CPU2"]?.ToString();
        var v2 = dox.AsValue()["CPU2"].ToString();
        var v3 = ntj?["CPU2"]?.ToString();

//            Assert.Equal(v1,v2 );

        var s1 = (string?)stn?["CPU"];
        var s2 = (string?)dox.AsValue()["CPU"];

        Assert.Equal(s1, s2);

        var s3 = (string?)stn?["Empty"];
        var s4 = (string?)dox.AsValue()["Empty"];

        Assert.Equal(s3, s4);

        /*
        var s5 = (string?)stn?["Empty2"];
        var s6 = (string?)dox["Empty2"];

        Assert.Equal(s5, s6);
        */
    }


    [Fact]
    public void UnicodeAndEscapedPropertyNames()
    {
        // 絵文字（サロゲートペア）を含むキー
        var json = @"{ ""CPU\ud83d\ude00"": ""Intel"", ""日本語"": ""値"", ""empty"": null }";

        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        // UTF-16 文字列キー
        Assert.Equal(stj.GetProperty("CPU😀").ToString(), dox.GetProperty("CPU😀").ToString());
        Assert.Equal(stj.GetProperty("日本語").ToString(), dox.GetProperty("日本語").ToString());

        // UTF-8 Span キー（stackalloc）
        ReadOnlySpan<byte> utf8Key = Encoding.UTF8.GetBytes("CPU😀");
        Assert.Equal(stj.GetProperty(utf8Key).ToString(), dox.GetProperty(utf8Key).ToString());
    }


    [Fact]
    public void CultureInvariant_Output()
    {
        var ci = CultureInfo.GetCultureInfo("ja-JP");
        CultureInfo.CurrentCulture = ci;

        var json = @"{ ""N"": 1234.5, ""B"": true, ""T"": ""2024-01-01T00:00:00Z"" }";

        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        // JSON 出力はカルチャ非依存で一致
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(stj), dox.ToJsonString());
    }


    [Fact]
    public void DeepNesting_PathAndCounts()
    {
        var json = @"{ ""A"": { ""B"": { ""C"": [1, { ""D"": ""x"" }, 3] } } }";
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        // 配列・オブジェクトのカウント整合
        Assert.Equal(stj.GetProperty("A").GetProperty("B").GetProperty("C").GetArrayLength(),
            dox.GetProperty("A").GetProperty("B").GetProperty("C").GetArrayLength());

        // DoxElement 特有の GetPath() が STJ 風のパス表現（例：$.A.B.C[1].D）と一致するか
        var d = dox.GetProperty("A").GetProperty("B").GetProperty("C").AsValue()[1]["D"];
        Assert.Equal("$.A.B.C[1].D", d.GetPath());
    }


    [Fact]
    public void Enumeration_PreservesOrder()
    {
        var json = @"{ ""A"":1, ""B"":2, ""C"":3 }";
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        var stjNames = stj.EnumerateObject().Select(p => p.Name).ToArray();
        var doxNames = dox.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(stjNames, doxNames);
    }


    [Fact]
    public void ValueEquals_Utf8()
    {
        var json = @"{ ""CPU"": ""Intel"" }";
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("CPU");
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.GetProperty("CPU");

        var intel = "Intel"u8;

        Assert.True(stj.ValueEquals(intel));
        Assert.True(dox.ValueEquals("Intel"));
        Assert.True(dox.ValueEquals(intel));
    }


    [Fact]
    public void TryGetProperty_NullValue()
    {
        var json = @"{ ""ExistsNull"": null }";
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        Assert.True(stj.TryGetProperty("ExistsNull", out var stjProp));
        Assert.True(dox.TryGetProperty("ExistsNull", out var doxProp));
        Assert.Equal(stjProp.ValueKind.ToString(), doxProp.ValueKind.ToString()); // Null vs Null
    }


    [Fact]
    public void EmptyObjectAndArray()
    {
        var json = @"{ ""O"": {}, ""A"": [] }";
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var stn = JsonNode.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        Assert.Equal(0, stj.GetProperty("O").GetPropertyCount());
        Assert.Equal(0, dox.GetProperty("O").GetPropertyCount());

        Assert.Equal(0, stj.GetProperty("A").GetArrayLength());
        Assert.Equal(0, dox.GetProperty("A").GetArrayLength());

        Assert.Equal(0, stn?["O"]?.AsObject().Count);
        Assert.Equal(0, stn?["A"]?.AsArray().Count);
    }

    [Theory]
    [InlineData(@"null")]
    [InlineData(@"true")]
    [InlineData(@"false")]
    [InlineData(@"""text""")]
    [InlineData(@"123")]
    [InlineData(@"-12.5")]
    [InlineData(@"[1,true,null,""x"",{""n"":2}]")]
    [InlineData(
        @"{""name"":""Taro"",""age"":30,""enabled"":true,""tags"":[""a"",""b""],""profile"":{""score"":12.5,""empty"":null}}")]
    [InlineData(@"{""escaped\u0020name"":""value"",""日本語"":""値"",""emoji\ud83d\ude00"":""ok""}")]
    public void DoxElement_MatchesJsonElementDomBehavior(string json)
    {
        using var stjDoc = System.Text.Json.JsonDocument.Parse(json);
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        AssertJsonElementCompatible(stjDoc.RootElement, dox);
    }

    [Fact]
    public void DoxElement_MatchesJsonElementInvalidOperationForWrongKinds()
    {
        var json =
            @"{ ""String"": ""value"", ""Number"": 1, ""Boolean"": true, ""Null"": null, ""Array"": [1], ""Object"": { ""A"": 1 } }";
        using var stjDoc = System.Text.Json.JsonDocument.Parse(json);
        var stj = stjDoc.RootElement;
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        Assert.Throws<InvalidOperationException>(() => stj.GetProperty("String").GetArrayLength());
        Assert.Throws<InvalidOperationException>(() => dox.GetProperty("String").GetArrayLength());
        Assert.Throws<InvalidOperationException>(() => stj.GetProperty("Array").GetPropertyCount());
        Assert.Throws<InvalidOperationException>(() => dox.GetProperty("Array").GetPropertyCount());
        Assert.Throws<InvalidOperationException>(() => stj.GetProperty("Number").GetBoolean());
        Assert.Throws<InvalidOperationException>(() => dox.GetProperty("Number").GetBoolean());
        Assert.Throws<InvalidOperationException>(() => stj.GetProperty("Object").GetString());
        Assert.Throws<InvalidOperationException>(() => dox.GetProperty("Object").GetString());
        Assert.Throws<KeyNotFoundException>(() => stj.GetProperty("Missing"));
        Assert.Throws<KeyNotFoundException>(() => dox.GetProperty("Missing"));
    }

    [Fact]
    public void NumericBoundaries_And_Overflow()
    {
        var json = $@"{{ 
        ""MaxByte"": {byte.MaxValue}, 
        ""OverflowByte"": {byte.MaxValue + 1}, 
        ""MaxInt"": {int.MaxValue}, 
        ""MinInt"": {int.MinValue} 
    }}";
        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        // 正常系
        Assert.Equal(byte.MaxValue, (byte)dox.AsValue()["MaxByte"]);
        Assert.Equal(int.MaxValue, (int)dox.AsValue()["MaxInt"]);

        // 異常系：Byteへのキャストでオーバーフローする場合の例外確認
        // STJは throw しますが、Doxの実装もそれに合わせているか？
        Assert.Throws<InvalidOperationException>(() => (byte)dox.AsValue()["OverflowByte"]);
    }

    [Fact]
    public void SpecialTypes_Guid_DateTime()
    {
        var guid = Guid.NewGuid();
        var date = DateTime.UtcNow;
        // JSON上では文字列として表現される
        var json = $@"{{ ""G"": ""{guid}"", ""D"": ""{date:O}"" }}";

        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;

        // 文字列からのパース変換確認
        Assert.Equal(guid, (Guid)dox.AsValue()["G"]);
        Assert.Equal(date.ToUniversalTime(), ((DateTime)dox.AsValue()["D"]).ToUniversalTime());
    }

    [Fact]
    public void Modification_ReplaceValue()
    {
        var json = @"{ ""Count"": 1 }";
        var doc = Json.JsonDocument.Parse(json, SerializerSettings.Default); // RootではなくDoc自体を保持
        var root = doc.RootElement;

        // 値を更新
        root.GetProperty("Count").AsValue().ReplaceWith(999);

        // 同じIDを見ているため、反映されていることを期待する設計か？
        Assert.Equal(999, (int)root.GetProperty("Count").AsValue());

        // JSON出力にも反映されるか
        Assert.Contains("999", root.ToJsonString());
    }

    [Fact]
    public void DoxElementOperation_ObjectAndArrayMutations()
    {
        var json = @"{ ""Name"": ""Taro"", ""Enabled"": true, ""Items"": [1, 2] }";
        var doc = Json.JsonDocument.Parse(json, SerializerSettings.Default);
        var root = doc.RootElement;

        var obj = root.AsObject();
        Assert.False(obj.TryAdd("Enabled", true));

        obj["Enabled"] = false;
        Assert.True(obj.Remove("Name"));

        var tags = obj.AddArray("Tags");
        tags.Add("alpha");
        tags.Add("beta");

        root = doc.RootElement;

        var items = root.GetProperty("Items").AsArray();
        items.Insert(1, 10);
        items[0].ReplaceWith(99);
        items.RemoveAt(1);

        var tail = items.AddObject();
        tail.Add("Kind", "tail");
        tail.Add("Value", 42);

        root = doc.RootElement;

        Assert.Equal(3, root.GetPropertyCount());
        Assert.False(root.TryGetProperty("Name", out _));
        Assert.False(root.GetProperty("Enabled").GetBoolean());
        Assert.Equal(3, root.GetProperty("Items").GetArrayLength());
        Assert.Equal(99, root.GetProperty("Items").AsArray()[0].AsElement().GetInt32());
        Assert.Equal(2, root.GetProperty("Items").AsArray()[1].AsElement().GetInt32());
        Assert.Equal("tail", root.GetProperty("Items").AsArray()[2].AsObject()["Kind"].AsElement().GetString());
        Assert.Equal("$.Items[2].Value", root.GetProperty("Items").AsArray()[2].AsObject()["Value"].GetPath());
        Assert.Equal("$.Tags[1]", root.GetProperty("Tags").AsArray()[1].GetPath());

        Assert.Equal(
            @"{""Enabled"":false,""Items"":[99,2,{""Kind"":""tail"",""Value"":42}],""Tags"":[""alpha"",""beta""]}",
            root.ToJsonString());
    }

    [Fact]
    public void DoxElementOperation_CloneIsIndependent()
    {
        var json = @"{ ""Nested"": { ""Value"": 1 }, ""Items"": [1] }";
        var root = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var clone = root.Clone();

        clone.GetProperty("Nested").AsObject()["Value"] = 2;
        clone.GetProperty("Items").AsArray().Add(3);
        clone.AsObject().Add("Added", true);

        Assert.Equal(1, root.GetProperty("Nested").GetProperty("Value").GetInt32());
        Assert.Equal(1, root.GetProperty("Items").GetArrayLength());
        Assert.False(root.TryGetProperty("Added", out _));

        Assert.Equal(2, clone.GetProperty("Nested").GetProperty("Value").GetInt32());
        Assert.Equal(2, clone.GetProperty("Items").GetArrayLength());
        Assert.True(clone.GetProperty("Added").GetBoolean());
        Assert.NotEqual(root, clone);
    }

    /*
    [Fact]
    public void Equality_EdgeCases()
    {
        string json = @"{ ""A"": 1, ""B"": 1.0, ""S"": ""1"" }";
        var dox = REDox.JsonDocument.Parse(json).Root;
        var stn = System.Text.Json.Nodes.JsonNode.Parse(json);

        // 数値としての等価性（JSON仕様的には 1 と 1.0 は等しい場合が多いが、ライブラリによる）
        // Doxの実装次第だが、ここを明確にしておくテストは価値がある
        Assert.IsFalse(dox["A"] == dox["B"]);
        Assert.IsFalse(stn?["A"] == stn?["B"]);

        // 文字列 "1" と 数値 1 は異なるはず
        Assert.IsFalse(dox["A"] == dox["S"]);
        Assert.IsFalse(stn?["A"] == stn?["B"]);

        // null 比較
        Assert.IsFalse(dox["A"] == (string?)null);
        Assert.IsFalse(stn?["A"] == null);
    }
    */

    [Fact]
    public void Binary_Base64_Handling()
    {
        byte[] data = { 1, 2, 3, 4, 5 };
        var base64 = Convert.ToBase64String(data);
        var json = $@"{{ ""Bin"": ""{base64}"" }}";

        var dox = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        var stj = System.Text.Json.JsonDocument.Parse(json).RootElement;

        Assert.True(data.SequenceEqual(stj.GetProperty("Bin").GetBytesFromBase64()));
    }

    private static void AssertJsonElementCompatible(JsonElement stj, DElement dox)
    {
        Assert.Equal((int)stj.ValueKind, (int)dox.ValueKind);

        switch (stj.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                AssertObjectCompatible(stj, dox);
                break;

            case System.Text.Json.JsonValueKind.Array:
                AssertArrayCompatible(stj, dox);
                break;

            case System.Text.Json.JsonValueKind.String:
                Assert.Equal(stj.GetString(), dox.GetString());
                break;

            case System.Text.Json.JsonValueKind.Number:
                AssertNumberCompatible(stj, dox);
                break;

            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                Assert.Equal(stj.GetBoolean(), dox.GetBoolean());
                break;

            case System.Text.Json.JsonValueKind.Null:
                Assert.Null(stj.GetString());
                Assert.Null(dox.GetString());
                break;
        }
    }

    private static void AssertObjectCompatible(JsonElement stj, DElement dox)
    {
        Assert.Equal(stj.GetPropertyCount(), dox.GetPropertyCount());

        var stjProps = stj.EnumerateObject().ToArray();
        var doxProps = dox.EnumerateObject().ToArray();

        Assert.Equal(stjProps.Length, doxProps.Length);

        for (var i = 0; i < stjProps.Length; i++)
        {
            var stjProp = stjProps[i];
            var doxProp = doxProps[i];

            Assert.Equal(stjProp.Name, doxProp.Name);
            AssertJsonElementCompatible(stjProp.Value, doxProp.Value);

            Assert.Equal(stj.TryGetProperty(stjProp.Name, out var stjByName),
                dox.TryGetProperty(stjProp.Name, out var doxByName));
            AssertJsonElementCompatible(stjByName, doxByName);

            var utf8Name = Encoding.UTF8.GetBytes(stjProp.Name);
            Assert.Equal(stj.TryGetProperty(utf8Name, out var stjByUtf8),
                dox.TryGetProperty(utf8Name, out var doxByUtf8));
            AssertJsonElementCompatible(stjByUtf8, doxByUtf8);
        }
    }

    private static void AssertArrayCompatible(JsonElement stj, DElement dox)
    {
        Assert.Equal(stj.GetArrayLength(), dox.GetArrayLength());

        var stjItems = stj.EnumerateArray().ToArray();
        var doxItems = dox.EnumerateArray().ToArray();

        Assert.Equal(stjItems.Length, doxItems.Length);

        for (var i = 0; i < stjItems.Length; i++)
        {
            AssertJsonElementCompatible(stjItems[i], doxItems[i]);
        }
    }

    private static void AssertNumberCompatible(JsonElement stj, DElement dox)
    {
        Assert.Equal(stj.TryGetInt32(out var stjInt32), dox.TryGetInt32(out var doxInt32));
        Assert.Equal(stjInt32, doxInt32);
        Assert.Equal(stj.TryGetInt64(out var stjInt64), dox.TryGetInt64(out var doxInt64));
        Assert.Equal(stjInt64, doxInt64);
        Assert.Equal(stj.TryGetDouble(out var stjDouble), dox.TryGetDouble(out var doxDouble));
        Assert.Equal(stjDouble, doxDouble);
        Assert.Equal(stj.TryGetDecimal(out var stjDecimal), dox.TryGetDecimal(out var doxDecimal));
        Assert.Equal(stjDecimal, doxDecimal);
    }
}