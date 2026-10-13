// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using REDox.Serialization;

namespace REDox;

public abstract partial class Document
{
    private class TokenWriter : DataWriter
    {
        private readonly Stack<(uint parentId, uint latestId, uint keyId)> _stack;

        private Document _document;
        private uint _keyId;
        private uint _latestId;
        private uint _parentId;

        public TokenWriter()
        {
            _document = null!;
            _stack = new Stack<(uint, uint, uint)>();
        }

        public uint RootId { get; private set; }

        public void Reset(Document doc, uint rootId, SerializerSettings settings)
        {
            Reset(settings);

            _document = doc;
            _parentId = 0;
            _latestId = rootId;
            _keyId = 0;
            RootId = rootId;

            _stack.Clear();
        }

        private void NextToken(uint tokenId)
        {
            if (_latestId != 0)
            {
                if (_document.GetToken(_latestId).IsContainer)
                {
                    _document.LinkToken(_latestId, tokenId);
                }
            }

            _latestId = tokenId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddValueId(uint tokenId)
        {
            if (_parentId == 0)
            {
                RootId = tokenId;
                return;
            }

            ref var token = ref _document._tokens[_parentId];

            if (token.IsExtended)
            {
                if (token.Type == DTokenType.Map)
                {
                    if (_keyId == 0)
                    {
                        _keyId = tokenId;
                    }
                    else
                    {
                        _document.GetElementObject(_parentId).AddKeyValuePairInternal(
                            _keyId, tokenId);
                        _keyId = 0;
                    }
                }
                else
                {
                    _document.GetElementArray(_parentId).AddInternal(tokenId);
                }
            }
            else
            {
                token.Increment();
            }

            NextToken(tokenId);
        }

        public override void WriteStartArray(int? definiteLength)
        {
            var doc = _document;
            var sequenceMode = (doc._emptyExtendId | doc._emptyTokenId) == 0;
            var capacity = definiteLength != null ? definiteLength.Value : 0;

            if (_parentId == 0)
            {
                if (sequenceMode && RootId == 0)
                {
                    RootId = doc.AllocToken(DToken.MakeArray(0));
                }
                else
                {
                    RootId = doc.ExtendContainer(RootId, DTokenType.Array, capacity);
                }

                _parentId = RootId;
                _latestId = 0;
            }
            else
            {
                var tokenId = sequenceMode
                    ? doc.AllocToken(DToken.MakeArray(0))
                    : doc.AddExtendArray(capacity).Id;
                AddValueId(tokenId);

                _stack.Push((_parentId, _latestId, _keyId));

                _parentId = tokenId;
                _latestId = 0;
                _keyId = 0;
            }
        }


        public override void WriteEndArray()
        {
            if (_stack.Count > 0)
            {
                var state = _stack.Pop();

                _parentId = state.parentId;
                _latestId = state.latestId;
                _keyId = state.keyId;
            }
        }

        public override void WriteStartMap(int? definiteLength)
        {
            var doc = _document;
            var sequenceMode = (doc._emptyExtendId | doc._emptyTokenId) == 0;
            var capacity = definiteLength != null ? definiteLength.Value : 0;

            if (_parentId == 0)
            {
                if (sequenceMode && RootId == 0)
                {
                    RootId = doc.AllocToken(DToken.MakeMap(0));
                }
                else
                {
                    RootId = doc.ExtendContainer(RootId, DTokenType.Map, capacity);
                }

                _parentId = RootId;
                _latestId = 0;
            }
            else
            {
                var tokenId = sequenceMode
                    ? doc.AllocToken(DToken.MakeMap(0))
                    : doc.AddExtendObject(capacity).Id;
                AddValueId(tokenId);

                _stack.Push((_parentId, _latestId, _keyId));

                _parentId = tokenId;
                _latestId = 0;
                _keyId = 0;
            }
        }

        public override void WriteEndMap()
        {
            var token = _document.GetToken(_parentId);

            if (!token.IsExtended)
            {
                Debug.Assert(token.Type == DTokenType.Map);
                Debug.Assert((token.Count & 1) == 0);

                token.Count = token.Count >> 1;

                _document.SetToken(_parentId, token);
            }

            if (_stack.Count > 0)
            {
                var state = _stack.Pop();

                _parentId = state.parentId;
                _latestId = state.latestId;
                _keyId = state.keyId;
            }
        }


        public override void WriteBigNumber(ReadOnlySpan<byte> value, BigNumberKind kind = BigNumberKind.Default)
        {
            AddValueId(_document.ExtendToken(_parentId == 0 ? RootId : 0, kind.ToVariant(), value.ToArray()));
        }

        public override void WriteChar(char value)
        {
            AddValueId(_document.ExtendStringToken(_parentId == 0 ? RootId : 0, StringKind.Default, value));
        }

        public override void WriteDecimal(decimal value)
        {
            AddValueId(_document.ExtendToken(_parentId == 0 ? RootId : 0, DTokenVariant.FloatDecimal, value));
        }

        public override void WriteGuid(Guid value)
        {
            AddValueId(_document.ExtendGuidToken(_parentId == 0 ? RootId : 0, value));
        }

        public override void WriteByteString(ReadOnlySpan<byte> value, ByteStringKind kind = ByteStringKind.Default)
        {
            AddValueId(_document.ExtendToken(_parentId == 0 ? RootId : 0, kind.ToVariant(), value.ToArray()));
        }

        public override void WriteDateTime(DateTime value)
        {
            AddValueId(_document.ExtendToken(_parentId == 0 ? RootId : 0, DTokenVariant.Timestamp, value));
        }

        public override void WriteDateTimeOffset(DateTimeOffset value)
        {
            AddValueId(_document.ExtendToken(_parentId == 0 ? RootId : 0, DTokenVariant.TimestampOffsetDateTime,
                value));
        }

        public override void WriteHalf(Half value)
        {
            AddValueId(_document.ExtendLiteralToken(_parentId == 0 ? RootId : 0,
                DToken.MakeExtendInlineSingle(FloatKind.Half, (float)value)));
        }

        public override void WriteSingle(float value)
        {
            AddValueId(_document.ExtendLiteralToken(_parentId == 0 ? RootId : 0,
                DToken.MakeExtendInlineSingle(FloatKind.Single, value)));
        }

        public override void WriteBoolean(bool value)
        {
            AddValueId(_document.ExtendLiteralToken(_parentId == 0 ? RootId : 0,
                DToken.MakeExtendLiteral(value ? DTokenVariant.BooleanTrue : DTokenVariant.BooleanFalse, 0)));
        }

        public override void WriteDouble(double value)
        {
            AddValueId(_document.ExtendFloatToken(_parentId == 0 ? RootId : 0, FloatKind.Default, value));
        }

        public override void WriteNull()
        {
            AddValueId(_document.ExtendLiteralToken(_parentId == 0 ? RootId : 0,
                DToken.MakeExtendLiteral(DTokenVariant.Null, 0)));
        }

        public override void WriteInt32(int value)
        {
            AddValueId(_document.ExtendLiteralToken(_parentId == 0 ? RootId : 0,
                DToken.MakeExtendInlineInteger(IntegerKind.Default, value)));
        }

        public override void WriteUInt32(uint value)
        {
            AddValueId(_document.ExtendLiteralToken(_parentId == 0 ? RootId : 0,
                DToken.MakeExtendInlineInteger(IntegerKind.Default, value)));
        }

        public override void WriteUInt64(ulong value)
        {
            AddValueId(_document.ExtendIntegerToken(_parentId == 0 ? RootId : 0, value));
        }

        public override void WriteInt64(long value)
        {
            AddValueId(_document.ExtendIntegerToken(_parentId == 0 ? RootId : 0, IntegerKind.Default, value));
        }

        public override void WriteString(string value)
        {
            AddValueId(_document.ExtendStringToken(_parentId == 0 ? RootId : 0, StringKind.Default, value));
        }

        public override void WriteString(ReadOnlySpan<char> value)
        {
            AddValueId(_document.ExtendStringToken(_parentId == 0 ? RootId : 0, StringKind.Default, new string(value)));
        }

        public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
        {
            AddValueId(_document.ExtendSymbolToken(_parentId == 0 ? RootId : 0, value, kind));
        }

        public override void WriteSymbol(string value, SymbolKind kind = SymbolKind.Default)
        {
            AddValueId(_document.ExtendSymbolToken(_parentId == 0 ? RootId : 0, value, kind));
        }

        public override void WriteSymbol(ReadOnlySpan<char> value, SymbolKind kind = SymbolKind.Default)
        {
            AddValueId(_document.ExtendSymbolToken(_parentId == 0 ? RootId : 0, new string(value), kind));
        }

        public override void WriteSymbol(ReadOnlySpan<byte> utf8Bytes, SymbolKind kind = SymbolKind.Default)
        {
            AddValueId(_document.ExtendSymbolToken(_parentId == 0 ? RootId : 0, Utf8Helper.GetUtf16String(utf8Bytes),
                kind));
        }

        public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
        {
            AddValueId(_document.ExtendStringToken(_parentId == 0 ? RootId : 0, StringKind.Default,
                Utf8Helper.GetUtf16String(utf8Bytes)));
        }
    }
}