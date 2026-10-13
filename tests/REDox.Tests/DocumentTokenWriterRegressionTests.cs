// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class DocumentTokenWriterRegressionTests
{
    private static readonly Json5WriteOptions s_writeOptions = new()
    {
        PreserveTrivia = false,
        PropertyNameStyle = Json5QuoteStyle.AlwaysDouble
    };

    public static TheoryData<string, bool, bool> ContainerKeyCases()
    {
        var cases = new TheoryData<string, bool, bool>();
        foreach (var shape in new[] { "map", "arrayWithMap", "mapWithMapKey", "mapWithArrayKey" })
        {
            foreach (var reuseSlots in new[] { false, true })
            {
                cases.Add(shape, reuseSlots, false);
                cases.Add(shape, reuseSlots, true);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(ContainerKeyCases))]
    public void CopyingContainerKeysShouldPreserveNestedMapsAndReleaseAllPayloads(
        string shape, bool reuseSlots, bool serializeInArray)
    {
        var inner = new DMap();
        inner.Add("inner", 1);
        var key = inner.AsElement();
        if (shape == "arrayWithMap")
        {
            key = new DArray(inner).AsElement();
        }
        else if (shape is "mapWithMapKey" or "mapWithArrayKey")
        {
            var outer = new DMap();
            outer.Add(shape == "mapWithMapKey" ? inner.AsElement() : new DArray(inner).AsElement(), 2);
            key = outer.AsElement();
        }

        var source = new DMap();
        source.Add(key, 9);
        var value = new DMap();
        value.Add("value", new DArray(7, 8));
        source.Add("after", value);
        using var destination = Json5Document.Parse(reuseSlots
                ? "[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16]"
                : "0",
            options: new Json5DocumentOptions { PreserveTrivia = false });

        if (serializeInArray)
        {
            destination.RootElement.AsValue().ReplaceWith(new[] { source.AsElement() });
        }
        else
        {
            destination.RootElement.AsValue().ReplaceWith(source.AsElement());
        }

        var copiedMap = serializeInArray
            ? destination.RootElement.EnumerateArray().Single()
            : destination.RootElement;
        var pairs = copiedMap.EnumerateMap().ToArray();
        Assert.Equal(2, pairs.Length);
        AssertEquivalent(key, pairs[0].Key);
        Assert.Equal(9, pairs[0].Value.GetInt32());
        Assert.Equal("after", pairs[1].Key.GetString());
        Assert.Equal("{\"value\":[7,8]}", Json5Document.EncodeToString(pairs[1].Value, s_writeOptions));

        var rootContainer = serializeInArray
            ? (DContainer)destination.RootElement.AsArray()
            : destination.RootElement.AsMap();
        if (rootContainer is DArray array)
        {
            array.Clear();
        }
        else
        {
            ((DMap)rootContainer).Clear();
        }

        // Every copied key, value, and nested container must be reclaimed.
        Assert.Same(rootContainer, Assert.Single(destination.GetExtends().ToArray(), item => item != null));
    }

    private static void AssertEquivalent(DElement expected, DElement actual)
    {
        Assert.Equal(expected.Token.Type, actual.Token.Type);
        if (expected.Token.Type == DTokenType.Array)
        {
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            Assert.Equal(expectedItems.Length, actualItems.Length);
            for (var i = 0; i < expectedItems.Length; i++)
            {
                AssertEquivalent(expectedItems[i], actualItems[i]);
            }
        }
        else if (expected.Token.Type == DTokenType.Map)
        {
            var expectedPairs = expected.EnumerateMap().ToArray();
            var actualPairs = actual.EnumerateMap().ToArray();
            Assert.Equal(expectedPairs.Length, actualPairs.Length);
            for (var i = 0; i < expectedPairs.Length; i++)
            {
                AssertEquivalent(expectedPairs[i].Key, actualPairs[i].Key);
                AssertEquivalent(expectedPairs[i].Value, actualPairs[i].Value);
            }
        }
        else
        {
            Assert.Equal(Json5Document.EncodeToString(expected, s_writeOptions),
                Json5Document.EncodeToString(actual, s_writeOptions));
        }
    }
}