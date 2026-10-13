// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace REDox;

static class Helper
{
    public const int MaxDataSize = int.MaxValue - 256;

    public static byte[] ReadStream(Stream stream, out int length, int defaultStreamBufferSize)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] buffer;

        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;

            if (remaining > MaxDataSize)
            {
                throw new ArgumentException(
                    $"Stream is too large to read into a single buffer ({remaining} bytes).", nameof(stream));
            }

            buffer = ArrayPool<byte>.Shared.Rent((int)remaining + 1);
        }
        else
        {
            buffer = ArrayPool<byte>.Shared.Rent(defaultStreamBufferSize);
        }

        var written = 0;

        try
        {
            while (true)
            {
                if (written == buffer.Length)
                {
                    GrowStreamBuffer(ref buffer, written);
                }

                var read = stream.Read(buffer.AsSpan(written));

                if (read == 0)
                {
                    break;
                }

                written += read;
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        length = written;

        return buffer;
    }

    private static void GrowStreamBuffer(ref byte[] buffer, int length)
    {
        if (buffer.Length >= MaxDataSize)
        {
            ArrayPool<byte>.Shared.Return(buffer);

            throw new InvalidOperationException("Stream is too large to read into a single buffer.");
        }

        var nextSize = (int)Math.Min((long)buffer.Length * 2, MaxDataSize);
        var next = ArrayPool<byte>.Shared.Rent(nextSize);

        Array.Copy(buffer, next, length);
        ArrayPool<byte>.Shared.Return(buffer);

        buffer = next;
    }

    public static void StableSort<T>(List<T> list, IComparer<T>? comparer = null)
    {
        comparer ??= Comparer<T>.Default;

        var indexed = new (T Item, int Index)[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            indexed[i] = (list[i], i);
        }

        Array.Sort(indexed, (a, b) =>
        {
            var c = comparer.Compare(a.Item, b.Item);
            if (c != 0)
            {
                return c;
            }

            return a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < list.Count; i++)
        {
            list[i] = indexed[i].Item;
        }
    }

    public ref struct LocalList<T>
    {
        public LocalList(Span<T> buf)
        {
            Buf = buf;
            Count = 0;
            _pool = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T value)
        {
            if (Count >= Buf.Length)
            {
                Resize(Count + 1);
            }

            Buf[Count++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCount(int count)
        {
            while (Count < count)
            {
                Add(default!);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> AsSpan()
        {
            return Buf.Slice(0, Count);
        }

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return ref Buf[index];
            }
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            private set;
        }

        private void Resize(int minCapacity)
        {
            var nextSize = Math.Max(Buf.Length * 2, minCapacity);
            if (nextSize == 0)
            {
                nextSize = 4;
            }

            var pool = ArrayPool<T>.Shared.Rent(nextSize);
            Buf.Slice(0, Count).CopyTo(pool);

            if (_pool != null)
            {
                ArrayPool<T>.Shared.Return(_pool);
            }

            _pool = pool;
            Buf = pool.AsSpan();
        }

        public void Dispose()
        {
            if (_pool != null)
            {
                ArrayPool<T>.Shared.Return(_pool);
                _pool = null;
            }
        }

        public Span<T> Buf;
        private T[]? _pool;
    }

    public ref struct LocalStringBuilder
    {
        public LocalStringBuilder(Span<byte> buf)
        {
            _buf = buf;
            Length = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(int index, ReadOnlySpan<byte> str)
        {
            var length = str.Length;

            CheckExpand(length);

            for (var i = Length - 1; i >= index; i--)
            {
                _buf[i + length] = _buf[i];
            }

            Length += length;

            foreach (var c in str)
            {
                _buf[index++] = c;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(ReadOnlySpan<byte> str)
        {
            CheckExpand(str.Length);

            foreach (var c in str)
            {
                _buf[Length++] = c;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ToSpan()
        {
            return _buf.Slice(0, Length);
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            private set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckExpand(int length)
        {
            if (Length + length > _buf.Length)
            {
                var newSb = new byte[(_buf.Length + length) * 2];

                for (var i = 0; i < _buf.Length; i++)
                {
                    newSb[i] = _buf[i];
                }

                var count = Length;
                this = new LocalStringBuilder(newSb.AsSpan());
                Length = count;
            }
        }

        private readonly Span<byte> _buf;
    }

    public ref struct LocalStack<T>
    {
        public LocalStack(Span<T> stack, int maxDepth)
        {
            if (maxDepth == 0)
            {
                maxDepth = SerializerSettings.DefaultMaxDepth;
            }

            Count = 0;
            _maxDepth = maxDepth;

            if (stack.Length > maxDepth)
            {
                Buf = stack.Slice(0, maxDepth);
            }
            else
            {
                Buf = stack;
            }
        }

        private bool Resize()
        {
            if (Count >= _maxDepth)
            {
                return false;
            }

            var pool = ArrayPool<T>.Shared.Rent(Buf.Length * 2);

            Buf.Slice(0, Count).CopyTo(pool);

            var stackPt = Count;
            Count = stackPt;

            if (_pool != null)
            {
                ArrayPool<T>.Shared.Return(_pool);
            }

            _pool = pool;

            if (pool.Length < _maxDepth)
            {
                Buf = pool.AsSpan();
            }
            else
            {
                Buf = pool.AsSpan(0, _maxDepth);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Push(T v)
        {
            if (Count >= Buf.Length)
            {
                if (!Resize())
                {
                    return false;
                }
            }

            Buf[Count++] = v;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Peek()
        {
            return ref Buf[Count - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Pop()
        {
            return Buf[--Count];
        }

        public void Dispose()
        {
            if (_pool != null)
            {
                ArrayPool<T>.Shared.Return(_pool);
                _pool = null;
            }
        }

        public Span<T> Buf;
        public int Count;
        private T[]? _pool;
        private readonly int _maxDepth;
    }

    public ref struct LocalUtf8KeySet
    {
        public struct Entry
        {
            // A text token is never zero, so the default token can mark an empty slot.
            public DToken Key;
            public int Hash;
        }

        private readonly Document _document;
        private readonly bool _ignoreCase;
        private Span<Entry> _entries;
        private Entry[]? _rented;
        private int _count;

        /// <summary>
        ///     Creates a set of text tokens compared by the document's
        ///     <see cref="SerializerSettings.PropertyNameCaseInsensitive" /> setting.
        /// </summary>
        /// <param name="document">The document that resolves the text of the added tokens.</param>
        /// <param name="buffer">Initial storage whose length is zero or a power of two.</param>
        public LocalUtf8KeySet(Document document, Span<Entry> buffer)
        {
            if ((buffer.Length & (buffer.Length - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of two.", nameof(buffer));
            }

            _document = document;
            _ignoreCase = document.Settings.PropertyNameCaseInsensitive;
            _entries = buffer;
            _rented = null;
            _count = 0;
            _entries.Clear();
        }

        /// <summary>Adds a text token to the set.</summary>
        /// <param name="key">A text token resolvable by the document passed to the constructor.</param>
        /// <returns><see langword="false" /> if an equal key already exists; otherwise, <see langword="true" />.</returns>
        public bool Add(DToken key)
        {
            var bytes = _document.GetTextBytes(key);
            var hash = Utf8Helper.GetHashCode(bytes, _ignoreCase);

            if (_entries.IsEmpty)
            {
                Grow();
            }

            while (true)
            {
                var mask = _entries.Length - 1;
                var index = hash & mask;
                while (!_entries[index].Key.Equals(default))
                {
                    ref var entry = ref _entries[index];
                    if (entry.Hash == hash && (entry.Key.Equals(key) ||
                                               IsSameText(entry.Key, bytes)))
                    {
                        return false;
                    }

                    index = (index + 1) & mask;
                }

                // Grow only for a new key, and keep at least a quarter of the slots empty.
                if (_count >= _entries.Length - (_entries.Length >> 2))
                {
                    Grow();
                    continue;
                }

                _entries[index] = new Entry { Key = key, Hash = hash };
                _count++;
                return true;
            }
        }

        private readonly bool IsSameText(DToken key, ReadOnlySpan<byte> bytes)
        {
            return Utf8Helper.Equals(_document.GetTextBytes(key), bytes, _ignoreCase);
        }

        private void Grow()
        {
            var capacity = Math.Max(32, checked(_entries.Length * 2));
            var rented = ArrayPool<Entry>.Shared.Rent(capacity);
            var entries = rented.AsSpan(0, capacity);
            entries.Clear();

            foreach (var entry in _entries)
            {
                if (entry.Key.Equals(default))
                {
                    continue;
                }

                var index = entry.Hash & (capacity - 1);
                while (!entries[index].Key.Equals(default))
                {
                    index = (index + 1) & (capacity - 1);
                }

                entries[index] = entry;
            }

            var previous = _rented;
            _entries = entries;
            _rented = rented;
            if (previous != null)
            {
                ArrayPool<Entry>.Shared.Return(previous);
            }
        }

        public void Clear()
        {
            _entries.Clear();
            _count = 0;
        }

        public void Dispose()
        {
            if (_rented != null)
            {
                ArrayPool<Entry>.Shared.Return(_rented);
                _rented = null;
            }

            _entries = default;
            _count = 0;
        }
    }

    private sealed class Gen2GcCallback
    {
        private readonly Action _callback;

        private Gen2GcCallback(Action callback)
        {
            _callback = callback;
        }

        public static void Register(Action callback)
        {
            var target = new Gen2GcCallback(callback);

            GC.KeepAlive(target);
        }

        ~Gen2GcCallback()
        {
            try
            {
                _callback.Invoke();
            }
            finally
            {
                if (!AppDomain.CurrentDomain.IsFinalizingForUnload() && !Environment.HasShutdownStarted)
                {
                    GC.ReRegisterForFinalize(this);
                }
            }
        }
    }

    public sealed class InstanceCache<T> : IDisposable
    {
        private const int PoolSize = 32;

        private static readonly InstanceCache<T>?[] s_pool = new InstanceCache<T>?[PoolSize];

        static InstanceCache()
        {
            Gen2GcCallback.Register(() => Clear());
        }

        private InstanceCache(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public static int Count
        {
            get
            {
                var count = 0;
                for (var i = 0; i < PoolSize; i++)
                {
                    if (s_pool[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Dispose()
        {
            var start = Environment.CurrentManagedThreadId;

            for (var i = 0; i < PoolSize; i++)
            {
                var index = (start + i) % PoolSize;

                if (s_pool[index] == null)
                {
                    if (Interlocked.CompareExchange(ref s_pool[index], this, null) == null)
                    {
                        return;
                    }
                }
            }

            if (Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public static void Clear()
        {
            for (var i = 0; i < PoolSize; i++)
            {
                var item = s_pool[i];

                if (item != null)
                {
                    if (Interlocked.CompareExchange(ref s_pool[i], null, item) == item)
                    {
                        if (item.Value is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstanceCache<T> Get(Func<T> factory)
        {
            var start = Environment.CurrentManagedThreadId;

            for (var i = 0; i < PoolSize; i++)
            {
                var index = (start + i) % PoolSize;

                var item = s_pool[index];

                if (item != null)
                {
                    if (Interlocked.CompareExchange(ref s_pool[index], null, item) == item)
                    {
                        return item;
                    }
                }
            }

            return new InstanceCache<T>(factory());
        }
    }
}