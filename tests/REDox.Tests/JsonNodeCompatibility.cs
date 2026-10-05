using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class JsonNodeCompatibility
{
    [Fact]
    public void Override()
    {
        var doxArr = new DArray { 1, 2, 3 };
        for (var i = 0; i < 1000; i++)
        {
            doxArr[0] = new DObject { { "A", 1 }, { "B", true } };
        }
    }

    [Fact]
    public void ValidTest()
    {
        var inst = new { name = "my name", age = 33, date = DateTime.Now };

        var obj = DValue.From(inst, SerializerSettings.Default).AsObject();

        Assert.True(obj.FirstOrDefault(kv => kv.Key == "name").Value.IsValid);
        Assert.True(obj.FirstOrDefault(kv => kv.Key == "name").Value.GetValueKind() == Json.JsonValueKind.String);
        Assert.True(obj.FirstOrDefault(kv => kv.Key == "name").Value.GetToken().Kind == DTokenKind.String);

        Assert.False(obj.FirstOrDefault(kv => kv.Key == "name2").Value.IsValid);
        Assert.True(obj.FirstOrDefault(kv => kv.Key == "name2").Value.GetValueKind() == Json.JsonValueKind.Undefined);
        Assert.True(obj.FirstOrDefault(kv => kv.Key == "name2").Value.GetToken().Kind == DTokenKind.Control);


        var arr1 = DValue.From(new[] { 1, 2, 3 }).AsArray();

        Assert.True(arr1.FirstOrDefault().IsValid);
        Assert.True(arr1.FirstOrDefault().GetValueKind() == Json.JsonValueKind.Number);
        Assert.True(arr1.FirstOrDefault().GetToken().Kind == DTokenKind.Integer);

        var arr2 = DValue.From(new int[] { }).AsArray();

        Assert.False(arr2.FirstOrDefault().IsValid);
        Assert.True(arr2.FirstOrDefault().GetValueKind() == Json.JsonValueKind.Undefined);
        Assert.True(arr2.FirstOrDefault().GetToken().Kind == DTokenKind.Control);
    }

    [Fact]
    public void FromObject()
    {
        var inst = new { name = "my name", age = 33, date = DateTime.Now };

        var sn = System.Text.Json.JsonSerializer.SerializeToNode(inst)!.AsObject();
        var dn = DValue.From(inst).AsObject();

        var sjson = sn.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var djson = dn.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        Assert.Equal(sjson, djson);
        Assert.Equal(sn.Count, dn.Count);


        sn["age"] = "my age";
        dn["age"] = "my age";
        sn.Add("mail", true);
        dn.Add("mail", true);

        sjson = sn.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        djson = dn.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        Assert.Equal(sjson, djson);
        Assert.Equal(sn.Count, dn.Count);

        sn["age"] = 66;
        dn["age"] = 66;
        sn.Remove("name");
        dn.Remove("name");

        sjson = sn.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        djson = dn.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        Assert.Equal(sjson, djson);
        Assert.Equal(sn.Count, dn.AsValue().Count);
    }


    [Fact]
    public void FromArray()
    {
        var inst = new[] { 1, 2, 3 };

        var sn = System.Text.Json.JsonSerializer.SerializeToNode(inst)!.AsArray();
        var dn = DValue.From(inst).AsArray();

        var sjson = sn.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var djson = dn.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        Assert.Equal(sjson, djson);
        Assert.Equal(sn.Count, dn.Count);

        sn[0] = "my name";
        dn[0] = "my name";
        sn.Add(true);
        dn.Add(true);

        sjson = sn.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        djson = dn.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        Assert.Equal(sjson, djson);
        Assert.Equal(sn.Count, dn.AsValue().Count);

        sn.Insert(1, "test");
        dn.Insert(1, "test");
        sn.Remove(3);
        dn.Remove(3);

        sjson = sn.ToJsonString(new JsonSerializerOptions
            { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        djson = dn.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        Assert.Equal(sjson, djson);
        Assert.Equal(sn.Count, dn.AsValue().Count);
    }

    [Fact]
    public void ReplaceObject()
    {
        var root = new DObject();
        root["A"] = DValue.Create(new { name = "my name", date = DateTime.Now });

        Assert.Equal("my name", root["A"]["name"].ToString());

        root["A"] = DValue.Create(new { name = "any", date = DateTime.Now });

        Assert.Equal("any", root["A"]["name"].ToString());

        var obj = root["A"].AsObject();

        Assert.True(obj.IsValid);

        root["A"] = DValue.Create(new { name = "test", date = DateTime.Now });

        Assert.False(obj.IsValid);

        root["A"] = DValue.Create(new[] { 1, 2, 3 });

        Assert.Equal(2, (int)root["A"][1]);
        Assert.Equal(@"{""A"":[1,2,3]}", root.ToJsonString());

        root["A"] = DValue.Create(new[] { 4, 5, 6 });

        Assert.Equal(5, (int)root["A"][1]);
        Assert.Equal(@"{""A"":[4,5,6]}", root.ToJsonString());

        var arr = root["A"].AsArray();

        Assert.True(arr.IsValid);

        root["A"] = true;

        Assert.False(arr.IsValid);

        Assert.True((bool)root["A"]);

        Assert.Equal(@"{""A"":true}", root.ToJsonString());
    }

    [Fact]
    public void DeepClone()
    {
        var json = @"{""a"":123,""b"":[1,2,3],""c"":{""d"":true}}";

        var obj1a = JObject.Parse(json);
        var obj1b = obj1a.DeepClone();
        obj1a!["c"]!["d"] = false;
        obj1b!["a"] = "test";

        var obj2a = DValue.ParseJson(json);
        var obj2b = obj2a.DeepClone();
        obj2a!["c"].AsObject()["d"] = false;
        obj2b!["a"] = "test";

        Assert.Equal(obj1a.ToString(), obj2a.ToString());
        Assert.Equal(obj1b.ToString(), obj2b.ToString());
    }

    [Fact]
    public void JsonPath()
    {
        var json = @"{
              'channel': {
                'title': 'Sample Feed',
                'link': 'https://feed.example.com',
                'item': [1,2,3],
                'list': [{'Key':123},{'Key':[true,false]}]
              }
            }";

        var node1 = DValue.ParseJson5(json);
        var node2 = JsonNode.Parse(node1.ToJsonString());

        {
            var path1 = node1["channel"].GetPath();
            var path2 = node2!["channel"]!.GetPath();

            Assert.Equal(path1, path2);
        }

        {
            var path1 = node1["channel"]["item"][1].GetPath();
            var path2 = node2!["channel"]!["item"]![1]!.GetPath();

            Assert.Equal(path1, path2);
        }

        {
            var path1 = node1["channel"]["list"][1].GetPath();
            var path2 = node2!["channel"]!["list"]![1]!.GetPath();

            Assert.Equal(path1, path2);
        }

        {
            var path1 = node1["channel"]["list"][1]!["Key"].GetPath();
            var path2 = node2!["channel"]!["list"]![1]!["Key"]!.GetPath();

            Assert.Equal(path1, path2);
        }

        {
            var path1 = node1["channel"]["list"][1]!["Key"][1]!.GetPath();
            var path2 = node2!["channel"]!["list"]![1]!["Key"]![1]!.GetPath();

            Assert.Equal(path1, path2);
        }
    }

    [Fact]
    public void LookupLastProperty()
    {
        var obj = new DObject();

        for (var i = 0; i < 100; i++)
        {
            obj.Add("Prop" + i, i);
        }

        var count = 0;
        for (var i = 0; i < 10000; i++)
        {
            count += (int)obj["Prop99"];
        }
    }

    [Fact]
    public void CreateManualy()
    {
        var newtonsoftJson = string.Empty;
        var systemJson = string.Empty;
        var redoxJson = string.Empty;

        {
            var array = new JArray();
            array.Add("Handmade entry");
            array.Add(new DateTime(2021, 8, 4));

            var o = new JObject();
            o["MyArray"] = array;

            newtonsoftJson = o.ToString();
        }

        {
            var array = new JsonArray();
            array.Add("Handmade entry");
            array.Add(new DateTime(2021, 8, 4));

            var o = new JsonObject();
            o["MyArray"] = array;

            systemJson = o.ToJsonString();
        }

        {
            var array = new DArray();
            array.Add("Handmade entry");
            array.Add(new DateTime(2021, 8, 4));

            var o = new DObject();
            o["MyArray"] = array;

            redoxJson = o.ToJsonString();
        }

        using var doc = Json.JsonDocument.Parse(newtonsoftJson, SerializerSettings.Default);

        newtonsoftJson = Json.JsonDocument.EncodeToString(doc.RootElement);

        Assert.Equal(newtonsoftJson, systemJson);
        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void CreateCollectionInitializer()
    {
        var newtonsoftJson = string.Empty;
        var systemJson = string.Empty;
        var redoxJson = string.Empty;

        {
            var o = new JObject
            {
                { "Cpu", "Arm" },
                { "Memory", 64 },
                {
                    "Drives", new JArray
                    {
                        "NVMe",
                        "HDD"
                    }
                }
            };
            newtonsoftJson = o.ToString();
        }

        {
            var o = new JsonObject
            {
                { "Cpu", "Arm" },
                { "Memory", 64 },
                {
                    "Drives", new JsonArray
                    {
                        "NVMe",
                        "HDD"
                    }
                }
            };
            systemJson = o.ToJsonString();
        }

        {
            var o = new DObject
            {
                { "Cpu", "Arm" },
                { "Memory", 64 },
                {
                    "Drives", new DArray
                    {
                        "NVMe",
                        "HDD"
                    }
                }
            };
            redoxJson = o.ToJsonString();
        }

        using var doc = Json.JsonDocument.Parse(newtonsoftJson, SerializerSettings.Default);

        newtonsoftJson = Json.JsonDocument.EncodeToString(doc.RootElement);

        Assert.Equal(newtonsoftJson, systemJson);
        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void CreateFromAnObject()
    {
        var i1 = (JValue)JToken.FromObject(12345);
        var i2 = DValue.Create(12345);

        Assert.Equal(i1.Type.ToString(), i2.GetToken().Kind.ToString());
        Assert.Equal(i1.ToString(), i2.ToString());

        var s1 = (JValue)JToken.FromObject("Plain text");
        var s2 = DValue.Create("Plain text");

        Assert.Equal(s1.Type.ToString(), s2.GetToken().Kind.ToString());
        Assert.Equal(s1.ToString(), s2.ToString());

        var workstation = new Workstation
        {
            Cpu = "Arm",
            Memory = 64,
            Drives = new List<string>
            {
                "NVMe",
                "HDD"
            }
        };

        var o1 = (JObject)JToken.FromObject(workstation);
        var o2 = DValue.Create(workstation);

        using var doc1 = Json.JsonDocument.Parse(o1.ToString(), SerializerSettings.Default);

        Assert.Equal(Json.JsonDocument.EncodeToString(doc1.RootElement),
            o2.ToJsonString());

        var a1 = (JArray)JToken.FromObject(workstation.Drives);
        var a2 = DValue.Create(workstation.Drives);

        using var doc = Json.JsonDocument.Parse(a1.ToString(), SerializerSettings.Default);

        Assert.Equal(Json.JsonDocument.EncodeToString(doc.RootElement),
            a2.ToJsonString());
    }

    [Fact]
    public void CreateFromAnAnonymous()
    {
        var entries = new List<Entry>
        {
            new()
            {
                Title = "Release 1.0",
                Description = "Release 1.0 notes",
                Categories = new List<string>
                {
                    "release",
                    "movie"
                },
                Link = "release-1-0.html"
            }
        };

        var o1 = JObject.FromObject(new
        {
            channel = new
            {
                title = "Sample Feed",
                link = "https://feed.example.com",
                description = "Sample feed description.",
                item =
                    from p in entries
                    orderby p.Title
                    select new
                    {
                        title = p.Title,
                        description = p.Description,
                        link = p.Link,
                        category = p.Categories
                    }
            }
        });

        var o2 = DValue.Create(new
        {
            channel = new
            {
                title = "Sample Feed",
                link = "https://feed.example.com",
                description = "Sample feed description.",
                item =
                    from p in entries
                    orderby p.Title
                    select new
                    {
                        title = p.Title,
                        description = p.Description,
                        link = p.Link,
                        category = p.Categories
                    }
            }
        });

        using var doc = Json.JsonDocument.Parse(o1.ToString(), SerializerSettings.Default);

        var newtonsoftJson = Json.JsonDocument.EncodeToString(doc.RootElement);
        var redoxJson = o2.ToJsonString();

        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void ParseArray()
    {
        var newtonsoftJson = string.Empty;
        var redoxJson = string.Empty;

        var json = @"[
              'Low',
              'Middle',
              'High'
            ]";

        {
            var a = JArray.Parse(json);

            newtonsoftJson = a.ToString();
        }

        {
            var a = DValue.ParseJson5(json);

            redoxJson = a.ToString();
        }

        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void ParseObject()
    {
        var newtonsoftJson = string.Empty;
        var redoxJson = string.Empty;

        var json = @"{
  CPU: 'Arm',
  Drives: [
    'Optical drive',
    '2 terabyte disk'
  ]
}";
        {
            var o = JObject.Parse(json);

            newtonsoftJson = o.ToString();
        }

        {
            var o = DValue.ParseJson5(json);

            redoxJson = o.ToString();
        }

        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void ParseAllToken()
    {
        var t1 = JToken.Parse("{}");
        var r1 = DValue.ParseJson("{}");

        Assert.Equal(t1.Type.ToString(), r1.GetValueKind().ToString());

        var t2 = JToken.Parse("[]");
        var r2 = DValue.ParseJson("[]");

        Assert.Equal(t2.Type.ToString(), r2.GetValueKind().ToString());

        var t3 = JToken.Parse("null");
        var r3 = DValue.ParseJson("null");

        Assert.Equal(t3.Type.ToString(), r3.GetValueKind().ToString());
        // Null

        var t4 = JToken.Parse(@"'Some text!'");
        var r4 = DValue.ParseJson5(@"'Some text!'");

        Assert.Equal(t4.Type.ToString(), r4.GetValueKind().ToString());
        // String
    }

    [Fact]
    public void DeserializingWithLINQ()
    {
        var json = @"[
              {
                'Title': 'REDox release notes',
                'Author': {
                  'Name': 'Taro Yamada',
                  'Twitter': '@tyamada',
                  'Picture': '/avatar.png'
                },
                'Date': '2024-04-01T09:15:00',
                'BodyHtml': '&lt;h3&gt;Title!&lt;/h3&gt;\r\n&lt;p&gt;Content!&lt;/p&gt;'
              }
            ]";

        var newtonsoftJson = string.Empty;
        var redoxJson = string.Empty;

        {
            var articleArray = JArray.Parse(json);

            IList<Article> articles = articleArray.Select(p => new Article
            {
                Title = (string)p["Title"]!,
                AuthorName = (string)p["Author"]!["Name"]!,
                AuthorTwitter = (string)p["Author"]!["Twitter"]!,
                PostedDate = (DateTime)p["Date"]!,
                Body = HttpUtility.HtmlDecode((string)p["BodyHtml"]!)
            }).ToList();

            newtonsoftJson = articles[0].Body!;
        }

        {
            var articleArray = DValue.ParseJson5(json).AsArray();

            IList<Article> articles = articleArray.Select(p => new Article
            {
                Title = (string)p["Title"]!,
                AuthorName = (string)p["Author"]["Name"]!,
                AuthorTwitter = (string)p["Author"]["Twitter"]!,
                PostedDate = (DateTime)p["Date"]!,
                Body = HttpUtility.HtmlDecode((string)p["BodyHtml"]!)
            }).ToList();

            redoxJson = articles[0].Body!;
        }

        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void SerializingWithLINQ()
    {
        IList<Article> articles = new List<Article>
        {
            new()
            {
                Title = "REDox release notes",
                AuthorName = "Taro Yamada",
                AuthorTwitter = "tyamada",
                PostedDate = new DateTime(2024, 4, 1, 9, 15, 0),
                Body = @"<h3>Title!</h3><p>Content!</p>"
            }
        };

        var newtonsoftJson = string.Empty;
        var redoxJson = string.Empty;

        {
            var articleNodes = new JArray(
                articles.Select(p => new JObject
                {
                    { "Title", p.Title },
                    {
                        "Author", new JObject
                        {
                            { "Name", p.AuthorName },
                            { "Twitter", p.AuthorTwitter }
                        }
                    },
                    { "Date", p.PostedDate },
                    { "BodyHtml", HttpUtility.HtmlEncode(p.Body) }
                })
            );

            newtonsoftJson = articleNodes.ToString();
        }

        {
            var articleNodes = new DArray(
                articles.Select(p => new DObject
                {
                    { "Title", p.Title },
                    {
                        "Author", new DObject
                        {
                            { "Name", p.AuthorName },
                            { "Twitter", p.AuthorTwitter }
                        }
                    },
                    { "Date", p.PostedDate },
                    { "BodyHtml", HttpUtility.HtmlEncode(p.Body) }
                })
            );

            redoxJson = articleNodes.ToString()!;
        }

        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void ModifyJson()
    {
        var json = @"{
              'channel': {
                'title': 'Sample Feed',
                'link': 'https://feed.example.com',
                'description': 'Sample feed description.',
                'obsolete': 'Deprecated value',
                'item': []
              }
            }";

        var newtonsoftJson = string.Empty;
        var redoxJson = string.Empty;

        {
            var feed = JObject.Parse(json);

            var channel = (JObject)feed["channel"]!;

            channel["title"] = ((string)channel["title"])!.ToUpper();
            channel["description"] = ((string)channel["description"])!.ToUpper();

            channel.Property("obsolete")!.Remove();

//                channel.Property("description")!.AddAfterSelf(new JProperty("new", "New value"));

            var item = (JArray)channel["item"]!;
            item.Add("First");
            item.Add("Second");

            newtonsoftJson = feed.ToString();
        }

        {
            var feed = DValue.ParseJson5(json);

            var channel = feed["channel"].AsObject();

            channel["title"] = ((string?)channel["title"])?.ToUpper();
            channel["description"] = ((string?)channel["description"])?.ToUpper();

            channel.Remove("obsolete");

//                channel.InsertAfter("description",new KeyValuePair<string,Element>("new", "New value"));

            var item = channel["item"].AsArray();
            item.Add("First");
            item.Add("Second");

            redoxJson = feed.ToString()!;
        }

        Assert.Equal(newtonsoftJson, redoxJson);
    }

    [Fact]
    public void ConvertToCollection()
    {
        var json = @"{
              'd': [
                {
                  'Name': 'Hanako Suzuki'
                },
                {
                  'Name': 'Ichiro Tanaka'
                }
              ]
            }";

        {
            var o = JObject.Parse(json);
            var a = (JArray)o["d"]!;
            var person = a.ToObject<IList<Person>>()!;

            Assert.Equal("Hanako Suzuki", person[0].Name);
            Assert.Equal("Ichiro Tanaka", person[1].Name);
        }

        {
            var o = DValue.ParseJson5(json);
            var a = o["d"];
            var person = a.To<IList<Person>>()!;

            Assert.Equal("Hanako Suzuki", person[0].Name);
            Assert.Equal("Ichiro Tanaka", person[1].Name);
        }
    }

    [Fact]
    public void ValueTest()
    {
        {
            var node = JsonNode.Parse("123");

            var v1 = node?.AsValue();

            Assert.Equal((int?)v1, 123);
        }

        {
            var node = DValue.ParseJson("123");

            var v1 = node;

            Assert.Equal((int?)v1, 123);
        }
    }

    [Fact]
    public void ConvertToValue()
    {
        {
            var v1 = new JValue(true);

            var b = v1.ToObject<bool>();

            Assert.True(b);

            var i = v1.ToObject<int>();

            Assert.Equal(1, i);

            var s = v1.ToObject<string>()!;

            Assert.Equal("True", s);
        }
        {
            var v1 = DValue.Create(true);

            var b = v1.To<bool>();

            Assert.True(b);

            var i = v1.To<int>();

            Assert.Equal(1, i);

            var s = v1.To<string>()!;

            Assert.Equal("True", s);
        }
    }

    [Fact]
    public void ConvertToAType()
    {
        {
            var v1 = new JValue(true);

            var b = (bool)v1.ToObject(typeof(bool))!;

            Assert.True(b);

            var i = (int)v1.ToObject(typeof(int))!;

            Assert.Equal(1, i);

            var s = (string)v1.ToObject(typeof(string))!;

            Assert.Equal("True", s);
        }
        {
            var v1 = DValue.Create(true);

            var b = (bool)v1.ToObject(typeof(bool))!;

            Assert.True(b);

            var i = (int)v1.ToObject(typeof(int))!;

            Assert.Equal(1, i);

            var s = (string)v1.ToObject(typeof(string))!;

            Assert.Equal("True", s);
        }
    }

    [Fact]
    public void DValueScalarCoercionSucceedsWhenAllowed()
    {
        var settings = new DoxSerializerSettings
        {
            AllowRelaxedScalarConversion = true
        };
        var obj = new DObject(settings)
        {
            ["number"] = DValue.Create(123),
            ["boolean"] = DValue.Create(true)
        };

        Assert.Equal("123", (string?)obj["number"]);
        Assert.Equal(1, obj["boolean"].To<int>());
    }

    [Fact]
    public void DValueScalarCoercionThrowsWhenDisallowed()
    {
        var settings = new DoxSerializerSettings
        {
            AllowRelaxedScalarConversion = false
        };
        var obj = new DObject(settings)
        {
            ["number"] = DValue.Create(123),
            ["boolean"] = DValue.Create(true)
        };

        Assert.ThrowsAny<Exception>(() => (string?)obj["number"]);
        Assert.ThrowsAny<Exception>(() => obj["boolean"].To<int>());
    }

    [Fact]
    public void ConditionalSerialize()
    {
        var member = new Staff();
        member.Name = "Staff Member";
        var lead = new Staff();
        lead.Name = "Team Lead";

        member.Manager = lead;

        // 自己参照となる Manager は ShouldSerializeManager により出力対象外になる
        lead.Manager = lead;

        var json1 = JsonConvert.SerializeObject(new[] { member, lead });

        var json2 = Json.JsonSerializer.Serialize(new[] { member, lead }, SerializerSettings.Default);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void ValueEquals()
    {
        var json = @"[1,1,1]";

        var root = JsonNode.Parse(json)!;

        Assert.False(root[0] == root[1]);
        Assert.True((int)root[0]! == (int)root[1]!);

        var root2 = (JArray)JToken.Parse(json)!;

        Assert.False(root2[0] == root2[1]);
        Assert.True((int)root2[0]! == (int)root2[1]!);
        Assert.False((JValue)root2[0]! == (JValue)root2[1]!);

        var root3 = DValue.ParseJson(json);

        //Assert.False(root3[0] == root3[1]);
        Assert.True((int)root3[0] == (int)root3[1]);
    }

    [Fact]
    public void SameKey()
    {
        var json = @"{""a"":123,""a"":true,""b"":678}";

        var obj0 = System.Text.Json.JsonDocument.Parse(json).RootElement;

        var obj1 = JsonNode.Parse(json);

        var obj2 = DValue.ParseJson(json);

        Assert.Equal(obj1!.ToJsonString(), obj2.ToJsonString());

//            Assert.Equal(obj2["a"].ToString(), obj0.GetProperty("a").ToString());
        Assert.Equal(obj2["b"].ToString(), obj0.GetProperty("b").ToString());

        var obj3 = obj2.DeepClone();

        Assert.Equal(obj2.ToString(), obj3.ToString());
    }

    [Fact]
    public void CastingValue()
    {
        {
            var v1 = new JValue("1");
            var i = (int)v1;

            Assert.Equal(1, i);

            var v2 = new JValue(true);
            var b = (bool)v2;

            Assert.True(b);

            var v3 = new JValue("42.75");
            var d = (decimal)v3;

            Assert.Equal(42.75m, d);

            var v4 = new JValue(new DateTime(2022, 3, 15));
            var s = (string)v4!;

            Assert.Equal("03/15/2022 00:00:00", s);

            var v5 = new JValue("http://www.example.org");
            var u = (Uri)v5!;

            Assert.Equal("http://www.example.org/", u.ToString());

            var v6 = JValue.CreateNull();
            u = (Uri?)v6;

            Assert.Null(u);

            Assert.Equal("{null}", u != null ? u.ToString() : "{null}");

            var dt = (DateTime?)v6;

            Assert.Equal("{null}", dt != null ? dt.ToString() : "{null}");

            var ui1 = new JValue(ulong.MaxValue);
            var ui = (ulong)ui1;

            Assert.Equal(ulong.MaxValue, ui);
        }
        {
            var v1 = DValue.Create("1");
            var i = (int)v1;

            Assert.Equal(1, i);

            var v2 = DValue.Create(true);
            var b = (bool)v2;

            Assert.True(b);

            var v3 = DValue.Create("42.75");
            var d = (decimal)v3;

            Assert.Equal(42.75m, d);

            var v4 = DValue.Create(new DateTime(2022, 3, 15));
            var s = (string)v4!;

            Assert.Equal("03/15/2022 00:00:00", s);

            var v5 = DValue.Create("http://www.example.org");
            var u = v5.To<Uri?>();

            Assert.Equal("http://www.example.org/", u?.ToString());

            var v6 = DValue.Null;
            u = v6.To<Uri?>();

            Assert.Null(u);

            Assert.Equal("{null}", u != null ? u.ToString() : "{null}");

            var dt = (DateTime?)v6;

            Assert.Equal("{null}", dt != null ? dt.ToString() : "{null}");

            var ui1 = DValue.Create(ulong.MaxValue);
            var ui = (ulong)ui1;

            Assert.Equal(ulong.MaxValue, ui);
        }
    }

    public class Workstation
    {
        public string? Cpu { get; set; }
        public int Memory { get; set; }
        public IList<string>? Drives { get; set; }
    }

    public class Entry
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Link { get; set; }
        public IList<string>? Categories { get; set; }
    }

    public class Article
    {
        public string? Title { get; set; }
        public string? AuthorName { get; set; }
        public string? AuthorTwitter { get; set; }
        public string? Body { get; set; }
        public DateTime PostedDate { get; set; }
    }


    private record Person
    {
        public Person(string? name, int age, bool isMale)
        {
            Name = name;
            Age = age;
            IsMale = isMale;
        }

        public string? Name { get; set; }

        public int Age { get; set; }

        public bool IsMale { get; set; }

        public override string ToString()
        {
            return $"Person(Name={Name}, Age={Age}, IsMale={IsMale})";
        }
    }

    public class Staff
    {
        public string? Name { get; set; }
        public Staff? Manager { get; set; }

        public bool ShouldSerializeManager()
        {
            // 自分自身を上司とする場合は Manager を出力しない
            return Manager != this;
        }
    }
}