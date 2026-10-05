using System;
using System.Threading.Tasks;

namespace REDox.Cbor.Tests;

public sealed class CborMalformedInputTest
{
    [Fact]
    public void ParseUnclosedContainerMustNotExposePreviousDocumentTokens()
    {
        byte[] alice = [0x82, 0x65, (byte)'a', (byte)'l', (byte)'i', (byte)'c', (byte)'e', 0x19, 0x10, 0xE1];
        using (var doc = CborDocument.Parse(alice))
        {
            Assert.Equal("""["alice",4321]""", REDox.Json.JsonDocument.EncodeToString(doc.RootElement));
        }

        byte[] attack = [0x82, 0xF5];
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = CborDocument.Parse(attack);
        });
    }

    [Theory]
    [InlineData(new byte[] { 0x82, 0xF5 })]
    [InlineData(new byte[] { 0x9F, 0xF5 })]
    [InlineData(new byte[] { 0xA1, 0xF5 })]
    [InlineData(new byte[] { 0xA1, 0xF5, 0xA1 })]
    public void ParseUnclosedContainerShouldThrowDocumentParseException(byte[] input)
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = CborDocument.Parse(input);
        });
    }

    [Theory]
    [InlineData(new byte[] { 0x7F, 0x7A, 0xFF, 0xFF, 0xFF, 0xFB })]
    [InlineData(new byte[] { 0x5F, 0x5A, 0xFF, 0xFF, 0xFF, 0xFB })]
    [InlineData(new byte[] { 0xDF, 0x7A, 0xFF, 0xFF, 0xFF, 0xFB })]
    [InlineData(new byte[] { 0x7A, 0xFF, 0xFF, 0xFF, 0xFB })]
    [InlineData(new byte[] { 0x5B, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFB })]
    [InlineData(new byte[] { 0x1F })]
    [InlineData(new byte[] { 0x3F })]
    public void ParseOutOfRangeLengthShouldThrowWithoutHanging(byte[] input)
    {
        // Run on a dedicated thread so a regression fails the test instead of hanging the runner.
        var parse = Task.Factory.StartNew(() =>
        {
            Assert.ThrowsAny<DocumentParseException>(() =>
            {
                using var doc = CborDocument.Parse(input);
            });
        }, TaskCreationOptions.LongRunning);

        Assert.True(parse.Wait(TimeSpan.FromSeconds(5)),
            $"CborDocument.Parse did not complete within five seconds for: {Convert.ToHexString(input)}");
        parse.GetAwaiter().GetResult();
    }
}
