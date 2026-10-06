// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using REDox.Json;
using REDox.Serialization;

namespace REDox;

public readonly struct DProperty : IEquatable<DProperty>
{
    internal DProperty(Document doc, uint nameId, uint valueId)
    {
        _document = doc;
        _nameId = nameId;
        _valueId = valueId;
        _version = doc.Version;
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(nameof(DProperty));
        }
    }

    public DTriviaCollection LeadingTrivia
    {
        get
        {
            ThrowIfInvalid();

            return new DTriviaCollection(new DElement(_document, _nameId));
        }
    }

    public bool IsValid
    {
        get
        {
            if (_document == null)
            {
                return false;
            }

            return _document.Version == _version;
        }
    }

    public string Name
    {
        get
        {
            ThrowIfInvalid();

            return _document.GetStringValue(_nameId);
        }
    }

    public bool NameEquals(ReadOnlySpan<byte> utf8Text)
    {
        ThrowIfInvalid();

        if (_document.GetToken(_nameId).IsExtended)
        {
            return Utf8Helper.Equals(utf8Text, _document.GetStringValue(_nameId));
        }

        return _document.GetUtf8BytesValue(_nameId).SequenceEqual(utf8Text);
    }

    public bool NameEquals(ReadOnlySpan<char> text)
    {
        ThrowIfInvalid();

        if (_document.GetToken(_nameId).IsExtended)
        {
            return _document.GetStringValue(_nameId).AsSpan().SequenceEqual(text);
        }

        return Utf8Helper.Equals(_document.GetUtf8BytesValue(_nameId), text);
    }

    public bool NameEquals(string text)
    {
        ThrowIfInvalid();

        if (_document.GetToken(_nameId).IsExtended)
        {
            return _document.GetStringValue(_nameId) == text;
        }

        return text != null && Utf8Helper.Equals(_document.GetUtf8BytesValue(_nameId), text.AsSpan());
    }

    public DElement Value
    {
        get
        {
            ThrowIfInvalid();

            return new DElement(_document, _valueId);
        }
    }

    public void WriteTo(DataWriter writer)
    {
        ThrowIfInvalid();

        writer.WriteString(_document.GetUtf8BytesValue(_nameId));

        writer.WriteValue(new DElement(_document, _valueId));
    }

    public override string ToString()
    {
        ThrowIfInvalid();

        return new DElement(_document, _nameId).ToJsonString() + ":" + Value.ToJsonString();
    }

    public bool Equals(DProperty other)
    {
        return _document == other._document &&
               _nameId == other._nameId &&
               _valueId == other._valueId &&
               _version == other._version;
    }

    public override bool Equals(object? obj)
    {
        return obj is DProperty other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_document, _nameId, _valueId, _version);
    }

    private readonly Document _document;
    private readonly uint _nameId;
    private readonly uint _valueId;
    private readonly int _version;
}