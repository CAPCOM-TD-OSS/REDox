// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using REDox.Json;
using REDox.Serialization.Converters;

namespace REDox.Tests;

public sealed class EnumConverterTests
{
    [Flags]
    public enum Access : ulong
    {
        [EnumMember(Value = "read")] Read = 1,
        [EnumMember(Value = "write")] Write = 2
    }

    public enum ByteKind : byte
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum Int16Kind : short
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum Int32Kind
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum Int64Kind : long
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum SByteKind : sbyte
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum UInt16Kind : ushort
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum UInt32Kind : uint
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public enum UInt64Kind : ulong
    {
        [EnumMember(Value = "named")] Value = 42
    }

    public static IEnumerable<object[]> IntegralCases()
    {
        object[] values =
        [
            SByteKind.Value, ByteKind.Value, Int16Kind.Value, UInt16Kind.Value,
            Int32Kind.Value, UInt32Kind.Value, Int64Kind.Value, UInt64Kind.Value
        ];
        foreach (var value in values)
        {
            yield return [value.GetType(), value, true];
            yield return [value.GetType(), value, false];
        }
    }

    [Theory]
    [MemberData(nameof(IntegralCases))]
    public void EnumMembersRoundTripWithEachUnderlyingType(Type type, object value, bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings { AllowDynamicGenericConverters = dynamicConverters };
        var stringSettings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            Converters = [new StringEnumConverter()]
        };

        Assert.Equal("42", JsonSerializer.Serialize(value, type, settings));
        Assert.Equal(value, JsonSerializer.Deserialize("42", type, settings));
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(type, typeof(int));
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        dictionary[value] = 7;

        foreach (var keySettings in new[] { settings, stringSettings })
        {
            var json = JsonSerializer.Serialize(dictionary, dictionaryType, keySettings);
            Assert.Equal("{\"named\":7}", json);
            var result = (IDictionary)JsonSerializer.Deserialize(json, dictionaryType, keySettings)!;
            Assert.Equal(7, result[value]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FlagNamesUseEnumMemberAttributes(bool dynamicConverters)
    {
        var settings = new DoxSerializerSettings
        {
            AllowDynamicGenericConverters = dynamicConverters,
            Converters = [new StringEnumConverter()]
        };
        var value = Access.Read | Access.Write;

        Assert.Equal("\"read, write\"", JsonSerializer.Serialize(value, settings));
        Assert.Equal(value, JsonSerializer.Deserialize<Access>("\"read, write\"", settings));
    }
}