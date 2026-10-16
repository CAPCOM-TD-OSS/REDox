// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace REDox;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
[DebuggerTypeProxy(typeof(DebugView))]
public sealed partial class DMap : DContainer, IList<KeyValuePair<DValue, DValue>>
{
    private KeyValuePair<uint, uint>[] _values;

    public DMap(SerializerSettings? settings = null, int capacity = 0)
    {
        _values = capacity > 0 ? new KeyValuePair<uint, uint>[capacity] : [];
        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;
    }

    public DMap(IEnumerable<KeyValuePair<DValue, DValue>> enumerator,
        SerializerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _values = [];
        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;

        foreach (var v in enumerator)
        {
            Add(v);
        }
    }

    internal DMap(DElement element, int capacity)
    {
        _element = element;
        _values = new KeyValuePair<uint, uint>[capacity];
    }

    internal DMap(DElement element, DMap src)
    {
        _element = element;
        _values = src.KeyValuePairs.ToArray();
        _count = _values.Length;
        TriviaId = src.TriviaId;
    }

    internal DMap(DElement element, DObject src)
    {
        _element = element;
        _values = src.KeyValuePairs.ToArray();
        _count = _values.Length;
        TriviaId = src.TriviaId;
    }

    internal ReadOnlySpan<KeyValuePair<uint, uint>> KeyValuePairs => _values.AsSpan(0, _count);

    public override int Capacity
    {
        get => _values.Length;
        set
        {
            ThrowIfInvalid();

            if (value < _count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Array.Resize(ref _values, value);
        }
    }

    public KeyValuePair<DValue, DValue> this[int index]
    {
        get
        {
            ThrowIfInvalid();

            if ((uint)index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var entry = _values[index];

            return new KeyValuePair<DValue, DValue>(new DValue(Document, entry.Key),
                new DValue(Document, entry.Value));
        }
        set
        {
            ThrowIfInvalid();

            if ((uint)index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var entry = _values[index];

            Document.JoinValue(entry.Key, value.Key);
            Document.JoinValue(entry.Value, value.Value);
        }
    }

    bool ICollection<KeyValuePair<DValue, DValue>>.IsReadOnly => false;

    public IEnumerator<KeyValuePair<DValue, DValue>> GetEnumerator()
    {
        ThrowIfInvalid();

        for (var i = 0; i < _count; i++)
        {
            yield return new KeyValuePair<DValue, DValue>(new DValue(Document, _values[i].Key),
                new DValue(this, _values[i].Value, i));
        }
    }

    public void Add(KeyValuePair<DValue, DValue> keyValuePair)
    {
        Add(keyValuePair.Key, keyValuePair.Value);
    }

    public bool Contains(KeyValuePair<DValue, DValue> keyValuePair)
    {
        ThrowIfInvalid();

        return IndexOf(keyValuePair) >= 0;
    }

    public void CopyTo(KeyValuePair<DValue, DValue>[] keyValuePair, int index)
    {
        ArgumentNullException.ThrowIfNull(keyValuePair);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        ThrowIfInvalid();

        if (keyValuePair.Length - index < _count)
        {
            throw new ArgumentException("Destination array is not long enough.", nameof(keyValuePair));
        }

        var doc = Document;

        for (var i = 0; i < _count; i++)
        {
            keyValuePair[index + i] = new KeyValuePair<DValue, DValue>(new DValue(doc, _values[i].Key),
                new DValue(this, _values[i].Value, i));
        }
    }

    public bool Remove(KeyValuePair<DValue, DValue> keyValuePair)
    {
        ThrowIfInvalid();

        var index = IndexOf(keyValuePair);

        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);

        return true;
    }

    public void Clear()
    {
        ThrowIfInvalid();

        var doc = Document;
        var rootId = Id;
        var linkId = doc.GetToken(rootId).LinkId;

        for (var i = 0; i < _count; i++)
        {
            doc.Free(_values[i].Key, rootId, linkId);
            doc.Free(_values[i].Value, rootId, linkId);
        }

        _values.AsSpan(0, _count).Clear();
        _count = 0;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int IndexOf(KeyValuePair<DValue, DValue> keyValuePair)
    {
        ThrowIfInvalid();

        var doc = Document;

        for (var i = 0; i < _count; i++)
        {
            if (keyValuePair.Key.Equals(new DValue(doc, _values[i].Key)) &&
                keyValuePair.Value.Equals(new DValue(doc, _values[i].Value)))
            {
                return i;
            }
        }

        return -1;
    }

    public void Insert(int index, KeyValuePair<DValue, DValue> keyValuePair)
    {
        ThrowIfInvalid();

        if ((uint)index > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var keyId = Document.JoinValue(0, keyValuePair.Key);
        var valueId = Document.JoinValue(0, keyValuePair.Value);

        InsertKeyValuePairInternal(index, keyId, valueId);
    }

    public void RemoveAt(int index)
    {
        ThrowIfInvalid();

        if ((uint)index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var doc = Document;
        var rootId = Id;
        var linkId = doc.GetToken(rootId).LinkId;
        doc.Free(_values[index].Key, rootId, linkId);
        doc.Free(_values[index].Value, rootId, linkId);

        _values.AsSpan().Slice(index + 1, _count - index - 1)
            .CopyTo(_values.AsSpan().Slice(index, _count - index - 1));
        _values[--_count] = default;
    }

    /// <summary>Exchanges two entries without copying or replacing their keys or values.</summary>
    /// <remarks>
    ///     Trivia remains attached to its existing tokens. Swapping an entry with itself has no effect.
    ///     Position-bound values for moved entries become invalid; token-based references remain valid.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either index is outside the map.</exception>
    /// <exception cref="ObjectDisposedException">The map is no longer valid.</exception>
    public void Swap(int index1, int index2)
    {
        ThrowIfInvalid();

        if ((uint)index1 >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index1));
        }

        if ((uint)index2 >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index2));
        }

        if (index1 == index2)
        {
            return;
        }

        (_values[index1], _values[index2]) = (_values[index2], _values[index1]);
    }

    protected internal override DElement GetKeyElement(int index)
    {
        return new DElement(Document, _values[index].Key);
    }

    protected internal override DElement GetValueElement(int index)
    {
        return new DElement(Document, _values[index].Value);
    }

    public DMap DeepClone()
    {
        ThrowIfInvalid();

        var count = Count;
        var map = new DMap(Document.Settings, count);

        for (var i = 0; i < count; i++)
        {
            map.Add(new DElement(Document, _values[i].Key), new DElement(Document, _values[i].Value));
        }

        return map;
    }

    public void SetValue<T>(T value, int index)
    {
        ThrowIfInvalid();

        if ((uint)index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Document.JoinAny(_values[index].Value, value);
    }

    public void SetKey<T>(T value, int index)
    {
        ThrowIfInvalid();

        if ((uint)index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Document.JoinAny(_values[index].Key, value);
    }

    public DObject AddObject<T>(T key, int capacity = 0)
    {
        ThrowIfInvalid();

        var keyId = Document.JoinAny(0, key);

        var obj = Document.AddExtendObject(capacity);

        AddKeyValuePairInternal(keyId, obj.Id);

        return obj;
    }

    public DMap AddMap<T>(T key, int capacity = 0)
    {
        ThrowIfInvalid();

        var keyId = Document.JoinAny(0, key);

        var map = Document.AddExtendMap(capacity);

        AddKeyValuePairInternal(keyId, map.Id);

        return map;
    }

    public DArray AddArray<T>(T key, int capacity = 0)
    {
        ThrowIfInvalid();

        var keyId = Document.JoinAny(0, key);

        var arr = Document.AddExtendArray(capacity);

        AddKeyValuePairInternal(keyId, arr.Id);

        return arr;
    }

    public void Add<TKey, TValue>(TKey key, TValue value)
    {
        ThrowIfInvalid();

        var keyId = Document.JoinAny(0, key);
        var valueId = Document.JoinAny(0, value);

        AddKeyValuePairInternal(keyId, valueId);
    }

    public void Add(DValue key, DValue value)
    {
        ThrowIfInvalid();

        var keyId = Document.JoinValue(0, key);
        var valueId = Document.JoinValue(0, value);

        AddKeyValuePairInternal(keyId, valueId);
    }

    private void Add(DElement key, DElement value)
    {
        var keyId = Document.JoinElement(0, key);
        var valueId = Document.JoinElement(0, value);

        AddKeyValuePairInternal(keyId, valueId);
    }

    internal void AddKeyValuePairInternal(uint keyId, uint valueId)
    {
        if (_count >= _values.Length)
        {
            Array.Resize(ref _values, (_values.Length + 1) * 2);
        }

        _values[_count++] = new KeyValuePair<uint, uint>(keyId, valueId);
    }

    private void InsertKeyValuePairInternal(int index, uint keyId, uint valueId)
    {
        if (_count >= _values.Length)
        {
            Array.Resize(ref _values, (_values.Length + 1) * 2);
        }

        _values.AsSpan(index, _count - index)
            .CopyTo(_values.AsSpan(index + 1, _count - index));
        _values[index] = new KeyValuePair<uint, uint>(keyId, valueId);
        _count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal KeyValuePair<uint, uint> GetKeyValuePairInternal(int index)
    {
        return _values[index];
    }

    internal override void Release()
    {
        Clear();

        base.Release();
    }

    public static implicit operator DElement(DMap value)
    {
        return value._element;
    }

    public DObject AsObject()
    {
        ThrowIfInvalid();

        return Document.GetElementObject(_element.Id);
    }

    protected internal override DValue GetValue(int index)
    {
        return new DValue(Document, _values[index].Value);
    }

    protected internal override void SetValue(DValue value, int index)
    {
        Document.JoinValue(_values[index].Value, value);
    }
}