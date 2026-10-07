// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization.DataContractJson;

sealed class DataContractDateTimeConverter : DataConverter<DateTime>
{
    public override DateTime Read(in DataReader reader, uint tokenId, DateTime existingValue)
    {
        return reader.ReadDateTime(tokenId);
    }

    public override void Write(DataWriter writer, DateTime value)
    {
        // DataContractJsonSerializer writes the local offset even for DateTime.MinValue/MaxValue.
        if (value.Kind == DateTimeKind.Unspecified &&
            (value.Ticks == DateTime.MinValue.Ticks || value.Ticks == DateTime.MaxValue.Ticks))
        {
            writer.WriteDateTimeOffset(new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value)));
            return;
        }

        writer.WriteDateTime(value);
    }
}
