// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using REDox.Json;

namespace REDox.Tests;

public sealed class DValueValidityRegressionTests
{
    private static readonly string[] s_invalidSources =
    [
        "default", "disposed", "documentVersion", "reusedToken", "shiftedArray",
        "shiftedObject", "shiftedMap", "replacedView", "clearedArray"
    ];

    public static TheoryData<string, string> InvalidConversions()
    {
        var cases = new TheoryData<string, string>();
        foreach (var source in s_invalidSources)
        foreach (var operation in new[]
                 {
                     "element", "implicitElement", "container", "array", "object", "map",
                     "arrayIndexer", "objectIndexer", "root", "parent", "clone", "deserialize",
                     "deserializeObject", "trivia", "integerCopy", "nullableIntegerCopy",
                     "boolCast", "doubleCast", "stringCast", "decimalCast", "floatCast"
                 })
        {
            cases.Add(source, operation);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidConversions))]
    public void InvalidValueCannotProduceValidHandle(string source, string operation)
    {
        var value = CreateInvalidValue(source);

        Assert.Throws<ObjectDisposedException>(() => ConvertValue(value, operation));
        Assert.False(value.IsValid);
    }

    public static TheoryData<string, string> InvalidCopies()
    {
        var cases = new TheoryData<string, string>();
        foreach (var source in s_invalidSources)
        foreach (var operation in new[]
                 {
                     "from", "fromBoxed", "fromInterface", "fromObject", "serialize",
                     "serializeBoxed", "serializeInterface", "add", "set", "objectAdd",
                     "objectAddBoxed", "objectAddInterface", "objectTryAdd", "objectSetNew", "objectSetBoxed",
                     "mapKey", "mapValue", "replace", "replaceBoxed", "replaceInterface",
                     "replaceScalar", "arrayConstructor"
                 })
        {
            cases.Add(source, operation);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidCopies))]
    public void CopyingInvalidValueIsRejectedBeforeChangingTarget(string source, string operation)
    {
        var value = CreateInvalidValue(source);
        var array = new DArray(1);
        var obj = new DObject { { "keep", 1 } };
        var map = new DMap();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            switch (operation)
            {
                case "from": DValue.From(value); break;
                case "fromBoxed": DValue.From<object>(value); break;
                case "fromInterface": DValue.From<IDoxNode>(value); break;
                case "fromObject": DValue.FromObject(value, typeof(DValue)); break;
                case "serialize": JsonSerializer.Serialize(value); break;
                case "serializeBoxed": JsonSerializer.Serialize<object>(value); break;
                case "serializeInterface": JsonSerializer.Serialize<IDoxNode>(value); break;
                case "add": array.Add(value); break;
                case "set": array[0] = value; break;
                case "objectAdd": obj.Add("invalid", value); break;
                case "objectAddBoxed": obj.Add<object>("invalid", value); break;
                case "objectAddInterface": obj.Add<IDoxNode>("invalid", value); break;
                case "objectTryAdd": obj.TryAdd("invalid", value); break;
                case "objectSetNew": obj["invalid"] = value; break;
                case "objectSetBoxed": obj.SetProperty("invalid", value); break;
                case "mapKey": map.Add(value, 1); break;
                case "mapValue": map.Add("invalid", value); break;
                case "replace": array[0].ReplaceWith(value); break;
                case "replaceBoxed": array[0].ReplaceWith<object>(value); break;
                case "replaceInterface": array[0].ReplaceWith<IDoxNode>(value); break;
                case "replaceScalar": DValue.Null.ReplaceWith(value); break;
                case "arrayConstructor": _ = new DArray(value); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });

        Assert.Equal("[1]", array.ToJsonString());
        Assert.Equal("{\"keep\":1}", obj.ToJsonString());
        Assert.Equal(0, map.Count);
        Assert.False(value.IsValid);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("disposed")]
    [InlineData("documentVersion")]
    [InlineData("reusedToken")]
    [InlineData("shiftedArray")]
    [InlineData("shiftedObject")]
    [InlineData("shiftedMap")]
    [InlineData("replacedView")]
    [InlineData("clearedArray")]
    public void CreatingValueFromBoxedValuePreservesInvalidity(string source)
    {
        var value = CreateInvalidValue(source);
        var wrapped = DValue.Create((object)value);

        Assert.False(wrapped.IsValid);
        Assert.Equal(value, wrapped);
        Assert.Throws<ObjectDisposedException>(() => wrapped.AsElement());
        Assert.Throws<ObjectDisposedException>(() => new DArray(wrapped));
    }

    [Fact]
    public void CreatingValueFromInvalidElementPreservesInvalidity()
    {
        using var document = JsonDocument.Parse("[1,2]");
        var root = document.RootElement;
        var array = root.AsArray();
        array.RemoveAt(1);
        document.Dispose();

        foreach (var element in new[] { default(DElement), root, document.RootElement })
        {
            Assert.False(element.IsValid);
            foreach (var value in new[] { DValue.Create(element), DValue.Create((object)element) })
            {
                Assert.False(value.IsValid);
                Assert.Throws<ObjectDisposedException>(() => value.AsElement());
            }
        }
    }

    [Fact]
    public void InvalidNullValueCannotBecomeValidThroughNullableConversion()
    {
        var array = new DArray(DValue.Null);
        var value = array[0].AsElement().AsValue();
        array.RemoveAt(0);
        array.Add(DValue.Null);

        Assert.False(value.IsValid);
        Assert.Throws<ObjectDisposedException>(() => DValue.Create((int?)value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootValueRemainsValidAfterDescendantEdits(bool json5)
    {
        const string json = "{\"channel\":{\"obsolete\":0,\"items\":[1,2]}}";
        using Document document = json5 ? Json5Document.Parse(json) : JsonDocument.Parse(json);
        var rootElement = document.RootElement;
        var root = rootElement.AsValue();
        var channel = root["channel"].AsObject();
        channel.Remove("obsolete");
        var items = channel["items"].AsArray();
        items.RemoveAt(0);
        items.Add(3);

        Assert.False(rootElement.IsValid);
        Assert.True(root.IsValid);
        Assert.True(root.AsElement().IsValid);
        Assert.Equal("{\"channel\":{\"items\":[2,3]}}", root.ToJsonString());
        Assert.Equal(3, (int)root["channel"]["items"][1]);
        Assert.True(DValue.DeepEquals(root, root.DeepClone()));
        Assert.True(DValue.DeepEquals(root, DValue.From(root)));

        root.AsObject().Clear();
        Assert.True(root.IsValid);
        Assert.Equal("{}", root.ToJsonString());

        document.Dispose();
        Assert.False(root.IsValid);
        Assert.Throws<ObjectDisposedException>(() => root.AsElement());
    }

    [Fact]
    public void RootValueFollowsRootReplacement()
    {
        var root = DValue.ParseJson("{\"items\":[1,2]}");
        root.ReplaceWith(new DArray(3, 4));

        Assert.True(root.IsValid);
        Assert.Equal("[3,4]", root.ToJsonString());
        Assert.Equal(4, (int)root[1]);

        root.ReplaceWith(true);
        Assert.True(root.IsValid);
        Assert.True((bool)root);

        root.ReplaceWith(DValue.Null);
        Assert.True(root.IsValid);
        Assert.Null((int?)root);
    }

    [Fact]
    public void ValidValuesAndExplicitNullStillConvertAndCopy()
    {
        foreach (var value in new[] { DValue.Null, DValue.Create(42), DValue.Create("text") })
        {
            Assert.True(value.AsElement().AsValue().IsValid);
            Assert.Equal(value, DValue.Create((object)value));
            Assert.True(DValue.DeepEquals(value, DValue.From(value)));
            Assert.True(DValue.DeepEquals(value, new DArray(value)[0]));
        }

        var array = new DArray(1, 2);
        var slot = array[1];
        array.RemoveAt(1);
        Assert.False(slot.IsValid);
        array.Add(99);
        Assert.True(slot.IsValid);
        Assert.Equal(99, slot.AsElement().GetInt32());
        Assert.True(array.AsValue().AsArray().IsValid);

        var obj = new DObject { { "keep", 1 }, { "drop", 2 } };
        var keep = obj["keep"];
        obj.Remove("drop");
        Assert.True(keep.IsValid);
        Assert.Equal(1, keep.AsElement().GetInt32());
        Assert.True(obj.AsValue().AsObject().IsValid);
        var map = new DMap();
        Assert.True(map.AsValue().AsMap().IsValid);
        Assert.True(map.AsValue().AsContainer().IsValid);
    }

    private static object? ConvertValue(DValue value, string operation)
    {
        return operation switch
        {
            "element" => value.AsElement(),
            "implicitElement" => (DElement)value,
            "container" => value.AsContainer(),
            "array" => value.AsArray(),
            "object" => value.AsObject(),
            "map" => value.AsMap(),
            "arrayIndexer" => value[0],
            "objectIndexer" => value["keep"],
            "root" => value.Root,
            "parent" => value.Parent,
            "clone" => value.DeepClone(),
            "deserialize" => value.To<DValue>(),
            "deserializeObject" => value.ToObject(typeof(DValue)),
            "trivia" => value.LeadingTrivia,
            "boolCast" => (bool)value,
            "doubleCast" => (double)value,
            "stringCast" => (string?)value,
            "decimalCast" => (decimal)value,
            "floatCast" => (float)value,
            "integerCopy" => DValue.Create((int)value),
            "nullableIntegerCopy" => DValue.Create((int?)value),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static DValue CreateInvalidValue(string source)
    {
        DValue value;
        switch (source)
        {
            case "default":
                value = default;
                break;
            case "disposed":
                var document = JsonDocument.Parse("[1]");
                value = document.RootElement.AsValue();
                document.Dispose();
                break;
            case "documentVersion":
                var versioned = new DArray { new DArray(1), 2 };
                value = versioned[0].AsElement().AsValue();
                versioned.RemoveAt(1);
                break;
            case "reusedToken":
                var reused = new DArray(1, 2);
                value = reused[1].AsElement().AsValue();
                reused.RemoveAt(1);
                reused.Add(99);
                Assert.Equal((uint)value.Payload, reused[1].AsElement().Id);
                break;
            case "shiftedArray":
                var array = new DArray { new DArray(1) };
                value = array[0];
                array.Insert(0, 0);
                break;
            case "shiftedObject":
                var obj = new DObject { { "drop", 0 }, { "keep", new DObject { { "x", 1 } } } };
                value = obj["keep"];
                obj.Remove("drop");
                break;
            case "shiftedMap":
                var map = new DMap();
                map.Add("keep", new DObject { { "x", 1 } });
                using (var enumerator = map.GetEnumerator())
                {
                    Assert.True(enumerator.MoveNext());
                    value = enumerator.Current.Value;
                }

                map.Insert(0, new KeyValuePair<DValue, DValue>("first", 0));
                break;
            case "replacedView":
                var replaced = new DObject { { "keep", new DArray(1) } };
                value = replaced["keep"];
                replaced.AsMap();
                break;
            case "clearedArray":
                var cleared = new DArray(1);
                value = cleared[0];
                cleared.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }

        Assert.False(value.IsValid);
        return value;
    }
}