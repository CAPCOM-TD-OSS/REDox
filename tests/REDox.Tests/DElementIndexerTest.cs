using System;
using REDox.Json;

namespace REDox.Tests;

public sealed class DElementIndexerTest
{
    [Fact]
    public void IndexerReturnsElementsOfParsedArray()
    {
        using var doc = JsonDocument.Parse("[1,[2,3],{\"a\":4},\"x\"]");
        var root = doc.RootElement;

        Assert.Equal(1, root[0].GetInt32());
        Assert.Equal(3, root[1][1].GetInt32());
        Assert.Equal(4, root[2].GetProperty("a").GetInt32());
        Assert.Equal("x", root[3].GetString());
    }

    [Fact]
    public void IndexerReturnsElementsOfPrimitiveArrays()
    {
        using var doc = JsonDocument.Parse("[[1,2,3],[4,5],[6,[7],8],[9]]");
        var root = doc.RootElement;

        Assert.Equal(3, root[0][2].GetInt32());
        Assert.Equal(5, root[1][1].GetInt32());
        Assert.Equal(7, root[2][1][0].GetInt32());
        Assert.Equal(8, root[2][2].GetInt32());
        Assert.Equal(9, root[3][0].GetInt32());
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("[[1,2,3]]")]
    [InlineData("{\"a\":[1,2,3],\"b\":0}")]
    [InlineData("{\"b\":0,\"a\":[1,2,3]}")]
    public void IndexerMatchesEnumeration(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var array = root.Token.Type == DTokenType.Array && root.GetArrayLength() == 3 ? root
            : root.Token.Type == DTokenType.Array ? root[0] : root.GetProperty("a");

        var i = 0;
        foreach (var e in array.EnumerateArray())
        {
            Assert.Equal(e.GetInt32(), array[i++].GetInt32());
        }

        Assert.Equal(3, i);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void IndexerThrowsWhenOutOfRange(int index)
    {
        using var doc = JsonDocument.Parse("[1,2]");
        var root = doc.RootElement;

        Assert.Throws<ArgumentOutOfRangeException>(() => root[index]);
    }

    [Fact]
    public void IndexerThrowsWhenNotArray()
    {
        using var doc = JsonDocument.Parse("{\"a\":1}");
        var root = doc.RootElement;

        Assert.Throws<InvalidOperationException>(() => root[0]);
    }

    [Fact]
    public void IndexerWorksOnModifiedArray()
    {
        using var doc = JsonDocument.Parse("[1,2]");
        var root = doc.RootElement;

        root.AsArray().Add(3);

        Assert.Equal(3, root.GetArrayLength());
        Assert.Equal(2, root[1].GetInt32());
        Assert.Equal(3, root[2].GetInt32());
    }
}
