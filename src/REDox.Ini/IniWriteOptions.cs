// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Ini;

/// <summary>Controls INI formatting and UTF-8 output.</summary>
/// <remarks>All flags default to <see langword="false" />.</remarks>
public readonly record struct IniWriteOptions
{
    private readonly IniCommentStyle _commentStyle;
    private readonly Utf8TextWriteOptions _textWriteOptions;

    /// <summary>Gets the comment delimiter style. The default is <see cref="IniCommentStyle.PreserveOrSemicolon" />.</summary>
    /// <remarks>Applies to retained and attached comments when <see cref="PreserveTrivia" /> is enabled.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is not a defined comment style.</exception>
    public IniCommentStyle CommentStyle
    {
        get => _commentStyle;
        init
        {
            if (value is not (IniCommentStyle.PreserveOrSemicolon or IniCommentStyle.PreserveOrHash or
                IniCommentStyle.AlwaysSemicolon or IniCommentStyle.AlwaysHash))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _commentStyle = value;
        }
    }

    /// <summary>Gets whether retained trivia and attached comments are written.</summary>
    /// <remarks>Parse with <see cref="IniDocumentOptions.PreserveTrivia" /> to retain the original formatting.</remarks>
    public bool PreserveTrivia { get; init; }

    /// <summary>Gets whether generated key/value separators contain spaces, as in <c>key = value</c>.</summary>
    /// <remarks>Preserved separators keep their original spacing.</remarks>
    public bool WriteSpaces { get; init; }

    /// <summary>Gets whether the output begins with a UTF-8 byte order mark.</summary>
    public bool WriteBom
    {
        get => _textWriteOptions.WriteBom;
        init => _textWriteOptions = _textWriteOptions with { WriteBom = value };
    }

    /// <summary>Gets the line ending used for generated line breaks. The default is <see cref="System.Environment.NewLine" />.</summary>
    /// <value>Either <c>\n</c> or <c>\r\n</c>.</value>
    /// <exception cref="System.ArgumentNullException">The assigned value is <see langword="null" />.</exception>
    /// <exception cref="System.ArgumentException">The assigned value is neither <c>\n</c> nor <c>\r\n</c>.</exception>
    public string NewLine
    {
        get => _textWriteOptions.NewLine;
        init => _textWriteOptions = _textWriteOptions with { NewLine = value };
    }

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;
}