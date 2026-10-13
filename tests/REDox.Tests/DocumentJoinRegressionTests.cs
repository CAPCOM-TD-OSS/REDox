// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class DocumentJoinRegressionTests
{
    [Theory]
    [InlineData('\0', "\0")]
    [InlineData('A', "A")]
    [InlineData('漢', "漢")]
    [InlineData('\uD800', "\uFFFD")]
    [InlineData('\uDFFF', "\uFFFD")]
    [InlineData('\uFFFF', "\uFFFF")]
    public void JoiningCharShouldMatchSerializerReplacementFallback(char value, string expected)
    {
        using var direct = Json5Document.Parse("/*keep*/'old'",
            options: new Json5DocumentOptions { PreserveTrivia = true });
        direct.RootElement.AsValue().ReplaceWith(DValue.Create(value));
        Assert.Equal(expected, direct.RootElement.GetString());
        Assert.Equal(StringKind.SingleQuote, direct.RootElement.Token.StringKind);
        Assert.Contains("/*keep*/", Json5Document.EncodeToString(direct.RootElement,
            new Json5WriteOptions { PreserveTrivia = true }));

        using var serialized = DoxNodeDocument.CreateFrom(value, SerializerSettings.Default);
        Assert.Equal(expected, serialized.RootElement.GetString());
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("object")]
    [InlineData("value")]
    public void JoiningGuidShouldUseNetworkByteOrder(string inputType)
    {
        using var document = JsonDocument.Parse("0");
        var guid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var id = document.RootElement.Id;
        switch (inputType)
        {
            case "generic": document.JoinAny(id, guid); break;
            case "object": document.JoinObject(id, guid); break;
            case "value": document.JoinValue(id, DValue.Create(guid)); break;
        }

        Assert.Equal(ByteStringKind.Guid, document.RootElement.Token.ByteStringKind);
        Assert.Equal(Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF"),
            document.RootElement.GetByteString().ToArray());
        Assert.Equal(guid, document.RootElement.GetGuid());
    }

    [Fact]
    public void JoiningBoxedValuesShouldPreserveKindsAndContents()
    {
        using var document = Json5Document.Parse("0x1");
        var id = document.RootElement.Id;
        document.JoinObject(id, 42);
        Assert.Equal(IntegerKind.Hexadecimal, document.RootElement.Token.IntegerKind);
        Assert.Equal(42, document.RootElement.GetInt32());

        document.JoinObject(id, 123.45m);
        Assert.Equal(FloatKind.Decimal, document.RootElement.Token.FloatKind);
        Assert.Equal(123.45m, document.RootElement.GetDecimal());

        var date = new DateTime(2026, 10, 12, 12, 34, 56, DateTimeKind.Utc);
        document.JoinObject(id, date);
        Assert.Equal(date, document.RootElement.GetDateTime());
        var offset = new DateTimeOffset(date).ToOffset(TimeSpan.FromHours(9));
        document.JoinObject(id, offset);
        Assert.Equal(TimestampKind.OffsetDateTime, document.RootElement.Token.TimestampKind);
        Assert.Equal(offset.Offset, document.RootElement.GetDateTimeOffset().Offset);
        Assert.Equal(offset, document.RootElement.GetDateTimeOffset());
    }

    [Fact]
    public void JoiningObjectsShouldPreserveConverterDispatch()
    {
        var array = new DArray(new DoxSerializerSettings
        {
            Converters = [new IntConverter(), new EnumConverter()]
        });
        using var document = array.Document;
        var direct = document.JoinAny(0, 42);
        var genericObject = document.JoinAny<object>(0, 42);
        var boxed = document.JoinObject(0, 42);
        var enumeration = document.JoinObject(0, TestEnum.Value);

        Assert.Equal(42, new DElement(document, direct).GetInt32());
        Assert.Equal("custom int", new DElement(document, genericObject).GetString());
        Assert.Equal(42, new DElement(document, boxed).GetInt32());
        Assert.Equal("custom enum", new DElement(document, enumeration).GetString());
    }

    private enum TestEnum
    {
        Value = 42
    }

    private sealed class IntConverter : DataConverter<int>
    {
        public override int Read(in DataReader reader, uint tokenId, int existingValue)
        {
            throw new NotSupportedException();
        }

        public override void Write(DataWriter writer, int value)
        {
            writer.WriteString("custom int");
        }
    }

    private sealed class EnumConverter : DataConverter<TestEnum>
    {
        public override TestEnum Read(in DataReader reader, uint tokenId, TestEnum existingValue)
        {
            throw new NotSupportedException();
        }

        public override void Write(DataWriter writer, TestEnum value)
        {
            writer.WriteString("custom enum");
        }
    }
}