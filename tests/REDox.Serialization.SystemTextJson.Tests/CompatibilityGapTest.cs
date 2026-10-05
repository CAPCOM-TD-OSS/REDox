using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

/// <summary>
///     System.Text.Json 互換レイヤーの未実装項目を検証するテスト。
///     現時点では失敗するものが含まれます。
/// </summary>
public class CompatibilityGapTest
{
    private static string SerializeByRedox<T>(T value, JsonSerializerOptions? options = null)
    {
        return Json.JsonSerializer.Serialize(value, new SystemTextJsonSerializerSettings(options));
    }

    private static T? DeserializeByRedox<T>(string json, JsonSerializerOptions? options = null)
    {
        return Json.JsonSerializer.Deserialize<T>(json, new SystemTextJsonSerializerSettings(options));
    }

    #region Converters

    private sealed class UpperStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString()?.ToLowerInvariant();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUpperInvariant());
        }
    }

    private sealed class StringHolder
    {
        public string Name { get; set; } = "box";
    }

    [Fact]
    public void ConverterForPrimitiveTypeFromOptionsIsUsed()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UpperStringConverter());

        var value = new StringHolder();

        Assert.Equal(JsonSerializer.Serialize(value, options), SerializeByRedox(value, options));
    }

    #endregion

    #region Enum

    public enum Color
    {
        RedColor,
        GreenColor,

        [JsonStringEnumMemberName("blue!")] BlueColor
    }

    private sealed class ColorHolder
    {
        public Color Value { get; set; } = Color.GreenColor;
    }

    [Fact]
    public void StringEnumConverterRespectsNamingPolicy()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        var value = new ColorHolder();

        Assert.Equal(JsonSerializer.Serialize(value, options), SerializeByRedox(value, options));
    }

    [Fact]
    public void StringEnumConverterRejectsIntegerValuesWhenDisallowed()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter(null, false));

        Assert.ThrowsAny<Exception>(() => DeserializeByRedox<ColorHolder>("{\"Value\":1}", options));
    }

    [Fact]
    public void StringEnumMemberNameAttributeIsRespected()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var value = new ColorHolder { Value = Color.BlueColor };

        Assert.Equal(JsonSerializer.Serialize(value, options), SerializeByRedox(value, options));
    }

    #endregion

    #region ExtensionData / Unmapped members

    private sealed class ExtensionDataHolder
    {
        public int Id { get; set; }

        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    [Fact]
    public void ExtensionDataCapturesUnmappedMembers()
    {
        const string json = "{\"Id\":1,\"Unknown\":\"abc\"}";

        var result = DeserializeByRedox<ExtensionDataHolder>(json);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
        Assert.True(result.Extra.ContainsKey("Unknown"));
    }

    [Fact]
    public void ExtensionDataIsWrittenBack()
    {
        var expected = JsonSerializer.Deserialize<ExtensionDataHolder>("{\"Id\":1,\"Unknown\":\"abc\"}");

        Assert.Equal(JsonSerializer.Serialize(expected), SerializeByRedox(expected));
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class StrictHolder
    {
        public int Id { get; set; }
    }

    [Fact]
    public void UnmappedMemberHandlingDisallowThrows()
    {
        Assert.ThrowsAny<Exception>(() => DeserializeByRedox<StrictHolder>("{\"Id\":1,\"Unknown\":2}"));
    }

    [Fact]
    public void UnmappedMemberHandlingFromOptionsThrows()
    {
        var options = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        Assert.ThrowsAny<Exception>(() => DeserializeByRedox<StringHolder>("{\"Name\":\"a\",\"Unknown\":2}", options));
    }

    #endregion

    #region Required members

    private sealed class RequiredHolder
    {
        [JsonRequired] public int Id { get; set; }

        public string? Name { get; set; }
    }

    [Fact]
    public void JsonRequiredAttributeThrowsWhenMissing()
    {
        Assert.ThrowsAny<Exception>(() => DeserializeByRedox<RequiredHolder>("{\"Name\":\"a\"}"));
    }

    private sealed class RequiredKeywordHolder
    {
        public required int Id { get; set; }
    }

    [Fact]
    public void RequiredKeywordThrowsWhenMissing()
    {
        Assert.ThrowsAny<Exception>(() => DeserializeByRedox<RequiredKeywordHolder>("{}"));
    }

    #endregion

    #region Object creation handling

    private sealed class PopulateHolder
    {
        [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
        public List<int> Values { get; } = new() { 1, 2 };
    }

    [Fact]
    public void ObjectCreationHandlingPopulateAppendsToExistingCollection()
    {
        var expected = JsonSerializer.Deserialize<PopulateHolder>("{\"Values\":[3,4]}");
        var actual = DeserializeByRedox<PopulateHolder>("{\"Values\":[3,4]}");

        Assert.NotNull(actual);
        Assert.Equal(expected!.Values, actual!.Values);
    }

    #endregion

    #region Number handling

    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    private sealed class NumberHolder
    {
        public int Value { get; set; } = 42;
    }

    [Fact]
    public void TypeLevelNumberHandlingIsApplied()
    {
        var value = new NumberHolder();

        Assert.Equal(JsonSerializer.Serialize(value), SerializeByRedox(value));
    }

    private sealed class StringRequest
    {
        public required string Username { get; set; }
    }

    [Fact]
    public void StringPropertyRejectsNumericToken()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string json = "{\"username\":123}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StringRequest>(json, options));
        Assert.ThrowsAny<Exception>(() => DeserializeByRedox<StringRequest>(json, options));
    }

    #endregion

    #region Polymorphism

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(IntDerived), 1)]
    public class IntBase
    {
        public int Id { get; set; }
    }

    public sealed class IntDerived : IntBase
    {
        public string? Extra { get; set; }
    }

    [Fact]
    public void IntegerTypeDiscriminatorIsSupported()
    {
        IntBase value = new IntDerived { Id = 1, Extra = "x" };

        Assert.Equal(JsonSerializer.Serialize(value), SerializeByRedox(value));
    }

    [Fact]
    public void IntegerTypeDiscriminatorRoundTrips()
    {
        var json = JsonSerializer.Serialize<IntBase>(new IntDerived { Id = 1, Extra = "x" });

        var result = DeserializeByRedox<IntBase>(json);

        Assert.IsType<IntDerived>(result);
    }

    [JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
    [JsonDerivedType(typeof(TolerantDerived), "derived")]
    public class TolerantBase
    {
        public int Id { get; set; }
    }

    public sealed class TolerantDerived : TolerantBase
    {
    }

    [Fact]
    public void UnrecognizedTypeDiscriminatorIsIgnoredWhenConfigured()
    {
        const string json = "{\"$type\":\"unknown\",\"Id\":1}";

        var expected = JsonSerializer.Deserialize<TolerantBase>(json);
        var actual = DeserializeByRedox<TolerantBase>(json);

        Assert.NotNull(actual);
        Assert.Equal(expected!.Id, actual!.Id);
    }

    [JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
    [JsonDerivedType(typeof(AncestorDerived), "derived")]
    public class AncestorBase
    {
        public int Id { get; set; }
    }

    public class AncestorDerived : AncestorBase
    {
    }

    public sealed class AncestorGrandChild : AncestorDerived
    {
    }

    [Fact]
    public void UnknownDerivedTypeFallsBackToNearestAncestor()
    {
        AncestorBase value = new AncestorGrandChild { Id = 1 };

        Assert.Equal(JsonSerializer.Serialize(value), SerializeByRedox(value));
    }

    #endregion

    #region Constructors

    private sealed class CtorHolder
    {
        [JsonConstructor]
        public CtorHolder(string name, int count)
        {
            Name = name;
            Count = count;
        }

        [JsonPropertyName("n")] public string Name { get; }

        public int Count { get; }
    }

    [Fact]
    public void ConstructorParameterMatchesJsonPropertyName()
    {
        const string json = "{\"n\":\"abc\",\"Count\":3}";

        var expected = JsonSerializer.Deserialize<CtorHolder>(json);
        var actual = DeserializeByRedox<CtorHolder>(json);

        Assert.NotNull(actual);
        Assert.Equal(expected!.Name, actual!.Name);
        Assert.Equal(expected.Count, actual.Count);
    }

    #endregion

    #region JsonInclude

    private sealed class IncludeHolder
    {
        [JsonInclude] internal int Internal { get; set; } = 5;

        [JsonInclude] public int PrivateSetter { get; private set; } = 7;
    }

    [Fact]
    public void JsonIncludeOnNonPublicMemberIsSerialized()
    {
        var value = new IncludeHolder();

        Assert.Equal(JsonSerializer.Serialize(value), SerializeByRedox(value));
    }

    #endregion

    #region Reference handling

    private sealed class Node
    {
        public int Id { get; set; }

        public Node? Next { get; set; }
    }

    [Fact]
    public void CustomReferenceHandlerIsUsed()
    {
        var options = new JsonSerializerOptions
        {
            ReferenceHandler = new CustomReferenceHandler()
        };

        var node = new Node { Id = 1 };
        node.Next = node;

        Assert.Equal(JsonSerializer.Serialize(node, options), SerializeByRedox(node, options));
    }

    private sealed class CustomReferenceHandler : ReferenceHandler
    {
        public override System.Text.Json.Serialization.ReferenceResolver CreateResolver()
        {
            return new CustomReferenceResolver();
        }
    }

    private sealed class CustomReferenceResolver : System.Text.Json.Serialization.ReferenceResolver
    {
        private readonly Dictionary<object, string> _objectToReferenceId = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, object> _referenceIdToObject = new();
        private int _referenceCount;

        public override void AddReference(string referenceId, object value)
        {
            _referenceIdToObject.Add(referenceId, value);
        }

        public override string GetReference(object value, out bool alreadyExists)
        {
            if (_objectToReferenceId.TryGetValue(value, out var referenceId))
            {
                alreadyExists = true;
                return referenceId;
            }

            alreadyExists = false;
            referenceId = (++_referenceCount).ToString();
            _objectToReferenceId.Add(value, referenceId);
            return referenceId;
        }

        public override object ResolveReference(string referenceId)
        {
            return _referenceIdToObject[referenceId];
        }
    }

    #endregion

    #region DefaultIgnoreCondition

    private sealed class NeverIgnoreHolder
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Name { get; set; }
    }

    [Fact]
    public void DefaultIgnoreConditionNeverOverridesGlobalSetting()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var value = new NeverIgnoreHolder();

        Assert.Equal(JsonSerializer.Serialize(value, options), SerializeByRedox(value, options));
    }

    #endregion
}