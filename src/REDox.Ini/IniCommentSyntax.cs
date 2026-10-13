// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Ini;

/// <summary>Specifies which INI comment delimiters are recognized when reading.</summary>
public enum IniCommentSyntax
{
    /// <summary>Recognizes both semicolons and hash signs. This is the default.</summary>
    Both = 0,

    /// <summary>Recognizes semicolons; hash signs are ordinary characters.</summary>
    Semicolon = 1,

    /// <summary>Recognizes hash signs; semicolons are ordinary characters.</summary>
    Hash = 2
}