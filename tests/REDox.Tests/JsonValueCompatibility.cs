using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public class JsonValueCompatibility
{
    private static void CompareDom(JsonNode? stj, JToken ntj, DValue dox)
    {
        var doxJson = dox.ToJsonString();

        Assert.Equal(stj?.ToJsonString() ?? "null", doxJson);
        Assert.Equal(ntj.ToString(Formatting.None), doxJson);
        Assert.True(JsonNode.DeepEquals(stj, JsonNode.Parse(doxJson)));
        Assert.True(JToken.DeepEquals(ntj, JToken.Parse(doxJson)));
    }

    private void CompareObject(JsonObject s, DObject d)
    {
        Assert.Equal(s.ToJsonString(), d.ToJsonString());
        Assert.Equal(s.Count, d.Count);

        for (var i = 0; i < s.Count; i++)
        {
            var sv = s.GetAt(i);
            var dv = d.GetAt(i);

            Assert.Equal(sv.Key, dv.Key);
            Assert.Equal(JsonNode.DeepEquals(sv.Value, s[sv.Key]), DValue.DeepEquals(dv.Value, d[dv.Key]));

            Assert.Equal(sv.Value?.GetPropertyName(), dv.Value.GetPropertyName());
            Assert.Equal(sv.Value?.GetPath(), dv.Value.GetPath());
        }

        var se = s.GetEnumerator();
        var de = d.GetEnumerator();

        for (;;)
        {
            var sm = se.MoveNext();
            var dm = de.MoveNext();

            Assert.Equal(sm, dm);

            if (!sm)
            {
                break;
            }

            var sv = se.Current;
            var dv = de.Current;

            Assert.Equal(sv.Key, dv.Key);
            Assert.Equal(JsonNode.DeepEquals(sv.Value, s[sv.Key]), DValue.DeepEquals(dv.Value, d[dv.Key]));
            Assert.Equal(sv.Value?.GetPropertyName(), dv.Value.GetPropertyName());

            Assert.Equal(sv.Value?.GetPath(), dv.Value.GetPath());
        }
    }

    [Fact]
    public void HalfValue()
    {
        var half = (Half)1.5;

        var value = DValue.Create(half);

        var arr = new DArray(value);

        var obj = new DObject
        {
            { "arr", arr }
        };

        var json = obj.ToJsonString();

        Assert.Equal("{\"arr\":[1.5]}", json);
    }

    [Fact]
    public void ObjectValue()
    {
        var s1 = new JsonObject
            { { "name", "myname" }, { "age", 46 }, { "mail", true }, { "list", new JsonArray { 1, 2, 3 } } };
        var s2 = new JsonObject
            { { "name", "myname" }, { "age", 46.0 }, { "mail", true }, { "list", new JsonArray { 1.0, 2.0, 3.0 } } };
        var s3 = new JsonObject
            { { "age", 46 }, { "name", "myname" }, { "mail", true }, { "list", new JsonArray { 1, 2, 3 } } };
        var d1 = new DObject
            { { "name", "myname" }, { "age", 46 }, { "mail", true }, { "list", new DArray { 1, 2, 3 } } };
        var d2 = new DObject
            { { "name", "myname" }, { "age", 46.0 }, { "mail", true }, { "list", new DArray { 1.0, 2.0, 3.0 } } };
        var d3 = new DObject
            { { "age", 46 }, { "name", "myname" }, { "mail", true }, { "list", new DArray { 1, 2, 3 } } };

        Assert.Equal(s1.ToJsonString(), s2.ToJsonString());
        Assert.Equal(d1.ToJsonString(), d2.ToJsonString());

        Assert.Equal(JsonNode.DeepEquals(s1, s2), DValue.DeepEquals(d1, d2));
        Assert.Equal(JsonNode.DeepEquals(s1, s3), DValue.DeepEquals(d1, d3));

        CompareObject(s1, d1);

        s1.Add("newprop", "newvalue");
        d1.Add("newprop", "newvalue");

        CompareObject(s1, d1);

        Assert.Equal(s1.TryAdd("newprop", "newvalue"), d1.TryAdd("newprop", "newvalue"));
        Assert.Equal(s1.TryAdd("newprop2", true), d1.TryAdd("newprop2", true));

        CompareObject(s1, d1);

        Assert.Throws<ArgumentException>(() => s1.Add("newprop", "newvalue"));
        Assert.Throws<ArgumentException>(() => d1.Add("newprop", "newvalue"));

        CompareObject(s1, d1);

        Assert.Equal(JsonNode.DeepEquals(s1, s3), DValue.DeepEquals(d1, d3));

        s1.Remove("age");
        d1.Remove("age");

        CompareObject(s1, d1);

//            Assert.Equal(s1.TryGetPropertyValue("age", out var sa),d1.TryGet

        s1.Clear();
        d1.Clear();

        CompareObject(s1, d1);

        s1.Add("age", 123);
        d1.Add("age", 123);

        CompareObject(s1, d1);
    }

    /*
    [Fact]
    public void DateTimeValue()
    {
        var time = DateTime.Now;
        var dto = DateTimeOffset.Now;

        JsonArray sa = new JsonArray();
        DoxArray da = new DoxArray(new REDox.Serialization.SystemTextJson.SystemTextJsonSerializerSettings());
        sa.Add(time);
        da.Add(time);

        Assert.Equal(sa.ToJsonString(), da.ToJsonString());

        sa[0] =  time;
        da[0] =  time;

        Assert.Equal(sa.ToJsonString(), da.ToJsonString());

        sa[0] =  dto;
        da[0] =  dto;

        Assert.Equal(sa.ToJsonString(), da.ToJsonString());

        sa[0] = System.Text.Json.JsonSerializer.SerializeToNode(new DateTime[]{time,time});
        da[0] = DoxValue.Create(new DateTime[]{time,time});

        Assert.Equal(sa.ToJsonString(), da.ToJsonString());
    }
    */

    [Fact]
    public void ArrayValue()
    {
        var s1 = new JsonArray { 1, 2, 3 };
        var s2 = new JsonArray { 1.0, 2.0, 3.0 };
        var s3 = new JsonArray { 1.0, 2.0, 4.0 };
        var d1 = new DArray { 1, 2, 3 };
        var d2 = new DArray { 1.0, 2.0, 3.0 };
        var d3 = new DArray { 1.0, 2.0, 4.0 };

        for (var i = 0; i < s1.Count; i++)
        {
            Assert.Equal(s1[i]!.GetElementIndex(), i);
        }

        for (var i = 0; i < d1.Count; i++)
        {
            Assert.Equal(d1[i].GetElementIndex(), i);
        }

        Assert.Equal(s1.ToJsonString(), s2.ToJsonString());
        Assert.Equal(d1.ToJsonString(), d2.ToJsonString());
        Assert.Equal(s1.ToJsonString(), d1.ToJsonString());

        Assert.Equal(JsonNode.DeepEquals(s1, s2), DValue.DeepEquals(d1, d2));
        Assert.Equal(JsonNode.DeepEquals(s1, s3), DValue.DeepEquals(d1, d3));
    }

    [Fact]
    public void ArrayDomMutations_MatchSystemTextJsonAndNewtonsoft()
    {
        var stj = new JsonArray
        {
            1,
            new JsonObject { ["name"] = "old" },
            new JsonArray { true, false },
            null
        };
        var ntj = new JArray(
            1,
            new JObject { ["name"] = "old" },
            new JArray(true, false),
            JValue.CreateNull());
        var dox = new DArray
        {
            1,
            new DObject { { "name", "old" } },
            new DArray { true, false },
            DValue.Null
        };

        CompareDom(stj, ntj, dox.AsValue());

        stj[0] = "one";
        ntj[0] = "one";
        dox[0] = "one";

        stj.Insert(2, new JsonObject { ["created"] = true });
        ntj.Insert(2, new JObject { ["created"] = true });
        dox.Insert(2, new DObject { { "created", true } });

        CompareDom(stj, ntj, dox.AsValue());
        Assert.Equal(stj[2]!["created"]!.GetPath(), dox[2]["created"].GetPath());

        stj[3]!.AsArray().Insert(1, 10);
        ((JArray)ntj[3]!).Insert(1, 10);
        dox[3].AsArray().Insert(1, 10);

        stj[1] = "object replaced";
        ntj[1] = "object replaced";
        dox[1].ReplaceWith("object replaced");

        CompareDom(stj, ntj, dox.AsValue());
        Assert.Equal(stj[3]![1]!.GetPath(), dox[3][1].GetPath());

        var stjRemoved = stj[4];
        Assert.True(stj.Remove(stjRemoved));
        ntj[4]!.Remove();
        Assert.True(dox.Remove(dox[4]));

        stj.Add(new JsonArray { "tail", null });
        ntj.Add(new JArray("tail", JValue.CreateNull()));
        dox.Add(new DArray { "tail", DValue.Null });

        CompareDom(stj, ntj, dox.AsValue());

        stj.Clear();
        ntj.RemoveAll();
        dox.Clear();

        CompareDom(stj, ntj, dox.AsValue());

        stj.Add(42);
        ntj.Add(42);
        dox.Add(42);

        CompareDom(stj, ntj, dox.AsValue());
    }

    [Fact]
    public void ValueReplacements_MatchSystemTextJsonAndNewtonsoft()
    {
        var stj = JsonNode.Parse("""{"value":1,"items":[{"kind":"old"},2]}""")!.AsObject();
        var ntj = JObject.Parse("""{"value":1,"items":[{"kind":"old"},2]}""");
        var dox = DValue.ParseJson("""{"value":1,"items":[{"kind":"old"},2]}""").AsObject();

        var oldDoxObject = dox["items"][0].AsObject();
        Assert.True(oldDoxObject.IsValid);

        stj["value"] = "text";
        ntj["value"] = "text";
        dox["value"].ReplaceWith("text");

        stj["items"]![0] = true;
        ntj["items"]![0] = true;
        dox["items"][0].ReplaceWith(true);

        CompareDom(stj, ntj, dox.AsValue());
        Assert.False(oldDoxObject.IsValid);

        stj["value"] = null;
        ntj["value"] = JValue.CreateNull();
        dox["value"].ReplaceWith(DValue.Null);

        stj["items"]![1] = new JsonObject { ["nested"] = new JsonArray { 1, "two" } };
        ntj["items"]![1] = new JObject { ["nested"] = new JArray(1, "two") };
        dox["items"][1].ReplaceWith(new DObject
        {
            { "nested", new DArray { 1, "two" } }
        });

        CompareDom(stj, ntj, dox.AsValue());
        Assert.Equal(stj["items"]![1]!["nested"]![1]!.GetPath(), dox["items"][1]["nested"][1].GetPath());

        stj["value"] = new JsonArray { false, new JsonObject { ["id"] = 7 } };
        ntj["value"] = new JArray(false, new JObject { ["id"] = 7 });
        dox["value"].ReplaceWith(new DArray
        {
            false,
            new DObject { { "id", 7 } }
        });

        stj["items"]!.AsArray().RemoveAt(0);
        ((JArray)ntj["items"]!).RemoveAt(0);
        dox["items"].AsArray().RemoveAt(0);

        CompareDom(stj, ntj, dox.AsValue());
        Assert.Equal(stj["items"]![0]!["nested"]![0]!.GetPath(), dox["items"][0]["nested"][0].GetPath());
    }

    [Fact]
    public void ArrayRemoveNullValue_MatchesSystemTextJsonAndNewtonsoftResult()
    {
        var stj = new JsonArray { null, 1, null, "tail" };
        var ntj = new JArray(JValue.CreateNull(), 1, JValue.CreateNull(), "tail");
        var dox = new DArray { DValue.Null, 1, DValue.Null, "tail" };

        Assert.True(stj.Remove(null));
        ntj.RemoveAt(0);
        Assert.True(dox.Remove(DValue.Null));

        CompareDom(stj, ntj, dox.AsValue());
//        Assert.True(dox.Contains(DValue.Null));

        Assert.True(stj.Remove(null));
        ntj.RemoveAt(1);
        Assert.True(dox.Remove(DValue.Null));

        CompareDom(stj, ntj, dox.AsValue());
        Assert.False(stj.Remove(null));
        Assert.False(dox.Remove(DValue.Null));
    }

    [Fact]
    public void SymbolTest()
    {
        var value = DValue.Create(new Utf8Symbol("ABC"));

        Assert.Equal("ABC", value.ToString());
        Assert.Equal(DTokenKind.Symbol, value.GetToken().Kind);

        var map = new DMap();
        map.Add(value, value);

        Assert.Equal(@"{""ABC"":""ABC""}", map.ToJsonString());

        var obj = new DObject();
        obj.Add((string)value!, DValue.Create((byte)123));

        Assert.Equal(@"{""ABC"":123}", obj.ToJsonString());
    }

    [Fact]
    public void ArrayCopyTo_WithOffsetAndInvalidArguments()
    {
        ICollection<DValue> values = new DArray
        {
            1,
            "two",
            DValue.Null
        };

        var copied = new DValue[5];

        values.CopyTo(copied, 1);

        Assert.Equal(default, copied[0]);
        Assert.Equal(1, (int)copied[1]);
        Assert.Equal("two", (string?)copied[2]);
        Assert.True(copied[3].GetToken().Kind == DTokenKind.Null);
        Assert.Equal(default, copied[4]);

        Assert.Throws<ArgumentNullException>(() => values.CopyTo(null!, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => values.CopyTo(new DValue[3], -1));

        Assert.Throws<ArgumentException>(() => values.CopyTo(new DValue[3], 1));
    }

    [Fact]
    public void StandaloneValueReplaceWith_ReturnsReplacementMatchingSystemTextJsonAndNewtonsoft()
    {
        var original = DValue.Create(1);

        JsonNode? stj = JsonValue.Create("two");
        JToken ntj = new JValue("two");
        var dox = original.ReplaceWith("two");

        CompareDom(stj, ntj, dox);
        Assert.Equal("1", original.ToJsonString());

        stj = new JsonArray { 1, false };
        ntj = new JArray(1, false);
        dox = dox.ReplaceWith(new DArray { 1, false });

        CompareDom(stj, ntj, dox);

        JsonNode? stjNull = null;
        ntj = JValue.CreateNull();
        dox = dox.ReplaceWith(DValue.Null);

        CompareDom(stjNull, ntj, dox);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 64, 128, 255 })]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 })]
    public void BinaryValue(byte[] src)
    {
        var base16 = @"""" + Convert.ToHexString(src) + @"""";
        var base64 = @"""" + Convert.ToBase64String(src) + @"""";
        var base64Url = @"""" + ToBase64Url(src) + @"""";

        var d1 = DValue.Create(src, ByteStringKind.Base16);
        Assert.Equal(base16, d1.ToJsonString());

        var d2 = DValue.Create(src, ByteStringKind.Base64);
        Assert.Equal(base64, d2.ToJsonString());

        var d3 = DValue.Create(src, ByteStringKind.Default);
        Assert.Equal(base64, d3.ToJsonString());

        var d4 = DValue.Create(src, ByteStringKind.Base64Url);
        Assert.Equal(base64Url, d4.ToJsonString());

        var arr = new DArray { d1, d2, d3, d4, DValue.Create(src, ByteStringKind.Raw) };

        Assert.Equal(base16.Trim('"'), (string?)arr[0]);
        Assert.Equal(base64.Trim('"'), (string?)arr[1]);
        Assert.Equal(base64.Trim('"'), (string?)arr[2]);
        Assert.Equal(base64Url.Trim('"'), (string?)arr[3]);
        Assert.Equal(Encoding.UTF8.GetString(src), (string?)arr[4]);

        var e1 = Json.JsonDocument.Parse(d1.ToJsonString(), SerializerSettings.Default).RootElement;
        var e2 = Json.JsonDocument.Parse(d2.ToJsonString(), SerializerSettings.Default).RootElement;
        var e3 = Json.JsonDocument.Parse(d3.ToJsonString(), SerializerSettings.Default).RootElement;
        var e4 = Json.JsonDocument.Parse(d4.ToJsonString(), SerializerSettings.Default).RootElement;

        Assert.True(src.AsSpan().SequenceEqual(e1.GetByteString(ByteStringKind.Base16)));
        Assert.True(src.AsSpan().SequenceEqual(e2.GetByteString(ByteStringKind.Base64)));
        Assert.True(src.AsSpan().SequenceEqual(e3.GetByteString()));
        Assert.True(src.AsSpan().SequenceEqual(e4.GetByteString(ByteStringKind.Base64Url)));
    }

    [Fact]
    public void BinaryGuidValue_ToString()
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var arr = new DArray { DValue.Create(guid) };

        Assert.Equal(guid.ToString(), (string?)arr[0]);
    }

    [Theory]
    [InlineData("A")]
    public void StringValue(string value)
    {
        JsonNode s = JsonValue.Create(value)!;
        var d = DValue.Create(value);
        Assert.Equal(s.ToString(), d.ToString());
        Assert.Equal(s.GetPath(), d.GetPath());
        Assert.Equal((int)s.GetValueKind(), (int)d.GetValueKind());
    }

    [Theory]
    [InlineData('A')]
//        [InlineData('あ')]
    public void CharValue(char value)
    {
        JsonNode s = JsonValue.Create(value);
        var d = DValue.Create(value);
        Assert.Equal(
            s.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            d.ToJsonString());
        Assert.Equal(s.ToString(), d.ToString());
        Assert.Equal(s.GetPath(), d.GetPath());
        Assert.Equal((int)s.GetValueKind(), (int)d.GetValueKind());
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(1234567UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MinValue)]
    [InlineData(123456789101112UL)]
    public void UInt64Value(ulong value)
    {
        JsonNode s = JsonValue.Create(value);
        var d = DValue.Create(value);

        Assert.Equal(s.ToJsonString(), d.ToJsonString());
        Assert.Equal(s.ToString(), d.ToString());
        Assert.Equal(s.GetPath(), d.GetPath());
        Assert.Equal((int)s.GetValueKind(), (int)d.GetValueKind());

        var s2 = s;
        var d2 = d;

        Assert.Equal(s, s2);
        Assert.Equal(d, d2);

        s = value;
        d = value;

        Assert.NotEqual(s, s2);
        Assert.Equal(d, d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (decimal)value;
        d = (decimal)value;

        Assert.False(s == s2);
//        Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (double)value;
        d = (double)value;

        Assert.False(s == s2);
        //      Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (decimal)value;
        d = (decimal)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(1234567)]
    [InlineData(123456789101112)]
    [InlineData(-1234567)]
    [InlineData(-123456789101112)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void IntValue(long value)
    {
        JsonNode s = JsonValue.Create(value);
        var d = DValue.Create(value);

        Assert.Equal(s.ToJsonString(), d.ToJsonString());
        Assert.Equal(s.ToString(), d.ToString());
        Assert.Equal(s.GetPath(), d.GetPath());
        Assert.Equal((int)s.GetValueKind(), (int)d.GetValueKind());

        var s2 = s;
        var d2 = d;

        Assert.Equal(s, s2);
        Assert.Equal(d, d2);

        s = value;
        d = value;

        Assert.NotEqual(s, s2);
        Assert.Equal(d, d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (decimal)value;
        d = (decimal)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (double)value;
        d = (double)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (decimal)value;
        d = (decimal)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));
    }


    [Theory]
    [InlineData(1.23)]
    [InlineData(0.3333333432674408)]
    [InlineData(1234567.89)]
    [InlineData(123456789101112.0001)]
    public void FloatValue(double value)
    {
        JsonNode s = JsonValue.Create(value);
        var d = DValue.Create(value);

        Assert.Equal(s.ToJsonString(), d.ToJsonString());
        Assert.Equal(s.ToString(), d.ToString());
        Assert.Equal(s.GetPath(), d.GetPath());
        Assert.Equal((int)s.GetValueKind(), (int)d.GetValueKind());

        var s2 = s;
        var d2 = d;

        Assert.Equal(s, s2);
        Assert.Equal(d, d2);

        s = value;
        d = value;

        Assert.NotEqual(s, s2);
        Assert.Equal(d, d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (float)value;
        d = (float)value;

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (decimal)value;
        d = (decimal)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (long)value;
        d = (long)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));
    }

    [Theory]
    [InlineData("1.23")]
    [InlineData("0.00")]
    [InlineData("1234567.89")]
    [InlineData("123456789101112.0001")]
    [InlineData("4503599627370496")]
    public void DecimalValue(string text)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);

        JsonNode s = JsonValue.Create(value);
        var d = DValue.Create(value);

        Assert.Equal(s.ToJsonString(), d.ToJsonString());
        Assert.Equal(s.ToString(), d.ToString());
        Assert.Equal(s.GetPath(), d.GetPath());
        Assert.Equal((int)s.GetValueKind(), (int)d.GetValueKind());

        var s2 = s;
        var d2 = d;

        Assert.Equal(s, s2);
        Assert.Equal(d, d2);

        s = value;
        d = value;

        Assert.NotEqual(s, s2);
        Assert.Equal(d, d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (float)value;
        d = (float)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (double)value;
        d = (double)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));

        s = (long)value;
        d = (long)value;

        Assert.False(s == s2);
        //Assert.False(d == d2);

        Assert.Equal(JsonNode.DeepEquals(s, s2), DValue.DeepEquals(d, d2));
    }
}