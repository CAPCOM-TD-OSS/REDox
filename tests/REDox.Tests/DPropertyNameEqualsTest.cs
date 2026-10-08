using System;
using System.Text;
using REDox.Json;

namespace REDox.Tests;

public sealed class DPropertyNameEqualsTest
{
    [Theory]
    [InlineData("{\"abc\":1}", "abc")]
    [InlineData("{\"\":1}", "")]
    [InlineData("{\"あいう\":1}", "あいう")]
    [InlineData("{\"\\u3042\\\"x\":1}", "あ\"x")]
    [InlineData("{\"🙂\":1}", "🙂")]
    public void NameEqualsMatchesParsedName(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);

        AssertNameEquals(doc.RootElement, name);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("あいう")]
    [InlineData("🙂")]
    public void NameEqualsMatchesAddedName(string name)
    {
        using var doc = JsonDocument.Parse("{}");
        doc.RootElement.AsObject().Add(name, 1);

        AssertNameEquals(doc.RootElement, name);
    }

    [Fact]
    public void GetPropertyFindsParsedAndAddedNames()
    {
        using var doc = JsonDocument.Parse("{\"a\":1,\"あ\":2}");
        var root = doc.RootElement;
        root.AsObject().Add("🙂", 3);

        Assert.Equal(2, root.GetProperty("あ").GetInt32());
        Assert.Equal(2, root.GetProperty("あ".AsSpan()).GetInt32());
        Assert.Equal(2, root.GetProperty("あ"u8).GetInt32());
        Assert.Equal(3, root.GetProperty("🙂").GetInt32());
        Assert.Equal(3, root.GetProperty("🙂"u8).GetInt32());
        Assert.False(root.TryGetProperty("b", out _));
        Assert.False(root.TryGetProperty("b"u8, out _));
    }

    private static void AssertNameEquals(DElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
        {
            Assert.True(prop.NameEquals(name));
            Assert.True(prop.NameEquals(name.AsSpan()));
            Assert.True(prop.NameEquals(Encoding.UTF8.GetBytes(name)));

            var other = name + "x";
            Assert.False(prop.NameEquals(other));
            Assert.False(prop.NameEquals(other.AsSpan()));
            Assert.False(prop.NameEquals(Encoding.UTF8.GetBytes(other)));

            if (name.Length > 0)
            {
                var shorter = name.Substring(0, name.Length - 1);
                Assert.False(prop.NameEquals(shorter));
                Assert.False(prop.NameEquals(shorter.AsSpan()));
            }
        }
    }
}