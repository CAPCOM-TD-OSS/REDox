using System;
using System.Buffers.Binary;

namespace REDox.MessagePack.Tests;

public sealed class MessagePackExt32MaxLengthTest
{
    private const int SentinelLength = 0x1000000;

    private static byte[] CreateExt32(byte typeCode, int payloadLength)
    {
        var data = new byte[payloadLength + 6];
        data[0] = 0xc9;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(1), (uint)payloadLength);
        data[5] = typeCode;

        for (var i = 0; i < payloadLength; i++)
        {
            data[6 + i] = (byte)(i * 31 + 7);
        }

        return data;
    }

    [Theory]
    [InlineData(SentinelLength - 1)]
    [InlineData(SentinelLength)]
    public void Ext32PayloadLengthMustBePreserved(int payloadLength)
    {
        var data = CreateExt32(5, payloadLength);

        using var doc = MessagePackDocument.Parse(data, SerializerSettings.Default);

        var root = doc.RootElement;
        Assert.Equal(2, root.GetArrayLength());

        var e = root.EnumerateArray();
        e.MoveNext();
        e.MoveNext();
        var payload = e.Current.GetByteString();

        Assert.Equal(payloadLength, payload.Length);
        Assert.True(payload.SequenceEqual(data.AsSpan(6)));
    }
}
