// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace REDox;

[DebuggerDisplay("{ToString(),nq}")]
public partial struct DToken : IEquatable<DToken>
{
    private const uint MaxTokenId = 0x3fffffff;
    private const uint MaxValueCount = 0x3fffffff;
    private const uint MaxExtendId = 0xfffffff;

    private const long PayloadMask = 0x00FFFFFFFFFFFFFFL;
    private const long InlineFloatMask = 0x07FFFFFFFFFFFFFFL;

    internal DToken(long v)
    {
        _value = v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DToken MakeJump(uint jumpId)
    {
        Debug.Assert(jumpId <= MaxTokenId);
        return new DToken(jumpId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DToken MakeArray(int count)
    {
        Debug.Assert(count >= 0 && count <= MaxValueCount);
        return new DToken(((long)DTokenType.Array << 60) | (uint)count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DToken MakeMap(int count)
    {
        Debug.Assert(count >= 0 && count <= MaxValueCount);
        return new DToken(((long)DTokenType.Map << 60) | (uint)count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DToken Make(DTokenVariant kind, long payload)
    {
        Debug.Assert((payload & ~PayloadMask) == 0, "payload overflow");
        return new DToken(((long)kind << 56) | payload);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DToken Make(DTokenKind kind, long payload)
    {
        Debug.Assert((payload & ~PayloadMask) == 0, "payload overflow");
        return new DToken(((long)kind << 59) | payload);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DToken MakeInlineFloat(long payload)
    {
        Debug.Assert((payload & ~InlineFloatMask) == 0, "payload overflow");
        return new DToken(((long)DTokenKind.InlineFloat << 59) | payload);
    }

    public override string ToString()
    {
        if (Kind == DTokenKind.Control)
        {
            if (IsEmpty)
            {
                if (IsEmptyToken)
                {
                    return $"Empty next: {EmptyId}";
                }

                if (IsEmptyExtend)
                {
                    return $"EmptyExt id: {ExtendId} next: {EmptyId}";
                }
            }
            else
            {
                if (_value == 0)
                {
                    return "Nop";
                }

                return $"Jump link: {JumpId}";
            }
        }

        if (IsExtended)
        {
            if (IsContainer)
            {
                return $"{Type}* id: {ExtendId} link: {LinkId}";
            }

            if (Kind == DTokenKind.InlineFloat)
            {
                return $"{Kind}* value: {DecodeInlineFloatPayload(this)}";
            }

            if (Kind == DTokenKind.Boolean || Kind == DTokenKind.Null)
            {
                if (TriviaId != 0)
                {
                    return $"{Variant}* trivia: {TriviaId}";
                }

                return $"{Variant}*";
            }

            if (IsExtendInlineLiteral)
            {
                if (Kind == DTokenKind.Integer)
                {
                    return $"IntegerInline* value: {DecodeInlineIntegerPayload(this)}";
                }

                if (Kind == DTokenKind.Float)
                {
                    return $"FloatInline* value: {DecodeInlineSinglePayload(this)}";
                }

                return $"{Variant}* payload: {_value & PayloadMask:X}";
            }

            if (TriviaId != 0)
            {
                return $"{Variant}* id: {ExtendId} trivia: {TriviaId}";
            }

            return $"{Variant}* id: {ExtendId}";
        }

        if (IsContainer)
        {
            return $"{Type} count: {Count} link: {LinkId}";
        }

        if (Kind == DTokenKind.InlineFloat)
        {
            return $"{Kind} value: {DecodeInlineFloatPayload(this)}";
        }

        if (Kind == DTokenKind.Boolean || Kind == DTokenKind.Null)
        {
            return $"{Variant}";
        }

        var (length, offset) = DecodeLengthOffsetPayload(this);

        return $"{Variant} payload: {_value & PayloadMask:X} (length: {length} , offset: {offset})";
    }

    public bool IsExtended
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value < 0;
    }

    public bool IsEmpty => IsEmptyExtend || IsEmptyToken;

    public bool IsContainer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((_value >> 61) & 3) == 3;
    }

    public bool IsIgnore
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((_value >> 60) & 7) == 0;
    }

    public bool IsLiteral
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((_value >> 60) & 7) == 1;
    }

    public bool IsNumeric
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((_value >> 61) & 3) == 1;
    }

    public bool IsStringEncoded
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ((_value >> 61) & 3) == 2;
    }

    public DTokenType Type
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (DTokenType)((_value >> 60) & 7);
    }

    public DTokenKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (DTokenKind)((_value >> 59) & 0xf);
    }

    public DTokenVariant Variant
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (DTokenVariant)((_value >> 56) & 0x7f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal set => _value = (long)(((ulong)_value & 0x80ffffffffffffffUL) | ((ulong)value << 56));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetSubKind(DTokenKind kind)
    {
        Debug.Assert(Kind == kind);
        return (int)((_value >> 56) & 7);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetInlineSubKind(DTokenKind kind)
    {
        Debug.Assert(Kind == kind);

        var subKind = (int)((_value >> 56) & 7);

        if (subKind == 7)
        {
            if (IsExtended)
            {
                return (int)((_value >> 53) & 7);
            }

            return 0;
        }

        return subKind;
    }

    public IntegerKind IntegerKind => (IntegerKind)GetInlineSubKind(DTokenKind.Integer);

    public FloatKind FloatKind => (FloatKind)GetInlineSubKind(DTokenKind.Float);

    public StringKind StringKind => (StringKind)GetSubKind(DTokenKind.String);

    public SymbolKind SymbolKind => (SymbolKind)GetSubKind(DTokenKind.Symbol);

    public TriviaKind TriviaKind => (TriviaKind)GetSubKind(DTokenKind.Trivia);

    public TimestampKind TimestampKind => (TimestampKind)GetSubKind(DTokenKind.Timestamp);

    public ByteStringKind ByteStringKind => (ByteStringKind)GetSubKind(DTokenKind.ByteString);

    public BigNumberKind BigNumberKind => (BigNumberKind)GetSubKind(DTokenKind.BigNumber);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(DToken left, DToken right)
    {
        return left._value == right._value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(DToken left, DToken right)
    {
        return left._value != right._value;
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is DToken token)
        {
            return _value == token._value;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(DToken token)
    {
        return _value == token._value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator long(DToken value)
    {
        return value._value;
    }

    private long _value;
}