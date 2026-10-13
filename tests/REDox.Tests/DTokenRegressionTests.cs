using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class DTokenRegressionTests
{
    [Theory]
    [InlineData(DTokenType.Map, 0x1c000000)]
    [InlineData(DTokenType.Map, 0x3c000000)]
    [InlineData(DTokenType.Array, 0x1c000000)]
    [InlineData(DTokenType.Array, 0x3c000000)]
    public void ContainerWithInlineMarkerInLinkRemainsValidAfterEditing(DTokenType type, uint linkId)
    {
        var container = CreateContainerWithLink(type, linkId);
        using var document = container.Document;

        // Freeing another token changes the version and forces container revalidation.
        var detached = document.AddExtendArray(0);
        document.Free(detached.Id, 0, 0);

        Assert.True(container.IsValid);
    }

    [Theory]
    [InlineData(DTokenType.Map, 0x1c000000)]
    [InlineData(DTokenType.Map, 0x3c000000)]
    [InlineData(DTokenType.Array, 0x1c000000)]
    [InlineData(DTokenType.Array, 0x3c000000)]
    public void ContainerWithInlineMarkerInLinkEnumeratesTrivia(DTokenType type, uint linkId)
    {
        var container = CreateContainerWithLink(type, linkId);
        using var document = container.Document;

        container.LeadingTrivia.AddLast("/*comment*/", TriviaKind.BlockComment);

        var enumerator = new Document.TriviaTokenEnumerator(document, container.Id);
        Assert.Equal(1, enumerator.Count);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(TriviaKind.BlockComment, document.GetToken(enumerator.Current).TriviaKind);
        Assert.False(enumerator.MoveNext());
    }

    [Theory]
    [InlineData(DTokenType.Map, 0x1c000000)]
    [InlineData(DTokenType.Map, 0x3c000000)]
    [InlineData(DTokenType.Array, 0x1c000000)]
    [InlineData(DTokenType.Array, 0x3c000000)]
    public void FreeingContainerWithInlineMarkerInLinkPreservesExtendId(DTokenType type, uint linkId)
    {
        var container = CreateContainerWithLink(type, linkId);
        using var document = container.Document;
        var tokenId = container.Id;
        var extendId = document.GetToken(tokenId).ExtendId;

        document.Free(tokenId, 0, 0);

        var freeToken = document.GetToken(tokenId);
        Assert.True(freeToken.IsEmptyExtend);
        Assert.Equal(extendId, freeToken.ExtendId);

        var replacement = document.AddExtendArray(0);
        Assert.Equal(tokenId, replacement.Id);
        Assert.Equal(extendId, document.GetToken(replacement.Id).ExtendId);
    }

    private static DContainer CreateContainerWithLink(DTokenType type, uint linkId)
    {
        DContainer container = type == DTokenType.Array ? new DArray() : new DObject();
        var token = container.Document.GetToken(container.Id);

        // Set the link metadata directly to avoid allocating a huge token array.
        token.LinkId = linkId;
        container.Document.SetToken(container.Id, token);

        return container;
    }

    [Theory]
    [InlineData(300.1)]
    [InlineData(-300.1)]
    [InlineData(1.3)]
    [InlineData(123.456)]
    public void InlineFloatReplacementPreservesDoubleValue(double value)
    {
        var array = new DArray { 123.456 };

        Assert.Equal(123.456, (double)array[0]);

        array[0] = value;

        Assert.Equal(value, (double)array[0]);
    }

    [Fact]
    public void HexadecimalNotationSurvivesInlineIntegerStorageTransitions()
    {
        using var document = Json5Document.Parse("[0x10]");
        var array = document.RootElement.AsArray();

        array[0] = 17;
        array[0] = 1L << 52;

        Assert.Equal("[0x10000000000000]", Json5Document.EncodeToString(document.RootElement));

        array[0] = 18;

        Assert.Equal("[0x12]", Json5Document.EncodeToString(document.RootElement));
    }

    [Fact]
    public void SequentialIntegerReplacementsPreserveHexadecimalNotation()
    {
        using var document = Json5Document.Parse("[0x10]");
        var array = document.RootElement.AsArray();

        array[0] = 17;

        Assert.Equal("[0x11]", Json5Document.EncodeToString(document.RootElement));

        array[0] = 18;

        Assert.Equal("[0x12]", Json5Document.EncodeToString(document.RootElement));
        Assert.Equal(IntegerKind.Hexadecimal, array[0].AsElement().Token.IntegerKind);
    }

    [Fact]
    public void InlineFloatCanConvertToDecimalBeforeAndAfterAddingTrivia()
    {
        var array = new DArray { 123.456 };

        Assert.Equal(123.456m, (decimal)array[0]);

        array[0].LeadingTrivia.AddLast("/*comment*/", TriviaKind.BlockComment);

        Assert.Equal(123.456m, (decimal)array[0]);
    }

    [Fact]
    public void InlineFloatCanConvertToDecimalThroughReader()
    {
        var array = new DArray { 123.456 };
        var reader = new DataReader(array);

        Assert.Equal(123.456m, reader.ReadDecimal(array[0].AsElement().Id));
    }
}