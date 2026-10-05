using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

public class CollectionReadTest
{
    [Fact]
    public void ListMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<List<int>>();
    }

    [Fact]
    public void SetMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<HashSet<int>>();
    }

    [Fact]
    public void LinkedListMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<LinkedList<int>>();
    }

    [Fact]
    public void ReadOnlyInterfaceMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<IReadOnlyCollection<int>>();
    }

    [Fact]
    public void EnumerableMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<IEnumerable<int>>();
    }

    [Fact]
    public void ProducerConsumerMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<ConcurrentQueue<int>>();
    }

    [Fact]
    public void DefaultListMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<CustomIntList>();
    }

    [Fact]
    public void DefaultSetMapPreservesReferenceAndPropertyNumberHandling()
    {
        AssertMapMatches<CustomIntSet>();
    }

    private static void AssertMapMatches<T>() where T : class, IEnumerable<int>
    {
        const string json = "{\"First\":{\"$id\":\"1\",\"$values\":[\"1\",\"2\",\"2\"]},\"Second\":{\"$ref\":\"1\"}}";
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve };
        var expected = JsonSerializer.Deserialize<Holder<T>>(json, options)!;
        var actual = Json.JsonSerializer.Deserialize<Holder<T>>(json, new SystemTextJsonSerializerSettings(options))!;

        Assert.NotNull(actual.First);
        Assert.Equal(expected.First!.Count(), actual.First.Count());
        Assert.Equal<int>(expected.First, actual.First);
        Assert.Same(actual.First, actual.Second);
    }

    [Theory]
    [InlineData("[1,2,2]")]
    [InlineData("null")]
    public void PlainArraysAndNullMatchSystemTextJson(string json)
    {
        var settings = new SystemTextJsonSerializerSettings();
        Assert.Equal(JsonSerializer.Deserialize<List<int>>(json),
            Json.JsonSerializer.Deserialize<List<int>>(json, settings));
        Assert.Equal(JsonSerializer.Deserialize<HashSet<int>>(json),
            Json.JsonSerializer.Deserialize<HashSet<int>>(json, settings));
    }

    [Theory]
    [InlineData(typeof(LinkedList<int>))]
    [InlineData(typeof(IReadOnlyCollection<int>))]
    [InlineData(typeof(IEnumerable<int>))]
    [InlineData(typeof(ConcurrentQueue<int>))]
    [InlineData(typeof(CustomIntList))]
    [InlineData(typeof(CustomIntSet))]
    public void OtherCollectionsReadPlainArraysAndNull(Type type)
    {
        var settings = new SystemTextJsonSerializerSettings();
        foreach (var json in new[] { "[1,2,2]", "null" })
        {
            var expected = (IEnumerable<int>?)JsonSerializer.Deserialize(json, type);
            var actual = (IEnumerable<int>?)Json.JsonSerializer.Deserialize(json, type, settings);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void MapsWithoutValuesAreRejected()
    {
        var settings = new SystemTextJsonSerializerSettings();
        Assert.ThrowsAny<Exception>(() => Json.JsonSerializer.Deserialize<List<int>>("{}", settings));
        Assert.ThrowsAny<Exception>(() => Json.JsonSerializer.Deserialize<HashSet<int>>("{}", settings));
    }

    public class CustomIntList : List<int>
    {
    }

    public class CustomIntSet : HashSet<int>
    {
    }

    public class Holder<T> where T : class, IEnumerable<int>
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public T? First { get; set; }

        public T? Second { get; set; }
    }
}