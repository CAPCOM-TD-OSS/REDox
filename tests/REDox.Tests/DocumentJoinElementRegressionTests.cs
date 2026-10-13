// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class DocumentJoinElementRegressionTests
{
    [Theory]
    [InlineData("decimal")]
    [InlineData("double")]
    [InlineData("string")]
    [InlineData("symbol")]
    [InlineData("date")]
    [InlineData("offset")]
    public void JoiningExtendedScalarSharesImmutablePayloadAndPreservesDestinationTrivia(string kind)
    {
        var value = kind switch
        {
            "decimal" => DValue.Create(1234567890.1234567890123456789m),
            "double" => DValue.Create(1e100),
            "string" => DValue.Create("text", StringKind.SingleQuote),
            "symbol" => DValue.Create(new Utf8Symbol("symbol"), SymbolKind.Reference),
            "date" => DValue.Create(new DateTime(2026, 10, 12, 1, 2, 3, DateTimeKind.Utc)),
            "offset" => DValue.Create(new DateTimeOffset(2026, 10, 12, 1, 2, 3, TimeSpan.FromHours(9))),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var source = DoxNodeDocument.Create(value);
        using var destination = Json5Document.Parse("/*keep*/0/*tail*/",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var expected = DoxSerializer.Serialize(source.RootElement);
        var sourceToken = source.RootElement.Token;
        var payload = source.GetExtends()[(int)sourceToken.ExtendId];
        var originalId = destination.RootId;

        Assert.Equal(originalId, destination.JoinElement(originalId, source.RootElement));

        Assert.Equal(sourceToken.Variant, destination.RootElement.Token.Variant);
        Assert.Same(payload, destination.GetExtends()[(int)destination.RootElement.Token.ExtendId]);
        source.Dispose();
        Assert.Equal(expected, DoxSerializer.Serialize(destination.RootElement));
        Assert.Contains(destination.RootElement.EnumerateTrivia(), trivia => trivia.GetString() == "keep");
        Assert.Contains(destination.TrailingTrivia.GetEnumerator(), trivia => trivia.GetString() == "tail");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoiningBinaryScalarCopiesPayloadButSelfReplacementDoesNotAllocate(bool bigNumber)
    {
        var bytes = "123456789012345678901234567890"u8.ToArray();
        var value = bigNumber
            ? new DValue { Instance = bytes, Payload = (long)DToken.Make(DTokenVariant.BigNumberInteger, 0) }
            : DValue.Create(bytes, ByteStringKind.Raw);
        using var source = DoxNodeDocument.Create(value);
        using var destination = JsonDocument.Parse("0");
        var originalToken = source.RootElement.Token;
        var originalPayload = source.GetExtends()[(int)originalToken.ExtendId];

        source.JoinElement(source.RootId, source.RootElement);

        Assert.Equal(originalToken, source.RootElement.Token);
        Assert.Same(originalPayload, source.GetExtends()[(int)originalToken.ExtendId]);
        destination.JoinElement(destination.RootId, source.RootElement);
        Assert.NotSame(originalPayload, destination.GetExtends()[(int)destination.RootElement.Token.ExtendId]);
        bytes[0] = (byte)'9';
        source.Dispose();

        var result = bigNumber
            ? destination.RootElement.GetBigNumber()
            : destination.RootElement.GetByteString();
        Assert.Equal("123456789012345678901234567890"u8.ToArray(), result.ToArray());
        Assert.Equal(originalToken.Variant, destination.RootElement.Token.Variant);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingParsedChildPreservesKindsSiblingAndTrivia(bool materializeParent)
    {
        byte[] bytes = [1, 2, 3];
        var source = new DObject
        {
            { "hex", DValue.Create(42, IntegerKind.Hexadecimal) },
            { "quoted", DValue.Create("text", StringKind.SingleQuote) },
            { "unsigned", DValue.Create(ulong.MaxValue) },
            { "undefined", new DValue { Instance = DTokenVariant.NullUndefined } },
            { "bytes", DValue.Create(bytes, ByteStringKind.Raw) },
            { "nested", new DArray { new DArray { 7 }, new DObject { { "n", 8 } } } }
        };
        using var sourceDocument = source.Document;
        using var destination = Json5Document.Parse("[/*keep*/[0,1],/*sibling*/99]/*tail*/",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        if (materializeParent)
        {
            destination.RootElement.AsArray();
        }

        var target = destination.RootElement[0];
        destination.JoinElement(target.Id, source.AsElement());
        sourceDocument.Dispose();
        bytes[0] = 99;
        target = destination.RootElement[0];

        Assert.Equal(IntegerKind.Hexadecimal, target.GetProperty("hex").Token.IntegerKind);
        Assert.Equal(StringKind.SingleQuote, target.GetProperty("quoted").Token.StringKind);
        Assert.Equal(ulong.MaxValue, target.GetProperty("unsigned").GetUInt64());
        Assert.Equal(DTokenVariant.NullUndefined, target.GetProperty("undefined").Token.Variant);
        Assert.Equal(new byte[] { 1, 2, 3 }, target.GetProperty("bytes").GetByteString().ToArray());
        Assert.Equal("$[0].nested[1].n", target.GetProperty("nested")[1].GetProperty("n").AsValue().GetPath());
        Assert.Contains(target.EnumerateTrivia(), trivia => trivia.GetString() == "keep");
        Assert.Equal(99, destination.RootElement[1].GetInt32());
        Assert.Contains(destination.RootElement[1].EnumerateTrivia(), trivia => trivia.GetString() == "sibling");
        Assert.Contains(destination.TrailingTrivia.GetEnumerator(), trivia => trivia.GetString() == "tail");
    }

    [Fact]
    public void AppendingContainerDuringPooledGrowthPreservesExistingTokensAndTrailingTrivia()
    {
        var source = new DArray(Enumerable.Range(0, 128).Select(i => DValue.Create(i)));
        using var sourceDocument = source.Document;
        using var destination = Json5Document.Parse("{keep:1}/*tail*/",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        var tail = destination.TrailingTrivia.GetEnumerator().Single(trivia => trivia.GetString() == "tail");

        destination.RootElement.AsObject().Add("joined", source.AsElement());

        Assert.True(tail.IsValid);
        Assert.Equal("tail", tail.GetString());
        Assert.Equal(1, destination.RootElement.GetProperty("keep").GetInt32());
        var joined = destination.RootElement.GetProperty("joined");
        Assert.Equal(128, joined.GetArrayLength());
        Assert.Equal(127, joined[127].GetInt32());
        Assert.Equal("$", destination.RootElement.AsValue().GetPath());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoiningDeepContainersUsesExplicitTraversalStack(bool replace)
    {
        var source = new DArray();
        using var sourceDocument = source.Document;
        var current = source;
        for (var i = 0; i < 10000; i++)
        {
            current = current.AddArray();
        }

        current.Add(42);
        using var destination = JsonDocument.Parse("0");
        var id = destination.JoinElement(replace ? destination.RootId : 0, source.AsElement());
        sourceDocument.Dispose();
        var result = new DElement(destination, id);
        for (var i = 0; i < 10001; i++)
        {
            result = result.EnumerateArray().Single();
        }

        Assert.Equal(42, result.GetInt32());
    }
}