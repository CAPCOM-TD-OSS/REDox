namespace REDox.Tests;

public class TokenTest
{
    [Theory]
    [InlineData(2)]
    [InlineData(float.MinValue)]
    [InlineData(float.MaxValue)]
    [InlineData(1.23)]
    [InlineData(-1.24)]
    public void SingleLiteral(float value)
    {
        var token = DToken.MakeExtendInlineSingle(FloatKind.Inherit, value);

        Assert.True(token.HasInlineSubKind);
        Assert.Equal((uint)FloatKind.Inherit, token.SubKind);

        var result = DToken.DecodeInlineSinglePayload(token);

        Assert.Equal(value, result);

        var d = new DArray { value };

        Assert.Equal(value, (float)d[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(-1)]
    [InlineData(100000000)]
    [InlineData(-100000000)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(DToken.MinInlineInteger)]
    [InlineData(DToken.MaxInlineInteger)]
    public void IntegerLiteral(long value)
    {
        var token = DToken.MakeExtendInlineInteger(IntegerKind.Hexadecimal, value);

        Assert.True(token.HasInlineSubKind);
        Assert.Equal((uint)IntegerKind.Hexadecimal, token.SubKind);

        var result = DToken.DecodeInlineIntegerPayload(token);

        Assert.Equal(value, result);
        Assert.Equal(IntegerKind.Hexadecimal, token.IntegerKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineFloatDoesNotHaveInlineSubKindEvenWithVariantMarker(bool extended)
    {
        const double value = 2147483648.1;
        var payload = DToken.EncodeInlineFloatPayload(value);
        Assert.True(payload >= 0);

        var token = extended ? DToken.MakeExtendInlineFloat(payload) : DToken.MakeInlineFloat(payload);

        Assert.Equal(7, (int)token.Variant & 7);
        Assert.False(token.HasInlineSubKind);
        Assert.Equal(extended, token.IsInlinePayload);
        Assert.Equal(value, DToken.DecodeInlineFloatPayload(token));
    }

    [Fact]
    public void SourceBackedInheritMarkerDoesNotHaveInlineSubKind()
    {
        var token = DToken.Make(IntegerKind.Inherit.ToVariant(), 0);

        Assert.False(token.HasInlineSubKind);
        Assert.False(token.IsInlinePayload);
        Assert.Equal((uint)IntegerKind.Default, token.SubKind);
        Assert.Equal(IntegerKind.Default, token.IntegerKind);
    }

    [Fact]
    public void InlineSubKindClassificationMatchesAllHeaderPatterns()
    {
        for (var header = 0; header < 256; header++)
        {
            // E=1, marker=111, and a non-container Kind other than InlineFloat.
            var expected = header is 0x87 or 0x8f or 0x97 or 0x9f or 0xa7 or 0xaf or 0xb7
                or 0xc7 or 0xcf or 0xd7 or 0xdf;

            foreach (var payload in new[] { 0L, 0x001fffffffffffffL, 0x00ffffffffffffffL })
            {
                var token = new DToken(((long)header << 56) | payload);

                Assert.Equal(expected, token.HasInlineSubKind);
            }
        }
    }

    [Theory]
    [InlineData(DTokenType.Map, 0x1bffffff)]
    [InlineData(DTokenType.Map, 0x1c000000)]
    [InlineData(DTokenType.Map, 0x1fffffff)]
    [InlineData(DTokenType.Map, 0x20000000)]
    [InlineData(DTokenType.Map, 0x3bffffff)]
    [InlineData(DTokenType.Map, 0x3c000000)]
    [InlineData(DTokenType.Map, 0x3fffffff)]
    [InlineData(DTokenType.Array, 0x1bffffff)]
    [InlineData(DTokenType.Array, 0x1c000000)]
    [InlineData(DTokenType.Array, 0x1fffffff)]
    [InlineData(DTokenType.Array, 0x20000000)]
    [InlineData(DTokenType.Array, 0x3bffffff)]
    [InlineData(DTokenType.Array, 0x3c000000)]
    [InlineData(DTokenType.Array, 0x3fffffff)]
    public void ExtendedContainerIsNotInlineRegardlessOfLinkId(DTokenType type, uint linkId)
    {
        var token = DToken.MakeExtendContainer(type, 1, linkId);

        Assert.True(token.IsExtended);
        Assert.True(token.IsContainer);
        Assert.Equal(type, token.Type);
        Assert.Equal(linkId, token.LinkId);
        Assert.Equal(1u, token.ExtendId);
        Assert.False(token.HasInlineSubKind);
        Assert.False(token.IsInlinePayload);
        Assert.False(token.IsExtendInlineLiteral);
    }

    [Fact]
    public void LiteralTest()
    {
        var token1 = DToken.MakeExtendLiteral(DTokenVariant.BooleanTrue, 0);
        Assert.Equal(DTokenType.Literal, token1.Type);
        Assert.Equal(DTokenKind.Boolean, token1.Kind);
        Assert.Equal(DTokenVariant.BooleanTrue, token1.Variant);
        Assert.True(token1.IsExtendInlineLiteral);

        var token2 = DToken.MakeExtendLiteral(DTokenVariant.BooleanFalse, 0);
        Assert.Equal(DTokenType.Literal, token2.Type);
        Assert.Equal(DTokenKind.Boolean, token2.Kind);
        Assert.Equal(DTokenVariant.BooleanFalse, token2.Variant);
        Assert.True(token2.IsExtendInlineLiteral);

        var token3 = DToken.MakeExtendLiteral(DTokenVariant.Null, 0);
        Assert.Equal(DTokenType.Literal, token3.Type);
        Assert.Equal(DTokenKind.Null, token3.Kind);
        Assert.Equal(DTokenVariant.Null, token3.Variant);
        Assert.True(token3.IsExtendInlineLiteral);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(100, 100)]
    [InlineData(10000, 10000)]
    [InlineData(0xfffffff, 0xfffffff)]
    [InlineData(0x3fffffff, 0xfffffff)]
    public void EmptyToken(uint emptyId, uint extendId)
    {
        var token = DToken.MakeEmpty(emptyId);

        Assert.True(token.IsEmptyToken);
        Assert.False(token.IsEmptyExtend);
        Assert.Equal(DTokenType.Ignore, token.Type);
        Assert.Equal(DTokenKind.Control, token.Kind);
        Assert.True(token.IsIgnore);
        Assert.Equal(emptyId, token.EmptyId);
        Assert.True(token.IsExtended);

        var token2 = DToken.MakeEmptyExtend(emptyId, extendId);
        Assert.False(token2.IsEmptyToken);
        Assert.True(token2.IsEmptyExtend);
        Assert.Equal(DTokenType.Ignore, token2.Type);
        Assert.Equal(DTokenKind.Control, token2.Kind);
        Assert.True(token.IsIgnore);
        Assert.Equal(emptyId, token2.EmptyId);
        Assert.Equal(extendId, token2.ExtendId);
        Assert.True(token.IsExtended);

        TestContext.Current.TestOutputHelper?.WriteLine(token.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine(token2.ToString());
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(1.3)]
    [InlineData(1.333333)]
    [InlineData(-0.25)]
    [InlineData(-0.23)]
    [InlineData(1234555.00000000001)]
    [InlineData(-99999999999999.99999999)]
    [InlineData(-1234555.00000000001)]
    [InlineData(123456789.987654321)]
    public void FloatLiteral(double value)
    {
        var dox = DoxSerializer.Serialize(value, SerializerSettings.Default);

        var result = DoxSerializer.Deserialize<double>(dox, SerializerSettings.Default);

        Assert.Equal(value, result);

        var payload = DToken.EncodeInlineFloatPayload(value);

        if (payload >= 0)
        {
            var token = DToken.MakeInlineFloat(payload);
            var decoded = DToken.DecodeInlineFloatPayload(token);

            Assert.Equal(result, decoded);
        }

        if (payload >= 0)
        {
            var token2 = DToken.MakeExtendInlineFloat(payload);

            Assert.Equal(DTokenType.ExtNumber, token2.Type);
            Assert.Equal(DTokenKind.InlineFloat, token2.Kind);
            Assert.True(token2.IsExtended);

            var decoed = DToken.DecodeInlineFloatPayload(token2);
            Assert.Equal(result, decoed);
        }

        var arr = new DArray();
        arr.Add(value);

        foreach (var token in arr.Document.GetTokens())
        {
            TestContext.Current.TestOutputHelper?.WriteLine(token.ToString());
        }

        Assert.Equal(value, (double)arr[0]);
    }
}