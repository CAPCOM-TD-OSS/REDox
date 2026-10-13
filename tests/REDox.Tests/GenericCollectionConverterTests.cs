// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Collections.ObjectModel;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class GenericCollectionConverterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CollectionsRoundTrip(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var values = new[] { 1, 2, 3 };

        Assert.Equal(values, DoxSerializer.DeepCopy(new List<int>(values), settings));
        Assert.Equal(values, DoxSerializer.DeepCopy<IList<int>>(new List<int>(values), settings));
        Assert.Equal(values, DoxSerializer.DeepCopy(new LinkedList<int>(values), settings));
        Assert.Equal(values, DoxSerializer.DeepCopy(new Queue<int>(values), settings));
        Assert.True(DoxSerializer.DeepCopy(new HashSet<int>(values), settings).SetEquals(values));
        Assert.Equal(values, DoxSerializer.DeepCopy(new ReadOnlyCollection<int>(values), settings));
        Assert.Equal(values, DoxSerializer.DeepCopy<IEnumerable<int>>(new List<int>(values), settings));
    }

    [Theory]
    [InlineData(true, DictionaryFormatHandling.Object)]
    [InlineData(false, DictionaryFormatHandling.Object)]
    [InlineData(true, DictionaryFormatHandling.KeyValuePair)]
    [InlineData(false, DictionaryFormatHandling.KeyValuePair)]
    [InlineData(true, DictionaryFormatHandling.Map)]
    [InlineData(false, DictionaryFormatHandling.Map)]
    public void DictionariesRoundTrip(bool dynamicConverters, DictionaryFormatHandling format)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            DictionaryFormatHandling = format
        };
        var source = new Dictionary<string, int> { ["first"] = 1, ["second"] = 2 };

        Assert.Equal(source, DoxSerializer.DeepCopy(source, settings));
        Assert.Equal(source, DoxSerializer.DeepCopy<IDictionary<string, int>>(source, settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeyValuePairsRespectNamingPolicy(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            PropertyNamingPolicy = NamingPolicy.CamelCase
        };
        var source = new KeyValuePair<string, int>("test", 42);

        Assert.Equal(source, DoxSerializer.DeepCopy(source, settings));
        Assert.Equal(source,
            JsonSerializer.Deserialize<KeyValuePair<string, int>>("{\"key\":\"test\",\"value\":42}", settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingKeyValuePairMembersUseDefaultValues(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };

        Assert.Equal(default,
            JsonSerializer.Deserialize<KeyValuePair<int, bool>>("{}", settings));
        Assert.Equal(new KeyValuePair<int, bool>(7, false),
            JsonSerializer.Deserialize<KeyValuePair<int, bool>>("{\"Key\":7}", settings));
        Assert.Equal(new KeyValuePair<string, int>(null!, 9),
            JsonSerializer.Deserialize<KeyValuePair<string, int>>("{\"Value\":9}", settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeyValuePairsRespectIgnoreReadOnlyProperties(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            IgnoreReadOnlyProperties = true
        };

        Assert.Equal(default,
            DoxSerializer.DeepCopy(new KeyValuePair<int, bool>(7, true), settings));
    }
}