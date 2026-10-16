// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Linq;

namespace REDox.Ini.Tests;

public sealed class IniSwapTest
{
    [Fact]
    public void SwapMovesExistingLeadingTriviaAndKeepsDocumentTrailingTrivia()
    {
        const string input = "\n; B explanation\nb  =  2\n# A explanation\na=1\n; tail\n";
        using var document = IniDocument.Parse(input, options: new IniDocumentOptions { PreserveTrivia = true });
        var map = document.RootElement.AsMap();
        var entries = document.RootElement.EnumerateObject().ToArray();
        var trailing = document.TrailingTrivia.GetEnumerator().ToArray();

        map.Swap(0, 1);

        Assert.Equal(new[] { entries[1], entries[0] }, document.RootElement.EnumerateObject());
        Assert.Equal(trailing, document.TrailingTrivia.GetEnumerator().ToArray());
        var encoded = document.RootElement.ToIni(new IniWriteOptions { PreserveTrivia = true, NewLine = "\n" });
        Assert.Equal("\n# A explanation\na=1\n; B explanation\nb  =  2\n; tail\n", encoded);
        using var reparsed = IniDocument.Parse(encoded);
        Assert.Equal("1", reparsed.RootElement.GetProperty("a").GetString());
        Assert.Equal("2", reparsed.RootElement.GetProperty("b").GetString());
    }
}