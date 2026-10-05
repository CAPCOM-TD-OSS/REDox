// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace REDox;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
[DebuggerTypeProxy(typeof(DebugView))]
public abstract partial class Document : IDisposable
{
    protected Document(SerializerSettings settings, bool isPooled = true)
    {
        _tokens = Array.Empty<DToken>();
        _tokenPt = 1;
        Settings = settings;
        IsPooled = isPooled;
    }

    public DElement RootElement => new(this, RootId);

    public bool IsValid => RootId > 0;

    protected uint RootId { get; set; }

    public SerializerSettings Settings { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void EnsureCapacity(int capacity)
    {
        capacity = Math.Max(capacity, 2);

        if (_tokens.Length < capacity)
        {
            ExpandTokens(capacity);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        Version++;

        if (_tokens.Length > 0)
        {
            if (IsPooled)
            {
                ArrayPool<DToken>.Shared.Return(_tokens);
            }

            _tokens = [];
        }

        _tokenPt = 0;

        ClearExtensionInfo();

        RootId = 0;
    }

    public ReadOnlySpan<DToken> GetTokens()
    {
        return _tokens.AsSpan(0, _tokenPt);
    }

    internal ReadOnlySpan<object?> GetExtends()
    {
        return _extends.AsSpan(0, _extendCount);
    }

    protected void Reset(SerializerSettings settings)
    {
        _tokenPt = 1;
        Version++;
        Settings = settings;
        RootId = 0;
        ClearExtensionInfo();
    }

    protected T CreateSnapshot<T>() where T : Document
    {
        if (!IsValid)
        {
            throw new ObjectDisposedException(nameof(Document));
        }

        var doc = (T)MemberwiseClone();
        doc._tokens = _tokens.AsSpan(0, _tokenPt).ToArray();
        doc.IsPooled = false;
        doc._parentTable = null;
        doc._parentVersion = 0;

        var count = _extendCount;

        doc._extends = count == 0 ? [] : new object?[count];
        doc._extendCount = count;

        _extends.AsSpan(0, count).CopyTo(doc._extends.AsSpan());

        for (var i = 0; i < count; i++)
        {
            var v = doc._extends[i];

            if (v is DContainer)
            {
                if (v is DObject obj)
                {
                    doc._extends[i] = new DObject(new DElement(doc, obj.Id), obj);
                }
                else if (v is DMap map)
                {
                    doc._extends[i] = new DMap(new DElement(doc, map.Id), map);
                }
                else if (v is DArray arr)
                {
                    doc._extends[i] = new DArray(new DElement(doc, arr.Id), arr);
                }
            }
            else if (v is List<uint> trivia)
            {
                doc._extends[i] = new List<uint>(trivia);
            }
        }

        return doc;
    }

    protected virtual ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding UTF-8 bytes.");
    }

    protected virtual string DecodeString(DToken token)
    {
        return Encoding.UTF8.GetString(DecodeUtf8Bytes(token));
    }

    protected virtual long DecodeInteger(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding integers.");
    }

    protected decimal DecodeDecimal(DToken token)
    {
        if (TryDecodeDecimal(token, out var result))
        {
            return result;
        }

        throw new OverflowException("The value cannot be represented as a decimal.");
    }

    protected virtual bool TryDecodeDecimal(DToken token, out decimal result)
    {
        return TryConvertToDecimal(DecodeFloat(token), out result);
    }

    internal static bool TryConvertToDecimal(double value, out decimal result)
    {
        if (double.IsFinite(value) && value > (double)decimal.MinValue && value < (double)decimal.MaxValue)
        {
            result = (decimal)value;
            return true;
        }

        result = default;
        return false;
    }

    protected virtual double DecodeFloat(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding floating-point numbers.");
    }

    protected virtual DateTime DecodeDateTime(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding DateTime values.");
    }

    protected virtual DateTimeOffset DecodeDateTimeOffset(DToken token)
    {
        return new DateTimeOffset(DecodeDateTime(token));
    }

    protected virtual ReadOnlySpan<byte> DecodeBigNumber(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding big numbers.");
    }

    protected virtual ReadOnlySpan<byte> DecodeByteString(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding byte strings.");
    }

    protected virtual ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        throw new NotSupportedException("This document does not support decoding trivia.");
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected internal uint AllocToken(DToken token)
    {
        var tokenId = _tokenPt++;
        var tokens = _tokens;

        if (tokenId < tokens.Length)
        {
            tokens[tokenId] = token;
        }
        else
        {
            ExpandTokens(tokens.Length * 2);
            _tokens[tokenId] = token;
        }

        return (uint)tokenId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void LinkToken(uint latestId, uint tokenId)
    {
        var latest = _tokens[latestId];

        if (latest.IsContainer)
        {
            _tokens[latestId].LinkId = tokenId;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void IncToken(uint tokenId)
    {
        _tokens[tokenId].Increment();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected internal uint NextToken(uint tokenId)
    {
        var token = GetToken(tokenId);

        if (token.IsContainer)
        {
            return token.LinkId;
        }

        return tokenId + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected internal DToken GetToken(uint tokenId)
    {
        return _tokens[tokenId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected internal void SetToken(uint tokenId, DToken value)
    {
        _tokens[tokenId] = value;
    }
}