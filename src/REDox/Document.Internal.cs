// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using REDox.Serialization;

namespace REDox;

public abstract partial class Document
{
    private uint _emptyExtendId;
    private uint _emptyTokenId;
    private int _extendCount;
    private object?[] _extends = [];
    private uint[]? _parentTable;
    private int _parentVersion;
    private int _tokenPt;
    private DToken[] _tokens;

    internal int Version { get; private set; }

    public bool IsPooled { get; private set; }

    private void ExpandTokens(int capacity)
    {
        if (capacity < 2)
        {
            capacity = 2;
        }

        var tokens = _tokens;

        if (!IsPooled)
        {
            Array.Resize(ref _tokens, capacity);
        }
        else
        {
            if (_tokens.Length < capacity)
            {
                _tokens = ArrayPool<DToken>.Shared.Rent(capacity);

                if (tokens.Length > 1)
                {
                    tokens.AsSpan().Slice(0, _tokenPt - 1).CopyTo(_tokens);
                    ArrayPool<DToken>.Shared.Return(tokens);
                }

                _tokens[0] = default;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint AllocEmptyToken(DToken token)
    {
        if (_emptyTokenId != 0)
        {
            var tokenId = _emptyTokenId;

            _emptyTokenId = _tokens[tokenId].EmptyId;
            _parentVersion = 0;

            _tokens[tokenId] = token;

            return tokenId;
        }

        return AllocToken(token);
    }

    private void ExpandExtends(int capacity)
    {
        if (!IsPooled)
        {
            Array.Resize(ref _extends, capacity);
        }
        else
        {
            var buffer = ArrayPool<object?>.Shared.Rent(capacity);
            _extends.AsSpan().Slice(0, _extendCount).CopyTo(buffer.AsSpan());
            if (_extends.Length > 0)
            {
                ArrayPool<object?>.Shared.Return(_extends, true);
            }

            _extends = buffer;
        }
    }

    private void ClearExtensionInfo()
    {
        if (!IsPooled)
        {
            _extends = [];
            _parentTable = null;
        }
        else
        {
            if (_extends.Length > 0)
            {
                Array.Clear(_extends, 0, _extendCount);
                ArrayPool<object?>.Shared.Return(_extends);
                _extends = [];
            }

            if (_parentTable != null)
            {
                ArrayPool<uint>.Shared.Return(_parentTable);
                _parentTable = null;
            }
        }

        _extendCount = 0;
        _emptyTokenId = 0;
        _emptyExtendId = 0;
    }

    internal DObject AddExtendObject(int capacity)
    {
        var tokenId = AllocEmptyExtendToken();
        var extendId = _tokens[tokenId].ExtendId;

        var obj = new DObject(new DElement(this, tokenId), capacity);

        _extends[extendId] = obj;
        _tokens[tokenId] = DToken.MakeExtendContainer(DTokenType.Map, extendId);

        return obj;
    }

    internal DMap AddExtendMap(int capacity)
    {
        var tokenId = AllocEmptyExtendToken();
        var extendId = _tokens[tokenId].ExtendId;

        var map = new DMap(new DElement(this, tokenId), capacity);
        _extends[extendId] = map;
        _tokens[tokenId] = DToken.MakeExtendContainer(DTokenType.Map, extendId);

        return map;
    }

    internal DArray AddExtendArray(int capacity)
    {
        var tokenId = AllocEmptyExtendToken();
        var extendId = _tokens[tokenId].ExtendId;

        var arr = new DArray(new DElement(this, tokenId), capacity);

        _extends[extendId] = arr;
        _tokens[tokenId] = DToken.MakeExtendContainer(DTokenType.Array, extendId);

        return arr;
    }

    private void EnsureExtendCapacity(int capacity)
    {
        if (_extends.Length < capacity)
        {
            ExpandExtends(capacity);
        }
    }

    private void BuildParentTable(uint[] parentTable, uint rootId)
    {
        using var stack = new Helper.LocalStack<uint>(stackalloc uint[64], int.MaxValue);

        stack.Push(rootId);

        while (stack.Count > 0)
        {
            var tokenId = stack.Pop();
            var token = GetToken(tokenId);

            if (!token.IsContainer)
            {
                continue;
            }

            if (token.Type == DTokenType.Map)
            {
                foreach (var kv in EnumerateKeyValue(tokenId))
                {
                    parentTable[kv.Key] = tokenId;
                    parentTable[kv.Value] = tokenId;

                    stack.Push(kv.Key);
                    stack.Push(kv.Value);
                }
            }
            else
            {
                foreach (var v in EnumerateValue(tokenId))
                {
                    parentTable[v] = tokenId;

                    stack.Push(v);
                }
            }
        }
    }

    internal uint GetParentId(uint tokenId)
    {
        if (_parentTable == null || _parentVersion != _tokenPt)
        {
            if (_parentTable == null || _parentTable.Length < _tokenPt)
            {
                if (!IsPooled)
                {
                    _parentTable = new uint[_tokens.Length];
                }
                else
                {
                    if (_parentTable != null)
                    {
                        ArrayPool<uint>.Shared.Return(_parentTable);
                    }

                    _parentTable = ArrayPool<uint>.Shared.Rent(_tokens.Length);
                }
            }

            _parentTable.AsSpan(0, _tokenPt).Clear();
            BuildParentTable(_parentTable, RootElement.Id);
            _parentVersion = _tokenPt;
        }

        return _parentTable[tokenId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CreateExtension(DContainer container, int capacity, DToken token)
    {
        IsPooled = false;
        _extends = new object[capacity + 1];
        _extends[0] = container;
        _extendCount = 1;
        _tokens = new DToken[capacity + 2];
        _tokens[1] = token;
        _tokenPt = 2;
        RootId = 1;
    }

    private DMap CreateMap(DToken token, uint tokenId)
    {
        if (token.Type != DTokenType.Map)
        {
            throw new InvalidOperationException();
        }

        var count = token.Count;
        var map = new DMap(new DElement(this, tokenId), count);

        foreach (var kv in EnumerateKeyValue(tokenId))
        {
            map.AddKeyValuePairInternal(kv.Key, kv.Value);
        }

        var extendId = AllocExtendId();

        _extends[extendId] = map;
        _tokens[tokenId] = DToken.MakeExtendContainer(DTokenType.Map, extendId, token.LinkId);

        return map;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DMap GetElementMap(uint tokenId)
    {
        var token = GetToken(tokenId);

        if (token.IsExtended)
        {
            var inst = _extends[token.ExtendId];

            if (inst is DMap map)
            {
                return map;
            }

            var obj = (DObject)inst!;
            map = new DMap(new DElement(this, tokenId), obj);
            obj.Invalidate();
            _extends[token.ExtendId] = map;
            return map;
        }

        return CreateMap(token, tokenId);
    }

    private DObject CreateObject(DToken token, uint tokenId)
    {
        if (token.Type != DTokenType.Map)
        {
            throw new InvalidOperationException();
        }

        var count = token.Count;
        var obj = new DObject(new DElement(this, tokenId), count);

        foreach (var kv in EnumerateKeyValue(tokenId))
        {
            obj.AddKeyValuePairInternal(kv.Key, kv.Value);
        }

        var extendId = AllocExtendId();

        _extends[extendId] = obj;
        _tokens[tokenId] = DToken.MakeExtendContainer(DTokenType.Map, extendId, token.LinkId);

        return obj;
    }

    internal List<uint> GetElementTriviaList(uint tokenId)
    {
        var token = GetToken(tokenId);

        var triviaId = GetLeadingTrivia(tokenId);

        if (triviaId == 0 || triviaId == uint.MaxValue)
        {
            triviaId = CreateLeadingTrivia(tokenId);

            if (token.IsContainer)
            {
                var container = GetElementContainer(tokenId);
                container.TriviaId = triviaId;
            }
            else
            {
                if (token.IsExtended && !token.IsExtendInlineLiteral)
                {
                    _tokens[tokenId].TriviaId = triviaId;
                }
                else
                {
                    switch (token.Kind)
                    {
                        case DTokenKind.Boolean:
                        case DTokenKind.Null:
                            _tokens[tokenId] = DToken.MakeExtendLiteral(token.Variant, triviaId);
                            break;
                        case DTokenKind.Integer:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.IntegerKind;
                                if (kind == IntegerKind.Inherit)
                                {
                                    kind = IntegerKind.Default;
                                }

                                if (kind == IntegerKind.Unsigned)
                                {
                                    _extends[extendId] = (long)GetUnsignedIntegerValue(tokenId);
                                }
                                else
                                {
                                    _extends[extendId] = GetSignedIntegerValue(tokenId);
                                }

                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.String:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.StringKind;
                                if (kind == StringKind.Inherit)
                                {
                                    kind = StringKind.Default;
                                }

                                _extends[extendId] = GetStringValue(tokenId);
                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.Symbol:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.SymbolKind;
                                if (kind == SymbolKind.Inherit)
                                {
                                    kind = SymbolKind.Default;
                                }

                                _extends[extendId] = GetStringValue(tokenId);
                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.Float:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.FloatKind;
                                if (kind == FloatKind.Inherit)
                                {
                                    kind = FloatKind.Default;
                                }

                                if (kind == FloatKind.Decimal)
                                {
                                    _extends[extendId] = GetDecimalValue(tokenId);
                                }
                                else
                                {
                                    _extends[extendId] = GetFloatingValue(tokenId);
                                }

                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.InlineFloat:
                            {
                                var extendId = AllocExtendId();
                                _extends[extendId] = GetFloatingValue(tokenId);
                                _tokens[tokenId] = DToken.MakeExtend(FloatKind.Default.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.Timestamp:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.TimestampKind;
                                if (kind == TimestampKind.Inherit)
                                {
                                    kind = TimestampKind.Default;
                                }

                                if (kind == TimestampKind.OffsetDateTime)
                                {
                                    _extends[extendId] = GetDateTimeOffsetValue(tokenId);
                                }
                                else
                                {
                                    _extends[extendId] = GetDateTimeValue(tokenId);
                                }

                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.ByteString:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.ByteStringKind;
                                if (kind == ByteStringKind.Inherit)
                                {
                                    kind = ByteStringKind.Default;
                                }

                                _extends[extendId] = GetByteStringValue(tokenId).ToArray();
                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        case DTokenKind.BigNumber:
                            {
                                var extendId = AllocExtendId();
                                var kind = token.BigNumberKind;
                                if (kind == BigNumberKind.Inherit)
                                {
                                    kind = BigNumberKind.Default;
                                }

                                _extends[extendId] = GetBigNumberValue(tokenId).ToArray();
                                _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), triviaId, extendId);
                            }
                            break;
                        default:
                            throw new NotSupportedException($"{token.Kind} is not supported.");
                    }
                }
            }
        }

        return (List<uint>)_extends[triviaId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DContainer GetElementContainer(uint tokenId)
    {
        var token = GetToken(tokenId);

        if (token.IsExtended)
        {
            return (DContainer)_extends[token.ExtendId]!;
        }

        if (token.Type == DTokenType.Map)
        {
            return CreateObject(token, tokenId);
        }

        return CreateArray(token, tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DObject GetElementObject(uint tokenId)
    {
        var token = GetToken(tokenId);

        if (token.Type != DTokenType.Map)
        {
            throw new InvalidOperationException();
        }

        if (token.IsExtended)
        {
            var inst = _extends[token.ExtendId];

            if (inst is DObject obj)
            {
                return obj;
            }

            var map = (DMap)inst!;
            obj = new DObject(new DElement(this, tokenId), map);
            map.Invalidate();
            _extends[token.ExtendId] = obj;
            return obj;
        }

        return CreateObject(token, tokenId);
    }

    private DArray CreateArray(DToken token, uint tokenId)
    {
        if (token.Type != DTokenType.Array)
        {
            throw new InvalidOperationException();
        }

        var count = token.Count;

        var arr = new DArray(new DElement(this, tokenId), count);

        foreach (var valueId in EnumerateValue(tokenId))
        {
            arr.AddInternal(valueId);
        }

        var extendId = AllocExtendId();

        _extends[extendId] = arr;
        _tokens[tokenId] = DToken.MakeExtendContainer(DTokenType.Array, extendId, token.LinkId);

        return arr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint AllocEmptyExtendToken()
    {
        if (_emptyExtendId != 0)
        {
            var tokenId = _emptyExtendId;

            _emptyExtendId = _tokens[tokenId].EmptyId;
            _parentVersion = 0;

            return tokenId;
        }

        var extendId = AllocExtendId();

        return AllocToken(DToken.MakeEmptyExtend(0, extendId));
    }

    private uint AllocExtendId()
    {
        if (_emptyExtendId != 0)
        {
            var tokenId = _emptyExtendId;
            var token = _tokens[tokenId];
            _emptyExtendId = token.EmptyId;

            _tokens[tokenId] = DToken.MakeEmpty(_emptyTokenId);
            _emptyTokenId = tokenId;
            _parentVersion = 0;

            return token.ExtendId;
        }

        var id = _extendCount;

        if (_extendCount >= _extends.Length)
        {
            ExpandExtends(Math.Max(4, _extends.Length * 2));
        }

        _extendCount++;

        return (uint)id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DArray GetElementArray(uint tokenId)
    {
        var token = GetToken(tokenId);

        if (token.IsExtended)
        {
            return (DArray)_extends[token.ExtendId]!;
        }

        return CreateArray(token, tokenId);
    }

    internal bool ValidateContainer(uint tokenId, DContainer container)
    {
        if (RootId == 0)
        {
            return false;
        }

        var token = GetToken(tokenId);

        if (token.IsExtended && !token.IsExtendInlineLiteral)
        {
            return _extends[token.ExtendId] == container;
        }

        return false;
    }

    private void ClearContainer(uint tokenId)
    {
        var token = _tokens[tokenId];

        Debug.Assert(token.IsContainer);

        if (token.IsExtended)
        {
            var extendId = token.ExtendId;
            ((DContainer)_extends[extendId]!).Release();
            _extends[extendId] = null;
        }
        else
        {
            var linkId = token.LinkId;

            if (token.Type == DTokenType.Array)
            {
                foreach (var v in EnumerateValue(tokenId))
                {
                    Free(v, tokenId, linkId);
                }
            }
            else
            {
                foreach (var kv in EnumerateKeyValue(tokenId))
                {
                    Free(kv.Key, tokenId, linkId);
                    Free(kv.Value, tokenId, linkId);
                }
            }
        }
    }

    internal void Free(uint tokenId, uint rootId, uint linkId)
    {
        var token = _tokens[tokenId];

        if (token.IsContainer)
        {
            ClearContainer(tokenId);
        }

        if (linkId == 0 || linkId == rootId + 1 || tokenId != rootId + 1)
        {
            if (token.IsExtended && !token.IsExtendInlineLiteral)
            {
                var extendId = token.ExtendId;

                _extends[extendId] = null;

                if (_emptyExtendId != 0)
                {
                    _tokens[tokenId] = DToken.MakeEmptyExtend(_emptyExtendId, extendId);
                    _emptyExtendId = tokenId;
                }
                else
                {
                    _tokens[tokenId] = DToken.MakeEmptyExtend(0, extendId);
                    _emptyExtendId = tokenId;
                }
            }
            else
            {
                if (_emptyTokenId != 0)
                {
                    _tokens[tokenId] = DToken.MakeEmpty(_emptyTokenId);
                    _emptyTokenId = tokenId;
                }
                else
                {
                    _tokens[tokenId] = DToken.MakeEmpty(0);
                    _emptyTokenId = tokenId;
                }
            }
        }
        else
        {
            _tokens[tokenId] = default;
        }

        Version++;
        _parentVersion = 0;
    }

    internal uint JoinValue(uint tokenId, DValue value)
    {
        if (value.Instance is DTokenVariant variant)
        {
            switch (variant.ToKind())
            {
                case DTokenKind.String:
                    {
                        Span<byte> temp = stackalloc byte[8];
                        var c = (char)value.Payload;
                        var bytes = Encoding.UTF8.GetBytes([c], temp);
                        var utf8Bytes = temp.Slice(0, bytes);

                        return ExtendStringToken(tokenId, (StringKind)((int)variant & 7),
                            Utf8Helper.GetUtf16String(utf8Bytes));
                    }
                case DTokenKind.Boolean:
                case DTokenKind.Null:
                    return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(variant, 0));
                case DTokenKind.Float:
                    {
                        var payload = value.Payload;
                        var floatValue = Unsafe.BitCast<long, double>(payload);

                        return ExtendFloatToken(tokenId, (FloatKind)((int)variant & 7), floatValue);
                    }
                case DTokenKind.Integer:
                    if (variant == DTokenVariant.IntegerUnsigned)
                    {
                        return ExtendIntegerToken(tokenId, (ulong)value.Payload);
                    }

                    return ExtendIntegerToken(tokenId, (IntegerKind)((int)variant & 7), value.Payload);
                case DTokenKind.Timestamp:
                    {
                        var payload = value.Payload;
                        var dateTimeValue = Unsafe.BitCast<long, DateTime>(payload);

                        return ExtendToken(tokenId, variant, dateTimeValue);
                    }
                default:
                    throw new InvalidOperationException();
            }
        }

        if (value.Payload == 0)
        {
            if (value.Instance == null)
            {
                return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(DTokenVariant.Null, 0));
            }

            return SerializeToTokens(tokenId, value.Instance, value.Instance?.GetType() ?? typeof(object), Settings);
        }

        if (value.Instance is Document document)
        {
            return JoinElement(tokenId, new DElement(document, (uint)value.Payload));
        }

        if (value.Instance is DContainer container)
        {
            return JoinElement(tokenId, new DElement(container.Document, (uint)value.Payload));
        }

        return ExtendToken(tokenId, new DToken(value.Payload).Variant, value.Instance);
    }

    private uint ExtendIntegerToken(uint tokenId, ulong value)
    {
        if (value <= DToken.MaxInlineInteger)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendInlineInteger(IntegerKind.Unsigned, (long)value));
        }

        return ExtendToken(tokenId, DTokenVariant.IntegerUnsigned, (long)value);
    }

    private uint ExtendStringToken(uint tokenId, StringKind kind, string? value)
    {
        if (value == null)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(DTokenVariant.Null, 0));
        }

        return ExtendToken(tokenId, kind.ToVariant(), value);
    }

    private uint ExtendSymbolToken(uint tokenId, string? value, SymbolKind kind)
    {
        if (value == null)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(DTokenVariant.Null, 0));
        }

        return ExtendToken(tokenId, kind.ToVariant(), value);
    }

    private uint ExtendSymbolToken(uint tokenId, Utf8Symbol value, SymbolKind kind)
    {
        return ExtendToken(tokenId, kind.ToVariant(), value);
    }

    private uint ExtendIntegerToken(uint tokenId, IntegerKind kind, long value)
    {
        if (value >= DToken.MinInlineInteger && value <= DToken.MaxInlineInteger)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendInlineInteger(kind, value));
        }

        return ExtendToken(tokenId, kind.ToVariant(), value);
    }

    private uint ExtendFloatToken(uint tokenId, FloatKind kind, double value)
    {
        var s = (float)value;

        if (s == value)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendInlineSingle(kind, s));
        }

        if (kind == FloatKind.Default)
        {
            var payload = DToken.EncodeInlineFloatPayload(value);
            if (payload >= 0)
            {
                return ExtendLiteralToken(tokenId, DToken.MakeExtendInlineFloat(payload));
            }
        }

        return ExtendToken(tokenId, kind.ToVariant(), value);
    }

    internal uint JoinAny<T>(uint tokenId, T value)
    {
        if (typeof(T) == typeof(DValue))
        {
            return JoinValue(tokenId, Unsafe.As<T, DValue>(ref value));
        }

        if (typeof(T) == typeof(DObject))
        {
            return JoinElement(tokenId, Unsafe.As<T, DObject>(ref value).AsElement());
        }

        if (typeof(T) == typeof(DMap))
        {
            return JoinElement(tokenId, Unsafe.As<T, DMap>(ref value).AsElement());
        }

        if (typeof(T) == typeof(DArray))
        {
            return JoinElement(tokenId, Unsafe.As<T, DArray>(ref value).AsElement());
        }

        if (typeof(T) == typeof(DElement))
        {
            return JoinElement(tokenId, Unsafe.As<T, DElement>(ref value));
        }

        if (typeof(T) == typeof(bool))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendLiteral(Unsafe.As<T, bool>(ref value)
                    ? DTokenVariant.BooleanTrue
                    : DTokenVariant.BooleanFalse, 0));
        }

        if (typeof(T) == typeof(byte))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineInteger(IntegerKind.Inherit, Unsafe.As<T, byte>(ref value)));
        }

        if (typeof(T) == typeof(sbyte))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineInteger(IntegerKind.Inherit, Unsafe.As<T, sbyte>(ref value)));
        }

        if (typeof(T) == typeof(ushort))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineInteger(IntegerKind.Inherit, Unsafe.As<T, ushort>(ref value)));
        }

        if (typeof(T) == typeof(short))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineInteger(IntegerKind.Inherit, Unsafe.As<T, short>(ref value)));
        }

        if (typeof(T) == typeof(int))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineInteger(IntegerKind.Inherit, Unsafe.As<T, int>(ref value)));
        }

        if (typeof(T) == typeof(uint))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineInteger(IntegerKind.Inherit, Unsafe.As<T, uint>(ref value)));
        }

        if (typeof(T) == typeof(long))
        {
            return ExtendIntegerToken(tokenId, IntegerKind.Inherit, Unsafe.As<T, long>(ref value));
        }

        if (typeof(T) == typeof(ulong))
        {
            return ExtendIntegerToken(tokenId, Unsafe.As<T, ulong>(ref value));
        }

        if (typeof(T) == typeof(float))
        {
            return ExtendLiteralToken(tokenId,
                DToken.MakeExtendInlineSingle(FloatKind.Single, Unsafe.As<T, float>(ref value)));
        }

        if (typeof(T) == typeof(Half))
        {
            return ExtendFloatToken(tokenId, FloatKind.Half, (double)Unsafe.As<T, Half>(ref value));
        }

        if (typeof(T) == typeof(double))
        {
            return ExtendFloatToken(tokenId, FloatKind.Default, Unsafe.As<T, double>(ref value));
        }

        if (typeof(T) == typeof(decimal))
        {
            return ExtendToken(tokenId, DTokenVariant.FloatDecimal, value);
        }

        if (typeof(T) == typeof(DateTime))
        {
            return ExtendToken(tokenId,
                TimestampKind.Inherit.ToVariant(), value);
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            return ExtendToken(tokenId, DTokenVariant.TimestampOffsetDateTime, value);
        }

        if (typeof(T) == typeof(string))
        {
            return ExtendToken(tokenId, StringKind.Inherit.ToVariant(), Unsafe.As<T, string?>(ref value));
        }

        if (typeof(T) == typeof(Guid))
        {
            Span<byte> guidBytes = stackalloc byte[16];
            Unsafe.As<T, Guid>(ref value).TryWriteBytes(guidBytes, true, out var written);
            return ExtendToken(tokenId, DTokenVariant.ByteStringGuid, guidBytes.ToArray());
        }

        if (typeof(T) == typeof(byte[]))
        {
            return ExtendToken(tokenId, ByteStringKind.Inherit.ToVariant(), value);
        }

        if (value == null)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(DTokenVariant.Null, 0));
        }

        return SerializeToTokens(tokenId, value, value.GetType(), Settings);
    }

    internal uint JoinObject(uint tokenId, object? value)
    {
        if (value == null)
        {
            return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(DTokenVariant.Null, 0));
        }

        if (value is DElement element)
        {
            return JoinElement(tokenId, element);
        }

        if (value is DValue doxValue)
        {
            return JoinValue(tokenId, doxValue);
        }

        if (value is IDoxNode node)
        {
            return JoinElement(tokenId, node.AsElement());
        }

        var type = value.GetType();

        if (!type.IsEnum)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    return JoinAny(tokenId, (string)value);
                case TypeCode.Byte:
                    return JoinAny(tokenId, (byte)value);
                case TypeCode.SByte:
                    return JoinAny(tokenId, (sbyte)value);
                case TypeCode.Int16:
                    return JoinAny(tokenId, (short)value);
                case TypeCode.UInt16:
                    return JoinAny(tokenId, (ushort)value);
                case TypeCode.Int32:
                    return JoinAny(tokenId, (int)value);
                case TypeCode.UInt32:
                    return JoinAny(tokenId, (uint)value);
                case TypeCode.Int64:
                    return JoinAny(tokenId, (long)value);
                case TypeCode.UInt64:
                    return JoinAny(tokenId, (ulong)value);
                case TypeCode.Boolean:
                    return JoinAny(tokenId, (bool)value);
                case TypeCode.Single:
                    return JoinAny(tokenId, (float)value);
                case TypeCode.Double:
                    return JoinAny(tokenId, (double)value);
                case TypeCode.Decimal:
                    return JoinAny(tokenId, (decimal)value);
                case TypeCode.DateTime:
                    return JoinAny(tokenId, (DateTime)value);
            }

            if (type == typeof(DateTimeOffset))
            {
                return JoinAny(tokenId, (DateTimeOffset)value);
            }
        }

        return SerializeToTokens(tokenId, value, type, Settings);
    }

    internal uint JoinTrivia(uint tokenId, byte[] trivia, TriviaKind kind)
    {
        return ExtendTriviaToken(tokenId, kind, trivia);
    }

    internal uint JoinElement(uint tokenId, DElement value)
    {
        var token = value.Token;

        if (token.IsContainer)
        {
            return CopyToTokens(tokenId, value);
        }

        var variant = token.Variant;

        switch (variant.ToKind())
        {
            case DTokenKind.Boolean:
                return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(variant, 0));
            case DTokenKind.Integer:
                {
                    var integerKind = token.IntegerKind;

                    if (integerKind == IntegerKind.Unsigned)
                    {
                        return ExtendIntegerToken(tokenId, (ulong)value.GetInt64());
                    }

                    return ExtendIntegerToken(tokenId, integerKind, value.GetInt64());
                }
            case DTokenKind.Float:
                {
                    var floatKind = token.FloatKind;

                    if (floatKind == FloatKind.Decimal)
                    {
                        return ExtendToken(tokenId, variant, value.GetDecimal());
                    }

                    return ExtendFloatToken(tokenId, floatKind, value.GetDouble());
                }
            case DTokenKind.InlineFloat:
                return ExtendLiteralToken(tokenId, token);
            case DTokenKind.String:
                return ExtendStringToken(tokenId, token.StringKind, value.GetString());
            case DTokenKind.Symbol:
                return ExtendSymbolToken(tokenId, value.GetString(), token.SymbolKind);
            case DTokenKind.Timestamp:
                if (variant == DTokenVariant.TimestampOffsetDateTime)
                {
                    return ExtendToken(tokenId, variant, value.GetDateTimeOffset());
                }

                return ExtendToken(tokenId, variant, value.GetDateTime());

            case DTokenKind.BigNumber:
                return ExtendToken(tokenId, variant, value.GetBigNumber().ToArray());
            case DTokenKind.ByteString:
                return ExtendToken(tokenId, variant, value.GetByteString().ToArray());
            case DTokenKind.Null:
                return ExtendLiteralToken(tokenId, DToken.MakeExtendLiteral(DTokenVariant.Null, 0));
            default:
                throw new NotSupportedException();
        }
    }


    internal KeyValueEnumerator EnumerateKeyValue(uint tokenId)
    {
        return new KeyValueEnumerator(this, tokenId);
    }

    internal int GetValueCount(uint tokenId)
    {
        var token = GetToken(tokenId);

        if (!token.IsContainer)
        {
            throw new InvalidOperationException();
        }

        if (token.IsExtended)
        {
            return GetExtendContainer(token).Count;
        }

        return token.Count;
    }

    internal TriviaTokenEnumerator EnumerateTrivia(uint tokenId)
    {
        return new TriviaTokenEnumerator(this, tokenId);
    }

    internal ValueEnumerator EnumerateValue(uint tokenId)
    {
        return new ValueEnumerator(this, tokenId);
    }

    protected void ReplaceTokens(ReadOnlySpan<DToken> tokens)
    {
        ExpandTokens(tokens.Length);

        tokens.CopyTo(_tokens);
        _tokenPt = tokens.Length;
        _parentVersion = 0;
    }

    private uint CreateLeadingTrivia(uint tokenId)
    {
        var enumerator = new TriviaTokenEnumerator(this, tokenId);

        var count = enumerator.Count;

        var list = new List<uint>(count);

        foreach (var triviaId in enumerator)
        {
            list.Add(triviaId);
        }

        var extendId = AllocExtendId();

        if (extendId == 0)
        {
            extendId = AllocExtendId();
        }

        _extends[extendId] = list;

        return extendId;
    }

    private uint GetLeadingTrivia(uint tokenId)
    {
        var token = _tokens[tokenId];

        if (token.IsExtended)
        {
            if (token.IsContainer)
            {
                return GetExtendContainer(token).TriviaId;
            }

            if (!token.IsInlinePayload)
            {
                return token.TriviaId;
            }
        }
        else
        {
            if (_tokens[tokenId - 1].IsLeadingTrivia)
            {
                return uint.MaxValue;
            }
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ExtendLiteralToken(uint tokenId, DToken literalToken)
    {
        Debug.Assert(literalToken.IsExtended);

        if (tokenId > 0)
        {
            var token = _tokens[tokenId];
            var triviaId = GetLeadingTrivia(tokenId);

            if (literalToken.IsInlinePayload && token.Kind == literalToken.Kind)
            {
                if (literalToken.InlineKind == 7)
                {
                    literalToken.InlineKind = (uint)token.Variant & 7;
                }
            }

            if (triviaId > 0)
            {
                if (literalToken.IsInlinePayload)
                {
                    if (literalToken.Kind == DTokenKind.InlineFloat)
                    {
                        return ExtendToken(tokenId, DTokenVariant.Float, DToken.DecodeInlineFloatPayload(literalToken));
                    }

                    switch (literalToken.Kind)
                    {
                        case DTokenKind.Integer:
                            return ExtendToken(tokenId, literalToken.IntegerKind.ToVariant(),
                                DToken.DecodeInlineIntegerPayload(literalToken));
                        case DTokenKind.Float:
                            return ExtendToken(tokenId, literalToken.FloatKind.ToVariant(),
                                (double)DToken.DecodeInlineSinglePayload(literalToken));
                    }

                    throw new NotSupportedException();
                }

                if (triviaId == uint.MaxValue)
                {
                    triviaId = CreateLeadingTrivia(tokenId);
                }

                literalToken.TriviaId = triviaId;
            }

            if (token.IsContainer)
            {
                ClearContainer(tokenId);

                var linkId = token.LinkId;
                if (linkId != 0 && linkId != tokenId + 1)
                {
                    Debug.Assert(_tokens[tokenId + 1] == default);

                    _tokens[tokenId + 1] = DToken.MakeJump(linkId);
                }
            }

            if (token.IsExtended && !token.IsExtendInlineLiteral)
            {
                _emptyExtendId = AllocEmptyToken(DToken.MakeEmptyExtend(_emptyExtendId, token.ExtendId));
            }

            _tokens[tokenId] = literalToken;
        }
        else
        {
            tokenId = AllocEmptyToken(literalToken);
        }

        return tokenId;
    }

    private uint ExtendContainer(uint tokenId, DTokenType type, int capacity)
    {
        var extendId = 0U;
        var linkId = 0U;

        if (tokenId != 0)
        {
            var token = _tokens[tokenId];
            linkId = tokenId + 1;

            if (token.IsContainer)
            {
                ClearContainer(tokenId);
                linkId = token.LinkId;
            }

            if (token.IsExtended && !token.IsExtendInlineLiteral)
            {
                extendId = token.ExtendId;
            }
            else
            {
                extendId = AllocExtendId();
            }
        }
        else
        {
            tokenId = AllocEmptyExtendToken();
            extendId = _tokens[tokenId].ExtendId;
        }

        if (type == DTokenType.Map)
        {
            _extends[extendId] = new DObject(new DElement(this, tokenId), capacity);
        }
        else
        {
            _extends[extendId] = new DArray(new DElement(this, tokenId), capacity);
        }

        _tokens[tokenId] = DToken.MakeExtendContainer(type, extendId, linkId);

        return tokenId;
    }

    private uint ExtendTriviaToken(uint tokenId, TriviaKind kind, byte[] value)
    {
        if (tokenId != 0)
        {
            var token = _tokens[tokenId];

            Debug.Assert(token.Kind == DTokenKind.Trivia);

            var extendId = 0U;

            if (token.IsExtended)
            {
                extendId = token.ExtendId;
            }
            else
            {
                extendId = AllocExtendId();
            }

            if (kind == TriviaKind.Inherit)
            {
                kind = token.TriviaKind;
            }

            _extends[extendId] = value;
            _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), 0, extendId);
        }
        else
        {
            if (kind == TriviaKind.Inherit)
            {
                kind = TriviaKind.Default;
            }

            tokenId = AllocEmptyExtendToken();

            var extendId = _tokens[tokenId].ExtendId;

            _extends[extendId] = value;
            _tokens[tokenId] = DToken.MakeExtend(kind.ToVariant(), 0, extendId);
        }

        return tokenId;
    }

    private uint ExtendToken(uint tokenId, DTokenVariant variant, object? value)
    {
        Debug.Assert(!variant.IsContainer);

        if (tokenId != 0)
        {
            var token = _tokens[tokenId];
            var linkId = tokenId + 1;
            var triviaId = GetLeadingTrivia(tokenId);

            if (token.IsContainer)
            {
                ClearContainer(tokenId);

                linkId = token.LinkId;

                if (linkId != 0 && linkId != tokenId + 1)
                {
                    Debug.Assert(_tokens[tokenId + 1] == default);

                    _tokens[tokenId + 1] = DToken.MakeJump(linkId);
                }
            }

            var extendId = 0U;

            if (token.IsExtended && !token.IsExtendInlineLiteral)
            {
                extendId = token.ExtendId;
            }
            else
            {
                extendId = AllocExtendId();
            }

            if (triviaId == uint.MaxValue)
            {
                triviaId = CreateLeadingTrivia(tokenId);
            }

            if (variant.IsInherit)
            {
                if (!token.Variant.IsInherit && token.Kind == variant.ToKind())
                {
                    variant = token.Variant;
                }
                else
                {
                    variant = variant.ToDefault();
                }
            }

            _extends[extendId] = value;
            _tokens[tokenId] = DToken.MakeExtend(variant, triviaId, extendId);
        }
        else
        {
            if (variant.IsInherit)
            {
                variant = variant.ToDefault();
            }

            tokenId = AllocEmptyExtendToken();

            var extendId = _tokens[tokenId].ExtendId;

            _extends[extendId] = value;
            _tokens[tokenId] = DToken.MakeExtend(variant, 0, extendId);
        }

        return tokenId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetStringExact(uint tokenId, out string? value)
    {
        ref var token = ref _tokens[tokenId];

        if (token.Kind == DTokenKind.String)
        {
            value = token.IsExtended
                ? GetExtendString(token)
                : DecodeString(token);

            return true;
        }

        if (token.Kind == DTokenKind.Symbol)
        {
            value = token.IsExtended
                ? Utf8Helper.GetUtf16String(GetExtendUtf8Bytes(token))
                : DecodeString(token);

            return true;
        }

        value = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetUtf8BytesExact(uint tokenId, out ReadOnlySpan<byte> value)
    {
        ref var token = ref _tokens[tokenId];

        if (token.Type == DTokenType.Text)
        {
            value = token.IsExtended
                ? GetExtendUtf8Bytes(token)
                : DecodeUtf8Bytes(token);
            return true;
        }

        value = default;
        return false;
    }

    internal string GetStringValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        if (token.Type == DTokenType.Text)
        {
            return token.IsExtended ? GetExtendString(token) : DecodeString(token);
        }

        if (token.Kind == DTokenKind.Null)
        {
            return null!;
        }

        return Utf8Helper.GetUtf16String(GetUtf8BytesValue(tokenId));
    }

    internal ReadOnlySpan<byte> GetUtf8BytesValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        if (token.IsContainer)
        {
            return default;
        }

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                return token.IsExtended
                    ? GetExtendUtf8Bytes(token)
                    : DecodeUtf8Bytes(token);
            case DTokenKind.Integer:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(string));
            {
                Span<byte> temp = stackalloc byte[32];

                if (token.IntegerKind == IntegerKind.Unsigned)
                {
                    if (Utf8Formatter.TryFormat(GetUnsignedIntegerValue(tokenId), temp, out var bytes))
                    {
                        return temp.Slice(0, bytes).ToArray();
                    }
                }
                else
                {
                    if (Utf8Formatter.TryFormat(GetSignedIntegerValue(tokenId), temp, out var bytes))
                    {
                        return temp.Slice(0, bytes).ToArray();
                    }
                }
            }
                break;
            case DTokenKind.Float:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(string));
            {
                Span<byte> temp = stackalloc byte[64];

                if (token.FloatKind == FloatKind.Decimal)
                {
                    if (Utf8Formatter.TryFormat(GetDecimalValue(tokenId), temp, out var bytes))
                    {
                        return temp.Slice(0, bytes).ToArray();
                    }
                }
                else
                {
                    if (Utf8Formatter.TryFormat(GetFloatingValue(tokenId), temp, out var bytes))
                    {
                        return temp.Slice(0, bytes).ToArray();
                    }
                }
            }
                break;
            case DTokenKind.Boolean:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(string));
                if (GetBooleanValue(tokenId))
                {
                    return Utf8Helper.TrueLiteral;
                }

                return Utf8Helper.FalseLiteral;
            case DTokenKind.Timestamp:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(string));
            {
                Span<byte> temp = stackalloc byte[64];
                if (token.TimestampKind == TimestampKind.OffsetDateTime)
                {
                    if (Utf8Formatter.TryFormat(
                            token.IsExtended ? GetExtendDateTimeOffset(token) : DecodeDateTimeOffset(token),
                            temp,
                            out var bytes))
                    {
                        return temp.Slice(0, bytes).ToArray();
                    }
                }
                else
                {
                    if (Utf8Formatter.TryFormat(
                            token.IsExtended ? GetExtendDateTime(token) : DecodeDateTime(token), temp,
                            out var bytes))
                    {
                        return temp.Slice(0, bytes).ToArray();
                    }
                }
            }
                break;
            case DTokenKind.BigNumber:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(string));
                return GetBigNumberValue(tokenId);
            case DTokenKind.ByteString:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(string));
                if (token.Variant == DTokenVariant.ByteStringRaw)
                {
                    return GetByteStringValue(tokenId);
                }

                return Utf8Helper.EncodeByteString(GetByteStringValue(tokenId), token.ByteStringKind);
            case DTokenKind.Control:
                return default;
            case DTokenKind.Null:
                return default;
        }

        throw new InvalidOperationException(
            $"Cannot convert value {new DElement(this, tokenId)} to {typeof(string)}");
    }

    private void ThrowIfRelaxedScalarConversionDisabled(uint tokenId, Type targetType, bool formatException = false)
    {
        if (Settings.AllowRelaxedScalarConversion)
        {
            return;
        }

        var message = $"Cannot convert token {_tokens[tokenId].Kind} to {targetType}";

        if (formatException)
        {
            throw new FormatException(message);
        }

        throw new InvalidOperationException(message);
    }

    internal ReadOnlySpan<byte> GetTriviaValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        if (token.Kind != DTokenKind.Trivia)
        {
            throw new InvalidOperationException();
        }

        if (token.IsExtended)
        {
            return GetExtendTrivia(token);
        }

        return DecodeTrivia(token);
    }

    internal char GetCharValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    var str = token.IsExtended ? GetExtendUtf8Bytes(token) : DecodeUtf8Bytes(token);

                    if (Encoding.UTF8.GetCharCount(str) == 1)
                    {
                        Span<char> ch = stackalloc char[1];
                        Encoding.UTF8.GetChars(str, ch);
                        return ch[0];
                    }
                }
                break;
            case DTokenKind.Integer:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(char));
            {
                var c = GetUnsignedIntegerValue(tokenId);

                if (c <= char.MaxValue)
                {
                    return (char)c;
                }
            }
                break;
        }

        throw new InvalidOperationException($"Cannot convert value {new DElement(this, tokenId)} to {typeof(char)}");
    }

    internal bool GetBooleanValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Boolean:
                return token.Variant == DTokenVariant.BooleanTrue;
            case DTokenKind.String:
            case DTokenKind.Symbol:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(bool));
                if (token.IsExtended)
                {
                    if (bool.TryParse(GetStringValue(tokenId).AsSpan().Trim(), out var bvalue))
                    {
                        return bvalue;
                    }
                }
                else
                {
                    var source = GetUtf8BytesValue(tokenId).Trim(" \t\r\n"u8);

                    if (Utf8Parser.TryParse(source, out bool bvalue, out var bytesConsumed) &&
                        source.Length == bytesConsumed)
                    {
                        return bvalue;
                    }
                }

                break;
            case DTokenKind.Integer:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(bool));
                if (token.IntegerKind == IntegerKind.Unsigned)
                {
                    if (TryGetUnsignedIntegerExact(tokenId, out var uvalue))
                    {
                        return uvalue != 0;
                    }
                }
                else
                {
                    if (TryGetSignedIntegerExact(tokenId, out var ivalue))
                    {
                        return ivalue != 0;
                    }
                }

                break;
            case DTokenKind.Float:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(bool));
                if (TryGetFloatingExact(tokenId, out var fvalue))
                {
                    return fvalue != 0;
                }

                break;
            case DTokenKind.Control:
                return false;
        }

        throw new InvalidOperationException($"Cannot convert value {new DElement(this, tokenId)} to {typeof(bool)}");
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetBooleanExact(uint tokenId, out bool value)
    {
        ref var token = ref _tokens[tokenId];

        var exkind = token.Variant;

        if (exkind.ToKind() == DTokenKind.Boolean)
        {
            value = exkind == DTokenVariant.BooleanTrue;
            return true;
        }

        value = false;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetSignedIntegerExact(uint tokenId, out long value)
    {
        var token = _tokens[tokenId];

        if (token.Kind == DTokenKind.Integer)
        {
            value = token.IsExtended ? GetExtendInteger(token) : DecodeInteger(token);

            if (token.Variant != DTokenVariant.IntegerUnsigned || (ulong)value <= long.MaxValue)
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetUnsignedIntegerExact(uint tokenId, out ulong value)
    {
        var token = _tokens[tokenId];

        if (token.Kind == DTokenKind.Integer)
        {
            value = (ulong)(token.IsExtended ? GetExtendInteger(token) : DecodeInteger(token));

            if (token.Variant == DTokenVariant.IntegerUnsigned || (long)value >= 0)
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    internal ulong GetUnsignedIntegerValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Integer:
                {
                    var ivalue = token.IsExtended ? GetExtendInteger(token) : DecodeInteger(token);

                    if (token.Variant == DTokenVariant.IntegerUnsigned)
                    {
                        return (ulong)ivalue;
                    }

                    if (ivalue >= 0)
                    {
                        return (ulong)ivalue;
                    }
                }
                break;
            case DTokenKind.String:
            case DTokenKind.Symbol:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(ulong));
                if (token.IsExtended)
                {
                    if (ulong.TryParse(GetStringValue(tokenId).AsSpan().Trim(), out var value))
                    {
                        return value;
                    }
                }
                else
                {
                    var source = GetUtf8BytesValue(tokenId).Trim(" \t\r\n"u8);

                    if (Utf8Parser.TryParse(source, out ulong value, out var bytes) && source.Length == bytes)
                    {
                        return value;
                    }
                }

                break;
            case DTokenKind.Float:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(ulong), true);
                if (token.Variant == DTokenVariant.FloatDecimal)
                {
                    var dvalue = decimal.Round(GetDecimalValue(tokenId));

                    if (dvalue < ulong.MinValue || dvalue > ulong.MaxValue)
                    {
                        break;
                    }

                    return (ulong)dvalue;
                }

                if (TryGetFloatingExact(tokenId, out var fvalue))
                {
                    fvalue = double.Round(fvalue);

                    if (fvalue < 0 || fvalue >= 9223372036854775808.0)
                    {
                        break;
                    }

                    return (ulong)fvalue;
                }

                break;
            case DTokenKind.Boolean:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(ulong));
                if (TryGetBooleanExact(tokenId, out var bvalue))
                {
                    return bvalue ? 1UL : 0UL;
                }

                break;
            case DTokenKind.BigNumber:
                {
                    if (!TryGetBigNumberExact(tokenId, out var nvalue))
                    {
                        break;
                    }

                    if (Utf8Parser.TryParse(nvalue, out ulong value, out var bytes) && nvalue.Length == bytes)
                    {
                        return value;
                    }

                    switch (token.Variant)
                    {
                        case DTokenVariant.BigNumberInt128:
                            if (Int128.TryParse(nvalue, out var int128))
                            {
                                return (ulong)int128;
                            }

                            break;
                        case DTokenVariant.BigNumberUInt128:
                            if (UInt128.TryParse(nvalue, out var uint128))
                            {
                                return (ulong)uint128;
                            }

                            break;
                    }
                }
                break;
            case DTokenKind.Control:
                return 0;
        }

        throw new InvalidOperationException($"Cannot convert value {new DElement(this, tokenId)} to {typeof(ulong)}");
    }

    internal long GetSignedIntegerValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Integer:
                {
                    var ivalue = token.IsExtended ? GetExtendInteger(token) : DecodeInteger(token);

                    if (token.Variant == DTokenVariant.IntegerUnsigned)
                    {
                        if ((ulong)ivalue <= long.MaxValue)
                        {
                            return ivalue;
                        }
                    }
                    else
                    {
                        return ivalue;
                    }
                }
                break;
            case DTokenKind.String:
            case DTokenKind.Symbol:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(long));
                if (token.IsExtended)
                {
                    if (long.TryParse(GetStringValue(tokenId).AsSpan().Trim(), out var value))
                    {
                        return value;
                    }
                }
                else
                {
                    var source = GetUtf8BytesValue(tokenId).Trim(" \t\r\n"u8);

                    if (Utf8Parser.TryParse(source, out long value, out var bytes) && source.Length == bytes)
                    {
                        return value;
                    }
                }

                break;
            case DTokenKind.Float:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(long), true);
                if (token.Variant == DTokenVariant.FloatDecimal)
                {
                    var dvalue = decimal.Round(GetDecimalValue(tokenId));

                    if (dvalue < long.MinValue || dvalue > long.MaxValue)
                    {
                        break;
                    }

                    return dvalue < 0 ? (long)dvalue : (long)(ulong)dvalue;
                }

                if (TryGetFloatingExact(tokenId, out var fvalue))
                {
                    fvalue = double.Round(fvalue);

                    if (fvalue < -9223372036854775808.0 || fvalue >= 9223372036854775808.0)
                    {
                        break;
                    }

                    return (long)fvalue;
                }

                break;
            case DTokenKind.Boolean:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(long));
                if (TryGetBooleanExact(tokenId, out var bvalue))
                {
                    return bvalue ? 1 : 0;
                }

                break;
            case DTokenKind.BigNumber:
                {
                    if (!TryGetBigNumberExact(tokenId, out var nvalue))
                    {
                        break;
                    }

                    if (Utf8Parser.TryParse(nvalue, out long value, out var bytes) && nvalue.Length == bytes)
                    {
                        return value;
                    }

                    switch (token.Variant)
                    {
                        case DTokenVariant.BigNumberInt128:
                            if (Int128.TryParse(nvalue, out var int128))
                            {
                                return (long)int128;
                            }

                            break;
                        case DTokenVariant.BigNumberUInt128:
                            if (UInt128.TryParse(nvalue, out var uint128))
                            {
                                return (long)uint128;
                            }

                            break;
                    }
                }
                break;
            case DTokenKind.Control:
                return 0;
        }

        throw new InvalidOperationException($"Cannot convert value {new DElement(this, tokenId)} to {typeof(long)}");
    }

    internal DateTime GetDateTimeValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Timestamp:
                if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                {
                    return token.IsExtended
                        ? GetExtendDateTimeOffset(token).DateTime
                        : DecodeDateTimeOffset(token).DateTime;
                }

                return token.IsExtended ? GetExtendDateTime(token) : DecodeDateTime(token);
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    if (!Settings.AllowRelaxedScalarConversion)
                    {
                        if (TryGetDateTimeExact(tokenId, out var exactValue))
                        {
                            return exactValue;
                        }

                        ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(DateTime), true);
                    }

                    var source = GetUtf8BytesValue(tokenId);

                    if (Utf8Helper.TryParseTimestamp(source, out var dateTime, out var offset,
                            Settings.DateFormatString,
                            false))
                    {
                        if (offset.HasValue)
                        {
                            dateTime = new DateTime(dateTime.Ticks, DateTimeKind.Local);
                        }

                        return dateTime;
                    }
                }
                break;
            case DTokenKind.Control:
                return default;
        }

        throw new InvalidOperationException(
            $"Cannot convert value {new DElement(this, tokenId)} to {typeof(DateTime)}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetDateTimeExact(uint tokenId, out DateTime value)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    {
                        var source = token.IsExtended ? GetExtendUtf8Bytes(token) : DecodeUtf8Bytes(token);

                        if (Utf8Helper.TryParseDateTimeOffsetIso8601(source, out var dateTime, out var offset))
                        {
                            if (offset == null)
                            {
                                value = dateTime;
                            }
                            else
                            {
                                if (dateTime.Ticks < offset.Value.Ticks)
                                {
                                    value = default;
                                }
                                else
                                {
                                    value =
                                        new DateTime(dateTime.Ticks - offset.Value.Ticks, DateTimeKind.Utc).ToLocalTime();
                                }
                            }

                            return true;
                        }
                    }
                }
                break;
            case DTokenKind.Timestamp:
                if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                {
                    value = token.IsExtended
                        ? GetExtendDateTimeOffset(token).DateTime
                        : DecodeDateTimeOffset(token).DateTime;
                    return true;
                }

                value = token.IsExtended ? GetExtendDateTime(token) : DecodeDateTime(token);
                return true;
        }

        value = default;
        return false;
    }

    internal Guid GetGuidValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    if (!Settings.AllowRelaxedScalarConversion)
                    {
                        if (TryGetGuidExact(tokenId, out var exactValue))
                        {
                            return exactValue;
                        }

                        ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(Guid), true);
                    }

                    var source = token.IsExtended ? GetExtendUtf8Bytes(token) : DecodeUtf8Bytes(token);

                    if (Utf8Parser.TryParse(source, out Guid value, out var bytesConsumed) &&
                        bytesConsumed == source.Length)
                    {
                        return value;
                    }

                    source = source.Trim(" \r\n\t"u8);

                    if (source.Length < 128)
                    {
                        Span<char> chars = stackalloc char[source.Length];

                        var charCount = Encoding.UTF8.GetChars(source, chars);

                        if (Guid.TryParse(chars.Slice(0, charCount), out value))
                        {
                            return value;
                        }
                    }
                }

                break;
            case DTokenKind.ByteString:
                {
                    ReadOnlySpan<byte> source;

                    if (token.IsExtended)
                    {
                        source = GetExtendBytes(token);
                    }
                    else
                    {
                        source = DecodeByteString(token);
                    }

                    if (source.Length != 16)
                    {
                        if (Utf8Parser.TryParse(source, out Guid value, out var bytesConsumed) &&
                            bytesConsumed == source.Length)
                        {
                            return value;
                        }

                        break;
                    }

                    return new Guid(source, true);
                }
        }

        throw new InvalidOperationException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetGuidExact(uint tokenId, out Guid value)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    {
                        var source = token.IsExtended ? GetExtendUtf8Bytes(token) : DecodeUtf8Bytes(token);

                        if (Utf8Parser.TryParse(source, out value, out var bytesConsumed) && bytesConsumed == source.Length)
                        {
                            return true;
                        }
                    }
                }
                break;
            case DTokenKind.ByteString:
                {
                    ReadOnlySpan<byte> source;

                    if (token.IsExtended)
                    {
                        source = GetExtendBytes(token);
                    }
                    else
                    {
                        source = DecodeByteString(token);
                    }

                    if (source.Length != 16)
                    {
                        break;
                    }

                    value = new Guid(source, true);
                    return true;
                }
        }

        value = Guid.Empty;
        return false;
    }

    internal bool TryGetDateTimeOffsetExact(uint tokenId, out DateTimeOffset value)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    {
                        var source = token.IsExtended ? GetExtendUtf8Bytes(token) : DecodeUtf8Bytes(token);

                        if (Utf8Helper.TryParseDateTimeOffsetIso8601(source, out var dateTime, out var offset))
                        {
                            if (offset != null)
                            {
                                value = new DateTimeOffset(dateTime, offset.Value);
                            }
                            else
                            {
                                value = dateTime;
                            }

                            return true;
                        }
                    }
                }
                break;
            case DTokenKind.Timestamp:
                if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                {
                    value = token.IsExtended
                        ? GetExtendDateTimeOffset(token)
                        : DecodeDateTimeOffset(token);
                    return true;
                }

                value = token.IsExtended ? GetExtendDateTime(token) : DecodeDateTime(token);
                return true;
        }

        value = default;
        return false;
    }

    internal DateTimeOffset GetDateTimeOffsetValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Timestamp:
                if (token.Variant == DTokenVariant.TimestampOffsetDateTime)
                {
                    return token.IsExtended ? GetExtendDateTimeOffset(token) : DecodeDateTimeOffset(token);
                }

                return token.IsExtended ? GetExtendDateTime(token) : DecodeDateTime(token);
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    if (!Settings.AllowRelaxedScalarConversion)
                    {
                        if (TryGetDateTimeOffsetExact(tokenId, out var exactValue))
                        {
                            return exactValue;
                        }

                        ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(DateTimeOffset),
                            true);
                    }

                    var source = GetUtf8BytesValue(tokenId);

                    if (Utf8Helper.TryParseTimestamp(source, out var result, out var offset, Settings.DateFormatString,
                            true))
                    {
                        const long maxOffsetTicks = 504_000_000_000; // 14:00

                        if (offset.HasValue && offset.Value.Ticks <= maxOffsetTicks &&
                            offset.Value.Ticks >= -maxOffsetTicks)
                        {
                            return new DateTimeOffset(
                                new DateTime(result.ToUniversalTime().Ticks + offset.Value.Ticks,
                                    DateTimeKind.Unspecified),
                                offset.Value);
                        }

                        return result;
                    }
                }
                break;
            case DTokenKind.Control:
                return default;
        }

        throw new InvalidOperationException(
            $"Cannot convert value {new DElement(this, tokenId)} to {typeof(DateTimeOffset)}");
    }

    internal bool TryGetDecimalExact(uint tokenId, out decimal value)
    {
        var token = GetToken(tokenId);

        switch (token.Kind)
        {
            case DTokenKind.Float:
                if (token.IsExtended)
                {
                    if (token.Variant == DTokenVariant.FloatDecimal)
                    {
                        value = GetExtendDecimal(token);
                        return true;
                    }

                    if (TryConvertToDecimal(GetExtendDouble(token), out value))
                    {
                        return true;
                    }

                    break;
                }

                if (TryDecodeDecimal(token, out value))
                {
                    return true;
                }

                break;
            case DTokenKind.Integer:
                if (token.Variant == DTokenVariant.IntegerUnsigned)
                {
                    if (TryGetUnsignedIntegerExact(tokenId, out var ulvalue))
                    {
                        value = ulvalue;
                        return true;
                    }
                }
                else
                {
                    if (TryGetSignedIntegerExact(tokenId, out var lvalue))
                    {
                        value = lvalue;
                        return true;
                    }
                }

                break;
            case DTokenKind.BigNumber:
                {
                    var source = GetBigNumberValue(tokenId);

                    if (Utf8Parser.TryParse(source, out value, out var bytesConsumed) && source.Length == bytesConsumed)
                    {
                        return true;
                    }
                }
                break;
        }

        value = 0;
        return false;
    }


    internal decimal GetDecimalValue(uint tokenId)
    {
        var token = GetToken(tokenId);

        switch (token.Kind)
        {
            case DTokenKind.Float:
                if (token.IsExtended)
                {
                    if (token.Variant == DTokenVariant.FloatDecimal)
                    {
                        return GetExtendDecimal(token);
                    }

                    if (TryConvertToDecimal(GetExtendDouble(token), out var evalue))
                    {
                        return evalue;
                    }

                    break;
                }

            {
                if (TryDecodeDecimal(token, out var dvalue))
                {
                    return dvalue;
                }
            }
                break;
            case DTokenKind.Integer:
                if (token.Variant == DTokenVariant.IntegerUnsigned)
                {
                    if (TryGetUnsignedIntegerExact(tokenId, out var uvalue))
                    {
                        return uvalue;
                    }
                }
                else
                {
                    if (TryGetSignedIntegerExact(tokenId, out var lvalue))
                    {
                        return lvalue;
                    }
                }

                break;
            case DTokenKind.String:
            case DTokenKind.Symbol:
            case DTokenKind.ByteString:
                {
                    ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(decimal));

                    if (token.IsExtended)
                    {
                        if (decimal.TryParse(GetStringValue(tokenId).AsSpan().Trim(), out var dvalue))
                        {
                            return dvalue;
                        }
                    }
                    else
                    {
                        var source = GetUtf8BytesValue(tokenId).Trim(" \t\r\n"u8);

                        if (Utf8Parser.TryParse(source, out decimal dvalue, out var bytesConsumed) &&
                            source.Length == bytesConsumed)
                        {
                            return dvalue;
                        }
                    }
                }
                break;
            case DTokenKind.BigNumber:
                {
                    var source = GetBigNumberValue(tokenId);

                    if (Utf8Parser.TryParse(source, out decimal dvalue, out var bytesConsumed) &&
                        source.Length == bytesConsumed)
                    {
                        return dvalue;
                    }
                }
                break;
            case DTokenKind.Boolean:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(decimal));
                if (TryGetBooleanExact(tokenId, out var bvalue))
                {
                    return bvalue ? 1m : 0m;
                }

                break;
            case DTokenKind.Control:
                return 0;
        }

        throw new InvalidOperationException(
            $"Cannot convert value {new DElement(this, tokenId)} to {typeof(decimal)}");
    }

    internal double GetFloatingValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.InlineFloat:
                return DToken.DecodeInlineFloatPayload(token);
            case DTokenKind.Float:
                if (token.Variant == DTokenVariant.FloatDecimal)
                {
                    return (double)(token.IsExtended ? GetExtendDecimal(token) : DecodeDecimal(token));
                }

                return token.IsExtended ? GetExtendDouble(token) : DecodeFloat(token);
            case DTokenKind.Integer:
                if (token.Variant == DTokenVariant.IntegerUnsigned)
                {
                    if (TryGetUnsignedIntegerExact(tokenId, out var uvalue))
                    {
                        return uvalue;
                    }
                }
                else
                {
                    if (TryGetSignedIntegerExact(tokenId, out var lvalue))
                    {
                        return lvalue;
                    }
                }

                break;
            case DTokenKind.String:
            case DTokenKind.Symbol:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(double));
                if (token.IsExtended)
                {
                    if (double.TryParse(GetStringValue(tokenId).AsSpan().Trim(), out var result))
                    {
                        return result;
                    }
                }
                else
                {
                    var source = GetUtf8BytesValue(tokenId).Trim(" \t\r\n"u8);

                    if (Utf8Parser.TryParse(source, out double result, out var bytesConsumed) &&
                        source.Length == bytesConsumed)
                    {
                        return result;
                    }
                }

                break;
            case DTokenKind.Boolean:
                ThrowIfRelaxedScalarConversionDisabled(tokenId, typeof(double));
                if (TryGetBooleanExact(tokenId, out var bvalue))
                {
                    return bvalue ? 1.0 : 0.0;
                }

                break;
            case DTokenKind.BigNumber:
                {
                    var source = GetBigNumberValue(tokenId);

                    if (Utf8Parser.TryParse(source, out double result, out var bytesConsumed) &&
                        source.Length == bytesConsumed)
                    {
                        return result;
                    }
                }
                break;
            case DTokenKind.Control:
                return 0;
        }

        throw new InvalidOperationException(
            $"Cannot convert value {new DElement(this, tokenId)} to {typeof(double)}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetFloatingExact(uint tokenId, out double value)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.InlineFloat:
                value = DToken.DecodeInlineFloatPayload(token);
                return true;
            case DTokenKind.Float:
                if (token.Variant == DTokenVariant.FloatDecimal)
                {
                    value = (double)(token.IsExtended ? GetExtendDecimal(token) : DecodeDecimal(token));
                    return true;
                }

                value = token.IsExtended ? GetExtendDouble(token) : DecodeFloat(token);
                return true;
            case DTokenKind.Integer:
                if (token.Variant == DTokenVariant.IntegerUnsigned)
                {
                    if (TryGetUnsignedIntegerExact(tokenId, out var lvalue))
                    {
                        value = lvalue;
                        return true;
                    }
                }
                else
                {
                    if (TryGetSignedIntegerExact(tokenId, out var lvalue))
                    {
                        value = lvalue;
                        return true;
                    }
                }

                break;
            case DTokenKind.BigNumber:
                {
                    if (TryGetBigNumberExact(tokenId, out var nvalue))
                    {
                        if (Utf8Parser.TryParse(nvalue, out value, out var bytesConsumed) && nvalue.Length == bytesConsumed)
                        {
                            return true;
                        }
                    }
                }
                break;
        }

        value = 0;
        return false;
    }

    internal bool TryGetByteStringExact(uint tokenId, out ReadOnlySpan<byte> value, ByteStringKind byteStringKind)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.String:
            case DTokenKind.Symbol:
                {
                    var utf8Bytes = GetUtf8BytesValue(tokenId);

                    switch (byteStringKind)
                    {
                        case ByteStringKind.Default:
                        case ByteStringKind.Base64:
                            {
                                if (Utf8Helper.TryParseBase64(utf8Bytes, out value, out var bytesConsumed))
                                {
                                    return bytesConsumed == utf8Bytes.Length;
                                }
                            }
                            break;
                        case ByteStringKind.Base64Url:
                            {
                                if (Utf8Helper.TryParseBase64Url(utf8Bytes, out value, out var bytesConsumed))
                                {
                                    return bytesConsumed == utf8Bytes.Length;
                                }
                            }
                            break;
                        case ByteStringKind.Base16:
                            {
                                if (Utf8Helper.TryParseBase16(utf8Bytes, out value, out var bytesConsumed))
                                {
                                    return bytesConsumed == utf8Bytes.Length;
                                }
                            }
                            break;
                        case ByteStringKind.Guid:
                            {
                                if (Utf8Parser.TryParse(utf8Bytes, out Guid guid, out var bytesConsumed) &&
                                    utf8Bytes.Length == bytesConsumed)
                                {
                                    var guidBytes = new byte[16];
                                    guid.TryWriteBytes(guidBytes, true, out var written);
                                    value = guidBytes;
                                    return true;
                                }
                            }
                            break;
                    }
                }
                break;
            case DTokenKind.ByteString:
                value = token.IsExtended ? GetExtendBytes(token) : DecodeByteString(token);
                return true;
        }

        value = default;
        return false;
    }

    internal ReadOnlySpan<byte> GetByteStringValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.ByteString:
                return token.IsExtended ? GetExtendBytes(token) : DecodeByteString(token);
            case DTokenKind.BigNumber:
                return GetBigNumberValue(tokenId);
            case DTokenKind.String:
            case DTokenKind.Symbol:
                return Convert.FromBase64String(GetStringValue(tokenId));
        }

        throw new InvalidOperationException($"Cannot convert value {new DElement(this, tokenId)} to Binary");
    }

    internal ReadOnlySpan<byte> GetBigNumberValue(uint tokenId)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Integer:
                {
                    Span<byte> buffer = stackalloc byte[32];
                    var written = token.IntegerKind == IntegerKind.Unsigned
                        ? Utf8Helper.EncodeNumber(buffer, GetUnsignedIntegerValue(tokenId))
                        : Utf8Helper.EncodeNumber(buffer, GetSignedIntegerValue(tokenId));
                    return buffer.Slice(0, written).ToArray();
                }
            case DTokenKind.Float:
            case DTokenKind.InlineFloat:
                {
                    Span<byte> buffer = stackalloc byte[64];
                    var written = token.Kind == DTokenKind.Float && token.FloatKind == FloatKind.Decimal
                        ? Utf8Helper.EncodeNumber(buffer, GetDecimalValue(tokenId),
                            FloatFormatHandling.SpecialFloatAsSymbol)
                        : Utf8Helper.EncodeNumber(buffer, GetFloatingValue(tokenId),
                            FloatFormatHandling.SpecialFloatAsSymbol);
                    return buffer.Slice(0, written).ToArray();
                }
            case DTokenKind.String:
            case DTokenKind.Symbol:
                return GetUtf8BytesValue(tokenId);
            case DTokenKind.BigNumber:
                return token.IsExtended ? GetExtendBytes(token) : DecodeBigNumber(token);
            case DTokenKind.Control:
                return default;
        }

        throw new InvalidOperationException($"Cannot convert value {new DElement(this, tokenId)} to BigNumber");
    }

    internal bool TryGetBigNumberExact(uint tokenId, out ReadOnlySpan<byte> value)
    {
        ref var token = ref _tokens[tokenId];

        switch (token.Kind)
        {
            case DTokenKind.Integer:
            case DTokenKind.Float:
            case DTokenKind.InlineFloat:
                value = GetBigNumberValue(tokenId);
                return true;
            case DTokenKind.BigNumber:
                value = token.IsExtended ? GetExtendBytes(token) : DecodeBigNumber(token);
                return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal DContainer GetExtendContainer(DToken token)
    {
        Debug.Assert(token.IsExtended && !token.IsExtendInlineLiteral && token.IsContainer);
        Debug.Assert(_extends[token.ExtendId] != null);
        return (DContainer)_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetExtendInteger(DToken token)
    {
        if (token.IsInlinePayload)
        {
            return DToken.DecodeInlineIntegerPayload(token);
        }

        Debug.Assert(token.Kind == DTokenKind.Integer);
        return (long)_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetExtendDouble(DToken token)
    {
        if (token.IsInlinePayload)
        {
            return DToken.DecodeInlineSinglePayload(token);
        }

        Debug.Assert(token.Kind == DTokenKind.Float && token.Variant != DTokenVariant.FloatDecimal);
        return (double)_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private decimal GetExtendDecimal(DToken token)
    {
        Debug.Assert(token.Variant == DTokenVariant.FloatDecimal);
        return (decimal)_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] GetExtendTrivia(DToken token)
    {
        Debug.Assert(token.Kind == DTokenKind.Trivia);

        return (byte[])_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetExtendString(DToken token)
    {
        Debug.Assert(token.Type == DTokenType.Text);

        var value = _extends[token.ExtendId]!;

        if (token.Kind == DTokenKind.Symbol && value is Utf8Symbol utf8Symbol)
        {
            return (string)utf8Symbol;
        }

        return (string)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] GetExtendBytes(DToken token)
    {
        Debug.Assert(token.Kind == DTokenKind.ByteString || token.Kind == DTokenKind.BigNumber);
        return (byte[])_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetExtendUtf8Bytes(DToken token)
    {
        Debug.Assert(token.Type == DTokenType.Text);

        var value = _extends[token.ExtendId]!;

        if (token.Kind == DTokenKind.Symbol && value is Utf8Symbol utf8Symbol)
        {
            return utf8Symbol;
        }

        return Utf8Helper.GetUtf8String((string)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DateTime GetExtendDateTime(DToken token)
    {
        Debug.Assert(token.Kind == DTokenKind.Timestamp && token.Variant != DTokenVariant.TimestampOffsetDateTime);
        return (DateTime)_extends[token.ExtendId]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DateTimeOffset GetExtendDateTimeOffset(DToken token)
    {
        Debug.Assert(token.Variant == DTokenVariant.TimestampOffsetDateTime);
        return (DateTimeOffset)_extends[token.ExtendId]!;
    }

    private uint CopyToTokens(uint rootId, DElement element)
    {
        if (!element.IsValid)
        {
            throw new ObjectDisposedException(element.Document.ToString());
        }

        using (var cache = Helper.InstanceCache<TokenWriter>.Get(() => new TokenWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);
            writer.Reset(this, rootId, Settings);

            WriteToken(writer, reader, reader.RootId);

            return writer.RootId;
        }
    }

    protected uint SerializeToTokens<T>(uint rootId, T value, SerializerSettings settings)
    {
        using (var cache = Helper.InstanceCache<TokenWriter>.Get(() => new TokenWriter()))
        {
            var writer = cache.Value;
            writer.Reset(this, rootId, settings);
            writer.WriteValue(value);

            return writer.RootId;
        }
    }

    protected uint SerializeToTokens(uint rootId, object value, Type inputType, SerializerSettings settings)
    {
        using (var cache = Helper.InstanceCache<TokenWriter>.Get(() => new TokenWriter()))
        {
            var writer = cache.Value;
            writer.Reset(this, rootId, settings);

            var converter = settings.GetConverter(inputType);
            converter.WriteObject(writer, inputType, value);

            return writer.RootId;
        }
    }

    private void WriteToken(DataWriter writer, in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Map)
            {
                writer.WriteStartMap(reader.GetValueCount(tokenId));
                foreach (var kv in reader.EnumerateMap(tokenId))
                {
                    WriteToken(writer, reader, kv.Key);
                    WriteToken(writer, reader, kv.Value);
                }

                writer.WriteEndMap();
            }
            else
            {
                writer.WriteStartArray(reader.GetValueCount(tokenId));
                foreach (var v in reader.EnumerateArray(tokenId))
                {
                    WriteToken(writer, reader, v);
                }

                writer.WriteEndArray();
            }
        }
        else
        {
            switch (token.Kind)
            {
                case DTokenKind.Boolean:
                    writer.WriteBoolean(reader.ReadBoolean(tokenId));
                    break;
                case DTokenKind.Integer:
                    if (token.IntegerKind == IntegerKind.Unsigned)
                    {
                        writer.WriteUInt64(reader.ReadUInt64(tokenId));
                    }
                    else
                    {
                        writer.WriteInt64(reader.ReadInt64(tokenId));
                    }

                    break;
                case DTokenKind.Float:
                    switch (token.FloatKind)
                    {
                        case FloatKind.Decimal:
                            writer.WriteDecimal(reader.ReadDecimal(tokenId));
                            break;
                        case FloatKind.Single:
                            writer.WriteSingle(reader.ReadSingle(tokenId));
                            break;
                        case FloatKind.Half:
                            writer.WriteHalf(reader.ReadHalf(tokenId));
                            break;
                        default:
                            writer.WriteDouble(reader.ReadDouble(tokenId));
                            break;
                    }

                    break;
                case DTokenKind.String:
                    if (token.IsExtended)
                    {
                        writer.WriteString(reader.ReadString(tokenId));
                    }
                    else
                    {
                        writer.WriteString(reader.ReadUtf8String(tokenId));
                    }

                    break;
                case DTokenKind.Symbol:
                    writer.WriteSymbol(reader.ReadString(tokenId), token.SymbolKind);
                    break;
                case DTokenKind.Null:
                    writer.WriteNull();
                    break;
                case DTokenKind.Timestamp:
                    if (token.TimestampKind == TimestampKind.OffsetDateTime)
                    {
                        writer.WriteDateTimeOffset(reader.ReadDateTimeOffset(tokenId));
                    }
                    else
                    {
                        writer.WriteDateTime(reader.ReadDateTime(tokenId));
                    }

                    break;
                case DTokenKind.BigNumber:
                    writer.WriteBigNumber(reader.ReadBigNumber(tokenId), token.BigNumberKind);
                    break;
                case DTokenKind.ByteString:
                    writer.WriteByteString(reader.ReadByteString(tokenId), token.ByteStringKind);
                    break;
                case DTokenKind.InlineFloat:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
                default:
                    throw new InvalidOperationException($"Cannot write token of kind {token.Kind} to writer");
            }
        }
    }
}