using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using REDox.Json;

namespace REDox.Serialization.DataContractJson.Tests;

public sealed class DataContractJsonCompatibilityGapTest
{
    private static string SerializeByDataContract(Type type, object? value,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var serializer = new DataContractJsonSerializer(type, settings);

        using var ms = new MemoryStream();

        serializer.WriteObject(ms, value);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static object? DeserializeByDataContract(Type type, string json,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var serializer = new DataContractJsonSerializer(type, settings);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return serializer.ReadObject(ms);
    }

    private static string SerializeByRedox(Type type, object? value,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        return JsonSerializer.Serialize(value, type, new DataContractJsonSerializerSettings(settings));
    }

    private static object? DeserializeByRedox(Type type, string json,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        return JsonSerializer.Deserialize(json, type, new DataContractJsonSerializerSettings(settings));
    }

    #region RootName

    [DataContract]
    public sealed class SimpleData
    {
        [DataMember] public int Id { get; set; } = 1;

        [DataMember] public string Name { get; set; } = "abc";
    }

    [Fact]
    public void RootNameIsEmitted()
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            RootName = "root"
        };

        var value = new SimpleData();

        Assert.Equal(SerializeByDataContract(typeof(SimpleData), value, settings),
            SerializeByRedox(typeof(SimpleData), value, settings));
    }

    [Fact]
    public void RootNameIsRead()
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            RootName = "root"
        };

        var json = SerializeByDataContract(typeof(SimpleData), new SimpleData(), settings);

        var result = (SimpleData?)DeserializeByRedox(typeof(SimpleData), json, settings);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
        Assert.Equal("abc", result.Name);
    }

    #endregion

    #region SerializeReadOnlyTypes

    [DataContract]
    public sealed class ReadOnlyData
    {
        public ReadOnlyData()
        {
        }

        public ReadOnlyData(int id)
        {
            Id = id;
        }

        [DataMember] public int Id { get; }
    }

    [Fact]
    public void SerializeReadOnlyTypesEmitsGetOnlyMembers()
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            SerializeReadOnlyTypes = true
        };

        var value = new ReadOnlyData(5);

        Assert.Equal(SerializeByDataContract(typeof(ReadOnlyData), value, settings),
            SerializeByRedox(typeof(ReadOnlyData), value, settings));
    }

    [Fact]
    public void SerializeReadOnlyTypesDisabledSkipsGetOnlyMembers()
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            SerializeReadOnlyTypes = false
        };

        var value = new ReadOnlyData(5);

        Assert.Equal(SerializeByDataContract(typeof(ReadOnlyData), value, settings),
            SerializeByRedox(typeof(ReadOnlyData), value, settings));
    }

    #endregion

    #region IgnoreExtensionDataObject

    [DataContract(Name = "Extensible", Namespace = "")]
    public sealed class ExtensibleV1 : IExtensibleDataObject
    {
        [DataMember] public int Id { get; set; }

        public ExtensionDataObject? ExtensionData { get; set; }
    }

    [DataContract(Name = "Extensible", Namespace = "")]
    public sealed class ExtensibleV2
    {
        [DataMember] public int Id { get; set; }

        [DataMember] public string? Extra { get; set; }
    }

    [Fact]
    public void ExtensionDataIsRoundTrippedWhenNotIgnored()
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            IgnoreExtensionDataObject = false
        };

        var json = SerializeByDataContract(typeof(ExtensibleV2), new ExtensibleV2 { Id = 1, Extra = "x" }, settings);

        var expectedInstance = DeserializeByDataContract(typeof(ExtensibleV1), json, settings);
        var expected = SerializeByDataContract(typeof(ExtensibleV1), expectedInstance, settings);

        var actualInstance = DeserializeByRedox(typeof(ExtensibleV1), json, settings);
        var actual = SerializeByRedox(typeof(ExtensibleV1), actualInstance, settings);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ExtensionDataIsDroppedWhenIgnored()
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            IgnoreExtensionDataObject = true
        };

        var json = SerializeByDataContract(typeof(ExtensibleV2), new ExtensibleV2 { Id = 1, Extra = "x" }, settings);

        var expectedInstance = DeserializeByDataContract(typeof(ExtensibleV1), json, settings);
        var expected = SerializeByDataContract(typeof(ExtensibleV1), expectedInstance, settings);

        var actualInstance = DeserializeByRedox(typeof(ExtensibleV1), json, settings);
        var actual = SerializeByRedox(typeof(ExtensibleV1), actualInstance, settings);

        Assert.Equal(expected, actual);
    }

    [DataContract(Name = "Order", Namespace = "")]
    public sealed class OrderHolder : IExtensibleDataObject
    {
        [DataMember] public string? Mmm { get; set; }

        public ExtensionDataObject? ExtensionData { get; set; }
    }

    [Theory]
    [InlineData("{\"Zzz\":\"z\",\"Aaa\":\"a\",\"Mmm\":\"m\"}")]
    [InlineData("{\"Mmm\":\"m\",\"Zzz\":\"z\",\"Aaa\":\"a\"}")]
    [InlineData("{\"Aaa\":\"a\",\"Mmm\":\"m\",\"Zzz\":\"z\"}")]
    public void ExtensionDataPreservesMemberOrder(string json)
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            IgnoreExtensionDataObject = false
        };

        var expected = SerializeByDataContract(typeof(OrderHolder),
            DeserializeByDataContract(typeof(OrderHolder), json, settings), settings);

        var actual = SerializeByRedox(typeof(OrderHolder),
            DeserializeByRedox(typeof(OrderHolder), json, settings), settings);

        Assert.Equal(expected, actual);
    }

    #endregion

    #region DateTimeFormat

    [DataContract]
    public sealed class DateData
    {
        [DataMember] public DateTime Value { get; set; } = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    }

    [Fact]
    public void DateTimeFormatProviderIsApplied()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // A matching ambient culture can conceal an ignored format provider.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("D", CultureInfo.GetCultureInfo("ja-JP"))
            };

            var value = new DateData();
            var expected = SerializeByDataContract(typeof(DateData), value, settings);

            Assert.Contains("2024年1月2日", expected);
            Assert.Equal(expected, SerializeByRedox(typeof(DateData), value, settings));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void DateTimeFormatProviderIsAppliedWhenDeserializing()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("dd MMMM yyyy HH:mm:ss", CultureInfo.GetCultureInfo("fr-FR"))
            };
            const string json = "\"02 janvier 2024 03:04:05\"";
            var expected = (DateTime)DeserializeByDataContract(typeof(DateTime), json, settings)!;

            Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5), expected);
            var actual = (DateTime)DeserializeByRedox(typeof(DateTime), json, settings)!;
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Kind, actual.Kind);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("2024-01-02T03:04:05")]
    [InlineData("2024-01-02T03:04:05Z")]
    public void DateTimeStylesIsApplied(string text)
    {
        var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
        {
            DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)
            {
                DateTimeStyles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
            }
        };

        // A Z suffix already implies UTC and can conceal ignored parsing styles.
        var json = $"{{\"Value\":\"{text}\"}}";

        var expected = DeserializeByDataContract(typeof(DateData), json, settings) as DateData;
        var actual = DeserializeByRedox(typeof(DateData), json, settings) as DateData;

        Assert.NotNull(expected);
        Assert.Equal(DateTimeKind.Utc, expected.Value.Kind);
        Assert.NotNull(actual);
        Assert.Equal(expected!.Value, actual!.Value);
        Assert.Equal(expected.Value.Kind, actual.Value.Kind);
    }

    #endregion
}