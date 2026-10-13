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
public sealed partial class DObject : DContainer, IDictionary<string, DValue>
{
    private const int MinBucketCount = 4;

    private int[]? _buckets;
    private int[] _hashCodes = [];
    private int _indexedCount = -1;
    private int[] _next = [];
    private KeyValuePair<uint, uint>[] _values;

    public DObject(SerializerSettings? settings = null, int capacity = 0)
    {
        _values = capacity > 0 ? new KeyValuePair<uint, uint>[capacity] : [];
        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;
    }

    public DObject(IEnumerable<KeyValuePair<string, DValue>> enumerator,
        SerializerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _values = [];
        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;

        foreach (var v in enumerator)
        {
            Add(v.Key, v.Value);
        }
    }

    internal DObject(DElement element, int capacity)
    {
        _element = element;
        _values = new KeyValuePair<uint, uint>[capacity];
    }

    internal DObject(DElement element, DObject src)
    {
        _element = element;
        _values = src.KeyValuePairs.ToArray();
        _count = _values.Length;
        TriviaId = src.TriviaId;
    }

    internal DObject(DElement element, DMap src)
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
            if (value < _count)
            {
                throw new ArgumentOutOfRangeException();
            }

            Array.Resize(ref _values, value);
        }
    }

    public DValue this[int index]
    {
        get
        {
            ThrowIfInvalid();
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            return GetValue(index);
        }
        set
        {
            ThrowIfInvalid();
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            SetValue(value, index);
        }
    }

    private StringComparer KeyComparer =>
        Document.Settings.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    bool ICollection<KeyValuePair<string, DValue>>.IsReadOnly => false;


    public IEnumerator<KeyValuePair<string, DValue>> GetEnumerator()
    {
        ThrowIfInvalid();

        for (var i = 0; i < _count; i++)
        {
            var propertyName = GetPropertyName(i);

            if (propertyName != null)
            {
                yield return new KeyValuePair<string, DValue>(propertyName,
                    new DValue(this, _values[i].Value, i));
            }
        }
    }

    public void Add(string key, DValue value)
    {
        ThrowIfInvalid();

        Add<DValue>(key, value);
    }

    void ICollection<KeyValuePair<string, DValue>>.Add(KeyValuePair<string, DValue> keyValuePair)
    {
        ThrowIfInvalid();

        Add(keyValuePair.Key, keyValuePair.Value);
    }

    bool ICollection<KeyValuePair<string, DValue>>.Contains(KeyValuePair<string, DValue> keyValuePair)
    {
        ArgumentNullException.ThrowIfNull(keyValuePair.Key);
        ThrowIfInvalid();

        var index = FindIndex(keyValuePair.Key);

        if (index < 0)
        {
            return false;
        }

        return new DValue(this, _values[index].Value, index).Equals(keyValuePair.Value);
    }

    void ICollection<KeyValuePair<string, DValue>>.CopyTo(KeyValuePair<string, DValue>[] keyValuePair,
        int index)
    {
        ThrowIfInvalid();
        ArgumentNullException.ThrowIfNull(keyValuePair);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0);

        if (keyValuePair.Length - index < _count)
        {
            throw new ArgumentException();
        }

        for (var i = 0; i < _count; i++)
        {
            keyValuePair[index + i] = GetAt(i);
        }
    }

    public bool ContainsKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfInvalid();

        return FindIndex(key) >= 0;
    }

    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfInvalid();

        var index = FindIndex(key);

        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    bool ICollection<KeyValuePair<string, DValue>>.Remove(KeyValuePair<string, DValue> keyValuePair)
    {
        ArgumentNullException.ThrowIfNull(keyValuePair.Key);
        ThrowIfInvalid();

        var index = FindIndex(keyValuePair.Key);

        if (index < 0)
        {
            return false;
        }

        if (!new DValue(this, _values[index].Value, index).Equals(keyValuePair.Value))
        {
            return false;
        }

        RemoveAt(index);

        return true;
    }

    bool IDictionary<string, DValue>.TryGetValue(string key, out DValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfInvalid();

        return TryGetPropertyValue(key, out value);
    }

    public DValue this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);

            ThrowIfInvalid();
            var index = FindIndex(key);

            if (index >= 0)
            {
                return new DValue(this, _values[index].Value, index);
            }

            throw new KeyNotFoundException(key);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(key);

            ThrowIfInvalid();
            var index = GetEntryRefOrAdd(key);

            var entry = _values[index];

            if (entry.Value == 0)
            {
                try
                {
                    entry = new KeyValuePair<uint, uint>(entry.Key, Document.JoinValue(0, value));
                    _values[index] = entry;
                }
                catch
                {
                    RemoveAt(index);
                    throw;
                }
            }
            else
            {
                Document.JoinValue(entry.Value, value);
            }
        }
    }

    public ICollection<string> Keys => new KeyCollection(this);

    public ICollection<DValue> Values => new ValueCollection(this);

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

        _count = 0;

        _buckets = null;
        _indexedCount = -1;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    protected internal override DValue GetValue(int index)
    {
        return new DValue(Document, _values[index].Value);
    }

    protected internal override void SetValue(DValue value, int index)
    {
        Document.JoinValue(_values[index].Value, value);
    }

    public bool TryGetPropertyValue(string propertyName, out DValue value)
    {
        ThrowIfInvalid();

        var index = FindIndex(propertyName);
        if (index < 0)
        {
            value = default;
            return false;
        }

        value = new DValue(this, _values[index].Value, index);
        return true;
    }

    public KeyValuePair<string, DValue> GetAt(int index)
    {
        ThrowIfInvalid();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));

        return new KeyValuePair<string, DValue>(GetPropertyName(index) ?? string.Empty,
            new DValue(this, _values[index].Value, index));
    }


    internal string? GetPropertyName(int index)
    {
        var id = _values[index].Key;
        var doc = Document;

        if (doc.TryGetStringExact(id, out var name))
        {
            if (!doc.GetToken(id).IsExtended)
            {
                doc.JoinAny(id, name);
            }

            return name;
        }

        return null;
    }

    internal DElement GetValueElement(string propertyName)
    {
        var valueId = FindValueId(propertyName);

        return new DElement(Document, valueId);
    }

    public DObject DeepClone()
    {
        ThrowIfInvalid();

        var count = Count;
        var obj = new DObject(Document.Settings, count);

        var dDoc = obj.Document;
        var sDoc = Document;

        for (var i = 0; i < count; i++)
        {
            ref var entry = ref _values[i];

            var key = GetPropertyName(i);
            uint keyId;

            if (key != null)
            {
                keyId = dDoc.JoinAny(0, key);
            }
            else
            {
                keyId = dDoc.JoinElement(0, new DElement(sDoc, entry.Key));
            }

            var valueId = dDoc.JoinElement(0, new DElement(sDoc, entry.Value));

            obj.AddKeyValuePairInternal(keyId, valueId);
        }

        return obj;
    }

    public bool TryGetPropertyEntry(string propertyName, out DProperty property)
    {
        var index = FindIndex(propertyName);

        if (index < 0)
        {
            property = default;
            return false;
        }

        ref var entry = ref _values[index];

        property = new DProperty(Document, entry.Key, entry.Value);
        return true;
    }

    public DProperty GetPropertyEntry(string propertyName)
    {
        var index = FindIndex(propertyName);

        if (index < 0)
        {
            throw new KeyNotFoundException(propertyName);
        }

        ref var entry = ref _values[index];

        return new DProperty(Document, entry.Key, entry.Value);
    }

    private int GetEntryRefOrAdd(string propertyName)
    {
        var index = FindIndex(propertyName);

        if (index >= 0)
        {
            return index;
        }

        return CreateEntry(propertyName);
    }

    private bool TryAddEntry(string propertyName, out int index)
    {
        if (FindIndex(propertyName) >= 0)
        {
            index = -1;
            return false;
        }

        index = CreateEntry(propertyName);

        return true;
    }

    internal void SetProperty(string propertyName, object? value)
    {
        var index = GetEntryRefOrAdd(propertyName);

        var entry = _values[index];

        if (entry.Value == 0)
        {
            try
            {
                entry = new KeyValuePair<uint, uint>(entry.Key, Document.JoinObject(0, value));
                _values[index] = entry;
            }
            catch
            {
                RemoveAt(index);
                throw;
            }
        }
        else
        {
            Document.JoinObject(entry.Value, value);
        }
    }

    public bool TryAdd<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfInvalid();

        if (!TryAddEntry(key, out var index))
        {
            return false;
        }

        var entry = _values[index];

        try
        {
            entry = new KeyValuePair<uint, uint>(entry.Key, Document.JoinAny(entry.Value, value));
            _values[index] = entry;
        }
        catch
        {
            RemoveAt(index);
            throw;
        }

        return true;
    }

    public DObject AddObject(string key, int capacity = 0)
    {
        ThrowIfInvalid();

        if (!TryAddEntry(key, out var index))
        {
            throw new ArgumentException();
        }

        var entry = _values[index];

        var obj = Document.AddExtendObject(capacity);

        _values[index] = new KeyValuePair<uint, uint>(entry.Key, obj.Id);

        return obj;
    }

    public DMap AddMap(string key, int capacity = 0)
    {
        ThrowIfInvalid();

        if (!TryAddEntry(key, out var index))
        {
            throw new ArgumentException();
        }

        var entry = _values[index];

        var map = Document.AddExtendMap(capacity);

        _values[index] = new KeyValuePair<uint, uint>(entry.Key, map.Id);

        return map;
    }

    public DArray AddArray(string key, int capacity = 0)
    {
        ThrowIfInvalid();

        if (!TryAddEntry(key, out var index))
        {
            throw new ArgumentException();
        }

        var entry = _values[index];

        var arr = Document.AddExtendArray(capacity);

        _values[index] = new KeyValuePair<uint, uint>(entry.Key, arr.Id);

        return arr;
    }

    public void Add<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!TryAdd(key, value))
        {
            throw new ArgumentException();
        }
    }

    public IEnumerable<DProperty> Properties()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return new DProperty(Document, _values[i].Key, _values[i].Value);
        }
    }

    public DMap AsMap()
    {
        return Document.GetElementMap(_element.Id);
    }

    protected internal override DElement GetKeyElement(int index)
    {
        return new DElement(Document, _values[index].Key);
    }

    protected internal override DElement GetValueElement(int index)
    {
        return new DElement(Document, _values[index].Value);
    }

    private void RemoveAt(int index)
    {
        var doc = Document;
        var rootId = Id;
        var linkId = doc.GetToken(rootId).LinkId;
        doc.Free(_values[index].Key, rootId, linkId);
        if (_values[index].Value != 0)
        {
            doc.Free(_values[index].Value, rootId, linkId);
        }

        _values.AsSpan().Slice(index + 1, _count - index - 1)
            .CopyTo(_values.AsSpan().Slice(index, _count - index - 1));
        _values[--_count] = default;

        _buckets = null;
        _indexedCount = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint FindValueId(string propertyName)
    {
        var index = FindIndex(propertyName);

        if (index < 0)
        {
            return 0;
        }

        return _values[index].Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPow2(int value)
    {
        var size = MinBucketCount;
        while (size < value)
        {
            size <<= 1;
        }

        return size;
    }

    private void RebuildIndex()
    {
        var count = _count;

        if (_hashCodes.Length < _values.Length)
        {
            _hashCodes = new int[_values.Length];
            _next = new int[_values.Length];
        }

        var bucketCount = NextPow2(count);

        if (_buckets == null || _buckets.Length != bucketCount)
        {
            _buckets = new int[bucketCount];
        }
        else
        {
            Array.Clear(_buckets, 0, _buckets.Length);
        }

        var comparer = KeyComparer;

        for (var i = 0; i < count; i++)
        {
            var propertyName = GetPropertyName(i);

            if (propertyName != null)
            {
                InsertIndex(i, comparer.GetHashCode(propertyName));
            }
            else
            {
                _hashCodes[i] = 0;
                _next[i] = -1;
            }
        }

        _indexedCount = count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureIndex()
    {
        if (_buckets == null || _indexedCount != _count)
        {
            RebuildIndex();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertIndex(int index, int hashCode)
    {
        var buckets = _buckets!;
        var bucket = hashCode & (buckets.Length - 1);

        _hashCodes[index] = hashCode;
        _next[index] = buckets[bucket] - 1;
        buckets[bucket] = index + 1;
    }

    private void GrowBuckets(int bucketCount)
    {
        if (_buckets == null || _buckets.Length != bucketCount)
        {
            _buckets = new int[bucketCount];
        }
        else
        {
            Array.Clear(_buckets, 0, _buckets.Length);
        }

        var buckets = _buckets;
        var mask = bucketCount - 1;
        var count = _indexedCount;

        for (var i = 0; i < count; i++)
        {
            var bucket = _hashCodes[i] & mask;
            _next[i] = buckets[bucket] - 1;
            buckets[bucket] = i + 1;
        }
    }

    private void AddToIndex(int index, string key)
    {
        if (_buckets == null || _indexedCount != index)
        {
            return;
        }

        if (_hashCodes.Length < _values.Length)
        {
            Array.Resize(ref _hashCodes, _values.Length);
            Array.Resize(ref _next, _values.Length);
        }

        _hashCodes[index] = KeyComparer.GetHashCode(key);
        _indexedCount = index + 1;

        if (index + 1 > _buckets.Length)
        {
            GrowBuckets(NextPow2(index + 1));
            return;
        }

        InsertIndex(index, _hashCodes[index]);
    }

    private int FindIndex(string propertyName)
    {
        EnsureIndex();

        var comparer = KeyComparer;
        var buckets = _buckets!;
        var hashCode = comparer.GetHashCode(propertyName);
        var bucket = hashCode & (buckets.Length - 1);

        for (var i = buckets[bucket] - 1; i >= 0; i = _next[i])
        {
            if (_hashCodes[i] == hashCode)
            {
                var name = GetPropertyName(i);

                if (name != null && comparer.Equals(name, propertyName))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CreateEntry(string key)
    {
        if (_count >= _values.Length)
        {
            Capacity = Math.Max(4, _values.Length) * 2;
        }

        var doc = Document;
        var index = _count++;

        var keyId = doc.JoinAny(0, key);

        _values[index] = new KeyValuePair<uint, uint>(keyId, 0);

        AddToIndex(index, key);

        return index;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AddKeyValuePairInternal(uint keyId, uint valueId)
    {
        if (_count >= _values.Length)
        {
            Capacity = Math.Max(4, _values.Length) * 2;
        }

        _values[_count++] = new KeyValuePair<uint, uint>(keyId, valueId);
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

    private class KeyCollection : ICollection<string>
    {
        private readonly DObject _object;

        public KeyCollection(DObject obj)
        {
            _object = obj;
        }

        public void Add(string v)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(string keyName)
        {
            return _object.ContainsKey(keyName);
        }

        public int Count => _object.Count;

        public void CopyTo(string[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfLessThan(arrayIndex, 0);

            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException();
            }

            var index = arrayIndex;
            foreach (var kv in _object)
            {
                array[index++] = kv.Key;
            }
        }

        public bool Remove(string key)
        {
            throw new NotSupportedException();
        }

        public bool IsReadOnly => true;

        public IEnumerator<string> GetEnumerator()
        {
            foreach (var kv in _object)
            {
                yield return kv.Key;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private class ValueCollection : ICollection<DValue>
    {
        private readonly DObject _object;

        public ValueCollection(DObject obj)
        {
            _object = obj;
        }

        public void Add(DValue v)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(DValue value)
        {
            foreach (var kv in _object)
            {
                if (kv.Value.Equals(value))
                {
                    return true;
                }
            }

            return false;
        }

        public int Count => _object.Count;

        public void CopyTo(DValue[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfLessThan(arrayIndex, 0);

            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException();
            }

            var index = arrayIndex;
            foreach (var kv in _object)
            {
                array[index++] = kv.Value;
            }
        }

        public bool Remove(DValue key)
        {
            throw new NotSupportedException();
        }

        public bool IsReadOnly => true;

        public IEnumerator<DValue> GetEnumerator()
        {
            foreach (var kv in _object)
            {
                yield return kv.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}