// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using REDox.Json;

namespace REDox.Tests;

public sealed class DocumentNumericValueRegressionTests
{
    [Theory]
    [InlineData("-9223372036854775808")]
    [InlineData("18446744073709551615")]
    [InlineData("123.456")]
    [InlineData("1234567890123456789012345678")]
    [InlineData("79228162514264337593543950335")]
    public void NumericValueReadsShouldPreservePrecisionWithoutRelaxedConversion(string json)
    {
        using var document = JsonDocument.Parse(json,
            new DoxSerializerSettings { AllowRelaxedScalarConversion = false });
        var id = document.RootElement.Id;

        Assert.Equal(decimal.Parse(json, CultureInfo.InvariantCulture), document.GetDecimalValue(id));
        Assert.Equal(double.Parse(json, CultureInfo.InvariantCulture), document.GetFloatingValue(id));

        document.JoinAny(id, decimal.Parse(json, CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(json, CultureInfo.InvariantCulture), document.GetDecimalValue(id));
        Assert.Equal((double)decimal.Parse(json, CultureInfo.InvariantCulture), document.GetFloatingValue(id));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e100")]
    [InlineData("79228162514264337593543950336")]
    public void DecimalExactReadShouldRejectUnrepresentableNumbers(string json)
    {
        using var document = Json5Document.Parse(json);

        Assert.False(document.RootElement.TryGetDecimal(out _));
    }

    [Theory]
    [InlineData("'42.75'", true)]
    [InlineData("'42.75'", false)]
    [InlineData("true", true)]
    [InlineData("true", false)]
    public void FallbackValueReadsShouldRespectRelaxedConversionSettings(string json, bool allowRelaxed)
    {
        using var document = Json5Document.Parse(json,
            new DoxSerializerSettings { AllowRelaxedScalarConversion = allowRelaxed });
        var id = document.RootElement.Id;

        if (allowRelaxed)
        {
            Assert.Equal(json == "true" ? 1m : 42.75m, document.GetDecimalValue(id));
            Assert.Equal(json == "true" ? 1.0 : 42.75, document.GetFloatingValue(id));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => document.GetDecimalValue(id));
            Assert.Throws<InvalidOperationException>(() => document.GetFloatingValue(id));
        }
    }
}