using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Collections.ObjectModel
{
    [Serializable]
    public class ReadOnlyCollection<T> : IList<T>, IList, IReadOnlyList<T>
    {
        private readonly IList<T> list; // Do not rename (binary serialization)

        public ReadOnlyCollection(IList<T> list)
        {
            if (list == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.list);
            }
            this.list = list;
        }

        /// <summary>Gets an empty <see cref="ReadOnlyCollection{T}"/>.</summary>
        /// <value>An empty <see cref="ReadOnlyCollection{T}"/>.</value>
        /// <remarks>The returned instance is immutable and will always be empty.</remarks>
        public static ReadOnlyCollection<T> Empty { get; } = new ReadOnlyCollection<T>(Array.Empty<T>());

        public int Count => list.Count;

        public T this[int index] => list[index];

        public bool Contains(T value)
        {
            return list.Contains(value);
        }

        public void CopyTo(T[] array, int index)
        {
            list.CopyTo(array, index);
        }

        public IEnumerator<T> GetEnumerator() =>
            list.Count == 0 ? SZGenericArrayEnumerator<T>.Empty :
            list.GetEnumerator();

        public int IndexOf(T value)
        {
            return list.IndexOf(value);
        }

        protected IList<T> Items => list;

        bool ICollection<T>.IsReadOnly => true;

        T IList<T>.this[int index]
        {
            get => list[index];
            set => ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void ICollection<T>.Add(T value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void ICollection<T>.Clear()
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void IList<T>.Insert(int index, T value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        bool ICollection<T>.Remove(T value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
            return false;
        }

        void IList<T>.RemoveAt(int index)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)list).GetEnumerator();
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => list is ICollection coll ? coll.SyncRoot : this;

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

            if (index < 0)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            if (array is T[] items)
            {
                list.CopyTo(items, index);
            }
            else
            {
                //
                // Catch the obvious case assignment will fail.
                // We can't find all possible problems by doing the check though.
                // For example, if the element type of the Array is derived from T,
                // we can't figure out if we can successfully copy the element beforehand.
                //
                Type targetType = array.GetType().GetElementType()!;
                Type sourceType = typeof(T);
                if (!(targetType.IsAssignableFrom(sourceType) || sourceType.IsAssignableFrom(targetType)))
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }

                //
                // We can't cast array of value type to object[], so we don't support
                // widening of primitive types here.
                //
                object?[]? objects = array as object[];
                if (objects == null)
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }

                int count = list.Count;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        objects[index++] = list[i];
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }
            }
        }

        bool IList.IsFixedSize => true;

        bool IList.IsReadOnly => true;

        object? IList.this[int index]
        {
            get => list[index];
            set => ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        int IList.Add(object? value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
            return -1;
        }

        void IList.Clear()
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        private static bool IsCompatibleObject(object? value)
        {
            // Non-null values are fine.  Only accept nulls if T is a class or Nullable<U>.
            // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
            return (value is T) || (value == null && default(T) == null);
        }

        bool IList.Contains(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return Contains((T)value!);
            }
            return false;
        }

        int IList.IndexOf(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return IndexOf((T)value!);
            }
            return -1;
        }

        void IList.Insert(int index, object? value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void IList.Remove(object? value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void IList.RemoveAt(int index)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }
    }

    /// <summary>
    /// Provides static methods for read-only collections.
    /// </summary>
    public static class ReadOnlyCollection
    {
        /// <summary>
        /// Creates a new <see cref="ReadOnlyCollection{T}"/> from the specified span of values.
        /// This method (simplifies collection initialization)[/dotnet/csharp/language-reference/operators/collection-expressions]
        /// to create a new <see cref="ReadOnlyCollection{T}"/> with the specified values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="values">The span of values to include in the collection.</param>
        /// <returns>A new <see cref="ReadOnlyCollection{T}"/> containing the specified values.</returns>
        public static ReadOnlyCollection<T> CreateCollection<T>(params ReadOnlySpan<T> values) =>
            values.IsEmpty ? ReadOnlyCollection<T>.Empty : new ReadOnlyCollection<T>(values.ToArray());

        /// <summary>
        /// Creates a new <see cref="ReadOnlySet{T}"/> from the specified span of values.
        /// This method (simplifies collection initialization)[/dotnet/csharp/language-reference/operators/collection-expressions]
        /// to create a new <see cref="ReadOnlySet{T}"/> with the specified values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="values">The span of values to include in the collection.</param>
        /// <returns>A new <see cref="ReadOnlySet{T}"/> containing the specified values.</returns>
        public static ReadOnlySet<T> CreateSet<T>(params ReadOnlySpan<T> values)
        {
            if (values.IsEmpty)
            {
                return ReadOnlySet<T>.Empty;
            }

            HashSet<T> hashSet = [];
            foreach (T value in values)
            {
                hashSet.Add(value);
            }

            return new ReadOnlySet<T>(hashSet);
        }
    }

    /// <summary>Represents a read-only, generic set of values.</summary>
    /// <typeparam name="T">The type of values in the set.</typeparam>
    public class ReadOnlySet<T> : IReadOnlySet<T>, ISet<T>, ICollection
    {
        /// <summary>The wrapped set.</summary>
        private readonly ISet<T> _set;

        /// <summary>Initializes a new instance of the <see cref="ReadOnlySet{T}"/> class that is a wrapper around the specified set.</summary>
        /// <param name="set">The set to wrap.</param>
        public ReadOnlySet(ISet<T> set)
        {
            ArgumentNullException.ThrowIfNull(set);
            _set = set;
        }

        /// <summary>Gets an empty <see cref="ReadOnlySet{T}"/>.</summary>
        public static ReadOnlySet<T> Empty { get; } = new ReadOnlySet<T>(new HashSet<T>());

        /// <summary>Gets the set that is wrapped by this <see cref="ReadOnlySet{T}"/> object.</summary>
        protected ISet<T> Set => _set;

        /// <inheritdoc/>
        public int Count => _set.Count;

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() =>
            _set.Count == 0 ? ((IEnumerable<T>)[]).GetEnumerator() :
            _set.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public bool Contains(T item) => _set.Contains(item);

        /// <inheritdoc/>
        public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);

        /// <inheritdoc/>
        public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);

        /// <inheritdoc/>
        public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);

        /// <inheritdoc/>
        public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);

        /// <inheritdoc/>
        public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);

        /// <inheritdoc/>
        void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        void ICollection.CopyTo(Array array, int index) => CollectionHelpers.CopyTo(_set, array, index);

        /// <inheritdoc/>
        bool ICollection<T>.IsReadOnly => true;

        /// <inheritdoc/>
        bool ICollection.IsSynchronized => false;

        /// <inheritdoc/>
        object ICollection.SyncRoot => _set is ICollection c ? c.SyncRoot : this;

        /// <inheritdoc/>
        bool ISet<T>.Add(T item) => throw new NotSupportedException();

        /// <inheritdoc/>
        void ISet<T>.ExceptWith(IEnumerable<T> other) => throw new NotSupportedException();

        /// <inheritdoc/>
        void ISet<T>.IntersectWith(IEnumerable<T> other) => throw new NotSupportedException();

        /// <inheritdoc/>
        void ISet<T>.SymmetricExceptWith(IEnumerable<T> other) => throw new NotSupportedException();

        /// <inheritdoc/>
        void ISet<T>.UnionWith(IEnumerable<T> other) => throw new NotSupportedException();

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => throw new NotSupportedException();

        /// <inheritdoc/>
        void ICollection<T>.Clear() => throw new NotSupportedException();

        /// <inheritdoc/>
        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
    }

    /// <summary>Provides internal helpers for collection types.</summary>
    internal static class CollectionHelpers
    {
        internal static void ValidateCopyToArguments(int sourceCount, Array array, int index)
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

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, array.Length);

            if (array.Length - index < sourceCount)
            {
                throw new ArgumentException(SR.Arg_ArrayPlusOffTooSmall);
            }
        }

        internal static void CopyTo<T>(ICollection<T> collection, Array array, int index)
        {
            ValidateCopyToArguments(collection.Count, array, index);

            if (collection is ICollection nonGenericCollection)
            {
                // Easy out if the ICollection<T> implements the non-generic ICollection
                nonGenericCollection.CopyTo(array, index);
            }
            else if (array is T[] items)
            {
                collection.CopyTo(items, index);
            }
            else
            {
                // We can't cast array of value type to object[], so we don't support widening of primitive types here.
                if (array is not object?[] objects)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }

                try
                {
                    foreach (T item in collection)
                    {
                        objects[index++] = item;
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }
            }
        }
    }
}
