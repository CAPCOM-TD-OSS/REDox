// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace REDox.Serialization.Converters;

sealed class SpanTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var genericDef = type.GetGenericTypeDefinition();

        if ( /*genericDef == typeof(System.Span<>) ||
            genericDef == typeof(System.ReadOnlySpan<>) ||*/
            genericDef == typeof(Memory<>) ||
            genericDef == typeof(ReadOnlyMemory<>))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var genericDef = type.GetGenericTypeDefinition();
        var readOnly = genericDef == typeof(ReadOnlyMemory<>);

        if (!readOnly && genericDef != typeof(Memory<>))
        {
            throw new NotSupportedException($"The type '{type}' is not supported by SpanTypeConverter.");
        }

        var elementType = type.GetGenericArguments()[0];

        // Common element types are instantiated statically so that they work with NativeAOT.
        var known = CreateKnownConverter(readOnly, elementType, settings);

        if (known != null)
        {
            return known;
        }

        if (!RuntimeFeature.IsDynamicCodeSupported || !settings.AllowDynamicGenericConverters)
        {
            throw new NotSupportedException(
                $"The type '{type}' cannot be converted because dynamic code is unavailable. " +
                "Use Memory<T> or ReadOnlyMemory<T> with a primitive element type, or an array.");
        }

        return CreateDynamicConverter(readOnly, elementType, settings);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "Memory converters only require their public constructor, which is explicitly preserved.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(MemoryConverter<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ReadOnlyMemoryConverter<>))]
    [RequiresDynamicCode(ConverterHelper.DynamicCode)]
    private static DataConverter CreateDynamicConverter(bool readOnly, Type elementType, SerializerSettings settings)
    {
        return ConverterHelper.CreateConverter(
            (readOnly ? typeof(ReadOnlyMemoryConverter<>) : typeof(MemoryConverter<>)).MakeGenericType(elementType),
            settings);
    }

    private static DataConverter? CreateKnownConverter(bool readOnly, Type elementType, SerializerSettings settings)
    {
        if (elementType == typeof(byte))
        {
            return Create<byte>(readOnly, settings);
        }

        if (elementType == typeof(sbyte))
        {
            return Create<sbyte>(readOnly, settings);
        }

        if (elementType == typeof(short))
        {
            return Create<short>(readOnly, settings);
        }

        if (elementType == typeof(ushort))
        {
            return Create<ushort>(readOnly, settings);
        }

        if (elementType == typeof(int))
        {
            return Create<int>(readOnly, settings);
        }

        if (elementType == typeof(uint))
        {
            return Create<uint>(readOnly, settings);
        }

        if (elementType == typeof(long))
        {
            return Create<long>(readOnly, settings);
        }

        if (elementType == typeof(ulong))
        {
            return Create<ulong>(readOnly, settings);
        }

        if (elementType == typeof(float))
        {
            return Create<float>(readOnly, settings);
        }

        if (elementType == typeof(double))
        {
            return Create<double>(readOnly, settings);
        }

        if (elementType == typeof(bool))
        {
            return Create<bool>(readOnly, settings);
        }

        if (elementType == typeof(char))
        {
            return Create<char>(readOnly, settings);
        }

        if (elementType == typeof(string))
        {
            return Create<string>(readOnly, settings);
        }

        if (elementType == typeof(Guid))
        {
            return Create<Guid>(readOnly, settings);
        }

        return null;
    }

    private static DataConverter Create<T>(bool readOnly, SerializerSettings settings)
    {
        return readOnly ? new ReadOnlyMemoryConverter<T>(settings) : new MemoryConverter<T>(settings);
    }

    private class MemoryConverter<T> : DataConverter<Memory<T?>>
    {
        private readonly DataConverter<T> _valueConverter;

        public MemoryConverter(SerializerSettings settings)
        {
            _valueConverter = (DataConverter<T>)settings.GetConverter(typeof(T));
        }

        public override Memory<T?> Read(in DataReader reader, uint tokenId, Memory<T?> existingValue)
        {
            if (typeof(T) == typeof(byte))
            {
                var data = reader.ReadByteString(tokenId).ToArray().AsMemory();

                return Unsafe.As<Memory<byte>, Memory<T?>>(ref data);
            }
            else
            {
                var data = new T?[reader.GetValueCount(tokenId)];
                var i = 0;

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    data[i++] = _valueConverter.Read(reader, valueId, default);
                }

                return data!.AsMemory();
            }
        }

        public override void Write(DataWriter writer, Memory<T?> value)
        {
            if (typeof(T) == typeof(byte))
            {
                writer.WriteByteString(Unsafe.As<Memory<T?>, Memory<byte>>(ref value).Span);
            }
            else
            {
                writer.WriteStartArray(value.Length);
                foreach (var v in value.Span)
                {
                    _valueConverter.Write(writer, v);
                }

                writer.WriteEndArray();
            }
        }
    }

    private class ReadOnlyMemoryConverter<T> : DataConverter<ReadOnlyMemory<T?>>
    {
        private readonly DataConverter<T> _valueConverter;

        public ReadOnlyMemoryConverter(SerializerSettings settings)
        {
            _valueConverter = (DataConverter<T>)settings.GetConverter(typeof(T));
        }

        public override ReadOnlyMemory<T?> Read(in DataReader reader, uint tokenId,
            ReadOnlyMemory<T?> existingValue)
        {
            if (typeof(T) == typeof(byte))
            {
                var data = (ReadOnlyMemory<byte>)reader.ReadByteString(tokenId).ToArray().AsMemory();

                return Unsafe.As<ReadOnlyMemory<byte>, ReadOnlyMemory<T?>>(ref data);
            }
            else
            {
                var data = new T?[reader.GetValueCount(tokenId)];
                var i = 0;

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    data[i++] = _valueConverter.Read(reader, valueId, default);
                }

                return data!.AsMemory();
            }
        }

        public override void Write(DataWriter writer, ReadOnlyMemory<T?> value)
        {
            if (typeof(T) == typeof(byte))
            {
                writer.WriteByteString(Unsafe.As<ReadOnlyMemory<T?>, ReadOnlyMemory<byte>>(ref value).Span);
            }
            else
            {
                writer.WriteStartArray(value.Length);
                foreach (var v in value.Span)
                {
                    _valueConverter.Write(writer, v);
                }

                writer.WriteEndArray();
            }
        }
    }
}