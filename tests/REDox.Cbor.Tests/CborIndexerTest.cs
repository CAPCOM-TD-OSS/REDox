using SysCborTag = System.Formats.Cbor.CborTag;
using SysCborWriter = System.Formats.Cbor.CborWriter;

namespace REDox.Cbor.Tests;

public sealed class CborIndexerTest
{
    [Fact]
    public void IndexerSkipsPreservedTagTrivia()
    {
        var writer = new SysCborWriter();
        writer.WriteStartArray(3);
        writer.WriteInt32(1);
        writer.WriteTag((SysCborTag)1000);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteEndArray();

        using var doc = CborDocument.Parse(writer.Encode(), SerializerSettings.Default,
            new CborDocumentOptions { PreserveTag = true });
        var root = doc.RootElement;

        Assert.Equal(1, root[0].GetInt32());
        Assert.Equal(2, root[1].GetInt32());
        Assert.Equal(3, root[2].GetInt32());
    }
}
