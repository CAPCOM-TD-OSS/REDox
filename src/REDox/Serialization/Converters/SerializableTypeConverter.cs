// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using REDox.Serialization.Metadata;

#pragma warning disable SYSLIB0050

namespace REDox.Serialization.Converters;

sealed class SerializableTypeConverter : DataConverterFactory
{
    private static readonly ElementFormatter s_formatter = new();

    public override bool CanConvert(Type type)
    {
        return type.IsDefined(typeof(SerializableAttribute), true) && type.IsAssignableTo(typeof(ISerializable));
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        return ConverterHelper.CreateConverter(typeof(SerializableConverter<>).MakeGenericType(type), settings);
    }

    private class SerializableConverter<T> : DataConverter<T> where T : ISerializable
    {
        private readonly DataContract _contract;
        private readonly Func<SerializationInfo, StreamingContext, T> _generator;
        private bool _isReference;
        private DataProperty? _property;

        public SerializableConverter(SerializerSettings settings)
        {
            _generator = CreateFactory();
            _contract = settings.GetContract(typeof(T));
            _isReference = _contract.IsReference;

            if ((settings.PreserveReferencesHandling & PreserveReferencesHandling.Objects) != 0)
            {
                _isReference = true;
            }
        }

        protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
            SerializerSettings settings)
        {
            if (property.IsReadOnly &&
                (settings.PreserveReferencesHandling & PreserveReferencesHandling.IgnoreReadOnly) != 0 &&
                _isReference)
            {
                var converter = (SerializableConverter<T>)MemberwiseClone();
                converter._isReference = false;
                converter._property = property;
                return converter;
            }

            return base.ResolvePropertyConverter(property, settings);
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return default;
            }

            if (existingValue != null)
            {
                var type = existingValue.GetType();
                if (type != typeof(T))
                {
                    return (T?)reader.Settings.GetConverter(type)
                        .ReadObject(in reader, typeof(T), tokenId, existingValue);
                }
            }

            var info = new SerializationInfo(typeof(T), s_formatter);
            uint refId = 0;

            foreach (var kv in reader.EnumerateMap(tokenId))
            {
                var name = reader.ReadUtf8String(kv.Key);

                if (name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                {
                    if (reader.ReadTypedObject(_contract, tokenId, kv.Value, ref existingValue))
                    {
                        return existingValue;
                    }

                    continue;
                }

                if (name.SequenceEqual(Utf8Helper.RefTag))
                {
                    return (T?)reader.ReadReference(kv.Value);
                }

                if (name.SequenceEqual(Utf8Helper.IdTag))
                {
                    refId = kv.Value;
                    continue;
                }

                info.AddValue(Utf8Helper.GetUtf16String(name), reader.ReadElement(kv.Value).Clone());
            }

            var instance = _generator(info, reader.Settings.Context);

            if (refId > 0)
            {
                reader.AddReference(refId, instance);
            }

            return instance;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                if (writer.Settings.IncludeDerivedProperties || _contract.IsPolymorphic)
                {
                    var type = value.GetType();
                    if (type != typeof(T))
                    {
                        writer.Settings.GetConverter(type, _property).WriteObject(writer, typeof(T), value);
                        return;
                    }
                }

                Write(writer, value, false);
            }
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                Write(writer, (T)value, objectType != typeof(T));
            }
        }

        private void Write(DataWriter writer, T value, bool typeNeeded)
        {
            var info = new SerializationInfo(typeof(T), s_formatter);

            value.GetObjectData(info, writer.Settings.Context);

            if (_isReference && writer.TryWriteReference(value))
            {
                return;
            }

            var typed = _contract.AlwaysWriteTypeDiscriminator || (typeNeeded && _contract.IsPolymorphic);
            var count = info.MemberCount;

            if (typed)
            {
                if (_isReference)
                {
                    writer.WriteStartMap(count + 2);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(count + 1);
                }

                writer.WriteTypeDiscriminator(_contract);
            }
            else
            {
                if (_isReference)
                {
                    writer.WriteStartMap(count + 1);
                    writer.WriteReferenceId();
                }
                else
                {
                    writer.WriteStartMap(count);
                }
            }

            foreach (var v in info)
            {
                writer.WriteString(v.Name);

                if (v.Value == null)
                {
                    writer.WriteNull();
                }
                else
                {
                    writer.WriteObject(v.Value, typeof(object));
                }
            }

            writer.WriteEndMap();
        }

        private Func<SerializationInfo, StreamingContext, T> CreateFactory()
        {
            var constructor = typeof(T).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(SerializationInfo), typeof(StreamingContext) },
                null
            )!;

            var serializationInfoParam = Expression.Parameter(typeof(SerializationInfo), "info");
            var streamingContextParam = Expression.Parameter(typeof(StreamingContext), "context");

            var newExpression = Expression.New(constructor, serializationInfoParam, streamingContextParam);
            var lambdaExpression =
                Expression.Lambda<Func<SerializationInfo, StreamingContext, T>>(newExpression,
                    serializationInfoParam,
                    streamingContextParam);

            return lambdaExpression.Compile();
        }
    }

    private class ElementFormatter : IFormatterConverter
    {
        public object Convert(object value, Type type)
        {
            var result = ((DElement)value).ToObject(type);

            if (result == null)
            {
                return null!;
            }

            return result;
        }

        public object Convert(object value, TypeCode typeCode)
        {
            switch (typeCode)
            {
                case TypeCode.Boolean:
                    return ToBoolean(value);
                case TypeCode.Byte:
                    return ToByte(value);
                case TypeCode.SByte:
                    return ToSByte(value);
                case TypeCode.Object:
                    return value;
                case TypeCode.String:
                    return ToString(value)!;
                case TypeCode.DateTime:
                    return ToDateTime(value);
                case TypeCode.Decimal:
                    return ToDecimal(value);
                case TypeCode.Char:
                    return ToChar(value);
                case TypeCode.Int16:
                    return ToInt16(value);
                case TypeCode.UInt16:
                    return ToUInt16(value);
                case TypeCode.Int32:
                    return ToInt32(value);
                case TypeCode.UInt32:
                    return ToUInt32(value);
                case TypeCode.Int64:
                    return ToInt64(value);
                case TypeCode.UInt64:
                    return ToUInt64(value);
                case TypeCode.Double:
                    return ToDouble(value);
                case TypeCode.Single:
                    return ToSingle(value);
                case TypeCode.DBNull:
                    return DBNull.Value;
                default:
                    throw new InvalidCastException();
            }
        }

        public bool ToBoolean(object value)
        {
            return ((DElement)value).GetBoolean();
        }

        public byte ToByte(object value)
        {
            return ((DElement)value).GetByte();
        }

        public char ToChar(object value)
        {
            return (char)((DElement)value).AsValue();
        }

        public DateTime ToDateTime(object value)
        {
            return ((DElement)value).To<DateTime>();
        }

        public decimal ToDecimal(object value)
        {
            return ((DElement)value).GetDecimal();
        }

        public double ToDouble(object value)
        {
            return ((DElement)value).GetDouble();
        }

        public short ToInt16(object value)
        {
            return ((DElement)value).GetInt16();
        }

        public int ToInt32(object value)
        {
            return ((DElement)value).GetInt32();
        }

        public long ToInt64(object value)
        {
            return ((DElement)value).GetInt64();
        }

        public uint ToUInt32(object value)
        {
            return ((DElement)value).GetUInt32();
        }

        public ulong ToUInt64(object value)
        {
            return ((DElement)value).GetUInt64();
        }

        public sbyte ToSByte(object value)
        {
            return ((DElement)value).GetSByte();
        }

        public float ToSingle(object value)
        {
            return ((DElement)value).GetSingle();
        }

        public string? ToString(object value)
        {
            var element = (DElement)value;

            if (element.Token.Kind == DTokenKind.Null)
            {
                return null!;
            }

            return element.GetString();
        }

        public ushort ToUInt16(object value)
        {
            return ((DElement)value).GetUInt16();
        }
    }
}