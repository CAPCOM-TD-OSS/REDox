// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace REDox.Serialization.NewtonsoftJson;

sealed class NumericsTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (type == typeof(BigInteger))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(BigInteger))
        {
            return new BigIntegerConverter();
        }

        throw new NotSupportedException();
    }

    private class BigIntegerConverter : DataConverter<BigInteger>
    {
        public override BigInteger Read(in DataReader reader, uint tokenId, BigInteger existingValue)
        {
            var token = reader.GetToken(tokenId);

            if (token.Kind == DTokenKind.ByteString)
            {
                return new BigInteger(reader.ReadByteString(tokenId));
            }

            return BigInteger.Parse(Encoding.UTF8.GetString(reader.ReadBigNumber(tokenId)),
                CultureInfo.InvariantCulture);
        }

        public override void Write(DataWriter writer, BigInteger value)
        {
            writer.WriteBigNumber(Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)),
                BigNumberKind.Integer);
        }
    }
}