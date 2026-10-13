// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Tests;

public sealed class DocumentTokenBufferRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservingCapacityShouldPreserveLastAllocatedToken(bool isPooled)
    {
        using var document = new CapacityTestDocument(isPooled);
        var token = DToken.MakeExtendInlineInteger(IntegerKind.Inherit, 123456789);
        var id = document.AllocToken(token);
        var expected = document.GetTokens().ToArray();

        document.Reserve(4096);

        Assert.Equal(expected, document.GetTokens().ToArray());
        Assert.Equal(token, document.GetToken(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocatingAcrossCapacityBoundariesShouldPreserveEveryToken(bool isPooled)
    {
        using var document = new CapacityTestDocument(isPooled);
        for (var i = 1; i <= 4096; i++)
        {
            Assert.Equal((uint)i,
                document.AllocToken(DToken.MakeExtendInlineInteger(IntegerKind.Inherit, i)));
        }

        Assert.Equal(4097, document.GetTokens().Length);
        Assert.Equal(default, document.GetToken(0));
        for (var i = 1; i <= 4096; i++)
        {
            Assert.Equal(DToken.MakeExtendInlineInteger(IntegerKind.Inherit, i), document.GetToken((uint)i));
        }
    }

    private sealed class CapacityTestDocument(bool isPooled) : Document(SerializerSettings.Default, isPooled)
    {
        public void Reserve(int capacity)
        {
            EnsureCapacity(capacity);
        }
    }
}