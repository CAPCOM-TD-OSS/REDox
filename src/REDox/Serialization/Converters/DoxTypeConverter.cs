// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using REDox.Json;

namespace REDox.Serialization.Converters;

sealed class DoxTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (type == typeof(DElement) || type.IsAssignableTo(typeof(IDoxNode)))
        {
            return true;
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "DoxNodeConverter<> is instantiated only for IDoxNode implementations; its parameterless constructor is explicitly preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2071",
        Justification = "DoxNodeConverter<> is instantiated only for IDoxNode implementations; its parameterless constructor is explicitly preserved.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "The generic instantiation is guarded by RuntimeFeature.IsDynamicCodeSupported and AllowDynamicGenericConverters.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(DoxNodeConverter<>))]
    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(DElement))
        {
            return new ElementConverter();
        }

        if (type == typeof(DValue))
        {
            return new DoxValueConverter();
        }

        if (type == typeof(DObject))
        {
            return new ObjectElementConverter();
        }

        if (type == typeof(DMap))
        {
            return new MapElementConverter();
        }

        if (type == typeof(DArray))
        {
            return new ArrayElementConverter();
        }

        if (type == typeof(IDoxNode))
        {
            // Statically instantiated so that it works with NativeAOT and trimming.
            return new DoxNodeConverter<IDoxNode>();
        }

        if (type.IsAssignableTo(typeof(IDoxNode)) &&
            RuntimeFeature.IsDynamicCodeSupported && settings.AllowDynamicGenericConverters)
        {
            return (DataConverter)Activator.CreateInstance(typeof(DoxNodeConverter<>).MakeGenericType(type))!;
        }

        throw new NotSupportedException(
            $"The type '{type}' cannot be converted because dynamic code is unavailable. " +
            "Use DElement, DValue, DObject, DMap, DArray or IDoxNode.");
    }

    private static void WriteElement(DataWriter writer, DElement value)
    {
        if (!value.IsValid)
        {
            throw new ObjectDisposedException(nameof(DElement));
        }

        var requiresStringKeys = writer.RequiresStringKeys;

        var tokenId = value.Id;
        var source = value.Document;
        var index = 0;
        var latestId = tokenId + 1;
        var parentId = 0U;

        using var stack =
            new Helper.LocalStack<(uint latestId, uint parentId, int index)>(
                stackalloc (uint latestId, uint parentId, int index)[16], int.MaxValue);

        for (;;)
        {
            var token = source.GetToken(tokenId);

            if (token.IsContainer)
            {
                if (token.Type == DTokenType.Array)
                {
                    if (token.IsExtended)
                    {
                        var arr = source.GetElementArray(tokenId);

                        if (index == 0)
                        {
                            parentId = tokenId;
                            writer.WriteStartArray(arr.Count);
                        }

                        if (index < arr.Count)
                        {
                            tokenId = arr.GetValueInternal(index);
                            index++;

                            if (source.GetToken(tokenId).IsContainer)
                            {
                                stack.Push((latestId, parentId, index));
                                index = 0;
                                latestId = tokenId + 1;
                            }

                            continue;
                        }

                        if (index == arr.Count)
                        {
                            writer.WriteEndArray();

                            if (stack.Count == 0)
                            {
                                return;
                            }

                            (latestId, parentId, index) = stack.Pop();
                            tokenId = parentId;
                            continue;
                        }
                    }
                    else
                    {
                        if (index == 0)
                        {
                            parentId = tokenId;
                            writer.WriteStartArray(token.Count);
                        }

                        if (index < token.Count)
                        {
                            tokenId = latestId;

                            while (source.GetToken(tokenId).IsIgnore)
                            {
                                var ignored = source.GetToken(tokenId);
                                tokenId = ignored.Kind == DTokenKind.Control && !ignored.IsExtended && ignored.JumpId != 0
                                    ? ignored.JumpId
                                    : tokenId + 1;
                            }

                            latestId = source.NextToken(tokenId);
                            index++;

                            if (source.GetToken(tokenId).IsContainer)
                            {
                                stack.Push((latestId, parentId, index));
                                index = 0;
                                latestId = tokenId + 1;
                            }

                            continue;
                        }

                        if (index == token.Count)
                        {
                            writer.WriteEndArray();

                            if (stack.Count == 0)
                            {
                                return;
                            }

                            (latestId, parentId, index) = stack.Pop();
                            tokenId = parentId;
                            continue;
                        }
                    }
                }
                else
                {
                    if (token.IsExtended)
                    {
                        var node = source.GetExtendContainer(token);

                        if (index == 0)
                        {
                            parentId = tokenId;
                            writer.WriteStartMap(node.Count);
                        }

                        if (index < node.Count * 2)
                        {
                            if ((index & 1) == 0)
                            {
                                var keyElement = new DElement(node.Document, node.GetKeyId(index / 2));
                                var keyToken = keyElement.Token;

                                if (requiresStringKeys)
                                {
                                    if (keyToken.IsContainer)
                                    {
                                        writer.WriteString(JsonDocument.Encode(keyElement));
                                    }
                                    else
                                    {
                                        if (keyToken.Kind == DTokenKind.String && keyToken.IsExtended)
                                        {
                                            writer.WriteString(keyElement.Document.GetStringValue(keyElement.Id));
                                        }
                                        else
                                        {
                                            writer.WriteString(keyElement.Document.GetUtf8BytesValue(keyElement.Id));
                                        }
                                    }

                                    index++;
                                    continue;
                                }

                                tokenId = keyElement.Id;
                            }
                            else
                            {
                                tokenId = node.GetValueId(index / 2);
                            }

                            index++;
                            if (source.GetToken(tokenId).IsContainer)
                            {
                                stack.Push((latestId, parentId, index));
                                index = 0;
                                latestId = tokenId + 1;
                            }

                            continue;
                        }

                        if (index == node.Count * 2)
                        {
                            writer.WriteEndMap();

                            if (stack.Count == 0)
                            {
                                return;
                            }

                            (latestId, parentId, index) = stack.Pop();
                            tokenId = parentId;
                            continue;
                        }
                    }
                    else
                    {
                        if (index == 0)
                        {
                            parentId = tokenId;
                            writer.WriteStartMap(token.Count);
                        }

                        if (index < token.Count * 2)
                        {
                            if ((index & 1) == 0)
                            {
                                tokenId = latestId;
                                while (source.GetToken(tokenId).IsIgnore)
                                {
                                    var ignored = source.GetToken(tokenId);
                                    tokenId = ignored.Kind == DTokenKind.Control && !ignored.IsExtended && ignored.JumpId != 0
                                        ? ignored.JumpId
                                        : tokenId + 1;
                                }

                                latestId = source.NextToken(tokenId);
                                index++;

                                if (source.GetToken(tokenId).IsContainer)
                                {
                                    stack.Push((latestId, parentId, index));
                                    index = 0;
                                    latestId = tokenId + 1;
                                }
                            }
                            else
                            {
                                tokenId = latestId;
                                while (source.GetToken(tokenId).IsIgnore)
                                {
                                    var ignored = source.GetToken(tokenId);
                                    tokenId = ignored.Kind == DTokenKind.Control && !ignored.IsExtended && ignored.JumpId != 0
                                        ? ignored.JumpId
                                        : tokenId + 1;
                                }

                                latestId = source.NextToken(tokenId);
                                index++;

                                if (source.GetToken(tokenId).IsContainer)
                                {
                                    stack.Push((latestId, parentId, index));
                                    index = 0;
                                    latestId = tokenId + 1;
                                }
                            }

                            continue;
                        }

                        if (index == token.Count * 2)
                        {
                            writer.WriteEndMap();

                            if (stack.Count == 0)
                            {
                                return;
                            }

                            (latestId, parentId, index) = stack.Pop();
                            tokenId = parentId;
                            continue;
                        }
                    }
                }
            }
            else
            {
                switch (token.Kind)
                {
                    case DTokenKind.Boolean:
                        writer.WriteBoolean(source.GetBooleanValue(tokenId));
                        break;
                    case DTokenKind.Integer:
                        if (token.Variant == DTokenVariant.IntegerUnsigned)
                        {
                            writer.WriteUInt64(source.GetUnsignedIntegerValue(tokenId));
                        }
                        else
                        {
                            writer.WriteInt64(source.GetSignedIntegerValue(tokenId));
                        }

                        break;
                    case DTokenKind.InlineFloat:
                        writer.WriteDouble(source.GetFloatingValue(tokenId));
                        break;
                    case DTokenKind.Float:
                        switch (token.FloatKind)
                        {
                            case FloatKind.Half:
                                writer.WriteHalf((Half)source.GetFloatingValue(tokenId));
                                break;
                            case FloatKind.Single:
                            case FloatKind.Inherit:
                                writer.WriteSingle((float)source.GetFloatingValue(tokenId));
                                break;
                            case FloatKind.Decimal:
                                writer.WriteDecimal(source.GetDecimalValue(tokenId));
                                break;
                            default:
                                writer.WriteDouble(source.GetFloatingValue(tokenId));
                                break;
                        }

                        break;
                    case DTokenKind.String:
                    case DTokenKind.Symbol:
                        if (token.IsExtended)
                        {
                            writer.WriteString(source.GetStringValue(tokenId));
                        }
                        else
                        {
                            writer.WriteString(source.GetUtf8BytesValue(tokenId));
                        }

                        break;
                    case DTokenKind.BigNumber:
                        writer.WriteBigNumber(source.GetBigNumberValue(tokenId), token.BigNumberKind);
                        break;
                    case DTokenKind.ByteString:
                        writer.WriteByteString(source.GetByteStringValue(tokenId),
                            token.ByteStringKind);
                        break;
                    case DTokenKind.Timestamp:
                        if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                        {
                            writer.WriteDateTimeOffset(source.GetDateTimeOffsetValue(tokenId));
                        }
                        else
                        {
                            writer.WriteDateTime(source.GetDateTimeValue(tokenId));
                        }

                        break;
                    case DTokenKind.Null:
                        writer.WriteNull();
                        break;
                }
            }


            if (parentId == 0)
            {
                return;
            }

            tokenId = parentId;
        }
    }

    private sealed class DoxNodeConverter<T> : DataConverter<T> where T : IDoxNode
    {
        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return default;
            }

            var value = DoxNodeDocument.CreateFrom(reader.ReadElement(tokenId), reader.Settings).RootElement.AsValue();

            return (T)(object)value;
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteElement(writer, value.AsElement());
        }
    }

    private class ObjectElementConverter : DataConverter<DObject>
    {
        public override DObject? Read(in DataReader reader, uint tokenId, DObject? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return null;
            }

            var element = reader.ReadElement(tokenId);

            if (element.Token.Type != DTokenType.Map)
            {
                throw new InvalidOperationException("Expected a map token.");
            }

            return element.Clone().AsObject();
        }

        public override void Write(DataWriter writer, DObject? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteElement(writer, value);
        }
    }

    private class MapElementConverter : DataConverter<DMap>
    {
        public override DMap? Read(in DataReader reader, uint tokenId, DMap? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return null;
            }

            var element = reader.ReadElement(tokenId);

            if (element.Token.Type != DTokenType.Map)
            {
                throw new InvalidOperationException("Expected a map token.");
            }

            return element.Clone().AsMap();
        }

        public override void Write(DataWriter writer, DMap? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteElement(writer, value);
        }
    }

    private class ArrayElementConverter : DataConverter<DArray>
    {
        public override DArray? Read(in DataReader reader, uint tokenId, DArray? existingValue)
        {
            if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
            {
                return null;
            }

            var element = reader.ReadElement(tokenId);

            if (element.Token.Type != DTokenType.Array)
            {
                throw new InvalidOperationException("Expected an array token.");
            }

            return element.Clone().AsArray();
        }

        public override void Write(DataWriter writer, DArray? value)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            WriteElement(writer, value);
        }
    }

    private class DoxValueConverter : DataConverter<DValue>
    {
        public override DValue Read(in DataReader reader, uint tokenId, DValue existingValue)
        {
            return reader.ReadElement(tokenId).Clone().AsValue();
        }

        public override void Write(DataWriter writer, DValue value)
        {
            WriteElement(writer, value);
        }
    }

    private class ElementConverter : DataConverter<DElement>
    {
        public override DElement Read(in DataReader reader, uint tokenId, DElement existingValue)
        {
            return reader.ReadElement(tokenId).Clone();
        }

        public override void Write(DataWriter writer, DElement value)
        {
            WriteElement(writer, value);
        }
    }
}