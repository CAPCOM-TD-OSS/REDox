// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace REDox.Serialization.DataContractJson;

sealed class DateTimeFormatConverter : DataConverter<DateTime>
{
    private readonly string _format;
    private readonly IFormatProvider _provider;
    private readonly DateTimeStyles _styles;
    private readonly bool _useInvariantTimestamp;

    public DateTimeFormatConverter(DateTimeFormat format)
    {
        _format = format.FormatString;
        _provider = format.FormatProvider;
        _styles = format.DateTimeStyles;
        // Only this exact Gregorian format without an offset has equivalent UTF-8 parsing semantics.
        _useInvariantTimestamp = ReferenceEquals(_provider, CultureInfo.InvariantCulture) &&
                                 (_styles == DateTimeStyles.None || _styles == DateTimeStyles.RoundtripKind) &&
                                 (_format == "yyyy-MM-ddTHH:mm:ss.fffffff" ||
                                  _format == "yyyy-MM-dd'T'HH:mm:ss.fffffff");
    }

    public override DateTime Read(in DataReader reader, uint tokenId, DateTime existingValue)
    {
        var source = reader.ReadUtf8String(tokenId);
        if (_useInvariantTimestamp && source.Length == 27 &&
            Utf8Parser.TryParse(source, out DateTime value, out var consumed, 'O') && consumed == source.Length)
        {
            return value;
        }

        char[]? rented = null;
        var chars = source.Length <= 256
            ? stackalloc char[256]
            : rented = ArrayPool<char>.Shared.Rent(source.Length);
        try
        {
            var written = Encoding.UTF8.GetChars(source, chars);
            return DateTime.ParseExact(chars.Slice(0, written), _format, _provider, _styles);
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    public override void Write(DataWriter writer, DateTime value)
    {
        char[]? rented = null;
        Span<char> chars = stackalloc char[128];
        try
        {
            int written;
            while (!value.TryFormat(chars, out written, _format, _provider))
            {
                var next = ArrayPool<char>.Shared.Rent(checked(chars.Length * 2));
                if (rented != null)
                {
                    ArrayPool<char>.Shared.Return(rented);
                }

                rented = next;
                chars = rented;
            }

            // TryFormat's output is consumed synchronously before returning the pooled buffer.
            writer.WriteString(chars.Slice(0, written));
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}