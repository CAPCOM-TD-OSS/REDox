// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class ContainerSwapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArraySwapPreservesTokensTriviaAndChildren(bool snapshot)
    {
        const string input = "[/*first*/{leaf:1}, /*middle*/[2], /*last*/3]";
        using var source = Json5Document.Parse(input, options: new Json5DocumentOptions { PreserveTrivia = true });
        using var document = snapshot ? source.Duplicate() : null;
        var root = (document ?? source).RootElement;
        var elements = root.EnumerateArray().ToArray();
        var comments = elements.Select(element => element.AsValue().LeadingTrivia.GetEnumerator().ToArray()).ToArray();
        var child = elements[0].AsObject();
        var leaf = child["leaf"].AsElement();
        Assert.Equal(root.AsValue(), elements[0].AsValue().Parent);
        Assert.Equal("$[0].leaf", leaf.AsValue().GetPath());
        var array = root.AsArray();
        var first = array[0];
        var middle = array[1];
        var last = array[2];
        var capacity = array.Capacity;

        array.Swap(0, 2);

        Assert.Equal(3, array.Count);
        Assert.Equal(capacity, array.Capacity);
        Assert.Equal(new[] { elements[2], elements[1], elements[0] }, root.EnumerateArray());
        Assert.False(first.IsValid);
        Assert.True(middle.IsValid);
        Assert.False(last.IsValid);
        Assert.True(child.IsValid);
        Assert.Equal("$[2].leaf", leaf.AsValue().GetPath());
        Assert.Equal(root.AsValue(), elements[0].AsValue().Parent);
        for (var i = 0; i < elements.Length; i++)
        {
            Assert.True(elements[i].IsValid);
            Assert.Equal(comments[i], elements[i].AsValue().LeadingTrivia.GetEnumerator().ToArray());
            Assert.All(comments[i], trivia => Assert.True(trivia.IsValid));
        }

        child["leaf"] = 42;
        Assert.Equal(42, array[2].AsElement().GetProperty("leaf").GetInt32());
        Assert.Equal("[3,[2],{\"leaf\":42}]", array.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapSwapPreservesKeyValuePairsTriviaAndChildren(bool snapshot)
    {
        const string input = "{/*first*/a:/*value a*/{leaf:1}, /*middle*/b:2, /*last*/c:/*value c*/[3]}";
        using var source = Json5Document.Parse(input, options: new Json5DocumentOptions { PreserveTrivia = true });
        using var document = snapshot ? source.Duplicate() : null;
        var root = (document ?? source).RootElement;
        var entries = root.EnumerateMap().ToArray();
        var tokens = entries.SelectMany(entry => new[] { entry.Key, entry.Value }).ToArray();
        var trivia = tokens.Select(token => token.AsValue().LeadingTrivia.GetEnumerator().ToArray()).ToArray();
        var child = entries[0].Value.AsObject();
        var leaf = child["leaf"].AsElement();
        Assert.Equal(root.AsValue(), entries[0].Value.AsValue().Parent);
        var map = root.AsMap();
        var positionalValues = new List<DValue>();
        foreach (var entry in map)
        {
            positionalValues.Add(entry.Value);
        }

        var capacity = map.Capacity;

        map.Swap(0, 2);

        Assert.Equal(3, map.Count);
        Assert.Equal(capacity, map.Capacity);
        Assert.Equal(new[] { entries[2], entries[1], entries[0] }, root.EnumerateMap());
        Assert.False(positionalValues[0].IsValid);
        Assert.True(positionalValues[1].IsValid);
        Assert.False(positionalValues[2].IsValid);
        Assert.True(child.IsValid);
        Assert.Equal("$.a.leaf", leaf.AsValue().GetPath());
        Assert.Equal(root.AsValue(), entries[0].Value.AsValue().Parent);
        for (var i = 0; i < tokens.Length; i++)
        {
            Assert.True(tokens[i].IsValid);
            Assert.Equal(trivia[i], tokens[i].AsValue().LeadingTrivia.GetEnumerator().ToArray());
            Assert.All(trivia[i], item => Assert.True(item.IsValid));
        }

        child["leaf"] = 42;
        Assert.Equal(42, map[2].Value.AsElement().GetProperty("leaf").GetInt32());
        Assert.Equal("{\"c\":[3],\"b\":2,\"a\":{\"leaf\":42}}", map.ToJsonString());
        var obj = map.AsObject();
        Assert.Equal(42, obj["a"].AsElement().GetProperty("leaf").GetInt32());
        Assert.Equal(2, (int)obj["b"]);
    }

    [Fact]
    public void MapSwapSupportsDuplicateAndContainerKeys()
    {
        var map = new DMap();
        using var document = map.AsElement().Document;
        map.Add("same", 1);
        map.Add("same", 2);
        map.Add(new DArray(3), new DObject { ["leaf"] = 4 });
        var entries = map.AsElement().EnumerateMap().ToArray();
        var keyChild = entries[2].Key.EnumerateArray().Single();
        Assert.Equal(entries[2].Key.Id, document.GetParentId(keyChild.Id));

        map.Swap(0, 2);

        Assert.Equal(new[] { entries[2], entries[1], entries[0] }, map.AsElement().EnumerateMap());
        Assert.Equal(entries[2].Key.Id, document.GetParentId(keyChild.Id));
        Assert.Equal(3, keyChild.GetInt32());
        Assert.Equal(4, map[0].Value.AsElement().GetProperty("leaf").GetInt32());
        Assert.Equal("same", (string?)map[1].Key);
        Assert.Equal(2, (int)map[1].Value);
        Assert.Equal("same", (string?)map[2].Key);
        Assert.Equal(1, (int)map[2].Value);
    }

    public static TheoryData<bool, int, int, int, string> InvalidIndices()
    {
        var data = new TheoryData<bool, int, int, int, string>();
        foreach (var isMap in new[] { false, true })
        {
            data.Add(isMap, 0, 0, 0, "index1");
            data.Add(isMap, 2, -1, 0, "index1");
            data.Add(isMap, 2, 2, 0, "index1");
            data.Add(isMap, 2, 0, -1, "index2");
            data.Add(isMap, 2, 0, 2, "index2");
            data.Add(isMap, 2, 2, 2, "index1");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidIndices))]
    public void InvalidIndicesAreRejectedBeforeChangingTheContainer(bool isMap, int count,
        int index1, int index2, string parameter)
    {
        using var document = JsonDocument.Parse(isMap
            ? count == 0 ? "{}" : "{\"a\":1,\"b\":2}"
            : count == 0
                ? "[]"
                : "[1,2]");
        var container = isMap ? (DContainer)document.RootElement.AsMap() : document.RootElement.AsArray();
        var before = container.ToJsonString();
        Action swap = isMap
            ? () => ((DMap)container).Swap(index1, index2)
            : () => ((DArray)container).Swap(index1, index2);

        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(swap).ParamName);
        Assert.Equal(before, container.ToJsonString());
    }

    [Fact]
    public void SwappingAnEntryWithItselfPreservesHandlesAndEnumeration()
    {
        var array = new DArray(1);
        using var arrayDocument = array.AsElement().Document;
        var value = array[0];
        using var arrayEnumerator = array.GetEnumerator();
        var map = new DMap();
        using var mapDocument = map.AsElement().Document;
        map.Add("a", 1);
        using var mapEnumerator = map.GetEnumerator();
        var entry = map[0];

        array.Swap(0, 0);
        map.Swap(0, 0);

        Assert.True(value.IsValid);
        Assert.Equal(value, array[0]);
        Assert.Equal(entry, map[0]);
        Assert.True(arrayEnumerator.MoveNext());
        Assert.False(arrayEnumerator.MoveNext());
        Assert.True(mapEnumerator.MoveNext());
        Assert.False(mapEnumerator.MoveNext());
    }

    [Fact]
    public void SwapRejectsDisposedContainersAndReplacedMapViews()
    {
        var array = new DArray(1);
        array.AsElement().Document.Dispose();
        var map = new DMap();
        map.AsElement().Document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => array.Swap(0, 0));
        Assert.Throws<ObjectDisposedException>(() => map.Swap(0, 0));

        using var document = JsonDocument.Parse("{\"a\":1}");
        var replaced = document.RootElement.AsMap();
        replaced.AsObject();
        Assert.Throws<ObjectDisposedException>(() => replaced.Swap(0, 0));
    }
}