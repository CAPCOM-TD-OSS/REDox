// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace REDox;

public abstract partial class Document
{
    public struct TriviaTokenEnumerator
    {
        private int _index;
        private uint _latestId;
        private readonly TriviaList? _triviaList;

        public uint Current { get; private set; }

        public int Count { get; }

        public TriviaTokenEnumerator(Document doc, uint tokenId)
        {
            _index = 0;
            Count = 0;
            Current = 0;

            var token = doc.GetToken(tokenId);

            if (token.IsExtended)
            {
                if (token.IsInlinePayload)
                {
                    return;
                }

                var triviaId = token.IsContainer ? doc.GetExtendContainer(token).TriviaId : token.TriviaId;

                if (triviaId > 0)
                {
                    _triviaList = (TriviaList)doc._extends[triviaId]!;
                    Count = _triviaList.Count;
                }
            }
            else
            {
                while (tokenId > 1)
                {
                    tokenId--;
                    token = doc.GetToken(tokenId);
                    if (!token.IsLeadingTrivia)
                    {
                        break;
                    }

                    Count++;
                    _latestId = tokenId;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TriviaTokenEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_index < Count)
            {
                if (_triviaList != null)
                {
                    Current = _triviaList[_index++];
                }
                else
                {
                    Current = _latestId;
                    _index++;
                    _latestId++;
                }

                return true;
            }

            return false;
        }
    }

    public struct ValueEnumerator
    {
        private int _index;
        private uint _latestId;
        private DToken _token;

        internal Document Document { get; }

        public uint Current { get; private set; }

        public ValueEnumerator(Document doc, uint tokenId)
        {
            Document = doc;
            _index = 0;
            Current = 0;

            _token = doc.GetToken(tokenId);

            if (!_token.IsExtended)
            {
                _latestId = tokenId + 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var doc = Document;

            if (_token.IsExtended)
            {
                var node = (DArray)doc.GetExtendContainer(_token);

                while (_index < node.Count)
                {
                    Current = node.GetValueInternal(_index);
                    _index++;
                    return true;
                }
            }
            else
            {
                while (_index < _token.Count)
                {
                    var valueId = _latestId;
                    var token = doc.GetToken(valueId);

                    while (token.IsIgnore)
                    {
                        valueId = token.Kind == DTokenKind.Control && token.JumpId != 0 ? token.JumpId : valueId + 1;
                        token = doc.GetToken(valueId);
                    }

                    Current = valueId;
                    _latestId = token.IsContainer ? token.LinkId : valueId + 1;
                    _index++;

                    return true;
                }
            }

            return false;
        }
    }

    public struct KeyValueEnumerator
    {
        private int _index;
        private uint _latestId;
        private DToken _token;

        internal Document Document { get; }

        public KeyValuePair<uint, uint> Current { get; private set; }

        public KeyValueEnumerator(Document doc, uint tokenId)
        {
            Document = doc;
            _index = 0;
            Current = default;

            _token = doc.GetToken(tokenId);

            if (!_token.IsExtended)
            {
                _latestId = tokenId + 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyValueEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var doc = Document;

            if (_token.IsExtended)
            {
                var node = doc.GetExtendContainer(_token);

                if (node is DMap map)
                {
                    while (_index < node.Count)
                    {
                        Current = map.GetKeyValuePairInternal(_index);
                        _index++;
                        return true;
                    }
                }
                else
                {
                    var obj = (DObject)node;
                    while (_index < node.Count)
                    {
                        Current = obj.GetKeyValuePairInternal(_index);
                        _index++;
                        return true;
                    }
                }
            }
            else
            {
                while (_index < _token.Count)
                {
                    var keyId = _latestId;
                    var token = doc.GetToken(keyId);

                    while (token.IsIgnore)
                    {
                        keyId = token.Kind == DTokenKind.Control && token.JumpId != 0 ? token.JumpId : keyId + 1;
                        token = doc.GetToken(keyId);
                    }

                    var valueId = token.IsContainer ? token.LinkId : keyId + 1;

                    token = doc.GetToken(valueId);
                    while (token.IsIgnore)
                    {
                        valueId = token.Kind == DTokenKind.Control && token.JumpId != 0 ? token.JumpId : valueId + 1;
                        token = doc.GetToken(valueId);
                    }

                    Current = new KeyValuePair<uint, uint>(keyId, valueId);
                    _latestId = token.IsContainer ? token.LinkId : valueId + 1;
                    _index++;

                    return true;
                }
            }

            return false;
        }
    }

    public struct ObjectEnumerator : IEnumerator<DProperty>, IEnumerable<DProperty>
    {
        private KeyValueEnumerator _enumerator;
        private readonly uint _parentId;

        public ObjectEnumerator(Document doc, uint id)
        {
            _enumerator = new KeyValueEnumerator(doc, id);
            _parentId = id;
        }

        public DProperty Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var kv = _enumerator.Current;
                var doc = _enumerator.Document;

                return new DProperty(doc, kv.Key, kv.Value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ObjectEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Dispose()
        {
        }

        object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotSupportedException();
        }

        IEnumerator<DProperty> IEnumerable<DProperty>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public struct MapEnumerator : IEnumerator<KeyValuePair<DElement, DElement>>,
        IEnumerable<KeyValuePair<DElement, DElement>>
    {
        private KeyValueEnumerator _enumerator;

        public MapEnumerator(Document doc, uint id)
        {
            _enumerator = new KeyValueEnumerator(doc, id);
        }

        public KeyValuePair<DElement, DElement> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var kv = _enumerator.Current;
                var doc = _enumerator.Document;

                return new KeyValuePair<DElement, DElement>(
                    new DElement(doc, kv.Key),
                    new DElement(doc, kv.Value));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MapEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Dispose()
        {
        }

        object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotSupportedException();
        }

        IEnumerator<KeyValuePair<DElement, DElement>> IEnumerable<KeyValuePair<DElement, DElement>>.
            GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public struct ArrayEnumerator : IEnumerator<DElement>, IEnumerable<DElement>
    {
        private ValueEnumerator _enumerator;

        public ArrayEnumerator(Document doc, uint id)
        {
            _enumerator = new ValueEnumerator(doc, id);
        }

        public DElement Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var value = _enumerator.Current;
                var doc = _enumerator.Document;

                return new DElement(doc, value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Dispose()
        {
        }

        object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotSupportedException();
        }

        IEnumerator<DElement> IEnumerable<DElement>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }


    public struct TriviaEnumerator : IEnumerator<DTrivia>, IEnumerable<DTrivia>
    {
        private TriviaTokenEnumerator _enumerator;
        private readonly Document _doc;
        private readonly uint _tokenId;
        private readonly int _documentVersion;
        private readonly int _triviaVersion;

        public TriviaEnumerator(Document doc, uint id)
        {
            _enumerator = new TriviaTokenEnumerator(doc, id);
            _doc = doc;
            _tokenId = id;
            _documentVersion = doc.Version;
            _triviaVersion = doc.GetElementTriviaVersion(id);
        }

        public DTrivia Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfInvalid();
                var value = _enumerator.Current;

                return new DTrivia(_doc, value, _tokenId);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TriviaEnumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            ThrowIfInvalid();
            return _enumerator.MoveNext();
        }

        private void ThrowIfInvalid()
        {
            if (_doc.Version != _documentVersion || _doc.GetElementTriviaVersion(_tokenId) != _triviaVersion)
            {
                throw new InvalidOperationException("The trivia collection changed during enumeration.");
            }
        }

        public void Dispose()
        {
        }

        object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotSupportedException();
        }

        IEnumerator<DTrivia> IEnumerable<DTrivia>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}