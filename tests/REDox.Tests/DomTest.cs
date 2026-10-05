using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using REDox.Json;
using REDox.Serialization;
using DoxJsonDocument = REDox.Json.JsonDocument;

namespace REDox.Tests;

public sealed class DomTest
{
    [Fact]
    public void ReplaceLoopInlineLiteralAndExtend()
    {
        var arr = new DArray { 1, 2, 3 };

        for (var i = 0; i < 1000; i++)
        {
            switch (i & 3)
            {
                case 0:
                    arr[0] = true;
                    break;
                case 1:
                    arr[0] = "ABC";
                    break;
                case 2:
                    arr[0] = 123;
                    break;
                case 3:
                    arr[0] = DateTime.Now;
                    break;
            }
        }

        var reader = new DataReader(arr);

        Assert.Equal(2, reader.Document.GetExtends().Length);
        Assert.Equal(6, reader.Document.GetTokens().Length);
    }

    [Fact]
    public void ReplaceLoopInlineLiteralAndExtend2()
    {
        var root = DValue.ParseJson("true");

        for (var i = 0; i < 1000; i++)
        {
            switch (i & 3)
            {
                case 0:
                    root.ReplaceWith(new DObject());
                    break;
                case 1:
                    root.ReplaceWith(true);
                    break;
                case 2:
                    root.ReplaceWith(new DArray());
                    break;
                case 3:
                    root.ReplaceWith(123);
                    break;
            }
        }

        var reader = new DataReader(root);

        Assert.Equal(1, reader.Document.GetExtends().Length);
        Assert.Equal(3, reader.Document.GetTokens().Length);
    }

    [Fact]
    public void ObjectView_IsInvalidated_WhenConvertedToMap()
    {
        var obj = new DObject();
        obj.Add("a", 1);

        var map = obj.AsMap();

        Assert.False(obj.IsValid);
        Assert.True(map.IsValid);

        Assert.Throws<ObjectDisposedException>(() => obj.Add("b", 2));
        Assert.Throws<ObjectDisposedException>(() => obj.ContainsKey("a"));

        // Existing data must survive the view conversion.
        Assert.Equal(1, map.Count);
        Assert.Equal(1, map[0].Value.AsElement().GetInt32());
    }

    [Fact]
    public void MapView_IsInvalidated_WhenConvertedBackToObject()
    {
        var obj = new DObject();
        obj.Add("a", 1);

        var map = obj.AsMap();
        var current = map.AsObject();

        Assert.False(obj.IsValid);
        Assert.False(map.IsValid);
        Assert.True(current.IsValid);

        // Existing data must survive both conversions.
        Assert.True(current.ContainsKey("a"));
        Assert.Equal(1, current["a"].AsElement().GetInt32());

        Assert.Throws<ObjectDisposedException>(() => map.Add(1, 2));
    }

    [Fact]
    public void ValueHandle_FromInvalidatedView_IsAlsoInvalid()
    {
        var obj = new DObject();
        obj.Add("a", 1);

        var value = obj["a"];

        Assert.True(value.IsValid);

        var map = obj.AsMap();

        Assert.False(obj.IsValid);
        Assert.False(value.IsValid);
        Assert.True(map.IsValid);
    }

    [Fact]
    public void ObjectView_MustNotSilentlyDiverge_AfterConvertingToMap()
    {
        var obj = new DObject();
        obj.Add("a", 1);

        // DMap is created by copying DObject's KeyValuePairs.
        // Document._extends now points to the DMap, not this DObject.
        var map = obj.AsMap();

        // Current implementation reports the old DObject as valid because
        // its DElement version is still current.
        Assert.False(obj.IsValid);

        Assert.Throws<ObjectDisposedException>(() => { obj.Add("b", 2); });
        Assert.Throws<ObjectDisposedException>(() => { obj.ContainsKey("b"); });

        // Obtain the view that is currently reachable through Document.
        //
        // If DObject and DMap are coherent views of the same logical
        // container, "b" must also be visible here.
        var current = map.AsObject();

        // Fails on the current implementation:
        // current was reconstructed from map's independent _values array,
        // which never received "b".
        Assert.False(current.ContainsKey("b"));
    }

    [Fact]
    public void DValue_TracksContainerSlot_WhenTokenSlotIsReused()
    {
        var array = new DArray(1, 2);

        var slot = array[1];

        Assert.True(slot.IsValid);
        Assert.Equal(2, slot.AsElement().GetInt32());

        array.RemoveAt(1);

        Assert.False(slot.IsValid);

        array.Add(999);

        // DValue represents the slot, not the identity of the old value.
        Assert.True(slot.IsValid);
        Assert.Equal(999, slot.AsElement().GetInt32());
    }

    [Fact]
    public void DProperty_EqualityUsesDocumentNameValueAndVersion()
    {
        var obj = new DObject { { "a", 1 }, { "b", 1 } };

        var property = obj.GetPropertyEntry("a");
        var sameProperty = obj.GetPropertyEntry("a");
        var differentProperty = obj.GetPropertyEntry("b");

        Assert.True(property.Equals(sameProperty));
        Assert.True(property.Equals((object)sameProperty));
        Assert.Equal(property.GetHashCode(), sameProperty.GetHashCode());
        Assert.False(property.Equals(differentProperty));
        Assert.False(property.Equals(null));
    }

    [Fact]
    public void DProperty_EqualityDoesNotThrowAfterDocumentInvalidation()
    {
        var doc = DoxJsonDocument.Parse("{\"a\":1}");
        var property = doc.RootElement.AsObject().GetPropertyEntry("a");
        var sameProperty = property;

        doc.Dispose();

        Assert.True(property.Equals(sameProperty));
        Assert.Equal(property.GetHashCode(), sameProperty.GetHashCode());
    }

    [Fact]
    public void DElement_InvalidOperationExceptionsIncludeMessages()
    {
        using var doc = DoxJsonDocument.Parse(
            "{\"text\":\"x\",\"number\":1,\"array\":[],\"object\":{},\"boolean\":true}");

        var root = doc.RootElement;

        AssertInvalidOperation(
            "The element does not contain a Boolean value.",
            () => root.GetProperty("number").GetBoolean());
        AssertInvalidOperation(
            "The element does not contain a numeric value.",
            () => root.GetProperty("text").TryGetInt32(out _));
        AssertInvalidOperation(
            "The element does not contain a string or null value.",
            () => root.GetProperty("number").GetString());
        AssertInvalidOperation(
            "The element does not contain a timestamp or text value.",
            () => root.GetProperty("boolean").TryGetDateTime(out _));
        AssertInvalidOperation(
            "The element does not contain a text or byte string value.",
            () => root.GetProperty("number").TryGetGuid(out _));
        AssertInvalidOperation(
            "The element is not a container.",
            () => root.GetProperty("number").GetValueCount());
        AssertInvalidOperation(
            "The element is not an array.",
            () => root.GetProperty("object").GetArrayLength());
        AssertInvalidOperation(
            "The element is not a map.",
            () => root.GetProperty("array").GetPropertyCount());
    }

    private static void AssertInvalidOperation(string expectedMessage, Action action)
    {
        var exception = Assert.Throws<InvalidOperationException>(action);

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void DMap_IListOperationsTreatMapAsKeyValuePairSequence()
    {
        var map = new DMap();
        IList<KeyValuePair<DValue, DValue>> list = map;
        var inserted = new KeyValuePair<DValue, DValue>(DValue.Create("b"), DValue.Create(2));

        map.Add("a", 1);
        map.Add("c", 3);
        list.Insert(1, inserted);

        Assert.Equal(@"{""a"":1,""b"":2,""c"":3}", map.ToJsonString());
        Assert.Equal("b", (string?)list[1].Key);
        Assert.Equal(2, (int)list[1].Value);
        Assert.Equal(1, list.IndexOf(list[1]));
        Assert.True(list.Contains(list[1]));

        list[1] = new KeyValuePair<DValue, DValue>(DValue.Create("updated"), DValue.Create(true));

        Assert.Equal(@"{""a"":1,""updated"":true,""c"":3}", map.ToJsonString());
        Assert.True(list.Remove(list[1]));
        Assert.Equal(@"{""a"":1,""c"":3}", map.ToJsonString());
    }

    [Fact]
    public void RandomOperations_MatchDictionary()
    {
        var random = new Random(12345);
        var actual = new DObject();
        var expected = new Dictionary<string, int>(
            StringComparer.Ordinal);

        var keys = Enumerable.Range(0, 100)
            .Select(i => $"key-{i}")
            .ToArray();

        for (var operation = 0; operation < 100_000; operation++)
        {
            var key = keys[random.Next(keys.Length)];

            switch (random.Next(4))
            {
                case 0:
                    {
                        var value = random.Next();

                        var expectedResult =
                            expected.TryAdd(key, value);

                        var actualResult =
                            actual.TryAdd(key, value);

                        Assert.Equal(expectedResult, actualResult);
                        break;
                    }

                case 1:
                    Assert.Equal(
                        expected.Remove(key),
                        actual.Remove(key));
                    break;

                case 2:
                    Assert.Equal(
                        expected.ContainsKey(key),
                        actual.ContainsKey(key));
                    break;

                case 3:
                    expected.Clear();
                    actual.Clear();
                    break;
            }

            Assert.Equal(expected.Count, actual.Count);

            foreach (var expectedKey in expected.Keys)
            {
                Assert.True(actual.ContainsKey(expectedKey));
            }
        }
    }

    [Fact]
    public void NullKey_ThrowsArgumentNullException()
    {
        var obj = new DObject();

        Assert.Throws<ArgumentNullException>(() => obj.ContainsKey(null!));

        Assert.Throws<ArgumentNullException>(() => obj.Remove(null!));

        Assert.Throws<ArgumentNullException>(() => obj.Add(null!, 1));
    }

    [Fact]
    public void ClearThenReuse_WorksRepeatedly()
    {
        var obj = new DObject();

        for (var iteration = 0; iteration < 100; iteration++)
        {
            for (var i = 0; i < 20; i++)
            {
                obj.Add($"key-{i}", i);
            }

            Assert.True(obj.ContainsKey("key-10"));

            obj.Clear();

            Assert.Equal(0, obj.Count);
            Assert.False(obj.ContainsKey("key-10"));
        }
    }

    [Fact]
    public void RemoveThenAdd_MaintainsCorrectIndex()
    {
        var obj = new DObject();

        for (var i = 0; i < 16; i++)
        {
            obj.Add($"old-{i}", i);
        }

        obj.Remove("old-7");
        obj.Add("new-key", 100);

        Assert.False(obj.ContainsKey("old-7"));
        Assert.True(obj.ContainsKey("new-key"));

        for (var i = 0; i < 16; i++)
        {
            if (i != 7)
            {
                Assert.True(obj.ContainsKey($"old-{i}"));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public void Remove_ThenLookup_RebuildsCorrectIndex(int removeIndex)
    {
        var obj = new DObject();

        for (var i = 0; i < 10; i++)
        {
            obj.Add($"key-{i}", i);
        }

        Assert.True(obj.Remove($"key-{removeIndex}"));

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(
                i != removeIndex,
                obj.ContainsKey($"key-{i}"));
        }
    }

    [Fact]
    public void HashCollision_DifferentKeysRemainSearchable()
    {
        var (key1, key2) = FindHashCollision();

        var obj = new DObject();
        obj.Add(key1, 1);
        obj.Add(key2, 2);

        Assert.True(obj.ContainsKey(key1));
        Assert.True(obj.ContainsKey(key2));

        Assert.True(obj.Remove(key1));
        Assert.False(obj.ContainsKey(key1));
        Assert.True(obj.ContainsKey(key2));
    }

    private static (string First, string Second) FindHashCollision()
    {
        var hashes = new Dictionary<int, string>();

        for (var i = 0; i < 1_000_000; i++)
        {
            var text = $"collision-key-{i}";
            var hash = text.GetHashCode();

            if (hashes.TryGetValue(hash, out var previous) &&
                previous != text)
            {
                return (previous, text);
            }

            hashes[hash] = text;
        }

        throw new InvalidOperationException(
            "この実行では衝突を検出できませんでした。");
    }

    [Fact]
    public void InlineLiteralTest()
    {
        var json5 = "[0xff]";

        var value = DValue.ParseJson5(json5);

        var result = Json5Document.EncodeToString(value);

        Assert.Equal(json5, result);

        value.AsArray()[0] = 128;

        var result2 = Json5Document.EncodeToString(value);

        Assert.Equal("[0x80]", result2);
    }

    [Fact]
    public void TokenToString()
    {
        var obj = DValue.From(new
        {
            Name = "Alice",
            Age = 30,
            Value = 0.25f
        }).AsObject();

        obj.Add("IsActive", true);
        obj.Add("Symbol", DValue.Create(long.MaxValue));

        foreach (var token in obj.Document.GetTokens())
        {
            TestContext.Current.TestOutputHelper?.WriteLine(token.ToString());
        }
    }

    [Fact]
    public void MaterializedContainerView_RemainsValid_WhenUnrelatedSiblingIsRemoved()
    {
        var root = DValue.ParseJson("""{"keep":{"x":1},"drop":[1,2,3]}""").AsObject();

        var keep = root["keep"].AsObject();
        var drop = root["drop"].AsArray();

        Assert.True(keep.IsValid);
        Assert.True(drop.IsValid);

        Assert.True(root.Remove("drop"));

        // Removed container must be invalid.
        Assert.False(drop.IsValid);

        // Unrelated materialized container should recover through ValidateContainer.
        Assert.True(keep.IsValid);

        keep["x"] = 2;
        keep["y"] = true;

        Assert.Equal("""{"keep":{"x":2,"y":true}}""", root.ToJsonString());
    }

    [Fact]
    public void RemovedArrayElement_StaysInvalid_AfterTokenReuse()
    {
        var arr = DValue.ParseJson("""[{"old":1},2,3]""").AsArray();

        var removedValue = arr[0];
        var removedObject = removedValue.AsObject();

        Assert.True(removedValue.IsValid);
        Assert.True(removedObject.IsValid);

        arr.RemoveAt(0);

        Assert.False(removedValue.IsValid);
        Assert.False(removedObject.IsValid);

        arr.Insert(0, new DObject { { "new", true } });

        Assert.Equal("""[{"new":true},2,3]""", arr.ToJsonString());

        // Even if token slots are reused internally, old handles must not become valid again.
//        Assert.False(removedValue.IsValid);
        Assert.False(removedObject.IsValid);
    }

    [Fact]
    public void ParentTable_BuiltBeforeArrayRemove_UpdatesPathAfterMutation()
    {
        var dox = DValue.ParseJson("""{"a":[{"x":1},{"x":2}],"z":0}""").AsObject();
        var stj = JsonNode.Parse("""{"a":[{"x":1},{"x":2}],"z":0}""")!.AsObject();

        // Force parent table construction before mutation.
        Assert.Equal(stj["a"]![1]!["x"]!.GetPath(), dox["a"][1]["x"].GetPath());

        dox["a"].AsArray().RemoveAt(0);
        stj["a"]!.AsArray().RemoveAt(0);

        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());

        // This catches stale parent table bugs.
        Assert.Equal(stj["a"]![0]!["x"]!.GetPath(), dox["a"][0]["x"].GetPath());
    }

    [Fact]
    public void ParentTable_BuiltBeforeAdd_RegistersNewChildPath()
    {
        var dox = DValue.ParseJson("""{"a":1}""").AsObject();
        var stj = JsonNode.Parse("""{"a":1}""")!.AsObject();

        // Force parent table construction.
        Assert.Equal(stj["a"]!.GetPath(), dox["a"].GetPath());

        dox["b"] = new DArray { 10, 20 };
        stj["b"] = new JsonArray(10, 20);

        Assert.Equal(stj.ToJsonString(), dox.ToJsonString());
        Assert.Equal(stj["b"]![1]!.GetPath(), dox["b"][1].GetPath());
    }

    [Fact]
    public void Parent_MatchesJsonNodeParent_ForObjectPropertiesAndArrayItems()
    {
        var json = """{"object":{"value":1,"array":[true,{"leaf":2}]},"scalar":3}""";
        var stj = JsonNode.Parse(json)!.AsObject();
        var dox = DValue.ParseJson(json).AsObject();

        Assert.Null(stj.Parent);
        Assert.Null(dox.Parent);

        AssertParentMatches(stj["object"], dox["object"]);
        AssertParentMatches(stj["scalar"], dox["scalar"]);
        AssertParentMatches(stj["object"]!["value"], dox["object"]["value"]);
        AssertParentMatches(stj["object"]!["array"], dox["object"]["array"]);
        AssertParentMatches(stj["object"]!["array"]![0], dox["object"]["array"][0]);
        AssertParentMatches(stj["object"]!["array"]![1], dox["object"]["array"][1]);
        AssertParentMatches(stj["object"]!["array"]![1]!["leaf"], dox["object"]["array"][1]["leaf"]);
    }

    [Fact]
    public void ContainerParent_MatchesValueParent()
    {
        var json = """{"object":{"array":[{"leaf":1}]}}""";
        var stj = JsonNode.Parse(json)!.AsObject();
        var dox = DValue.ParseJson(json).AsObject();

        var objectValue = dox["object"];
        var objectContainer = objectValue.AsObject();
        var arrayValue = objectValue["array"];
        var arrayContainer = arrayValue.AsArray();
        var nestedObjectValue = arrayValue[0];
        var nestedObjectContainer = nestedObjectValue.AsObject();

        Assert.Equal(objectValue.Parent!.Value.ToJsonString(), objectContainer.Parent!.Value.ToJsonString());
        Assert.Equal(arrayValue.Parent!.Value.ToJsonString(), arrayContainer.Parent!.Value.ToJsonString());
        Assert.Equal(nestedObjectValue.Parent!.Value.ToJsonString(),
            nestedObjectContainer.Parent!.Value.ToJsonString());

        AssertParentMatches(stj["object"], objectContainer);
        AssertParentMatches(stj["object"]!["array"], arrayContainer);
        AssertParentMatches(stj["object"]!["array"]![0], nestedObjectContainer);
    }

    private static void AssertParentMatches(JsonNode? stjNode, DValue doxValue)
    {
        Assert.NotNull(stjNode);
        Assert.Equal(stjNode!.Parent?.ToJsonString(), doxValue.Parent?.ToJsonString());
    }

    private static void AssertParentMatches(JsonNode? stjNode, DContainer doxContainer)
    {
        Assert.NotNull(stjNode);
        Assert.Equal(stjNode!.Parent?.ToJsonString(), doxContainer.Parent?.ToJsonString());
    }

    [Fact]
    public void SequenceContainer_ReplacedWithExtendContainer_StillEnumeratesCorrectly()
    {
        using var doc = DoxJsonDocument.Parse(
            """{"a":[1,{"b":[2,3]},4],"c":{"d":5}}""",
            SerializerSettings.Default);

        var root = doc.RootElement.AsObject();

        var nested = root["a"][1]["b"].AsArray();

        nested[0] = new DObject { { "x", true } };
        nested.RemoveAt(1);
        nested.Add(9);

        root["c"] = false;

        Assert.Equal(
            """{"a":[1,{"b":[{"x":true},9]},4],"c":false}""",
            root.ToJsonString());

        // Enumerate after compact-token + extend-container mixed mutation.
        var a = root["a"].AsArray();

        Assert.Equal(3, a.Count);
        Assert.Equal(1, (int)a[0]);
        Assert.Equal("""{"b":[{"x":true},9]}""", a[1].ToJsonString());
        Assert.Equal(4, (int)a[2]);
    }

    [Fact]
    public void ObjectDictionaryCache_RemoveAndReAddSameKey_ShouldResolveNewEntry()
    {
        var obj = new DObject
        {
            { "a", 1 },
            { "b", 2 },
            { "c", 3 }
        };

        // Force dictionary/cache construction.
        Assert.True(obj.ContainsKey("a"));
        Assert.Equal(1, (int)obj["a"]);

        Assert.True(obj.Remove("a"));

        obj["a"] = 100;

        Assert.Equal(100, (int)obj["a"]);
        Assert.Equal("""{"b":2,"c":3,"a":100}""", obj.ToJsonString());
    }

    [Fact]
    public void ObjectDictionaryCache_RemoveMiddleThenLookupTail_ShouldNotReturnOldIndex()
    {
        var obj = new DObject
        {
            { "a", 1 },
            { "b", 2 },
            { "c", 3 },
            { "d", 4 }
        };

        // Force dictionary/cache construction.
        Assert.Equal(4, (int)obj["d"]);

        Assert.True(obj.Remove("b"));

        Assert.Equal(3, (int)obj["c"]);
        Assert.Equal(4, (int)obj["d"]);
        Assert.False(obj.ContainsKey("b"));

        obj["e"] = 5;

        Assert.Equal("""{"a":1,"c":3,"d":4,"e":5}""", obj.ToJsonString());
    }

    [Fact]
    public void ObjectDictionaryCache_LargeObjectRemoveAndReAdd_ShouldResolveCurrentIndex()
    {
        var obj = new DObject();

        for (var i = 0; i < 12; i++)
        {
            obj.Add($"k{i}", i);
        }

        // Force dictionary/cache construction.
        Assert.Equal(10, (int)obj["k10"]);

        Assert.True(obj.Remove("k5"));
        Assert.Equal(10, (int)obj["k10"]);
        Assert.False(obj.ContainsKey("k5"));

        obj["k5"] = 500;

        Assert.Equal(500, (int)obj["k5"]);
        Assert.Equal(12, obj.Count);
    }

    [Fact]
    public void ObjectDictionaryCache_LargeObjectWithDuplicateKeys_ShouldUseLastEntry()
    {
        var obj = DValue.ParseJson(
                """{"k0":0,"dup":1,"k1":1,"k2":2,"k3":3,"k4":4,"k5":5,"k6":6,"k7":7,"dup":2}""")
            .AsObject();

        Assert.Equal(2, (int)obj["dup"]);
        Assert.Equal(2, (int)obj["dup"]);

        Assert.True(obj.Remove("dup"));

        Assert.Equal(1, (int)obj["dup"]);
    }

    [Fact]
    public void ObjectKeyLookup_CaseSensitiveByDefault()
    {
        var obj = new DObject { { "Name", 1 } };

        Assert.True(obj.ContainsKey("Name"));
        Assert.False(obj.ContainsKey("name"));
        Assert.False(obj.ContainsKey("NAME"));
        Assert.Throws<KeyNotFoundException>(() => obj["name"]);

        obj.Add("name", 2);

        Assert.Equal(1, (int)obj["Name"]);
        Assert.Equal(2, (int)obj["name"]);
        Assert.Equal(2, obj.Count);
    }

    [Fact]
    public void ObjectKeyLookup_PropertyNameCaseInsensitive_IgnoresCase()
    {
        var settings = new DoxSerializerSettings { PropertyNameCaseInsensitive = true };
        var obj = new DObject(settings) { { "Name", 1 } };

        Assert.True(obj.ContainsKey("Name"));
        Assert.True(obj.ContainsKey("name"));
        Assert.True(obj.ContainsKey("NAME"));
        Assert.Equal(1, (int)obj["nAmE"]);
        Assert.True(((IDictionary<string, DValue>)obj).TryGetValue("NAME", out var value));
        Assert.Equal(1, (int)value);

        obj["NAME"] = 10;

        Assert.Equal(1, obj.Count);
        Assert.Equal(10, (int)obj["Name"]);

        Assert.Throws<ArgumentException>(() => obj.Add("name", 2));

        Assert.True(obj.Remove("nAME"));
        Assert.False(obj.ContainsKey("Name"));
        Assert.Equal(0, obj.Count);
    }

    [Fact]
    public void ObjectKeyLookup_PropertyNameCaseInsensitive_LargeObject()
    {
        var settings = new DoxSerializerSettings { PropertyNameCaseInsensitive = true };
        var obj = new DObject(settings);

        for (var i = 0; i < 20; i++)
        {
            obj.Add($"Key{i}", i);
        }

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(i, (int)obj[$"key{i}"]);
            Assert.Equal(i, (int)obj[$"KEY{i}"]);
        }

        Assert.True(obj.Remove("KEY5"));
        Assert.False(obj.ContainsKey("key5"));

        obj.Add("kEy5", 500);

        Assert.Equal(500, (int)obj["Key5"]);
        Assert.Equal(20, obj.Count);
    }

    [Fact]
    public void ObjectDictionary_CopyTo_WithOffset_CopiesEntriesInOrder()
    {
        ICollection<KeyValuePair<string, DValue>> obj = new DObject
        {
            { "a", 1 },
            { "b", true },
            { "c", "text" }
        };

        var values = new KeyValuePair<string, DValue>[5];

        obj.CopyTo(values, 1);

        Assert.Equal(default, values[0]);
        Assert.Equal("a", values[1].Key);
        Assert.Equal(1, (int)values[1].Value);
        Assert.Equal("b", values[2].Key);
        Assert.True((bool)values[2].Value);
        Assert.Equal("c", values[3].Key);
        Assert.Equal("text", (string?)values[3].Value);
        Assert.Equal(default, values[4]);
    }

    [Fact]
    public void ObjectDictionary_CopyTo_ValidatesArguments()
    {
        ICollection<KeyValuePair<string, DValue>> obj = new DObject
        {
            { "a", 1 },
            { "b", 2 }
        };

        Assert.Throws<ArgumentNullException>(() => obj.CopyTo(null!, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => obj.CopyTo(new KeyValuePair<string, DValue>[2], -1));

        Assert.Throws<ArgumentException>(() => obj.CopyTo(new KeyValuePair<string, DValue>[2], 1));
    }

    [Fact]
    public void ObjectDictionary_NullKeyApis_ThrowArgumentNullException()
    {
        var obj = new DObject { { "a", 1 } };
        var dictionary = (IDictionary<string, DValue>)obj;
        var collection = (ICollection<KeyValuePair<string, DValue>>)obj;

        Assert.Throws<ArgumentNullException>(() => dictionary.TryGetValue(null!, out _));

        Assert.Throws<ArgumentNullException>(() => _ = dictionary[null!]);

        Assert.Throws<ArgumentNullException>(() => dictionary[null!] = 1);

        Assert.Throws<ArgumentNullException>(() => obj.TryAdd(null!, 1));

        Assert.Throws<ArgumentNullException>(() => collection.Contains(new KeyValuePair<string, DValue>(null!, 1)));

        Assert.Throws<ArgumentNullException>(() => collection.Remove(new KeyValuePair<string, DValue>(null!, 1)));
    }

    [Fact]
    public void ArrayRemoveRange_ThenAppendNestedContainers_ShouldNotLeakFreedTokens()
    {
        var arr = DValue.ParseJson("""[0,{"a":1},[2,3],4,5]""").AsArray();

        var removedObject = arr[1].AsObject();
        var removedArray = arr[2].AsArray();

        arr.RemoveRange(1, 2);

        Assert.False(removedObject.IsValid);
        Assert.False(removedArray.IsValid);

        arr.Add(new DObject { { "newObject", true } });
        arr.Add(new DArray { "x", "y" });

        Assert.Equal("""[0,4,5,{"newObject":true},["x","y"]]""", arr.ToJsonString());
    }

    [Fact]
    public void ClearAndRefillArray_Repeatedly_ShouldKeepLogicalContentStable()
    {
        var arr = new DArray();

        for (var i = 0; i < 256; i++)
        {
            arr.Clear();

            arr.Add(i);
            arr.Add(new DObject { { "i", i }, { "even", i % 2 == 0 } });
            arr.Add(new DArray { i, i + 1 });

            Assert.Equal(i, (int)arr[0]);
            Assert.Equal(i, (int)arr[1]["i"]);
            Assert.Equal(i + 1, (int)arr[2][1]);
        }

        Assert.Equal("""[255,{"i":255,"even":false},[255,256]]""", arr.ToJsonString());
    }

    [Fact]
    public void UInt64MaxValue_RoundTrips_AsValueArrayAndObjectProperty()
    {
        const ulong value = ulong.MaxValue;

        var scalar = DValue.Create(value);
        Assert.Equal(value, (ulong)scalar);
        Assert.Equal("18446744073709551615", scalar.ToJsonString());

        var arr = new DArray { value };
        Assert.Equal(value, (ulong)arr[0]);
        Assert.Equal("[18446744073709551615]", arr.ToJsonString());

        var obj = new DObject { { "u", value } };
        Assert.Equal(value, (ulong)obj["u"]);
        Assert.Equal("""{"u":18446744073709551615}""", obj.ToJsonString());
    }

    [Fact]
    public void InlineAsciiBoundary_AndNonAsciiString_RoundTrip()
    {
        var arr = new DArray
        {
            "",
            "a",
            "abcdef",
            "abcdefg",
            "あ",
            "😀"
        };

        var ntj = new JArray("", "a", "abcdef", "abcdefg", "あ", "😀");

        Assert.Equal(ntj.ToString(), arr.ToString());

        for (var i = 0; i < arr.Count; i++)
        {
            Assert.Equal((string?)ntj[i], (string?)arr[i]);
        }
    }

    [Fact]
    public void ReplaceRoot_BackAndForth_BetweenScalarArrayAndObject()
    {
        var root = DValue.ParseJson("""{"a":1}""");

        root = root.ReplaceWith(true);
        Assert.Equal("true", root.ToJsonString());
        Assert.True((bool)root);

        root = root.ReplaceWith(new DArray { 1, 2, 3 });
        Assert.Equal("[1,2,3]", root.ToJsonString());
        Assert.Equal(2, (int)root[1]);

        root = root.ReplaceWith(new DObject { { "x", 10 }, { "y", false } });
        Assert.Equal("""{"x":10,"y":false}""", root.ToJsonString());
        Assert.Equal(10, (int)root["x"]);
        Assert.False((bool)root["y"]);
    }

    [Fact]
    public void Json5TriviaPreserve_AfterReplacingValue_ShouldKeepSurroundingTriviaParsable()
    {
        var json5 = """
                    {
                        // leading comment for a
                        a: 1,
                        // leading comment for b
                        b: [2, 3],
                    }
                    """;

        using var doc = Json5Document.Parse(
            json5,
            SerializerSettings.Default,
            new Json5DocumentOptions { PreserveTrivia = true });

        DomTestHelper.DumpTokens(TestContext.Current.TestOutputHelper!, doc);

        var root = doc.RootElement.AsObject();

        root["a"] = 100;
        root["b"][0].ReplaceWith(new DObject { { "x", true } });

        var json5Out = Json5Document.EncodeToString(
            doc.RootElement,
            new Json5WriteOptions { PreserveTrivia = true });

        TestContext.Current.TestOutputHelper?.WriteLine(json5Out);

        DomTestHelper.DumpTokens(TestContext.Current.TestOutputHelper!, doc);

        using var reparsed = Json5Document.Parse(
            json5Out,
            SerializerSettings.Default,
            new Json5DocumentOptions { PreserveTrivia = true });

        Assert.Equal(
            """{"a":100,"b":[{"x":true},3]}""",
            JsonDocument.EncodeToString(reparsed.RootElement));
    }
}