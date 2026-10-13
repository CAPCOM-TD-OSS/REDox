// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace REDox;

public readonly struct DTriviaCollection
{
    private readonly DElement _element;

    internal DTriviaCollection(DElement element)
    {
        _element = element;
    }

    public bool IsValid => _element.IsValid;

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(nameof(DTriviaCollection));
        }
    }

    public void Clear()
    {
        ThrowIfInvalid();

        _element.Document.ClearTrivia(_element.Id);
    }

    public void AddFirst(string value, TriviaKind kind = TriviaKind.Default)
    {
        ThrowIfInvalid();

        _element.Document.AddTrivia(_element.Id, Encoding.UTF8.GetBytes(value), kind, true);
    }

    public void AddLast(string value, TriviaKind kind = TriviaKind.Default)
    {
        ThrowIfInvalid();

        _element.Document.AddTrivia(_element.Id, Encoding.UTF8.GetBytes(value), kind, false);
    }

    public Document.TriviaEnumerator GetEnumerator()
    {
        ThrowIfInvalid();

        return new Document.TriviaEnumerator(_element.Document, _element.Id);
    }
}