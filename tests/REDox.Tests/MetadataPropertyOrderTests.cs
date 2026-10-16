// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using REDox.Json;
using REDox.Serialization;
using REDox.Serialization.Metadata;

namespace REDox.Tests;

public sealed class MetadataPropertyOrderTests
{
    public static TheoryData<bool, string> MutableObjectIdCases => ForBothConverters(
        """{"$id":"1","Name":"first","Next":null}""",
        """{"Name":"first","$id":"1","Next":null}""",
        """{"Name":"first","Next":null,"$id":"1"}""");

    public static TheoryData<bool, string> SelfReferenceCases => ForBothConverters(
        """{"$id":"1","Name":"first","Next":{"$ref":"1"}}""",
        """{"Name":"first","$id":"1","Next":{"$ref":"1"}}""",
        """{"Name":"first","Next":{"$ref":"1"},"$id":"1"}""",
        """{"Next":{"$ref":"1"},"Name":"first","$id":"1"}""",
        """{"Next":{"$ref":"1"},"Name":"first","\u0024id":"1"}""");

    public static TheoryData<bool, string> ParameterizedObjectIdCases => ForBothConverters(
        """{"$id":"1","Name":"first"}""",
        """{"Name":"first","$id":"1"}""");

    // Additional ordinary properties exercise $ref in the middle and at the end.
    public static TheoryData<bool, string> ReferenceCases => ForBothConverters(
        """{"$ref":"1","Name":"ignored","Next":null}""",
        """{"Name":"ignored","$ref":"1","Next":null}""",
        """{"Name":"ignored","Next":null,"$ref":"1"}""");

    public static TheoryData<bool, string> TypeDiscriminatorCases => ForBothConverters(
        """{"$type":"derived","Name":"first","Extra":42}""",
        """{"Name":"first","$type":"derived","Extra":42}""",
        """{"Name":"first","Extra":42,"$type":"derived"}""");

    public static TheoryData<bool, string> CollectionCases => ForBothConverters(
        """{"$id":"1","$values":[1,2]}""",
        """{"$values":[1,2],"$id":"1"}""");

    public static TheoryData<bool, string> IdAndTypeDiscriminatorCases => ForBothConverters(
        """{"$id":"1","$type":"derived","Name":"first","Extra":42}""",
        """{"$type":"derived","$id":"1","Name":"first","Extra":42}""",
        """{"$id":"1","Name":"first","$type":"derived","Extra":42}""",
        """{"$type":"derived","Name":"first","$id":"1","Extra":42}""",
        """{"Name":"first","$id":"1","$type":"derived","Extra":42}""",
        """{"Name":"first","$type":"derived","$id":"1","Extra":42}""");

    [Theory]
    [MemberData(nameof(MutableObjectIdCases))]
    public void MutableObjectIdPreservesSubsequentReference(bool dynamicConverters, string first)
    {
        var result = DeserializePair<Node>(first, """{"$ref":"1"}""", dynamicConverters);

        Assert.NotNull(result.First);
        Assert.Equal("first", result.First.Name);
        Assert.Same(result.First, result.Second);
    }

    [Theory]
    [MemberData(nameof(SelfReferenceCases))]
    public void MutableObjectIdPreservesSelfReference(bool dynamicConverters, string json)
    {
        var result = JsonSerializer.Deserialize<Node>(json, CreateSettings(dynamicConverters));

        Assert.NotNull(result);
        Assert.Equal("first", result.Name);
        Assert.Same(result, result.Next);
    }

    [Theory]
    [MemberData(nameof(ParameterizedObjectIdCases))]
    public void ParameterizedObjectIdPreservesReference(bool dynamicConverters, string first)
    {
        var result = DeserializePair<ConstructorNode>(first, """{"$ref":"1"}""", dynamicConverters);

        Assert.NotNull(result.First);
        Assert.Equal("first", result.First.Name);
        Assert.Same(result.First, result.Second);
    }

    [Theory]
    [MemberData(nameof(ReferenceCases))]
    public void RefResolvesAtEveryPosition(bool dynamicConverters, string second)
    {
        var result = DeserializePair<Node>("""{"$id":"1","Name":"first"}""", second, dynamicConverters);

        Assert.NotNull(result.First);
        Assert.Same(result.First, result.Second);
        Assert.Equal("first", result.Second!.Name);
    }

    [Theory]
    [MemberData(nameof(TypeDiscriminatorCases))]
    public void TypeRestoresDerivedPropertiesAtEveryPosition(bool dynamicConverters, string json)
    {
        var result = JsonSerializer.Deserialize<BaseNode>(json, CreateSettings(dynamicConverters));

        var derived = Assert.IsType<DerivedNode>(result);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
    }

    [Theory]
    [MemberData(nameof(CollectionCases))]
    public void CollectionValuesAndIdAreOrderIndependent(bool dynamicConverters, string first)
    {
        var result = DeserializePair<List<int>>(first, """{"$ref":"1"}""", dynamicConverters);

        Assert.NotNull(result.First);
        Assert.Equal(new[] { 1, 2 }, result.First);
        Assert.Same(result.First, result.Second);
    }

    [Theory]
    [MemberData(nameof(IdAndTypeDiscriminatorCases))]
    public void IdAndTypeCombinedPreserveReference(bool dynamicConverters, string first)
    {
        var result = DeserializePair<BaseNode>(first, """{"$ref":"1"}""", dynamicConverters);

        var derived = Assert.IsType<DerivedNode>(result.First);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
        Assert.Same(result.First, result.Second);
    }

    [Theory]
    [MemberData(nameof(IdAndTypeDiscriminatorCases))]
    public void IdAndTypeCombinedPreserveSelfReference(bool dynamicConverters, string first)
    {
        var json = "{\"Next\":{\"$ref\":\"1\"}," + first[1..];

        var result = JsonSerializer.Deserialize<BaseNode>(json, CreateSettings(dynamicConverters));

        var derived = Assert.IsType<DerivedNode>(result);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
        Assert.Same(derived, derived.Next);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LateIdPreservesExistingInstance(bool dynamicConverters)
    {
        var existing = new Node();

        var result = JsonSerializer.DeserializeTo(
            """{"Next":{"$ref":"1"},"Name":"first","$id":"1"}""", existing,
            CreateSettings(dynamicConverters));

        Assert.Same(existing, result);
        Assert.Equal("first", result.Name);
        Assert.Same(result, result.Next);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReferenceLookaheadSupportsMaterializedObjects(bool dynamicConverters)
    {
        using var document = JsonDocument.Parse(
            """{"First":{"Next":{"$ref":"1"},"Name":"first","$id":"1"},"Second":{"$ref":"1"}}""",
            CreateSettings(dynamicConverters));
        document.RootElement.AsObject();
        document.RootElement.GetProperty("First").AsObject();

        var result = document.RootElement.To<Holder<Node>>();

        Assert.NotNull(result);
        Assert.NotNull(result.First);
        Assert.Same(result.First, result.First.Next);
        Assert.Same(result.First, result.Second);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PropertiesNamedLikeReferenceMetadataKeepTheirValues(bool dynamicConverters)
    {
        var result = JsonSerializer.Deserialize<MetadataNamedProperties>(
            """{"Name":"first","$id":"literal-id","$ref":"literal-ref"}""", CreateSettings(dynamicConverters));

        Assert.NotNull(result);
        Assert.Equal("first", result.Name);
        Assert.Equal("literal-id", result.IdValue);
        Assert.Equal("literal-ref", result.RefValue);
    }

    private static TheoryData<bool, string> ForBothConverters(params string[] jsonCases)
    {
        var data = new TheoryData<bool, string>();
        foreach (var json in jsonCases)
        {
            data.Add(true, json);
            data.Add(false, json);
        }

        return data;
    }

    private static PolymorphicSettings CreateSettings(bool dynamicConverters)
    {
        return new PolymorphicSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            PreserveReferencesHandling = PreserveReferencesHandling.All
        };
    }

    private static Holder<T> DeserializePair<T>(string first, string second, bool dynamicConverters) where T : class
    {
        var json = $$"""{"First":{{first}},"Second":{{second}}}""";
        var result = JsonSerializer.Deserialize<Holder<T>>(json, CreateSettings(dynamicConverters));
        Assert.NotNull(result);
        return result;
    }

    public sealed class Holder<T> where T : class
    {
        public T? First { get; set; }

        public T? Second { get; set; }
    }

    public sealed class Node
    {
        public string? Name { get; set; }

        public Node? Next { get; set; }
    }

    public sealed record ConstructorNode(string Name);

    public class BaseNode
    {
        public string? Name { get; set; }

        public BaseNode? Next { get; set; }
    }

    public sealed class MetadataNamedProperties
    {
        public string? Name { get; set; }

        [DataProperty("$id")] public string? IdValue { get; set; }

        [DataProperty("$ref")] public string? RefValue { get; set; }
    }

    public sealed class DerivedNode : BaseNode
    {
        public int Extra { get; set; }
    }

    private sealed class PolymorphicSettings : DoxSerializerSettings
    {
        protected override DataContract ResolveContract(Type type)
        {
            var contract = base.ResolveContract(type);
            if (type == typeof(DerivedNode))
            {
                return contract with { TypeDiscriminator = new TypeDiscriminator(new Utf8Symbol("derived")) };
            }

            if (type == typeof(BaseNode))
            {
                return contract with { IsPolymorphic = true, DerivedTypes = [GetContract(typeof(DerivedNode))] };
            }

            return contract;
        }
    }
}