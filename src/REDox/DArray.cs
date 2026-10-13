// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace REDox;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
[DebuggerTypeProxy(typeof(DebugView))]
public sealed partial class DArray : DContainer, IList<DValue>
{
    private uint[] _values;

    public DArray(SerializerSettings? settings = null, int capacity = 0)
    {
        _values = capacity > 0 ? new uint[capacity] : [];
        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;
    }

    public DArray(IEnumerable<DValue> items, SerializerSettings? settings = null)
    {
        if (items is ICollection<DValue> collection)
        {
            _values = new uint[collection.Count];
        }
        else
        {
            _values = Array.Empty<uint>();
        }

        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;

        foreach (var item in items)
        {
            AddInternal(Document.JoinValue(0, item));
        }
    }

    public DArray(IEnumerable<DContainer> items, SerializerSettings? settings = null)
    {
        if (items is ICollection<DContainer> collection)
        {
            _values = new uint[collection.Count];
        }
        else
        {
            _values = Array.Empty<uint>();
        }

        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;

        foreach (var item in items)
        {
            AddInternal(Document.JoinElement(0, item));
        }
    }

    public DArray(params DValue[] items)
    {
        _values = new uint[items.Length];
        _element = new DoxNodeDocument(this, SerializerSettings.Default).RootElement;

        foreach (var item in items)
        {
            AddInternal(Document.JoinValue(0, item));
        }
    }

    public DArray(SerializerSettings settings, params DValue[] items)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _values = new uint[items.Length];
        _element = new DoxNodeDocument(this, settings).RootElement;

        foreach (var item in items)
        {
            AddInternal(Document.JoinValue(0, item));
        }
    }

    public DArray(scoped ReadOnlySpan<DValue> items, SerializerSettings? settings)
    {
        _values = new uint[items.Length];
        _element = new DoxNodeDocument(this, settings ?? SerializerSettings.Default).RootElement;

        foreach (var item in items)
        {
            AddInternal(Document.JoinValue(0, item));
        }
    }

    internal DArray(DElement element, int capacity)
    {
        _element = element;
        _values = new uint[capacity];
    }

    internal DArray(DElement element, DArray src)
    {
        _element = element;
        _values = new uint[src.Count];
        _count = src._count;
        TriviaId = src.TriviaId;
        Array.Copy(src._values, _values, _count);
    }

    public override int Capacity
    {
        get => _values.Length;
        set
        {
            if (value < _count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Array.Resize(ref _values, value);
        }
    }

    bool ICollection<DValue>.IsReadOnly => false;

    IEnumerator<DValue> IEnumerable<DValue>.GetEnumerator()
    {
        ThrowIfInvalid();
        return new Enumerator(this);
    }

    public void Add(DValue value)
    {
        ThrowIfInvalid();

        AddInternal(Document.JoinValue(0, value));
    }

    public bool Contains(DValue value)
    {
        return IndexOf(value) >= 0;
    }

    void ICollection<DValue>.CopyTo(DValue[]? array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0);

        if (index + _count > array.Length)
        {
            throw new ArgumentException();
        }

        for (var i = 0; i < _count; i++)
        {
            array[i + index] = this[i];
        }
    }

    public DValue this[int index]
    {
        get
        {
            ThrowIfInvalid();

            if ((uint)index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new DValue(this, _values[index], index);
        }
        set
        {
            ThrowIfInvalid();

            if ((uint)index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Document.JoinValue(_values[index], value);
        }
    }

    public int IndexOf(DValue value)
    {
        ThrowIfInvalid();

        var count = Count;
        for (var i = 0; i < count; i++)
        {
            var current = this[i];

            if (current.Equals(value) ||
                (current.GetToken().Kind == DTokenKind.Null &&
                 value.GetToken().Kind == DTokenKind.Null))
            {
                return i;
            }
        }

        return -1;
    }

    public void Insert(int index, DValue value)
    {
        ThrowIfInvalid();

        if (index < 0 || index > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var doc = Document;
        InsertInternal(index, doc.JoinValue(0, value));
    }

    public bool Remove(DValue value)
    {
        var index = IndexOf(value);
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
        var linkId = doc.GetToken(Id).LinkId;
        for (var i = 0; i < _count; i++)
        {
            doc.Free(_values[i], rootId, linkId);
        }

        _count = 0;
    }

    public void RemoveAt(int index)
    {
        ThrowIfInvalid();

        if ((uint)index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Document.Free(_values[index], Id, Document.GetToken(Id).LinkId);
        _values.AsSpan().Slice(index + 1, _count - index - 1)
            .CopyTo(_values.AsSpan().Slice(index, _count - index - 1));
        _count--;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        ThrowIfInvalid();
        return GetEnumerator();
    }

    protected internal override DValue GetValue(int index)
    {
        return new DValue(Document, _values[index]);
    }

    protected internal override void SetValue(DValue value, int index)
    {
        Document.JoinValue(_values[index], value);
    }

    protected internal override DElement GetKeyElement(int index)
    {
        throw new NotSupportedException();
    }

    protected internal override DElement GetValueElement(int index)
    {
        return new DElement(Document, _values[index]);
    }

    public void RemoveRange(int index, int count)
    {
        ThrowIfInvalid();

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (_count - index < count)
        {
            throw new ArgumentException();
        }

        var doc = Document;
        var rootId = Id;
        var linkId = doc.GetToken(Id).LinkId;
        for (var i = 0; i < count; i++)
        {
            doc.Free(_values[index + i], rootId, linkId);
        }

        _values.AsSpan().Slice(index + count, _count - index - count)
            .CopyTo(_values.AsSpan().Slice(index, _count - index - count));
        _count -= count;
    }

    public Enumerator GetEnumerator()
    {
        ThrowIfInvalid();
        return new Enumerator(this);
    }

    public DArray DeepClone()
    {
        var count = Count;
        var arr = new DArray(Document.Settings, count);

        for (var i = 0; i < count; i++)
        {
            arr.Add(new DElement(Document, _values[i]));
        }

        return arr;
    }

    internal void SetValueInternal(object? value, int index)
    {
        if ((uint)index >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Document.JoinObject(_values[index], value);
    }

    public DObject AddObject(int capacity = 0)
    {
        ThrowIfInvalid();
        var obj = Document.AddExtendObject(capacity);
        AddInternal(obj.Id);
        return obj;
    }

    public DMap AddMap(int capacity = 0)
    {
        ThrowIfInvalid();
        var map = Document.AddExtendMap(capacity);
        AddInternal(map.Id);
        return map;
    }

    public DArray AddArray(int capacity = 0)
    {
        ThrowIfInvalid();
        var arr = Document.AddExtendArray(capacity);
        AddInternal(arr.Id);
        return arr;
    }

    public void Add<T>(T value)
    {
        ThrowIfInvalid();

        AddInternal(Document.JoinAny(0, value));
    }

    public void Insert<T>(int index, T value)
    {
        ThrowIfInvalid();

        if (index < 0 || index > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        InsertInternal(index, Document.JoinAny(0, value));
    }

    private void EnsureCapacityForAdd()
    {
        if (_count >= _values.Length)
        {
            Array.Resize(ref _values, Math.Max(4, _values.Length) * 2);
        }
    }

    internal void AddInternal(uint tokenId)
    {
        EnsureCapacityForAdd();

        _values[_count++] = tokenId;
    }

    private void InsertInternal(int index, uint tokenId)
    {
        EnsureCapacityForAdd();

        _values.AsSpan().Slice(index, _count - index).CopyTo(_values.AsSpan(index + 1, _count - index));
        _values[index] = tokenId;
        _count++;
    }

    internal uint GetValueInternal(int index)
    {
        return _values[index];
    }

    internal override void Release()
    {
        Clear();

        base.Release();
    }

    public struct Enumerator : IEnumerator<DValue>
    {
        private readonly DArray _array;
        private int _index;

        internal Enumerator(DArray array)
        {
            _array = array;
            _index = -1;
        }

        public bool MoveNext()
        {
            _index++;
            return _index < _array._count;
        }

        public DValue Current => _array[_index];

        object IEnumerator.Current => Current;

        public void Reset()
        {
            _index = -1;
        }

        public void Dispose()
        {
        }
    }
}