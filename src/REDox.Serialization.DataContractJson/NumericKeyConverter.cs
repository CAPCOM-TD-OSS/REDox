// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.DataContractJson;

sealed class NumericKeyConverter<T> : DataConverter<T> where T : struct, IUtf8SpanParsable<T>, ISpanFormattable
{
    private readonly DataConverter<T> _converter;

    public NumericKeyConverter(DataConverter<T> converter)
    {
        _converter = converter;
    }

    protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
        SerializerSettings settings)
    {
        var converter = _converter.ResolvePropertyConverter(property, settings);
        return ReferenceEquals(converter, _converter)
            ? this
            : new NumericKeyConverter<T>((DataConverter<T>)converter);
    }

    public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
    {
        // The framework uses the current culture for numeric keys in simple dictionary JSON.
        return T.Parse(reader.ReadUtf8String(tokenId), CultureInfo.CurrentCulture);
    }

    public override void WriteAsPropertyName(DataWriter writer, T value)
    {
        Span<char> buffer = stackalloc char[128];
        if (value.TryFormat(buffer, out var written, default, CultureInfo.CurrentCulture))
        {
            writer.WriteString(buffer.Slice(0, written));
        }
        else
        {
            writer.WriteString(value.ToString(null, CultureInfo.CurrentCulture));
        }
    }

    public override T Read(in DataReader reader, uint tokenId, T existingValue)
    {
        return _converter.Read(reader, tokenId, existingValue);
    }

    public override void Write(DataWriter writer, T value)
    {
        _converter.Write(writer, value);
    }
}