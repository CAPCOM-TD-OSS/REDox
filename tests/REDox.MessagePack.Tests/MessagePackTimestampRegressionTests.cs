// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace REDox.MessagePack.Tests;

public sealed class MessagePackTimestampRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSpecDateTimeShouldCountAsOneArrayElement(bool definiteLength)
    {
        var value = new DateTime(2026, 10, 12, 12, 34, 56, DateTimeKind.Utc);
        using var writer = new MessagePackWriter(options: new MessagePackWriteOptions { OldSpec = true });
        writer.WriteStartArray(definiteLength ? 2 : null);
        writer.WriteDateTime(value);
        writer.WriteInt32(42);
        writer.WriteEndArray();

        var values = global::MessagePack.MessagePackSerializer.Deserialize<long[]>(writer.Encode(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new[] { value.ToBinary(), 42L }, values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSpecDateTimeShouldCountAsOneMapValue(bool definiteLength)
    {
        var value = new DateTime(2026, 10, 12, 12, 34, 56, DateTimeKind.Utc);
        using var writer = new MessagePackWriter(options: new MessagePackWriteOptions { OldSpec = true });
        writer.WriteStartMap(definiteLength ? 2 : null);
        writer.WriteString("time");
        writer.WriteDateTime(value);
        writer.WriteString("tail");
        writer.WriteInt32(42);
        writer.WriteEndMap();

        var values = global::MessagePack.MessagePackSerializer.Deserialize<Dictionary<string, long>>(writer.Encode(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, values.Count);
        Assert.Equal(value.ToBinary(), values["time"]);
        Assert.Equal(42, values["tail"]);
    }

    [Theory]
    [InlineData(-62135596800L, 0, 0xc7, 15)] // DateTime.MinValue (UTC)
    [InlineData(-1L, 0, 0xc7, 15)]
    [InlineData(-1L, 9999999, 0xc7, 15)] // One tick before the Unix epoch
    [InlineData(0L, 0, 0xd6, 6)]
    [InlineData(0L, 1, 0xd7, 10)]
    [InlineData(4294967295L, 0, 0xd6, 6)] // Timestamp32 upper boundary
    [InlineData(4294967295L, 9999999, 0xd7, 10)]
    [InlineData(4294967296L, 0, 0xd7, 10)]
    [InlineData(4294967296L, 1, 0xd7, 10)]
    [InlineData(17179869183L, 0, 0xd7, 10)] // Timestamp64 upper boundary
    [InlineData(17179869183L, 9999999, 0xd7, 10)]
    [InlineData(17179869184L, 0, 0xc7, 15)]
    [InlineData(17179869184L, 1, 0xc7, 15)]
    [InlineData(253402300799L, 9999999, 0xc7, 15)] // DateTime.MaxValue (UTC)
    public void TimestampShouldUseSmallestLosslessFormat(long seconds, int fractionalTicks, int code, int length)
    {
        var value = DateTime.UnixEpoch.AddTicks(seconds * TimeSpan.TicksPerSecond + fractionalTicks);
        using var writer = new MessagePackWriter();
        writer.WriteDateTime(value);
        var bytes = writer.Encode();

        Assert.Equal(code, bytes[0]);
        Assert.Equal(length, bytes.Length);
        Assert.Equal(global::MessagePack.MessagePackSerializer.Serialize(value,
            cancellationToken: TestContext.Current.CancellationToken), bytes);
        Assert.Equal(value, global::MessagePack.MessagePackSerializer.Deserialize<DateTime>(bytes,
            cancellationToken: TestContext.Current.CancellationToken));
        using var document = MessagePackDocument.Parse(bytes);
        Assert.Equal(value, document.RootElement.GetDateTime());
    }

    [Theory]
    [InlineData(17179869184L, 0)]
    [InlineData(253402300799L, 9999999)]
    public void ParsingTimestamp96ShouldSupportPositiveSeconds(long seconds, int fractionalTicks)
    {
        var value = DateTime.UnixEpoch.AddTicks(seconds * TimeSpan.TicksPerSecond + fractionalTicks);
        var bytes = global::MessagePack.MessagePackSerializer.Serialize(value,
            cancellationToken: TestContext.Current.CancellationToken);

        using var document = MessagePackDocument.Parse(bytes);

        Assert.Equal(value, document.RootElement.GetDateTime());
    }
}