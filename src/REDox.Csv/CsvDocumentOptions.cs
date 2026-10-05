// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Csv;

public readonly record struct CsvDocumentOptions
{
    /// <summary>
    ///     Uses the first record as headers and requires each data record to have the same column count.
    /// </summary>
    public bool HasHeaderRecord { get; init; }

    public char SeparatorChar { get; init; }
}