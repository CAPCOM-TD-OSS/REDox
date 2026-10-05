// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Cbor;

public readonly record struct CborDocumentOptions
{
    public bool UseSequenceFormat { get; init; }

    public bool PreserveTag { get; init; }

    public int MaxDepth { get; init; }

    public int MaxLength { get; init; }
}