using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Collections.Immutable
{
    /// <summary>
    /// An internal non-generic interface implemented by <see cref="ImmutableArray{T}"/>
    /// that allows for recognition of an <see cref="ImmutableArray{T}"/> instance and access
    /// to its underlying array, without actually knowing the type of value
    /// stored in it.
    /// </summary>
    /// <remarks>
    /// Casting to this interface requires a boxed instance of the <see cref="ImmutableArray{T}"/> struct,
    /// and as such should be avoided. This interface is useful, however, where the value
    /// is already boxed and we want to try to reuse immutable arrays instead of copying them.
    /// ** This interface is INTENTIONALLY INTERNAL, as it gives access to the inner array.  **
    /// </remarks>
    internal interface IImmutableArray
    {
        /// <summary>
        /// Gets an untyped reference to the array.
        /// </summary>
        Array? Array { get; }
    }

    /// <summary>
    /// A set of initialization methods for instances of <see cref="ImmutableArray{T}"/>.
    /// </summary>
    public static class ImmutableArray
    {
        // <summary>
        /// A two element array useful for throwing exceptions the way LINQ does.
        /// </summary>
        internal static readonly byte[] TwoElementArray = new byte[2];

        /// <summary>
        /// Creates an empty <see cref="ImmutableArray{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <returns>An empty array.</returns>
        public static ImmutableArray<T> Create<T>()
        {
            return ImmutableArray<T>.Empty;
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified element as its only member.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="item">The element to store in the array.</param>
        /// <returns>A 1-element immutable array containing the specified item.</returns>
        public static ImmutableArray<T> Create<T>(T item)
        {
            T[] array = new[] { item };
            return new ImmutableArray<T>(array);
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="item1">The first element to store in the array.</param>
        /// <param name="item2">The second element to store in the array.</param>
        /// <returns>A 2-element immutable array containing the specified items.</returns>
        public static ImmutableArray<T> Create<T>(T item1, T item2)
        {
            T[] array = new[] { item1, item2 };
            return new ImmutableArray<T>(array);
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="item1">The first element to store in the array.</param>
        /// <param name="item2">The second element to store in the array.</param>
        /// <param name="item3">The third element to store in the array.</param>
        /// <returns>A 3-element immutable array containing the specified items.</returns>
        public static ImmutableArray<T> Create<T>(T item1, T item2, T item3)
        {
            T[] array = new[] { item1, item2, item3 };
            return new ImmutableArray<T>(array);
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="item1">The first element to store in the array.</param>
        /// <param name="item2">The second element to store in the array.</param>
        /// <param name="item3">The third element to store in the array.</param>
        /// <param name="item4">The fourth element to store in the array.</param>
        /// <returns>A 4-element immutable array containing the specified items.</returns>
        public static ImmutableArray<T> Create<T>(T item1, T item2, T item3, T item4)
        {
            T[] array = new[] { item1, item2, item3, item4 };
            return new ImmutableArray<T>(array);
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="items">The elements to store in the array.</param>
        /// <returns>An immutable array containing the specified items.</returns>
        public static ImmutableArray<T> Create<T>(params ReadOnlySpan<T> items)
        {
            if (items.IsEmpty)
            {
                return ImmutableArray<T>.Empty;
            }

            T[] array = items.ToArray();
            return new ImmutableArray<T>(array);
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="items">The elements to store in the array.</param>
        /// <returns>An immutable array containing the specified items.</returns>
        [OverloadResolutionPriority(-1)]
        public static ImmutableArray<T> Create<T>(Span<T> items)
        {
            return Create((ReadOnlySpan<T>)items);
        }

        /// <summary>
        /// Produce an immutable array of contents from specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element in the list.</typeparam>
        /// <param name="items">The elements to store in the array.</param>
        /// <returns>An immutable array containing the specified items.</returns>
        public static ImmutableArray<T> ToImmutableArray<T>(this ReadOnlySpan<T> items)
        {
            return Create(items);
        }

        /// <summary>
        /// Produce an immutable array of contents from specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element in the list.</typeparam>
        /// <param name="items">The elements to store in the array.</param>
        /// <returns>An immutable array containing the specified items.</returns>
        [OverloadResolutionPriority(-1)]
        public static ImmutableArray<T> ToImmutableArray<T>(this Span<T> items)
        {
            return Create((ReadOnlySpan<T>)items);
        }

        /// <summary>
        /// Creates an <see cref="ImmutableArray{T}"/> with the specified elements.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the array.</typeparam>
        /// <param name="items">The elements to store in the array.</param>
        /// <returns>An immutable array containing the specified items.</returns>
        public static ImmutableArray<T> Create<T>(params T[]? items)
        {
            if (items == null || items.Length == 0)
            {
                return ImmutableArray<T>.Empty;
            }

            // We can't trust that the array passed in will never be mutated by the caller.
            // The caller may have passed in an array explicitly (not relying on compiler params keyword)
            // and could then change the array after the call, thereby violating the immutable
            // guarantee provided by this struct. So we always copy the array to ensure it won't ever change.
            var tmp = new T[items.Length];
            Array.Copy(items, tmp, items.Length);
            return new ImmutableArray<T>(tmp);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImmutableArray{T}"/> struct.
        /// </summary>
        /// <param name="items">The array to initialize the array with. A defensive copy is made.</param>
        /// <param name="start">The index of the first element in the source array to include in the resulting array.</param>
        /// <param name="length">The number of elements from the source array to include in the resulting array.</param>
        /// <remarks>
        /// This overload allows helper methods or custom builder classes to efficiently avoid paying a redundant
        /// tax for copying an array when the new array is a segment of an existing array.
        /// </remarks>
        public static ImmutableArray<T> Create<T>(T[] items, int start, int length)
        {
            Requires.NotNull(items, nameof(items));
            Requires.Range(start >= 0 && start <= items.Length, nameof(start));
            Requires.Range(length >= 0 && length <= items.Length - start, nameof(length));

            if (length == 0)
            {
                // Avoid allocating an array.
                return ImmutableArray<T>.Empty;
            }

            var array = new T[length];
            Array.Copy(items, start, array, 0, length);
            return new ImmutableArray<T>(array);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImmutableArray{T}"/> struct.
        /// </summary>
        /// <param name="items">The array to initialize the array with.
        /// The selected array segment may be copied into a new array.</param>
        /// <param name="start">The index of the first element in the source array to include in the resulting array.</param>
        /// <param name="length">The number of elements from the source array to include in the resulting array.</param>
        /// <remarks>
        /// This overload allows helper methods or custom builder classes to efficiently avoid paying a redundant
        /// tax for copying an array when the new array is a segment of an existing array.
        /// </remarks>
        public static ImmutableArray<T> Create<T>(ImmutableArray<T> items, int start, int length)
        {
            Requires.Range(start >= 0 && start <= items.Length, nameof(start));
            Requires.Range(length >= 0 && length <= items.Length - start, nameof(length));

            if (length == 0)
            {
                return ImmutableArray<T>.Empty;
            }

            if (start == 0 && length == items.Length)
            {
                return items;
            }

            var array = new T[length];
            Array.Copy(items.array!, start, array, 0, length);
            return new ImmutableArray<T>(array);
        }

    }

    public readonly struct ImmutableArray<T> : IEnumerable<T>, IEquatable<ImmutableArray<T>>, IImmutableArray
    {
        // Avoid the extra generic instantiation for Array.Empty<T>()
        public static readonly ImmutableArray<T> Empty = new ImmutableArray<T>(new T[0]);

        /// <summary>
        /// The backing field for this instance. References to this value should never be shared with outside code.
        /// </summary>
        /// <remarks>
        /// This would be private, but we make it internal so that our own extension methods can access it.
        /// </remarks>
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        internal readonly T[]? array;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImmutableArray{T}"/> struct
        /// *without making a defensive copy*.
        /// </summary>
        /// <param name="items">The array to use. May be null for "default" arrays.</param>
        internal ImmutableArray(T[]? items)
        {
            this.array = items;
        }

        #region Operators

        /// <summary>
        /// Checks equality between two instances.
        /// </summary>
        /// <param name="left">The instance to the left of the operator.</param>
        /// <param name="right">The instance to the right of the operator.</param>
        /// <returns><c>true</c> if the values' underlying arrays are reference equal; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool operator ==(ImmutableArray<T> left, ImmutableArray<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks inequality between two instances.
        /// </summary>
        /// <param name="left">The instance to the left of the operator.</param>
        /// <param name="right">The instance to the right of the operator.</param>
        /// <returns><c>true</c> if the values' underlying arrays are reference not equal; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool operator !=(ImmutableArray<T> left, ImmutableArray<T> right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Checks equality between two instances.
        /// </summary>
        /// <param name="left">The instance to the left of the operator.</param>
        /// <param name="right">The instance to the right of the operator.</param>
        /// <returns><c>true</c> if the values' underlying arrays are reference equal; <c>false</c> otherwise.</returns>
        public static bool operator ==(ImmutableArray<T>? left, ImmutableArray<T>? right)
        {
            return left.GetValueOrDefault().Equals(right.GetValueOrDefault());
        }

        /// <summary>
        /// Checks inequality between two instances.
        /// </summary>
        /// <param name="left">The instance to the left of the operator.</param>
        /// <param name="right">The instance to the right of the operator.</param>
        /// <returns><c>true</c> if the values' underlying arrays are reference not equal; <c>false</c> otherwise.</returns>
        public static bool operator !=(ImmutableArray<T>? left, ImmutableArray<T>? right)
        {
            return !left.GetValueOrDefault().Equals(right.GetValueOrDefault());
        }

        #endregion

        /// <summary>
        /// Gets the element at the specified index in the read-only list.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <returns>The element at the specified index in the read-only list.</returns>
        public T this[int index]
        {
            [NonVersionable]
            get
            {
                // We intentionally do not check this.array != null, and throw NullReferenceException
                // if this is called while uninitialized.
                // The reason for this is perf.
                // Length and the indexer must be absolutely trivially implemented for the JIT optimization
                // of removing array bounds checking to work.
                return this.array![index];
            }
        }

        /// <summary>
        /// Gets a read-only reference to the element at the specified index in the read-only list.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get a reference to.</param>
        /// <returns>A read-only reference to the element at the specified index in the read-only list.</returns>
        public ref readonly T ItemRef(int index)
        {
            // We intentionally do not check this.array != null, and throw NullReferenceException
            // if this is called while uninitialized.
            // The reason for this is perf.
            // Length and the indexer must be absolutely trivially implemented for the JIT optimization
            // of removing array bounds checking to work.
            return ref this.array![index];
        }

        /// <summary>
        /// Gets a value indicating whether this collection is empty.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public bool IsEmpty
        {
            [NonVersionable]
            get { return this.array!.Length == 0; }
        }

        /// <summary>
        /// Gets the number of elements in the array.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public int Length
        {
            [NonVersionable]
            get
            {
                // We intentionally do not check this.array != null, and throw NullReferenceException
                // if this is called while uninitialized.
                // The reason for this is perf.
                // Length and the indexer must be absolutely trivially implemented for the JIT optimization
                // of removing array bounds checking to work.
                return this.array!.Length;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this struct was initialized without an actual array instance.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public bool IsDefault
        {
            get { return this.array == null; }
        }

        /// <summary>
        /// Gets a value indicating whether this struct is empty or uninitialized.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public bool IsDefaultOrEmpty
        {
            get
            {
                ImmutableArray<T> self = this;
                return self.array == null || self.array.Length == 0;
            }
        }

        /// <summary>
        /// Gets an untyped reference to the array.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        Array? IImmutableArray.Array
        {
            get { return this.array; }
        }

        /// <summary>
        /// Gets the string to display in the debugger watches window for this instance.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebuggerDisplay
        {
            get
            {
                ImmutableArray<T> self = this;
                return self.IsDefault ? "Uninitialized" : $"Length = {self.Length}";
            }
        }

        /// <summary>
        /// Copies the contents of this array to the specified array.
        /// </summary>
        /// <param name="destination">The array to copy to.</param>
        public void CopyTo(T[] destination)
        {
            ImmutableArray<T> self = this;
            self.ThrowNullRefIfNotInitialized();
            Array.Copy(self.array, destination, self.Length);
        }

        /// <summary>
        /// Copies the contents of this array to the specified array.
        /// </summary>
        /// <param name="destination">The array to copy to.</param>
        /// <param name="destinationIndex">The index into the destination array to which the first copied element is written.</param>
        public void CopyTo(T[] destination, int destinationIndex)
        {
            ImmutableArray<T> self = this;
            self.ThrowNullRefIfNotInitialized();
            Array.Copy(self.array, 0, destination, destinationIndex, self.Length);
        }

        /// <summary>
        /// Copies the contents of this array to the specified array.
        /// </summary>
        /// <param name="sourceIndex">The index into this collection of the first element to copy.</param>
        /// <param name="destination">The array to copy to.</param>
        /// <param name="destinationIndex">The index into the destination array to which the first copied element is written.</param>
        /// <param name="length">The number of elements to copy.</param>
        public void CopyTo(int sourceIndex, T[] destination, int destinationIndex, int length)
        {
            ImmutableArray<T> self = this;
            self.ThrowNullRefIfNotInitialized();
            Array.Copy(self.array, sourceIndex, destination, destinationIndex, length);
        }

        /// <summary>
        /// Returns a builder that is populated with the same contents as this array.
        /// </summary>
        /// <returns>The new builder.</returns>
        public ImmutableArray<T>.Builder ToBuilder()
        {
            ImmutableArray<T> self = this;
            if (self.Length == 0)
            {
                return new Builder(); // allow the builder to create itself with a reasonable default capacity
            }

            var builder = new Builder(self.Length);
            builder.AddRange(self);
            return builder;
        }

        /// <summary>
        /// Returns an enumerator for the contents of the array.
        /// </summary>
        /// <returns>An enumerator.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            ImmutableArray<T> self = this;
            self.ThrowNullRefIfNotInitialized();
            return new Enumerator(self.array);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            ImmutableArray<T> self = this;
            return self.array == null ? 0 : self.array.GetHashCode();
        }

        /// <summary>
        /// Determines whether the specified <see cref="object"/> is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="object"/> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="object"/> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is IImmutableArray other && this.array == other.Array;
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.
        /// </returns>
        [NonVersionable]
        public bool Equals(ImmutableArray<T> other)
        {
            return this.array == other.array;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImmutableArray{T}"/> struct by casting the underlying
        /// array to an array of type <typeparam name="TOther"/>.
        /// </summary>
        /// <exception cref="InvalidCastException">Thrown if the cast is illegal.</exception>
        public ImmutableArray<
#nullable disable
            TOther
#nullable restore
            > CastArray<TOther>() where TOther : class?
        {
            return new ImmutableArray<TOther>((TOther[])(object)array!);
        }

        /// <summary>
        /// Returns an enumerator for the contents of the array.
        /// </summary>
        /// <returns>An enumerator.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the <see cref="IsDefault"/> property returns true.</exception>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            ImmutableArray<T> self = this;
            self.ThrowInvalidOperationIfNotInitialized();
            return EnumeratorObject.Create(self.array);
        }

        /// <summary>
        /// Returns an enumerator for the contents of the array.
        /// </summary>
        /// <returns>An enumerator.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the <see cref="IsDefault"/> property returns true.</exception>
        IEnumerator IEnumerable.GetEnumerator()
        {
            ImmutableArray<T> self = this;
            self.ThrowInvalidOperationIfNotInitialized();
            return EnumeratorObject.Create(self.array);
        }

        /// <summary>
        /// Throws a null reference exception if the array field is null.
        /// </summary>
        [MemberNotNull(nameof(array))]
        internal void ThrowNullRefIfNotInitialized()
        {
            // Force NullReferenceException if array is null by touching its Length.
            // This way of checking has a nice property of requiring very little code
            // and not having any conditions/branches.
            // In a faulting scenario we are relying on hardware to generate the fault.
            // And in the non-faulting scenario (most common) the check is virtually free since
            // if we are going to do anything with the array, we will need Length anyways
            // so touching it, and potentially causing a cache miss, is not going to be an
            // extra expense.
            _ = this.array!.Length;
        }

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> if the <see cref="array"/> field is null, i.e. the
        /// <see cref="IsDefault"/> property returns true.  The
        /// <see cref="InvalidOperationException"/> message specifies that the operation cannot be performed
        /// on a default instance of <see cref="ImmutableArray{T}"/>.
        ///
        /// This is intended for explicitly implemented interface method and property implementations.
        /// </summary>
        [MemberNotNull(nameof(array))]
        private void ThrowInvalidOperationIfNotInitialized()
        {
            if (this.array == null)
            {
                throw new InvalidOperationException(SR.InvalidOperationOnDefaultArray);
            }
        }

        /// <summary>
        /// An array enumerator.
        /// </summary>
        /// <remarks>
        /// It is important that this enumerator does NOT implement <see cref="IDisposable"/>.
        /// We want the iterator to inline when we do foreach and to not result in
        /// a try/finally frame in the client.
        /// </remarks>
        public struct Enumerator
        {
            /// <summary>
            /// The array being enumerated.
            /// </summary>
            private readonly T[] _array;

            /// <summary>
            /// The currently enumerated position.
            /// </summary>
            /// <value>
            /// -1 before the first call to <see cref="MoveNext"/>.
            /// >= this.array.Length after <see cref="MoveNext"/> returns false.
            /// </value>
            private int _index;

            /// <summary>
            /// Initializes a new instance of the <see cref="Enumerator"/> struct.
            /// </summary>
            /// <param name="array">The array to enumerate.</param>
            internal Enumerator(T[] array)
            {
                _array = array;
                _index = -1;
            }

            /// <summary>
            /// Gets the currently enumerated value.
            /// </summary>
            public T Current
            {
                get
                {
                    // PERF: no need to do a range check, we already did in MoveNext.
                    // if user did not call MoveNext or ignored its result (incorrect use)
                    // they will still get an exception from the array access range check.
                    return _array[_index];
                }
            }

            /// <summary>
            /// Advances to the next value to be enumerated.
            /// </summary>
            /// <returns><c>true</c> if another item exists in the array; <c>false</c> otherwise.</returns>
            public bool MoveNext()
            {
                return ++_index < _array.Length;
            }
        }

        /// <summary>
        /// An array enumerator that implements <see cref="IEnumerator{T}"/> pattern (including <see cref="IDisposable"/>).
        /// </summary>
        private sealed class EnumeratorObject : IEnumerator<T>
        {
            /// <summary>
            /// A shareable singleton for enumerating empty arrays.
            /// </summary>
            private static readonly IEnumerator<T> s_EmptyEnumerator =
                new EnumeratorObject(ImmutableArray<T>.Empty.array!);

            /// <summary>
            /// The array being enumerated.
            /// </summary>
            private readonly T[] _array;

            /// <summary>
            /// The currently enumerated position.
            /// </summary>
            /// <value>
            /// -1 before the first call to <see cref="MoveNext"/>.
            /// this.array.Length - 1 after MoveNext returns false.
            /// </value>
            private int _index;

            /// <summary>
            /// Initializes a new instance of the <see cref="Enumerator"/> class.
            /// </summary>
            private EnumeratorObject(T[] array)
            {
                _index = -1;
                _array = array;
            }

            /// <summary>
            /// Gets the currently enumerated value.
            /// </summary>
            public T Current
            {
                get
                {
                    // unsigned compare performs the range check in one compare
                    if (unchecked((uint)_index) >= (uint)_array.Length)
                    {
                        // Before first or after last MoveNext.
                        ThrowHelper.ThrowInvalidOperationException();
                    }

                    return _array[_index];
                }
            }

            /// <summary>
            /// Gets the currently enumerated value.
            /// </summary>
            object? IEnumerator.Current
            {
                get { return this.Current; }
            }

            /// <summary>
            /// If another item exists in the array, advances to the next value to be enumerated.
            /// </summary>
            /// <returns><c>true</c> if another item exists in the array; <c>false</c> otherwise.</returns>
            public bool MoveNext()
            {
                int newIndex = _index + 1;
                int length = _array.Length;

                // unsigned math is used to prevent false positive if index + 1 overflows.
                if ((uint)newIndex <= (uint)length)
                {
                    _index = newIndex;
                    return (uint)newIndex < (uint)length;
                }

                return false;
            }

            /// <summary>
            /// Resets enumeration to the start of the array.
            /// </summary>
            void IEnumerator.Reset()
            {
                _index = -1;
            }

            /// <summary>
            /// Disposes this enumerator.
            /// </summary>
            /// <remarks>
            /// Currently has no action.
            /// </remarks>
            public void Dispose()
            {
                // we do not have any native or disposable resources.
                // nothing to do here.
            }

            /// <summary>
            /// Creates an enumerator for the specified array.
            /// </summary>
            internal static IEnumerator<T> Create(T[] array)
            {
                if (array.Length != 0)
                {
                    return new EnumeratorObject(array);
                }
                else
                {
                    return s_EmptyEnumerator;
                }
            }
        }

        /// <summary>
        /// A writable array accessor that can be converted into an <see cref="ImmutableArray{T}"/>
        /// instance without allocating memory.
        /// </summary>
        [DebuggerDisplay("Count = {Count}")]
        public sealed class Builder : IList<T>, IReadOnlyList<T>
        {
            /// <summary>
            /// The backing array for the builder.
            /// </summary>
            private T[] _elements;

            /// <summary>
            /// The number of initialized elements in the array.
            /// </summary>
            private int _count;

            /// <summary>
            /// Initializes a new instance of the <see cref="Builder"/> class.
            /// </summary>
            /// <param name="capacity">The initial capacity of the internal array.</param>
            internal Builder(int capacity)
            {
                Requires.Range(capacity >= 0, nameof(capacity));
                _elements = new T[capacity];
                _count = 0;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Builder"/> class.
            /// </summary>
            internal Builder()
                : this(8)
            {
            }

            /// <summary>
            /// Get and sets the length of the internal array.  When set the internal array is
            /// reallocated to the given capacity if it is not already the specified length.
            /// </summary>
            public int Capacity
            {
                get { return _elements.Length; }
                set
                {
                    if (value < _count)
                    {
                        throw new ArgumentException(SR.CapacityMustBeGreaterThanOrEqualToCount, paramName: nameof(value));
                    }

                    if (value != _elements.Length)
                    {
                        if (value > 0)
                        {
                            var temp = new T[value];
                            if (_count > 0)
                            {
                                Array.Copy(_elements, temp, _count);
                            }

                            _elements = temp;
                        }
                        else
                        {
                            _elements = ImmutableArray<T>.Empty.array!;
                        }
                    }
                }
            }

            /// <summary>
            /// Gets or sets the length of the builder.
            /// </summary>
            /// <remarks>
            /// If the value is decreased, the array contents are truncated.
            /// If the value is increased, the added elements are initialized to the default value of type <typeparamref name="T"/>.
            /// </remarks>
            public int Count
            {
                get
                {
                    return _count;
                }

                set
                {
                    Requires.Range(value >= 0, nameof(value));
                    if (value < _count)
                    {
                        // truncation mode
                        // Clear the elements of the elements that are effectively removed.

                        // PERF: Array.Clear works well for big arrays,
                        //       but may have too much overhead with small ones (which is the common case here)
                        if (_count - value > 64)
                        {
                            Array.Clear(_elements, value, _count - value);
                        }
                        else
                        {
                            for (int i = value; i < this.Count; i++)
                            {
                                _elements[i] = default(T)!;
                            }
                        }
                    }
                    else if (value > _count)
                    {
                        // expansion
                        this.EnsureCapacity(value);
                    }

                    _count = value;
                }
            }

            private static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();

            /// <summary>
            /// Gets or sets the element at the specified index.
            /// </summary>
            /// <param name="index">The index.</param>
            /// <returns></returns>
            /// <exception cref="IndexOutOfRangeException">
            /// </exception>
            public T this[int index]
            {
                get
                {
                    if (index >= this.Count)
                    {
                        ThrowIndexOutOfRangeException();
                    }

                    return _elements[index];
                }

                set
                {
                    if (index >= this.Count)
                    {
                        ThrowIndexOutOfRangeException();
                    }

                    _elements[index] = value;
                }
            }

            /// <summary>
            /// Gets a read-only reference to the element at the specified index.
            /// </summary>
            /// <param name="index">The index.</param>
            /// <returns></returns>
            /// <exception cref="IndexOutOfRangeException">
            /// </exception>
            public ref readonly T ItemRef(int index)
            {
                if (index >= this.Count)
                {
                    ThrowIndexOutOfRangeException();
                }

                return ref this._elements[index];
            }

            /// <summary>
            /// Gets a value indicating whether the <see cref="ICollection{T}"/> is read-only.
            /// </summary>
            /// <returns>true if the <see cref="ICollection{T}"/> is read-only; otherwise, false.
            ///   </returns>
            bool ICollection<T>.IsReadOnly
            {
                get { return false; }
            }

            /// <summary>
            /// Returns an immutable copy of the current contents of this collection.
            /// </summary>
            /// <returns>An immutable array.</returns>
            public ImmutableArray<T> ToImmutable()
            {
                return new ImmutableArray<T>(this.ToArray());
            }

            /// <summary>
            /// Extracts the internal array as an <see cref="ImmutableArray{T}"/> and replaces it
            /// with a zero length array.
            /// </summary>
            /// <exception cref="InvalidOperationException">When <see cref="ImmutableArray{T}.Builder.Count"/> doesn't
            /// equal <see cref="ImmutableArray{T}.Builder.Capacity"/>.</exception>
            public ImmutableArray<T> MoveToImmutable()
            {
                if (Capacity != Count)
                {
                    throw new InvalidOperationException(SR.CapacityMustEqualCountOnMove);
                }

                T[] temp = _elements;
                _elements = ImmutableArray<T>.Empty.array!;
                _count = 0;
                return new ImmutableArray<T>(temp);
            }

            /// <summary>
            /// Returns the current contents as an <see cref="ImmutableArray{T}"/> and sets the collection to a zero length array.
            /// </summary>
            /// <remarks>
            /// If <see cref="Capacity"/> equals <see cref="Count"/>, the internal array will be extracted
            /// as an <see cref="ImmutableArray{T}"/> without copying the contents. Otherwise, the contents
            /// will be copied into a new array. The collection will then be set to a zero length array.
            /// </remarks>
            /// <returns>An immutable array.</returns>
            public ImmutableArray<T> DrainToImmutable()
            {
                T[] result = _elements;

                if (result.Length != _count)
                {
                    result = ToArray();
                }

                _elements = ImmutableArray<T>.Empty.array!;
                _count = 0;

                return new ImmutableArray<T>(result);
            }

            /// <summary>
            /// Removes all items from the <see cref="ICollection{T}"/>.
            /// </summary>
            public void Clear()
            {
                this.Count = 0;
            }

            /// <summary>
            /// Inserts an item to the <see cref="IList{T}"/> at the specified index.
            /// </summary>
            /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
            /// <param name="item">The object to insert into the <see cref="IList{T}"/>.</param>
            public void Insert(int index, T item)
            {
                Requires.Range(index >= 0 && index <= this.Count, nameof(index));
                this.EnsureCapacity(this.Count + 1);

                if (index < this.Count)
                {
                    Array.Copy(_elements, index, _elements, index + 1, this.Count - index);
                }

                _count++;
                _elements[index] = item;
            }

            /// <summary>
            /// Inserts the specified values at the specified index.
            /// </summary>
            /// <param name="index">The index at which to insert the value.</param>
            /// <param name="items">The elements to insert.</param>
            public void InsertRange(int index, ImmutableArray<T> items)
            {
                Requires.Range(index >= 0 && index <= this.Count, nameof(index));

                if (items.IsEmpty)
                {
                    return;
                }

                this.EnsureCapacity(this.Count + items.Length);

                if (index != this.Count)
                {
                    Array.Copy(_elements, index, _elements, index + items.Length, _count - index);
                }

                Array.Copy(items.array!, 0, _elements, index, items.Length);

                _count += items.Length;
            }

            /// <summary>
            /// Adds an item to the <see cref="ICollection{T}"/>.
            /// </summary>
            /// <param name="item">The object to add to the <see cref="ICollection{T}"/>.</param>
            public void Add(T item)
            {
                int newCount = _count + 1;
                this.EnsureCapacity(newCount);
                _elements[_count] = item;
                _count = newCount;
            }


            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <param name="items">The items.</param>
            public void AddRange(params T[] items)
            {
                Requires.NotNull(items, nameof(items));

                int offset = this.Count;
                this.Count += items.Length;

                Array.Copy(items, 0, _elements, offset, items.Length);
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <typeparam name="TDerived">The type that derives from the type of item already in the array.</typeparam>
            /// <param name="items">The items.</param>
            public void AddRange<TDerived>(TDerived[] items) where TDerived : T
            {
                Requires.NotNull(items, nameof(items));

                int offset = this.Count;
                this.Count += items.Length;

                Array.Copy(items, 0, _elements, offset, items.Length);
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <param name="items">The items.</param>
            /// <param name="length">The number of elements from the source array to add.</param>
            public void AddRange(T[] items, int length)
            {
                Requires.NotNull(items, nameof(items));
                Requires.Range(length >= 0 && length <= items.Length, nameof(length));

                int offset = this.Count;
                this.Count += length;

                Array.Copy(items, 0, _elements, offset, length);
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <param name="items">The items.</param>
            public void AddRange(ImmutableArray<T> items)
            {
                this.AddRange(items, items.Length);
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <param name="items">The items.</param>
            /// <param name="length">The number of elements from the source array to add.</param>
            public void AddRange(ImmutableArray<T> items, int length)
            {
                Requires.Range(length >= 0, nameof(length));

                if (items.array != null)
                {
                    this.AddRange(items.array, length);
                }
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <param name="items">The items to add at the end of the array.</param>
            public void AddRange(params ReadOnlySpan<T> items)
            {
                int offset = this.Count;
                this.Count += items.Length;

                items.CopyTo(new Span<T>(_elements, offset, items.Length));
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <typeparam name="TDerived">The type that derives from the type of item already in the array.</typeparam>
            /// <param name="items">The items to add at the end of the array.</param>
            public void AddRange<TDerived>(params ReadOnlySpan<TDerived> items) where TDerived : T
            {
                int offset = this.Count;
                this.Count += items.Length;

                var elements = new Span<T>(_elements, offset, items.Length);
                for (int i = 0; i < items.Length; i++)
                {
                    elements[i] = items[i];
                }
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <typeparam name="TDerived">The type that derives from the type of item already in the array.</typeparam>
            /// <param name="items">The items to add at the end of the array.</param>
            public void AddRange<TDerived>(ImmutableArray<TDerived> items) where TDerived : T
            {
                if (items.array != null)
                {
                    this.AddRange(items.array);
                }
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <param name="items">The items to add at the end of the array.</param>
            public void AddRange(Builder items)
            {
                Requires.NotNull(items, nameof(items));
                this.AddRange(items._elements, items.Count);
            }

            /// <summary>
            /// Adds the specified items to the end of the array.
            /// </summary>
            /// <typeparam name="TDerived">The type that derives from the type of item already in the array.</typeparam>
            /// <param name="items">The items to add at the end of the array.</param>
            public void AddRange<TDerived>(ImmutableArray<TDerived>.Builder items) where TDerived : T
            {
                Requires.NotNull(items, nameof(items));
                this.AddRange(items._elements, items.Count);
            }

            /// <summary>
            /// Removes the first occurrence of the specified element from the builder.
            /// If no match is found, the builder remains unchanged.
            /// </summary>
            /// <param name="element">The element.</param>
            /// <returns>A value indicating whether the specified element was found and removed from the collection.</returns>
            public bool Remove(T element)
            {
                int index = this.IndexOf(element);
                if (index >= 0)
                {
                    this.RemoveAt(index);
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Removes the first occurrence of the specified element from the builder.
            /// If no match is found, the builder remains unchanged.
            /// </summary>
            /// <param name="element">The element to remove.</param>
            /// <param name="equalityComparer">
            /// The equality comparer to use in the search.
            /// If <c>null</c>, <see cref="EqualityComparer{T}.Default"/> is used.
            /// </param>
            /// <returns>A value indicating whether the specified element was found and removed from the collection.</returns>
            public bool Remove(T element, IEqualityComparer<T>? equalityComparer)
            {
                int index = this.IndexOf(element, 0, _count, equalityComparer);

                if (index >= 0)
                {
                    this.RemoveAt(index);
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Removes the <see cref="IList{T}"/> item at the specified index.
            /// </summary>
            /// <param name="index">The zero-based index of the item to remove.</param>
            public void RemoveAt(int index)
            {
                Requires.Range(index >= 0 && index < this.Count, nameof(index));

                if (index < this.Count - 1)
                {
                    Array.Copy(_elements, index + 1, _elements, index, this.Count - index - 1);
                }

                this.Count--;
            }

            /// <summary>
            /// Removes the specified values from this list.
            /// </summary>
            /// <param name="index">The 0-based index into the array for the element to omit from the returned array.</param>
            /// <param name="length">The number of elements to remove.</param>
            public void RemoveRange(int index, int length)
            {
                Requires.Range(index >= 0 && index <= _count, nameof(index));
                Requires.Range(length >= 0 && index <= _count - length, nameof(length));

                if (length == 0)
                {
                    return;
                }

                if (index + length < this._count)
                {
                    Array.Copy(_elements, index + length, _elements, index, this.Count - index - length);
                }

                this.Count -= length;
            }

            /// <summary>
            /// Replaces the first equal element in the list with the specified element.
            /// </summary>
            /// <param name="oldValue">The element to replace.</param>
            /// <param name="newValue">The element to replace the old element with.</param>
            public void Replace(T oldValue, T newValue)
            {
                this.Replace(oldValue, newValue, EqualityComparer<T>.Default);
            }

            /// <summary>
            /// Replaces the first equal element in the list with the specified element.
            /// </summary>
            /// <param name="oldValue">The element to replace.</param>
            /// <param name="newValue">The element to replace the old element with.</param>
            /// <param name="equalityComparer">
            /// The equality comparer to use in the search.
            /// If <c>null</c>, <see cref="EqualityComparer{T}.Default"/> is used.
            /// </param>
            public void Replace(T oldValue, T newValue, IEqualityComparer<T>? equalityComparer)
            {
                int index = this.IndexOf(oldValue, 0, _count, equalityComparer);

                if (index >= 0)
                {
                    _elements[index] = newValue;
                }
            }

            /// <summary>
            /// Determines whether the <see cref="ICollection{T}"/> contains a specific value.
            /// </summary>
            /// <param name="item">The object to locate in the <see cref="ICollection{T}"/>.</param>
            /// <returns>
            /// true if <paramref name="item"/> is found in the <see cref="ICollection{T}"/>; otherwise, false.
            /// </returns>
            public bool Contains(T item)
            {
                return this.IndexOf(item) >= 0;
            }

            /// <summary>
            /// Creates a new array with the current contents of this Builder.
            /// </summary>
            public T[] ToArray()
            {
                if (this.Count == 0)
                {
                    return Empty.array!;
                }

                T[] result = new T[this.Count];
                Array.Copy(_elements, result, this.Count);
                return result;
            }

            /// <summary>
            /// Copies the current contents to the specified array.
            /// </summary>
            /// <param name="array">The array to copy to.</param>
            /// <param name="index">The starting index of the target array.</param>
            public void CopyTo(T[] array, int index)
            {
                Requires.NotNull(array, nameof(array));
                Requires.Range(index >= 0 && index + this.Count <= array.Length, nameof(index));
                Array.Copy(_elements, 0, array, index, this.Count);
            }

            /// <summary>
            /// Copies the contents of this array to the specified array.
            /// </summary>
            /// <param name="destination">The array to copy to.</param>
            public void CopyTo(T[] destination)
            {
                Requires.NotNull(destination, nameof(destination));
                Array.Copy(_elements, 0, destination, 0, this.Count);
            }

            /// <summary>
            /// Copies the contents of this array to the specified array.
            /// </summary>
            /// <param name="sourceIndex">The index into this collection of the first element to copy.</param>
            /// <param name="destination">The array to copy to.</param>
            /// <param name="destinationIndex">The index into the destination array to which the first copied element is written.</param>
            /// <param name="length">The number of elements to copy.</param>
            public void CopyTo(int sourceIndex, T[] destination, int destinationIndex, int length)
            {
                Requires.NotNull(destination, nameof(destination));
                Requires.Range(length >= 0, nameof(length));
                Requires.Range(sourceIndex >= 0 && sourceIndex + length <= this.Count, nameof(sourceIndex));
                Requires.Range(destinationIndex >= 0 && destinationIndex + length <= destination.Length, nameof(destinationIndex));
                Array.Copy(_elements, sourceIndex, destination, destinationIndex, length);
            }

            /// <summary>
            /// Resizes the array to accommodate the specified capacity requirement.
            /// </summary>
            /// <param name="capacity">The required capacity.</param>
            private void EnsureCapacity(int capacity)
            {
                if (_elements.Length < capacity)
                {
                    int newCapacity = Math.Max(_elements.Length * 2, capacity);
                    Array.Resize(ref _elements, newCapacity);
                }
            }

            /// <summary>
            /// Determines the index of a specific item in the <see cref="IList{T}"/>.
            /// </summary>
            /// <param name="item">The object to locate in the <see cref="IList{T}"/>.</param>
            /// <returns>
            /// The index of <paramref name="item"/> if found in the list; otherwise, -1.
            /// </returns>
            public int IndexOf(T item)
            {
                return this.IndexOf(item, 0, _count, EqualityComparer<T>.Default);
            }

            /// <summary>
            /// Searches the array for the specified item.
            /// </summary>
            /// <param name="item">The item to search for.</param>
            /// <param name="startIndex">The index at which to begin the search.</param>
            /// <returns>The 0-based index into the array where the item was found; or -1 if it could not be found.</returns>
            public int IndexOf(T item, int startIndex)
            {
                return this.IndexOf(item, startIndex, this.Count - startIndex, EqualityComparer<T>.Default);
            }

            /// <summary>
            /// Searches the array for the specified item.
            /// </summary>
            /// <param name="item">The item to search for.</param>
            /// <param name="startIndex">The index at which to begin the search.</param>
            /// <param name="count">The number of elements to search.</param>
            /// <returns>The 0-based index into the array where the item was found; or -1 if it could not be found.</returns>
            public int IndexOf(T item, int startIndex, int count)
            {
                return this.IndexOf(item, startIndex, count, EqualityComparer<T>.Default);
            }

            /// <summary>
            /// Searches the array for the specified item.
            /// </summary>
            /// <param name="item">The item to search for.</param>
            /// <param name="startIndex">The index at which to begin the search.</param>
            /// <param name="count">The number of elements to search.</param>
            /// <param name="equalityComparer">
            /// The equality comparer to use in the search.
            /// If <c>null</c>, <see cref="EqualityComparer{T}.Default"/> is used.
            /// </param>
            /// <returns>The 0-based index into the array where the item was found; or -1 if it could not be found.</returns>
            public int IndexOf(T item, int startIndex, int count, IEqualityComparer<T>? equalityComparer)
            {
                if (count == 0 && startIndex == 0)
                {
                    return -1;
                }

                Requires.Range(startIndex >= 0 && startIndex <= this.Count, nameof(startIndex));
                Requires.Range(count >= 0 && (uint)(startIndex + count) <= (uint)this.Count, nameof(count));

                equalityComparer ??= EqualityComparer<T>.Default;
                if (equalityComparer == EqualityComparer<T>.Default)
                {
                    return Array.IndexOf(_elements, item, startIndex, count);
                }
                else
                {
                    for (int i = startIndex; i < startIndex + count; i++)
                    {
                        if (equalityComparer.Equals(_elements[i], item))
                        {
                            return i;
                        }
                    }

                    return -1;
                }
            }

            /// <summary>
            /// Searches the array for the specified item.
            /// </summary>
            /// <param name="item">The item to search for.</param>
            /// <param name="startIndex">The index at which to begin the search.</param>
            /// <param name="equalityComparer">
            /// The equality comparer to use in the search.
            /// If <c>null</c>, <see cref="EqualityComparer{T}.Default"/> is used.
            /// </param>
            /// <returns>The 0-based index into the array where the item was found; or -1 if it could not be found.</returns>
            public int IndexOf(T item, int startIndex, IEqualityComparer<T>? equalityComparer)
            {
                return this.IndexOf(item, startIndex, this.Count - startIndex, equalityComparer);
            }

            /// <summary>
            /// Copies the current contents to the specified <see cref="Span{T}"/>.
            /// </summary>
            /// <param name="destination">The <see cref="Span{T}"/> to copy to.</param>
            public void CopyTo(Span<T> destination)
            {
                Requires.Range(this.Count <= destination.Length, nameof(destination));
                new ReadOnlySpan<T>(_elements, 0, this.Count).CopyTo(destination);
            }

            /// <summary>
            /// Returns an enumerator for the contents of the array.
            /// </summary>
            /// <returns>An enumerator.</returns>
            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < this.Count; i++)
                {
                    yield return this[i];
                }
            }

            /// <summary>
            /// Returns an enumerator for the contents of the array.
            /// </summary>
            /// <returns>An enumerator.</returns>
            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return this.GetEnumerator();
            }

            /// <summary>
            /// Returns an enumerator for the contents of the array.
            /// </summary>
            /// <returns>An enumerator.</returns>
            IEnumerator IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }

            /// <summary>
            /// Adds items to this collection.
            /// </summary>
            /// <typeparam name="TDerived">The type of source elements.</typeparam>
            /// <param name="items">The source array.</param>
            /// <param name="length">The number of elements to add to this array.</param>
            private void AddRange<TDerived>(TDerived[] items, int length) where TDerived : T
            {
                this.EnsureCapacity(this.Count + length);

                int offset = this.Count;
                this.Count += length;

                T[] nodes = _elements;
                for (int i = 0; i < length; i++)
                {
                    nodes[offset + i] = items[i];
                }
            }
        }
    }

    /// <summary>
    /// Common runtime checks that throw <see cref="ArgumentException"/> upon failure.
    /// </summary>
    internal static class Requires
    {
        /// <summary>
        /// Throws an exception if the specified parameter's value is null.
        /// </summary>
        /// <typeparam name="T">The type of the parameter.</typeparam>
        /// <param name="value">The value of the argument.</param>
        /// <param name="parameterName">The name of the parameter to include in any thrown exception.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is <c>null</c></exception>
        [DebuggerStepThrough]
        public static void NotNull<T>([NotNull] T value, string? parameterName)
            where T : class // ensures value-types aren't passed to a null checking method
        {
            if (value == null)
            {
                FailArgumentNullException(parameterName);
            }
        }

        /// <summary>
        /// Throws an exception if the specified parameter's value is null.  It passes through the specified value back as a return value.
        /// </summary>
        /// <typeparam name="T">The type of the parameter.</typeparam>
        /// <param name="value">The value of the argument.</param>
        /// <param name="parameterName">The name of the parameter to include in any thrown exception.</param>
        /// <returns>The value of the parameter.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is <c>null</c></exception>
        [DebuggerStepThrough]
        public static T NotNullPassthrough<T>([NotNull] T value, string? parameterName)
            where T : class // ensures value-types aren't passed to a null checking method
        {
            NotNull(value, parameterName);
            return value;
        }

        /// <summary>
        /// Throws an exception if the specified parameter's value is null.
        /// </summary>
        /// <typeparam name="T">The type of the parameter.</typeparam>
        /// <param name="value">The value of the argument.</param>
        /// <param name="parameterName">The name of the parameter to include in any thrown exception.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is <c>null</c></exception>
        /// <remarks>
        /// This method exists for callers who themselves only know the type as a generic parameter which
        /// may or may not be a class, but certainly cannot be null.
        /// </remarks>
        [DebuggerStepThrough]
        public static void NotNullAllowStructs<T>([NotNull] T value, string? parameterName)
        {
            if (null == value)
            {
                FailArgumentNullException(parameterName);
            }
        }

        /// <summary>
        /// Throws an <see cref="ArgumentNullException"/>.
        /// </summary>
        /// <param name="parameterName">The name of the parameter that was null.</param>
        [DoesNotReturn]
        [DebuggerStepThrough]
        public static void FailArgumentNullException(string? parameterName)
        {
            // Separating out this throwing operation helps with inlining of the caller
            throw new ArgumentNullException(parameterName);
        }

        /// <summary>
        /// Throws an <see cref="ArgumentOutOfRangeException"/> if a condition does not evaluate to true.
        /// </summary>
        [DebuggerStepThrough]
        public static void Range([DoesNotReturnIf(false)] bool condition, string? parameterName, string? message = null)
        {
            if (!condition)
            {
                FailRange(parameterName, message);
            }
        }

        /// <summary>
        /// Throws an <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        [DoesNotReturn]
        [DebuggerStepThrough]
        public static void FailRange(string? parameterName, string? message = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
            else
            {
                throw new ArgumentOutOfRangeException(parameterName, message);
            }
        }

        /// <summary>
        /// Throws an <see cref="ArgumentException"/> if a condition does not evaluate to true.
        /// </summary>
        [DebuggerStepThrough]
        public static void Argument([DoesNotReturnIf(false)] bool condition, string? parameterName, string? message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, parameterName);
            }
        }

        /// <summary>
        /// Throws an <see cref="ArgumentException"/> if a condition does not evaluate to true.
        /// </summary>
        [DebuggerStepThrough]
        public static void Argument([DoesNotReturnIf(false)] bool condition)
        {
            if (!condition)
            {
                throw new ArgumentException();
            }
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> for a disposed object.
        /// </summary>
        /// <typeparam name="TDisposed">Specifies the type of the disposed object.</typeparam>
        /// <param name="disposed">The disposed object.</param>
        [DoesNotReturn]
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.NoInlining)] // inlining this on .NET < 4.5.2 on x64 causes InvalidProgramException.
        public static void FailObjectDisposed<TDisposed>(TDisposed disposed)
        {
            // separating out this throwing helps with inlining of the caller, especially
            // due to the retrieval of the type's name
            throw new ObjectDisposedException(disposed!.GetType().FullName);
        }
    }
}
