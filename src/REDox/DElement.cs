// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using REDox.Json;
using REDox.Serialization;

namespace REDox;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
[DebuggerTypeProxy(typeof(DebugView))]
public readonly partial struct DElement : IEquatable<DElement>
{
    internal DElement(Document document, uint id)
    {
        Document = document;
        Id = id;
        Version = document.Version;
    }

    internal Document Document { get; }

    internal uint Id { get; }

    internal int Version { get; }

    public T To<T>(T target)
    {
        ThrowIfInvalid();

        return Serializer.DeserializeToInternal(this, target);
    }

    public T? To<T>()
    {
        ThrowIfInvalid();

        return Serializer.DeserializeInternal<T>(this);
    }

    public object? ToObject(Type returnType)
    {
        ThrowIfInvalid();

        ArgumentNullException.ThrowIfNull(returnType);

        return Serializer.DeserializeInternal(this, returnType);
    }

    public object ToObject(Type returnType, object target)
    {
        ThrowIfInvalid();

        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(target);

        return Serializer.DeserializeToInternal(this, returnType, target);
    }

    public DToken Token
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Document.GetToken(Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DArray AsArray()
    {
        ThrowIfInvalid();

        return Document.GetElementArray(Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DObject AsObject()
    {
        ThrowIfInvalid();

        return Document.GetElementObject(Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DValue AsValue()
    {
        ThrowIfInvalid();

        return DValue.Create(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DMap AsMap()
    {
        ThrowIfInvalid();

        return Document.GetElementMap(Id);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DContainer AsContainer()
    {
        ThrowIfInvalid();

        return Document.GetElementContainer(Id);
    }

    public Document.ArrayEnumerator EnumerateArray()
    {
        return new Document.ArrayEnumerator(Document, Id);
    }

    public Document.MapEnumerator EnumerateMap()
    {
        return new Document.MapEnumerator(Document, Id);
    }

    public Document.ObjectEnumerator EnumerateObject()
    {
        return new Document.ObjectEnumerator(Document, Id);
    }

    public Document.TriviaEnumerator EnumerateTrivia()
    {
        return new Document.TriviaEnumerator(Document, Id);
    }

    public bool GetBoolean()
    {
        ThrowIfInvalid();

        if (Document.TryGetBooleanExact(Id, out var value))
        {
            return value;
        }

        throw new InvalidOperationException("The element does not contain a Boolean value.");
    }

    public byte GetByte()
    {
        if (TryGetByte(out var value))
        {
            return value;
        }

        throw new FormatException();
    }

    public bool TryGetByte(out byte value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetUnsignedIntegerExact(Id, out var ivalue))
        {
            if (ivalue <= byte.MaxValue)
            {
                value = (byte)ivalue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public sbyte GetSByte()
    {
        if (TryGetSByte(out var value))
        {
            return value;
        }

        throw new FormatException();
    }

    public bool TryGetSByte(out sbyte value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetSignedIntegerExact(Id, out var ivalue))
        {
            if (ivalue >= sbyte.MinValue && ivalue <= sbyte.MaxValue)
            {
                value = (sbyte)ivalue;
                return true;
            }
        }

        value = 0;
        return false;
    }


    public ushort GetUInt16()
    {
        if (TryGetUInt16(out var value))
        {
            return value;
        }

        throw new FormatException();
    }

    public bool TryGetUInt16(out ushort value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetUnsignedIntegerExact(Id, out var ivalue))
        {
            if (ivalue <= ushort.MaxValue)
            {
                value = (ushort)ivalue;
                return true;
            }
        }

        value = 0;
        return false;
    }


    public short GetInt16()
    {
        if (TryGetInt16(out var value))
        {
            return value;
        }

        throw new FormatException();
    }

    public bool TryGetInt16(out short value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetSignedIntegerExact(Id, out var ivalue))
        {
            if (ivalue >= short.MinValue && ivalue <= short.MaxValue)
            {
                value = (short)ivalue;
                return true;
            }
        }

        value = 0;
        return false;
    }


    public uint GetUInt32()
    {
        if (TryGetUInt32(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetUInt32(out uint value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetUnsignedIntegerExact(Id, out var ivalue))
        {
            if (ivalue <= uint.MaxValue)
            {
                value = (uint)ivalue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public string? GetString()
    {
        ThrowIfInvalid();

        if (Document.TryGetStringExact(Id, out var value))
        {
            return value;
        }

        if (Document.GetToken(Id).Kind == DTokenKind.Null)
        {
            return null;
        }

        throw new InvalidOperationException("The element does not contain a string or null value.");
    }

    public int GetInt32()
    {
        if (TryGetInt32(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetInt32(out int value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetSignedIntegerExact(Id, out var ivalue))
        {
            if (ivalue >= int.MinValue && ivalue <= int.MaxValue)
            {
                value = (int)ivalue;
                return true;
            }
        }

        value = 0;
        return false;
    }


    public ulong GetUInt64()
    {
        if (TryGetUInt64(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetUInt64(out ulong value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetUnsignedIntegerExact(Id, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }


    public long GetInt64()
    {
        if (TryGetInt64(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetInt64(out long value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetSignedIntegerExact(Id, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }


    public float GetSingle()
    {
        if (TryGetSingle(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetSingle(out float value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Variant;

        if (!kind.IsNumber)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetFloatingExact(Id, out var fvalue))
        {
            value = (float)fvalue;
            return true;
        }

        value = 0;
        return false;
    }

    public double GetDouble()
    {
        if (TryGetDouble(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetDouble(out double value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetFloatingExact(Id, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    public decimal GetDecimal()
    {
        if (TryGetDecimal(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetDecimal(out decimal value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetDecimalExact(Id, out var dvalue))
        {
            value = dvalue;
            return true;
        }

        value = 0;
        return false;
    }

    public DateTime GetDateTime()
    {
        if (TryGetDateTime(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetDateTime(out DateTime value)
    {
        ThrowIfInvalid();

        var token = Document.GetToken(Id);

        if (token.Kind != DTokenKind.Timestamp && token.Type != DTokenType.Text)
        {
            throw new InvalidOperationException("The element does not contain a timestamp or text value.");
        }

        if (Document.TryGetDateTimeExact(Id, out var dvalue))
        {
            value = dvalue;
            return true;
        }

        value = default;
        return false;
    }

    public DateTimeOffset GetDateTimeOffset()
    {
        if (TryGetDateTimeOffset(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetDateTimeOffset(out DateTimeOffset value)
    {
        ThrowIfInvalid();

        var token = Document.GetToken(Id);

        if (token.Kind != DTokenKind.Timestamp && token.Type != DTokenType.Text)
        {
            throw new InvalidOperationException("The element does not contain a timestamp or text value.");
        }

        if (Document.TryGetDateTimeOffsetExact(Id, out var dvalue))
        {
            value = dvalue;
            return true;
        }

        value = default;
        return false;
    }

    public Guid GetGuid()
    {
        if (TryGetGuid(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    public bool TryGetGuid(out Guid value)
    {
        ThrowIfInvalid();

        var token = Document.GetToken(Id);

        if (!(token.Type == DTokenType.Text || token.Kind == DTokenKind.ByteString))
        {
            throw new InvalidOperationException("The element does not contain a text or byte string value.");
        }

        if (Document.TryGetGuidExact(Id, out var dvalue))
        {
            value = dvalue;
            return true;
        }

        value = Guid.Empty;
        return false;
    }

    public DElement GetProperty(string propertyName)
    {
        if (TryGetProperty(propertyName, out var result))
        {
            return result;
        }

        throw new KeyNotFoundException();
    }

    public DElement GetProperty(ReadOnlySpan<char> propertyName)
    {
        if (TryGetProperty(propertyName, out var result))
        {
            return result;
        }

        throw new KeyNotFoundException();
    }

    public DElement GetProperty(ReadOnlySpan<byte> propertyName)
    {
        if (TryGetProperty(propertyName, out var result))
        {
            return result;
        }

        throw new KeyNotFoundException();
    }

    public bool TryGetProperty(string propertyName, out DElement value)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ThrowIfInvalid();

        foreach (var prop in EnumerateObject())
        {
            if (prop.NameEquals(propertyName))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }


    public bool TryGetProperty(ReadOnlySpan<char> propertyName, out DElement value)
    {
        ThrowIfInvalid();

        foreach (var prop in EnumerateObject())
        {
            if (prop.NameEquals(propertyName))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TryGetProperty(ReadOnlySpan<byte> propertyName, out DElement value)
    {
        ThrowIfInvalid();

        foreach (var prop in EnumerateObject())
        {
            if (prop.NameEquals(propertyName))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TryGetByteString(out ReadOnlySpan<byte> value, ByteStringKind binaryKind = ByteStringKind.Default)
    {
        ThrowIfInvalid();

        var token = Document.GetToken(Id);

        if (!(token.Type == DTokenType.Text || token.Kind == DTokenKind.ByteString))
        {
            throw new InvalidOperationException("The element does not contain a text or byte string value.");
        }

        if (Document.TryGetByteStringExact(Id, out var dvalue, binaryKind))
        {
            value = dvalue;
            return true;
        }

        value = default;
        return false;
    }

    public ReadOnlySpan<byte> GetByteString(ByteStringKind binaryKind = ByteStringKind.Default)
    {
        if (TryGetByteString(out var result, binaryKind))
        {
            return result;
        }

        throw new FormatException();
    }


    public bool TryGetBigNumber(out ReadOnlySpan<byte> value)
    {
        ThrowIfInvalid();

        var kind = Document.GetToken(Id).Kind;

        if (!kind.IsNumeric)
        {
            throw new InvalidOperationException("The element does not contain a numeric value.");
        }

        if (Document.TryGetBigNumberExact(Id, out var dvalue))
        {
            value = dvalue;
            return true;
        }

        value = default;
        return false;
    }

    public ReadOnlySpan<byte> GetBigNumber()
    {
        if (TryGetBigNumber(out var result))
        {
            return result;
        }

        throw new FormatException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(Document?.ToString(), "Cannot access a disposed object.");
        }
    }

    public override string? ToString()
    {
        ThrowIfInvalid();

        var kind = Token.Kind;

        if (kind.IsStringEncoded)
        {
            return Document.GetStringValue(Id);
        }

        if (kind == DTokenKind.Boolean)
        {
            return Token.Variant == DTokenVariant.BooleanTrue ? "True" : "False";
        }

        if (kind == DTokenKind.Null)
        {
            return string.Empty;
        }

        if (kind == DTokenKind.Control)
        {
            return null;
        }

        return JsonDocument.EncodeToString(this);
    }

    public bool ValueEquals(ReadOnlySpan<byte> utf8Text)
    {
        ThrowIfInvalid();

        if (Token.IsExtended)
        {
            if (Document.TryGetStringExact(Id, out var value))
            {
                return Utf8Helper.Equals(utf8Text, value);
            }
        }
        else
        {
            if (Document.TryGetUtf8BytesExact(Id, out var value))
            {
                return value.SequenceEqual(utf8Text);
            }
        }

        return false;
    }

    public bool ValueEquals(string? text)
    {
        ThrowIfInvalid();

        if (text == null)
        {
            return Token.Kind == DTokenKind.Null;
        }

        return ValueEquals(text.AsSpan());
    }

    public bool ValueEquals(ReadOnlySpan<char> text)
    {
        ThrowIfInvalid();

        if (Token.IsExtended)
        {
            if (Document.TryGetStringExact(Id, out var value))
            {
                return text.SequenceEqual(value.AsSpan());
            }
        }
        else
        {
            if (Document.TryGetUtf8BytesExact(Id, out var value))
            {
                return Utf8Helper.Equals(value, text);
            }
        }

        return false;
    }

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (Id == 0)
            {
                return false;
            }

            return Document.Version == Version;
        }
    }

    public int GetValueCount()
    {
        ThrowIfInvalid();

        if (!Token.IsContainer)
        {
            throw new InvalidOperationException("The element is not a container.");
        }

        return Document.GetValueCount(Id);
    }

    /// <summary>
    /// Gets the array element at the specified index.
    /// </summary>
    /// <remarks>
    /// For parsed arrays this is an O(n) operation. Use <see cref="EnumerateArray"/> to iterate all elements.
    /// </remarks>
    public DElement this[int index]
    {
        get
        {
            var length = GetArrayLength();

            if ((uint)index >= (uint)length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var token = Token;

            if (token.IsExtended)
            {
                var node = (DArray)Document.GetExtendContainer(token);
                return new DElement(Document, node.GetValueInternal(index));
            }

            if (token.LinkId - Id - 1 == (uint)length)
            {
                return new DElement(Document, Id + 1 + (uint)index);
            }

            var enumerator = new Document.ValueEnumerator(Document, Id);

            for (var i = 0; i <= index; i++)
            {
                enumerator.MoveNext();
            }

            return new DElement(Document, enumerator.Current);
        }
    }

    public int GetArrayLength()
    {
        ThrowIfInvalid();

        if (Token.Type != DTokenType.Array)
        {
            throw new InvalidOperationException("The element is not an array.");
        }

        return Document.GetValueCount(Id);
    }

    public int GetPropertyCount()
    {
        ThrowIfInvalid();

        if (Token.Type != DTokenType.Map)
        {
            throw new InvalidOperationException("The element is not a map.");
        }

        return Document.GetValueCount(Id);
    }

    public void WriteTo(DataWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ThrowIfInvalid();

        writer.WriteValue(this);
    }

    public DElement Clone()
    {
        ThrowIfInvalid();

        return DoxNodeDocument.CreateFrom(this, Document.Settings).RootElement;
    }

    public override bool Equals(object? obj)
    {
        if (obj is DElement other)
        {
            return Equals(other);
        }

        return false;
    }

    public bool Equals(DElement other)
    {
        return Document == other.Document && Id == other.Id && Version == other.Version;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Document, Id, Version);
    }
}