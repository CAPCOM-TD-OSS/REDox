// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using REDox.Json;
using REDox.Serialization;
using REDox.Serialization.Metadata;
using DataContract = REDox.Serialization.Metadata.DataContract;
using SerializationException = REDox.Serialization.SerializationException;

namespace REDox.Tests;

public sealed class PolymorphicReadTests
{
    public static TheoryData<bool, string> DiscriminatedCases => ForBothConverters(
        """{"$type":"derived","Name":"first","Extra":42}""",
        """{"Name":"first","$type":"derived","Extra":42}""",
        """{"Name":"first","Extra":42,"$type":"derived"}""");

    public static TheoryData<bool, string> UnresolvedCases => ForBothConverters(
        """{"Name":"first","Extra":42}""",
        """{"Name":"first","Extra":42,"$type":"unknown"}""",
        """{"$type":"unknown","Name":"first","Extra":42}""");

    [Theory]
    [MemberData(nameof(DiscriminatedCases))]
    public void AbstractTypesWaitForDiscriminator(bool dynamicConverters, string json)
    {
        var result = JsonSerializer.Deserialize<AbstractBase>(json, CreateSettings(dynamicConverters));

        var derived = Assert.IsType<AbstractDerived>(result);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
        Assert.Equal(1, derived.DeserializingCount);
        Assert.Equal(1, derived.DeserializedCount);
    }

    [Theory]
    [MemberData(nameof(DiscriminatedCases))]
    public void InterfacesWaitForDiscriminator(bool dynamicConverters, string json)
    {
        var result = JsonSerializer.Deserialize<IModel>(json, CreateSettings(dynamicConverters));

        var derived = Assert.IsType<Derived>(result);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
    }

    [Theory]
    [MemberData(nameof(DiscriminatedCases))]
    public void DisallowedMembersAreCheckedAgainstResolvedType(bool dynamicConverters, string json)
    {
        var result = JsonSerializer.Deserialize<Base>(json, CreateSettings(dynamicConverters));

        var derived = Assert.IsType<Derived>(result);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
    }

    [Theory]
    [MemberData(nameof(DiscriminatedCases))]
    public void ParameterizedConstructorsWaitForMemberValidation(bool dynamicConverters, string json)
    {
        var result = JsonSerializer.Deserialize<ConstructorBase>(json, CreateSettings(dynamicConverters));

        var derived = Assert.IsType<ConstructorDerived>(result);
        Assert.Equal("first", derived.Name);
        Assert.Equal(42, derived.Extra);
    }

    [Theory]
    [MemberData(nameof(UnresolvedCases))]
    public void UnresolvedAbstractTypesReturnNullWithoutCallbacks(bool dynamicConverters, string json)
    {
        var settings = CreateSettings(dynamicConverters, UnmappedMemberHandling.Skip);

        Assert.Null(JsonSerializer.Deserialize<AbstractBase>(json, settings));
        Assert.Null(JsonSerializer.Deserialize<IModel>(json, settings));
    }

    [Theory]
    [MemberData(nameof(UnresolvedCases))]
    public void UnresolvedTypesStillReportUnmappedMember(bool dynamicConverters, string json)
    {
        var settings = CreateSettings(dynamicConverters);

        AssertUnmappedMember(() => JsonSerializer.Deserialize<Base>(json, settings), "Extra");
        AssertUnmappedMember(() => JsonSerializer.Deserialize<AbstractBase>(json, settings), "Extra");
        AssertUnmappedMember(() => JsonSerializer.Deserialize<ConstructorBase>(json, settings), "Extra");
    }

    [Theory]
    [MemberData(nameof(DiscriminatedCases))]
    public void TrulyUnknownMembersAreRejectedAfterTypeResolution(bool dynamicConverters, string json)
    {
        var withUnknownMember = "{\"Unknown\":true," + json[1..];

        var error = Assert.Throws<SerializationException>(() =>
            JsonSerializer.Deserialize<Base>(withUnknownMember, CreateSettings(dynamicConverters)));

        Assert.Equal(SerializationError.UnmappedMember, error.Error);
        Assert.Equal("Unknown", error.Member);
        Assert.Equal(typeof(Derived), error.Contract!.Type);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandledUnmappedMembersAreAllReportedInOrder(bool dynamicConverters)
    {
        var errors = new List<string>();
        var settings = new PolymorphicSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            UnmappedMemberHandling = UnmappedMemberHandling.Disallow,
            Error = (_, args) =>
            {
                errors.Add(Assert.IsType<string>(args.Error.Member));
                args.Handled = true;
            }
        };
        const string json = """{"Unknown1":true,"Name":"first","$type":"unknown","Unknown2":false}""";

        var result = JsonSerializer.Deserialize<Base>(json, settings);

        Assert.IsType<Base>(result);
        Assert.Equal("first", result.Name);
        Assert.Equal(new[] { "Unknown1", "Unknown2" }, errors);

        errors.Clear();
        var constructed = JsonSerializer.Deserialize<ConstructorBase>(json, settings);

        Assert.IsType<ConstructorBase>(constructed);
        Assert.Equal("first", constructed.Name);
        Assert.Equal(new[] { "Unknown1", "Unknown2" }, errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonPolymorphicUnmappedMembersAreStillReportedImmediately(bool dynamicConverters)
    {
        var settings = CreateSettings(dynamicConverters);

        var error = Assert.Throws<SerializationException>(() =>
            JsonSerializer.Deserialize<PlainModel>("""{"Unknown":true,"Name":"first"}""", settings));

        Assert.Equal(SerializationError.UnmappedMember, error.Error);
        Assert.Null(error.Target);
    }

    private static void AssertUnmappedMember(Action action, string member)
    {
        var error = Assert.Throws<SerializationException>(action);
        Assert.Equal(SerializationError.UnmappedMember, error.Error);
        Assert.Equal(member, error.Member);
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

    private static PolymorphicSettings CreateSettings(bool dynamicConverters,
        UnmappedMemberHandling unmappedMemberHandling = UnmappedMemberHandling.Disallow)
    {
        return new PolymorphicSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            UnmappedMemberHandling = unmappedMemberHandling
        };
    }

    public interface IModel
    {
        string? Name { get; set; }
    }

    public class PlainModel
    {
        public string? Name { get; set; }
    }

    public class Base : PlainModel
    {
    }

    public sealed class Derived : Base, IModel
    {
        public int Extra { get; set; }
    }

    public abstract class AbstractBase : PlainModel
    {
        [DataIgnore] public int DeserializingCount { get; private set; }

        [DataIgnore] public int DeserializedCount { get; private set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            DeserializingCount++;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            DeserializedCount++;
        }
    }

    public sealed class AbstractDerived : AbstractBase
    {
        public int Extra { get; set; }
    }

    public class ConstructorBase
    {
        public ConstructorBase(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public sealed class ConstructorDerived : ConstructorBase
    {
        public ConstructorDerived(string name) : base(name)
        {
        }

        public int Extra { get; set; }
    }

    private sealed class PolymorphicSettings : DoxSerializerSettings
    {
        protected override DataContract ResolveContract(Type type)
        {
            var contract = base.ResolveContract(type) with { UnmappedMemberHandling = UnmappedMemberHandling };
            var derivedType = type == typeof(AbstractBase) ? typeof(AbstractDerived)
                : type == typeof(ConstructorBase) ? typeof(ConstructorDerived)
                : type == typeof(Base) || type == typeof(IModel) ? typeof(Derived)
                : null;
            if (derivedType != null)
            {
                return contract with { IsPolymorphic = true, DerivedTypes = [GetContract(derivedType)] };
            }

            if (type == typeof(Derived) || type == typeof(AbstractDerived) || type == typeof(ConstructorDerived))
            {
                return contract with
                {
                    IsPolymorphic = true,
                    TypeDiscriminator = new TypeDiscriminator(new Utf8Symbol("derived"))
                };
            }

            return contract;
        }
    }
}