using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using REDox.Json;

namespace REDox.Serialization.DataContractJson.Tests;

public sealed class DateOnlyKeyCultureCompatibilityTest
{
    [Theory]
    [InlineData("", true)]
    [InlineData("pt-BR", true)]
    [InlineData("sv-SE", true)]
    [InlineData("ja-JP", true)]
    [InlineData("th-TH", true)]
    [InlineData("", false)]
    [InlineData("pt-BR", false)]
    [InlineData("sv-SE", false)]
    [InlineData("ja-JP", false)]
    [InlineData("th-TH", false)]
    public void DateOnlyDictionaryKeysMatchFramework(string cultureName, bool dynamicConverters)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            };
            var adapter = new DataContractJsonSerializerSettings(settings)
            {
                AllowDynamicGenericConverters = dynamicConverters
            };
            var value = new Dictionary<DateOnly, int>
            {
                [new DateOnly(1999, 1, 1)] = 1,
                [new DateOnly(2024, 1, 2)] = 2,
                [new DateOnly(2024, 11, 23)] = 3
            };
            var serializer = new DataContractJsonSerializer(typeof(Dictionary<DateOnly, int>), settings);
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, value);
            var json = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Equal(json, JsonSerializer.Serialize(value, adapter));
            if (cultureName == "th-TH")
            {
                // Gregorian 1999 is represented as Buddhist year 2542 in the key string.
                Assert.Contains("2542", json);
            }

            stream.Position = 0;
            var expected = (Dictionary<DateOnly, int>)serializer.ReadObject(stream)!;
            var actual = JsonSerializer.Deserialize<Dictionary<DateOnly, int>>(json, adapter)!;
            Assert.Equal(expected.Count, actual.Count);
            foreach (var entry in expected)
            {
                Assert.True(actual.TryGetValue(entry.Key, out var actualValue));
                Assert.Equal(entry.Value, actualValue);
            }

            Assert.Equal(json, JsonSerializer.Serialize(actual, adapter));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}