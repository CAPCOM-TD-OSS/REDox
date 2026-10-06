using System;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class JsonTextEncoderInvalidUtf8Test
{
    public static TheoryData<string, byte[]> InvalidUtf8Inputs => new()
    {
        { "NonBmp", [0xC2, (byte)'"', (byte)'a'] },
        { "NonBmp", [0xC2, (byte)'\\', (byte)'a'] },
        { "NonBmp", [0xE0, (byte)'"', (byte)'<'] },
        { "NonBmp", [(byte)'x', 0xE0, (byte)'<', (byte)'/', (byte)'s'] },
        { "Ranges", [0xC2, (byte)'"', (byte)'a'] },
        { "Ranges", [0xC2, (byte)'\\', (byte)'a'] },
        { "Ranges", [0xE0, (byte)'"', (byte)'<'] },
        { "Ranges", [0xF0, (byte)'"', (byte)'<', (byte)'\\'] },
        { "NonBmp", [0xC2] },
        { "Ranges", [0xC2] },
        { "NonBmp", [0xE1, 0x80] },
        { "Ranges", [0xE1, 0x80] },
        { "NonBmp", [0xF0, 0x90, 0x80] },
        { "Ranges", [0xF0, 0x90, 0x80] },
        { "NonBmp", [0x80, 0xFF, (byte)'"'] },
        { "Ranges", [0x80, 0xFF, (byte)'"'] },
        { "NonBmp", [0xC0, 0xAF, (byte)'<'] },
        { "Ranges", [0xC0, 0xAF, (byte)'<'] },
        { "NonBmp", [0xE0, 0x80, 0xAF, (byte)'<'] },
        { "Ranges", [0xE0, 0x80, 0xAF, (byte)'<'] },
        { "NonBmp", [0xED, 0xA0, 0x80, (byte)'\\'] },
        { "Ranges", [0xED, 0xA0, 0x80, (byte)'\\'] },
        { "NonBmp", [0xF4, 0x90, 0x80, 0x80, (byte)'"'] },
        { "Ranges", [0xF4, 0x90, 0x80, 0x80, (byte)'"'] },
        { "NonBmp", [0xE1, 0x80, (byte)'"', (byte)'<', (byte)'\\', (byte)'\n'] },
        { "Ranges", [0xE1, 0x80, (byte)'"', (byte)'<', (byte)'\\', (byte)'\n'] },
        { "NonBmp", [0xF0, 0x90, 0x80, (byte)'"', (byte)'<', (byte)'\\', (byte)'\n'] },
        { "Ranges", [0xF0, 0x90, 0x80, (byte)'"', (byte)'<', (byte)'\\', (byte)'\n'] },
    };

    private static TextEncoderPolicy CreatePolicy(string kind)
    {
        return kind switch
        {
            "NonBmp" => new TextEncoderPolicy(TextEscapeMask.NonBmp | TextEscapeMask.LessThan),
            "Ranges" => new TextEncoderPolicy([TextEscapeRange.Create('\u00e9', '\u00e9')], TextEscapeMask.LessThan),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Theory]
    [MemberData(nameof(InvalidUtf8Inputs))]
    public void EncodeInvalidUtf8MustNotEmitUnescapedSpecialCharacters(string policyKind, byte[] input)
    {
        var encoder = JsonTextEncoder.Create(CreatePolicy(policyKind));
        var dest = new byte[input.Length * 12 + 2];

        var written = encoder.Encode(dest, input);

        AssertSafeJsonString(dest.AsSpan(0, written), input);
    }

    [Theory]
    [MemberData(nameof(InvalidUtf8Inputs))]
    public void EncodeQuotedInvalidUtf8MustNotEmitUnescapedSpecialCharacters(string policyKind, byte[] input)
    {
        var encoder = JsonTextEncoder.Create(CreatePolicy(policyKind));
        var dest = new byte[input.Length * 12 + 2];

        var written = encoder.Encode(dest, input, '"');

        AssertSafeJsonString(dest.AsSpan(0, written), input);
    }

    [Theory]
    [InlineData("NonBmp", "", "\"\"")]
    [InlineData("Ranges", "", "\"\"")]
    [InlineData("NonBmp", "ascii<\"\\\n", "\"ascii\\u003c\\\"\\\\\\n\"")]
    [InlineData("Ranges", "ascii<\"\\\n", "\"ascii\\u003c\\\"\\\\\\n\"")]
    [InlineData("NonBmp", "éあ🙂", "\"éあ\\ud83d\\ude42\"")]
    [InlineData("Ranges", "éあ🙂", "\"\\u00e9あ🙂\"")]
    [InlineData("NonBmp", "\u0080\u07ff\u0800\ud7ff\ue000\uffff\U00010000\U0010ffff",
        "\"\u0080\u07ff\u0800\ud7ff\ue000\uffff\\ud800\\udc00\\udbff\\udfff\"")]
    [InlineData("Ranges", "\u0080\u07ff\u0800\ud7ff\ue000\uffff\U00010000\U0010ffff",
        "\"\u0080\u07ff\u0800\ud7ff\ue000\uffff\U00010000\U0010ffff\"")]
    public void EncodeValidUtf8PreservesEscaping(string policyKind, string input, string expected)
    {
        var encoder = JsonTextEncoder.Create(CreatePolicy(policyKind));
        var utf8 = Encoding.UTF8.GetBytes(input);
        var dest = new byte[utf8.Length * 12 + 2];

        var written = encoder.Encode(dest, utf8);

        Assert.Equal(expected, Encoding.UTF8.GetString(dest.AsSpan(0, written)));

        written = encoder.Encode(dest, utf8, '"');

        Assert.Equal(expected, Encoding.UTF8.GetString(dest.AsSpan(0, written)));
    }

    private static void AssertSafeJsonString(ReadOnlySpan<byte> output, byte[] input)
    {
        var hex = Convert.ToHexString(output);
        var source = Convert.ToHexString(input);

        Assert.True(output.Length >= 2, $"Output too short: {hex}");
        Assert.Equal((byte)'"', output[0]);
        Assert.Equal((byte)'"', output[^1]);

        var body = output.Slice(1, output.Length - 2);

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (c == (byte)'\\')
            {
                Assert.True(i + 1 < body.Length, $"Dangling backslash in output {hex} for input {source}");
                Assert.Contains((char)body[i + 1], "\"\\/bfnrtu");
                i++;
                continue;
            }

            Assert.False(c == (byte)'"', $"Unescaped '\"' at {i} in output {hex} for input {source}");
            Assert.False(c == (byte)'<', $"Unescaped '<' at {i} in output {hex} for input {source}");
        }

        using var json = System.Text.Json.JsonDocument.Parse(output.ToArray());
        Assert.Equal(Encoding.UTF8.GetString(input), json.RootElement.GetString());
    }
}
