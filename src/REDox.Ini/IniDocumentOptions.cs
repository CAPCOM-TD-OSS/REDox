// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Ini;

/// <summary>Controls how INI documents are parsed.</summary>
/// <remarks>All flags default to <see langword="false" />.</remarks>
public readonly record struct IniDocumentOptions
{
    private readonly IniCommentSyntax _commentSyntax;

    /// <summary>Gets the recognized comment delimiters. The default is <see cref="IniCommentSyntax.Both" />.</summary>
    /// <remarks>Applies to full-line comments, comments after section headers, and enabled inline comments.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is not a defined comment syntax.</exception>
    public IniCommentSyntax CommentSyntax
    {
        get => _commentSyntax;
        init
        {
            if (value is not (IniCommentSyntax.Both or IniCommentSyntax.Semicolon or IniCommentSyntax.Hash))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _commentSyntax = value;
        }
    }

    /// <summary>Gets whether comments, whitespace, and separators are retained for later writing.</summary>
    /// <remarks>Use <see cref="IniWriteOptions.PreserveTrivia" /> when writing to preserve the retained trivia.</remarks>
    public bool PreserveTrivia { get; init; }

    /// <summary>Gets whether the delimiters selected by <see cref="CommentSyntax" /> begin inline comments in values.</summary>
    /// <remarks>
    ///     Inline comments require whitespace before the delimiter, or a delimiter at the start of the value.
    ///     When disabled, delimiters remain part of the value. Selected full-line comments are always recognized.
    /// </remarks>
    public bool AllowInlineComments { get; init; }

    /// <summary>Gets whether duplicate keys, section names, and global key/section name collisions are accepted.</summary>
    /// <remarks>
    ///     Duplicate names are retained when enabled; lookup by name returns the first match.
    ///     Names are compared according to <see cref="SerializerSettings.PropertyNameCaseInsensitive" />, and keys with the
    ///     same name in different sections are always allowed.
    /// </remarks>
    public bool AllowDuplicateKeys { get; init; }
}