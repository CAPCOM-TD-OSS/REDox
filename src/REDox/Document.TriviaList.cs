// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox;

public abstract partial class Document
{
    internal void ClearTrivia(uint tokenId)
    {
        if (new TriviaTokenEnumerator(this, tokenId).Count == 0)
        {
            return;
        }

        var list = GetElementTriviaList(tokenId);
        foreach (var triviaId in list.Items)
        {
            FreeTrivia(triviaId);
        }

        list.Clear();
    }

    internal void AddTrivia(uint tokenId, byte[] value, TriviaKind kind, bool first)
    {
        var list = GetElementTriviaList(tokenId);
        var triviaId = JoinTrivia(0, value, kind);
        if (first)
        {
            list.AddFirst(triviaId);
        }
        else
        {
            list.Add(triviaId);
        }
    }

    private void FreeLeadingTrivia(uint tokenId)
    {
        var triviaId = GetLeadingTrivia(tokenId);
        if (triviaId == 0)
        {
            return;
        }

        if (triviaId == uint.MaxValue)
        {
            foreach (var id in EnumerateTrivia(tokenId))
            {
                FreeTrivia(id);
            }

            return;
        }

        var list = (TriviaList)_extends[triviaId]!;
        foreach (var id in list.Items)
        {
            FreeTrivia(id);
        }

        list.Clear();
        _extends[triviaId] = null;
        _emptyExtendId = AllocEmptyToken(DToken.MakeEmptyExtend(_emptyExtendId, triviaId));
    }

    internal int GetElementTriviaVersion(uint tokenId)
    {
        var triviaId = GetLeadingTrivia(tokenId);
        return triviaId == 0 || triviaId == uint.MaxValue ? 0 : ((TriviaList)_extends[triviaId]!).Version;
    }

    private sealed class TriviaList
    {
        private uint[] _items;

        internal TriviaList(int capacity)
        {
            _items = capacity == 0 ? [] : new uint[capacity];
        }

        internal TriviaList(TriviaList other) : this(other.Count)
        {
            other.Items.CopyTo(_items);
            Count = other.Count;
            Version = other.Version;
        }

        internal int Count { get; private set; }
        internal int Version { get; private set; }
        internal ReadOnlySpan<uint> Items => _items.AsSpan(0, Count);
        internal uint this[int index] => Items[index];

        internal void Add(uint tokenId)
        {
            EnsureCapacity();
            _items[Count++] = tokenId;
        }

        internal void AddFirst(uint tokenId)
        {
            EnsureCapacity();
            _items.AsSpan(0, Count).CopyTo(_items.AsSpan(1));
            _items[0] = tokenId;
            Count++;
        }

        internal void Clear()
        {
            _items.AsSpan(0, Count).Clear();
            Count = 0;
            Version++;
        }

        private void EnsureCapacity()
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, Math.Max(4, _items.Length * 2));
            }
        }
    }
}