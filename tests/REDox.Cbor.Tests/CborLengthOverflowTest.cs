using System;
using System.Threading.Tasks;

namespace REDox.Cbor.Tests;

public sealed class CborLengthOverflowTest
{
    public static TheoryData<byte[]> OverflowingLengthInputs =>
    [
        // indefinite-length text string: chunk length does not fit in int or goes negative
        new byte[] { 0x7F, 0x7B, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },
        new byte[] { 0x7F, 0x7B, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF },
        new byte[] { 0x7F, 0x7B, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },
        new byte[] { 0x7F, 0x7A, 0x80, 0x00, 0x00, 0x00, 0xFF },
        new byte[] { 0x7F, 0x7A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },

        // indefinite-length byte string
        new byte[] { 0x5F, 0x5B, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },
        new byte[] { 0x5F, 0x5B, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF },
        new byte[] { 0x5F, 0x5A, 0x80, 0x00, 0x00, 0x00, 0xFF },
        new byte[] { 0x5F, 0x5A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },

        // multiple chunks whose declared lengths overflow when summed
        new byte[] { 0x7F, 0x7A, 0x7F, 0xFF, 0xFF, 0xFF, 0x61, 0x61, 0x7A, 0x7F, 0xFF, 0xFF, 0xFF, 0x61, 0x61, 0xFF },

        // definite-length strings with 32/64-bit lengths larger than the input
        new byte[] { 0x7A, 0x80, 0x00, 0x00, 0x00, 0x61 },
        new byte[] { 0x5A, 0x80, 0x00, 0x00, 0x00, 0x41 },
        new byte[] { 0x7B, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x61 },
        new byte[] { 0x5B, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x41 },

        // the same strings nested in an array, a map value and a tag
        new byte[] { 0x81, 0x7F, 0x7A, 0xFF, 0xFF, 0xFF, 0xFB, 0xFF },
        new byte[] { 0xA1, 0x61, 0x61, 0x5F, 0x5B, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFB, 0xFF },
        new byte[] { 0xC0, 0x7F, 0x7B, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFB, 0xFF },
    ];

    [Theory]
    [MemberData(nameof(OverflowingLengthInputs))]
    public async Task ParseOverflowingLengthShouldThrowDocumentParseExceptionWithoutHanging(byte[] input)
    {
        await RunWithoutHangingAsync(input, () =>
        {
            Assert.ThrowsAny<DocumentParseException>(() =>
            {
                using var doc = CborDocument.Parse(input);
            });
        });
    }

    [Theory]
    [MemberData(nameof(OverflowingLengthInputs))]
    public async Task TryParseOverflowingLengthShouldReturnFalseWithoutHanging(byte[] input)
    {
        await RunWithoutHangingAsync(input, () =>
        {
            var parsed = CborDocument.TryParse(input, out var doc);

            doc?.Dispose();

            Assert.False(parsed);
        });
    }

    private static async Task RunWithoutHangingAsync(byte[] input, Action action)
    {
        // Run on a dedicated thread so a regression fails the test instead of hanging the runner.
        var task = Task.Factory.StartNew(action, TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"CBOR parsing did not complete within five seconds for: {Convert.ToHexString(input)}");
        }
    }
}
