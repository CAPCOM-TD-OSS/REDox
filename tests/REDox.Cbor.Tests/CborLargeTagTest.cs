using System.Linq;
using SysCborTag = System.Formats.Cbor.CborTag;
using SysCborWriter = System.Formats.Cbor.CborWriter;

namespace REDox.Cbor.Tests;

public sealed class CborLargeTagTest
{
    private static readonly CborDocumentOptions s_preserve = new() { PreserveTag = true };
    private static readonly CborWriteOptions s_writePreserve = new() { PreserveTag = true };

    public static TheoryData<ulong> LargeTags => new()
    {
        0x1_0000_0002UL,
        0x1_0000_0003UL,
        0x1_0000_0001UL,
        0x8000_0000_0000_0002UL,
        ulong.MaxValue
    };

    private static byte[] CreateTaggedByteString(ulong tag, byte[] payload)
    {
        var writer = new SysCborWriter();
        writer.WriteTag((SysCborTag)tag);
        writer.WriteByteString(payload);
        return writer.Encode();
    }

    [Theory]
    [MemberData(nameof(LargeTags))]
    public void LargeTagMustNotBeInterpretedAsTruncatedKnownTag(ulong tag)
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        var cbor = CreateTaggedByteString(tag, payload);

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.True(doc.RootElement.TryGetByteString(out var bytes));
        Assert.Equal(payload, bytes.ToArray());
    }

    [Theory]
    [MemberData(nameof(LargeTags))]
    public void LargeTagMustBePreservedAsTrivia(ulong tag)
    {
        byte[] payload = [1, 2, 3];
        var cbor = CreateTaggedByteString(tag, payload);

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default, s_preserve);

        var trivia = doc.RootElement.EnumerateTrivia().ToList();
        Assert.Single(trivia);
        Assert.Equal(TriviaKind.Tag, trivia[0].Kind);
        Assert.Equal(tag.ToString(), trivia[0].GetString());
    }

    [Theory]
    [MemberData(nameof(LargeTags))]
    public void LargeTagMustRoundTrip(ulong tag)
    {
        byte[] payload = [1, 2, 3];
        var cbor = CreateTaggedByteString(tag, payload);

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default, s_preserve);

        Assert.Equal(cbor, CborDocument.Encode(doc.RootElement, s_writePreserve));
    }
}