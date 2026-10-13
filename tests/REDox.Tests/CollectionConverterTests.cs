// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class CollectionConverterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConcreteListsKeepTheirTargetType(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = new ArrayList { "first", true, null };
        var derived = new DerivedList { "first", true, null };

        Assert.Equal(source.Cast<object?>(), DoxSerializer.DeepCopy(source, settings).Cast<object?>());
        var copy = DoxSerializer.DeepCopy(derived, settings);
        Assert.IsType<DerivedList>(copy);
        Assert.Equal(derived.Cast<object?>(), copy.Cast<object?>());
        Assert.Equal(typeof(DerivedList), settings.GetConverter(typeof(DerivedList)).TargetType);
    }

    [Theory]
    [InlineData(true, "list")]
    [InlineData(false, "list")]
    [InlineData(true, "collection")]
    [InlineData(false, "collection")]
    [InlineData(true, "enumerable")]
    [InlineData(false, "enumerable")]
    public void InterfacesKeepTheirDefaultImplementations(bool dynamicConverters, string kind)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var type = kind switch
        {
            "list" => typeof(IList),
            "collection" => typeof(ICollection),
            _ => typeof(IEnumerable)
        };

        var result = JsonSerializer.Deserialize("[\"first\",true,null]", type, settings);

        Assert.NotNull(result);
        if (kind == "list")
        {
            Assert.IsType<ArrayList>(result);
        }
        else
        {
            Assert.IsType<object[]>(result);
        }

        Assert.Equal(new object?[] { "first", true, null }, ((IEnumerable)result).Cast<object?>());
    }

    [Theory]
    [InlineData(true, DictionaryFormatHandling.Object)]
    [InlineData(false, DictionaryFormatHandling.Object)]
    [InlineData(true, DictionaryFormatHandling.Map)]
    [InlineData(false, DictionaryFormatHandling.Map)]
    [InlineData(true, DictionaryFormatHandling.KeyValuePair)]
    [InlineData(false, DictionaryFormatHandling.KeyValuePair)]
    public void DictionariesKeepTheirTypeAndFormat(bool dynamicConverters, DictionaryFormatHandling format)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            DictionaryFormatHandling = format
        };
        var source = new DerivedDictionary { ["first"] = "value", ["second"] = true, ["third"] = null };

        var copy = DoxSerializer.DeepCopy(source, settings);

        Assert.IsType<DerivedDictionary>(copy);
        Assert.Equal(source.Count, copy.Count);
        foreach (DictionaryEntry entry in source)
        {
            Assert.Equal(entry.Value, copy[entry.Key]);
        }

        Assert.Equal(typeof(DerivedDictionary), settings.GetConverter(typeof(DerivedDictionary)).TargetType);
    }

    [Theory]
    [InlineData(true, UnknownObjectTypeHandling.Default)]
    [InlineData(false, UnknownObjectTypeHandling.Default)]
    [InlineData(true, UnknownObjectTypeHandling.Element)]
    [InlineData(false, UnknownObjectTypeHandling.Element)]
    public void DictionaryInterfacesKeepTheirDefaultImplementations(bool dynamicConverters,
        UnknownObjectTypeHandling handling)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            UnknownObjectTypeHandling = handling
        };

        var result = JsonSerializer.Deserialize<IDictionary>("{\"first\":true}", settings);

        Assert.NotNull(result);
        if (handling == UnknownObjectTypeHandling.Element)
        {
            Assert.IsType<Dictionary<string, object>>(result);
        }
        else
        {
            Assert.IsType<Hashtable>(result);
        }

        Assert.True(result.Contains("first"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingListsArePopulated(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var existing = new DerivedList { "existing" };

        var result = JsonSerializer.DeserializeTo("[\"added\"]", existing, settings);

        Assert.Same(existing, result);
        Assert.Equal(new[] { "existing", "added" }, result.Cast<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReferenceMetadataKeepsTheConcreteListType(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            PreserveReferencesHandling = PreserveReferencesHandling.Arrays
        };
        var source = new DerivedList { "first", "second" };

        var copy = DoxSerializer.DeepCopy(source, settings);

        Assert.IsType<DerivedList>(copy);
        Assert.Equal(source.Cast<string>(), copy.Cast<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonPublicParameterlessConstructorsRemainSupported(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var source = PrivateConstructorList.Create();
        source.Add("first");

        var copy = DoxSerializer.DeepCopy(source, settings);

        Assert.IsType<PrivateConstructorList>(copy);
        Assert.Equal(source.Cast<string>(), copy.Cast<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuiltInCollectionsReadWithoutDynamicGeneration(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };

        Assert.Equal(new[] { "first", "second" },
            JsonSerializer.Deserialize<Queue>("[\"first\",\"second\"]", settings)!.Cast<string>());
        Assert.Equal(new[] { "second", "first" },
            JsonSerializer.Deserialize<Stack>("[\"first\",\"second\"]", settings)!.Cast<string>());
        Assert.Equal(new[] { true, false, true },
            JsonSerializer.Deserialize<BitArray>("[true,false,true]", settings)!.Cast<bool>());
    }

    public sealed class DerivedList : ArrayList
    {
    }

    public sealed class DerivedDictionary : Hashtable
    {
    }

    public sealed class PrivateConstructorList : ArrayList
    {
        private PrivateConstructorList()
        {
        }

        public static PrivateConstructorList Create()
        {
            return new PrivateConstructorList();
        }
    }
}