using System;
using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace System.Collections.Generic
{
    internal sealed class GenericEmptyEnumerator<T> : IEnumerator<T>
    {
        public static readonly GenericEmptyEnumerator<T> Instance = new GenericEmptyEnumerator<T>();

        private GenericEmptyEnumerator() { }

        public bool MoveNext() => false;

        public T Current
        {
            get
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                return default!;
            }
        }

        object? IEnumerator.Current => Current;

        public void Reset() { }

        public void Dispose() { }
    }
    public class KeyNotFoundException : SystemException
    {
        public KeyNotFoundException()
            : base(string.Empty)
        { }

        public KeyNotFoundException(string? message)
            : base(message ?? string.Empty)
        { }

        public KeyNotFoundException(string? message, Exception? innerException)
            : base(message ?? string.Empty, innerException)
        { }
    }
    public interface IEnumerable<out T> : System.Collections.IEnumerable
        where T : allows ref struct
    {
        new IEnumerator<T> GetEnumerator();
    }
    public interface IEnumerator<out T> : IDisposable, IEnumerator
        where T : allows ref struct
    {
        new T Current
        {
            get;
        }
    }
    public interface IAsyncEnumerable<out T> where T : allows ref struct
    {
        /// <summary>Returns an enumerator that iterates asynchronously through the collection.</summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that may be used to cancel the asynchronous iteration.</param>
        /// <returns>An enumerator that can be used to iterate asynchronously through the collection.</returns>
        IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
    }
    public interface IAsyncEnumerator<out T> : IAsyncDisposable where T : allows ref struct
    {
        /// <summary>Advances the enumerator asynchronously to the next element of the collection.</summary>
        /// <returns>
        /// A <see cref="ValueTask{Boolean}"/> that will complete with a result of <c>true</c> if the enumerator
        /// was successfully advanced to the next element, or <c>false</c> if the enumerator has passed the end
        /// of the collection.
        /// </returns>
        ValueTask<bool> MoveNextAsync();

        /// <summary>Gets the element in the collection at the current position of the enumerator.</summary>
        T Current { get; }
    }
    public interface IReadOnlyCollection<out T> : IEnumerable<T>
    {
        int Count
        {
            get;
        }
    }
    public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
    {
        T this[int index]
        {
            get;
        }
    }
    public interface ICollection<T> : IEnumerable<T>
    {
        int Count
        {
            get;
        }
        bool IsReadOnly
        {
            get;
        }

        void Add(T item);
        void Clear();
        bool Contains(T item);
        void CopyTo(T[] array, int arrayIndex);
        bool Remove(T item);
    }
    // The generic IComparer interface implements a method that compares
    // two objects. It is used in conjunction with the Sort and
    // BinarySearch methods on the Array, List, and SortedList classes.
    public interface IComparer<in T> where T : allows ref struct
    {
        // Compares two objects. An implementation of this method must return a
        // value less than zero if x is less than y, zero if x is equal to y, or a
        // value greater than zero if x is greater than y.
        //
        int Compare(T? x, T? y);
    }
    // An IList is an ordered collection of objects.  The exact ordering
    // is up to the implementation of the list, ranging from a sorted
    // order to insertion order.
    public interface IList<T> : ICollection<T>
    {
        // The Item property provides methods to read and edit entries in the List.
        T this[int index]
        {
            get;
            set;
        }

        // Returns the index of a particular item, if it is in the list.
        // Returns -1 if the item isn't in the list.
        int IndexOf(T item);

        // Inserts value into the list at position index.
        // index must be non-negative and less than or equal to the
        // number of elements in the list.  If index equals the number
        // of items in the list, then value is appended to the end.
        void Insert(int index, T item);

        // Removes the item at position index.
        void RemoveAt(int index);
    }
    public abstract class Comparer<T> : IComparer, IComparer<T>
    {
        public static Comparer<T> Default { get; } = new ObjectComparer<T>();

        public abstract int Compare(T? x, T? y);

        int IComparer.Compare(object? x, object? y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;
            if (x is T && y is T) return Compare((T)x, (T)y);
            ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArgumentForComparison);
            return 0;
        }
    }
    public sealed class ObjectComparer<T> : Comparer<T>
    {
        public override int Compare(T? x, T? y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;
            if (x is IComparable<T> generic) return generic.CompareTo(y);
            if (x is IComparable comparable) return comparable.CompareTo(y);
            throw new ArgumentException();
        }
    }

    public class List<T> : IList<T>, IList, IEnumerable<T>, IEnumerable, IReadOnlyList<T>
    {
        private const int DefaultCapacity = 4;

        internal T[] _items; // Do not rename (binary serialization)
        internal int _size; // Do not rename (binary serialization)
        internal int _version; // Do not rename (binary serialization)

        // avoid the extra generic instantiation for Array.Empty<T>()
        private static readonly T[] s_emptyArray = new T[0];

        // Constructs a List. The list is initially empty and has a capacity
        // of zero. Upon adding the first element to the list the capacity is
        // increased to DefaultCapacity, and then increased in multiples of two
        // as required.
        public List()
        {
            _items = s_emptyArray;
        }

        // Constructs a List with a given initial capacity. The list is
        // initially empty, but will have room for the given number of elements
        // before any reallocations are required.
        //
        public List(int capacity)
        {
            if (capacity < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);

            if (capacity == 0)
                _items = s_emptyArray;
            else
                _items = new T[capacity];
        }

        // Constructs a List, copying the contents of the given collection. The
        // size and capacity of the new list will both be equal to the size of the
        // given collection.
        //
        public List(IEnumerable<T> collection)
        {
            if (collection == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);

            if (collection is ICollection<T> c)
            {
                int count = c.Count;
                if (count == 0)
                {
                    _items = s_emptyArray;
                }
                else
                {
                    _items = new T[count];
                    c.CopyTo(_items, 0);
                    _size = count;
                }
            }
            else
            {
                _items = s_emptyArray;
                using (IEnumerator<T> en = collection!.GetEnumerator())
                {
                    while (en.MoveNext())
                    {
                        Add(en.Current);
                    }
                }
            }
        }

        // Gets and sets the capacity of this list.  The capacity is the size of
        // the internal array used to hold items.  When set, the internal
        // array of the list is reallocated to the given capacity.
        //
        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.value, ExceptionResource.ArgumentOutOfRange_SmallCapacity);
                }

                if (value != _items.Length)
                {
                    if (value > 0)
                    {
                        T[] newItems = new T[value];
                        if (_size > 0)
                        {
                            Array.Copy(_items, newItems, _size);
                        }
                        _items = newItems;
                    }
                    else
                    {
                        _items = s_emptyArray;
                    }
                }
            }
        }
        
        // Read-only property describing how many elements are in the List.
        public int Count => _size;

        bool IList.IsFixedSize => false;

        // Is this List read-only?
        bool ICollection<T>.IsReadOnly => false;

        bool IList.IsReadOnly => false;

        // Is this List synchronized (thread-safe)?
        bool ICollection.IsSynchronized => false;

        // Synchronization root for this object.
        object ICollection.SyncRoot => this;

        // Sets or Gets the element at the given index.
        public T this[int index]
        {
            get
            {
                // Following trick can reduce the range check by one
                if ((uint)index >= (uint)_size)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessException();
                }
                return _items[index];
            }

            set
            {
                if ((uint)index >= (uint)_size)
                {
                    ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessException();
                }
                _items[index] = value;
                _version++;
            }
        }

        private static bool IsCompatibleObject(object? value)
        {
            // Non-null values are fine.  Only accept nulls if T is a class or Nullable<U>.
            // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
            return (value is T) || (value == null && default(T) == null);
        }

        object? IList.this[int index]
        {
            get => this[index];
            set
            {
                ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, ExceptionArgument.value);

                try
                {
                    this[index] = (T)value!;
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(T));
                }
            }
        }

        // Adds the given object to the end of this list. The size of the list is
        // increased by one. If required, the capacity of the list is doubled
        // before adding the new element.
        //
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            _version++;
            T[] array = _items;
            int size = _size;
            if ((uint)size < (uint)array.Length)
            {
                _size = size + 1;
                array[size] = item;
            }
            else
            {
                AddWithResize(item);
            }
        }

        // Non-inline from List.Add to improve its code quality as uncommon path
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            int size = _size;
            Grow(size + 1);
            _size = size + 1;
            _items[size] = item;
        }

        int IList.Add(object? item)
        {
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(item, ExceptionArgument.item);

            try
            {
                Add((T)item!);
            }
            catch (InvalidCastException)
            {
                ThrowHelper.ThrowWrongValueTypeArgumentException(item, typeof(T));
            }

            return Count - 1;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            private readonly List<T> _list;
            private readonly int _version;

            private int _index;
            private T _current;

            internal Enumerator(List<T> list)
            {
                _list = list;
                _version = list._version;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                List<T> localList = _list;

                if (_version != _list._version)
                {
                    throw new InvalidOperationException();
                }

                if ((uint)_index < (uint)localList._size)
                {
                    _current = localList._items[_index];
                    _index++;
                    return true;
                }

                _current = default;
                _index = -1;
                return false;
            }
            public T Current => _current;

            object? IEnumerator.Current
            {
                get
                {
                    if (_index <= 0)
                    {
                        throw new InvalidOperationException();
                    }

                    return _current;
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _list._version)
                {
                    throw new InvalidOperationException();
                }

                _index = 0;
                _current = default;
            }
        }

        internal void Grow(int capacity)
        {
            Capacity = GetNewCapacity(capacity);
        }
        internal void GrowForInsertion(int indexToInsert, int insertionCount = 1)
        {
            int requiredCapacity = checked(_size + insertionCount);
            int newCapacity = GetNewCapacity(requiredCapacity);

            // Inline and adapt logic from set_Capacity

            T[] newItems = new T[newCapacity];
            if (indexToInsert != 0)
            {
                Array.Copy(_items, newItems, indexToInsert);
            }

            if (_size != indexToInsert)
            {
                Array.Copy(_items, indexToInsert, newItems, indexToInsert + insertionCount, _size - indexToInsert);
            }

            _items = newItems;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetNewCapacity(int capacity)
        {
            int newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;

            if ((uint)newCapacity > Array.MaxLength) newCapacity = Array.MaxLength;

            if (newCapacity < capacity) newCapacity = capacity;

            return newCapacity;
        }

        public bool Exists(Predicate<T> match)
            => FindIndex(match) != -1;

        public T? Find(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException();
            }

            for (int i = 0; i < _size; i++)
            {
                if (match(_items[i]))
                {
                    return _items[i];
                }
            }
            return default;
        }

        public List<T> FindAll(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException();
            }

            List<T> list = new List<T>();
            for (int i = 0; i < _size; i++)
            {
                if (match(_items[i]))
                {
                    list.Add(_items[i]);
                }
            }
            return list;
        }

        public int FindIndex(Predicate<T> match)
            => FindIndex(0, _size, match);

        public int FindIndex(int startIndex, Predicate<T> match)
            => FindIndex(startIndex, _size - startIndex, match);

        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            if ((uint)startIndex > (uint)_size)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (count < 0 || startIndex > _size - count)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (match == null)
            {
                throw new ArgumentNullException();
            }

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                if (match(_items[i])) return i;
            }
            return -1;
        }

        public T? FindLast(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException();
            }

            for (int i = _size - 1; i >= 0; i--)
            {
                if (match(_items[i]))
                {
                    return _items[i];
                }
            }
            return default;
        }

        public int FindLastIndex(Predicate<T> match)
            => FindLastIndex(_size - 1, _size, match);

        public int FindLastIndex(int startIndex, Predicate<T> match)
            => FindLastIndex(startIndex, startIndex + 1, match);

        public int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException();
            }

            if (_size == 0)
            {
                // Special case for 0 length List
                if (startIndex != -1)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                // Make sure we're not out of range
                if ((uint)startIndex >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }

            // 2nd have of this also catches when startIndex == MAXINT, so MAXINT - 0 + 1 == -1, which is < 0.
            if (count < 0 || startIndex - count + 1 < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            int endIndex = startIndex - count;
            for (int i = startIndex; i > endIndex; i--)
            {
                if (match(_items[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        public void ForEach(Action<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException();
            }

            int version = _version;

            for (int i = 0; i < _size; i++)
            {
                if (version != _version)
                {
                    break;
                }
                action(_items[i]);
            }

            if (version != _version)
                throw new InvalidOperationException();
        }

        // Inserts an element into this list at a given index. The size of the list
        // is increased by one. If required, the capacity of the list is doubled
        // before inserting the new element.
        //
        public void Insert(int index, T item)
        {
            // Note that insertions at the end are legal.
            if ((uint)index > (uint)_size)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (_size == _items.Length)
            {
                GrowForInsertion(index, 1);
            }
            else if (index < _size)
            {
                Array.Copy(_items, index, _items, index + 1, _size - index);
            }
            _items[index] = item;
            _size++;
            _version++;
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (collection is null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);

            if (collection is ICollection<T> c)
            {
                int count = c.Count;
                if (count > 0)
                {
                    if (_items.Length - _size < count)
                    {
                        Grow(checked(_size + count));
                    }

                    c.CopyTo(_items, _size);
                    _size += count;
                    _version++;
                }
            }
            else
            {
                using (IEnumerator<T> en = collection.GetEnumerator())
                {
                    while (en.MoveNext())
                    {
                        Add(en.Current);
                    }
                }
            }
        }

        // ToArray returns an array containing the contents of the List.
        // This requires copying the List, which is an O(n) operation.
        public T[] ToArray()
        {
            if (_size == 0)
            {
                return Array.Empty<T>();
            }

            T[] array = new T[_size];
            Array.Copy(_items, array, _size);
            return array;
        }

        // This method removes all items which matches the predicate.
        // The complexity is O(n).
        public int RemoveAll(Predicate<T> match)
        {
            if (match is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.match);
            }

            int freeIndex = 0;   // the first free slot in items array

            // Find the first item which needs to be removed.
            while (freeIndex < _size && !match(_items[freeIndex])) freeIndex++;
            if (freeIndex >= _size) return 0;

            int current = freeIndex + 1;
            while (current < _size)
            {
                // Find the first item which needs to be kept.
                while (current < _size && match(_items[current])) current++;

                if (current < _size)
                {
                    // copy item to the free slot.
                    _items[freeIndex++] = _items[current++];
                }
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(_items, freeIndex, _size - freeIndex); // Clear the elements so that the gc can reclaim the references.
            }

            int result = _size - freeIndex;
            _size = freeIndex;
            _version++;
            return result;
        }

        int IList.IndexOf(object? item)
        {
            if (IsCompatibleObject(item))
            {
                return IndexOf((T)item!);
            }
            return -1;
        }

        void IList.Insert(int index, object? item)
        {
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(item, ExceptionArgument.item);

            try
            {
                Insert(index, (T)item!);
            }
            catch (InvalidCastException)
            {
                ThrowHelper.ThrowWrongValueTypeArgumentException(item, typeof(T));
            }
        }

        public int IndexOf(T item)
            => Array.IndexOf<T>(_items, item, 0, _size);

        // Contains returns true if the specified element is in the List.
        // It does a linear, O(n) search.  Equality is determined by calling
        // EqualityComparer<T>.Default.Equals().
        //
        public bool Contains(T item)
        {
            return _size != 0 && IndexOf(item) >= 0;
        }

        bool IList.Contains(object? item)
        {
            if (IsCompatibleObject(item))
            {
                return Contains((T)item!);
            }
            return false;
        }

        // Copies this List into array, which must be of a
        // compatible array type.
        public void CopyTo(T[] array)
            => CopyTo(array, 0);

        // Copies this List into array, which must be of a
        // compatible array type.
        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            if ((array != null) && (array.Rank != 1))
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
            }

            try
            {
                // Array.Copy will check for NULL.
                Array.Copy(_items, 0, array!, arrayIndex, _size);
            }
            catch (ArrayTypeMismatchException)
            {
                ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
            }
        }

        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            if (_size - index < count)
            {
                throw new ArgumentException();
            }

            // Delegate rest of error checking to Array.Copy.
            Array.Copy(_items, index, array, arrayIndex, count);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            // Delegate rest of error checking to Array.Copy.
            Array.Copy(_items, 0, array, arrayIndex, _size);
        }

        // Clears the contents of List.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _version++;
            if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                int size = _size;
                _size = 0;
                if (size > 0)
                {
                    Array.Clear(_items, 0, size); // Clear the elements so that the gc can reclaim the references.
                }
            }
            else
            {
                _size = 0;
            }
        }

        // Removes the first occurrence of the given element, if found.
        // The size of the list is decreased by one if successful.
        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        void IList.Remove(object? item)
        {
            if (IsCompatibleObject(item))
            {
                Remove((T)item!);
            }
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size)
            {
                throw new ArgumentOutOfRangeException();
            }
            _size--;
            if (index < _size)
            {
                Array.Copy(_items, index + 1, _items, index, _size - index);
            }
            if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _items[_size] = default;
            }
            _version++;
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (_size - index < count)
                throw new ArgumentOutOfRangeException();

            if (count > 0)
            {
                _size -= count;
                if (index < _size)
                {
                    Array.Copy(_items, index + count, _items, index, _size - index);
                }

                _version++;
                if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    Array.Clear(_items, _size, count);
                }
            }
        }
    }
    // An IDictionary is a possibly unordered set of key-value pairs.
    // Keys can be any non-null object.  Values can be any object.
    // You can look up a value in an IDictionary via the default indexed
    // property, Items.
    public interface IDictionary<TKey, TValue> : ICollection<KeyValuePair<TKey, TValue>>
    {
        // Interfaces are not serializable
        // The Item property provides methods to read and edit entries
        // in the Dictionary.
        TValue this[TKey key]
        {
            get;
            set;
        }

        // Returns a collections of the keys in this dictionary.
        ICollection<TKey> Keys
        {
            get;
        }

        // Returns a collections of the values in this dictionary.
        ICollection<TValue> Values
        {
            get;
        }

        // Returns whether this dictionary contains a particular key.
        //
        bool ContainsKey(TKey key);

        // Adds a key-value pair to the dictionary.
        //
        void Add(TKey key, TValue value);

        // Removes a particular key from the dictionary.
        //
        bool Remove(TKey key);

        bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value);
    }
    // Provides a read-only view of a generic dictionary.
    public interface IReadOnlyDictionary<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    {
        bool ContainsKey(TKey key);
        bool TryGetValue(TKey key, out TValue value);

        TValue this[TKey key] { get; }
        IEnumerable<TKey> Keys { get; }
        IEnumerable<TValue> Values { get; }
    }
    /// <summary>
    /// Generic collection that guarantees the uniqueness of its elements, as defined
    /// by some comparer. It also supports basic set operations such as Union, Intersection,
    /// Complement and Exclusive Complement.
    /// </summary>
    public interface ISet<T> : ICollection<T>
    {
        //Add ITEM to the set, return true if added, false if duplicate
        new bool Add(T item);

        //Transform this set into its union with the IEnumerable<T> other
        void UnionWith(IEnumerable<T> other);

        //Transform this set into its intersection with the IEnumerable<T> other
        void IntersectWith(IEnumerable<T> other);

        //Transform this set so it contains no elements that are also in other
        void ExceptWith(IEnumerable<T> other);

        //Transform this set so it contains elements initially in this or in other, but not both
        void SymmetricExceptWith(IEnumerable<T> other);

        //Check if this set is a subset of other
        bool IsSubsetOf(IEnumerable<T> other);

        //Check if this set is a superset of other
        bool IsSupersetOf(IEnumerable<T> other);

        //Check if this set is a subset of other, but not the same as it
        bool IsProperSupersetOf(IEnumerable<T> other);

        //Check if this set is a superset of other, but not the same as it
        bool IsProperSubsetOf(IEnumerable<T> other);

        //Check if this set has any elements in common with other
        bool Overlaps(IEnumerable<T> other);

        //Check if this set contains the same and only the same elements as other
        bool SetEquals(IEnumerable<T> other);
    }
    /// <summary>
    /// Provides a readonly abstraction of a set.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public interface IReadOnlySet<T> : IReadOnlyCollection<T>
    {
        /// <summary>
        /// Determines if the set contains a specific item
        /// </summary>
        /// <param name="item">The item to check if the set contains.</param>
        /// <returns><see langword="true" /> if found; otherwise <see langword="false" />.</returns>
        bool Contains(T item);
        /// <summary>
        /// Determines whether the current set is a proper (strict) subset of a specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns><see langword="true" /> if the current set is a proper subset of other; otherwise <see langword="false" />.</returns>
        /// <exception cref="ArgumentNullException">other is <see langword="null" />.</exception>
        bool IsProperSubsetOf(IEnumerable<T> other);
        /// <summary>
        /// Determines whether the current set is a proper (strict) superset of a specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns><see langword="true" /> if the collection is a proper superset of other; otherwise <see langword="false" />.</returns>
        /// <exception cref="ArgumentNullException">other is <see langword="null" />.</exception>
        bool IsProperSupersetOf(IEnumerable<T> other);
        /// <summary>
        /// Determine whether the current set is a subset of a specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns><see langword="true" /> if the current set is a subset of other; otherwise <see langword="false" />.</returns>
        /// <exception cref="ArgumentNullException">other is <see langword="null" />.</exception>
        bool IsSubsetOf(IEnumerable<T> other);
        /// <summary>
        /// Determine whether the current set is a super set of a specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current set</param>
        /// <returns><see langword="true" /> if the current set is a subset of other; otherwise <see langword="false" />.</returns>
        /// <exception cref="ArgumentNullException">other is <see langword="null" />.</exception>
        bool IsSupersetOf(IEnumerable<T> other);
        /// <summary>
        /// Determines whether the current set overlaps with the specified collection.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns><see langword="true" /> if the current set and other share at least one common element; otherwise, <see langword="false" />.</returns>
        /// <exception cref="ArgumentNullException">other is <see langword="null" />.</exception>
        bool Overlaps(IEnumerable<T> other);
        /// <summary>
        /// Determines whether the current set and the specified collection contain the same elements.
        /// </summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns><see langword="true" /> if the current set is equal to other; otherwise, <see langword="false" />.</returns>
        /// <exception cref="ArgumentNullException">other is <see langword="null" />.</exception>
        bool SetEquals(IEnumerable<T> other);
    }
    public interface IEqualityComparer<in T> where T : allows ref struct
    {
        bool Equals(T? x, T? y);
        int GetHashCode(T obj);
    }
    internal interface IInternalStringEqualityComparer : IEqualityComparer<string?>
    {
        IEqualityComparer<string?> GetUnderlyingEqualityComparer();

        /// <summary>
        /// Unwraps the internal equality comparer, if proxied.
        /// Otherwise returns the equality comparer itself or its default equivalent.
        /// </summary>
        internal static IEqualityComparer<string?> GetUnderlyingEqualityComparer(IEqualityComparer<string?> outerComparer)
        {
            if (outerComparer is IInternalStringEqualityComparer internalComparer)
            {
                return internalComparer.GetUnderlyingEqualityComparer();
            }
            else
            {
                return outerComparer;
            }
        }
    }
    public abstract class EqualityComparer<T> : IEqualityComparer, IEqualityComparer<T>
    {
        public static EqualityComparer<T> Default { [Intrinsic] get; } = Create();

        [Intrinsic]
        private static EqualityComparer<T> Create()
        {
            // The compiler will overwrite the Create method with optimized
            // instantiation-specific implementation.
            // This body serves as a fallback when instantiation-specific implementation is unavailable.
            // If that happens, the compiler ensures we generate data structures to make the fallback work
            // when this method is compiled.

            if (typeof(T) == typeof(string))
            {
                return Unsafe.As<EqualityComparer<T>>(new StringEqualityComparer());
            }

            //return Unsafe.As<EqualityComparer<T>>(EqualityComparerHelpers.GetComparer(typeof(T).TypeHandle));
            return new ObjectEqualityComparer<T>();
        }

        public static EqualityComparer<T> Create(Func<T?, T?, bool> equals, Func<T, int>? getHashCode = null)
        {
            if (equals == null) throw new ArgumentNullException();

            getHashCode ??= _ => throw new NotSupportedException();

            return new DelegateEqualityComparer<T>(equals, getHashCode);
        }

        public abstract bool Equals(T? x, T? y);
        public abstract int GetHashCode(T obj);

        int IEqualityComparer.GetHashCode(object? obj)
        {
            if (obj == null) return 0;
            if (obj is T) return GetHashCode((T)obj);
            throw new ArgumentException();
            return 0;
        }

        bool IEqualityComparer.Equals(object? x, object? y)
        {
            if (x == y) return true;
            if (x == null || y == null) return false;
            if ((x is T) && (y is T)) return Equals((T)x, (T)y);
            throw new ArgumentException();
            return false;
        }
    }
    /// <summary>
    /// An <see cref="IEqualityComparer{Object}"/> that uses reference equality
    /// (<see cref="object.ReferenceEquals(object?, object?)"/>) instead of value equality
    /// (<see cref="object.Equals(object?)"/>) when comparing two object instances.
    /// </summary>
    public sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, IEqualityComparer
    {
        private ReferenceEqualityComparer() { }

        /// <summary>Gets the singleton <see cref="ReferenceEqualityComparer"/> instance.</summary>
        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj!);
    }
    internal abstract class RandomizedStringEqualityComparer : EqualityComparer<string?>, IInternalStringEqualityComparer
    {
        private readonly MarvinSeed _seed;
        private readonly IEqualityComparer<string?> _underlyingComparer;

        private unsafe RandomizedStringEqualityComparer(IEqualityComparer<string?> underlyingComparer)
        {
            _underlyingComparer = underlyingComparer;

            fixed (MarvinSeed* seed = &_seed)
            {
                //Interop.GetRandomBytes((byte*)seed, sizeof(MarvinSeed));
            }
        }

        internal static RandomizedStringEqualityComparer Create(IEqualityComparer<string?> underlyingComparer, bool ignoreCase)
        {
            if (!ignoreCase)
            {
                return new OrdinalComparer(underlyingComparer);
            }
            else
            {
                return new OrdinalIgnoreCaseComparer(underlyingComparer);
            }
        }

        public IEqualityComparer<string?> GetUnderlyingEqualityComparer() => _underlyingComparer;

        private struct MarvinSeed
        {
            internal uint p0;
            internal uint p1;
        }

        private sealed class OrdinalComparer : RandomizedStringEqualityComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalComparer(IEqualityComparer<string?> wrappedComparer)
                    : base(wrappedComparer)
            {
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(ReadOnlySpan<char> span) =>
                span.ToString();

            public override bool Equals(string? x, string? y) => string.Equals(x, y);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(ReadOnlySpan<char> alternate, string? other)
            {
                // See explanation in System.OrdinalComparer.Equals.
                if (alternate.IsEmpty && other is null)
                {
                    return false;
                }

                return alternate.SequenceEqual(other);
            }

            public override int GetHashCode(string? obj)
            {
                if (obj is null)
                {
                    return 0;
                }

                // The Ordinal version of Marvin32 operates over bytes.
                // The multiplication from # chars -> # bytes will never integer overflow.
                return Marvin.ComputeHash32(
                    ref obj.GetRawStringDataAsUInt8(),
                    (uint)obj.Length * 2,
                    _seed.p0, _seed.p1);
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(ReadOnlySpan<char> alternate) =>
                Marvin.ComputeHash32(
                    ref Unsafe.As<char, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(alternate)),
                    (uint)alternate.Length * 2,
                    _seed.p0, _seed.p1);
        }
        private sealed class OrdinalIgnoreCaseComparer : RandomizedStringEqualityComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalIgnoreCaseComparer(IEqualityComparer<string?> wrappedComparer)
                : base(wrappedComparer)
            {
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(ReadOnlySpan<char> span) =>
                span.ToString();

            public override bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(ReadOnlySpan<char> alternate, string? other)
            {
                if (alternate.IsEmpty && other is null)
                {
                    return false;
                }

                return alternate.EqualsOrdinalIgnoreCase(other);
            }

            public override int GetHashCode(string? obj)
            {
                if (obj is null)
                {
                    return 0;
                }

                // The OrdinalIgnoreCase version of Marvin32 operates over chars,
                // so pass in the char count directly.
                return Marvin.ComputeHash32OrdinalIgnoreCase(
                    ref obj.GetRawStringData(),
                    obj.Length,
                    _seed.p0, _seed.p1);
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(ReadOnlySpan<char> alternate) =>
                Marvin.ComputeHash32OrdinalIgnoreCase(
                    ref System.Runtime.InteropServices.MemoryMarshal.GetReference(alternate),
                    alternate.Length,
                    _seed.p0, _seed.p1);
        }
    }
    public class NonRandomizedStringEqualityComparer : EqualityComparer<string?>, IInternalStringEqualityComparer
    {
        // Dictionary<...>.Comparer and similar methods need to return the original IEqualityComparer
        // that was passed in to the ctor. The caller chooses one of these singletons so that the
        // GetUnderlyingEqualityComparer method can return the correct value.

        private static readonly NonRandomizedStringEqualityComparer WrappedAroundDefaultComparer = new OrdinalComparer(EqualityComparer<string?>.Default);
        private static readonly NonRandomizedStringEqualityComparer WrappedAroundStringComparerOrdinal = new OrdinalComparer(StringComparer.Ordinal);
        private static readonly NonRandomizedStringEqualityComparer WrappedAroundStringComparerOrdinalIgnoreCase = new OrdinalIgnoreCaseComparer(StringComparer.OrdinalIgnoreCase);

        private readonly IEqualityComparer<string?> _underlyingComparer;

        private NonRandomizedStringEqualityComparer(IEqualityComparer<string?> underlyingComparer)
        {
            _underlyingComparer = underlyingComparer;
        }

        public override bool Equals(string? x, string? y)
        {
            // This instance may have been deserialized into a class that doesn't guarantee
            // these parameters are non-null. Can't short-circuit the null checks.

            return string.Equals(x, y);
        }

        public override int GetHashCode(string? obj)
        {
            // This instance may have been deserialized into a class that doesn't guarantee
            // these parameters are non-null. Can't short-circuit the null checks.

            return obj?.GetNonRandomizedHashCode() ?? 0;
        }

        internal virtual RandomizedStringEqualityComparer GetRandomizedEqualityComparer()
        {
            return RandomizedStringEqualityComparer.Create(_underlyingComparer, ignoreCase: false);
        }

        // Gets the comparer that should be returned back to the caller when querying the
        // ICollection.Comparer property. Also used for serialization purposes.
        public virtual IEqualityComparer<string?> GetUnderlyingEqualityComparer() => _underlyingComparer;

        private sealed class OrdinalComparer : NonRandomizedStringEqualityComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalComparer(IEqualityComparer<string?> wrappedComparer) : base(wrappedComparer)
            {
            }

            public override bool Equals(string? x, string? y) => string.Equals(x, y);

            public override int GetHashCode(string? obj)
            {
                return obj.GetNonRandomizedHashCode();
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(ReadOnlySpan<char> span) =>
                string.GetNonRandomizedHashCode(span);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(ReadOnlySpan<char> alternate, string? other)
            {
                if (alternate.IsEmpty && other is null)
                {
                    return false;
                }

                return alternate.SequenceEqual(other);
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(ReadOnlySpan<char> span) =>
                span.ToString();
        }

        private sealed class OrdinalIgnoreCaseComparer : NonRandomizedStringEqualityComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalIgnoreCaseComparer(IEqualityComparer<string?> wrappedComparer) : base(wrappedComparer)
            {
            }

            public override bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            public override int GetHashCode(string? obj) => obj is null ? 0 : obj.GetHashCodeOrdinalIgnoreCase();

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(ReadOnlySpan<char> span) =>
                string.GetHashCodeOrdinalIgnoreCase(span);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(ReadOnlySpan<char> alternate, string? other)
            {
                if (alternate.IsEmpty && other is null)
                {
                    return false;
                }

                return alternate.EqualsOrdinalIgnoreCase(other);
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(ReadOnlySpan<char> span) =>
                span.ToString();

            internal override RandomizedStringEqualityComparer GetRandomizedEqualityComparer()
            {
                return RandomizedStringEqualityComparer.Create(_underlyingComparer, ignoreCase: true);
            }
        }

        public static IEqualityComparer<string>? GetStringComparer(object comparer)
        {
            // Special-case EqualityComparer<string>.Default, StringComparer.Ordinal, and StringComparer.OrdinalIgnoreCase.
            // We use a non-randomized comparer for improved perf, falling back to a randomized comparer if the
            // hash buckets become unbalanced.

            if (ReferenceEquals(comparer, EqualityComparer<string>.Default))
            {
                return WrappedAroundDefaultComparer;
            }

            if (ReferenceEquals(comparer, StringComparer.Ordinal))
            {
                return WrappedAroundStringComparerOrdinal;
            }

            if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
            {
                return WrappedAroundStringComparerOrdinalIgnoreCase;
            }

            return null;
        }
    }
    internal sealed class DelegateEqualityComparer<T> : EqualityComparer<T>
    {
        private readonly Func<T?, T?, bool> _equals;
        private readonly Func<T, int> _getHashCode;

        public DelegateEqualityComparer(Func<T?, T?, bool> equals, Func<T, int> getHashCode)
        {
            _equals = equals;
            _getHashCode = getHashCode;
        }

        public override bool Equals(T? x, T? y) =>
            _equals(x, y);

        public override int GetHashCode(T obj) =>
            _getHashCode(obj);

        public override bool Equals(object? obj) =>
            obj is DelegateEqualityComparer<T> other &&
            _equals == other._equals &&
            _getHashCode == other._getHashCode;
    }
    public sealed class GenericEqualityComparer<T> : EqualityComparer<T> where T : IEquatable<T>?
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T? x, T? y)
        {
            if (x != null)
            {
                if (y != null) return x.Equals(y);
                return false;
            }
            if (y != null) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(T obj) =>
            obj?.GetHashCode() ?? 0;
    }
    public sealed class NullableEqualityComparer<T> : EqualityComparer<T?> where T : struct
    {
        public NullableEqualityComparer() { }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T? x, T? y)
        {
            if (x.HasValue)
            {
                if (y.HasValue) return EqualityComparer<T>.Default.Equals(x.value, y.value);
                return false;
            }
            if (y.HasValue) return false;
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(T? obj) =>
            obj.GetHashCode();
    }
    public sealed class ObjectEqualityComparer<T> : EqualityComparer<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T? x, T? y)
        {
            if (x != null)
            {
                if (y != null) return x.Equals(y);
                return false;
            }
            if (y != null) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(T obj) =>
            obj?.GetHashCode() ?? 0;
    }
    public sealed class EnumEqualityComparer<T> : EqualityComparer<T> where T : struct, Enum
    {
        public EnumEqualityComparer() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T x, T y)
        {
            return RuntimeHelpers.EnumEquals(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(T obj) =>
            obj.GetHashCode();
    }
    internal sealed class StringEqualityComparer : EqualityComparer<string>
    {
        public override bool Equals(string? x, string? y) => string.Equals(x, y);

        public override int GetHashCode(string obj) => obj?.GetHashCode() ?? 0;

        public override bool Equals(object? obj) => obj is StringEqualityComparer;
    }
    /// <summary>Equality comparer for hashsets of hashsets</summary>
    internal sealed class HashSetEqualityComparer<T> : IEqualityComparer<HashSet<T>?>
    {
        public bool Equals(HashSet<T>? x, HashSet<T>? y)
        {
            // If they're the exact same instance, they're equal.
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            // They're not both null, so if either is null, they're not equal.
            if (x == null || y == null)
            {
                return false;
            }

            EqualityComparer<T> defaultComparer = EqualityComparer<T>.Default;

            // If both sets use the same comparer, they're equal if they're the same
            // size and one is a "subset" of the other.
            if (HashSet<T>.EqualityComparersAreEqual(x, y))
            {
                return x.Count == y.Count && y.IsSubsetOfHashSetWithSameComparer(x);
            }

            // Otherwise, do an O(N^2) match.
            foreach (T yi in y)
            {
                bool found = false;
                foreach (T xi in x)
                {
                    if (defaultComparer.Equals(yi, xi))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(HashSet<T>? obj)
        {
            int hashCode = 0; // default to 0 for null/empty set

            if (obj != null)
            {
                foreach (T t in obj)
                {
                    if (t != null)
                    {
                        hashCode ^= t.GetHashCode(); // same hashcode as default comparer
                    }
                }
            }

            return hashCode;
        }

        // Equals method for the comparer itself.
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is HashSetEqualityComparer<T>;

        public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode();
    }
    public static class KeyValuePair
    {
        // Creates a new KeyValuePair<TKey, TValue> from the given values.
        public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value) =>
            new KeyValuePair<TKey, TValue>(key, value);

        internal static string PairToString(object? key, object? value)
        {
            return $"[{key}, {value}]";
        }
    }
    public struct KeyValuePair<TKey, TValue>
    {
        private readonly TKey key; // Do not rename
        private readonly TValue value; // Do not rename

        public KeyValuePair(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
        }

        public TKey Key => key;

        public TValue Value => value;

        public override string ToString()
        {
            return KeyValuePair.PairToString(Key, Value);
        }

        public void Deconstruct(out TKey key, out TValue value)
        {
            key = Key;
            value = Value;
        }
    }
    /// <summary>
    /// Used internally to control behavior of insertion into a <see cref="Dictionary{TKey, TValue}"/> or <see cref="HashSet{T}"/>.
    /// </summary>
    internal enum InsertionBehavior : byte
    {
        /// <summary>
        /// The default insertion behavior.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies that an existing entry with the same key should be overwritten if encountered.
        /// </summary>
        OverwriteExisting = 1,

        /// <summary>
        /// Specifies that if an existing entry with the same key is encountered, an exception should be thrown.
        /// </summary>
        ThrowOnExisting = 2
    }
    public class Dictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue> where TKey : notnull
    {
        // constants for serialization
        private const string VersionName = "Version"; // Do not rename
        private const string HashSizeName = "HashSize"; // Do not rename
        private const string KeyValuePairsName = "KeyValuePairs"; // Do not rename
        private const string ComparerName = "Comparer"; // Do not rename

        private int[]? _buckets;
        private Entry[]? _entries;
#if TARGET_64BIT
        private ulong _fastModMultiplier;
#endif
        private int _count;
        private int _freeList;
        private int _freeCount;
        private int _version;
        private IEqualityComparer<TKey>? _comparer;
        private KeyCollection? _keys;
        private ValueCollection? _values;
        private const int StartOfFreeList = -3;

        public Dictionary() : this(0, null) { }

        public Dictionary(int capacity) : this(capacity, null) { }

        public Dictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }

        public Dictionary(int capacity, IEqualityComparer<TKey>? comparer)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }
            if (capacity > 0)
            {
                Initialize(capacity);
            }

            // For reference types, we always want to store a comparer instance, either
            // the one provided, or if one wasn't provided, the default (accessing
            // EqualityComparer<TKey>.Default with shared generics on every dictionary
            // access can add measurable overhead).  For value types, if no comparer is
            // provided, or if the default is provided, we'd prefer to use
            // EqualityComparer<TKey>.Default.Equals on every use, enabling the compiler to
            // devirtualize and possibly inline the operation.
            if (!typeof(TKey).IsValueType)
            {
                _comparer = comparer ?? EqualityComparer<TKey>.Default;

                // Special-case EqualityComparer<string>.Default, StringComparer.Ordinal, and StringComparer.OrdinalIgnoreCase.
                // We use a non-randomized comparer for improved perf, falling back to a randomized comparer if the
                // hash buckets become unbalanced.
                if (typeof(TKey) == typeof(string) &&
                    NonRandomizedStringEqualityComparer.GetStringComparer(_comparer!) is IEqualityComparer<string> stringComparer)
                {
                    _comparer = (IEqualityComparer<TKey>)stringComparer;
                }
            }
            else if (comparer is not null && // first check for null to avoid forcing default comparer instantiation unnecessarily
                     comparer != EqualityComparer<TKey>.Default)
            {
                _comparer = comparer;
            }
        }

        public Dictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        public Dictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer) :
            this(dictionary?.Count ?? 0, comparer)
        {
            if (dictionary == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
            }

            AddRange(dictionary);
        }

        public Dictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        public Dictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) :
            this((collection as ICollection<KeyValuePair<TKey, TValue>>)?.Count ?? 0, comparer)
        {
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
            }

            AddRange(collection);
        }

        private void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> enumerable)
        {
            // It is likely that the passed-in enumerable is Dictionary<TKey,TValue>. When this is the case,
            // avoid the enumerator allocation and overhead by looping through the entries array directly.
            // We only do this when dictionary is Dictionary<TKey,TValue> and not a subclass, to maintain
            // back-compat with subclasses that may have overridden the enumerator behavior.
            if (enumerable.GetType() == typeof(Dictionary<TKey, TValue>))
            {
                Dictionary<TKey, TValue> source = (Dictionary<TKey, TValue>)enumerable;

                if (source.Count == 0)
                {
                    // Nothing to copy, all done
                    return;
                }

                Entry[] oldEntries = source._entries;
                if (source._comparer == _comparer)
                {
                    // If comparers are the same, we can copy _entries without rehashing.
                    CopyEntries(oldEntries, source._count);
                    return;
                }

                // Comparers differ need to rehash all the entries via Add
                int count = source._count;
                for (int i = 0; i < count; i++)
                {
                    // Only copy if an entry
                    if (oldEntries[i].next >= -1)
                    {
                        Add(oldEntries[i].key, oldEntries[i].value);
                    }
                }
                return;
            }

            // We similarly special-case KVP<>[] and List<KVP<>>, as they're commonly used to seed dictionaries, and
            // we want to avoid the enumerator costs (e.g. allocation) for them as well. Extract a span if possible.
            ReadOnlySpan<KeyValuePair<TKey, TValue>> span;
            if (enumerable is KeyValuePair<TKey, TValue>[] array)
            {
                span = array;
            }
            else if (enumerable.GetType() == typeof(List<KeyValuePair<TKey, TValue>>))
            {
                span = CollectionsMarshal.AsSpan((List<KeyValuePair<TKey, TValue>>)enumerable);
            }
            else
            {
                // Fallback path for all other enumerables
                foreach (KeyValuePair<TKey, TValue> pair in enumerable)
                {
                    Add(pair.Key, pair.Value);
                }
                return;
            }

            // We got a span. Add the elements to the dictionary.
            foreach (KeyValuePair<TKey, TValue> pair in span)
            {
                Add(pair.Key, pair.Value);
            }
        }

        public IEqualityComparer<TKey> Comparer
        {
            get
            {
                if(typeof(TKey) == typeof(string))
                {
                    return (IEqualityComparer<TKey>)IInternalStringEqualityComparer.GetUnderlyingEqualityComparer((IEqualityComparer<string?>)_comparer);
                }
                else
                {
                    return _comparer ?? EqualityComparer<TKey>.Default;
                }
            }
        }

        public int Count => _count - _freeCount;

        /// <summary>
        /// Gets the total numbers of elements the internal data structure can hold without resizing.
        /// </summary>
        public int Capacity => _entries?.Length ?? 0;

        public KeyCollection Keys => _keys ??= new KeyCollection(this);

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        public ValueCollection Values => _values ??= new ValueCollection(this);

        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public TValue this[TKey key]
        {
            get
            {
                ref TValue value = ref FindValue(key);
                if (!Unsafe.IsNullRef(ref value))
                {
                    return value;
                }

                throw new KeyNotFoundException();
                return default;
            }
            set
            {
                bool modified = TryInsert(key, value, InsertionBehavior.OverwriteExisting);
            }
        }

        public void Add(TKey key, TValue value)
        {
            bool modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair) =>
            Add(keyValuePair.Key, keyValuePair.Value);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
        {
            ref TValue value = ref FindValue(keyValuePair.Key);
            if (!Unsafe.IsNullRef(ref value) && EqualityComparer<TValue>.Default.Equals(value, keyValuePair.Value))
            {
                return true;
            }

            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            ref TValue value = ref FindValue(keyValuePair.Key);
            if (!Unsafe.IsNullRef(ref value) && EqualityComparer<TValue>.Default.Equals(value, keyValuePair.Value))
            {
                Remove(keyValuePair.Key);
                return true;
            }

            return false;
        }

        public void Clear()
        {
            int count = _count;
            if (count > 0)
            {

                Array.Clear(_buckets);

                _count = 0;
                _freeList = -1;
                _freeCount = 0;
                Array.Clear(_entries, 0, count);
            }
        }

        public bool ContainsKey(TKey key) =>
            !Unsafe.IsNullRef(ref FindValue(key));

        public bool ContainsValue(TValue value)
        {
            Entry[]? entries = _entries;
            if (value == null)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (entries![i].next >= -1 && entries[i].value == null)
                    {
                        return true;
                    }
                }
            }
            else if (typeof(TValue).IsValueType)
            {
                // ValueType: Devirtualize with EqualityComparer<TValue>.Default intrinsic
                for (int i = 0; i < _count; i++)
                {
                    if (entries![i].next >= -1 && EqualityComparer<TValue>.Default.Equals(entries[i].value, value))
                    {
                        return true;
                    }
                }
            }
            else
            {
                // Object type: Shared Generic, EqualityComparer<TValue>.Default won't devirtualize
                // So cache in a local rather than get EqualityComparer per loop iteration
                EqualityComparer<TValue> defaultComparer = EqualityComparer<TValue>.Default;
                for (int i = 0; i < _count; i++)
                {
                    if (entries![i].next >= -1 && defaultComparer.Equals(entries[i].value, value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if ((uint)index > (uint)array.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessOrEqualException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            int count = _count;
            Entry[]? entries = _entries;
            for (int i = 0; i < count; i++)
            {
                if (entries![i].next >= -1)
                {
                    array[index++] = new KeyValuePair<TKey, TValue>(entries[i].key, entries[i].value);
                }
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            Count == 0 ? GenericEmptyEnumerator<KeyValuePair<TKey, TValue>>.Instance :
            GetEnumerator();


        internal ref TValue FindValue(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            ref Entry entry = ref Unsafe.NullRef<Entry>();
            if (_buckets != null)
            {
                IEqualityComparer<TKey>? comparer = _comparer;
                if (typeof(TKey).IsValueType && // comparer can only be null for value types
                    comparer == null)
                {
                    uint hashCode = (uint)EqualityComparer<TKey>.Default.GetHashCode(key);
                    int i = GetBucket(hashCode);
                    Entry[]? entries = _entries;
                    uint collisionCount = 0;

                    // ValueType: Devirtualize with EqualityComparer<TKey>.Default intrinsic
                    i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                    do
                    {
                        // Test in if to drop range check for following array access
                        if ((uint)i >= (uint)entries.Length)
                        {
                            goto ReturnNotFound;
                        }

                        entry = ref entries[i];
                        if (entry.hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.key, key))
                        {
                            goto ReturnFound;
                        }

                        i = entry.next;

                        collisionCount++;
                    } while (collisionCount <= (uint)entries.Length);

                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    goto ConcurrentOperation;
                }
                else
                {
                    uint hashCode = (uint)comparer.GetHashCode(key);
                    int i = GetBucket(hashCode);
                    Entry[]? entries = _entries;
                    uint collisionCount = 0;
                    i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                    do
                    {
                        // Test in if to drop range check for following array access
                        if ((uint)i >= (uint)entries.Length)
                        {
                            goto ReturnNotFound;
                        }

                        entry = ref entries[i];
                        if (entry.hashCode == hashCode && comparer.Equals(entry.key, key))
                        {
                            goto ReturnFound;
                        }

                        i = entry.next;

                        collisionCount++;
                    } while (collisionCount <= (uint)entries.Length);

                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    goto ConcurrentOperation;
                }
            }

            goto ReturnNotFound;

        ConcurrentOperation:
            throw new InvalidOperationException();
        ReturnFound:
            ref TValue value = ref entry.value;
        Return:
            return ref value;
        ReturnNotFound:
            value = ref Unsafe.NullRef<TValue>();
            goto Return;
        }

        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            int[] buckets = new int[size];
            Entry[] entries = new Entry[size];

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _freeList = -1;
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
#endif
            _buckets = buckets;
            _entries = entries;


            return size;
        }

        private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            if (_buckets == null)
            {
                Initialize(0);
            }

            Entry[]? entries = _entries;
            IEqualityComparer<TKey>? comparer = _comparer;
            uint hashCode = (uint)((typeof(TKey).IsValueType && comparer == null) ? key.GetHashCode() : comparer!.GetHashCode(key));

            uint collisionCount = 0;
            ref int bucket = ref GetBucket(hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based

            if (typeof(TKey).IsValueType && // comparer can only be null for value types
                comparer == null)
            {
                while ((uint)i < (uint)entries.Length)
                {
                    if (entries[i].hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entries[i].key, key))
                    {
                        if (behavior == InsertionBehavior.OverwriteExisting)
                        {
                            entries[i].value = value;
                            return true;
                        }

                        if (behavior == InsertionBehavior.ThrowOnExisting)
                        {
                            throw new ArgumentException();
                        }

                        return false;
                    }

                    i = entries[i].next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }
            else
            {
                while ((uint)i < (uint)entries.Length)
                {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
                    {
                        if (behavior == InsertionBehavior.OverwriteExisting)
                        {
                            entries[i].value = value;
                            return true;
                        }

                        if (behavior == InsertionBehavior.ThrowOnExisting)
                        {
                            throw new ArgumentException();
                        }

                        return false;
                    }

                    i = entries[i].next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeList = StartOfFreeList - entries[_freeList].next;
                _freeCount--;
            }
            else
            {
                int count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucket(hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            ref Entry entry = ref entries![index];
            entry.hashCode = hashCode;
            entry.next = bucket - 1; // Value in _buckets is 1-based
            entry.key = key;
            entry.value = value;
            bucket = index + 1; // Value in _buckets is 1-based
            _version++;

            // Value types never rehash
            if (!typeof(TKey).IsValueType && collisionCount > HashHelpers.HashCollisionThreshold && comparer is NonRandomizedStringEqualityComparer)
            {
                // If we hit the collision threshold we'll need to switch to the comparer which is using randomized string hashing
                // i.e. EqualityComparer<string>.Default.
                Resize(entries.Length, true);
            }

            return true;
        }

        /// <summary>
        /// Gets an instance of a type that may be used to perform operations on the current <see cref="Dictionary{TKey, TValue}"/>
        /// using a <typeparamref name="TAlternateKey"/> as a key instead of a <typeparamref name="TKey"/>.
        /// </summary>
        /// <typeparam name="TAlternateKey">The alternate type of a key for performing lookups.</typeparam>
        /// <returns>The created lookup instance.</returns>
        /// <exception cref="InvalidOperationException">The dictionary's comparer is not compatible with <typeparamref name="TAlternateKey"/>.</exception>
        /// <remarks>
        /// The dictionary must be using a comparer that implements <see cref="IAlternateEqualityComparer{TAlternateKey, TKey}"/> with
        /// <typeparamref name="TAlternateKey"/> and <typeparamref name="TKey"/>. If it doesn't, an exception will be thrown.
        /// </remarks>
        public AlternateLookup<TAlternateKey> GetAlternateLookup<TAlternateKey>()
            where TAlternateKey : notnull, allows ref struct
        {
            if (!AlternateLookup<TAlternateKey>.IsCompatibleKey(this))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_IncompatibleComparer);
            }

            return new AlternateLookup<TAlternateKey>(this);
        }

        /// <summary>
        /// Gets an instance of a type that may be used to perform operations on the current <see cref="Dictionary{TKey, TValue}"/>
        /// using a <typeparamref name="TAlternateKey"/> as a key instead of a <typeparamref name="TKey"/>.
        /// </summary>
        /// <typeparam name="TAlternateKey">The alternate type of a key for performing lookups.</typeparam>
        /// <param name="lookup">The created lookup instance when the method returns true, or a default instance that should not be used if the method returns false.</param>
        /// <returns>true if a lookup could be created; otherwise, false.</returns>
        /// <remarks>
        /// The dictionary must be using a comparer that implements <see cref="IAlternateEqualityComparer{TAlternateKey, TKey}"/> with
        /// <typeparamref name="TAlternateKey"/> and <typeparamref name="TKey"/>. If it doesn't, the method will return false.
        /// </remarks>
        public bool TryGetAlternateLookup<TAlternateKey>(
            out AlternateLookup<TAlternateKey> lookup)
            where TAlternateKey : notnull, allows ref struct
        {
            if (AlternateLookup<TAlternateKey>.IsCompatibleKey(this))
            {
                lookup = new AlternateLookup<TAlternateKey>(this);
                return true;
            }

            lookup = default;
            return false;
        }

        /// <summary>
        /// Provides a type that may be used to perform operations on a <see cref="Dictionary{TKey, TValue}"/>
        /// using a <typeparamref name="TAlternateKey"/> as a key instead of a <typeparamref name="TKey"/>.
        /// </summary>
        /// <typeparam name="TAlternateKey">The alternate type of a key for performing lookups.</typeparam>
        public readonly struct AlternateLookup<TAlternateKey> where TAlternateKey : notnull, allows ref struct
        {
            /// <summary>Initialize the instance. The dictionary must have already been verified to have a compatible comparer.</summary>
            internal AlternateLookup(Dictionary<TKey, TValue> dictionary)
            {
                Dictionary = dictionary;
            }

            /// <summary>Gets the <see cref="Dictionary{TKey, TValue}"/> against which this instance performs operations.</summary>
            public Dictionary<TKey, TValue> Dictionary { get; }

            /// <summary>Gets or sets the value associated with the specified alternate key.</summary>
            /// <param name="key">The alternate key of the value to get or set.</param>
            /// <value>
            /// The value associated with the specified alternate key. If the specified alternate key is not found, a get operation throws
            /// a <see cref="KeyNotFoundException"/>, and a set operation creates a new element with the specified key.
            /// </value>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            /// <exception cref="KeyNotFoundException">The property is retrieved and alternate key does not exist in the collection.</exception>
            public TValue this[TAlternateKey key]
            {
                get
                {
                    ref TValue value = ref FindValue(key, out _);
                    if (Unsafe.IsNullRef(ref value))
                    {
                        ThrowHelper.ThrowKeyNotFoundException(GetAlternateComparer(Dictionary).Create(key));
                    }

                    return value;
                }
                set => GetValueRefOrAddDefault(key, out _) = value;
            }

            /// <summary>Checks whether the dictionary has a comparer compatible with <typeparamref name="TAlternateKey"/>.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static bool IsCompatibleKey(Dictionary<TKey, TValue> dictionary)
            {
                return dictionary._comparer is IAlternateEqualityComparer<TAlternateKey, TKey>;
            }

            /// <summary>Gets the dictionary's alternate comparer. The dictionary must have already been verified as compatible.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static IAlternateEqualityComparer<TAlternateKey, TKey> GetAlternateComparer(Dictionary<TKey, TValue> dictionary)
            {
                return Unsafe.As<IAlternateEqualityComparer<TAlternateKey, TKey>>(dictionary._comparer)!;
            }

            /// <summary>Gets the value associated with the specified alternate key.</summary>
            /// <param name="key">The alternate key of the value to get.</param>
            /// <param name="value">
            /// When this method returns, contains the value associated with the specified key, if the key is found;
            /// otherwise, the default value for the type of the value parameter.
            /// </param>
            /// <returns><see langword="true"/> if an entry was found; otherwise, <see langword="false"/>.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            public bool TryGetValue(TAlternateKey key, [MaybeNullWhen(false)] out TValue value)
            {
                ref TValue valueRef = ref FindValue(key, out _);
                if (!Unsafe.IsNullRef(ref valueRef))
                {
                    value = valueRef;
                    return true;
                }

                value = default;
                return false;
            }

            /// <summary>Gets the value associated with the specified alternate key.</summary>
            /// <param name="key">The alternate key of the value to get.</param>
            /// <param name="actualKey">
            /// When this method returns, contains the actual key associated with the alternate key, if the key is found;
            /// otherwise, the default value for the type of the key parameter.
            /// </param>
            /// <param name="value">
            /// When this method returns, contains the value associated with the specified key, if the key is found;
            /// otherwise, the default value for the type of the value parameter.
            /// </param>
            /// <returns><see langword="true"/> if an entry was found; otherwise, <see langword="false"/>.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            public bool TryGetValue(TAlternateKey key, [MaybeNullWhen(false)] out TKey actualKey, [MaybeNullWhen(false)] out TValue value)
            {
                ref TValue valueRef = ref FindValue(key, out actualKey);
                if (!Unsafe.IsNullRef(ref valueRef))
                {
                    value = valueRef;
                    return true;
                }

                value = default;
                return false;
            }

            /// <summary>Determines whether the <see cref="Dictionary{TKey, TValue}"/> contains the specified alternate key.</summary>
            /// <param name="key">The alternate key to check.</param>
            /// <returns><see langword="true"/> if the key is in the dictionary; otherwise, <see langword="false"/>.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            public bool ContainsKey(TAlternateKey key) =>
                !Unsafe.IsNullRef(ref FindValue(key, out _));

            /// <summary>Finds the entry associated with the specified alternate key.</summary>
            /// <param name="key">The alternate key.</param>
            /// <param name="actualKey">The actual key, if found.</param>
            /// <returns>A reference to the value associated with the key, if found; otherwise, a null reference.</returns>
            internal ref TValue FindValue(TAlternateKey key, [MaybeNullWhen(false)] out TKey actualKey)
            {
                Dictionary<TKey, TValue> dictionary = Dictionary;
                IAlternateEqualityComparer<TAlternateKey, TKey> comparer = GetAlternateComparer(dictionary);

                ref Entry entry = ref Unsafe.NullRef<Entry>();
                if (dictionary._buckets != null)
                {
                    uint hashCode = (uint)comparer.GetHashCode(key);
                    int i = dictionary.GetBucket(hashCode);
                    Entry[]? entries = dictionary._entries;
                    uint collisionCount = 0;
                    i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                    do
                    {
                        // Should be a while loop https://github.com/dotnet/runtime/issues/9422
                        // Test in if to drop range check for following array access
                        if ((uint)i >= (uint)entries.Length)
                        {
                            goto ReturnNotFound;
                        }

                        entry = ref entries[i];
                        if (entry.hashCode == hashCode && comparer.Equals(key, entry.key))
                        {
                            goto ReturnFound;
                        }

                        i = entry.next;

                        collisionCount++;
                    } while (collisionCount <= (uint)entries.Length);

                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    goto ConcurrentOperation;
                }

                goto ReturnNotFound;

            ConcurrentOperation:
                ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
            ReturnFound:
                ref TValue value = ref entry.value;
                actualKey = entry.key;
            Return:
                return ref value;
            ReturnNotFound:
                value = ref Unsafe.NullRef<TValue>();
                actualKey = default!;
                goto Return;
            }

            /// <summary>Removes the value with the specified alternate key from the <see cref="Dictionary{TKey, TValue}"/>.</summary>
            /// <param name="key">The alternate key of the element to remove.</param>
            /// <returns>true if the element is successfully found and removed; otherwise, false.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            public bool Remove(TAlternateKey key) =>
                Remove(key, out _, out _);

            /// <summary>
            /// Removes the value with the specified alternate key from the <see cref="Dictionary{TKey, TValue}"/>,
            /// and copies the element to the value parameter.
            /// </summary>
            /// <param name="key">The alternate key of the element to remove.</param>
            /// <param name="actualKey">The removed key.</param>
            /// <param name="value">The removed element.</param>
            /// <returns>true if the element is successfully found and removed; otherwise, false.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            public bool Remove(TAlternateKey key, [MaybeNullWhen(false)] out TKey actualKey, [MaybeNullWhen(false)] out TValue value)
            {
                Dictionary<TKey, TValue> dictionary = Dictionary;
                IAlternateEqualityComparer<TAlternateKey, TKey> comparer = GetAlternateComparer(dictionary);

                if (dictionary._buckets != null)
                {
                    uint collisionCount = 0;

                    uint hashCode = (uint)comparer.GetHashCode(key);

                    ref int bucket = ref dictionary.GetBucket(hashCode);
                    Entry[]? entries = dictionary._entries;
                    int last = -1;
                    int i = bucket - 1; // Value in buckets is 1-based
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];

                        if (entry.hashCode == hashCode && comparer.Equals(key, entry.key))
                        {
                            if (last < 0)
                            {
                                bucket = entry.next + 1; // Value in buckets is 1-based
                            }
                            else
                            {
                                entries[last].next = entry.next;
                            }

                            actualKey = entry.key;
                            value = entry.value;

                            entry.next = StartOfFreeList - dictionary._freeList;

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                            {
                                entry.key = default!;
                            }

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                            {
                                entry.value = default!;
                            }

                            dictionary._freeList = i;
                            dictionary._freeCount++;
                            return true;
                        }

                        last = i;
                        i = entry.next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }

                actualKey = default;
                value = default;
                return false;
            }

            /// <summary>Attempts to add the specified key and value to the dictionary.</summary>
            /// <param name="key">The alternate key of the element to add.</param>
            /// <param name="value">The value of the element to add.</param>
            /// <returns>true if the key/value pair was added to the dictionary successfully; otherwise, false.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
            public bool TryAdd(TAlternateKey key, TValue value)
            {
                ref TValue? slot = ref GetValueRefOrAddDefault(key, out bool exists);
                if (!exists)
                {
                    slot = value;
                    return true;
                }

                return false;
            }

            /// <inheritdoc cref="CollectionsMarshal.GetValueRefOrAddDefault{TKey, TValue}(Dictionary{TKey, TValue}, TKey, out bool)"/>
            internal ref TValue? GetValueRefOrAddDefault(TAlternateKey key, out bool exists)
            {
                // NOTE: this method is a mirror of GetValueRefOrAddDefault above. Keep it in sync.

                Dictionary<TKey, TValue> dictionary = Dictionary;
                IAlternateEqualityComparer<TAlternateKey, TKey> comparer = GetAlternateComparer(dictionary);

                if (dictionary._buckets == null)
                {
                    dictionary.Initialize(0);
                }

                Entry[]? entries = dictionary._entries;

                uint hashCode = (uint)comparer.GetHashCode(key);

                uint collisionCount = 0;
                ref int bucket = ref dictionary.GetBucket(hashCode);
                int i = bucket - 1; // Value in _buckets is 1-based

                while ((uint)i < (uint)entries.Length)
                {
                    if (entries[i].hashCode == hashCode && comparer.Equals(key, entries[i].key))
                    {
                        exists = true;

                        return ref entries[i].value!;
                    }

                    i = entries[i].next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }

                TKey actualKey = comparer.Create(key);
                if (actualKey is null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
                }

                int index;
                if (dictionary._freeCount > 0)
                {
                    index = dictionary._freeList;
                    dictionary._freeList = StartOfFreeList - entries[dictionary._freeList].next;
                    dictionary._freeCount--;
                }
                else
                {
                    int count = dictionary._count;
                    if (count == entries.Length)
                    {
                        dictionary.Resize();
                        bucket = ref dictionary.GetBucket(hashCode);
                    }
                    index = count;
                    dictionary._count = count + 1;
                    entries = dictionary._entries;
                }

                ref Entry entry = ref entries![index];
                entry.hashCode = hashCode;
                entry.next = bucket - 1; // Value in _buckets is 1-based
                entry.key = actualKey;
                entry.value = default!;
                bucket = index + 1; // Value in _buckets is 1-based
                dictionary._version++;

                // Value types never rehash
                if (!typeof(TKey).IsValueType && collisionCount > HashHelpers.HashCollisionThreshold && comparer is NonRandomizedStringEqualityComparer)
                {
                    // If we hit the collision threshold we'll need to switch to the comparer which is using randomized string hashing
                    // i.e. EqualityComparer<string>.Default.
                    dictionary.Resize(entries.Length, true);

                    exists = false;

                    // At this point the entries array has been resized, so the current reference we have is no longer valid.
                    // We're forced to do a new lookup and return an updated reference to the new entry instance. This new
                    // lookup is guaranteed to always find a value though and it will never return a null reference here.
                    ref TValue? value = ref dictionary.FindValue(actualKey)!;

                    return ref value;
                }

                exists = false;

                return ref entry.value!;
            }
        }

        /// <summary>
        /// A helper class containing APIs exposed through <see cref="CollectionsMarshal"/> or <see cref="CollectionExtensions"/>.
        /// These methods are relatively niche and only used in specific scenarios, so adding them in a separate type avoids
        /// the additional overhead on each <see cref="Dictionary{TKey, TValue}"/> instantiation, especially in AOT scenarios.
        /// </summary>
        internal static class CollectionsMarshalHelper
        {
            /// <inheritdoc cref="CollectionsMarshal.GetValueRefOrAddDefault{TKey, TValue}(Dictionary{TKey, TValue}, TKey, out bool)"/>
            public static ref TValue? GetValueRefOrAddDefault(Dictionary<TKey, TValue> dictionary, TKey key, out bool exists)
            {
                // NOTE: this method is mirrored by Dictionary<TKey, TValue>.TryInsert above.
                // If you make any changes here, make sure to keep that version in sync as well.

                if (key == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
                }

                if (dictionary._buckets == null)
                {
                    dictionary.Initialize(0);
                }

                Entry[]? entries = dictionary._entries;

                IEqualityComparer<TKey>? comparer = dictionary._comparer;

                uint hashCode = (uint)((typeof(TKey).IsValueType && comparer == null) ? key.GetHashCode() : comparer!.GetHashCode(key));

                uint collisionCount = 0;
                ref int bucket = ref dictionary.GetBucket(hashCode);
                int i = bucket - 1; // Value in _buckets is 1-based

                if (typeof(TKey).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                    comparer == null)
                {
                    // ValueType: Devirtualize with EqualityComparer<TKey>.Default intrinsic
                    while ((uint)i < (uint)entries.Length)
                    {
                        if (entries[i].hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entries[i].key, key))
                        {
                            exists = true;

                            return ref entries[i].value!;
                        }

                        i = entries[i].next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
                else
                {
                    while ((uint)i < (uint)entries.Length)
                    {
                        if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
                        {
                            exists = true;

                            return ref entries[i].value!;
                        }

                        i = entries[i].next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }

                int index;
                if (dictionary._freeCount > 0)
                {
                    index = dictionary._freeList;
                    dictionary._freeList = StartOfFreeList - entries[dictionary._freeList].next;
                    dictionary._freeCount--;
                }
                else
                {
                    int count = dictionary._count;
                    if (count == entries.Length)
                    {
                        dictionary.Resize();
                        bucket = ref dictionary.GetBucket(hashCode);
                    }
                    index = count;
                    dictionary._count = count + 1;
                    entries = dictionary._entries;
                }

                ref Entry entry = ref entries![index];
                entry.hashCode = hashCode;
                entry.next = bucket - 1; // Value in _buckets is 1-based
                entry.key = key;
                entry.value = default!;
                bucket = index + 1; // Value in _buckets is 1-based
                dictionary._version++;

                // Value types never rehash
                if (!typeof(TKey).IsValueType && collisionCount > HashHelpers.HashCollisionThreshold && comparer is NonRandomizedStringEqualityComparer)
                {
                    // If we hit the collision threshold we'll need to switch to the comparer which is using randomized string hashing
                    // i.e. EqualityComparer<string>.Default.
                    dictionary.Resize(entries.Length, true);

                    exists = false;

                    // At this point the entries array has been resized, so the current reference we have is no longer valid.
                    // We're forced to do a new lookup and return an updated reference to the new entry instance. This new
                    // lookup is guaranteed to always find a value though and it will never return a null reference here.
                    ref TValue? value = ref dictionary.FindValue(key)!;

                    return ref value;
                }

                exists = false;

                return ref entry.value!;
            }
        }

        private void Resize() => Resize(HashHelpers.ExpandPrime(_count), false);

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            Entry[] entries = new Entry[newSize];

            int count = _count;
            Array.Copy(_entries, entries, count);

            if (!typeof(TKey).IsValueType && forceNewHashCodes)
            {
                IEqualityComparer<TKey> comparer = _comparer = (IEqualityComparer<TKey>)((NonRandomizedStringEqualityComparer)_comparer).GetRandomizedEqualityComparer();

                for (int i = 0; i < count; i++)
                {
                    if (entries[i].next >= -1)
                    {
                        entries[i].hashCode = (uint)comparer.GetHashCode(entries[i].key);
                    }
                }
            }

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new int[newSize];
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
#endif
            for (int i = 0; i < count; i++)
            {
                if (entries[i].next >= -1)
                {
                    ref int bucket = ref GetBucket(entries[i].hashCode);
                    entries[i].next = bucket - 1; // Value in _buckets is 1-based
                    bucket = i + 1;
                }
            }

            _entries = entries;
        }

        public bool Remove(TKey key)
        {
            // The overload Remove(TKey key, out TValue value) is a copy of this method with one additional
            // statement to copy the value for entry being removed into the output parameter.
            // Code has been intentionally duplicated for performance reasons.
            // If you make any changes here, make sure to keep that version in sync as well.

            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            if (_buckets != null)
            {
                uint collisionCount = 0;

                IEqualityComparer<TKey>? comparer = _comparer;
                uint hashCode = (uint)(typeof(TKey).IsValueType && comparer == null ? key.GetHashCode() : comparer!.GetHashCode(key));

                ref int bucket = ref GetBucket(hashCode);
                Entry[]? entries = _entries;
                int last = -1;
                int i = bucket - 1; // Value in buckets is 1-based

                if (typeof(TKey).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                    comparer == null)
                {
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];

                        if (entry.hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.key, key))
                        {
                            if (last < 0)
                            {
                                bucket = entry.next + 1; // Value in buckets is 1-based
                            }
                            else
                            {
                                entries[last].next = entry.next;
                            }

                            entry.next = StartOfFreeList - _freeList;

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                            {
                                entry.key = default!;
                            }

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                            {
                                entry.value = default!;
                            }

                            _freeList = i;
                            _freeCount++;
                            return true;
                        }

                        last = i;
                        i = entry.next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
                else
                {
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];

                        if (entry.hashCode == hashCode && comparer.Equals(entry.key, key))
                        {
                            if (last < 0)
                            {
                                bucket = entry.next + 1; // Value in buckets is 1-based
                            }
                            else
                            {
                                entries[last].next = entry.next;
                            }

                            entry.next = StartOfFreeList - _freeList;

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                            {
                                entry.key = default!;
                            }

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                            {
                                entry.value = default!;
                            }

                            _freeList = i;
                            _freeCount++;
                            return true;
                        }

                        last = i;
                        i = entry.next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
            }

            return false;
        }

        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            // This overload is a copy of the overload Remove(TKey key) with one additional
            // statement to copy the value for entry being removed into the output parameter.
            // Code has been intentionally duplicated for performance reasons.
            // If you make any changes here, make sure to keep the other overload in sync as well.

            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            if (_buckets != null)
            {
                uint collisionCount = 0;

                IEqualityComparer<TKey>? comparer = _comparer;
                uint hashCode = (uint)(typeof(TKey).IsValueType && comparer == null ? key.GetHashCode() : comparer!.GetHashCode(key));

                ref int bucket = ref GetBucket(hashCode);
                Entry[]? entries = _entries;
                int last = -1;
                int i = bucket - 1; // Value in buckets is 1-based

                if (typeof(TKey).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                    comparer == null)
                {
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];

                        if (entry.hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.key, key))
                        {
                            if (last < 0)
                            {
                                bucket = entry.next + 1; // Value in buckets is 1-based
                            }
                            else
                            {
                                entries[last].next = entry.next;
                            }

                            value = entry.value;

                            entry.next = StartOfFreeList - _freeList;

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                            {
                                entry.key = default!;
                            }

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                            {
                                entry.value = default!;
                            }

                            _freeList = i;
                            _freeCount++;
                            return true;
                        }

                        last = i;
                        i = entry.next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
                else
                {
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];

                        if (entry.hashCode == hashCode && comparer.Equals(entry.key, key))
                        {
                            if (last < 0)
                            {
                                bucket = entry.next + 1; // Value in buckets is 1-based
                            }
                            else
                            {
                                entries[last].next = entry.next;
                            }

                            value = entry.value;

                            entry.next = StartOfFreeList - _freeList;

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                            {
                                entry.key = default!;
                            }

                            if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
                            {
                                entry.value = default!;
                            }

                            _freeList = i;
                            _freeCount++;
                            return true;
                        }

                        last = i;
                        i = entry.next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop; which means a concurrent update has happened.
                            // Break out of the loop and throw, rather than looping forever.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
            }

            value = default;
            return false;
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            ref TValue valRef = ref FindValue(key);
            if (!Unsafe.IsNullRef(ref valRef))
            {
                value = valRef;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryAdd(TKey key, TValue value) =>
            TryInsert(key, value, InsertionBehavior.None);

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index) =>
            CopyTo(array, index);

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if (array.Rank != 1)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
            }

            if (array.GetLowerBound(0) != 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
            }

            if ((uint)index > (uint)array.Length)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                CopyTo(pairs, index);
            }
            else if (array is DictionaryEntry[] dictEntryArray)
            {
                Entry[]? entries = _entries;
                for (int i = 0; i < _count; i++)
                {
                    if (entries![i].next >= -1)
                    {
                        dictEntryArray[index++] = new DictionaryEntry(entries[i].key, entries[i].value);
                    }
                }
            }
            else
            {
                object[]? objects = array as object[];
                if (objects == null)
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }

                try
                {
                    int count = _count;
                    Entry[]? entries = _entries;
                    for (int i = 0; i < count; i++)
                    {
                        if (entries![i].next >= -1)
                        {
                            objects[index++] = new KeyValuePair<TKey, TValue>(entries[i].key, entries[i].value);
                        }
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<TKey, TValue>>)this).GetEnumerator();

        /// <summary>
        /// Ensures that the dictionary can hold up to 'capacity' entries without any further expansion of its backing storage
        /// </summary>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            int currentCapacity = _entries == null ? 0 : _entries.Length;
            if (currentCapacity >= capacity)
            {
                return currentCapacity;
            }

            _version++;

            if (_buckets == null)
            {
                return Initialize(capacity);
            }

            int newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize, forceNewHashCodes: false);
            return newSize;
        }

        /// <summary>
        /// Sets the capacity of this dictionary to what it would be if it had been originally initialized with all its entries
        /// </summary>
        /// <remarks>
        /// This method can be used to minimize the memory overhead
        /// once it is known that no new elements will be added.
        ///
        /// To allocate minimum size storage array, execute the following statements:
        ///
        /// dictionary.Clear();
        /// dictionary.TrimExcess();
        /// </remarks>
        public void TrimExcess() => TrimExcess(Count);

        /// <summary>
        /// Sets the capacity of this dictionary to hold up 'capacity' entries without any further expansion of its backing storage
        /// </summary>
        /// <remarks>
        /// This method can be used to minimize the memory overhead
        /// once it is known that no new elements will be added.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Passed capacity is lower than entries count.</exception>
        public void TrimExcess(int capacity)
        {
            if (capacity < Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            int newSize = HashHelpers.GetPrimeAtLeast(capacity);
            Entry[]? oldEntries = _entries;
            if (oldEntries is null || newSize >= oldEntries.Length)
            {
                return;
            }

            _version++;

            int[] buckets = new int[newSize];
            Entry[] entries = new Entry[newSize];

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _freeList = -1;
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
#endif
            _buckets = buckets;
            _entries = entries;

            CopyEntries(oldEntries, _count);
        }

        private void CopyEntries(Entry[] entries, int count)
        {
            Entry[] newEntries = _entries;
            int newCount = 0;
            for (int i = 0; i < count; i++)
            {
                uint hashCode = entries[i].hashCode;
                if (entries[i].next >= -1)
                {
                    ref Entry entry = ref newEntries[newCount];
                    entry = entries[i];
                    ref int bucket = ref GetBucket(hashCode);
                    entry.next = bucket - 1; // Value in _buckets is 1-based
                    bucket = newCount + 1;
                    newCount++;
                }
            }

            _count = newCount;
            _freeCount = 0;
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        bool IDictionary.IsFixedSize => false;

        bool IDictionary.IsReadOnly => false;

        ICollection IDictionary.Keys => Keys;

        ICollection IDictionary.Values => Values;

        object? IDictionary.this[object key]
        {
            get
            {
                if (IsCompatibleKey(key))
                {
                    ref TValue value = ref FindValue((TKey)key);
                    if (!Unsafe.IsNullRef(ref value))
                    {
                        return value;
                    }
                }

                return null;
            }
            set
            {
                if (key == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
                }
                ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

                try
                {
                    TKey tempKey = (TKey)key;
                    try
                    {
                        this[tempKey] = (TValue)value!;
                    }
                    catch (InvalidCastException)
                    {
                        ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));
                    }
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
                }
            }
        }

        private static bool IsCompatibleKey(object key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            return key is TKey;
        }

        void IDictionary.Add(object key, object? value)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

            try
            {
                TKey tempKey = (TKey)key;

                try
                {
                    Add(tempKey, (TValue)value!);
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));
                }
            }
            catch (InvalidCastException)
            {
                ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
            }
        }

        bool IDictionary.Contains(object key)
        {
            if (IsCompatibleKey(key))
            {
                return ContainsKey((TKey)key);
            }

            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DictEntry);

        void IDictionary.Remove(object key)
        {
            if (IsCompatibleKey(key))
            {
                Remove((TKey)key);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucket(uint hashCode)
        {
            int[] buckets = _buckets!;
#if TARGET_64BIT
            return ref buckets[HashHelpers.FastMod(hashCode, (uint)buckets.Length, _fastModMultiplier)];
#else
            return ref buckets[(uint)hashCode % buckets.Length];
#endif
        }

        private struct Entry
        {
            public uint hashCode;
            /// <summary>
            /// 0-based index of next entry in chain: -1 means end of chain
            /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            /// </summary>
            public int next;
            public TKey key;     // Key of entry
            public TValue value; // Value of entry
        }
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;
            private readonly int _getEnumeratorRetType;  // What should Enumerator.Current return?

            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(Dictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _getEnumeratorRetType = getEnumeratorRetType;
                _current = default;
            }

            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_dictionary._count)
                {
                    ref Entry entry = ref _dictionary._entries![_index++];

                    if (entry.next >= -1)
                    {
                        _current = new KeyValuePair<TKey, TValue>(entry.key, entry.value);
                        return true;
                    }
                }

                _index = _dictionary._count + 1;
                _current = default;
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => _current;

            public void Dispose() { }

            object? IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || (_index == _dictionary._count + 1))
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    if (_getEnumeratorRetType == DictEntry)
                    {
                        return new DictionaryEntry(_current.Key, _current.Value);
                    }

                    return new KeyValuePair<TKey, TValue>(_current.Key, _current.Value);
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _dictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                _index = 0;
                _current = default;
            }

            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (_index == 0 || (_index == _dictionary._count + 1))
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    return new DictionaryEntry(_current.Key, _current.Value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (_index == 0 || (_index == _dictionary._count + 1))
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    return _current.Key;
                }
            }

            object? IDictionaryEnumerator.Value
            {
                get
                {
                    if (_index == 0 || (_index == _dictionary._count + 1))
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    return _current.Value;
                }
            }
        }
        public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;

            public KeyCollection(Dictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                }

                _dictionary = dictionary;
            }

            public Enumerator GetEnumerator() => new Enumerator(_dictionary);

            public void CopyTo(TKey[] array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (index < 0 || index > array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                int count = _dictionary._count;
                Entry[]? entries = _dictionary._entries;
                for (int i = 0; i < count; i++)
                {
                    if (entries![i].next >= -1) array[index++] = entries[i].key;
                }
            }

            public int Count => _dictionary.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            void ICollection<TKey>.Add(TKey item) =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);

            void ICollection<TKey>.Clear() =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);

            public bool Contains(TKey item) =>
                _dictionary.ContainsKey(item);

            bool ICollection<TKey>.Remove(TKey item)
            {
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);
                return false;
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() =>
                Count == 0 ? SZGenericArrayEnumerator<TKey>.Empty :
                GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<TKey>)this).GetEnumerator();

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (array.Rank != 1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TKey[] keys)
                {
                    CopyTo(keys, index);
                }
                else
                {
                    object[]? objects = array as object[];
                    if (objects == null)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                    }

                    int count = _dictionary._count;
                    Entry[]? entries = _dictionary._entries;
                    try
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (entries![i].next >= -1) objects[index++] = entries[i].key;
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            public struct Enumerator : IEnumerator<TKey>, IEnumerator
            {
                private readonly Dictionary<TKey, TValue> _dictionary;
                private int _index;
                private readonly int _version;
                private TKey? _currentKey;

                internal Enumerator(Dictionary<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _index = 0;
                    _currentKey = default;
                }

                public void Dispose() { }

                public bool MoveNext()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    while ((uint)_index < (uint)_dictionary._count)
                    {
                        ref Entry entry = ref _dictionary._entries![_index++];

                        if (entry.next >= -1)
                        {
                            _currentKey = entry.key;
                            return true;
                        }
                    }

                    _index = _dictionary._count + 1;
                    _currentKey = default;
                    return false;
                }

                public TKey Current => _currentKey!;

                object? IEnumerator.Current
                {
                    get
                    {
                        if (_index == 0 || (_index == _dictionary._count + 1))
                        {
                            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                        }

                        return _currentKey;
                    }
                }

                void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    _index = 0;
                    _currentKey = default;
                }
            }
        }
        public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;

            public ValueCollection(Dictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                }

                _dictionary = dictionary;
            }

            public Enumerator GetEnumerator() => new Enumerator(_dictionary);

            public void CopyTo(TValue[] array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if ((uint)index > array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                int count = _dictionary._count;
                Entry[]? entries = _dictionary._entries;
                for (int i = 0; i < count; i++)
                {
                    if (entries![i].next >= -1) array[index++] = entries[i].value;
                }
            }

            public int Count => _dictionary.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            void ICollection<TValue>.Add(TValue item) =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);

            bool ICollection<TValue>.Remove(TValue item)
            {
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);
                return false;
            }

            void ICollection<TValue>.Clear() =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);

            bool ICollection<TValue>.Contains(TValue item) => _dictionary.ContainsValue(item);

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() =>
                Count == 0 ? SZGenericArrayEnumerator<TValue>.Empty :
                GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<TValue>)this).GetEnumerator();

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (array.Rank != 1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TValue[] values)
                {
                    CopyTo(values, index);
                }
                else
                {
                    object[]? objects = array as object[];
                    if (objects == null)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                    }

                    int count = _dictionary._count;
                    Entry[]? entries = _dictionary._entries;
                    try
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (entries![i].next >= -1) objects[index++] = entries[i].value!;
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            public struct Enumerator : IEnumerator<TValue>, IEnumerator
            {
                private readonly Dictionary<TKey, TValue> _dictionary;
                private int _index;
                private readonly int _version;
                private TValue? _currentValue;

                internal Enumerator(Dictionary<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _index = 0;
                    _currentValue = default;
                }

                public void Dispose() { }

                public bool MoveNext()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    while ((uint)_index < (uint)_dictionary._count)
                    {
                        ref Entry entry = ref _dictionary._entries![_index++];

                        if (entry.next >= -1)
                        {
                            _currentValue = entry.value;
                            return true;
                        }
                    }
                    _index = _dictionary._count + 1;
                    _currentValue = default;
                    return false;
                }

                public TValue Current => _currentValue!;

                object? IEnumerator.Current
                {
                    get
                    {
                        if (_index == 0 || (_index == _dictionary._count + 1))
                        {
                            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                        }

                        return _currentValue;
                    }
                }

                void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    _index = 0;
                    _currentValue = default;
                }
            }
        }
    }

    internal ref struct BitHelper
    {
        private const int IntSize = sizeof(int) * 8;
        private readonly Span<int> _span;

        internal BitHelper(Span<int> span, bool clear)
        {
            if (clear)
            {
                span.Clear();
            }
            _span = span;
        }

        internal void MarkBit(int bitPosition)
        {
            uint bitArrayIndex = (uint)bitPosition / IntSize;

            if (bitArrayIndex < (uint)_span.Length)
            {
                _span[(int)bitArrayIndex] |= (1 << (int)((uint)bitPosition % IntSize));
            }
        }

        internal bool IsMarked(int bitPosition)
        {
            uint bitArrayIndex = (uint)bitPosition / IntSize;

            return
                bitArrayIndex < (uint)_span.Length &&
                (_span[(int)bitArrayIndex] & (1 << ((int)((uint)bitPosition % IntSize)))) != 0;
        }

        /// <summary>How many ints must be allocated to represent n bits. Returns (n+31)/32, but avoids overflow.</summary>
        internal static int ToIntArrayLength(int n) => n > 0 ? ((n - 1) / IntSize + 1) : 0;
    }
    public class HashSet<T> : ICollection<T>, ISet<T>, IReadOnlyCollection<T>, IReadOnlySet<T>
    {
        // This uses the same array-based implementation as Dictionary<TKey, TValue>.

        // Constants for serialization
        private const string CapacityName = "Capacity"; // Do not rename (binary serialization)
        private const string ElementsName = "Elements"; // Do not rename (binary serialization)
        private const string ComparerName = "Comparer"; // Do not rename (binary serialization)
        private const string VersionName = "Version"; // Do not rename (binary serialization)

        /// <summary>Cutoff point for stackallocs. This corresponds to the number of ints.</summary>
        private const int StackAllocThreshold = 100;

        /// <summary>
        /// When constructing a hashset from an existing collection, it may contain duplicates,
        /// so this is used as the max acceptable excess ratio of capacity to count. Note that
        /// this is only used on the ctor and not to automatically shrink if the hashset has, e.g,
        /// a lot of adds followed by removes. Users must explicitly shrink by calling TrimExcess.
        /// This is set to 3 because capacity is acceptable as 2x rounded up to nearest prime.
        /// </summary>
        private const int ShrinkThreshold = 3;
        private const int StartOfFreeList = -3;

        private int[]? _buckets;
        private Entry[]? _entries;
#if TARGET_64BIT
        private ulong _fastModMultiplier;
#endif
        private int _count;
        private int _freeList;
        private int _freeCount;
        private int _version;
        private IEqualityComparer<T>? _comparer;

        #region Constructors

        public HashSet() : this((IEqualityComparer<T>?)null) { }

        public HashSet(IEqualityComparer<T>? comparer)
        {
            // For reference types, we always want to store a comparer instance, either
            // the one provided, or if one wasn't provided, the default (accessing
            // EqualityComparer<TKey>.Default with shared generics on every dictionary
            // access can add measurable overhead).  For value types, if no comparer is
            // provided, or if the default is provided, we'd prefer to use
            // EqualityComparer<TKey>.Default.Equals on every use, enabling the JIT to
            // devirtualize and possibly inline the operation.
            if (!typeof(T).IsValueType)
            {
                _comparer = comparer ?? EqualityComparer<T>.Default;

                // Special-case EqualityComparer<string>.Default, StringComparer.Ordinal, and StringComparer.OrdinalIgnoreCase.
                // We use a non-randomized comparer for improved perf, falling back to a randomized comparer if the
                // hash buckets become unbalanced.
                if (typeof(T) == typeof(string) &&
                    NonRandomizedStringEqualityComparer.GetStringComparer(_comparer) is IEqualityComparer<string> stringComparer)
                {
                    _comparer = (IEqualityComparer<T>)stringComparer;
                }
            }
            else if (comparer is not null && // first check for null to avoid forcing default comparer instantiation unnecessarily
                     comparer != EqualityComparer<T>.Default)
            {
                _comparer = comparer;
            }
        }

        public HashSet(int capacity) : this(capacity, null) { }

        public HashSet(IEnumerable<T> collection) : this(collection, null) { }

        public HashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) : this(comparer)
        {
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
            }

            if (collection is HashSet<T> otherAsHashSet && EffectiveEqualityComparersAreEqual(this, otherAsHashSet))
            {
                ConstructFrom(otherAsHashSet);
            }
            else
            {
                // To avoid excess resizes, first set size based on collection's count. The collection may
                // contain duplicates, so call TrimExcess if resulting HashSet is larger than the threshold.
                if (collection is ICollection<T> coll)
                {
                    int count = coll.Count;
                    if (count > 0)
                    {
                        Initialize(count);
                    }
                }

                UnionWith(collection);

                if (_count > 0 && _entries!.Length / _count > ShrinkThreshold)
                {
                    TrimExcess();
                }
            }

        }

        public HashSet(int capacity, IEqualityComparer<T>? comparer) : this(comparer)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            if (capacity > 0)
            {
                Initialize(capacity);
            }
        }

        /// <summary>Initializes the HashSet from another HashSet with the same element type and equality comparer.</summary>
        private void ConstructFrom(HashSet<T> source)
        {
            if (source.Count == 0)
            {
                // As well as short-circuiting on the rest of the work done,
                // this avoids errors from trying to access source._buckets
                // or source._entries when they aren't initialized.
                return;
            }

            int capacity = source._buckets!.Length;
            int threshold = HashHelpers.ExpandPrime(source.Count + 1);

            if (threshold >= capacity)
            {
                _buckets = (int[])source._buckets.Clone();
                _entries = (Entry[])source._entries!.Clone();
                _freeList = source._freeList;
                _freeCount = source._freeCount;
                _count = source._count;
#if TARGET_64BIT
                _fastModMultiplier = source._fastModMultiplier;
#endif
            }
            else
            {
                Initialize(source.Count);
                CopyEntries(source._entries!, source._count);
            }
        }

        #endregion

        #region ICollection<T> methods

        void ICollection<T>.Add(T item) => AddIfNotPresent(item, out _);

        /// <summary>Removes all elements from the <see cref="HashSet{T}"/> object.</summary>
        public void Clear()
        {
            int count = _count;
            if (count > 0)
            {
                Array.Clear(_buckets);
                _count = 0;
                _freeList = -1;
                _freeCount = 0;
                Array.Clear(_entries, 0, count);
            }
        }

        /// <summary>Determines whether the <see cref="HashSet{T}"/> contains the specified element.</summary>
        /// <param name="item">The element to locate in the <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object contains the specified element; otherwise, false.</returns>
        public bool Contains(T item) => FindItemIndex(item) >= 0;

        /// <summary>Gets the index of the item in <see cref="_entries"/>, or -1 if it's not in the set.</summary>
        private int FindItemIndex(T item)
        {
            int[]? buckets = _buckets;
            if (buckets != null)
            {
                Entry[]? entries = _entries;

                uint collisionCount = 0;
                IEqualityComparer<T>? comparer = _comparer;

                if (typeof(T).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                    comparer == null)
                {
                    // ValueType: Devirtualize with EqualityComparer<TValue>.Default intrinsic
                    int hashCode = item!.GetHashCode();
                    int i = GetBucketRef(hashCode) - 1; // Value in _buckets is 1-based
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];
                        if (entry.HashCode == hashCode && EqualityComparer<T>.Default.Equals(entry.Value, item))
                        {
                            return i;
                        }
                        i = entry.Next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop, which means a concurrent update has happened.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
                else
                {
                    int hashCode = item != null ? comparer.GetHashCode(item) : 0;
                    int i = GetBucketRef(hashCode) - 1; // Value in _buckets is 1-based
                    while ((uint)i < (uint)entries.Length)
                    {
                        ref Entry entry = ref entries[i];
                        if (entry.HashCode == hashCode && comparer.Equals(entry.Value, item))
                        {
                            return i;
                        }
                        i = entry.Next;

                        collisionCount++;
                        if (collisionCount > (uint)entries.Length)
                        {
                            // The chain of entries forms a loop, which means a concurrent update has happened.
                            ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                        }
                    }
                }
            }
            return -1;
        }

        /// <summary>Gets a reference to the specified hashcode's bucket, containing an index into <see cref="_entries"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucketRef(int hashCode)
        {
            int[] buckets = _buckets!;
#if TARGET_64BIT
            return ref buckets[HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, _fastModMultiplier)];
#else
            return ref buckets[(uint)hashCode % (uint)buckets.Length];
#endif
        }

        public bool Remove(T item)
        {
            if (_buckets != null)
            {
                Entry[]? entries = _entries;

                uint collisionCount = 0;
                int last = -1;

                IEqualityComparer<T>? comparer = _comparer;
                int hashCode =
                    typeof(T).IsValueType && comparer == null ? item!.GetHashCode() :
                    item is not null ? comparer!.GetHashCode(item) :
                    0;

                ref int bucket = ref GetBucketRef(hashCode);
                int i = bucket - 1; // Value in buckets is 1-based

                while ((uint)i < (uint)entries.Length)
                {
                    ref Entry entry = ref entries[i];

                    if (entry.HashCode == hashCode && (comparer?.Equals(entry.Value, item) ?? EqualityComparer<T>.Default.Equals(entry.Value, item)))
                    {
                        if (last < 0)
                        {
                            bucket = entry.Next + 1; // Value in buckets is 1-based
                        }
                        else
                        {
                            entries[last].Next = entry.Next;
                        }

                        entry.Next = StartOfFreeList - _freeList;

                        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                        {
                            entry.Value = default!;
                        }

                        _freeList = i;
                        _freeCount++;
                        return true;
                    }

                    last = i;
                    i = entry.Next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }

            return false;
        }

        /// <summary>Gets the number of elements that are contained in the set.</summary>
        public int Count => _count - _freeCount;

        /// <summary>
        /// Gets the total numbers of elements the internal data structure can hold without resizing.
        /// </summary>
        public int Capacity => _entries?.Length ?? 0;

        bool ICollection<T>.IsReadOnly => false;

        #endregion

        #region AlternateLookup
        #endregion

        #region IEnumerable methods

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
            Count == 0 ? SZGenericArrayEnumerator<T>.Empty :
            GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        #endregion

        #region ISerializable methods
        #endregion

        #region IDeserializationCallback methods
        #endregion

        #region HashSet methods

        /// <summary>Adds the specified element to the <see cref="HashSet{T}"/>.</summary>
        /// <param name="item">The element to add to the set.</param>
        /// <returns>true if the element is added to the <see cref="HashSet{T}"/> object; false if the element is already present.</returns>
        public bool Add(T item) => AddIfNotPresent(item, out _);

        /// <summary>Searches the set for a given value and returns the equal value it finds, if any.</summary>
        /// <param name="equalValue">The value to search for.</param>
        /// <param name="actualValue">The value from the set that the search found, or the default value of <typeparamref name="T"/> when the search yielded no match.</param>
        /// <returns>A value indicating whether the search was successful.</returns>
        /// <remarks>
        /// This can be useful when you want to reuse a previously stored reference instead of
        /// a newly constructed one (so that more sharing of references can occur) or to look up
        /// a value that has more complete data than the value you currently have, although their
        /// comparer functions indicate they are equal.
        /// </remarks>
        public bool TryGetValue(T equalValue, [MaybeNullWhen(false)] out T actualValue)
        {
            if (_buckets != null)
            {
                int index = FindItemIndex(equalValue);
                if (index >= 0)
                {
                    actualValue = _entries![index].Value;
                    return true;
                }
            }

            actualValue = default;
            return false;
        }

        /// <summary>Modifies the current <see cref="HashSet{T}"/> object to contain all elements that are present in itself, the specified collection, or both.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // If this set is empty and other is a HashSet with the same effective comparer,
            // we can copy the data directly instead of adding each element individually.
            if (Count == 0 && other is HashSet<T> otherAsSet && EffectiveEqualityComparersAreEqual(this, otherAsSet))
            {
                ConstructFrom(otherAsSet);
                return;
            }

            foreach (T item in other)
            {
                AddIfNotPresent(item, out _);
            }
        }

        /// <summary>Modifies the current <see cref="HashSet{T}"/> object to contain only elements that are present in that object and in the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // Intersection of anything with empty set is empty set, so return if count is 0.
            // Same if the set intersecting with itself is the same set.
            if (Count == 0 || other == this)
            {
                return;
            }

            // If other is known to be empty, intersection is empty set; remove all elements, and we're done.
            if (other is ICollection<T> otherAsCollection)
            {
                if (otherAsCollection.Count == 0)
                {
                    Clear();
                    return;
                }

                // Faster if other is a hashset using same equality comparer; so check
                // that other is a hashset using the same equality comparer.
                if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
                {
                    IntersectWithHashSetWithSameComparer(otherAsSet);
                    return;
                }
            }

            IntersectWithEnumerable(other);
        }

        /// <summary>Removes all elements in the specified collection from the current <see cref="HashSet{T}"/> object.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // This is already the empty set; return.
            if (Count == 0)
            {
                return;
            }

            // Special case if other is this; a set minus itself is the empty set.
            if (other == this)
            {
                Clear();
                return;
            }

            // Remove every element in other from this.
            foreach (T element in other)
            {
                Remove(element);
            }
        }

        /// <summary>Modifies the current <see cref="HashSet{T}"/> object to contain only elements that are present either in that object or in the specified collection, but not both.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // If set is empty, then symmetric difference is other.
            if (Count == 0)
            {
                UnionWith(other);
                return;
            }

            // Special-case this; the symmetric difference of a set with itself is the empty set.
            if (other == this)
            {
                Clear();
                return;
            }

            // If other is a HashSet, it has unique elements according to its equality comparer,
            // but if they're using different equality comparers, then assumption of uniqueness
            // will fail. So first check if other is a hashset using the same equality comparer;
            // symmetric except is a lot faster and avoids bit array allocations if we can assume
            // uniqueness.
            if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
            {
                SymmetricExceptWithUniqueHashSet(otherAsSet);
            }
            else
            {
                SymmetricExceptWithEnumerable(other);
            }
        }

        /// <summary>Determines whether a <see cref="HashSet{T}"/> object is a subset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object is a subset of <paramref name="other"/>; otherwise, false.</returns>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // The empty set is a subset of any set, and a set is a subset of itself.
            // Set is always a subset of itself.
            if (Count == 0 || other == this)
            {
                return true;
            }

            if (other is ICollection<T> otherAsCollection)
            {
                // If this has more elements then it can't be a subset.
                if (Count > otherAsCollection.Count)
                {
                    return false;
                }

                // Faster if other has unique elements according to this equality comparer; so check
                // that other is a hashset using the same equality comparer.
                if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
                {
                    return IsSubsetOfHashSetWithSameComparer(otherAsSet);
                }
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount >= 0;
        }

        /// <summary>Determines whether a <see cref="HashSet{T}"/> object is a proper subset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object is a proper subset of <paramref name="other"/>; otherwise, false.</returns>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // No set is a proper subset of itself.
            if (other == this)
            {
                return false;
            }

            if (other is ICollection<T> otherAsCollection)
            {
                // No set is a proper subset of a set with less or equal number of elements.
                if (otherAsCollection.Count <= Count)
                {
                    return false;
                }

                // The empty set is a proper subset of anything but the empty set.
                if (Count == 0)
                {
                    // Based on check above, other is not empty when Count == 0.
                    return true;
                }

                // Faster if other is a hashset (and we're using same equality comparer).
                if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
                {
                    // This has strictly less than number of items in other, so the following
                    // check suffices for proper subset.
                    return IsSubsetOfHashSetWithSameComparer(otherAsSet);
                }
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount > 0;
        }

        /// <summary>Determines whether a <see cref="HashSet{T}"/> object is a proper superset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object is a superset of <paramref name="other"/>; otherwise, false.</returns>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // A set is always a superset of itself.
            if (other == this)
            {
                return true;
            }

            // Try to fall out early based on counts.
            if (other is ICollection<T> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    return true;
                }

                // Try to compare based on counts alone if other is a hashset with same equality comparer.
                if (other is HashSet<T> otherAsSet &&
                    EqualityComparersAreEqual(this, otherAsSet) &&
                    otherAsSet.Count > Count)
                {
                    return false;
                }
            }

            foreach (T element in other)
            {
                if (!Contains(element))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Determines whether a <see cref="HashSet{T}"/> object is a proper superset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object is a proper superset of <paramref name="other"/>; otherwise, false.</returns>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // The empty set isn't a proper superset of any set, and a set is never a strict superset of itself.
            if (Count == 0 || other == this)
            {
                return false;
            }

            if (other is ICollection<T> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    // Note that this has at least one element, based on above check.
                    return true;
                }

                // Faster if other is a hashset with the same equality comparer
                if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
                {
                    if (otherAsSet.Count >= Count)
                    {
                        return false;
                    }

                    // Now perform element check.
                    return otherAsSet.IsSubsetOfHashSetWithSameComparer(this);
                }
            }

            // Couldn't fall out in the above cases; do it the long way
            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
            return uniqueCount < Count && unfoundCount == 0;
        }

        /// <summary>Determines whether the current <see cref="HashSet{T}"/> object and a specified collection share common elements.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object and <paramref name="other"/> share at least one common element; otherwise, false.</returns>
        public bool Overlaps(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            if (Count == 0)
            {
                return false;
            }

            // Set overlaps itself
            if (other == this)
            {
                return true;
            }

            // Fast path for Overlaps when other is HashSet with same equality comparer
            if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
            {
                return otherAsSet.Count > Count ? OverlapsHashSetImpl(this, otherAsSet) : OverlapsHashSetImpl(otherAsSet, this);
            }

            return OverlapsEnumerableImpl(other);
        }

        private bool OverlapsEnumerableImpl(IEnumerable<T> other)
        {
            foreach (T element in other)
            {
                if (Contains(element))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsHashSetImpl(HashSet<T> smaller, HashSet<T> larger)
        {
            foreach (T element in smaller)
            {
                if (larger.Contains(element))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Determines whether a <see cref="HashSet{T}"/> object and the specified collection contain the same elements.</summary>
        /// <param name="other">The collection to compare to the current <see cref="HashSet{T}"/> object.</param>
        /// <returns>true if the <see cref="HashSet{T}"/> object is equal to <paramref name="other"/>; otherwise, false.</returns>
        public bool SetEquals(IEnumerable<T> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // A set is equal to itself.
            if (other == this)
            {
                return true;
            }

            if (other is ICollection<T> otherAsCollection)
            {
                // If this is empty, they are equal iff other is empty.
                if (Count == 0)
                {
                    return otherAsCollection.Count == 0;
                }

                // Faster if other is a hashset and we're using same equality comparer.
                if (other is HashSet<T> otherAsSet && EqualityComparersAreEqual(this, otherAsSet))
                {
                    // Attempt to return early: since both contain unique elements, if they have
                    // different counts, then they can't be equal.
                    if (Count != otherAsSet.Count)
                    {
                        return false;
                    }

                    // Already confirmed that the sets have the same number of distinct elements, so if
                    // one is a subset of the other then they must be equal.
                    return IsSubsetOfHashSetWithSameComparer(otherAsSet);
                }

                // Can't be equal if other set contains fewer elements than this.
                if (Count > otherAsCollection.Count)
                {
                    return false;
                }
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
            return uniqueCount == Count && unfoundCount == 0;
        }

        public void CopyTo(T[] array) => CopyTo(array, 0, Count);

        /// <summary>Copies the elements of a <see cref="HashSet{T}"/> object to an array, starting at the specified array index.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
        public void CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);

        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            // Will the array, starting at arrayIndex, be able to hold elements? Note: not
            // checking arrayIndex >= array.Length (consistency with list of allowing
            // count of 0; subsequent check takes care of the rest)
            if (arrayIndex > array.Length || count > array.Length - arrayIndex)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            Entry[]? entries = _entries;
            for (int i = 0; i < _count && count != 0; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    array[arrayIndex++] = entry.Value;
                    count--;
                }
            }
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate from a <see cref="HashSet{T}"/> collection.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.match);
            }

            Entry[]? entries = _entries;
            int numRemoved = 0;
            for (int i = 0; i < _count; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    // Cache value in case delegate removes it
                    T value = entry.Value;
                    if (match(value))
                    {
                        // Check again that remove actually removed it.
                        if (Remove(value))
                        {
                            numRemoved++;
                        }
                    }
                }
            }

            return numRemoved;
        }

        /// <summary>Gets the <see cref="IEqualityComparer"/> object that is used to determine equality for the values in the set.</summary>
        public IEqualityComparer<T> Comparer
        {
            get
            {
                if (typeof(T) == typeof(string))
                {
                    return (IEqualityComparer<T>)IInternalStringEqualityComparer.GetUnderlyingEqualityComparer((IEqualityComparer<string?>)_comparer);
                }
                else
                {
                    return _comparer ?? EqualityComparer<T>.Default;
                }
            }
        }

        /// <summary>
        /// Similar to <see cref="Comparer"/> but surfaces the actual comparer being used to hash entries.
        /// </summary>
        internal IEqualityComparer<T> EffectiveComparer => _comparer ?? EqualityComparer<T>.Default;

        /// <summary>Ensures that this hash set can hold the specified number of elements without growing.</summary>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            int currentCapacity = _entries == null ? 0 : _entries.Length;
            if (currentCapacity >= capacity)
            {
                return currentCapacity;
            }

            if (_buckets == null)
            {
                return Initialize(capacity);
            }

            int newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize, forceNewHashCodes: false);
            return newSize;
        }

        private void Resize() => Resize(HashHelpers.ExpandPrime(_count), forceNewHashCodes: false);

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            var entries = new Entry[newSize];

            int count = _count;
            Array.Copy(_entries, entries, count);

            if (!typeof(T).IsValueType && forceNewHashCodes)
            {
                IEqualityComparer<T> comparer = _comparer = (IEqualityComparer<T>)((NonRandomizedStringEqualityComparer)_comparer).GetRandomizedEqualityComparer();

                for (int i = 0; i < count; i++)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.Next >= -1)
                    {
                        entry.HashCode = entry.Value != null ? comparer.GetHashCode(entry.Value) : 0;
                    }
                }
            }

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new int[newSize];
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
#endif
            for (int i = 0; i < count; i++)
            {
                ref Entry entry = ref entries[i];
                if (entry.Next >= -1)
                {
                    ref int bucket = ref GetBucketRef(entry.HashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = i + 1;
                }
            }

            _entries = entries;
        }

        private void CopyEntries(Entry[] entries, int count)
        {
            Entry[] newEntries = _entries;
            int newCount = 0;
            for (int i = 0; i < count; i++)
            {
                int hashCode = entries[i].HashCode;
                if (entries[i].Next >= -1)
                {
                    ref Entry entry = ref newEntries[newCount];
                    entry = entries[i];
                    ref int bucket = ref GetBucketRef(hashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = newCount + 1;
                    newCount++;
                }
            }

            _count = newCount;
            _freeCount = 0;
        }

        /// <summary>
        /// Sets the capacity of a <see cref="HashSet{T}"/> object to the actual number of elements it contains,
        /// rounded up to a nearby, implementation-specific value.
        /// </summary>
        public void TrimExcess() => TrimExcess(Count);

        /// <summary>
        /// Sets the capacity of a <see cref="HashSet{T}"/> object to the specified number of entries,
        /// rounded up to a nearby, implementation-specific value.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException">Passed capacity is lower than entries count.</exception>
        public void TrimExcess(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, Count);

            int newSize = HashHelpers.GetPrimeAtLeast(capacity);
            Entry[]? oldEntries = _entries;
            if (oldEntries is null || newSize >= oldEntries.Length)
            {
                return;
            }

            _version++;

            var buckets = new int[newSize];
            var entries = new Entry[newSize];

            // Assign member variables after both arrays are allocated to guard against corruption from OOM if second fails.
            _freeList = -1;
            _buckets = buckets;
            _entries = entries;
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
#endif

            CopyEntries(oldEntries, _count);
        }

        #endregion

        #region Helper methods

        /// <summary>Returns an <see cref="IEqualityComparer"/> object that can be used for equality testing of a <see cref="HashSet{T}"/> object.</summary>
        public static IEqualityComparer<HashSet<T>> CreateSetComparer() => new HashSetEqualityComparer<T>();

        /// <summary>
        /// Initializes buckets and slots arrays. Uses suggested capacity by finding next prime
        /// greater than or equal to capacity.
        /// </summary>
        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            var buckets = new int[size];
            var entries = new Entry[size];

            // Assign member variables after both arrays are allocated to guard against corruption from OOM if second fails.
            _freeList = -1;
            _buckets = buckets;
            _entries = entries;
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
#endif

            return size;
        }

        /// <summary>Adds the specified element to the set if it's not already contained.</summary>
        /// <param name="value">The element to add to the set.</param>
        /// <param name="location">The index into <see cref="_entries"/> of the element.</param>
        /// <returns>true if the element is added to the <see cref="HashSet{T}"/> object; false if the element is already present.</returns>
        private bool AddIfNotPresent(T value, out int location)
        {
            if (_buckets == null)
            {
                Initialize(0);
            }

            Entry[]? entries = _entries;

            IEqualityComparer<T>? comparer = _comparer;
            int hashCode;

            uint collisionCount = 0;
            ref int bucket = ref Unsafe.NullRef<int>();

            if (typeof(T).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                comparer == null)
            {
                hashCode = value!.GetHashCode();
                bucket = ref GetBucketRef(hashCode);
                int i = bucket - 1; // Value in _buckets is 1-based

                // ValueType: Devirtualize with EqualityComparer<TValue>.Default intrinsic
                while ((uint)i < (uint)entries.Length)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.HashCode == hashCode && EqualityComparer<T>.Default.Equals(entry.Value, value))
                    {
                        location = i;
                        return false;
                    }
                    i = entry.Next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop, which means a concurrent update has happened.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }
            else
            {
                hashCode = value != null ? comparer.GetHashCode(value) : 0;
                bucket = ref GetBucketRef(hashCode);
                int i = bucket - 1; // Value in _buckets is 1-based
                while ((uint)i < (uint)entries.Length)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.HashCode == hashCode && comparer.Equals(entry.Value, value))
                    {
                        location = i;
                        return false;
                    }
                    i = entry.Next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop, which means a concurrent update has happened.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeCount--;
                _freeList = StartOfFreeList - entries[_freeList].Next;
            }
            else
            {
                int count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucketRef(hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            {
                ref Entry entry = ref entries![index];
                entry.HashCode = hashCode;
                entry.Next = bucket - 1; // Value in _buckets is 1-based
                entry.Value = value;
                bucket = index + 1;
                _version++;
                location = index;
            }

            // Value types never rehash
            if (!typeof(T).IsValueType && collisionCount > HashHelpers.HashCollisionThreshold && comparer is NonRandomizedStringEqualityComparer)
            {
                // If we hit the collision threshold we'll need to switch to the comparer which is using randomized string hashing
                // i.e. EqualityComparer<string>.Default.
                Resize(entries.Length, forceNewHashCodes: true);
                location = FindItemIndex(value);
            }

            return true;
        }

        /// <summary>
        /// Implementation Notes:
        /// If other is a hashset and is using same equality comparer, then checking subset is
        /// faster. Simply check that each element in this is in other.
        ///
        /// Note: if other doesn't use same equality comparer, then Contains check is invalid,
        /// which is why callers must take are of this.
        ///
        /// If callers are concerned about whether this is a proper subset, they take care of that.
        /// </summary>
        internal bool IsSubsetOfHashSetWithSameComparer(HashSet<T> other)
        {
            foreach (T item in this)
            {
                if (!other.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// If other is a hashset that uses same equality comparer, intersect is much faster
        /// because we can use other's Contains
        /// </summary>
        private void IntersectWithHashSetWithSameComparer(HashSet<T> other)
        {
            Entry[]? entries = _entries;
            for (int i = 0; i < _count; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    T item = entry.Value;
                    if (!other.Contains(item))
                    {
                        Remove(item);
                    }
                }
            }
        }

        /// <summary>
        /// Iterate over other. If contained in this, mark an element in bit array corresponding to
        /// its position in _slots. If anything is unmarked (in bit array), remove it.
        ///
        /// This attempts to allocate on the stack, if below StackAllocThreshold.
        /// </summary>
        private unsafe void IntersectWithEnumerable(IEnumerable<T> other)
        {
            // Keep track of current last index; don't want to move past the end of our bit array
            // (could happen if another thread is modifying the collection).
            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThreshold];
            BitHelper bitHelper = (uint)intArrayLength <= StackAllocThreshold ?
                new BitHelper(span.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            // Mark if contains: find index of in slots array and mark corresponding element in bit array.
            foreach (T item in other)
            {
                int index = FindItemIndex(item);
                if (index >= 0)
                {
                    bitHelper.MarkBit(index);
                }
            }

            // If anything unmarked, remove it. Perf can be optimized here if BitHelper had a
            // FindFirstUnmarked method.
            for (int i = 0; i < originalCount; i++)
            {
                ref Entry entry = ref _entries![i];
                if (entry.Next >= -1 && !bitHelper.IsMarked(i))
                {
                    Remove(entry.Value);
                }
            }
        }

        /// <summary>
        /// if other is a set, we can assume it doesn't have duplicate elements, so use this
        /// technique: if can't remove, then it wasn't present in this set, so add.
        ///
        /// As with other methods, callers take care of ensuring that other is a hashset using the
        /// same equality comparer.
        /// </summary>
        /// <param name="other"></param>
        private void SymmetricExceptWithUniqueHashSet(HashSet<T> other)
        {
            foreach (T item in other)
            {
                if (!Remove(item))
                {
                    AddIfNotPresent(item, out _);
                }
            }
        }

        /// <summary>
        /// Implementation notes:
        ///
        /// Used for symmetric except when other isn't a HashSet. This is more tedious because
        /// other may contain duplicates. HashSet technique could fail in these situations:
        /// 1. Other has a duplicate that's not in this: HashSet technique would add then
        /// remove it.
        /// 2. Other has a duplicate that's in this: HashSet technique would remove then add it
        /// back.
        /// In general, its presence would be toggled each time it appears in other.
        ///
        /// This technique uses bit marking to indicate whether to add/remove the item. If already
        /// present in collection, it will get marked for deletion. If added from other, it will
        /// get marked as something not to remove.
        ///
        /// </summary>
        /// <param name="other"></param>
        private unsafe void SymmetricExceptWithEnumerable(IEnumerable<T> other)
        {
            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> itemsToRemoveSpan = stackalloc int[StackAllocThreshold / 2];
            BitHelper itemsToRemove = intArrayLength <= StackAllocThreshold / 2 ?
                new BitHelper(itemsToRemoveSpan.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            Span<int> itemsAddedFromOtherSpan = stackalloc int[StackAllocThreshold / 2];
            BitHelper itemsAddedFromOther = intArrayLength <= StackAllocThreshold / 2 ?
                new BitHelper(itemsAddedFromOtherSpan.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            foreach (T item in other)
            {
                int location;
                if (AddIfNotPresent(item, out location))
                {
                    // wasn't already present in collection; flag it as something not to remove
                    // *NOTE* if location is out of range, we should ignore. BitHelper will
                    // detect that it's out of bounds and not try to mark it. But it's
                    // expected that location could be out of bounds because adding the item
                    // will increase _lastIndex as soon as all the free spots are filled.
                    itemsAddedFromOther.MarkBit(location);
                }
                else
                {
                    // already there...if not added from other, mark for remove.
                    // *NOTE* Even though BitHelper will check that location is in range, we want
                    // to check here. There's no point in checking items beyond originalCount
                    // because they could not have been in the original collection
                    if (location < originalCount && !itemsAddedFromOther.IsMarked(location))
                    {
                        itemsToRemove.MarkBit(location);
                    }
                }
            }

            // if anything marked, remove it
            for (int i = 0; i < originalCount; i++)
            {
                if (itemsToRemove.IsMarked(i))
                {
                    Remove(_entries![i].Value);
                }
            }
        }

        /// <summary>
        /// Determines counts that can be used to determine equality, subset, and superset. This
        /// is only used when other is an IEnumerable and not a HashSet. If other is a HashSet
        /// these properties can be checked faster without use of marking because we can assume
        /// other has no duplicates.
        ///
        /// The following count checks are performed by callers:
        /// 1. Equals: checks if unfoundCount = 0 and uniqueFoundCount = _count; i.e. everything
        /// in other is in this and everything in this is in other
        /// 2. Subset: checks if unfoundCount >= 0 and uniqueFoundCount = _count; i.e. other may
        /// have elements not in this and everything in this is in other
        /// 3. Proper subset: checks if unfoundCount > 0 and uniqueFoundCount = _count; i.e
        /// other must have at least one element not in this and everything in this is in other
        /// 4. Proper superset: checks if unfound count = 0 and uniqueFoundCount strictly less
        /// than _count; i.e. everything in other was in this and this had at least one element
        /// not contained in other.
        ///
        /// An earlier implementation used delegates to perform these checks rather than returning
        /// an ElementCount struct; however this was changed due to the perf overhead of delegates.
        /// </summary>
        /// <param name="other"></param>
        /// <param name="returnIfUnfound">Allows us to finish faster for equals and proper superset
        /// because unfoundCount must be 0.</param>
        private unsafe (int UniqueCount, int UnfoundCount) CheckUniqueAndUnfoundElements(IEnumerable<T> other, bool returnIfUnfound)
        {
            // Need special case in case this has no elements.
            if (_count == 0)
            {
                int numElementsInOther = 0;
                foreach (T item in other)
                {
                    numElementsInOther++;
                    break; // break right away, all we want to know is whether other has 0 or 1 elements
                }

                return (UniqueCount: 0, UnfoundCount: numElementsInOther);
            }

            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThreshold];
            BitHelper bitHelper = intArrayLength <= StackAllocThreshold ?
                new BitHelper(span.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            int unfoundCount = 0; // count of items in other not found in this
            int uniqueFoundCount = 0; // count of unique items in other found in this

            foreach (T item in other)
            {
                int index = FindItemIndex(item);
                if (index >= 0)
                {
                    if (!bitHelper.IsMarked(index))
                    {
                        // Item hasn't been seen yet.
                        bitHelper.MarkBit(index);
                        uniqueFoundCount++;
                    }
                }
                else
                {
                    unfoundCount++;
                    if (returnIfUnfound)
                    {
                        break;
                    }
                }
            }

            return (uniqueFoundCount, unfoundCount);
        }

        /// <summary>
        /// Checks if equality comparers are equal. This is used for algorithms that can
        /// speed up if it knows the other item has unique elements. I.e. if they're using
        /// different equality comparers, then uniqueness assumption between sets break.
        /// </summary>
        internal static bool EqualityComparersAreEqual(HashSet<T> set1, HashSet<T> set2) => set1.Comparer.Equals(set2.Comparer);

        /// <summary>
        /// Checks if effective equality comparers are equal. This is used for algorithms that
        /// require that both collections use identical hashing implementations for their entries.
        /// </summary>
        internal static bool EffectiveEqualityComparersAreEqual(HashSet<T> set1, HashSet<T> set2) => set1.EffectiveComparer.Equals(set2.EffectiveComparer);


        #endregion

        private struct Entry
        {
            public int HashCode;
            /// <summary>
            /// 0-based index of next entry in chain: -1 means end of chain
            /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            /// </summary>
            public int Next;
            public T Value;
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly HashSet<T> _hashSet;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(HashSet<T> hashSet)
            {
                _hashSet = hashSet;
                _version = hashSet._version;
                _index = 0;
                _current = default!;
            }

            public bool MoveNext()
            {
                if (_version != _hashSet._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_hashSet._count)
                {
                    ref Entry entry = ref _hashSet._entries![_index++];
                    if (entry.Next >= -1)
                    {
                        _current = entry.Value;
                        return true;
                    }
                }

                _index = _hashSet._count + 1;
                _current = default!;
                return false;
            }

            public T Current => _current;

            public void Dispose() { }

            object? IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || (_index == _hashSet._count + 1))
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    return _current;
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _hashSet._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                _index = 0;
                _current = default!;
            }
        }
    }

    /// <summary>
    /// Represents a first-in, first-out collection of objects.
    /// </summary>
    /// <remarks>
    /// Implemented as a circular buffer, so <see cref="Enqueue(T)"/> and <see cref="Dequeue"/> are typically <c>O(1)</c>.
    /// </remarks>
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public class Queue<T> : IEnumerable<T>,
        ICollection,
        IReadOnlyCollection<T>
    {
        private T[] _array;
        private int _head;       // The index from which to dequeue if the queue isn't empty.
        private int _tail;       // The index at which to enqueue if the queue isn't full.
        private int _size;       // Number of elements.
        private int _version;

        // Creates a queue with room for capacity objects. The default initial
        // capacity and grow factor are used.
        public Queue()
        {
            _array = [];
        }

        // Creates a queue with room for capacity objects. The default grow factor
        // is used.
        public Queue(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            _array = new T[capacity];
        }

        // Fills a Queue with the elements of an ICollection.  Uses the enumerator
        // to get each of the elements.
        public Queue(IEnumerable<T> collection)
        {
            ArgumentNullException.ThrowIfNull(collection);

            _array = EnumerableHelpers.ToArray(collection, out _size);
            if (_size != _array.Length) _tail = _size;
        }

        public int Count => _size;

        /// <summary>
        /// Gets the total numbers of elements the internal data structure can hold without resizing.
        /// </summary>
        public int Capacity => _array.Length;

        /// <inheritdoc cref="ICollection.IsSynchronized" />
        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        // Removes all Objects from the queue.
        public void Clear()
        {
            if (_size != 0)
            {
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    if (_head < _tail)
                    {
                        Array.Clear(_array, _head, _size);
                    }
                    else
                    {
                        Array.Clear(_array, _head, _array.Length - _head);
                        Array.Clear(_array, 0, _tail);
                    }
                }

                _size = 0;
            }

            _head = 0;
            _tail = 0;
            _version++;
        }

        // CopyTo copies a collection into an Array, starting at a particular
        // index into the array.
        public void CopyTo(T[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.arrayIndex, ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }

            if (array.Length - arrayIndex < _size)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidOffLen);
            }

            int numToCopy = _size;
            if (numToCopy == 0) return;

            int firstPart = Math.Min(_array.Length - _head, numToCopy);
            Array.Copy(_array, _head, array, arrayIndex, firstPart);
            numToCopy -= firstPart;
            if (numToCopy > 0)
            {
                Array.Copy(_array, 0, array, arrayIndex + _array.Length - _head, numToCopy);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (array.Rank != 1)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported, ExceptionArgument.array);
            }

            if (array.GetLowerBound(0) != 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound, ExceptionArgument.array);
            }

            int arrayLen = array.Length;
            if (index < 0 || index > arrayLen)
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessOrEqualException();
            }

            if (arrayLen - index < _size)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidOffLen);
            }

            int numToCopy = _size;
            if (numToCopy == 0) return;

            try
            {
                int firstPart = (_array.Length - _head < numToCopy) ? _array.Length - _head : numToCopy;
                Array.Copy(_array, _head, array, index, firstPart);
                numToCopy -= firstPart;

                if (numToCopy > 0)
                {
                    Array.Copy(_array, 0, array, index + _array.Length - _head, numToCopy);
                }
            }
            catch (ArrayTypeMismatchException)
            {
                ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
            }
        }

        // Adds item to the tail of the queue.
        public void Enqueue(T item)
        {
            if (_size == _array.Length)
            {
                Grow(_size + 1);
            }

            _array[_tail] = item;
            MoveNext(ref _tail);
            _size++;
            _version++;
        }

        // GetEnumerator returns an IEnumerator over this Queue.  This
        // Enumerator will support removing.
        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <internalonly/>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
            Count == 0 ? SZGenericArrayEnumerator<T>.Empty :
            GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        // Removes the object at the head of the queue and returns it. If the queue
        // is empty, this method throws an
        // InvalidOperationException.
        public T Dequeue()
        {
            int head = _head;
            T[] array = _array;

            if (_size == 0)
            {
                ThrowForEmptyQueue();
            }

            T removed = array[head];
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                array[head] = default!;
            }
            MoveNext(ref _head);
            _size--;
            _version++;
            return removed;
        }

        public bool TryDequeue([MaybeNullWhen(false)] out T result)
        {
            int head = _head;
            T[] array = _array;

            if (_size == 0)
            {
                result = default;
                return false;
            }

            result = array[head];
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                array[head] = default!;
            }
            MoveNext(ref _head);
            _size--;
            _version++;
            return true;
        }

        // Returns the object at the head of the queue. The object remains in the
        // queue. If the queue is empty, this method throws an
        // InvalidOperationException.
        public T Peek()
        {
            if (_size == 0)
            {
                ThrowForEmptyQueue();
            }

            return _array[_head];
        }

        public bool TryPeek([MaybeNullWhen(false)] out T result)
        {
            if (_size == 0)
            {
                result = default;
                return false;
            }

            result = _array[_head];
            return true;
        }

        // Returns true if the queue contains at least one object equal to item.
        // Equality is determined using EqualityComparer<T>.Default.Equals().
        public bool Contains(T item)
        {
            if (_size == 0)
            {
                return false;
            }

            if (_head < _tail)
            {
                return Array.IndexOf(_array, item, _head, _size) >= 0;
            }

            // We've wrapped around. Check both partitions, the least recently enqueued first.
            return
                Array.IndexOf(_array, item, _head, _array.Length - _head) >= 0 ||
                Array.IndexOf(_array, item, 0, _tail) >= 0;
        }

        // Iterates over the objects in the queue, returning an array of the
        // objects in the Queue, or an empty array if the queue is empty.
        // The order of elements in the array is first in to last in, the same
        // order produced by successive calls to Dequeue.
        public T[] ToArray()
        {
            if (_size == 0)
            {
                return [];
            }

            T[] arr = new T[_size];

            if (_head < _tail)
            {
                Array.Copy(_array, _head, arr, 0, _size);
            }
            else
            {
                Array.Copy(_array, _head, arr, 0, _array.Length - _head);
                Array.Copy(_array, 0, arr, _array.Length - _head, _tail);
            }

            return arr;
        }

        // PRIVATE Grows or shrinks the buffer to hold capacity objects. Capacity
        // must be >= _size.
        private void SetCapacity(int capacity)
        {
            T[] newarray = new T[capacity];
            if (_size > 0)
            {
                if (_head < _tail)
                {
                    Array.Copy(_array, _head, newarray, 0, _size);
                }
                else
                {
                    Array.Copy(_array, _head, newarray, 0, _array.Length - _head);
                    Array.Copy(_array, 0, newarray, _array.Length - _head, _tail);
                }
            }

            _array = newarray;
            _head = 0;
            _tail = (_size == capacity) ? 0 : _size;
            _version++;
        }

        // Increments the index wrapping it if necessary.
        private void MoveNext(ref int index)
        {
            // It is tempting to use the remainder operator here but it is actually much slower
            // than a simple comparison and a rarely taken branch.
            // JIT produces better code than with ternary operator ?:
            int tmp = index + 1;
            if (tmp == _array.Length)
            {
                tmp = 0;
            }
            index = tmp;
        }

        private void ThrowForEmptyQueue()
        {
            throw new InvalidOperationException(SR.InvalidOperation_EmptyQueue);
        }

        public void TrimExcess()
        {
            int threshold = (int)(_array.Length * 0.9);
            if (_size < threshold)
            {
                SetCapacity(_size);
            }
        }

        /// <summary>
        /// Sets the capacity of a <see cref="Queue{T}"/> object to the specified number of entries.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException">Passed capacity is lower than entries count.</exception>
        public void TrimExcess(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, _size);

            if (capacity == _array.Length)
                return;

            SetCapacity(capacity);
        }

        /// <summary>
        /// Ensures that the capacity of this Queue is at least the specified <paramref name="capacity"/>.
        /// </summary>
        /// <param name="capacity">The minimum capacity to ensure.</param>
        /// <returns>The new capacity of this queue.</returns>
        public int EnsureCapacity(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);

            if (_array.Length < capacity)
            {
                Grow(capacity);
            }

            return _array.Length;
        }

        private void Grow(int capacity)
        {
            const int GrowFactor = 2;
            const int MinimumGrow = 4;

            int newcapacity = GrowFactor * _array.Length;

            // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newcapacity > Array.MaxLength) newcapacity = Array.MaxLength;

            // Ensure minimum growth is respected.
            newcapacity = Math.Max(newcapacity, _array.Length + MinimumGrow);

            // If the computed capacity is still less than specified, set to the original argument.
            // Capacities exceeding Array.MaxLength will be surfaced as OutOfMemoryException by Array.Resize.
            if (newcapacity < capacity) newcapacity = capacity;

            SetCapacity(newcapacity);
        }

        // Implements an enumerator for a Queue.  The enumerator uses the
        // internal version number of the list to ensure that no modifications are
        // made to the list while an enumeration is in progress.
        public struct Enumerator : IEnumerator<T>,
            IEnumerator
        {
            private readonly Queue<T> _queue;
            private readonly int _version;
            private int _i;
            private T? _currentElement;

            internal Enumerator(Queue<T> queue)
            {
                _queue = queue;
                _version = queue._version;
                _i = -1;
                _currentElement = default;
            }

            public void Dispose()
            {
                _i = -2;
                _currentElement = default;
            }

            public bool MoveNext()
            {
                if (_version != _queue._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                Queue<T> q = _queue;
                int size = q._size;

                int offset = _i + 1;
                if ((uint)offset < (uint)size)
                {
                    _i = offset;

                    T[] array = q._array;
                    int index = q._head + offset;
                    if ((uint)index < (uint)array.Length)
                    {
                        _currentElement = array[index];
                    }
                    else
                    {
                        // The index has wrapped around the end of the array. Shift the index and then
                        // get the current element. It is tempting to dedup this dereferencing with that
                        // in the if block above, but the if block above avoids a bounds check for the
                        // accesses that are in that portion, whereas these still incur it.
                        index -= array.Length;
                        _currentElement = array[index];
                    }

                    return true;
                }

                _i = -2;
                _currentElement = default;
                return false;
            }

            public T Current => _currentElement!;

            object? IEnumerator.Current => Current;

            void IEnumerator.Reset()
            {
                if (_version != _queue._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                _i = -1;
                _currentElement = default;
            }
        }
    }

    // A simple stack of objects.  Internally it is implemented as an array,
    // so Push can be O(n).  Pop is O(1).
    /// <summary>
    /// Represents a last-in, first-out collection of objects.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public class Stack<T> : IEnumerable<T>,
        System.Collections.ICollection,
        IReadOnlyCollection<T>
    {
        private T[] _array; // Storage for stack elements. Do not rename (binary serialization)
        private int _size; // Number of items in the stack. Do not rename (binary serialization)
        private int _version; // Used to keep enumerator in sync w/ collection. Do not rename (binary serialization)

        private const int DefaultCapacity = 4;

        public Stack()
        {
            _array = Array.Empty<T>();
        }

        // Create a stack with a specific initial capacity.  The initial capacity
        // must be a non-negative number.
        public Stack(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            _array = new T[capacity];
        }

        // Fills a Stack with the contents of a particular collection.  The items are
        // pushed onto the stack in the same order they are read by the enumerator.
        public Stack(IEnumerable<T> collection)
        {
            ArgumentNullException.ThrowIfNull(collection);

            _array = EnumerableHelpers.ToArray(collection, out _size);
        }

        public int Count => _size;


        /// <summary>
        /// Gets the total numbers of elements the internal data structure can hold without resizing.
        /// </summary>
        public int Capacity => _array.Length;

        /// <inheritdoc cref="ICollection.IsSynchronized" />
        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        // Removes all Objects from the Stack.
        public void Clear()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(_array, 0, _size); // Don't need to doc this but we clear the elements so that the gc can reclaim the references.
            }
            _size = 0;
            _version++;
        }

        public bool Contains(T item)
        {
            // Compare items using the default equality comparer

            // PERF: Internally Array.LastIndexOf calls
            // EqualityComparer<T>.Default.LastIndexOf, which
            // is specialized for different types. This
            // boosts performance since instead of making a
            // virtual method call each iteration of the loop,
            // via EqualityComparer<T>.Default.Equals, we
            // only make one virtual call to EqualityComparer.LastIndexOf.

            return _size != 0 && Array.LastIndexOf(_array, item, _size - 1) >= 0;
        }

        // Copies the stack into an array.
        public void CopyTo(T[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }

            if (array.Length - arrayIndex < _size)
            {
                throw new ArgumentException(SR.Argument_InvalidOffLen);
            }

            int srcIndex = 0;
            int dstIndex = arrayIndex + _size;
            while (srcIndex < _size)
            {
                array[--dstIndex] = _array[srcIndex++];
            }
        }

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (array.Rank != 1)
            {
                throw new ArgumentException(SR.Arg_RankMultiDimNotSupported, nameof(array));
            }

            if (array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException(SR.Arg_NonZeroLowerBound, nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }

            if (array.Length - arrayIndex < _size)
            {
                throw new ArgumentException(SR.Argument_InvalidOffLen);
            }

            try
            {
                Array.Copy(_array, 0, array, arrayIndex, _size);
                Array.Reverse(array, arrayIndex, _size);
            }
            catch (ArrayTypeMismatchException)
            {
                throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
            }
        }

        // Returns an IEnumerator for this Stack.
        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <internalonly/>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
            Count == 0 ? EnumerableHelpers.GetEmptyEnumerator<T>() :
            GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        public void TrimExcess()
        {
            int threshold = (int)(_array.Length * 0.9);
            if (_size < threshold)
            {
                Array.Resize(ref _array, _size);
            }
        }

        /// <summary>
        /// Sets the capacity of a <see cref="Stack{T}"/> object to a specified number of entries.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException">Passed capacity is lower than 0 or entries count.</exception>
        public void TrimExcess(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, _size);

            if (capacity == _array.Length)
                return;

            Array.Resize(ref _array, capacity);
        }

        // Returns the top object on the stack without removing it.  If the stack
        // is empty, Peek throws an InvalidOperationException.
        public T Peek()
        {
            int size = _size - 1;
            T[] array = _array;

            if ((uint)size >= (uint)array.Length)
            {
                ThrowForEmptyStack();
            }

            return array[size];
        }

        public bool TryPeek([MaybeNullWhen(false)] out T result)
        {
            int size = _size - 1;
            T[] array = _array;

            if ((uint)size >= (uint)array.Length)
            {
                result = default!;
                return false;
            }
            result = array[size];
            return true;
        }

        // Pops an item from the top of the stack.  If the stack is empty, Pop
        // throws an InvalidOperationException.
        public T Pop()
        {
            int size = _size - 1;
            T[] array = _array;

            // if (_size == 0) is equivalent to if (size == -1), and this case
            // is covered with (uint)size, thus allowing bounds check elimination
            // https://github.com/dotnet/coreclr/pull/9773
            if ((uint)size >= (uint)array.Length)
            {
                ThrowForEmptyStack();
            }

            _version++;
            _size = size;
            T item = array[size];
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                array[size] = default!;     // Free memory quicker.
            }
            return item;
        }

        public bool TryPop([MaybeNullWhen(false)] out T result)
        {
            int size = _size - 1;
            T[] array = _array;

            if ((uint)size >= (uint)array.Length)
            {
                result = default!;
                return false;
            }

            _version++;
            _size = size;
            result = array[size];
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                array[size] = default!;
            }
            return true;
        }

        // Pushes an item to the top of the stack.
        public void Push(T item)
        {
            int size = _size;
            T[] array = _array;

            if ((uint)size < (uint)array.Length)
            {
                array[size] = item;
                _version++;
                _size = size + 1;
            }
            else
            {
                PushWithResize(item);
            }
        }

        // Non-inline from Stack.Push to improve its code quality as uncommon path
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PushWithResize(T item)
        {
            Grow(_size + 1);
            _array[_size] = item;
            _version++;
            _size++;
        }

        /// <summary>
        /// Ensures that the capacity of this Stack is at least the specified <paramref name="capacity"/>.
        /// If the current capacity of the Stack is less than specified <paramref name="capacity"/>,
        /// the capacity is increased by continuously twice current capacity until it is at least the specified <paramref name="capacity"/>.
        /// </summary>
        /// <param name="capacity">The minimum capacity to ensure.</param>
        /// <returns>The new capacity of this stack.</returns>
        public int EnsureCapacity(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);

            if (_array.Length < capacity)
            {
                Grow(capacity);
            }

            return _array.Length;
        }

        private void Grow(int capacity)
        {
            int newcapacity = _array.Length == 0 ? DefaultCapacity : 2 * _array.Length;

            // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast.
            if ((uint)newcapacity > Array.MaxLength) newcapacity = Array.MaxLength;

            // If computed capacity is still less than specified, set to the original argument.
            // Capacities exceeding Array.MaxLength will be surfaced as OutOfMemoryException by Array.Resize.
            if (newcapacity < capacity) newcapacity = capacity;

            Array.Resize(ref _array, newcapacity);
        }

        // Copies the Stack to an array, in the same order Pop would return the items.
        public T[] ToArray()
        {
            if (_size == 0)
                return Array.Empty<T>();

            T[] objArray = new T[_size];
            int i = 0;
            while (i < _size)
            {
                objArray[i] = _array[_size - i - 1];
                i++;
            }
            return objArray;
        }

        private void ThrowForEmptyStack()
        {
            throw new InvalidOperationException(SR.InvalidOperation_EmptyStack);
        }

        public struct Enumerator : IEnumerator<T>, System.Collections.IEnumerator
        {
            private readonly Stack<T> _stack;
            private readonly int _version;
            private int _index;
            private T? _currentElement;

            internal Enumerator(Stack<T> stack)
            {
                _stack = stack;
                _version = stack._version;
                _index = stack.Count;
                _currentElement = default;
            }

            public void Dispose() => _index = -1;

            public bool MoveNext()
            {
                if (_version != _stack._version)
                {
                    ThrowInvalidVersion();
                }

                T[] array = _stack._array;
                int index = _index - 1;
                if ((uint)index < (uint)array.Length)
                {
                    _currentElement = array[index];
                    _index = index;
                    return true;
                }

                _currentElement = default;
                _index = -1;
                return false;
            }

            public T Current => _currentElement!;

            object? System.Collections.IEnumerator.Current => Current;

            void IEnumerator.Reset()
            {
                if (_version != _stack._version)
                {
                    ThrowInvalidVersion();
                }

                _index = _stack.Count;
                _currentElement = default;
            }

            private static void ThrowInvalidVersion() => throw new InvalidOperationException(SR.InvalidOperation_EnumFailedVersion);
        }
    }

    /// <summary>Internal helper functions for working with enumerables.</summary>
    internal static class EnumerableHelpers
    {
        /// <summary>Gets an enumerator singleton for an empty collection.</summary>
        internal static IEnumerator<T> GetEmptyEnumerator<T>() => ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator();

        /// <summary>Converts an enumerable to an array using the same logic as List{T}.</summary>
        /// <param name="source">The enumerable to convert.</param>
        /// <param name="length">The number of items stored in the resulting array, 0-indexed.</param>
        /// <returns>
        /// The resulting array.  The length of the array may be greater than <paramref name="length"/>,
        /// which is the actual number of elements in the array.
        /// </returns>
        internal static T[] ToArray<T>(IEnumerable<T> source, out int length)
        {
            if (source is ICollection<T> ic)
            {
                int count = ic.Count;
                if (count != 0)
                {
                    // Allocate an array of the desired size, then copy the elements into it. Note that this has the same
                    // issue regarding concurrency as other existing collections like List<T>. If the collection size
                    // concurrently changes between the array allocation and the CopyTo, we could end up either getting an
                    // exception from overrunning the array (if the size went up) or we could end up not filling as many
                    // items as 'count' suggests (if the size went down).  This is only an issue for concurrent collections
                    // that implement ICollection<T>, which as of .NET 4.6 is just ConcurrentDictionary<TKey, TValue>.
                    T[] arr = new T[count];
                    ic.CopyTo(arr, 0);
                    length = count;
                    return arr;
                }
            }
            else
            {
                using (IEnumerator<T> en = source.GetEnumerator())
                {
                    if (en.MoveNext())
                    {
                        const int DefaultCapacity = 4;
                        T[] arr = new T[DefaultCapacity];
                        arr[0] = en.Current;
                        int count = 1;

                        while (en.MoveNext())
                        {
                            if (count == arr.Length)
                            {
                                // MaxLength = 2147483591, the largest array the runtime allows
                                const int MaxLength = 0x7FFFFFC7;

                                // This is the same growth logic as in List<T>:
                                // If the array is currently empty, we make it a default size.  Otherwise, we attempt to
                                // double the size of the array.  Doubling will overflow once the size of the array reaches
                                // 2^30, since doubling to 2^31 is 1 larger than Int32.MaxValue.  In that case, we instead
                                // constrain the length to be Array.MaxLength (this overflow check works because of the
                                // cast to uint).
                                int newLength = count << 1;
                                if ((uint)newLength > MaxLength)
                                {
                                    newLength = MaxLength <= count ? count + 1 : MaxLength;
                                }

                                Array.Resize(ref arr, newLength);
                            }

                            arr[count++] = en.Current;
                        }

                        length = count;
                        return arr;
                    }
                }
            }

            length = 0;
            return Array.Empty<T>();
        }
    }

    internal sealed class ArraySortHelper<T>
    {
        private static readonly ArraySortHelper<T> s_defaultArraySortHelper = new ArraySortHelper<T>();

        public static ArraySortHelper<T> Default
        {
            get
            {
                return s_defaultArraySortHelper;
            }
        }

        public void Sort(Span<T> keys, IComparer<T>? comparer)
        {
            // Add a try block here to detect IComparers (or their
            // underlying IComparables, etc) that are bogus.
            try
            {
                comparer ??= Comparer<T>.Default;
                IntrospectiveSort(keys, comparer.Compare);
            }
            catch (IndexOutOfRangeException)
            {
                ThrowHelper.ThrowArgumentException_BadComparer(comparer);
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_IComparerFailed, e);
            }
        }

        public int BinarySearch(T[] array, int index, int length, T value, IComparer<T>? comparer)
        {
            try
            {
                comparer ??= Comparer<T>.Default;
                return InternalBinarySearch(array, index, length, value, comparer);
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_IComparerFailed, e);
                return 0;
            }
        }

        internal static void Sort(Span<T> keys, Comparison<T> comparer)
        {
            // Add a try block here to detect bogus comparisons
            try
            {
                IntrospectiveSort(keys, comparer);
            }
            catch (IndexOutOfRangeException)
            {
                ThrowHelper.ThrowArgumentException_BadComparer(comparer);
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_IComparerFailed, e);
            }
        }

        internal static int InternalBinarySearch(T[] array, int index, int length, T value, IComparer<T> comparer)
        {
            int lo = index;
            int hi = index + length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                int order = comparer.Compare(array[i], value);

                if (order == 0) return i;
                if (order < 0)
                {
                    lo = i + 1;
                }
                else
                {
                    hi = i - 1;
                }
            }

            return ~lo;
        }

        private static void SwapIfGreater(Span<T> keys, Comparison<T> comparer, int i, int j)
        {
            if (comparer(keys[i], keys[j]) > 0)
            {
                T key = keys[i];
                keys[i] = keys[j];
                keys[j] = key;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap(Span<T> a, int i, int j)
        {
            T t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        internal static void IntrospectiveSort(Span<T> keys, Comparison<T> comparer)
        {
            if (keys.Length > 1)
            {
                IntroSort(keys, 2 * (BitOperations.Log2((uint)keys.Length) + 1), comparer);
            }
        }

        // IntroSort is recursive; block it from being inlined into itself as
        // this is currenly not profitable.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void IntroSort(Span<T> keys, int depthLimit, Comparison<T> comparer)
        {
            int partitionSize = keys.Length;
            while (partitionSize > 1)
            {
                if (partitionSize <= Array.IntrosortSizeThreshold)
                {

                    if (partitionSize == 2)
                    {
                        SwapIfGreater(keys, comparer, 0, 1);
                        return;
                    }

                    if (partitionSize == 3)
                    {
                        SwapIfGreater(keys, comparer, 0, 1);
                        SwapIfGreater(keys, comparer, 0, 2);
                        SwapIfGreater(keys, comparer, 1, 2);
                        return;
                    }

                    InsertionSort(keys.Slice(0, partitionSize), comparer);
                    return;
                }

                if (depthLimit == 0)
                {
                    HeapSort(keys.Slice(0, partitionSize), comparer);
                    return;
                }
                depthLimit--;

                int p = PickPivotAndPartition(keys.Slice(0, partitionSize), comparer);

                // Note we've already partitioned around the pivot and do not have to move the pivot again.
                IntroSort(keys[(p + 1)..partitionSize], depthLimit, comparer);
                partitionSize = p;
            }
        }

        private static int PickPivotAndPartition(Span<T> keys, Comparison<T> comparer)
        {
            int hi = keys.Length - 1;

            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int middle = hi >> 1;

            // Sort lo, mid and hi appropriately, then pick mid as the pivot.
            SwapIfGreater(keys, comparer, 0, middle);  // swap the low with the mid point
            SwapIfGreater(keys, comparer, 0, hi);   // swap the low with the high
            SwapIfGreater(keys, comparer, middle, hi); // swap the middle with the high

            T pivot = keys[middle];
            Swap(keys, middle, hi - 1);
            int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

            while (left < right)
            {
                while (comparer(keys[++left], pivot) < 0) ;
                while (comparer(pivot, keys[--right]) < 0) ;

                if (left >= right)
                    break;

                Swap(keys, left, right);
            }

            // Put pivot in the right location.
            if (left != hi - 1)
            {
                Swap(keys, left, hi - 1);
            }
            return left;
        }

        private static void HeapSort(Span<T> keys, Comparison<T> comparer)
        {
            int n = keys.Length;
            for (int i = n >> 1; i >= 1; i--)
            {
                DownHeap(keys, i, n, comparer);
            }

            for (int i = n; i > 1; i--)
            {
                Swap(keys, 0, i - 1);
                DownHeap(keys, 1, i - 1, comparer);
            }
        }

        private static void DownHeap(Span<T> keys, int i, int n, Comparison<T> comparer)
        {
            T d = keys[i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && comparer(keys[child - 1], keys[child]) < 0)
                {
                    child++;
                }

                if (!(comparer(d, keys[child - 1]) < 0))
                    break;

                keys[i - 1] = keys[child - 1];
                i = child;
            }

            keys[i - 1] = d;
        }

        private static void InsertionSort(Span<T> keys, Comparison<T> comparer)
        {
            for (int i = 0; i < keys.Length - 1; i++)
            {
                T t = keys[i + 1];

                int j = i;
                while (j >= 0 && comparer(t, keys[j]) < 0)
                {
                    keys[j + 1] = keys[j];
                    j--;
                }

                keys[j + 1] = t;
            }
        }
    }

    internal static class ArraySortHelper<TKey, TValue>
    {
        private static readonly ArraySortHelper<TKey, TValue> s_defaultArraySortHelper = new ArraySortHelper<TKey, TValue>();

        public static ArraySortHelper<TKey, TValue> Default
        {
            get
            {
                return s_defaultArraySortHelper;
            }
        }
    }

    /// <summary>Helper methods for use in array/span sorting routines.</summary>
    internal static class SortUtils
    {
        public static int MoveNansToFront<TKey, TValue>(Span<TKey> keys, Span<TValue> values) where TKey : notnull
        {
            int left = 0;

            for (int i = 0; i < keys.Length; i++)
            {
                if ((typeof(TKey) == typeof(double) && double.IsNaN((double)(object)keys[i])) ||
                    (typeof(TKey) == typeof(float) && float.IsNaN((float)(object)keys[i])) ||
                    (typeof(TKey) == typeof(Half) && Half.IsNaN((Half)(object)keys[i])))
                {
                    TKey temp = keys[left];
                    keys[left] = keys[i];
                    keys[i] = temp;

                    if ((uint)i < (uint)values.Length) // check to see if we have values
                    {
                        TValue tempValue = values[left];
                        values[left] = values[i];
                        values[i] = tempValue;
                    }

                    left++;
                }
            }

            return left;
        }
    }

    internal sealed class ArraySortHelperForTComparer<T, TComparer>
        where TComparer : IComparer<T>
    {
        internal static void Sort(Span<T> keys, TComparer comparer)
        {
            // Add a try block here to detect bogus comparisons
            try
            {
                IntrospectiveSort(keys, comparer);
            }
            catch (IndexOutOfRangeException)
            {
                ThrowHelper.ThrowArgumentException_BadComparer(comparer);
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_IComparerFailed, e);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap(Span<T> a, int i, int j)
        {
            T t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        private static void SwapIfGreater(Span<T> keys, TComparer comparer, int i, int j)
        {
            if (comparer.Compare(keys[i], keys[j]) > 0)
            {
                T key = keys[i];
                keys[i] = keys[j];
                keys[j] = key;
            }
        }

        internal static void IntrospectiveSort(Span<T> keys, TComparer comparer)
        {
            if (keys.Length > 1)
            {
                IntroSort(keys, 2 * (BitOperations.Log2((uint)keys.Length) + 1), comparer);
            }
        }

        // IntroSort is recursive; block it from being inlined into itself as
        // this is currenly not profitable.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void IntroSort(Span<T> keys, int depthLimit, TComparer comparer)
        {
            int partitionSize = keys.Length;
            while (partitionSize > 1)
            {
                if (partitionSize <= Array.IntrosortSizeThreshold)
                {
                    if (partitionSize == 2)
                    {
                        SwapIfGreater(keys, comparer, 0, 1);
                        return;
                    }

                    if (partitionSize == 3)
                    {
                        SwapIfGreater(keys, comparer, 0, 1);
                        SwapIfGreater(keys, comparer, 0, 2);
                        SwapIfGreater(keys, comparer, 1, 2);
                        return;
                    }

                    InsertionSort(keys.Slice(0, partitionSize), comparer);
                    return;
                }

                if (depthLimit == 0)
                {
                    HeapSort(keys.Slice(0, partitionSize), comparer);
                    return;
                }
                depthLimit--;

                int p = PickPivotAndPartition(keys.Slice(0, partitionSize), comparer);

                // Note we've already partitioned around the pivot and do not have to move the pivot again.
                IntroSort(keys[(p + 1)..partitionSize], depthLimit, comparer);
                partitionSize = p;
            }
        }

        private static int PickPivotAndPartition(Span<T> keys, TComparer comparer)
        {
            int hi = keys.Length - 1;

            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int middle = hi >> 1;

            // Sort lo, mid and hi appropriately, then pick mid as the pivot.
            SwapIfGreater(keys, comparer, 0, middle); // swap the low with the mid point
            SwapIfGreater(keys, comparer, 0, hi); // swap the low with the high
            SwapIfGreater(keys, comparer, middle, hi); // swap the middle with the high

            T pivot = keys[middle];
            Swap(keys, middle, hi - 1);
            int left = 0, right = hi - 1; // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

            while (left < right)
            {
                while (comparer.Compare(keys[++left], pivot) < 0) ;
                while (comparer.Compare(pivot, keys[--right]) < 0) ;

                if (left >= right)
                    break;

                Swap(keys, left, right);
            }

            // Put pivot in the right location.
            if (left != hi - 1)
            {
                Swap(keys, left, hi - 1);
            }
            return left;
        }

        private static void HeapSort(Span<T> keys, TComparer comparer)
        {
            int n = keys.Length;
            for (int i = n >> 1; i >= 1; i--)
            {
                DownHeap(keys, i, n, comparer);
            }

            for (int i = n; i > 1; i--)
            {
                Swap(keys, 0, i - 1);
                DownHeap(keys, 1, i - 1, comparer);
            }
        }

        private static void DownHeap(Span<T> keys, int i, int n, TComparer comparer)
        {
            T d = keys[i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && comparer.Compare(keys[child - 1], keys[child]) < 0)
                {
                    child++;
                }

                if (!(comparer.Compare(d, keys[child - 1]) < 0))
                    break;

                keys[i - 1] = keys[child - 1];
                i = child;
            }

            keys[i - 1] = d;
        }

        private static void InsertionSort(Span<T> keys, TComparer comparer)
        {
            for (int i = 0; i < keys.Length - 1; i++)
            {
                T t = keys[i + 1];

                int j = i;
                while (j >= 0 && comparer.Compare(t, keys[j]) < 0)
                {
                    keys[j + 1] = keys[j];
                    j--;
                }

                keys[j + 1] = t;
            }
        }
    }


    /// <summary>Provides a helper for efficiently building arrays and lists.</summary>
    /// <remarks>This is implemented as an inline array of rented arrays.</remarks>
    /// <typeparam name="T">Specifies the element type of the collection being built.</typeparam>
    internal ref struct SegmentedArrayBuilder<T>
    {
        /// <summary>The size to use for the first segment that's stack allocated by the caller.</summary>
        /// <remarks>
        /// This value needs to be small enough that we don't need to be overly concerned about really large
        /// value types. It's not unreasonable for a method to say it has 8 locals of a T, and that's effectively
        /// what this is.
        /// </remarks>
        private const int ScratchBufferSize = 8;
        /// <summary>Minimum size to request renting from the pool.</summary>
        private const int MinimumRentSize = 16;

        /// <summary>The array of segments.</summary>
        /// <remarks><see cref="_segmentsCount"/> is how many of the segments are valid in <see cref="_segments"/>, not including <see cref="_firstSegment"/>.</remarks>
        private Arrays _segments;
        /// <summary>The scratch buffer provided by the caller.</summary>
        /// <remarks>This is treated as the initial segment, before anything in <see cref="_segments"/>.</remarks>
        private Span<T> _firstSegment;
        /// <summary>The current span. This points either to <see cref="_firstSegment"/> or to <see cref="_segments"/>[<see cref="_segmentsCount"/> - 1].</summary>
        private Span<T> _currentSegment;
        /// <summary>The count of segments in <see cref="_segments"/> that are valid.</summary>
        /// <remarks>All but the last are known to be fully populated.</remarks>
        private int _segmentsCount;
        /// <summary>The total number of elements in all but the current/last segment.</summary>
        private int _countInFinishedSegments;
        /// <summary>The number of elements in the current/last segment.</summary>
        private int _countInCurrentSegment;

        /// <summary>Initialize the builder.</summary>
        /// <param name="scratchBuffer">A buffer that can be used as part of the builder.</param>
        public SegmentedArrayBuilder(Span<T> scratchBuffer)
        {
            _currentSegment = _firstSegment = scratchBuffer;
        }

        /// <summary>Clean up the resources used by the builder.</summary>
        public void Dispose()
        {
            int segmentsCount = _segmentsCount;
            if (segmentsCount != 0)
            {
                ReturnArrays(segmentsCount);
            }
        }

        private void ReturnArrays(int segmentsCount)
        {
            ReadOnlySpan<T[]> segments = _segments;

            // We need to return all rented arrays to the pool, and if the arrays contain any references,
            // we want to clear them first so that the pool doesn't artificially root contained objects.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                // Return all but the last segment. All of these are full and need to be entirely cleared.
                segmentsCount--;
                foreach (T[] segment in segments.Slice(0, segmentsCount))
                {
                    Array.Clear(segment);
                    ArrayPool<T>.Shared.Return(segment);
                }

                // For the last segment, we can clear only what we know was used.
                T[] currentSegment = segments[segmentsCount];
                Array.Clear(currentSegment, 0, _countInCurrentSegment);
                ArrayPool<T>.Shared.Return(currentSegment);
            }
            else
            {
                // Return every rented array without clearing.
                for (int i = 0; i < segments.Length; i++)
                {
                    T[] segment = segments[i];
                    if (segment is null)
                    {
                        break;
                    }

                    ArrayPool<T>.Shared.Return(segment);
                }
            }
        }

        /// <summary>Gets the number of elements in the builder.</summary>
        public readonly int Count => checked(_countInFinishedSegments + _countInCurrentSegment);

        /// <summary>Adds an item into the builder.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            Span<T> currentSegment = _currentSegment;
            int countInCurrentSegment = _countInCurrentSegment;
            if ((uint)countInCurrentSegment < (uint)currentSegment.Length)
            {
                currentSegment[countInCurrentSegment] = item;
                _countInCurrentSegment++;
            }
            else
            {
                AddSlow(item);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddSlow(T item)
        {
            Expand();
            _currentSegment[0] = item;
            _countInCurrentSegment = 1;
        }

        /// <summary>Adds a collection of items into the builder.</summary>
        public void AddRange(IEnumerable<T> source)
        {
            if (source is ICollection<T> collection)
            {
                int collectionCount = collection.Count;

                // If the source is empty, there's nothing to add.
                if (collectionCount == 0)
                {
                    return;
                }

                // If the source is something from which we can get a span, e.g. a T[] or a List<T>,
                // we can do one or two copies to handle copying everything, even if we need to split
                // across segments.
                if (Enumerable.TryGetSpan(source, out ReadOnlySpan<T> sourceSpan))
                {
                    int availableSpaceInCurrentSpan = _currentSegment.Length - _countInCurrentSegment;
                    ReadOnlySpan<T> sourceSlice = sourceSpan.Slice(0, Math.Min(availableSpaceInCurrentSpan, sourceSpan.Length));
                    sourceSlice.CopyTo(_currentSegment.Slice(_countInCurrentSegment));
                    _countInCurrentSegment += sourceSlice.Length;
                    sourceSlice = sourceSpan.Slice(sourceSlice.Length);

                    if (!sourceSlice.IsEmpty)
                    {
                        Expand(sourceSlice.Length);
                        sourceSlice.CopyTo(_currentSegment);
                        _countInCurrentSegment = sourceSlice.Length;
                    }

                    return;
                }

                // Otherwise, since we have an ICollection, we'd like to use ICollection.CopyTo, but it
                // requires targeting an array, so we can't use it if we're using a scratch buffer.
                bool currentSegmentIsScratchBufferWithRemainingSpace = _segmentsCount == 0 && _countInCurrentSegment < _currentSegment.Length;
                if (!currentSegmentIsScratchBufferWithRemainingSpace)
                {
                    // It also only works if we can copy the whole collection in one go, which
                    // means we need enough space in the current segment to hold the whole collection
                    // or we need to be at the end of the current segment so we can allocate a
                    // new one without leaving any holes.
                    int remainingSpaceInCurrentSegment = _currentSegment.Length - _countInCurrentSegment;

                    // If there's no space remaining in the current segment, we can just expand
                    // into a new segment that we ensure is large enough.
                    if (remainingSpaceInCurrentSegment == 0)
                    {
                        Expand(collectionCount);
                        collection.CopyTo(_segments[_segmentsCount - 1], 0);
                        _countInCurrentSegment = collectionCount;
                        return;
                    }

                    // If there's enough space remaining in the current segment, we can also just copy into it.
                    if (collectionCount <= remainingSpaceInCurrentSegment)
                    {
                        collection.CopyTo(_segments[_segmentsCount - 1], _countInCurrentSegment);
                        _countInCurrentSegment += collectionCount;
                        return;
                    }

                    // Otherwise, we're forced to fall back to enumeration.
                }
            }

            // Fall back to enumerating and adding each element individually.
            AddNonICollectionRangeInlined(source);
        }

        /// <summary>Adds a collection of items into the builder.</summary>
        /// <remarks>
        /// The implementation assumes the caller has already ruled out the source being
        /// and ICollection and thus doesn't bother checking to see if it is.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AddNonICollectionRange(IEnumerable<T> source) =>
            AddNonICollectionRangeInlined(source);

        /// <summary>Adds a collection of items into the builder.</summary>
        /// <remarks>
        /// The implementation assumes the caller has already ruled out the source being
        /// and ICollection and thus doesn't bother checking to see if it is.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddNonICollectionRangeInlined(IEnumerable<T> source)
        {
            Span<T> currentSegment = _currentSegment;
            int countInCurrentSegment = _countInCurrentSegment;

            foreach (T item in source)
            {
                if ((uint)countInCurrentSegment < (uint)currentSegment.Length)
                {
                    currentSegment[countInCurrentSegment] = item;
                    countInCurrentSegment++;
                }
                else
                {
                    Expand();
                    currentSegment = _currentSegment;
                    currentSegment[0] = item;
                    countInCurrentSegment = 1;
                }
            }

            _countInCurrentSegment = countInCurrentSegment;
        }

        /// <summary>Creates an array containing all of the elements in the builder.</summary>
        public readonly T[] ToArray()
        {
            T[] result;
            int count = Count;

            if (count != 0)
            {
                result = GC.AllocateUninitializedArray<T>(count);
                ToSpanInlined(result);
            }
            else
            {
                result = [];
            }

            return result;
        }

        /// <summary>Creates a list containing all of the elements in the builder.</summary>
        public readonly List<T> ToList()
        {
            List<T> result;
            int count = Count;

            if (count != 0)
            {
                result = new List<T>(count);

                CollectionsMarshal.SetCount(result, count);
                ToSpanInlined(CollectionsMarshal.AsSpan(result));
            }
            else
            {
                result = [];
            }

            return result;
        }

        /// <summary>Creates an array containing all of the elements in the builder.</summary>
        /// <param name="additionalLength">The number of extra elements of room to allocate in the resulting array.</param>
        public readonly T[] ToArray(int additionalLength)
        {
            T[] result;
            int count = checked(Count + additionalLength);

            if (count != 0)
            {
                result = GC.AllocateUninitializedArray<T>(count);
                ToSpanInlined(result);
            }
            else
            {
                result = [];
            }

            return result;
        }

        /// <summary>Populates the destination span with all of the elements in the builder.</summary>
        /// <param name="destination">The destination span.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public readonly void ToSpan(Span<T> destination) => ToSpanInlined(destination);

        /// <summary>Populates the destination span with all of the elements in the builder.</summary>
        /// <param name="destination">The destination span.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly void ToSpanInlined(Span<T> destination)
        {
            int segmentsCount = _segmentsCount;
            if (segmentsCount != 0)
            {
                // Copy the first segment
                ReadOnlySpan<T> firstSegment = _firstSegment;
                firstSegment.CopyTo(destination);
                destination = destination.Slice(firstSegment.Length);

                // Copy the 0..N-1 segments
                segmentsCount--;
                if (segmentsCount != 0)
                {
                    foreach (T[] arr in ((ReadOnlySpan<T[]>)_segments).Slice(0, segmentsCount))
                    {
                        ReadOnlySpan<T> segment = arr;
                        segment.CopyTo(destination);
                        destination = destination.Slice(segment.Length);
                    }
                }
            }

            // Copy the last segment
            _currentSegment.Slice(0, _countInCurrentSegment).CopyTo(destination);
        }

        /// <summary>Appends a new segment onto the builder.</summary>
        /// <param name="minimumRequired">The minimum amount of space to allocate in a new segment being appended.</param>
        private void Expand(int minimumRequired = MinimumRentSize)
        {
            if (minimumRequired < MinimumRentSize)
            {
                minimumRequired = MinimumRentSize;
            }

            // Update our count of the number of elements in the arrays.
            // If we know we're exceeding the maximum allowed array length, throw.
            int currentSegmentLength = _currentSegment.Length;
            checked { _countInFinishedSegments += currentSegmentLength; }
            if (_countInFinishedSegments > Array.MaxLength)
            {
                throw new OutOfMemoryException();
            }

            // Use a typical doubling algorithm to decide the length of the next array
            // and allocate it. We want to double the current array length, but if the
            // minimum required is larger than that, use the minimum required. And if
            // doubling would result in going above the max array length, only use the
            // max array length, as List<T> does.
            int newSegmentLength = (int)Math.Min(Math.Max(minimumRequired, currentSegmentLength * 2L), Array.MaxLength);
            _currentSegment = _segments[_segmentsCount] = ArrayPool<T>.Shared.Rent(newSegmentLength);
            _segmentsCount++;
        }

#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable IDE0051 // Remove unused private members
        /// <summary>A struct to hold all of the T[]s that compose the full result set.</summary>
        /// <remarks>
        /// Starting at the minimum size of <see cref="MinimumRentSize"/>, and with a minimum of doubling
        /// on every growth, this is large enough to hold the maximum number arrays that could result
        /// until the total length has exceeded Array.MaxLength.
        /// </remarks>
        [InlineArray(27)]
        private struct Arrays
        {
            private T[] _values;
        }

        /// <summary>Provides a stack-allocatable buffer for use as an argument to the builder.</summary>
        [InlineArray(ScratchBufferSize)]
        public struct ScratchBuffer
        {
            private T _item;
        }
#pragma warning restore IDE0051
#pragma warning restore IDE0044
    }

    internal ref struct ValueListBuilder<T>
    {
        private Span<T> _span;
        private T[]? _arrayFromPool;
        private int _pos;

        public ValueListBuilder(Span<T> scratchBuffer)
        {
            _span = scratchBuffer;
        }

        public ValueListBuilder(int capacity)
        {
            Grow(capacity);
        }

        public int Length
        {
            get => _pos;
            set => _pos = value;
        }

        public ref T this[int index] => ref _span[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(T item)
        {
            int pos = _pos;

            Span<T> span = _span;
            if ((uint)pos < (uint)span.Length)
            {
                span[pos] = item;
                _pos = pos + 1;
            }
            else
            {
                AddWithResize(item);
            }
        }

        // Hide uncommon path
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            int pos = _pos;
            Grow(1);
            _span[pos] = item;
            _pos = pos + 1;
        }

        public ReadOnlySpan<T> AsSpan() => _span.Slice(0, _pos);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            T[]? toReturn = _arrayFromPool;
            if (toReturn is not null)
            {
                _arrayFromPool = null;
                ArrayPool<T>.Shared.Return(toReturn, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }
        }

        // Note that consuming implementations depend on the list only growing if it's absolutely
        // required.  If the list is already large enough to hold the additional items be added,
        // it must not grow. The list is used in a number of places where the reference is checked
        // and it's expected to match the initial reference provided to the constructor if that
        // span was sufficiently large.
        private void Grow(int additionalCapacityRequired = 1)
        {
            const int ArrayMaxLength = 0x7FFFFFC7; // same as Array.MaxLength

            // Double the size of the span.  If it's currently empty, default to size 4,
            // although it'll be increased in Rent to the pool's minimum bucket size.
            int nextCapacity = Math.Max(_span.Length != 0 ? _span.Length * 2 : 4, _span.Length + additionalCapacityRequired);

            // If the computed doubled capacity exceeds the possible length of an array, then we
            // want to downgrade to either the maximum array length if that's large enough to hold
            // an additional item, or the current length + 1 if it's larger than the max length, in
            // which case it'll result in an OOM when calling Rent below.  In the exceedingly rare
            // case where _span.Length is already int.MaxValue (in which case it couldn't be a managed
            // array), just use that same value again and let it OOM in Rent as well.
            if ((uint)nextCapacity > ArrayMaxLength)
            {
                nextCapacity = Math.Max(Math.Max(_span.Length + 1, ArrayMaxLength), _span.Length);
            }

            T[] array = ArrayPool<T>.Shared.Rent(nextCapacity);
            _span.CopyTo(array);

            T[]? toReturn = _arrayFromPool;
            _span = _arrayFromPool = array;
            if (toReturn is not null)
            {
                ArrayPool<T>.Shared.Return(toReturn, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }
        }
    }
}
