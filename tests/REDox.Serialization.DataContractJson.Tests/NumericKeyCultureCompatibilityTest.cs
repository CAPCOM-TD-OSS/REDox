using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using REDox.Json;
using FrameworkSettings = System.Runtime.Serialization.Json.DataContractJsonSerializerSettings;

namespace REDox.Serialization.DataContractJson.Tests;

public sealed class NumericKeyCultureCompatibilityTest
{
    [Theory]
    [InlineData("pt-BR", true)]
    [InlineData("de-DE", true)]
    [InlineData("sv-SE", true)]
    [InlineData("en-US", true)]
    [InlineData("pt-BR", false)]
    [InlineData("de-DE", false)]
    [InlineData("sv-SE", false)]
    [InlineData("en-US", false)]
    public void NumericDictionaryKeysMatchFramework(string culture, bool simpleDictionary)
    {
        var settings = new FrameworkSettings { UseSimpleDictionaryFormat = simpleDictionary };
        // Construct settings before switching culture to verify that key conversion uses the call's culture.
        var adapter = new DataContractJsonSerializerSettings(settings);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            AssertMatchesFramework(new Dictionary<sbyte, int> { [-42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<byte, int> { [42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<short, int> { [-42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<ushort, int> { [42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<int, int> { [-42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<uint, int> { [42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<long, int> { [-42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<ulong, int> { [42] = 1, [10] = 2 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<float, int> { [0.1f] = 1, [0.4f] = 4, [-0.5f] = 5 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<double, int> { [0.1] = 1, [0.4] = 4, [-0.5] = 5 }, settings, adapter);
            AssertMatchesFramework(new Dictionary<decimal, int> { [0.10m] = 1, [0.4m] = 4, [-0.5m] = 5 }, settings, adapter);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static void AssertMatchesFramework<T>(Dictionary<T, int> value, FrameworkSettings settings,
        DataContractJsonSerializerSettings adapter) where T : notnull
    {
        var serializer = new DataContractJsonSerializer(typeof(Dictionary<T, int>), settings);
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        var expectedJson = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal(expectedJson, JsonSerializer.Serialize(value, adapter));

        stream.Position = 0;
        var expected = (Dictionary<T, int>)serializer.ReadObject(stream)!;
        var actual = JsonSerializer.Deserialize<Dictionary<T, int>>(expectedJson, adapter)!;
        Assert.Equal(expected.Count, actual.Count);
        foreach (var entry in expected)
        {
            Assert.True(actual.TryGetValue(entry.Key, out var actualValue));
            Assert.Equal(entry.Value, actualValue);
        }
    }

    [Theory]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    public void NumericDictionaryKeysRejectTrailingText(Type keyType)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var settings = new FrameworkSettings { UseSimpleDictionaryFormat = true };
            var type = typeof(Dictionary<,>).MakeGenericType(keyType, typeof(int));
            const string json = "{\"0,1suffix\":10}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Assert.ThrowsAny<Exception>(() => new DataContractJsonSerializer(type, settings).ReadObject(stream));
            Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize(json, type,
                new DataContractJsonSerializerSettings(settings)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}