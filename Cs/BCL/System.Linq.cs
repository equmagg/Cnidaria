using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Linq.Utilities;

namespace System.Linq
{
    internal enum ExceptionArgument
    {
        collectionSelector,
        count,
        elementSelector,
        enumerable,
        first,
        func,
        index,
        inner,
        innerKeySelector,
        keySelector,
        outer,
        outerKeySelector,
        predicate,
        resultSelector,
        second,
        seedSelector,
        selector,
        source,
        third,
        size,
        other,
        start,
        step,
        endInclusive,
    }

    internal static class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowArgumentNullException(ExceptionArgument argument) => throw new ArgumentNullException(GetArgumentString(argument));

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument) => throw new ArgumentOutOfRangeException(GetArgumentString(argument));

        [DoesNotReturn]
        internal static void ThrowMoreThanOneElementException() => throw new InvalidOperationException(SR.MoreThanOneElement);

        [DoesNotReturn]
        internal static void ThrowMoreThanOneMatchException() => throw new InvalidOperationException(SR.MoreThanOneMatch);

        [DoesNotReturn]
        internal static void ThrowNoElementsException() => throw new InvalidOperationException(SR.NoElements);

        [DoesNotReturn]
        internal static void ThrowNoMatchException() => throw new InvalidOperationException(SR.NoMatch);

        [DoesNotReturn]
        internal static void ThrowNotSupportedException() => throw new NotSupportedException();

        [DoesNotReturn]
        internal static bool ThrowNotSupportedException_Boolean() => throw new NotSupportedException();

        [DoesNotReturn]
        internal static void ThrowOverflowException() => throw new OverflowException();

        private static string GetArgumentString(ExceptionArgument argument)
        {
            switch (argument)
            {
                case ExceptionArgument.collectionSelector: return nameof(ExceptionArgument.collectionSelector);
                case ExceptionArgument.count: return nameof(ExceptionArgument.count);
                case ExceptionArgument.elementSelector: return nameof(ExceptionArgument.elementSelector);
                case ExceptionArgument.enumerable: return nameof(ExceptionArgument.enumerable);
                case ExceptionArgument.first: return nameof(ExceptionArgument.first);
                case ExceptionArgument.func: return nameof(ExceptionArgument.func);
                case ExceptionArgument.index: return nameof(ExceptionArgument.index);
                case ExceptionArgument.inner: return nameof(ExceptionArgument.inner);
                case ExceptionArgument.innerKeySelector: return nameof(ExceptionArgument.innerKeySelector);
                case ExceptionArgument.keySelector: return nameof(ExceptionArgument.keySelector);
                case ExceptionArgument.outer: return nameof(ExceptionArgument.outer);
                case ExceptionArgument.outerKeySelector: return nameof(ExceptionArgument.outerKeySelector);
                case ExceptionArgument.predicate: return nameof(ExceptionArgument.predicate);
                case ExceptionArgument.resultSelector: return nameof(ExceptionArgument.resultSelector);
                case ExceptionArgument.second: return nameof(ExceptionArgument.second);
                case ExceptionArgument.seedSelector: return nameof(ExceptionArgument.seedSelector);
                case ExceptionArgument.selector: return nameof(ExceptionArgument.selector);
                case ExceptionArgument.source: return nameof(ExceptionArgument.source);
                case ExceptionArgument.third: return nameof(ExceptionArgument.third);
                case ExceptionArgument.size: return nameof(ExceptionArgument.size);
                case ExceptionArgument.other: return nameof(ExceptionArgument.other);
                case ExceptionArgument.start: return nameof(ExceptionArgument.start);
                case ExceptionArgument.step: return nameof(ExceptionArgument.step);
                case ExceptionArgument.endInclusive: return nameof(ExceptionArgument.endInclusive);
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// Contains helper methods for System.Linq.
    /// </summary>
    internal static class Utilities
    {
        /// <summary>
        /// Decides if two equality comparers are equivalent.
        /// </summary>
        /// <typeparam name="TSource">The type of each comparer.</typeparam>
        /// <param name="left">The first comparer.</param>
        /// <param name="right">The second comparer.</param>
        /// <returns><c>true</c> if the equality comparers are equal; otherwise, <c>false</c>.</returns>
        public static bool AreEqualityComparersEqual<TSource>(IEqualityComparer<TSource>? left, IEqualityComparer<TSource>? right)
        {
            if (left == right)
            {
                return true;
            }

            var defaultComparer = EqualityComparer<TSource>.Default;

            if (left is null)
            {
                // Micro-opt: Typically it's impossible to get a different instance
                // of the default comparer without reflection/serialization.
                // Save a virtual method call to Equals in the common case.
                return right == defaultComparer || right!.Equals(defaultComparer);
            }

            if (right is null)
            {
                return left == defaultComparer || left.Equals(defaultComparer);
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Combines two predicates.
        /// </summary>
        /// <typeparam name="TSource">The type of the predicate argument.</typeparam>
        /// <param name="predicate1">The first predicate to run.</param>
        /// <param name="predicate2">The second predicate to run.</param>
        /// <returns>
        /// A new predicate that will evaluate to <c>true</c> only if both the first and
        /// second predicates return true. If the first predicate returns <c>false</c>,
        /// the second predicate will not be run.
        /// </returns>
        public static Func<TSource, bool> CombinePredicates<TSource>(Func<TSource, bool> predicate1, Func<TSource, bool> predicate2) =>
            x => predicate1(x) && predicate2(x);

        /// <summary>
        /// Combines two selectors.
        /// </summary>
        /// <typeparam name="TSource">The type of the first selector's argument.</typeparam>
        /// <typeparam name="TMiddle">The type of the second selector's argument.</typeparam>
        /// <typeparam name="TResult">The type of the second selector's return value.</typeparam>
        /// <param name="selector1">The first selector to run.</param>
        /// <param name="selector2">The second selector to run.</param>
        /// <returns>
        /// A new selector that represents the composition of the first selector with the second selector.
        /// </returns>
        public static Func<TSource, TResult> CombineSelectors<TSource, TMiddle, TResult>(Func<TSource, TMiddle> selector1, Func<TMiddle, TResult> selector2) =>
            x => selector2(selector1(x));
    }

    public static class Enumerable
    {
        internal static bool IsSizeOptimized { get; } = false;

        public static IEnumerable<TSource> AsEnumerable<TSource>(this IEnumerable<TSource> source) => source;

        /// <summary>Returns an empty <see cref="IEnumerable{TResult}"/>.</summary>
        public static IEnumerable<TResult> Empty<TResult>() =>
            Array.Empty<TResult>(); // explicitly not using [] in case the compiler ever changed to using Enumerable.Empty

        /// <summary>Gets whether the enumerable is an empty array</summary>
        /// <remarks>
        /// If <see cref="Empty{TResult}"/> is ever changed to return something other than an empty array,
        /// this helper should also be updated to return true for that in addition to for an empty array.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEmptyArray<TSource>(IEnumerable<TSource> source) =>
            source is TSource[] { Length: 0 };

        /// <summary>
        /// Sets the <paramref name="list"/>'s <see cref="List{T}.Count"/> to be <paramref name="count"/>
        /// and returns the relevant portion of the list's backing array as a span.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Span<T> SetCountAndGetSpan<T>(List<T> list, int count)
        {
            CollectionsMarshal.SetCount(list, count);
            return CollectionsMarshal.AsSpan(list);
        }

        /// <summary>Validates that source is not null and then tries to extract a span from the source.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // fast type checks that don't add a lot of overhead
        internal static bool TryGetSpan<TSource>(this IEnumerable<TSource> source, out ReadOnlySpan<TSource> span)
        {
            // Use `GetType() == typeof(...)` rather than `is` to avoid cast helpers.  This is measurably cheaper
            // but does mean we could end up missing some rare cases where we could get a span but don't (e.g. a uint[]
            // masquerading as an int[]).  That's an acceptable tradeoff.  The Unsafe usage is only after we've
            // validated the exact type; this could be changed to a cast in the future if the JIT starts to recognize it.
            // We only pay the comparison/branching costs here for super common types we expect to be used frequently
            // with LINQ methods.

            bool result = true;
            if (source.GetType() == typeof(TSource[]))
            {
                span = Unsafe.As<TSource[]>(source);
            }
            else if (source.GetType() == typeof(List<TSource>))
            {
                span = CollectionsMarshal.AsSpan(Unsafe.As<List<TSource>>(source));
            }
            else
            {
                span = default;
                result = false;
            }

            return result;
        }

        public static TSource[] ToArray<TSource>(this IEnumerable<TSource> source)
        {
            if (!IsSizeOptimized && source is Iterator<TSource> iterator)
            {
                return iterator.ToArray();
            }

            if (source is ICollection<TSource> collection)
            {
                return ICollectionToArray(collection);
            }

            return EnumerableToArray(source);

            [MethodImpl(MethodImplOptions.NoInlining)] // avoid large stack allocation impacting other paths
            static TSource[] EnumerableToArray(IEnumerable<TSource> source)
            {
                if (source is null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
                }

                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                builder.AddNonICollectionRangeInlined(source);
                TSource[] result = builder.ToArray();

                builder.Dispose();
                return result;
            }
        }

        private static TSource[] ICollectionToArray<TSource>(ICollection<TSource> collection)
        {
            int count = collection.Count;
            if (count != 0)
            {
                var result = new TSource[count];
                collection.CopyTo(result, 0);
                return result;
            }

            return [];
        }

        public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (!IsSizeOptimized && source is Iterator<TSource> iterator)
            {
                return iterator.ToList();
            }

            return new List<TSource>(source);
        }

        /// <summary>
        /// Creates a <see cref="Dictionary{TKey,TValue}"/> from an <see cref="IEnumerable{T}"/> according to the default comparer for the key type.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys from elements of <paramref name="source"/></typeparam>
        /// <typeparam name="TValue">The type of the values from elements of <paramref name="source"/></typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to create a <see cref="Dictionary{TKey,TValue}"/> from.</param>
        /// <returns>A <see cref="Dictionary{TKey,TValue}"/> that contains keys and values from <paramref name="source"/> and uses default comparer for the key type.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is a null reference.</exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> contains one or more duplicate keys.</exception>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source) where TKey : notnull =>
            source.ToDictionary(null);

        /// <summary>
        /// Creates a <see cref="Dictionary{TKey,TValue}"/> from an <see cref="IEnumerable{T}"/> according to specified key comparer.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys from elements of <paramref name="source"/></typeparam>
        /// <typeparam name="TValue">The type of the values from elements of <paramref name="source"/></typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to create a <see cref="Dictionary{TKey,TValue}"/> from.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer{TKey}"/> to compare keys.</param>
        /// <returns>A <see cref="Dictionary{TKey,TValue}"/> that contains keys and values from <paramref name="source"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is a null reference.</exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> contains one or more duplicate keys.</exception>
        /// <remarks>
        /// If <paramref name="comparer"/> is null, the default equality comparer <see cref="EqualityComparer{TKey}.Default"/> is used to compare keys.
        /// </remarks>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source, IEqualityComparer<TKey>? comparer) where TKey : notnull
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return new(source, comparer);
        }

        /// <summary>
        /// Creates a <see cref="Dictionary{TKey,TValue}"/> from an <see cref="IEnumerable{T}"/> according to the default comparer for the key type.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys from elements of <paramref name="source"/></typeparam>
        /// <typeparam name="TValue">The type of the values from elements of <paramref name="source"/></typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to create a <see cref="Dictionary{TKey,TValue}"/> from.</param>
        /// <returns>A <see cref="Dictionary{TKey,TValue}"/> that contains keys and values from <paramref name="source"/> and uses default comparer for the key type.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is a null reference.</exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> contains one or more duplicate keys.</exception>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<(TKey Key, TValue Value)> source) where TKey : notnull =>
            source.ToDictionary(null);

        /// <summary>
        /// Creates a <see cref="Dictionary{TKey,TValue}"/> from an <see cref="IEnumerable{T}"/> according to specified key comparer.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys from elements of <paramref name="source"/></typeparam>
        /// <typeparam name="TValue">The type of the values from elements of <paramref name="source"/></typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to create a <see cref="Dictionary{TKey,TValue}"/> from.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer{TKey}"/> to compare keys.</param>
        /// <returns>A <see cref="Dictionary{TKey,TValue}"/> that contains keys and values from <paramref name="source"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is a null reference.</exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> contains one or more duplicate keys.</exception>
        /// <remarks>
        /// If <paramref name="comparer"/> is null, the default equality comparer <see cref="EqualityComparer{TKey}.Default"/> is used to compare keys.
        /// </remarks>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<(TKey Key, TValue Value)> source, IEqualityComparer<TKey>? comparer) where TKey : notnull =>
            source.ToDictionary(vt => vt.Key, vt => vt.Value, comparer);

        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) where TKey : notnull =>
            ToDictionary(source, keySelector, null);

        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer) where TKey : notnull
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (keySelector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.keySelector);
            }

            if (source.TryGetNonEnumeratedCount(out int capacity))
            {
                if (capacity == 0)
                {
                    return new Dictionary<TKey, TSource>(comparer);
                }

                if (source is TSource[] array)
                {
                    return SpanToDictionary(array, keySelector, comparer);
                }

                if (source is List<TSource> list)
                {
                    ReadOnlySpan<TSource> span = CollectionsMarshal.AsSpan(list);
                    return SpanToDictionary(span, keySelector, comparer);
                }
            }

            Dictionary<TKey, TSource> d = new Dictionary<TKey, TSource>(capacity, comparer);
            foreach (TSource element in source)
            {
                d.Add(keySelector(element), element);
            }

            return d;
        }

        private static Dictionary<TKey, TSource> SpanToDictionary<TSource, TKey>(ReadOnlySpan<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer) where TKey : notnull
        {
            Dictionary<TKey, TSource> d = new Dictionary<TKey, TSource>(source.Length, comparer);
            foreach (TSource element in source)
            {
                d.Add(keySelector(element), element);
            }
            return d;
        }

        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector) where TKey : notnull =>
            ToDictionary(source, keySelector, elementSelector, null);

        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey>? comparer) where TKey : notnull
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (keySelector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.keySelector);
            }

            if (elementSelector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.elementSelector);
            }

            if (source.TryGetNonEnumeratedCount(out int capacity))
            {
                if (capacity == 0)
                {
                    return new Dictionary<TKey, TElement>(comparer);
                }

                if (source is TSource[] array)
                {
                    return SpanToDictionary(array, keySelector, elementSelector, comparer);
                }

                if (source is List<TSource> list)
                {
                    ReadOnlySpan<TSource> span = CollectionsMarshal.AsSpan(list);
                    return SpanToDictionary(span, keySelector, elementSelector, comparer);
                }
            }

            Dictionary<TKey, TElement> d = new Dictionary<TKey, TElement>(capacity, comparer);
            foreach (TSource element in source)
            {
                d.Add(keySelector(element), elementSelector(element));
            }

            return d;
        }

        private static Dictionary<TKey, TElement> SpanToDictionary<TSource, TKey, TElement>(ReadOnlySpan<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey>? comparer) where TKey : notnull
        {
            Dictionary<TKey, TElement> d = new Dictionary<TKey, TElement>(source.Length, comparer);
            foreach (TSource element in source)
            {
                d.Add(keySelector(element), elementSelector(element));
            }
            return d;
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source) => source.ToHashSet(comparer: null);

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            // Don't pre-allocate based on knowledge of size, as potentially many elements will be dropped.
            return new HashSet<TSource>(source, comparer);
        }

        /// <summary>Default initial capacity to use when creating sets for internal temporary storage.</summary>
        /// <remarks>This is based on the implicit size used in previous implementations, which used a custom Set type.</remarks>
        private const int DefaultInternalSetCapacity = 7;

        public static TSource Last<TSource>(this IEnumerable<TSource> source)
        {
            TSource? last = source.TryGetLast(out bool found);
            if (!found)
            {
                ThrowHelper.ThrowNoElementsException();
            }

            return last!;
        }

        public static TSource Last<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            TSource? last = source.TryGetLast(predicate, out bool found);
            if (!found)
            {
                ThrowHelper.ThrowNoMatchException();
            }

            return last!;
        }

        public static TSource? LastOrDefault<TSource>(this IEnumerable<TSource> source) =>
            source.TryGetLast(out _);

        /// <summary>Returns the last element of a sequence, or a default value if the sequence contains no elements.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{T}" /> to return the last element of.</param>
        /// <param name="defaultValue">The default value to return if the sequence is empty.</param>
        /// <returns><paramref name="defaultValue" /> if the source sequence is empty; otherwise, the last element in the <see cref="IEnumerable{T}" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        public static TSource LastOrDefault<TSource>(this IEnumerable<TSource> source, TSource defaultValue)
        {
            TSource? last = source.TryGetLast(out bool found);
            return found ? last! : defaultValue;
        }

        public static TSource? LastOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
            => source.TryGetLast(predicate, out _);

        /// <summary>Returns the last element of a sequence that satisfies a condition or a default value if no such element is found.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{T}" /> to return an element from.</param>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="defaultValue">The default value to return if the sequence is empty.</param>
        /// <returns><paramref name="defaultValue" /> if the sequence is empty or if no elements pass the test in the predicate function; otherwise, the last element that passes the test in the predicate function.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> or <paramref name="predicate" /> is <see langword="null" />.</exception>
        public static TSource LastOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue)
        {
            TSource? last = source.TryGetLast(predicate, out bool found);
            return found ? last! : defaultValue;
        }

        private static TSource? TryGetLast<TSource>(this IEnumerable<TSource> source, out bool found)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return
                source is Iterator<TSource> iterator ? iterator.TryGetLast(out found) :
                TryGetLastNonIterator(source, out found);
        }

        private static TSource? TryGetLastNonIterator<TSource>(IEnumerable<TSource> source, out bool found)
        {
            if (source is IList<TSource> list)
            {
                int count = list.Count;
                if (count > 0)
                {
                    found = true;
                    return list[count - 1];
                }
            }
            else
            {
                using IEnumerator<TSource> e = source.GetEnumerator();
                if (e.MoveNext())
                {
                    TSource result;
                    do
                    {
                        result = e.Current;
                    }
                    while (e.MoveNext());

                    found = true;
                    return result;
                }
            }

            found = false;
            return default;
        }

        private static TSource? TryGetLast<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, out bool found)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (predicate is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);
            }

            if (source is OrderedIterator<TSource> ordered)
            {
                return ordered.TryGetLast(predicate, out found);
            }

            if (source is IList<TSource> list)
            {
                for (int i = list.Count - 1; i >= 0; --i)
                {
                    TSource result = list[i];
                    if (predicate(result))
                    {
                        found = true;
                        return result;
                    }
                }
            }
            else
            {
                using IEnumerator<TSource> e = source.GetEnumerator();
                while (e.MoveNext())
                {
                    TSource result = e.Current;
                    if (predicate(result))
                    {
                        while (e.MoveNext())
                        {
                            TSource element = e.Current;
                            if (predicate(element))
                            {
                                result = element;
                            }
                        }

                        found = true;
                        return result;
                    }
                }
            }

            found = false;
            return default;
        }

        public static int Min(this IEnumerable<int> source) => Min(source, comparer: null);

        public static long Min(this IEnumerable<long> source) => Min(source, comparer: null);

        private static T MinIntegerEnumerator<T>(IEnumerable<T> source) where T : struct, IBinaryInteger<T>
        {
            using IEnumerator<T> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            T value = e.Current;
            while (e.MoveNext())
            {
                T x = e.Current;
                if (x < value)
                {
                    value = x;
                }
            }

            return value;
        }

        public static int? Min(this IEnumerable<int?> source) => MinInteger(source);

        public static long? Min(this IEnumerable<long?> source) => MinInteger(source);

        private static T? MinInteger<T>(this IEnumerable<T?> source) where T : struct, IBinaryInteger<T>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            T? value = null;
            using IEnumerator<T?> e = source.GetEnumerator();
            // Start off knowing that we have a non-null value (or exit here, knowing we don't)
            // so we don't have to keep testing for nullity.
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }
            while (!value.HasValue);

            // Keep hold of the wrapped value, and do comparisons on that, rather than
            // using the lifted operation each time.
            T valueVal = value.GetValueOrDefault();
            while (e.MoveNext())
            {
                T? cur = e.Current;
                T x = cur.GetValueOrDefault();

                // Do not replace & with &&. The branch prediction cost outweighs the extra operation
                // unless nulls either never happen or always happen.
                if (cur.HasValue & x < valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static float Min(this IEnumerable<float> source) => MinFloat(source);

        public static float? Min(this IEnumerable<float?> source) => MinFloat(source);

        public static double Min(this IEnumerable<double> source) => MinFloat(source);

        public static double? Min(this IEnumerable<double?> source) => MinFloat(source);

        private static T MinFloat<T>(this IEnumerable<T> source) where T : struct, IFloatingPointIeee754<T>
        {
            T value;

            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source.TryGetSpan(out ReadOnlySpan<T> span))
            {
                if (span.IsEmpty)
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = span[0];
                for (int i = 1; (uint)i < (uint)span.Length; i++)
                {
                    T current = span[i];
                    if (current < value)
                    {
                        value = current;
                    }
                    else if (T.IsNaN(current))
                    {
                        return current;
                    }
                }

                return value;
            }

            using IEnumerator<T> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = e.Current;
            if (T.IsNaN(value))
            {
                return value;
            }

            while (e.MoveNext())
            {
                T x = e.Current;
                if (x < value)
                {
                    value = x;
                }

                // Normally NaN < anything is false, as is anything < NaN
                // However, this leads to some irksome outcomes in Min and Max.
                // If we use those semantics then Min(NaN, 5.0) is NaN, but
                // Min(5.0, NaN) is 5.0!  To fix this, we impose a total
                // ordering where NaN is smaller than every value, including
                // negative infinity.
                // Not testing for NaN therefore isn't an option, but since we
                // can't find a smaller value, we can short-circuit.
                else if (T.IsNaN(x))
                {
                    return x;
                }
            }

            return value;
        }

        private static T? MinFloat<T>(this IEnumerable<T?> source) where T : struct, IFloatingPointIeee754<T>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            T? value = null;

            using IEnumerator<T?> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }
            while (!value.HasValue);

            T valueVal = value.GetValueOrDefault();
            if (T.IsNaN(valueVal))
            {
                return value;
            }

            while (e.MoveNext())
            {
                T? cur = e.Current;
                if (cur.HasValue)
                {
                    T x = cur.GetValueOrDefault();
                    if (x < valueVal)
                    {
                        valueVal = x;
                        value = cur;
                    }
                    else if (T.IsNaN(x))
                    {
                        return cur;
                    }
                }
            }

            return value;
        }

        public static decimal Min(this IEnumerable<decimal> source)
        {
            decimal value;

            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source.TryGetSpan(out ReadOnlySpan<decimal> span))
            {
                if (span.IsEmpty)
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = span[0];
                for (int i = 1; (uint)i < (uint)span.Length; i++)
                {
                    if (span[i] < value)
                    {
                        value = span[i];
                    }
                }

                return value;
            }

            using IEnumerator<decimal> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = e.Current;
            while (e.MoveNext())
            {
                decimal x = e.Current;
                if (x < value)
                {
                    value = x;
                }
            }

            return value;
        }

        public static decimal? Min(this IEnumerable<decimal?> source)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            decimal? value = null;
            using IEnumerator<decimal?> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }
            while (!value.HasValue);

            decimal valueVal = value.GetValueOrDefault();
            while (e.MoveNext())
            {
                decimal? cur = e.Current;
                decimal x = cur.GetValueOrDefault();
                if (cur.HasValue && x < valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static TSource? Min<TSource>(this IEnumerable<TSource> source) => Min(source, comparer: null);

        /// <summary>Returns the minimum value in a generic sequence.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">A sequence of values to determine the minimum value of.</param>
        /// <param name="comparer">The <see cref="IComparer{T}" /> to compare values.</param>
        /// <returns>The minimum value in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No object in <paramref name="source" /> implements the <see cref="System.IComparable" /> or <see cref="System.IComparable{T}" /> interface.</exception>
        /// <remarks>
        /// <para>If type <typeparamref name="TSource" /> implements <see cref="System.IComparable{T}" />, the <see cref="Min{T}(IEnumerable{T})" /> method uses that implementation to compare values. Otherwise, if type <typeparamref name="TSource" /> implements <see cref="System.IComparable" />, that implementation is used to compare values.</para>
        /// <para>If <typeparamref name="TSource" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// <para>In Visual Basic query expression syntax, an `Aggregate Into Min()` clause translates to an invocation of <see cref="O:Enumerable.Min" />.</para>
        /// </remarks>
        public static TSource? Min<TSource>(this IEnumerable<TSource> source, IComparer<TSource>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source.TryGetSpan(out ReadOnlySpan<TSource> span))
            {
                return span.Min(comparer);
            }

            // For the non-span integer sequences, use a direct comparison rather than paying the
            // per-element cost of Comparer<TSource>.Default.
            if (comparer is null || comparer == Comparer<TSource>.Default)
            {
                if (typeof(TSource) == typeof(byte)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<byte>)source);
                if (typeof(TSource) == typeof(sbyte)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<sbyte>)source);
                if (typeof(TSource) == typeof(ushort)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<ushort>)source);
                if (typeof(TSource) == typeof(short)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<short>)source);
                if (typeof(TSource) == typeof(char)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<char>)source);
                if (typeof(TSource) == typeof(uint)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<uint>)source);
                if (typeof(TSource) == typeof(int)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<int>)source);
                if (typeof(TSource) == typeof(ulong)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<ulong>)source);
                if (typeof(TSource) == typeof(long)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<long>)source);
                if (typeof(TSource) == typeof(nuint)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<nuint>)source);
                if (typeof(TSource) == typeof(nint)) return (TSource)(object)MinIntegerEnumerator((IEnumerable<nint>)source);
            }

            comparer ??= Comparer<TSource>.Default;

            TSource? value = default;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (value is null)
            {
                do
                {
                    if (!e.MoveNext())
                    {
                        return value;
                    }

                    value = e.Current;
                }
                while (value is null);

                while (e.MoveNext())
                {
                    TSource next = e.Current;
                    if (next is not null && comparer.Compare(next, value) < 0)
                    {
                        value = next;
                    }
                }
            }
            else
            {
                if (!e.MoveNext())
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = e.Current;
                if (comparer == Comparer<TSource>.Default)
                {
                    while (e.MoveNext())
                    {
                        TSource next = e.Current;
                        if (Comparer<TSource>.Default.Compare(next, value) < 0)
                        {
                            value = next;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource next = e.Current;
                        if (comparer.Compare(next, value) < 0)
                        {
                            value = next;
                        }
                    }
                }
            }

            return value;
        }

        /// <summary>Returns the minimum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the minimum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>The value with the minimum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="System.IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => MinBy(source, keySelector, comparer: null);

        /// <summary>Returns the minimum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the minimum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="comparer">The <see cref="IComparer{TKey}" /> to compare keys.</param>
        /// <returns>The value with the minimum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (keySelector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.keySelector);
            }

            comparer ??= Comparer<TKey>.Default;

            using IEnumerator<TSource> e = source.GetEnumerator();

            if (!e.MoveNext())
            {
                if (default(TSource) is null)
                {
                    return default;
                }
                else
                {
                    ThrowHelper.ThrowNoElementsException();
                }
            }

            TSource value = e.Current;
            TKey key = keySelector(value);

            if (default(TKey) is null)
            {
                if (key is null)
                {
                    TSource firstValue = value;

                    do
                    {
                        if (!e.MoveNext())
                        {
                            // All keys are null, surface the first element.
                            return firstValue;
                        }

                        value = e.Current;
                        key = keySelector(value);
                    }
                    while (key is null);
                }

                while (e.MoveNext())
                {
                    TSource nextValue = e.Current;
                    TKey nextKey = keySelector(nextValue);
                    if (nextKey is not null && comparer.Compare(nextKey, key) < 0)
                    {
                        key = nextKey;
                        value = nextValue;
                    }
                }
            }
            else
            {
                if (comparer == Comparer<TKey>.Default)
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (Comparer<TKey>.Default.Compare(nextKey, key) < 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (comparer.Compare(nextKey, key) < 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
            }

            return value;
        }

        public static int Min<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) => MinInteger(source, selector);

        public static int? Min<TSource>(this IEnumerable<TSource> source, Func<TSource, int?> selector) => MinInteger(source, selector);

        public static long Min<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector) => MinInteger(source, selector);

        public static long? Min<TSource>(this IEnumerable<TSource> source, Func<TSource, long?> selector) => MinInteger(source, selector);

        private static TResult MinInteger<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) where TResult : struct, IBinaryInteger<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult value;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = selector(e.Current);
            while (e.MoveNext())
            {
                TResult x = selector(e.Current);
                if (x < value)
                {
                    value = x;
                }
            }

            return value;
        }

        private static TResult? MinInteger<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult?> selector) where TResult : struct, IBinaryInteger<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult? value = null;
            using IEnumerator<TSource> e = source.GetEnumerator();
            // Start off knowing that we've a non-null value (or exit here, knowing we don't)
            // so we don't have to keep testing for nullity.
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }
            while (!value.HasValue);

            // Keep hold of the wrapped value, and do comparisons on that, rather than
            // using the lifted operation each time.
            TResult valueVal = value.GetValueOrDefault();
            while (e.MoveNext())
            {
                TResult? cur = selector(e.Current);
                TResult x = cur.GetValueOrDefault();

                // Do not replace & with &&. The branch prediction cost outweighs the extra operation
                // unless nulls either never happen or always happen.
                if (cur.HasValue & x < valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static float Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector) => MinFloat(source, selector);

        public static float? Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float?> selector) => MinFloat(source, selector);

        public static double Min<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) => MinFloat(source, selector);

        public static double? Min<TSource>(this IEnumerable<TSource> source, Func<TSource, double?> selector) => MinFloat(source, selector);

        private static TResult MinFloat<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) where TResult : struct, IFloatingPointIeee754<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult value;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = selector(e.Current);
            if (TResult.IsNaN(value))
            {
                return value;
            }

            while (e.MoveNext())
            {
                TResult x = selector(e.Current);
                if (x < value)
                {
                    value = x;
                }

                // Normally NaN < anything is false, as is anything < NaN
                // However, this leads to some irksome outcomes in Min and Max.
                // If we use those semantics then Min(NaN, 5.0) is NaN, but
                // Min(5.0, NaN) is 5.0!  To fix this, we impose a total
                // ordering where NaN is smaller than every value, including
                // negative infinity.
                // Not testing for NaN therefore isn't an option, but since we
                // can't find a smaller value, we can short-circuit.
                else if (TResult.IsNaN(x))
                {
                    return x;
                }
            }

            return value;
        }

        private static TResult? MinFloat<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult?> selector) where TResult : struct, IFloatingPointIeee754<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult? value = null;
            using IEnumerator<TSource> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }
            while (!value.HasValue);

            TResult valueVal = value.GetValueOrDefault();
            if (TResult.IsNaN(valueVal))
            {
                return value;
            }

            while (e.MoveNext())
            {
                TResult? cur = selector(e.Current);
                if (cur.HasValue)
                {
                    TResult x = cur.GetValueOrDefault();
                    if (x < valueVal)
                    {
                        valueVal = x;
                        value = cur;
                    }
                    else if (TResult.IsNaN(x))
                    {
                        return cur;
                    }
                }
            }

            return value;
        }

        public static decimal Min<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            decimal value;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = selector(e.Current);
            while (e.MoveNext())
            {
                decimal x = selector(e.Current);
                if (x < value)
                {
                    value = x;
                }
            }

            return value;
        }

        public static decimal? Min<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal?> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            decimal? value = null;
            using IEnumerator<TSource> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }
            while (!value.HasValue);

            decimal valueVal = value.GetValueOrDefault();
            while (e.MoveNext())
            {
                decimal? cur = selector(e.Current);
                decimal x = cur.GetValueOrDefault();
                if (cur.HasValue && x < valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static TResult? Min<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult? value = default;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (value is null)
            {
                do
                {
                    if (!e.MoveNext())
                    {
                        return value;
                    }

                    value = selector(e.Current);
                }
                while (value is null);

                Comparer<TResult> comparer = Comparer<TResult>.Default;
                while (e.MoveNext())
                {
                    TResult x = selector(e.Current);
                    if (x is not null && comparer.Compare(x, value) < 0)
                    {
                        value = x;
                    }
                }
            }
            else
            {
                if (!e.MoveNext())
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = selector(e.Current);
                while (e.MoveNext())
                {
                    TResult x = selector(e.Current);
                    if (Comparer<TResult>.Default.Compare(x, value) < 0)
                    {
                        value = x;
                    }
                }
            }

            return value;
        }

        public static int Max(this IEnumerable<int> source) => Max(source, comparer: null);

        public static long Max(this IEnumerable<long> source) => Max(source, comparer: null);

        private static T MaxIntegerEnumerator<T>(IEnumerable<T> source) where T : struct, IBinaryInteger<T>
        {
            using IEnumerator<T> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            T value = e.Current;
            while (e.MoveNext())
            {
                T x = e.Current;
                if (x > value)
                {
                    value = x;
                }
            }

            return value;
        }

        public static int? Max(this IEnumerable<int?> source) => MaxInteger(source);

        public static long? Max(this IEnumerable<long?> source) => MaxInteger(source);

        private static T? MaxInteger<T>(this IEnumerable<T?> source) where T : struct, IBinaryInteger<T>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            T? value = null;
            using IEnumerator<T?> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }
            while (!value.HasValue);

            T valueVal = value.GetValueOrDefault();
            if (valueVal >= T.Zero)
            {
                // To avoid having to check cur.HasValue every iteration of the loop,
                // we special-case the circumstance where the first value we found
                // is >= 0.  We can then compare its value against the value stored in
                // all subsequent nullables, regardless of whether they're null or not,
                // because if they are null, the value will be 0 and the comparison will
                // still be accurate.
                while (e.MoveNext())
                {
                    T? cur = e.Current;
                    T x = cur.GetValueOrDefault();
                    if (x > valueVal)
                    {
                        valueVal = x;
                        value = cur;
                    }
                }
            }
            else
            {
                while (e.MoveNext())
                {
                    T? cur = e.Current;
                    T x = cur.GetValueOrDefault();

                    // Do not replace & with &&. The branch prediction cost outweighs the extra operation
                    // unless nulls either never happen or always happen.
                    if (cur.HasValue & x > valueVal)
                    {
                        valueVal = x;
                        value = cur;
                    }
                }
            }

            return value;
        }

        public static double Max(this IEnumerable<double> source) => MaxFloat(source);

        public static double? Max(this IEnumerable<double?> source) => MaxFloat(source);

        public static float Max(this IEnumerable<float> source) => MaxFloat(source);

        public static float? Max(this IEnumerable<float?> source) => MaxFloat(source);

        private static T MaxFloat<T>(this IEnumerable<T> source) where T : struct, IFloatingPointIeee754<T>
        {
            T value;

            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source.TryGetSpan(out ReadOnlySpan<T> span))
            {
                if (span.IsEmpty)
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                int i;
                for (i = 0; i < span.Length && T.IsNaN(span[i]); i++) ;

                if (i == span.Length)
                {
                    return span[^1];
                }

                for (value = span[i]; (uint)i < (uint)span.Length; i++)
                {
                    if (span[i] > value)
                    {
                        value = span[i];
                    }
                }

                return value;
            }

            using IEnumerator<T> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            // As described in a comment on Min(this IEnumerable<T>) NaN is ordered
            // less than all other values. We need to do explicit checks to ensure this, but
            // once we've found a value that is not NaN we need no longer worry about it,
            // so first loop until such a value is found (or not, as the case may be).
            value = e.Current;
            while (T.IsNaN(value))
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }

            while (e.MoveNext())
            {
                T x = e.Current;
                if (x > value)
                {
                    value = x;
                }
            }

            return value;
        }

        private static T? MaxFloat<T>(this IEnumerable<T?> source) where T : struct, IFloatingPointIeee754<T>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            T? value = null;
            using IEnumerator<T?> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }
            while (!value.HasValue);

            T valueVal = value.GetValueOrDefault();
            while (T.IsNaN(valueVal))
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                T? cur = e.Current;
                if (cur.HasValue)
                {
                    valueVal = (value = cur).GetValueOrDefault();
                }
            }

            while (e.MoveNext())
            {
                T? cur = e.Current;
                T x = cur.GetValueOrDefault();

                // Do not replace & with &&. The branch prediction cost outweighs the extra operation
                // unless nulls either never happen or always happen.
                if (cur.HasValue & x > valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static decimal Max(this IEnumerable<decimal> source)
        {
            decimal value;

            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source.TryGetSpan(out ReadOnlySpan<decimal> span))
            {
                if (span.IsEmpty)
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = span[0];
                for (int i = 1; (uint)i < (uint)span.Length; i++)
                {
                    if (span[i] > value)
                    {
                        value = span[i];
                    }
                }

                return value;
            }

            using IEnumerator<decimal> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = e.Current;
            while (e.MoveNext())
            {
                decimal x = e.Current;
                if (x > value)
                {
                    value = x;
                }
            }

            return value;
        }


        public static decimal? Max(this IEnumerable<decimal?> source)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            decimal? value = null;
            using IEnumerator<decimal?> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = e.Current;
            }
            while (!value.HasValue);

            decimal valueVal = value.GetValueOrDefault();
            while (e.MoveNext())
            {
                decimal? cur = e.Current;
                decimal x = cur.GetValueOrDefault();
                if (cur.HasValue && x > valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static TSource? Max<TSource>(this IEnumerable<TSource> source) => Max(source, comparer: null);

        /// <summary>Returns the maximum value in a generic sequence.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">A sequence of values to determine the maximum value of.</param>
        /// <param name="comparer">The <see cref="IComparer{T}" /> to compare values.</param>
        /// <returns>The maximum value in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No object in <paramref name="source" /> implements the <see cref="System.IComparable" /> or <see cref="System.IComparable{T}" /> interface.</exception>
        /// <remarks>
        /// <para>If type <typeparamref name="TSource" /> implements <see cref="System.IComparable{T}" />, the <see cref="Max{T}(IEnumerable{T})" /> method uses that implementation to compare values. Otherwise, if type <typeparamref name="TSource" /> implements <see cref="System.IComparable" />, that implementation is used to compare values.</para>
        /// <para>If <typeparamref name="TSource" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// <para>In Visual Basic query expression syntax, an `Aggregate Into Max()` clause translates to an invocation of <see cref="O:Enumerable.Max" />.</para>
        /// </remarks>
        public static TSource? Max<TSource>(this IEnumerable<TSource> source, IComparer<TSource>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source.TryGetSpan(out ReadOnlySpan<TSource> span))
            {
                return span.Max(comparer);
            }

            // For the non-span integer sequences, use a direct comparison rather than paying the
            // per-element cost of Comparer<TSource>.Default.
            if (comparer is null || comparer == Comparer<TSource>.Default)
            {
                if (typeof(TSource) == typeof(byte)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<byte>)source);
                if (typeof(TSource) == typeof(sbyte)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<sbyte>)source);
                if (typeof(TSource) == typeof(ushort)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<ushort>)source);
                if (typeof(TSource) == typeof(short)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<short>)source);
                if (typeof(TSource) == typeof(char)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<char>)source);
                if (typeof(TSource) == typeof(uint)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<uint>)source);
                if (typeof(TSource) == typeof(int)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<int>)source);
                if (typeof(TSource) == typeof(ulong)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<ulong>)source);
                if (typeof(TSource) == typeof(long)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<long>)source);
                if (typeof(TSource) == typeof(nuint)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<nuint>)source);
                if (typeof(TSource) == typeof(nint)) return (TSource)(object)MaxIntegerEnumerator((IEnumerable<nint>)source);
            }

            comparer ??= Comparer<TSource>.Default;

            TSource? value = default;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (value is null)
            {
                do
                {
                    if (!e.MoveNext())
                    {
                        return value;
                    }

                    value = e.Current;
                }
                while (value is null);

                while (e.MoveNext())
                {
                    TSource next = e.Current;
                    if (next is not null && comparer.Compare(next, value) > 0)
                    {
                        value = next;
                    }
                }
            }
            else
            {
                if (!e.MoveNext())
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = e.Current;
                if (comparer == Comparer<TSource>.Default)
                {
                    while (e.MoveNext())
                    {
                        TSource next = e.Current;
                        if (Comparer<TSource>.Default.Compare(next, value) > 0)
                        {
                            value = next;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource next = e.Current;
                        if (comparer.Compare(next, value) > 0)
                        {
                            value = next;
                        }
                    }
                }
            }

            return value;
        }

        /// <summary>Returns the maximum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the maximum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>The value with the maximum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="System.IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => MaxBy(source, keySelector, null);

        /// <summary>Returns the maximum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the maximum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="comparer">The <see cref="IComparer{TKey}" /> to compare keys.</param>
        /// <returns>The value with the maximum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (keySelector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.keySelector);
            }

            comparer ??= Comparer<TKey>.Default;

            using IEnumerator<TSource> e = source.GetEnumerator();

            if (!e.MoveNext())
            {
                if (default(TSource) is null)
                {
                    return default;
                }
                else
                {
                    ThrowHelper.ThrowNoElementsException();
                }
            }

            TSource value = e.Current;
            TKey key = keySelector(value);

            if (default(TKey) is null)
            {
                if (key is null)
                {
                    TSource firstValue = value;

                    do
                    {
                        if (!e.MoveNext())
                        {
                            // All keys are null, surface the first element.
                            return firstValue;
                        }

                        value = e.Current;
                        key = keySelector(value);
                    }
                    while (key is null);
                }

                while (e.MoveNext())
                {
                    TSource nextValue = e.Current;
                    TKey nextKey = keySelector(nextValue);
                    if (nextKey is not null && comparer.Compare(nextKey, key) > 0)
                    {
                        key = nextKey;
                        value = nextValue;
                    }
                }
            }
            else
            {
                if (comparer == Comparer<TKey>.Default)
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (Comparer<TKey>.Default.Compare(nextKey, key) > 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (comparer.Compare(nextKey, key) > 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
            }

            return value;
        }

        public static int Max<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) => MaxInteger(source, selector);

        public static int? Max<TSource>(this IEnumerable<TSource> source, Func<TSource, int?> selector) => MaxInteger(source, selector);

        public static long Max<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector) => MaxInteger(source, selector);

        public static long? Max<TSource>(this IEnumerable<TSource> source, Func<TSource, long?> selector) => MaxInteger(source, selector);

        private static TResult MaxInteger<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) where TResult : struct, IBinaryInteger<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult value;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = selector(e.Current);
            while (e.MoveNext())
            {
                TResult x = selector(e.Current);
                if (x > value)
                {
                    value = x;
                }
            }

            return value;
        }

        private static TResult? MaxInteger<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult?> selector) where TResult : struct, IBinaryInteger<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult? value = null;
            using IEnumerator<TSource> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }
            while (!value.HasValue);

            TResult valueVal = value.GetValueOrDefault();
            if (valueVal >= TResult.Zero)
            {
                // We can fast-path this case where we know HasValue will
                // never affect the outcome, without constantly checking
                // if we're in such a state. Similar fast-paths could
                // be done for other cases, but as all-positive
                // or mostly-positive integer values are quite common in real-world
                // uses, it's only been done in this direction for int? and long?.
                while (e.MoveNext())
                {
                    TResult? cur = selector(e.Current);
                    TResult x = cur.GetValueOrDefault();
                    if (x > valueVal)
                    {
                        valueVal = x;
                        value = cur;
                    }
                }
            }
            else
            {
                while (e.MoveNext())
                {
                    TResult? cur = selector(e.Current);
                    TResult x = cur.GetValueOrDefault();

                    // Do not replace & with &&. The branch prediction cost outweighs the extra operation
                    // unless nulls either never happen or always happen.
                    if (cur.HasValue & x > valueVal)
                    {
                        valueVal = x;
                        value = cur;
                    }
                }
            }

            return value;
        }

        public static float Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector) => MaxFloat(source, selector);

        public static float? Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float?> selector) => MaxFloat(source, selector);

        public static double Max<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) => MaxFloat(source, selector);

        public static double? Max<TSource>(this IEnumerable<TSource> source, Func<TSource, double?> selector) => MaxFloat(source, selector);

        private static TResult MaxFloat<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) where TResult : struct, IFloatingPointIeee754<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult value;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = selector(e.Current);
            while (TResult.IsNaN(value))
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }

            while (e.MoveNext())
            {
                TResult x = selector(e.Current);
                if (x > value)
                {
                    value = x;
                }
            }

            return value;
        }

        private static TResult? MaxFloat<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult?> selector) where TResult : struct, IFloatingPointIeee754<TResult>
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult? value = null;
            using IEnumerator<TSource> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }
            while (!value.HasValue);

            TResult valueVal = value.GetValueOrDefault();
            while (TResult.IsNaN(valueVal))
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                TResult? cur = selector(e.Current);
                if (cur.HasValue)
                {
                    valueVal = (value = cur).GetValueOrDefault();
                }
            }

            while (e.MoveNext())
            {
                TResult? cur = selector(e.Current);
                TResult x = cur.GetValueOrDefault();

                // Do not replace & with &&. The branch prediction cost outweighs the extra operation
                // unless nulls either never happen or always happen.
                if (cur.HasValue & x > valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static decimal Max<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            decimal value;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (!e.MoveNext())
            {
                ThrowHelper.ThrowNoElementsException();
            }

            value = selector(e.Current);
            while (e.MoveNext())
            {
                decimal x = selector(e.Current);
                if (x > value)
                {
                    value = x;
                }
            }

            return value;
        }

        public static decimal? Max<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal?> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            decimal? value = null;
            using IEnumerator<TSource> e = source.GetEnumerator();
            do
            {
                if (!e.MoveNext())
                {
                    return value;
                }

                value = selector(e.Current);
            }
            while (!value.HasValue);

            decimal valueVal = value.GetValueOrDefault();
            while (e.MoveNext())
            {
                decimal? cur = selector(e.Current);
                decimal x = cur.GetValueOrDefault();
                if (cur.HasValue && x > valueVal)
                {
                    valueVal = x;
                    value = cur;
                }
            }

            return value;
        }

        public static TResult? Max<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            TResult? value = default;
            using IEnumerator<TSource> e = source.GetEnumerator();
            if (value is null)
            {
                do
                {
                    if (!e.MoveNext())
                    {
                        return value;
                    }

                    value = selector(e.Current);
                }
                while (value is null);

                Comparer<TResult> comparer = Comparer<TResult>.Default;
                while (e.MoveNext())
                {
                    TResult x = selector(e.Current);
                    if (x is not null && comparer.Compare(x, value) > 0)
                    {
                        value = x;
                    }
                }
            }
            else
            {
                if (!e.MoveNext())
                {
                    ThrowHelper.ThrowNoElementsException();
                }

                value = selector(e.Current);
                while (e.MoveNext())
                {
                    TResult x = selector(e.Current);
                    if (Comparer<TResult>.Default.Compare(x, value) > 0)
                    {
                        value = x;
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// Sorts the elements of a sequence in ascending order.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">A sequence of values to order.</param>
        /// <returns>An <see cref="IOrderedEnumerable{TElement}"/> whose elements are sorted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method is implemented by using deferred execution. The immediate return value is an object
        /// that stores all the information that is required to perform the action.
        /// The query represented by this method is not executed until the object is enumerated by calling
        /// its <see cref="IEnumerable{T}.GetEnumerator"/> method.
        ///
        /// This method compares elements by using the default comparer <see cref="Comparer{T}.Default"/>.
        /// </remarks>
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source) =>
            Order(source, comparer: null);

        /// <summary>
        /// Sorts the elements of a sequence in ascending order.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">A sequence of values to order.</param>
        /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
        /// <returns>An <see cref="IOrderedEnumerable{TElement}"/> whose elements are sorted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method is implemented by using deferred execution. The immediate return value is an object
        /// that stores all the information that is required to perform the action.
        /// The query represented by this method is not executed until the object is enumerated by calling
        /// its <see cref="IEnumerable{T}.GetEnumerator"/> method.
        ///
        /// If comparer is <see langword="null"/>, the default comparer <see cref="Comparer{T}.Default"/> is used to compare elements.
        /// </remarks>
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source, IComparer<T>? comparer) =>
            TypeIsImplicitlyStable<T>() && (comparer is null || comparer == Comparer<T>.Default) ?
                new ImplicitlyStableOrderedIterator<T>(source, descending: false) :
                OrderBy(source, EnumerableSorter<T>.IdentityFunc, comparer);

        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
            => new OrderedIterator<TSource, TKey>(source, keySelector, null, false, null);

        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
            => new OrderedIterator<TSource, TKey>(source, keySelector, comparer, false, null);

        /// <summary>
        /// Sorts the elements of a sequence in descending order.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">A sequence of values to order.</param>
        /// <returns>An <see cref="IOrderedEnumerable{TElement}"/> whose elements are sorted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method is implemented by using deferred execution. The immediate return value is an object
        /// that stores all the information that is required to perform the action.
        /// The query represented by this method is not executed until the object is enumerated by calling
        /// its <see cref="IEnumerable{T}.GetEnumerator"/> method.
        ///
        /// This method compares elements by using the default comparer <see cref="Comparer{T}.Default"/>.
        /// </remarks>
        public static IOrderedEnumerable<T> OrderDescending<T>(this IEnumerable<T> source) =>
            OrderDescending(source, comparer: null);

        /// <summary>
        /// Sorts the elements of a sequence in descending order.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">A sequence of values to order.</param>
        /// <param name="comparer">An <see cref="IComparer{T}"/> to compare keys.</param>
        /// <returns>An <see cref="IOrderedEnumerable{TElement}"/> whose elements are sorted.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method is implemented by using deferred execution. The immediate return value is an object
        /// that stores all the information that is required to perform the action.
        /// The query represented by this method is not executed until the object is enumerated by calling
        /// its <see cref="IEnumerable{T}.GetEnumerator"/> method.
        ///
        /// If comparer is <see langword="null"/>, the default comparer <see cref="Comparer{T}.Default"/> is used to compare elements.
        /// </remarks>
        public static IOrderedEnumerable<T> OrderDescending<T>(this IEnumerable<T> source, IComparer<T>? comparer) =>
            TypeIsImplicitlyStable<T>() && (comparer is null || comparer == Comparer<T>.Default) ?
                new ImplicitlyStableOrderedIterator<T>(source, descending: true) :
                OrderByDescending(source, EnumerableSorter<T>.IdentityFunc, comparer);

        public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            new OrderedIterator<TSource, TKey>(source, keySelector, null, true, null);

        public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer) =>
            new OrderedIterator<TSource, TKey>(source, keySelector, comparer, true, null);

        public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return source.CreateOrderedEnumerable(keySelector, null, false);
        }

        public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return source.CreateOrderedEnumerable(keySelector, comparer, false);
        }

        public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return source.CreateOrderedEnumerable(keySelector, null, true);
        }

        public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return source.CreateOrderedEnumerable(keySelector, comparer, true);
        }

        /// <summary>Gets whether the results of an unstable sort will be observably the same as a stable sort.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TypeIsImplicitlyStable<T>()
        {
            Type t = typeof(T);
            if (typeof(T).IsEnum)
            {
                t = typeof(T).GetEnumUnderlyingType();
            }

            // Check for integral primitive types that compare equally iff they have the same bit pattern.
            // bool is included because, even though technically it can have 256 different values, anything
            // other than 0/1 is only producible using unsafe code. It's tempting to include a type like string
            // here, as it's so commonly used with ordering, but two different string objects can compare equally,
            // and their reference identity can be observable in a stable vs unstable sort.
            return
                t == typeof(sbyte) || t == typeof(byte) || t == typeof(bool) ||
                t == typeof(short) || t == typeof(ushort) || t == typeof(char) ||
                t == typeof(int) || t == typeof(uint) ||
                t == typeof(long) || t == typeof(ulong) ||
                t == typeof(nint) || t == typeof(nuint);
        }

        public static TSource First<TSource>(this IEnumerable<TSource> source)
        {
            TSource? first = source.TryGetFirst(out bool found);
            if (!found)
            {
                ThrowHelper.ThrowNoElementsException();
            }

            return first!;
        }

        public static TSource First<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            TSource? first = source.TryGetFirst(predicate, out bool found);
            if (!found)
            {
                ThrowHelper.ThrowNoMatchException();
            }

            return first!;
        }

        public static TSource? FirstOrDefault<TSource>(this IEnumerable<TSource> source) =>
            source.TryGetFirst(out _);

        /// <summary>Returns the first element of a sequence, or a default value if the sequence contains no elements.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}" /> to return the first element of.</param>
        /// <param name="defaultValue">The default value to return if the sequence is empty.</param>
        /// <returns><paramref name="defaultValue" /> if <paramref name="source" /> is empty; otherwise, the first element in <paramref name="source" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, TSource defaultValue)
        {
            TSource? first = source.TryGetFirst(out bool found);
            return found ? first! : defaultValue;
        }

        public static TSource? FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) =>
            source.TryGetFirst(predicate, out _);

        /// <summary>Returns the first element of the sequence that satisfies a condition or a default value if no such element is found.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{T}" /> to return an element from.</param>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="defaultValue">The default value to return if the sequence is empty.</param>
        /// <returns><paramref name="defaultValue" /> if <paramref name="source" /> is empty or if no element passes the test specified by <paramref name="predicate" />; otherwise, the first element in <paramref name="source" /> that passes the test specified by <paramref name="predicate" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> or <paramref name="predicate" /> is <see langword="null" />.</exception>
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue)
        {
            TSource? first = source.TryGetFirst(predicate, out bool found);
            return found ? first! : defaultValue;
        }


        private static TSource? TryGetFirst<TSource>(this IEnumerable<TSource> source, out bool found)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return
                !IsSizeOptimized && source is Iterator<TSource> iterator ? iterator.TryGetFirst(out found) :
                TryGetFirstNonIterator(source, out found);
        }

        private static TSource? TryGetFirstNonIterator<TSource>(IEnumerable<TSource> source, out bool found)
        {
            if (source is IList<TSource> list)
            {
                if (list.Count > 0)
                {
                    found = true;
                    return list[0];
                }
            }
            else
            {
                using IEnumerator<TSource> e = source.GetEnumerator();
                if (e.MoveNext())
                {
                    found = true;
                    return e.Current;
                }
            }

            found = false;
            return default;
        }

        private static TSource? TryGetFirst<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, out bool found)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (predicate is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);
            }

            if (source.TryGetSpan(out ReadOnlySpan<TSource> span))
            {
                foreach (TSource element in span)
                {
                    if (predicate(element))
                    {
                        found = true;
                        return element;
                    }
                }
            }
            else
            {
                foreach (TSource element in source)
                {
                    if (predicate(element))
                    {
                        found = true;
                        return element;
                    }
                }
            }

            found = false;
            return default;
        }

        public static int Count<TSource>(this IEnumerable<TSource> source)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source is ICollection<TSource> collectionoft)
            {
                return collectionoft.Count;
            }

            if (source is Iterator<TSource> iterator)
            {
                return iterator.GetCount(onlyIfCheap: false);
            }

            if (source is ICollection collection)
            {
                return collection.Count;
            }

            int count = 0;
            using IEnumerator<TSource> e = source.GetEnumerator();
            checked
            {
                while (e.MoveNext())
                {
                    count++;
                }
            }

            return count;
        }

        public static int Count<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (predicate is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);
            }

            int count = 0;
            if (source.TryGetSpan(out ReadOnlySpan<TSource> span))
            {
                foreach (TSource element in span)
                {
                    if (predicate(element))
                    {
                        count++;
                    }
                }
            }
            else
            {
                foreach (TSource element in source)
                {
                    if (predicate(element))
                    {
                        checked { count++; }
                    }
                }
            }

            return count;
        }

        /// <summary>
        ///   Attempts to determine the number of elements in a sequence without forcing an enumeration.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <param name="source">A sequence that contains elements to be counted.</param>
        /// <param name="count">
        ///     When this method returns, contains the count of <paramref name="source" /> if successful,
        ///     or zero if the method failed to determine the count.</param>
        /// <returns>
        ///   <see langword="true" /> if the count of <paramref name="source"/> can be determined without enumeration;
        ///   otherwise, <see langword="false" />.
        /// </returns>
        /// <remarks>
        ///   The method performs a series of type tests, identifying common subtypes whose
        ///   count can be determined without enumerating; this includes <see cref="ICollection{T}"/>,
        ///   <see cref="ICollection"/> as well as internal types used in the LINQ implementation.
        ///
        ///   The method is typically a constant-time operation, but ultimately this depends on the complexity
        ///   characteristics of the underlying collection implementation.
        /// </remarks>
        public static bool TryGetNonEnumeratedCount<TSource>(this IEnumerable<TSource> source, out int count)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (source is ICollection<TSource> collectionoft)
            {
                count = collectionoft.Count;
                return true;
            }

            if (source is Iterator<TSource> iterator)
            {
                int c = iterator.GetCount(onlyIfCheap: true);
                if (c >= 0)
                {
                    count = c;
                    return true;
                }
            }

            if (source is ICollection collection)
            {
                count = collection.Count;
                return true;
            }

            count = 0;
            return false;
        }

        public static long LongCount<TSource>(this IEnumerable<TSource> source)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            // TryGetSpan isn't used here because if it's expected that there are more than int.MaxValue elements,
            // the source can't possibly be something from which we can extract a span.

            long count = 0;
            using IEnumerator<TSource> e = source.GetEnumerator();
            while (e.MoveNext())
            {
                checked { count++; }
            }

            return count;
        }

        public static long LongCount<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (predicate is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);
            }

            // TryGetSpan isn't used here because if it's expected that there are more than int.MaxValue elements,
            // the source can't possibly be something from which we can extract a span.

            long count = 0;
            foreach (TSource element in source)
            {
                if (predicate(element))
                {
                    checked { count++; }
                }
            }

            return count;
        }

        public static bool Contains<TSource>(this IEnumerable<TSource> source, TSource value)
        {
            if (source is ICollection<TSource> collection)
            {
                return collection.Contains(value);
            }

            if (!IsSizeOptimized && source is Iterator<TSource> iterator)
            {
                return iterator.Contains(value);
            }

            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            return ContainsIterate(source, value, null);
        }

        private static bool ContainsIterate<TSource>(IEnumerable<TSource> source, TSource value, IEqualityComparer<TSource>? comparer)
        {
            if (comparer is null)
            {
                if (typeof(TSource).IsValueType)
                {
                    foreach (TSource element in source)
                    {
                        if (EqualityComparer<TSource>.Default.Equals(element, value))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                comparer = EqualityComparer<TSource>.Default;
            }

            foreach (TSource element in source)
            {
                if (comparer.Equals(element, value))
                {
                    return true;
                }
            }

            return false;
        }

        private static TSource? TryGetElementAtNonIterator<TSource>(IEnumerable<TSource> source, int index, out bool found)
        {
            Debug.Assert(source is not null);

            if (index >= 0)
            {
                using IEnumerator<TSource> e = source.GetEnumerator();
                while (e.MoveNext())
                {
                    if (index == 0)
                    {
                        found = true;
                        return e.Current;
                    }

                    index--;
                }
            }

            found = false;
            return default;
        }

        private abstract class Iterator<TSource> : IEnumerable<TSource>, IEnumerator<TSource>
        {
            private readonly int _threadId = Environment.CurrentManagedThreadId;

            private protected int _state;
            private protected TSource _current = default!;

            /// <summary>
            /// The item currently yielded by this iterator.
            /// </summary>
            public TSource Current => _current;

            /// <summary>
            /// Makes a shallow copy of this iterator.
            /// </summary>
            /// <remarks>
            /// This method is called if <see cref="GetEnumerator"/> is called more than once.
            /// </remarks>
            private protected abstract Iterator<TSource> Clone();

            /// <summary>
            /// Puts this iterator in a state whereby no further enumeration will take place.
            /// </summary>
            /// <remarks>
            /// Derived classes should override this method if necessary to clean up any
            /// mutable state they hold onto (for example, calling Dispose on other enumerators).
            /// </remarks>
            public virtual void Dispose()
            {
                _current = default!;
                _state = -1;
            }

            /// <summary>
            /// Gets the enumerator used to yield values from this iterator.
            /// </summary>
            /// <remarks>
            /// If <see cref="GetEnumerator"/> is called for the first time on the same thread
            /// that created this iterator, the result will be this iterator. Otherwise, the result
            /// will be a shallow copy of this iterator.
            /// </remarks>
            public Iterator<TSource> GetEnumerator()
            {
                Iterator<TSource> enumerator = _state == 0 && _threadId == Environment.CurrentManagedThreadId ? this : Clone();
                enumerator._state = 1;
                return enumerator;
            }

            /// <summary>
            /// Retrieves the next item in this iterator and yields it via <see cref="Current"/>.
            /// </summary>
            /// <returns><c>true</c> if there was another value to be yielded; otherwise, <c>false</c>.</returns>
            public abstract bool MoveNext();

            /// <summary>
            /// Returns an enumerable that maps each item in this iterator based on a selector.
            /// </summary>
            /// <typeparam name="TResult">The type of the mapped items.</typeparam>
            /// <param name="selector">The selector used to map each item.</param>
            public virtual IEnumerable<TResult> Select<TResult>(Func<TSource, TResult> selector) =>
                !IsSizeOptimized
                ? new IteratorSelectIterator<TSource, TResult>(this, selector)
                : new IEnumerableSelectIterator<TSource, TResult>(this, selector);


            /// <summary>
            /// Returns an enumerable that filters each item in this iterator based on a predicate.
            /// </summary>
            /// <param name="predicate">The predicate used to filter each item.</param>
            public virtual IEnumerable<TSource> Where(Func<TSource, bool> predicate) =>
                new IEnumerableWhereIterator<TSource>(this, predicate);

            object? IEnumerator.Current => Current;

            IEnumerator<TSource> IEnumerable<TSource>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            void IEnumerator.Reset() => ThrowHelper.ThrowNotSupportedException();

            /// <summary>
            /// Produce an array of the sequence through an optimized path.
            /// </summary>
            /// <returns>The array.</returns>
            public abstract TSource[] ToArray();

            /// <summary>
            /// Produce a <see cref="List{TSource}"/> of the sequence through an optimized path.
            /// </summary>
            /// <returns>The <see cref="List{TSource}"/>.</returns>
            public abstract List<TSource> ToList();

            /// <summary>
            /// Returns the count of elements in the sequence.
            /// </summary>
            /// <param name="onlyIfCheap">If true then the count should only be calculated if doing
            /// so is quick (sure or likely to be constant time), otherwise -1 should be returned.</param>
            /// <returns>The number of elements.</returns>
            public abstract int GetCount(bool onlyIfCheap);

            /// <summary>
            /// Creates a new iterator that skips the specified number of elements from this sequence.
            /// </summary>
            /// <param name="count">The number of elements to skip.</param>
            /// <returns>An <see cref="Iterator{TSource}"/> with the first <paramref name="count"/> items removed, or null if known empty.</returns>
            public virtual Iterator<TSource>? Skip(int count) => new IEnumerableSkipTakeIterator<TSource>(this, count, -1);

            /// <summary>
            /// Creates a new iterator that takes the specified number of elements from this sequence.
            /// </summary>
            /// <param name="count">The number of elements to take.</param>
            /// <returns>An <see cref="Iterator{TSource}"/> with only the first <paramref name="count"/> items, or null if known empty.</returns>
            public virtual Iterator<TSource>? Take(int count) => new IEnumerableSkipTakeIterator<TSource>(this, 0, count - 1);

            /// <summary>
            /// Gets the item associated with a 0-based index in this sequence.
            /// </summary>
            /// <param name="index">The 0-based index to access.</param>
            /// <param name="found"><c>true</c> if the sequence contains an element at that index, <c>false</c> otherwise.</param>
            /// <returns>The element if <paramref name="found"/> is <c>true</c>, otherwise, the default value of <typeparamref name="TSource"/>.</returns>
            public virtual TSource? TryGetElementAt(int index, out bool found) =>
                index == 0 ? TryGetFirst(out found) :
                TryGetElementAtNonIterator(this, index, out found);

            /// <summary>
            /// Gets the first item in this sequence.
            /// </summary>
            /// <param name="found"><c>true</c> if the sequence contains an element, <c>false</c> otherwise.</param>
            /// <returns>The element if <paramref name="found"/> is <c>true</c>, otherwise, the default value of <typeparamref name="TSource"/>.</returns>
            public virtual TSource? TryGetFirst(out bool found) => TryGetFirstNonIterator(this, out found);

            /// <summary>
            /// Gets the last item in this sequence.
            /// </summary>
            /// <param name="found"><c>true</c> if the sequence contains an element, <c>false</c> otherwise.</param>
            /// <returns>The element if <paramref name="found"/> is <c>true</c>, otherwise, the default value of <typeparamref name="TSource"/>.</returns>
            public virtual TSource? TryGetLast(out bool found) => TryGetLastNonIterator(this, out found);

            public virtual bool Contains(TSource value) => ContainsIterate(this, value, null);
        }

        private abstract class OrderedIterator<TElement> : Iterator<TElement>, IOrderedEnumerable<TElement>
        {
            internal readonly IEnumerable<TElement> _source;

            protected OrderedIterator(IEnumerable<TElement> source) => _source = source;

            private protected int[] SortedMap(TElement[] buffer) => GetEnumerableSorter().Sort(buffer, buffer.Length);

            internal int[] SortedMap(TElement[] buffer, int minIdx, int maxIdx) =>
                GetEnumerableSorter().Sort(buffer, buffer.Length, minIdx, maxIdx);

            internal abstract EnumerableSorter<TElement> GetEnumerableSorter(EnumerableSorter<TElement>? next = null);

            internal abstract CachingComparer<TElement> GetComparer(CachingComparer<TElement>? childComparer = null);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            IOrderedEnumerable<TElement> IOrderedEnumerable<TElement>.CreateOrderedEnumerable<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey>? comparer, bool descending) =>
                new OrderedIterator<TElement, TKey>(_source, keySelector, comparer, @descending, this);

            public TElement? TryGetLast(Func<TElement, bool> predicate, out bool found)
            {
                CachingComparer<TElement> comparer = GetComparer();
                using IEnumerator<TElement> e = _source.GetEnumerator();
                TElement value;
                do
                {
                    if (!e.MoveNext())
                    {
                        found = false;
                        return default;
                    }

                    value = e.Current;
                }
                while (!predicate(value));

                comparer.SetElement(value);
                while (e.MoveNext())
                {
                    TElement x = e.Current;
                    if (predicate(x) && comparer.Compare(x, false) >= 0)
                    {
                        value = x;
                    }
                }

                found = true;
                return value;
            }

            public override TElement[] ToArray()
            {
                TElement[] buffer = _source.ToArray();
                if (buffer.Length <= 1)
                {
                    return buffer;
                }

                TElement[] array = new TElement[buffer.Length];
                Fill(buffer, array);
                return array;
            }

            public override List<TElement> ToList()
            {
                TElement[] buffer = _source.ToArray();

                List<TElement> list = new(buffer.Length);
                if (buffer.Length >= 2)
                {
                    Fill(buffer, SetCountAndGetSpan(list, buffer.Length));
                }
                else if (buffer.Length == 1)
                {
                    list.Add(buffer[0]);
                }

                return list;
            }

            private void Fill(TElement[] buffer, Span<TElement> destination)
            {
                int[] map = SortedMap(buffer);
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = buffer[map[i]];
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                if (_source is Iterator<TElement> iterator)
                {
                    return iterator.GetCount(onlyIfCheap);
                }

                return !onlyIfCheap || _source is ICollection<TElement> || _source is ICollection ? _source.Count() : -1;
            }

            internal TElement[] ToArray(int minIdx, int maxIdx)
            {
                TElement[] buffer = _source.ToArray();
                if (buffer.Length <= minIdx)
                {
                    return [];
                }

                if (buffer.Length <= maxIdx)
                {
                    maxIdx = buffer.Length - 1;
                }

                if (minIdx == maxIdx)
                {
                    return [GetEnumerableSorter().ElementAt(buffer, buffer.Length, minIdx)];
                }

                TElement[] array = new TElement[maxIdx - minIdx + 1];

                Fill(minIdx, maxIdx, buffer, array);

                return array;
            }

            internal List<TElement> ToList(int minIdx, int maxIdx)
            {
                TElement[] buffer = _source.ToArray();
                if (buffer.Length <= minIdx)
                {
                    return [];
                }

                if (buffer.Length <= maxIdx)
                {
                    maxIdx = buffer.Length - 1;
                }

                if (minIdx == maxIdx)
                {
                    return new List<TElement>(1) { GetEnumerableSorter().ElementAt(buffer, buffer.Length, minIdx) };
                }

                List<TElement> list = [];
                Fill(minIdx, maxIdx, buffer, SetCountAndGetSpan(list, maxIdx - minIdx + 1));
                return list;
            }

            private void Fill(int minIdx, int maxIdx, TElement[] buffer, Span<TElement> destination)
            {
                int[] map = SortedMap(buffer, minIdx, maxIdx);
                int idx = 0;
                while (minIdx <= maxIdx)
                {
                    destination[idx] = buffer[map[minIdx]];
                    ++idx;
                    ++minIdx;
                }
            }

            internal int GetCount(int minIdx, int maxIdx, bool onlyIfCheap)
            {
                int count = GetCount(onlyIfCheap);
                if (count <= 0)
                {
                    return count;
                }

                if (count <= minIdx)
                {
                    return 0;
                }

                return (count <= maxIdx ? count : maxIdx + 1) - minIdx;
            }

            public override Iterator<TElement> Skip(int count) => new SkipTakeOrderedIterator<TElement>(this, count, int.MaxValue);

            public override Iterator<TElement> Take(int count) => new SkipTakeOrderedIterator<TElement>(this, 0, count - 1);

            public override TElement? TryGetElementAt(int index, out bool found)
            {
                if (index == 0)
                {
                    return TryGetFirst(out found);
                }

                if (index > 0)
                {
                    TElement[] buffer = _source.ToArray();
                    if (index < buffer.Length)
                    {
                        found = true;
                        return GetEnumerableSorter().ElementAt(buffer, buffer.Length, index);
                    }
                }

                found = false;
                return default;
            }

            public override TElement? TryGetFirst(out bool found)
            {
                CachingComparer<TElement> comparer = GetComparer();
                using IEnumerator<TElement> e = _source.GetEnumerator();
                if (!e.MoveNext())
                {
                    found = false;
                    return default;
                }

                TElement value = e.Current;
                comparer.SetElement(value);
                while (e.MoveNext())
                {
                    TElement x = e.Current;
                    if (comparer.Compare(x, true) < 0)
                    {
                        value = x;
                    }
                }

                found = true;
                return value;
            }

            public override TElement? TryGetLast(out bool found)
            {
                using IEnumerator<TElement> e = _source.GetEnumerator();
                if (!e.MoveNext())
                {
                    found = false;
                    return default;
                }

                CachingComparer<TElement> comparer = GetComparer();
                TElement value = e.Current;
                comparer.SetElement(value);
                while (e.MoveNext())
                {
                    TElement current = e.Current;
                    if (comparer.Compare(current, false) >= 0)
                    {
                        value = current;
                    }
                }

                found = true;
                return value;
            }

            public TElement? TryGetLast(int minIdx, int maxIdx, out bool found)
            {
                TElement[] buffer = _source.ToArray();
                if (minIdx < buffer.Length)
                {
                    found = true;
                    return (maxIdx < buffer.Length - 1) ?
                        GetEnumerableSorter().ElementAt(buffer, buffer.Length, maxIdx) :
                        Last(buffer);
                }

                found = false;
                return default;
            }

            public override bool Contains(TElement value) =>
                _source.Contains(value);

            private TElement Last(TElement[] items)
            {
                CachingComparer<TElement> comparer = GetComparer();

                TElement value = items[0];
                comparer.SetElement(value);

                for (int i = 1; i < items.Length; ++i)
                {
                    TElement x = items[i];
                    if (comparer.Compare(x, cacheLower: false) >= 0)
                    {
                        value = x;
                    }
                }

                return value;
            }
        }

        public static IEnumerable<int> Range(int start, int count)
        {
            long max = ((long)start) + count - 1;
            if (count < 0 || max > int.MaxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count);
            }

            if (count == 0)
            {
                return [];
            }

            return new RangeIterator<int>(start, start + count);
        }

        private sealed partial class OrderedIterator<TElement, TKey> : OrderedIterator<TElement>
        {
            private readonly OrderedIterator<TElement>? _parent;
            private readonly Func<TElement, TKey> _keySelector;
            private readonly IComparer<TKey> _comparer;
            private readonly bool _descending;
            private TElement[]? _buffer;
            private int[]? _map;

            internal OrderedIterator(IEnumerable<TElement> source, Func<TElement, TKey> keySelector, IComparer<TKey>? comparer, bool descending, OrderedIterator<TElement>? parent) :
                base(source)
            {
                if (source is null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
                }
                if (keySelector is null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.keySelector);
                }

                _parent = parent;
                _keySelector = keySelector;
                _comparer = comparer ?? Comparer<TKey>.Default;
                _descending = descending;
            }

            private protected override Iterator<TElement> Clone() => new OrderedIterator<TElement, TKey>(_source, _keySelector, _comparer, _descending, _parent);

            internal override EnumerableSorter<TElement> GetEnumerableSorter(EnumerableSorter<TElement>? next)
            {
                // Special case the common use of string with default comparer. Comparer<string>.Default checks the
                // thread's Culture on each call which is an overhead which is not required, because we are about to
                // do a sort which remains on the current thread (and EnumerableSorter is not used afterwards).
                IComparer<TKey> comparer = _comparer;
                if (typeof(TKey) == typeof(string) && comparer == Comparer<string>.Default)
                {
                    comparer = (IComparer<TKey>)StringComparer.CurrentCulture;
                }

                EnumerableSorter<TElement> sorter = new EnumerableSorter<TElement, TKey>(_keySelector, comparer, _descending, next);
                if (_parent is not null)
                {
                    sorter = _parent.GetEnumerableSorter(sorter);
                }

                return sorter;
            }

            internal override CachingComparer<TElement> GetComparer(CachingComparer<TElement>? childComparer)
            {
                CachingComparer<TElement> cmp = childComparer is null
                    ? new CachingComparer<TElement, TKey>(_keySelector, _comparer, _descending)
                    : new CachingComparerWithChild<TElement, TKey>(_keySelector, _comparer, _descending, childComparer);
                return _parent is not null ? _parent.GetComparer(cmp) : cmp;
            }

            public override bool MoveNext()
            {
                int state = _state;

            Initialized:
                if (state > 1)
                {

                    int[] map = _map;
                    int i = state - 2;
                    if ((uint)i < (uint)map.Length)
                    {
                        _current = _buffer[map[i]];
                        _state++;
                        return true;
                    }
                }
                else if (state == 1)
                {
                    TElement[] buffer = _source.ToArray();
                    if (buffer.Length != 0)
                    {
                        _map = SortedMap(buffer);
                        _buffer = buffer;
                        _state = state = 2;
                        goto Initialized;
                    }
                }

                Dispose();
                return false;
            }

            public override void Dispose()
            {
                _buffer = null;
                _map = null;
                base.Dispose();
            }

            // For complicated cases, rely on the base implementation that's more comprehensive.
            // For the simple case of OrderBy(...).First() or OrderByDescending(...).First() (i.e. where
            // there's just a single comparer we need to factor in), we can just do the iteration directly.

            public override TElement? TryGetFirst(out bool found)
            {
                if (_parent is not null)
                {
                    return base.TryGetFirst(out found);
                }

                using IEnumerator<TElement> e = _source.GetEnumerator();

                if (e.MoveNext())
                {
                    IComparer<TKey> comparer = _comparer;
                    Func<TElement, TKey> keySelector = _keySelector;

                    TElement resultValue = e.Current;
                    TKey resultKey = keySelector(resultValue);

                    if (_descending)
                    {
                        while (e.MoveNext())
                        {
                            TElement nextValue = e.Current;
                            TKey nextKey = keySelector(nextValue);
                            if (comparer.Compare(nextKey, resultKey) > 0)
                            {
                                resultKey = nextKey;
                                resultValue = nextValue;
                            }
                        }
                    }
                    else
                    {
                        while (e.MoveNext())
                        {
                            TElement nextValue = e.Current;
                            TKey nextKey = keySelector(nextValue);
                            if (comparer.Compare(nextKey, resultKey) < 0)
                            {
                                resultKey = nextKey;
                                resultValue = nextValue;
                            }
                        }
                    }

                    found = true;
                    return resultValue;
                }

                found = false;
                return default;
            }

            public override TElement? TryGetLast(out bool found)
            {
                if (_parent is not null)
                {
                    return base.TryGetLast(out found);
                }

                using IEnumerator<TElement> e = _source.GetEnumerator();

                if (e.MoveNext())
                {
                    IComparer<TKey> comparer = _comparer;
                    Func<TElement, TKey> keySelector = _keySelector;

                    TElement resultValue = e.Current;
                    TKey resultKey = keySelector(resultValue);

                    if (_descending)
                    {
                        while (e.MoveNext())
                        {
                            TElement nextValue = e.Current;
                            TKey nextKey = keySelector(nextValue);
                            if (comparer.Compare(nextKey, resultKey) <= 0)
                            {
                                resultKey = nextKey;
                                resultValue = nextValue;
                            }
                        }
                    }
                    else
                    {
                        while (e.MoveNext())
                        {
                            TElement nextValue = e.Current;
                            TKey nextKey = keySelector(nextValue);
                            if (comparer.Compare(nextKey, resultKey) >= 0)
                            {
                                resultKey = nextKey;
                                resultValue = nextValue;
                            }
                        }
                    }

                    found = true;
                    return resultValue;
                }

                found = false;
                return default;
            }
        }

        /// <summary>An ordered enumerable used by Order/OrderDescending for Ts that are bitwise indistinguishable for any considered equal.</summary>
        private sealed partial class ImplicitlyStableOrderedIterator<TElement> : OrderedIterator<TElement>
        {
            private readonly bool _descending;
            private TElement[]? _buffer;

            public ImplicitlyStableOrderedIterator(IEnumerable<TElement> source, bool descending) : base(source)
            {
                Debug.Assert(TypeIsImplicitlyStable<TElement>());

                if (source is null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
                }

                _descending = descending;
            }

            private protected override Iterator<TElement> Clone() => new ImplicitlyStableOrderedIterator<TElement>(_source, _descending);

            internal override CachingComparer<TElement> GetComparer(CachingComparer<TElement>? childComparer) =>
                childComparer is null ?
                    new CachingComparer<TElement, TElement>(EnumerableSorter<TElement>.IdentityFunc, Comparer<TElement>.Default, _descending) :
                    new CachingComparerWithChild<TElement, TElement>(EnumerableSorter<TElement>.IdentityFunc, Comparer<TElement>.Default, _descending, childComparer);

            internal override EnumerableSorter<TElement> GetEnumerableSorter(EnumerableSorter<TElement>? next) =>
                new EnumerableSorter<TElement, TElement>(EnumerableSorter<TElement>.IdentityFunc, Comparer<TElement>.Default, _descending, next);

            public override bool MoveNext()
            {
                int state = _state;
                TElement[]? buffer;

            Initialized:
                if (state > 1)
                {
                    buffer = _buffer;

                    int i = state - 2;
                    if ((uint)i < (uint)buffer.Length)
                    {
                        _current = buffer[i];
                        _state++;
                        return true;
                    }
                }
                else if (state == 1)
                {
                    buffer = _source.ToArray();
                    if (buffer.Length != 0)
                    {
                        Sort(buffer, _descending);
                        _buffer = buffer;
                        _state = state = 2;
                        goto Initialized;
                    }
                }

                Dispose();
                return false;
            }

            public override void Dispose()
            {
                _buffer = null;
                base.Dispose();
            }

            private static void Sort(Span<TElement> span, bool descending)
            {
                if (descending)
                {
                    span.Sort(static (a, b) => Comparer<TElement>.Default.Compare(b, a));
                }
                else
                {
                    span.Sort();
                }
            }

            public override TElement[] ToArray()
            {
                TElement[] array = _source.ToArray();
                Sort(array, _descending);
                return array;
            }

            public override List<TElement> ToList()
            {
                List<TElement> list = _source.ToList();
                Sort(CollectionsMarshal.AsSpan(list), _descending);
                return list;
            }

            public override TElement? TryGetFirst(out bool found) =>
                TryGetFirstOrLast(out found, first: !_descending);

            public override TElement? TryGetLast(out bool found) =>
                TryGetFirstOrLast(out found, first: _descending);

            private TElement? TryGetFirstOrLast(out bool found, bool first)
            {
                if (TryGetSpan(_source, out ReadOnlySpan<TElement> span))
                {
                    if (span.Length != 0)
                    {
                        found = true;
                        return first ?
                            Min(_source) :
                            Max(_source);
                    }
                }
                else
                {
                    using IEnumerator<TElement> e = _source.GetEnumerator();

                    if (e.MoveNext())
                    {
                        TElement resultValue = e.Current;

                        if (first)
                        {
                            while (e.MoveNext())
                            {
                                TElement nextValue = e.Current;
                                if (Comparer<TElement>.Default.Compare(nextValue, resultValue) < 0)
                                {
                                    resultValue = nextValue;
                                }
                            }
                        }
                        else
                        {
                            while (e.MoveNext())
                            {
                                TElement nextValue = e.Current;
                                if (Comparer<TElement>.Default.Compare(nextValue, resultValue) >= 0)
                                {
                                    resultValue = nextValue;
                                }
                            }
                        }

                        found = true;
                        return resultValue;
                    }
                }

                found = false;
                return default;
            }
        }

        private sealed class SkipTakeOrderedIterator<TElement> : Iterator<TElement>
        {
            private readonly OrderedIterator<TElement> _source;
            private readonly int _minIndexInclusive;
            private readonly int _maxIndexInclusive;

            private TElement[]? _buffer;
            private int[]? _map;
            private int _maxIdx;

            public SkipTakeOrderedIterator(OrderedIterator<TElement> source, int minIdxInclusive, int maxIdxInclusive)
            {
                _source = source;
                _minIndexInclusive = minIdxInclusive;
                _maxIndexInclusive = maxIdxInclusive;
            }

            private protected override Iterator<TElement> Clone() => new SkipTakeOrderedIterator<TElement>(_source, _minIndexInclusive, _maxIndexInclusive);

            public override bool MoveNext()
            {
                int state = _state;

            Initialized:
                if (state > 1)
                {
                    int[] map = _map;
                    int i = state - 2 + _minIndexInclusive;
                    if (i <= _maxIdx)
                    {
                        _current = _buffer[map[i]];
                        _state++;
                        return true;
                    }
                }
                else if (state == 1)
                {
                    TElement[] buffer = _source._source.ToArray();
                    int count = buffer.Length;
                    if (count > _minIndexInclusive)
                    {
                        _maxIdx = _maxIndexInclusive;
                        if (count <= _maxIdx)
                        {
                            _maxIdx = count - 1;
                        }

                        if (_minIndexInclusive == _maxIdx)
                        {
                            _current = _source.GetEnumerableSorter().ElementAt(buffer, count, _minIndexInclusive);
                            _state = -1;
                            return true;
                        }

                        _map = _source.SortedMap(buffer, _minIndexInclusive, _maxIdx);
                        _buffer = buffer;
                        _state = state = 2;
                        goto Initialized;
                    }
                }

                Dispose();
                return false;
            }

            public override Iterator<TElement>? Skip(int count)
            {
                int minIndex = _minIndexInclusive + count;
                return (uint)minIndex > (uint)_maxIndexInclusive ? null : new SkipTakeOrderedIterator<TElement>(_source, minIndex, _maxIndexInclusive);
            }

            public override Iterator<TElement> Take(int count)
            {
                int maxIndex = _minIndexInclusive + count - 1;
                if ((uint)maxIndex >= (uint)_maxIndexInclusive)
                {
                    return this;
                }

                return new SkipTakeOrderedIterator<TElement>(_source, _minIndexInclusive, maxIndex);
            }

            public override TElement? TryGetElementAt(int index, out bool found)
            {
                if ((uint)index <= (uint)(_maxIndexInclusive - _minIndexInclusive))
                {
                    return _source.TryGetElementAt(index + _minIndexInclusive, out found);
                }

                found = false;
                return default;
            }

            public override TElement? TryGetFirst(out bool found) => _source.TryGetElementAt(_minIndexInclusive, out found);

            public override TElement? TryGetLast(out bool found) =>
                _source.TryGetLast(_minIndexInclusive, _maxIndexInclusive, out found);

            public override TElement[] ToArray() => _source.ToArray(_minIndexInclusive, _maxIndexInclusive);

            public override List<TElement> ToList() => _source.ToList(_minIndexInclusive, _maxIndexInclusive);

            public override int GetCount(bool onlyIfCheap) => _source.GetCount(_minIndexInclusive, _maxIndexInclusive, onlyIfCheap);
        }

        /// <summary>Fills the <paramref name="destination"/> with incrementing numbers, starting from <paramref name="value"/>.</summary>
        private static void FillIncrementing<T>(Span<T> destination, T value) where T : INumber<T>
        {
            if (Vector.IsHardwareAccelerated &&
                Vector<T>.IsSupported &&
                destination.Length >= Vector<T>.Count)
            {
                Vector<T> current = new Vector<T>(value) + Vector<T>.Indices;
                Vector<T> increment = new Vector<T>(T.CreateTruncating(Vector<T>.Count));

                while (destination.Length >= Vector<T>.Count)
                {
                    current.CopyTo(destination);
                    current += increment;
                    destination = destination.Slice(Vector<T>.Count);
                }

                value = current[0];
            }

            foreach (ref T slot in destination)
            {
                slot = value++;
            }
        }

        public static IEnumerable<TResult> Select<TSource, TResult>(
            this IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            if (source is Iterator<TSource> iterator)
            {
                // With native AOT, calling into the `Select` generic virtual method results in NxM
                // expansion of native code. If the option is enabled, we don't call the generic virtual
                // for value types. We don't do the same for reference types because reference type
                // expansion can happen lazily at runtime and the AOT compiler does postpone it (we
                // don't need more code, just more data structures describing the new types).
                if (IsSizeOptimized && typeof(TResult).IsValueType)
                {
                    return source is IList<TSource> il
                        ? new SizeOptIListSelectIterator<TSource, TResult>(il, selector)
                        : new IEnumerableSelectIterator<TSource, TResult>(iterator, selector);
                }
                else
                {
                    return iterator.Select(selector);
                }
            }

            if (source is IList<TSource> ilist)
            {
                if (IsSizeOptimized)
                {
                    return new SizeOptIListSelectIterator<TSource, TResult>(ilist, selector);
                }

                if (source is TSource[] array)
                {
                    if (array.Length == 0)
                    {
                        return [];
                    }

                    return new ArraySelectIterator<TSource, TResult>(array, selector);
                }

                if (source is List<TSource> list)
                {
                    return new ListSelectIterator<TSource, TResult>(list, selector);
                }

                return new IListSelectIterator<TSource, TResult>(ilist, selector);
            }

            return new IEnumerableSelectIterator<TSource, TResult>(source, selector);
        }

        public static IEnumerable<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, int, TResult> selector)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (selector is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.selector);
            }

            if (IsEmptyArray(source))
            {
                return [];
            }

            return SelectIterator(source, selector);
        }

        private static IEnumerable<TResult> SelectIterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, int, TResult> selector)
        {
            int index = -1;
            foreach (TSource element in source)
            {
                checked
                {
                    index++;
                }

                yield return selector(element, index);
            }
        }

        public static IEnumerable<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (predicate is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);
            }

            if (source is Iterator<TSource> iterator)
            {
                return iterator.Where(predicate);
            }

            // Only use IList when size-optimizing (no array or List specializations).
            if (IsSizeOptimized && source is IList<TSource> sourceList)
            {
                return new SizeOptIListWhereIterator<TSource>(sourceList, predicate);
            }

            if (source is TSource[] array)
            {
                if (array.Length == 0)
                {
                    return [];
                }

                return new ArrayWhereIterator<TSource>(array, predicate);
            }

            if (source is List<TSource> list)
            {
                return new ListWhereIterator<TSource>(list, predicate);
            }

            return new IEnumerableWhereIterator<TSource>(source, predicate);
        }

        public static IEnumerable<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        {
            if (source is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (predicate is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);
            }

            if (IsEmptyArray(source))
            {
                return [];
            }

            return WhereIterator(source, predicate);
        }

        private static IEnumerable<TSource> WhereIterator<TSource>(IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        {
            int index = -1;
            foreach (TSource element in source)
            {
                checked
                {
                    index++;
                }

                if (predicate(element, index))
                {
                    yield return element;
                }
            }
        }

        // A comparer that chains comparisons, and pushes through the last element found to be
        // lower or higher (depending on use), so as to represent the sort of comparisons
        // done by OrderBy().ThenBy() combinations.
        private abstract class CachingComparer<TElement>
        {
            internal abstract int Compare(TElement element, bool cacheLower);

            internal abstract void SetElement(TElement element);
        }

        private class CachingComparer<TElement, TKey> : CachingComparer<TElement>
        {
            protected readonly Func<TElement, TKey> _keySelector;
            protected readonly IComparer<TKey> _comparer;
            protected readonly bool _descending;
            protected TKey? _lastKey;

            public CachingComparer(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending)
            {
                _keySelector = keySelector;
                _comparer = comparer;
                _descending = descending;
            }

            internal override int Compare(TElement element, bool cacheLower)
            {
                TKey newKey = _keySelector(element);
                int cmp = _descending ? _comparer.Compare(_lastKey, newKey) : _comparer.Compare(newKey, _lastKey);
                if (cacheLower == cmp < 0)
                {
                    _lastKey = newKey;
                }

                return cmp;
            }

            internal override void SetElement(TElement element)
            {
                _lastKey = _keySelector(element);
            }
        }

        private sealed class CachingComparerWithChild<TElement, TKey> : CachingComparer<TElement, TKey>
        {
            private readonly CachingComparer<TElement> _child;

            public CachingComparerWithChild(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending, CachingComparer<TElement> child)
                : base(keySelector, comparer, descending)
            {
                _child = child;
            }

            internal override int Compare(TElement element, bool cacheLower)
            {
                TKey newKey = _keySelector(element);
                int cmp = _descending ? _comparer.Compare(_lastKey, newKey) : _comparer.Compare(newKey, _lastKey);
                if (cmp == 0)
                {
                    return _child.Compare(element, cacheLower);
                }

                if (cacheLower == cmp < 0)
                {
                    _lastKey = newKey;
                    _child.SetElement(element);
                }

                return cmp;
            }

            internal override void SetElement(TElement element)
            {
                base.SetElement(element);
                _child.SetElement(element);
            }
        }

        private abstract class EnumerableSorter<TElement>
        {
            /// <summary>Function that returns its input unmodified.</summary>
            /// <remarks>
            /// Used for reference equality in order to avoid unnecessary computation when a caller
            /// can benefit from knowing that the produced value is identical to the input.
            /// </remarks>
            internal static readonly Func<TElement, TElement> IdentityFunc = e => e;

            internal abstract void ComputeKeys(TElement[] elements, int count);

            internal abstract int CompareAnyKeys(int index1, int index2);

            private int[] ComputeMap(TElement[] elements, int count)
            {
                ComputeKeys(elements, count);

                int[] map = new int[count];
                FillIncrementing(map, 0);
                return map;
            }

            internal int[] Sort(TElement[] elements, int count)
            {
                int[] map = ComputeMap(elements, count);
                QuickSort(map, 0, count - 1);
                return map;
            }

            internal int[] Sort(TElement[] elements, int count, int minIdx, int maxIdx)
            {
                int[] map = ComputeMap(elements, count);
                PartialQuickSort(map, 0, count - 1, minIdx, maxIdx);
                return map;
            }

            internal TElement ElementAt(TElement[] elements, int count, int idx)
            {
                int[] map = ComputeMap(elements, count);
                return idx == 0 ?
                    elements[Min(map, count)] :
                    elements[QuickSelect(map, count - 1, idx)];
            }

            protected abstract void QuickSort(int[] map, int left, int right);

            // Sorts the k elements between minIdx and maxIdx without sorting all elements
            // Time complexity: O(n + k log k) best and average case. O(n^2) worse case.
            protected abstract void PartialQuickSort(int[] map, int left, int right, int minIdx, int maxIdx);

            // Finds the element that would be at idx if the collection was sorted.
            // Time complexity: O(n) best and average case. O(n^2) worse case.
            protected abstract int QuickSelect(int[] map, int right, int idx);

            protected abstract int Min(int[] map, int count);
        }

        private sealed class EnumerableSorter<TElement, TKey> : EnumerableSorter<TElement>
        {
            private readonly Func<TElement, TKey> _keySelector;
            private readonly IComparer<TKey> _comparer;
            private readonly bool _descending;
            private readonly EnumerableSorter<TElement>? _next;
            private TKey[]? _keys;

            internal EnumerableSorter(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending, EnumerableSorter<TElement>? next)
            {
                _keySelector = keySelector;
                _comparer = comparer;
                _descending = descending;
                _next = next;
            }

            internal override void ComputeKeys(TElement[] elements, int count)
            {
                Func<TElement, TKey> keySelector = _keySelector;
                if (!ReferenceEquals(keySelector, IdentityFunc))
                {
                    var keys = new TKey[count];
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i] = keySelector(elements[i]);
                    }
                    _keys = keys;
                }
                else
                {
                    // The key selector is our known identity function, which means we don't
                    // need to invoke the key selector for every element.  Further, we can just
                    // use the original array as the keys (even if count is smaller, as the additional
                    // values will just be ignored).
                    Debug.Assert(typeof(TKey) == typeof(TElement));
                    _keys = (TKey[])(object)elements;
                }

                _next?.ComputeKeys(elements, count);
            }

            internal override int CompareAnyKeys(int index1, int index2)
            {
                TKey[]? keys = _keys;

                int c = _comparer.Compare(keys[index1], keys[index2]);
                if (c == 0)
                {
                    if (_next is null)
                    {
                        return index1 - index2; // ensure stability of sort
                    }

                    return _next.CompareAnyKeys(index1, index2);
                }

                // -c will result in a negative value for int.MinValue (-int.MinValue == int.MinValue).
                // Flipping keys earlier is more likely to trigger something strange in a comparer,
                // particularly as it comes to the sort being stable.
                return (_descending != (c > 0)) ? 1 : -1;
            }

            private int CompareAnyKeys_DefaultComparer_NoNext_Ascending(int index1, int index2)
            {
                TKey[]? keys = _keys;

                int c = Comparer<TKey>.Default.Compare(keys[index1], keys[index2]);
                return
                    c == 0 ? index1 - index2 : // ensure stability of sort
                    c;
            }

            private int CompareAnyKeys_DefaultComparer_NoNext_Descending(int index1, int index2)
            {
                TKey[]? keys = _keys;

                int c = Comparer<TKey>.Default.Compare(keys[index2], keys[index1]);
                return
                    c == 0 ? index1 - index2 : // ensure stability of sort
                    c;
            }

            private int CompareKeys(int index1, int index2) => index1 == index2 ? 0 : CompareAnyKeys(index1, index2);

            protected override void QuickSort(int[] keys, int lo, int hi)
            {
                Comparison<int> comparison;

                if (typeof(TKey).IsValueType && _next is null && _comparer == Comparer<TKey>.Default)
                {
                    // We can use Comparer<TKey>.Default.Compare and benefit from devirtualization and inlining.
                    // We can also avoid extra steps to check whether we need to deal with a subsequent tie breaker (_next).
                    if (!_descending)
                    {
                        comparison = CompareAnyKeys_DefaultComparer_NoNext_Ascending;
                    }
                    else
                    {
                        comparison = CompareAnyKeys_DefaultComparer_NoNext_Descending;
                    }
                }
                else
                {
                    comparison = CompareAnyKeys;
                }

                new Span<int>(keys, lo, hi - lo + 1).Sort(comparison);
            }

            // Sorts the k elements between minIdx and maxIdx without sorting all elements
            // Time complexity: O(n + k log k) best and average case. O(n^2) worse case.
            protected override void PartialQuickSort(int[] map, int left, int right, int minIdx, int maxIdx)
            {
                do
                {
                    int i = left;
                    int j = right;
                    int x = map[i + ((j - i) >> 1)];
                    do
                    {
                        while (i < map.Length && CompareKeys(x, map[i]) > 0)
                        {
                            i++;
                        }

                        while (j >= 0 && CompareKeys(x, map[j]) < 0)
                        {
                            j--;
                        }

                        if (i > j)
                        {
                            break;
                        }

                        if (i < j)
                        {
                            int temp = map[i];
                            map[i] = map[j];
                            map[j] = temp;
                        }

                        i++;
                        j--;
                    }
                    while (i <= j);

                    if (minIdx >= i)
                    {
                        left = i + 1;
                    }
                    else if (maxIdx <= j)
                    {
                        right = j - 1;
                    }

                    if (j - left <= right - i)
                    {
                        if (left < j)
                        {
                            PartialQuickSort(map, left, j, minIdx, maxIdx);
                        }

                        left = i;
                    }
                    else
                    {
                        if (i < right)
                        {
                            PartialQuickSort(map, i, right, minIdx, maxIdx);
                        }

                        right = j;
                    }
                }
                while (left < right);
            }

            // Finds the element that would be at idx if the collection was sorted.
            // Time complexity: O(n) best and average case. O(n^2) worse case.
            protected override int QuickSelect(int[] map, int right, int idx)
            {
                int left = 0;
                do
                {
                    int i = left;
                    int j = right;
                    int x = map[i + ((j - i) >> 1)];
                    do
                    {
                        while (i < map.Length && CompareKeys(x, map[i]) > 0)
                        {
                            i++;
                        }

                        while (j >= 0 && CompareKeys(x, map[j]) < 0)
                        {
                            j--;
                        }

                        if (i > j)
                        {
                            break;
                        }

                        if (i < j)
                        {
                            int temp = map[i];
                            map[i] = map[j];
                            map[j] = temp;
                        }

                        i++;
                        j--;
                    }
                    while (i <= j);

                    if (i <= idx)
                    {
                        left = i + 1;
                    }
                    else
                    {
                        right = j - 1;
                    }

                    if (j - left <= right - i)
                    {
                        if (left < j)
                        {
                            right = j;
                        }

                        left = i;
                    }
                    else
                    {
                        if (i < right)
                        {
                            left = i;
                        }

                        right = j;
                    }
                }
                while (left < right);

                return map[idx];
            }

            protected override int Min(int[] map, int count)
            {
                int index = 0;
                for (int i = 1; i < count; i++)
                {
                    if (CompareKeys(map[i], map[index]) < 0)
                    {
                        index = i;
                    }
                }
                return map[index];
            }
        }

        public interface IOrderedEnumerable<out TElement> : IEnumerable<TElement>
        {
            IOrderedEnumerable<TElement> CreateOrderedEnumerable<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey>? comparer, bool descending);
        }

        /// <summary>
        /// An iterator that maps each item of an <see cref="Iterator{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source elements.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        private sealed class IteratorSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly Iterator<TSource> _source;
            private readonly Func<TSource, TResult> _selector;
            private Iterator<TSource>? _enumerator;

            public IteratorSelectIterator(Iterator<TSource> source, Func<TSource, TResult> selector)
            {
                _source = source;
                _selector = selector;
            }

            private protected override Iterator<TResult> Clone() =>
                new IteratorSelectIterator<TSource, TResult>(_source, _selector);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_enumerator.MoveNext())
                        {
                            _current = _selector(_enumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override void Dispose()
            {
                if (_enumerator is not null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                base.Dispose();
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new IteratorSelectIterator<TSource, TResult2>(_source, CombineSelectors(_selector, selector));

            public override Iterator<TResult>? Skip(int count)
            {
                Iterator<TSource>? source = _source.Skip(count);
                return source is null ? null : new IteratorSelectIterator<TSource, TResult>(source, _selector);
            }

            public override Iterator<TResult>? Take(int count)
            {
                Iterator<TSource>? source = _source.Take(count);
                return source is null ? null : new IteratorSelectIterator<TSource, TResult>(source, _selector);
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                bool sourceFound;
                TSource? input = _source.TryGetElementAt(index, out sourceFound);
                found = sourceFound;
                return sourceFound ? _selector(input!) : default!;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                bool sourceFound;
                TSource? input = _source.TryGetFirst(out sourceFound);
                found = sourceFound;
                return sourceFound ? _selector(input!) : default!;
            }

            public override TResult? TryGetLast(out bool found)
            {
                bool sourceFound;
                TSource? input = _source.TryGetLast(out sourceFound);
                found = sourceFound;
                return sourceFound ? _selector(input!) : default!;
            }

            private TResult[] ToArrayNoPresizing()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                Func<TSource, TResult> selector = _selector;
                foreach (TSource input in _source)
                {
                    builder.Add(selector(input));
                }

                TResult[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            private TResult[] PreallocatingToArray(int count)
            {
                TResult[] array = new TResult[count];
                Fill(_source, array, _selector);
                return array;
            }

            public override TResult[] ToArray()
            {
                int count = _source.GetCount(onlyIfCheap: true);
                return count switch
                {
                    -1 => ToArrayNoPresizing(),
                    0 => [],
                    _ => PreallocatingToArray(count),
                };
            }

            private List<TResult> ToListNoPresizing()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                Func<TSource, TResult> selector = _selector;
                foreach (TSource input in _source)
                {
                    builder.Add(selector(input));
                }

                List<TResult> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override List<TResult> ToList()
            {
                int count = _source.GetCount(onlyIfCheap: true);
                List<TResult> list;
                switch (count)
                {
                    case -1:
                        list = ToListNoPresizing();
                        break;
                    case 0:
                        list = [];
                        break;
                    default:
                        list = new List<TResult>(count);
                        Fill(_source, SetCountAndGetSpan(list, count), _selector);
                        break;
                }

                return list;
            }

            private static void Fill(Iterator<TSource> source, Span<TResult> results, Func<TSource, TResult> func)
            {
                int index = 0;
                foreach (TSource item in source)
                {
                    results[index] = func(item);
                    ++index;
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                if (!onlyIfCheap)
                {
                    // In case someone uses Count() to force evaluation of
                    // the selector, run it provided `onlyIfCheap` is false.

                    int count = 0;

                    foreach (TSource item in _source)
                    {
                        _selector(item);
                        checked { count++; }
                    }

                    return count;
                }

                return _source.GetCount(onlyIfCheap);
            }
        }

        /// <summary>
        /// An iterator that maps each item of an <see cref="IEnumerable{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source enumerable.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        private sealed class IEnumerableSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly IEnumerable<TSource> _source;
            private readonly Func<TSource, TResult> _selector;
            private IEnumerator<TSource>? _enumerator;

            public IEnumerableSelectIterator(IEnumerable<TSource> source, Func<TSource, TResult> selector)
            {
                _source = source;
                _selector = selector;
            }

            private protected override Iterator<TResult> Clone() =>
                new IEnumerableSelectIterator<TSource, TResult>(_source, _selector);

            public override void Dispose()
            {
                if (_enumerator is not null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                base.Dispose();
            }

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_enumerator.MoveNext())
                        {
                            _current = _selector(_enumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new IEnumerableSelectIterator<TSource, TResult2>(_source, CombineSelectors(_selector, selector));

            public override TResult[] ToArray()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                Func<TSource, TResult> selector = _selector;
                foreach (TSource item in _source)
                {
                    builder.Add(selector(item));
                }

                TResult[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TResult> ToList()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                Func<TSource, TResult> selector = _selector;
                foreach (TSource item in _source)
                {
                    builder.Add(selector(item));
                }

                List<TResult> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;

                foreach (TSource item in _source)
                {
                    _selector(item);
                    checked
                    {
                        count++;
                    }
                }

                return count;
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if (index >= 0)
                {
                    IEnumerator<TSource> e = _source.GetEnumerator();
                    try
                    {
                        while (e.MoveNext())
                        {
                            if (index == 0)
                            {
                                found = true;
                                return _selector(e.Current);
                            }

                            index--;
                        }
                    }
                    finally
                    {
                        (e as IDisposable)?.Dispose();
                    }
                }

                found = false;
                return default;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                using IEnumerator<TSource> e = _source.GetEnumerator();
                if (e.MoveNext())
                {
                    found = true;
                    return _selector(e.Current);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found)
            {
                using IEnumerator<TSource> e = _source.GetEnumerator();

                if (e.MoveNext())
                {
                    found = true;
                    TSource last = e.Current;

                    while (e.MoveNext())
                    {
                        last = e.Current;
                    }

                    return _selector(last);
                }

                found = false;
                return default;
            }
        }

        /// <summary>
        /// An iterator that maps each item of an array.
        /// </summary>
        /// <typeparam name="TSource">The type of the source array.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        [DebuggerDisplay("Count = {CountForDebugger}")]
        private sealed class ArraySelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly TSource[] _source;
            private readonly Func<TSource, TResult> _selector;

            public ArraySelectIterator(TSource[] source, Func<TSource, TResult> selector)
            {
                _source = source;
                _selector = selector;
            }

            private int CountForDebugger => _source.Length;

            private protected override Iterator<TResult> Clone() => new ArraySelectIterator<TSource, TResult>(_source, _selector);

            public override bool MoveNext()
            {
                TSource[] source = _source;
                int index = _state - 1;
                if ((uint)index < (uint)source.Length)
                {
                    _state++;
                    _current = _selector(source[index]);
                    return true;
                }

                Dispose();
                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new ArraySelectIterator<TSource, TResult2>(_source, CombineSelectors(_selector, selector));

            public override TResult[] ToArray()
            {
                // See assert in constructor.
                // Since _source should never be empty, we don't check for 0/return Array.Empty.
                TSource[] source = _source;

                var results = new TResult[source.Length];
                Fill(source, results, _selector);

                return results;
            }

            public override List<TResult> ToList()
            {
                TSource[] source = _source;
                Debug.Assert(source.Length > 0);

                var results = new List<TResult>(source.Length);
                Fill(source, SetCountAndGetSpan(results, source.Length), _selector);

                return results;
            }

            private static void Fill(ReadOnlySpan<TSource> source, Span<TResult> destination, Func<TSource, TResult> func)
            {
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = func(source[i]);
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                if (!onlyIfCheap)
                {
                    foreach (TSource item in _source)
                    {
                        _selector(item);
                    }
                }

                return _source.Length;
            }

            public override Iterator<TResult>? Skip(int count)
            {
                Debug.Assert(count > 0);
                if (count >= _source.Length)
                {
                    return null;
                }

                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, count, int.MaxValue);
            }

            public override Iterator<TResult> Take(int count)
            {
                return count >= _source.Length ?
                    this :
                    new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, 0, count - 1);
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                TSource[] source = _source;
                if ((uint)index < (uint)source.Length)
                {
                    found = true;
                    return _selector(source[index]);
                }

                found = false;
                return default;
            }

            public override TResult TryGetFirst(out bool found)
            {
                found = true;
                return _selector(_source[0]);
            }

            public override TResult TryGetLast(out bool found)
            {
                found = true;
                return _selector(_source[^1]);
            }

            public override bool Contains(TResult value)
            {
                foreach (TSource item in _source)
                {
                    if (EqualityComparer<TResult>.Default.Equals(_selector(item), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that maps each item of a <see cref="List{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source list.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        [DebuggerDisplay("Count = {CountForDebugger}")]
        private sealed class ListSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly List<TSource> _source;
            private readonly Func<TSource, TResult> _selector;
            private List<TSource>.Enumerator _enumerator;

            public ListSelectIterator(List<TSource> source, Func<TSource, TResult> selector)
            {
                _source = source;
                _selector = selector;
            }

            private int CountForDebugger => _source.Count;

            private protected override Iterator<TResult> Clone() => new ListSelectIterator<TSource, TResult>(_source, _selector);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_enumerator.MoveNext())
                        {
                            _current = _selector(_enumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new ListSelectIterator<TSource, TResult2>(_source, CombineSelectors(_selector, selector));

            public override TResult[] ToArray()
            {
                ReadOnlySpan<TSource> source = CollectionsMarshal.AsSpan(_source);
                if (source.Length == 0)
                {
                    return [];
                }

                var results = new TResult[source.Length];
                Fill(source, results, _selector);

                return results;
            }

            public override List<TResult> ToList()
            {
                ReadOnlySpan<TSource> source = CollectionsMarshal.AsSpan(_source);

                var results = new List<TResult>(source.Length);
                Fill(source, SetCountAndGetSpan(results, source.Length), _selector);

                return results;
            }

            private static void Fill(ReadOnlySpan<TSource> source, Span<TResult> destination, Func<TSource, TResult> func)
            {
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = func(source[i]);
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                int count = _source.Count;

                if (!onlyIfCheap)
                {
                    for (int i = 0; i < count; i++)
                    {
                        _selector(_source[i]);
                    }
                }

                return count;
            }

            public override Iterator<TResult> Skip(int count)
            {
                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, count, int.MaxValue);
            }

            public override Iterator<TResult> Take(int count)
            {
                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, 0, count - 1);
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if ((uint)index < (uint)_source.Count)
                {
                    found = true;
                    return _selector(_source[index]);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                if (_source.Count != 0)
                {
                    found = true;
                    return _selector(_source[0]);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found)
            {
                int len = _source.Count;
                if (len != 0)
                {
                    found = true;
                    return _selector(_source[len - 1]);
                }

                found = false;
                return default;
            }

            public override bool Contains(TResult value)
            {
                int count = _source.Count;

                for (int i = 0; i < count; i++)
                {
                    if (EqualityComparer<TResult>.Default.Equals(_selector(_source[i]), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that maps each item of an <see cref="IList{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source list.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        [DebuggerDisplay("Count = {CountForDebugger}")]
        private sealed class IListSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly IList<TSource> _source;
            private readonly Func<TSource, TResult> _selector;
            private IEnumerator<TSource>? _enumerator;

            public IListSelectIterator(IList<TSource> source, Func<TSource, TResult> selector)
            {
                _source = source;
                _selector = selector;
            }

            private int CountForDebugger => _source.Count;

            private protected override Iterator<TResult> Clone() => new IListSelectIterator<TSource, TResult>(_source, _selector);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_enumerator.MoveNext())
                        {
                            _current = _selector(_enumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override void Dispose()
            {
                if (_enumerator is not null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                base.Dispose();
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new IListSelectIterator<TSource, TResult2>(_source, CombineSelectors(_selector, selector));

            public override TResult[] ToArray()
            {
                int count = _source.Count;
                if (count == 0)
                {
                    return [];
                }

                var results = new TResult[count];
                Fill(_source, results, _selector);

                return results;
            }

            public override List<TResult> ToList()
            {
                IList<TSource> source = _source;
                int count = _source.Count;

                var results = new List<TResult>(count);
                Fill(source, SetCountAndGetSpan(results, count), _selector);

                return results;
            }

            private static void Fill(IList<TSource> source, Span<TResult> results, Func<TSource, TResult> func)
            {
                for (int i = 0; i < results.Length; i++)
                {
                    results[i] = func(source[i]);
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                int count = _source.Count;

                if (!onlyIfCheap)
                {
                    for (int i = 0; i < count; i++)
                    {
                        _selector(_source[i]);
                    }
                }

                return count;
            }

            public override Iterator<TResult> Skip(int count)
            {
                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, count, int.MaxValue);
            }

            public override Iterator<TResult> Take(int count)
            {
                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, 0, count - 1);
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if ((uint)index < (uint)_source.Count)
                {
                    found = true;
                    return _selector(_source[index]);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                if (_source.Count != 0)
                {
                    found = true;
                    return _selector(_source[0]);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found)
            {
                int len = _source.Count;
                if (len != 0)
                {
                    found = true;
                    return _selector(_source[len - 1]);
                }

                found = false;
                return default;
            }

            public override bool Contains(TResult value)
            {
                int count = _source.Count;

                for (int i = 0; i < count; i++)
                {
                    if (EqualityComparer<TResult>.Default.Equals(_selector(_source[i]), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that maps each item of part of an <see cref="IList{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source list.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        [DebuggerDisplay("Count = {Count}")]
        private sealed class IListSkipTakeSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly IList<TSource> _source;
            private readonly Func<TSource, TResult> _selector;
            private readonly int _minIndexInclusive;
            private readonly int _maxIndexInclusive;

            public IListSkipTakeSelectIterator(IList<TSource> source, Func<TSource, TResult> selector, int minIndexInclusive, int maxIndexInclusive)
            {
                _source = source;
                _selector = selector;
                _minIndexInclusive = minIndexInclusive;
                _maxIndexInclusive = maxIndexInclusive;
            }

            private protected override Iterator<TResult> Clone() =>
                new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, _minIndexInclusive, _maxIndexInclusive);

            public override bool MoveNext()
            {
                // _state - 1 represents the zero-based index into the list.
                // Having a separate field for the index would be more readable. However, we save it
                // into _state with a bias to minimize field size of the iterator.
                int index = _state - 1;
                if ((uint)index <= (uint)(_maxIndexInclusive - _minIndexInclusive) && index < _source.Count - _minIndexInclusive)
                {
                    _current = _selector(_source[_minIndexInclusive + index]);
                    ++_state;
                    return true;
                }

                Dispose();
                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new IListSkipTakeSelectIterator<TSource, TResult2>(_source, CombineSelectors(_selector, selector), _minIndexInclusive, _maxIndexInclusive);

            public override Iterator<TResult>? Skip(int count)
            {
                int minIndex = _minIndexInclusive + count;
                return (uint)minIndex > (uint)_maxIndexInclusive ? null : new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, minIndex, _maxIndexInclusive);
            }

            public override Iterator<TResult> Take(int count)
            {
                int maxIndex = _minIndexInclusive + count - 1;
                return (uint)maxIndex >= (uint)_maxIndexInclusive ? this : new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, _minIndexInclusive, maxIndex);
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if ((uint)index <= (uint)(_maxIndexInclusive - _minIndexInclusive) && index < _source.Count - _minIndexInclusive)
                {
                    found = true;
                    return _selector(_source[_minIndexInclusive + index]);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                if (_source.Count > _minIndexInclusive)
                {
                    found = true;
                    return _selector(_source[_minIndexInclusive]);
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found)
            {
                int lastIndex = _source.Count - 1;
                if (lastIndex >= _minIndexInclusive)
                {
                    found = true;
                    return _selector(_source[Math.Min(lastIndex, _maxIndexInclusive)]);
                }

                found = false;
                return default;
            }

            private int Count
            {
                get
                {
                    int count = _source.Count;
                    if (count <= _minIndexInclusive)
                    {
                        return 0;
                    }

                    return Math.Min(count - 1, _maxIndexInclusive) - _minIndexInclusive + 1;
                }
            }

            public override TResult[] ToArray()
            {
                int count = Count;
                if (count == 0)
                {
                    return [];
                }

                TResult[] array = new TResult[count];
                Fill(_source, array, _selector, _minIndexInclusive);

                return array;
            }

            public override List<TResult> ToList()
            {
                int count = Count;
                if (count == 0)
                {
                    return [];
                }

                List<TResult> list = new List<TResult>(count);
                Fill(_source, SetCountAndGetSpan(list, count), _selector, _minIndexInclusive);

                return list;
            }

            private static void Fill(IList<TSource> source, Span<TResult> destination, Func<TSource, TResult> func, int sourceIndex)
            {
                for (int i = 0; i < destination.Length; i++, sourceIndex++)
                {
                    destination[i] = func(source[sourceIndex]);
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                int count = Count;

                if (!onlyIfCheap)
                {
                    int end = _minIndexInclusive + count;
                    for (int i = _minIndexInclusive; i != end; ++i)
                    {
                        _selector(_source[i]);
                    }
                }

                return count;
            }

            public override bool Contains(TResult value)
            {
                int count = Count;

                int end = _minIndexInclusive + count;
                for (int i = _minIndexInclusive; i != end; ++i)
                {
                    if (EqualityComparer<TResult>.Default.Equals(_selector(_source[i]), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class SizeOptIListSelectIterator<TSource, TResult>(IList<TSource> _source, Func<TSource, TResult> _selector)
            : Iterator<TResult>
        {
            private IEnumerator<TSource>? _enumerator;

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;

                foreach (TSource item in _source)
                {
                    _selector(item);
                    checked
                    {
                        count++;
                    }
                }

                return count;
            }

            public override Iterator<TResult> Skip(int count)
            {
                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, count, int.MaxValue);
            }

            public override Iterator<TResult> Take(int count)
            {
                return new IListSkipTakeSelectIterator<TSource, TResult>(_source, _selector, 0, count - 1);
            }

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_enumerator.MoveNext())
                        {
                            _current = _selector(_enumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override void Dispose()
            {
                if (_enumerator is { } e)
                {
                    _enumerator = null;
                    e.Dispose();
                }

                base.Dispose();
            }

            public override TResult[] ToArray()
            {
                TResult[] array = new TResult[_source.Count];
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = _selector(_source[i]);
                }
                return array;
            }

            public override List<TResult> ToList()
            {
                List<TResult> list = new List<TResult>(_source.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    list.Add(_selector(_source[i]));
                }
                return list;
            }

            private protected override Iterator<TResult> Clone()
                => new SizeOptIListSelectIterator<TSource, TResult>(_source, _selector);
        }

        /// <summary>
        /// An iterator that filters each item of an <see cref="IEnumerable{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source enumerable.</typeparam>
        private sealed class IEnumerableWhereIterator<TSource> : Iterator<TSource>
        {
            private readonly IEnumerable<TSource> _source;
            private readonly Func<TSource, bool> _predicate;
            private IEnumerator<TSource>? _enumerator;

            public IEnumerableWhereIterator(IEnumerable<TSource> source, Func<TSource, bool> predicate)
            {
                _source = source;
                _predicate = predicate;
            }

            private protected override Iterator<TSource> Clone() => new IEnumerableWhereIterator<TSource>(_source, _predicate);

            public override void Dispose()
            {
                if (_enumerator is not null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                base.Dispose();
            }

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        while (_enumerator.MoveNext())
                        {
                            TSource item = _enumerator.Current;
                            if (_predicate(item))
                            {
                                _current = item;
                                return true;
                            }
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult> Select<TResult>(Func<TSource, TResult> selector) =>
                new IEnumerableWhereSelectIterator<TSource, TResult>(_source, _predicate, selector);

            public override IEnumerable<TSource> Where(Func<TSource, bool> predicate) =>
                new IEnumerableWhereIterator<TSource>(_source, Utilities.CombinePredicates(_predicate, predicate));

            public override int GetCount(bool onlyIfCheap)
            {
                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;

                foreach (TSource item in _source)
                {
                    if (_predicate(item))
                    {
                        checked
                        {
                            count++;
                        }
                    }
                }

                return count;
            }

            public override TSource[] ToArray()
            {
                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                Func<TSource, bool> predicate = _predicate;
                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        builder.Add(item);
                    }
                }

                TSource[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TSource> ToList()
            {
                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                Func<TSource, bool> predicate = _predicate;
                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        builder.Add(item);
                    }
                }

                List<TSource> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override TSource? TryGetFirst(out bool found)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        found = true;
                        return item;
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetLast(out bool found)
            {
                using IEnumerator<TSource> e = _source.GetEnumerator();

                if (e.MoveNext())
                {
                    Func<TSource, bool> predicate = _predicate;
                    TSource? last = default;
                    do
                    {
                        TSource current = e.Current;
                        if (predicate(current))
                        {
                            last = current;
                            found = true;

                            while (e.MoveNext())
                            {
                                current = e.Current;
                                if (predicate(current))
                                {
                                    last = current;
                                }
                            }

                            return last;
                        }
                    }
                    while (e.MoveNext());
                }

                found = false;
                return default;
            }

            public override TSource? TryGetElementAt(int index, out bool found)
            {
                if (index >= 0)
                {
                    Func<TSource, bool> predicate = _predicate;

                    foreach (TSource item in _source)
                    {
                        if (predicate(item))
                        {
                            if (index == 0)
                            {
                                found = true;
                                return item;
                            }

                            index--;
                        }
                    }
                }

                found = false;
                return default;
            }

            public override bool Contains(TSource value)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item) && EqualityComparer<TSource>.Default.Equals(item, value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that filters each item of an array.
        /// </summary>
        /// <typeparam name="TSource">The type of the source array.</typeparam>
        private sealed partial class ArrayWhereIterator<TSource> : Iterator<TSource>
        {
            private readonly TSource[] _source;
            private readonly Func<TSource, bool> _predicate;

            public ArrayWhereIterator(TSource[] source, Func<TSource, bool> predicate)
            {
                _source = source;
                _predicate = predicate;
            }

            private protected override Iterator<TSource> Clone() =>
                new ArrayWhereIterator<TSource>(_source, _predicate);

            public override bool MoveNext()
            {
                int index = _state - 1;
                TSource[] source = _source;

                while ((uint)index < (uint)source.Length)
                {
                    TSource item = source[index];
                    index = _state++;
                    if (_predicate(item))
                    {
                        _current = item;
                        return true;
                    }
                }

                Dispose();
                return false;
            }

            public override IEnumerable<TResult> Select<TResult>(Func<TSource, TResult> selector) =>
                new ArrayWhereSelectIterator<TSource, TResult>(_source, _predicate, selector);

            public override IEnumerable<TSource> Where(Func<TSource, bool> predicate) =>
                new ArrayWhereIterator<TSource>(_source, CombinePredicates(_predicate, predicate));

            public override int GetCount(bool onlyIfCheap) => GetCount(onlyIfCheap, _source, _predicate);

            public static int GetCount(bool onlyIfCheap, ReadOnlySpan<TSource> source, Func<TSource, bool> predicate)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;

                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        checked { count++; }
                    }
                }

                return count;
            }

            public override TSource[] ToArray() => ToArray(_source, _predicate);

            public static TSource[] ToArray(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate)
            {
                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        builder.Add(item);
                    }
                }

                TSource[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TSource> ToList() => ToList(_source, _predicate);

            public static List<TSource> ToList(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate)
            {
                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        builder.Add(item);
                    }
                }

                List<TSource> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override TSource? TryGetFirst(out bool found)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        found = true;
                        return item;
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetLast(out bool found)
            {
                TSource[] source = _source;
                Func<TSource, bool> predicate = _predicate;

                for (int i = source.Length - 1; i >= 0; i--)
                {
                    if (predicate(source[i]))
                    {
                        found = true;
                        return source[i];
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetElementAt(int index, out bool found)
            {
                if (index >= 0)
                {
                    Func<TSource, bool> predicate = _predicate;

                    foreach (TSource item in _source)
                    {
                        if (predicate(item))
                        {
                            if (index == 0)
                            {
                                found = true;
                                return item;
                            }

                            index--;
                        }
                    }
                }

                found = false;
                return default;
            }

            public override bool Contains(TSource value)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item) && EqualityComparer<TSource>.Default.Equals(item, value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that filters each item of a <see cref="List{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source list.</typeparam>
        private sealed class ListWhereIterator<TSource> : Iterator<TSource>
        {
            private readonly List<TSource> _source;
            private readonly Func<TSource, bool> _predicate;
            private List<TSource>.Enumerator _enumerator;

            public ListWhereIterator(List<TSource> source, Func<TSource, bool> predicate)
            {
                _source = source;
                _predicate = predicate;
            }

            private protected override Iterator<TSource> Clone() =>
                new ListWhereIterator<TSource>(_source, _predicate);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        while (_enumerator.MoveNext())
                        {
                            TSource item = _enumerator.Current;
                            if (_predicate(item))
                            {
                                _current = item;
                                return true;
                            }
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult> Select<TResult>(Func<TSource, TResult> selector) =>
                new ListWhereSelectIterator<TSource, TResult>(_source, _predicate, selector);

            public override IEnumerable<TSource> Where(Func<TSource, bool> predicate) =>
                new ListWhereIterator<TSource>(_source, CombinePredicates(_predicate, predicate));

            public override int GetCount(bool onlyIfCheap) => ArrayWhereIterator<TSource>.GetCount(onlyIfCheap, CollectionsMarshal.AsSpan(_source), _predicate);

            public override TSource[] ToArray() => ArrayWhereIterator<TSource>.ToArray(CollectionsMarshal.AsSpan(_source), _predicate);

            public override List<TSource> ToList() => ArrayWhereIterator<TSource>.ToList(CollectionsMarshal.AsSpan(_source), _predicate);

            public override TSource? TryGetFirst(out bool found)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in CollectionsMarshal.AsSpan(_source))
                {
                    if (predicate(item))
                    {
                        found = true;
                        return item;
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetLast(out bool found)
            {
                ReadOnlySpan<TSource> source = CollectionsMarshal.AsSpan(_source);
                Func<TSource, bool> predicate = _predicate;

                for (int i = source.Length - 1; i >= 0; i--)
                {
                    if (predicate(source[i]))
                    {
                        found = true;
                        return source[i];
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetElementAt(int index, out bool found)
            {
                if (index >= 0)
                {
                    Func<TSource, bool> predicate = _predicate;

                    foreach (TSource item in CollectionsMarshal.AsSpan(_source))
                    {
                        if (predicate(item))
                        {
                            if (index == 0)
                            {
                                found = true;
                                return item;
                            }

                            index--;
                        }
                    }
                }

                found = false;
                return default;
            }

            public override bool Contains(TSource value)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item) && EqualityComparer<TSource>.Default.Equals(item, value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that filters, then maps, each item of an array.
        /// </summary>
        /// <typeparam name="TSource">The type of the source array.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        private sealed class ArrayWhereSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly TSource[] _source;
            private readonly Func<TSource, bool> _predicate;
            private readonly Func<TSource, TResult> _selector;

            public ArrayWhereSelectIterator(TSource[] source, Func<TSource, bool> predicate, Func<TSource, TResult> selector)
            {
                _source = source;
                _predicate = predicate;
                _selector = selector;
            }

            private protected override Iterator<TResult> Clone() =>
                new ArrayWhereSelectIterator<TSource, TResult>(_source, _predicate, _selector);

            public override bool MoveNext()
            {
                int index = _state - 1;
                TSource[] source = _source;

                while ((uint)index < (uint)source.Length)
                {
                    TSource item = source[index];
                    index = _state++;
                    if (_predicate(item))
                    {
                        _current = _selector(item);
                        return true;
                    }
                }

                Dispose();
                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new ArrayWhereSelectIterator<TSource, TResult2>(_source, _predicate, CombineSelectors(_selector, selector));

            public override int GetCount(bool onlyIfCheap) => GetCount(onlyIfCheap, _source, _predicate, _selector);

            public static int GetCount(bool onlyIfCheap, ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;

                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        selector(item);
                        checked
                        {
                            count++;
                        }
                    }
                }

                return count;
            }

            public override TResult[] ToArray() => ToArray(_source, _predicate, _selector);

            public static TResult[] ToArray(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector)
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        builder.Add(selector(item));
                    }
                }

                TResult[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TResult> ToList() => ToList(_source, _predicate, _selector);

            public static List<TResult> ToList(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector)
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        builder.Add(selector(item));
                    }
                }

                List<TResult> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override TResult? TryGetFirst(out bool found) => TryGetFirst(_source, _predicate, _selector, out found);

            public static TResult? TryGetFirst(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector, out bool found)
            {
                foreach (TSource item in source)
                {
                    if (predicate(item))
                    {
                        found = true;
                        return selector(item);
                    }
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found) => TryGetLast(_source, _predicate, _selector, out found);

            public static TResult? TryGetLast(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector, out bool found)
            {
                for (int i = source.Length - 1; i >= 0; i--)
                {
                    if (predicate(source[i]))
                    {
                        found = true;
                        return selector(source[i]);
                    }
                }

                found = false;
                return default;
            }

            public override TResult? TryGetElementAt(int index, out bool found) => TryGetElementAt(_source, _predicate, _selector, index, out found);

            public static TResult? TryGetElementAt(
                ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector, int index, out bool found)
            {
                if (index >= 0)
                {
                    foreach (TSource item in source)
                    {
                        if (predicate(item))
                        {
                            if (index == 0)
                            {
                                found = true;
                                return selector(item);
                            }

                            index--;
                        }
                    }
                }

                found = false;
                return default;
            }

            public override bool Contains(TResult value) => Contains(_source, _predicate, _selector, value);

            public static bool Contains(ReadOnlySpan<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector, TResult value)
            {
                foreach (TSource item in source)
                {
                    if (predicate(item) && EqualityComparer<TResult>.Default.Equals(selector(item), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// An iterator that filters, then maps, each item of a <see cref="List{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source list.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        private sealed class ListWhereSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly List<TSource> _source;
            private readonly Func<TSource, bool> _predicate;
            private readonly Func<TSource, TResult> _selector;
            private List<TSource>.Enumerator _enumerator;

            public ListWhereSelectIterator(List<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector)
            {
                _source = source;
                _predicate = predicate;
                _selector = selector;
            }

            private protected override Iterator<TResult> Clone() =>
                new ListWhereSelectIterator<TSource, TResult>(_source, _predicate, _selector);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        while (_enumerator.MoveNext())
                        {
                            TSource item = _enumerator.Current;
                            if (_predicate(item))
                            {
                                _current = _selector(item);
                                return true;
                            }
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new ListWhereSelectIterator<TSource, TResult2>(_source, _predicate, CombineSelectors(_selector, selector));

            public override int GetCount(bool onlyIfCheap) 
                => ArrayWhereSelectIterator<TSource, TResult>.GetCount(onlyIfCheap, CollectionsMarshal.AsSpan(_source), _predicate, _selector);

            public override TResult[] ToArray() => ArrayWhereSelectIterator<TSource, TResult>.ToArray(CollectionsMarshal.AsSpan(_source), _predicate, _selector);

            public override List<TResult> ToList() => ArrayWhereSelectIterator<TSource, TResult>.ToList(CollectionsMarshal.AsSpan(_source), _predicate, _selector);

            public override TResult? TryGetElementAt(int index, out bool found) 
                => ArrayWhereSelectIterator<TSource, TResult>.TryGetElementAt(CollectionsMarshal.AsSpan(_source), _predicate, _selector, index, out found);

            public override TResult? TryGetFirst(out bool found) 
                => ArrayWhereSelectIterator<TSource, TResult>.TryGetFirst(CollectionsMarshal.AsSpan(_source), _predicate, _selector, out found);

            public override TResult? TryGetLast(out bool found) 
                => ArrayWhereSelectIterator<TSource, TResult>.TryGetLast(CollectionsMarshal.AsSpan(_source), _predicate, _selector, out found);

            public override bool Contains(TResult value) 
                => ArrayWhereSelectIterator<TSource, TResult>.Contains(CollectionsMarshal.AsSpan(_source), _predicate, _selector, value);
        }

        private sealed class SizeOptIListWhereIterator<TSource> : Iterator<TSource>
        {
            private readonly IList<TSource> _source;
            private readonly Func<TSource, bool> _predicate;
            private IEnumerator<TSource>? _enumerator;

            public SizeOptIListWhereIterator(IList<TSource> source, Func<TSource, bool> predicate)
            {
                _source = source;
                _predicate = predicate;
            }

            private protected override Iterator<TSource> Clone() =>
                new SizeOptIListWhereIterator<TSource>(_source, _predicate);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        while (_enumerator!.MoveNext())
                        {
                            TSource item = _enumerator.Current;
                            if (_predicate(item))
                            {
                                _current = item;
                                return true;
                            }
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override void Dispose()
            {
                if (_enumerator is { } e)
                {
                    _enumerator = null;
                    e.Dispose();
                }

                base.Dispose();
            }

            public override IEnumerable<TSource> Where(Func<TSource, bool> predicate) =>
                new SizeOptIListWhereIterator<TSource>(_source, Utilities.CombinePredicates(_predicate, predicate));

            public override TSource[] ToArray()
            {
                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                foreach (TSource item in _source)
                {
                    if (_predicate(item))
                    {
                        builder.Add(item);
                    }
                }

                TSource[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TSource> ToList()
            {
                SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TSource> builder = new(scratch);

                foreach (TSource item in _source)
                {
                    if (_predicate(item))
                    {
                        builder.Add(item);
                    }
                }

                List<TSource> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override int GetCount(bool onlyIfCheap)
            {
                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;
                foreach (TSource item in _source)
                {
                    if (_predicate(item))
                    {
                        checked { count++; }
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// An iterator that filters, then maps, each item of an <see cref="IEnumerable{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source enumerable.</typeparam>
        /// <typeparam name="TResult">The type of the mapped items.</typeparam>
        private sealed class IEnumerableWhereSelectIterator<TSource, TResult> : Iterator<TResult>
        {
            private readonly IEnumerable<TSource> _source;
            private readonly Func<TSource, bool> _predicate;
            private readonly Func<TSource, TResult> _selector;
            private IEnumerator<TSource>? _enumerator;

            public IEnumerableWhereSelectIterator(IEnumerable<TSource> source, Func<TSource, bool> predicate, Func<TSource, TResult> selector)
            {
                _source = source;
                _predicate = predicate;
                _selector = selector;
            }

            private protected override Iterator<TResult> Clone() =>
                new IEnumerableWhereSelectIterator<TSource, TResult>(_source, _predicate, _selector);

            public override void Dispose()
            {
                if (_enumerator is not null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                base.Dispose();
            }

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        while (_enumerator.MoveNext())
                        {
                            TSource item = _enumerator.Current;
                            if (_predicate(item))
                            {
                                _current = _selector(item);
                                return true;
                            }
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new IEnumerableWhereSelectIterator<TSource, TResult2>(_source, _predicate, CombineSelectors(_selector, selector));

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of
                // the selector, run it provided `onlyIfCheap` is false.

                if (onlyIfCheap)
                {
                    return -1;
                }

                int count = 0;

                foreach (TSource item in _source)
                {
                    if (_predicate(item))
                    {
                        _selector(item);
                        checked
                        {
                            count++;
                        }
                    }
                }

                return count;
            }

            public override TResult[] ToArray()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                Func<TSource, bool> predicate = _predicate;
                Func<TSource, TResult> selector = _selector;
                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        builder.Add(selector(item));
                    }
                }

                TResult[] result = builder.ToArray();
                builder.Dispose();

                return result;
            }

            public override List<TResult> ToList()
            {
                SegmentedArrayBuilder<TResult>.ScratchBuffer scratch = default;
                SegmentedArrayBuilder<TResult> builder = new(scratch);

                Func<TSource, bool> predicate = _predicate;
                Func<TSource, TResult> selector = _selector;
                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        builder.Add(selector(item));
                    }
                }

                List<TResult> result = builder.ToList();
                builder.Dispose();

                return result;
            }

            public override TResult? TryGetFirst(out bool found)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item))
                    {
                        found = true;
                        return _selector(item);
                    }
                }

                found = false;
                return default;
            }

            public override TResult? TryGetLast(out bool found)
            {
                using IEnumerator<TSource> e = _source.GetEnumerator();

                if (e.MoveNext())
                {
                    Func<TSource, bool> predicate = _predicate;
                    TSource? last = default;
                    do
                    {
                        TSource current = e.Current;
                        if (predicate(current))
                        {
                            last = current;
                            found = true;

                            while (e.MoveNext())
                            {
                                current = e.Current;
                                if (predicate(current))
                                {
                                    last = current;
                                }
                            }

                            return _selector(last);
                        }
                    }
                    while (e.MoveNext());
                }

                found = false;
                return default;
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if (index >= 0)
                {
                    Func<TSource, bool> predicate = _predicate;

                    foreach (TSource item in _source)
                    {
                        if (predicate(item))
                        {
                            if (index == 0)
                            {
                                found = true;
                                return _selector(item);
                            }

                            index--;
                        }
                    }
                }

                found = false;
                return default;
            }

            public override bool Contains(TResult value)
            {
                Func<TSource, bool> predicate = _predicate;

                foreach (TSource item in _source)
                {
                    if (predicate(item) && EqualityComparer<TResult>.Default.Equals(_selector(item), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }


        /// <summary>
        /// An iterator that yields the items of part of an <see cref="IEnumerable{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source enumerable.</typeparam>
        private sealed class IEnumerableSkipTakeIterator<TSource> : Iterator<TSource>
        {
            private readonly IEnumerable<TSource> _source;
            private readonly int _minIndexInclusive;
            private readonly int _maxIndexInclusive; // -1 if we want everything past _minIndexInclusive.
                                                     // If this is -1, it's impossible to set a limit on the count.
            private IEnumerator<TSource>? _enumerator;

            internal IEnumerableSkipTakeIterator(IEnumerable<TSource> source, int minIndexInclusive, int maxIndexInclusive)
            {
                _source = source;
                _minIndexInclusive = minIndexInclusive;
                _maxIndexInclusive = maxIndexInclusive;
            }

            // If this is true (e.g. at least one Take call was made), then we have an upper bound
            // on how many elements we can have.
            private bool HasLimit => _maxIndexInclusive != -1;

            private int Limit => _maxIndexInclusive + 1 - _minIndexInclusive; // This is that upper bound.

            private protected override Iterator<TSource> Clone() =>
                new IEnumerableSkipTakeIterator<TSource>(_source, _minIndexInclusive, _maxIndexInclusive);

            public override void Dispose()
            {
                if (_enumerator is not null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                base.Dispose();
            }

            public override int GetCount(bool onlyIfCheap)
            {
                if (onlyIfCheap)
                {
                    return -1;
                }

                if (!HasLimit)
                {
                    // If HasLimit is false, we contain everything past _minIndexInclusive.
                    // Therefore, we have to iterate the whole enumerable.
                    return Math.Max(_source.Count() - _minIndexInclusive, 0);
                }

                using IEnumerator<TSource> en = _source.GetEnumerator();
                // We only want to iterate up to _maxIndexInclusive + 1.
                // Past that, we know the enumerable will be able to fit this subset,
                // so the count will just be _maxIndexInclusive + 1 - _minIndexInclusive.

                // Note that it is possible for _maxIndexInclusive to be int.MaxValue here,
                // so + 1 may result in signed integer overflow. We need to handle this.
                // At the same time, however, we are guaranteed that our max count can fit
                // in an int because if that is true, then _minIndexInclusive must > 0.

                uint count = SkipAndCount((uint)_maxIndexInclusive + 1, en);
                return Math.Max((int)count - _minIndexInclusive, 0);
            }

            public override bool MoveNext()
            {
                // Cases where GetEnumerator has not been called or Dispose has already
                // been called need to be handled explicitly, due to the default: clause.
                int taken = _state - 3;
                if (taken < -2)
                {
                    Dispose();
                    return false;
                }

                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (!SkipBeforeFirst(_enumerator))
                        {
                            // Reached the end before we finished skipping.
                            break;
                        }

                        _state = 3;
                        goto default;
                    default:
                        if ((!HasLimit || taken < Limit) && _enumerator.MoveNext())
                        {
                            if (HasLimit)
                            {
                                // If we are taking an unknown number of elements, it's important not to increment _state.
                                // _state - 3 may eventually end up overflowing & we'll hit the Dispose branch even though
                                // we haven't finished enumerating.
                                _state++;
                            }
                            _current = _enumerator.Current;
                            return true;
                        }

                        break;
                }

                Dispose();
                return false;
            }

            public override Iterator<TSource>? Skip(int count)
            {
                int minIndex = _minIndexInclusive + count;

                if (!HasLimit)
                {
                    if (minIndex < 0)
                    {
                        // If we don't know our max count and minIndex can no longer fit in a positive int,
                        // then we will need to wrap ourselves in another iterator.
                        // This can happen, for example, during e.Skip(int.MaxValue).Skip(int.MaxValue).
                        return new IEnumerableSkipTakeIterator<TSource>(this, count, -1);
                    }
                }
                else if ((uint)minIndex > (uint)_maxIndexInclusive)
                {
                    // If minIndex overflows and we have an upper bound, we will go down this branch.
                    // We know our upper bound must be smaller than minIndex, since our upper bound fits in an int.
                    // This branch should not be taken if we don't have a bound.
                    return null;
                }

                return new IEnumerableSkipTakeIterator<TSource>(_source, minIndex, _maxIndexInclusive);
            }

            public override Iterator<TSource> Take(int count)
            {
                int maxIndex = _minIndexInclusive + count - 1;
                if (!HasLimit)
                {
                    if (maxIndex < 0)
                    {
                        // If we don't know our max count and maxIndex can no longer fit in a positive int,
                        // then we will need to wrap ourselves in another iterator.
                        // Note that although maxIndex may be too large, the difference between it and
                        // _minIndexInclusive (which is count - 1) must fit in an int.
                        // Example: e.Skip(50).Take(int.MaxValue).

                        return new IEnumerableSkipTakeIterator<TSource>(this, 0, count - 1);
                    }
                }
                else if ((uint)maxIndex >= (uint)_maxIndexInclusive)
                {
                    // If we don't know our max count, we can't go down this branch.
                    // It's always possible for us to contain more than count items, as the rest
                    // of the enumerable past _minIndexInclusive can be arbitrarily long.
                    return this;
                }

                return new IEnumerableSkipTakeIterator<TSource>(_source, _minIndexInclusive, maxIndex);
            }

            public override TSource? TryGetElementAt(int index, out bool found)
            {
                // If the index is negative or >= our max count, return early.
                if (index >= 0 && (!HasLimit || index < Limit))
                {
                    if (_source is Iterator<TSource> iterator)
                    {
                        return iterator.TryGetElementAt(_minIndexInclusive + index, out found);
                    }

                    using IEnumerator<TSource> en = _source.GetEnumerator();
                    if (SkipBefore(_minIndexInclusive + index, en) && en.MoveNext())
                    {
                        found = true;
                        return en.Current;
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetFirst(out bool found)
            {
                if (_source is Iterator<TSource> iterator)
                {
                    return iterator.TryGetElementAt(_minIndexInclusive, out found);
                }

                using (IEnumerator<TSource> en = _source.GetEnumerator())
                {
                    if (SkipBeforeFirst(en) && en.MoveNext())
                    {
                        found = true;
                        return en.Current;
                    }
                }

                found = false;
                return default;
            }

            public override TSource? TryGetLast(out bool found)
            {
                if (_source is Iterator<TSource> iterator &&
                    iterator.GetCount(onlyIfCheap: true) is int count &&
                    count >= 0)
                {
                    if (count <= _minIndexInclusive)
                    {
                        found = false;
                        return default;
                    }

                    // If there's no upper bound, or if there are fewer items in the list
                    // than the upper bound allows, just return the last element of the list.
                    // Otherwise, get the element at the upper bound.
                    return (uint)count <= (uint)_maxIndexInclusive ?
                        iterator.TryGetLast(out found) :
                        iterator.TryGetElementAt(_maxIndexInclusive, out found);
                }

                using (IEnumerator<TSource> en = _source.GetEnumerator())
                {
                    if (SkipBeforeFirst(en) && en.MoveNext())
                    {
                        int remaining = Limit - 1; // Max number of items left, not counting the current element.
                        int comparand = HasLimit ? 0 : int.MinValue; // If we don't have an upper bound, have the comparison always return true.
                        TSource result;

                        do
                        {
                            remaining--;
                            result = en.Current;
                        }
                        while (remaining >= comparand && en.MoveNext());

                        found = true;
                        return result;
                    }
                }

                found = false;
                return default;
            }

            public override TSource[] ToArray()
            {
                using (IEnumerator<TSource> en = _source.GetEnumerator())
                {
                    if (SkipBeforeFirst(en) && en.MoveNext())
                    {
                        int remaining = Limit - 1; // Max number of items left, not counting the current element.
                        int comparand = HasLimit ? 0 : int.MinValue; // If we don't have an upper bound, have the comparison always return true.

                        SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                        SegmentedArrayBuilder<TSource> builder = new(scratch);
                        do
                        {
                            remaining--;
                            builder.Add(en.Current);
                        }
                        while (remaining >= comparand && en.MoveNext());

                        TSource[] result = builder.ToArray();
                        builder.Dispose();

                        return result;
                    }
                }

                return [];
            }

            public override List<TSource> ToList()
            {
                using (IEnumerator<TSource> en = _source.GetEnumerator())
                {
                    if (SkipBeforeFirst(en) && en.MoveNext())
                    {
                        int remaining = Limit - 1; // Max number of items left, not counting the current element.
                        int comparand = HasLimit ? 0 : int.MinValue; // If we don't have an upper bound, have the comparison always return true.

                        SegmentedArrayBuilder<TSource>.ScratchBuffer scratch = default;
                        SegmentedArrayBuilder<TSource> builder = new(scratch);
                        do
                        {
                            remaining--;
                            builder.Add(en.Current);
                        }
                        while (remaining >= comparand && en.MoveNext());

                        List<TSource> result = builder.ToList();
                        builder.Dispose();

                        return result;
                    }
                }

                return [];
            }

            private bool SkipBeforeFirst(IEnumerator<TSource> en) => SkipBefore(_minIndexInclusive, en);

            private static bool SkipBefore(int index, IEnumerator<TSource> en) => SkipAndCount(index, en) == index;

            private static int SkipAndCount(int index, IEnumerator<TSource> en)
            {
                return (int)SkipAndCount((uint)index, en);
            }

            private static uint SkipAndCount(uint index, IEnumerator<TSource> en)
            {
                for (uint i = 0; i < index; i++)
                {
                    if (!en.MoveNext())
                    {
                        return i;
                    }
                }

                return index;
            }
        }


        /// <summary>
        /// An iterator that yields a range of consecutive integers.
        /// </summary>
        [DebuggerDisplay("Count = {CountForDebugger}")]
        private sealed class RangeIterator<T> : Iterator<T>, IList<T>, IReadOnlyList<T> where T : INumber<T>
        {
            private readonly T _start;
            private readonly T _endExclusive;

            public RangeIterator(T start, T endExclusive)
            {
                _start = start;
                _endExclusive = endExclusive;
            }

            private int CountForDebugger => int.CreateTruncating(_endExclusive - _start);

            private protected override Iterator<T> Clone() => new RangeIterator<T>(_start, _endExclusive);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _current = _start;
                        _state = 2;
                        return true;

                    case 2:
                        if (++_current == _endExclusive)
                        {
                            break;
                        }

                        return true;
                }

                _state = -1;
                return false;
            }

            public override void Dispose()
            {
                _state = -1; // Don't reset current
            }

            public override IEnumerable<TResult> Select<TResult>(Func<T, TResult> selector)
            {
                return new RangeSelectIterator<T, TResult>(_start, _endExclusive, selector);
            }

            public override T[] ToArray()
            {
                T start = _start;
                T[] array = new T[Count];
                FillIncrementing(array, start);
                return array;
            }

            public override List<T> ToList()
            {
                (T start, T end) = (_start, _endExclusive);
                int count = int.CreateTruncating(end - start);
                List<T> list = new List<T>(count);
                FillIncrementing(SetCountAndGetSpan(list, count), start);
                return list;
            }

            public void CopyTo(T[] array, int arrayIndex) =>
                FillIncrementing(array.AsSpan(arrayIndex, Count), _start);

            public override int GetCount(bool onlyIfCheap) => Count;

            public int Count => int.CreateTruncating(_endExclusive - _start);

            public override Iterator<T>? Skip(int count) =>
                count >= Count ? null :
                new RangeIterator<T>(_start + T.CreateTruncating(count), _endExclusive);

            public override Iterator<T> Take(int count) =>
                count >= Count ? this :
                new RangeIterator<T>(_start, _start + T.CreateTruncating(count));

            public override T TryGetElementAt(int index, out bool found)
            {
                if ((uint)index < (uint)Count)
                {
                    found = true;
                    return _start + T.CreateTruncating(index);
                }

                found = false;
                return T.Zero;
            }

            public override T TryGetFirst(out bool found)
            {
                found = true;
                return _start;
            }

            public override T TryGetLast(out bool found)
            {
                found = true;
                return _endExclusive - T.One;
            }

            public override bool Contains(T item) =>
                uint.CreateTruncating(item - _start) < (uint)Count;

            public int IndexOf(T item)
            {
                uint index = uint.CreateTruncating(item - _start);
                if (index < (uint)Count)
                {
                    return (int)index;
                }

                return -1;
            }

            public T this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)Count)
                    {
                        ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index);
                    }

                    return _start + T.CreateTruncating(index);
                }
                set => ThrowHelper.ThrowNotSupportedException();
            }

            public bool IsReadOnly => true;

            void ICollection<T>.Add(T item) => ThrowHelper.ThrowNotSupportedException();
            void ICollection<T>.Clear() => ThrowHelper.ThrowNotSupportedException();
            void IList<T>.Insert(int index, T item) => ThrowHelper.ThrowNotSupportedException();
            bool ICollection<T>.Remove(T item) => ThrowHelper.ThrowNotSupportedException_Boolean();
            void IList<T>.RemoveAt(int index) => ThrowHelper.ThrowNotSupportedException();
        }

        private sealed class RangeSelectIterator<T, TResult> : Iterator<TResult> where T : INumber<T>
        {
            private readonly T _start;
            private readonly T _end;
            private readonly Func<T, TResult> _selector;

            public RangeSelectIterator(T start, T end, Func<T, TResult> selector)
            {
                _start = start;
                _end = end;
                _selector = selector;
            }

            private int Count => int.CreateTruncating(_end - _start);

            private protected override Iterator<TResult> Clone() =>
                new RangeSelectIterator<T, TResult>(_start, _end, _selector);

            public override bool MoveNext()
            {
                if (_state < 1 || _state == (Count + 1))
                {
                    Dispose();
                    return false;
                }

                int index = _state++ - 1;
                _current = _selector(_start + T.CreateTruncating(index));
                return true;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector) =>
                new RangeSelectIterator<T, TResult2>(_start, _end, CombineSelectors(_selector, selector));

            public override TResult[] ToArray()
            {
                var results = new TResult[Count];
                Fill(results, _start, _selector);

                return results;
            }

            public override List<TResult> ToList()
            {
                int count = Count;
                var results = new List<TResult>(count);
                Fill(SetCountAndGetSpan(results, count), _start, _selector);

                return results;
            }

            private static void Fill(Span<TResult> results, T start, Func<T, TResult> func)
            {
                for (int i = 0; i < results.Length; i++, start++)
                {
                    results[i] = func(start);
                }
            }

            public override int GetCount(bool onlyIfCheap)
            {
                // In case someone uses Count() to force evaluation of the selector,
                // run it provided `onlyIfCheap` is false.
                if (!onlyIfCheap)
                {
                    for (T i = _start; i != _end; i++)
                    {
                        _selector(i);
                    }
                }

                return Count;
            }

            public override Iterator<TResult>? Skip(int count)
            {
                if (count >= Count)
                {
                    return null;
                }

                return new RangeSelectIterator<T, TResult>(_start + T.CreateTruncating(count), _end, _selector);
            }

            public override Iterator<TResult> Take(int count)
            {
                if (count >= Count)
                {
                    return this;
                }

                return new RangeSelectIterator<T, TResult>(_start, _start + T.CreateTruncating(count), _selector);
            }

            public override TResult? TryGetElementAt(int index, out bool found)
            {
                if ((uint)index < (uint)Count)
                {
                    found = true;
                    return _selector(_start + T.CreateTruncating(index));
                }

                found = false;
                return default;
            }

            public override TResult TryGetFirst(out bool found)
            {
                found = true;
                return _selector(_start);
            }

            public override TResult TryGetLast(out bool found)
            {
                found = true;
                return _selector(_end - T.One);
            }

            public override bool Contains(TResult value)
            {
                for (T i = _start; i != _end; i++)
                {
                    if (EqualityComparer<TResult>.Default.Equals(_selector(i), value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }


    }
}
