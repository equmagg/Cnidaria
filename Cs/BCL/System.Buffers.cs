namespace System.Buffers
{
    /// <summary>
    /// This enum defines the various potential status that can be returned from Span-based operations
    /// that support processing of input contained in multiple discontiguous buffers.
    /// </summary>
    public enum OperationStatus
    {
        /// <summary>
        /// The entire input buffer has been processed and the operation is complete.
        /// </summary>
        Done,
        /// <summary>
        /// The input is partially processed, up to what could fit into the destination buffer.
        /// The caller can enlarge the destination buffer, slice the buffers appropriately, and retry.
        /// </summary>
        DestinationTooSmall,
        /// <summary>
        /// The input is partially processed, up to the last valid chunk of the input that could be consumed.
        /// The caller can stitch the remaining unprocessed input with more data, slice the buffers appropriately, and retry.
        /// </summary>
        NeedMoreData,
        /// <summary>
        /// The input contained invalid bytes which could not be processed. If the input is partially processed,
        /// the destination contains the partial result. This guarantees that no additional data appended to the input
        /// will make the invalid sequence valid.
        /// </summary>
        InvalidData,
    }

    public abstract class ArrayPool<T>
    {
        private static readonly SharedArrayPool<T> s_shared = new SharedArrayPool<T>();
        /// <summary>
        /// Retrieves a shared <see cref="ArrayPool{T}"/> instance.
        /// </summary>
        /// <remarks>
        /// The shared pool provides a default implementation of <see cref="ArrayPool{T}"/>
        /// that's intended for general applicability.  It maintains arrays of multiple sizes, and
        /// may hand back a larger array than was actually requested, but will never hand back a smaller
        /// array than was requested. Renting a buffer from it with <see cref="Rent"/> will result in an
        /// existing buffer being taken from the pool if an appropriate buffer is available or in a new
        /// buffer being allocated if one is not available.
        /// byte[] and char[] are the most commonly pooled array types. For these we use a special pool type
        /// optimized for very fast access speeds, at the expense of more memory consumption.
        /// The shared pool instance is created lazily on first access.
        /// </remarks>
        public static ArrayPool<T> Shared => s_shared;
        /// <summary>
        /// Creates a new <see cref="ArrayPool{T}"/> instance using default configuration options.
        /// </summary>
        /// <returns>A new <see cref="ArrayPool{T}"/> instance.</returns>
        public static ArrayPool<T> Create() => new ConfigurableArrayPool<T>();
        /// <summary>
        /// Creates a new <see cref="ArrayPool{T}"/> instance using custom configuration options.
        /// </summary>
        public static ArrayPool<T> Create(int maxArrayLength, int maxArraysPerBucket) =>
            new ConfigurableArrayPool<T>(maxArrayLength, maxArraysPerBucket);

        /// <summary>
        /// Retrieves a buffer that is at least the requested length.
        /// </summary>
        /// <param name="minimumLength">The minimum length of the array needed.</param>
        /// <returns>
        /// An array that is at least <paramref name="minimumLength"/> in length.
        /// </returns>
        /// <remarks>
        /// This buffer is loaned to the caller and should be returned to the same pool via
        /// <see cref="Return"/> so that it may be reused in subsequent usage of <see cref="Rent"/>.
        /// It is not a fatal error to not return a rented buffer, but failure to do so may lead to
        /// decreased application performance, as the pool may need to create a new buffer to replace
        /// the one lost.
        /// </remarks>
        public abstract T[] Rent(int minimumLength);

        /// <summary>
        /// Returns to the pool an array that was previously obtained via <see cref="Rent"/> on the same
        /// <see cref="ArrayPool{T}"/> instance.
        /// </summary>
        /// <param name="array">
        /// The buffer previously obtained from <see cref="Rent"/> to return to the pool.
        /// </param>
        /// <param name="clearArray">
        /// If <c>true</c> and if the pool will store the buffer to enable subsequent reuse, <see cref="Return"/>
        /// will clear <paramref name="array"/> of its contents so that a subsequent consumer via <see cref="Rent"/>
        /// will not see the previous consumer's content.  If <c>false</c> or if the pool will release the buffer,
        /// the array's contents are left unchanged.
        /// </param>
        /// <remarks>
        /// Once a buffer has been returned to the pool, the caller gives up all ownership of the buffer
        /// and must not use it. The reference returned from a given call to <see cref="Rent"/> must only be
        /// returned via <see cref="Return"/> once.  The default <see cref="ArrayPool{T}"/>
        /// may hold onto the returned buffer in order to rent it again, or it may release the returned buffer
        /// if it's determined that the pool already has enough buffers stored.
        /// </remarks>
        public abstract void Return(T[] array, bool clearArray = false);

        internal void Return(T[] array, int lengthToClear)
        {
            array.AsSpan(0, lengthToClear).Clear();
            Return(array);
        }
    }
    internal sealed class SharedArrayPool<T> : ArrayPool<T>
    {
        /// <summary>The number of buckets (array sizes) in the pool, one for each array length, starting from length 16.</summary>
        private const int NumBuckets = 27; // Utilities.SelectBucketIndex(1024 * 1024 * 1024 + 1)

        /// <summary>A per-thread array of arrays, to cache one array per array size per thread.</summary>
        [ThreadStatic]
        private static SharedArrayPoolThreadLocalArray[]? t_tlsBuckets;

        /// <summary>
        /// An array of per-core partitions. The slots are lazily initialized to avoid creating
        /// lots of overhead for unused array sizes.
        /// </summary>
        private readonly SharedArrayPoolPartitions?[] _buckets = new SharedArrayPoolPartitions[NumBuckets];
        /// <summary>Whether the callback to trim arrays in response to memory pressure has been created.</summary>
        private bool _trimCallbackCreated;

        /// <summary>Allocate a new <see cref="SharedArrayPoolPartitions"/> and try to store it into the <see cref="_buckets"/> array.</summary>
        private SharedArrayPoolPartitions CreatePerCorePartitions(int bucketIndex)
        {
            var inst = new SharedArrayPoolPartitions();
            return System.Threading.Interlocked.CompareExchange(ref _buckets[bucketIndex], inst, null) ?? inst;
        }

        /// <summary>Gets an ID for the pool to use with events.</summary>
        private int Id => GetHashCode();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override T[] Rent(int minimumLength)
        {
            T[]? buffer;

            // Get the bucket number for the array length. The result may be out of range of buckets,
            // either for too large a value or for 0 and negative values.
            int bucketIndex = Utilities.SelectBucketIndex(minimumLength);

            // First, try to get an array from TLS if possible.
            SharedArrayPoolThreadLocalArray[]? tlsBuckets = t_tlsBuckets;
            if (tlsBuckets is not null && (uint)bucketIndex < (uint)tlsBuckets.Length)
            {
                buffer = Unsafe.As<T[]>(tlsBuckets[bucketIndex].Array);
                if (buffer is not null)
                {
                    tlsBuckets[bucketIndex].Array = null;
                    return buffer;
                }
            }

            // Next, try to get an array from one of the partitions.
            SharedArrayPoolPartitions?[] perCoreBuckets = _buckets;
            if ((uint)bucketIndex < (uint)perCoreBuckets.Length)
            {
                SharedArrayPoolPartitions? b = perCoreBuckets[bucketIndex];
                if (b is not null)
                {
                    buffer = Unsafe.As<T[]>(b.TryPop());
                    if (buffer is not null)
                    {
                        return buffer;
                    }
                }

                // No buffer available.  Ensure the length we'll allocate matches that of a bucket
                // so we can later return it.
                minimumLength = Utilities.GetMaxSizeForBucket(bucketIndex);
            }
            else if (minimumLength == 0)
            {
                // We allow requesting zero-length arrays (even though pooling such an array isn't valuable)
                // as it's a valid length array, and we want the pool to be usable in general instead of using
                // `new`, even for computed lengths. But, there's no need to log the empty array.  Our pool is
                // effectively infinite for empty arrays and we'll never allocate for rents and never store for returns.
                return [];
            }
            else
            {
                if (minimumLength < 0) throw new ArgumentOutOfRangeException();
            }

            // For large arrays, we prefer to avoid the zero-initialization costs. However, as the resulting
            // arrays could end up containing arbitrary bit patterns, we only allow this for types for which
            // every possible bit pattern is valid.
            buffer = typeof(T).IsPrimitive && typeof(T) != typeof(bool) ?
                GC.AllocateUninitializedArray<T>(minimumLength) :
                new T[minimumLength];

            return buffer;
        }
    }
    internal sealed class ConfigurableArrayPool<T> : ArrayPool<T>
    {
        /// <summary>The default maximum length of each array in the pool (2^20).</summary>
        private const int DefaultMaxArrayLength = 1024 * 1024;
        /// <summary>The default maximum number of arrays per bucket that are available for rent.</summary>
        private const int DefaultMaxNumberOfArraysPerBucket = 50;

        private readonly Bucket[] _buckets;

        internal ConfigurableArrayPool() : this(DefaultMaxArrayLength, DefaultMaxNumberOfArraysPerBucket)
        {
        }

        internal ConfigurableArrayPool(int maxArrayLength, int maxArraysPerBucket)
        {
            if (maxArrayLength <= 0 || maxArraysPerBucket <= 0) throw new ArgumentOutOfRangeException();

            // Our bucketing algorithm has a min length of 2^4 and a max length of 2^30.
            // Constrain the actual max used to those values.
            const int MinimumArrayLength = 0x10, MaximumArrayLength = 0x40000000;
            if (maxArrayLength > MaximumArrayLength)
            {
                maxArrayLength = MaximumArrayLength;
            }
            else if (maxArrayLength < MinimumArrayLength)
            {
                maxArrayLength = MinimumArrayLength;
            }

            // Create the buckets.
            int poolId = Id;
            int maxBuckets = Utilities.SelectBucketIndex(maxArrayLength);
            var buckets = new Bucket[maxBuckets + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new Bucket(Utilities.GetMaxSizeForBucket(i), maxArraysPerBucket, poolId);
            }
            _buckets = buckets;
        }

        /// <summary>Gets an ID for the pool to use with events.</summary>
        private int Id => GetHashCode();

        /// <summary>Provides a thread-safe bucket containing buffers that can be Rent'd and Return'd.</summary>
        private sealed class Bucket
        {
            internal readonly int _bufferLength;
            private readonly T[]?[] _buffers;
            private readonly int _poolId;

            private int _index;

            internal Bucket(int bufferLength, int numberOfBuffers, int poolId)
            {
                _buffers = new T[numberOfBuffers][];
                _bufferLength = bufferLength;
                _poolId = poolId;
            }
        }
    }

    /// <summary>Wrapper for arrays stored in ThreadStatic buckets.</summary>
    internal struct SharedArrayPoolThreadLocalArray
    {
        /// <summary>The stored array.</summary>
        public Array? Array;
        /// <summary>Environment.TickCount timestamp for when this array was observed by Trim.</summary>
        public int MillisecondsTimeStamp;

        public SharedArrayPoolThreadLocalArray(Array array)
        {
            Array = array;
            MillisecondsTimeStamp = 0;
        }
    }

    /// <summary>Provides a collection of partitions, each of which is a pool of arrays.</summary>
    internal sealed class SharedArrayPoolPartitions
    {
        /// <summary>The partitions.</summary>
        private readonly Partition[] _partitions;

        /// <summary>Initializes the partitions.</summary>
        public SharedArrayPoolPartitions()
        {
            // Create the partitions.  We create as many as there are processors, limited by our max.
            var partitions = new Partition[SharedArrayPoolStatics.s_partitionCount];
            for (int i = 0; i < partitions.Length; i++)
            {
                partitions[i] = new Partition();
            }
            _partitions = partitions;
        }

        /// <summary>
        /// Try to push the array into any partition with available space, starting with partition associated with the current core.
        /// If all partitions are full, the array will be dropped.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPush(Array array)
        {
            // Try to push on to the associated partition first.  If that fails,
            // round-robin through the other partitions.
            Partition[] partitions = _partitions;
            int index = (int)((uint)System.Threading.Thread.GetCurrentProcessorId() % (uint)SharedArrayPoolStatics.s_partitionCount); // mod by constant in tier 1
            for (int i = 0; i < partitions.Length; i++)
            {
                if (partitions[index].TryPush(array)) return true;
                if (++index == partitions.Length) index = 0;
            }

            return false;
        }

        /// <summary>
        /// Try to pop an array from any partition with available arrays, starting with partition associated with the current core.
        /// If all partitions are empty, null is returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Array? TryPop()
        {
            // Try to pop from the associated partition first.  If that fails, round-robin through the other partitions.
            Array? arr;
            Partition[] partitions = _partitions;
            int index = (int)((uint)System.Threading.Thread.GetCurrentProcessorId() % (uint)SharedArrayPoolStatics.s_partitionCount); // mod by constant in tier 1
            for (int i = 0; i < partitions.Length; i++)
            {
                if ((arr = partitions[index].TryPop()) is not null) return arr;
                if (++index == partitions.Length) index = 0;
            }
            return null;
        }

        public void Trim(int currentMilliseconds, int id, Utilities.MemoryPressure pressure)
        {
            Partition[] partitions = _partitions;
            for (int i = 0; i < partitions.Length; i++)
            {
                partitions[i].Trim(currentMilliseconds, id, pressure);
            }
        }

        private sealed class Partition
        {
            /// <summary>The arrays in the partition.</summary>
            private readonly Array?[] _arrays = new Array[SharedArrayPoolStatics.s_maxArraysPerPartition];
            /// <summary>Number of arrays stored in <see cref="_arrays"/>.</summary>
            private int _count;
            /// <summary>Timestamp set by Trim when it sees this as 0.</summary>
            private int _millisecondsTimestamp;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryPush(Array array)
            {
                bool enqueued = false;
                System.Threading.Monitor.Enter(this);
                Array?[] arrays = _arrays;
                int count = _count;
                if ((uint)count < (uint)arrays.Length)
                {
                    if (count == 0)
                    {
                        // Reset the time stamp now that we're transitioning from empty to non-empty.
                        // Trim will see this as 0 and initialize it to the current time when Trim is called.
                        _millisecondsTimestamp = 0;
                    }
                    // arrays[count] = array, but avoiding stelemref
                    Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(arrays), count) = array;
                    _count = count + 1;
                    enqueued = true;
                }
                System.Threading.Monitor.Exit(this);
                return enqueued;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Array? TryPop()
            {
                Array? arr = null;
                System.Threading.Monitor.Enter(this);
                Array?[] arrays = _arrays;
                int count = _count - 1;
                if ((uint)count < (uint)arrays.Length)
                {
                    arr = arrays[count];
                    arrays[count] = null;
                    _count = count;
                }
                System.Threading.Monitor.Exit(this);
                return arr;
            }

            public void Trim(int currentMilliseconds, int id, Utilities.MemoryPressure pressure)
            {
                const int TrimAfterMS = 60 * 1000;                                  // Trim after 60 seconds for low/moderate pressure
                const int HighTrimAfterMS = 10 * 1000;                              // Trim after 10 seconds for high pressure

                if (_count == 0)
                {
                    return;
                }

                int trimMilliseconds = pressure == Utilities.MemoryPressure.High ? HighTrimAfterMS : TrimAfterMS;

                lock (this)
                {
                    if (_count == 0)
                    {
                        return;
                    }

                    if (_millisecondsTimestamp == 0)
                    {
                        _millisecondsTimestamp = currentMilliseconds;
                        return;
                    }

                    if ((currentMilliseconds - _millisecondsTimestamp) <= trimMilliseconds)
                    {
                        return;
                    }

                    // We've elapsed enough time since the first item went into the partition.
                    // Drop the top item(s) so they can be collected.

                    int trimCount = pressure switch
                    {
                        Utilities.MemoryPressure.High => SharedArrayPoolStatics.s_maxArraysPerPartition,
                        Utilities.MemoryPressure.Medium => 2,
                        _ => 1,
                    };

                    while (_count > 0 && trimCount-- > 0)
                    {
                        Array? array = _arrays[--_count];
                        _arrays[_count] = null;
                    }

                    _millisecondsTimestamp = _count > 0 ?
                        _millisecondsTimestamp + (trimMilliseconds / 4) : // Give the remaining items a bit more time
                        0;
                }
            }
        }
    }

    internal static class SharedArrayPoolStatics
    {
        /// <summary>Number of partitions to employ.</summary>
        internal static readonly int s_partitionCount = GetPartitionCount();
        /// <summary>The maximum number of arrays per array size to store per partition.</summary>
        internal static readonly int s_maxArraysPerPartition = GetMaxArraysPerPartition();

        /// <summary>Gets the maximum number of partitions to shard arrays into.</summary>
        /// <remarks>Defaults to int.MaxValue.  Whatever value is returned will end up being clamped to <see cref="Environment.ProcessorCount"/>.</remarks>
        private static int GetPartitionCount()
        {
            int partitionCount = TryGetInt32EnvironmentVariable("DOTNET_SYSTEM_BUFFERS_SHAREDARRAYPOOL_MAXPARTITIONCOUNT", out int result) && result > 0 ? 
                result :
                int.MaxValue; // no limit other than processor count
            return Math.Min(partitionCount, Environment.ProcessorCount);
        }

        /// <summary>Gets the maximum number of arrays of a given size allowed to be cached per partition.</summary>
        /// <returns>Defaults to 32. This does not factor in or impact the number of arrays cached per thread in TLS (currently only 1).</returns>
        private static int GetMaxArraysPerPartition()
        {
            return TryGetInt32EnvironmentVariable("DOTNET_SYSTEM_BUFFERS_SHAREDARRAYPOOL_MAXARRAYSPERPARTITION", out int result) && result > 0 ? 
                result :
                32; // arbitrary limit
        }

        /// <summary>Look up an environment variable and try to parse it as an Int32.</summary>
        /// <remarks>This avoids using anything that might in turn recursively use the ArrayPool.</remarks>
        private static bool TryGetInt32EnvironmentVariable(string variable, out int result)
        {
            result = 0;
            return false;
        }
    }

    internal static class Utilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int SelectBucketIndex(int bufferSize)
        {
            // Buffers are bucketed so that a request between 2^(n-1) + 1 and 2^n is given a buffer of 2^n
            // Bucket index is log2(bufferSize - 1) with the exception that buffers between 1 and 16 bytes
            // are combined, and the index is slid down by 3 to compensate.
            // Zero is a valid bufferSize, and it is assigned the highest bucket index so that zero-length
            // buffers are not retained by the pool. The pool will return the Array.Empty singleton for these.
            return System.Numerics.BitOperations.Log2((uint)bufferSize - 1 | 15) - 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetMaxSizeForBucket(int binIndex)
        {
            int maxSize = 16 << binIndex;
            return maxSize;
        }

        internal enum MemoryPressure
        {
            Low,
            Medium,
            High
        }
    }
}
namespace System.Buffers.Binary
{
    public static class BinaryPrimitives
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReverseEndianness(byte value) => value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReverseEndianness(ushort value)
        {
            return (ushort)((value >> 8) + (value << 8));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static char ReverseEndianness(char value) => (char)ReverseEndianness((ushort)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReverseEndianness(uint value)
        {
            return System.Numerics.BitOperations.RotateRight(value & 0x00FF00FFu, 8) // xx zz
                + System.Numerics.BitOperations.RotateLeft(value & 0xFF00FF00u, 8); // ww yy
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReverseEndianness(ulong value)
        {
            return ((ulong)ReverseEndianness((uint)value) << 32)
                + ReverseEndianness((uint)(value >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte ReverseEndianness(sbyte value) => value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReverseEndianness(short value) => (short)ReverseEndianness((ushort)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReverseEndianness(int value) => (int)ReverseEndianness((uint)value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReverseEndianness(long value) => (long)ReverseEndianness((ulong)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteSingleLittleEndian(Span<byte> destination, float value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                int tmp = ReverseEndianness(BitConverter.SingleToInt32Bits(value));
                System.Runtime.InteropServices.MemoryMarshal.Write<int>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<float>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteDoubleLittleEndian(Span<byte> destination, double value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                long tmp = ReverseEndianness(BitConverter.DoubleToInt64Bits(value));
                System.Runtime.InteropServices.MemoryMarshal.Write<long>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<double>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt16LittleEndian(Span<byte> destination, short value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                short tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<short>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<short>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt32LittleEndian(Span<byte> destination, int value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                int tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<int>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<int>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt64LittleEndian(Span<byte> destination, long value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                long tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<long>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<long>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                ushort tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<ushort>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<ushort>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                uint tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<uint>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<uint>(destination, in value);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt64LittleEndian(Span<byte> destination, ulong value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                ulong tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<ulong>(destination, in tmp);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<ulong>(destination, in value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadSingleLittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                BitConverter.Int32BitsToSingle(ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<int>(source))) :
                System.Runtime.InteropServices.MemoryMarshal.Read<float>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ReadDoubleLittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                BitConverter.Int64BitsToDouble(ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<long>(source))) :
                System.Runtime.InteropServices.MemoryMarshal.Read<double>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReadInt16LittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<short>(source)) :
                System.Runtime.InteropServices.MemoryMarshal.Read<short>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32LittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<int>(source)) :
                System.Runtime.InteropServices.MemoryMarshal.Read<int>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadInt64LittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<long>(source)) :
                System.Runtime.InteropServices.MemoryMarshal.Read<long>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<ushort>(source)) :
                System.Runtime.InteropServices.MemoryMarshal.Read<ushort>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<uint>(source)) :
                System.Runtime.InteropServices.MemoryMarshal.Read<uint>(source);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source)
        {
            return !BitConverter.IsLittleEndian ?
                ReverseEndianness(System.Runtime.InteropServices.MemoryMarshal.Read<ulong>(source)) :
                System.Runtime.InteropServices.MemoryMarshal.Read<ulong>(source);
        }
    }
}