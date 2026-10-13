// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace REDox.Serialization.Converters;

class EnumTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (type.IsEnum)
        {
            return true;
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2071",
        Justification = "The application must preserve enum fields; enum types satisfy the struct constraint without an explicit parameterless constructor.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "The application must preserve public enum fields and their EnumMember attributes for runtime-supplied enum types.")]
    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        // Native AOT must use the non-generic converter even if the setting is enabled.
        if (!RuntimeFeature.IsDynamicCodeSupported || !settings.AllowDynamicGenericConverters)
        {
            return new DefaultConverter(type, settings);
        }

        switch (Type.GetTypeCode(type.GetEnumUnderlyingType()))
        {
            case TypeCode.SByte:
                return ConverterHelper.CreateConverter(typeof(SByteEnumConverter<>).MakeGenericType(type), settings,
                    this);
            case TypeCode.Int16:
                return ConverterHelper.CreateConverter(typeof(Int16EnumConverter<>).MakeGenericType(type), settings,
                    this);
            case TypeCode.Int32:
                return ConverterHelper.CreateConverter(typeof(Int32EnumConverter<>).MakeGenericType(type), settings,
                    this);
            case TypeCode.Int64:
                return ConverterHelper.CreateConverter(typeof(Int64EnumConverter<>).MakeGenericType(type), settings,
                    this);
            case TypeCode.Byte:
                return ConverterHelper.CreateConverter(typeof(ByteEnumConverter<>).MakeGenericType(type), settings,
                    this);
            case TypeCode.UInt16:
                return ConverterHelper.CreateConverter(typeof(UInt16EnumConverter<>).MakeGenericType(type),
                    settings,
                    this);
            case TypeCode.UInt32:
                return ConverterHelper.CreateConverter(typeof(UInt32EnumConverter<>).MakeGenericType(type),
                    settings,
                    this);
            case TypeCode.UInt64:
                return ConverterHelper.CreateConverter(typeof(UInt64EnumConverter<>).MakeGenericType(type),
                    settings,
                    this);
            default:
                throw new InvalidOperationException();
        }
    }

    private sealed class SByteEnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public SByteEnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (sbyte)_converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<sbyte, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, sbyte>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadSByte(tokenId);

            return Unsafe.BitCast<sbyte, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, sbyte>(value));
        }
    }

    private sealed class Int16EnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public Int16EnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (short)_converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<short, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, short>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadInt16(tokenId);

            return Unsafe.BitCast<short, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, short>(value));
        }
    }

    private sealed class Int32EnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public Int32EnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (int)_converter.Read(reader, tokenId);

            return Unsafe.BitCast<int, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, int>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadInt32(tokenId);

            return Unsafe.BitCast<int, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, int>(value));
        }
    }

    private sealed class Int64EnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public Int64EnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = _converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<long, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, long>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadInt64(tokenId);

            return Unsafe.BitCast<long, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, long>(value));
        }
    }

    private sealed class ByteEnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public ByteEnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (byte)_converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<byte, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, byte>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadByte(tokenId);

            return Unsafe.BitCast<byte, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, byte>(value));
        }
    }

    private sealed class UInt16EnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public UInt16EnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (ushort)_converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<ushort, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, ushort>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadUInt16(tokenId);

            return Unsafe.BitCast<ushort, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, ushort>(value));
        }
    }

    private sealed class UInt32EnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        private readonly EnumConverterHelper _converter;

        public UInt32EnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (uint)_converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<uint, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, Unsafe.BitCast<T, uint>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadUInt32(tokenId);

            return Unsafe.BitCast<uint, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteInt64(Unsafe.BitCast<T, uint>(value));
        }
    }

    private class UInt64EnumConverter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
        T> : DataConverter<T> where T : struct
    {
        protected readonly EnumConverterHelper _converter;

        public UInt64EnumConverter(SerializerSettings settings, DataConverterFactory factory)
        {
            _converter = new EnumConverterHelper(typeof(T), null, true, settings);
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var value = (ulong)_converter.Read(reader, tokenId, true);

            return Unsafe.BitCast<ulong, T>(value);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            _converter.Write(writer, (long)Unsafe.BitCast<T, ulong>(value), true);
        }

        public override T Read(in DataReader reader, uint tokenId, T existingValue)
        {
            var value = reader.ReadUInt64(tokenId);

            return Unsafe.BitCast<ulong, T>(value);
        }

        public override void Write(DataWriter writer, T value)
        {
            writer.WriteUInt64(Unsafe.BitCast<T, ulong>(value));
        }
    }

    private class DefaultConverter : DataConverter
    {
        private readonly EnumConverterHelper _converter;
        private readonly bool _isULong;
        private readonly Type _type;

        public DefaultConverter(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]
            Type type, SerializerSettings settings)
        {
            _type = type;
            _converter = new EnumConverterHelper(type, null, true, settings);
            _isULong = Type.GetTypeCode(type.GetEnumUnderlyingType()) == TypeCode.UInt64;
        }

        protected internal override Type TargetType => _type;

        public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
        {
            return ToEnum(_converter.Read(reader, tokenId, true));
        }

        public override void WriteObjectAsPropertyName(DataWriter writer, object? value)
        {
            _converter.Write(writer, ToInt64(value!), true);
        }

        public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
        {
            return ToEnum(_converter.Read(reader, tokenId));
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var underlying = ToInt64(value);

            if (_isULong)
            {
                writer.WriteUInt64((ulong)underlying);
            }
            else
            {
                writer.WriteInt64(underlying);
            }
        }

        private object ToEnum(long value)
        {
            return _isULong ? Enum.ToObject(_type, (ulong)value) : Enum.ToObject(_type, value);
        }

        private long ToInt64(object value)
        {
            return _isULong ? (long)Convert.ToUInt64(value) : Convert.ToInt64(value);
        }
    }
}