// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class DElementCloneRegressionTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[[],{},[],{}]")]
    public void ClonePreservesEmptyContainers(string json)
    {
        using var source = JsonDocument.Parse(json);
        var clone = source.RootElement.Clone();
        using var document = clone.Document;
        source.Dispose();

        Assert.Equal(json, JsonDocument.EncodeToString(clone));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClonePreservesSubtreeAfterSourceEditsAndDisposal(bool edited)
    {
        using var source = Json5Document.Parse("{before:0,target:[1,{a:[2,3],b:[]},4],after:5}");
        var element = source.RootElement.GetProperty("target");
        if (edited)
        {
            var array = element.AsArray();
            array.RemoveAt(0);
            array.Insert(0, 10);
            array[1].AsObject().Remove("b");
            array[1].AsObject().Add("c", new DArray { 6, 7 });
            element = source.RootElement.GetProperty("target");
        }

        var expected = JsonDocument.EncodeToString(element);
        var version = source.Version;
        var tokens = source.GetTokens().ToArray();
        var clone = element.Clone();
        using var clonedDocument = clone.Document;

        Assert.Equal(version, source.Version);
        Assert.Equal(tokens, source.GetTokens().ToArray());
        source.RootElement.AsObject().Remove("target");
        source.Dispose();

        Assert.Equal(expected, JsonDocument.EncodeToString(clone));
        Assert.Equal("$[1].a[1]", clone[1].GetProperty("a")[1].AsValue().GetPath());
        clone.AsArray().Add(99);
        clone[1].GetProperty("a").AsArray().RemoveAt(0);
        clone = clonedDocument.RootElement;
        Assert.Equal(3, clone[1].GetProperty("a")[0].GetInt32());
        Assert.Equal(99, clone[3].GetInt32());
    }

    [Fact]
    public void ClonePreservesContainerKeysAndDuplicateKeysInOrder()
    {
        var source = new DMap
        {
            { new DArray { 1, 2 }, new DObject { { "a", 3 } } },
            { "same", new DArray() },
            { "same", new DMap() }
        };
        using var document = source.Document;
        var clone = source.AsElement().Clone();
        using var clonedDocument = clone.Document;
        source.Clear();
        document.Dispose();

        var entries = clone.AsMap();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, (int)entries[0].Key[1]);
        Assert.Equal(3, (int)entries[0].Value["a"]);
        Assert.Equal("same", (string?)entries[1].Key);
        Assert.Equal("same", (string?)entries[2].Key);
        Assert.Empty(entries[1].Value.AsArray());
        Assert.Empty(entries[2].Value.AsMap());
        entries.Add(new DArray { 4 }, 5);
        Assert.Equal(5, (int)entries[3].Value);
    }

    [Fact]
    public void CloneCopiesBinaryPayloadsAndPreservesScalarKinds()
    {
        byte[] bytes = [1, 2, 3];
        var bigNumber = "123456789012345678901234567890"u8.ToArray();
        var offset = new DateTimeOffset(2026, 10, 12, 1, 2, 3, TimeSpan.FromHours(9));
        var source = new DArray
        {
            DValue.Create(bytes, ByteStringKind.Raw),
            new DValue { Instance = bigNumber, Payload = (long)DToken.Make(DTokenVariant.BigNumberInteger, 0) },
            DValue.Create(ulong.MaxValue),
            DValue.Create(1234567890.1234567890123456789m),
            DValue.Create("quoted", StringKind.SingleQuote),
            DValue.Create(new Utf8Symbol("symbol"), SymbolKind.Reference),
            DValue.Create(offset),
            new DValue { Instance = DTokenVariant.NullUndefined }
        };
        using var document = source.Document;
        var clone = source.AsElement().Clone();
        using var clonedDocument = clone.Document;

        Assert.NotSame(document.GetExtends()[(int)source[0].GetToken().ExtendId],
            clonedDocument.GetExtends()[(int)clone[0].Token.ExtendId]);
        bytes[0] = 99;
        bigNumber[0] = (byte)'9';
        document.Dispose();

        Assert.Equal(new byte[] { 1, 2, 3 }, clone[0].GetByteString().ToArray());
        Assert.Equal("123456789012345678901234567890"u8.ToArray(), clone[1].GetBigNumber().ToArray());
        Assert.Equal(ByteStringKind.Raw, clone[0].Token.ByteStringKind);
        Assert.Equal(BigNumberKind.Integer, clone[1].Token.BigNumberKind);
        Assert.Equal(ulong.MaxValue, clone[2].GetUInt64());
        Assert.Equal(1234567890.1234567890123456789m, clone[3].GetDecimal());
        Assert.Equal(StringKind.SingleQuote, clone[4].Token.StringKind);
        Assert.Equal(SymbolKind.Reference, clone[5].Token.SymbolKind);
        Assert.Equal(offset.Offset, clone[6].GetDateTimeOffset().Offset);
        Assert.Equal(DTokenVariant.NullUndefined, clone[7].Token.Variant);
    }

    [Fact]
    public void CloneDecodesDoxPayloadsIndependentlyOfSourceBuffer()
    {
        var date = new DateTime(2026, 10, 12, 1, 2, 3, DateTimeKind.Utc);
        var offset = new DateTimeOffset(date).ToOffset(TimeSpan.FromHours(9));
        var array = new DArray
        {
            long.MinValue, ulong.MaxValue, 1.23456789, 1e100, (Half)1.5,
            1234567890.1234567890123456789m, "text", date, offset,
            DValue.Create(new byte[] { 1, 2, 3 }, ByteStringKind.Raw), true, DValue.Null
        };
        using var arrayDocument = array.Document;
        var bytes = DoxDocument.Encode(array.AsElement());
        using var source = DoxDocument.Parse(bytes);
        var clone = source.RootElement.Clone();
        using var document = clone.Document;
        source.Dispose();
        bytes.AsSpan().Clear();

        Assert.Equal(long.MinValue, clone[0].GetInt64());
        Assert.Equal(ulong.MaxValue, clone[1].GetUInt64());
        Assert.Equal(1.23456789, clone[2].GetDouble());
        Assert.Equal(1e100, clone[3].GetDouble());
        Assert.Equal(1.5f, clone[4].GetSingle());
        Assert.Equal(1234567890.1234567890123456789m, clone[5].GetDecimal());
        Assert.Equal("text", clone[6].GetString());
        Assert.Equal(date, clone[7].GetDateTime());
        Assert.Equal(offset.Offset, clone[8].GetDateTimeOffset().Offset);
        Assert.Equal(new byte[] { 1, 2, 3 }, clone[9].GetByteString().ToArray());
        Assert.True(clone[10].GetBoolean());
        Assert.Equal(DTokenKind.Null, clone[11].Token.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloneOmitsParsedAndEditedTrivia(bool edited)
    {
        using var source = Json5Document.Parse("/*head*/[/*one*/true,/*two*/{a:1}]/*tail*/",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        if (edited)
        {
            source.RootElement.AsArray();
            source.RootElement[0].AsValue().LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
            source.RootElement[1].AsValue().LeadingTrivia.AddLast("added", TriviaKind.BlockComment);
        }

        var clone = source.RootElement.Clone();
        using var document = clone.Document;
        source.Dispose();

        Assert.Empty(clone.EnumerateTrivia());
        Assert.Empty(clone[0].EnumerateTrivia());
        Assert.Empty(clone[1].EnumerateTrivia());
        Assert.Equal("[true,{\"a\":1}]", JsonDocument.EncodeToString(clone));
    }

    [Fact]
    public void CloneSupportsDeepContainersAndPreservesSettings()
    {
        var settings = new DoxSerializerSettings();
        var source = new DArray(settings);
        using var document = source.Document;
        var current = source;
        for (var i = 0; i < 10000; i++)
        {
            current = current.AddArray();
        }

        current.Add(42);

        var clone = source.AsElement().Clone();
        using var clonedDocument = clone.Document;
        document.Dispose();

        Assert.Same(settings, clonedDocument.Settings);
        for (var i = 0; i < 10001; i++)
        {
            Assert.Equal(1, clone.GetArrayLength());
            clone = clone.EnumerateArray().Single();
        }

        Assert.Equal(42, clone.GetInt32());
    }
}