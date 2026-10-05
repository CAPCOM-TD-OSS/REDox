// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.DataContractJson;

sealed class ObjectConverter : DataConverter<object>
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
            throw new InvalidOperationException();
        }

        return value;
    }

    public override void WriteAsPropertyName(DataWriter writer, object value)
    {
        var name = value.ToString();

        if (name == null)
        {
            throw new InvalidOperationException();
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

            return reader.ReadElement(tokenId).Clone();
        }

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Map)
            {
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

                            return converter.ReadObject(reader, converter.TargetType, tokenId, null);
                        }
                    }
                }

                return new object();
            }

            if (existingValue != null && existingValue.GetType() != typeof(object))
            {
                return reader.ReadObject(tokenId, existingValue.GetType(), null);
            }

            var length = reader.GetValueCount(tokenId);

            if (reader.Settings.UnknownArrayTypeHandling == UnknownArrayTypeHandling.SzArray)
            {
                var list = new object?[length];
                var index = 0;

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    list[index++] = Read(reader, valueId, default);
                }

                return list;
            }
            else
            {
                var list = new List<object?>(reader.GetValueCount(tokenId));

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    list.Add(Read(reader, valueId, default));
                }

                return list;
            }
        }

        switch (token.Kind)
        {
            case DTokenKind.BigNumber:
                return reader.ReadDouble(tokenId);
            case DTokenKind.String:
            case DTokenKind.Symbol:
                return reader.ReadString(tokenId);
            case DTokenKind.Integer:
                {
                    var value = reader.ReadInt64(tokenId);
                    if (value >= int.MinValue && value <= int.MaxValue)
                    {
                        return (int)value;
                    }

                    return value;
                }
            case DTokenKind.Float:
                {
                    if (token.Variant == DTokenVariant.FloatDecimal)
                    {
                        return reader.ReadDecimal(tokenId);
                    }

                    var value = reader.ReadDouble(tokenId);

                    if (value < (double)decimal.MinValue || value > (double)decimal.MaxValue)
                    {
                        return value;
                    }

                    return reader.ReadDecimal(tokenId);
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
                writer.WriteStartMap(0);
                writer.WriteEndMap();
            }
            else
            {
                writer.WriteObject(value, typeof(object));
            }
        }
    }
}