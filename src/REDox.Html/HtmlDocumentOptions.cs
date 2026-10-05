// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Html;

public readonly record struct HtmlDocumentOptions
{
    public bool PreserveTrivia { get; init; }

    public int MaxDepth { get; init; }
}