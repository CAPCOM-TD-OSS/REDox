// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Ini;

/// <summary>Specifies the delimiter used when writing INI comments.</summary>
public enum IniCommentStyle
{
    /// <summary>
    ///     Preserves retained INI source delimiters and uses semicolons for new, modified, or non-INI comments. This is
    ///     the default.
    /// </summary>
    PreserveOrSemicolon = 0,

    /// <summary>Preserves retained INI source delimiters and uses hash signs for new, modified, or non-INI comments.</summary>
    PreserveOrHash = 1,

    /// <summary>Writes all comments with semicolons.</summary>
    AlwaysSemicolon = 2,

    /// <summary>Writes all comments with hash signs.</summary>
    AlwaysHash = 3
}