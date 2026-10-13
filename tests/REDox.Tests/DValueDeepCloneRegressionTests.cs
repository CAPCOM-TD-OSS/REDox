// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class DValueDeepCloneRegressionTests
{
    private static readonly string[] s_scalars =
    [
        "null", "bool", "char", "byte", "sbyte", "short", "ushort", "int", "uint",
        "long", "ulong", "half", "float", "double", "decimal", "dateTime", "dateTimeOffset",
        "string", "symbol", "guid", "bytes", "boxedInt", "boxedBytes", "bigNumber", "undefined"
    ];

    public static TheoryData<string> Scalars()
    {
        var data = new TheoryData<string>();
        foreach (var scalar in s_scalars)
        {
            data.Add(scalar);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scalars))]
    public void CloneInitiallyPreservesScalarValueAndVariant(string kind)
    {
        var source = CreateScalar(kind);
        var clone = source.DeepClone();

        Assert.True(clone.IsValid);
        Assert.Equal(source.GetToken().Variant, clone.GetToken().Variant);
        Assert.Equal(DoxSerializer.Serialize(source), DoxSerializer.Serialize(clone));
    }

    [Theory]
    [MemberData(nameof(Scalars))]
    public void DocumentScalarCloneSurvivesSourceDisposal(string kind)
    {
        var source = DoxNodeDocument.Create(CreateScalar(kind)).RootElement.AsValue();
        var variant = source.GetToken().Variant;
        var expected = DoxSerializer.Serialize(source);
        var clone = source.DeepClone();

        source.AsElement().Document.Dispose();

        Assert.True(clone.IsValid);
        Assert.Equal(variant, clone.GetToken().Variant);
        Assert.Equal(expected, DoxSerializer.Serialize(clone));
    }

    [Fact]
    public void DocumentScalarCloneRetainsValueWhenSourceIsReplaced()
    {
        var source = DValue.From(42);
        var clone = source.DeepClone();

        source.ReplaceWith(99);

        Assert.Equal(42, (int)clone);
    }

    [Fact]
    public void ReplacingDocumentScalarCloneDoesNotChangeSource()
    {
        var source = DValue.From(42);
        var clone = source.DeepClone();

        clone.ReplaceWith(99);

        Assert.Equal(42, (int)source);
    }

    [Fact]
    public void ObjectPropertyScalarCloneRetainsValueWhenSourceIsReplaced()
    {
        var obj = new DObject { { "n", 42 } };
        var clone = obj["n"].DeepClone();

        obj["n"] = 99;

        Assert.Equal(42, (int)clone);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("object")]
    [InlineData("mapValue")]
    [InlineData("mapKey")]
    [InlineData("mapEnumeratedValue")]
    [InlineData("element")]
    public void ContainerScalarCloneSurvivesSourceRemoval(string kind)
    {
        DValue source;
        Action remove;
        switch (kind)
        {
            case "array":
                var array = new DArray { 42 };
                source = array[0];
                remove = () => array.RemoveAt(0);
                break;
            case "object":
                var obj = new DObject { { "n", 42 } };
                source = obj["n"];
                remove = () => obj.Remove("n");
                break;
            case "element":
                var parent = new DArray { 42 };
                source = parent[0].AsElement().AsValue();
                remove = () => parent.RemoveAt(0);
                break;
            default:
                var map = new DMap { { 42, 42 } };
                if (kind == "mapEnumeratedValue")
                {
                    using var enumerator = map.GetEnumerator();
                    Assert.True(enumerator.MoveNext());
                    source = enumerator.Current.Value;
                }
                else
                {
                    source = kind == "mapKey" ? map[0].Key : map[0].Value;
                }

                remove = () => map.RemoveAt(0);
                break;
        }

        var clone = source.DeepClone();

        remove();

        Assert.True(clone.IsValid);
        Assert.Equal(42, (int)clone);
    }

    [Theory]
    [InlineData(ByteStringKind.Default)]
    [InlineData(ByteStringKind.Base64)]
    [InlineData(ByteStringKind.Base64Url)]
    [InlineData(ByteStringKind.Base16)]
    [InlineData(ByteStringKind.Guid)]
    [InlineData(ByteStringKind.Raw)]
    [InlineData(ByteStringKind.Inherit)]
    public void StandaloneByteStringCloneSharesValueBuffer(ByteStringKind kind)
    {
        var bytes = new byte[16];
        bytes[0] = 42;
        var source = DValue.Create(bytes, kind);
        var clone = source.DeepClone();

        bytes[0] = 99;

        Assert.Equal(source.GetToken().Variant, clone.GetToken().Variant);
        Assert.Equal(99, ((ReadOnlySpan<byte>)clone)[0]);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("array")]
    [InlineData("object")]
    public void DocumentByteStringCloneDoesNotShareSourceBuffer(string kind)
    {
        byte[] bytes = [42];
        var value = DValue.Create(bytes, ByteStringKind.Raw);
        var source = kind switch
        {
            "array" => new DArray { value }[0],
            "object" => new DObject { { "bytes", value } }["bytes"],
            _ => DoxNodeDocument.Create(value).RootElement.AsValue()
        };
        var clone = source.DeepClone();

        bytes[0] = 99;
        source.AsElement().Document.Dispose();

        Assert.True(clone.IsValid);
        Assert.Equal(DTokenVariant.ByteStringRaw, clone.GetToken().Variant);
        Assert.Equal(42, ((ReadOnlySpan<byte>)clone)[0]);
    }

    public static TheoryData<BigNumberKind, bool> BigNumbers()
    {
        var data = new TheoryData<BigNumberKind, bool>();
        foreach (var kind in new[]
                 {
                     BigNumberKind.Default, BigNumberKind.Integer, BigNumberKind.Decimal,
                     BigNumberKind.Float, BigNumberKind.Int128, BigNumberKind.UInt128, BigNumberKind.Hexadecimal
                 })
        {
            data.Add(kind, false);
            data.Add(kind, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BigNumbers))]
    public void BigNumberClonePreservesVariantAndDocumentIndependence(BigNumberKind kind, bool documentBacked)
    {
        var bytes = "123456789012345678901234567890"u8.ToArray();
        var value = new DValue { Instance = bytes, Payload = (long)DToken.Make(kind.ToVariant(), 0) };
        var source = documentBacked ? DoxNodeDocument.Create(value).RootElement.AsValue() : value;
        var clone = source.DeepClone();

        bytes[0] = (byte)'9';
        if (documentBacked)
        {
            source.AsElement().Document.Dispose();
        }

        Assert.True(clone.IsValid);
        Assert.Equal(kind.ToVariant(), clone.GetToken().Variant);
        var expected = documentBacked ? "123456789012345678901234567890"u8.ToArray() : bytes;
        Assert.Equal(expected, ((ReadOnlySpan<byte>)clone).ToArray());
    }

    [Theory]
    [InlineData("byteArray")]
    [InlineData("memory")]
    [InlineData("readOnlyMemory")]
    public void BoxedStandaloneByteStringCloneSharesValueBuffer(string kind)
    {
        byte[] bytes = [42];
        object buffer = kind switch
        {
            "memory" => bytes.AsMemory(),
            "readOnlyMemory" => (ReadOnlyMemory<byte>)bytes,
            _ => bytes
        };
        var source = DValue.Create(buffer);
        Assert.Equal(DTokenKind.ByteString, source.GetToken().Kind);
        var clone = source.DeepClone();

        bytes[0] = 99;

        Assert.Equal(99, ((ReadOnlySpan<byte>)clone)[0]);
    }

    [Fact]
    public void StandaloneReplaceWithScalarValueDoesNotShareSourceDocument()
    {
        var source = DValue.From(42);
        var result = DValue.Null.ReplaceWith(source);

        source.ReplaceWith(99);

        Assert.Equal(42, (int)result);
    }

    [Fact]
    public void DocumentScalarClonePreservesSerializerSettings()
    {
        var settings = new DoxSerializerSettings { AllowRelaxedScalarConversion = false };
        var source = DValue.From(42, settings);
        var clone = source.DeepClone();

        source.AsElement().Document.Dispose();

        Assert.Same(settings, clone.AsElement().Document.Settings);
        Assert.Equal(42, (int)clone);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("list")]
    [InlineData("dictionary")]
    public void BoxedContainerCloneIsIndependentOfOriginalCollection(string kind)
    {
        int[] array = [42];
        var list = new List<int> { 42 };
        var dictionary = new Dictionary<string, int> { ["n"] = 42 };
        object value = kind switch
        {
            "list" => list,
            "dictionary" => dictionary,
            _ => array
        };
        var clone = DValue.Create(value).DeepClone();

        array[0] = 99;
        list[0] = 99;
        dictionary["n"] = 99;

        Assert.True(clone.IsValid);
        Assert.Equal(42, (int)(kind == "dictionary" ? clone["n"] : clone[0]));
    }

    [Theory]
    [InlineData("ulong")]
    [InlineData("undefined")]
    public void ObjectClonePreservesUnsignedIntegerAndNullVariant(string kind)
    {
        var value = CreateScalar(kind);
        var source = new DObject { { "n", value } };
        var variant = source["n"].GetToken().Variant;
        var expected = DoxSerializer.Serialize(source["n"]);
        var clone = source.DeepClone();

        source.Document.Dispose();

        Assert.Equal(variant, clone["n"].GetToken().Variant);
        Assert.Equal(expected, DoxSerializer.Serialize(clone["n"]));
    }

    [Fact]
    public void ObjectClonePreservesNestedScalarValuesAfterSourceEditsAndDisposal()
    {
        var source = new DObject { { "n", 42 }, { "bytes", DValue.Create(new byte[] { 42 }, ByteStringKind.Raw) } };
        var clone = source.AsValue().DeepClone();

        source["n"] = 99;
        source.Remove("bytes");
        source.Document.Dispose();

        Assert.True(clone.IsValid);
        Assert.Equal(42, (int)clone["n"]);
        Assert.Equal(42, ((ReadOnlySpan<byte>)clone["bytes"])[0]);
    }

    private static DValue CreateScalar(string kind)
    {
        return kind switch
        {
            "null" => DValue.Null,
            "bool" => DValue.Create(true),
            "char" => DValue.Create('x'),
            "byte" => DValue.Create(byte.MaxValue),
            "sbyte" => DValue.Create(sbyte.MinValue),
            "short" => DValue.Create(short.MinValue),
            "ushort" => DValue.Create(ushort.MaxValue),
            "int" => DValue.Create(int.MinValue),
            "uint" => DValue.Create(uint.MaxValue),
            "long" => DValue.Create(long.MinValue),
            "ulong" => DValue.Create(ulong.MaxValue),
            "half" => DValue.Create((Half)1.5),
            "float" => DValue.Create(1.5f),
            "double" => DValue.Create(1.5),
            "decimal" => DValue.Create(1234567890.1234567890123456789m),
            "dateTime" => DValue.Create(new DateTime(2026, 10, 12, 1, 2, 3, DateTimeKind.Utc)),
            "dateTimeOffset" => DValue.Create(new DateTimeOffset(2026, 10, 12, 1, 2, 3, TimeSpan.FromHours(9))),
            "string" => DValue.Create("hello", StringKind.SingleQuote),
            "symbol" => DValue.Create(new Utf8Symbol("hello"), SymbolKind.Identifier),
            "guid" => DValue.Create(Guid.Parse("d69c667b-2a6e-482b-91f4-45ff02fa0310")),
            "bytes" => DValue.Create(new byte[] { 42 }, ByteStringKind.Raw),
            "boxedInt" => DValue.Create((object)42),
            "boxedBytes" => DValue.Create(new byte[] { 42 }),
            "bigNumber" => Json5Document.Parse("1234567890123456789012345678901234567890").RootElement.AsValue(),
            "undefined" => new DValue { Instance = DTokenVariant.NullUndefined },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}