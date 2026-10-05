// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace REDox.Serialization;

public abstract class ReferenceResolver
{
    private Stack<object>? _cycleDetectedStack;

    private protected ReferenceResolver()
    {
    }

    internal abstract void AddReference(in DataReader reader, uint tokenId, object value);

    internal abstract bool TryWriteReference(DataWriter writer, object value);

    internal abstract void WriteReferenceId(DataWriter writer);

    internal abstract object? ResolveReference(in DataReader reader, uint tokenId);

    protected internal virtual bool CheckReferenced(object value)
    {
        return false;
    }

    internal bool IsCycleReference(object value)
    {
        if (_cycleDetectedStack is null)
        {
            return false;
        }

        foreach (var item in _cycleDetectedStack)
        {
            if (ReferenceEquals(item, value))
            {
                return true;
            }
        }

        return false;
    }

    internal void PushCycleReference(object value)
    {
        _cycleDetectedStack ??= new Stack<object>();
        _cycleDetectedStack.Push(value);
    }

    internal void PopCycleReference()
    {
        _cycleDetectedStack?.Pop();
    }
}

public abstract class ReferenceResolver<TId> : ReferenceResolver
{
    private TId _pendingId = default!;

    protected abstract TId GetReference(object value, out bool alreadyExists);

    protected abstract void AddReference(TId referenceId, object value);

    protected abstract object? ResolveReference(TId referenceId);

    protected abstract void WriteId(DataWriter writer, TId referenceId);

    protected abstract TId ReadId(in DataReader reader, uint tokenId);

    internal sealed override void AddReference(in DataReader reader, uint tokenId, object value)
    {
        AddReference(ReadId(in reader, tokenId), value);
    }

    internal sealed override bool TryWriteReference(DataWriter writer, object value)
    {
        var referenceId = GetReference(value, out var alreadyExists);

        if (!alreadyExists)
        {
            _pendingId = referenceId;
            return false;
        }

        writer.WriteStartMap(1);
        writer.WriteSymbol(Utf8Helper.RefTag, SymbolKind.Metadata);
        WriteId(writer, referenceId);
        writer.WriteEndMap();
        return true;
    }

    internal sealed override void WriteReferenceId(DataWriter writer)
    {
        writer.WriteSymbol(Utf8Helper.IdTag, SymbolKind.Metadata);
        WriteId(writer, _pendingId);
        _pendingId = default!;
    }

    internal sealed override object? ResolveReference(in DataReader reader, uint tokenId)
    {
        return ResolveReference(ReadId(in reader, tokenId));
    }
}

sealed class DefaultReferenceResolver : ReferenceResolver<int>
{
    private readonly Dictionary<object, int> _objectToReferenceId = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, object> _referenceIdToObject = new();
    private int _referenceCount;

    protected override int GetReference(object value, out bool alreadyExists)
    {
        ref var referenceId =
            ref CollectionsMarshal.GetValueRefOrAddDefault(_objectToReferenceId, value, out alreadyExists);

        if (!alreadyExists)
        {
            referenceId = ++_referenceCount;
        }

        return referenceId;
    }

    protected internal override bool CheckReferenced(object value)
    {
        return _objectToReferenceId.ContainsKey(value);
    }

    protected override void AddReference(int referenceId, object value)
    {
        _referenceIdToObject.Add(referenceId, value);
    }

    protected override object? ResolveReference(int referenceId)
    {
        return _referenceIdToObject.GetValueOrDefault(referenceId);
    }

    protected override void WriteId(DataWriter writer, int referenceId)
    {
        Span<char> buffer = stackalloc char[11];
        referenceId.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture);
        writer.WriteSymbol(buffer[..written], SymbolKind.Reference);
    }

    protected override int ReadId(in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        if (token.IsNumeric)
        {
            return reader.ReadInt32(tokenId);
        }

        if (token.IsStringEncoded)
        {
            var utf8Bytes = reader.ReadUtf8String(tokenId);

            if (Utf8Parser.TryParse(utf8Bytes, out int id, out var bytesConsumed) && utf8Bytes.Length == bytesConsumed)
            {
                return id;
            }
        }

        throw new FormatException();
    }
}