// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using REDox.Json;
using REDox.Serialization;
using REDox.Serialization.Metadata;

namespace REDox.Tests;

public sealed class ObjectConverterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassesUseParameterizedConstructors(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = new ClassModel(7, true);

        Assert.Equal(source, DoxSerializer.DeepCopy(source, settings));
        Assert.Equal(new ClassModel(7, false),
            JsonSerializer.Deserialize<ClassModel>("{\"Id\":7}", settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassesWithoutSelectedConstructorAreCreatedUninitialized(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };

        var result = JsonSerializer.Deserialize<NoSelectedConstructor>("{\"Id\":7}", settings);

        Assert.NotNull(result);
        Assert.Equal(7, result.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructsAndNullableStructsRoundTrip(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = new StructModel(7, true);

        Assert.Equal(source, DoxSerializer.DeepCopy(source, settings));
        Assert.Equal(source, DoxSerializer.DeepCopy<StructModel?>(source, settings));
        Assert.Null(JsonSerializer.Deserialize<StructModel?>("null", settings));
        Assert.Equal(new StructModel(7, false),
            JsonSerializer.Deserialize<StructModel>("{\"Id\":7}", settings));
    }

    [Fact]
    public void DictionaryKeysUseParseMethodWithReflectionConverter()
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = false };
        var source = new Dictionary<DictionaryKey, int> { [new DictionaryKey(7)] = 42 };

        Assert.Equal(source, DoxSerializer.DeepCopy(source, settings));
    }

    [Fact]
    public void ReflectionConverterReadsNullStructAsDefaultValue()
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = false };

        Assert.Equal(default, JsonSerializer.Deserialize<StructModel>("null", settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingMembersArePopulatedWithDefaultValues(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            DefaultValueHandling = DefaultValueHandling.Populate
        };

        var result = JsonSerializer.Deserialize<InitializedModel>("{}", settings);

        Assert.NotNull(result);
        Assert.Equal(0, result.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtensionDataUsesDefaultDictionaryImplementation(bool dynamicConverters)
    {
        var settings = new ExtensionSettings { AllowDynamicGenericConverters = dynamicConverters };

        var result = JsonSerializer.Deserialize<ExtensionModel>("{\"extra\":42}", settings);

        Assert.NotNull(result);
        Assert.NotNull(result.Extra);
        Assert.Equal(42, result.Extra["extra"]);
        Assert.Equal(result.Extra, DoxSerializer.DeepCopy(result, settings).Extra);
    }

    public sealed record ClassModel(int Id, bool Flag);

    public readonly record struct StructModel(int Id, bool Flag);

    public sealed class NoSelectedConstructor
    {
        public NoSelectedConstructor(int unused)
        {
            throw new InvalidOperationException("Constructor must not run.");
        }

        public NoSelectedConstructor(string unused)
        {
            throw new InvalidOperationException("Constructor must not run.");
        }

        public int Id { get; set; }
    }

    public sealed record DictionaryKey(int Value)
    {
        public static DictionaryKey Parse(string value)
        {
            return new DictionaryKey(int.Parse(value));
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }

    public sealed class InitializedModel
    {
        public int Id { get; set; } = 123;
    }

    public sealed class ExtensionModel
    {
        public IDictionary<string, int>? Extra { get; set; }
    }

    private sealed class ExtensionSettings : DoxSerializerSettings
    {
        protected override DataContract ResolveContract(Type type)
        {
            var contract = base.ResolveContract(type);
            return type == typeof(ExtensionModel)
                ? contract with { Properties = [], ExtensionData = contract.Properties[0] }
                : contract;
        }
    }
}