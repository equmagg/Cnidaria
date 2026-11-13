using Internal;
using Internal.Runtime;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace System.Runtime.CompilerServices
{
    public sealed class ConditionalWeakTable<TKey, 
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : class
        where TValue : class?
    {
        // Lifetimes of keys and values:
        // Inserting a key and value into the dictionary will not
        // prevent the key from dying, even if the key is strongly reachable
        // from the value. Once the key dies, the dictionary automatically removes
        // the key/value entry.
        //
        // Thread safety guarantees:
        // ConditionalWeakTable is fully thread-safe and requires no
        // additional locking to be done by callers.
        //
        // OOM guarantees:
        // Will not corrupt unmanaged handle table on OOM. No guarantees
        // about managed weak table consistency. Native handles reclamation
        // may be delayed until appdomain shutdown.

        private const int InitialCapacity = 8;  // Initial length of the table. Must be a power of two.
        private readonly object _lock;          // This lock protects all mutation of data in the table.  Readers do not take this lock.
        private volatile Container _container;  // The actual storage for the table; swapped out as the table grows. [cDAC] [ConditionalWeakTable] :
                                                // Contract depends on the exact names of this field and its type.
        private int _activeEnumeratorRefCount;  // The number of outstanding enumerators on the table

        public ConditionalWeakTable()
        {
            _lock = new object();
            _container = new Container(this);
        }

        /// <summary>Gets the value of the specified key.</summary>
        /// <param name="key">key of the value to find. Cannot be null.</param>
        /// <param name="value">
        /// If the key is found, contains the value associated with the key upon method return.
        /// If the key is not found, contains default(TValue).
        /// </param>
        /// <returns>Returns "true" if key was found, "false" otherwise.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            return _container.TryGetValueWorker(key, out value);
        }

        /// <summary>Adds a key to the table.</summary>
        /// <param name="key">key to add. May not be null.</param>
        /// <param name="value">value to associate with key.</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is already entered into the dictionary.</exception>
        public void Add(TKey key, TValue value)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            lock (_lock)
            {
                int entryIndex = _container.FindEntry(key, out _);
                if (entryIndex != -1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_AddingDuplicate);
                }

                CreateEntry(key, value);
            }
        }

        /// <summary>Adds a key to the table if it doesn't already exist.</summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The key's property value.</param>
        /// <returns>true if the key/value pair was added; false if the table already contained the key.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        public bool TryAdd(TKey key, TValue value)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            lock (_lock)
            {
                int entryIndex = _container.FindEntry(key, out _);
                if (entryIndex != -1)
                {
                    return false;
                }

                CreateEntry(key, value);
                return true;
            }
        }

        /// <summary>Adds the key and value if the key doesn't exist, or updates the existing key's value if it does exist.</summary>
        /// <param name="key">key to add or update. May not be null.</param>
        /// <param name="value">value to associate with key.</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        public void AddOrUpdate(TKey key, TValue value)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            lock (_lock)
            {
                int entryIndex = _container.FindEntry(key, out _);

                // if we found a key we should just update, if no we should create a new entry.
                if (entryIndex != -1)
                {
                    _container.UpdateValue(entryIndex, value);
                }
                else
                {
                    CreateEntry(key, value);
                }
            }
        }

        /// <summary>Removes a key and its value from the table.</summary>
        /// <param name="key">The key to remove.</param>
        /// <returns><see langword="true"/> if the key is found and removed; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        public bool Remove(TKey key)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            lock (_lock)
            {
                return _container.Remove(key, out _);
            }
        }

        /// <summary>Removes a key and its value from the table, and returns the removed value if it was present.</summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="value">value removed from the table, if it was present.</param>
        /// <returns><see langword="true"/> if the key is found and removed; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (key is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }

            lock (_lock)
            {
                return _container.Remove(key, out value);
            }
        }

        /// <summary>Clear all the key/value pairs</summary>
        public void Clear()
        {
            lock (_lock)
            {
                // To clear, we would prefer to simply drop the existing container
                // and replace it with an empty one, as that's overall more efficient.
                // However, if there are any active enumerators, we don't want to do
                // that as it will end up removing all of the existing entries and
                // allowing new items to be added at the same indices when the container
                // is filled and replaced, and one of the guarantees we try to make with
                // enumeration is that new items added after enumeration starts won't be
                // included in the enumeration. As such, if there are active enumerators,
                // we simply use the container's removal functionality to remove all of the
                // keys; then when the table is resized, if there are still active enumerators,
                // these empty slots will be maintained.
                if (_activeEnumeratorRefCount > 0)
                {
                    _container.RemoveAllKeys();
                }
                else
                {
                    _container = new Container(this);
                }
            }
        }

        /// <summary>
        /// Searches for a specified key in the table and returns the corresponding value. If the key does
        /// not exist in the table, the method adds the given value and binds it to the specified key.
        /// </summary>
        /// <param name="key">The key of the value to find. It cannot be <see langword="null"/>.</param>
        /// <param name="value">The value to add and bind to <typeparamref name="TKey"/>, if one does not exist already.</param>
        /// <returns>The value bound to <typeparamref name="TKey"/> in the current <see cref="ConditionalWeakTable{TKey, TValue}"/> instance, after the method completes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        public TValue GetOrAdd(TKey key, TValue value)
        {
            // key is validated by TryGetValue
            if (TryGetValue(key, out TValue? existingValue))
            {
                return existingValue;
            }

            return GetOrAddLocked(key, value);
        }

        /// <summary>
        /// Searches for a specified key in the table and returns the corresponding value. If the key does not exist
        /// in the table, the method invokes the supplied factory to create a value that is bound to the specified key.
        /// </summary>
        /// <param name="key">The key of the value to find. It cannot be <see langword="null"/>.</param>
        /// <param name="valueFactory">The callback that creates a value for key, if one does not exist already. It cannot be <see langword="null"/>.</param>
        /// <returns>The value bound to <typeparamref name="TKey"/> in the current <see cref="ConditionalWeakTable{TKey, TValue}"/> instance, after the method completes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="valueFactory"/> are <see langword="null"/>.</exception>
        /// <remarks>
        /// If multiple threads try to initialize the same key, the table may invoke <paramref name="valueFactory"/> multiple times
        /// with the same key. Exactly one of these calls will succeed and the returned value of that call will be the one added to
        /// the table and returned by all the racing <see cref="GetOrAdd(TKey, Func{TKey, TValue})"/> calls. This rule permits the
        /// table to invoke <paramref name="valueFactory"/> outside the internal table lock, to prevent deadlocks.
        /// </remarks>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            ArgumentNullException.ThrowIfNull(valueFactory);

            // key is validated by TryGetValue
            if (TryGetValue(key, out TValue? existingValue))
            {
                return existingValue;
            }

            // create the value outside of the lock
            TValue value = valueFactory(key);

            return GetOrAddLocked(key, value);
        }

        /// <summary>
        /// Searches for a specified key in the table and returns the corresponding value. If the key does not exist
        /// in the table, the method invokes the supplied factory to create a value that is bound to the specified key.
        /// </summary>
        /// <typeparam name="TArg">The type of the additional argument to use with the value factory.</typeparam>
        /// <param name="key">The key of the value to find. It cannot be <see langword="null"/>.</param>
        /// <param name="valueFactory">The callback that creates a value for key, if one does not exist already. It cannot be <see langword="null"/>.</param>
        /// <param name="factoryArgument">The additional argument to supply to <paramref name="valueFactory"/> upon invocation.</param>
        /// <returns>The value bound to <typeparamref name="TKey"/> in the current <see cref="ConditionalWeakTable{TKey, TValue}"/> instance, after the method completes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="valueFactory"/> are <see langword="null"/>.</exception>
        /// <remarks>
        /// If multiple threads try to initialize the same key, the table may invoke <paramref name="valueFactory"/> multiple times with the
        /// same key. Exactly one of these calls will succeed and the returned value of that call will be the one added to the table and
        /// returned by all the racing <see cref="GetOrAdd{TArg}(TKey, Func{TKey, TArg, TValue}, TArg)"/> calls. This rule permits the
        /// table to invoke <paramref name="valueFactory"/> outside the internal table lock, to prevent deadlocks.
        /// </remarks>
        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
            where TArg : allows ref struct
        {
            ArgumentNullException.ThrowIfNull(valueFactory);

            // key is validated by TryGetValue
            if (TryGetValue(key, out TValue? existingValue))
            {
                return existingValue;
            }

            // create the value outside of the lock
            TValue value = valueFactory(key, factoryArgument);

            return GetOrAddLocked(key, value);
        }

        /// <summary>
        /// Searches for a specified key in the table and returns the corresponding value. If the key does not exist
        /// in the table, the method invokes a callback method to create a value that is bound to the specified key.
        /// </summary>
        /// <param name="key">key of the value to find. Cannot be null.</param>
        /// <param name="createValueCallback">callback that creates value for key. Cannot be null.</param>
        /// <returns></returns>
        /// <remarks>
        /// <para>
        /// If multiple threads try to initialize the same key, the table may invoke createValueCallback
        /// multiple times with the same key. Exactly one of these calls will succeed and the returned
        /// value of that call will be the one added to the table and returned by all the racing GetValue() calls.
        /// This rule permits the table to invoke createValueCallback outside the internal table lock
        /// to prevent deadlocks.
        /// </para>
        /// <para>
        /// Consider using <see cref="GetOrAdd(TKey, Func{TKey, TValue})"/> (or one of its overloads) instead.
        /// </para>
        /// </remarks>
        public TValue GetValue(TKey key, CreateValueCallback createValueCallback)
        {
            ArgumentNullException.ThrowIfNull(createValueCallback);

            // key is validated by TryGetValue
            if (TryGetValue(key, out TValue? existingValue))
            {
                return existingValue;
            }

            // create the value outside of the lock
            TValue value = createValueCallback(key);

            return GetOrAddLocked(key, value);
        }

        private TValue GetOrAddLocked(TKey key, TValue value)
        {
            lock (_lock)
            {
                // Now that we've taken the lock, must recheck in case we lost a race to add the key.
                if (_container.TryGetValueWorker(key, out TValue? existingValue))
                {
                    return existingValue;
                }
                else
                {
                    // Verified in-lock that we won the race to add the key. Add it now.
                    CreateEntry(key, value);
                    return value;
                }
            }
        }

        /// <summary>
        /// Helper method to call GetValue without passing a creation delegate.  Uses Activator.CreateInstance
        /// to create new instances as needed.  If TValue does not have a default constructor, this will throw.
        /// </summary>
        /// <param name="key">key of the value to find. Cannot be null.</param>
        /// <remarks>
        /// Consider using <see cref="GetOrAdd(TKey, Func{TKey, TValue})"/> (or one of its overloads) instead.
        /// </remarks>
        public TValue GetOrCreateValue(TKey key) => GetValue(key, static _ => Activator.CreateInstance<TValue>());

        public delegate TValue CreateValueCallback(TKey key);

        /// <summary>Gets an enumerator for the table.</summary>
        /// <remarks>
        /// The returned enumerator will not extend the lifetime of
        /// any object pairs in the table, other than the one that's Current.  It will not return entries
        /// that have already been collected, nor will it return entries added after the enumerator was
        /// retrieved.  It may not return all entries that were present when the enumerat was retrieved,
        /// however, such as not returning entries that were collected or removed after the enumerator
        /// was retrieved but before they were enumerated.
        /// </remarks>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            lock (_lock)
            {
                Container c = _container;
                return c is null || c.FirstFreeEntry == 0 ?
                    GenericEmptyEnumerator<KeyValuePair<TKey, TValue>>.Instance :
                    new Enumerator(this);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<TKey, TValue>>)this).GetEnumerator();

        /// <summary>Provides an enumerator for the table.</summary>
        private sealed class Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            // The enumerator would ideally hold a reference to the Container and the end index within that
            // container.  However, the safety of the CWT depends on the only reference to the Container being
            // from the CWT itself; the Container then employs a two-phase finalization scheme, where the first
            // phase nulls out that parent CWT's reference, guaranteeing that the second time it's finalized there
            // can be no other existing references to it in use that would allow for concurrent usage of the
            // native handles with finalization.  We would break that if we allowed this Enumerator to hold a
            // reference to the Container.  Instead, the Enumerator holds a reference to the CWT rather than to
            // the Container, and it maintains the CWT._activeEnumeratorRefCount field to track whether there
            // are outstanding enumerators that have yet to be disposed/finalized.  If there aren't any, the CWT
            // behaves as it normally does.  If there are, certain operations are affected, in particular resizes.
            // Normally when the CWT is resized, it enumerates the contents of the table looking for indices that
            // contain entries which have been collected or removed, and it frees those up, effectively moving
            // down all subsequent entries in the container (not in the existing container, but in a replacement).
            // This, however, would cause the enumerator's understanding of indices to break.  So, as long as
            // there is any outstanding enumerator, no compaction is performed.

            private ConditionalWeakTable<TKey, TValue>? _table; // parent table, set to null when disposed
            private readonly int _maxIndexInclusive;            // last index in the container that should be enumerated
            private int _currentIndex;                          // the current index into the container
            private KeyValuePair<TKey, TValue> _current;        // the current entry set by MoveNext and returned from Current

            public Enumerator(ConditionalWeakTable<TKey, TValue> table)
            {
                // Store a reference to the parent table and increase its active enumerator count.
                _table = table;
                table._activeEnumeratorRefCount++;

                // Store the max index to be enumerated.
                _maxIndexInclusive = table._container.FirstFreeEntry - 1;
                _currentIndex = -1;
            }

            ~Enumerator()
            {
                Dispose();
            }

            public void Dispose()
            {
                // Use an interlocked operation to ensure that only one thread can get access to
                // the _table for disposal and thus only decrement the ref count once.
                ConditionalWeakTable<TKey, TValue>? table = Interlocked.Exchange(ref _table, null);
                if (table != null)
                {
                    // Ensure we don't keep the last current alive unnecessarily
                    _current = default;

                    // Decrement the ref count that was incremented when constructed
                    lock (table._lock)
                    {
                        table._activeEnumeratorRefCount--;
                    }

                    // Finalization is purely to decrement the ref count.  We can suppress it now.
                    GC.SuppressFinalize(this);
                }
            }

            public bool MoveNext()
            {
                // Start by getting the current table.  If it's already been disposed, it will be null.
                ConditionalWeakTable<TKey, TValue>? table = _table;
                if (table != null)
                {
                    // Once have the table, we need to lock to synchronize with other operations on
                    // the table, like adding.
                    lock (table._lock)
                    {
                        // From the table, we have to get the current container.  This could have changed
                        // since we grabbed the enumerator, but the index-to-pair mapping should not have
                        // due to there being at least one active enumerator.  If the table (or rather its
                        // container at the time) has already been finalized, this will be null.
                        Container c = table._container;
                        if (c != null)
                        {
                            // We have the container.  Find the next entry to return, if there is one.
                            // We need to loop as we may try to get an entry that's already been removed
                            // or collected, in which case we try again.
                            while (_currentIndex < _maxIndexInclusive)
                            {
                                _currentIndex++;
                                if (c.TryGetEntry(_currentIndex, out TKey? key, out TValue? value))
                                {
                                    _current = new KeyValuePair<TKey, TValue>(key, value);
                                    return true;
                                }
                            }
                        }
                    }
                }

                // Nothing more to enumerate.
                return false;
            }

            public KeyValuePair<TKey, TValue> Current
            {
                get
                {
                    if (_currentIndex < 0)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    return _current;
                }
            }

            object? IEnumerator.Current => Current;

            public void Reset() { }
        }

        /// <summary>Worker for adding a new key/value pair. Will resize the container if it is full.</summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        private void CreateEntry(TKey key, TValue value)
        {
            Container c = _container;
            if (!c.HasCapacity)
                _container = c = c.Resize();
            c.CreateEntryNoResize(key, value);
        }

        [StructLayout(LayoutKind.Auto)]
        private struct Entry
        {
            public DependentHandle depHnd;
            public int HashCode;    // Cached copy of key's hashcode
            public int Next;        // Index of next entry, -1 if last
        }

        /// <summary>
        /// Container holds the actual data for the table.  A given instance of Container always has the same capacity.  When we need
        /// more capacity, we create a new Container, copy the old one into the new one, and discard the old one.  This helps enable lock-free
        /// reads from the table, as readers never need to deal with motion of entries due to rehashing.
        /// </summary>
        private sealed class Container
        {
            private readonly ConditionalWeakTable<TKey, TValue> _parent;  // the ConditionalWeakTable with which this container is associated
            private int[] _buckets;                // _buckets[hashcode & (_buckets.Length - 1)] contains index of the first entry in bucket (-1 if empty).
                                                   // [cDAC] [ConditionalWeakTable] : Contract depends on this exact name
            private Entry[] _entries;              // the table entries containing the stored dependency handles. [cDAC] [ConditionalWeakTable] :
                                                   // Contract depends on this exact name
            private int _firstFreeEntry;           // _firstFreeEntry < _entries.Length => table has capacity,  entries grow from the bottom of the table.
            private bool _invalid;                 // flag detects if OOM or other background exception threw us out of the lock.
            private bool _finalized;               // set to true when initially finalized
            private volatile object? _oldKeepAlive; // used to ensure the next allocated container isn't finalized until this one is GC'd

            internal Container(ConditionalWeakTable<TKey, TValue> parent)
            {
                const int Size = InitialCapacity;
                _buckets = new int[Size];
                for (int i = 0; i < _buckets.Length; i++)
                {
                    _buckets[i] = -1;
                }
                _entries = new Entry[Size];

                // Only store the parent after all of the allocations have happened successfully.
                // Otherwise, as part of growing or clearing the container, we could end up allocating
                // a new Container that fails (OOMs) part way through construction but that gets finalized
                // and ends up clearing out some other container present in the associated CWT.
                _parent = parent;
            }

            private Container(ConditionalWeakTable<TKey, TValue> parent, int[] buckets, Entry[] entries, int firstFreeEntry)
            {
                _parent = parent;
                _buckets = buckets;
                _entries = entries;
                _firstFreeEntry = firstFreeEntry;
            }

            internal bool HasCapacity => _firstFreeEntry < _entries.Length;

            internal int FirstFreeEntry => _firstFreeEntry;

            /// <summary>Worker for adding a new key/value pair. Container must NOT be full.</summary>
            internal void CreateEntryNoResize(TKey key, TValue value)
            {
                VerifyIntegrity();
                _invalid = true;

                int hashCode = RuntimeHelpers.GetHashCode(key) & int.MaxValue;
                int newEntry = _firstFreeEntry++;

                _entries[newEntry].HashCode = hashCode;
                _entries[newEntry].depHnd = new DependentHandle(key, value);
                int bucket = hashCode & (_buckets.Length - 1);
                _entries[newEntry].Next = _buckets[bucket];

                // This write must be volatile, as we may be racing with concurrent readers.  If they see
                // the new entry, they must also see all of the writes earlier in this method.
                Volatile.Write(ref _buckets[bucket], newEntry);

                _invalid = false;
            }

            /// <summary>Worker for finding a key/value pair. Must hold _lock.</summary>
            internal bool TryGetValueWorker(TKey key, [MaybeNullWhen(false)] out TValue value)
            {
                int entryIndex = FindEntry(key, out object? secondary);
                value = Unsafe.As<TValue>(secondary);
                return entryIndex != -1;
            }

            /// <summary>
            /// Returns -1 if not found (if key expires during FindEntry, this can be treated as "not found.").
            /// Must hold _lock, or be prepared to retry the search while holding _lock.
            /// </summary>
            /// <remarks>This method requires <paramref name="value"/> to be on the stack to be properly tracked.</remarks>
            internal int FindEntry(TKey key, out object? value)
            {
                int hashCode = RuntimeHelpers.TryGetHashCode(key);

                if (hashCode == 0)
                {
                    // No hash code has been assigned to the key, so therefore it has not been added
                    // to any ConditionalWeakTable.
                    value = null;
                    return -1;
                }

                hashCode &= int.MaxValue;
                int bucket = hashCode & (_buckets.Length - 1);
                for (int entriesIndex = Volatile.Read(ref _buckets[bucket]); entriesIndex != -1; entriesIndex = _entries[entriesIndex].Next)
                {
                    if (_entries[entriesIndex].HashCode == hashCode && _entries[entriesIndex].depHnd.UnsafeGetTargetAndDependent(out value) == key)
                    {
                        GC.KeepAlive(this); // Ensure we don't get finalized while accessing DependentHandle

                        return entriesIndex;
                    }
                }

                GC.KeepAlive(this); // Ensure we don't get finalized while accessing DependentHandle
                value = null;
                return -1;
            }

            /// <summary>Gets the entry at the specified entry index.</summary>
            internal bool TryGetEntry(int index, [NotNullWhen(true)] out TKey? key, [MaybeNullWhen(false)] out TValue value)
            {
                if (index < _entries.Length)
                {
                    object? oKey = _entries[index].depHnd.UnsafeGetTargetAndDependent(out object? oValue);

                    GC.KeepAlive(this); // Ensure we don't get finalized while accessing DependentHandle

                    if (oKey != null)
                    {
                        key = Unsafe.As<TKey>(oKey);
                        value = Unsafe.As<TValue>(oValue!);
                        return true;
                    }
                }

                key = default;
                value = default;
                return false;
            }

            /// <summary>Removes all of the keys in the table.</summary>
            internal void RemoveAllKeys()
            {
                for (int i = 0; i < _firstFreeEntry; i++)
                {
                    RemoveIndex(i);
                }
            }

            /// <summary>Removes the specified key from the table, if it exists.</summary>
            internal bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
            {
                VerifyIntegrity();

                int entryIndex = FindEntry(key, out object? valueObject);
                if (entryIndex != -1)
                {
                    RemoveIndex(entryIndex);
                    value = Unsafe.As<TValue>(valueObject!);
                    return true;
                }

                value = null;
                return false;
            }

            // The handle stays allocated for readers racing with this, a resize or the finalizer frees it
            private void RemoveIndex(int entryIndex)
            {
                ref Entry entry = ref _entries[entryIndex];

                // We do not free the handle here, as we may be racing with readers who already saw the hash code.
                // Instead, we simply overwrite the entry's hash code, so subsequent reads will ignore it.
                // The handle will be free'd in Container's finalizer, after the table is resized or discarded.
                Volatile.Write(ref entry.HashCode, -1);

                // Also, clear the key to allow GC to collect objects pointed to by the entry
                entry.depHnd.UnsafeSetTargetToNull();

            }

            internal void UpdateValue(int entryIndex, TValue newValue)
            {
                VerifyIntegrity();
                _invalid = true;

                _entries[entryIndex].depHnd.UnsafeSetDependent(newValue);

                _invalid = false;
            }

            /// <summary>Resize, and scrub expired keys off bucket lists. Must hold _lock.</summary>
            /// <remarks>
            /// _firstEntry is less than _entries.Length on exit, that is, the table has at least one free entry.
            /// </remarks>
            internal Container Resize()
            {
                bool hasExpiredEntries = false;
                int newSize = _buckets.Length;

                if (_parent is null || _parent._activeEnumeratorRefCount == 0)
                {
                    // If any expired or removed keys exist, we won't resize.
                    // If there any active enumerators, though, we don't want
                    // to compact and thus have no expired entries.
                    for (int entriesIndex = 0; entriesIndex < _entries.Length; entriesIndex++)
                    {
                        ref Entry entry = ref _entries[entriesIndex];

                        if (entry.HashCode == -1)
                        {
                            // the entry was removed
                            hasExpiredEntries = true;
                            break;
                        }

                        if (entry.depHnd.IsAllocated && entry.depHnd.UnsafeGetTarget() is null)
                        {
                            // the entry has expired
                            hasExpiredEntries = true;
                            break;
                        }
                    }
                }

                if (!hasExpiredEntries)
                {
                    // Not necessary to check for overflow here, the attempt to allocate new arrays will throw
                    newSize = _buckets.Length * 2;
                }

                return Resize(newSize);
            }

            internal Container Resize(int newSize)
            {
                // Reallocate both buckets and entries and rebuild the bucket and entries from scratch.
                // This serves both to scrub entries with expired keys and to put the new entries in the proper bucket.
                int[] newBuckets = new int[newSize];
                for (int bucketIndex = 0; bucketIndex < newBuckets.Length; bucketIndex++)
                {
                    newBuckets[bucketIndex] = -1;
                }
                Entry[] newEntries = new Entry[newSize];
                int newEntriesIndex = 0;
                bool activeEnumerators = _parent != null && _parent._activeEnumeratorRefCount > 0;
                bool transferredHandles;

                // Migrate existing entries to the new table.
                if (activeEnumerators)
                {
                    transferredHandles = true;

                    // There's at least one active enumerator, which means we don't want to
                    // remove any expired/removed entries, in order to not affect existing
                    // entries indices.  Copy over the entries while rebuilding the buckets list,
                    // as the buckets are dependent on the buckets list length, which is changing.
                    for (; newEntriesIndex < _entries.Length; newEntriesIndex++)
                    {
                        ref Entry oldEntry = ref _entries[newEntriesIndex];
                        ref Entry newEntry = ref newEntries[newEntriesIndex];
                        int hashCode = oldEntry.HashCode;

                        newEntry.HashCode = hashCode;
                        newEntry.depHnd = oldEntry.depHnd;
                        int bucket = hashCode & (newBuckets.Length - 1);
                        newEntry.Next = newBuckets[bucket];
                        newBuckets[bucket] = newEntriesIndex;
                    }
                }
                else
                {
                    transferredHandles = false;

                    // There are no active enumerators, which means we want to compact by
                    // removing expired/removed entries.
                    for (int entriesIndex = 0; entriesIndex < _entries.Length; entriesIndex++)
                    {
                        ref Entry oldEntry = ref _entries[entriesIndex];
                        int hashCode = oldEntry.HashCode;
                        DependentHandle depHnd = oldEntry.depHnd;
                        if (hashCode != -1 && depHnd.IsAllocated)
                        {
                            if (depHnd.UnsafeGetTarget() is not null)
                            {
                                transferredHandles = true;

                                ref Entry newEntry = ref newEntries[newEntriesIndex];

                                // Entry is used and has not expired. Link it into the appropriate bucket list.
                                newEntry.HashCode = hashCode;
                                newEntry.depHnd = depHnd;
                                int bucket = hashCode & (newBuckets.Length - 1);
                                newEntry.Next = newBuckets[bucket];
                                newBuckets[bucket] = newEntriesIndex;
                                newEntriesIndex++;
                            }
                            else
                            {
                                // Pretend the item was removed, so that this container's finalizer
                                // will clean up this dependent handle.
                                Volatile.Write(ref oldEntry.HashCode, -1);
                            }
                        }
                    }
                }

                // Create the new container.  We want to transfer the responsibility of freeing the handles from
                // the old container to the new container, and also ensure that the new container isn't finalized
                // while the old container may still be in use.  As such, we store a reference from the old container
                // to the new one, which will keep the new container alive as long as the old one is.
                var newContainer = new Container(_parent!, newBuckets, newEntries, newEntriesIndex);
                if (activeEnumerators)
                {
                    // If there are active enumerators, both the old container and the new container may be storing
                    // the same entries with -1 hash codes, which the finalizer will clean up even if the container
                    // is not the active container for the table.  To prevent that, we want to stop the old container
                    // from being finalized, as it no longer has any responsibility for any cleanup.
                    GC.SuppressFinalize(this);
                }

                if (transferredHandles)
                {
                    // Once this is set, the old container's finalizer will not free transferred dependent handles,
                    // and the new container's finalizer can't be run until this container is no longer in use.
                    _oldKeepAlive = newContainer;
                }

                GC.KeepAlive(this); // ensure we don't get finalized while accessing DependentHandles.

                return newContainer;
            }

            private void VerifyIntegrity()
            {
                if (_invalid)
                {
                    throw new InvalidOperationException(SR.InvalidOperation_CollectionCorrupted);
                }
            }

            ~Container()
            {
                // Skip doing anything if the container is invalid, including if somehow
                // the container object was allocated but its associated table never set.
                if (_invalid || _parent is null)
                {
                    return;
                }

                // It's possible that the ConditionalWeakTable could have been resurrected, in which case code could
                // be accessing this Container as it's being finalized.  We don't support usage after finalization,
                // but we also don't want to potentially corrupt state by allowing dependency handles to be used as
                // or after they've been freed.  To avoid that, if it's at all possible that another thread has a
                // reference to this container via the CWT, we remove such a reference and then re-register for
                // finalization: the next time around, we can be sure that no references remain to this and we can
                // clean up the dependency handles without fear of corruption.
                if (!_finalized)
                {
                    _finalized = true;
                    lock (_parent._lock)
                    {
                        if (_parent._container == this)
                        {
                            _parent._container = null!;
                        }
                    }
                    GC.ReRegisterForFinalize(this); // next time it's finalized, we'll be sure there are no remaining refs
                    return;
                }

                Entry[] entries = _entries;
                _invalid = true;
                _entries = null!;
                _buckets = null!;

                if (entries != null)
                {
                    for (int entriesIndex = 0; entriesIndex < entries.Length; entriesIndex++)
                    {
                        // We need to free handles in two cases:
                        // - If this container still owns the dependency handle (meaning ownership hasn't been transferred
                        //   to another container that replaced this one), then it should be freed.
                        // - If this container had the entry removed, then even if in general ownership was transferred to
                        //   another container, removed entries are not, therefore this container must free them.
                        if (_oldKeepAlive is null || entries[entriesIndex].HashCode == -1)
                        {
                            entries[entriesIndex].depHnd.Dispose();
                        }
                    }
                }
            }
        }
    }

    public static class RuntimeFeature
    {
        public static bool IsDynamicCodeSupported => false;

        public static bool IsDynamicCodeCompiled => false;

        /// <summary>
        /// Gets a value that indicates whether the runtime supports multithreading, including
        /// creating threads and using blocking synchronization primitives. This property
        /// returns <see langword="false"/> on platforms or configurations where multithreading
        /// is not supported or is disabled, such as single-threaded browser environments and WASI.
        /// </summary>
        [UnsupportedOSPlatformGuard("browser")]
        [UnsupportedOSPlatformGuard("wasi")]
        public static bool IsMultithreadingSupported
#if FEATURE_MULTITHREADING
            => true;
#else
            => false;
#endif

        internal static void ThrowIfMultithreadingIsNotSupported()
        {
            if (!IsMultithreadingSupported)
            {
                throw new PlatformNotSupportedException();
            }
        }

        internal static void ThrowIfBlockingIsNotSupported(int millisecondsTimeout)
        {
            if (millisecondsTimeout != 0)
            {
                ThrowIfMultithreadingIsNotSupported();
            }
        }
    }
    public static class IsExternalInit
    {
    }
    [CLSCompliant(false)]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Event)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        private readonly string?[] _transformNames;

        public TupleElementNamesAttribute(string?[] transformNames)
        {
            ArgumentNullException.ThrowIfNull(transformNames);

            _transformNames = transformNames;
        }

        public IList<string?> TransformNames => _transformNames;
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Interface, Inherited = false)]
    internal sealed class IntrinsicAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class IndexerNameAttribute : Attribute
    {
        public IndexerNameAttribute(string indexerName)
        {
        }
    }
    /// <summary>
    /// Used to indicate to the compiler that the <c>.locals init</c>
    /// flag should not be set in method headers.
    /// </summary>
    /// <remarks>
    /// This attribute is unsafe because it may reveal uninitialized memory to
    /// the application in certain instances (e.g., reading from uninitialized
    /// stackalloc'd memory). If applied to a method directly, the attribute
    /// applies to that method and all nested functions (lambdas, local
    /// functions) below it. If applied to a type or module, it applies to all
    /// methods nested inside. This attribute is intentionally not permitted on
    /// assemblies. Use at the module level instead to apply to multiple type
    /// declarations.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Module
        | AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Interface
        | AttributeTargets.Constructor
        | AttributeTargets.Method
        | AttributeTargets.Property
        | AttributeTargets.Event, Inherited = false)]
    public sealed class SkipLocalsInitAttribute : Attribute
    {
        public SkipLocalsInitAttribute()
        {
        }
    }
    /// <summary>
    /// When applied to a type this custom attribute will cause any static field of this type to have a fixed address
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FixedAddressValueTypeAttribute : Attribute
    {
    }
    // Indicates that a method's behavior depends exactly on the IsSupported of the named intrinsics type
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true, Inherited = false)]
    internal sealed class CompExactlyDependsOnAttribute : Attribute
    {
        public CompExactlyDependsOnAttribute(Type intrinsicsTypeUsedInHelperFunction)
        {
        }
    }
    /// <summary>
    /// When applied to an attribute class, instructs the compiler to flow applications of that attribute,
    /// from source code down to compiler-generated symbols. This can help IL-based analysis tools.
    /// </summary>
    /// <remarks>
    /// One example where this attribute applies is in C# primary constructor parameters. If an attribute
    /// marked with <see cref="CompilerLoweringPreserveAttribute"/> gets applied to a primary constructor
    /// parameter, the attribute will also be applied to any compiler-generated fields storing that parameter.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class CompilerLoweringPreserveAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompilerLoweringPreserveAttribute"/> class.
        /// </summary>
        public CompilerLoweringPreserveAttribute() { }
    }
    /// <summary>
    /// Indicates the type of the async method builder that should be used by a language compiler to
    /// build the attributed async method or to build the attributed type when used as the return type
    /// of an async method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Delegate | 
        AttributeTargets.Enum | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    {
        /// <summary>Initializes the <see cref="AsyncMethodBuilderAttribute"/>.</summary>
        /// <param name="builderType">The <see cref="Type"/> of the associated builder.</param>
        public AsyncMethodBuilderAttribute(Type builderType) => BuilderType = builderType;

        /// <summary>Gets the <see cref="Type"/> of the associated builder.</summary>
        public Type BuilderType { get; }
    }
    /// <summary>
    /// Indicates that a class or struct is a union type, enabling compiler support for union behaviors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Any class or struct annotated with this attribute is recognized by the C# compiler as a union type.
    /// Union types may support behaviors such as implicit conversions from case types, pattern matching
    /// that unwraps the union's contents, and switch exhaustiveness checking.
    /// </para>
    /// </remarks>
    /// <seealso cref="IUnion" />
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class UnionAttribute : Attribute
    {
    }

    /// <summary>
    /// Provides a common interface for accessing the contents of a union type at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The C# compiler automatically implements this interface on types generated by union declarations.
    /// User-defined types annotated with <see cref="UnionAttribute" /> may also implement this interface
    /// to provide runtime access to the union's value. Implementing this interface is not required
    /// for union behaviors provided by the compiler.
    /// </para>
    /// </remarks>
    /// <seealso cref="UnionAttribute" />
    public interface IUnion
    {
        /// <summary>
        /// Gets the value contained in the union, or <see langword="null" /> if the union has no value.
        /// </summary>
        /// <value>
        /// The current value of the union as one of its case types, or <see langword="null" />.
        /// </value>
        object? Value { get; }
    }

    public static class RuntimeHelpers
    {
        [Intrinsic]
        internal static void SetNextCallAsyncContinuation(object value) => throw new UnreachableException();

        internal static unsafe void RunClassConstructorCore(nint context, nint address)
        {
            nint result = System.Runtime.RuntimeImports.RhpClassInitEnter(context);
            if (result == 0)
                return;
            if (result != 1)
                throw (Exception)GCHandle.FromIntPtr(result).Target;
            try
            {
                ((delegate*<void>)address)();
            }
            catch (Exception exception)
            {
                var failure = new TypeInitializationException(null, exception);
                nint handle = GCHandle.ToIntPtr(GCHandle.Alloc(failure));
                System.Runtime.RuntimeImports.RhpClassInitComplete(context, handle);
                throw failure;
            }
            System.Runtime.RuntimeImports.RhpClassInitComplete(context, 0);
        }

        public static bool TryEnsureSufficientExecutionStack() => true;

        public static void EnsureSufficientExecutionStack()
        {
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsBitwiseEquatable<T>()
        {
            return false;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static bool IsReferenceOrContainsReferences<T>() where T : allows ref struct => IsReferenceOrContainsReferences<T>();

        [Intrinsic]
        internal static bool EnumEquals<T>(T x, T y) where T : struct, Enum
        {
            return x.Equals(y);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static bool IsKnownConstant(int value)
        {
            return false; // to do
        }

        internal static ref byte GetRawData(this object obj) =>
            ref Unsafe.As<RawData>(obj).Data;

        public static int GetHashCode(object? o) => o is null ? 0 : System.Runtime.RuntimeImports.RhGetObjectHashCode(o);

        // The identity hash if the object has one, so a lookup by a never-hashed key finds nothing without assigning one
        internal static int TryGetHashCode(object? o) => o is null ? 0 : System.Runtime.RuntimeImports.RhTryGetObjectHashCode(o);
        [Intrinsic]
        internal static bool IsKnownConstant(Type? t) => false;

        [Intrinsic]
        internal static bool IsKnownConstant(string? t) => false;

        [Intrinsic]
        internal static bool IsKnownConstant(char t) => false;

        [Intrinsic]
        internal static bool IsKnownConstant<T>(T t) where T : struct => false;

        [Intrinsic]
        internal static unsafe MethodTable* GetMethodTable(this object obj) => obj.GetMethodTable();

        internal static unsafe ref MethodTable* GetMethodTableRef(this object obj)
            => ref obj.m_pEEType;

        // Returns true iff the object has a component size;
        // i.e., is variable length like System.String or Array.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool ObjectHasComponentSize(object obj)
        {
            return GetMethodTable(obj)->HasComponentSize;
        }

        // Returns true iff the type of the object requires finalization,
        // which includes a finalizer inherited from a base type.
        internal static unsafe bool ObjectHasFinalizer(object obj)
        {
            return GetMethodTable(obj)->IsFinalizable;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class RawArrayData
    {
        public uint Length; // Array._numComponents padded to IntPtr
#if TARGET_64BIT
        public uint Padding;
#endif
        public byte Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class RawData
    {
        public byte Data;
    }
    public enum MethodImplOptions
    {
        Unmanaged = 0x0004,
        NoInlining = 0x0008,
        ForwardRef = 0x0010,
        Synchronized = 0x0020,
        NoOptimization = 0x0040,
        PreserveSig = 0x0080,
        AggressiveInlining = 0x0100,
        AggressiveOptimization = 0x0200,
        Async = 0x2000,
        InternalCall = 0x1000
    }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    public sealed class MethodImplAttribute : Attribute
    {
        public MethodImplAttribute(MethodImplOptions methodImplOptions)
        {
            Value = methodImplOptions;
        }

        public MethodImplAttribute(short value)
        {
            Value = (MethodImplOptions)value;
        }

        public MethodImplAttribute()
        {
        }

        public MethodImplOptions Value { get; }
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
    public sealed class CollectionBuilderAttribute : Attribute
    {
        public CollectionBuilderAttribute(Type builderType, string methodName)
        {
            BuilderType = builderType;
            MethodName = methodName;
        }
        public Type BuilderType { get; }
        public string MethodName { get; }
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerAttribute : Attribute
    {
        /// <summary>Initializes the <see cref="InterpolatedStringHandlerAttribute"/>.</summary>
        public InterpolatedStringHandlerAttribute() { }
    }
    /// <summary>Indicates which arguments to a method involving an interpolated string handler should be passed to that handler.</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
    {
        public InterpolatedStringHandlerArgumentAttribute(string argument) => Arguments = [argument];

        public InterpolatedStringHandlerArgumentAttribute(params string[] arguments) => Arguments = arguments;

        /// <summary>Gets the names of the arguments that should be passed to the handler.</summary>
        /// <remarks>The empty string may be used as the name of the receiver in an instance method.</remarks>
        public string[] Arguments { get; }
    }
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int length)
        {
            Length = length;
        }

        public int Length { get; }
    }
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ExtensionAttribute : Attribute
    {
        public ExtensionAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class RequiresLocationAttribute : Attribute
    {
        public RequiresLocationAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }

        public string ParameterName { get; }
    }
    /// <summary>
    /// Indicates that a method will allow a variable number of arguments in its invocation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
    public sealed class ParamCollectionAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class IsByRefLikeAttribute : Attribute
    {
        public IsByRefLikeAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
    public sealed class ExtensionMarkerAttribute : Attribute
    {
        public ExtensionMarkerAttribute(string name) => Name = name;

        public string Name { get; }
    }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    public sealed class IsUnmanagedAttribute : Attribute
    {
        public IsUnmanagedAttribute() { }
    }
    [InterpolatedStringHandler]
    public ref struct DefaultInterpolatedStringHandler
    {
        /// <summary>Expected average length of formatted data used for an individual interpolation expression result.</summary>
        /// <remarks>
        /// This is inherited from string.Format, and could be changed based on further data.
        /// string.Format actually uses `format.Length + args.Length * 8`, but format.Length
        /// includes the format items themselves, e.g. "{0}", and since it's rare to have double-digit
        /// numbers of items, we bump the 8 up to 11 to account for the three extra characters in "{d}",
        /// since the compiler-provided base length won't include the equivalent character count.
        /// </remarks>
        private const int GuessedLengthPerHole = 11;
        /// <summary>Minimum size array to rent from the pool.</summary>
        /// <remarks>Same as stack-allocation size used today by string.Format.</remarks>
        private const int MinimumArrayPoolLength = 256;

        /// <summary>Optional provider to pass to IFormattable.ToString or ISpanFormattable.TryFormat calls.</summary>
        private readonly IFormatProvider? _provider;
        /// <summary>Array rented from the array pool and used to back <see cref="_chars"/>.</summary>
        private char[]? _arrayToReturnToPool;
        /// <summary>The span to write into.</summary>
        private Span<char> _chars;
        /// <summary>Position at which to write the next character.</summary>
        private int _pos;
        /// <summary>Whether <see cref="_provider"/> provides an ICustomFormatter.</summary>
        /// <remarks>
        /// Custom formatters are very rare.  We want to support them, but it's ok if we make them more expensive
        /// in order to make them as pay-for-play as possible.  So, we avoid adding another reference type field
        /// to reduce the size of the handler and to reduce required zero'ing, by only storing whether the provider
        /// provides a formatter, rather than actually storing the formatter.  This in turn means, if there is a
        /// formatter, we pay for the extra interface call on each AppendFormatted that needs it.
        /// </remarks>
        private readonly bool _hasCustomFormatter;

        /// <summary>Creates a handler used to translate an interpolated string into a <see cref="string"/>.</summary>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount)
        {
            _provider = null;
            _chars = _arrayToReturnToPool = System.Buffers.ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
            _pos = 0;
            _hasCustomFormatter = false;
        }

        /// <summary>Creates a handler used to translate an interpolated string into a <see cref="string"/>.</summary>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider)
        {
            _provider = provider;
            _chars = _arrayToReturnToPool = System.Buffers.ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
            _pos = 0;
            _hasCustomFormatter = provider is not null && HasCustomFormatter(provider);
        }

        /// <summary>Creates a handler used to translate an interpolated string into a <see cref="string"/>.</summary>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider, Span<char> initialBuffer)
        {
            _provider = provider;
            _chars = initialBuffer;
            _arrayToReturnToPool = null;
            _pos = 0;
            _hasCustomFormatter = provider is not null && HasCustomFormatter(provider);
        }

        /// <summary>Derives a default length with which to seed the handler.</summary>
        /// <param name="literalLength">The number of constant characters outside of interpolation expressions in the interpolated string.</param>
        /// <param name="formattedCount">The number of interpolation expressions in the interpolated string.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // becomes a constant when inputs are constant
        internal static int GetDefaultLength(int literalLength, int formattedCount) =>
            Math.Max(MinimumArrayPoolLength, literalLength + (formattedCount * GuessedLengthPerHole));

        /// <summary>Gets the built <see cref="string"/>.</summary>
        /// <returns>The built string.</returns>
        public override string ToString() => new string(Text);

        /// <summary>Gets the built <see cref="string"/> and clears the handler.</summary>
        /// <returns>The built string.</returns>
        /// <remarks>
        /// This releases any resources used by the handler. The method should be invoked only
        /// once and as the last thing performed on the handler. Subsequent use is erroneous, ill-defined,
        /// and may destabilize the process, as may using any other copies of the handler after
        /// <see cref="ToStringAndClear" /> is called on any one of them.
        /// </remarks>
        public string ToStringAndClear()
        {
            string result = new string(Text);
            Clear();
            return result;
        }

        /// <summary>Clears the handler.</summary>
        /// <remarks>
        /// This releases any resources used by the handler. The method should be invoked only
        /// once and as the last thing performed on the handler. Subsequent use is erroneous, ill-defined,
        /// and may destabilize the process, as may using any other copies of the handler after <see cref="Clear"/>
        /// is called on any one of them.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            char[]? toReturn = _arrayToReturnToPool;

            // Defensive clear
            _arrayToReturnToPool = null;
            _chars = default;
            _pos = 0;

            if (toReturn is not null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(toReturn);
            }
        }

        /// <summary>Gets a span of the characters appended to the handler.</summary>
        public ReadOnlySpan<char> Text => _chars.Slice(0, _pos);

        /// <summary>Writes the specified string to the handler.</summary>
        /// <param name="value">The string to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendLiteral(string value)
        {
            if (value.TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                GrowThenCopyString(value);
            }
        }

        #region AppendFormatted

        #region AppendFormatted T
        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        public void AppendFormatted<T>(T value)
        {
            // This method could delegate to AppendFormatted with a null format, but explicitly passing
            // default as the format to TryFormat helps to improve code quality in some cases when TryFormat is inlined,
            // e.g. for Int32 it enables the JIT to eliminate code in the inlined method based on a length check on the format.

            // If there's a custom formatter, always use it.
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format: null);
                return;
            }

            if (value is null)
            {
                return;
            }

            string? s;
            if (value is IFormattable)
            {
                // If the value can format itself directly into our buffer, do so.

                throw new NotImplementedException();
            }
            else
            {
                s = value.ToString();
            }

            if (s is not null)
            {
                AppendLiteral(s);
            }
        }

        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="format">The format string.</param>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        public void AppendFormatted<T>(T value, string? format)
        {
            // If there's a custom formatter, always use it.
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format);
                return;
            }

            if (value is null)
            {
                return;
            }

            string? s;
            if (value is IFormattable)
            {
                // If the value can format itself directly into our buffer, do so.

                throw new NotImplementedException();
            }
            else
            {
                s = value.ToString();
            }

            if (s is not null)
            {
                AppendLiteral(s);
            }
        }

        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        public void AppendFormatted<T>(T value, int alignment)
        {
            int startingPos = _pos;
            AppendFormatted(value);
            if (alignment != 0)
            {
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }

        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="format">The format string.</param>
        /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            int startingPos = _pos;
            AppendFormatted(value, format);
            if (alignment != 0)
            {
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }
        #endregion

        #region AppendFormatted ReadOnlySpan<char>
        /// <summary>Writes the specified character span to the handler.</summary>
        /// <param name="value">The span to write.</param>
        public void AppendFormatted(scoped ReadOnlySpan<char> value)
        {
            // Fast path for when the value fits in the current buffer
            if (value.TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                GrowThenCopySpan(value);
            }
        }

        /// <summary>Writes the specified string of chars to the handler.</summary>
        public void AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null)
        {
            bool leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            int paddingRequired = alignment - value.Length;
            if (paddingRequired <= 0)
            {
                // The value is as large or larger than the required amount of padding,
                // so just write the value.
                AppendFormatted(value);
                return;
            }

            // Write the value along with the appropriate padding.
            EnsureCapacityForAdditionalChars(value.Length + paddingRequired);
            if (leftAlign)
            {
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
                _chars.Slice(_pos, paddingRequired).Fill(' ');
                _pos += paddingRequired;
            }
            else
            {
                _chars.Slice(_pos, paddingRequired).Fill(' ');
                _pos += paddingRequired;
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
            }
        }
        #endregion

        #region AppendFormatted string
        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        public void AppendFormatted(string? value)
        {
            // Fast-path for no custom formatter and a non-null string that fits in the current destination buffer.
            if (!_hasCustomFormatter &&
                value is not null &&
                value.TryCopyTo(_chars.Slice(_pos)))
            {
                _pos += value.Length;
            }
            else
            {
                AppendFormattedSlow(value);
            }
        }

        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        /// <remarks>
        /// Slow path to handle a custom formatter, potentially null value,
        /// or a string that doesn't fit in the current buffer.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendFormattedSlow(string? value)
        {
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format: null);
            }
            else if (value is not null)
            {
                EnsureCapacityForAdditionalChars(value.Length);
                value.CopyTo(_chars.Slice(_pos));
                _pos += value.Length;
            }
        }

        /// <summary>Writes the specified value to the handler.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="alignment">Minimum number of characters that should be written for this value.  If the value is negative, it indicates left-aligned and the required minimum is the absolute value.</param>
        /// <param name="format">The format string.</param>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null) =>
            // Format is meaningless for strings and doesn't make sense for someone to specify.  We have the overload
            // simply to disambiguate between ROS<char> and object, just in case someone does specify a format, as
            // string is implicitly convertible to both. Just delegate to the T-based implementation.
            AppendFormatted<string?>(value, alignment, format);
        #endregion

        #endregion


        /// <summary>Gets whether the provider provides a custom formatter.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // only used in a few hot path call sites
        internal static bool HasCustomFormatter(IFormatProvider provider)
        {
            return
                provider.GetType() != typeof(System.Globalization.CultureInfo) && // optimization to avoid GetFormat in the majority case
                throw new NotSupportedException();//provider.GetFormat(typeof(ICustomFormatter)) != null;
        }

        /// <summary>Formats the value using the custom formatter from the provider.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="format">The format string.</param>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendCustomFormatter<T>(T value, string? format)
        {
            ICustomFormatter? formatter = null;//(ICustomFormatter?)_provider.GetFormat(typeof(ICustomFormatter));
            if (formatter is not null && formatter.Format(format, value, _provider) is string customFormatted)
            {
                AppendLiteral(customFormatted);
            }
        }

        private void AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
        {
            int charsWritten = _pos - startingPos;

            bool leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            int paddingNeeded = alignment - charsWritten;
            if (paddingNeeded > 0)
            {
                EnsureCapacityForAdditionalChars(paddingNeeded);

                if (leftAlign)
                {
                    _chars.Slice(_pos, paddingNeeded).Fill(' ');
                }
                else
                {
                    _chars.Slice(startingPos, charsWritten).CopyTo(_chars.Slice(startingPos + paddingNeeded));
                    _chars.Slice(startingPos, paddingNeeded).Fill(' ');
                }

                _pos += paddingNeeded;
            }
        }

        /// <summary>Ensures <see cref="_chars"/> has the capacity to store <paramref name="additionalChars"/> beyond <see cref="_pos"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacityForAdditionalChars(int additionalChars)
        {
            if (_chars.Length - _pos < additionalChars)
            {
                Grow(additionalChars);
            }
        }

        /// <summary>Fallback for fast path in <see cref="AppendLiteral(string)"/> when there's not enough space in the destination.</summary>
        /// <param name="value">The string to write.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowThenCopyString(string value)
        {
            Grow(value.Length);
            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        /// <summary>Fallback for <see cref="AppendFormatted(ReadOnlySpan{char})"/> for when not enough space exists in the current buffer.</summary>
        /// <param name="value">The span to write.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowThenCopySpan(scoped ReadOnlySpan<char> value)
        {
            Grow(value.Length);
            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        /// <summary>Grows <see cref="_chars"/> to have the capacity to store at least <paramref name="additionalChars"/> beyond <see cref="_pos"/>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
        private void Grow(int additionalChars)
        {
            // This method is called when the remaining space (_chars.Length - _pos) is
            // insufficient to store a specific number of additional characters.  Thus, we
            // need to grow to at least that new total. GrowCore will handle growing by more
            // than that if possible.
            GrowCore((uint)_pos + (uint)additionalChars);
        }

        /// <summary>Grows the size of <see cref="_chars"/>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
        private void Grow()
        {
            // This method is called when the remaining space in _chars isn't sufficient to continue
            // the operation.  Thus, we need at least one character beyond _chars.Length.  GrowCore
            // will handle growing by more than that if possible.
            GrowCore((uint)_chars.Length + 1);
        }

        /// <summary>Grow the size of <see cref="_chars"/> to at least the specified <paramref name="requiredMinCapacity"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // but reuse this grow logic directly in both of the above grow routines
        private void GrowCore(uint requiredMinCapacity)
        {
            // We want the max of how much space we actually required and doubling our capacity (without going beyond the max allowed length). We
            // also want to avoid asking for small arrays, to reduce the number of times we need to grow, and since we're working with unsigned
            // ints that could technically overflow if someone tried to, for example, append a huge string to a huge string, we also clamp to int.MaxValue.
            // Even if the array creation fails in such a case, we may later fail in ToStringAndClear.

            uint newCapacity = Math.Max(requiredMinCapacity, Math.Min((uint)_chars.Length * 2, string.MaxLength));
            int arraySize = (int)Math.Clamp(newCapacity, MinimumArrayPoolLength, int.MaxValue);

            char[] newArray = System.Buffers.ArrayPool<char>.Shared.Rent(arraySize);
            _chars.Slice(0, _pos).CopyTo(newArray);

            char[]? toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = newArray;

            if (toReturn is not null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(toReturn);
            }
        }

    }
    [InlineArray(2)]
    public struct InlineArray2<T>
    {
        private T _element0;
    }
    [InlineArray(3)]
    public struct InlineArray3<T>
    {
        private T _element0;
    }
    [InlineArray(4)]
    public struct InlineArray4<T>
    {
        private T _element0;
    }
    [InlineArray(5)]
    public struct InlineArray5<T>
    {
        private T _element0;
    }
    [InlineArray(6)]
    public struct InlineArray6<T>
    {
        private T _element0;
    }
    [InlineArray(7)]
    public struct InlineArray7<T>
    {
        private T _element0;
    }
    [InlineArray(8)]
    public struct InlineArray8<T>
    {
        private T _element0;
    }
    [InlineArray(9)]
    public struct InlineArray9<T>
    {
        private T _element0;
    }
    [InlineArray(10)]
    public struct InlineArray10<T>
    {
        private T _element0;
    }
    [InlineArray(11)]
    public struct InlineArray11<T>
    {
        private T _element0;
    }
    [InlineArray(12)]
    public struct InlineArray12<T>
    {
        private T _element0;
    }
    [InlineArray(13)]
    public struct InlineArray13<T>
    {
        private T _element0;
    }
    [InlineArray(14)]
    public struct InlineArray14<T>
    {
        private T _element0;
    }
    [InlineArray(15)]
    public struct InlineArray15<T>
    {
        private T _element0;
    }
    [InlineArray(16)]
    public struct InlineArray16<T>
    {
        private T _element0;
    }
    public sealed class SwitchExpressionException : InvalidOperationException
    {
        public SwitchExpressionException()
            : base() { }

        public SwitchExpressionException(object? unmatchedValue) : this()
        {
            UnmatchedValue = unmatchedValue;
        }

        public SwitchExpressionException(string? message) : base(message) { }

        public SwitchExpressionException(string? message, Exception? innerException)
            : base(message, innerException) { }

        public object? UnmatchedValue { get; }

        public override string Message
        {
            get
            {
                if (UnmatchedValue is null)
                {
                    return base.Message;
                }

                return base.Message + Environment.NewLine;
            }
        }
    }
    public static unsafe class Unsafe
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TTo BitCast<TFrom, TTo>(TFrom source)
            where TFrom : allows ref struct
            where TTo : allows ref struct
        {
            if (sizeof(TFrom) != sizeof(TTo))
            {
                throw new NotSupportedException();
            }
            return ReadUnaligned<TTo>(ref As<TFrom, byte>(ref source));
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadUnaligned<T>(void* source)
            where T : allows ref struct
        {
            return *(T*)source;

            // ldarg.0
            // unaligned. 0x1
            // ldobj !!T
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadUnaligned<T>(scoped ref readonly byte source)
            where T : allows ref struct
        {
            return As<byte, T>(ref Unsafe.AsRef<byte>(in source));
            // ldarg.0
            // unaligned. 0x1
            // ldobj !!T
            // ret
        }
        [NonVersionable]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUnaligned<T>(void* destination, T value)
            where T : allows ref struct
        {
            *(T*)destination = value;

            // ldarg .0
            // ldarg .1
            // unaligned. 0x01
            // stobj !!T
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUnaligned<T>(ref byte destination, T value)
            where T : allows ref struct
        {
            As<byte, T>(ref destination) = value;
            // ldarg .0
            // ldarg .1
            // unaligned. 0x01
            // stobj !!T
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyBlock(void* destination, void* source, uint byteCount)
        {
            throw new PlatformNotSupportedException();

            // ldarg .0
            // ldarg .1
            // ldarg .2
            // cpblk
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyBlock(ref byte destination, ref readonly byte source, uint byteCount)
        {
            throw new PlatformNotSupportedException();

            // ldarg .0
            // ldarg .1
            // ldarg .2
            // cpblk
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyBlockUnaligned(void* destination, void* source, uint byteCount)
        {
            throw new PlatformNotSupportedException();

            // ldarg .0
            // ldarg .1
            // ldarg .2
            // unaligned. 0x1
            // cpblk
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyBlockUnaligned(ref byte destination, ref readonly byte source, uint byteCount)
        {
            throw new PlatformNotSupportedException();

            // ldarg .0
            // ldarg .1
            // ldarg .2
            // unaligned. 0x1
            // cpblk
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T As<T>(object o) where T : class?
        {
            throw new PlatformNotSupportedException();
            // ldarg.0
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref TTo As<TFrom, TTo>(ref TFrom source)
            where TFrom : allows ref struct
            where TTo : allows ref struct
        {
            throw new PlatformNotSupportedException();
            // ldarg.0
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref T AsRef<T>(void* source)
            where T : allows ref struct
        {
            return ref *(T*)source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AsRef<T>(scoped ref readonly T source)
            where T : allows ref struct
        {
            //ldarg .0
            //ret
            return ref source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AsPointer<T>(ref readonly T value)
            where T : allows ref struct
        {
            throw new PlatformNotSupportedException();

            // ldarg.0
            // conv.u
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Add<T>(ref T source, int elementOffset)
            where T : allows ref struct
        {
            return ref source;


            // ldarg .0
            // ldarg .1
            // sizeof !!T
            // conv.i
            // mul
            // add
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void* Add<T>(void* source, int elementOffset)
            where T : allows ref struct
        {
            return (byte*)source + (elementOffset * (nint)sizeof(T));


            // ldarg .0
            // ldarg .1
            // sizeof !!T
            // conv.i
            // mul
            // add
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Add<T>(ref T source, IntPtr elementOffset)
            where T : allows ref struct
        {
            return ref AddByteOffset<T>(ref source, (IntPtr)((nint)elementOffset * (nint)sizeof(T)));
            // ldarg .0
            // ldarg .1
            // sizeof !!T
            // mul
            // add
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Add<T>(ref T source, nuint elementOffset)
            where T : allows ref struct
        {
            return ref AddByteOffset(ref source, (nuint)(elementOffset * (nuint)sizeof(T)));

            // ldarg .0
            // ldarg .1
            // sizeof !!T
            // mul
            // add
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref T AddByteOffset<T>(ref T source, nuint byteOffset)
            where T : allows ref struct
        {
            return ref AddByteOffset<T>(ref source, (IntPtr)(void*)byteOffset);
            // ldarg .0
            // ldarg .1
            // add
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AddByteOffset<T>(ref T source, IntPtr byteOffset)
            where T : allows ref struct
        {
            // ldarg.0
            // ldarg.1
            // add
            // ret
            return ref source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint ByteOffset<T>(ref T origin, ref T target)
            where T : allows ref struct
        {
            return 0;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>()
            where T : allows ref struct
        {
            return 0;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T NullRef<T>()
            where T : allows ref struct
        {
            return ref AsRef<T>(null);

            // ldc.i4.0
            // conv.u
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullRef<T>(ref readonly T source)
            where T : allows ref struct
        {
            return true;
            // ldarg.0
            // ldc.i4.0
            // conv.u
            // ceq
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreSame<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
            where T : allows ref struct
        {
            // ldarg.0
            // ldarg.1
            // ceq
            // ret
            return false;
        }
        [NonVersionable]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressGreaterThan<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
            where T : allows ref struct
        {
            throw new PlatformNotSupportedException();

            // ldarg.0
            // ldarg.1
            // cgt.un
            // ret
        }

        [NonVersionable]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressLessThan<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
            where T : allows ref struct
        {
            throw new PlatformNotSupportedException();

            // ldarg.0
            // ldarg.1
            // clt.un
            // ret
        }

        /// <summary>
        /// Determines whether the memory address referenced by <paramref name="left"/> is less than
        /// or equal to the memory address referenced by <paramref name="right"/>.
        /// </summary>
        /// <remarks>
        /// This check is conceptually similar to "(void*)(&amp;left) &lt;= (void*)(&amp;right)".
        /// </remarks>
        [Intrinsic]
        [NonVersionable]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressLessThanOrEqualTo<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
            where T : allows ref struct
        {
            return !IsAddressGreaterThan(in left, in right);
        }

        [NonVersionable]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipInit<T>(out T value)
            where T : allows ref struct
        {
            throw new PlatformNotSupportedException();

            // ret
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public struct AsyncIteratorMethodBuilder
    {
        private Task<VoidTaskResult> m_task;

        internal object ObjectIdForDebugger => m_task ?? (m_task = AsyncTaskMethodBuilder<VoidTaskResult>.CreateWeaklyTypedStateMachineBox());

        public static AsyncIteratorMethodBuilder Create()
        {
            return default(AsyncIteratorMethodBuilder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MoveNext<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        public void Complete()
        {
            if (m_task is null)
            {
                m_task = Task.s_cachedCompleted;
                return;
            }
            AsyncTaskMethodBuilder<VoidTaskResult>.SetExistingTaskResult(m_task, default(VoidTaskResult));
            if (m_task is IAsyncStateMachineBox asyncStateMachineBox)
            {
                asyncStateMachineBox.ClearStateUponCompletion();
            }
        }
    }


    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncIteratorStateMachineAttribute : StateMachineAttribute
    {
        public AsyncIteratorStateMachineAttribute(Type stateMachineType)
            : base(stateMachineType)
        {
        }
    }


    internal static class AsyncMethodBuilderCore
    {
        private sealed class ContinuationWrapper
        {
            private readonly Action<Action, Task> _invokeAction;

            internal readonly Action _continuation;

            internal readonly Task _innerTask;

            internal ContinuationWrapper(Action continuation, Action<Action, Task> invokeAction, Task innerTask)
            {
                _invokeAction = invokeAction;
                _continuation = continuation;
                _innerTask = innerTask;
            }

            internal void Invoke()
            {
                _invokeAction(_continuation, _innerTask);
            }
        }

        [DebuggerStepThrough]
        public static void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            if (stateMachine is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.stateMachine);
            }
            Thread currentThread = Thread.CurrentThread;
            ExecutionContext executionContext = currentThread._executionContext;
            SynchronizationContext synchronizationContext = currentThread._synchronizationContext;
            try
            {
                stateMachine.MoveNext();
            }
            finally
            {
                if (synchronizationContext != currentThread._synchronizationContext)
                {
                    currentThread._synchronizationContext = synchronizationContext;
                }
                ExecutionContext executionContext2 = currentThread._executionContext;
                if (executionContext != executionContext2)
                {
                    ExecutionContext.RestoreChangedContextToThread(currentThread, executionContext, executionContext2);
                }
            }
        }

        public static void SetStateMachine(IAsyncStateMachine stateMachine, Task task)
        {
            if (stateMachine is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.stateMachine);
            }
            if (task is not null)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.AsyncMethodBuilder_InstanceNotInitialized);
            }
        }

        internal static string GetAsyncStateMachineDescription(IAsyncStateMachine stateMachine)
        {
            Type type = stateMachine.GetType();
            return type.FullName;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void LogTraceOperationBegin(Task t, Type stateMachineType)
        {
            TplEventSource.Log.TraceOperationBegin(t.Id, "Async: " + stateMachineType.Name, 0L);
        }

        internal static Action CreateContinuationWrapper(Action continuation, Action<Action, Task> invokeAction, Task innerTask)
        {
            return new ContinuationWrapper(continuation, invokeAction, innerTask).Invoke;
        }

        internal static Action TryGetStateMachineForDebugger(Action action)
        {
            object target = action.Target;
            if (!(target is IAsyncStateMachineBox asyncStateMachineBox))
            {
                if (!(target is ContinuationWrapper continuationWrapper))
                {
                    return action;
                }
                return TryGetStateMachineForDebugger(continuationWrapper._continuation);
            }
            return asyncStateMachineBox.GetStateMachineObject().MoveNext;
        }

        internal static Task TryGetContinuationTask(Action continuation)
        {
            if (!(continuation.Target is ContinuationWrapper continuationWrapper))
            {
                return continuation.Target as Task;
            }
            return continuationWrapper._innerTask;
        }

        internal static IAsyncStateMachineBox TryGetStateMachineBox(Action continuation)
        {
            object target = continuation.Target;
            if (target is IAsyncStateMachineBox result)
            {
                return result;
            }
            if (target is ContinuationWrapper continuationWrapper)
            {
                return TryGetStateMachineBox(continuationWrapper._continuation);
            }
            return null;
        }
    }


    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class AsyncStateMachineAttribute : StateMachineAttribute
    {
        public AsyncStateMachineAttribute(Type stateMachineType)
            : base(stateMachineType)
        {
        }
    }


    public struct AsyncTaskMethodBuilder
    {
        private Task<VoidTaskResult> m_task;

        public Task Task
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return m_task ?? InitializeTaskAsPromise();
            }
        }

        internal object ObjectIdForDebugger => m_task ?? (m_task = AsyncTaskMethodBuilder<VoidTaskResult>.CreateWeaklyTypedStateMachineBox());

        public static AsyncTaskMethodBuilder Create()
        {
            return default(AsyncTaskMethodBuilder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [DebuggerStepThrough]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            AsyncMethodBuilderCore.SetStateMachine(stateMachine, null);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Task<VoidTaskResult> InitializeTaskAsPromise()
        {
            return m_task = new Task<VoidTaskResult>();
        }

        public void SetResult()
        {
            if (m_task is null)
            {
                m_task = System.Threading.Tasks.Task.s_cachedCompleted;
            }
            else
            {
                AsyncTaskMethodBuilder<VoidTaskResult>.SetExistingTaskResult(m_task, default(VoidTaskResult));
            }
        }

        public void SetException(Exception exception)
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.SetException(exception, ref m_task);
        }

        internal void SetNotificationForWaitCompletion(bool enabled)
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.SetNotificationForWaitCompletion(enabled, ref m_task);
        }
    }

    public struct AsyncTaskMethodBuilder<TResult>
    {
        [DebuggerDisplay("{DebuggerDisplay,nq}")]
        private class AsyncStateMachineBox<TStateMachine> : Task<TResult>, IAsyncStateMachineBox where TStateMachine : IAsyncStateMachine
        {
            private static readonly ContextCallback s_callback = ExecutionContextCallback;

            public TStateMachine StateMachine;

            private string DebuggerDisplay
            {
                get
                {
                    object obj;
                    if (!(typeof(TStateMachine) != typeof(IAsyncStateMachine)))
                    {
                        obj = StateMachine?.ToString();
                        if (obj is null)
                        {
                            obj = "IAsyncStateMachine";
                        }
                    }
                    else
                    {
                        obj = typeof(TStateMachine).Name;
                    }
                    string value = (string)obj;
                    if (base.IsCompletedSuccessfully && typeof(TResult) != typeof(VoidTaskResult))
                    {
                        return $"Id = {base.Id}, Status = {base.Status}, Method = {value}, Result = {m_result}";
                    }
                    return $"Id = {base.Id}, Status = {base.Status}, Method = {value}";
                }
            }

            public Action MoveNextAction => (Action)(m_action ?? (m_action = new Action(MoveNext)));

            public ref ExecutionContext Context => ref Unsafe.As<object, ExecutionContext>(ref m_stateObject);

            private static void ExecutionContextCallback(object s)
            {
                Unsafe.As<AsyncStateMachineBox<TStateMachine>>(s).StateMachine.MoveNext();
            }

            public AsyncStateMachineBox()
            {
                m_stateFlags |= 2048;
            }

            internal sealed override void ExecuteDirectly(Thread threadPoolThread)
            {
                MoveNext(threadPoolThread);
            }

            public void MoveNext()
            {
                MoveNext(null);
            }

            private void MoveNext(Thread threadPoolThread)
            {
                AsyncInstrumentation.Flags flags = AsyncInstrumentation.Flags.Disabled;
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    InstrumentedMoveNext(threadPoolThread, flags);
                }
                else
                {
                    MoveNext(threadPoolThread, flags);
                }
            }

            private protected virtual void InstrumentedMoveNext(Thread threadPoolThread, AsyncInstrumentation.Flags flags)
            {
                AsyncStateMachineDispatcherInfo.ResumeAsyncMethod(this, flags);
                MoveNext(threadPoolThread, flags);
            }

            private protected void MoveNext(Thread threadPoolThread, AsyncInstrumentation.Flags flags)
            {
                if (AsyncInstrumentation.IsEnabled.Tpl(flags))
                {
                    TplEventSource.Log.TraceSynchronousWorkBegin(base.Id, CausalitySynchronousWork.Execution);
                }
                ExecutionContext context = Context;
                if (context == ExecutionContext.DefaultFlowSuppressed)
                {
                    StateMachine.MoveNext();
                }
                else if (threadPoolThread is null)
                {
                    ExecutionContext.RunInternal(context, s_callback, this);
                }
                else
                {
                    ExecutionContext.RunFromThreadPoolDispatchLoop(threadPoolThread, context, s_callback, this);
                }
                if (base.IsCompleted)
                {
                    ClearStateUponCompletion();
                }
                if (AsyncInstrumentation.IsEnabled.Tpl(flags))
                {
                    TplEventSource.Log.TraceSynchronousWorkEnd(CausalitySynchronousWork.Execution);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ClearStateUponCompletion()
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncDebugger(flags))
                {
                    System.Threading.Tasks.Task.RemoveFromActiveTasks(this);
                }
                StateMachine = default(TStateMachine);
                Context = null;
            }

            IAsyncStateMachine IAsyncStateMachineBox.GetStateMachineObject()
            {
                return StateMachine;
            }

            bool IAsyncStateMachineBox.GetDiagnosticData(out ulong methodId, out int state, out object nextContinuation)
            {
                if (AsyncStateMachineDispatcherInfo.IsSupported)
                {
                    methodId = AsyncStateMachineDiagnostics<TStateMachine>.MethodId;
                    state = AsyncStateMachineDiagnostics<TStateMachine>.GetState(ref StateMachine);
                    nextContinuation = base.ContinuationForDiagnostics;
                    return true;
                }
                methodId = 0uL;
                state = -1;
                nextContinuation = null;
                return false;
            }
        }

        private Task<TResult> m_task;

        public Task<TResult> Task
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return m_task ?? InitializeTaskAsPromise();
            }
        }

        internal object ObjectIdForDebugger => m_task ?? (m_task = CreateWeaklyTypedStateMachineBox());

        public static AsyncTaskMethodBuilder<TResult> Create()
        {
            return default(AsyncTaskMethodBuilder<TResult>);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [DebuggerStepThrough]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            AsyncMethodBuilderCore.SetStateMachine(stateMachine, m_task);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        internal static void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine, ref Task<TResult> taskField) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            try
            {
                IAsyncStateMachineBox asyncStateMachineBox = GetStateMachineBox(ref stateMachine, ref taskField);
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    asyncStateMachineBox = AsyncStateMachineDispatcherInfo.CreateDispatcher(asyncStateMachineBox, flags);
                }
                awaiter.OnCompleted(asyncStateMachineBox.MoveNextAction);
            }
            catch (Exception exception)
            {
                System.Threading.Tasks.Task.ThrowAsync(exception, null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine, [NotNull] ref Task<TResult> taskField) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            IAsyncStateMachineBox stateMachineBox = GetStateMachineBox(ref stateMachine, ref taskField);
            AwaitUnsafeOnCompleted(ref awaiter, stateMachineBox);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        internal static void AwaitUnsafeOnCompleted<TAwaiter>(ref TAwaiter awaiter, IAsyncStateMachineBox box) where TAwaiter : ICriticalNotifyCompletion
        {
            TAwaiter val = default(TAwaiter);
            if (val is not null && awaiter is ITaskAwaiter)
            {
                TaskAwaiter.UnsafeOnCompletedInternal(Unsafe.As<TAwaiter, TaskAwaiter>(ref awaiter).m_task, box, continueOnCapturedContext: true);
                return;
            }
            val = default(TAwaiter);
            if (val is not null && awaiter is IConfiguredTaskAwaiter)
            {
                ref ConfiguredTaskAwaitable.ConfiguredTaskAwaiter reference = ref Unsafe.As<TAwaiter, ConfiguredTaskAwaitable.ConfiguredTaskAwaiter>(ref awaiter);
                TaskAwaiter.UnsafeOnCompletedInternal(reference.m_task, box, (reference.m_options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0);
                return;
            }
            val = default(TAwaiter);
            if (val is not null && awaiter is IStateMachineBoxAwareAwaiter)
            {
                try
                {
                    ((IStateMachineBoxAwareAwaiter)(object)awaiter).AwaitUnsafeOnCompleted(box);
                    return;
                }
                catch (Exception exception)
                {
                    System.Threading.Tasks.Task.ThrowAsync(exception, null);
                    return;
                }
            }
            try
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                }
                ref TAwaiter reference2 = ref awaiter;
                val = default(TAwaiter);
                if (val is null)
                {
                    val = reference2;
                    reference2 = ref val;
                }
                Action moveNextAction = box.MoveNextAction;
                reference2.UnsafeOnCompleted(moveNextAction);
            }
            catch (Exception exception2)
            {
                System.Threading.Tasks.Task.ThrowAsync(exception2, null);
            }
        }

        private static IAsyncStateMachineBox GetStateMachineBox<TStateMachine>(ref TStateMachine stateMachine, [NotNull] ref Task<TResult> taskField) where TStateMachine : IAsyncStateMachine
        {
            ExecutionContext executionContext = ExecutionContext.CaptureForSuspension(Thread.CurrentThread);
            if (taskField is AsyncStateMachineBox<TStateMachine> asyncStateMachineBox)
            {
                if (asyncStateMachineBox.Context != executionContext)
                {
                    asyncStateMachineBox.Context = executionContext;
                }
                return asyncStateMachineBox;
            }
            if (taskField is AsyncStateMachineBox<IAsyncStateMachine> asyncStateMachineBox2)
            {
                if (asyncStateMachineBox2.StateMachine is null)
                {
                    Debugger.NotifyOfCrossThreadDependency();
                    asyncStateMachineBox2.StateMachine = stateMachine;
                }
                asyncStateMachineBox2.Context = executionContext;
                return asyncStateMachineBox2;
            }
            Debugger.NotifyOfCrossThreadDependency();
            AsyncInstrumentation.Flags flags = AsyncInstrumentation.Flags.Disabled;
            AsyncStateMachineBox<TStateMachine> asyncStateMachineBox3 = (AsyncStateMachineBox<TStateMachine>)(taskField = ((!AsyncInstrumentation.IsActive || !AsyncInstrumentation.LoadFlags(out flags)) ? new AsyncStateMachineBox<TStateMachine>() : new AsyncStateMachineBox<TStateMachine>()));
            asyncStateMachineBox3.StateMachine = stateMachine;
            asyncStateMachineBox3.Context = executionContext;
            if (flags != AsyncInstrumentation.Flags.Disabled)
            {
                if (AsyncInstrumentation.IsEnabled.Tpl(flags))
                {
                    AsyncMethodBuilderCore.LogTraceOperationBegin(asyncStateMachineBox3, stateMachine.GetType());
                }
                if (AsyncInstrumentation.IsEnabled.AsyncDebugger(flags))
                {
                    System.Threading.Tasks.Task.AddToActiveTasks(asyncStateMachineBox3);
                }
            }
            return asyncStateMachineBox3;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Task<TResult> InitializeTaskAsPromise()
        {
            return m_task = new Task<TResult>();
        }

        internal static Task<TResult> CreateWeaklyTypedStateMachineBox()
        {
            return new AsyncStateMachineBox<IAsyncStateMachine>();
        }

        public void SetResult(TResult result)
        {
            if (m_task is null)
            {
                m_task = System.Threading.Tasks.Task.FromResult(result);
            }
            else
            {
                SetExistingTaskResult(m_task, result);
            }
        }

        internal static void SetExistingTaskResult(Task<TResult> task, TResult result)
        {
            if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags))
            {
                if (AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    AsyncStateMachineDispatcherInfo.CompleteAsyncMethod(task, flags);
                }
                if (AsyncInstrumentation.IsEnabled.Tpl(flags))
                {
                    TplEventSource.Log.TraceOperationEnd(task.Id, AsyncCausalityStatus.Completed);
                }
            }
            if (!task.TrySetResult(result))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        public void SetException(Exception exception)
        {
            SetException(exception, ref m_task);
        }

        internal static void SetException(Exception exception, ref Task<TResult> taskField)
        {
            if (exception is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);
            }
            Task<TResult> task = taskField ?? (taskField = new Task<TResult>());
            if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
            {
                AsyncStateMachineDispatcherInfo.UnwindAsyncFrame(task, flags);
            }
            if (!((exception is OperationCanceledException ex) ? task.TrySetCanceled(ex.CancellationToken, ex) : task.TrySetException(exception)))
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }

        internal void SetNotificationForWaitCompletion(bool enabled)
        {
            SetNotificationForWaitCompletion(enabled, ref m_task);
        }

        internal static void SetNotificationForWaitCompletion(bool enabled, [NotNull] ref Task<TResult> taskField)
        {
            (taskField ?? (taskField = CreateWeaklyTypedStateMachineBox())).SetNotificationForWaitCompletion(enabled);
        }
    }


    public struct AsyncVoidMethodBuilder
    {
        private SynchronizationContext _synchronizationContext;

        private AsyncTaskMethodBuilder _builder;

        private Task Task => _builder.Task;

        internal object ObjectIdForDebugger => _builder.ObjectIdForDebugger;

        public static AsyncVoidMethodBuilder Create()
        {
            SynchronizationContext current = SynchronizationContext.Current;
            current?.OperationStarted();
            return new AsyncVoidMethodBuilder
            {
                _synchronizationContext = current
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [DebuggerStepThrough]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _builder.SetStateMachine(stateMachine);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }

        public void SetResult()
        {
            if (TplEventSource.Log.IsEnabled())
            {
                TplEventSource.Log.TraceOperationEnd(Task.Id, AsyncCausalityStatus.Completed);
            }
            SynchronizationContext synchronizationContext = _synchronizationContext;
            _builder.SetResult();
            if (synchronizationContext is not null)
            {
                NotifySynchronizationContextOfCompletion(synchronizationContext);
            }
        }

        public void SetException(Exception exception)
        {
            if (exception is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);
            }
            if (TplEventSource.Log.IsEnabled())
            {
                TplEventSource.Log.TraceOperationEnd(Task.Id, AsyncCausalityStatus.Error);
            }
            SynchronizationContext synchronizationContext = _synchronizationContext;
            if (synchronizationContext is not null)
            {
                try
                {
                    System.Threading.Tasks.Task.ThrowAsync(exception, synchronizationContext);
                }
                finally
                {
                    NotifySynchronizationContextOfCompletion(synchronizationContext);
                }
            }
            else
            {
                System.Threading.Tasks.Task.ThrowAsync(exception, null);
            }
            _builder.SetResult();
        }

        private static void NotifySynchronizationContextOfCompletion(SynchronizationContext context)
        {
            try
            {
                context.OperationCompleted();
            }
            catch (Exception exception)
            {
                System.Threading.Tasks.Task.ThrowAsync(exception, null);
            }
        }
    }


    [StructLayout(LayoutKind.Auto)]
    public readonly struct ConfiguredAsyncDisposable
    {
        private readonly IAsyncDisposable _source;

        private readonly bool _continueOnCapturedContext;

        internal ConfiguredAsyncDisposable(IAsyncDisposable source, bool continueOnCapturedContext)
        {
            _source = source;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        public ConfiguredValueTaskAwaitable DisposeAsync()
        {
            return _source.DisposeAsync().ConfigureAwait(_continueOnCapturedContext);
        }
    }


    [StructLayout(LayoutKind.Auto)]
    public readonly struct ConfiguredCancelableAsyncEnumerable<T> where T : allows ref struct
    {
        [StructLayout(LayoutKind.Auto)]
        public readonly struct Enumerator
        {
            private readonly IAsyncEnumerator<T> _enumerator;

            private readonly bool _continueOnCapturedContext;

            public T Current => _enumerator.Current;

            internal Enumerator(IAsyncEnumerator<T> enumerator, bool continueOnCapturedContext)
            {
                _enumerator = enumerator;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public ConfiguredValueTaskAwaitable<bool> MoveNextAsync()
            {
                return _enumerator.MoveNextAsync().ConfigureAwait(_continueOnCapturedContext);
            }

            public ConfiguredValueTaskAwaitable DisposeAsync()
            {
                return _enumerator.DisposeAsync().ConfigureAwait(_continueOnCapturedContext);
            }
        }

        private readonly IAsyncEnumerable<T> _enumerable;

        private readonly CancellationToken _cancellationToken;

        private readonly bool _continueOnCapturedContext;

        internal ConfiguredCancelableAsyncEnumerable(IAsyncEnumerable<T> enumerable, bool continueOnCapturedContext, CancellationToken cancellationToken)
        {
            _enumerable = enumerable;
            _continueOnCapturedContext = continueOnCapturedContext;
            _cancellationToken = cancellationToken;
        }

        public ConfiguredCancelableAsyncEnumerable<T> ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredCancelableAsyncEnumerable<T>(_enumerable, continueOnCapturedContext, _cancellationToken);
        }

        public ConfiguredCancelableAsyncEnumerable<T> WithCancellation(CancellationToken cancellationToken)
        {
            return new ConfiguredCancelableAsyncEnumerable<T>(_enumerable, _continueOnCapturedContext, cancellationToken);
        }

        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator(_enumerable.GetAsyncEnumerator(_cancellationToken), _continueOnCapturedContext);
        }
    }


    public readonly struct ConfiguredTaskAwaitable
    {
        /// <summary>The task being awaited.</summary>
        private readonly ConfiguredTaskAwaiter m_configuredTaskAwaiter;

        /// <summary>Initializes the <see cref="ConfiguredTaskAwaitable"/>.</summary>
        /// <param name="task">The awaitable <see cref="Task"/>.</param>
        /// <param name="options">Options to control the behavior of the awaiter.</param>
        internal ConfiguredTaskAwaitable(Task task, ConfigureAwaitOptions options)
        {
            m_configuredTaskAwaiter = new ConfiguredTaskAwaiter(task, options);
        }

        /// <summary>Gets an awaiter for this awaitable.</summary>
        /// <returns>The awaiter.</returns>
        public ConfiguredTaskAwaiter GetAwaiter()
        {
            return m_configuredTaskAwaiter;
        }

        /// <summary>Provides an awaiter for a <see cref="ConfiguredTaskAwaitable"/>.</summary>
        /// <remarks>This type is intended for compiler use only.</remarks>
        public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion, IConfiguredTaskAwaiter
        {
            // WARNING: Unsafe.As is used to access the generic ConfiguredTaskAwaiter as this.
            // Its layout must remain the same.

            /// <summary>The task being awaited.</summary>
            internal readonly Task m_task;
            /// <summary>Options for how this awaiter behaves. This is a bit field with values from <see cref="ConfigureAwaitOptions"/>.</summary>
            internal readonly ConfigureAwaitOptions m_options;

            /// <summary>Initializes the <see cref="ConfiguredTaskAwaiter"/>.</summary>
            /// <param name="task">The <see cref="Task"/> to await.</param>
            /// <param name="options">Options used to configure how an await is performed.</param>
            internal ConfiguredTaskAwaiter(Task task, ConfigureAwaitOptions options)
            {
                m_task = task;
                m_options = options;
            }

            /// <summary>Gets whether the task being awaited is completed.</summary>
            /// <remarks>This property is intended for compiler user rather than use directly in code.</remarks>
            /// <exception cref="NullReferenceException">The awaiter was not properly initialized.</exception>
            public bool IsCompleted => ((m_options & ConfigureAwaitOptions.ForceYielding) == 0) && m_task.IsCompleted;

            /// <summary>Schedules the continuation onto the <see cref="Task"/> associated with this <see cref="TaskAwaiter"/>.</summary>
            /// <param name="continuation">The action to invoke when the await operation completes.</param>
            /// <exception cref="ArgumentNullException">The <paramref name="continuation"/> argument is null (<see langword="Nothing" /> in Visual Basic).</exception>
            /// <exception cref="NullReferenceException">The awaiter was not properly initialized.</exception>
            /// <remarks>This method is intended for compiler use rather than use directly in code.</remarks>
            public void OnCompleted(Action continuation)
            {
                TaskAwaiter.OnCompletedInternal(m_task, continuation, (m_options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0, flowExecutionContext: true);
            }

            /// <summary>Schedules the continuation onto the <see cref="Task"/> associated with this <see cref="TaskAwaiter"/>.</summary>
            /// <param name="continuation">The action to invoke when the await operation completes.</param>
            /// <exception cref="ArgumentNullException">The <paramref name="continuation"/> argument is null (<see langword="Nothing" /> in Visual Basic).</exception>
            /// <exception cref="NullReferenceException">The awaiter was not properly initialized.</exception>
            /// <remarks>This method is intended for compiler use rather than use directly in code.</remarks>
            public void UnsafeOnCompleted(Action continuation)
            {
                TaskAwaiter.OnCompletedInternal(m_task, continuation, (m_options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0, flowExecutionContext: false);
            }

            /// <summary>Ends the await on the completed <see cref="Task"/>.</summary>
            /// <exception cref="NullReferenceException">The awaiter was not properly initialized.</exception>
            /// <exception cref="TaskCanceledException">The task was canceled.</exception>
            /// <exception cref="Exception">The task completed in a Faulted state.</exception>
            [StackTraceHidden]
            public void GetResult()
            {
                TaskAwaiter.ValidateEnd(m_task, m_options);
            }
        }
    }

    public readonly struct ConfiguredTaskAwaitable<TResult>
    {
        public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion, IConfiguredTaskAwaiter
        {
            internal readonly Task<TResult> m_task;

            internal readonly ConfigureAwaitOptions m_options;

            public bool IsCompleted
            {
                get
                {
                    if ((m_options & ConfigureAwaitOptions.ForceYielding) == 0)
                    {
                        return m_task.IsCompleted;
                    }
                    return false;
                }
            }

            internal ConfiguredTaskAwaiter(Task<TResult> task, ConfigureAwaitOptions options)
            {
                m_task = task;
                m_options = options;
            }

            public void OnCompleted(Action continuation)
            {
                TaskAwaiter.OnCompletedInternal(m_task, continuation, (m_options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0, flowExecutionContext: true);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                TaskAwaiter.OnCompletedInternal(m_task, continuation, (m_options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0, flowExecutionContext: false);
            }

            public TResult GetResult()
            {
                TaskAwaiter.ValidateEnd(m_task);
                return m_task.ResultOnSuccess;
            }
        }

        private readonly ConfiguredTaskAwaiter m_configuredTaskAwaiter;

        internal ConfiguredTaskAwaitable(Task<TResult> task, ConfigureAwaitOptions options)
        {
            m_configuredTaskAwaiter = new ConfiguredTaskAwaiter(task, options);
        }

        public ConfiguredTaskAwaiter GetAwaiter()
        {
            return m_configuredTaskAwaiter;
        }
    }


    [StructLayout(LayoutKind.Auto)]
    public readonly struct ConfiguredValueTaskAwaitable
    {
        [StructLayout(LayoutKind.Auto)]
        public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion, IStateMachineBoxAwareAwaiter
        {
            private readonly ValueTask _value;

            public bool IsCompleted
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return _value.IsCompleted;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ConfiguredValueTaskAwaiter(in ValueTask value)
            {
                _value = value;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetResult()
            {
                _value.ThrowIfCompletedUnsuccessfully();
            }

            public void OnCompleted(Action continuation)
            {
                object obj = _value._obj;
                if (obj is Task task)
                {
                    task.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
                }
                else if (obj is not null)
                {
                    Unsafe.As<IValueTaskSource>(obj).OnCompleted(ValueTaskAwaiter.s_invokeActionDelegate, continuation, _value._token, (ValueTaskSourceOnCompletedFlags)(2 | (_value._continueOnCapturedContext ? 1 : 0)));
                }
                else
                {
                    Task.CompletedTask.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
                }
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                object obj = _value._obj;
                if (obj is Task task)
                {
                    task.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
                }
                else if (obj is not null)
                {
                    Unsafe.As<IValueTaskSource>(obj).OnCompleted(ValueTaskAwaiter.s_invokeActionDelegate, continuation, _value._token, _value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None);
                }
                else
                {
                    Task.CompletedTask.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
                }
            }

            void IStateMachineBoxAwareAwaiter.AwaitUnsafeOnCompleted(IAsyncStateMachineBox box)
            {
                object obj = _value._obj;
                if (obj is Task task)
                {
                    TaskAwaiter.UnsafeOnCompletedInternal(task, box, _value._continueOnCapturedContext);
                }
                else if (obj is not null)
                {
                    if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && !(obj is IAsyncStateMachineBox) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                    {
                        box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                    }
                    Unsafe.As<IValueTaskSource>(obj).OnCompleted(ThreadPool.s_invokeAsyncStateMachineBox, box, _value._token, _value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None);
                }
                else
                {
                    TaskAwaiter.UnsafeOnCompletedInternal(Task.CompletedTask, box, _value._continueOnCapturedContext);
                }
            }
        }

        internal readonly ValueTask _value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ConfiguredValueTaskAwaitable(in ValueTask value)
        {
            _value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ConfiguredValueTaskAwaiter GetAwaiter()
        {
            return new ConfiguredValueTaskAwaiter(in _value);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct ConfiguredValueTaskAwaitable<TResult>
    {
        [StructLayout(LayoutKind.Auto)]
        public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion, IStateMachineBoxAwareAwaiter
        {
            private readonly ValueTask<TResult> _value;

            public bool IsCompleted
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return _value.IsCompleted;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ConfiguredValueTaskAwaiter(in ValueTask<TResult> value)
            {
                _value = value;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public TResult GetResult()
            {
                return _value.Result;
            }

            public void OnCompleted(Action continuation)
            {
                object obj = _value._obj;
                if (obj is Task<TResult> task)
                {
                    task.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
                }
                else if (obj is not null)
                {
                    Unsafe.As<IValueTaskSource<TResult>>(obj).OnCompleted(ValueTaskAwaiter.s_invokeActionDelegate, continuation, _value._token, (ValueTaskSourceOnCompletedFlags)(2 | (_value._continueOnCapturedContext ? 1 : 0)));
                }
                else
                {
                    Task.CompletedTask.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
                }
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                object obj = _value._obj;
                if (obj is Task<TResult> task)
                {
                    task.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
                }
                else if (obj is not null)
                {
                    Unsafe.As<IValueTaskSource<TResult>>(obj).OnCompleted(ValueTaskAwaiter.s_invokeActionDelegate, continuation, _value._token, _value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None);
                }
                else
                {
                    Task.CompletedTask.ConfigureAwait(_value._continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
                }
            }

            void IStateMachineBoxAwareAwaiter.AwaitUnsafeOnCompleted(IAsyncStateMachineBox box)
            {
                object obj = _value._obj;
                if (obj is Task<TResult> task)
                {
                    TaskAwaiter.UnsafeOnCompletedInternal(task, box, _value._continueOnCapturedContext);
                }
                else if (obj is not null)
                {
                    if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && !(obj is IAsyncStateMachineBox) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                    {
                        box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                    }
                    Unsafe.As<IValueTaskSource<TResult>>(obj).OnCompleted(ThreadPool.s_invokeAsyncStateMachineBox, box, _value._token, _value._continueOnCapturedContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None);
                }
                else
                {
                    TaskAwaiter.UnsafeOnCompletedInternal(Task.CompletedTask, box, _value._continueOnCapturedContext);
                }
            }
        }

        internal readonly ValueTask<TResult> _value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ConfiguredValueTaskAwaitable(in ValueTask<TResult> value)
        {
            _value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ConfiguredValueTaskAwaiter GetAwaiter()
        {
            return new ConfiguredValueTaskAwaiter(in _value);
        }
    }


    internal unsafe class Continuation
    {
        public Continuation? Next;
        public ResumeInfo* ResumeInfo;
        public ContinuationFlags Flags;
        public int State;

#if TARGET_64BIT
        private const int PointerSize = 8;
#else
        private const int PointerSize = 4;
#endif

        private const int DataOffset = PointerSize /* Next */ + PointerSize /* Resume */ + 8 /* Flags + State */;

        // See note in ContinuationFlags above for the computation of these offsets.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetContinuationContext()
        {
            const uint mask = (1u << (int)ContinuationFlags.ContinuationContextIndexNumBits) - 1;
            uint index = ((uint)Flags >> (int)ContinuationFlags.ContinuationContextIndexFirstBit) & mask;
            ref byte data = ref RuntimeHelpers.GetRawData(this);
            return Unsafe.As<byte, object>(ref Unsafe.Add(ref data, (DataOffset - PointerSize) + index * PointerSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasException()
        {
            const uint mask = (1u << (int)ContinuationFlags.ExceptionIndexNumBits) - 1;
            return ((uint)Flags & (mask << (int)ContinuationFlags.ExceptionIndexFirstBit)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetExecutionContext(out ExecutionContext execContext)
        {
            const uint mask = (1u << (int)ContinuationFlags.ExecutionContextIndexNumBits) - 1;
            uint index = ((uint)Flags >> (int)ContinuationFlags.ExecutionContextIndexFirstBit) & mask;
            if (index == 0)
            {
                execContext = null;
                return false;
            }
            ref byte data = ref RuntimeHelpers.GetRawData(this);
            execContext = Unsafe.As<byte, ExecutionContext?>(ref Unsafe.Add(ref data, (DataOffset - PointerSize) + index * PointerSize));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception ex)
        {
            const uint mask = (1u << (int)ContinuationFlags.ExceptionIndexNumBits) - 1;
            uint index = ((uint)Flags >> (int)ContinuationFlags.ExceptionIndexFirstBit) & mask;
            ref byte data = ref RuntimeHelpers.GetRawData(this);
            Unsafe.As<byte, Exception>(ref Unsafe.Add(ref data, (DataOffset - PointerSize) + index * PointerSize)) = ex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref byte GetResultStorageOrNull()
        {
            const uint mask = (1u << (int)ContinuationFlags.ResultIndexNumBits) - 1;
            uint index = ((uint)Flags >> (int)ContinuationFlags.ResultIndexFirstBit) & mask;
            if (index == 0)
                return ref Unsafe.NullRef<byte>();
            ref byte data = ref RuntimeHelpers.GetRawData(this);
            return ref Unsafe.Add(ref data, (DataOffset - PointerSize) + index * PointerSize);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe ref struct AsyncDispatcherInfo
    {
        // Dispatcher info for next dispatcher present on stack, or
        // null if none.
        [FieldOffset(0)]
        public AsyncDispatcherInfo* Next;

        // Next continuation the dispatcher will process.
#if TARGET_64BIT
        [FieldOffset(8)]
#else
        [FieldOffset(4)]
#endif
        public Continuation? NextContinuation;
#if TARGET_64BIT
        [FieldOffset(16)]
#else
        [FieldOffset(8)]
#endif
        // The runtime async Task being dispatched.
        // This is used by debuggers in the case of nested dispatcher info (multiple runtime-async Tasks on the same thread)
        // to match an inflight Task to the corresponding Continuation chain.
        public Task? CurrentTask;

#if TARGET_64BIT
        [FieldOffset(24)]
#else
        [FieldOffset(12)]
#endif
        public AsyncProfiler.Info AsyncProfilerInfo;

        // Information about current task dispatching, to be used for async
        // stackwalking.
        [ThreadStatic]
        internal static unsafe AsyncDispatcherInfo* t_current; // Debugger depends on the exact name of this field.
    }

    internal static class AsyncProfiler
    {
        internal ref struct Info
        {
            public ulong DispatcherId;
            public object? Context;
            public object? CurrentContinuation;
            public IAsyncStateMachineBox? LastContinuation;
            public ref nint ContinuationTable;
            public uint ContinuationIndex;
            public bool CurrentContinuationCompleted;
            public bool CurrentContinuationResumes;
            public bool ReachedLastContinuation;
        }
    }

    [Flags]
    internal enum ContinuationFlags
    {
        ContinueOnThreadPool = 1 << 0,
        ContinueOnCapturedSynchronizationContext = 1 << 1,
        ContinueOnCapturedTaskScheduler = 1 << 2,
        // This is an await of valueTask.AsTask() (e.g. valueTask.AsTask()
        // returned from an async version). This flag affects how
        // ValueTaskSourceContinuation handling computes the flags to pass to
        // IValueTaskSource.OnCompleted.
        ValueTaskAdaptedToTask = 1 << 3,

        AllContinuationFlags = ContinueOnThreadPool | ContinueOnCapturedSynchronizationContext | ContinueOnCapturedTaskScheduler,

        // The flags encode where in the continuation various members are stored.
        // If the encoded index is 0, it means no such member is present.
        // Otherwise the exact offset of the member is computed as
        //   DataOffset + (index - 1) * PointerSize
        //
        ExecutionContextIndexFirstBit = 4,
        ExecutionContextIndexNumBits = 2,

        ContinuationContextIndexFirstBit = 6,
        ContinuationContextIndexNumBits = 2,

        ExceptionIndexFirstBit = 8,
        ExceptionIndexNumBits = 3,

        // For JIT, the continuation stores space for every possible type of
        // async callee's result. We need to represent the offset to each of
        // these, so we allocate the rest of the bits for this.
        ResultIndexFirstBit = 11,
        ResultIndexNumBits = 21,
    }


    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class EnumeratorCancellationAttribute : Attribute
    {
    }


    public interface IAsyncStateMachine
    {
        void MoveNext();

        void SetStateMachine(IAsyncStateMachine stateMachine);
    }


    /// <summary>
    /// An interface implemented by all <see cref="AsyncTaskMethodBuilder{TResult}.AsyncStateMachineBox{TStateMachine}"/> instances, regardless of generics.
    /// </summary>
    internal interface IAsyncStateMachineBox
    {
        /// <summary>Move the state machine forward.</summary>
        void MoveNext();

        /// <summary>
        /// Gets an action for moving forward the contained state machine.
        /// This will lazily-allocate the delegate as needed.
        /// </summary>
        Action MoveNextAction { get; }

        /// <summary>Gets the state machine as a boxed object.  This should only be used for debugging purposes.</summary>
        IAsyncStateMachine GetStateMachineObject();

        /// <summary>Clears the state of the box.</summary>
        void ClearStateUponCompletion();

        /// <summary>Gets the state machine diagnostic data.</summary>
        bool GetDiagnosticData(out ulong methodId, out int state, out object? nextContinuation);
    }


    /// <summary>
    /// Marker interface used to know whether a particular awaiter is either a
    /// CTA.ConfiguredTaskAwaiter or a CTA`1.ConfiguredTaskAwaiter.  It must not
    /// be implemented by any other awaiters.
    /// </summary>
    internal interface IConfiguredTaskAwaiter { }


    public interface ICriticalNotifyCompletion : INotifyCompletion
    {
        void UnsafeOnCompleted(Action continuation);
    }


    public interface INotifyCompletion
    {
        void OnCompleted(Action continuation);
    }


    internal interface IStateMachineBoxAwareAwaiter
    {
        void AwaitUnsafeOnCompleted(IAsyncStateMachineBox box);
    }


    /// <summary>
    /// Defines a property for accessing the value that an object references.
    /// </summary>
    public interface IStrongBox
    {
        /// <summary>
        /// Gets or sets the value the object references.
        /// </summary>
        object? Value { get; set; }
    }


    /// <summary>
    /// Marker interface used to know whether a particular awaiter is either a
    /// TaskAwaiter or a TaskAwaiter`1.  It must not be implemented by any other
    /// awaiters.
    /// </summary>
    internal interface ITaskAwaiter { }


    [StructLayout(LayoutKind.Auto)]
    public struct PoolingAsyncValueTaskMethodBuilder
    {
        private static readonly PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.StateMachineBox s_syncSuccessSentinel = PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.s_syncSuccessSentinel;

        private PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.StateMachineBox m_task;

        public ValueTask Task
        {
            get
            {
                if (m_task == s_syncSuccessSentinel)
                {
                    return default(ValueTask);
                }
                PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.StateMachineBox stateMachineBox = m_task ?? (m_task = PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.CreateWeaklyTypedStateMachineBox());
                return new ValueTask(stateMachineBox, stateMachineBox.Version);
            }
        }

        internal object ObjectIdForDebugger => m_task ?? (m_task = PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.CreateWeaklyTypedStateMachineBox());

        public static PoolingAsyncValueTaskMethodBuilder Create()
        {
            return default(PoolingAsyncValueTaskMethodBuilder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            AsyncMethodBuilderCore.SetStateMachine(stateMachine, null);
        }

        public void SetResult()
        {
            if (m_task is null)
            {
                m_task = s_syncSuccessSentinel;
            }
            else
            {
                m_task.SetResult(default(VoidTaskResult));
            }
        }

        public void SetException(Exception exception)
        {
            PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.SetException(exception, ref m_task);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            PoolingAsyncValueTaskMethodBuilder<VoidTaskResult>.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public struct PoolingAsyncValueTaskMethodBuilder<TResult>
    {
        internal abstract class StateMachineBox : IValueTaskSource<TResult>, IValueTaskSource
        {
            protected Action _moveNextAction;

            public ExecutionContext Context;

            protected ManualResetValueTaskSourceCore<TResult> _valueTaskSource;

            public short Version => _valueTaskSource.Version;

            public void SetResult(TResult result)
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    AsyncStateMachineDispatcherInfo.CompleteAsyncMethod(this, flags);
                }
                _valueTaskSource.SetResult(result);
            }

            public void SetException(Exception error)
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    AsyncStateMachineDispatcherInfo.UnwindAsyncFrame(this, flags);
                }
                _valueTaskSource.SetException(error);
            }

            public ValueTaskSourceStatus GetStatus(short token)
            {
                return _valueTaskSource.GetStatus(token);
            }

            public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
            {
                _valueTaskSource.OnCompleted(continuation, state, token, flags);
            }

            TResult IValueTaskSource<TResult>.GetResult(short token)
            {
                throw NotImplemented.ByDesign;
            }

            void IValueTaskSource.GetResult(short token)
            {
                throw NotImplemented.ByDesign;
            }
        }

        private sealed class SyncSuccessSentinelStateMachineBox : StateMachineBox
        {
            public SyncSuccessSentinelStateMachineBox()
            {
                SetResult(default(TResult));
            }
        }

        private sealed class StateMachineBox<TStateMachine> : StateMachineBox, IValueTaskSource<TResult>, IValueTaskSource, IAsyncStateMachineBox, IThreadPoolWorkItem where TStateMachine : IAsyncStateMachine
        {
            private static readonly ContextCallback s_callback = ExecutionContextCallback;

            private static readonly PaddedReference[] s_perCoreCache = new PaddedReference[Environment.ProcessorCount];

            [ThreadStatic]
            private static StateMachineBox<TStateMachine> t_tlsCache;

            public TStateMachine StateMachine;

            private static ref StateMachineBox<TStateMachine> PerCoreCacheSlot
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    int num = (int)((uint)Thread.GetCurrentProcessorId() % (uint)Environment.ProcessorCount);
                    return ref Unsafe.As<object, StateMachineBox<TStateMachine>>(ref s_perCoreCache[num].Object);
                }
            }

            public Action MoveNextAction => _moveNextAction ?? (_moveNextAction = MoveNext);

            private object ContinuationForDiagnostics
            {
                get
                {
                    object continuationForDiagnostics = _valueTaskSource.ContinuationForDiagnostics;
                    if (continuationForDiagnostics != this)
                    {
                        return continuationForDiagnostics;
                    }
                    return null;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static StateMachineBox<TStateMachine> RentFromCache()
            {
                StateMachineBox<TStateMachine> stateMachineBox = t_tlsCache;
                if (stateMachineBox is not null)
                {
                    t_tlsCache = null;
                }
                else
                {
                    ref StateMachineBox<TStateMachine> perCoreCacheSlot = ref PerCoreCacheSlot;
                    if (perCoreCacheSlot is null || (stateMachineBox = Interlocked.Exchange(ref perCoreCacheSlot, null)) is null)
                    {
                        stateMachineBox = new StateMachineBox<TStateMachine>();
                    }
                }
                return stateMachineBox;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ReturnToCache()
            {
                ClearStateUponCompletion();
                _valueTaskSource.Reset();
                if (t_tlsCache is null)
                {
                    t_tlsCache = this;
                    return;
                }
                ref StateMachineBox<TStateMachine> perCoreCacheSlot = ref PerCoreCacheSlot;
                if (perCoreCacheSlot is null)
                {
                    Volatile.Write(ref perCoreCacheSlot, this);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ClearStateUponCompletion()
            {
                StateMachine = default(TStateMachine);
                Context = null;
            }

            private static void ExecutionContextCallback(object s)
            {
                Unsafe.As<StateMachineBox<TStateMachine>>(s).StateMachine.MoveNext();
            }

            void IThreadPoolWorkItem.Execute()
            {
                MoveNext();
            }

            public void MoveNext()
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    AsyncStateMachineDispatcherInfo.ResumeAsyncMethod(this, flags);
                }
                ExecutionContext context = Context;
                if (context == ExecutionContext.DefaultFlowSuppressed)
                {
                    StateMachine.MoveNext();
                }
                else
                {
                    ExecutionContext.RunInternal(context, s_callback, this);
                }
            }

            TResult IValueTaskSource<TResult>.GetResult(short token)
            {
                try
                {
                    return _valueTaskSource.GetResult(token);
                }
                finally
                {
                    ReturnToCache();
                }
            }

            void IValueTaskSource.GetResult(short token)
            {
                try
                {
                    _valueTaskSource.GetResult(token);
                }
                finally
                {
                    ReturnToCache();
                }
            }

            IAsyncStateMachine IAsyncStateMachineBox.GetStateMachineObject()
            {
                return StateMachine;
            }

            bool IAsyncStateMachineBox.GetDiagnosticData(out ulong methodId, out int state, out object nextContinuation)
            {
                if (AsyncStateMachineDispatcherInfo.IsSupported)
                {
                    methodId = AsyncStateMachineDiagnostics<TStateMachine>.MethodId;
                    state = AsyncStateMachineDiagnostics<TStateMachine>.GetState(ref StateMachine);
                    nextContinuation = ContinuationForDiagnostics;
                    return true;
                }
                methodId = 0uL;
                state = -1;
                nextContinuation = null;
                return false;
            }
        }

        internal static readonly StateMachineBox s_syncSuccessSentinel = new SyncSuccessSentinelStateMachineBox();

        private StateMachineBox m_task;

        private TResult _result;

        public ValueTask<TResult> Task
        {
            get
            {
                if (m_task == s_syncSuccessSentinel)
                {
                    return new ValueTask<TResult>(_result);
                }
                StateMachineBox stateMachineBox = m_task ?? (m_task = CreateWeaklyTypedStateMachineBox());
                return new ValueTask<TResult>(stateMachineBox, stateMachineBox.Version);
            }
        }

        internal object ObjectIdForDebugger => m_task ?? (m_task = CreateWeaklyTypedStateMachineBox());

        public static PoolingAsyncValueTaskMethodBuilder<TResult> Create()
        {
            return default(PoolingAsyncValueTaskMethodBuilder<TResult>);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            AsyncMethodBuilderCore.SetStateMachine(stateMachine, null);
        }

        public void SetResult(TResult result)
        {
            if (m_task is null)
            {
                _result = result;
                m_task = s_syncSuccessSentinel;
            }
            else
            {
                m_task.SetResult(result);
            }
        }

        public void SetException(Exception exception)
        {
            SetException(exception, ref m_task);
        }

        internal static void SetException(Exception exception, [NotNull] ref StateMachineBox boxFieldRef)
        {
            if (exception is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.exception);
            }
            (boxFieldRef ?? (boxFieldRef = CreateWeaklyTypedStateMachineBox())).SetException(exception);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        internal static void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine, ref StateMachineBox box) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            try
            {
                IAsyncStateMachineBox asyncStateMachineBox = GetStateMachineBox(ref stateMachine, ref box);
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    asyncStateMachineBox = AsyncStateMachineDispatcherInfo.CreateDispatcher(asyncStateMachineBox, flags);
                }
                awaiter.OnCompleted(asyncStateMachineBox.MoveNextAction);
            }
            catch (Exception exception)
            {
                System.Threading.Tasks.Task.ThrowAsync(exception, null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine, [NotNull] ref StateMachineBox boxRef) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            IAsyncStateMachineBox stateMachineBox = GetStateMachineBox(ref stateMachine, ref boxRef);
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitUnsafeOnCompleted(ref awaiter, stateMachineBox);
        }

        private static IAsyncStateMachineBox GetStateMachineBox<TStateMachine>(ref TStateMachine stateMachine, [NotNull] ref StateMachineBox boxFieldRef) where TStateMachine : IAsyncStateMachine
        {
            ExecutionContext executionContext = ExecutionContext.CaptureForSuspension(Thread.CurrentThread);
            if (boxFieldRef is StateMachineBox<TStateMachine> stateMachineBox)
            {
                if (stateMachineBox.Context != executionContext)
                {
                    stateMachineBox.Context = executionContext;
                }
                return stateMachineBox;
            }
            if (boxFieldRef is StateMachineBox<IAsyncStateMachine> stateMachineBox2)
            {
                if (stateMachineBox2.StateMachine is null)
                {
                    Debugger.NotifyOfCrossThreadDependency();
                    stateMachineBox2.StateMachine = stateMachine;
                }
                stateMachineBox2.Context = executionContext;
                return stateMachineBox2;
            }
            Debugger.NotifyOfCrossThreadDependency();
            StateMachineBox<TStateMachine> stateMachineBox3 = (StateMachineBox<TStateMachine>)(boxFieldRef = StateMachineBox<TStateMachine>.RentFromCache());
            stateMachineBox3.StateMachine = stateMachine;
            stateMachineBox3.Context = executionContext;
            return stateMachineBox3;
        }

        internal static StateMachineBox CreateWeaklyTypedStateMachineBox()
        {
            return new StateMachineBox<IAsyncStateMachine>();
        }
    }


    // Keep in sync with CORINFO_AsyncResumeInfo in corinfo.h
    internal unsafe struct ResumeInfo
    {
        public delegate*<Continuation, ref byte, Continuation?> Resume;
        // IP to use for diagnostics. Can be null for hand-rolled continuations
        // like ValueTaskSourceContinuation.
        // For normal JIT-created continuations this points into the jitted
        // suspension code.
        // For debug codegen the IP resolves via an ASYNC native->IL mapping to
        // the IL AsyncHelpers.Await (or other async function) call which
        // caused the suspension.
        // For optimized codegen the mapping into the root method may be more
        // approximate (e.g. because of inlining).
        // For all codegens the offset of DiagnosticsIP matches
        // DiagnosticNativeOffset for the corresponding AsyncSuspensionPoint in
        // the debug info.
        public void* DiagnosticIP;
    }


    internal sealed class RuntimeAsyncTaskContinuation : Continuation, IThreadPoolWorkItem
    {
        private static class TaskContinuationResume
        {
            [FixedAddressValueType]
            public unsafe static readonly ResumeInfo ResumeInfo = new ResumeInfo
            {
                DiagnosticIP = null,
                Resume = &ResumeTaskContinuation
            };

            internal static readonly SendOrPostCallback s_postCallback = delegate(object state)
            {
                ((Task)state).ExecuteDirectly(null);
            };

            private static Continuation ResumeTaskContinuation(Continuation cont, ref byte result)
            {
                RuntimeAsyncTaskContinuation obj = (RuntimeAsyncTaskContinuation)cont;
                obj.Next = null;
                obj.RuntimeAsyncTask = null;
                obj.ContinuationContext = null;
                AsyncHelpers.ReturnTaskContinuation(obj);
                obj.GetResult(ref result);
                return null;
            }
        }

        internal Task Task;

        internal Task RuntimeAsyncTask;

        private unsafe delegate*<Task, ref byte, void> _getResult;

        internal object ContinuationContext;

        public unsafe RuntimeAsyncTaskContinuation()
        {
            ResumeInfo = (ResumeInfo*)Unsafe.AsPointer(in TaskContinuationResume.ResumeInfo);
        }

        void IThreadPoolWorkItem.Execute()
        {
            Execute(canInline: true);
        }

        internal void Execute(bool canInline)
        {
            if ((Flags & ContinuationFlags.AllContinuationFlags) == 0 || !QueueIfNecessary(canInline))
            {
                if (canInline)
                {
                    RuntimeAsyncTask.ExecuteDirectly(null);
                }
                else
                {
                    ThreadPool.UnsafeQueueUserWorkItemInternal(RuntimeAsyncTask, preferLocal: true);
                }
            }
        }

        private bool QueueIfNecessary(bool canInline)
        {
            if ((Flags & ContinuationFlags.ContinueOnThreadPool) != 0)
            {
                Flags &= ~ContinuationFlags.ContinueOnThreadPool;
                SynchronizationContext synchronizationContext = Thread.CurrentThread._synchronizationContext;
                if (synchronizationContext is null || synchronizationContext.GetType() == typeof(SynchronizationContext))
                {
                    TaskScheduler internalCurrent = TaskScheduler.InternalCurrent;
                    if (internalCurrent is null || internalCurrent == TaskScheduler.Default)
                    {
                        return false;
                    }
                }
                ThreadPool.UnsafeQueueUserWorkItemInternal(RuntimeAsyncTask, preferLocal: true);
                return true;
            }
            if ((Flags & ContinuationFlags.ContinueOnCapturedSynchronizationContext) != 0)
            {
                Flags &= ~ContinuationFlags.ContinueOnCapturedSynchronizationContext;
                SynchronizationContext synchronizationContext2 = (SynchronizationContext)ContinuationContext;
                if (canInline && synchronizationContext2 == Thread.CurrentThread._synchronizationContext)
                {
                    return false;
                }
                try
                {
                    synchronizationContext2.Post(TaskContinuationResume.s_postCallback, RuntimeAsyncTask);
                }
                catch (Exception exception)
                {
                    Task.ThrowAsync(exception, null);
                }
                return true;
            }
            if ((Flags & ContinuationFlags.ContinueOnCapturedTaskScheduler) != 0)
            {
                Flags &= ~ContinuationFlags.ContinueOnCapturedTaskScheduler;
                TaskScheduler scheduler = (TaskScheduler)ContinuationContext;
                TaskSchedulerAwaitTaskContinuation.RunOrScheduleAction((Action)RuntimeAsyncTask.m_action, scheduler, null, canInline);
                return true;
            }
            return false;
        }

        public unsafe void GetResult(ref byte returnValue)
        {
            Task task = Task;
            Task = null;
            _getResult(task, ref returnValue);
        }

        public unsafe void Initialize(Task task)
        {
            Task = task;
            _getResult = &GetResult;
        }

        public unsafe void Initialize<T>(Task<T> task)
        {
            Task = task;
            _getResult = &GetResult<T>;
        }

        private static void GetResult(Task task, ref byte result)
        {
            TaskAwaiter.ValidateEnd(task);
        }

        private static void GetResult<T>(Task task, ref byte result)
        {
            Task<T> task2 = Unsafe.As<Task, Task<T>>(ref task);
            TaskAwaiter.ValidateEnd(task2);
            Unsafe.As<byte, T>(ref result) = task2.ResultOnSuccess;
        }
    }


    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class StateMachineAttribute : Attribute
    {
        public Type StateMachineType { get; }

        public StateMachineAttribute(Type stateMachineType)
        {
            StateMachineType = stateMachineType;
        }
    }


    /// <summary>
    /// Holds a reference to a value.
    /// </summary>
    /// <typeparam name="T">The type of the value that the <see cref = "StrongBox{T}"></see> references.</typeparam>
    public class StrongBox<T> : IStrongBox
    {
        /// <summary>
        /// Gets the strongly typed value associated with the <see cref = "StrongBox{T}"></see>
        /// <remarks>This is explicitly exposed as a field instead of a property to enable loading the address of the field.</remarks>
        /// </summary>
        [MaybeNull] public T Value = default!;

        /// <summary>
        /// Initializes a new StrongBox which can receive a value when used in a reference call.
        /// </summary>
        public StrongBox()
        {
        }

        /// <summary>
        /// Initializes a new <see cref = "StrongBox{T}"></see> with the specified value.
        /// </summary>
        /// <param name="value">A value that the <see cref = "StrongBox{T}"></see> will reference.</param>
        public StrongBox(T value)
        {
            Value = value;
        }

        object? IStrongBox.Value
        {
            get => Value;
            set => Value = (T)value!;
        }
    }


    public readonly struct TaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion, ITaskAwaiter
    {
        internal readonly Task m_task;

        public bool IsCompleted => m_task.IsCompleted;

        internal TaskAwaiter(Task task)
        {
            m_task = task;
        }

        public void OnCompleted(Action continuation)
        {
            OnCompletedInternal(m_task, continuation, continueOnCapturedContext: true, flowExecutionContext: true);
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompletedInternal(m_task, continuation, continueOnCapturedContext: true, flowExecutionContext: false);
        }

        public void GetResult()
        {
            ValidateEnd(m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ValidateEnd(Task task, ConfigureAwaitOptions options = ConfigureAwaitOptions.None)
        {
            if (task.IsWaitNotificationEnabledOrNotRanToCompletion)
            {
                HandleNonSuccessAndDebuggerNotification(task, options);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
        {
            if (!task.IsCompleted)
            {
                task.InternalWait(-1, default(CancellationToken));
            }
            task.NotifyDebuggerOfWaitCompletionIfNecessary();
            if (!task.IsCompletedSuccessfully)
            {
                if ((options & ConfigureAwaitOptions.SuppressThrowing) == 0)
                {
                    ThrowForNonSuccess(task);
                }
                task.MarkExceptionsAsHandled();
            }
        }

        private static void ThrowForNonSuccess(Task task)
        {
            switch (task.Status)
            {
            case TaskStatus.Canceled:
                task.GetCancellationExceptionDispatchInfo()?.Throw();
                throw new TaskCanceledException(task);
            case TaskStatus.Faulted:
            {
                List<ExceptionDispatchInfo> exceptionDispatchInfos = task.GetExceptionDispatchInfos();
                if (exceptionDispatchInfos.Count > 0)
                {
                    exceptionDispatchInfos[0].Throw();
                    break;
                }
                throw task.Exception;
            }
            }
        }

        internal static void OnCompletedInternal(Task task, Action continuation, bool continueOnCapturedContext, bool flowExecutionContext)
        {
            ArgumentNullException.ThrowIfNull(continuation);
            if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && (AsyncInstrumentation.IsEnabled.Tpl(flags) || AsyncInstrumentation.IsEnabled.AsyncDebugger(flags)))
            {
                continuation = OutputWaitEtwEvents(task, continuation);
            }
            task.SetContinuationForAwait(continuation, continueOnCapturedContext, flowExecutionContext);
        }

        internal static void UnsafeOnCompletedInternal(Task task, IAsyncStateMachineBox stateMachineBox, bool continueOnCapturedContext)
        {
            if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags))
            {
                if (AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    if (!(task is IAsyncStateMachineBox))
                    {
                        stateMachineBox = AsyncStateMachineDispatcherInfo.CreateDispatcher(stateMachineBox, flags);
                    }
                    else
                    {
                        bool flag = false;
                        if (continueOnCapturedContext)
                        {
                            SynchronizationContext current = SynchronizationContext.Current;
                            bool num = current is not null && current.GetType() != typeof(SynchronizationContext);
                            TaskScheduler internalCurrent = TaskScheduler.InternalCurrent;
                            bool flag2 = internalCurrent is not null && internalCurrent != TaskScheduler.Default;
                            flag = num | flag2;
                        }
                        if (flag)
                        {
                            stateMachineBox = AsyncStateMachineDispatcherInfo.CreateDispatcher(stateMachineBox, flags);
                        }
                    }
                }
                if (AsyncInstrumentation.IsEnabled.Tpl(flags) || AsyncInstrumentation.IsEnabled.AsyncDebugger(flags))
                {
                    task.SetContinuationForAwait(OutputWaitEtwEvents(task, stateMachineBox.MoveNextAction), continueOnCapturedContext, flowExecutionContext: false);
                    return;
                }
            }
            task.UnsafeSetContinuationForAwait(stateMachineBox, continueOnCapturedContext);
        }

        private static Action OutputWaitEtwEvents(Task task, Action continuation)
        {
            if (Task.s_asyncDebuggingEnabled)
            {
                Task.AddToActiveTasks(task);
            }
            TplEventSource log = TplEventSource.Log;
            if (log.IsEnabled())
            {
                Task internalCurrent = Task.InternalCurrent;
                Task task2 = AsyncMethodBuilderCore.TryGetContinuationTask(continuation);
                log.TaskWaitBegin(internalCurrent?.m_taskScheduler.Id ?? TaskScheduler.Default.Id, internalCurrent?.Id ?? 0, task.Id, TplEventSource.TaskWaitBehavior.Asynchronous, task2?.Id ?? 0);
            }
            return AsyncMethodBuilderCore.CreateContinuationWrapper(continuation, delegate(Action innerContinuation, Task innerTask)
            {
                if (Task.s_asyncDebuggingEnabled)
                {
                    Task.RemoveFromActiveTasks(innerTask);
                }
                TplEventSource log2 = TplEventSource.Log;
                Guid oldActivityThatWillContinue = default(Guid);
                bool num = log2.IsEnabled();
                if (num)
                {
                    Task internalCurrent2 = Task.InternalCurrent;
                    log2.TaskWaitEnd(internalCurrent2?.m_taskScheduler.Id ?? TaskScheduler.Default.Id, internalCurrent2?.Id ?? 0, innerTask.Id);
                    if (log2.TasksSetActivityIds && (innerTask.Options & (TaskCreationOptions)0x400) != TaskCreationOptions.None)
                    {
                        EventSource.SetCurrentThreadActivityId(TplEventSource.CreateGuidForTaskID(innerTask.Id), out oldActivityThatWillContinue);
                    }
                }
                innerContinuation();
                if (num)
                {
                    log2.TaskWaitContinuationComplete(innerTask.Id);
                    if (log2.TasksSetActivityIds && (innerTask.Options & (TaskCreationOptions)0x400) != TaskCreationOptions.None)
                    {
                        EventSource.SetCurrentThreadActivityId(oldActivityThatWillContinue);
                    }
                }
            }, task);
        }
    }

    public readonly struct TaskAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion, ITaskAwaiter
    {
        private readonly Task<TResult> m_task;

        public bool IsCompleted => m_task.IsCompleted;

        internal TaskAwaiter(Task<TResult> task)
        {
            m_task = task;
        }

        public void OnCompleted(Action continuation)
        {
            TaskAwaiter.OnCompletedInternal(m_task, continuation, continueOnCapturedContext: true, flowExecutionContext: true);
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            TaskAwaiter.OnCompletedInternal(m_task, continuation, continueOnCapturedContext: true, flowExecutionContext: false);
        }

        public TResult GetResult()
        {
            TaskAwaiter.ValidateEnd(m_task);
            return m_task.ResultOnSuccess;
        }
    }


    public readonly struct ValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion, IStateMachineBoxAwareAwaiter
    {
        internal static readonly Action<object> s_invokeActionDelegate = delegate(object state)
        {
            if (state is Action action)
            {
                action();
            }
            else
            {
                ThrowHelper.ThrowUnexpectedStateForKnownCallback(state);
            }
        };

        private readonly ValueTask _value;

        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _value.IsCompleted;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueTaskAwaiter(in ValueTask value)
        {
            _value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult()
        {
            _value.ThrowIfCompletedUnsuccessfully();
        }

        public void OnCompleted(Action continuation)
        {
            object obj = _value._obj;
            if (obj is Task task)
            {
                task.GetAwaiter().OnCompleted(continuation);
            }
            else if (obj is not null)
            {
                Unsafe.As<IValueTaskSource>(obj).OnCompleted(s_invokeActionDelegate, continuation, _value._token, ValueTaskSourceOnCompletedFlags.UseSchedulingContext | ValueTaskSourceOnCompletedFlags.FlowExecutionContext);
            }
            else
            {
                Task.CompletedTask.GetAwaiter().OnCompleted(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            object obj = _value._obj;
            if (obj is Task task)
            {
                task.GetAwaiter().UnsafeOnCompleted(continuation);
            }
            else if (obj is not null)
            {
                Unsafe.As<IValueTaskSource>(obj).OnCompleted(s_invokeActionDelegate, continuation, _value._token, ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
            }
            else
            {
                Task.CompletedTask.GetAwaiter().UnsafeOnCompleted(continuation);
            }
        }

        void IStateMachineBoxAwareAwaiter.AwaitUnsafeOnCompleted(IAsyncStateMachineBox box)
        {
            object obj = _value._obj;
            if (obj is Task task)
            {
                TaskAwaiter.UnsafeOnCompletedInternal(task, box, continueOnCapturedContext: true);
            }
            else if (obj is not null)
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && !(obj is IAsyncStateMachineBox) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                }
                Unsafe.As<IValueTaskSource>(obj).OnCompleted(ThreadPool.s_invokeAsyncStateMachineBox, box, _value._token, ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
            }
            else
            {
                TaskAwaiter.UnsafeOnCompletedInternal(Task.CompletedTask, box, continueOnCapturedContext: true);
            }
        }
    }

    public readonly struct ValueTaskAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion, IStateMachineBoxAwareAwaiter
    {
        private readonly ValueTask<TResult> _value;

        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _value.IsCompleted;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueTaskAwaiter(in ValueTask<TResult> value)
        {
            _value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult GetResult()
        {
            return _value.Result;
        }

        public void OnCompleted(Action continuation)
        {
            object obj = _value._obj;
            if (obj is Task<TResult> task)
            {
                task.GetAwaiter().OnCompleted(continuation);
            }
            else if (obj is not null)
            {
                Unsafe.As<IValueTaskSource<TResult>>(obj).OnCompleted(ValueTaskAwaiter.s_invokeActionDelegate, continuation, _value._token, ValueTaskSourceOnCompletedFlags.UseSchedulingContext | ValueTaskSourceOnCompletedFlags.FlowExecutionContext);
            }
            else
            {
                Task.CompletedTask.GetAwaiter().OnCompleted(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            object obj = _value._obj;
            if (obj is Task<TResult> task)
            {
                task.GetAwaiter().UnsafeOnCompleted(continuation);
            }
            else if (obj is not null)
            {
                Unsafe.As<IValueTaskSource<TResult>>(obj).OnCompleted(ValueTaskAwaiter.s_invokeActionDelegate, continuation, _value._token, ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
            }
            else
            {
                Task.CompletedTask.GetAwaiter().UnsafeOnCompleted(continuation);
            }
        }

        void IStateMachineBoxAwareAwaiter.AwaitUnsafeOnCompleted(IAsyncStateMachineBox box)
        {
            object obj = _value._obj;
            if (obj is Task<TResult> task)
            {
                TaskAwaiter.UnsafeOnCompletedInternal(task, box, continueOnCapturedContext: true);
            }
            else if (obj is not null)
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags) && !(obj is IAsyncStateMachineBox) && AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                {
                    box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                }
                Unsafe.As<IValueTaskSource<TResult>>(obj).OnCompleted(ThreadPool.s_invokeAsyncStateMachineBox, box, _value._token, ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
            }
            else
            {
                TaskAwaiter.UnsafeOnCompletedInternal(Task.CompletedTask, box, continueOnCapturedContext: true);
            }
        }
    }


    [StructLayout(LayoutKind.Sequential, Size = 1)]
    public readonly struct YieldAwaitable
    {
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        [Intrinsic]
        public readonly struct YieldAwaiter : ICriticalNotifyCompletion, INotifyCompletion, IStateMachineBoxAwareAwaiter
        {
            private static readonly WaitCallback s_waitCallbackRunAction = RunAction;

            private static readonly SendOrPostCallback s_sendOrPostCallbackRunAction = RunAction;

            public bool IsCompleted => false;

            public void OnCompleted(Action continuation)
            {
                QueueContinuation(continuation, flowContext: true);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                QueueContinuation(continuation, flowContext: false);
            }

            private static void QueueContinuation(Action continuation, bool flowContext)
            {
                ArgumentNullException.ThrowIfNull(continuation);
                if (TplEventSource.Log.IsEnabled())
                {
                    continuation = OutputCorrelationEtwEvent(continuation);
                }
                SynchronizationContext current = SynchronizationContext.Current;
                if (current is not null && current.GetType() != typeof(SynchronizationContext))
                {
                    current.Post(s_sendOrPostCallbackRunAction, continuation);
                    return;
                }
                TaskScheduler current2 = TaskScheduler.Current;
                if (current2 == TaskScheduler.Default)
                {
                    if (flowContext)
                    {
                        ThreadPool.QueueUserWorkItem(s_waitCallbackRunAction, continuation);
                    }
                    else
                    {
                        ThreadPool.UnsafeQueueUserWorkItem(s_waitCallbackRunAction, continuation);
                    }
                }
                else
                {
                    Task.Factory.StartNew(continuation, default(CancellationToken), TaskCreationOptions.PreferFairness, current2);
                }
            }

            void IStateMachineBoxAwareAwaiter.AwaitUnsafeOnCompleted(IAsyncStateMachineBox box)
            {
                if (AsyncInstrumentation.IsActive && AsyncInstrumentation.LoadFlags(out var flags))
                {
                    if (AsyncInstrumentation.IsEnabled.AsyncProfiler(flags))
                    {
                        box = AsyncStateMachineDispatcherInfo.CreateDispatcher(box, flags);
                    }
                    if (AsyncInstrumentation.IsEnabled.Tpl(flags))
                    {
                        QueueContinuation(box.MoveNextAction, flowContext: false);
                        return;
                    }
                }
                SynchronizationContext current = SynchronizationContext.Current;
                if (current is not null && current.GetType() != typeof(SynchronizationContext))
                {
                    current.Post(delegate(object s)
                    {
                        ((IAsyncStateMachineBox)s).MoveNext();
                    }, box);
                    return;
                }
                TaskScheduler current2 = TaskScheduler.Current;
                if (current2 == TaskScheduler.Default)
                {
                    ThreadPool.UnsafeQueueUserWorkItemInternal(box, preferLocal: false);
                    return;
                }
                Task.Factory.StartNew(delegate(object s)
                {
                    ((IAsyncStateMachineBox)s).MoveNext();
                }, box, default(CancellationToken), TaskCreationOptions.PreferFairness, current2);
            }

            private static Action OutputCorrelationEtwEvent(Action continuation)
            {
                return continuation;
            }

            private static void RunAction(object state)
            {
                ((Action)state)();
            }

            public void GetResult()
            {
            }
        }

        public YieldAwaiter GetAwaiter()
        {
            return default(YieldAwaiter);
        }
    }


    [StructLayout(LayoutKind.Auto)]
    public struct AsyncValueTaskMethodBuilder
    {
        private static readonly Task<VoidTaskResult> s_syncSuccessSentinel = AsyncValueTaskMethodBuilder<VoidTaskResult>.s_syncSuccessSentinel;

        private Task<VoidTaskResult> m_task;

        public ValueTask Task
        {
            get
            {
                if (m_task == s_syncSuccessSentinel)
                {
                    return default(ValueTask);
                }
                return new ValueTask(m_task ?? (m_task = new Task<VoidTaskResult>()));
            }
        }

        internal object ObjectIdForDebugger => m_task ?? (m_task = AsyncTaskMethodBuilder<VoidTaskResult>.CreateWeaklyTypedStateMachineBox());

        public static AsyncValueTaskMethodBuilder Create()
        {
            return default(AsyncValueTaskMethodBuilder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            AsyncMethodBuilderCore.SetStateMachine(stateMachine, null);
        }

        public void SetResult()
        {
            if (m_task is null)
            {
                m_task = s_syncSuccessSentinel;
            }
            else
            {
                AsyncTaskMethodBuilder<VoidTaskResult>.SetExistingTaskResult(m_task, default(VoidTaskResult));
            }
        }

        public void SetException(Exception exception)
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.SetException(exception, ref m_task);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<VoidTaskResult>.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public struct AsyncValueTaskMethodBuilder<TResult>
    {
        internal static readonly Task<TResult> s_syncSuccessSentinel = new Task<TResult>(default(TResult));

        private Task<TResult> m_task;

        private TResult _result;

        public ValueTask<TResult> Task
        {
            get
            {
                if (m_task == s_syncSuccessSentinel)
                {
                    return new ValueTask<TResult>(_result);
                }
                return new ValueTask<TResult>(m_task ?? (m_task = new Task<TResult>()));
            }
        }

        internal object ObjectIdForDebugger => m_task ?? (m_task = AsyncTaskMethodBuilder<TResult>.CreateWeaklyTypedStateMachineBox());

        public static AsyncValueTaskMethodBuilder<TResult> Create()
        {
            return default(AsyncValueTaskMethodBuilder<TResult>);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            AsyncMethodBuilderCore.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            AsyncMethodBuilderCore.SetStateMachine(stateMachine, null);
        }

        public void SetResult(TResult result)
        {
            if (m_task is null)
            {
                _result = result;
                m_task = s_syncSuccessSentinel;
            }
            else
            {
                AsyncTaskMethodBuilder<TResult>.SetExistingTaskResult(m_task, result);
            }
        }

        public void SetException(Exception exception)
        {
            AsyncTaskMethodBuilder<TResult>.SetException(exception, ref m_task);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<TResult>.AwaitOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            AsyncTaskMethodBuilder<TResult>.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine, ref m_task);
        }
    }

    /// <summary>Provides the instrumentation flags consulted by async method builders and runtime async.</summary>
    internal static class AsyncInstrumentation
    {
        [Flags]
        public enum Flags : uint
        {
            Disabled = 0x0,

            CreateAsyncContext = 0x1,
            ResumeAsyncContext = 0x2,
            SuspendAsyncContext = 0x4,
            CompleteAsyncContext = 0x8,
            UnwindAsyncException = 0x10,
            ResumeAsyncMethod = 0x20,
            CompleteAsyncMethod = 0x40,

            AsyncProfiler = 0x1000000,
            AsyncDebugger = 0x2000000,
            Tpl = 0x4000000,

            Synchronize = 0x80000000
        }

        public static class IsEnabled
        {
            public static bool CreateAsyncContext(Flags flags) => (Flags.CreateAsyncContext & flags) != 0;
            public static bool ResumeAsyncContext(Flags flags) => (Flags.ResumeAsyncContext & flags) != 0;
            public static bool SuspendAsyncContext(Flags flags) => (Flags.SuspendAsyncContext & flags) != 0;
            public static bool CompleteAsyncContext(Flags flags) => (Flags.CompleteAsyncContext & flags) != 0;
            public static bool UnwindAsyncException(Flags flags) => (Flags.UnwindAsyncException & flags) != 0;
            public static bool ResumeAsyncMethod(Flags flags) => (Flags.ResumeAsyncMethod & flags) != 0;
            public static bool CompleteAsyncMethod(Flags flags) => (Flags.CompleteAsyncMethod & flags) != 0;
            public static bool AsyncProfiler(Flags flags) => IsAsyncProfilerSupported && (Flags.AsyncProfiler & flags) != 0;
            public static bool AsyncDebugger(Flags flags) => IsAsyncDebuggerSupported && (Flags.AsyncDebugger & flags) != 0;
            public static bool Tpl(Flags flags) => IsTplSupported && (Flags.Tpl & flags) != 0;
        }

        public static bool IsAsyncProfilerSupported => false;

        public static bool IsAsyncDebuggerSupported => false;

        public static bool IsTplSupported => false;

        public static bool IsSupported => false;

        public static bool IsActive => false;

        public static Flags LoadFlags() => Flags.Disabled;

        public static bool LoadFlags(out Flags flags)
        {
            flags = Flags.Disabled;
            return false;
        }
    }

    // The async profiler is not ported, AsyncInstrumentation.IsActive keeps every caller out
    internal static class AsyncStateMachineDispatcherInfo
    {
        public static bool IsSupported => false;

        internal static IAsyncStateMachineBox CreateDispatcher(IAsyncStateMachineBox box, AsyncInstrumentation.Flags flags) => box;

        internal static void UnwindAsyncFrame(object completingBox, AsyncInstrumentation.Flags flags)
        {
        }

        internal static void ResumeAsyncMethod(IAsyncStateMachineBox box, AsyncInstrumentation.Flags flags)
        {
        }

        internal static void CompleteAsyncMethod(object completingBox, AsyncInstrumentation.Flags flags)
        {
        }
    }


    internal static class AsyncStateMachineDiagnostics<TStateMachine> where TStateMachine : IAsyncStateMachine
    {
        public static ulong MethodId => 0;

        public static int GetState(ref TStateMachine stateMachine) => -1;
    }

    /// <summary>Helpers the compiler calls from runtime async methods, the JIT gives them their meaning</summary>
    public static class AsyncHelpers
    {
        private sealed unsafe class ValueTaskSourceContinuation : Continuation
        {
            internal object? Source;
            internal short Token;
            internal delegate*<object, Action<object?>, object?, short, ValueTaskSourceOnCompletedFlags, void> OnCompletedValueTaskSource;
            private delegate*<object, short, ref byte, void> _getResult;

            public ValueTaskSourceContinuation()
            {
                ResumeInfo = (ResumeInfo*)Unsafe.AsPointer(in ValueTaskSourceContinuationResume.ResumeInfo);
            }

            [StackTraceHidden]
            public void GetResult(ref byte returnValue)
            {
                // Avoid retaining source. The call below may throw.
                object source = Source;
                Source = null;

                _getResult(source, Token, ref returnValue);
            }

            public void Initialize(IValueTaskSource source, short token)
            {
                Source = source;
                Token = token;
                OnCompletedValueTaskSource = &OnCompleted;
                _getResult = &GetResult;
            }

            public void Initialize<T>(IValueTaskSource<T> source, short token)
            {
                Source = source;
                Token = token;
                OnCompletedValueTaskSource = &OnCompleted<T>;
                _getResult = &GetResult<T>;
            }

            private static void OnCompleted(object source, Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            {
                IValueTaskSource typedSource = Unsafe.As<object, IValueTaskSource>(ref source);
                typedSource.OnCompleted(continuation, state, token, flags);
            }

            [StackTraceHidden]
            private static void GetResult(object source, short token, ref byte result)
            {
                IValueTaskSource typedSource = Unsafe.As<object, IValueTaskSource>(ref source);
                typedSource.GetResult(token);
            }

            private static void OnCompleted<T>(object source, Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            {
                IValueTaskSource<T> typedSource = Unsafe.As<object, IValueTaskSource<T>>(ref source);
                typedSource.OnCompleted(continuation, state, token, flags);
            }

            [StackTraceHidden]
            private static void GetResult<T>(object source, short token, ref byte result)
            {
                IValueTaskSource<T> typedSource = Unsafe.As<object, IValueTaskSource<T>>(ref source);
                Unsafe.As<byte, T>(ref result) = typedSource.GetResult(token);
            }

            private static class ValueTaskSourceContinuationResume
            {
                [FixedAddressValueType]
                public static readonly ResumeInfo ResumeInfo = new ResumeInfo
                {
                    DiagnosticIP = null,
                    Resume = &ResumeValueTaskSourceContinuation,
                };

                [StackTraceHidden]
                private static Continuation? ResumeValueTaskSourceContinuation(Continuation cont, ref byte result)
                {
                    var vtsCont = (ValueTaskSourceContinuation)cont;
                    vtsCont.Next = null;

                    t_runtimeAsyncAwaitState.CachedValueTaskSourceContinuation = vtsCont;

                    vtsCont.GetResult(ref result);
                    return null;
                }
            }
        }

        // This is the "magic" method on which other "Await" methods are built.
        // Calling this from an Async method returns the continuation to the caller thus
        // explicitly initiates suspension.
        [Intrinsic]
        private static void AsyncSuspend(Continuation continuation) => throw new UnreachableException();

        // An intrinsic that provides access to continuations produced by Async calls.
        // Calling this after an Async method call returns:
        //   * `null` if the call has completed synchronously, or
        //   * a continuation object if the call requires suspension.
        //     In this case the formal result of the call is undefined.
        [Intrinsic]
        private static Continuation? AsyncCallContinuation() => throw new UnreachableException();

        // Indicate that an upcoming await should be done as a "tail await" that does not introduce a new suspension point.
        [Intrinsic]
        private static void TailAwait() => throw new UnreachableException();

        // This is state used by suspension/resumption machinery and stored in
        // the two places that initiate runtime async chains: either a
        // task-returning thunk, or DispatchContinuations. A pointer to this
        // state is kept in the runtime async TLS. This storage method avoids
        // costly write barriers on the hot path of suspension/resumption.
        [NonVersionable]
        private ref struct RuntimeAsyncStackState
        {
            // The following are the possible introducers of asynchrony into a chain of awaits.
            // In other words - when we build a chain of continuations it would be logicaly attached
            // to one of these notifiers.
            public ICriticalNotifyCompletion? CriticalNotifier;
            public INotifyCompletion? Notifier;
            public ValueTaskSourceContinuation? ValueTaskSourceContinuation;
            public RuntimeAsyncTaskContinuation? TaskContinuation;
            public unsafe delegate*<Continuation, int, Action, void> AwaiterContinuation;
            public int AwaiterOffset;

            // When we suspend in the leaf, the contexts are captured into these fields.
            public ExecutionContext? LeafExecutionContext;
            public SynchronizationContext? LeafSynchronizationContext;

            public unsafe RuntimeAsyncStackState* Next;
        }

        // Used during suspensions to hold the continuation chain and on what we are waiting.
        // Methods like CreateRuntimeAsyncTask will unlink the state and wrap into a Task.
        [NonVersionable]
        private unsafe struct RuntimeAsyncAwaitState
        {
            public Continuation? SentinelContinuation;
            public RuntimeAsyncTaskContinuation? CachedTaskContinuation;
            public ValueTaskSourceContinuation? CachedValueTaskSourceContinuation;

            // We cache the thread here to avoid unnecessary repeated TLS lookups.
            public Thread? CurrentThread;

            public RuntimeAsyncStackState* StackState;

            public void CaptureContexts()
            {
                // CaptureContext is called from leaf await helpers. We either just started a runtime async chain
                // (from a thunk), or we came from DispatchContinuations (on resumption).
                // Both cases have already initialized CurrentThread.
                Thread? curThread = CurrentThread;
                // Here we get the execution context for presenting to the notifier,
                // not for flowing across suspension to potentially another thread.
                // Therefore we do not need to worry about IsFlowSuppressed
                StackState->LeafExecutionContext = curThread._executionContext;
                StackState->LeafSynchronizationContext = curThread._synchronizationContext;
            }

            // At the start of an async chain (task-returning thunk or DispatchContinuations) this function
            // is called
            [NonVersionable]
            public void Push(RuntimeAsyncStackState* stackState)
            {
                stackState->AwaiterContinuation = null;
                stackState->AwaiterOffset = 0;
                stackState->Next = StackState;
                StackState = stackState;
                CurrentThread ??= Thread.CurrentThread;
            }

            // This function is called at the end of an async chain
            [NonVersionable]
            public void Pop()
            {
                StackState = StackState->Next;
            }
        }

        private struct AsyncContexts
        {
            private SynchronizationContext? _synchronizationContext;
            private ExecutionContext? _executionContext;

            public AsyncContexts(Thread thread)
            {
                _synchronizationContext = thread._synchronizationContext;
                _executionContext = thread._executionContext;
            }

            public void Pop(Thread thread)
            {
                // The common case is that these have not changed, so avoid the cost of a write barrier if not needed.
                if (_synchronizationContext != thread._synchronizationContext)
                {
                    // Restore changed SynchronizationContext back to previous
                    thread._synchronizationContext = _synchronizationContext;
                }

                ExecutionContext? currentExecutionCtx = thread._executionContext;
                if (_executionContext != currentExecutionCtx)
                {
                    ExecutionContext.RestoreChangedContextToThread(thread, _executionContext, currentExecutionCtx);
                }
            }
        }

        [ThreadStatic]
        private static RuntimeAsyncAwaitState t_runtimeAsyncAwaitState;

        internal static void ReturnTaskContinuation(RuntimeAsyncTaskContinuation continuation)
        {
            t_runtimeAsyncAwaitState.CachedTaskContinuation = continuation;
        }


        private sealed class RuntimeAsyncYielder : ICriticalNotifyCompletion
        {
            public static readonly RuntimeAsyncYielder Instance = new();

            public void OnCompleted(Action continuation) => throw new NotSupportedException();

            public void UnsafeOnCompleted(Action continuation)
            {
                if (TplEventSource.Log.IsEnabled())
                {
                    default(YieldAwaitable.YieldAwaiter).UnsafeOnCompleted(continuation);
                    return;
                }

                Task raTask = (Task)continuation.Target!;

                SynchronizationContext? syncCtx = SynchronizationContext.Current;
                if (syncCtx != null && syncCtx.GetType() != typeof(SynchronizationContext))
                {
                    syncCtx.Post(static s => ((Task)s!).ExecuteDirectly(null), raTask);
                }
                else
                {
                    TaskScheduler scheduler = TaskScheduler.Current;
                    if (scheduler == TaskScheduler.Default)
                    {
                        ThreadPool.UnsafeQueueUserWorkItemInternal(raTask, preferLocal: false);
                    }
                    else
                    {
                        Task.Factory.StartNew(static s => ((Task)s!).ExecuteDirectly(null), raTask, default, TaskCreationOptions.PreferFairness, scheduler);
                    }
                }
            }
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe void Suspend(Task task, ConfigureAwaitOptions options)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            RuntimeAsyncTaskContinuation? taskCont = state.CachedTaskContinuation;
            if (taskCont != null)
            {
                state.CachedTaskContinuation = null;
            }
            else
            {
                taskCont = new RuntimeAsyncTaskContinuation();
            }

            taskCont.Initialize(task);

            if ((options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0)
            {
                CaptureContinuationContext(ref taskCont.ContinuationContext, ref taskCont.Flags);
            }
            else
            {
                taskCont.Flags |= ContinuationFlags.ContinueOnThreadPool;
            }

            sentinelContinuation.Next = taskCont;
            state.StackState->TaskContinuation = taskCont;

            state.CaptureContexts();
            AsyncSuspend(taskCont);
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe T Suspend<T>(Task<T> task, ConfigureAwaitOptions options)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            RuntimeAsyncTaskContinuation? taskCont = state.CachedTaskContinuation;
            if (taskCont != null)
            {
                state.CachedTaskContinuation = null;
            }
            else
            {
                taskCont = new RuntimeAsyncTaskContinuation();
            }

            taskCont.Initialize<T>(task);

            if ((options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0)
            {
                CaptureContinuationContext(ref taskCont.ContinuationContext, ref taskCont.Flags);
            }
            else
            {
                taskCont.Flags |= ContinuationFlags.ContinueOnThreadPool;
            }

            sentinelContinuation.Next = taskCont;
            state.StackState->TaskContinuation = taskCont;

            state.CaptureContexts();
            AsyncSuspend(taskCont);
            return default!;
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe void TransparentSuspend(IValueTaskSource source, short token)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            ValueTaskSourceContinuation? vtsCont = state.CachedValueTaskSourceContinuation;
            if (vtsCont != null)
            {
                state.CachedValueTaskSourceContinuation = null;
            }
            else
            {
                vtsCont = new ValueTaskSourceContinuation();
            }

            vtsCont.Initialize(source, token);

            sentinelContinuation.Next = vtsCont;
            state.StackState->ValueTaskSourceContinuation = vtsCont;
            state.CaptureContexts();
            AsyncSuspend(vtsCont);
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe void Suspend(IValueTaskSource source, short token, bool continueOnCapturedContext)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            ValueTaskSourceContinuation? vtsCont = state.CachedValueTaskSourceContinuation;
            if (vtsCont != null)
            {
                state.CachedValueTaskSourceContinuation = null;
            }
            else
            {
                vtsCont = new ValueTaskSourceContinuation();
            }

            vtsCont.Initialize(source, token);

            if (continueOnCapturedContext)
            {
                // We only need to capture flags.
                // If needed, VTS will use the scheduling context captured in the "state".
                CaptureContinuationContextFlags(ref vtsCont.Flags, state.CurrentThread!);
            }
            else
            {
                vtsCont.Flags |= ContinuationFlags.ContinueOnThreadPool;
            }

            sentinelContinuation.Next = vtsCont;
            state.StackState->ValueTaskSourceContinuation = vtsCont;

            state.CaptureContexts();
            AsyncSuspend(vtsCont);
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe T TransparentSuspend<T>(IValueTaskSource<T> source, short token)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            ValueTaskSourceContinuation? vtsCont = state.CachedValueTaskSourceContinuation;
            if (vtsCont != null)
            {
                state.CachedValueTaskSourceContinuation = null;
            }
            else
            {
                vtsCont = new ValueTaskSourceContinuation();
            }

            vtsCont.Initialize<T>(source, token);

            sentinelContinuation.Next = vtsCont;
            state.StackState->ValueTaskSourceContinuation = vtsCont;
            state.CaptureContexts();
            AsyncSuspend(vtsCont);
            return default!;
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe T Suspend<T>(IValueTaskSource<T> source, short token, bool continueOnCapturedContext)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            ValueTaskSourceContinuation? vtsCont = state.CachedValueTaskSourceContinuation;
            if (vtsCont != null)
            {
                state.CachedValueTaskSourceContinuation = null;
            }
            else
            {
                vtsCont = new ValueTaskSourceContinuation();
            }

            vtsCont.Initialize<T>(source, token);

            if (continueOnCapturedContext)
            {
                // We only need to capture flags.
                // If needed, VTS will use the scheduling context captured in the "state".
                CaptureContinuationContextFlags(ref vtsCont.Flags, state.CurrentThread!);
            }
            else
            {
                vtsCont.Flags |= ContinuationFlags.ContinueOnThreadPool;
            }

            sentinelContinuation.Next = vtsCont;
            state.StackState->ValueTaskSourceContinuation = vtsCont;

            state.CaptureContexts();
            AsyncSuspend(vtsCont);
            return default!;
        }

        /// <summary>
        /// Used by internal thunks that implement awaiting on Task.
        /// </summary>
        /// <param name="task">Task whose completion we are awaiting.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe void TransparentSuspend(Task task)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            RuntimeAsyncTaskContinuation? taskCont = state.CachedTaskContinuation;
            if (taskCont != null)
            {
                state.CachedTaskContinuation = null;
            }
            else
            {
                taskCont = new RuntimeAsyncTaskContinuation();
            }

            taskCont.Initialize(task);

            sentinelContinuation.Next = taskCont;
            state.StackState->TaskContinuation = taskCont;

            state.CaptureContexts();
            AsyncSuspend(taskCont);
        }

        /// <summary>
        /// Used by internal thunks that implement awaiting on Task.
        /// </summary>
        /// <param name="task">Task whose completion we are awaiting.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        private static unsafe T TransparentSuspend<T>(Task<T> task)
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            RuntimeAsyncTaskContinuation? taskCont = state.CachedTaskContinuation;
            if (taskCont != null)
            {
                state.CachedTaskContinuation = null;
            }
            else
            {
                taskCont = new RuntimeAsyncTaskContinuation();
            }

            taskCont.Initialize<T>(task);

            sentinelContinuation.Next = taskCont;
            state.StackState->TaskContinuation = taskCont;

            state.CaptureContexts();
            AsyncSuspend(taskCont);
            return default!;
        }

        // The next four overloads 'TransparentAwait' are called by the JIT in
        // async versions of task-returning methods to await the task that
        // would normally be returned.
        [MethodImpl(MethodImplOptions.Async)]
        private static void TransparentAwait(Task task)
        {
            if (!task.IsCompleted)
            {
                TailAwait();
                TransparentSuspend(task);
                return;
            }

            TaskAwaiter.ValidateEnd(task);
        }

        [MethodImpl(MethodImplOptions.Async)]
        private static void TransparentAwait(ValueTask task)
        {
            object? obj = task._obj;
            if (obj == null)
            {
                return;
            }

            if (obj is Task t)
            {
                if (!t.IsCompleted)
                {
                    TailAwait();
                    TransparentSuspend(t);
                    return;
                }

                TaskAwaiter.ValidateEnd(t);
                return;
            }

            Debug.Assert(obj is IValueTaskSource);
            IValueTaskSource vts = Unsafe.As<object, IValueTaskSource>(ref obj);
            if (vts.GetStatus(task._token) == ValueTaskSourceStatus.Pending)
            {
                TailAwait();
                TransparentSuspend(vts, task._token);
                return;
            }

            vts.GetResult(task._token);
        }

        [MethodImpl(MethodImplOptions.Async)]
        private static T TransparentAwait<T>(Task<T> task)
        {
            if (!task.IsCompleted)
            {
                TailAwait();
                return TransparentSuspend(task);
            }

            TaskAwaiter.ValidateEnd(task);
            return task.ResultOnSuccess;
        }

        [MethodImpl(MethodImplOptions.Async)]
        private static T TransparentAwait<T>(ValueTask<T> task)
        {
            object? obj = task._obj;
            if (obj == null)
            {
                return task._result!;
            }

            if (obj is Task<T> t)
            {
                if (!t.IsCompleted)
                {
                    TailAwait();
                    return TransparentSuspend(t);
                }

                TaskAwaiter.ValidateEnd(t);
                return t.ResultOnSuccess;
            }

            Debug.Assert(obj is IValueTaskSource<T>);
            IValueTaskSource<T> vts = Unsafe.As<object, IValueTaskSource<T>>(ref obj);
            if (vts.GetStatus(task._token) == ValueTaskSourceStatus.Pending)
            {
                TailAwait();
                return TransparentSuspend(vts, task._token);
            }

            return vts.GetResult(task._token);
        }

        // Represents execution of a chain of suspended and resuming runtime
        // async functions.
        private sealed class RuntimeAsyncTask<T> : Task<T>
        {
            public RuntimeAsyncTask()
            {
                // We use the base Task's state object field to store the Continuation while posting the task around.
                // Ensure that state object isn't published out for others to see.
                Debug.Assert((m_stateFlags & (int)InternalTaskOptions.PromiseTask) != 0, "Expected state flags to already be configured.");
                Debug.Assert(m_stateObject is null, "Expected to be able to use the state object field for Continuation.");
                m_action = new Action(DispatchContinuations);
                m_stateFlags |= (int)InternalTaskOptions.HiddenState;
            }

            internal override void ExecuteDirectly(Thread? threadPoolThread)
            {
                DispatchContinuations();
            }

            private Action GetContinuationAction()
            {
                object? action = m_action;
                Debug.Assert(action is Action);
                return Unsafe.As<Action>(action);
            }

            private Continuation MoveContinuationState()
            {
                object? stateObject = m_stateObject;
                Debug.Assert(stateObject is Continuation);
                m_stateObject = null;
                return Unsafe.As<Continuation>(stateObject);
            }

            private void SetContinuationState(Continuation value)
            {
                Debug.Assert(m_stateObject == null);
                m_stateObject = value;
            }

            internal unsafe bool HandleSuspended(ref RuntimeAsyncAwaitState state)
            {
                Thread? currentThread = state.CurrentThread;
                Debug.Assert(currentThread != null);

                RuntimeAsyncStackState* stackState = state.StackState;
                ExecutionContext? suspendingExecutionContext = stackState->LeafExecutionContext;
                SynchronizationContext? suspendingSyncContext = stackState->LeafSynchronizationContext;
                if (suspendingExecutionContext != currentThread._executionContext)
                {
                    currentThread._executionContext = suspendingExecutionContext;
                }

                if (suspendingSyncContext != currentThread._synchronizationContext)
                {
                    currentThread._synchronizationContext = suspendingSyncContext;
                }

                Continuation sentinelContinuation = state.SentinelContinuation!;
                Continuation headContinuation = sentinelContinuation.Next!;
                sentinelContinuation.Next = null;

                SetContinuationState(headContinuation);

                try
                {
                    if (stackState->ValueTaskSourceContinuation is { } valueTaskSourceCont)
                    {
                        Debug.Assert(headContinuation == valueTaskSourceCont);
                        object? source = valueTaskSourceCont.Source;
                        Debug.Assert(source != null);
                        // The awaiter must inform the ValueTaskSource on whether the continuation
                        // wants to run on a context, although the source may decide to ignore the suggestion.
                        // Since the behavior of the source takes precedence, we clear the context flags of
                        // the awaiting continuation (so it will run transparently on what the source decides)
                        // and then tell the source if the awaiting frame prefers to continue on a context.
                        // The reason why we do it here and not when the notifier is created is because
                        // the continuation chain builds from the innermost frame out and at the time when the
                        // notifier is created we do not know yet if the caller wants to continue on a context.

                        // Skip to a nontransparent/user continuation. Such continuation must exist.
                        // Since we see a VTS notifier, something was directly or indirectly
                        // awaiting either an async thunk for a ValueTask-returning method or
                        // the direct AsyncHelpers.Await(ValueTask/ValueTask<T>) path.
                        // In either case, that can only happen in nontransparent/user code.
                        Continuation contWithContinueFlags = valueTaskSourceCont;
                        while ((contWithContinueFlags.Flags & (ContinuationFlags.AllContinuationFlags | ContinuationFlags.ValueTaskAdaptedToTask)) == 0 &&
                               contWithContinueFlags.Next != null)
                        {
                            contWithContinueFlags = contWithContinueFlags.Next;
                        }

                        ContinuationFlags continuationFlags = contWithContinueFlags.Flags;
                        const ContinuationFlags continueOnContextFlags =
                            ContinuationFlags.ContinueOnCapturedSynchronizationContext |
                            ContinuationFlags.ContinueOnCapturedTaskScheduler;

                        ValueTaskSourceOnCompletedFlags configFlags = ValueTaskSourceOnCompletedFlags.None;
                        if ((continuationFlags & continueOnContextFlags) != 0)
                        {
                            // if await has captured some context, inform the source
                            configFlags = ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
                        }

                        // Clear continuation flags, so that continuation runs transparently
                        contWithContinueFlags.Flags &= ~ContinuationFlags.AllContinuationFlags;

                        valueTaskSourceCont.OnCompletedValueTaskSource(
                            source,
                            ThreadPool.s_dispatchRuntimeAsyncContinuationsCallback,
                            this,
                            valueTaskSourceCont.Token,
                            configFlags);
                    }
                    else if (stackState->AwaiterContinuation != null)
                    {
                        // The awaiter is stored in the continuation for the caller of
                        // AwaitAwaiterInContinuation or UnsafeAwaitAwaiterInContinuation.
                        Debug.Assert((headContinuation.Flags & ContinuationFlags.AllContinuationFlags) == 0);
                        stackState->AwaiterContinuation(
                            headContinuation, stackState->AwaiterOffset, GetContinuationAction());
                    }
                    else if (stackState->CriticalNotifier is { } critNotifier)
                    {
                        // Result of async call to AwaitAwaiter or UnsafeAwaitAwaiter.
                        // These never have special continuation context handling.
                        Debug.Assert((headContinuation.Flags & ContinuationFlags.AllContinuationFlags) == 0);
                        critNotifier.UnsafeOnCompleted(GetContinuationAction());
                    }
                    else if (stackState->TaskContinuation is { } taskCont)
                    {
                        Debug.Assert(headContinuation == taskCont);
                        // Runtime async callable wrapper for task returning
                        // method. This implements the context transparent
                        // forwarding and makes these wrappers minimal cost.
                        Debug.Assert(taskCont.Task != null);
                        taskCont.RuntimeAsyncTask = this;
                        if (!taskCont.Task.AddTaskContinuation(taskCont, addBeforeOthers: false))
                        {
                            taskCont.Execute(canInline: false);
                        }
                    }
                    else
                    {
                        Debug.Assert((headContinuation.Flags & ContinuationFlags.AllContinuationFlags) == 0);
                        Debug.Assert(stackState->Notifier != null);
                        stackState->Notifier!.OnCompleted(GetContinuationAction());
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Task.ThrowAsync(ex, targetContext: null);
                }

                return false;
            }

            [StackTraceHidden]
            // Diagnostic tooling depends on this name when classifying async callstack frames.
            private unsafe void DispatchContinuations()
            {
                // Intentionally skip initialization for this state; the Push
                // call below will initialize non-GC refs, and GC refs will be
                // zeroed by prolog.
                RuntimeAsyncStackState stackState;

                ref RuntimeAsyncAwaitState awaitState = ref t_runtimeAsyncAwaitState;
                awaitState.Push(&stackState);

                AsyncContexts contexts = new AsyncContexts(awaitState.CurrentThread!);

                ref AsyncDispatcherInfo* refDispatcherInfo = ref AsyncDispatcherInfo.t_current;

                AsyncDispatcherInfo asyncDispatcherInfo;
                asyncDispatcherInfo.Next = refDispatcherInfo;
                asyncDispatcherInfo.NextContinuation = MoveContinuationState();
                asyncDispatcherInfo.CurrentTask = this;
                refDispatcherInfo = &asyncDispatcherInfo;

                while (true)
                {
                    Debug.Assert(asyncDispatcherInfo.NextContinuation != null);
                    try
                    {
                        Continuation curContinuation = asyncDispatcherInfo.NextContinuation;
                        Continuation? nextContinuation = curContinuation.Next;
                        asyncDispatcherInfo.NextContinuation = nextContinuation;

                        Debug.Assert(awaitState.CurrentThread != null);
                        if (curContinuation.TryGetExecutionContext(out ExecutionContext? execContext))
                        {
                            RestoreExecutionContext(awaitState.CurrentThread, execContext);
                        }

                        ref byte resultLoc = ref nextContinuation != null ? ref nextContinuation.GetResultStorageOrNull() : ref GetResultStorage();

                        Continuation? newContinuation = curContinuation.ResumeInfo->Resume(curContinuation, ref resultLoc);

                        if (newContinuation != null)
                        {
                            newContinuation.Next = nextContinuation;
                            HandleSuspended(ref awaitState);

                            contexts.Pop(awaitState.CurrentThread!);
                            awaitState.Pop();
                            refDispatcherInfo = asyncDispatcherInfo.Next;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        uint unwindedFrames = 1; // Count current frame.
                        Continuation? handlerContinuation = UnwindToPossibleHandler(asyncDispatcherInfo.NextContinuation, ex, ref unwindedFrames);
                        if (handlerContinuation == null)
                        {
                            // Tail of AsyncTaskMethodBuilderT.SetException
                            bool successfullySet = ex is OperationCanceledException oce ?
                                TrySetCanceled(oce.CancellationToken, oce) :
                                TrySetException(ex);

                            contexts.Pop(awaitState.CurrentThread!);
                            awaitState.Pop();
                            refDispatcherInfo = asyncDispatcherInfo.Next;

                            if (!successfullySet)
                            {
                                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
                            }

                            return;
                        }

                        handlerContinuation.SetException(ex);
                        asyncDispatcherInfo.NextContinuation = handlerContinuation;
                    }

                    if (asyncDispatcherInfo.NextContinuation == null)
                    {
                        bool successfullySet = TrySetResult(m_result);

                        contexts.Pop(awaitState.CurrentThread!);
                        awaitState.Pop();
                        refDispatcherInfo = asyncDispatcherInfo.Next;

                        if (!successfullySet)
                        {
                            ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
                        }

                        return;
                    }

                    if (QueueContinuationFollowUpActionIfNecessary(asyncDispatcherInfo.NextContinuation))
                    {
                        contexts.Pop(awaitState.CurrentThread!);
                        awaitState.Pop();
                        refDispatcherInfo = asyncDispatcherInfo.Next;
                        return;
                    }
                }
            }

            private ref byte GetResultStorage() => ref Unsafe.As<T?, byte>(ref m_result);

            private static unsafe Continuation? UnwindToPossibleHandler(Continuation? continuation, Exception ex, ref uint unwindedFrames)
            {
                while (true)
                {
                    if (continuation == null || continuation.HasException())
                        return continuation;

                    continuation = continuation.Next;
                    unwindedFrames++;
                }
            }

            private bool QueueContinuationFollowUpActionIfNecessary(Continuation continuation)
            {
                if ((continuation.Flags & ContinuationFlags.ContinueOnThreadPool) != 0)
                {
                    SynchronizationContext? ctx = Thread.CurrentThreadAssumedInitialized._synchronizationContext;
                    if (ctx == null || ctx.GetType() == typeof(SynchronizationContext))
                    {
                        TaskScheduler? sched = TaskScheduler.InternalCurrent;
                        if (sched == null || sched == TaskScheduler.Default)
                        {
                            // Can inline
                            return false;
                        }
                    }

                    SetContinuationState(continuation);
                    ThreadPool.UnsafeQueueUserWorkItemInternal(this, preferLocal: true);
                    return true;
                }

                if ((continuation.Flags & ContinuationFlags.ContinueOnCapturedSynchronizationContext) != 0)
                {
                    object continuationContext = continuation.GetContinuationContext();
                    Debug.Assert(continuationContext is SynchronizationContext { });
                    SynchronizationContext continuationSyncCtx = (SynchronizationContext)continuationContext;

                    if (continuationSyncCtx == Thread.CurrentThreadAssumedInitialized._synchronizationContext)
                    {
                        // Inline
                        return false;
                    }

                    SetContinuationState(continuation);

                    try
                    {
                        continuationSyncCtx.Post(s_postCallback, this);
                    }
                    catch (Exception ex)
                    {
                        Task.ThrowAsync(ex, targetContext: null);
                    }

                    return true;
                }

                if ((continuation.Flags & ContinuationFlags.ContinueOnCapturedTaskScheduler) != 0)
                {
                    object continuationContext = continuation.GetContinuationContext();
                    Debug.Assert(continuationContext is TaskScheduler { });
                    TaskScheduler sched = (TaskScheduler)continuationContext;

                    SetContinuationState(continuation);
                    TaskSchedulerAwaitTaskContinuation.RunOrScheduleAction(GetContinuationAction(), sched, null, true);

                    return true;
                }

                return false;
            }

            private static readonly SendOrPostCallback s_postCallback = static state =>
            {
                Debug.Assert(state is RuntimeAsyncTask<T>);
                ((RuntimeAsyncTask<T>)state).DispatchContinuations();
            };
        }

        private static void FinalizeRuntimeAsyncTask<T>(ref RuntimeAsyncAwaitState state, RuntimeAsyncTask<T> task)
        {
            // We restore back to the leaf context to call OnCompleted, so we
            // need to push/pop contexts around that as well.
            AsyncContexts contexts = new AsyncContexts(state.CurrentThread!);

            task.HandleSuspended(ref state);
            contexts.Pop(state.CurrentThread!);
        }

        // Change return type to RuntimeAsyncTask<T?> -- no benefit since this is used for Task returning thunks only
#pragma warning disable CA1859
        // When a Task-returning thunk gets a continuation result
        // it calls here to make a Task that awaits on the current async state.
        private static Task<T?> CreateRuntimeAsyncTask<T>(ref RuntimeAsyncAwaitState state)
        {
            RuntimeAsyncTask<T?> result = new();
            FinalizeRuntimeAsyncTask(ref state, result);
            return result;
        }

        private static Task CreateRuntimeAsyncTask(ref RuntimeAsyncAwaitState state)
        {
            RuntimeAsyncTask<VoidTaskResult> result = new();
            FinalizeRuntimeAsyncTask(ref state, result);
            return result;
        }

        private static ValueTask<T?> CreateRuntimeAsyncValueTask<T>(ref RuntimeAsyncAwaitState state)
        {
            // We only come to these methods in the expensive case (already
            // suspended), so ValueTask optimization here is not relevant.
            return new ValueTask<T?>(CreateRuntimeAsyncTask<T>(ref state));
        }

        private static ValueTask CreateRuntimeAsyncValueTask(ref RuntimeAsyncAwaitState state)
        {
            return new ValueTask(CreateRuntimeAsyncTask(ref state));
        }

        private static Task<T?> TaskFromException<T>(Exception ex)
        {
            Task<T?> task = new();
            bool successfullySet = ex is OperationCanceledException oce ?
                task.TrySetCanceled(oce.CancellationToken, oce) :
                task.TrySetException(ex);

            Debug.Assert(successfullySet);
            return task;
        }

        private static Task TaskFromException(Exception ex)
        {
            Task task = new();
            // Tail of AsyncTaskMethodBuilderT.SetException
            bool successfullySet = ex is OperationCanceledException oce ?
                task.TrySetCanceled(oce.CancellationToken, oce) :
                task.TrySetException(ex);

            Debug.Assert(successfullySet);
            return task;
        }

        private static ValueTask ValueTaskFromException(Exception ex)
        {
            // We only come to these methods in the expensive case (exception),
            // so ValueTask optimization here is not relevant.
            return new ValueTask(TaskFromException(ex));
        }

        private static ValueTask<T?> ValueTaskFromException<T>(Exception ex)
        {
            return new ValueTask<T?>(TaskFromException<T>(ex));
        }

        // Called when capturing execution context for suspension.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ExecutionContext? CaptureExecutionContext()
        {
            return ExecutionContext.CaptureForSuspension(Thread.CurrentThreadAssumedInitialized);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RestoreExecutionContext(Thread thread, ExecutionContext? previousExecCtx)
        {
            ExecutionContext? currentExecCtx = thread._executionContext;
            if (previousExecCtx != currentExecCtx && previousExecCtx != ExecutionContext.DefaultFlowSuppressed)
            {
                ExecutionContext.RestoreChangedContextToThread(thread, previousExecCtx, currentExecCtx);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CaptureContexts(out Thread thread, out ExecutionContext? execCtx, out SynchronizationContext? syncCtx)
        {
            Thread curThread = Thread.CurrentThreadAssumedInitialized;
            thread = curThread;
            // Here we get the execution context for synchronous restoring,
            // not for flowing across suspension to potentially another thread.
            // Therefore we do not need to worry about IsFlowSuppressed
            execCtx = curThread._executionContext;
            syncCtx = curThread._synchronizationContext;
        }

        // Restore contexts onto thread. If "resumed" then this is not the first starting call for the async method.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RestoreContexts(bool resumed, Thread thread, ExecutionContext? previousExecCtx, SynchronizationContext? previousSyncCtx)
        {
            if (!resumed)
            {
                if (previousSyncCtx != thread._synchronizationContext)
                {
                    thread._synchronizationContext = previousSyncCtx;
                }

                ExecutionContext? currentExecCtx = thread._executionContext;
                if (previousExecCtx != currentExecCtx)
                {
                    ExecutionContext.RestoreChangedContextToThread(thread, previousExecCtx, currentExecCtx);
                }
            }
        }

        // Restore contexts onto current Thread as we unwind during suspension. We control the code that runs
        // during suspension and we do not need to raise ExecutionContext notifications -- we know that it is
        // not going to be accessed and that DispatchContinuations will return it back to the leaf's context
        // before calling user code, and restore the original contexts with appropriate notifications before
        // returning.
        private static void RestoreContextsOnSuspension(bool resumed, ExecutionContext? previousExecCtx, SynchronizationContext? previousSyncCtx)
        {
            if (!resumed)
            {
                Thread thread = Thread.CurrentThreadAssumedInitialized;
                if (previousSyncCtx != thread._synchronizationContext)
                {
                    thread._synchronizationContext = previousSyncCtx;
                }

                if (previousExecCtx != thread._executionContext)
                {
                    thread._executionContext = previousExecCtx;
                }
            }
        }

        private static void CaptureContinuationContext(ref object? continuationContext, ref ContinuationFlags flags)
        {
            SynchronizationContext? syncCtx = Thread.CurrentThreadAssumedInitialized._synchronizationContext;
            if (syncCtx != null && syncCtx.GetType() != typeof(SynchronizationContext))
            {
                flags |= ContinuationFlags.ContinueOnCapturedSynchronizationContext;
                continuationContext = syncCtx;
                return;
            }

            TaskScheduler? sched = TaskScheduler.InternalCurrent;
            if (sched != null && sched != TaskScheduler.Default)
            {
                flags |= ContinuationFlags.ContinueOnCapturedTaskScheduler;
                continuationContext = sched;
                return;
            }

            flags |= ContinuationFlags.ContinueOnThreadPool;
        }

        // Same as above, but only captures flags
        private static void CaptureContinuationContextFlags(ref ContinuationFlags flags, Thread currentThread)
        {
            SynchronizationContext? syncCtx = currentThread._synchronizationContext;
            if (syncCtx != null && syncCtx.GetType() != typeof(SynchronizationContext))
            {
                flags |= ContinuationFlags.ContinueOnCapturedSynchronizationContext;
                return;
            }

            TaskScheduler? sched = TaskScheduler.InternalCurrent;
            if (sched != null && sched != TaskScheduler.Default)
            {
                flags |= ContinuationFlags.ContinueOnCapturedTaskScheduler;
                return;
            }

            flags |= ContinuationFlags.ContinueOnThreadPool;
        }

        // Finish suspension in the common case of a custom await or for a ConfigureAwait(false) task await:
        // - Capture current ExecutionContext into the continuation
        // - Restore ExecutionContext and SynchronizationContext to the current Thread object
        private static void FinishSuspensionNoContinuationContext(ref ExecutionContext? execCtx, bool resumed, ExecutionContext? previousExecCtx, SynchronizationContext? previousSyncCtx)
        {
            Thread thread = Thread.CurrentThreadAssumedInitialized;

            ExecutionContext? threadExecCtx = thread._executionContext;

            // Commonly when we reuse a continuation we have already the right
            // context saved, or we are saving null and it is already null.
            if (threadExecCtx != execCtx)
            {
                if (threadExecCtx != null && threadExecCtx.InstanceIsFlowSuppressed)
                {
                    execCtx = ExecutionContext.DefaultFlowSuppressed;
                }
                else
                {
                    execCtx = threadExecCtx;
                }
            }
            else
            {
                Debug.Assert(threadExecCtx == null || !threadExecCtx.InstanceIsFlowSuppressed || execCtx == ExecutionContext.DefaultFlowSuppressed);
            }

            if (!resumed)
            {
                if (previousSyncCtx != thread._synchronizationContext)
                {
                    thread._synchronizationContext = previousSyncCtx;
                }

                if (previousExecCtx != threadExecCtx)
                {
                    thread._executionContext = previousExecCtx;
                }
            }
        }

        // Finish suspension in the common case of a standard task await:
        // - Record continuation context to determine where to continue on resumption
        // - Capture current ExecutionContext into the continuation
        // - Restore ExecutionContext and SynchronizationContext to the current Thread object
        private static void FinishSuspensionWithContinuationContext(ref object continuationContext, ref ContinuationFlags flags, ref ExecutionContext? execCtx, bool resumed, ExecutionContext? previousExecCtx, SynchronizationContext? previousSyncCtx)
        {
            Thread thread = Thread.CurrentThreadAssumedInitialized;
            SynchronizationContext? threadSyncCtx = thread._synchronizationContext;
            if (threadSyncCtx != null && threadSyncCtx.GetType() != typeof(SynchronizationContext))
            {
                flags |= ContinuationFlags.ContinueOnCapturedSynchronizationContext;
                if (continuationContext != threadSyncCtx)
                {
                    continuationContext = threadSyncCtx;
                }
            }
            else
            {
                TaskScheduler? sched = TaskScheduler.InternalCurrent;
                if (sched != null && sched != TaskScheduler.Default)
                {
                    flags |= ContinuationFlags.ContinueOnCapturedTaskScheduler;
                    if (continuationContext != sched)
                    {
                        continuationContext = sched;
                    }
                }
                else
                {
                    flags |= ContinuationFlags.ContinueOnThreadPool;
                }
            }

            ExecutionContext? threadExecCtx = thread._executionContext;
            // Commonly when we reuse a continuation we have already the right
            // context saved, or we are saving null and it is already null.
            if (threadExecCtx != execCtx)
            {
                if (threadExecCtx != null && threadExecCtx.InstanceIsFlowSuppressed)
                {
                    execCtx = ExecutionContext.DefaultFlowSuppressed;
                }
                else
                {
                    execCtx = threadExecCtx;
                }
            }
            else
            {
                Debug.Assert(threadExecCtx == null || !threadExecCtx.InstanceIsFlowSuppressed || execCtx == ExecutionContext.DefaultFlowSuppressed);
            }

            if (!resumed)
            {
                if (previousSyncCtx != threadSyncCtx)
                {
                    thread._synchronizationContext = previousSyncCtx;
                }

                if (previousExecCtx != threadExecCtx)
                {
                    thread._executionContext = previousExecCtx;
                }
            }
        }

        /// <summary>
        /// Awaits the specified awaiter and returns when the awaiter has completed.
        /// </summary>
        /// <typeparam name="TAwaiter">The awaiter type.</typeparam>
        /// <param name="awaiter">The awaiter to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        [StackTraceHidden]
        public static unsafe void AwaitAwaiter<TAwaiter>(TAwaiter awaiter) where TAwaiter : INotifyCompletion
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();
            state.StackState->Notifier = awaiter;
            state.CaptureContexts();
            AsyncSuspend(sentinelContinuation);
        }

        // Must be NoInlining because we use AsyncSuspend to manufacture an explicit suspension point.
        // It will not capture/restore any local state that is live across it.

        /// <summary>
        /// Awaits the specified awaiter without capturing the execution context and returns when the awaiter has completed.
        /// </summary>
        /// <typeparam name="TAwaiter">The awaiter type.</typeparam>
        /// <param name="awaiter">The awaiter to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.Async)]
        [StackTraceHidden]
        public static unsafe void UnsafeAwaitAwaiter<TAwaiter>(TAwaiter awaiter) where TAwaiter : ICriticalNotifyCompletion
        {
            ref RuntimeAsyncAwaitState state = ref t_runtimeAsyncAwaitState;
            Continuation? sentinelContinuation = state.SentinelContinuation ??= new Continuation();

            // We optimize specially for "await Task.Yield()" -- in the same
            // way that YieldAwaiter implements IStateMachineBoxAwareAwaiter
            // for async1. This avoids allocating internal thread pool objects
            // for this case.
            if (typeof(TAwaiter) == typeof(YieldAwaitable.YieldAwaiter))
            {
                state.StackState->CriticalNotifier = RuntimeAsyncYielder.Instance;
            }
            else
            {
                state.StackState->CriticalNotifier = awaiter;
            }

            state.CaptureContexts();
            AsyncSuspend(sentinelContinuation);
        }

        /// <summary>
        /// Awaits the specified <see cref="Task{T}"/> and returns its result, throwing any exception produced by the task.
        /// </summary>
        /// <typeparam name="T">The result type produced by the task.</typeparam>
        /// <param name="task">The task to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static T Await<T>(Task<T> task)
        {
            if (!task.IsCompleted)
            {
                TailAwait();
                return Suspend(task, ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            TaskAwaiter.ValidateEnd(task);
            return task.ResultOnSuccess;
        }

        /// <summary>
        /// Awaits the specified <see cref="Task"/> and throws any exception produced by the operation.
        /// </summary>
        /// <param name="task">The task to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static void Await(Task task)
        {
            if (!task.IsCompleted)
            {
                TailAwait();
                Suspend(task, ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            TaskAwaiter.ValidateEnd(task);
        }

        /// <summary>
        /// Awaits the specified <see cref="ValueTask{T}"/> and returns its result, throwing any exception produced by the operation.
        /// </summary>
        /// <typeparam name="T">The result type produced by the value task.</typeparam>
        /// <param name="task">The value task to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static T Await<T>(ValueTask<T> task)
        {
            object? obj = task._obj;
            if (obj == null)
            {
                return task._result!;
            }

            if (obj is Task<T> t)
            {
                if (t.IsCompleted)
                {
                    TaskAwaiter.ValidateEnd(t);
                    return t.ResultOnSuccess;
                }

                TailAwait();
                return Suspend(t, ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            Debug.Assert(obj is IValueTaskSource<T>);
            IValueTaskSource<T> vts = Unsafe.As<object, IValueTaskSource<T>>(ref obj);
            if (vts.GetStatus(task._token) == ValueTaskSourceStatus.Pending)
            {
                TailAwait();
                return Suspend(vts, task._token, true);
            }

            return vts.GetResult(task._token);
        }

        /// <summary>
        /// Awaits the specified <see cref="ValueTask"/> and throws any exception produced by the operation.
        /// </summary>
        /// <param name="task">The value task to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static void Await(ValueTask task)
        {
            object? obj = task._obj;
            if (obj == null)
            {
                return;
            }

            if (obj is Task t)
            {
                if (t.IsCompleted)
                {
                    TaskAwaiter.ValidateEnd(t);
                    return;
                }

                TailAwait();
                Suspend(t, ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            Debug.Assert(obj is IValueTaskSource);
            IValueTaskSource vts = Unsafe.As<object, IValueTaskSource>(ref obj);
            if (vts.GetStatus(task._token) == ValueTaskSourceStatus.Pending)
            {
                TailAwait();
                Suspend(vts, task._token, true);
                return;
            }

            vts.GetResult(task._token);
        }

        /// <summary>
        /// Awaits the specified configured task awaitable without capturing the execution context and throws any exception produced by the operation.
        /// </summary>
        /// <param name="configuredAwaitable">The configured awaitable to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static void Await(ConfiguredTaskAwaitable configuredAwaitable)
        {
            ConfiguredTaskAwaitable.ConfiguredTaskAwaiter awaiter = configuredAwaitable.GetAwaiter();
            if ((awaiter.m_options & ConfigureAwaitOptions.SuppressThrowing) != 0)
            {
                TailAwait();
                AwaitTaskWithRareOptions(awaiter);
                return;
            }

            if (!awaiter.IsCompleted)
            {
                TailAwait();
                Suspend(awaiter.m_task, awaiter.m_options);
                return;
            }

            awaiter.GetResult();
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async | MethodImplOptions.NoInlining)]
        [StackTraceHidden]
        private static void AwaitTaskWithRareOptions(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter awaiter)
        {
            if (!awaiter.IsCompleted)
            {
                UnsafeAwaitAwaiter(awaiter);
            }

            awaiter.GetResult();
        }

        /// <summary>
        /// Awaits the specified configured value task awaitable without capturing the execution context and throws any exception produced by the operation.
        /// </summary>
        /// <param name="configuredAwaitable">The configured value task awaitable to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static void Await(ConfiguredValueTaskAwaitable configuredAwaitable)
        {
            ValueTask task = configuredAwaitable._value;
            object? obj = task._obj;
            if (obj == null)
            {
                return;
            }

            if (obj is Task t)
            {
                if (t.IsCompleted)
                {
                    TaskAwaiter.ValidateEnd(t);
                    return;
                }

                TailAwait();
                Suspend(t, task._continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : ConfigureAwaitOptions.None);
                return;
            }

            Debug.Assert(obj is IValueTaskSource);
            IValueTaskSource vts = Unsafe.As<object, IValueTaskSource>(ref obj);
            if (vts.GetStatus(task._token) == ValueTaskSourceStatus.Pending)
            {
                TailAwait();
                Suspend(vts, task._token, task._continueOnCapturedContext);
                return;
            }

            vts.GetResult(task._token);
        }

        /// <summary>
        /// Awaits the specified configured task awaitable and returns its result, throwing any exception produced by the operation.
        /// </summary>
        /// <typeparam name="T">The result type produced by the awaitable.</typeparam>
        /// <param name="configuredAwaitable">The configured awaitable to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static T Await<T>(ConfiguredTaskAwaitable<T> configuredAwaitable)
        {
            ConfiguredTaskAwaitable<T>.ConfiguredTaskAwaiter awaiter = configuredAwaitable.GetAwaiter();
            // For the T version of ConfiguredTaskAwaiter we do not need to
            // support ConfigureAwaitOptions.SuppressThrowing, but we do need
            // to support ForceYielding. That one gets folded into the
            // awaiter's IsCompleted here.
            if (!awaiter.IsCompleted)
            {
                TailAwait();
                return Suspend(awaiter.m_task, awaiter.m_options);
            }

            return awaiter.GetResult();
        }

        /// <summary>
        /// Awaits the specified configured value task awaitable and returns its result, throwing any exception produced by the operation.
        /// </summary>
        /// <typeparam name="T">The result type produced by the awaitable.</typeparam>
        /// <param name="configuredAwaitable">The configured awaitable to await.</param>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.Async)]
        [StackTraceHidden]
        public static T Await<T>(ConfiguredValueTaskAwaitable<T> configuredAwaitable)
        {
            ValueTask<T> task = configuredAwaitable._value;
            object? obj = task._obj;
            if (obj == null)
            {
                return task._result!;
            }

            if (obj is Task<T> t)
            {
                if (t.IsCompleted)
                {
                    TaskAwaiter.ValidateEnd(t);
                    return t.ResultOnSuccess;
                }

                TailAwait();
                return Suspend(t, task._continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : ConfigureAwaitOptions.None);
            }

            Debug.Assert(obj is IValueTaskSource<T>);
            IValueTaskSource<T> vts = Unsafe.As<object, IValueTaskSource<T>>(ref obj);
            if (vts.GetStatus(task._token) == ValueTaskSourceStatus.Pending)
            {
                TailAwait();
                return Suspend(vts, task._token, task._continueOnCapturedContext);
            }

            return vts.GetResult(task._token);
        }

        public static void HandleAsyncEntryPoint(Task task) => task.GetAwaiter().GetResult();

        public static int HandleAsyncEntryPoint(Task<int> task) => task.GetAwaiter().GetResult();
    }
}
