// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class BuiltInTypeConverter : DataConverterFactory
{
    private const NumberHandling FloatingPointStringHandlingMask =
        NumberHandling.AllowReadingFromString | NumberHandling.AllowNamedFloatingPointLiterals;

    private static bool TryParseNamedFloatingPointLiteral(ReadOnlySpan<byte> source, out double value)
    {
        if (source.SequenceEqual("NaN"u8))
        {
            value = double.NaN;
            return true;
        }

        if (source.SequenceEqual("Infinity"u8))
        {
            value = double.PositiveInfinity;
            return true;
        }

        if (source.SequenceEqual("-Infinity"u8))
        {
            value = double.NegativeInfinity;
            return true;
        }

        value = 0;
        return false;
    }

    internal static MethodInfo? GetWritePropertyMethod(DataConverter? converter)
    {
        if (converter is StringConverter)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyString),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is GuidConverter)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyGuid),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is Int32Converter int32Converter &&
            (int32Converter.NumberHandling & NumberHandling.WriteAsString) == 0)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyInt32),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is Int64Converter int64Converter &&
            (int64Converter.NumberHandling & NumberHandling.WriteAsString) == 0)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyInt64),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is UInt32Converter uint32Converter &&
            (uint32Converter.NumberHandling & NumberHandling.WriteAsString) == 0)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyUInt32),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is UInt64Converter uint64Converter &&
            (uint64Converter.NumberHandling & NumberHandling.WriteAsString) == 0)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyUInt64),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is BooleanConverter)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyBoolean),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is SingleConverter singleConverter &&
            (singleConverter.NumberHandling & NumberHandling.WriteAsString) == 0)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertySingle),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (converter is DoubleConverter doubleConverter &&
            (doubleConverter.NumberHandling & NumberHandling.WriteAsString) == 0)
        {
            return typeof(DataWriter).GetMethod(nameof(DataWriter.WritePropertyDouble),
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        return null;
    }

    public override bool CanConvert(Type type)
    {
        if (type == typeof(bool) || type == typeof(char))
        {
            return true;
        }

        if (type == typeof(string) || type == typeof(object))
        {
            return true;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
            type == typeof(Guid) || type == typeof(Uri))
        {
            return true;
        }

        if (type.IsAssignableTo(typeof(Type)))
        {
            return true;
        }

        if (type.IsAssignableTo(typeof(MemberInfo)))
        {
            return true;
        }

        if (type == typeof(DBNull))
        {
            return true;
        }

        if (type == typeof(Version))
        {
            return true;
        }

        if (type == typeof(TimeOnly))
        {
            return true;
        }

        if (type == typeof(DateOnly))
        {
            return true;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
            case TypeCode.SByte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
        }

        if (type == typeof(Half))
        {
            return true;
        }

        if (type == typeof(Int128) || type == typeof(UInt128))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(bool))
        {
            return new BooleanConverter();
        }

        if (type == typeof(char))
        {
            return new CharConverter();
        }

        if (type == typeof(string))
        {
            return new StringConverter();
        }

        if (type == typeof(DateTime))
        {
            return new DateTimeConverter();
        }

        if (type == typeof(DateTimeOffset))
        {
            return new DateTimeOffsetConverter(settings, false);
        }

        if (type == typeof(TimeSpan))
        {
            return new TimeSpanConverter();
        }

        if (type == typeof(Guid))
        {
            return new GuidConverter();
        }

        if (type == typeof(Uri))
        {
            return new UriConverter();
        }

        if (type == typeof(object))
        {
            return new ObjectConverter(settings);
        }

        if (type.IsAssignableTo(typeof(Type)))
        {
            return ConverterHelper.CreateConverter(typeof(TypeConverter<>).MakeGenericType(type), settings);
        }

        if (type.IsAssignableTo(typeof(MemberInfo)))
        {
            return ConverterHelper.CreateConverter(typeof(MemberInfoConverter<>).MakeGenericType(type), settings);
        }

        if (type == typeof(DBNull))
        {
            return new DBNullConverter();
        }

        if (type == typeof(Version))
        {
            return new VersionConverter();
        }

        if (type == typeof(TimeOnly))
        {
            return new TimeOnlyConverter();
        }

        if (type == typeof(DateOnly))
        {
            return new DateOnlyConverter();
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
                return new ByteConverter(settings.GetContract(typeof(byte)).NumberHandling);
            case TypeCode.Int16:
                return new Int16Converter(settings.GetContract(typeof(short)).NumberHandling);
            case TypeCode.Int32:
                return new Int32Converter(settings.GetContract(typeof(int)).NumberHandling);
            case TypeCode.Int64:
                return new Int64Converter(settings.GetContract(typeof(long)).NumberHandling);
            case TypeCode.SByte:
                return new SByteConverter(settings.GetContract(typeof(sbyte)).NumberHandling);
            case TypeCode.UInt16:
                return new UInt16Converter(settings.GetContract(typeof(ushort)).NumberHandling);
            case TypeCode.UInt32:
                return new UInt32Converter(settings.GetContract(typeof(uint)).NumberHandling);
            case TypeCode.UInt64:
                return new UInt64Converter(settings.GetContract(typeof(ulong)).NumberHandling);
            case TypeCode.Single:
                return new SingleConverter(settings.GetContract(typeof(float)).NumberHandling);
            case TypeCode.Double:
                return new DoubleConverter(settings.GetContract(typeof(double)).NumberHandling);
            case TypeCode.Decimal:
                return new DecimalConverter(settings.GetContract(typeof(decimal)).NumberHandling);
        }

        if (type == typeof(Half))
        {
            return new HalfConverter(settings.GetContract(typeof(Half)).NumberHandling);
        }

        if (type == typeof(Int128))
        {
            return new Int128Converter(settings.GetContract(typeof(Int128)).NumberHandling);
        }

        if (type == typeof(UInt128))
        {
            return new UInt128Converter(settings.GetContract(typeof(UInt128)).NumberHandling);
        }

        throw new NotSupportedException($"The type {type} is not supported.");
    }


    private sealed class Int64Converter : DataConverter<long>
    {
        public Int64Converter(NumberHandling numberHandling)
        {
            NumberHandling = numberHandling;
        }

        public NumberHandling NumberHandling { get; }

        public override long ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out long value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Int64 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, long value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Int64 format.");
            }
        }


        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != NumberHandling)
            {
                return new Int64Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override long Read(in DataReader reader, uint tokenId, long existingValue)
        {
            if ((NumberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out long value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Int64 format.");
            }

            return reader.ReadInt64(tokenId);
        }

        public override void Write(DataWriter writer, long value)
        {
            if ((NumberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                if (Utf8Formatter.TryFormat(value, temp, out var bytes))
                {
                    writer.WriteNumberString(temp.Slice(0, bytes));
                }
                else
                {
                    throw new FormatException("Invalid Int64 format.");
                }
            }
            else
            {
                writer.WriteInt64(value);
            }
        }
    }

    private sealed class HalfConverter : DataConverter<Half>
    {
        private readonly NumberHandling _numberHandling;

        public HalfConverter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new HalfConverter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override Half ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Half.TryParse(reader.ReadUtf8String(tokenId), CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            throw new FormatException("Invalid Half format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, Half value)
        {
            Span<byte> buffer = stackalloc byte[32];

            if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            {
                writer.WriteString(buffer.Slice(0, written));
            }
            else
            {
                throw new FormatException("Invalid Half format.");
            }
        }

        public override Half Read(in DataReader reader, uint tokenId, Half existingValue)
        {
            if ((_numberHandling & FloatingPointStringHandlingMask) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if ((_numberHandling & NumberHandling.AllowNamedFloatingPointLiterals) != 0 &&
                    TryParseNamedFloatingPointLiteral(source, out var named))
                {
                    return (Half)named;
                }

                if ((_numberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                    Half.TryParse(source, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }

                throw new FormatException("Invalid Half format.");
            }

            return reader.ReadHalf(tokenId);
        }

        public override void Write(DataWriter writer, Half value)
        {
            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0,
                    Utf8Helper.EncodeNumber(temp, value, FloatFormatHandling.SpecialFloatAsSymbol)));
            }
            else
            {
                writer.WriteHalf(value);
            }
        }
    }

    private sealed class Int128Converter : DataConverter<Int128>
    {
        private readonly NumberHandling _numberHandling;

        public Int128Converter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new Int128Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override Int128 ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Int128.TryParse(reader.ReadUtf8String(tokenId), CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            throw new FormatException("Invalid Int128 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, Int128 value)
        {
            Span<byte> buffer = stackalloc byte[40];

            if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            {
                writer.WriteString(buffer.Slice(0, written));
            }
            else
            {
                throw new FormatException("Invalid Int128 format.");
            }
        }

        public override Int128 Read(in DataReader reader, uint tokenId, Int128 existingValue)
        {
            if (reader.GetToken(tokenId).IsStringEncoded &&
                (_numberHandling & NumberHandling.AllowReadingFromString) == 0 &&
                !reader.Settings.AllowRelaxedScalarConversion)
            {
                throw new FormatException("Invalid Int128 format.");
            }

            if (Int128.TryParse(reader.ReadBigNumber(tokenId), CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            throw new FormatException("Invalid Int128 format.");
        }

        public override void Write(DataWriter writer, Int128 value)
        {
            Span<byte> buffer = stackalloc byte[40];

            if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            {
                if ((_numberHandling & NumberHandling.WriteAsString) != 0)
                {
                    writer.WriteNumberString(buffer.Slice(0, written));
                }
                else
                {
                    writer.WriteBigNumber(buffer.Slice(0, written), BigNumberKind.Int128);
                }
            }
            else
            {
                throw new FormatException("Invalid Int128 format.");
            }
        }
    }

    private sealed class UInt128Converter : DataConverter<UInt128>
    {
        private readonly NumberHandling _numberHandling;

        public UInt128Converter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }


        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new UInt128Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override UInt128 Read(in DataReader reader, uint tokenId, UInt128 existingValue)
        {
            if (reader.GetToken(tokenId).IsStringEncoded &&
                (_numberHandling & NumberHandling.AllowReadingFromString) == 0 &&
                !reader.Settings.AllowRelaxedScalarConversion)
            {
                throw new FormatException("Invalid UInt128 format.");
            }

            if (UInt128.TryParse(reader.ReadBigNumber(tokenId), CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            throw new FormatException("Invalid UInt128 format.");
        }

        public override UInt128 ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (UInt128.TryParse(reader.ReadUtf8String(tokenId), CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            throw new FormatException("Invalid UInt128 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, UInt128 value)
        {
            Span<byte> buffer = stackalloc byte[40];
            if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            {
                writer.WriteString(buffer.Slice(0, written));
            }
            else
            {
                throw new FormatException("Invalid UInt128 format.");
            }
        }

        public override void Write(DataWriter writer, UInt128 value)
        {
            Span<byte> buffer = stackalloc byte[40];

            if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            {
                if ((_numberHandling & NumberHandling.WriteAsString) != 0)
                {
                    writer.WriteNumberString(buffer.Slice(0, written));
                }
                else
                {
                    writer.WriteBigNumber(buffer.Slice(0, written), BigNumberKind.UInt128);
                }
            }
            else
            {
                throw new FormatException("Invalid UInt128 format.");
            }
        }
    }

    private sealed class Int32Converter : DataConverter<int>
    {
        public Int32Converter(NumberHandling numberHandling)
        {
            NumberHandling = numberHandling;
        }

        public NumberHandling NumberHandling { get; }

        public override int ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out int value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Int32 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, int value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Int32 format.");
            }
        }


        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != NumberHandling)
            {
                return new Int32Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override int Read(in DataReader reader, uint tokenId, int existingValue)
        {
            if ((NumberHandling & NumberHandling.AllowReadingFromString) != 0 && reader.GetToken(tokenId).IsStringEncoded)
            {
                var utf8Bytes = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(utf8Bytes, out int value, out var bytesConsumed) && utf8Bytes.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Int32 format.");
            }

            return reader.ReadInt32(tokenId);
        }

        public override void Write(DataWriter writer, int value)
        {
            if ((NumberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteInt32(value);
            }
        }
    }

    private sealed class Int16Converter : DataConverter<short>
    {
        private readonly NumberHandling _numberHandling;

        public Int16Converter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }


        public override short ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out short value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Int16 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, short value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Int16 format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new Int16Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override short Read(in DataReader reader, uint tokenId, short existingValue)
        {
            if ((_numberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out short value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Int16 format.");
            }

            return reader.ReadInt16(tokenId);
        }

        public override void Write(DataWriter writer, short value)
        {
            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteInt32(value);
            }
        }
    }

    private sealed class SByteConverter : DataConverter<sbyte>
    {
        private readonly NumberHandling _numberHandling;

        public SByteConverter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }

        public override sbyte ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out sbyte value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid SByte format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, sbyte value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid SByte format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new SByteConverter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override sbyte Read(in DataReader reader, uint tokenId, sbyte existingValue)
        {
            if ((_numberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out sbyte value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid SByte format.");
            }

            return reader.ReadSByte(tokenId);
        }

        public override void Write(DataWriter writer, sbyte value)
        {
            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteInt32(value);
            }
        }
    }

    private sealed class UInt64Converter : DataConverter<ulong>
    {
        public UInt64Converter(NumberHandling numberHandling)
        {
            NumberHandling = numberHandling;
        }

        public NumberHandling NumberHandling { get; }

        public override ulong ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out ulong value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid UInt64 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, ulong value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid UInt64 format.");
            }
        }


        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != NumberHandling)
            {
                return new UInt64Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override ulong Read(in DataReader reader, uint tokenId, ulong existingValue)
        {
            if ((NumberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out ulong value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid UInt64 format.");
            }

            return reader.ReadUInt64(tokenId);
        }

        public override void Write(DataWriter writer, ulong value)
        {
            if ((NumberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteUInt64(value);
            }
        }
    }

    private sealed class UInt32Converter : DataConverter<uint>
    {
        public UInt32Converter(NumberHandling numberHandling)
        {
            NumberHandling = numberHandling;
        }

        public NumberHandling NumberHandling { get; }

        public override uint ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out uint value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid UInt32 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, uint value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid UInt32 format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != NumberHandling)
            {
                return new UInt32Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override uint Read(in DataReader reader, uint tokenId, uint existingValue)
        {
            if ((NumberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out uint value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid UInt32 format.");
            }

            return reader.ReadUInt32(tokenId);
        }

        public override void Write(DataWriter writer, uint value)
        {
            if ((NumberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteUInt32(value);
            }
        }
    }

    private sealed class UInt16Converter : DataConverter<ushort>
    {
        private readonly NumberHandling _numberHandling;

        public UInt16Converter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }

        public override ushort ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out ushort value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid UInt16 format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, ushort value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid UInt16 format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new UInt16Converter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override ushort Read(in DataReader reader, uint tokenId, ushort existingValue)
        {
            if ((_numberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out ushort value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid UInt16 format.");
            }

            return reader.ReadUInt16(tokenId);
        }

        public override void Write(DataWriter writer, ushort value)
        {
            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteUInt32(value);
            }
        }
    }

    private sealed class ByteConverter : DataConverter<byte>
    {
        private readonly NumberHandling _numberHandling;

        public ByteConverter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }

        public override byte ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out byte value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Byte format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, byte value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Byte format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new ByteConverter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override byte Read(in DataReader reader, uint tokenId, byte existingValue)
        {
            if ((_numberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out byte value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Byte format.");
            }

            return reader.ReadByte(tokenId);
        }

        public override void Write(DataWriter writer, byte value)
        {
            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0, Utf8Helper.EncodeNumber(temp, value)));
            }
            else
            {
                writer.WriteUInt32(value);
            }
        }
    }

    private sealed class SingleConverter : DataConverter<float>
    {
        public SingleConverter(NumberHandling numberHandling)
        {
            NumberHandling = numberHandling;
        }

        public NumberHandling NumberHandling { get; }

        public override float ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out float value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Single format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, float value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Single format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != NumberHandling)
            {
                return new SingleConverter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override float Read(in DataReader reader, uint tokenId, float existingValue)
        {
            if ((NumberHandling & FloatingPointStringHandlingMask) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if ((NumberHandling & NumberHandling.AllowNamedFloatingPointLiterals) != 0 &&
                    TryParseNamedFloatingPointLiteral(source, out var named))
                {
                    return (float)named;
                }

                if ((NumberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                    Utf8Parser.TryParse(source, out float value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Single format.");
            }

            return reader.ReadSingle(tokenId);
        }

        public override void Write(DataWriter writer, float value)
        {
            if ((NumberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0,
                    Utf8Helper.EncodeNumber(temp, value, FloatFormatHandling.SpecialFloatAsSymbol)));
            }
            else
            {
                writer.WriteSingle(value);
            }
        }
    }

    private sealed class DoubleConverter : DataConverter<double>
    {
        public DoubleConverter(NumberHandling numberHandling)
        {
            NumberHandling = numberHandling;
        }

        public NumberHandling NumberHandling { get; }

        public override double ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out double value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Double format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, double value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Double format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != NumberHandling)
            {
                return new DoubleConverter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override double Read(in DataReader reader, uint tokenId, double existingValue)
        {
            if ((NumberHandling & FloatingPointStringHandlingMask) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if ((NumberHandling & NumberHandling.AllowNamedFloatingPointLiterals) != 0 &&
                    TryParseNamedFloatingPointLiteral(source, out var named))
                {
                    return named;
                }

                if ((NumberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                    Utf8Parser.TryParse(source, out double value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Double format.");
            }

            return reader.ReadDouble(tokenId);
        }

        public override void Write(DataWriter writer, double value)
        {
            if ((NumberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0,
                    Utf8Helper.EncodeNumber(temp, value, FloatFormatHandling.SpecialFloatAsSymbol)));
            }
            else
            {
                writer.WriteDouble(value);
            }
        }
    }

    private sealed class DecimalConverter : DataConverter<decimal>
    {
        private readonly NumberHandling _numberHandling;

        public DecimalConverter(NumberHandling numberHandling)
        {
            _numberHandling = numberHandling;
        }

        public override decimal ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out decimal value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Decimal format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, decimal value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteNumberString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Decimal format.");
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.NumberHandling.HasValue && property.NumberHandling.Value != _numberHandling)
            {
                return new DecimalConverter(property.NumberHandling.Value);
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override decimal Read(in DataReader reader, uint tokenId, decimal existingValue)
        {
            if ((_numberHandling & NumberHandling.AllowReadingFromString) != 0 &&
                reader.GetToken(tokenId).IsStringEncoded)
            {
                var source = reader.ReadUtf8String(tokenId);

                if (Utf8Parser.TryParse(source, out decimal value, out var bytesConsumed) &&
                    source.Length == bytesConsumed)
                {
                    return value;
                }

                throw new FormatException("Invalid Decimal format.");
            }

            return reader.ReadDecimal(tokenId);
        }

        public override void Write(DataWriter writer, decimal value)
        {
            if ((_numberHandling & NumberHandling.WriteAsString) != 0)
            {
                Span<byte> temp = stackalloc byte[64];
                writer.WriteNumberString(temp.Slice(0,
                    Utf8Helper.EncodeNumber(temp, value, FloatFormatHandling.SpecialFloatAsSymbol)));
            }
            else
            {
                writer.WriteDecimal(value);
            }
        }
    }

    private sealed class BooleanConverter : DataConverter<bool>
    {
        public override bool ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out bool value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Boolean format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, bool value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Boolean format.");
            }
        }

        public override bool Read(in DataReader reader, uint tokenId, bool existingValue)
        {
            return reader.ReadBoolean(tokenId);
        }

        public override void Write(DataWriter writer, bool value)
        {
            writer.WriteBoolean(value);
        }
    }


    private sealed class StringConverter : DataConverter<string>
    {
        public override string ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            return reader.ReadString(tokenId)!;
        }

        public override void WriteAsPropertyName(DataWriter writer, string value)
        {
            if (writer.Settings.DictionaryKeyPolicy != null)
            {
                writer.WriteString(writer.Settings.DictionaryKeyPolicy.ConvertName(value));
            }
            else
            {
                writer.WriteString(value);
            }
        }

        public override string? Read(in DataReader reader, uint tokenId, string? existingValue)
        {
            return reader.ReadString(tokenId);
        }

        public override void Write(DataWriter writer, string? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteString(value);
            }
        }
    }

    private sealed class CharConverter : DataConverter<char>
    {
        public override char ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out ushort value, out var bytes))
            {
                return (char)value;
            }

            throw new FormatException("Invalid Char format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, char value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Char format.");
            }
        }

        public override char Read(in DataReader reader, uint tokenId, char existingValue)
        {
            return reader.ReadChar(tokenId);
        }

        public override void Write(DataWriter writer, char value)
        {
            writer.WriteChar(value);
        }
    }


    private sealed class DateTimeConverter : DataConverter<DateTime>
    {
        public override DateTime Read(in DataReader reader, uint tokenId, DateTime existingValue)
        {
            var dateTime = reader.ReadDateTime(tokenId);

            switch (reader.Settings.DateTimeZoneHandling)
            {
                case DateTimeZoneHandling.Local:
                    if (dateTime.Kind == DateTimeKind.Unspecified)
                    {
                        return new DateTime(dateTime.Ticks, DateTimeKind.Local);
                    }

                    return dateTime.ToLocalTime();
                case DateTimeZoneHandling.Utc:
                    if (dateTime.Kind == DateTimeKind.Unspecified)
                    {
                        return new DateTime(dateTime.Ticks, DateTimeKind.Utc);
                    }

                    return dateTime.ToUniversalTime();
                case DateTimeZoneHandling.Unspecified:
                    return new DateTime(dateTime.Ticks, DateTimeKind.Unspecified);
                default:
                    return dateTime;
            }
        }

        public override void Write(DataWriter writer, DateTime value)
        {
            switch (writer.Settings.DateTimeZoneHandling)
            {
                case DateTimeZoneHandling.Local:
                    if (value.Kind == DateTimeKind.Unspecified)
                    {
                        writer.WriteDateTime(new DateTime(value.Ticks, DateTimeKind.Local));
                    }
                    else
                    {
                        writer.WriteDateTime(value.ToLocalTime());
                    }

                    break;
                case DateTimeZoneHandling.Utc:
                    if (value.Kind == DateTimeKind.Unspecified)
                    {
                        writer.WriteDateTime(new DateTime(value.Ticks, DateTimeKind.Utc));
                    }
                    else
                    {
                        writer.WriteDateTime(value.ToUniversalTime());
                    }

                    break;
                case DateTimeZoneHandling.Unspecified:
                    writer.WriteDateTime(new DateTime(value.Ticks, DateTimeKind.Unspecified));
                    break;
                default:
                    writer.WriteDateTime(value);
                    break;
            }
        }
    }

    internal sealed class DateTimeOffsetConverter : DataConverter<DateTimeOffset>
    {
        private static readonly byte[] DateTimeId = Encoding.UTF8.GetBytes("DateTime");
        private static readonly byte[] OffsetMinutesId = Encoding.UTF8.GetBytes("OffsetMinutes");
        private readonly DataContract _contract;

        private readonly DateTimeConverter _dateTimeConverter = new();
        private readonly DataConverter<int> _int32Converter;

        public DateTimeOffsetConverter(SerializerSettings settings, bool offsetMinutes)
        {
            _contract = settings.GetContract(typeof(DateTimeOffset));
            _int32Converter = new Int32Converter(settings.NumberHandling);
            OffsetMinutes = offsetMinutes;
        }

        public bool OffsetMinutes { get; set; }

        public override DateTimeOffset Read(in DataReader reader, uint tokenId, DateTimeOffset existingValue)
        {
            var token = reader.GetToken(tokenId);

            if (token.Type == DTokenType.Map)
            {
                DateTime? dateTime = null;
                int? offset = null;

                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    var name = reader.ReadUtf8String(kv.Key);
                    if (name.SequenceEqual(DateTimeId))
                    {
                        dateTime = _dateTimeConverter.Read(reader, kv.Value, default);
                    }
                    else
                    {
                        if (name.SequenceEqual(OffsetMinutesId))
                        {
                            offset = _int32Converter.Read(reader, kv.Value, default);
                        }
                    }
                }

                if (dateTime != null)
                {
                    if (offset != null)
                    {
                        var timeSpan = TimeSpan.FromMinutes(offset.Value);

                        if (timeSpan.Ticks != 0 || dateTime.Value.Kind == DateTimeKind.Local)
                        {
                            dateTime = dateTime.Value.ToUniversalTime();
                        }

                        return new DateTimeOffset(
                            new DateTime(dateTime.Value.Ticks + timeSpan.Ticks, DateTimeKind.Unspecified),
                            timeSpan);
                    }

                    return new DateTimeOffset(dateTime.Value);
                }

                throw new FormatException("Invalid DateTimeOffset format.");
            }

            if (token.Type == DTokenType.Array)
            {
                if (reader.GetValueCount(tokenId) != 2)
                {
                    throw new FormatException("Invalid DateTimeOffset format.");
                }

                var enumerator = reader.EnumerateArray(tokenId);

                enumerator.MoveNext();
                var dateTime = reader.ReadDateTime(enumerator.Current);
                enumerator.MoveNext();
                var offset = reader.ReadInt32(enumerator.Current);

                var timeSpan = TimeSpan.FromMinutes(offset);

                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);

                return new DateTimeOffset(
                    dateTime,
                    timeSpan);
            }

            return reader.ReadDateTimeOffset(tokenId);
        }

        public override void Write(DataWriter writer, DateTimeOffset value)
        {
            if (OffsetMinutes)
            {
                var typed = _contract.AlwaysWriteTypeDiscriminator;

                if (typed)
                {
                    writer.WriteStartMap(3);
                    writer.WriteTypeDiscriminator(_contract);
                }
                else
                {
                    writer.WriteStartMap(2);
                }

                writer.WriteString(DateTimeId);
                _dateTimeConverter.Write(writer,
                    new DateTime(value.DateTime.Ticks - value.Offset.Ticks, DateTimeKind.Utc));
                writer.WriteString(OffsetMinutesId);
                _int32Converter.Write(writer, (int)value.Offset.TotalMinutes);
                writer.WriteEndMap();
            }
            else
            {
                writer.WriteDateTimeOffset(value);
            }
        }
    }

    private sealed class TimeSpanConverter : DataConverter<TimeSpan>
    {
        public override TimeSpan Read(in DataReader reader, uint tokenId, TimeSpan existingValue)
        {
            var source = reader.ReadUtf8String(tokenId);

            if (Utf8Parser.TryParse(source, out TimeSpan value, out var bytes))
            {
                return value;
            }

            return existingValue;
        }

        public override void Write(DataWriter writer, TimeSpan value)
        {
            writer.WriteString(value.ToString());
        }
    }

    private sealed class GuidConverter : DataConverter<Guid>
    {
        public override Guid ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            if (Utf8Parser.TryParse(reader.ReadUtf8String(tokenId), out Guid value, out var bytes))
            {
                return value;
            }

            throw new FormatException("Invalid Guid format.");
        }

        public override void WriteAsPropertyName(DataWriter writer, Guid value)
        {
            Span<byte> temp = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, temp, out var bytes))
            {
                writer.WriteString(temp.Slice(0, bytes));
            }
            else
            {
                throw new FormatException("Invalid Guid format.");
            }
        }

        public override Guid Read(in DataReader reader, uint tokenId, Guid existingValue)
        {
            return reader.ReadGuid(tokenId);
        }

        public override void Write(DataWriter writer, Guid value)
        {
            writer.WriteGuid(value);
        }
    }

    private sealed class TypeConverter<T> : DataConverter<T> where T : Type
    {
        public TypeConverter(SerializerSettings settings)
        {
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            var name = reader.ReadString(tokenId);

            if (name == null)
            {
                return null;
            }

            return Type.GetType(name) as T;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteString(value.AssemblyQualifiedName!);
            }
        }
    }

    private sealed class MemberInfoConverter<T> : DataConverter<T> where T : MemberInfo
    {
        public MemberInfoConverter(SerializerSettings settings)
        {
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            var name = reader.ReadString(tokenId);

            if (name == null)
            {
                return null;
            }

            return null;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteString(value.ToString()!);
            }
        }
    }

    internal sealed class UriConverter : DataConverter<Uri>
    {
        public UriConverter()
        {
            OriginalString = true;
        }

        public UriConverter(bool originalString)
        {
            OriginalString = originalString;
        }

        public bool OriginalString { get; set; } = true;

        public override Uri? Read(in DataReader reader, uint tokenId, Uri? existingValue)
        {
            var value = reader.ReadString(tokenId);

            if (value == null)
            {
                return null;
            }

            return new Uri(value, UriKind.RelativeOrAbsolute);
        }

        public override void Write(DataWriter writer, Uri? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteString(OriginalString ? value.OriginalString : value.ToString());
            }
        }
    }

    private sealed class DBNullConverter : DataConverter<DBNull>
    {
        public override DBNull? Read(in DataReader reader, uint tokenId, DBNull? existingValue)
        {
            return DBNull.Value;
        }

        public override void Write(DataWriter writer, DBNull? value)
        {
            writer.WriteNull();
        }
    }

    private sealed class VersionConverter : DataConverter<Version>
    {
        public override Version ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = reader.ReadString(tokenId);

            if (value == null)
            {
                throw new FormatException("Invalid Version format.");
            }

            return Version.Parse(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, Version value)
        {
            var name = value.ToString();

            if (writer.Settings.DictionaryKeyPolicy != null)
            {
                name = writer.Settings.DictionaryKeyPolicy.ConvertName(name);
            }

            writer.WriteString(name);
        }

        public override Version? Read(in DataReader reader, uint tokenId, Version? existingValue)
        {
            var str = reader.ReadString(tokenId);

            if (str == null)
            {
                return null;
            }

            return Version.Parse(str);
        }

        public override void Write(DataWriter writer, Version? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteString(value.ToString());
            }
        }
    }

    private sealed class TimeOnlyConverter : DataConverter<TimeOnly>
    {
        public override TimeOnly ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = reader.ReadString(tokenId);

            if (value == null)
            {
                throw new FormatException("Invalid TimeOnly format.");
            }

            return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
        }

        public override void WriteAsPropertyName(DataWriter writer, TimeOnly value)
        {
            var name = writer.Settings.Culture == null
                ? value.ToString()
                : value.ToString("T", CultureInfo.InvariantCulture);

            if (writer.Settings.DictionaryKeyPolicy != null)
            {
                name = writer.Settings.DictionaryKeyPolicy.ConvertName(name);
            }

            writer.WriteString(name);
        }

        public override TimeOnly Read(in DataReader reader, uint tokenId, TimeOnly existingValue)
        {
            var str = reader.ReadString(tokenId);

            if (str == null)
            {
                throw new FormatException("Invalid TimeOnly format.");
            }

            return TimeOnly.Parse(str, CultureInfo.InvariantCulture);
        }

        public override void Write(DataWriter writer, TimeOnly value)
        {
            Span<byte> buffer = stackalloc byte[16];

            if (value.TryFormat(buffer, out var written, "T", CultureInfo.InvariantCulture))
            {
                writer.WriteString(buffer.Slice(0, written));
            }
            else
            {
                throw new FormatException("Invalid TimeOnly format.");
            }
        }
    }

    private sealed class DateOnlyConverter : DataConverter<DateOnly>
    {
        public override DateOnly ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = reader.ReadString(tokenId);

            if (value == null)
            {
                throw new FormatException("Invalid DateOnly format.");
            }

            return DateOnly.Parse(value, CultureInfo.InvariantCulture);
        }

        public override void WriteAsPropertyName(DataWriter writer, DateOnly value)
        {
            var name = writer.Settings.Culture == null
                ? value.ToString()
                : $"{value.Year:D4}-{value.Month:D2}-{value.Day:D2}";

            if (writer.Settings.DictionaryKeyPolicy != null)
            {
                name = writer.Settings.DictionaryKeyPolicy.ConvertName(name);
            }

            writer.WriteString(name);
        }

        public override DateOnly Read(in DataReader reader, uint tokenId, DateOnly existingValue)
        {
            var str = reader.ReadString(tokenId);

            if (str == null)
            {
                throw new FormatException("Invalid DateOnly format.");
            }

            return DateOnly.Parse(str, CultureInfo.InvariantCulture);
        }

        public override void Write(DataWriter writer, DateOnly value)
        {
            Span<byte> buffer = stackalloc byte[16];

            if (value.TryFormat(buffer, out var written, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            {
                writer.WriteString(buffer.Slice(0, written));
            }
            else
            {
                throw new FormatException("Invalid DateOnly format.");
            }
        }
    }

    private sealed class ObjectConverter : DataConverter<object>
    {
        private readonly DataContract _contract;

        public ObjectConverter(SerializerSettings settings)
        {
            IsReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Objects) != 0;

            _contract = settings.GetContract(typeof(object));
        }

        public bool IsReference { get; set; }

        public override object ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = reader.ReadString(tokenId);

            if (value == null)
            {
                throw new InvalidOperationException("Invalid object format.");
            }

            return value;
        }

        public override void WriteAsPropertyName(DataWriter writer, object value)
        {
            var name = value.ToString();

            if (name == null)
            {
                throw new InvalidOperationException("Invalid object format.");
            }

            if (writer.Settings.DictionaryKeyPolicy != null)
            {
                name = writer.Settings.DictionaryKeyPolicy.ConvertName(name);
            }

            writer.WriteString(name);
        }

        public override object? Read(in DataReader reader, uint tokenId, object? existingValue)
        {
            var token = reader.GetToken(tokenId);
            var handling = reader.Settings.UnknownObjectTypeHandling;

            if (handling == UnknownObjectTypeHandling.Element)
            {
                if (token.Kind == DTokenKind.Null)
                {
                    return null;
                }

                return reader.ReadElement(tokenId);
            }

            if (token.IsContainer)
            {
                if (token.Type == DTokenType.Array)
                {
                    if (existingValue != null && existingValue.GetType() != typeof(object))
                    {
                        return reader.ReadObject(tokenId, existingValue.GetType(), null);
                    }

                    if (handling == UnknownObjectTypeHandling.Dictionary)
                    {
                        var length = reader.GetValueCount(tokenId);

                        if (reader.Settings.UnknownArrayTypeHandling == UnknownArrayTypeHandling.SzArray)
                        {
                            var list = new object?[length];
                            var index = 0;

                            foreach (var valueId in reader.EnumerateArray(tokenId))
                            {
                                list[index++] = Read(reader, valueId, null);
                            }

                            return list;
                        }
                        else
                        {
                            var list = new List<object?>(reader.GetValueCount(tokenId));

                            foreach (var valueId in reader.EnumerateArray(tokenId))
                            {
                                list.Add(Read(reader, valueId, null));
                            }

                            return list;
                        }
                    }

                    return reader.ReadElement(tokenId).AsArray();
                }

                if (existingValue != null && existingValue.GetType() != typeof(object))
                {
                    return reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
                }

                if (_contract.IsPolymorphic)
                {
                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        var name = reader.ReadUtf8String(kv.Key);
                        if (name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                        {
                            var converter =
                                reader.Settings.GetConverter(_contract, reader.ReadUtf8String(kv.Value));

                            if (converter.TargetType == typeof(object))
                            {
                                return new DObject(reader.Settings).AsElement();
                            }

                            return converter.ReadObject(in reader, converter.TargetType, tokenId, null);
                        }
                    }
                }

                if (handling == UnknownObjectTypeHandling.Dictionary)
                {
                    var dict = new Dictionary<string, object?>();

                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        var name = reader.ReadUtf8String(kv.Key);
                        var value = Read(reader, kv.Value, null);

                        dict[Utf8Helper.GetUtf16String(name)] = value;
                    }

                    return dict;
                }

                return reader.ReadElement(tokenId).AsObject();
            }

            switch (token.Kind)
            {
                case DTokenKind.BigNumber:
                    return reader.ReadDouble(tokenId);
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    return reader.ReadString(tokenId);
                case DTokenKind.Integer:
                    return reader.ReadInt64(tokenId);
                case DTokenKind.Float:
                    {
                        if (token.Variant == DTokenVariant.FloatDecimal)
                        {
                            return reader.ReadDecimal(tokenId);
                        }

                        return reader.ReadDouble(tokenId);
                    }
                case DTokenKind.Boolean:
                    return reader.ReadBoolean(tokenId);
                case DTokenKind.Timestamp:
                    return reader.ReadDateTime(tokenId);
                case DTokenKind.ByteString:
                    return reader.ReadByteString(tokenId).ToArray();
                case DTokenKind.Null:
                    return null;
            }


            return null;
        }

        public override void Write(DataWriter writer, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                var type = value.GetType();

                if (type == typeof(object))
                {
                    if (IsReference && writer.TryWriteReference(value))
                    {
                        return;
                    }

                    var typed = _contract.AlwaysWriteTypeDiscriminator;

                    if (typed)
                    {
                        if (IsReference)
                        {
                            writer.WriteStartMap(2);
                            writer.WriteReferenceId();
                        }
                        else
                        {
                            writer.WriteStartMap(1);
                        }

                        writer.WriteTypeDiscriminator(_contract);
                    }
                    else
                    {
                        if (IsReference)
                        {
                            writer.WriteStartMap(1);
                            writer.WriteReferenceId();
                        }
                        else
                        {
                            writer.WriteStartMap(0);
                        }
                    }

                    writer.WriteEndMap();
                }
                else
                {
                    writer.WriteObject(value, typeof(object));
                }
            }
        }
    }
}