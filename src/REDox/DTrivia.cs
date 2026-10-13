// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace REDox;

public readonly struct DTrivia : IEquatable<DTrivia>
{
    public bool IsValid
    {
        get
        {
            if (_doc == null)
            {
                return false;
            }

            return Id != 0 && _doc.Version == _version &&
                   _doc.GetToken(Id).Kind == DTokenKind.Trivia &&
                   _doc.GetElementTriviaVersion(_tokenId) == _triviaVersion;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(_doc?.ToString(), "Cannot access a disposed object.");
        }
    }

    public TriviaKind Kind
    {
        get
        {
            ThrowIfInvalid();
            return (TriviaKind)((int)_doc.GetToken(Id).Variant & 7);
        }
    }

    public string GetString()
    {
        ThrowIfInvalid();
        return Encoding.UTF8.GetString(_doc.GetTriviaValue(Id));
    }

    public void ReplaceWith(string text, TriviaKind kind = TriviaKind.Inherit)
    {
        ThrowIfInvalid();
        _doc.EnsureElementTriviaList(_tokenId);
        _doc.JoinTrivia(Id, Utf8Helper.GetUtf8String(text), kind);
    }

    public override string ToString()
    {
        return GetString();
    }

    internal uint Id { get; }

    public override bool Equals(object? obj)
    {
        if (obj is DTrivia other)
        {
            return Equals(other);
        }

        return false;
    }

    public bool Equals(DTrivia other)
    {
        return _doc == other._doc && Id == other.Id && _version == other._version &&
               _tokenId == other._tokenId && _triviaVersion == other._triviaVersion;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_doc, Id, _version, _tokenId, _triviaVersion);
    }

    internal DTrivia(Document document, uint id, uint tokenId)
    {
        _doc = document;
        Id = id;
        _tokenId = tokenId;
        _version = document.Version;
        _triviaVersion = document.GetElementTriviaVersion(tokenId);
    }

    private readonly int _version;
    private readonly int _triviaVersion;
    private readonly Document _doc;
    private readonly uint _tokenId;
}