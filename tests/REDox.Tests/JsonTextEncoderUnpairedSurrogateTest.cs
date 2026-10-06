using System;
using System.Text;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class JsonTextEncoderUnpairedSurrogateTest
{
    private const string High = "\ud83d";
    private const string Low = "\ude42";

    public static TheoryData<string, string, string> UnpairedSurrogateInputs()
    {
        var data = new TheoryData<string, string, string>();

        foreach (var kind in new[] { "NonBmp", "Ranges" })
        {
            data.Add(kind, High + "\"", "\\\"");
            data.Add(kind, High + "<", "\\u003c");
            data.Add(kind, High + "\\", "\\\\");
            data.Add(kind, High + "a", "a");
            data.Add(kind, Low + "\"", "\\\"");
            data.Add(kind, Low + "<", "\\u003c");
            data.Add(kind, High, "");
            data.Add(kind, High + High + "\"", "\\\"");
        }

        return data;
    }

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
    [MemberData(nameof(UnpairedSurrogateInputs))]
    public void EncodeUnpairedSurrogateMustNotSwallowFollowingCharacter(string policyKind, string input, string expectedTail)
    {
        var encoder = JsonTextEncoder.Create(CreatePolicy(policyKind));
        var dest = new byte[input.Length * 12 + 2];

        var written = encoder.Encode(dest, input.AsSpan());

        var output = Encoding.Latin1.GetString(dest, 0, written);

        Assert.StartsWith("\"", output);
        Assert.EndsWith("\"", output);

        var body = output.Substring(1, output.Length - 2);

        Assert.EndsWith(expectedTail, body);
        AssertNoUnescapedSpecialCharacters(body, output);
    }

    public static TheoryData<string, string> RangesInputs => new()
    {
        { High, "\\ufffd" },
        { Low, "\\ufffd" },
        { High + "\"", "\\ufffd\\\"" },
        { Low + "\"", "\\ufffd\\\"" },
        { High + "<", "\\ufffd\\u003c" },
        { High + "\\", "\\ufffd\\\\" },
        { High + "a", "\\ufffda" },
        { High + High + "\"", "\\ufffd\\ufffd\\\"" },
        { Low + Low, "\\ufffd\\ufffd" },
        { High + High + Low, "\\ufffd🙂" },
        { Low + High + Low, "\\ufffd🙂" },
        { High + Low + "\"", "🙂\\\"" },
        { "\ud800\udc00", "\U00010000" },
        { "\udbff\udfff", "\U0010ffff" },
        { "asciiéあ", "ascii\\u00e9あ" },
    };

    [Theory]
    [MemberData(nameof(RangesInputs))]
    public void EncodeRangesMustPreserveValidPairsAndReplaceUnpairedSurrogates(string input, string expectedBody)
    {
        var encoder = JsonTextEncoder.Create(CreatePolicy("Ranges"));
        var dest = new byte[input.Length * 12 + 2];

        var written = encoder.Encode(dest, input.AsSpan());

        Assert.Equal(Encoding.UTF8.GetBytes("\"" + expectedBody + "\""), dest.AsSpan(0, written).ToArray());
    }

    private static void AssertNoUnescapedSpecialCharacters(string body, string output)
    {
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (c == '\\')
            {
                Assert.True(i + 1 < body.Length, $"Dangling backslash in output {output}");
                i++;
                continue;
            }

            Assert.False(c == '"', $"Unescaped '\"' at {i} in output {output}");
            Assert.False(c == '<', $"Unescaped '<' at {i} in output {output}");
        }
    }
}
