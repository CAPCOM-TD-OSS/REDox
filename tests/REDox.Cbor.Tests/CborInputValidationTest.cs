﻿using System;
using System.IO;

namespace REDox.Cbor.Tests;

public sealed class CborInputValidationTest
{
    [Theory]
    [InlineData("")]
    [InlineData("18")]
    [InlineData("1900")]
    [InlineData("1A000000")]
    [InlineData("1B00000000000000")]
    [InlineData("F9FF")]
    [InlineData("FA000000")]
    [InlineData("FB00000000000000")]
    [InlineData("1F")]
    [InlineData("3F")]
    [InlineData("DF")]
    [InlineData("FF")]
    [InlineData("C0")]
    [InlineData("D864")]
    [InlineData("61")]
    [InlineData("41")]
    [InlineData("7AFFFFFFFB")]
    [InlineData("5AFFFFFFFB")]
    [InlineData("5BFFFFFFFFFFFFFFFB")]
    [InlineData("7F")]
    [InlineData("5F")]
    [InlineData("7F61")]
    [InlineData("5F41")]
    [InlineData("7F7801")]
    [InlineData("5F5801")]
    [InlineData("7F7AFFFFFFFB")]
    [InlineData("5F5AFFFFFFFB")]
    [InlineData("7F7BFFFFFFFFFFFFFFFB")]
    [InlineData("5F5BFFFFFFFFFFFFFFFB")]
    [InlineData("7F7A000000")]
    [InlineData("5F5B00000000000000")]
    [InlineData("7F7FFF")]
    [InlineData("5F5FFF")]
    [InlineData("7F40FF")]
    [InlineData("5F60FF")]
    [InlineData("82F5")]
    [InlineData("9FF5")]
    [InlineData("A1F5")]
    [InlineData("A1F5A1")]
    [InlineData("BFF5FF")]
    [InlineData("9AFFFFFFFF")]
    [InlineData("BA80000000")]
    [InlineData("BAFFFFFFFF")]
    [InlineData("C482")]
    [InlineData("C48220C25AFFFFFFFB")]
    public void InvalidInputIsRejectedByAllParseOverloads(string hex)
    {
        var input = Convert.FromHexString(hex);

        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var document = CborDocument.Parse(input);
        });
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var document = CborDocument.Parse(input.AsMemory());
        });
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var document = CborDocument.Parse(input.AsSpan());
        });
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var stream = new MemoryStream(input);
            using var document = CborDocument.Parse(stream);
        });

        Assert.False(CborDocument.TryParse(input, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("82F5")]
    [InlineData("9FF5")]
    [InlineData("A1F5")]
    [InlineData("A1F5A1")]
    [InlineData("C0")]
    [InlineData("FF")]
    [InlineData("BFF5FF")]
    public void IncompleteSequenceItemIsRejected(string hex)
    {
        var input = Convert.FromHexString("01" + hex);
        var options = new CborDocumentOptions { UseSequenceFormat = true };

        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var document = CborDocument.Parse(input, options: options);
        });
        Assert.False(CborDocument.TryParse(input, out var result, options: options));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("00", "0")]
    [InlineData("1818", "24")]
    [InlineData("190100", "256")]
    [InlineData("1A00010000", "65536")]
    [InlineData("1B0000000100000000", "4294967296")]
    [InlineData("20", "-1")]
    [InlineData("F5", "true")]
    [InlineData("F93C00", "1")]
    [InlineData("60", "\"\"")]
    [InlineData("780161", "\"a\"")]
    [InlineData("79000161", "\"a\"")]
    [InlineData("7A0000000161", "\"a\"")]
    [InlineData("7FFF", "\"\"")]
    [InlineData("7F606161626263FF", "\"abc\"")]
    [InlineData("7F7B000000000000000161FF", "\"a\"")]
    [InlineData("80", "[]")]
    [InlineData("A0", "{}")]
    [InlineData("9FFF", "[]")]
    [InlineData("BFFF", "{}")]
    [InlineData("8180", "[[]]")]
    [InlineData("9A0000000101", "[1]")]
    [InlineData("BA00000001616101", "{\"a\":1}")]
    [InlineData("BF616101FF", "{\"a\":1}")]
    public void ValidInputStillDecodes(string hex, string expected)
    {
        var input = Convert.FromHexString(hex);

        using var document = CborDocument.Parse(input);
        Assert.Equal(expected, REDox.Json.JsonDocument.EncodeToString(document.RootElement));
        Assert.True(CborDocument.TryParse(input, out var result));
        using (result)
        {
            Assert.Equal(expected, REDox.Json.JsonDocument.EncodeToString(result.RootElement));
        }
    }

    [Theory]
    [InlineData("40", "40")]
    [InlineData("580101", "4101")]
    [InlineData("59000101", "4101")]
    [InlineData("5A0000000101", "4101")]
    [InlineData("5FFF", "40")]
    [InlineData("5F404101420203FF", "43010203")]
    [InlineData("5F5B000000000000000101FF", "4101")]
    public void ValidByteStringStillDecodes(string hex, string expected)
    {
        using var document = CborDocument.Parse(Convert.FromHexString(hex));

        Assert.Equal(Convert.FromHexString(expected), CborDocument.Encode(document.RootElement));
    }

    [Theory]
    [InlineData(0x5F, 0x58)]
    [InlineData(0x7F, 0x78)]
    public void IndefiniteLengthStringExceedingMaxLengthIsRejected(byte header, byte chunkHeader)
    {
        var options = new CborDocumentOptions { MaxLength = 256 };
        var input = CreateIndefiniteLengthString(header, chunkHeader, 2, 200);

        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var document = CborDocument.Parse(input, options: options);
        });
        Assert.False(CborDocument.TryParse(input, out var result, options: options));
        Assert.Null(result);
    }

    [Theory]
    [InlineData(0x5F, 0x58)]
    [InlineData(0x7F, 0x78)]
    public void IndefiniteLengthStringWithinMaxLengthIsAccepted(byte header, byte chunkHeader)
    {
        var options = new CborDocumentOptions { MaxLength = 256 };
        var input = CreateIndefiniteLengthString(header, chunkHeader, 2, 128);

        using var document = CborDocument.Parse(input, options: options);
        Assert.NotNull(document);
    }

    private static byte[] CreateIndefiniteLengthString(byte header, byte chunkHeader, int chunkCount, byte chunkLength)
    {
        var input = new byte[2 + chunkCount * (2 + chunkLength)];
        var index = 0;
        input[index++] = header;

        for (var i = 0; i < chunkCount; i++)
        {
            input[index++] = chunkHeader;
            input[index++] = chunkLength;
            input.AsSpan(index, chunkLength).Fill((byte)'a');
            index += chunkLength;
        }

        input[index] = 0xFF;
        return input;
    }

    [Theory]
    [InlineData("", "[]")]
    [InlineData("0181026178", "[1,[2],\"x\"]")]
    [InlineData("9F01FFBF616102FF", "[[1],{\"a\":2}]")]
    public void ValidSequenceStillDecodes(string hex, string expected)
    {
        var input = Convert.FromHexString(hex);
        var options = new CborDocumentOptions { UseSequenceFormat = true };

        using var document = CborDocument.Parse(input, options: options);
        Assert.Equal(expected, REDox.Json.JsonDocument.EncodeToString(document.RootElement));
        Assert.True(CborDocument.TryParse(input, out var result, options: options));
        using (result)
        {
            Assert.Equal(expected, REDox.Json.JsonDocument.EncodeToString(result.RootElement));
        }
    }
}